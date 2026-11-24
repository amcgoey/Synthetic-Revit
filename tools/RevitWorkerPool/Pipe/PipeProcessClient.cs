using System;
using System.Diagnostics;
using ricaun.NamedPipeWrapper;

namespace RevitWorkerPool.Pipe;

/// <summary>
/// Named-pipe client targeting <c>ricaun.&lt;ProcessName&gt;.&lt;PID&gt;</c>.
/// </summary>
public sealed class PipeProcessClient<TRead, TWrite> : NamedPipeClient<TRead, TWrite>
    where TRead : class
    where TWrite : class
{
    public PipeProcessClient(string pipeName)
        : base(pipeName)
    {
        PipeName = pipeName;
    }

    public PipeProcessClient(string processName, int processId)
        : this(GetPipeName(processName, processId))
    {
    }

    public PipeProcessClient(Process process)
        : this(GetPipeName(
            (process ?? throw new ArgumentNullException(nameof(process))).ProcessName,
            process.Id))
    {
    }

    public string PipeName { get; }

    public static string GetPipeName(string processName, int processId)
    {
        if (string.IsNullOrWhiteSpace(processName))
        {
            throw new ArgumentException("Process name must not be empty.", nameof(processName));
        }

        return $"ricaun.{processName}.{processId}";
    }
}
