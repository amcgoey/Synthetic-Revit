using System;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using RevitWorkerPool.Launching;

namespace RevitWorkerPool.Windowing;

/// <summary>
/// <see cref="IWorkerProcessLauncher"/> decorator that registers every spawned worker PID
/// with <see cref="OffScreenWindowTracker"/> prior to <c>ResumeThread</c> and untracks it
/// upon worker exit or disposal.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class OffScreenWindowingLauncher : IWorkerProcessLauncher, IDisposable, IAsyncDisposable
{
    private readonly bool _ownsTracker;
    private int _disposed;

    public OffScreenWindowingLauncher(
        IWorkerProcessLauncher innerLauncher,
        Action<string>? diagnosticLogger = null)
        : this(
            innerLauncher,
            new OffScreenWindowTracker(diagnosticLogger: diagnosticLogger),
            ownsTracker: true)
    {
    }

    public OffScreenWindowingLauncher(
        IWorkerProcessLauncher innerLauncher,
        OffScreenWindowTracker tracker,
        bool ownsTracker = false)
    {
        InnerLauncher = innerLauncher ?? throw new ArgumentNullException(nameof(innerLauncher));
        Tracker = tracker ?? throw new ArgumentNullException(nameof(tracker));
        _ownsTracker = ownsTracker;
    }

    public IWorkerProcessLauncher InnerLauncher { get; }

    public OffScreenWindowTracker Tracker { get; }

    public bool UsesCleanShim => InnerLauncher.UsesCleanShim;

    public async Task<LaunchedWorkerProcess> LaunchAsync(
        WorkerLaunchRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        cancellationToken.ThrowIfCancellationRequested();

        int? preTrackedPid = null;
        Action<int>? innerHook = request.OnProcessCreatedBeforeResume;

        var wrappedRequest = new WorkerLaunchRequest
        {
            RevitVersion = request.RevitVersion,
            ExecutablePath = request.ExecutablePath,
            Arguments = request.Arguments,
            WorkerId = request.WorkerId,
            ProjectRoot = request.ProjectRoot,
            WorkingDirectory = request.WorkingDirectory,
            AdditionalEnvironmentVariables = request.AdditionalEnvironmentVariables,
            OnProcessCreatedBeforeResume = pid =>
            {
                Tracker.TrackProcess(pid);
                preTrackedPid = pid;
                innerHook?.Invoke(pid);
            }
        };

        LaunchedWorkerProcess worker;
        try
        {
            worker = await InnerLauncher.LaunchAsync(wrappedRequest, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            if (preTrackedPid is int pidToClean)
            {
                Tracker.UntrackProcess(pidToClean);
            }

            throw;
        }

        if (preTrackedPid != worker.ProcessId)
        {
            if (preTrackedPid is int oldPid)
            {
                Tracker.UntrackProcess(oldPid);
            }

            Tracker.TrackProcess(worker.ProcessId);
        }

        int trackedPid = worker.ProcessId;
        worker.RegisterExitOrDisposeCallback(() => Tracker.UntrackProcess(trackedPid));
        return worker;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        if (_ownsTracker)
        {
            Tracker.Dispose();
        }
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}
