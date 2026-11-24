using System;
using RevitWorkerPool.Ipc;

namespace RevitWorkerPool.Supervisor;

/// <summary>
/// Configuration for <see cref="WorkerPoolSupervisor"/>.
/// </summary>
public sealed class SupervisorOptions
{
    public const int DefaultMaxActiveWorkers = 4;
    public const int DefaultWarmWorkerCount = 2;

    public string PipeName { get; init; } = SupervisorIpcConstants.DefaultPipeName;

    public int MaxActiveWorkers { get; init; } = DefaultMaxActiveWorkers;

    public int DefaultWarmCount { get; init; } = DefaultWarmWorkerCount;

    public TimeSpan DefaultBootTimeout { get; init; } = TimeSpan.FromSeconds(120);

    public TimeSpan DefaultExecutionTimeout { get; init; } = TimeSpan.FromSeconds(600);

    public TimeSpan IdleTimeout { get; init; } = TimeSpan.FromMinutes(SupervisorIpcConstants.DefaultIdleTtlMinutes);

    public TimeSpan DrainGraceTimeout { get; init; } = TimeSpan.FromSeconds(5);

    public bool ShimEnabled { get; init; }

    public string? ProjectRoot { get; init; }

    public string? TimingCacheDirectory { get; init; }

    public int? CurrentLatestVersion { get; init; }

    public bool Diagnostics { get; init; }
}
