using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RevitWorkerPool.Launching;
using RevitWorkerPool.Pipe;

namespace RevitWorkerPool.Tests;

/// <summary>
/// Headless <see cref="IWorkerProcessLauncher"/> test double that wires each launched worker
/// to an in-process <see cref="InProcessPipeTestServer"/> without spawning <c>Revit.exe</c>.
/// </summary>
public sealed class StubWorkerProcessLauncher : IWorkerProcessLauncher, IDisposable
{
    private static int _nextPid = 50000;

    private readonly Action<InProcessPipeTestServer, WorkerLaunchRequest>? _configureServer;
    private readonly bool _usesCleanShim;
    private readonly ConcurrentQueue<WorkerLaunchRequest> _requests = new();
    private readonly ConcurrentBag<StubWorkerRecord> _records = new();

    public StubWorkerProcessLauncher(
        Action<InProcessPipeTestServer, WorkerLaunchRequest>? configureServer = null,
        bool usesCleanShim = false)
    {
        _configureServer = configureServer;
        _usesCleanShim = usesCleanShim;
    }

    public bool UsesCleanShim => _usesCleanShim;

    public IReadOnlyCollection<WorkerLaunchRequest> LaunchedRequests => _requests.ToArray();

    public IReadOnlyCollection<StubWorkerRecord> Workers => _records.ToArray();

    public Task<LaunchedWorkerProcess> LaunchAsync(
        WorkerLaunchRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        _requests.Enqueue(request);

        int pid = Interlocked.Increment(ref _nextPid);
        string processName = $"StubRevit_{Guid.NewGuid():N}";
        string workerId = string.IsNullOrWhiteSpace(request.WorkerId)
            ? WorkerLaunchRequest.CreateWorkerId()
            : request.WorkerId;

        var server = new InProcessPipeTestServer(processName, pid);
        _configureServer?.Invoke(server, request);
        server.Start();

        request.OnProcessCreatedBeforeResume?.Invoke(pid);

        var record = new StubWorkerRecord(pid, workerId, server);
        _records.Add(record);

        var launched = new LaunchedWorkerProcess(
            pid,
            processName,
            workerId,
            hasExited: () => record.IsTerminated,
            exitCode: () => record.IsTerminated ? 0 : null,
            terminateAsync: _ =>
            {
                record.Terminate();
                return Task.CompletedTask;
            },
            onDispose: () => record.Dispose());

        return Task.FromResult(launched);
    }

    public void Dispose()
    {
        foreach (StubWorkerRecord record in _records)
        {
            record.Dispose();
        }
    }

    public sealed class StubWorkerRecord : IDisposable
    {
        private int _terminated;
        private int _disposed;

        public StubWorkerRecord(int processId, string workerId, InProcessPipeTestServer server)
        {
            ProcessId = processId;
            WorkerId = workerId;
            Server = server;
        }

        public int ProcessId { get; }

        public string WorkerId { get; }

        public InProcessPipeTestServer Server { get; }

        public bool IsTerminated => Volatile.Read(ref _terminated) != 0;

        public bool IsDisposed => Volatile.Read(ref _disposed) != 0;

        public void Terminate()
        {
            Interlocked.Exchange(ref _terminated, 1);
            Server.Dispose();
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            Terminate();
        }
    }
}
