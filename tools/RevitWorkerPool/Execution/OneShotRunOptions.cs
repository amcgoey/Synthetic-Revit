using System;
using System.Collections.Generic;
using RevitWorkerPool.Ipc;

namespace RevitWorkerPool.Execution;

/// <summary>
/// Options controlling a test execution against a single worker or the Worker Pool supervisor.
/// </summary>
public sealed class OneShotRunOptions
{
    public int RevitVersion { get; init; }

    public required string AssemblyPath { get; init; }

    public required string TrxPath { get; init; }

    public IReadOnlyList<string> Filters { get; init; } = Array.Empty<string>();

    public string? Category { get; init; }

    public IReadOnlyList<string> Categories { get; init; } = Array.Empty<string>();

    public TimeSpan BootTimeout { get; init; } = TimeSpan.FromSeconds(120);

    public TimeSpan ExecutionTimeout { get; init; } = TimeSpan.FromSeconds(600);

    public string? WorkerId { get; init; }

    public string? ProjectRoot { get; init; }

    public int? Shards { get; init; }

    public int? NextVersion { get; init; }

    public string PipeName { get; init; } = SupervisorIpcConstants.DefaultPipeName;

    public int IdleTtlMinutes { get; init; } = SupervisorIpcConstants.DefaultIdleTtlMinutes;

    public bool NoShim { get; init; }

    public bool Diagnostics { get; init; }

    public bool JsonOutput { get; init; }

    public bool OneShot { get; init; }
}
