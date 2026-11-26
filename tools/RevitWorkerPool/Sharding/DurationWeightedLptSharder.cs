using System;
using System.Collections.Generic;
using System.Linq;
using RevitWorkerPool.Discovery;
using RevitWorkerPool.Supervisor;

namespace RevitWorkerPool.Sharding;

/// <summary>
/// Represents a single fixture-partitioned test execution shard dispatched to one Standby Worker.
/// </summary>
public sealed record TestShardPlan(
    int ShardIndex,
    IReadOnlyList<DiscoveredFixtureTests> Fixtures,
    IReadOnlyList<string> RicaunTestFilters,
    double EstimatedWeightMs,
    int TotalMethodCount);

/// <summary>
/// Partitions matched <see cref="DiscoveredFixtureTests"/> strictly at <c>[TestFixture]</c>
/// boundaries using duration-weighted Longest Processing Time (LPT) scheduling, falling back
/// to ECMA-335 method counts for unseen fixtures.
/// </summary>
public static class DurationWeightedLptSharder
{
    public const int DefaultAutoShardCap = 4;
    private const double MinMixedFallbackMsPerMethod = 1.0;
    private const double MaxMixedFallbackMsPerMethod = 100.0;

    /// <summary>
    /// Computes the effective shard count ceiling for <paramref name="fixtureCount"/> matching fixtures.
    /// Single-fixture runs always produce <c>1</c> shard; explicit <paramref name="requestedShards"/>
    /// requests scale up to <c>min(fixtureCount, maxActiveCap)</c>, while automatic sharding
    /// (<paramref name="requestedShards"/> is <see langword="null"/>) caps at
    /// <c>min(fixtureCount, min(4, maxActiveCap))</c>.
    /// </summary>
    public static int ResolveShardCount(
        int fixtureCount,
        int? requestedShards = null,
        int maxActiveCap = SupervisorOptions.DefaultMaxActiveWorkers)
    {
        if (fixtureCount <= 0)
        {
            return 0;
        }

        if (requestedShards.HasValue && requestedShards.Value <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(requestedShards),
                requestedShards.Value,
                "Shard count must be >= 1.");
        }

        if (maxActiveCap <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxActiveCap),
                maxActiveCap,
                "MaxActiveWorkers cap must be >= 1.");
        }

        if (fixtureCount == 1)
        {
            return 1;
        }

        if (requestedShards.HasValue)
        {
            return Math.Clamp(Math.Min(fixtureCount, requestedShards.Value), 1, maxActiveCap);
        }

        int autoCap = Math.Min(DefaultAutoShardCap, maxActiveCap);
        return Math.Clamp(Math.Min(fixtureCount, autoCap), 1, autoCap);
    }

    /// <summary>
    /// Partitions <paramref name="fixtures"/> into balanced fixture-level shards without ever
    /// splitting a single <c>[TestFixture]</c> across multiple shards.
    /// </summary>
    public static IReadOnlyList<TestShardPlan> CreatePlan(
        IReadOnlyList<DiscoveredFixtureTests> fixtures,
        IReadOnlyDictionary<string, double>? historicalDurationsMs = null,
        int? requestedShards = null,
        int maxActiveCap = SupervisorOptions.DefaultMaxActiveWorkers)
    {
        ArgumentNullException.ThrowIfNull(fixtures);

        if (fixtures.Count == 0)
        {
            return Array.Empty<TestShardPlan>();
        }

        // Validate shard/cap parameters before computing weights, while deferring shard-count selection
        // until fixture weights and the 100%-positive-timing guard are evaluated.
        int ceilingShardCount = ResolveShardCount(fixtures.Count, requestedShards, maxActiveCap);

        double unseenPerMethodWeight = ComputeUnseenMethodWeight(fixtures, historicalDurationsMs);

        var weightedFixtures = new List<WeightedFixture>(fixtures.Count);
        bool allFixturesHavePositiveCachedTimings = historicalDurationsMs is not null;
        foreach (DiscoveredFixtureTests fixture in fixtures)
        {
            int methodCount = ResolveMethodCount(fixture);
            double weight;
            if (historicalDurationsMs is not null &&
                historicalDurationsMs.TryGetValue(fixture.FixtureFullName, out double cachedMs) &&
                double.IsFinite(cachedMs) &&
                cachedMs >= 0.0)
            {
                weight = cachedMs;
                if (cachedMs <= 0.0)
                {
                    allFixturesHavePositiveCachedTimings = false;
                }
            }
            else
            {
                weight = methodCount * unseenPerMethodWeight;
                allFixturesHavePositiveCachedTimings = false;
            }

            weightedFixtures.Add(new WeightedFixture(fixture, weight, methodCount));
        }

        weightedFixtures.Sort(static (a, b) =>
        {
            int weightCmp = b.Weight.CompareTo(a.Weight);
            if (weightCmp != 0)
            {
                return weightCmp;
            }

            int methodCmp = b.MethodCount.CompareTo(a.MethodCount);
            if (methodCmp != 0)
            {
                return methodCmp;
            }

            return StringComparer.Ordinal.Compare(
                a.Fixture.FixtureFullName,
                b.Fixture.FixtureFullName);
        });

        ShardBucket[] selectedBuckets = !requestedShards.HasValue &&
                                        allFixturesHavePositiveCachedTimings &&
                                        ceilingShardCount > 1
            ? SelectMakespanOptimalBuckets(weightedFixtures, ceilingShardCount)
            : AssignLptBuckets(weightedFixtures, ceilingShardCount);

        var result = new List<TestShardPlan>(selectedBuckets.Length);
        foreach (ShardBucket bucket in selectedBuckets)
        {
            result.Add(bucket.ToPlan());
        }

        return result;
    }

    private static ShardBucket[] SelectMakespanOptimalBuckets(
        IReadOnlyList<WeightedFixture> sortedWeightedFixtures,
        int maxAutoShards)
    {
        ShardBucket[] maxBuckets = AssignLptBuckets(sortedWeightedFixtures, maxAutoShards);
        double targetMakespan = GetMakespan(maxBuckets);
        double tolerance = Math.Max(1e-9, targetMakespan * 1e-12);

        for (int k = 1; k < maxAutoShards; k++)
        {
            ShardBucket[] candidateBuckets = AssignLptBuckets(sortedWeightedFixtures, k);
            if (Math.Abs(GetMakespan(candidateBuckets) - targetMakespan) <= tolerance)
            {
                return candidateBuckets;
            }
        }

        return maxBuckets;
    }

    private static ShardBucket[] AssignLptBuckets(
        IReadOnlyList<WeightedFixture> sortedWeightedFixtures,
        int shardCount)
    {
        var buckets = new ShardBucket[shardCount];
        for (int i = 0; i < shardCount; i++)
        {
            buckets[i] = new ShardBucket(i);
        }

        foreach (WeightedFixture item in sortedWeightedFixtures)
        {
            ShardBucket target = SelectLeastLoadedBucket(buckets);
            target.Add(item);
        }

        return buckets;
    }

    private static double GetMakespan(ShardBucket[] buckets)
    {
        double maxWeight = 0.0;
        foreach (ShardBucket bucket in buckets)
        {
            if (bucket.CumulativeWeight > maxWeight)
            {
                maxWeight = bucket.CumulativeWeight;
            }
        }

        return maxWeight;
    }

    private static double ComputeUnseenMethodWeight(
        IReadOnlyList<DiscoveredFixtureTests> fixtures,
        IReadOnlyDictionary<string, double>? historicalDurationsMs)
    {
        if (historicalDurationsMs is null || historicalDurationsMs.Count == 0)
        {
            return 1.0;
        }

        var knownPerMethodRates = new List<double>();
        foreach (DiscoveredFixtureTests fixture in fixtures)
        {
            if (historicalDurationsMs.TryGetValue(fixture.FixtureFullName, out double cachedMs) &&
                double.IsFinite(cachedMs) &&
                cachedMs > 0.0)
            {
                int methodCount = ResolveMethodCount(fixture);
                knownPerMethodRates.Add(cachedMs / methodCount);
            }
        }

        if (knownPerMethodRates.Count == 0)
        {
            return 1.0;
        }

        knownPerMethodRates.Sort();
        int count = knownPerMethodRates.Count;
        double medianRate = (count % 2 == 1)
            ? knownPerMethodRates[count / 2]
            : (knownPerMethodRates[(count / 2) - 1] + knownPerMethodRates[count / 2]) / 2.0;

        return Math.Clamp(medianRate, MinMixedFallbackMsPerMethod, MaxMixedFallbackMsPerMethod);
    }

    private static int ResolveMethodCount(DiscoveredFixtureTests fixture)
    {
        int rawCount = fixture.TotalMethodsInFixture > 0
            ? fixture.TotalMethodsInFixture
            : fixture.TestFullNames.Count;
        return Math.Max(1, rawCount);
    }

    private static ShardBucket SelectLeastLoadedBucket(ShardBucket[] buckets)
    {
        ShardBucket best = buckets[0];
        for (int i = 1; i < buckets.Length; i++)
        {
            ShardBucket candidate = buckets[i];
            int weightCmp = candidate.CumulativeWeight.CompareTo(best.CumulativeWeight);
            if (weightCmp < 0)
            {
                best = candidate;
                continue;
            }

            if (weightCmp == 0 && candidate.Fixtures.Count < best.Fixtures.Count)
            {
                best = candidate;
            }
        }

        return best;
    }

    private readonly record struct WeightedFixture(
        DiscoveredFixtureTests Fixture,
        double Weight,
        int MethodCount);

    private sealed class ShardBucket
    {
        public ShardBucket(int shardIndex)
        {
            ShardIndex = shardIndex;
        }

        public int ShardIndex { get; }

        public List<DiscoveredFixtureTests> Fixtures { get; } = new();

        public double CumulativeWeight { get; private set; }

        public int TotalMethodCount { get; private set; }

        public void Add(WeightedFixture item)
        {
            Fixtures.Add(item.Fixture);
            CumulativeWeight += item.Weight;
            TotalMethodCount += item.MethodCount;
        }

        public TestShardPlan ToPlan()
        {
            var filters = new List<string>();
            var seenFilters = new HashSet<string>(StringComparer.Ordinal);
            foreach (DiscoveredFixtureTests fixture in Fixtures)
            {
                foreach (string methodFullName in fixture.TestFullNames)
                {
                    string pattern = Ecma335TestDiscovery.FormatRicaunMethodFilter(methodFullName);
                    if (seenFilters.Add(pattern))
                    {
                        filters.Add(pattern);
                    }
                }
            }

            return new TestShardPlan(
                ShardIndex,
                Fixtures,
                filters,
                CumulativeWeight,
                TotalMethodCount);
        }
    }
}
