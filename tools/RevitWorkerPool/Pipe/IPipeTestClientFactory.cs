using RevitWorkerPool.Launching;

namespace RevitWorkerPool.Pipe;

/// <summary>
/// Factory creating <see cref="IPipeTestClient"/> instances for launched worker processes.
/// </summary>
public interface IPipeTestClientFactory
{
    IPipeTestClient CreateClient(LaunchedWorkerProcess worker);
}

/// <summary>
/// Default production <see cref="IPipeTestClientFactory"/> creating <see cref="RevitPipeTestClient"/> instances.
/// </summary>
public sealed class RevitPipeTestClientFactory : IPipeTestClientFactory
{
    private readonly Action<string>? _diagnosticLogger;

    public RevitPipeTestClientFactory(Action<string>? diagnosticLogger = null)
    {
        _diagnosticLogger = diagnosticLogger;
    }

    public IPipeTestClient CreateClient(LaunchedWorkerProcess worker)
    {
        ArgumentNullException.ThrowIfNull(worker);
        return new RevitPipeTestClient(worker.PipeName, _diagnosticLogger);
    }
}
