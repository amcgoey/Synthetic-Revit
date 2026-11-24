using System;

namespace RevitWorkerPool.Launching;

/// <summary>
/// Exception thrown when the clean-launch Detours shim cannot be verified, loaded,
/// or injected into a target worker process. Always fails closed without falling back
/// to unshimmed process creation.
/// </summary>
public sealed class CleanShimLaunchException : Exception
{
    public CleanShimLaunchException(string message)
        : base(message)
    {
    }

    public CleanShimLaunchException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
