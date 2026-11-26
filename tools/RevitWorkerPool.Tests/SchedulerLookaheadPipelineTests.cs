using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using NUnit.Framework;
using RevitWorkerPool.Discovery;
using RevitWorkerPool.Execution;
using RevitWorkerPool.Ipc;
using RevitWorkerPool.Launching;
using RevitWorkerPool.Pipe;
using RevitWorkerPool.Supervisor;
using ricaun.NUnit.Models;

namespace RevitWorkerPool.Tests;

[TestFixture]
public class SchedulerLookaheadPipelineTests
{
    private const ulong TwelveGiB = 12UL * 1024 * 1024 * 1024;

    private static readonly byte[] SixFixtureDllBytes = TestAssemblyCompiler.CompileToBytes(
        "Synthetic.RevitTests.MultiShard",
        """
        using NUnit.Framework;

        namespace MultiSuite
        {
            [TestFixture]
            [Category("RevitSuite")]
            public class Fix1 { [Test] public void T1() { } }

            [TestFixture]
            [Category("RevitSuite")]
            public class Fix2 { [Test] public void T2() { } }

            [TestFixture]
            [Category("RevitSuite")]
            public class Fix3 { [Test] public void T3() { } }

            [TestFixture]
            [Category("RevitSuite")]
            public class Fix4 { [Test] public void T4() { } }

            [TestFixture]
            [Category("RevitSuite")]
            public class Fix5 { [Test] public void T5() { } }

            [TestFixture]
            [Category("RevitSuite")]
            public class Fix6 { [Test] public void T6() { } }
        }
        """);

    private static readonly byte[] SingleFixtureDllBytes = TestAssemblyCompiler.CompileToBytes(
        "Synthetic.RevitTests.QuickSingle",
        """
        using NUnit.Framework;

        namespace QuickSuite
        {
            [TestFixture]
            [Category("RevitSmoke")]
            public class QuickFix
            {
                [Test]
                public void QuickTest() { }
            }
        }
        """);

    private string _tempRoot = string.Empty;
    private string _multiShardDll = string.Empty;
    private string _singleFixtureDll = string.Empty;
    private RevitInstallationDiscovery _discovery = null!;

    [SetUp]
    public void SetUp()
    {
        _tempRoot = Path.Combine(
            Path.GetTempPath(),
            "RevitWorkerPool_SchedulerTests",
            Guid.NewGuid().ToString("N"));

        foreach (int year in new[] { 2024, 2025, 2026 })
        {
            string revitDir = Path.Combine(_tempRoot, "Autodesk", $"Revit {year}");
            Directory.CreateDirectory(revitDir);
            File.WriteAllText(Path.Combine(revitDir, "Revit.exe"), string.Empty);
        }

        string multiDir = Path.Combine(_tempRoot, "multi");
        string singleDir = Path.Combine(_tempRoot, "single");
        Directory.CreateDirectory(multiDir);
        Directory.CreateDirectory(singleDir);

        _multiShardDll = Path.Combine(multiDir, "Synthetic.RevitTests.MultiShard.dll");
        _singleFixtureDll = Path.Combine(singleDir, "Synthetic.RevitTests.QuickSingle.dll");
        File.WriteAllBytes(_multiShardDll, SixFixtureDllBytes);
        File.WriteAllBytes(_singleFixtureDll, SingleFixtureDllBytes);

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
    public void FairRoundRobinScheduler_RotatesAcrossActiveRuns_AndTracksFastPathDispatches()
    {
        var scheduler = new FairRoundRobinScheduler<string>();
        Guid runA = Guid.NewGuid();
        Guid runB = Guid.NewGuid();
        Guid runC = Guid.NewGuid();

        scheduler.RegisterRun(runA);
        scheduler.RecordDispatch(runA);
        scheduler.RecordDispatch(runA);
        scheduler.Enqueue(runA, 2026, "A3");
        scheduler.Enqueue(runA, 2026, "A4");

        scheduler.RegisterRun(runB);
        scheduler.Enqueue(runB, 2026, "B1");
        scheduler.Enqueue(runB, 2026, "B2");

        scheduler.RegisterRun(runC);
        scheduler.Enqueue(runC, 2024, "C1");

        Assert.That(scheduler.PendingCount, Is.EqualTo(5));
        Assert.That(scheduler.ActiveRunCount, Is.EqualTo(3));

        // Next boot candidate across all versions must be unserved RunB ("B1"), ahead of already-served RunA.
        Assert.That(scheduler.TryPeekNextBootCandidate(out int nextVersion, out string? nextItem), Is.True);
        Assert.That(nextVersion, Is.EqualTo(2026));
        Assert.That(nextItem, Is.EqualTo("B1"));

        // Dequeuing 2026 rotates RunB -> RunA -> RunB without losing RunC's turn for 2024.
        Assert.That(scheduler.TryDequeueForVersion(2026, out Guid dRun1, out string? d1), Is.True);
        Assert.That((dRun1, d1), Is.EqualTo((runB, "B1")));

        Assert.That(scheduler.TryDequeueForVersion(2026, out Guid dRun2, out string? d2), Is.True);
        Assert.That((dRun2, d2), Is.EqualTo((runA, "A3")));

        Assert.That(scheduler.TryDequeueForVersion(2024, out Guid dRun3, out string? d3), Is.True);
        Assert.That((dRun3, d3), Is.EqualTo((runC, "C1")));

        Assert.That(scheduler.TryDequeueForVersion(2026, out Guid dRun4, out string? d4), Is.True);
        Assert.That((dRun4, d4), Is.EqualTo((runB, "B2")));

        Assert.That(scheduler.TryDequeueForVersion(2026, out Guid dRun5, out string? d5), Is.True);
        Assert.That((dRun5, d5), Is.EqualTo((runA, "A4")));

        scheduler.UnregisterRun(runA);
        scheduler.UnregisterRun(runB);
        scheduler.UnregisterRun(runC);
        Assert.That(scheduler.PendingCount, Is.EqualTo(0));
        Assert.That(scheduler.ActiveRunCount, Is.EqualTo(0));
    }

    [Test]
    public async Task FairRoundRobinDispatch_QuickRunBPreemptsMultiShardBacklogA_WhileSoloRunBurstsAcrossFourSlots()
    {
        // Part 1: Solo 4-shard run saturates all 4 active slots concurrently.
        int soloCurrentActive = 0;
        int soloPeakActive = 0;
        var soloFourActive = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var soloRelease = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        using (var soloLauncher = new StubWorkerProcessLauncher((server, _) =>
        {
            server.InitialIsBusy = false;
            server.ExecutionHandler = async (req, emitTest, ct) =>
            {
                int now = Interlocked.Increment(ref soloCurrentActive);
                UpdateMax(ref soloPeakActive, now);
                if (now == 4)
                {
                    soloFourActive.TrySetResult(true);
                }

                await soloRelease.Task.WaitAsync(ct).ConfigureAwait(false);
                Interlocked.Decrement(ref soloCurrentActive);
                return BuildSuccessResult(req, emitTest);
            };
        }, usesCleanShim: false))
        await using (var soloSupervisor = CreateSupervisor(soloLauncher, new StubMemoryProbe(0)))
        {
            Task<OneShotRunResult> soloRun = soloSupervisor.RunAsync(new OneShotRunOptions
            {
                RevitVersion = 2026,
                AssemblyPath = _multiShardDll,
                TrxPath = Path.Combine(_tempRoot, "out", "solo4.trx"),
                Shards = 4
            });

            await soloFourActive.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.That(Volatile.Read(ref soloPeakActive), Is.EqualTo(4));
            Assert.That(soloSupervisor.GetStatus().Active, Is.EqualTo(4));

            soloRelease.TrySetResult(true);
            OneShotRunResult soloResult = await soloRun.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.That(soloResult.Success, Is.True, soloResult.Error);
            Assert.That(soloResult.ShardCount, Is.EqualTo(4));
        }

        // Part 2: Background work occupies 2 slots so Agent A's 4-shard run fills remaining 2 active slots
        // and queues 2 trailing shards under one RunId; Agent B then arrives with a 1-fixture run
        // and must receive the very next freed slot ahead of Agent A's remaining backlog.
        var dispatchOrder = new ConcurrentQueue<string>();
        var slot1Barrier = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var slot2Barrier = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var agentAFirstBarrier = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var allRelease = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        int bgActive = 0;
        var bgTwoActive = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var agentATwoActive = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        int agentAActiveCount = 0;
        var agentBStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        using var launcher = new StubWorkerProcessLauncher((server, _) =>
        {
            server.InitialIsBusy = false;
            server.ExecutionHandler = async (req, emitTest, ct) =>
            {
                string dllName = Path.GetFileName(req.TestPathFile ?? string.Empty);
                string tag;
                if (dllName.Contains("QuickSingle", StringComparison.Ordinal))
                {
                    if ((req.TestFilters?.Any(f => f.Contains("QuickTest", StringComparison.Ordinal)) ?? false) &&
                        req.TestPathFile!.Contains("AgentB", StringComparison.OrdinalIgnoreCase))
                    {
                        tag = "AgentB";
                        dispatchOrder.Enqueue(tag);
                        agentBStarted.TrySetResult(true);
                    }
                    else
                    {
                        int bgIdx = Interlocked.Increment(ref bgActive);
                        tag = $"Bg{bgIdx}";
                        dispatchOrder.Enqueue(tag);
                        if (bgIdx == 2)
                        {
                            bgTwoActive.TrySetResult(true);
                        }

                        await (bgIdx == 1 ? slot1Barrier.Task : slot2Barrier.Task).WaitAsync(ct).ConfigureAwait(false);
                        return BuildSuccessResult(req, emitTest);
                    }
                }
                else
                {
                    int aIdx = Interlocked.Increment(ref agentAActiveCount);
                    tag = $"AgentA_{aIdx}";
                    dispatchOrder.Enqueue(tag);
                    if (aIdx == 2)
                    {
                        agentATwoActive.TrySetResult(true);
                    }

                    if (aIdx == 1)
                    {
                        await agentAFirstBarrier.Task.WaitAsync(ct).ConfigureAwait(false);
                        return BuildSuccessResult(req, emitTest);
                    }
                }

                await allRelease.Task.WaitAsync(ct).ConfigureAwait(false);
                return BuildSuccessResult(req, emitTest);
            };
        }, usesCleanShim: false);

        string bg1Dll = CopyAssemblyToSubdir(_singleFixtureDll, "Bg1");
        string bg2Dll = CopyAssemblyToSubdir(_singleFixtureDll, "Bg2");
        string agentBDll = CopyAssemblyToSubdir(_singleFixtureDll, "AgentB");

        await using var supervisor = CreateSupervisor(launcher, new StubMemoryProbe(0));

        // Occupy 2 of 4 active slots first so Agent A's 4-shard run fills remaining 2 active slots and queues 2 shards.
        Task<OneShotRunResult> bg1 = supervisor.RunAsync(new OneShotRunOptions
        {
            RevitVersion = 2026,
            AssemblyPath = bg1Dll,
            TrxPath = Path.Combine(_tempRoot, "out", "bg1.trx"),
            Shards = 1
        });
        Task<OneShotRunResult> bg2 = supervisor.RunAsync(new OneShotRunOptions
        {
            RevitVersion = 2026,
            AssemblyPath = bg2Dll,
            TrxPath = Path.Combine(_tempRoot, "out", "bg2.trx"),
            Shards = 1
        });
        await bgTwoActive.Task.WaitAsync(TimeSpan.FromSeconds(2));

        // Agent A submits a 4-shard run (2 active, 2 queued under same RunId).
        Task<OneShotRunResult> agentARun = supervisor.RunAsync(new OneShotRunOptions
        {
            RevitVersion = 2026,
            AssemblyPath = _multiShardDll,
            TrxPath = Path.Combine(_tempRoot, "out", "agentA.trx"),
            Shards = 4
        });
        await agentATwoActive.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await WaitForConditionAsync(() => supervisor.GetStatus() is { Active: 4, QueuedRuns: 2 });

        // Agent B arrives with a quick 1-fixture run while Agent A still has 2 shards queued.
        Task<OneShotRunResult> agentBRun = supervisor.RunAsync(new OneShotRunOptions
        {
            RevitVersion = 2026,
            AssemblyPath = agentBDll,
            TrxPath = Path.Combine(_tempRoot, "out", "agentB.trx"),
            Shards = 1
        });
        await WaitForConditionAsync(() => supervisor.GetStatus().QueuedRuns == 3);

        // Free 1 slot: Agent B MUST receive the very next worker slot ahead of Agent A's remaining 2 queued shards!
        slot1Barrier.TrySetResult(true);
        await agentBStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        // Release remaining barriers and confirm both Agent B and Agent A complete cleanly.
        slot2Barrier.TrySetResult(true);
        agentAFirstBarrier.TrySetResult(true);
        allRelease.TrySetResult(true);

        OneShotRunResult[] results = await Task.WhenAll(bg1, bg2, agentARun, agentBRun).WaitAsync(TimeSpan.FromSeconds(2));
        Assert.That(results.All(r => r.Success), Is.True);

        string[] order = dispatchOrder.ToArray();
        int idxB = Array.IndexOf(order, "AgentB");
        int idxA3 = Array.IndexOf(order, "AgentA_3");
        int idxA4 = Array.IndexOf(order, "AgentA_4");
        Assert.That(idxB, Is.GreaterThanOrEqualTo(0));
        Assert.That(idxA3, Is.GreaterThan(idxB), $"AgentB must dispatch ahead of AgentA's remaining backlog. Order: {string.Join(", ", order)}");
        Assert.That(idxA4, Is.GreaterThan(idxB));
    }

    [Test]
    public async Task Lookahead_BoundaryGuards_NeverSpawnsWhenQueueEmpty_NoShim_OrLowMemory()
    {
        // Guard 1: Queue empty (4 active, 0 queued, shim=true, RAM=16 GiB) -> 0 lookahead spawns.
        await AssertNoLookaheadAsync(
            concurrentRuns: 4,
            usesCleanShim: true,
            availableRamBytes: 16UL * 1024 * 1024 * 1024,
            expectedQueued: 0);

        // Guard 2: --no-shim (UsesCleanShim == false) -> 0 lookahead spawns even with queued work & 16 GiB RAM.
        await AssertNoLookaheadAsync(
            concurrentRuns: 6,
            usesCleanShim: false,
            availableRamBytes: 16UL * 1024 * 1024 * 1024,
            expectedQueued: 2);

        // Guard 3: RAM < 12 GiB -> 0 lookahead spawns even with queued work & UsesCleanShim == true.
        await AssertNoLookaheadAsync(
            concurrentRuns: 6,
            usesCleanShim: true,
            availableRamBytes: TwelveGiB - 1,
            expectedQueued: 2);
    }

    [Test]
    public async Task Lookahead_AllFourGuardsHold_CapsTotalResidentAtFive_ActiveAtFour_AndConsumesLookaheadSeamlessly()
    {
        int activeNow = 0;
        int peakActive = 0;
        int peakResident = 0;
        var fourActiveReached = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstWorkerRelease = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var allRelease = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var observedWorkerIds = new ConcurrentQueue<string>();
        string? lookaheadWorkerId = null;
        int launchedCount = 0;

        StubWorkerProcessLauncher? launcher = null;
        launcher = new StubWorkerProcessLauncher((server, req) =>
        {
            int idx = Interlocked.Increment(ref launchedCount);
            if (idx == 5)
            {
                Volatile.Write(ref lookaheadWorkerId, req.WorkerId);
                // Lookahead worker boots slightly asynchronously so we observe Booting -> ReadyStandby
                // while Active stays strictly capped at 4.
                server.InitialIsBusy = true;
                server.AutoReadyDelay = TimeSpan.FromMilliseconds(15);
            }
            else
            {
                server.InitialIsBusy = false;
            }

            server.ExecutionHandler = async (testReq, emitTest, ct) =>
            {
                observedWorkerIds.Enqueue(req.WorkerId);
                int currentActive = Interlocked.Increment(ref activeNow);
                UpdateMax(ref peakActive, currentActive);
                UpdateMax(ref peakResident, launcher!.Workers.Count(w => !w.IsTerminated));

                if (currentActive == 4)
                {
                    fourActiveReached.TrySetResult(true);
                }

                if (idx == 1)
                {
                    await firstWorkerRelease.Task.WaitAsync(ct).ConfigureAwait(false);
                }
                else
                {
                    await allRelease.Task.WaitAsync(ct).ConfigureAwait(false);
                }

                Interlocked.Decrement(ref activeNow);
                return BuildSuccessResult(testReq, emitTest);
            };
        }, usesCleanShim: true);

        using (launcher)
        await using (var supervisor = CreateSupervisor(launcher, new StubMemoryProbe(TwelveGiB), maxActiveWorkers: 4))
        {
            Task<OneShotRunResult>[] runs = Enumerable.Range(1, 6)
                .Select(i => supervisor.RunAsync(new OneShotRunOptions
                {
                    RevitVersion = 2026,
                    AssemblyPath = _singleFixtureDll,
                    TrxPath = Path.Combine(_tempRoot, "out", $"la_{i}.trx"),
                    Shards = 1
                }))
                .ToArray();

            await fourActiveReached.Task.WaitAsync(TimeSpan.FromSeconds(2));

            // Wait until the +1 lookahead standby worker finishes booting to ReadyStandby while 4 active remain busy.
            await WaitForConditionAsync(() =>
            {
                PoolStatusSnapshot s = supervisor.GetStatus();
                UpdateMax(ref peakResident, s.TotalResident);
                return s.Active == 4 && s.ReadyStandby == 1 && s.Booting == 0 && s.TotalResident == 5 && s.QueuedRuns == 2;
            });

            PoolStatusSnapshot preReleaseStatus = supervisor.GetStatus();
            Assert.That(preReleaseStatus.Active, Is.EqualTo(4));
            Assert.That(preReleaseStatus.ActiveCap, Is.EqualTo(4));
            Assert.That(preReleaseStatus.ReadyStandby, Is.EqualTo(1));
            Assert.That(preReleaseStatus.TotalResident, Is.EqualTo(5));
            Assert.That(launcher.Workers.Count(w => !w.IsTerminated), Is.EqualTo(5));

            string preBootedLookaheadId = Volatile.Read(ref lookaheadWorkerId)!;
            Assert.That(preBootedLookaheadId, Is.Not.Null.And.Not.Empty);

            // Releasing 1 active worker must immediately promote the pre-booted lookahead worker to Active (no cold gap).
            firstWorkerRelease.TrySetResult(true);
            await WaitForConditionAsync(() => observedWorkerIds.Contains(preBootedLookaheadId));

            allRelease.TrySetResult(true);
            OneShotRunResult[] results = await Task.WhenAll(runs).WaitAsync(TimeSpan.FromSeconds(2));

            Assert.That(results.All(r => r.Success), Is.True);
            Assert.That(Volatile.Read(ref peakActive), Is.LessThanOrEqualTo(4));
            Assert.That(Volatile.Read(ref peakResident), Is.LessThanOrEqualTo(5));
            Assert.That(new Win32MemoryProbe().GetAvailablePhysicalBytes(), Is.GreaterThan(0UL));
        }
    }

    [Test]
    public async Task Lookahead_Drain_CancelsInFlightLookaheadBootCleanly_AndLeavesZeroWorkers()
    {
        var fourActiveReached = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var fifthBootStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        int launchedCount = 0;

        using var launcher = new StubWorkerProcessLauncher((server, _) =>
        {
            int idx = Interlocked.Increment(ref launchedCount);
            if (idx <= 4)
            {
                server.InitialIsBusy = false;
                server.ExecutionHandler = async (req, emitTest, ct) =>
                {
                    if (Volatile.Read(ref launchedCount) >= 4)
                    {
                        fourActiveReached.TrySetResult(true);
                    }

                    await Task.Delay(TimeSpan.FromSeconds(10), ct).ConfigureAwait(false);
                    return BuildSuccessResult(req, emitTest);
                };
            }
            else
            {
                // Keep the +1 lookahead worker permanently booting (IsBusy = true) until drain cancels it.
                server.InitialIsBusy = true;
                fifthBootStarted.TrySetResult(true);
            }
        }, usesCleanShim: true);

        await using var supervisor = CreateSupervisor(
            launcher,
            new StubMemoryProbe(16UL * 1024 * 1024 * 1024),
            drainGraceTimeout: TimeSpan.FromMilliseconds(50),
            maxActiveWorkers: 4);

        Task<OneShotRunResult>[] runs = Enumerable.Range(1, 5)
            .Select(i => supervisor.RunAsync(new OneShotRunOptions
            {
                RevitVersion = 2026,
                AssemblyPath = _singleFixtureDll,
                TrxPath = Path.Combine(_tempRoot, "out", $"drain_la_{i}.trx"),
                Shards = 1
            }))
            .ToArray();

        await fourActiveReached.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await fifthBootStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await WaitForConditionAsync(() =>
        {
            PoolStatusSnapshot s = supervisor.GetStatus();
            return s.Active == 4 && s.Booting == 1 && s.TotalResident == 5;
        });

        DrainResult drain = await supervisor.DrainAsync(DrainMode.Force).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.That(drain.Success, Is.True);
        Assert.That(drain.Status!.TotalResident, Is.EqualTo(0));
        Assert.That(drain.Status.Booting, Is.EqualTo(0));
        Assert.That(drain.Status.ReadyStandby, Is.EqualTo(0));
        Assert.That(drain.Status.Active, Is.EqualTo(0));
        Assert.That(launcher.Workers.Count, Is.EqualTo(5));
        Assert.That(launcher.Workers.All(w => w.IsTerminated), Is.True);

        OneShotRunResult[] runResults = await Task.WhenAll(runs).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(runResults.All(r => !r.Success && r.ExitCode == 2), Is.True);
    }

    [Test]
    public async Task CrossVersionPipelining_MultiShardRunBootsZeroNextVersionUntilFinalShardInsideCapFour_AndSingleShardPrewarmsOne()
    {
        // Part 1: Multi-shard 2024 run (with 1 bg slot busy -> 3 active 2024 + 1 bg = 4 slots, 1 shard queued)
        // boots 0 Revit 2025 workers during earlier shards while 4 slots are occupied,
        // enters final shard (still 4 slots occupied -> 0 Revit 2025 workers yet),
        // then boots at most 1 Revit 2025 worker strictly inside cap 4 as soon as a cap-4 slot opens!
        var bgHold = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var shard1Hold = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var shard2Hold = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var remainingHold = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        int revit2024Execs = 0;
        var fourOccupied = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var fourth2024Entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        int bgActive = 0;
        var bgEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        using var launcher = new StubWorkerProcessLauncher((server, launchReq) =>
        {
            server.InitialIsBusy = false;
            server.ExecutionHandler = async (req, emitTest, ct) =>
            {
                if (launchReq.RevitVersion == 2026)
                {
                    if (Interlocked.Increment(ref bgActive) == 1)
                    {
                        bgEntered.TrySetResult(true);
                    }

                    await bgHold.Task.WaitAsync(ct).ConfigureAwait(false);
                    return BuildSuccessResult(req, emitTest);
                }

                if (launchReq.RevitVersion == 2024)
                {
                    int idx = Interlocked.Increment(ref revit2024Execs);
                    if (idx == 3)
                    {
                        fourOccupied.TrySetResult(true);
                    }
                    else if (idx == 4)
                    {
                        fourth2024Entered.TrySetResult(true);
                    }

                    Task waitTask = idx switch
                    {
                        1 => shard1Hold.Task,
                        2 => shard2Hold.Task,
                        _ => remainingHold.Task
                    };
                    await waitTask.WaitAsync(ct).ConfigureAwait(false);
                    return BuildSuccessResult(req, emitTest);
                }

                return BuildSuccessResult(req, emitTest);
            };
        }, usesCleanShim: false);

        await using var supervisor = CreateSupervisor(launcher, new StubMemoryProbe(0));

        // Occupy 1 slot on 2026 (sets lazy latest target=1 on 2026, which will sit behind pipeline prewarm inside cap 4).
        Task<OneShotRunResult> bg2026 = supervisor.RunAsync(new OneShotRunOptions
        {
            RevitVersion = 2026,
            AssemblyPath = _singleFixtureDll,
            TrxPath = Path.Combine(_tempRoot, "out", "pipe_bg2026.trx"),
            Shards = 1
        });
        await bgEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));

        // Launch 4-shard 2024 run with NextVersion = 2025 while 1 slot is occupied by 2026:
        // first 3 shards fill cap 4, 4th shard stays queued (not yet final shard).
        Task<OneShotRunResult> run2024 = supervisor.RunAsync(new OneShotRunOptions
        {
            RevitVersion = 2024,
            NextVersion = 2025,
            AssemblyPath = _multiShardDll,
            TrxPath = Path.Combine(_tempRoot, "out", "pipe_2024.trx"),
            Shards = 4
        });

        await fourOccupied.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await WaitForConditionAsync(() => supervisor.GetStatus() is { Active: 4, QueuedRuns: 1 });
        Assert.That(launcher.LaunchedRequests.Count(r => r.RevitVersion == 2025), Is.EqualTo(0),
            "Must boot 0 NextVersion workers during earlier shards when cap 4 is occupied.");

        // Release 2026 bg slot -> 4th (FINAL) shard of 2024 dispatches into cap 4 (still 4 active).
        bgHold.TrySetResult(true);
        Assert.That((await bg2026).Success, Is.True);
        await fourth2024Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await WaitForConditionAsync(() => supervisor.GetStatus() is { Active: 4, QueuedRuns: 0 });
        Assert.That(launcher.LaunchedRequests.Count(r => r.RevitVersion == 2025), Is.EqualTo(0),
            "Pipeline pre-warm must stay strictly inside cap 4 when all 4 slots are active.");

        // Release shard 1 of 2024 while shards 2, 3, 4 are still executing -> slot opens inside cap 4 -> boots 1 Revit 2025 worker!
        shard1Hold.TrySetResult(true);
        await WaitForConditionAsync(() =>
        {
            PoolStatusSnapshot s = supervisor.GetStatus();
            return s.Versions.TryGetValue(2025, out VersionWorkerStatus? v2025) &&
                   v2025.ReadyStandby == 1 &&
                   s.TotalResident <= 4;
        });

        Assert.That(launcher.LaunchedRequests.Count(r => r.RevitVersion == 2025), Is.EqualTo(1));
        string prewarmed2025WorkerId = launcher.Workers.Single(w => !w.IsTerminated && launcher.LaunchedRequests.Any(r => r.WorkerId == w.WorkerId && r.RevitVersion == 2025)).WorkerId;

        // Release remaining 2024 shards -> 2024 completes, leaving exactly the 1 pre-warmed 2025 standby (StandbyTarget = 0).
        shard2Hold.TrySetResult(true);
        remainingHold.TrySetResult(true);
        OneShotRunResult res2024 = await run2024.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.That(res2024.Success, Is.True, res2024.Error);

        PoolStatusSnapshot after2024 = supervisor.GetStatus();
        Assert.That(after2024.Versions[2025].ReadyStandby, Is.EqualTo(1));
        Assert.That(after2024.Versions[2025].StandbyTarget, Is.EqualTo(0));
        Assert.That(after2024.Versions.ContainsKey(2024), Is.False);

        // Next 2025 run consumes the pre-warmed worker immediately and does NOT replenish persistent 2025 standbys.
        OneShotRunResult res2025 = await supervisor.RunAsync(new OneShotRunOptions
        {
            RevitVersion = 2025,
            AssemblyPath = _singleFixtureDll,
            TrxPath = Path.Combine(_tempRoot, "out", "pipe_2025.trx"),
            Shards = 1
        }).WaitAsync(TimeSpan.FromSeconds(2));

        Assert.That(res2025.Success, Is.True);
        Assert.That(res2025.WorkerIds.Single(), Is.EqualTo(prewarmed2025WorkerId));
        await WaitForConditionAsync(() =>
        {
            PoolStatusSnapshot s = supervisor.GetStatus();
            return s.Active == 0 && !s.Versions.ContainsKey(2025);
        });

        // Part 2: Single-shard CLI run with --next-version 2025 when slots < 4 pre-warms 1 Revit 2025 worker inside cap 4.
        string pipeName = $"Synthetic_PipeCli_{Guid.NewGuid():N}";
        await using var cliSup = CreateSupervisor(launcher, new StubMemoryProbe(0), pipeName: pipeName);
        await using var ipcServer = new SupervisorIpcServer(pipeName, (r, ct) => cliSup.HandleIpcRequestAsync(r, ct));
        cliSup.AttachIpcServer(ipcServer);
        cliSup.Start();
        ipcServer.Start();

        int before2025Count = launcher.LaunchedRequests.Count(r => r.RevitVersion == 2025);
        using var cliOut = new StringWriter();
        using var cliErr = new StringWriter();
        int exitCode = await Program.RunCliAsync(
            new[]
            {
                "run",
                "--version", "2024",
                "--next-version", "2025",
                "--assembly", _singleFixtureDll,
                "--trx", Path.Combine(_tempRoot, "out", "cli_pipe_2024.trx"),
                "--pipe-name", pipeName,
                "--no-shim",
                "--json"
            },
            stdout: cliOut,
            stderr: cliErr).WaitAsync(TimeSpan.FromSeconds(2));

        Assert.That(exitCode, Is.EqualTo(0), cliErr.ToString());
        OneShotRunResult? cliResult = JsonConvert.DeserializeObject<OneShotRunResult>(cliOut.ToString().Trim());
        Assert.That(cliResult, Is.Not.Null);
        Assert.That(cliResult!.Success, Is.True);

        await WaitForConditionAsync(() =>
        {
            PoolStatusSnapshot s = cliSup.GetStatus();
            return s.Versions.TryGetValue(2025, out VersionWorkerStatus? v) && v.ReadyStandby == 1 && s.TotalResident <= 4;
        });
        Assert.That(launcher.LaunchedRequests.Count(r => r.RevitVersion == 2025) - before2025Count, Is.EqualTo(1));

        // Same-version or uninstalled NextVersion (2099) must not pre-warm anything extra.
        OneShotRunResult ignoredNext = await cliSup.RunAsync(new OneShotRunOptions
        {
            RevitVersion = 2024,
            NextVersion = 2099,
            AssemblyPath = _singleFixtureDll,
            TrxPath = Path.Combine(_tempRoot, "out", "pipe_ignored.trx"),
            Shards = 1
        }).WaitAsync(TimeSpan.FromSeconds(2));
        Assert.That(ignoredNext.Success, Is.True);
        Assert.That(cliSup.GetStatus().Versions.ContainsKey(2099), Is.False);
    }

    [Test]
    public async Task CrossVersionPipelining_DeferredPrewarmIsPrunedOnceTargetVersionRunsAndFinishes()
    {
        var bgRelease = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var shard1Release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var shardRestRelease = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var three2024Active = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var bgEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var revit2025Done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        int count2024 = 0;

        using var launcher = new StubWorkerProcessLauncher((server, launchReq) =>
        {
            server.InitialIsBusy = false;
            server.ExecutionHandler = async (req, emitTest, ct) =>
            {
                if (launchReq.RevitVersion == 2026)
                {
                    bgEntered.TrySetResult(true);
                    await bgRelease.Task.WaitAsync(ct).ConfigureAwait(false);
                    return BuildSuccessResult(req, emitTest);
                }

                if (launchReq.RevitVersion == 2024)
                {
                    int idx = Interlocked.Increment(ref count2024);
                    if (idx == 3)
                    {
                        three2024Active.TrySetResult(true);
                    }

                    await (idx == 1 ? shard1Release.Task : shardRestRelease.Task).WaitAsync(ct).ConfigureAwait(false);
                    return BuildSuccessResult(req, emitTest);
                }

                if (launchReq.RevitVersion == 2025)
                {
                    revit2025Done.TrySetResult(true);
                    return BuildSuccessResult(req, emitTest);
                }

                return BuildSuccessResult(req, emitTest);
            };
        }, usesCleanShim: false);

        await using var supervisor = CreateSupervisor(launcher, new StubMemoryProbe(0));

        // 1. Occupy 1 cap slot on 2026 so 4-shard 2024 run fills slots 2..4 and queues its 4th (final) shard.
        Task<OneShotRunResult> bg2026 = supervisor.RunAsync(new OneShotRunOptions
        {
            RevitVersion = 2026,
            AssemblyPath = _singleFixtureDll,
            TrxPath = Path.Combine(_tempRoot, "out", "stale_bg2026.trx"),
            Shards = 1
        });
        await bgEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Task<OneShotRunResult> run2024 = supervisor.RunAsync(new OneShotRunOptions
        {
            RevitVersion = 2024,
            NextVersion = 2025,
            AssemblyPath = _multiShardDll,
            TrxPath = Path.Combine(_tempRoot, "out", "stale_2024.trx"),
            Shards = 4
        });
        await three2024Active.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await WaitForConditionAsync(() => supervisor.GetStatus() is { Active: 4, QueuedRuns: 1 });

        // 2. Submit Revit 2025 run while 2024's final shard is still queued (so NextVersion=2025 prewarm has not booted yet).
        Task<OneShotRunResult> run2025 = supervisor.RunAsync(new OneShotRunOptions
        {
            RevitVersion = 2025,
            AssemblyPath = _singleFixtureDll,
            TrxPath = Path.Combine(_tempRoot, "out", "stale_2025.trx"),
            Shards = 1
        });
        await WaitForConditionAsync(() => supervisor.GetStatus().QueuedRuns == 2);

        // 3. Free 2026 slot -> round-robin dispatches 2025 ahead of 2024's 4th shard! 2025 finishes cleanly.
        bgRelease.TrySetResult(true);
        Assert.That((await bg2026.WaitAsync(TimeSpan.FromSeconds(2))).Success, Is.True);
        Assert.That((await run2025.WaitAsync(TimeSpan.FromSeconds(2))).Success, Is.True);
        await revit2025Done.Task.WaitAsync(TimeSpan.FromSeconds(2));

        // 4. Now 2024's 4th (final) shard dispatches and finishes AFTER 2025 already ran and finished.
        shard1Release.TrySetResult(true);
        shardRestRelease.TrySetResult(true);
        Assert.That((await run2024.WaitAsync(TimeSpan.FromSeconds(2))).Success, Is.True);

        // Give scheduler a brief settle window and assert no stale 2025 standby worker was booted.
        await Task.Delay(75);
        PoolStatusSnapshot finalStatus = supervisor.GetStatus();
        Assert.That(finalStatus.Versions.ContainsKey(2025), Is.False);
        Assert.That(launcher.LaunchedRequests.Count(r => r.RevitVersion == 2025), Is.EqualTo(1));
    }

    [Test]
    public async Task CrossVersionPipelining_QueuedPrewarmIsPrunedWhenTargetVersionRunRegistersAndCompletes()
    {
        var bgHold = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var revit2024Entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var revit2024Hold = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        int bgCount = 0;
        var threeBgActive = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        using var launcher = new StubWorkerProcessLauncher((server, launchReq) =>
        {
            server.InitialIsBusy = false;
            server.ExecutionHandler = async (req, emitTest, ct) =>
            {
                if (launchReq.RevitVersion == 2026)
                {
                    if (Interlocked.Increment(ref bgCount) == 3)
                    {
                        threeBgActive.TrySetResult(true);
                    }

                    await bgHold.Task.WaitAsync(ct).ConfigureAwait(false);
                    return BuildSuccessResult(req, emitTest);
                }

                if (launchReq.RevitVersion == 2024)
                {
                    revit2024Entered.TrySetResult(true);
                    await revit2024Hold.Task.WaitAsync(ct).ConfigureAwait(false);
                    return BuildSuccessResult(req, emitTest);
                }

                return BuildSuccessResult(req, emitTest);
            };
        }, usesCleanShim: false);

        await using var supervisor = CreateSupervisor(launcher, new StubMemoryProbe(0));

        // 1. Occupy 3 slots with 2026 and 1 slot with single-shard 2024 (NextVersion = 2025):
        //    2024 enters its final shard while cap-4 is full, queuing PipelinePrewarmRequest(2025).
        Task<OneShotRunResult>[] bgRuns = Enumerable.Range(1, 3)
            .Select(i => supervisor.RunAsync(new OneShotRunOptions
            {
                RevitVersion = 2026,
                AssemblyPath = _singleFixtureDll,
                TrxPath = Path.Combine(_tempRoot, "out", $"prune_bg_{i}.trx"),
                Shards = 1
            }))
            .ToArray();
        await threeBgActive.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Task<OneShotRunResult> run2024 = supervisor.RunAsync(new OneShotRunOptions
        {
            RevitVersion = 2024,
            NextVersion = 2025,
            AssemblyPath = _singleFixtureDll,
            TrxPath = Path.Combine(_tempRoot, "out", "prune_2024.trx"),
            Shards = 1
        });
        await revit2024Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await WaitForConditionAsync(() => supervisor.GetStatus().Active == 4);

        // 2. Register 2025 while cap-4 is saturated, then release all work so 2025 runs and finishes.
        Task<OneShotRunResult> run2025 = supervisor.RunAsync(new OneShotRunOptions
        {
            RevitVersion = 2025,
            AssemblyPath = _singleFixtureDll,
            TrxPath = Path.Combine(_tempRoot, "out", "prune_2025.trx"),
            Shards = 1
        });
        await WaitForConditionAsync(() => supervisor.GetStatus().QueuedRuns == 1);

        revit2024Hold.TrySetResult(true);
        bgHold.TrySetResult(true);

        OneShotRunResult[] allResults = await Task.WhenAll(bgRuns.Concat(new[] { run2024, run2025 })).WaitAsync(TimeSpan.FromSeconds(3));
        Assert.That(allResults.All(r => r.Success), Is.True);

        await Task.Delay(75);
        PoolStatusSnapshot finalStatus = supervisor.GetStatus();
        Assert.That(finalStatus.Versions.ContainsKey(2025), Is.False);
        Assert.That(launcher.LaunchedRequests.Count(r => r.RevitVersion == 2025), Is.EqualTo(1));
    }

    private async Task AssertNoLookaheadAsync(
        int concurrentRuns,
        bool usesCleanShim,
        ulong availableRamBytes,
        int expectedQueued)
    {
        int activeCount = 0;
        var fourActive = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseAll = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        using var launcher = new StubWorkerProcessLauncher((server, _) =>
        {
            server.InitialIsBusy = false;
            server.ExecutionHandler = async (req, emitTest, ct) =>
            {
                if (Interlocked.Increment(ref activeCount) == 4)
                {
                    fourActive.TrySetResult(true);
                }

                await releaseAll.Task.WaitAsync(ct).ConfigureAwait(false);
                Interlocked.Decrement(ref activeCount);
                return BuildSuccessResult(req, emitTest);
            };
        }, usesCleanShim: usesCleanShim);

        await using var supervisor = CreateSupervisor(
            launcher,
            new StubMemoryProbe(availableRamBytes),
            maxActiveWorkers: 4);

        Task<OneShotRunResult>[] runs = Enumerable.Range(1, concurrentRuns)
            .Select(i => supervisor.RunAsync(new OneShotRunOptions
            {
                RevitVersion = 2026,
                AssemblyPath = _singleFixtureDll,
                TrxPath = Path.Combine(_tempRoot, "out", $"guard_{usesCleanShim}_{availableRamBytes}_{i}.trx"),
                Shards = 1
            }))
            .ToArray();

        await fourActive.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await WaitForConditionAsync(() => supervisor.GetStatus() is { Active: 4 } s && s.QueuedRuns == expectedQueued);

        // Give async background tasks a brief moment to verify no lookahead boot was scheduled.
        await Task.Delay(50);

        PoolStatusSnapshot status = supervisor.GetStatus();
        Assert.That(status.Active, Is.EqualTo(4));
        Assert.That(status.Booting, Is.EqualTo(0));
        Assert.That(status.ReadyStandby, Is.EqualTo(0));
        Assert.That(status.TotalResident, Is.EqualTo(4));
        Assert.That(launcher.Workers.Count, Is.EqualTo(4));

        releaseAll.TrySetResult(true);
        OneShotRunResult[] results = await Task.WhenAll(runs).WaitAsync(TimeSpan.FromSeconds(2));
        Assert.That(results.All(r => r.Success), Is.True);
    }

    [Test]
    public async Task RamAdmissionGuard_GuaranteesFloorFourUnderLowMemory_AndRespectsSmallerCap()
    {
        const ulong GiB = 1024UL * 1024 * 1024;
        int activeCount = 0;
        var fourActive = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseAll = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        using var launcher = new StubWorkerProcessLauncher((server, _) =>
        {
            server.InitialIsBusy = false;
            server.ExecutionHandler = async (req, emitTest, ct) =>
            {
                if (Interlocked.Increment(ref activeCount) == 4)
                {
                    fourActive.TrySetResult(true);
                }

                await releaseAll.Task.WaitAsync(ct).ConfigureAwait(false);
                Interlocked.Decrement(ref activeCount);
                return BuildSuccessResult(req, emitTest);
            };
        }, usesCleanShim: false);

        // Even with 0 bytes free RAM and MaxActiveWorkers = 8, guaranteed floor of 4 slots executes.
        await using var supervisor = CreateSupervisor(launcher, new StubMemoryProbe(0UL), maxActiveWorkers: 8);
        Task<OneShotRunResult>[] runs = Enumerable.Range(1, 6)
            .Select(i => supervisor.RunAsync(new OneShotRunOptions
            {
                RevitVersion = 2026,
                AssemblyPath = _singleFixtureDll,
                TrxPath = Path.Combine(_tempRoot, "out", $"floor_{i}.trx"),
                Shards = 1
            }))
            .ToArray();

        await fourActive.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await WaitForConditionAsync(() => supervisor.GetStatus() is { Active: 4, QueuedRuns: 2 });
        await Task.Delay(50);

        PoolStatusSnapshot status = supervisor.GetStatus();
        Assert.That(status.Active, Is.EqualTo(4));
        Assert.That(status.Booting, Is.EqualTo(0));
        Assert.That(status.TotalResident, Is.EqualTo(4));
        Assert.That(status.QueuedRuns, Is.EqualTo(2));

        releaseAll.TrySetResult(true);
        OneShotRunResult[] results = await Task.WhenAll(runs).WaitAsync(TimeSpan.FromSeconds(3));
        Assert.That(results.All(r => r.Success), Is.True);

        // Drain and verify warm --count 6 under 9 GiB (< 10 GiB) stops at floor 4.
        using var warmLauncher = new StubWorkerProcessLauncher((server, _) =>
        {
            server.InitialIsBusy = false;
        }, usesCleanShim: true);
        await using var warmSupervisor = CreateSupervisor(warmLauncher, new StubMemoryProbe(10UL * GiB - 1), maxActiveWorkers: 8);
        WarmResult warm = await warmSupervisor.WarmAsync(2026, 6);
        Assert.That(warm.Success, Is.True, warm.Error);
        Assert.That(warm.ReadyCount, Is.EqualTo(4));
        Assert.That(warm.Status!.TotalResident, Is.EqualTo(4));

        // When MaxActiveWorkers = 2, floor is min(4, 2) = 2.
        await using var cap2Supervisor = CreateSupervisor(warmLauncher, new StubMemoryProbe(0UL), maxActiveWorkers: 2);
        WarmResult warm2 = await cap2Supervisor.WarmAsync(2026, 4);
        Assert.That(warm2.Success, Is.True, warm2.Error);
        Assert.That(warm2.ReadyCount, Is.EqualTo(2));
        Assert.That(warm2.Status!.TotalResident, Is.EqualTo(2));
    }

    [Test]
    public async Task RamAdmissionGuard_EvictsIdleWrongVersionStandby_WhenLowRamBlocksReachingFloor()
    {
        const ulong GiB = 1024UL * 1024 * 1024;
        var memoryProbe = new MutableMemoryProbe(20UL * GiB);
        using var launcher = new StubWorkerProcessLauncher((server, _) =>
        {
            server.InitialIsBusy = false;
            server.ExecutionHandler = (req, emitTest, _) => Task.FromResult(BuildSuccessResult(req, emitTest));
        }, usesCleanShim: true);

        await using var supervisor = CreateSupervisor(launcher, memoryProbe, maxActiveWorkers: 8);

        // Warm 4 idle 2024 standby workers while RAM is plentiful (floor = 4).
        WarmResult warm2024 = await supervisor.WarmAsync(2024, 4);
        Assert.That(warm2024.Success, Is.True, warm2024.Error);
        Assert.That(warm2024.ReadyCount, Is.EqualTo(4));

        // Drop available RAM below 10 GiB: slots 5..8 are denied, so 2026 demand MUST evict idle 2024 standbys.
        memoryProbe.SetAvailableBytes(5UL * GiB);

        OneShotRunResult run2026a = await supervisor.RunAsync(new OneShotRunOptions
        {
            RevitVersion = 2026,
            AssemblyPath = _singleFixtureDll,
            TrxPath = Path.Combine(_tempRoot, "out", "evict_floor_1.trx"),
            Shards = 1,
            BootTimeout = TimeSpan.FromSeconds(3)
        }).WaitAsync(TimeSpan.FromSeconds(3));

        Assert.That(run2026a.Success, Is.True, run2026a.Error);
        Assert.That(launcher.Workers.Count(w => w.IsTerminated), Is.GreaterThanOrEqualTo(2));
        Assert.That(supervisor.GetStatus().TotalResident, Is.LessThanOrEqualTo(4));
    }

    [Test]
    public async Task RamAdmissionGuard_ReservesTwoGiBPerBootingWorkerAboveFloor_ForDemandAndWarm()
    {
        const ulong GiB = 1024UL * 1024 * 1024;
        var memoryProbe = new MutableMemoryProbe(20UL * GiB);
        int executedCount = 0;
        var fourActive = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var holdActive = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var holdWave2Boots = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        int launchedCount = 0;

        using var launcher = new StubWorkerProcessLauncher((server, launchReq) =>
        {
            int idx = Interlocked.Increment(ref launchedCount);
            if (idx <= 4)
            {
                server.InitialIsBusy = false;
            }
            else
            {
                server.InitialIsBusy = true;
                _ = Task.Run(async () =>
                {
                    await holdWave2Boots.Task.ConfigureAwait(false);
                    while (!server.ClientEverConnected)
                    {
                        await Task.Delay(5).ConfigureAwait(false);
                    }
                    server.PushStatus(isBusy: false);
                });
            }

            server.ExecutionHandler = async (req, emitTest, ct) =>
            {
                if (Interlocked.Increment(ref executedCount) == 4)
                {
                    fourActive.TrySetResult(true);
                }

                await holdActive.Task.WaitAsync(ct).ConfigureAwait(false);
                return BuildSuccessResult(req, emitTest);
            };
        }, usesCleanShim: false);

        await using var supervisor = CreateSupervisor(launcher, memoryProbe, maxActiveWorkers: 8);

        // Occupy floor slots 1..4 as Active.
        Task<OneShotRunResult>[] floorRuns = Enumerable.Range(1, 4)
            .Select(i => supervisor.RunAsync(new OneShotRunOptions
            {
                RevitVersion = 2024,
                AssemblyPath = _singleFixtureDll,
                TrxPath = Path.Combine(_tempRoot, "out", $"res_floor_{i}.trx"),
                Shards = 1
            }))
            .ToArray();
        await fourActive.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await WaitForConditionAsync(() => supervisor.GetStatus().Active == 4);

        // Set free RAM to 13 GiB:
        // - Worker 5 (0 booting above floor): 13 - 0 = 13 >= 10 -> admitted (Booting = 1)
        // - Worker 6 (1 booting above floor): 13 - 2 = 11 >= 10 -> admitted (Booting = 2)
        // - Worker 7 (2 booting above floor): 13 - 4 = 9 < 10 -> DENIED!
        memoryProbe.SetAvailableBytes(13UL * GiB);

        Task<OneShotRunResult>[] aboveFloorRuns = Enumerable.Range(5, 4)
            .Select(i => supervisor.RunAsync(new OneShotRunOptions
            {
                RevitVersion = 2024,
                AssemblyPath = _singleFixtureDll,
                TrxPath = Path.Combine(_tempRoot, "out", $"res_above_{i}.trx"),
                Shards = 1
            }))
            .ToArray();

        await WaitForConditionAsync(() => supervisor.GetStatus() is { Active: 4, Booting: 2, QueuedRuns: 4 });
        await Task.Delay(60);

        PoolStatusSnapshot midStatus = supervisor.GetStatus();
        Assert.That(midStatus.Active, Is.EqualTo(4));
        Assert.That(midStatus.Booting, Is.EqualTo(2));
        Assert.That(midStatus.TotalResident, Is.EqualTo(6));
        Assert.That(midStatus.QueuedRuns, Is.EqualTo(4));

        // Release booting workers and active runs so everything completes cleanly.
        holdWave2Boots.TrySetResult(true);
        holdActive.TrySetResult(true);
        OneShotRunResult[] all = await Task.WhenAll(floorRuns.Concat(aboveFloorRuns)).WaitAsync(TimeSpan.FromSeconds(4));
        Assert.That(all.All(r => r.Success), Is.True);
    }

    [Test]
    public async Task MaxConcurrentBoots_ThrottlesMultiWaveWarmCountEightAndTen_AndAwaitsAllWavesWithoutStalling()
    {
        const ulong GiB = 1024UL * 1024 * 1024;
        int currentBooting = 0;
        int peakBooting = 0;
        var wave1FourBooting = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var wave2FourBooting = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var wave3TwoBooting = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseWave1 = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseWave2 = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseWave3 = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        int launchedCount = 0;

        using var launcher = new StubWorkerProcessLauncher((server, launchReq) =>
        {
            int idx = Interlocked.Increment(ref launchedCount);
            int bootingNow = Interlocked.Increment(ref currentBooting);
            UpdateMax(ref peakBooting, bootingNow);

            if (idx == 4)
            {
                wave1FourBooting.TrySetResult(true);
            }
            else if (idx == 8)
            {
                wave2FourBooting.TrySetResult(true);
            }
            else if (idx == 10)
            {
                wave3TwoBooting.TrySetResult(true);
            }

            server.InitialIsBusy = true;
            Task gate = idx <= 4
                ? releaseWave1.Task
                : (idx <= 8 ? releaseWave2.Task : releaseWave3.Task);

            _ = Task.Run(async () =>
            {
                await gate.ConfigureAwait(false);
                while (!server.ClientEverConnected)
                {
                    await Task.Delay(5).ConfigureAwait(false);
                }

                Interlocked.Decrement(ref currentBooting);
                server.PushStatus(isBusy: false);
            });
        }, usesCleanShim: true);

        await using var supervisor = CreateSupervisor(launcher, new StubMemoryProbe(32UL * GiB), maxActiveWorkers: 10);

        // Start warm --count 8 with PendingCount == 0: Wave 1 must cap at MaxConcurrentBoots = 4.
        Task<WarmResult> warm8Task = supervisor.WarmAsync(2026, count: 8, bootTimeout: TimeSpan.FromSeconds(10));
        await wave1FourBooting.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await WaitForConditionAsync(() => supervisor.GetStatus() is { Booting: 4, ReadyStandby: 0 });
        await Task.Delay(50);

        Assert.That(Volatile.Read(ref peakBooting), Is.EqualTo(4));
        Assert.That(warm8Task.IsCompleted, Is.False);

        // Release Wave 1 -> BootWorkerAsync must trigger Wave 2 even though PendingCount == 0, and WarmAsync must wait for Wave 2.
        releaseWave1.TrySetResult(true);
        await wave2FourBooting.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await WaitForConditionAsync(() => supervisor.GetStatus() is { ReadyStandby: 4, Booting: 4 });
        Assert.That(Volatile.Read(ref peakBooting), Is.EqualTo(4));
        Assert.That(warm8Task.IsCompleted, Is.False, "WarmAsync must await wave 2 before returning.");

        // Release Wave 2 -> WarmAsync completes with all 8 ReadyStandby workers.
        releaseWave2.TrySetResult(true);
        WarmResult warm8 = await warm8Task.WaitAsync(TimeSpan.FromSeconds(4));
        Assert.That(warm8.Success, Is.True, warm8.Error);
        Assert.That(warm8.TargetCount, Is.EqualTo(8));
        Assert.That(warm8.ReadyCount, Is.EqualTo(8));
        Assert.That(warm8.Status!.TotalResident, Is.EqualTo(8));

        // Now warm --count 10 -> Wave 3 launches remaining 2 workers up to MaxActiveWorkers = 10.
        Task<WarmResult> warm10Task = supervisor.WarmAsync(2026, count: 10, bootTimeout: TimeSpan.FromSeconds(10));
        await wave3TwoBooting.Task.WaitAsync(TimeSpan.FromSeconds(3));
        releaseWave3.TrySetResult(true);

        WarmResult warm10 = await warm10Task.WaitAsync(TimeSpan.FromSeconds(4));
        Assert.That(warm10.Success, Is.True, warm10.Error);
        Assert.That(warm10.TargetCount, Is.EqualTo(10));
        Assert.That(warm10.ReadyCount, Is.EqualTo(10));
        Assert.That(warm10.Status!.TotalResident, Is.EqualTo(10));
        Assert.That(Volatile.Read(ref peakBooting), Is.EqualTo(4));
    }

    [Test]
    public async Task MaxConcurrentBoots_ThrottlesGlobalBootsAcrossMixedDemandAndStandby_AndScalesActiveToEight()
    {
        const ulong GiB = 1024UL * 1024 * 1024;
        int currentBooting = 0;
        int peakBooting = 0;
        int currentActive = 0;
        int peakActive = 0;
        var fourBooting = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseWave1 = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseWave2 = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var eightActive = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseExecs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        int launchedCount = 0;

        using var launcher = new StubWorkerProcessLauncher((server, launchReq) =>
        {
            int idx = Interlocked.Increment(ref launchedCount);
            int bootingNow = Interlocked.Increment(ref currentBooting);
            UpdateMax(ref peakBooting, bootingNow);
            if (idx == 4)
            {
                fourBooting.TrySetResult(true);
            }

            server.InitialIsBusy = true;
            Task gate = idx <= 4 ? releaseWave1.Task : releaseWave2.Task;
            _ = Task.Run(async () =>
            {
                await gate.ConfigureAwait(false);
                while (!server.ClientEverConnected)
                {
                    await Task.Delay(5).ConfigureAwait(false);
                }

                Interlocked.Decrement(ref currentBooting);
                server.PushStatus(isBusy: false);
            });

            server.ExecutionHandler = async (req, emitTest, ct) =>
            {
                int activeNow = Interlocked.Increment(ref currentActive);
                UpdateMax(ref peakActive, activeNow);
                if (activeNow == 8)
                {
                    eightActive.TrySetResult(true);
                }

                await releaseExecs.Task.WaitAsync(ct).ConfigureAwait(false);
                Interlocked.Decrement(ref currentActive);
                return BuildSuccessResult(req, emitTest);
            };
        }, usesCleanShim: false);

        await using var supervisor = CreateSupervisor(launcher, new StubMemoryProbe(32UL * GiB), maxActiveWorkers: 8);

        // Submit 8 demand runs across 2024 and 2026 concurrently: Wave 1 caps at 4 booting globally.
        Task<OneShotRunResult>[] runs = Enumerable.Range(1, 8)
            .Select(i => supervisor.RunAsync(new OneShotRunOptions
            {
                RevitVersion = i <= 4 ? 2024 : 2026,
                AssemblyPath = _singleFixtureDll,
                TrxPath = Path.Combine(_tempRoot, "out", $"global_boot_{i}.trx"),
                Shards = 1
            }))
            .ToArray();

        await fourBooting.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await WaitForConditionAsync(() => supervisor.GetStatus() is { Booting: 4, Active: 0, QueuedRuns: 8 });
        await Task.Delay(50);
        Assert.That(Volatile.Read(ref peakBooting), Is.EqualTo(4));

        // Release Wave 1 -> 4 become Active and Wave 2 (4 more boots) launches, eventually reaching Active == 8.
        releaseWave1.TrySetResult(true);
        await WaitForConditionAsync(() => supervisor.GetStatus() is { Active: 4, Booting: 4 });
        Assert.That(Volatile.Read(ref peakBooting), Is.EqualTo(4));

        releaseWave2.TrySetResult(true);
        await eightActive.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.That(supervisor.GetStatus().Active, Is.EqualTo(8));
        Assert.That(Volatile.Read(ref peakBooting), Is.EqualTo(4));

        releaseExecs.TrySetResult(true);
        OneShotRunResult[] results = await Task.WhenAll(runs).WaitAsync(TimeSpan.FromSeconds(4));
        Assert.That(results.All(r => r.Success), Is.True);
        Assert.That(Volatile.Read(ref peakActive), Is.EqualTo(8));
    }

    [Test]
    public async Task Lookahead_AtDefaultCapEight_AllowsNinthResidentStandbyAtTwelveGiB_AndBlocksBelowTwelveGiB()
    {
        const ulong GiB = 1024UL * 1024 * 1024;
        var memoryProbe = new MutableMemoryProbe(24UL * GiB);
        int activeNow = 0;
        int peakActive = 0;
        int peakResident = 0;
        var eightActive = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstRelease = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var allRelease = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var observedWorkerIds = new ConcurrentQueue<string>();
        string? lookaheadWorkerId = null;
        int launchedCount = 0;

        StubWorkerProcessLauncher? launcher = null;
        launcher = new StubWorkerProcessLauncher((server, req) =>
        {
            int idx = Interlocked.Increment(ref launchedCount);
            if (idx == 9)
            {
                Volatile.Write(ref lookaheadWorkerId, req.WorkerId);
                server.InitialIsBusy = true;
                server.AutoReadyDelay = TimeSpan.FromMilliseconds(15);
            }
            else
            {
                server.InitialIsBusy = false;
            }

            server.ExecutionHandler = async (testReq, emitTest, ct) =>
            {
                observedWorkerIds.Enqueue(req.WorkerId);
                int currentActive = Interlocked.Increment(ref activeNow);
                UpdateMax(ref peakActive, currentActive);
                UpdateMax(ref peakResident, launcher!.Workers.Count(w => !w.IsTerminated));

                if (currentActive == 8)
                {
                    eightActive.TrySetResult(true);
                }

                if (idx == 1)
                {
                    await firstRelease.Task.WaitAsync(ct).ConfigureAwait(false);
                }
                else
                {
                    await allRelease.Task.WaitAsync(ct).ConfigureAwait(false);
                }

                Interlocked.Decrement(ref activeNow);
                return BuildSuccessResult(testReq, emitTest);
            };
        }, usesCleanShim: true);

        using (launcher)
        await using (var supervisor = CreateSupervisor(launcher, memoryProbe, maxActiveWorkers: 8))
        {
            Task<OneShotRunResult>[] runs = Enumerable.Range(1, 10)
                .Select(i => supervisor.RunAsync(new OneShotRunOptions
                {
                    RevitVersion = 2026,
                    AssemblyPath = _singleFixtureDll,
                    TrxPath = Path.Combine(_tempRoot, "out", $"la8_{i}.trx"),
                    Shards = 1
                }))
                .ToArray();

            await eightActive.Task.WaitAsync(TimeSpan.FromSeconds(4));

            await WaitForConditionAsync(() =>
            {
                PoolStatusSnapshot s = supervisor.GetStatus();
                UpdateMax(ref peakResident, s.TotalResident);
                return s.Active == 8 && s.ReadyStandby == 1 && s.Booting == 0 && s.TotalResident == 9 && s.QueuedRuns == 2;
            });

            PoolStatusSnapshot preRelease = supervisor.GetStatus();
            Assert.That(preRelease.ActiveCap, Is.EqualTo(8));
            Assert.That(preRelease.Active, Is.EqualTo(8));
            Assert.That(preRelease.ReadyStandby, Is.EqualTo(1));
            Assert.That(preRelease.TotalResident, Is.EqualTo(9));

            string preBootedLookaheadId = Volatile.Read(ref lookaheadWorkerId)!;
            Assert.That(preBootedLookaheadId, Is.Not.Null.And.Not.Empty);

            // Lower RAM to 11 GiB (< 12 GiB) before releasing first worker so after lookahead is consumed, no new lookahead boots.
            memoryProbe.SetAvailableBytes(11UL * GiB);
            firstRelease.TrySetResult(true);
            await WaitForConditionAsync(() => observedWorkerIds.Contains(preBootedLookaheadId));
            await Task.Delay(50);

            PoolStatusSnapshot belowLookaheadStatus = supervisor.GetStatus();
            Assert.That(belowLookaheadStatus.Active, Is.EqualTo(8));
            Assert.That(belowLookaheadStatus.Booting, Is.EqualTo(0));
            Assert.That(belowLookaheadStatus.ReadyStandby, Is.EqualTo(0));
            Assert.That(belowLookaheadStatus.TotalResident, Is.EqualTo(8));

            allRelease.TrySetResult(true);
            OneShotRunResult[] results = await Task.WhenAll(runs).WaitAsync(TimeSpan.FromSeconds(4));
            Assert.That(results.All(r => r.Success), Is.True);
            Assert.That(Volatile.Read(ref peakActive), Is.EqualTo(8));
            Assert.That(Volatile.Read(ref peakResident), Is.EqualTo(9));
        }
    }

    [Test]
    public async Task WarmAsync_WhenWave2BlockedByRamGuard_ReturnsAdmittedFloorWithoutStalling()
    {
        const ulong GiB = 1024UL * 1024 * 1024;
        var memoryProbe = new MutableMemoryProbe(9UL * GiB);
        using var launcher = new StubWorkerProcessLauncher((server, _) =>
        {
            server.InitialIsBusy = true;
            server.AutoReadyDelay = TimeSpan.FromMilliseconds(15);
        }, usesCleanShim: true);

        await using var supervisor = CreateSupervisor(launcher, memoryProbe, maxActiveWorkers: 8);
        WarmResult warm = await supervisor.WarmAsync(2026, count: 8, bootTimeout: TimeSpan.FromSeconds(5))
            .WaitAsync(TimeSpan.FromSeconds(4));

        Assert.That(warm.Success, Is.True, warm.Error);
        Assert.That(warm.TargetCount, Is.EqualTo(8));
        Assert.That(warm.ReadyCount, Is.EqualTo(4));
        Assert.That(warm.Status!.TotalResident, Is.EqualTo(4));
    }

    private WorkerPoolSupervisor CreateSupervisor(
        IWorkerProcessLauncher launcher,
        IMemoryProbe memoryProbe,
        TimeSpan? drainGraceTimeout = null,
        string? pipeName = null,
        int maxActiveWorkers = SupervisorOptions.DefaultMaxActiveWorkers)
    {
        return new WorkerPoolSupervisor(
            new SupervisorOptions
            {
                PipeName = pipeName ?? $"Synthetic_SchedTest_{Guid.NewGuid():N}",
                MaxActiveWorkers = maxActiveWorkers,
                CurrentLatestVersion = 2026,
                IdleTimeout = TimeSpan.FromMinutes(25),
                DrainGraceTimeout = drainGraceTimeout ?? TimeSpan.FromSeconds(5),
                TimingCacheDirectory = Path.Combine(_tempRoot, "timing_cache")
            },
            _discovery,
            launcher,
            new RevitPipeTestClientFactory(),
            bundleLeaseFactory: _ => new NoOpDisposable(),
            memoryProbe: memoryProbe);
    }

    private string CopyAssemblyToSubdir(string sourceDll, string subdirName)
    {
        string dir = Path.Combine(_tempRoot, subdirName);
        Directory.CreateDirectory(dir);
        string destDll = Path.Combine(dir, Path.GetFileName(sourceDll));
        File.Copy(sourceDll, destDll, overwrite: true);
        return destDll;
    }

    private static TestAssemblyModel BuildSuccessResult(TestRequest req, Action<TestModel> emitTest)
    {
        var test = new TestModel
        {
            Name = "QuickTest",
            FullName = "QuickSuite.QuickFix.QuickTest",
            Success = true,
            Time = 5.0
        };
        emitTest(test);
        return new TestAssemblyModel
        {
            FileName = req.TestPathFile ?? string.Empty,
            Name = "Synthetic.RevitTests",
            Success = true,
            Tests = new List<TestTypeModel>
            {
                new()
                {
                    Name = "QuickFix",
                    FullName = "QuickSuite.QuickFix",
                    Success = true,
                    Tests = new List<TestModel> { test }
                }
            }
        };
    }

    private static async Task WaitForConditionAsync(Func<bool> predicate)
    {
        DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
        while (DateTime.UtcNow < deadline)
        {
            if (predicate())
            {
                return;
            }

            await Task.Delay(10);
        }

        Assert.Fail("Timed out waiting for scheduler/lookahead/pipeline condition.");
    }

    private static void UpdateMax(ref int target, int candidate)
    {
        while (true)
        {
            int current = Volatile.Read(ref target);
            if (candidate <= current)
            {
                return;
            }

            if (Interlocked.CompareExchange(ref target, candidate, current) == current)
            {
                return;
            }
        }
    }

    private sealed class StubMemoryProbe : IMemoryProbe
    {
        private readonly ulong _availableBytes;

        public StubMemoryProbe(ulong availableBytes)
        {
            _availableBytes = availableBytes;
        }

        public ulong GetAvailablePhysicalBytes() => _availableBytes;
    }

    private sealed class MutableMemoryProbe : IMemoryProbe
    {
        private long _availableBytes;

        public MutableMemoryProbe(ulong availableBytes)
        {
            _availableBytes = (long)availableBytes;
        }

        public void SetAvailableBytes(ulong availableBytes) => Interlocked.Exchange(ref _availableBytes, (long)availableBytes);

        public ulong GetAvailablePhysicalBytes() => (ulong)Volatile.Read(ref _availableBytes);
    }

    private sealed class NoOpDisposable : IDisposable
    {
        public void Dispose()
        {
        }
    }
}
