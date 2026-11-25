using System.Threading;
using System.Threading.Tasks;

namespace RevitWorkerPool.Launching;

/// <summary>
/// Abstraction for launching isolated Revit worker processes (standard, clean-shimmed, or stub).
/// </summary>
public interface IWorkerProcessLauncher
{
    bool UsesCleanShim { get; }

    Task<LaunchedWorkerProcess> LaunchAsync(
        WorkerLaunchRequest request,
        CancellationToken cancellationToken = default);
}
