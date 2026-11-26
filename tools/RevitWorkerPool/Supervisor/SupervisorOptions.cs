using System;
using RevitWorkerPool.Ipc;

namespace RevitWorkerPool.Supervisor;

/// <summary>
/// Configuration for <see cref="WorkerPoolSupervisor"/>.
/// </summary>
public sealed class SupervisorOptions
{
    public const int MinMaxActiveWorkers = 1;
    public const int MaxMaxActiveWorkers = 10;
    public const int DefaultMaxActiveWorkers = 8;
    public const int DefaultWarmWorkerCount = 2;
    public const int DefaultAdmissionFloor = 4;
    public const int MaxConcurrentBoots = 4;
    public const ulong DefaultAdmissionMinAvailableMemoryBytes = 10UL * 1024 * 1024 * 1024;
    public const ulong DefaultBootMemoryReservationBytes = 2UL * 1024 * 1024 * 1024;
    public const ulong DefaultLookaheadMinAvailableMemoryBytes = 12UL * 1024 * 1024 * 1024;

    public string PipeName { get; init; } = SupervisorIpcConstants.DefaultPipeName;

    public int MaxActiveWorkers { get; init; } = DefaultMaxActiveWorkers;

    public int DefaultWarmCount { get; init; } = DefaultWarmWorkerCount;

    public int AdmissionFloor { get; init; } = DefaultAdmissionFloor;

    public ulong AdmissionMinAvailableMemoryBytes { get; init; } = DefaultAdmissionMinAvailableMemoryBytes;

    public ulong BootMemoryReservationBytes { get; init; } = DefaultBootMemoryReservationBytes;

    public ulong LookaheadMinAvailableMemoryBytes { get; init; } = DefaultLookaheadMinAvailableMemoryBytes;

    public IMemoryProbe? MemoryProbe { get; init; }

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
