using System;
using System.Collections.Generic;
using ricaun.NUnit.Models;

namespace RevitWorkerPool.Reporting;

/// <summary>
/// Input outcome from a single completed Test Shard for TRX aggregation.
/// </summary>
public sealed record TrxShardResult(
    TestAssemblyModel? AssemblyModel,
    IReadOnlyList<TestModel>? StreamedTests = null,
    DateTimeOffset? StartTime = null,
    DateTimeOffset? FinishTime = null);

/// <summary>
/// Combines results from multiple concurrent <c>Test Shard</c> workers into a single
/// Visual Studio 2010 TRX XML report built atop <see cref="TrxWriter"/>.
/// </summary>
public static class TrxMerger
{
    public static TrxSummary WriteMergedTrx(
        string trxPath,
        string assemblyPath,
        IReadOnlyList<TestAssemblyModel?> shardAssemblies,
        DateTimeOffset? startTime = null,
        DateTimeOffset? finishTime = null)
    {
        ArgumentNullException.ThrowIfNull(shardAssemblies);

        var shardResults = new List<TrxShardResult>(shardAssemblies.Count);
        foreach (TestAssemblyModel? asm in shardAssemblies)
        {
            shardResults.Add(new TrxShardResult(asm));
        }

        return WriteMergedTrx(trxPath, assemblyPath, shardResults, startTime, finishTime);
    }

    public static TrxSummary WriteMergedTrx(
        string trxPath,
        string assemblyPath,
        IReadOnlyList<TrxShardResult> shardResults,
        DateTimeOffset? startTime = null,
        DateTimeOffset? finishTime = null)
    {
        ArgumentNullException.ThrowIfNull(shardResults);

        DateTimeOffset overallStart = startTime ?? DateTimeOffset.MaxValue;
        DateTimeOffset overallFinish = finishTime ?? DateTimeOffset.MinValue;

        if (!startTime.HasValue || !finishTime.HasValue)
        {
            foreach (TrxShardResult shard in shardResults)
            {
                if (!startTime.HasValue && shard.StartTime is { } shardStart && shardStart < overallStart)
                {
                    overallStart = shardStart;
                }

                if (!finishTime.HasValue && shard.FinishTime is { } shardFinish && shardFinish > overallFinish)
                {
                    overallFinish = shardFinish;
                }
            }
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        if (overallStart == DateTimeOffset.MaxValue)
        {
            overallStart = now;
        }

        if (overallFinish == DateTimeOffset.MinValue || overallFinish < overallStart)
        {
            overallFinish = now >= overallStart ? now : overallStart;
        }

        var combinedTests = new List<TrxWriter.CollectedTrxTest>();
        for (int shardIndex = 0; shardIndex < shardResults.Count; shardIndex++)
        {
            TrxShardResult shard = shardResults[shardIndex];
            List<(string ClassName, TestModel Test)> shardTests =
                TrxWriter.CollectTests(shard.AssemblyModel, shard.StreamedTests);

            DateTimeOffset shardStart = shard.StartTime ?? overallStart;
            foreach ((string className, TestModel test) in shardTests)
            {
                combinedTests.Add(new TrxWriter.CollectedTrxTest(
                    className,
                    test,
                    shardIndex,
                    shardStart));
            }
        }

        return TrxWriter.WriteCollectedTests(
            trxPath,
            assemblyPath,
            combinedTests,
            overallStart,
            overallFinish);
    }
}
