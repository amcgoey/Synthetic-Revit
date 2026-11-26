using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using RevitWorkerPool.Execution;
using RevitWorkerPool.Launching;
using RevitWorkerPool.Pipe;

namespace RevitWorkerPool.Startup;

public sealed record ProtectedWorkerBootOptions
{
    public TimeSpan BootTimeout { get; init; } = TimeSpan.FromSeconds(120);

    public string MutexPrefix { get; init; } = StartupGateMutex.DefaultMutexPrefix;

    public TimeSpan GateAcquireTimeout { get; init; } = StartupGateMutex.DefaultAcquireTimeout;

    public TimeSpan GateHoldCap { get; init; } = StartupGateMutex.DefaultHoldCap;

    public TimeSpan PollInterval { get; init; } = TimeSpan.FromMilliseconds(250);

    public int MaxRetries { get; init; } = 1;

    public bool UseStartupGate { get; init; } = true;

    public string? JournalsDirectory { get; init; }
}

public sealed record ProtectedWorkerBootResult(
    LaunchedWorkerProcess Worker,
    IPipeTestClient PipeClient,
    int AttemptsUsed,
    int ModalAbortCount,
    IReadOnlyList<string> AbortedWorkerIds);

public sealed class StartupModalDetectedException : InvalidOperationException
{
    public StartupModalDetectedException(
        StartupModalDetection detection,
        int attemptsUsed,
        IReadOnlyList<string> abortedWorkerIds,
        string message)
        : base(message)
    {
        Detection = detection;
        AttemptsUsed = attemptsUsed;
        AbortedWorkerIds = abortedWorkerIds;
    }

    public StartupModalDetection Detection { get; }

    public int AttemptsUsed { get; }

    public IReadOnlyList<string> AbortedWorkerIds { get; }
}

/// <summary>
/// Reusable unshimmed worker boot orchestrator pairing <see cref="StartupGateMutex"/>
/// with <see cref="StartupModalWatchdog"/> and bounded single-retry recovery.
/// Shared by <see cref="Execution.OneShotTestRunner"/> and the worker pool supervisor.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class StartupBootProtector
{
    private readonly StartupModalWatchdog _watchdog;
    private readonly ProtectedWorkerBootOptions _defaultOptions;
    private readonly Action<string>? _diagnosticLogger;

    public StartupBootProtector(
        StartupModalWatchdog? watchdog = null,
        ProtectedWorkerBootOptions? defaultOptions = null,
        Action<string>? diagnosticLogger = null)
    {
        _watchdog = watchdog ?? new StartupModalWatchdog();
        _defaultOptions = defaultOptions ?? new ProtectedWorkerBootOptions();
        _diagnosticLogger = diagnosticLogger;
    }

    public ProtectedWorkerBootOptions DefaultOptions => _defaultOptions;

    public async Task<ProtectedWorkerBootResult> BootWorkerWithStartupProtectionAsync(
        IWorkerProcessLauncher launcher,
        IPipeTestClientFactory pipeClientFactory,
        WorkerLaunchRequest baseRequest,
        ProtectedWorkerBootOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(launcher);
        ArgumentNullException.ThrowIfNull(pipeClientFactory);
        ArgumentNullException.ThrowIfNull(baseRequest);

        ProtectedWorkerBootOptions opts = options ?? _defaultOptions;
        string resolvedJournalsDir = !string.IsNullOrWhiteSpace(opts.JournalsDirectory)
            ? opts.JournalsDirectory
            : StartupModalWatchdog.GetDefaultJournalsDirectory(baseRequest.RevitVersion);

        string baseWorkerId = string.IsNullOrWhiteSpace(baseRequest.WorkerId)
            ? WorkerLaunchRequest.CreateWorkerId()
            : baseRequest.WorkerId.Trim();

        int totalAttempts = Math.Max(0, opts.MaxRetries) + 1;
        var abortedWorkerIds = new List<string>();
        var ignoredJournals = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (int attempt = 0; attempt < totalAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string attemptWorkerId = attempt == 0
                ? baseWorkerId
                : $"{baseWorkerId}_retry{attempt}";
            WorkerLaunchRequest attemptRequest = CloneRequestWithWorkerId(baseRequest, attemptWorkerId);

            if (attempt > 0)
            {
                _diagnosticLogger?.Invoke(
                    $"[startup-protector] Retrying Revit {baseRequest.RevitVersion} worker boot " +
                    $"(attempt {attempt + 1}/{totalAttempts}, workerId={attemptWorkerId})...");
            }

            StartupGateMutex? gate = null;
            if (opts.UseStartupGate)
            {
                gate = new StartupGateMutex(
                    baseRequest.RevitVersion,
                    opts.MutexPrefix,
                    opts.GateAcquireTimeout,
                    opts.GateHoldCap,
                    _diagnosticLogger);

                try
                {
                    await gate.AcquireAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (TimeoutException ex)
                {
                    _diagnosticLogger?.Invoke(
                        $"[startup-protector] Warning: timed out acquiring Startup Gate '{gate.MutexName}' ({ex.Message}); proceeding with modal watchdog.");
                    gate.Dispose();
                    gate = null;
                }
                catch
                {
                    gate.Dispose();
                    throw;
                }
            }

            LaunchedWorkerProcess? worker = null;
            IPipeTestClient? pipeClient = null;
            StartupModalDetection? modalDetection = null;

            try
            {
                HashSet<string> attemptKnownJournals = BuildAttemptKnownJournals(
                    resolvedJournalsDir,
                    ignoredJournals,
                    attemptWorkerId);

                worker = await launcher.LaunchAsync(attemptRequest, cancellationToken).ConfigureAwait(false);
                _diagnosticLogger?.Invoke(
                    $"[startup-protector] Launched worker PID={worker.ProcessId}, workerId={worker.WorkerId}, pipe='{worker.PipeName}'.");

                pipeClient = pipeClientFactory.CreateClient(worker);

                using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                using var watchdogCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

                Task<StartupModalDetection?> watchdogTask = _watchdog.WaitForModalAsync(
                    worker,
                    resolvedJournalsDir,
                    attemptKnownJournals,
                    opts.PollInterval,
                    watchdogCts.Token);

                Task connectTask = pipeClient.ConnectAndWaitForReadyAsync(
                    worker,
                    opts.BootTimeout,
                    connectCts.Token);

                Task first = await Task.WhenAny(connectTask, watchdogTask).ConfigureAwait(false);

                if (first == watchdogTask)
                {
                    StartupModalDetection? detected = await watchdogTask.ConfigureAwait(false);
                    if (detected is not null)
                    {
                        modalDetection = detected;
                        connectCts.Cancel();
                        await ObserveCanceledOrFaultedTaskAsync(connectTask).ConfigureAwait(false);
                    }
                    else
                    {
                        // Worker exited before modal detection; await connectTask for exact exit/pipe failure,
                        // performing one final modal check in case a journal marker flushed right at exit.
                        try
                        {
                            await connectTask.ConfigureAwait(false);
                        }
                        catch when (!cancellationToken.IsCancellationRequested)
                        {
                            modalDetection = _watchdog.CheckOnce(
                                worker.ProcessId,
                                worker.WorkerId,
                                resolvedJournalsDir,
                                attemptKnownJournals,
                                worker.DesktopHandle);
                            if (modalDetection is null)
                            {
                                throw;
                            }
                        }
                    }
                }
                else
                {
                    try
                    {
                        await connectTask.ConfigureAwait(false);
                        watchdogCts.Cancel();
                        StartupModalDetection? lateModal = await ObserveWatchdogTaskAsync(watchdogTask).ConfigureAwait(false);
                        if (lateModal is not null)
                        {
                            modalDetection = lateModal;
                        }
                    }
                    catch when (!cancellationToken.IsCancellationRequested)
                    {
                        watchdogCts.Cancel();
                        StartupModalDetection? watchedModal = await ObserveWatchdogTaskAsync(watchdogTask).ConfigureAwait(false);
                        modalDetection = watchedModal ?? _watchdog.CheckOnce(
                            worker.ProcessId,
                            worker.WorkerId,
                            resolvedJournalsDir,
                            attemptKnownJournals,
                            worker.DesktopHandle);

                        if (modalDetection is null)
                        {
                            throw;
                        }
                    }
                }

                if (modalDetection is not null)
                {
                    _diagnosticLogger?.Invoke(
                        $"[startup-protector] Startup failure modal detected in {modalDetection.Reason} " +
                        $"for Revit {baseRequest.RevitVersion} (worker PID {worker.ProcessId}); terminating PID tree and releasing Startup Gate...");

                    await TestExecutionHelper.TerminateAndDisposeWorkerAsync(worker, pipeClient, _diagnosticLogger)
                        .ConfigureAwait(false);
                    worker = null;
                    pipeClient = null;

                    CollectAbortedAttemptJournals(resolvedJournalsDir, ignoredJournals, attemptWorkerId);
                    abortedWorkerIds.Add(attemptWorkerId);

                    gate?.Release();
                    gate?.Dispose();
                    gate = null;

                    if (attempt >= totalAttempts - 1)
                    {
                        throw new StartupModalDetectedException(
                            modalDetection,
                            attempt + 1,
                            abortedWorkerIds.ToArray(),
                            $"Revit {baseRequest.RevitVersion} worker startup aborted by {StartupModalWatchdog.StartupFailureDialogId} " +
                            $"({modalDetection.Reason}) after {attempt + 1} attempt(s).");
                    }

                    continue;
                }

                // Readiness reached cleanly (IsBusy == false): release Startup Gate immediately!
                gate?.Release();
                gate?.Dispose();
                gate = null;

                LaunchedWorkerProcess readyWorker = worker;
                IPipeTestClient readyPipeClient = pipeClient;
                worker = null;
                pipeClient = null;

                return new ProtectedWorkerBootResult(
                    readyWorker,
                    readyPipeClient,
                    attempt + 1,
                    abortedWorkerIds.Count,
                    abortedWorkerIds.ToArray());
            }
            finally
            {
                if (worker is not null || pipeClient is not null)
                {
                    await TestExecutionHelper.TerminateAndDisposeWorkerAsync(worker, pipeClient, _diagnosticLogger)
                        .ConfigureAwait(false);
                }

                gate?.Release();
                gate?.Dispose();
            }
        }

        throw new InvalidOperationException(
            $"Revit {baseRequest.RevitVersion} worker boot failed unexpectedly across {totalAttempts} attempt(s).");
    }

    private static WorkerLaunchRequest CloneRequestWithWorkerId(WorkerLaunchRequest source, string workerId)
    {
        return new WorkerLaunchRequest
        {
            RevitVersion = source.RevitVersion,
            ExecutablePath = source.ExecutablePath,
            Arguments = source.Arguments,
            WorkerId = workerId,
            ProjectRoot = source.ProjectRoot,
            WorkingDirectory = source.WorkingDirectory,
            DesktopName = source.DesktopName,
            AdditionalEnvironmentVariables = source.AdditionalEnvironmentVariables,
            OnProcessCreatedBeforeResume = source.OnProcessCreatedBeforeResume
        };
    }

    private static HashSet<string> BuildAttemptKnownJournals(
        string journalsDirectory,
        HashSet<string> ignoredJournals,
        string attemptWorkerId)
    {
        var known = new HashSet<string>(ignoredJournals, StringComparer.OrdinalIgnoreCase);
        foreach (string path in StartupModalWatchdog.ListJournalFilesNewestFirst(journalsDirectory))
        {
            string fullPath = Path.GetFullPath(path);
            if (known.Contains(fullPath))
            {
                continue;
            }

            string text = StartupModalWatchdog.ReadJournalTextBestEffort(fullPath);
            if (StartupModalWatchdog.ContainsWorkerIdMarker(text, attemptWorkerId))
            {
                continue;
            }

            known.Add(fullPath);
        }

        return known;
    }

    private static void CollectAbortedAttemptJournals(
        string journalsDirectory,
        HashSet<string> ignoredJournals,
        string attemptWorkerId)
    {
        foreach (string path in StartupModalWatchdog.ListJournalFilesNewestFirst(journalsDirectory))
        {
            string fullPath = Path.GetFullPath(path);
            if (ignoredJournals.Contains(fullPath))
            {
                continue;
            }

            string text = StartupModalWatchdog.ReadJournalTextBestEffort(fullPath);
            if (StartupModalWatchdog.ContainsWorkerIdMarker(text, attemptWorkerId) ||
                (!text.Contains("SYNTHETIC_WORKER_ID:", StringComparison.Ordinal) &&
                 !text.Contains("SYNTHETIC_TEST_RUN:", StringComparison.Ordinal) &&
                 !text.Contains("SYNTHETIC_BOOTSTRAP:", StringComparison.Ordinal)))
            {
                ignoredJournals.Add(fullPath);
            }
        }
    }

    private static async Task ObserveCanceledOrFaultedTaskAsync(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Expected when boot pipe wait is canceled after modal detection.
        }
    }

    private static async Task<StartupModalDetection?> ObserveWatchdogTaskAsync(Task<StartupModalDetection?> task)
    {
        try
        {
            return await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }
}
