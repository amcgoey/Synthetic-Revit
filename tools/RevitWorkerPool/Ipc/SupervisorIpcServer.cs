using System;
using System.Collections.Concurrent;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace RevitWorkerPool.Ipc;

/// <summary>
/// Local named-pipe IPC server accepting concurrent newline-delimited JSON requests
/// for the Worker Pool supervisor (<c>warm</c>, <c>run</c>, <c>status</c>, <c>drain</c>, <c>ping</c>).
/// </summary>
public sealed class SupervisorIpcServer : IAsyncDisposable, IDisposable
{
    private readonly Func<SupervisorIpcRequest, CancellationToken, Task<SupervisorIpcResponse>> _requestHandler;
    private readonly Action<string>? _diagnosticLogger;
    private readonly ConcurrentDictionary<Task, byte> _activeConnections = new();
    private readonly CancellationTokenSource _acceptCts = new();
    private readonly CancellationTokenSource _serverLifetimeCts = new();
    private readonly object _sync = new();

    private Task? _acceptLoopTask;
    private int _acceptStopped;
    private int _disposed;

    public SupervisorIpcServer(
        string pipeName,
        Func<SupervisorIpcRequest, CancellationToken, Task<SupervisorIpcResponse>> requestHandler,
        Action<string>? diagnosticLogger = null)
    {
        if (string.IsNullOrWhiteSpace(pipeName))
        {
            throw new ArgumentException("Pipe name must not be empty.", nameof(pipeName));
        }

        PipeName = pipeName;
        _requestHandler = requestHandler ?? throw new ArgumentNullException(nameof(requestHandler));
        _diagnosticLogger = diagnosticLogger;
    }

    public string PipeName { get; }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        lock (_sync)
        {
            if (_acceptLoopTask is not null)
            {
                return;
            }

            _acceptLoopTask = Task.Run(AcceptLoopAsync);
        }
    }

    /// <summary>
    /// Stops accepting new client connections while allowing active connections
    /// (such as an in-flight <c>drain</c> caller) to flush their responses.
    /// </summary>
    public void StopAcceptingNewConnections()
    {
        if (Interlocked.Exchange(ref _acceptStopped, 1) != 0)
        {
            return;
        }

        try
        {
            _acceptCts.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    /// <summary>
    /// Stops accepting new connections and waits for all in-flight connection handlers
    /// to finish writing and flushing responses.
    /// </summary>
    public async Task StopAndDrainActiveResponsesAsync(CancellationToken cancellationToken = default)
    {
        StopAcceptingNewConnections();

        Task? acceptTask;
        lock (_sync)
        {
            acceptTask = _acceptLoopTask;
        }

        if (acceptTask is not null)
        {
            try
            {
                await acceptTask.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                _diagnosticLogger?.Invoke($"[ipc] Accept loop exited with warning: {ex.Message}");
            }
        }

        Task[] active = _activeConnections.Keys.ToArray();
        if (active.Length > 0)
        {
            try
            {
                await Task.WhenAll(active).WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                _diagnosticLogger?.Invoke($"[ipc] Active connection cleanup warning: {ex.Message}");
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        StopAcceptingNewConnections();

        try
        {
            _serverLifetimeCts.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        try
        {
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await StopAndDrainActiveResponsesAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Best-effort shutdown drain.
        }
        finally
        {
            _acceptCts.Dispose();
            _serverLifetimeCts.Dispose();
        }
    }

    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    private async Task AcceptLoopAsync()
    {
        while (!_acceptCts.IsCancellationRequested)
        {
            NamedPipeServerStream? stream = null;
            try
            {
                stream = new NamedPipeServerStream(
                    PipeName,
                    PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                await stream.WaitForConnectionAsync(_acceptCts.Token).ConfigureAwait(false);

                NamedPipeServerStream connectedStream = stream;
                stream = null;

                Task handlerTask = Task.Run(() => HandleConnectionAsync(connectedStream));
                _activeConnections[handlerTask] = 0;
                _ = handlerTask.ContinueWith(
                    t => _activeConnections.TryRemove(t, out _),
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }
            catch (OperationCanceledException)
            {
                stream?.Dispose();
                break;
            }
            catch (Exception ex)
            {
                stream?.Dispose();
                if (!_acceptCts.IsCancellationRequested)
                {
                    _diagnosticLogger?.Invoke($"[ipc] Accept error on '{PipeName}': {ex.Message}");
                    await Task.Delay(25).ConfigureAwait(false);
                }
            }
        }
    }

    private async Task HandleConnectionAsync(NamedPipeServerStream stream)
    {
        using (stream)
        {
            SupervisorIpcResponse response;
            try
            {
                string? requestLine = await IpcFraming.ReadUtf8LineAsync(
                    stream,
                    IpcFraming.DefaultMaxLineBytes,
                    _serverLifetimeCts.Token).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(requestLine))
                {
                    return;
                }

                SupervisorIpcRequest? request;
                try
                {
                    request = JsonConvert.DeserializeObject<SupervisorIpcRequest>(requestLine);
                }
                catch (JsonException ex)
                {
                    response = new SupervisorIpcResponse
                    {
                        Success = false,
                        ExitCode = 2,
                        Error = $"Malformed IPC request JSON: {ex.Message}"
                    };
                    await WriteResponseAsync(stream, response).ConfigureAwait(false);
                    return;
                }

                if (request is null || string.IsNullOrWhiteSpace(request.Command))
                {
                    response = new SupervisorIpcResponse
                    {
                        RequestId = request?.RequestId ?? string.Empty,
                        Success = false,
                        ExitCode = 2,
                        Error = "IPC request command is required."
                    };
                    await WriteResponseAsync(stream, response).ConfigureAwait(false);
                    return;
                }

                using var executionCts = CancellationTokenSource.CreateLinkedTokenSource(_serverLifetimeCts.Token);
                using var monitorCts = new CancellationTokenSource();

                // Detect client crash/Ctrl+C during long-running requests without blocking write completion.
                Task disconnectMonitorTask = MonitorClientDisconnectAsync(
                    stream,
                    executionCts,
                    monitorCts.Token);

                try
                {
                    response = await _requestHandler(request, executionCts.Token).ConfigureAwait(false);
                }
                finally
                {
                    monitorCts.Cancel();
                    try
                    {
                        await disconnectMonitorTask.ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                    }
                    catch (IOException)
                    {
                    }
                }
            }
            catch (OperationCanceledException)
            {
                response = new SupervisorIpcResponse
                {
                    Success = false,
                    ExitCode = 2,
                    Error = "Supervisor IPC request was cancelled."
                };
            }
            catch (Exception ex)
            {
                response = new SupervisorIpcResponse
                {
                    Success = false,
                    ExitCode = 2,
                    Error = ex.Message
                };
            }

            await WriteResponseAsync(stream, response).ConfigureAwait(false);
        }
    }

    private static async Task MonitorClientDisconnectAsync(
        NamedPipeServerStream stream,
        CancellationTokenSource executionCts,
        CancellationToken monitorToken)
    {
        byte[] probeBuffer = new byte[1];
        try
        {
            while (!monitorToken.IsCancellationRequested)
            {
                int bytesRead = await stream.ReadAsync(probeBuffer.AsMemory(0, 1), monitorToken).ConfigureAwait(false);
                if (bytesRead == 0)
                {
                    executionCts.Cancel();
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (IOException)
        {
            if (!monitorToken.IsCancellationRequested)
            {
                executionCts.Cancel();
            }
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private async Task WriteResponseAsync(NamedPipeServerStream stream, SupervisorIpcResponse response)
    {
        if (!stream.IsConnected)
        {
            return;
        }

        try
        {
            string json = JsonConvert.SerializeObject(response, Formatting.None);
            await IpcFraming.WriteUtf8LineAsync(stream, json, CancellationToken.None).ConfigureAwait(false);

            if (stream.IsConnected)
            {
                stream.WaitForPipeDrain();
            }
        }
        catch (IOException ex)
        {
            _diagnosticLogger?.Invoke($"[ipc] Failed writing response (client disconnected): {ex.Message}");
        }
        catch (ObjectDisposedException)
        {
        }
    }
}
