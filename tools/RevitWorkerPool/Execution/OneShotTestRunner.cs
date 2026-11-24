using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using RevitWorkerPool.Containment;
using RevitWorkerPool.Discovery;
using RevitWorkerPool.Launching;
using RevitWorkerPool.Pipe;
using RevitWorkerPool.Reporting;
using RevitWorkerPool.Startup;
using RevitWorkerPool.Windowing;

namespace RevitWorkerPool.Execution;

/// <summary>
/// Orchestrates a single-use worker launch, bundle lease, readiness handshake,
/// named-pipe test execution, TRX emission, and immediate worker termination.
/// </summary>
public sealed class OneShotTestRunner
{
    private readonly RevitInstallationDiscovery _discovery;
    private readonly IWorkerProcessLauncher _launcher;
    private readonly IPipeTestClientFactory _pipeClientFactory;
    private readonly Func<string, IDisposable> _bundleLeaseFactory;
    private readonly Action<string>? _diagnosticLogger;
    private readonly StartupBootProtector? _startupProtector;

    public OneShotTestRunner(
        RevitInstallationDiscovery discovery,
        IWorkerProcessLauncher launcher,
        IPipeTestClientFactory pipeClientFactory,
        Func<string, IDisposable>? bundleLeaseFactory = null,
        Action<string>? diagnosticLogger = null,
        StartupBootProtector? startupProtector = null)
    {
        _discovery = discovery ?? throw new ArgumentNullException(nameof(discovery));
        _launcher = launcher ?? throw new ArgumentNullException(nameof(launcher));
        _pipeClientFactory = pipeClientFactory ?? throw new ArgumentNullException(nameof(pipeClientFactory));
        _bundleLeaseFactory = bundleLeaseFactory ?? DefaultAcquireBundleLease;
        _diagnosticLogger = diagnosticLogger;
        _startupProtector = startupProtector;
    }

    public async Task<OneShotRunResult> RunAsync(
        OneShotRunOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        string fullTrxPath = Path.GetFullPath(options.TrxPath);
        if (string.IsNullOrWhiteSpace(options.AssemblyPath) || !File.Exists(options.AssemblyPath))
        {
            return FailureResult(
                options.RevitVersion,
                fullTrxPath,
                Array.Empty<string>(),
                $"Test assembly not found: '{options.AssemblyPath}'.");
        }

        string fullAssemblyPath = Path.GetFullPath(options.AssemblyPath);
        RevitInstallation? installation = _discovery.TryFindInstallation(options.RevitVersion);
        if (installation is null)
        {
            return FailureResult(
                options.RevitVersion,
                fullTrxPath,
                Array.Empty<string>(),
                $"Autodesk Revit {options.RevitVersion} installation was not found.");
        }

        string workerId = string.IsNullOrWhiteSpace(options.WorkerId)
            ? WorkerLaunchRequest.CreateWorkerId()
            : options.WorkerId.Trim();

        string projectRoot = ResolveProjectRoot(options.ProjectRoot, fullAssemblyPath);
        string[] normalizedFilters = NormalizeTestFilters(options.Filters);

        Log($"[runner] Starting one-shot run: version={options.RevitVersion}, workerId={workerId}, exe='{installation.ExecutablePath}'");

        IDisposable? bundleLease = null;
        LaunchedWorkerProcess? worker = null;
        IPipeTestClient? pipeClient = null;
        DateTimeOffset runStart = DateTimeOffset.UtcNow;

        try
        {
            string? assemblyDir = Path.GetDirectoryName(fullAssemblyPath);
            bundleLease = _bundleLeaseFactory(assemblyDir ?? string.Empty);

            var launchRequest = new WorkerLaunchRequest
            {
                RevitVersion = options.RevitVersion,
                ExecutablePath = installation.ExecutablePath,
                Arguments = "/nosplash",
                WorkerId = workerId,
                ProjectRoot = projectRoot
            };

            StartupBootProtector? protector = ResolveStartupBootProtector();
            if (protector is not null && OperatingSystem.IsWindows())
            {
                ProtectedWorkerBootOptions bootOptions = protector.DefaultOptions with
                {
                    BootTimeout = options.BootTimeout
                };

                ProtectedWorkerBootResult bootResult = await protector.BootWorkerWithStartupProtectionAsync(
                    _launcher,
                    _pipeClientFactory,
                    launchRequest,
                    bootOptions,
                    cancellationToken).ConfigureAwait(false);

                worker = bootResult.Worker;
                pipeClient = bootResult.PipeClient;
                Log($"[runner] Worker PID={worker.ProcessId} reported IsBusy=false (ready, attempts={bootResult.AttemptsUsed}).");
            }
            else
            {
                worker = await _launcher.LaunchAsync(launchRequest, cancellationToken).ConfigureAwait(false);
                Log($"[runner] Launched worker PID={worker.ProcessId}, pipe='{worker.PipeName}'.");

                pipeClient = _pipeClientFactory.CreateClient(worker);
                await pipeClient.ConnectAndWaitForReadyAsync(worker, options.BootTimeout, cancellationToken)
                    .ConfigureAwait(false);
                Log($"[runner] Worker PID={worker.ProcessId} reported IsBusy=false (ready).");
            }

            var testRequest = new TestRequest
            {
                TestPathFile = fullAssemblyPath,
                TestFilters = normalizedFilters,
                Info = $"WorkerId={worker.WorkerId}"
            };

            PipeTestExecutionResult execution = await pipeClient.ExecuteTestsAsync(
                testRequest,
                worker,
                options.ExecutionTimeout,
                onTestCompleted: test =>
                {
                    string status = test.Skipped ? "SKIP" : (test.Success ? "PASS" : "FAIL");
                    Log($"[test] {status}: {test.FullName} ({test.Time:F1}ms)");
                },
                cancellationToken).ConfigureAwait(false);

            DateTimeOffset runFinish = DateTimeOffset.UtcNow;
            TrxSummary summary = TrxWriter.Write(
                fullTrxPath,
                fullAssemblyPath,
                execution.AssemblyResult,
                execution.StreamedTests,
                runStart,
                runFinish);

            bool zeroMatches = summary.TotalTests == 0;
            bool success = summary.FailedTests == 0;
            int exitCode = success ? 0 : 1;

            return new OneShotRunResult
            {
                Success = success,
                ExitCode = exitCode,
                Version = options.RevitVersion,
                TrxPath = summary.TrxPath,
                WorkerIds = new[] { worker.WorkerId },
                ShardCount = 1,
                TotalTests = summary.TotalTests,
                PassedTests = summary.PassedTests,
                FailedTests = summary.FailedTests,
                SkippedTests = summary.SkippedTests,
                ZeroMatches = zeroMatches,
                ShimEnabled = false,
                Error = null
            };
        }
        catch (StartupModalDetectedException ex)
        {
            return FailureResult(
                options.RevitVersion,
                fullTrxPath,
                ex.AbortedWorkerIds.Count > 0 ? ex.AbortedWorkerIds : new[] { workerId },
                ex.Message);
        }
        catch (Exception ex)
        {
            return FailureResult(
                options.RevitVersion,
                fullTrxPath,
                worker is not null ? new[] { worker.WorkerId } : new[] { workerId },
                ex.Message);
        }
        finally
        {
            if (pipeClient is not null)
            {
                await pipeClient.DisposeAsync().ConfigureAwait(false);
            }

            if (worker is not null)
            {
                try
                {
                    Log($"[runner] Terminating consumed worker PID={worker.ProcessId}...");
                    await worker.TerminateAsync(CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Log($"[runner] Warning while terminating worker PID={worker.ProcessId}: {ex.Message}");
                }
                finally
                {
                    worker.Dispose();
                }
            }

            bundleLease?.Dispose();
        }
    }

    /// <summary>
    /// Normalizes CLI filter fragments for <c>ricaun.NUnit.Services.WildcardPattern</c>
    /// (which uses full-string wildcard matching without implicit substring wrapping).
    /// </summary>
    public static string[] NormalizeTestFilters(IReadOnlyList<string>? filters)
    {
        if (filters is null || filters.Count == 0)
        {
            return Array.Empty<string>();
        }

        var normalized = new List<string>(filters.Count);
        foreach (string raw in filters)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            string trimmed = raw.Trim();
            if (trimmed.Contains('*') || trimmed.Contains('?'))
            {
                normalized.Add(trimmed);
            }
            else
            {
                normalized.Add($"*{trimmed}*");
            }
        }

        return normalized.ToArray();
    }

    private static IDisposable DefaultAcquireBundleLease(string assemblyDir)
    {
        string[] searchDirs = string.IsNullOrWhiteSpace(assemblyDir)
            ? Array.Empty<string>()
            : new[] { assemblyDir };
        return RicaunBundleLease.Acquire(testAdapterSearchDirectories: searchDirs);
    }

    private static string ResolveProjectRoot(string? explicitProjectRoot, string assemblyPath)
    {
        if (!string.IsNullOrWhiteSpace(explicitProjectRoot))
        {
            return Path.GetFullPath(explicitProjectRoot);
        }

        return RevitInstallationDiscovery.FindRepositoryRoot()
            ?? RevitInstallationDiscovery.FindRepositoryRoot(Path.GetDirectoryName(assemblyPath))
            ?? Directory.GetCurrentDirectory();
    }

    private static OneShotRunResult FailureResult(
        int version,
        string trxPath,
        IReadOnlyList<string> workerIds,
        string error)
    {
        return new OneShotRunResult
        {
            Success = false,
            ExitCode = 1,
            Version = version,
            TrxPath = trxPath,
            WorkerIds = workerIds,
            ShardCount = 1,
            TotalTests = 0,
            PassedTests = 0,
            FailedTests = 0,
            SkippedTests = 0,
            ZeroMatches = false,
            ShimEnabled = false,
            Error = error
        };
    }

    private StartupBootProtector? ResolveStartupBootProtector()
    {
        if (_startupProtector is not null)
        {
            return _startupProtector;
        }

        if (OperatingSystem.IsWindows() && IsUnshimmedProductionLauncher(_launcher))
        {
            return new StartupBootProtector(diagnosticLogger: _diagnosticLogger);
        }

        return null;
    }

    private static bool IsUnshimmedProductionLauncher(IWorkerProcessLauncher launcher)
    {
        IWorkerProcessLauncher current = launcher;
        while (OperatingSystem.IsWindows() && current is OffScreenWindowingLauncher windowing)
        {
            current = windowing.InnerLauncher;
        }

        return current is StandardProcessLauncher;
    }

    private void Log(string message)
    {
        _diagnosticLogger?.Invoke(message);
    }
}
