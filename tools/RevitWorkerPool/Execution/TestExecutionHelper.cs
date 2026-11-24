using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using RevitWorkerPool.Discovery;
using RevitWorkerPool.Launching;
using RevitWorkerPool.Pipe;
using RevitWorkerPool.Reporting;
using RevitWorkerPool.Startup;
using RevitWorkerPool.Windowing;

namespace RevitWorkerPool.Execution;

/// <summary>
/// Shared execution, TRX generation, failure result, and worker teardown helpers
/// used by both <see cref="OneShotTestRunner"/> and <c>WorkerPoolSupervisor</c>.
/// </summary>
internal static class TestExecutionHelper
{
    public static string ResolveProjectRoot(string? explicitProjectRoot, string? assemblyPath)
    {
        if (!string.IsNullOrWhiteSpace(explicitProjectRoot))
        {
            return Path.GetFullPath(explicitProjectRoot);
        }

        string? assemblyDir = string.IsNullOrWhiteSpace(assemblyPath)
            ? null
            : Path.GetDirectoryName(assemblyPath);

        return RevitInstallationDiscovery.FindRepositoryRoot()
            ?? RevitInstallationDiscovery.FindRepositoryRoot(assemblyDir)
            ?? Directory.GetCurrentDirectory();
    }

    public static async Task<OneShotRunResult> ExecuteOnReadyWorkerAsync(
        int revitVersion,
        LaunchedWorkerProcess worker,
        IPipeTestClient pipeClient,
        string fullAssemblyPath,
        string fullTrxPath,
        IReadOnlyList<string> filters,
        TimeSpan executionTimeout,
        bool shimEnabled,
        Action<string>? logger,
        CancellationToken cancellationToken)
    {
        DateTimeOffset runStart = DateTimeOffset.UtcNow;
        string[] normalizedFilters = OneShotTestRunner.NormalizeTestFilters(filters);

        var testRequest = new TestRequest
        {
            TestPathFile = fullAssemblyPath,
            TestFilters = normalizedFilters,
            Info = $"WorkerId={worker.WorkerId}"
        };

        PipeTestExecutionResult execution = await pipeClient.ExecuteTestsAsync(
            testRequest,
            worker,
            executionTimeout,
            onTestCompleted: test =>
            {
                string status = test.Skipped ? "SKIP" : (test.Success ? "PASS" : "FAIL");
                logger?.Invoke($"[test] {status}: {test.FullName} ({test.Time:F1}ms)");
            },
            cancellationToken).ConfigureAwait(false);

        DateTimeOffset runFinish = DateTimeOffset.UtcNow;
        TrxSummary summary = TrxWriter.Write(
            fullTrxPath,
            fullAssemblyPath,
            execution.AssemblyResult,
            execution.StreamedTests,
            runStart,
            runFinish);

        bool zeroMatches = summary.TotalTests == 0;
        bool success = summary.FailedTests == 0;
        int exitCode = success ? 0 : 1;

        return new OneShotRunResult
        {
            Success = success,
            ExitCode = exitCode,
            Version = revitVersion,
            TrxPath = summary.TrxPath,
            WorkerIds = new[] { worker.WorkerId },
            ShardCount = 1,
            TotalTests = summary.TotalTests,
            PassedTests = summary.PassedTests,
            FailedTests = summary.FailedTests,
            SkippedTests = summary.SkippedTests,
            ZeroMatches = zeroMatches,
            ShimEnabled = shimEnabled,
            Error = null
        };
    }

    public static OneShotRunResult InfrastructureFailureResult(
        int version,
        string trxPath,
        IReadOnlyList<string> workerIds,
        bool shimEnabled,
        string error)
    {
        return new OneShotRunResult
        {
            Success = false,
            ExitCode = 2,
            Version = version,
            TrxPath = trxPath,
            WorkerIds = workerIds,
            ShardCount = 1,
            TotalTests = 0,
            PassedTests = 0,
            FailedTests = 0,
            SkippedTests = 0,
            ZeroMatches = false,
            ShimEnabled = shimEnabled,
            Error = error
        };
    }

    public static async Task TerminateAndDisposeWorkerAsync(
        LaunchedWorkerProcess? worker,
        IPipeTestClient? pipeClient,
        Action<string>? logger = null)
    {
        if (pipeClient is not null)
        {
            try
            {
                await pipeClient.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger?.Invoke($"[worker] Warning while disposing pipe client: {ex.Message}");
            }
        }

        if (worker is not null)
        {
            try
            {
                logger?.Invoke($"[worker] Terminating consumed worker PID={worker.ProcessId} ({worker.WorkerId})...");
                await worker.TerminateAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger?.Invoke($"[worker] Warning while terminating worker PID={worker.ProcessId}: {ex.Message}");
            }
            finally
            {
                worker.Dispose();
            }
        }
    }

    public static StartupBootProtector? ResolveStartupBootProtector(
        IWorkerProcessLauncher launcher,
        StartupBootProtector? explicitProtector,
        Action<string>? logger)
    {
        if (explicitProtector is not null)
        {
            return explicitProtector;
        }

        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        IWorkerProcessLauncher current = launcher;
        while (current is OffScreenWindowingLauncher windowing)
        {
            current = windowing.InnerLauncher;
        }

        return current is StandardProcessLauncher
            ? new StartupBootProtector(diagnosticLogger: logger)
            : null;
    }
}
