using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using RevitWorkerPool.Discovery;
using RevitWorkerPool.Launching;
using RevitWorkerPool.Pipe;
using RevitWorkerPool.Reporting;
using RevitWorkerPool.Sharding;
using RevitWorkerPool.Startup;
using RevitWorkerPool.Windowing;

namespace RevitWorkerPool.Execution;

/// <summary>
/// Result of pre-dispatch ECMA-335 discovery and filter/category resolution.
/// </summary>
internal readonly record struct FilterPreparationResult(
    bool ShouldExecute,
    string[] ResolvedTestFilters,
    OneShotRunResult? ImmediateResult);

/// <summary>
/// Result of pre-dispatch ECMA-335 discovery and duration-weighted LPT fixture sharding.
/// </summary>
internal readonly record struct RunPlanPreparationResult(
    bool ShouldExecute,
    IReadOnlyList<TestShardPlan> Shards,
    OneShotRunResult? ImmediateResult);

/// <summary>
/// Completed pipe execution outcome and wall-clock timestamps for a single shard.
/// </summary>
internal readonly record struct ShardExecutionOutcome(
    PipeTestExecutionResult Execution,
    DateTimeOffset StartTime,
    DateTimeOffset FinishTime);

/// <summary>
/// Shared execution, discovery/filter preparation, sharding plan, TRX generation, failure result,
/// and worker teardown helpers used by both <see cref="OneShotTestRunner"/> and <c>WorkerPoolSupervisor</c>.
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

    /// <summary>
    /// Expands CLI <paramref name="filters"/> and <paramref name="category"/>/<paramref name="categories"/>
    /// against ECMA-335 metadata prior to consuming or booting a single one-shot worker, returning an
    /// immediate zero-match or infrastructure-failure <see cref="OneShotRunResult"/> when execution should short-circuit.
    /// </summary>
    public static FilterPreparationResult PrepareExecutionFilters(
        int revitVersion,
        string fullAssemblyPath,
        string fullTrxPath,
        IReadOnlyList<string>? filters,
        string? category,
        IReadOnlyList<string>? categories,
        bool shimEnabled,
        Action<string>? logger)
    {
        bool hasFilter = filters is not null && filters.Any(f => !string.IsNullOrWhiteSpace(f));
        List<string> effectiveCategories = NormalizeCategories(category, categories);
        if (!hasFilter && effectiveCategories.Count == 0)
        {
            return new FilterPreparationResult(
                ShouldExecute: true,
                ResolvedTestFilters: Array.Empty<string>(),
                ImmediateResult: null);
        }

        (TestDiscoveryResult? discoveryResult, OneShotRunResult? immediate) = DiscoverMatchingTests(
            revitVersion,
            fullAssemblyPath,
            fullTrxPath,
            filters,
            effectiveCategories,
            shimEnabled,
            logger);

        if (immediate is not null || discoveryResult is null)
        {
            return new FilterPreparationResult(
                ShouldExecute: false,
                ResolvedTestFilters: Array.Empty<string>(),
                ImmediateResult: immediate);
        }

        return new FilterPreparationResult(
            ShouldExecute: true,
            ResolvedTestFilters: discoveryResult.RicaunTestFilters.ToArray(),
            ImmediateResult: null);
    }

    /// <summary>
    /// Always discovers all concrete <c>[TestFixture]</c> classes (even when <paramref name="filters"/>
    /// and categories are omitted) and partitions matched fixtures via <see cref="DurationWeightedLptSharder"/>
    /// using historical durations from <paramref name="timingCache"/>.
    /// </summary>
    public static RunPlanPreparationResult PrepareRunPlan(
        int revitVersion,
        string fullAssemblyPath,
        string fullTrxPath,
        IReadOnlyList<string>? filters,
        string? category,
        IReadOnlyList<string>? categories,
        int? requestedShards,
        int maxActiveWorkers,
        FixtureTimingCache timingCache,
        bool shimEnabled,
        Action<string>? logger)
    {
        ArgumentNullException.ThrowIfNull(timingCache);

        if (requestedShards.HasValue && requestedShards.Value <= 0)
        {
            return new RunPlanPreparationResult(
                ShouldExecute: false,
                Shards: Array.Empty<TestShardPlan>(),
                ImmediateResult: InfrastructureFailureResult(
                    revitVersion,
                    fullTrxPath,
                    Array.Empty<string>(),
                    shimEnabled,
                    $"Shard count must be >= 1 (got {requestedShards.Value}).",
                    shardCount: 0));
        }

        List<string> effectiveCategories = NormalizeCategories(category, categories);
        (TestDiscoveryResult? discoveryResult, OneShotRunResult? immediate) = DiscoverMatchingTests(
            revitVersion,
            fullAssemblyPath,
            fullTrxPath,
            filters,
            effectiveCategories,
            shimEnabled,
            logger);

        if (immediate is not null || discoveryResult is null)
        {
            return new RunPlanPreparationResult(
                ShouldExecute: false,
                Shards: Array.Empty<TestShardPlan>(),
                ImmediateResult: immediate);
        }

        IReadOnlyDictionary<string, double> historicalDurations =
            timingCache.LoadFixtureDurations(revitVersion);

        IReadOnlyList<TestShardPlan> shards = DurationWeightedLptSharder.CreatePlan(
            discoveryResult.Fixtures,
            historicalDurations,
            requestedShards,
            maxActiveWorkers);

        logger?.Invoke(
            $"[runner] Planned {shards.Count} shard(s) across {discoveryResult.Fixtures.Count} fixture(s) " +
            $"({discoveryResult.TestFullNames.Count} test method(s)).");

        return new RunPlanPreparationResult(
            ShouldExecute: true,
            Shards: shards,
            ImmediateResult: null);
    }

    public static async Task<OneShotRunResult> ExecuteOnReadyWorkerAsync(
        int revitVersion,
        LaunchedWorkerProcess worker,
        IPipeTestClient pipeClient,
        string fullAssemblyPath,
        string fullTrxPath,
        IReadOnlyList<string> resolvedTestFilters,
        TimeSpan executionTimeout,
        bool shimEnabled,
        Action<string>? logger,
        CancellationToken cancellationToken)
    {
        ShardExecutionOutcome outcome = await ExecuteShardOnReadyWorkerAsync(
            worker,
            pipeClient,
            fullAssemblyPath,
            resolvedTestFilters,
            executionTimeout,
            logger,
            cancellationToken).ConfigureAwait(false);

        TrxSummary summary = TrxWriter.Write(
            fullTrxPath,
            fullAssemblyPath,
            outcome.Execution.AssemblyResult,
            outcome.Execution.StreamedTests,
            outcome.StartTime,
            outcome.FinishTime);

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

    public static async Task<ShardExecutionOutcome> ExecuteShardOnReadyWorkerAsync(
        LaunchedWorkerProcess worker,
        IPipeTestClient pipeClient,
        string shardAssemblyPath,
        IReadOnlyList<string> resolvedTestFilters,
        TimeSpan executionTimeout,
        Action<string>? logger,
        CancellationToken cancellationToken)
    {
        DateTimeOffset runStart = DateTimeOffset.UtcNow;
        string[] testFilters = resolvedTestFilters is null || resolvedTestFilters.Count == 0
            ? Array.Empty<string>()
            : resolvedTestFilters as string[] ?? resolvedTestFilters.ToArray();

        var testRequest = new TestRequest
        {
            TestPathFile = shardAssemblyPath,
            TestFilters = testFilters,
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
        return new ShardExecutionOutcome(execution, runStart, runFinish);
    }

    public static OneShotRunResult InfrastructureFailureResult(
        int version,
        string trxPath,
        IReadOnlyList<string> workerIds,
        bool shimEnabled,
        string error,
        int shardCount = 1)
    {
        return new OneShotRunResult
        {
            Success = false,
            ExitCode = 2,
            Version = version,
            TrxPath = trxPath,
            WorkerIds = workerIds,
            ShardCount = shardCount,
            TotalTests = 0,
            PassedTests = 0,
            FailedTests = 0,
            SkippedTests = 0,
            ZeroMatches = false,
            ShimEnabled = shimEnabled,
            Error = error
        };
    }

    private static List<string> NormalizeCategories(string? category, IReadOnlyList<string>? categories)
    {
        var effectiveCategories = new List<string>();
        var seenCategories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(category))
        {
            string trimmed = category.Trim();
            if (seenCategories.Add(trimmed))
            {
                effectiveCategories.Add(trimmed);
            }
        }

        if (categories is not null)
        {
            foreach (string rawCategory in categories)
            {
                if (string.IsNullOrWhiteSpace(rawCategory))
                {
                    continue;
                }

                string trimmed = rawCategory.Trim();
                if (seenCategories.Add(trimmed))
                {
                    effectiveCategories.Add(trimmed);
                }
            }
        }

        return effectiveCategories;
    }

    private static (TestDiscoveryResult? DiscoveryResult, OneShotRunResult? ImmediateResult) DiscoverMatchingTests(
        int revitVersion,
        string fullAssemblyPath,
        string fullTrxPath,
        IReadOnlyList<string>? filters,
        IReadOnlyList<string> effectiveCategories,
        bool shimEnabled,
        Action<string>? logger)
    {
        DateTimeOffset runStart = DateTimeOffset.UtcNow;
        TestDiscoveryResult discoveryResult;
        try
        {
            discoveryResult = effectiveCategories.Count <= 1
                ? Ecma335TestDiscovery.DiscoverAndFilter(
                    fullAssemblyPath,
                    filters,
                    effectiveCategories.FirstOrDefault())
                : Ecma335TestDiscovery.DiscoverAndFilterByCategories(
                    fullAssemblyPath,
                    filters,
                    effectiveCategories);
        }
        catch (Exception ex)
        {
            return (null, InfrastructureFailureResult(
                revitVersion,
                fullTrxPath,
                Array.Empty<string>(),
                shimEnabled,
                $"Failed to inspect test assembly metadata '{fullAssemblyPath}': {ex.Message}",
                shardCount: 0));
        }

        if (!discoveryResult.HasMatches)
        {
            TrxSummary emptySummary;
            try
            {
                emptySummary = TrxWriter.Write(
                    fullTrxPath,
                    fullAssemblyPath,
                    assemblyModel: null,
                    streamedTests: null,
                    startTime: runStart,
                    finishTime: DateTimeOffset.UtcNow);
            }
            catch (Exception ex)
            {
                return (null, InfrastructureFailureResult(
                    revitVersion,
                    fullTrxPath,
                    Array.Empty<string>(),
                    shimEnabled,
                    $"Failed to write empty TRX '{fullTrxPath}': {ex.Message}",
                    shardCount: 0));
            }

            string categoryDesc = string.Join(",", effectiveCategories);
            int filterCount = filters?.Count ?? 0;
            logger?.Invoke(
                $"[runner] 0 matching tests discovered in '{fullAssemblyPath}' " +
                $"(category='{categoryDesc}', filters={filterCount}); " +
                $"skipping worker launch and wrote empty TRX '{emptySummary.TrxPath}'.");

            return (null, new OneShotRunResult
            {
                Success = true,
                ExitCode = 0,
                Version = revitVersion,
                TrxPath = emptySummary.TrxPath,
                WorkerIds = Array.Empty<string>(),
                ShardCount = 0,
                TotalTests = 0,
                PassedTests = 0,
                FailedTests = 0,
                SkippedTests = 0,
                ZeroMatches = true,
                ShimEnabled = false,
                Error = null
            });
        }

        logger?.Invoke(
            $"[runner] ECMA-335 discovery matched {discoveryResult.TestFullNames.Count} test(s) " +
            $"across {discoveryResult.Fixtures.Count} fixture(s).");

        return (discoveryResult, null);
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
