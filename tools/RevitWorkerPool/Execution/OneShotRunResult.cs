using System.Collections.Generic;
using Newtonsoft.Json;

namespace RevitWorkerPool.Execution;

/// <summary>
/// Machine-readable result emitted by <c>RevitWorkerPool run --json</c>.
/// </summary>
public sealed class OneShotRunResult
{
    [JsonProperty("success")]
    public bool Success { get; init; }

    [JsonProperty("exitCode")]
    public int ExitCode { get; init; }

    [JsonProperty("version")]
    public int Version { get; init; }

    [JsonProperty("trxPath")]
    public string TrxPath { get; init; } = string.Empty;

    [JsonProperty("workerIds")]
    public IReadOnlyList<string> WorkerIds { get; init; } = System.Array.Empty<string>();

    [JsonProperty("shardCount")]
    public int ShardCount { get; init; }

    [JsonProperty("totalTests")]
    public int TotalTests { get; init; }

    [JsonProperty("passedTests")]
    public int PassedTests { get; init; }

    [JsonProperty("failedTests")]
    public int FailedTests { get; init; }

    [JsonProperty("skippedTests")]
    public int SkippedTests { get; init; }

    [JsonProperty("zeroMatches")]
    public bool ZeroMatches { get; init; }

    [JsonProperty("shimEnabled")]
    public bool ShimEnabled { get; init; }

    [JsonProperty("error")]
    public string? Error { get; init; }
}
