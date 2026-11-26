using System;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using RevitWorkerPool.Launching;

namespace RevitWorkerPool.Windowing;

/// <summary>
/// <see cref="IWorkerProcessLauncher"/> decorator that provisions a dedicated
/// <see cref="IsolatedDesktopHost"/> (<c>WinSta0\SyntheticWorker_*</c>) per launched worker,
/// passes its <c>FullDesktopName</c> via <see cref="WorkerLaunchRequest.DesktopName"/>,
/// and binds the desktop handle lifecycle to worker exit and Job Object teardown.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class IsolatedDesktopLauncher : IWorkerProcessLauncher
{
    private readonly Action<string>? _diagnosticLogger;

    public IsolatedDesktopLauncher(
        IWorkerProcessLauncher innerLauncher,
        Action<string>? diagnosticLogger = null)
    {
        InnerLauncher = innerLauncher ?? throw new ArgumentNullException(nameof(innerLauncher));
        _diagnosticLogger = diagnosticLogger;
    }

    public IWorkerProcessLauncher InnerLauncher { get; }

    public bool UsesCleanShim => InnerLauncher.UsesCleanShim;

    public async Task<LaunchedWorkerProcess> LaunchAsync(
        WorkerLaunchRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (!string.IsNullOrWhiteSpace(request.DesktopName))
        {
            return await InnerLauncher.LaunchAsync(request, cancellationToken).ConfigureAwait(false);
        }

        string workerTag = string.IsNullOrWhiteSpace(request.WorkerId)
            ? Guid.NewGuid().ToString("N")
            : request.WorkerId.Trim().Replace('\\', '_').Replace('/', '_');
        string rawDesktopName = $"SyntheticWorker_{workerTag}_{Guid.NewGuid():N}";
        string shortDesktopName = rawDesktopName.Length > 64 ? rawDesktopName[..64] : rawDesktopName;

        var desktop = new IsolatedDesktopHost(shortDesktopName);
        var wrappedRequest = new WorkerLaunchRequest
        {
            RevitVersion = request.RevitVersion,
            ExecutablePath = request.ExecutablePath,
            Arguments = request.Arguments,
            WorkerId = request.WorkerId,
            ProjectRoot = request.ProjectRoot,
            WorkingDirectory = request.WorkingDirectory,
            DesktopName = desktop.FullDesktopName,
            AdditionalEnvironmentVariables = request.AdditionalEnvironmentVariables,
            OnProcessCreatedBeforeResume = request.OnProcessCreatedBeforeResume
        };

        LaunchedWorkerProcess worker;
        try
        {
            worker = await InnerLauncher.LaunchAsync(wrappedRequest, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            desktop.Dispose();
            throw;
        }

        worker.AttachDesktop(desktop, desktop.DangerousGetHandle(), desktop.FullDesktopName);
        _diagnosticLogger?.Invoke(
            $"[windowing] Launched worker PID={worker.ProcessId} on isolated desktop '{desktop.FullDesktopName}'.");
        return worker;
    }
}
