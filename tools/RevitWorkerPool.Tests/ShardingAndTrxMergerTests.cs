using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Newtonsoft.Json;
using NUnit.Framework;
using RevitWorkerPool.Discovery;
using RevitWorkerPool.Execution;
using RevitWorkerPool.Ipc;
using RevitWorkerPool.Launching;
using RevitWorkerPool.Pipe;
using RevitWorkerPool.Reporting;
using RevitWorkerPool.Sharding;
using RevitWorkerPool.Supervisor;
using ricaun.NUnit.Models;

namespace RevitWorkerPool.Tests;

[TestFixture]
public class ShardingAndTrxMergerTests
{
    private string _tempRoot = string.Empty;
    private string _timingDir = string.Empty;
    private RevitInstallationDiscovery _discovery = null!;

    [SetUp]
    public void SetUp()
    {
        _tempRoot = Path.Combine(
            Path.GetTempPath(),
            "RevitWorkerPool_ShardingTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);

        _timingDir = Path.Combine(_tempRoot, "TimingCache");
        string revit2026Dir = Path.Combine(_tempRoot, "Autodesk", "Revit 2026");
        Directory.CreateDirectory(revit2026Dir);
        File.WriteAllText(Path.Combine(revit2026Dir, "Revit.exe"), string.Empty);

        _discovery = new RevitInstallationDiscovery(
            () => new[] { Path.Combine(_tempRoot, "Autodesk") },
            () => Array.Empty<UninstallRegistryEntry>());
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }

    [Test]
    public void CreatePlan_NeverSplitsSingleFixture_AndKeepsAllFixtureMethodsTogether()
    {
        var singleFixture = new[]
        {
            new DiscoveredFixtureTests(
                "Suite.MonolithFixture",
                Enumerable.Range(1, 12).Select(i => $"Suite.MonolithFixture.Test{i}").ToArray(),
                TotalMethodsInFixture: 12)
        };

        IReadOnlyList<TestShardPlan> singlePlan = DurationWeightedLptSharder.CreatePlan(
            singleFixture,
            historicalDurationsMs: null,
            requestedShards: 4,
            maxActiveCap: 4);

        Assert.That(singlePlan.Count, Is.EqualTo(1), "Single matching fixture must always map to 1 shard.");
        Assert.That(singlePlan[0].Fixtures.Select(f => f.FixtureFullName), Is.EqualTo(new[] { "Suite.MonolithFixture" }));
        Assert.That(singlePlan[0].RicaunTestFilters.Count, Is.EqualTo(12));

        var multiFixtures = new[]
        {
            new DiscoveredFixtureTests("Suite.FixA", new[] { "Suite.FixA.M1", "Suite.FixA.M2", "Suite.FixA.M3" }, 3),
            new DiscoveredFixtureTests("Suite.FixB", new[] { "Suite.FixB.M1", "Suite.FixB.M2" }, 2),
            new DiscoveredFixtureTests("Suite.FixC", new[] { "Suite.FixC.M1" }, 1)
        };

        IReadOnlyList<TestShardPlan> multiPlan = DurationWeightedLptSharder.CreatePlan(
            multiFixtures,
            historicalDurationsMs: null,
            requestedShards: 4,
            maxActiveCap: 4);

        Assert.That(multiPlan.Count, Is.EqualTo(3));
        foreach (DiscoveredFixtureTests fixture in multiFixtures)
        {
            List<TestShardPlan> owners = multiPlan
                .Where(s => s.Fixtures.Any(f => f.FixtureFullName == fixture.FixtureFullName))
                .ToList();
            Assert.That(owners.Count, Is.EqualTo(1), $"Fixture {fixture.FixtureFullName} must not split across shards.");
            foreach (string method in fixture.TestFullNames)
            {
                Assert.That(owners[0].RicaunTestFilters, Does.Contain($"{method}.*"));
            }
        }
    }

    [Test]
    public void CreatePlan_DecouplesAutoShardCapFourFromHostCap_AndScalesExplicitShardsToHostCap()
    {
        var sixFixtures = Enumerable.Range(1, 6)
            .Select(i => new DiscoveredFixtureTests($"Suite.Fix{i}", new[] { $"Suite.Fix{i}.Run" }, 1))
            .ToArray();
        var tenFixtures = Enumerable.Range(1, 10)
            .Select(i => new DiscoveredFixtureTests($"Suite.Fix{i}", new[] { $"Suite.Fix{i}.Run" }, 1))
            .ToArray();

        // Auto-sharding caps at 4 independent of host-wide MaxActiveWorkers (4, 8, 10).
        Assert.That(DurationWeightedLptSharder.CreatePlan(sixFixtures, null, requestedShards: null, maxActiveCap: 4).Count, Is.EqualTo(4));
        Assert.That(DurationWeightedLptSharder.CreatePlan(tenFixtures, null, requestedShards: null, maxActiveCap: 8).Count, Is.EqualTo(4));
        Assert.That(DurationWeightedLptSharder.CreatePlan(tenFixtures, null, requestedShards: null, maxActiveCap: 10).Count, Is.EqualTo(4));
        Assert.That(DurationWeightedLptSharder.CreatePlan(sixFixtures, null, requestedShards: null, maxActiveCap: 2).Count, Is.EqualTo(2));

        // Explicit --shards scales up to min(fixtureCount, hostMaxActiveWorkers).
        Assert.That(DurationWeightedLptSharder.CreatePlan(sixFixtures, null, requestedShards: 8, maxActiveCap: 4).Count, Is.EqualTo(4));
        Assert.That(DurationWeightedLptSharder.CreatePlan(sixFixtures, null, requestedShards: 8, maxActiveCap: 8).Count, Is.EqualTo(6));
        Assert.That(DurationWeightedLptSharder.CreatePlan(tenFixtures, null, requestedShards: 8, maxActiveCap: 8).Count, Is.EqualTo(8));
        Assert.That(DurationWeightedLptSharder.CreatePlan(tenFixtures, null, requestedShards: 10, maxActiveCap: 10).Count, Is.EqualTo(10));
        Assert.That(DurationWeightedLptSharder.CreatePlan(tenFixtures, null, requestedShards: 12, maxActiveCap: 10).Count, Is.EqualTo(10));
        Assert.That(DurationWeightedLptSharder.CreatePlan(sixFixtures, null, requestedShards: 3, maxActiveCap: 8).Count, Is.EqualTo(3));
        Assert.That(DurationWeightedLptSharder.CreatePlan(sixFixtures, null, requestedShards: 1, maxActiveCap: 8).Count, Is.EqualTo(1));
        Assert.That(DurationWeightedLptSharder.CreatePlan(sixFixtures, null, requestedShards: 4, maxActiveCap: 2).Count, Is.EqualTo(2));

        Assert.That(DurationWeightedLptSharder.ResolveShardCount(0, requestedShards: 4, maxActiveCap: 8), Is.EqualTo(0));
        Assert.That(DurationWeightedLptSharder.ResolveShardCount(1, requestedShards: 8, maxActiveCap: 8), Is.EqualTo(1));
        Assert.That(DurationWeightedLptSharder.ResolveShardCount(10, requestedShards: null, maxActiveCap: 10), Is.EqualTo(4));
        Assert.That(DurationWeightedLptSharder.ResolveShardCount(10, requestedShards: 9, maxActiveCap: 10), Is.EqualTo(9));

        IReadOnlyList<TestShardPlan> forcedOne = DurationWeightedLptSharder.CreatePlan(sixFixtures, null, requestedShards: 1, maxActiveCap: 8);
        Assert.That(forcedOne[0].Fixtures.Count, Is.EqualTo(6));
        Assert.That(forcedOne[0].RicaunTestFilters.Count, Is.EqualTo(6));
    }

    [Test]
    public void CreatePlan_DurationWeightedLpt_EliminatesMethodCountTailSkew()
    {
        var fixtures = new[]
        {
            new DiscoveredFixtureTests("Suite.SlowSingle", new[] { "Suite.SlowSingle.Slow" }, TotalMethodsInFixture: 1),
            new DiscoveredFixtureTests("Suite.SecondSlowSingle", new[] { "Suite.SecondSlowSingle.Slow2" }, TotalMethodsInFixture: 1),
            new DiscoveredFixtureTests(
                "Suite.FastTen",
                Enumerable.Range(1, 10).Select(i => $"Suite.FastTen.T{i}").ToArray(),
                TotalMethodsInFixture: 10),
            new DiscoveredFixtureTests(
                "Suite.FastEight",
                Enumerable.Range(1, 8).Select(i => $"Suite.FastEight.T{i}").ToArray(),
                TotalMethodsInFixture: 8)
        };

        var cachedMs = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["Suite.SlowSingle"] = 30_000.0,
            ["Suite.SecondSlowSingle"] = 20_000.0,
            ["Suite.FastTen"] = 500.0,
            ["Suite.FastEight"] = 400.0
        };

        IReadOnlyList<TestShardPlan> plan = DurationWeightedLptSharder.CreatePlan(
            fixtures,
            cachedMs,
            requestedShards: 2,
            maxActiveCap: 4);

        Assert.That(plan.Count, Is.EqualTo(2));
        Assert.That(plan[0].Fixtures.Select(f => f.FixtureFullName), Is.EqualTo(new[] { "Suite.SlowSingle" }));
        Assert.That(plan[0].EstimatedWeightMs, Is.EqualTo(30_000.0));

        Assert.That(
            plan[1].Fixtures.Select(f => f.FixtureFullName),
            Is.EqualTo(new[] { "Suite.SecondSlowSingle", "Suite.FastTen", "Suite.FastEight" }));
        Assert.That(plan[1].EstimatedWeightMs, Is.EqualTo(20_900.0));
    }

    [Test]
    public void CreatePlan_UnseenFallbackUsesMethodCount_AndKnownSlowDominatesUnseenDefaults()
    {
        var allUnseen = new[]
        {
            new DiscoveredFixtureTests("Suite.F1", new[] { "Suite.F1.M1" }, TotalMethodsInFixture: 1),
            new DiscoveredFixtureTests("Suite.F10", Enumerable.Range(1, 10).Select(i => $"Suite.F10.M{i}").ToArray(), TotalMethodsInFixture: 10),
            new DiscoveredFixtureTests("Suite.F6", Enumerable.Range(1, 6).Select(i => $"Suite.F6.M{i}").ToArray(), TotalMethodsInFixture: 6),
            new DiscoveredFixtureTests("Suite.F4", Enumerable.Range(1, 4).Select(i => $"Suite.F4.M{i}").ToArray(), TotalMethodsInFixture: 4)
        };

        IReadOnlyList<TestShardPlan> unseenPlan = DurationWeightedLptSharder.CreatePlan(
            allUnseen,
            historicalDurationsMs: null,
            requestedShards: 2,
            maxActiveCap: 4);

        Assert.That(unseenPlan[0].Fixtures.Select(f => f.FixtureFullName), Is.EqualTo(new[] { "Suite.F10", "Suite.F1" }));
        Assert.That(unseenPlan[1].Fixtures.Select(f => f.FixtureFullName), Is.EqualTo(new[] { "Suite.F6", "Suite.F4" }));

        // Mixed known-slow (1 method = 30,000ms) + unseen (10 methods) + unseen (2 methods):
        // Known slow must still dominate unseen defaults and sit on its own shard.
        var mixed = new[]
        {
            new DiscoveredFixtureTests("Suite.KnownSlow", new[] { "Suite.KnownSlow.Slow" }, TotalMethodsInFixture: 1),
            new DiscoveredFixtureTests("Suite.UnseenTen", Enumerable.Range(1, 10).Select(i => $"Suite.UnseenTen.M{i}").ToArray(), TotalMethodsInFixture: 10),
            new DiscoveredFixtureTests("Suite.UnseenTwo", new[] { "Suite.UnseenTwo.M1", "Suite.UnseenTwo.M2" }, TotalMethodsInFixture: 2)
        };

        var cachedOnlySlow = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["Suite.KnownSlow"] = 30_000.0
        };

        IReadOnlyList<TestShardPlan> mixedPlan = DurationWeightedLptSharder.CreatePlan(
            mixed,
            cachedOnlySlow,
            requestedShards: 2,
            maxActiveCap: 4);

        Assert.That(mixedPlan[0].Fixtures.Select(f => f.FixtureFullName), Is.EqualTo(new[] { "Suite.KnownSlow" }));
        Assert.That(mixedPlan[1].Fixtures.Select(f => f.FixtureFullName), Is.EqualTo(new[] { "Suite.UnseenTen", "Suite.UnseenTwo" }));
    }

    [Test]
    public void CreatePlan_MakespanAwareAutoSharding_ReducesStragglerShardsAndHandlesNonBinaryMakespans()
    {
        // Straggler-dominated (300s + 50s + 50s + 50s): k=4 makespan is 300s, achieved already at k=2.
        var stragglerFixtures = new[]
        {
            new DiscoveredFixtureTests("Suite.Straggler", new[] { "Suite.Straggler.Run" }, 1),
            new DiscoveredFixtureTests("Suite.SmallA", new[] { "Suite.SmallA.Run" }, 1),
            new DiscoveredFixtureTests("Suite.SmallB", new[] { "Suite.SmallB.Run" }, 1),
            new DiscoveredFixtureTests("Suite.SmallC", new[] { "Suite.SmallC.Run" }, 1)
        };
        var stragglerTimings = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["Suite.Straggler"] = 300_000.0,
            ["Suite.SmallA"] = 50_000.0,
            ["Suite.SmallB"] = 50_000.0,
            ["Suite.SmallC"] = 50_000.0
        };

        IReadOnlyList<TestShardPlan> stragglerPlan = DurationWeightedLptSharder.CreatePlan(
            stragglerFixtures,
            stragglerTimings,
            requestedShards: null,
            maxActiveCap: 8);

        Assert.That(stragglerPlan.Count, Is.EqualTo(2));
        Assert.That(stragglerPlan.Max(s => s.EstimatedWeightMs), Is.EqualTo(300_000.0));

        // Explicit --shards override must still be honored without makespan reduction.
        IReadOnlyList<TestShardPlan> explicitStragglerPlan = DurationWeightedLptSharder.CreatePlan(
            stragglerFixtures,
            stragglerTimings,
            requestedShards: 4,
            maxActiveCap: 8);
        Assert.That(explicitStragglerPlan.Count, Is.EqualTo(4));

        // Non-binary case 1: 300s straggler vs 350s remaining [150, 100, 100] -> k=2 makespan 350s, k=3 makespan 300s == k=4 -> 3 shards.
        var nonBinaryThreeFixtures = new[]
        {
            new DiscoveredFixtureTests("Suite.S300", new[] { "Suite.S300.Run" }, 1),
            new DiscoveredFixtureTests("Suite.S150", new[] { "Suite.S150.Run" }, 1),
            new DiscoveredFixtureTests("Suite.S100A", new[] { "Suite.S100A.Run" }, 1),
            new DiscoveredFixtureTests("Suite.S100B", new[] { "Suite.S100B.Run" }, 1)
        };
        var nonBinaryThreeTimings = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["Suite.S300"] = 300_000.0,
            ["Suite.S150"] = 150_000.0,
            ["Suite.S100A"] = 100_000.0,
            ["Suite.S100B"] = 100_000.0
        };

        IReadOnlyList<TestShardPlan> threeShardPlan = DurationWeightedLptSharder.CreatePlan(
            nonBinaryThreeFixtures,
            nonBinaryThreeTimings,
            requestedShards: null,
            maxActiveCap: 10);

        Assert.That(threeShardPlan.Count, Is.EqualTo(3));
        Assert.That(threeShardPlan.Max(s => s.EstimatedWeightMs), Is.EqualTo(300_000.0));

        // Non-binary case 2: [100, 70, 70, 40] -> k=1: 280, k=2: 140, k=3: 110, k=4: 100 -> 4 shards.
        var nonBinaryFourFixtures = new[]
        {
            new DiscoveredFixtureTests("Suite.W100", new[] { "Suite.W100.Run" }, 1),
            new DiscoveredFixtureTests("Suite.W70A", new[] { "Suite.W70A.Run" }, 1),
            new DiscoveredFixtureTests("Suite.W70B", new[] { "Suite.W70B.Run" }, 1),
            new DiscoveredFixtureTests("Suite.W40", new[] { "Suite.W40.Run" }, 1)
        };
        var nonBinaryFourTimings = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["Suite.W100"] = 100_000.0,
            ["Suite.W70A"] = 70_000.0,
            ["Suite.W70B"] = 70_000.0,
            ["Suite.W40"] = 40_000.0
        };

        IReadOnlyList<TestShardPlan> fourShardPlan = DurationWeightedLptSharder.CreatePlan(
            nonBinaryFourFixtures,
            nonBinaryFourTimings,
            requestedShards: null,
            maxActiveCap: 8);

        Assert.That(fourShardPlan.Count, Is.EqualTo(4));
        Assert.That(fourShardPlan.Max(s => s.EstimatedWeightMs), Is.EqualTo(100_000.0));
    }

    [Test]
    public void CreatePlan_MakespanAwareAutoSharding_Requires100PercentPositiveCachedTimings()
    {
        var fixtures = new[]
        {
            new DiscoveredFixtureTests("Suite.Slow", new[] { "Suite.Slow.Run" }, 1),
            new DiscoveredFixtureTests("Suite.FastA", new[] { "Suite.FastA.Run" }, 1),
            new DiscoveredFixtureTests("Suite.FastB", new[] { "Suite.FastB.Run" }, 1),
            new DiscoveredFixtureTests("Suite.FastC", new[] { "Suite.FastC.Run" }, 1),
            new DiscoveredFixtureTests("Suite.FastD", new[] { "Suite.FastD.Run" }, 1)
        };

        // Cold cache (empty) falls back to min(N, 4) = 4.
        Assert.That(
            DurationWeightedLptSharder.CreatePlan(fixtures, new Dictionary<string, double>(), requestedShards: null, maxActiveCap: 8).Count,
            Is.EqualTo(4));

        // Partial cache (1 matched fixture unseen) falls back to min(N, 4) = 4 even with dominant straggler.
        var partialTimings = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["Suite.Slow"] = 300_000.0,
            ["Suite.FastA"] = 10_000.0,
            ["Suite.FastB"] = 10_000.0,
            ["Suite.FastC"] = 10_000.0
        };
        Assert.That(
            DurationWeightedLptSharder.CreatePlan(fixtures, partialTimings, requestedShards: null, maxActiveCap: 8).Count,
            Is.EqualTo(4));

        // Zero cached duration on any matched fixture falls back to min(N, 4) = 4.
        var zeroDurationTimings = new Dictionary<string, double>(partialTimings, StringComparer.Ordinal)
        {
            ["Suite.FastD"] = 0.0
        };
        Assert.That(
            DurationWeightedLptSharder.CreatePlan(fixtures, zeroDurationTimings, requestedShards: null, maxActiveCap: 8).Count,
            Is.EqualTo(4));

        // Negative / non-finite cached duration also fails the positive-timing guard.
        var negativeDurationTimings = new Dictionary<string, double>(partialTimings, StringComparer.Ordinal)
        {
            ["Suite.FastD"] = -5.0
        };
        Assert.That(
            DurationWeightedLptSharder.CreatePlan(fixtures, negativeDurationTimings, requestedShards: null, maxActiveCap: 8).Count,
            Is.EqualTo(4));

        // Unrelated zero entry in cache does not block makespan reduction when 100% of matched fixtures are positive.
        var allMatchedPositiveWithUnrelatedZero = new Dictionary<string, double>(partialTimings, StringComparer.Ordinal)
        {
            ["Suite.FastD"] = 10_000.0,
            ["Suite.UnmatchedOther"] = 0.0
        };
        Assert.That(
            DurationWeightedLptSharder.CreatePlan(fixtures, allMatchedPositiveWithUnrelatedZero, requestedShards: null, maxActiveCap: 8).Count,
            Is.EqualTo(2));
    }

    [Test]
    public void PrepareRunPlan_DecouplesAutoCapFromHostCap_AndAppliesMakespanReductionOnlyWhenAllMatchedFixturesHavePositiveCachedTimings()
    {
        string asmDir = Path.Combine(_tempRoot, "PlanPrep");
        Directory.CreateDirectory(asmDir);
        string assemblyPath = TestAssemblyCompiler.CompileToFile(
            Path.Combine(asmDir, "Synthetic.RevitTests.2026.dll"),
            "Synthetic.RevitTests.2026",
            """
            using NUnit.Framework;

            namespace PlanSuite
            {
                [TestFixture, Category("Subset")]
                public class S300 { [Test] public void Run() { } }

                [TestFixture, Category("Subset")]
                public class S150 { [Test] public void Run() { } }

                [TestFixture, Category("Subset")]
                public class S100A { [Test] public void Run() { } }

                [TestFixture, Category("Subset")]
                public class S100B { [Test] public void Run() { } }

                [TestFixture]
                public class ExtraFastA { [Test] public void Run() { } }

                [TestFixture]
                public class ExtraFastB { [Test] public void Run() { } }
            }
            """);

        string trxPath = Path.Combine(_tempRoot, "out", "plan_prep.trx");
        var timingCache = new FixtureTimingCache(_timingDir);

        // Cold cache: auto-sharding caps at 4 even when host MaxActiveWorkers is 8.
        RunPlanPreparationResult coldAutoPlan = TestExecutionHelper.PrepareRunPlan(
            2026,
            assemblyPath,
            trxPath,
            filters: null,
            category: null,
            categories: null,
            requestedShards: null,
            maxActiveWorkers: 8,
            timingCache,
            shimEnabled: false,
            logger: null);
        Assert.That(coldAutoPlan.ShouldExecute, Is.True);
        Assert.That(coldAutoPlan.Shards.Count, Is.EqualTo(4));

        // Explicit --shards 6 scales up to min(fixtureCount=6, hostMaxActiveWorkers=8) = 6.
        RunPlanPreparationResult explicitSixPlan = TestExecutionHelper.PrepareRunPlan(
            2026,
            assemblyPath,
            trxPath,
            filters: null,
            category: null,
            categories: null,
            requestedShards: 6,
            maxActiveWorkers: 8,
            timingCache,
            shimEnabled: false,
            logger: null);
        Assert.That(explicitSixPlan.ShouldExecute, Is.True);
        Assert.That(explicitSixPlan.Shards.Count, Is.EqualTo(6));

        // Partial cache on Subset (3/4 cached): falls back to min(4, 4) = 4.
        timingCache.RecordObservedDurations(2026, new Dictionary<string, double>
        {
            ["PlanSuite.S300"] = 300_000.0,
            ["PlanSuite.S150"] = 150_000.0,
            ["PlanSuite.S100A"] = 100_000.0
        });
        RunPlanPreparationResult partialSubsetPlan = TestExecutionHelper.PrepareRunPlan(
            2026,
            assemblyPath,
            trxPath,
            filters: null,
            category: "Subset",
            categories: null,
            requestedShards: null,
            maxActiveWorkers: 8,
            timingCache,
            shimEnabled: false,
            logger: null);
        Assert.That(partialSubsetPlan.ShouldExecute, Is.True);
        Assert.That(partialSubsetPlan.Shards.Count, Is.EqualTo(4));

        // Zero cached duration on S100B still falls back to min(4, 4) = 4.
        timingCache.RecordObservedDurations(2026, new Dictionary<string, double>
        {
            ["PlanSuite.S100B"] = 0.0,
            ["PlanSuite.ExtraFastB"] = 0.0
        });
        RunPlanPreparationResult zeroDurationSubsetPlan = TestExecutionHelper.PrepareRunPlan(
            2026,
            assemblyPath,
            trxPath,
            filters: null,
            category: "Subset",
            categories: null,
            requestedShards: null,
            maxActiveWorkers: 8,
            timingCache,
            shimEnabled: false,
            logger: null);
        Assert.That(zeroDurationSubsetPlan.ShouldExecute, Is.True);
        Assert.That(zeroDurationSubsetPlan.Shards.Count, Is.EqualTo(4));

        // 100% positive cached durations on Subset (EMA updates S100B from 0 to 0.6 * 100,000 + 0.4 * 0 = 60,000):
        // Record a second pass so S100B = 100,000 (or direct fresh cache for exact 300s/350s -> 3).
        var cleanTimingCache = new FixtureTimingCache(Path.Combine(_tempRoot, "TimingCacheClean"));
        cleanTimingCache.RecordObservedDurations(2026, new Dictionary<string, double>
        {
            ["PlanSuite.S300"] = 300_000.0,
            ["PlanSuite.S150"] = 150_000.0,
            ["PlanSuite.S100A"] = 100_000.0,
            ["PlanSuite.S100B"] = 100_000.0,
            ["PlanSuite.ExtraFastB"] = 0.0
        });

        RunPlanPreparationResult reducedSubsetPlan = TestExecutionHelper.PrepareRunPlan(
            2026,
            assemblyPath,
            trxPath,
            filters: null,
            category: "Subset",
            categories: null,
            requestedShards: null,
            maxActiveWorkers: 8,
            cleanTimingCache,
            shimEnabled: false,
            logger: null);
        Assert.That(reducedSubsetPlan.ShouldExecute, Is.True);
        Assert.That(reducedSubsetPlan.Shards.Count, Is.EqualTo(3));

        // Full assembly still has unseen ExtraFastA + zero ExtraFastB -> falls back to 4 shards.
        RunPlanPreparationResult fullWithUnseenPlan = TestExecutionHelper.PrepareRunPlan(
            2026,
            assemblyPath,
            trxPath,
            filters: null,
            category: null,
            categories: null,
            requestedShards: null,
            maxActiveWorkers: 8,
            cleanTimingCache,
            shimEnabled: false,
            logger: null);
        Assert.That(fullWithUnseenPlan.ShouldExecute, Is.True);
        Assert.That(fullWithUnseenPlan.Shards.Count, Is.EqualTo(4));

        // Explicit --shards 4 on Subset overrides makespan reduction.
        RunPlanPreparationResult explicitFourSubsetPlan = TestExecutionHelper.PrepareRunPlan(
            2026,
            assemblyPath,
            trxPath,
            filters: null,
            category: "Subset",
            categories: null,
            requestedShards: 4,
            maxActiveWorkers: 8,
            cleanTimingCache,
            shimEnabled: false,
            logger: null);
        Assert.That(explicitFourSubsetPlan.ShouldExecute, Is.True);
        Assert.That(explicitFourSubsetPlan.Shards.Count, Is.EqualTo(4));
    }

    [Test]
    public async Task FixtureTimingCache_PersistsPerVersionEma_ReloadsAcrossRuns_AndIsConcurrencySafe()
    {
        var cache = new FixtureTimingCache(_timingDir);
        string expected2026Path = Path.Combine(_timingDir, "fixture_timings_2026.json");
        Assert.That(cache.GetCacheFilePath(2026), Is.EqualTo(expected2026Path));

        // First observation records observed ms directly.
        cache.RecordObservedDurations(2026, new Dictionary<string, double>
        {
            ["Suite.Slow"] = 30_000.0,
            ["Suite.Fast"] = 500.0
        });

        Assert.That(File.Exists(expected2026Path), Is.True);

        // Second observation applies EMA: 0.6 * observed + 0.4 * previous.
        cache.RecordObservedDurations(2026, new Dictionary<string, double>
        {
            ["Suite.Slow"] = 20_000.0
        });

        // Separate version cache remains isolated.
        cache.RecordObservedDurations(2024, new Dictionary<string, double>
        {
            ["Suite.Slow"] = 12_000.0
        });

        var reloaded = new FixtureTimingCache(_timingDir);
        IReadOnlyDictionary<string, double> timings2026 = reloaded.LoadFixtureDurations(2026);
        IReadOnlyDictionary<string, double> timings2024 = reloaded.LoadFixtureDurations(2024);

        Assert.That(timings2026["Suite.Slow"], Is.EqualTo(24_000.0).Within(0.001));
        Assert.That(timings2026["Suite.Fast"], Is.EqualTo(500.0).Within(0.001));
        Assert.That(timings2024["Suite.Slow"], Is.EqualTo(12_000.0).Within(0.001));

        // Concurrent atomic writes across distinct fixtures must never lose entries or leave temp files.
        Task[] writers = Enumerable.Range(0, 12)
            .Select(i => Task.Run(() => cache.RecordObservedDurations(2026, new Dictionary<string, double>
            {
                [$"Suite.Concurrent_{i}"] = 100.0 + i
            })))
            .ToArray();
        await Task.WhenAll(writers);

        IReadOnlyDictionary<string, double> afterConcurrent = reloaded.LoadFixtureDurations(2026);
        for (int i = 0; i < 12; i++)
        {
            Assert.That(afterConcurrent[$"Suite.Concurrent_{i}"], Is.EqualTo(100.0 + i).Within(0.001));
        }

        string[] leftoverTemps = Directory.GetFiles(_timingDir, "*.tmp");
        Assert.That(leftoverTemps, Is.Empty);
    }

    [Test]
    public void TrxMerger_CombinesShardsWithAccurateCountersDurationsErrorsAndTimes()
    {
        string trxPath = Path.Combine(_tempRoot, "merged", "results_2026.trx");
        string assemblyPath = Path.Combine(_tempRoot, "Synthetic.RevitTests.2026.dll");

        DateTimeOffset t0 = new(2026, 11, 24, 15, 0, 0, TimeSpan.Zero);
        DateTimeOffset t1 = t0.AddSeconds(32);

        var shard0Model = new TestAssemblyModel
        {
            FileName = @"C:\temp\shard0\Synthetic.RevitTests.2026.dll",
            Name = "Synthetic.RevitTests.2026",
            Success = true,
            Tests = new List<TestTypeModel>
            {
                new()
                {
                    Name = "SlowFixture",
                    FullName = "Suite.SlowFixture",
                    Success = true,
                    Tests = new List<TestModel>
                    {
                        new()
                        {
                            Name = "SlowTest",
                            FullName = "Suite.SlowFixture.SlowTest.SlowTest",
                            Success = true,
                            Time = 30_000.0,
                            Console = "slow stdout"
                        }
                    }
                }
            }
        };

        var shard1Model = new TestAssemblyModel
        {
            FileName = @"C:\temp\shard1\Synthetic.RevitTests.2026.dll",
            Name = "Synthetic.RevitTests.2026",
            Success = false,
            Tests = new List<TestTypeModel>
            {
                new()
                {
                    Name = "FastFixture",
                    FullName = "Suite.FastFixture",
                    Success = false,
                    Tests = new List<TestModel>
                    {
                        new()
                        {
                            Name = "PassFast",
                            FullName = "Suite.FastFixture.PassFast.PassFast",
                            Success = true,
                            Time = 250.0
                        },
                        new()
                        {
                            Name = "FailFast",
                            FullName = "Suite.FastFixture.FailFast.FailFast",
                            Success = false,
                            Time = 125.0,
                            Message = "Expected: 42\r\n   at Suite.FastFixture.FailFast() in Fast.cs:line 19"
                        },
                        new()
                        {
                            Name = "SkipFast",
                            FullName = "Suite.FastFixture.SkipFast.SkipFast",
                            Success = false,
                            Skipped = true,
                            Time = 0.0
                        }
                    }
                }
            }
        };

        TrxSummary summary = TrxMerger.WriteMergedTrx(
            trxPath,
            assemblyPath,
            new[]
            {
                new TrxShardResult(shard0Model, StartTime: t0, FinishTime: t0.AddSeconds(30)),
                new TrxShardResult(shard1Model, StartTime: t0.AddMilliseconds(50), FinishTime: t1)
            });

        Assert.That(summary.TotalTests, Is.EqualTo(4));
        Assert.That(summary.PassedTests, Is.EqualTo(2));
        Assert.That(summary.FailedTests, Is.EqualTo(1));
        Assert.That(summary.SkippedTests, Is.EqualTo(1));
        Assert.That(File.Exists(trxPath), Is.True);

        XDocument doc = XDocument.Load(trxPath);
        XNamespace ns = TrxWriter.TrxNamespace;

        XElement times = doc.Descendants(ns + "Times").Single();
        Assert.That(DateTimeOffset.Parse((string)times.Attribute("start")!), Is.EqualTo(t0));
        Assert.That(DateTimeOffset.Parse((string)times.Attribute("finish")!), Is.EqualTo(t1));

        XElement summaryEl = doc.Descendants(ns + "ResultSummary").Single();
        Assert.That((string?)summaryEl.Attribute("outcome"), Is.EqualTo("Failed"));

        XElement counters = doc.Descendants(ns + "Counters").Single();
        Assert.That((string?)counters.Attribute("total"), Is.EqualTo("4"));
        Assert.That((string?)counters.Attribute("executed"), Is.EqualTo("3"));
        Assert.That((string?)counters.Attribute("passed"), Is.EqualTo("2"));
        Assert.That((string?)counters.Attribute("failed"), Is.EqualTo("1"));
        Assert.That((string?)counters.Attribute("notExecuted"), Is.EqualTo("1"));
        Assert.That((string?)counters.Attribute("completed"), Is.EqualTo("3"));

        var results = doc.Descendants(ns + "UnitTestResult").ToList();
        Assert.That(results.Count, Is.EqualTo(4));

        XElement slowEl = results.Single(r => (string?)r.Attribute("testName") == "Suite.SlowFixture.SlowTest.SlowTest");
        Assert.That((string?)slowEl.Attribute("outcome"), Is.EqualTo("Passed"));
        Assert.That((string?)slowEl.Attribute("duration"), Is.EqualTo("00:00:30.0000000"));
        Assert.That((string?)slowEl.Descendants(ns + "StdOut").Single(), Is.EqualTo("slow stdout"));

        XElement failEl = results.Single(r => (string?)r.Attribute("testName") == "Suite.FastFixture.FailFast.FailFast");
        Assert.That((string?)failEl.Attribute("outcome"), Is.EqualTo("Failed"));
        Assert.That((string?)failEl.Attribute("duration"), Is.EqualTo("00:00:00.1250000"));
        Assert.That((string?)failEl.Descendants(ns + "Message").Single(), Is.EqualTo("Expected: 42"));
        Assert.That((string?)failEl.Descendants(ns + "StackTrace").Single(), Does.Contain("Suite.FastFixture.FailFast()"));

        var definitions = doc.Descendants(ns + "TestDefinitions").Descendants(ns + "UnitTest").ToList();
        Assert.That(definitions.Count, Is.EqualTo(4));
        Assert.That(definitions.All(d => (string?)d.Attribute("storage") == Path.GetFullPath(assemblyPath)), Is.True);
        Assert.That(doc.Descendants(ns + "TestEntries").Descendants(ns + "TestEntry").Count(), Is.EqualTo(4));
    }

    [Test]
    public async Task WorkerPoolSupervisor_EndToEndShardedExecution_MergesTrx_UpdatesCache_AndCleansStagingDirs()
    {
        const string runToken = "20261124_150000_9999_deadbeef";
        string buildParent = Path.Combine(_tempRoot, "Builds", runToken, "bin");
        string assemblyDir = Path.Combine(buildParent, $"Synthetic.RevitTests.2026_{runToken}");
        Directory.CreateDirectory(assemblyDir);

        string assemblyPath = TestAssemblyCompiler.CompileToFile(
            Path.Combine(assemblyDir, "Synthetic.RevitTests.2026.dll"),
            "Synthetic.RevitTests.2026",
            """
            using NUnit.Framework;

            namespace E2eSuite
            {
                [TestFixture]
                public class SlowOneMethodFixture
                {
                    [Test]
                    public void SlowOp() { }
                }

                [TestFixture]
                public class FastTwoMethodFixture
                {
                    [Test]
                    public void FastA() { }

                    [Test]
                    public void FastB() { }
                }

                [TestFixture]
                public class FailingFixture
                {
                    [Test]
                    public void FailingCheck() { }
                }
            }
            """);

        File.WriteAllText(Path.Combine(assemblyDir, "Synthetic.RevitTests.2026.deps.json"), "{}");

        var observedStageDirs = new ConcurrentBag<string>();
        var observedFiltersByWorker = new ConcurrentDictionary<string, string[]>();
        int runPass = 1;

        using var launcher = new StubWorkerProcessLauncher((server, launchReq) =>
        {
            server.InitialIsBusy = false;
            server.ExecutionHandler = (testReq, emitTest, _) =>
            {
                string dll = testReq.TestPathFile ?? string.Empty;
                Assert.That(File.Exists(dll), Is.True, $"Staged DLL must exist at '{dll}'.");
                string stageDir = Path.GetDirectoryName(dll)!;
                observedStageDirs.Add(stageDir);

                // Sibling files in assembly directory must also be staged.
                Assert.That(
                    File.Exists(Path.Combine(stageDir, "Synthetic.RevitTests.2026.deps.json")),
                    Is.True);

                string[] filters = testReq.TestFilters ?? Array.Empty<string>();
                observedFiltersByWorker[launchReq.WorkerId] = filters;

                var typeModels = new List<TestTypeModel>();
                bool overallSuccess = true;

                if (filters.Any(f => f.Contains("SlowOneMethodFixture", StringComparison.Ordinal)))
                {
                    var t = new TestModel
                    {
                        Name = "SlowOp",
                        FullName = "E2eSuite.SlowOneMethodFixture.SlowOp.SlowOp",
                        Success = true,
                        Time = Volatile.Read(ref runPass) == 1 ? 30_000.0 : 20_000.0
                    };
                    emitTest(t);
                    typeModels.Add(new TestTypeModel
                    {
                        Name = "SlowOneMethodFixture",
                        FullName = "E2eSuite.SlowOneMethodFixture",
                        Success = true,
                        Tests = new List<TestModel> { t }
                    });
                }

                if (filters.Any(f => f.Contains("FastTwoMethodFixture", StringComparison.Ordinal)))
                {
                    var ta = new TestModel
                    {
                        Name = "FastA",
                        FullName = "E2eSuite.FastTwoMethodFixture.FastA.FastA",
                        Success = true,
                        Time = 150.0
                    };
                    var tb = new TestModel
                    {
                        Name = "FastB",
                        FullName = "E2eSuite.FastTwoMethodFixture.FastB.FastB",
                        Success = true,
                        Time = 200.0
                    };
                    emitTest(ta);
                    emitTest(tb);
                    typeModels.Add(new TestTypeModel
                    {
                        Name = "FastTwoMethodFixture",
                        FullName = "E2eSuite.FastTwoMethodFixture",
                        Success = true,
                        Tests = new List<TestModel> { ta, tb }
                    });
                }

                if (filters.Any(f => f.Contains("FailingFixture", StringComparison.Ordinal)))
                {
                    bool failThisPass = Volatile.Read(ref runPass) == 1;
                    var tf = new TestModel
                    {
                        Name = "FailingCheck",
                        FullName = "E2eSuite.FailingFixture.FailingCheck.FailingCheck",
                        Success = !failThisPass,
                        Time = 100.0,
                        Message = failThisPass
                            ? "Boom assertion\r\n   at E2eSuite.FailingFixture.FailingCheck() in Failing.cs:line 12"
                            : string.Empty
                    };
                    emitTest(tf);
                    overallSuccess = !failThisPass;
                    typeModels.Add(new TestTypeModel
                    {
                        Name = "FailingFixture",
                        FullName = "E2eSuite.FailingFixture",
                        Success = !failThisPass,
                        Tests = new List<TestModel> { tf }
                    });
                }

                return Task.FromResult(new TestAssemblyModel
                {
                    FileName = dll,
                    Name = "Synthetic.RevitTests.2026",
                    Success = overallSuccess,
                    Tests = typeModels
                });
            };
        });

        string pipeName = $"Synthetic_ShardE2E_{Guid.NewGuid():N}";
        await using var supervisor = new WorkerPoolSupervisor(
            new SupervisorOptions
            {
                PipeName = pipeName,
                CurrentLatestVersion = 2026,
                IdleTimeout = TimeSpan.FromMinutes(25),
                TimingCacheDirectory = _timingDir
            },
            _discovery,
            launcher,
            new RevitPipeTestClientFactory(),
            bundleLeaseFactory: _ => new NoOpDisposable());

        await using var ipcServer = new SupervisorIpcServer(
            pipeName,
            (req, ct) => supervisor.HandleIpcRequestAsync(req, ct));
        supervisor.AttachIpcServer(ipcServer);
        supervisor.Start();
        ipcServer.Start();

        // Run 1: Unfiltered full-suite run discovers 3 fixtures and dispatches 3 concurrent shards.
        string run1Trx = Path.Combine(_tempRoot, "out", "run1_merged.trx");
        OneShotRunResult run1 = await supervisor.RunAsync(new OneShotRunOptions
        {
            RevitVersion = 2026,
            AssemblyPath = assemblyPath,
            TrxPath = run1Trx
        });

        Assert.That(run1.Success, Is.False);
        Assert.That(run1.ExitCode, Is.EqualTo(1));
        Assert.That(run1.ShardCount, Is.EqualTo(3));
        Assert.That(run1.WorkerIds.Count, Is.EqualTo(3));
        Assert.That(run1.WorkerIds.Distinct().Count(), Is.EqualTo(3));
        Assert.That(run1.TotalTests, Is.EqualTo(4));
        Assert.That(run1.PassedTests, Is.EqualTo(3));
        Assert.That(run1.FailedTests, Is.EqualTo(1));
        Assert.That(run1.SkippedTests, Is.EqualTo(0));
        Assert.That(
            run1.WorkerIds.All(id => launcher.Workers.Single(w => w.WorkerId == id).IsTerminated),
            Is.True,
            "All consumed shard workers must terminate immediately.");

        // Verify distinct shard staging dirs with <RunId> token preserved AND cleaned up after completion.
        string[] run1StageDirs = observedStageDirs.ToArray();
        Assert.That(run1StageDirs.Length, Is.EqualTo(3));
        Assert.That(run1StageDirs.Distinct(StringComparer.OrdinalIgnoreCase).Count(), Is.EqualTo(3));
        Assert.That(run1StageDirs.All(d => Path.GetFileName(d).Contains(runToken, StringComparison.Ordinal)), Is.True);
        Assert.That(Directory.GetDirectories(buildParent), Is.EqualTo(new[] { assemblyDir }), "All shard staging directories must be removed.");

        // Verify FastTwoMethodFixture stayed together on a single worker.
        string[] fastTwoWorkerFilters = observedFiltersByWorker.Values
            .Single(filters => filters.Any(f => f.Contains("FastTwoMethodFixture", StringComparison.Ordinal)));
        Assert.That(fastTwoWorkerFilters, Is.EquivalentTo(new[]
        {
            "E2eSuite.FastTwoMethodFixture.FastA.*",
            "E2eSuite.FastTwoMethodFixture.FastB.*"
        }));

        // Verify merged TRX contents & original assembly path.
        XDocument trxDoc1 = XDocument.Load(run1Trx);
        XNamespace ns = TrxWriter.TrxNamespace;
        XElement counters1 = trxDoc1.Descendants(ns + "Counters").Single();
        Assert.That((string?)counters1.Attribute("total"), Is.EqualTo("4"));
        Assert.That((string?)counters1.Attribute("passed"), Is.EqualTo("3"));
        Assert.That((string?)counters1.Attribute("failed"), Is.EqualTo("1"));

        XElement slowResult = trxDoc1.Descendants(ns + "UnitTestResult")
            .Single(e => (string?)e.Attribute("testName") == "E2eSuite.SlowOneMethodFixture.SlowOp.SlowOp");
        Assert.That((string?)slowResult.Attribute("duration"), Is.EqualTo("00:00:30.0000000"));

        XElement failResult = trxDoc1.Descendants(ns + "UnitTestResult")
            .Single(e => (string?)e.Attribute("testName") == "E2eSuite.FailingFixture.FailingCheck.FailingCheck");
        Assert.That((string?)failResult.Descendants(ns + "Message").Single(), Is.EqualTo("Boom assertion"));
        Assert.That((string?)failResult.Descendants(ns + "StackTrace").Single(), Does.Contain("FailingFixture.FailingCheck()"));
        Assert.That(
            trxDoc1.Descendants(ns + "UnitTest").All(u => (string?)u.Attribute("storage") == Path.GetFullPath(assemblyPath)),
            Is.True);

        // Verify timing cache persisted to disk.
        var cacheCheck = new FixtureTimingCache(_timingDir);
        IReadOnlyDictionary<string, double> savedAfterRun1 = cacheCheck.LoadFixtureDurations(2026);
        Assert.That(savedAfterRun1["E2eSuite.SlowOneMethodFixture"], Is.EqualTo(30_000.0));
        Assert.That(savedAfterRun1["E2eSuite.FastTwoMethodFixture"], Is.EqualTo(350.0));
        Assert.That(savedAfterRun1["E2eSuite.FailingFixture"], Is.EqualTo(100.0));

        // Run 2 via CLI --shards 2: reloads historical timings so SlowOneMethodFixture runs alone on Shard 0
        // while FastTwoMethodFixture + FailingFixture share Shard 1, and updates EMA on disk.
        Interlocked.Exchange(ref runPass, 2);
        observedStageDirs.Clear();
        observedFiltersByWorker.Clear();

        string run2Trx = Path.Combine(_tempRoot, "out", "run2_cli_shards2.trx");
        using var cliOut = new StringWriter();
        using var cliErr = new StringWriter();
        int cliExit = await Program.RunCliAsync(
            new[]
            {
                "run",
                "--version", "2026",
                "--assembly", assemblyPath,
                "--trx", run2Trx,
                "--shards", "2",
                "--pipe-name", pipeName,
                "--no-shim",
                "--json"
            },
            stdout: cliOut,
            stderr: cliErr);

        Assert.That(cliExit, Is.EqualTo(0), cliErr.ToString());
        OneShotRunResult? run2 = JsonConvert.DeserializeObject<OneShotRunResult>(cliOut.ToString().Trim());
        Assert.That(run2, Is.Not.Null);
        Assert.That(run2!.Success, Is.True);
        Assert.That(run2.ShardCount, Is.EqualTo(2));
        Assert.That(run2.WorkerIds.Count, Is.EqualTo(2));
        Assert.That(run2.TotalTests, Is.EqualTo(4));
        Assert.That(run2.PassedTests, Is.EqualTo(4));
        Assert.That(run2.FailedTests, Is.EqualTo(0));
        Assert.That(Directory.GetDirectories(buildParent), Is.EqualTo(new[] { assemblyDir }));

        // Verify duration-weighted LPT put SlowOneMethodFixture alone on one worker and FastTwo + Failing on the other.
        string[] run2SlowShard = observedFiltersByWorker[run2.WorkerIds[0]];
        string[] run2FastShard = observedFiltersByWorker[run2.WorkerIds[1]];
        Assert.That(run2SlowShard, Is.EqualTo(new[] { "E2eSuite.SlowOneMethodFixture.SlowOp.*" }));
        Assert.That(run2FastShard, Is.EquivalentTo(new[]
        {
            "E2eSuite.FastTwoMethodFixture.FastA.*",
            "E2eSuite.FastTwoMethodFixture.FastB.*",
            "E2eSuite.FailingFixture.FailingCheck.*"
        }));

        // Verify EMA update: 0.6 * 20,000 + 0.4 * 30,000 = 24,000.
        IReadOnlyDictionary<string, double> savedAfterRun2 = cacheCheck.LoadFixtureDurations(2026);
        Assert.That(savedAfterRun2["E2eSuite.SlowOneMethodFixture"], Is.EqualTo(24_000.0).Within(0.001));

        await supervisor.DrainAsync();
        Assert.That(launcher.Workers.All(w => w.IsTerminated), Is.True);
    }

    [Test]
    public async Task WorkerPoolSupervisor_MultiShardInfraFailure_CancelsSiblingTerminatesWorkersAndCleansStagingDirs()
    {
        string buildParent = Path.Combine(_tempRoot, "FailBuildParent");
        string assemblyDir = Path.Combine(buildParent, "Tests_Run_Fail");
        Directory.CreateDirectory(assemblyDir);

        string assemblyPath = TestAssemblyCompiler.CompileToFile(
            Path.Combine(assemblyDir, "Synthetic.RevitTests.2026.dll"),
            "Synthetic.RevitTests.2026",
            """
            using NUnit.Framework;

            namespace FailSuite
            {
                [TestFixture]
                public class FixA { [Test] public void A() { } }

                [TestFixture]
                public class FixB { [Test] public void B() { } }
            }
            """);

        int shardIndex = 0;
        using var launcher = new StubWorkerProcessLauncher((server, _) =>
        {
            int idx = Interlocked.Increment(ref shardIndex);
            if (idx == 1)
            {
                // Shard 0 stays hung during boot -> BootTimeout failure.
                server.InitialIsBusy = true;
            }
            else
            {
                server.InitialIsBusy = false;
                server.ExecutionHandler = async (req, emitTest, ct) =>
                {
                    await Task.Delay(TimeSpan.FromSeconds(10), ct).ConfigureAwait(false);
                    var t = new TestModel { Name = "B", FullName = "FailSuite.FixB.B.B", Success = true, Time = 1.0 };
                    emitTest(t);
                    return new TestAssemblyModel { FileName = req.TestPathFile ?? string.Empty, Name = "Asm", Success = true };
                };
            }
        });

        await using var supervisor = new WorkerPoolSupervisor(
            new SupervisorOptions
            {
                PipeName = $"Synthetic_ShardFail_{Guid.NewGuid():N}",
                CurrentLatestVersion = 2026,
                IdleTimeout = TimeSpan.FromMinutes(25),
                TimingCacheDirectory = _timingDir
            },
            _discovery,
            launcher,
            new RevitPipeTestClientFactory(),
            bundleLeaseFactory: _ => new NoOpDisposable());

        OneShotRunResult result = await supervisor.RunAsync(new OneShotRunOptions
        {
            RevitVersion = 2026,
            AssemblyPath = assemblyPath,
            TrxPath = Path.Combine(_tempRoot, "out", "fail_infra.trx"),
            BootTimeout = TimeSpan.FromMilliseconds(150),
            ExecutionTimeout = TimeSpan.FromSeconds(10)
        });

        Assert.That(result.Success, Is.False);
        Assert.That(result.ExitCode, Is.EqualTo(2));
        Assert.That(result.ShardCount, Is.EqualTo(2));
        Assert.That(result.Error, Does.Contain("IsBusy == false"));
        Assert.That(supervisor.GetStatus().Active, Is.EqualTo(0));
        Assert.That(
            result.WorkerIds.All(id => launcher.Workers.Single(w => w.WorkerId == id).IsTerminated),
            Is.True);
        Assert.That(Directory.GetDirectories(buildParent), Is.EqualTo(new[] { assemblyDir }));

        await supervisor.DrainAsync();
        Assert.That(launcher.Workers.All(w => w.IsTerminated), Is.True);
    }

    private sealed class NoOpDisposable : IDisposable
    {
        public void Dispose()
        {
        }
    }
}
