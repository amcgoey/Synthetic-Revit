using System;
using System.Collections.Generic;

namespace RevitWorkerPool.Execution;

/// <summary>
/// Options controlling a one-shot test execution against a single worker process.
/// </summary>
public sealed class OneShotRunOptions
{
    public int RevitVersion { get; init; }

    public required string AssemblyPath { get; init; }

    public required string TrxPath { get; init; }

    public IReadOnlyList<string> Filters { get; init; } = Array.Empty<string>();

    public string? Category { get; init; }

    public TimeSpan BootTimeout { get; init; } = TimeSpan.FromSeconds(120);

    public TimeSpan ExecutionTimeout { get; init; } = TimeSpan.FromSeconds(600);

    public string? WorkerId { get; init; }

    public string? ProjectRoot { get; init; }

    public bool Diagnostics { get; init; }

    public bool JsonOutput { get; init; }

    public bool OneShot { get; init; } = true;

    public bool NoShim { get; init; }
}
