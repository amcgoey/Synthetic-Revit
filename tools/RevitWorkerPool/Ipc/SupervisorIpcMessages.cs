using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using RevitWorkerPool.Execution;

namespace RevitWorkerPool.Ipc;

/// <summary>
/// Well-known control IPC commands handled by the Worker Pool supervisor.
/// </summary>
public static class SupervisorIpcCommands
{
    public const string Ping = "ping";
    public const string Warm = "warm";
    public const string Run = "run";
    public const string Status = "status";
    public const string Drain = "drain";
}

/// <summary>
/// Newline-delimited JSON request sent from CLI callers to the Worker Pool supervisor.
/// </summary>
public sealed class SupervisorIpcRequest
{
    [JsonProperty("requestId")]
    public string RequestId { get; init; } = Guid.NewGuid().ToString("N");

    [JsonProperty("command")]
    public required string Command { get; init; }

    [JsonProperty("version")]
    public int? Version { get; init; }

    [JsonProperty("count")]
    public int? Count { get; init; }

    [JsonProperty("bootTimeoutSeconds")]
    public double? BootTimeoutSeconds { get; init; }

    [JsonProperty("whenIdle")]
    public bool WhenIdle { get; init; }

    [JsonProperty("force")]
    public bool Force { get; init; }

    [JsonProperty("run")]
    public SupervisorRunPayload? Run { get; init; }
}

/// <summary>
/// Payload for an IPC <c>run</c> command.
/// </summary>
public sealed class SupervisorRunPayload
{
    [JsonProperty("version")]
    public int Version { get; init; }

    [JsonProperty("assemblyPath")]
    public required string AssemblyPath { get; init; }

    [JsonProperty("trxPath")]
    public required string TrxPath { get; init; }

    [JsonProperty("filters")]
    public IReadOnlyList<string> Filters { get; init; } = Array.Empty<string>();

    [JsonProperty("category")]
    public string? Category { get; init; }

    [JsonProperty("categories")]
    public IReadOnlyList<string> Categories { get; init; } = Array.Empty<string>();

    [JsonProperty("bootTimeoutSeconds")]
    public double BootTimeoutSeconds { get; init; } = 120;

    [JsonProperty("executionTimeoutSeconds")]
    public double ExecutionTimeoutSeconds { get; init; } = 600;

    [JsonProperty("workerId")]
    public string? WorkerId { get; init; }

    [JsonProperty("projectRoot")]
    public string? ProjectRoot { get; init; }

    [JsonProperty("shards")]
    public int? Shards { get; init; }

    [JsonProperty("diagnostics")]
    public bool Diagnostics { get; init; }
}

/// <summary>
/// Newline-delimited JSON response returned by the Worker Pool supervisor.
/// </summary>
public sealed class SupervisorIpcResponse
{
    [JsonProperty("requestId")]
    public string RequestId { get; init; } = string.Empty;

    [JsonProperty("success")]
    public bool Success { get; init; }

    [JsonProperty("exitCode")]
    public int ExitCode { get; init; }

    [JsonProperty("error")]
    public string? Error { get; init; }

    [JsonProperty("isDraining")]
    public bool IsDraining { get; init; }

    [JsonProperty("isDrainingWhenIdle")]
    public bool IsDrainingWhenIdle { get; init; }

    [JsonProperty("status")]
    public PoolStatusSnapshot? Status { get; init; }

    [JsonProperty("warmResult")]
    public WarmResult? WarmResult { get; init; }

    [JsonProperty("runResult")]
    public OneShotRunResult? RunResult { get; init; }

    [JsonProperty("drainResult")]
    public DrainResult? DrainResult { get; init; }
}

/// <summary>
/// Live aggregate and per-version worker pool counts returned by <c>status</c>.
/// </summary>
public sealed class PoolStatusSnapshot
{
    [JsonProperty("running")]
    public bool Running { get; init; } = true;

    [JsonProperty("supervisorPid")]
    public int SupervisorPid { get; init; }

    [JsonProperty("pipeName")]
    public string PipeName { get; init; } = string.Empty;

    [JsonProperty("shimEnabled")]
    public bool ShimEnabled { get; init; }

    [JsonProperty("draining")]
    public bool Draining { get; init; }

    [JsonProperty("drainingWhenIdle")]
    public bool DrainingWhenIdle { get; init; }

    [JsonProperty("booting")]
    public int Booting { get; init; }

    [JsonProperty("readyStandby")]
    public int ReadyStandby { get; init; }

    [JsonProperty("active")]
    public int Active { get; init; }

    [JsonProperty("totalResident")]
    public int TotalResident { get; init; }

    [JsonProperty("activeCap")]
    public int ActiveCap { get; init; }

    [JsonProperty("queuedRuns")]
    public int QueuedRuns { get; init; }

    [JsonProperty("activeRuns")]
    public int ActiveRuns { get; init; }

    [JsonProperty("activeWarmCalls")]
    public int ActiveWarmCalls { get; init; }

    [JsonProperty("lastActivityUtc")]
    public DateTimeOffset LastActivityUtc { get; init; }

    [JsonProperty("versions")]
    public IReadOnlyDictionary<int, VersionWorkerStatus> Versions { get; init; } =
        new Dictionary<int, VersionWorkerStatus>();
}

/// <summary>
/// Per-Revit-version worker counts inside <see cref="PoolStatusSnapshot"/>.
/// </summary>
public sealed class VersionWorkerStatus
{
    [JsonProperty("version")]
    public int Version { get; init; }

    [JsonProperty("standbyTarget")]
    public int StandbyTarget { get; init; }

    [JsonProperty("booting")]
    public int Booting { get; init; }

    [JsonProperty("readyStandby")]
    public int ReadyStandby { get; init; }

    [JsonProperty("active")]
    public int Active { get; init; }

    [JsonProperty("totalResident")]
    public int TotalResident { get; init; }
}

/// <summary>
/// Result returned from a supervisor <c>warm</c> request.
/// </summary>
public sealed class WarmResult
{
    [JsonProperty("success")]
    public bool Success { get; init; }

    [JsonProperty("exitCode")]
    public int ExitCode { get; init; }

    [JsonProperty("version")]
    public int Version { get; init; }

    [JsonProperty("targetCount")]
    public int TargetCount { get; init; }

    [JsonProperty("readyCount")]
    public int ReadyCount { get; init; }

    [JsonProperty("error")]
    public string? Error { get; init; }

    [JsonProperty("status")]
    public PoolStatusSnapshot? Status { get; init; }
}

/// <summary>
/// Result returned from a supervisor <c>drain</c> request.
/// </summary>
public sealed class DrainResult
{
    [JsonProperty("success")]
    public bool Success { get; init; }

    [JsonProperty("exitCode")]
    public int ExitCode { get; init; }

    [JsonProperty("terminatedWorkers")]
    public int TerminatedWorkers { get; init; }

    [JsonProperty("activeRuns")]
    public int ActiveRuns { get; init; }

    [JsonProperty("queuedRuns")]
    public int QueuedRuns { get; init; }

    [JsonProperty("activeWarmCalls")]
    public int ActiveWarmCalls { get; init; }

    [JsonProperty("drainingWhenIdle")]
    public bool DrainingWhenIdle { get; init; }

    [JsonProperty("error")]
    public string? Error { get; init; }

    [JsonProperty("status")]
    public PoolStatusSnapshot? Status { get; init; }
}
