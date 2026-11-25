using System;
using System.Diagnostics;
using ricaun.NamedPipeWrapper;

namespace RevitWorkerPool.Pipe;

/// <summary>
/// Named-pipe server exposing <c>ricaun.&lt;ProcessName&gt;.&lt;PID&gt;</c> using
/// <see cref="NamedPipeServer{TRead, TWrite}"/> with response/request wire types.
/// </summary>
public sealed class PipeProcessServer<TResponse, TRequest> : NamedPipeServer<TRequest, TResponse>
    where TResponse : class
    where TRequest : class
{
    public PipeProcessServer(string pipeName)
        : base(pipeName)
    {
        PipeName = pipeName;
    }

    public PipeProcessServer(string processName, int processId)
        : this(PipeProcessClient<TResponse, TRequest>.GetPipeName(processName, processId))
    {
    }

    public PipeProcessServer(Process process)
        : this(PipeProcessClient<TResponse, TRequest>.GetPipeName(
            (process ?? throw new ArgumentNullException(nameof(process))).ProcessName,
            process.Id))
    {
    }

    public string PipeName { get; }
}
