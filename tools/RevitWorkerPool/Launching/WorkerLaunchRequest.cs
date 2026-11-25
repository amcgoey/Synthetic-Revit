using System;
using System.Collections.Generic;

namespace RevitWorkerPool.Launching;

/// <summary>
/// Parameters required to launch a Revit (or test-stub) worker process.
/// </summary>
public sealed class WorkerLaunchRequest
{
    public int RevitVersion { get; init; }

    public required string ExecutablePath { get; init; }

    public string Arguments { get; init; } = "/nosplash";

    public string WorkerId { get; init; } = CreateWorkerId();

    public string? ProjectRoot { get; init; }

    public string? WorkingDirectory { get; init; }

    public IReadOnlyDictionary<string, string?>? AdditionalEnvironmentVariables { get; init; }

    /// <summary>
    /// Optional hook invoked after process creation and Job Object assignment while the
    /// primary thread is still suspended (extension seam for #229).
    /// </summary>
    public Action<int>? OnProcessCreatedBeforeResume { get; init; }

    public static string CreateWorkerId()
    {
        return $"w_{DateTime.UtcNow:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}"[..32];
    }
}
