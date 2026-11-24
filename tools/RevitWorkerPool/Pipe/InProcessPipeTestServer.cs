using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ricaun.NamedPipeWrapper;
using ricaun.NUnit.Models;

namespace RevitWorkerPool.Pipe;

/// <summary>
/// In-process <c>ricaun.RevitTest</c> named-pipe server for headless integration testing
/// against <see cref="RevitPipeTestClient"/> without spawning <c>Revit.exe</c>.
/// </summary>
public sealed class InProcessPipeTestServer : IDisposable
{
    private readonly PipeProcessServer<TestResponse, TestRequest> _server;
    private readonly ConcurrentQueue<TestRequest> _receivedRequests = new();
    private readonly object _sync = new();

    private CancellationTokenSource? _activeExecutionCts;
    private bool _started;
    private bool _disposed;

    public InProcessPipeTestServer(string pipeName)
    {
        if (string.IsNullOrWhiteSpace(pipeName))
        {
            throw new ArgumentException("Pipe name must not be empty.", nameof(pipeName));
        }

        PipeName = pipeName;
        _server = new PipeProcessServer<TestResponse, TestRequest>(pipeName);
        _server.ClientConnected += OnClientConnected;
        _server.ClientDisconnected += OnClientDisconnected;
        _server.ClientMessage += OnClientMessage;
    }

    public InProcessPipeTestServer(string processName, int processId)
        : this(PipeProcessClient<TestResponse, TestRequest>.GetPipeName(processName, processId))
    {
    }

    public string PipeName { get; }

    public bool InitialIsBusy { get; set; }

    public bool SuppressInitialMessage { get; set; }

    public TimeSpan? AutoReadyDelay { get; set; }

    public bool ClientEverConnected { get; private set; }

    public bool ClientDisconnected { get; private set; }

    public IReadOnlyCollection<TestRequest> ReceivedRequests => _receivedRequests.ToArray();

    /// <summary>
    /// Handler invoked when a <see cref="TestRequest"/> arrives from the client.
    /// Receives (request, emitStreamedTestModel, cancellationToken) and returns final <see cref="TestAssemblyModel"/>.
    /// </summary>
    public Func<TestRequest, Action<TestModel>, CancellationToken, Task<TestAssemblyModel>>? ExecutionHandler { get; set; }

    public event Action? OnClientDisconnectedCallback;

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        lock (_sync)
        {
            if (_started)
            {
                return;
            }

            _started = true;
            _server.Start();
        }
    }

    public void PushStatus(bool isBusy, string? info = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _server.PushMessage(new TestResponse
        {
            IsBusy = isBusy,
            Info = info
        });
    }

    public void PushResponse(TestResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        ObjectDisposedException.ThrowIf(_disposed, this);
        _server.PushMessage(response);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _activeExecutionCts?.Cancel();
        _activeExecutionCts?.Dispose();

        _server.ClientConnected -= OnClientConnected;
        _server.ClientDisconnected -= OnClientDisconnected;
        _server.ClientMessage -= OnClientMessage;

        try
        {
            if (_started)
            {
                _server.Stop();
            }
        }
        catch (Exception)
        {
            // Best-effort server shutdown.
        }
    }

    private void OnClientConnected(NamedPipeConnection<TestRequest, TestResponse> connection)
    {
        ClientEverConnected = true;

        if (!SuppressInitialMessage)
        {
            connection.PushMessage(new TestResponse
            {
                IsBusy = InitialIsBusy
            });
        }

        if (InitialIsBusy && AutoReadyDelay is TimeSpan delay)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(delay).ConfigureAwait(false);
                    if (!_disposed && connection.IsConnected)
                    {
                        connection.PushMessage(new TestResponse
                        {
                            IsBusy = false
                        });
                    }
                }
                catch (Exception)
                {
                    // Server disposed before delayed ready notification.
                }
            });
        }
    }

    private void OnClientDisconnected(NamedPipeConnection<TestRequest, TestResponse> connection)
    {
        ClientDisconnected = true;
        _activeExecutionCts?.Cancel();
        OnClientDisconnectedCallback?.Invoke();
    }

    private void OnClientMessage(NamedPipeConnection<TestRequest, TestResponse> connection, TestRequest? request)
    {
        if (request is null || _disposed)
        {
            return;
        }

        _receivedRequests.Enqueue(request);

        CancellationTokenSource cts;
        lock (_sync)
        {
            _activeExecutionCts?.Cancel();
            _activeExecutionCts?.Dispose();
            cts = new CancellationTokenSource();
            _activeExecutionCts = cts;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                connection.PushMessage(new TestResponse
                {
                    IsBusy = true,
                    Info = "Executing tests"
                });

                TestAssemblyModel result;
                if (ExecutionHandler is not null)
                {
                    result = await ExecutionHandler(
                        request,
                        test =>
                        {
                            if (!cts.IsCancellationRequested && connection.IsConnected)
                            {
                                connection.PushMessage(new TestResponse
                                {
                                    IsBusy = true,
                                    Test = test
                                });
                            }
                        },
                        cts.Token).ConfigureAwait(false);
                }
                else
                {
                    result = new TestAssemblyModel
                    {
                        FileName = request.TestPathFile ?? string.Empty,
                        Name = request.TestPathFile ?? string.Empty,
                        Success = true,
                        Tests = new List<TestTypeModel>()
                    };
                }

                if (!cts.IsCancellationRequested && connection.IsConnected)
                {
                    connection.PushMessage(new TestResponse
                    {
                        IsBusy = false,
                        Tests = result,
                        Info = "Tests Finished"
                    });
                }
            }
            catch (OperationCanceledException)
            {
                // Client disconnected mid-execution; mirrors PipeTestServer TestEngine.KillTests.
            }
        });
    }
}
