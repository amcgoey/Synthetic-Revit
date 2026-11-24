using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using RevitWorkerPool.Containment;
using RevitWorkerPool.Discovery;
using RevitWorkerPool.Launching;
using RevitWorkerPool.Pipe;

namespace RevitWorkerPool.Execution;

/// <summary>
/// Orchestrates a single-use worker launch, bundle lease, readiness handshake,
/// named-pipe test execution, TRX emission, and immediate worker termination.
/// </summary>
public sealed class OneShotTestRunner
{
    private readonly RevitInstallationDiscovery _discovery;
    private readonly IWorkerProcessLauncher _launcher;
    private readonly IPipeTestClientFactory _pipeClientFactory;
    private readonly Func<string, IDisposable> _bundleLeaseFactory;
    private readonly Action<string>? _diagnosticLogger;

    public OneShotTestRunner(
        RevitInstallationDiscovery discovery,
        IWorkerProcessLauncher launcher,
        IPipeTestClientFactory pipeClientFactory,
        Func<string, IDisposable>? bundleLeaseFactory = null,
        Action<string>? diagnosticLogger = null)
    {
        _discovery = discovery ?? throw new ArgumentNullException(nameof(discovery));
        _launcher = launcher ?? throw new ArgumentNullException(nameof(launcher));
        _pipeClientFactory = pipeClientFactory ?? throw new ArgumentNullException(nameof(pipeClientFactory));
        _bundleLeaseFactory = bundleLeaseFactory ?? DefaultAcquireBundleLease;
        _diagnosticLogger = diagnosticLogger;
    }

    public async Task<OneShotRunResult> RunAsync(
        OneShotRunOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        string fullTrxPath = Path.GetFullPath(options.TrxPath);
        if (string.IsNullOrWhiteSpace(options.AssemblyPath) || !File.Exists(options.AssemblyPath))
        {
            return FailureResult(
                options.RevitVersion,
                fullTrxPath,
                Array.Empty<string>(),
                $"Test assembly not found: '{options.AssemblyPath}'.");
        }

        string fullAssemblyPath = Path.GetFullPath(options.AssemblyPath);
        RevitInstallation? installation = _discovery.TryFindInstallation(options.RevitVersion);
        if (installation is null)
        {
            return FailureResult(
                options.RevitVersion,
                fullTrxPath,
                Array.Empty<string>(),
                $"Autodesk Revit {options.RevitVersion} installation was not found.");
        }

        string workerId = string.IsNullOrWhiteSpace(options.WorkerId)
            ? WorkerLaunchRequest.CreateWorkerId()
            : options.WorkerId.Trim();

        string projectRoot = TestExecutionHelper.ResolveProjectRoot(options.ProjectRoot, fullAssemblyPath);

        Log($"[runner] Starting one-shot run: version={options.RevitVersion}, workerId={workerId}, exe='{installation.ExecutablePath}'");

        IDisposable? bundleLease = null;
        LaunchedWorkerProcess? worker = null;
        IPipeTestClient? pipeClient = null;

        try
        {
            string? assemblyDir = Path.GetDirectoryName(fullAssemblyPath);
            bundleLease = _bundleLeaseFactory(assemblyDir ?? string.Empty);

            var launchRequest = new WorkerLaunchRequest
            {
                RevitVersion = options.RevitVersion,
                ExecutablePath = installation.ExecutablePath,
                Arguments = "/nosplash",
                WorkerId = workerId,
                ProjectRoot = projectRoot
            };

            worker = await _launcher.LaunchAsync(launchRequest, cancellationToken).ConfigureAwait(false);
            Log($"[runner] Launched worker PID={worker.ProcessId}, pipe='{worker.PipeName}'.");

            pipeClient = _pipeClientFactory.CreateClient(worker);
            await pipeClient.ConnectAndWaitForReadyAsync(worker, options.BootTimeout, cancellationToken)
                .ConfigureAwait(false);
            Log($"[runner] Worker PID={worker.ProcessId} reported IsBusy=false (ready).");

            return await TestExecutionHelper.ExecuteOnReadyWorkerAsync(
                options.RevitVersion,
                worker,
                pipeClient,
                fullAssemblyPath,
                fullTrxPath,
                options.Filters,
                options.ExecutionTimeout,
                shimEnabled: false,
                _diagnosticLogger,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return FailureResult(
                options.RevitVersion,
                fullTrxPath,
                worker is not null ? new[] { worker.WorkerId } : new[] { workerId },
                ex.Message);
        }
        finally
        {
            await TestExecutionHelper.TerminateAndDisposeWorkerAsync(worker, pipeClient, _diagnosticLogger)
                .ConfigureAwait(false);
            bundleLease?.Dispose();
        }
    }

    /// <summary>
    /// Normalizes CLI filter fragments for <c>ricaun.NUnit.Services.WildcardPattern</c>
    /// (which uses full-string wildcard matching without implicit substring wrapping).
    /// </summary>
    public static string[] NormalizeTestFilters(IReadOnlyList<string>? filters)
    {
        if (filters is null || filters.Count == 0)
        {
            return Array.Empty<string>();
        }

        var normalized = new List<string>(filters.Count);
        foreach (string raw in filters)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            string trimmed = raw.Trim();
            if (trimmed.Contains('*') || trimmed.Contains('?'))
            {
                normalized.Add(trimmed);
            }
            else
            {
                normalized.Add($"*{trimmed}*");
            }
        }

        return normalized.ToArray();
    }

    private static IDisposable DefaultAcquireBundleLease(string assemblyDir)
    {
        string[] searchDirs = string.IsNullOrWhiteSpace(assemblyDir)
            ? Array.Empty<string>()
            : new[] { assemblyDir };
        return RicaunBundleLease.Acquire(testAdapterSearchDirectories: searchDirs);
    }

    private static OneShotRunResult FailureResult(
        int version,
        string trxPath,
        IReadOnlyList<string> workerIds,
        string error)
    {
        return TestExecutionHelper.InfrastructureFailureResult(
            version,
            trxPath,
            workerIds,
            shimEnabled: false,
            error);
    }

    private void Log(string message)
    {
        _diagnosticLogger?.Invoke(message);
    }
}
