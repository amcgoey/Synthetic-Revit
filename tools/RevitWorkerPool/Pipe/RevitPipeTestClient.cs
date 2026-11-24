using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using RevitWorkerPool.Launching;
using ricaun.NamedPipeWrapper;
using ricaun.NUnit.Models;

namespace RevitWorkerPool.Pipe;

/// <summary>
/// Named-pipe RPC client implementing the <c>ricaun.RevitTest</c> wire protocol.
/// Readiness strictly requires an incoming <c>ServerMessage</c> packet confirming
/// <c>IsBusy == false</c>, never mere pipe existence or connection.
/// </summary>
public sealed class RevitPipeTestClient : IPipeTestClient
{
    private readonly PipeProcessClient<TestResponse, TestRequest> _client;
    private readonly Action<string>? _diagnosticLogger;
    private readonly object _sync = new();

    private bool _started;
    private bool _connected;
    private bool _isReady;
    private int _disposed;

    public RevitPipeTestClient(string pipeName, Action<string>? diagnosticLogger = null)
    {
        if (string.IsNullOrWhiteSpace(pipeName))
        {
            throw new ArgumentException("Pipe name must not be empty.", nameof(pipeName));
        }

        PipeName = pipeName;
        _diagnosticLogger = diagnosticLogger;
        _client = new PipeProcessClient<TestResponse, TestRequest>(pipeName)
        {
            AutoReconnect = false
        };
        _client.Disconnected += OnPersistentDisconnected;
    }

    public string PipeName { get; }

    public bool IsConnected
    {
        get
        {
            lock (_sync)
            {
                return _connected && Volatile.Read(ref _disposed) == 0;
            }
        }
    }

    public event Action? Disconnected;

    public async Task ConnectAndWaitForReadyAsync(
        LaunchedWorkerProcess worker,
        TimeSpan bootTimeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(worker);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        DateTime deadlineUtc = DateTime.UtcNow + bootTimeout;

        while (!NamedPipeUtils.PipeFileExists(PipeName))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (worker.HasExited)
            {
                throw new InvalidOperationException(
                    $"Worker PID {worker.ProcessId} exited (code {worker.ExitCode?.ToString() ?? "unknown"}) before named pipe '{PipeName}' opened.");
            }

            if (DateTime.UtcNow >= deadlineUtc)
            {
                throw new TimeoutException(
                    $"Timed out after {bootTimeout.TotalSeconds:F1}s waiting for worker PID {worker.ProcessId} pipe '{PipeName}'.");
            }

            await Task.Delay(50, cancellationToken).ConfigureAwait(false);
        }

        var readyTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        int receivedMessage = 0;
        bool lastIsBusy = true;

        void OnConnected(NamedPipeConnection<TestResponse, TestRequest> _)
        {
            lock (_sync)
            {
                _connected = true;
            }
            _diagnosticLogger?.Invoke($"[pipe] Connected to '{PipeName}'.");
        }

        void OnServerMessage(NamedPipeConnection<TestResponse, TestRequest> _, TestResponse? response)
        {
            if (response is null)
            {
                return;
            }

            Volatile.Write(ref receivedMessage, 1);
            lastIsBusy = response.IsBusy;
            _diagnosticLogger?.Invoke($"[pipe] ServerMessage IsBusy={response.IsBusy} Info={response.Info}");

            lock (_sync)
            {
                _connected = true;
                if (!response.IsBusy)
                {
                    _isReady = true;
                }
            }

            if (!response.IsBusy)
            {
                readyTcs.TrySetResult(true);
            }
        }

        void OnDisconnected(NamedPipeConnection<TestResponse, TestRequest> _)
        {
            lock (_sync)
            {
                _connected = false;
            }
            readyTcs.TrySetException(new IOException(
                $"Worker PID {worker.ProcessId} disconnected from '{PipeName}' before reporting IsBusy == false."));
        }

        void OnError(Exception ex)
        {
            _diagnosticLogger?.Invoke($"[pipe] Error during boot handshake on '{PipeName}': {ex.Message}");
            readyTcs.TrySetException(ex);
        }

        _client.Connected += OnConnected;
        _client.ServerMessage += OnServerMessage;
        _client.Disconnected += OnDisconnected;
        _client.Error += OnError;

        try
        {
            lock (_sync)
            {
                if (!_started)
                {
                    _started = true;
                    _client.Start();
                }
            }

            while (!readyTcs.Task.IsCompleted)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (worker.HasExited)
                {
                    throw new InvalidOperationException(
                        $"Worker PID {worker.ProcessId} exited (code {worker.ExitCode?.ToString() ?? "unknown"}) during readiness handshake.");
                }

                TimeSpan remaining = deadlineUtc - DateTime.UtcNow;
                if (remaining <= TimeSpan.Zero)
                {
                    throw new TimeoutException(
                        $"Worker PID {worker.ProcessId} pipe '{PipeName}' did not report ServerMessage.IsBusy == false within {bootTimeout.TotalSeconds:F1}s " +
                        $"(receivedServerMessage={Volatile.Read(ref receivedMessage) == 1}, lastIsBusy={lastIsBusy}).");
                }

                TimeSpan step = remaining < TimeSpan.FromMilliseconds(100)
                    ? remaining
                    : TimeSpan.FromMilliseconds(100);

                await Task.WhenAny(readyTcs.Task, Task.Delay(step, cancellationToken)).ConfigureAwait(false);
            }

            await readyTcs.Task.ConfigureAwait(false);
        }
        finally
        {
            _client.Connected -= OnConnected;
            _client.ServerMessage -= OnServerMessage;
            _client.Disconnected -= OnDisconnected;
            _client.Error -= OnError;
        }
    }

    public async Task<PipeTestExecutionResult> ExecuteTestsAsync(
        TestRequest request,
        LaunchedWorkerProcess worker,
        TimeSpan executionTimeout,
        Action<TestModel>? onTestCompleted = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(worker);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        lock (_sync)
        {
            if (!_connected || !_isReady)
            {
                throw new InvalidOperationException(
                    $"Cannot execute tests on '{PipeName}' before readiness handshake completes.");
            }
        }

        var executionTcs = new TaskCompletionSource<PipeTestExecutionResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var streamedTests = new List<TestModel>();
        string? lastInfo = null;

        void OnServerMessage(NamedPipeConnection<TestResponse, TestRequest> _, TestResponse? response)
        {
            if (response is null)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(response.Info))
            {
                lastInfo = response.Info;
                _diagnosticLogger?.Invoke($"[pipe] Info: {response.Info}");
            }

            if (response.Test is not null)
            {
                lock (streamedTests)
                {
                    streamedTests.Add(response.Test);
                }

                onTestCompleted?.Invoke(response.Test);
            }

            if (response.Tests is not null)
            {
                List<TestModel> snapshot;
                lock (streamedTests)
                {
                    snapshot = new List<TestModel>(streamedTests);
                }

                executionTcs.TrySetResult(new PipeTestExecutionResult(response.Tests, snapshot, lastInfo));
            }
        }

        void OnDisconnected(NamedPipeConnection<TestResponse, TestRequest> _)
        {
            lock (_sync)
            {
                _connected = false;
            }

            executionTcs.TrySetException(new IOException(
                $"Worker PID {worker.ProcessId} disconnected from '{PipeName}' before completing test execution."));
        }

        void OnError(Exception ex)
        {
            _diagnosticLogger?.Invoke($"[pipe] Error during test execution on '{PipeName}': {ex.Message}");
            executionTcs.TrySetException(ex);
        }

        _client.ServerMessage += OnServerMessage;
        _client.Disconnected += OnDisconnected;
        _client.Error += OnError;

        try
        {
            _client.PushMessage(request);

            DateTime deadlineUtc = DateTime.UtcNow + executionTimeout;
            while (!executionTcs.Task.IsCompleted)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (worker.HasExited)
                {
                    throw new InvalidOperationException(
                        $"Worker PID {worker.ProcessId} exited (code {worker.ExitCode?.ToString() ?? "unknown"}) while executing tests.");
                }

                TimeSpan remaining = deadlineUtc - DateTime.UtcNow;
                if (remaining <= TimeSpan.Zero)
                {
                    throw new TimeoutException(
                        $"Timed out after {executionTimeout.TotalSeconds:F1}s waiting for test results from worker PID {worker.ProcessId}.");
                }

                TimeSpan step = remaining < TimeSpan.FromMilliseconds(150)
                    ? remaining
                    : TimeSpan.FromMilliseconds(150);

                await Task.WhenAny(executionTcs.Task, Task.Delay(step, cancellationToken)).ConfigureAwait(false);
            }

            return await executionTcs.Task.ConfigureAwait(false);
        }
        finally
        {
            _client.ServerMessage -= OnServerMessage;
            _client.Disconnected -= OnDisconnected;
            _client.Error -= OnError;
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        lock (_sync)
        {
            _connected = false;
        }

        _client.Disconnected -= OnPersistentDisconnected;

        try
        {
            if (_started)
            {
                _client.Stop();
            }
        }
        catch (Exception)
        {
            // Best-effort pipe shutdown.
        }
    }

    private void OnPersistentDisconnected(NamedPipeConnection<TestResponse, TestRequest> _)
    {
        lock (_sync)
        {
            _connected = false;
        }

        Disconnected?.Invoke();
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}
