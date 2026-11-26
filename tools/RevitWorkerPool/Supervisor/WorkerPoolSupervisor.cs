using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using RevitWorkerPool.Containment;
using RevitWorkerPool.Discovery;
using RevitWorkerPool.Execution;
using RevitWorkerPool.Ipc;
using RevitWorkerPool.Launching;
using RevitWorkerPool.Pipe;
using RevitWorkerPool.Reporting;
using RevitWorkerPool.Sharding;
using RevitWorkerPool.Startup;

namespace RevitWorkerPool.Supervisor;

/// <summary>
/// On-demand Worker Pool supervisor managing pre-booted, single-use Revit Standby Workers,
/// host-wide concurrency cap enforcement, lazy auto-start/latest-version replenishment,
/// shared bundle lease lifetime, clean drain, and idle scale-to-zero.
/// </summary>
public sealed class WorkerPoolSupervisor : IAsyncDisposable, IDisposable
{
    private readonly SupervisorOptions _options;
    private readonly RevitInstallationDiscovery _discovery;
    private readonly IWorkerProcessLauncher _launcher;
    private readonly IPipeTestClientFactory _pipeClientFactory;
    private readonly Func<string?, IDisposable> _bundleLeaseFactory;
    private readonly TimeProvider _timeProvider;
    private readonly Func<int> _currentLatestVersionProvider;
    private readonly Action<string>? _diagnosticLogger;
    private readonly StartupBootProtector? _startupProtector;
    private readonly FixtureTimingCache _timingCache;
    private readonly IMemoryProbe _memoryProbe;

    private readonly object _sync = new();
    private readonly List<ManagedWorker> _workers = new();
    private readonly FairRoundRobinScheduler<PendingWorkItem> _scheduler = new();
    private readonly List<PipelinePrewarmRequest> _pendingPipelinePrewarms = new();
    private readonly Dictionary<int, int> _standbyTargets = new();
    private readonly Dictionary<int, long> _latestRunSequenceByVersion = new();
    private readonly ConcurrentDictionary<Task, byte> _backgroundTasks = new();
    private readonly ConcurrentDictionary<Guid, ActiveRunTracking> _activeRunTasks = new();
    private readonly CancellationTokenSource _supervisorCts = new();
    private readonly TaskCompletionSource<bool> _shutdownTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private IDisposable? _bundleLease;
    private ITimer? _idleTimer;
    private SupervisorIpcServer? _ipcServer;
    private DateTimeOffset _lastActivityUtc;
    private long _nextRunSequence;
    private int _inFlightRuns;
    private int _activeWarmCalls;
    private int _totalTerminatedWorkers;
    private bool _started;
    private bool _draining;
    private bool _disposed;
    private Task<DrainResult>? _drainTask;

    public WorkerPoolSupervisor(
        SupervisorOptions options,
        RevitInstallationDiscovery discovery,
        IWorkerProcessLauncher launcher,
        IPipeTestClientFactory pipeClientFactory,
        Func<string?, IDisposable>? bundleLeaseFactory = null,
        TimeProvider? timeProvider = null,
        Func<int>? currentLatestVersionProvider = null,
        Action<string>? diagnosticLogger = null,
        StartupBootProtector? startupProtector = null,
        FixtureTimingCache? timingCache = null,
        IMemoryProbe? memoryProbe = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        if (_options.MaxActiveWorkers <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "MaxActiveWorkers must be >= 1.");
        }

        _discovery = discovery ?? throw new ArgumentNullException(nameof(discovery));
        _launcher = launcher ?? throw new ArgumentNullException(nameof(launcher));
        _pipeClientFactory = pipeClientFactory ?? throw new ArgumentNullException(nameof(pipeClientFactory));
        _bundleLeaseFactory = bundleLeaseFactory ?? DefaultAcquireBundleLease;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _currentLatestVersionProvider = currentLatestVersionProvider ?? ResolveDefaultLatestVersion;
        _diagnosticLogger = diagnosticLogger;
        _startupProtector = startupProtector;
        _timingCache = timingCache ?? new FixtureTimingCache(
            _options.TimingCacheDirectory,
            diagnosticLogger: diagnosticLogger);
        _memoryProbe = memoryProbe ?? _options.MemoryProbe ?? new Win32MemoryProbe();
        _lastActivityUtc = _timeProvider.GetUtcNow();
    }

    public bool IsDraining
    {
        get
        {
            lock (_sync)
            {
                return _draining;
            }
        }
    }

    public bool HasShutdownCompleted => _shutdownTcs.Task.IsCompleted;

    /// <summary>
    /// Attaches the IPC server so <see cref="DrainAsync"/> stops accepting new IPC clients
    /// before tearing down workers.
    /// </summary>
    public void AttachIpcServer(SupervisorIpcServer ipcServer)
    {
        ArgumentNullException.ThrowIfNull(ipcServer);
        lock (_sync)
        {
            _ipcServer = ipcServer;
        }
    }

    /// <summary>
    /// Starts the supervisor, acquires the shared bundle lease for the supervisor's active lifetime,
    /// and arms the idle scale-to-zero timer.
    /// </summary>
    public void Start(string? initialAssemblyDir = null)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_draining)
            {
                return;
            }

            EnsureBundleLeaseAcquiredLocked(initialAssemblyDir);
            if (!_started)
            {
                _started = true;
                _lastActivityUtc = _timeProvider.GetUtcNow();
                ArmOrResetIdleTimerLocked();
            }
        }
    }

    /// <summary>
    /// Completes once the supervisor has drained to zero workers (via explicit <c>drain</c> or idle TTL).
    /// </summary>
    public Task WaitForShutdownAsync(CancellationToken cancellationToken = default)
    {
        return _shutdownTcs.Task.WaitAsync(cancellationToken);
    }

    /// <summary>
    /// Pre-boots <paramref name="count"/> Standby Workers (default 2) for <paramref name="version"/>
    /// (default <c>CURRENT_LATEST_VERSION</c>) up to the host-wide active cap.
    /// </summary>
    public async Task<WarmResult> WarmAsync(
        int? version = null,
        int? count = null,
        TimeSpan? bootTimeout = null,
        CancellationToken cancellationToken = default)
    {
        int resolvedVersion;
        try
        {
            resolvedVersion = version ?? _currentLatestVersionProvider();
        }
        catch (Exception ex)
        {
            return new WarmResult
            {
                Success = false,
                ExitCode = 2,
                Version = version ?? 0,
                TargetCount = 0,
                ReadyCount = 0,
                Error = ex.Message,
                Status = GetStatus()
            };
        }

        int requestedCount = count ?? _options.DefaultWarmCount;
        if (requestedCount <= 0)
        {
            return new WarmResult
            {
                Success = false,
                ExitCode = 2,
                Version = resolvedVersion,
                TargetCount = requestedCount,
                ReadyCount = 0,
                Error = $"Warm count must be >= 1 (got {requestedCount}).",
                Status = GetStatus()
            };
        }

        RevitInstallation? installation = _discovery.TryFindInstallation(resolvedVersion);
        if (installation is null)
        {
            return new WarmResult
            {
                Success = false,
                ExitCode = 2,
                Version = resolvedVersion,
                TargetCount = 0,
                ReadyCount = 0,
                Error = $"Autodesk Revit {resolvedVersion} installation was not found.",
                Status = GetStatus()
            };
        }

        TimeSpan effectiveBootTimeout = bootTimeout ?? _options.DefaultBootTimeout;
        int cappedTarget = Math.Min(requestedCount, _options.MaxActiveWorkers);
        List<Task<bool>> bootWaitTasks;

        lock (_sync)
        {
            if (_disposed || _draining)
            {
                return new WarmResult
                {
                    Success = false,
                    ExitCode = 2,
                    Version = resolvedVersion,
                    TargetCount = cappedTarget,
                    ReadyCount = 0,
                    Error = "Worker pool supervisor is draining.",
                    Status = GetStatusLocked()
                };
            }

            try
            {
                EnsureStartedLocked(initialAssemblyDir: null);
            }
            catch (Exception ex)
            {
                return new WarmResult
                {
                    Success = false,
                    ExitCode = 2,
                    Version = resolvedVersion,
                    TargetCount = cappedTarget,
                    ReadyCount = 0,
                    Error = ex.Message,
                    Status = GetStatusLocked()
                };
            }

            _activeWarmCalls++;
            TouchActivityLocked();

            _standbyTargets[resolvedVersion] = cappedTarget;
            ScheduleLocked(effectiveBootTimeout);

            bootWaitTasks = _workers
                .Where(w => w.Version == resolvedVersion && w.State == WorkerState.Booting)
                .Select(w => w.WarmWaitTcs.Task)
                .ToList();
        }

        try
        {
            if (bootWaitTasks.Count > 0)
            {
                await Task.WhenAll(bootWaitTasks).WaitAsync(cancellationToken).ConfigureAwait(false);
            }

            PoolStatusSnapshot status = GetStatus();
            int readyCount = status.Versions.TryGetValue(resolvedVersion, out VersionWorkerStatus? vs)
                ? vs.ReadyStandby
                : 0;

            return new WarmResult
            {
                Success = true,
                ExitCode = 0,
                Version = resolvedVersion,
                TargetCount = cappedTarget,
                ReadyCount = readyCount,
                Status = status
            };
        }
        catch (Exception ex)
        {
            PoolStatusSnapshot failureStatus;
            lock (_sync)
            {
                _standbyTargets.Remove(resolvedVersion);
                failureStatus = GetStatusLocked();
            }

            return new WarmResult
            {
                Success = false,
                ExitCode = 2,
                Version = resolvedVersion,
                TargetCount = cappedTarget,
                ReadyCount = 0,
                Error = ex.Message,
                Status = failureStatus
            };
        }
        finally
        {
            lock (_sync)
            {
                _activeWarmCalls--;
                TouchActivityLocked();
            }
        }
    }

    /// <summary>
    /// Dispatches a single test run to a ready Standby Worker (or waits for a booting worker / boots on demand),
    /// terminates the consumed worker PID tree immediately on completion, and triggers async replenishment.
    /// </summary>
    public async Task<OneShotRunResult> RunAsync(
        OneShotRunOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        string fullTrxPath = Path.GetFullPath(options.TrxPath);
        if (string.IsNullOrWhiteSpace(options.AssemblyPath) || !File.Exists(options.AssemblyPath))
        {
            return TestExecutionHelper.InfrastructureFailureResult(
                options.RevitVersion,
                fullTrxPath,
                Array.Empty<string>(),
                EffectiveShimEnabled,
                $"Test assembly not found: '{options.AssemblyPath}'.");
        }

        string fullAssemblyPath = Path.GetFullPath(options.AssemblyPath);
        RevitInstallation? installation = _discovery.TryFindInstallation(options.RevitVersion);
        if (installation is null)
        {
            return TestExecutionHelper.InfrastructureFailureResult(
                options.RevitVersion,
                fullTrxPath,
                Array.Empty<string>(),
                EffectiveShimEnabled,
                $"Autodesk Revit {options.RevitVersion} installation was not found.");
        }

        lock (_sync)
        {
            if (_disposed || _draining)
            {
                return TestExecutionHelper.InfrastructureFailureResult(
                    options.RevitVersion,
                    fullTrxPath,
                    Array.Empty<string>(),
                    EffectiveShimEnabled,
                    "Worker pool supervisor is draining.");
            }

            if (_started)
            {
                TouchActivityLocked();
            }
        }

        RunPlanPreparationResult runPlan = TestExecutionHelper.PrepareRunPlan(
            options.RevitVersion,
            fullAssemblyPath,
            fullTrxPath,
            options.Filters,
            options.Category,
            options.Categories,
            options.Shards,
            _options.MaxActiveWorkers,
            _timingCache,
            EffectiveShimEnabled,
            _diagnosticLogger);

        if (!runPlan.ShouldExecute)
        {
            return runPlan.ImmediateResult!;
        }

        IReadOnlyList<TestShardPlan> shards = runPlan.Shards;
        int? pipelineNextVersion = null;
        if (options.NextVersion is int requestedNextVersion &&
            requestedNextVersion != options.RevitVersion &&
            _discovery.TryFindInstallation(requestedNextVersion) is not null)
        {
            pipelineNextVersion = requestedNextVersion;
        }

        Guid runId = Guid.NewGuid();
        using var runExecutionCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _supervisorCts.Token);

        lock (_sync)
        {
            if (_disposed || _draining)
            {
                return TestExecutionHelper.InfrastructureFailureResult(
                    options.RevitVersion,
                    fullTrxPath,
                    Array.Empty<string>(),
                    EffectiveShimEnabled,
                    "Worker pool supervisor is draining.",
                    shards.Count);
            }

            try
            {
                EnsureStartedLocked(Path.GetDirectoryName(fullAssemblyPath));
            }
            catch (Exception ex)
            {
                return TestExecutionHelper.InfrastructureFailureResult(
                    options.RevitVersion,
                    fullTrxPath,
                    Array.Empty<string>(),
                    EffectiveShimEnabled,
                    ex.Message,
                    shards.Count);
            }

            long runSequence = ++_nextRunSequence;
            _latestRunSequenceByVersion[options.RevitVersion] = runSequence;
            _pendingPipelinePrewarms.RemoveAll(p => p.TargetVersion == options.RevitVersion);

            _inFlightRuns++;
            TouchActivityLocked();
            _scheduler.RegisterRun(runId);
            _activeRunTasks[runId] = new ActiveRunTracking(
                runExecutionCts,
                shards.Count,
                pipelineNextVersion,
                options.RevitVersion,
                runSequence);
        }

        var stagingDirs = new List<string>();
        var shardStates = new ShardExecutionState[shards.Count];
        bool completedWithoutInfraError = false;

        try
        {
            var shardAssemblyPaths = new string[shards.Count];
            if (shards.Count == 1)
            {
                shardAssemblyPaths[0] = fullAssemblyPath;
            }
            else
            {
                for (int i = 0; i < shards.Count; i++)
                {
                    ShardStagingHelper.StagedShardDirectory staged =
                        ShardStagingHelper.CreateStagingDirectory(
                            fullAssemblyPath,
                            shards[i].ShardIndex,
                            _diagnosticLogger);
                    stagingDirs.Add(staged.StagingDirectory);
                    shardAssemblyPaths[i] = staged.StagedAssemblyPath;
                }
            }

            DateTimeOffset runStart = DateTimeOffset.UtcNow;
            using var shardRunCts = CancellationTokenSource.CreateLinkedTokenSource(runExecutionCts.Token);
            string runProjectRoot = ResolveRunProjectRoot(options.ProjectRoot, fullAssemblyPath);
            var shardTasks = new Task[shards.Count];

            for (int i = 0; i < shards.Count; i++)
            {
                TestShardPlan shard = shards[i];
                string shardAssemblyPath = shardAssemblyPaths[i];
                string? preferredWorkerId = ResolveShardPreferredWorkerId(options.WorkerId, i, shards.Count);
                var state = new ShardExecutionState(i, shard);
                shardStates[i] = state;

                shardTasks[i] = ExecuteShardAsync(
                    runId,
                    options,
                    runProjectRoot,
                    state,
                    shardAssemblyPath,
                    preferredWorkerId,
                    shardRunCts);
            }

            await Task.WhenAll(shardTasks).ConfigureAwait(false);
            DateTimeOffset runFinish = DateTimeOffset.UtcNow;

            string[] workerIds = shardStates
                .Select(s => s.WorkerId)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Cast<string>()
                .ToArray();

            UpdateTimingCacheFromCompletedShards(options.RevitVersion, shardStates);

            Exception? primaryError = shardStates
                .Select(s => s.Error)
                .FirstOrDefault(e => e is not null and not OperationCanceledException)
                ?? shardStates.Select(s => s.Error).FirstOrDefault(e => e is not null);

            if (primaryError is not null)
            {
                return TestExecutionHelper.InfrastructureFailureResult(
                    options.RevitVersion,
                    fullTrxPath,
                    workerIds,
                    EffectiveShimEnabled,
                    primaryError.Message,
                    shards.Count);
            }

            completedWithoutInfraError = true;
            var trxInputs = new TrxShardResult[shards.Count];
            for (int i = 0; i < shards.Count; i++)
            {
                ShardExecutionOutcome outcome = shardStates[i].Outcome!.Value;
                trxInputs[i] = new TrxShardResult(
                    outcome.Execution.AssemblyResult,
                    outcome.Execution.StreamedTests,
                    outcome.StartTime,
                    outcome.FinishTime);
            }

            TrxSummary summary = TrxMerger.WriteMergedTrx(
                fullTrxPath,
                fullAssemblyPath,
                trxInputs,
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
                WorkerIds = workerIds,
                ShardCount = shards.Count,
                TotalTests = summary.TotalTests,
                PassedTests = summary.PassedTests,
                FailedTests = summary.FailedTests,
                SkippedTests = summary.SkippedTests,
                ZeroMatches = zeroMatches,
                ShimEnabled = EffectiveShimEnabled,
                Error = null
            };
        }
        catch (Exception ex)
        {
            string[] workerIds = shardStates
                .Where(s => s is not null && !string.IsNullOrWhiteSpace(s.WorkerId))
                .Select(s => s.WorkerId!)
                .ToArray();
            return TestExecutionHelper.InfrastructureFailureResult(
                options.RevitVersion,
                fullTrxPath,
                workerIds,
                EffectiveShimEnabled,
                ex.Message,
                shards.Count);
        }
        finally
        {
            ShardStagingHelper.CleanupStagingDirectories(stagingDirs, _diagnosticLogger);

            bool anyDispatched = shardStates.Any(s => s is not null && s.Dispatched);
            lock (_sync)
            {
                _activeRunTasks.TryRemove(runId, out _);
                _scheduler.UnregisterRun(runId);
                if (!completedWithoutInfraError)
                {
                    _pendingPipelinePrewarms.RemoveAll(p => p.RunId == runId);
                }

                _inFlightRuns--;
                TouchActivityLocked();

                if ((anyDispatched || _pendingPipelinePrewarms.Count > 0) && !_draining && !_disposed)
                {
                    ScheduleLocked();
                }
            }
        }
    }

    /// <summary>
    /// Returns live aggregate and per-version worker counts.
    /// </summary>
    public PoolStatusSnapshot GetStatus()
    {
        lock (_sync)
        {
            return GetStatusLocked();
        }
    }

    /// <summary>
    /// Cancels background replenishment, terminates all standby/booting/active workers cleanly,
    /// disposes the bundle lease, and signals supervisor shutdown. Idempotent.
    /// </summary>
    public Task<DrainResult> DrainAsync(CancellationToken cancellationToken = default)
    {
        Task<DrainResult> task;
        lock (_sync)
        {
            if (_drainTask is not null)
            {
                task = _drainTask;
            }
            else
            {
                _draining = true;
                task = ExecuteDrainAsync();
                _drainTask = task;
            }
        }

        return task.WaitAsync(cancellationToken);
    }

    /// <summary>
    /// Routes a control IPC request (<c>ping</c>, <c>warm</c>, <c>run</c>, <c>status</c>, <c>drain</c>).
    /// </summary>
    public async Task<SupervisorIpcResponse> HandleIpcRequestAsync(
        SupervisorIpcRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        switch (request.Command.Trim().ToLowerInvariant())
        {
            case SupervisorIpcCommands.Ping:
            {
                PoolStatusSnapshot status = GetStatus();
                return new SupervisorIpcResponse
                {
                    RequestId = request.RequestId,
                    Success = !status.Draining,
                    ExitCode = status.Draining ? 2 : 0,
                    IsDraining = status.Draining,
                    Status = status
                };
            }

            case SupervisorIpcCommands.Status:
            {
                PoolStatusSnapshot status = GetStatus();
                return new SupervisorIpcResponse
                {
                    RequestId = request.RequestId,
                    Success = true,
                    ExitCode = 0,
                    IsDraining = status.Draining,
                    Status = status
                };
            }

            case SupervisorIpcCommands.Warm:
            {
                TimeSpan? bootTimeout = request.BootTimeoutSeconds is > 0
                    ? TimeSpan.FromSeconds(request.BootTimeoutSeconds.Value)
                    : null;
                WarmResult warm = await WarmAsync(
                    request.Version,
                    request.Count,
                    bootTimeout,
                    cancellationToken).ConfigureAwait(false);

                return new SupervisorIpcResponse
                {
                    RequestId = request.RequestId,
                    Success = warm.Success,
                    ExitCode = warm.ExitCode,
                    Error = warm.Error,
                    IsDraining = warm.Status?.Draining ?? false,
                    Status = warm.Status,
                    WarmResult = warm
                };
            }

            case SupervisorIpcCommands.Run:
            {
                if (request.Run is null)
                {
                    return new SupervisorIpcResponse
                    {
                        RequestId = request.RequestId,
                        Success = false,
                        ExitCode = 2,
                        Error = "Run payload is required."
                    };
                }

                var runOptions = new OneShotRunOptions
                {
                    RevitVersion = request.Run.Version,
                    AssemblyPath = request.Run.AssemblyPath,
                    TrxPath = request.Run.TrxPath,
                    Filters = request.Run.Filters ?? Array.Empty<string>(),
                    Category = ResolvePayloadCategory(request.Run),
                    Categories = request.Run.Categories ?? Array.Empty<string>(),
                    BootTimeout = TimeSpan.FromSeconds(Math.Max(0.05, request.Run.BootTimeoutSeconds)),
                    ExecutionTimeout = TimeSpan.FromSeconds(Math.Max(0.05, request.Run.ExecutionTimeoutSeconds)),
                    WorkerId = request.Run.WorkerId,
                    ProjectRoot = !string.IsNullOrWhiteSpace(request.Run.ProjectRoot)
                        ? request.Run.ProjectRoot
                        : _options.ProjectRoot,
                    Shards = request.Run.Shards,
                    NextVersion = request.Run.NextVersion,
                    Diagnostics = request.Run.Diagnostics
                };

                OneShotRunResult result = await RunAsync(runOptions, cancellationToken).ConfigureAwait(false);
                return new SupervisorIpcResponse
                {
                    RequestId = request.RequestId,
                    Success = result.Success,
                    ExitCode = result.ExitCode,
                    Error = result.Error,
                    IsDraining = IsDraining,
                    RunResult = result
                };
            }

            case SupervisorIpcCommands.Drain:
            {
                DrainResult drain = await DrainAsync(cancellationToken).ConfigureAwait(false);
                return new SupervisorIpcResponse
                {
                    RequestId = request.RequestId,
                    Success = drain.Success,
                    ExitCode = drain.ExitCode,
                    Error = drain.Error,
                    IsDraining = true,
                    Status = drain.Status,
                    DrainResult = drain
                };
            }

            default:
                return new SupervisorIpcResponse
                {
                    RequestId = request.RequestId,
                    Success = false,
                    ExitCode = 2,
                    Error = $"Unsupported IPC command '{request.Command}'."
                };
        }
    }

    public async ValueTask DisposeAsync()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }
        }

        try
        {
            await DrainAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Best-effort drain during disposal.
            Log($"[supervisor] Warning: drain failed during disposal: {ex.Message}");
        }

        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _idleTimer?.Dispose();
            _idleTimer = null;
            _bundleLease?.Dispose();
            _bundleLease = null;
        }

        _supervisorCts.Dispose();
    }

    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    /// <summary>
    /// Extension seam for #234 sharding and #235 fair round-robin queueing: acquires a single
    /// ready worker for <paramref name="version"/>, waiting for an already-booting worker or
    /// launching on demand within <see cref="SupervisorOptions.MaxActiveWorkers"/>.
    /// </summary>
    internal async Task<WorkerAssignment> AcquireWorkerAsync(
        Guid runId,
        int version,
        string? preferredWorkerId,
        string projectRoot,
        TimeSpan bootTimeout,
        CancellationToken cancellationToken)
    {
        string resolvedProjectRoot = TestExecutionHelper.ResolveProjectRoot(projectRoot, assemblyPath: null);
        PendingWorkItem pending;

        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_draining)
            {
                throw new OperationCanceledException("Worker pool supervisor is draining.");
            }

            PruneDeadStandbyLocked();

            if (CountActiveLocked() < _options.MaxActiveWorkers &&
                !_scheduler.HasPendingMatching((v, item) =>
                    !item.Cancelled &&
                    v == version &&
                    ProjectRootsEqual(item.ProjectRoot, resolvedProjectRoot)))
            {
                ManagedWorker? readyWorker = FindHealthyStandbyLocked(version, resolvedProjectRoot);
                if (readyWorker is not null)
                {
                    AssignWorkerToActiveLocked(readyWorker);
                    _scheduler.RecordDispatch(runId);
                    MarkShardDispatchedLocked(runId);
                    ScheduleLocked(bootTimeout);
                    return new WorkerAssignment(readyWorker, readyWorker.Process!, readyWorker.PipeClient!);
                }
            }

            pending = new PendingWorkItem(runId, version, preferredWorkerId, resolvedProjectRoot, bootTimeout);
            _scheduler.Enqueue(runId, version, pending);
            ScheduleLocked(bootTimeout);
        }

        try
        {
            return await pending.AssignmentTcs.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            await CancelPendingWorkItemAsync(pending).ConfigureAwait(false);
            throw;
        }
    }

    internal Task ReleaseAndTerminateConsumedWorkerAsync(ManagedWorker worker)
    {
        return TerminateWorkerOnceAsync(worker, bootFailureError: null, isBootCancellation: false);
    }

    private async Task CancelPendingWorkItemAsync(PendingWorkItem pending)
    {
        WorkerAssignment? raceAssignment = null;
        lock (_sync)
        {
            pending.Cancelled = true;
            _scheduler.Remove(pending.RunId, pending);
            if (pending.AssignmentTcs.Task.IsCompletedSuccessfully)
            {
                raceAssignment = pending.AssignmentTcs.Task.Result;
            }
            else if (!_draining && !_disposed)
            {
                ScheduleLocked();
            }
        }

        if (raceAssignment is not null)
        {
            await ReleaseAndTerminateConsumedWorkerAsync(raceAssignment.Worker).ConfigureAwait(false);
        }
    }

    private void ApplyLazyLatestPolicyOnDispatch(int version)
    {
        int latestVersion;
        try
        {
            latestVersion = _currentLatestVersionProvider();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Log($"[supervisor] Warning: failed to resolve CURRENT_LATEST_VERSION for lazy standby policy: {ex.Message}");
            return;
        }

        if (version != latestVersion)
        {
            return;
        }

        lock (_sync)
        {
            if (_draining || _disposed)
            {
                return;
            }

            int currentTarget = _standbyTargets.GetValueOrDefault(latestVersion);
            if (currentTarget < 1)
            {
                _standbyTargets[latestVersion] = 1;
            }
        }
    }

    private void ScheduleLocked(TimeSpan? overrideBootTimeout = null)
    {
        if (_draining || _disposed)
        {
            return;
        }

        PruneDeadStandbyLocked();

        // 1. Dispatch any ready standby workers to matching queued items in fair round-robin order,
        //    strictly keeping Active <= MaxActiveWorkers.
        while (CountActiveLocked() < _options.MaxActiveWorkers &&
               _scheduler.TryDequeueMatching(
                   (version, item) => item.Cancelled || FindHealthyStandbyLocked(version, item.ProjectRoot) is not null,
                   out Guid matchedRunId,
                   out PendingWorkItem? pending))
        {
            if (pending is null || pending.Cancelled)
            {
                continue;
            }

            ManagedWorker? ready = FindHealthyStandbyLocked(pending.Version, pending.ProjectRoot);
            if (ready is null)
            {
                _scheduler.Enqueue(matchedRunId, pending.Version, pending);
                break;
            }

            AssignWorkerToActiveLocked(ready);
            MarkShardDispatchedLocked(matchedRunId);
            pending.AssignmentTcs.TrySetResult(new WorkerAssignment(ready, ready.Process!, ready.PipeClient!));
        }

        if (_scheduler.PendingCount == 0)
        {
            TrimSurplusLookaheadWorkersLocked();
        }

        // 2. Ensure queued items have booting workers within baseline cap MaxActiveWorkers
        //    in fair round-robin order (or evict idle mismatched standby if at cap).
        var accountedBooting = new Dictionary<(int Version, string NormalizedProjectRoot), int>(
            VersionProjectRootComparer.Instance);
        foreach (ManagedWorker worker in _workers)
        {
            if (worker.State == WorkerState.Booting)
            {
                var workerKey = (worker.Version, NormalizeProjectRoot(worker.ProjectRoot));
                accountedBooting[workerKey] = accountedBooting.GetValueOrDefault(workerKey) + 1;
            }
        }

        IReadOnlyList<(Guid RunId, int Version, PendingWorkItem Item)> orderedPending =
            _scheduler.GetRoundRobinPlan();
        foreach ((Guid _, int version, PendingWorkItem pending) in orderedPending)
        {
            if (pending.Cancelled)
            {
                continue;
            }

            var pendingKey = (version, NormalizeProjectRoot(pending.ProjectRoot));
            int existingBooting = accountedBooting.GetValueOrDefault(pendingKey);
            if (existingBooting > 0)
            {
                accountedBooting[pendingKey] = existingBooting - 1;
                continue;
            }

            if (CountTotalSlotsLocked() < _options.MaxActiveWorkers)
            {
                LaunchWorkerLocked(
                    version,
                    pending.PreferredWorkerId,
                    pending.ProjectRoot,
                    pending.BootTimeout,
                    WorkerBootPurpose.Demand);
            }
            else if (CountActiveLocked() < _options.MaxActiveWorkers)
            {
                // Reclaim one idle standby worker from another version or project root so active execution can use all cap slots.
                if (TryEvictIdleStandbyForOtherWorkLocked(orderedPending))
                {
                    // Eviction transitions the victim to Terminating and re-triggers ScheduleLocked upon exit.
                    break;
                }
            }
        }

        // 3. Strictly demand-gated, shimmed-only, RAM-guarded +1 lookahead standby slot.
        //    Permitted only when all four guards hold simultaneously and TotalResident <= MaxActiveWorkers + 1.
        if (CountActiveLocked() == _options.MaxActiveWorkers &&
            CountTotalSlotsLocked() < _options.MaxActiveWorkers + 1 &&
            _launcher.UsesCleanShim &&
            _scheduler.TryPeekNextBootCandidate(out int lookaheadVersion, out PendingWorkItem? lookaheadItem) &&
            lookaheadItem is not null &&
            !lookaheadItem.Cancelled &&
            _memoryProbe.GetAvailablePhysicalBytes() >= _options.LookaheadMinAvailableMemoryBytes)
        {
            LaunchWorkerLocked(
                lookaheadVersion,
                lookaheadItem.PreferredWorkerId,
                lookaheadItem.ProjectRoot,
                lookaheadItem.BootTimeout,
                WorkerBootPurpose.Lookahead);
        }

        // 4. Inside baseline cap MaxActiveWorkers: trigger one-time N -> N+1 pipeline pre-warming
        //    and replenish background standby targets only when no pending item is starving for slots.
        if (_scheduler.PendingCount == 0)
        {
            LaunchPendingPipelinePrewarmsLocked(overrideBootTimeout);

            foreach ((int version, int targetCount) in _standbyTargets.OrderBy(kvp => kvp.Key))
            {
                if (targetCount <= 0)
                {
                    continue;
                }

                string defaultProjectRoot = ResolveSupervisorProjectRoot(version);
                int currentStandbyAndBooting = _workers.Count(w =>
                    w.Version == version &&
                    ProjectRootsEqual(w.ProjectRoot, defaultProjectRoot) &&
                    w.State is WorkerState.Booting or WorkerState.ReadyStandby);

                while (currentStandbyAndBooting < targetCount &&
                       CountTotalSlotsLocked() < _options.MaxActiveWorkers)
                {
                    LaunchWorkerLocked(
                        version,
                        preferredWorkerId: null,
                        defaultProjectRoot,
                        overrideBootTimeout ?? _options.DefaultBootTimeout,
                        WorkerBootPurpose.StandbyTarget);
                    currentStandbyAndBooting++;
                }
            }
        }
    }

    private void LaunchPendingPipelinePrewarmsLocked(TimeSpan? overrideBootTimeout)
    {
        if (_pendingPipelinePrewarms.Count == 0)
        {
            return;
        }

        for (int i = 0; i < _pendingPipelinePrewarms.Count;)
        {
            PipelinePrewarmRequest prewarm = _pendingPipelinePrewarms[i];
            int targetVersion = prewarm.TargetVersion;

            bool isStale =
                _latestRunSequenceByVersion.GetValueOrDefault(targetVersion) > prewarm.SourceRunSequence ||
                _activeRunTasks.Values.Any(t => t.Version == targetVersion);
            if (isStale)
            {
                Log($"[supervisor] Pruned stale pipeline pre-warm for Revit {targetVersion} (sourceSeq={prewarm.SourceRunSequence}).");
                _pendingPipelinePrewarms.RemoveAll(p =>
                    p.TargetVersion == targetVersion &&
                    p.SourceRunSequence <= prewarm.SourceRunSequence);
                continue;
            }

            if (CountTotalSlotsLocked() >= _options.MaxActiveWorkers)
            {
                break;
            }

            bool alreadyHasStandbyOrBooting = _workers.Any(w =>
                w.Version == targetVersion &&
                w.State is WorkerState.Booting or WorkerState.ReadyStandby);

            if (!alreadyHasStandbyOrBooting)
            {
                LaunchWorkerLocked(
                    targetVersion,
                    preferredWorkerId: null,
                    ResolveSupervisorProjectRoot(targetVersion),
                    overrideBootTimeout ?? _options.DefaultBootTimeout,
                    WorkerBootPurpose.PipelinePrewarm);
            }

            _pendingPipelinePrewarms.RemoveAll(p => p.TargetVersion == targetVersion);
        }
    }

    private void TrimSurplusLookaheadWorkersLocked()
    {
        while (CountNonTerminatingWorkersLocked() > _options.MaxActiveWorkers)
        {
            ManagedWorker? victim = _workers
                .Where(w => w.State is WorkerState.ReadyStandby or WorkerState.Booting)
                .OrderByDescending(w => w.Purpose == WorkerBootPurpose.Lookahead)
                .ThenBy(w => w.State == WorkerState.ReadyStandby ? 0 : 1)
                .FirstOrDefault();

            if (victim is null)
            {
                break;
            }

            if (victim.State == WorkerState.ReadyStandby)
            {
                victim.State = WorkerState.Terminating;
                DetachDisconnectHandlerLocked(victim);
                Log($"[supervisor] Trimming surplus lookahead standby PID={victim.Process?.ProcessId} (Revit {victim.Version}) with empty queue.");
                TrackBackgroundTask(Task.Run(() => TerminateAndRemoveWorkerAsync(victim)));
            }
            else if (victim.State == WorkerState.Booting)
            {
                victim.State = WorkerState.Terminating;
                DetachDisconnectHandlerLocked(victim);
                Log($"[supervisor] Cancelling surplus lookahead boot (Revit {victim.Version}, workerId={victim.WorkerId}) with empty queue.");
                try
                {
                    victim.BootCts.Cancel();
                }
                catch (ObjectDisposedException)
                {
                }
            }
        }
    }

    private bool TryEvictIdleStandbyForOtherWorkLocked(
        IReadOnlyList<(Guid RunId, int Version, PendingWorkItem Item)> orderedPending)
    {
        var queuedTargets = new HashSet<(int Version, string NormalizedProjectRoot)>(
            VersionProjectRootComparer.Instance);
        foreach ((Guid _, int version, PendingWorkItem item) in orderedPending)
        {
            if (!item.Cancelled)
            {
                queuedTargets.Add((version, NormalizeProjectRoot(item.ProjectRoot)));
            }
        }

        ManagedWorker? victim = _workers.FirstOrDefault(w =>
            w.State == WorkerState.ReadyStandby &&
            !queuedTargets.Contains((w.Version, NormalizeProjectRoot(w.ProjectRoot))));

        if (victim is null)
        {
            return false;
        }

        victim.State = WorkerState.Terminating;
        DetachDisconnectHandlerLocked(victim);
        Log($"[supervisor] Evicting idle standby worker PID={victim.Process?.ProcessId} (Revit {victim.Version}, root='{victim.ProjectRoot}') to free cap slot.");

        TrackBackgroundTask(Task.Run(() => TerminateAndRemoveWorkerAsync(victim)));
        return true;
    }

    private void PruneDeadStandbyLocked()
    {
        for (int i = _workers.Count - 1; i >= 0; i--)
        {
            ManagedWorker worker = _workers[i];
            if (worker.State != WorkerState.ReadyStandby)
            {
                continue;
            }

            bool alive = worker.Process is { HasExited: false } &&
                         worker.PipeClient is { IsConnected: true };
            if (!alive)
            {
                worker.State = WorkerState.Terminating;
                DetachDisconnectHandlerLocked(worker);
                TrackBackgroundTask(Task.Run(() => TerminateAndRemoveWorkerAsync(worker)));
            }
        }
    }

    private ManagedWorker? FindHealthyStandbyLocked(int version, string projectRoot)
    {
        return _workers.FirstOrDefault(w =>
            w.Version == version &&
            ProjectRootsEqual(w.ProjectRoot, projectRoot) &&
            w.State == WorkerState.ReadyStandby &&
            w.Process is { HasExited: false } &&
            w.PipeClient is { IsConnected: true });
    }

    private void AssignWorkerToActiveLocked(ManagedWorker worker)
    {
        worker.State = WorkerState.Active;
        DetachDisconnectHandlerLocked(worker);
    }

    private void LaunchWorkerLocked(
        int version,
        string? preferredWorkerId,
        string? projectRootOverride,
        TimeSpan bootTimeout,
        WorkerBootPurpose purpose)
    {
        RevitInstallation? installation = _discovery.TryFindInstallation(version);
        if (installation is null)
        {
            var missingEx = new DirectoryNotFoundException(
                $"Autodesk Revit {version} installation was not found.");
            FailOnePendingLocked(version, projectRootOverride, missingEx);
            _standbyTargets.Remove(version);
            return;
        }

        string workerId = string.IsNullOrWhiteSpace(preferredWorkerId)
            ? WorkerLaunchRequest.CreateWorkerId()
            : preferredWorkerId.Trim();

        string projectRoot = TestExecutionHelper.ResolveProjectRoot(
            !string.IsNullOrWhiteSpace(projectRootOverride) ? projectRootOverride : _options.ProjectRoot,
            installation.ExecutablePath);
        var linkedBootCts = CancellationTokenSource.CreateLinkedTokenSource(_supervisorCts.Token);

        var worker = new ManagedWorker(
            version,
            workerId,
            installation.ExecutablePath,
            projectRoot,
            bootTimeout,
            purpose,
            linkedBootCts);

        _workers.Add(worker);
        Log($"[supervisor] Launching worker (Revit {version}, workerId={workerId}, purpose={purpose}, root='{projectRoot}').");

        TrackBackgroundTask(Task.Run(() => BootWorkerAsync(worker)));
    }

    private async Task BootWorkerAsync(ManagedWorker worker)
    {
        try
        {
            var launchRequest = new WorkerLaunchRequest
            {
                RevitVersion = worker.Version,
                ExecutablePath = worker.ExecutablePath,
                Arguments = "/nosplash",
                WorkerId = worker.WorkerId,
                ProjectRoot = worker.ProjectRoot
            };

            LaunchedWorkerProcess process;
            IPipeTestClient pipeClient;
            StartupBootProtector? protector = TestExecutionHelper.ResolveStartupBootProtector(
                _launcher,
                _startupProtector,
                _diagnosticLogger);

            if (protector is not null && OperatingSystem.IsWindows())
            {
                ProtectedWorkerBootOptions bootOptions = protector.DefaultOptions with
                {
                    BootTimeout = worker.BootTimeout
                };

                ProtectedWorkerBootResult bootResult = await protector.BootWorkerWithStartupProtectionAsync(
                    _launcher,
                    _pipeClientFactory,
                    launchRequest,
                    bootOptions,
                    worker.BootCts.Token).ConfigureAwait(false);

                process = bootResult.Worker;
                pipeClient = bootResult.PipeClient;

                bool shouldAbortProtected;
                lock (_sync)
                {
                    worker.WorkerId = process.WorkerId;
                    worker.Process = process;
                    worker.PipeClient = pipeClient;
                    shouldAbortProtected = _draining || _disposed || worker.State == WorkerState.Terminating || IsBootCancellationRequestedSafe(worker);
                    if (shouldAbortProtected)
                    {
                        worker.State = WorkerState.Terminating;
                    }
                }

                if (shouldAbortProtected)
                {
                    await CleanupFailedBootAsync(worker, new OperationCanceledException("Worker boot cancelled during drain.")).ConfigureAwait(false);
                    return;
                }
            }
            else
            {
                process = await TestExecutionHelper.LaunchWithJournalCreationGateAsync(
                    _launcher,
                    launchRequest,
                    worker.BootTimeout,
                    _diagnosticLogger,
                    worker.BootCts.Token).ConfigureAwait(false);

                bool shouldAbort;
                lock (_sync)
                {
                    worker.Process = process;
                    shouldAbort = _draining || _disposed || worker.State == WorkerState.Terminating || IsBootCancellationRequestedSafe(worker);
                    if (shouldAbort)
                    {
                        worker.State = WorkerState.Terminating;
                    }
                }

                if (shouldAbort)
                {
                    await CleanupFailedBootAsync(worker, new OperationCanceledException("Worker boot cancelled during drain.")).ConfigureAwait(false);
                    return;
                }

                pipeClient = _pipeClientFactory.CreateClient(process);
                lock (_sync)
                {
                    worker.PipeClient = pipeClient;
                    shouldAbort = _draining || _disposed || worker.State == WorkerState.Terminating || IsBootCancellationRequestedSafe(worker);
                    if (shouldAbort)
                    {
                        worker.State = WorkerState.Terminating;
                    }
                }

                if (shouldAbort)
                {
                    await CleanupFailedBootAsync(worker, new OperationCanceledException("Worker boot cancelled during drain.")).ConfigureAwait(false);
                    return;
                }

                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(worker.BootCts.Token);
                timeoutCts.CancelAfter(worker.BootTimeout);

                try
                {
                    await pipeClient.ConnectAndWaitForReadyAsync(process, worker.BootTimeout, timeoutCts.Token)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !IsBootCancellationRequestedSafe(worker))
                {
                    throw new TimeoutException(
                        $"Worker PID {process.ProcessId} pipe '{process.PipeName}' did not report ServerMessage.IsBusy == false within {worker.BootTimeout.TotalSeconds:F1}s.");
                }
            }

            bool isBootCancelled;
            lock (_sync)
            {
                isBootCancelled = _draining || _disposed || IsBootCancellationRequestedSafe(worker);
                if (isBootCancelled || worker.State == WorkerState.Terminating)
                {
                    worker.State = WorkerState.Terminating;
                }
                else
                {
                    if (CountActiveLocked() < _options.MaxActiveWorkers)
                    {
                        PendingWorkItem? matched = DequeuePendingForWorkerLocked(worker.Version, worker.ProjectRoot);
                        if (matched is not null)
                        {
                            worker.State = WorkerState.Active;
                            MarkShardDispatchedLocked(matched.RunId);
                            matched.AssignmentTcs.TrySetResult(new WorkerAssignment(worker, process, pipeClient));
                            worker.WarmWaitTcs.TrySetResult(true);
                            Log($"[supervisor] Worker PID={process.ProcessId} (Revit {worker.Version}) ready ({worker.State}).");
                            ScheduleLocked();
                            return;
                        }
                    }

                    string defaultProjectRoot = ResolveSupervisorProjectRoot(worker.Version);
                    int standbyTarget = _standbyTargets.GetValueOrDefault(worker.Version);
                    int existingReady = _workers.Count(w =>
                        w != worker &&
                        w.Version == worker.Version &&
                        ProjectRootsEqual(w.ProjectRoot, defaultProjectRoot) &&
                        w.State == WorkerState.ReadyStandby);

                    bool keepAsStandbyTarget =
                        ProjectRootsEqual(worker.ProjectRoot, defaultProjectRoot) &&
                        existingReady < standbyTarget &&
                        CountNonTerminatingWorkersLocked() <= _options.MaxActiveWorkers;

                    bool keepAsPipelinePrewarm =
                        worker.Purpose == WorkerBootPurpose.PipelinePrewarm &&
                        ProjectRootsEqual(worker.ProjectRoot, defaultProjectRoot) &&
                        existingReady == 0 &&
                        CountNonTerminatingWorkersLocked() <= _options.MaxActiveWorkers;

                    bool keepAsLookaheadStandby =
                        _scheduler.HasPendingMatching((v, item) =>
                            !item.Cancelled &&
                            v == worker.Version &&
                            ProjectRootsEqual(item.ProjectRoot, worker.ProjectRoot)) &&
                        CountActiveLocked() == _options.MaxActiveWorkers &&
                        CountNonTerminatingWorkersLocked() <= _options.MaxActiveWorkers + (_launcher.UsesCleanShim ? 1 : 0) &&
                        _workers.Count(w => w != worker && w.State is WorkerState.Booting or WorkerState.ReadyStandby) == 0;

                    if (keepAsStandbyTarget || keepAsPipelinePrewarm || keepAsLookaheadStandby)
                    {
                        worker.State = WorkerState.ReadyStandby;
                        AttachDisconnectHandlerLocked(worker, pipeClient);
                        worker.WarmWaitTcs.TrySetResult(true);
                        Log($"[supervisor] Worker PID={process.ProcessId} (Revit {worker.Version}) ready ({worker.State}, purpose={worker.Purpose}).");
                        if (_scheduler.PendingCount > 0 && CountActiveLocked() < _options.MaxActiveWorkers)
                        {
                            ScheduleLocked();
                        }

                        return;
                    }

                    worker.State = WorkerState.Terminating;
                    worker.WarmWaitTcs.TrySetResult(true);
                    Log($"[supervisor] Worker PID={process.ProcessId} (Revit {worker.Version}) unneeded upon ready (target={standbyTarget}, purpose={worker.Purpose}); terminating.");
                }
            }

            await CleanupUnneededOrCancelledBootAsync(
                worker,
                isCancellation: isBootCancelled).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await CleanupFailedBootAsync(worker, ex).ConfigureAwait(false);
        }
    }

    private static bool IsBootCancellationRequestedSafe(ManagedWorker worker)
    {
        try
        {
            return worker.BootCts.IsCancellationRequested;
        }
        catch (ObjectDisposedException)
        {
            return true;
        }
    }

    private Task CleanupUnneededOrCancelledBootAsync(ManagedWorker worker, bool isCancellation)
    {
        return TerminateWorkerOnceAsync(
            worker,
            bootFailureError: isCancellation ? new OperationCanceledException("Worker boot cancelled during drain.") : null,
            isBootCancellation: isCancellation);
    }

    private Task CleanupFailedBootAsync(ManagedWorker worker, Exception error)
    {
        bool isCancellation;
        lock (_sync)
        {
            bool bootCancelled = IsBootCancellationRequestedSafe(worker);
            isCancellation = _draining || _disposed || (error is OperationCanceledException && bootCancelled);
        }

        return TerminateWorkerOnceAsync(worker, error, isCancellation);
    }

    private Task TerminateAndRemoveWorkerAsync(ManagedWorker worker)
    {
        return TerminateWorkerOnceAsync(worker, bootFailureError: null, isBootCancellation: false);
    }

    private Task TerminateWorkerOnceAsync(
        ManagedWorker worker,
        Exception? bootFailureError,
        bool isBootCancellation)
    {
        Task terminationTask;
        lock (_sync)
        {
            if (worker.TerminationTask is not null)
            {
                terminationTask = worker.TerminationTask;
            }
            else
            {
                worker.State = WorkerState.Terminating;
                DetachDisconnectHandlerLocked(worker);
                LaunchedWorkerProcess? process = worker.Process;
                IPipeTestClient? pipeClient = worker.PipeClient;

                terminationTask = ExecuteWorkerTerminationAsync(
                    worker,
                    process,
                    pipeClient,
                    bootFailureError,
                    isBootCancellation);
                worker.TerminationTask = terminationTask;
            }
        }

        return terminationTask;
    }

    private async Task ExecuteWorkerTerminationAsync(
        ManagedWorker worker,
        LaunchedWorkerProcess? process,
        IPipeTestClient? pipeClient,
        Exception? bootFailureError,
        bool isBootCancellation)
    {
        await TestExecutionHelper.TerminateAndDisposeWorkerAsync(process, pipeClient, _diagnosticLogger)
            .ConfigureAwait(false);

        lock (_sync)
        {
            if (_workers.Remove(worker))
            {
                _totalTerminatedWorkers++;
            }

            try
            {
                worker.BootCts.Dispose();
            }
            catch (ObjectDisposedException)
            {
            }

            if (isBootCancellation)
            {
                worker.WarmWaitTcs.TrySetCanceled();
            }
            else if (bootFailureError is not null)
            {
                worker.WarmWaitTcs.TrySetException(bootFailureError);
                FailOnePendingLocked(worker.Version, worker.ProjectRoot, bootFailureError);
                if (_standbyTargets.Remove(worker.Version))
                {
                    Log($"[supervisor] Cleared standby target for Revit {worker.Version} after boot failure: {bootFailureError.Message}");
                }
            }

            if (!_draining && !_disposed)
            {
                ScheduleLocked();
            }
        }
    }

    private async Task<DrainResult> ExecuteDrainAsync()
    {
        Log("[supervisor] Draining Worker Pool...");

        SupervisorIpcServer? ipcServer;
        List<ManagedWorker> workersToStop;

        var readyStandbyToStop = new List<ManagedWorker>();

        lock (_sync)
        {
            ipcServer = _ipcServer;
            _standbyTargets.Clear();
            _pendingPipelinePrewarms.Clear();

            foreach (PendingWorkItem pending in _scheduler.ClearAll())
            {
                pending.Cancelled = true;
                pending.AssignmentTcs.TrySetCanceled();
            }

            workersToStop = new List<ManagedWorker>(_workers);
            foreach (ManagedWorker worker in workersToStop)
            {
                if (worker.State == WorkerState.ReadyStandby)
                {
                    worker.State = WorkerState.Terminating;
                    DetachDisconnectHandlerLocked(worker);
                    readyStandbyToStop.Add(worker);
                }
                else if (worker.State == WorkerState.Booting)
                {
                    worker.State = WorkerState.Terminating;
                    DetachDisconnectHandlerLocked(worker);
                    try
                    {
                        worker.BootCts.Cancel();
                    }
                    catch (ObjectDisposedException)
                    {
                    }
                }
            }

            _idleTimer?.Dispose();
            _idleTimer = null;
        }

        ipcServer?.StopAcceptingNewConnections();

        // Terminate idle ready standby workers immediately; booting workers unwind via BootWorkerAsync.
        if (readyStandbyToStop.Count > 0)
        {
            List<Task> immediateStops = readyStandbyToStop
                .Select(TerminateAndRemoveWorkerAsync)
                .ToList();
            await Task.WhenAll(immediateStops).ConfigureAwait(false);
        }

        // Allow active tests a brief grace window to complete cleanly before cancelling them.
        List<ActiveRunTracking> activeRuns = _activeRunTasks.Values.ToList();
        if (activeRuns.Count > 0)
        {
            if (_options.DrainGraceTimeout > TimeSpan.Zero)
            {
                DateTime deadline = DateTime.UtcNow + _options.DrainGraceTimeout;
                while (!_activeRunTasks.IsEmpty && DateTime.UtcNow < deadline)
                {
                    await Task.Delay(25).ConfigureAwait(false);
                }
            }

            foreach (ActiveRunTracking active in _activeRunTasks.Values)
            {
                try
                {
                    active.ExecutionCts.Cancel();
                }
                catch (ObjectDisposedException)
                {
                }
            }
        }

        try
        {
            _supervisorCts.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        // Wait until all active runs and background boot/termination tasks finish disposing workers.
        DateTime drainWaitDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
        while (DateTime.UtcNow < drainWaitDeadline)
        {
            Task[] remainingBg = _backgroundTasks.Keys.ToArray();
            if (remainingBg.Length > 0)
            {
                try
                {
                    await Task.WhenAll(remainingBg).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Log($"[supervisor] Warning: waiting on background tasks during drain ended with {ex.GetType().Name}: {ex.Message}");
                }
            }

            bool empty;
            lock (_sync)
            {
                empty = _workers.Count == 0 && _inFlightRuns == 0;
            }

            if (empty)
            {
                break;
            }

            await Task.Delay(20).ConfigureAwait(false);
        }

        // Force-cleanup any stubborn worker remnants if active execution didn't unwind cleanly.
        List<ManagedWorker> remnants;
        lock (_sync)
        {
            remnants = new List<ManagedWorker>(_workers);
        }

        foreach (ManagedWorker remnant in remnants)
        {
            await TerminateAndRemoveWorkerAsync(remnant).ConfigureAwait(false);
        }

        int terminatedCount;
        PoolStatusSnapshot finalStatus;
        lock (_sync)
        {
            _bundleLease?.Dispose();
            _bundleLease = null;
            terminatedCount = _totalTerminatedWorkers;
            finalStatus = GetStatusLocked();
        }

        _shutdownTcs.TrySetResult(true);
        Log($"[supervisor] Drain complete (terminatedWorkers={terminatedCount}).");

        return new DrainResult
        {
            Success = true,
            ExitCode = 0,
            TerminatedWorkers = terminatedCount,
            Status = finalStatus
        };
    }

    private PendingWorkItem? DequeuePendingForVersionLocked(int version)
    {
        while (_scheduler.TryDequeueForVersion(version, out _, out PendingWorkItem? candidate))
        {
            if (candidate is not null && !candidate.Cancelled)
            {
                return candidate;
            }
        }

        return null;
    }

    private PendingWorkItem? DequeuePendingForWorkerLocked(int version, string projectRoot)
    {
        while (_scheduler.TryDequeueMatching(
                   (v, item) => v == version && (item.Cancelled || ProjectRootsEqual(item.ProjectRoot, projectRoot)),
                   out _,
                   out PendingWorkItem? candidate))
        {
            if (candidate is not null && !candidate.Cancelled)
            {
                return candidate;
            }
        }

        return null;
    }

    private void MarkShardDispatchedLocked(Guid runId)
    {
        if (!_activeRunTasks.TryGetValue(runId, out ActiveRunTracking? tracking))
        {
            return;
        }

        if (tracking.RemainingUndispatchedShards > 0)
        {
            tracking.RemainingUndispatchedShards--;
        }

        if (tracking.RemainingUndispatchedShards == 0 &&
            tracking.PipelineNextVersion is int nextVersion &&
            !tracking.PipelineTriggered)
        {
            tracking.PipelineTriggered = true;
            bool targetNewerOrActive =
                _latestRunSequenceByVersion.GetValueOrDefault(nextVersion) > tracking.RunSequence ||
                _activeRunTasks.Values.Any(t => t.Version == nextVersion);
            if (!targetNewerOrActive)
            {
                _pendingPipelinePrewarms.Add(new PipelinePrewarmRequest(runId, nextVersion, tracking.RunSequence));
                Log($"[supervisor] Run {runId:N} entered final shard; queued N -> N+1 pipeline pre-warm for Revit {nextVersion}.");
            }
            else
            {
                Log($"[supervisor] Run {runId:N} skipped stale N -> N+1 pipeline pre-warm for Revit {nextVersion}.");
            }
        }
    }

    private void FailOnePendingLocked(int version, string? projectRoot, Exception error)
    {
        PendingWorkItem? pending = !string.IsNullOrWhiteSpace(projectRoot)
            ? DequeuePendingForWorkerLocked(version, projectRoot) ?? DequeuePendingForVersionLocked(version)
            : DequeuePendingForVersionLocked(version);
        if (pending is not null)
        {
            pending.AssignmentTcs.TrySetException(error);
        }
    }

    private void AttachDisconnectHandlerLocked(ManagedWorker worker, IPipeTestClient pipeClient)
    {
        void OnDisconnected()
        {
            lock (_sync)
            {
                if (_draining || _disposed || worker.State != WorkerState.ReadyStandby)
                {
                    return;
                }

                worker.State = WorkerState.Terminating;
                DetachDisconnectHandlerLocked(worker);
                Log($"[supervisor] Standby worker PID={worker.Process?.ProcessId} (Revit {worker.Version}) disconnected unexpectedly; replacing.");
                TrackBackgroundTask(Task.Run(() => TerminateAndRemoveWorkerAsync(worker)));
            }
        }

        worker.DisconnectHandler = OnDisconnected;
        pipeClient.Disconnected += OnDisconnected;
    }

    private static void DetachDisconnectHandlerLocked(ManagedWorker worker)
    {
        if (worker.PipeClient is not null && worker.DisconnectHandler is not null)
        {
            worker.PipeClient.Disconnected -= worker.DisconnectHandler;
            worker.DisconnectHandler = null;
        }
    }

    private int CountTotalSlotsLocked()
    {
        return _workers.Count;
    }

    private int CountNonTerminatingWorkersLocked()
    {
        return _workers.Count(w => w.State != WorkerState.Terminating);
    }

    private int CountActiveLocked()
    {
        return _workers.Count(w => w.State == WorkerState.Active);
    }

    private void EnsureStartedLocked(string? initialAssemblyDir)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureBundleLeaseAcquiredLocked(initialAssemblyDir);

        if (!_started)
        {
            _started = true;
            _lastActivityUtc = _timeProvider.GetUtcNow();
            ArmOrResetIdleTimerLocked();
        }
    }

    private void EnsureBundleLeaseAcquiredLocked(string? assemblyDir)
    {
        if (_bundleLease is null)
        {
            _bundleLease = _bundleLeaseFactory(assemblyDir);
        }
    }

    private void TouchActivityLocked()
    {
        _lastActivityUtc = _timeProvider.GetUtcNow();
        ArmOrResetIdleTimerLocked();
    }

    private void ArmOrResetIdleTimerLocked()
    {
        if (_draining || _disposed || _options.IdleTimeout <= TimeSpan.Zero || _options.IdleTimeout == Timeout.InfiniteTimeSpan)
        {
            return;
        }

        if (_idleTimer is null)
        {
            _idleTimer = _timeProvider.CreateTimer(
                OnIdleTimerElapsed,
                state: null,
                dueTime: _options.IdleTimeout,
                period: Timeout.InfiniteTimeSpan);
        }
        else
        {
            _idleTimer.Change(_options.IdleTimeout, Timeout.InfiniteTimeSpan);
        }
    }

    private void OnIdleTimerElapsed(object? state)
    {
        bool shouldDrain = false;
        lock (_sync)
        {
            if (_draining || _disposed)
            {
                return;
            }

            bool hasActiveWork = _inFlightRuns > 0 ||
                                 _activeWarmCalls > 0 ||
                                 _scheduler.PendingCount > 0 ||
                                 _workers.Any(w => w.State == WorkerState.Active);

            if (hasActiveWork)
            {
                ArmOrResetIdleTimerLocked();
                return;
            }

            TimeSpan elapsed = _timeProvider.GetUtcNow() - _lastActivityUtc;
            if (elapsed < _options.IdleTimeout)
            {
                TimeSpan remaining = _options.IdleTimeout - elapsed;
                _idleTimer?.Change(remaining > TimeSpan.Zero ? remaining : TimeSpan.FromMilliseconds(1), Timeout.InfiniteTimeSpan);
                return;
            }

            shouldDrain = true;
        }

        if (shouldDrain)
        {
            Log($"[supervisor] Idle TTL ({_options.IdleTimeout}) elapsed with zero active work; scaling to zero.");
            _ = Task.Run(async () =>
            {
                try
                {
                    await DrainAsync(CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _diagnosticLogger?.Invoke($"[supervisor] Idle drain error: {ex.Message}");
                }
            });
        }
    }

    private PoolStatusSnapshot GetStatusLocked()
    {
        int booting = 0;
        int readyStandby = 0;
        int active = 0;

        var perVersion = new Dictionary<int, (int Target, int Booting, int ReadyStandby, int Active)>();

        foreach ((int version, int target) in _standbyTargets)
        {
            if (target > 0)
            {
                perVersion[version] = (target, 0, 0, 0);
            }
        }

        foreach (ManagedWorker worker in _workers)
        {
            if (worker.State == WorkerState.Terminating)
            {
                continue;
            }

            (int target, int b, int r, int a) = perVersion.GetValueOrDefault(worker.Version);
            switch (worker.State)
            {
                case WorkerState.Booting:
                    booting++;
                    b++;
                    break;
                case WorkerState.ReadyStandby:
                    readyStandby++;
                    r++;
                    break;
                case WorkerState.Active:
                    active++;
                    a++;
                    break;
            }

            perVersion[worker.Version] = (target, b, r, a);
        }

        var versionDtos = new SortedDictionary<int, VersionWorkerStatus>();
        foreach ((int version, (int target, int b, int r, int a)) in perVersion)
        {
            versionDtos[version] = new VersionWorkerStatus
            {
                Version = version,
                StandbyTarget = target,
                Booting = b,
                ReadyStandby = r,
                Active = a,
                TotalResident = b + r + a
            };
        }

        return new PoolStatusSnapshot
        {
            Running = !_draining && !_disposed,
            SupervisorPid = Environment.ProcessId,
            PipeName = _options.PipeName,
            ShimEnabled = EffectiveShimEnabled,
            Draining = _draining,
            Booting = booting,
            ReadyStandby = readyStandby,
            Active = active,
            TotalResident = booting + readyStandby + active,
            ActiveCap = _options.MaxActiveWorkers,
            QueuedRuns = _scheduler.PendingCount,
            LastActivityUtc = _lastActivityUtc,
            Versions = versionDtos
        };
    }

    private bool EffectiveShimEnabled => _launcher.UsesCleanShim || _options.ShimEnabled;

    private void TrackBackgroundTask(Task task)
    {
        _backgroundTasks[task] = 0;
        _ = task.ContinueWith(
            t => _backgroundTasks.TryRemove(t, out _),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private static string? ResolvePayloadCategory(SupervisorRunPayload payload)
    {
        if (!string.IsNullOrWhiteSpace(payload.Category))
        {
            return payload.Category.Trim();
        }

        if (payload.Categories is { Count: 1 } && !string.IsNullOrWhiteSpace(payload.Categories[0]))
        {
            return payload.Categories[0].Trim();
        }

        return null;
    }

    private int ResolveDefaultLatestVersion()
    {
        return _options.CurrentLatestVersion
            ?? RevitInstallationDiscovery.ReadCurrentLatestVersion(_options.ProjectRoot);
    }

    private IDisposable DefaultAcquireBundleLease(string? assemblyDir)
    {
        var searchDirs = new List<string>();
        if (!string.IsNullOrWhiteSpace(assemblyDir))
        {
            searchDirs.Add(assemblyDir);
        }

        searchDirs.Add(AppContext.BaseDirectory);
        return RicaunBundleLease.Acquire(testAdapterSearchDirectories: searchDirs);
    }

    private string ResolveRunProjectRoot(string? runProjectRoot, string? assemblyPath)
    {
        string? explicitRoot = !string.IsNullOrWhiteSpace(runProjectRoot)
            ? runProjectRoot
            : _options.ProjectRoot;
        return TestExecutionHelper.ResolveProjectRoot(explicitRoot, assemblyPath);
    }

    private string ResolveSupervisorProjectRoot(int version)
    {
        RevitInstallation? installation = _discovery.TryFindInstallation(version);
        return TestExecutionHelper.ResolveProjectRoot(_options.ProjectRoot, installation?.ExecutablePath);
    }

    private static string NormalizeProjectRoot(string projectRoot)
    {
        return Path.GetFullPath(projectRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static bool ProjectRootsEqual(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return false;
        }

        return string.Equals(
            NormalizeProjectRoot(left),
            NormalizeProjectRoot(right),
            StringComparison.OrdinalIgnoreCase);
    }

    private async Task ExecuteShardAsync(
        Guid runId,
        OneShotRunOptions options,
        string runProjectRoot,
        ShardExecutionState state,
        string shardAssemblyPath,
        string? preferredWorkerId,
        CancellationTokenSource shardRunCts)
    {
        WorkerAssignment? assignment = null;
        if (!string.IsNullOrWhiteSpace(preferredWorkerId))
        {
            state.WorkerId = preferredWorkerId;
        }

        try
        {
            assignment = await AcquireWorkerAsync(
                runId,
                options.RevitVersion,
                preferredWorkerId,
                runProjectRoot,
                options.BootTimeout,
                shardRunCts.Token).ConfigureAwait(false);

            state.WorkerId = assignment.Worker.WorkerId;
            state.Dispatched = true;

            // Lazy auto-start policy: keep at least 1 standby worker only for CURRENT_LATEST_VERSION
            // unless already warmed higher (e.g., 2). Older versions keep target=0 unless explicitly warmed.
            ApplyLazyLatestPolicyOnDispatch(options.RevitVersion);

            state.Outcome = await TestExecutionHelper.ExecuteShardOnReadyWorkerAsync(
                assignment.Process,
                assignment.PipeClient,
                shardAssemblyPath,
                state.Shard.RicaunTestFilters,
                options.ExecutionTimeout,
                _diagnosticLogger,
                shardRunCts.Token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            state.Error = ex;
            try
            {
                shardRunCts.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        }
        finally
        {
            if (assignment is not null)
            {
                await ReleaseAndTerminateConsumedWorkerAsync(assignment.Worker).ConfigureAwait(false);
            }
        }
    }

    private void UpdateTimingCacheFromCompletedShards(
        int revitVersion,
        ShardExecutionState[] shardStates)
    {
        try
        {
            var completedShards = shardStates
                .Where(s => s is not null && s.Outcome.HasValue)
                .Select(s => (
                    s.Outcome!.Value.Execution.AssemblyResult,
                    (IReadOnlyList<ricaun.NUnit.Models.TestModel>?)s.Outcome.Value.Execution.StreamedTests,
                    (IReadOnlyList<DiscoveredFixtureTests>?)s.Shard.Fixtures));

            _timingCache.UpdateFromCompletedShards(revitVersion, completedShards);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or TimeoutException)
        {
            Log($"[sharding] Warning: failed to persist fixture timing cache for Revit {revitVersion}: {ex.Message}");
        }
    }

    private static string? ResolveShardPreferredWorkerId(
        string? baseWorkerId,
        int shardIndex,
        int shardCount)
    {
        if (string.IsNullOrWhiteSpace(baseWorkerId))
        {
            return null;
        }

        string trimmed = baseWorkerId.Trim();
        return shardCount <= 1 ? trimmed : $"{trimmed}_s{shardIndex}";
    }

    private void Log(string message)
    {
        _diagnosticLogger?.Invoke(message);
    }

    private sealed class ShardExecutionState
    {
        public ShardExecutionState(int shardIndex, TestShardPlan shard)
        {
            ShardIndex = shardIndex;
            Shard = shard;
        }

        public int ShardIndex { get; }

        public TestShardPlan Shard { get; }

        public string? WorkerId { get; set; }

        public bool Dispatched { get; set; }

        public ShardExecutionOutcome? Outcome { get; set; }

        public Exception? Error { get; set; }
    }

    internal sealed record WorkerAssignment(
        ManagedWorker Worker,
        LaunchedWorkerProcess Process,
        IPipeTestClient PipeClient);

    internal enum WorkerState
    {
        Booting,
        ReadyStandby,
        Active,
        Terminating
    }

    internal enum WorkerBootPurpose
    {
        Demand,
        StandbyTarget,
        Lookahead,
        PipelinePrewarm
    }

    internal sealed class ManagedWorker
    {
        public ManagedWorker(
            int version,
            string workerId,
            string executablePath,
            string projectRoot,
            TimeSpan bootTimeout,
            WorkerBootPurpose purpose,
            CancellationTokenSource bootCts)
        {
            Version = version;
            WorkerId = workerId;
            ExecutablePath = executablePath;
            ProjectRoot = projectRoot;
            BootTimeout = bootTimeout;
            Purpose = purpose;
            BootCts = bootCts;
            State = WorkerState.Booting;
        }

        public Guid SlotId { get; } = Guid.NewGuid();

        public int Version { get; }

        public string WorkerId { get; set; }

        public string ExecutablePath { get; }

        public string ProjectRoot { get; }

        public TimeSpan BootTimeout { get; }

        public WorkerBootPurpose Purpose { get; }

        public CancellationTokenSource BootCts { get; }

        public TaskCompletionSource<bool> WarmWaitTcs { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public WorkerState State { get; set; }

        public LaunchedWorkerProcess? Process { get; set; }

        public IPipeTestClient? PipeClient { get; set; }

        public Action? DisconnectHandler { get; set; }

        public Task? TerminationTask { get; set; }
    }

    private sealed class PendingWorkItem
    {
        public PendingWorkItem(
            Guid runId,
            int version,
            string? preferredWorkerId,
            string projectRoot,
            TimeSpan bootTimeout)
        {
            RunId = runId;
            Version = version;
            PreferredWorkerId = preferredWorkerId;
            ProjectRoot = projectRoot;
            BootTimeout = bootTimeout;
        }

        public Guid RunId { get; }

        public int Version { get; }

        public string? PreferredWorkerId { get; }

        public string ProjectRoot { get; }

        public TimeSpan BootTimeout { get; }

        public bool Cancelled { get; set; }

        public TaskCompletionSource<WorkerAssignment> AssignmentTcs { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class VersionProjectRootComparer : IEqualityComparer<(int Version, string NormalizedProjectRoot)>
    {
        public static VersionProjectRootComparer Instance { get; } = new();

        public bool Equals((int Version, string NormalizedProjectRoot) x, (int Version, string NormalizedProjectRoot) y)
        {
            return x.Version == y.Version &&
                   string.Equals(x.NormalizedProjectRoot, y.NormalizedProjectRoot, StringComparison.OrdinalIgnoreCase);
        }

        public int GetHashCode((int Version, string NormalizedProjectRoot) obj)
        {
            return HashCode.Combine(
                obj.Version,
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.NormalizedProjectRoot));
        }
    }

    private sealed class ActiveRunTracking
    {
        public ActiveRunTracking(
            CancellationTokenSource executionCts,
            int remainingUndispatchedShards,
            int? pipelineNextVersion,
            int version,
            long runSequence)
        {
            ExecutionCts = executionCts;
            RemainingUndispatchedShards = remainingUndispatchedShards;
            PipelineNextVersion = pipelineNextVersion;
            Version = version;
            RunSequence = runSequence;
        }

        public CancellationTokenSource ExecutionCts { get; }

        public int RemainingUndispatchedShards { get; set; }

        public int? PipelineNextVersion { get; }

        public int Version { get; }

        public long RunSequence { get; }

        public bool PipelineTriggered { get; set; }
    }

    private sealed record PipelinePrewarmRequest(Guid RunId, int TargetVersion, long SourceRunSequence);
}
