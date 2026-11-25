using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
public class SupervisorLifecycleTests
{
    private string _tempRoot = string.Empty;
    private string _fakeAssemblyPath = string.Empty;
    private RevitInstallationDiscovery _discovery = null!;
    private static readonly byte[] CompiledSupervisorTestDllBytes = TestAssemblyCompiler.CompileToBytes(
        "Synthetic.RevitTests",
        """
        using NUnit.Framework;

        namespace Ns
        {
            [TestFixture]
            [Category("RevitSmoke")]
            public class Fixture
            {
                [Test]
                public void SmokeTest() { }
            }

            [TestFixture]
            [Category("RevitAdapter")]
            public class Failing
            {
                [Test]
                public void Boom() { }
            }
        }
        """);

    [SetUp]
    public void SetUp()
    {
        _tempRoot = Path.Combine(
            Path.GetTempPath(),
            "RevitWorkerPool_SupervisorTests",
            Guid.NewGuid().ToString("N"));

        foreach (int year in new[] { 2024, 2026 })
        {
            string revitDir = Path.Combine(_tempRoot, "Autodesk", $"Revit {year}");
            Directory.CreateDirectory(revitDir);
            File.WriteAllText(Path.Combine(revitDir, "Revit.exe"), string.Empty);
        }

        _fakeAssemblyPath = Path.Combine(_tempRoot, "Synthetic.RevitTests.dll");
        File.WriteAllBytes(_fakeAssemblyPath, CompiledSupervisorTestDllBytes);

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
    public async Task Warm_Status_And_Drain_OverIpc_DefaultsLatestAndCount2_AndReleasesBundleLease()
    {
        int leaseAcquired = 0;
        int leaseDisposed = 0;
        string pipeName = CreateUniquePipeName();

        using var launcher = CreatePassingLauncher();
        await using var supervisor = new WorkerPoolSupervisor(
            new SupervisorOptions
            {
                PipeName = pipeName,
                CurrentLatestVersion = 2026,
                IdleTimeout = TimeSpan.FromMinutes(25),
                TimingCacheDirectory = Path.Combine(_tempRoot, "timing_cache")
            },
            _discovery,
            launcher,
            new RevitPipeTestClientFactory(),
            bundleLeaseFactory: _ =>
            {
                Interlocked.Increment(ref leaseAcquired);
                return new CountingDisposable(() => Interlocked.Increment(ref leaseDisposed));
            });

        await using var ipcServer = new SupervisorIpcServer(
            pipeName,
            (req, ct) => supervisor.HandleIpcRequestAsync(req, ct));
        supervisor.AttachIpcServer(ipcServer);
        supervisor.Start();
        ipcServer.Start();

        SupervisorIpcResponse warmResp = await SupervisorIpcClient.SendAsync(
            pipeName,
            new SupervisorIpcRequest { Command = SupervisorIpcCommands.Warm },
            TimeSpan.FromSeconds(5));

        Assert.That(warmResp.Success, Is.True, warmResp.Error);
        Assert.That(warmResp.ExitCode, Is.EqualTo(0));
        Assert.That(warmResp.WarmResult, Is.Not.Null);
        Assert.That(warmResp.WarmResult!.Version, Is.EqualTo(2026));
        Assert.That(warmResp.WarmResult.TargetCount, Is.EqualTo(2));
        Assert.That(warmResp.WarmResult.ReadyCount, Is.EqualTo(2));

        SupervisorIpcResponse statusResp = await SupervisorIpcClient.SendAsync(
            pipeName,
            new SupervisorIpcRequest { Command = SupervisorIpcCommands.Status },
            TimeSpan.FromSeconds(5));

        Assert.That(statusResp.Success, Is.True);
        PoolStatusSnapshot status = statusResp.Status!;
        Assert.That(status.Running, Is.True);
        Assert.That(status.Booting, Is.EqualTo(0));
        Assert.That(status.ReadyStandby, Is.EqualTo(2));
        Assert.That(status.Active, Is.EqualTo(0));
        Assert.That(status.TotalResident, Is.EqualTo(2));
        Assert.That(status.ActiveCap, Is.EqualTo(4));
        Assert.That(status.LastActivityUtc, Is.GreaterThan(DateTimeOffset.MinValue));
        Assert.That(status.Versions[2026].ReadyStandby, Is.EqualTo(2));
        Assert.That(status.Versions[2026].StandbyTarget, Is.EqualTo(2));

        Assert.That(Volatile.Read(ref leaseAcquired), Is.EqualTo(1));
        Assert.That(Volatile.Read(ref leaseDisposed), Is.EqualTo(0));

        SupervisorIpcResponse drainResp = await SupervisorIpcClient.SendAsync(
            pipeName,
            new SupervisorIpcRequest { Command = SupervisorIpcCommands.Drain },
            TimeSpan.FromSeconds(5));

        Assert.That(drainResp.Success, Is.True, drainResp.Error);
        Assert.That(drainResp.ExitCode, Is.EqualTo(0));
        Assert.That(drainResp.Status!.TotalResident, Is.EqualTo(0));
        Assert.That(drainResp.Status.Booting, Is.EqualTo(0));
        Assert.That(drainResp.Status.ReadyStandby, Is.EqualTo(0));
        Assert.That(drainResp.Status.Active, Is.EqualTo(0));
        Assert.That(launcher.Workers.All(w => w.IsTerminated), Is.True);
        Assert.That(Volatile.Read(ref leaseDisposed), Is.EqualTo(1));
        Assert.That(supervisor.HasShutdownCompleted, Is.True);
    }

    [Test]
    public async Task Warm_SupportsExplicitVersionAndCount_RespectsCap_AndRejectsMissingRevit()
    {
        string pipeName = CreateUniquePipeName();
        using var launcher = CreatePassingLauncher();
        await using var supervisor = CreateSupervisor(pipeName, launcher, currentLatestVersion: 2026);
        await using var ipcServer = StartServer(pipeName, supervisor);

        SupervisorIpcResponse missingResp = await SupervisorIpcClient.SendAsync(
            pipeName,
            new SupervisorIpcRequest { Command = SupervisorIpcCommands.Warm, Version = 2099, Count = 1 },
            TimeSpan.FromSeconds(5));

        Assert.That(missingResp.Success, Is.False);
        Assert.That(missingResp.ExitCode, Is.EqualTo(2));
        Assert.That(missingResp.Error, Does.Contain("2099"));

        SupervisorIpcResponse explicit2024 = await SupervisorIpcClient.SendAsync(
            pipeName,
            new SupervisorIpcRequest { Command = SupervisorIpcCommands.Warm, Version = 2024, Count = 1 },
            TimeSpan.FromSeconds(5));

        Assert.That(explicit2024.Success, Is.True, explicit2024.Error);
        Assert.That(explicit2024.WarmResult!.Version, Is.EqualTo(2024));
        Assert.That(explicit2024.WarmResult.ReadyCount, Is.EqualTo(1));

        SupervisorIpcResponse overCap = await SupervisorIpcClient.SendAsync(
            pipeName,
            new SupervisorIpcRequest { Command = SupervisorIpcCommands.Warm, Version = 2026, Count = 6 },
            TimeSpan.FromSeconds(5));

        Assert.That(overCap.Success, Is.True, overCap.Error);
        Assert.That(overCap.WarmResult!.TargetCount, Is.EqualTo(4));
        Assert.That(overCap.Status!.TotalResident, Is.LessThanOrEqualTo(4));
    }

    [Test]
    public async Task Run_OverIpc_LazyAutoStartMaintainsLatestStandby_TerminatesConsumed_AndLeavesZeroForOlderVersion()
    {
        string pipeName = CreateUniquePipeName();
        using var launcher = CreatePassingLauncher();
        await using var supervisor = CreateSupervisor(pipeName, launcher, currentLatestVersion: 2026);
        await using var ipcServer = StartServer(pipeName, supervisor);

        // 1. First run for CURRENT_LATEST_VERSION (2026) sets lazy target=1 and replenishes asynchronously.
        string trx1 = Path.Combine(_tempRoot, "out", "run1.trx");
        SupervisorIpcResponse run1 = await SendRunAsync(pipeName, 2026, trx1, shards: 1);

        Assert.That(run1.Success, Is.True, run1.Error);
        Assert.That(run1.ExitCode, Is.EqualTo(0));
        Assert.That(run1.RunResult!.TotalTests, Is.EqualTo(1));
        Assert.That(File.Exists(trx1), Is.True);

        string consumedId1 = run1.RunResult.WorkerIds.Single();
        StubWorkerProcessLauncher.StubWorkerRecord consumedRecord1 =
            launcher.Workers.Single(w => w.WorkerId == consumedId1);
        Assert.That(consumedRecord1.IsTerminated, Is.True, "Consumed worker must be terminated immediately.");

        PoolStatusSnapshot statusAfter1 = await WaitForStatusAsync(
            pipeName,
            s => s.ReadyStandby == 1 && s.Booting == 0 && s.Active == 0);
        Assert.That(statusAfter1.Versions[2026].StandbyTarget, Is.EqualTo(1));
        Assert.That(statusAfter1.Versions[2026].ReadyStandby, Is.EqualTo(1));

        string prewarmedStandbyId = launcher.Workers.Single(w => !w.IsTerminated).WorkerId;
        Assert.That(prewarmedStandbyId, Is.Not.EqualTo(consumedId1));

        // 2. Second run for 2026 dispatches immediately to the waiting standby worker, terminates it, and replenishes.
        string trx2 = Path.Combine(_tempRoot, "out", "run2.trx");
        SupervisorIpcResponse run2 = await SendRunAsync(pipeName, 2026, trx2, shards: 1);

        Assert.That(run2.Success, Is.True, run2.Error);
        Assert.That(run2.RunResult!.WorkerIds.Single(), Is.EqualTo(prewarmedStandbyId));
        Assert.That(
            launcher.Workers.Single(w => w.WorkerId == prewarmedStandbyId).IsTerminated,
            Is.True,
            "Consumed standby worker must never be reused.");

        await WaitForStatusAsync(pipeName, s => s.ReadyStandby == 1 && s.Booting == 0 && s.Active == 0);

        // 3. Older version (2024) runs on demand, terminates immediately, and leaves 0 persistent standbys.
        string trx2024 = Path.Combine(_tempRoot, "out", "run2024.trx");
        SupervisorIpcResponse run2024 = await SendRunAsync(pipeName, 2024, trx2024, shards: 1);

        Assert.That(run2024.Success, Is.True, run2024.Error);
        Assert.That(run2024.RunResult!.Version, Is.EqualTo(2024));
        string consumed2024 = run2024.RunResult.WorkerIds.Single();
        Assert.That(launcher.Workers.Single(w => w.WorkerId == consumed2024).IsTerminated, Is.True);

        PoolStatusSnapshot statusAfter2024 = await WaitForStatusAsync(
            pipeName,
            s => s.Active == 0 && s.Booting == 0 && s.ReadyStandby == 1);
        Assert.That(statusAfter2024.Versions.ContainsKey(2024), Is.False, "Older versions must not retain persistent standby workers.");
    }

    [Test]
    public async Task Warm_HigherTargetThanLazyOne_PreservesTargetTwoAfterRun()
    {
        string pipeName = CreateUniquePipeName();
        using var launcher = CreatePassingLauncher();
        await using var supervisor = CreateSupervisor(pipeName, launcher, currentLatestVersion: 2026);
        await using var ipcServer = StartServer(pipeName, supervisor);

        SupervisorIpcResponse warm = await SupervisorIpcClient.SendAsync(
            pipeName,
            new SupervisorIpcRequest { Command = SupervisorIpcCommands.Warm, Version = 2026, Count = 2 },
            TimeSpan.FromSeconds(5));
        Assert.That(warm.Success, Is.True);

        SupervisorIpcResponse run = await SendRunAsync(pipeName, 2026, Path.Combine(_tempRoot, "out", "warm2.trx"));
        Assert.That(run.Success, Is.True);

        PoolStatusSnapshot status = await WaitForStatusAsync(
            pipeName,
            s => s.ReadyStandby == 2 && s.Booting == 0 && s.Active == 0);
        Assert.That(status.Versions[2026].StandbyTarget, Is.EqualTo(2));
        Assert.That(status.Versions[2026].ReadyStandby, Is.EqualTo(2));
    }

    [Test]
    public async Task Run_WaitsForAlreadyBootingStandbyWorker_WithoutSpawningDuplicate()
    {
        string pipeName = CreateUniquePipeName();
        var workerBooting = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        using var launcher = new StubWorkerProcessLauncher((server, _) =>
        {
            server.InitialIsBusy = true;
            server.AutoReadyDelay = TimeSpan.FromMilliseconds(60);
            server.ExecutionHandler = CreateDefaultPassingHandler();
            workerBooting.TrySetResult(true);
        });

        await using var supervisor = CreateSupervisor(pipeName, launcher, currentLatestVersion: 2026);
        await using var ipcServer = StartServer(pipeName, supervisor);

        Task<WarmResult> warmTask = supervisor.WarmAsync(version: 2026, count: 1);
        await workerBooting.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.That(launcher.Workers.Count, Is.EqualTo(1));
        string bootingWorkerId = launcher.Workers.Single().WorkerId;

        SupervisorIpcResponse runResp = await SendRunAsync(
            pipeName,
            2026,
            Path.Combine(_tempRoot, "out", "booting.trx"),
            shards: 1);
        await warmTask;

        Assert.That(runResp.Success, Is.True, runResp.Error);
        Assert.That(runResp.RunResult!.WorkerIds.Single(), Is.EqualTo(bootingWorkerId));
        Assert.That(launcher.Workers.Single(w => w.WorkerId == bootingWorkerId).IsTerminated, Is.True);
    }

    [Test]
    public async Task WarmAsync_BootFailure_ClearsStandbyTargetAndDoesNotSpinInfiniteLaunches()
    {
        string pipeName = CreateUniquePipeName();
        var countingLauncher = new CountingThrowingWorkerProcessLauncher();
        await using var supervisor = CreateSupervisor(pipeName, countingLauncher, currentLatestVersion: 2026);
        await using var ipcServer = StartServer(pipeName, supervisor);

        WarmResult warm = await supervisor.WarmAsync(version: 2026, count: 1);

        Assert.That(warm.Success, Is.False);
        Assert.That(warm.ExitCode, Is.EqualTo(2));
        Assert.That(warm.Error, Does.Contain("Simulated launch failure."));

        await Task.Delay(75);

        PoolStatusSnapshot status = supervisor.GetStatus();
        Assert.That(countingLauncher.LaunchAttempts, Is.EqualTo(1));
        Assert.That(status.Booting, Is.EqualTo(0));
        Assert.That(status.ReadyStandby, Is.EqualTo(0));
        Assert.That(status.Versions.ContainsKey(2026), Is.False);
    }

    [Test]
    public async Task StandbyReplenishment_BootFailure_ClearsStandbyTargetAndStopsRetrying()
    {
        string pipeName = CreateUniquePipeName();
        int launchAttempts = 0;
        using var innerLauncher = CreatePassingLauncher();
        var conditionalLauncher = new DelegatingWorkerProcessLauncher(
            usesCleanShim: false,
            launchAsync: (req, ct) =>
            {
                int attempt = Interlocked.Increment(ref launchAttempts);
                if (attempt == 1)
                {
                    return innerLauncher.LaunchAsync(req, ct);
                }

                return Task.FromException<LaunchedWorkerProcess>(
                    new InvalidOperationException($"Simulated replenishment failure #{attempt}."));
            });

        await using var supervisor = CreateSupervisor(pipeName, conditionalLauncher, currentLatestVersion: 2026);
        await using var ipcServer = StartServer(pipeName, supervisor);

        WarmResult warm = await supervisor.WarmAsync(version: 2026, count: 1);
        Assert.That(warm.Success, Is.True, warm.Error);
        Assert.That(Volatile.Read(ref launchAttempts), Is.EqualTo(1));

        // Force disconnect of the ready standby worker to trigger replenishment, which fails on attempt 2.
        innerLauncher.Workers.Single().Terminate();

        PoolStatusSnapshot finalStatus = await WaitForStatusAsync(
            pipeName,
            s => s.Booting == 0 && s.ReadyStandby == 0 && !s.Versions.ContainsKey(2026));

        await Task.Delay(75);

        Assert.That(Volatile.Read(ref launchAttempts), Is.EqualTo(2));
        Assert.That(finalStatus.Booting, Is.EqualTo(0));
        Assert.That(finalStatus.ReadyStandby, Is.EqualTo(0));
    }

    [Test]
    public async Task AcquireWorkerAsync_QueuedBehindSaturatedCapFour_DoesNotTimeOutBeforeSlotOpens()
    {
        string pipeName = CreateUniquePipeName();
        int activeNow = 0;
        var fourActive = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseRest = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        int execIndex = 0;

        using var launcher = new StubWorkerProcessLauncher((server, _) =>
        {
            server.InitialIsBusy = false;
            server.ExecutionHandler = async (req, emitTest, ct) =>
            {
                int idx = Interlocked.Increment(ref execIndex);
                if (Interlocked.Increment(ref activeNow) == 4)
                {
                    fourActive.TrySetResult(true);
                }

                if (idx == 1)
                {
                    await releaseFirst.Task.WaitAsync(ct).ConfigureAwait(false);
                }
                else
                {
                    await releaseRest.Task.WaitAsync(ct).ConfigureAwait(false);
                }

                Interlocked.Decrement(ref activeNow);
                var test = new TestModel
                {
                    Name = "QueueWaitTest",
                    FullName = "Ns.Fixture.QueueWaitTest",
                    Success = true,
                    Time = 5.0
                };
                emitTest(test);
                return BuildAssemblyModel(req, test);
            };
        });

        await using var supervisor = CreateSupervisor(pipeName, launcher, currentLatestVersion: 2026);
        await using var ipcServer = StartServer(pipeName, supervisor);

        Task<OneShotRunResult>[] saturatingRuns = Enumerable.Range(1, 4)
            .Select(i => supervisor.RunAsync(new OneShotRunOptions
            {
                RevitVersion = 2026,
                AssemblyPath = _fakeAssemblyPath,
                TrxPath = Path.Combine(_tempRoot, "out", $"sat_{i}.trx"),
                BootTimeout = TimeSpan.FromSeconds(5),
                ExecutionTimeout = TimeSpan.FromSeconds(5)
            }))
            .ToArray();

        await fourActive.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // 5th run arrives with a 400ms BootTimeout while cap-4 workers run for >750ms (well past 400ms + 250ms = 650ms).
        Task<OneShotRunResult> queuedRun = supervisor.RunAsync(new OneShotRunOptions
        {
            RevitVersion = 2026,
            AssemblyPath = _fakeAssemblyPath,
            TrxPath = Path.Combine(_tempRoot, "out", "queued_5th.trx"),
            BootTimeout = TimeSpan.FromMilliseconds(400),
            ExecutionTimeout = TimeSpan.FromSeconds(5)
        });

        await Task.Delay(750);
        Assert.That(queuedRun.IsCompleted, Is.False, "Queued 5th run must wait patiently while cap-4 slots are busy.");

        releaseFirst.TrySetResult(true);
        releaseRest.TrySetResult(true);

        OneShotRunResult fifthResult = await queuedRun.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.That(fifthResult.Success, Is.True, fifthResult.Error);
        OneShotRunResult[] satResults = await Task.WhenAll(saturatingRuns).WaitAsync(TimeSpan.FromSeconds(3));
        Assert.That(satResults.All(r => r.Success), Is.True);
    }

    [Test]
    public async Task AcquireWorkerAsync_ProtectedBootRetryExceedingBootTimeout_SucceedsWithoutPrematureQueueTimeout()
    {
        string pipeName = CreateUniquePipeName();
        string mutexPrefix = $@"Local\Synthetic_ProtectedBootRetry_{Guid.NewGuid():N}_";
        string journalsDir = Path.Combine(_tempRoot, "JournalsRetry");
        Directory.CreateDirectory(journalsDir);

        int launchAttempt = 0;
        using var launcher = new StubWorkerProcessLauncher((server, req) =>
        {
            int attempt = Interlocked.Increment(ref launchAttempt);
            server.InitialIsBusy = true;

            if (attempt == 1)
            {
                string jPath = Path.Combine(journalsDir, "journal.0001.txt");
                _ = Task.Run(async () =>
                {
                    await Task.Delay(80);
                    File.WriteAllText(
                        jPath,
                        $"' 0:< SYNTHETIC_WORKER_ID:{req.WorkerId}\n'Id : TaskDialog_External_Tools_External_Tool_Failure\n");
                });
            }
            else
            {
                server.AutoReadyDelay = TimeSpan.FromMilliseconds(120);
                server.ExecutionHandler = CreateDefaultPassingHandler();
            }
        });

        var bootOptions = new RevitWorkerPool.Startup.ProtectedWorkerBootOptions
        {
            MutexPrefix = mutexPrefix,
            JournalsDirectory = journalsDir,
            GateAcquireTimeout = TimeSpan.FromSeconds(5),
            GateHoldCap = TimeSpan.FromSeconds(5),
            PollInterval = TimeSpan.FromMilliseconds(15),
            MaxRetries = 1,
            UseStartupGate = true
        };

        var protector = new RevitWorkerPool.Startup.StartupBootProtector(
            new RevitWorkerPool.Startup.StartupModalWatchdog(
                processSnapshotProvider: Array.Empty<RevitWorkerPool.Startup.ProcessSnapshotEntry>,
                windowEnumerator: _ => Array.Empty<RevitWorkerPool.Startup.TopLevelDialogWindow>()),
            bootOptions);

        await using var supervisor = new WorkerPoolSupervisor(
            new SupervisorOptions
            {
                PipeName = pipeName,
                CurrentLatestVersion = 2026,
                IdleTimeout = TimeSpan.FromMinutes(25),
                TimingCacheDirectory = Path.Combine(_tempRoot, "timing_cache")
            },
            _discovery,
            launcher,
            new RevitPipeTestClientFactory(),
            bundleLeaseFactory: _ => new CountingDisposable(() => { }),
            startupProtector: protector);
        await using var ipcServer = StartServer(pipeName, supervisor);

        // Hold the Startup Gate externally for 600ms so 600ms gate wait + retry > 700ms (= 450ms + 250ms)!
        using var gateHeld = new ManualResetEventSlim(false);
        using var releaseGate = new ManualResetEventSlim(false);
        var holderTask = Task.Run(() =>
        {
            using var mutex = new Mutex(initiallyOwned: false, $"{mutexPrefix}2024");
            bool acquired = false;
            try
            {
                acquired = mutex.WaitOne(TimeSpan.FromSeconds(5));
                Assert.That(acquired, Is.True);
                gateHeld.Set();
                releaseGate.Wait(TimeSpan.FromSeconds(5));
            }
            finally
            {
                if (acquired)
                {
                    mutex.ReleaseMutex();
                }
            }
        });

        Assert.That(gateHeld.Wait(TimeSpan.FromSeconds(2)), Is.True);

        Task<OneShotRunResult> runTask = supervisor.RunAsync(new OneShotRunOptions
        {
            RevitVersion = 2024,
            AssemblyPath = _fakeAssemblyPath,
            TrxPath = Path.Combine(_tempRoot, "out", "protected_retry.trx"),
            BootTimeout = TimeSpan.FromMilliseconds(450),
            ExecutionTimeout = TimeSpan.FromSeconds(5),
            Shards = 1
        });

        await Task.Delay(600);
        releaseGate.Set();
        await holderTask;

        OneShotRunResult result = await runTask.WaitAsync(TimeSpan.FromSeconds(8));
        Assert.That(result.Success, Is.True, result.Error);
        Assert.That(result.WorkerIds.Single(), Does.EndWith("_retry1"));
        Assert.That(Volatile.Read(ref launchAttempt), Is.EqualTo(2));
    }

    [Test]
    public async Task Run_EnforcesHostWideCapOfFour_AcrossMixedVersions_AndEvictsIdleStandby()
    {
        string pipeName = CreateUniquePipeName();
        int peakAlive = 0;
        int currentActiveExecutions = 0;
        int peakActiveExecutions = 0;
        var fourActiveReached = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseExecutions = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var gateSync = new object();

        StubWorkerProcessLauncher? launcher = null;
        launcher = new StubWorkerProcessLauncher((server, _) =>
        {
            server.InitialIsBusy = false;
            server.ExecutionHandler = async (req, emitTest, ct) =>
            {
                int aliveNow = launcher!.Workers.Count(w => !w.IsTerminated);
                UpdateMax(ref peakAlive, aliveNow);

                int activeNow = Interlocked.Increment(ref currentActiveExecutions);
                UpdateMax(ref peakActiveExecutions, activeNow);
                if (activeNow >= 4)
                {
                    fourActiveReached.TrySetResult(true);
                }

                await releaseExecutions.Task.WaitAsync(ct).ConfigureAwait(false);
                Interlocked.Decrement(ref currentActiveExecutions);

                var test = new TestModel
                {
                    Name = "CapTest",
                    FullName = "Ns.Fixture.CapTest",
                    Success = true,
                    Time = 5.0
                };
                emitTest(test);
                return BuildAssemblyModel(req, test);
            };

            lock (gateSync)
            {
                int alive = launcher!.Workers.Count(w => !w.IsTerminated);
                UpdateMax(ref peakAlive, alive);
            }
        });

        using (launcher)
        await using (var supervisor = CreateSupervisor(pipeName, launcher, currentLatestVersion: 2026))
        await using (var ipcServer = StartServer(pipeName, supervisor))
        {
            SupervisorIpcResponse warm = await SupervisorIpcClient.SendAsync(
                pipeName,
                new SupervisorIpcRequest { Command = SupervisorIpcCommands.Warm, Version = 2026, Count = 2 },
                TimeSpan.FromSeconds(5));
            Assert.That(warm.Success, Is.True);

            Task<SupervisorIpcResponse>[] runs =
            {
                SendRunAsync(pipeName, 2026, Path.Combine(_tempRoot, "out", "mix1.trx")),
                SendRunAsync(pipeName, 2024, Path.Combine(_tempRoot, "out", "mix2.trx")),
                SendRunAsync(pipeName, 2024, Path.Combine(_tempRoot, "out", "mix3.trx")),
                SendRunAsync(pipeName, 2024, Path.Combine(_tempRoot, "out", "mix4.trx")),
                SendRunAsync(pipeName, 2026, Path.Combine(_tempRoot, "out", "mix5.trx")),
                SendRunAsync(pipeName, 2024, Path.Combine(_tempRoot, "out", "mix6.trx"))
            };

            await fourActiveReached.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.That(Volatile.Read(ref peakAlive), Is.LessThanOrEqualTo(4), "Host-wide resident workers must never exceed 4.");

            PoolStatusSnapshot saturatedStatus = (await SupervisorIpcClient.SendAsync(
                pipeName,
                new SupervisorIpcRequest { Command = SupervisorIpcCommands.Status },
                TimeSpan.FromSeconds(5))).Status!;
            Assert.That(saturatedStatus.TotalResident, Is.LessThanOrEqualTo(4));
            Assert.That(saturatedStatus.Active, Is.EqualTo(4));

            releaseExecutions.TrySetResult(true);
            SupervisorIpcResponse[] responses = await Task.WhenAll(runs);

            Assert.That(
                responses.All(r => r.Success && r.ExitCode == 0),
                Is.True,
                string.Join(" | ", responses.Select((r, idx) => $"[{idx}] exit={r.ExitCode} err={r.Error}")));
            Assert.That(Volatile.Read(ref peakAlive), Is.LessThanOrEqualTo(4));
            Assert.That(Volatile.Read(ref peakActiveExecutions), Is.EqualTo(4));

            PoolStatusSnapshot settled = await WaitForStatusAsync(
                pipeName,
                s => s.Active == 0 && s.Booting == 0 && s.ReadyStandby == 2);
            Assert.That(settled.Versions.ContainsKey(2024), Is.False);
            Assert.That(settled.Versions[2026].ReadyStandby, Is.EqualTo(2));
        }
    }

    [Test]
    public async Task Run_ReturnsExpectedExitCodes_ForPass_ZeroMatch_TestFailure_AndInfraFailures()
    {
        // 1. Test failure -> ExitCode 1; Zero match -> ExitCode 0.
        string pipe1 = CreateUniquePipeName();
        using (var launcher1 = new StubWorkerProcessLauncher((server, _) =>
        {
            server.InitialIsBusy = false;
            server.ExecutionHandler = (req, emitTest, _) =>
            {
                if (req.TestFilters is { Length: > 0 } && req.TestFilters[0].Contains("Zero"))
                {
                    return Task.FromResult(new TestAssemblyModel
                    {
                        FileName = req.TestPathFile ?? string.Empty,
                        Name = "Asm",
                        Success = true,
                        Tests = new List<TestTypeModel>()
                    });
                }

                var failTest = new TestModel
                {
                    Name = "Boom",
                    FullName = "Ns.Failing.Boom",
                    Success = false,
                    Message = "Boom failed",
                    Time = 10.0
                };
                emitTest(failTest);
                return Task.FromResult(BuildAssemblyModel(req, failTest));
            };
        }))
        await using (var sup1 = CreateSupervisor(pipe1, launcher1, currentLatestVersion: 2026))
        await using (var srv1 = StartServer(pipe1, sup1))
        {
            SupervisorIpcResponse failResp = await SendRunAsync(
                pipe1,
                2024,
                Path.Combine(_tempRoot, "out", "fail.trx"),
                shards: 1);
            Assert.That(failResp.ExitCode, Is.EqualTo(1));
            Assert.That(failResp.Success, Is.False);
            Assert.That(failResp.RunResult!.FailedTests, Is.EqualTo(1));
            Assert.That(launcher1.Workers.All(w => w.IsTerminated), Is.True);

            SupervisorIpcResponse zeroResp = await SendRunAsync(
                pipe1,
                2024,
                Path.Combine(_tempRoot, "out", "zero.trx"),
                filters: new[] { "ZeroMatch" });
            Assert.That(zeroResp.ExitCode, Is.EqualTo(0));
            Assert.That(zeroResp.Success, Is.True);
            Assert.That(zeroResp.RunResult!.ZeroMatches, Is.True);

            // Missing Revit & missing DLL -> ExitCode 2.
            SupervisorIpcResponse missingRevit = await SendRunAsync(pipe1, 2099, Path.Combine(_tempRoot, "out", "missingRevit.trx"));
            Assert.That(missingRevit.ExitCode, Is.EqualTo(2));
            Assert.That(missingRevit.Success, Is.False);

            SupervisorIpcResponse missingDll = await SendRunAsync(
                pipe1,
                2026,
                Path.Combine(_tempRoot, "out", "missingDll.trx"),
                assemblyPath: Path.Combine(_tempRoot, "DoesNotExist.dll"));
            Assert.That(missingDll.ExitCode, Is.EqualTo(2));
            Assert.That(missingDll.Success, Is.False);
        }

        // 2. Hung boot timeout -> ExitCode 2, worker terminated, no standby leaked.
        string pipe2 = CreateUniquePipeName();
        using (var hungLauncher = new StubWorkerProcessLauncher((server, _) =>
        {
            server.InitialIsBusy = true;
        }))
        await using (var sup2 = CreateSupervisor(pipe2, hungLauncher, currentLatestVersion: 2026))
        await using (var srv2 = StartServer(pipe2, sup2))
        {
            SupervisorIpcResponse hungResp = await SendRunAsync(
                pipe2,
                2026,
                Path.Combine(_tempRoot, "out", "hung.trx"),
                bootTimeoutSeconds: 0.1,
                shards: 1);

            Assert.That(hungResp.ExitCode, Is.EqualTo(2));
            Assert.That(hungResp.Success, Is.False);
            Assert.That(hungResp.Error, Does.Contain("IsBusy == false"));
            Assert.That(hungLauncher.Workers.Count, Is.EqualTo(1));
            Assert.That(hungLauncher.Workers.Single().IsTerminated, Is.True);
            Assert.That(sup2.GetStatus().TotalResident, Is.EqualTo(0));
        }

        // 3. Process launch exception -> ExitCode 2.
        string pipe3 = CreateUniquePipeName();
        var throwingLauncher = new ThrowingWorkerProcessLauncher();
        await using (var sup3 = CreateSupervisor(pipe3, throwingLauncher, currentLatestVersion: 2026))
        await using (var srv3 = StartServer(pipe3, sup3))
        {
            SupervisorIpcResponse launchErrResp = await SendRunAsync(
                pipe3,
                2026,
                Path.Combine(_tempRoot, "out", "launchfail.trx"));

            Assert.That(launchErrResp.ExitCode, Is.EqualTo(2));
            Assert.That(launchErrResp.Success, Is.False);
            Assert.That(launchErrResp.Error, Does.Contain("Simulated launch failure"));
        }
    }

    [Test]
    public async Task IdleStandbyDisconnect_EvictsDeadWorkerAndReplenishes()
    {
        string pipeName = CreateUniquePipeName();
        using var launcher = CreatePassingLauncher();
        await using var supervisor = CreateSupervisor(pipeName, launcher, currentLatestVersion: 2026);
        await using var ipcServer = StartServer(pipeName, supervisor);

        WarmResult warm = await supervisor.WarmAsync(version: 2026, count: 1);
        Assert.That(warm.Success, Is.True);

        StubWorkerProcessLauncher.StubWorkerRecord firstWorker = launcher.Workers.Single();
        firstWorker.Terminate();

        PoolStatusSnapshot recovered = await WaitForStatusAsync(
            pipeName,
            s => s.ReadyStandby == 1 && s.Booting == 0 && launcher.Workers.Count >= 2);
        Assert.That(recovered.ReadyStandby, Is.EqualTo(1));

        StubWorkerProcessLauncher.StubWorkerRecord replacement = launcher.Workers.Single(w => !w.IsTerminated);
        Assert.That(replacement.WorkerId, Is.Not.EqualTo(firstWorker.WorkerId));
    }

    [Test]
    public async Task IdleTtl_ScalesToZeroOnlyAfterActiveWorkCompletes()
    {
        string pipeName = CreateUniquePipeName();
        var manualClock = new ManualTimeProvider(new DateTimeOffset(2026, 11, 24, 12, 0, 0, TimeSpan.Zero));
        var execStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var execRelease = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        int leaseDisposed = 0;
        using var launcher = new StubWorkerProcessLauncher((server, _) =>
        {
            server.InitialIsBusy = false;
            server.ExecutionHandler = async (req, emitTest, ct) =>
            {
                execStarted.TrySetResult(true);
                await execRelease.Task.WaitAsync(ct).ConfigureAwait(false);
                var t = new TestModel { Name = "Slow", FullName = "Ns.F.Slow", Success = true, Time = 1.0 };
                emitTest(t);
                return BuildAssemblyModel(req, t);
            };
        });

        await using var supervisor = new WorkerPoolSupervisor(
            new SupervisorOptions
            {
                PipeName = pipeName,
                CurrentLatestVersion = 2026,
                IdleTimeout = TimeSpan.FromMinutes(25),
                TimingCacheDirectory = Path.Combine(_tempRoot, "timing_cache")
            },
            _discovery,
            launcher,
            new RevitPipeTestClientFactory(),
            bundleLeaseFactory: _ => new CountingDisposable(() => Interlocked.Increment(ref leaseDisposed)),
            timeProvider: manualClock);

        WarmResult warm = await supervisor.WarmAsync(2026, 2);
        Assert.That(warm.Success, Is.True);

        // 20 minutes idle (< 25 min): pool remains warm.
        manualClock.Advance(TimeSpan.FromMinutes(20));
        Assert.That(supervisor.HasShutdownCompleted, Is.False);
        Assert.That(supervisor.GetStatus().ReadyStandby, Is.EqualTo(2));

        // Start active run and advance 30 minutes while executing: must NOT scale down mid-run.
        Task<OneShotRunResult> activeRunTask = supervisor.RunAsync(new OneShotRunOptions
        {
            RevitVersion = 2026,
            AssemblyPath = _fakeAssemblyPath,
            TrxPath = Path.Combine(_tempRoot, "out", "ttl.trx"),
            Shards = 1
        });
        await execStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        manualClock.Advance(TimeSpan.FromMinutes(30));
        Assert.That(supervisor.HasShutdownCompleted, Is.False);
        Assert.That(supervisor.GetStatus().Active, Is.EqualTo(1));

        execRelease.TrySetResult(true);
        OneShotRunResult runResult = await activeRunTask;
        Assert.That(runResult.Success, Is.True);

        // 24 minutes after active work finished (< 25 min): still alive.
        manualClock.Advance(TimeSpan.FromMinutes(24));
        Assert.That(supervisor.HasShutdownCompleted, Is.False);

        // Cross 25 minute idle boundary: drains cleanly to zero workers and disposes bundle lease.
        manualClock.Advance(TimeSpan.FromMinutes(2));
        await supervisor.WaitForShutdownAsync().WaitAsync(TimeSpan.FromSeconds(2));

        Assert.That(supervisor.HasShutdownCompleted, Is.True);
        Assert.That(supervisor.GetStatus().TotalResident, Is.EqualTo(0));
        Assert.That(launcher.Workers.All(w => w.IsTerminated), Is.True);
        Assert.That(Volatile.Read(ref leaseDisposed), Is.EqualTo(1));
    }

    [Test]
    public async Task SingletonMutex_AllowsAtMostOneSupervisorSpawnUnderConcurrentCallers()
    {
        string pipeName = CreateUniquePipeName();
        int spawnCount = 0;
        SupervisorIpcServer? server = null;

        try
        {
            var launchOptions = new SupervisorClientLaunchOptions
            {
                PipeName = pipeName,
                StartupTimeout = TimeSpan.FromSeconds(5)
            };

            Task[] callers = Enumerable.Range(0, 8)
                .Select(_ => SupervisorIpcClient.EnsureSupervisorRunningAsync(
                    launchOptions,
                    spawnSupervisorAction: (_, _) =>
                    {
                        Interlocked.Increment(ref spawnCount);
                        server = new SupervisorIpcServer(
                            pipeName,
                            (req, _) => Task.FromResult(new SupervisorIpcResponse
                            {
                                RequestId = req.RequestId,
                                Success = true,
                                ExitCode = 0,
                                Status = new PoolStatusSnapshot
                                {
                                    Running = true,
                                    PipeName = pipeName,
                                    ActiveCap = 4
                                }
                            }));
                        server.Start();
                    }))
                .ToArray();

            await Task.WhenAll(callers).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.That(Volatile.Read(ref spawnCount), Is.EqualTo(1));
        }
        finally
        {
            if (server is not null)
            {
                await server.DisposeAsync();
            }
        }
    }

    [Test]
    public async Task IpcServer_ClientCancellation_CancelsRunAndTerminatesWorker()
    {
        string pipeName = CreateUniquePipeName();
        var execStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var serverSawCancel = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        using var launcher = new StubWorkerProcessLauncher((server, _) =>
        {
            server.InitialIsBusy = false;
            server.ExecutionHandler = async (req, _, ct) =>
            {
                execStarted.TrySetResult(true);
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(10), ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    serverSawCancel.TrySetResult(true);
                    throw;
                }

                return BuildAssemblyModel(req);
            };
        });

        await using var supervisor = CreateSupervisor(pipeName, launcher, currentLatestVersion: 2026);
        await using var ipcServer = StartServer(pipeName, supervisor);

        using var clientCts = new CancellationTokenSource();
        Task<SupervisorIpcResponse> runTask = SendRunAsync(
            pipeName,
            2024,
            Path.Combine(_tempRoot, "out", "cancelled.trx"),
            cancellationToken: clientCts.Token);

        await execStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        clientCts.Cancel();

        try
        {
            await runTask;
        }
        catch (OperationCanceledException)
        {
        }

        await serverSawCancel.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await WaitForStatusAsync(pipeName, s => s.TotalResident == 0);
        Assert.That(launcher.Workers.All(w => w.IsTerminated), Is.True);
    }

    [Test]
    public async Task IdleTtl_RealShortTimeSpan_ScalesToZeroCleanly()
    {
        string pipeName = CreateUniquePipeName();
        int leaseDisposed = 0;
        using var launcher = CreatePassingLauncher();

        await using var supervisor = new WorkerPoolSupervisor(
            new SupervisorOptions
            {
                PipeName = pipeName,
                CurrentLatestVersion = 2026,
                IdleTimeout = TimeSpan.FromMilliseconds(50),
                TimingCacheDirectory = Path.Combine(_tempRoot, "timing_cache")
            },
            _discovery,
            launcher,
            new RevitPipeTestClientFactory(),
            bundleLeaseFactory: _ => new CountingDisposable(() => Interlocked.Increment(ref leaseDisposed)));

        WarmResult warm = await supervisor.WarmAsync(2026, 1);
        Assert.That(warm.Success, Is.True);
        Assert.That(supervisor.GetStatus().ReadyStandby, Is.EqualTo(1));

        await supervisor.WaitForShutdownAsync().WaitAsync(TimeSpan.FromSeconds(3));
        Assert.That(supervisor.HasShutdownCompleted, Is.True);
        Assert.That(supervisor.GetStatus().TotalResident, Is.EqualTo(0));
        Assert.That(launcher.Workers.All(w => w.IsTerminated), Is.True);
        Assert.That(Volatile.Read(ref leaseDisposed), Is.EqualTo(1));
    }

    [Test]
    public async Task Cli_StatusAndDrain_WhenSupervisorStopped_ReturnZeroWithoutAutoStart()
    {
        string unusedPipe = CreateUniquePipeName();
        using var capture = new StringWriter();
        using var errCapture = new StringWriter();

        int statusCode = await Program.RunCliAsync(
            new[] { "status", "--pipe-name", unusedPipe, "--json" },
            stdout: capture,
            stderr: errCapture);
        int drainCode = await Program.RunCliAsync(
            new[] { "drain", "--pipe-name", unusedPipe, "--json" },
            stdout: capture,
            stderr: errCapture);

        string output = capture.ToString();
        Assert.That(statusCode, Is.EqualTo(0));
        Assert.That(drainCode, Is.EqualTo(0));
        Assert.That(errCapture.ToString(), Is.Empty);
        Assert.That(output, Does.Contain("\"running\":false"));
        Assert.That(output, Does.Contain("\"terminatedWorkers\":0"));
        Assert.That(await SupervisorIpcClient.TryPingAsync(unusedPipe, TimeSpan.FromMilliseconds(50)), Is.Null);
    }

    [Test]
    public async Task Supervisor_PropagatesUsesCleanShim_AndFailsClosedWithExitCode2OnShimError()
    {
        string pipe1 = CreateUniquePipeName();
        using var shimmedStub = new StubWorkerProcessLauncher((server, _) =>
        {
            server.InitialIsBusy = false;
            server.ExecutionHandler = CreateDefaultPassingHandler();
        }, usesCleanShim: true);

        await using (var sup = CreateSupervisor(pipe1, shimmedStub, currentLatestVersion: 2026))
        {
            WarmResult warm = await sup.WarmAsync(2026, 1);
            Assert.That(warm.Success, Is.True);
            Assert.That(warm.Status!.ShimEnabled, Is.True);

            OneShotRunResult run = await sup.RunAsync(new OneShotRunOptions
            {
                RevitVersion = 2026,
                AssemblyPath = _fakeAssemblyPath,
                TrxPath = Path.Combine(_tempRoot, "out", "shim_sup.trx")
            });
            Assert.That(run.Success, Is.True);
            Assert.That(run.ShimEnabled, Is.True);
        }

        string pipe2 = CreateUniquePipeName();
        var missingShimLauncher = new CleanDetoursProcessLauncher(
            shimDllPath: Path.Combine(_tempRoot, "MissingShim.dll"),
            launcherDllPath: Path.Combine(_tempRoot, "MissingLauncher.dll"));

        await using (var supFail = CreateSupervisor(pipe2, missingShimLauncher, currentLatestVersion: 2026))
        {
            OneShotRunResult failRun = await supFail.RunAsync(new OneShotRunOptions
            {
                RevitVersion = 2026,
                AssemblyPath = _fakeAssemblyPath,
                TrxPath = Path.Combine(_tempRoot, "out", "shim_fail.trx")
            });
            Assert.That(failRun.Success, Is.False);
            Assert.That(failRun.ExitCode, Is.EqualTo(2));
            Assert.That(failRun.ShimEnabled, Is.True);
            Assert.That(failRun.Error, Does.Contain("MissingShim.dll"));
        }
    }

    [Test]
    public async Task Run_OverIpc_Ecma335CategoryAndFilter_ShortCircuitsZeroMatchBeforeConsumingStandby()
    {
        string pipeName = CreateUniquePipeName();
        using var launcher = CreatePassingLauncher();
        await using var supervisor = CreateSupervisor(pipeName, launcher, currentLatestVersion: 2026);
        await using var ipcServer = StartServer(pipeName, supervisor);

        WarmResult warm = await supervisor.WarmAsync(2026, 2);
        Assert.That(warm.Success, Is.True);
        Assert.That(warm.ReadyCount, Is.EqualTo(2));
        int workerCountAfterWarm = launcher.Workers.Count;

        // 1. Zero-match category short-circuits BEFORE consuming any standby worker.
        string zeroTrx = Path.Combine(_tempRoot, "out", "sup_zero_cat.trx");
        SupervisorIpcResponse zeroCatResp = await SendRunAsync(
            pipeName,
            2026,
            zeroTrx,
            category: "NonExistentCategory");

        Assert.That(zeroCatResp.Success, Is.True, zeroCatResp.Error);
        Assert.That(zeroCatResp.ExitCode, Is.EqualTo(0));
        Assert.That(zeroCatResp.RunResult, Is.Not.Null);
        Assert.That(zeroCatResp.RunResult!.ZeroMatches, Is.True);
        Assert.That(zeroCatResp.RunResult.TotalTests, Is.EqualTo(0));
        Assert.That(zeroCatResp.RunResult.WorkerIds, Is.Empty);
        Assert.That(zeroCatResp.RunResult.ShardCount, Is.EqualTo(0));
        Assert.That(File.Exists(zeroTrx), Is.True);

        PoolStatusSnapshot statusAfterZero = supervisor.GetStatus();
        Assert.That(statusAfterZero.ReadyStandby, Is.EqualTo(2), "Zero-match must not consume standby workers.");
        Assert.That(launcher.Workers.Count, Is.EqualTo(workerCountAfterWarm));
        Assert.That(launcher.Workers.All(w => !w.IsTerminated), Is.True);

        // 2. Corrupt assembly + category/filter returns ExitCode=2 without consuming standby worker.
        string corruptDll = Path.Combine(_tempRoot, "Corrupt.RevitTests.dll");
        File.WriteAllText(corruptDll, "not-a-valid-pe");
        SupervisorIpcResponse corruptResp = await SendRunAsync(
            pipeName,
            2026,
            Path.Combine(_tempRoot, "out", "sup_corrupt.trx"),
            assemblyPath: corruptDll,
            category: "RevitSmoke");

        Assert.That(corruptResp.Success, Is.False);
        Assert.That(corruptResp.ExitCode, Is.EqualTo(2));
        Assert.That(corruptResp.Error, Does.Contain("Failed to inspect test assembly metadata"));
        Assert.That(supervisor.GetStatus().ReadyStandby, Is.EqualTo(2));

        // 3. Matching category + filter consumes 1 standby worker and passes exact RicaunTestFilters.
        string matchTrx = Path.Combine(_tempRoot, "out", "sup_match_cat.trx");
        SupervisorIpcResponse matchResp = await SendRunAsync(
            pipeName,
            2026,
            matchTrx,
            filters: new[] { "Fixture" },
            category: "RevitSmoke");

        Assert.That(matchResp.Success, Is.True, matchResp.Error);
        Assert.That(matchResp.ExitCode, Is.EqualTo(0));
        Assert.That(matchResp.RunResult!.ZeroMatches, Is.False);
        Assert.That(matchResp.RunResult.TotalTests, Is.EqualTo(1));

        string consumedWorkerId = matchResp.RunResult.WorkerIds.Single();
        StubWorkerProcessLauncher.StubWorkerRecord consumedWorker =
            launcher.Workers.Single(w => w.WorkerId == consumedWorkerId);
        Assert.That(consumedWorker.IsTerminated, Is.True);
        Assert.That(
            consumedWorker.Server.ReceivedRequests.Single().TestFilters,
            Is.EqualTo(new[] { "Ns.Fixture.SmokeTest.*" }));

        await WaitForStatusAsync(pipeName, s => s.ReadyStandby == 2 && s.Booting == 0 && s.Active == 0);

        // 4. IPC payload Categories[] (without singular Category) expands multiple categories.
        string multiCatTrx = Path.Combine(_tempRoot, "out", "sup_multi_cat.trx");
        SupervisorIpcResponse multiCatResp = await SendRunAsync(
            pipeName,
            2026,
            multiCatTrx,
            categories: new[] { "RevitSmoke", "RevitAdapter" },
            shards: 1);

        Assert.That(multiCatResp.Success, Is.True, multiCatResp.Error);
        Assert.That(multiCatResp.ExitCode, Is.EqualTo(0));
        string multiCatWorkerId = multiCatResp.RunResult!.WorkerIds.Single();
        StubWorkerProcessLauncher.StubWorkerRecord multiCatWorker =
            launcher.Workers.Single(w => w.WorkerId == multiCatWorkerId);
        Assert.That(multiCatWorker.IsTerminated, Is.True);
        Assert.That(
            multiCatWorker.Server.ReceivedRequests.Single().TestFilters,
            Is.EqualTo(new[] { "Ns.Failing.Boom.*", "Ns.Fixture.SmokeTest.*" }));

        await WaitForStatusAsync(pipeName, s => s.ReadyStandby == 2 && s.Booting == 0 && s.Active == 0);
    }

    private WorkerPoolSupervisor CreateSupervisor(
        string pipeName,
        IWorkerProcessLauncher launcher,
        int currentLatestVersion,
        TimeProvider? timeProvider = null)
    {
        return new WorkerPoolSupervisor(
            new SupervisorOptions
            {
                PipeName = pipeName,
                CurrentLatestVersion = currentLatestVersion,
                IdleTimeout = TimeSpan.FromMinutes(25),
                TimingCacheDirectory = Path.Combine(_tempRoot, "timing_cache")
            },
            _discovery,
            launcher,
            new RevitPipeTestClientFactory(),
            bundleLeaseFactory: _ => new CountingDisposable(static () => { }),
            timeProvider: timeProvider);
    }

    private static SupervisorIpcServer StartServer(string pipeName, WorkerPoolSupervisor supervisor)
    {
        var server = new SupervisorIpcServer(
            pipeName,
            (req, ct) => supervisor.HandleIpcRequestAsync(req, ct));
        supervisor.AttachIpcServer(server);
        supervisor.Start();
        server.Start();
        return server;
    }

    private Task<SupervisorIpcResponse> SendRunAsync(
        string pipeName,
        int version,
        string trxPath,
        string? assemblyPath = null,
        IReadOnlyList<string>? filters = null,
        string? category = null,
        IReadOnlyList<string>? categories = null,
        double bootTimeoutSeconds = 5,
        int? shards = null,
        CancellationToken cancellationToken = default)
    {
        return SupervisorIpcClient.SendAsync(
            pipeName,
            new SupervisorIpcRequest
            {
                Command = SupervisorIpcCommands.Run,
                Run = new SupervisorRunPayload
                {
                    Version = version,
                    AssemblyPath = assemblyPath ?? _fakeAssemblyPath,
                    TrxPath = trxPath,
                    Filters = filters ?? Array.Empty<string>(),
                    Category = category,
                    Categories = categories ?? Array.Empty<string>(),
                    BootTimeoutSeconds = bootTimeoutSeconds,
                    ExecutionTimeoutSeconds = 10,
                    Shards = shards
                }
            },
            TimeSpan.FromSeconds(5),
            cancellationToken);
    }

    private static async Task<PoolStatusSnapshot> WaitForStatusAsync(
        string pipeName,
        Func<PoolStatusSnapshot, bool> predicate)
    {
        DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
        while (DateTime.UtcNow < deadline)
        {
            SupervisorIpcResponse resp = await SupervisorIpcClient.SendAsync(
                pipeName,
                new SupervisorIpcRequest { Command = SupervisorIpcCommands.Status },
                TimeSpan.FromSeconds(2));

            if (resp.Status is not null && predicate(resp.Status))
            {
                return resp.Status;
            }

            await Task.Delay(15);
        }

        SupervisorIpcResponse last = await SupervisorIpcClient.SendAsync(
            pipeName,
            new SupervisorIpcRequest { Command = SupervisorIpcCommands.Status },
            TimeSpan.FromSeconds(2));
        Assert.Fail($"Timed out waiting for pool status predicate. Last: booting={last.Status?.Booting}, readyStandby={last.Status?.ReadyStandby}, active={last.Status?.Active}, totalResident={last.Status?.TotalResident}");
        return last.Status!;
    }

    private static StubWorkerProcessLauncher CreatePassingLauncher()
    {
        return new StubWorkerProcessLauncher((server, _) =>
        {
            server.InitialIsBusy = true;
            server.AutoReadyDelay = TimeSpan.FromMilliseconds(5);
            server.ExecutionHandler = CreateDefaultPassingHandler();
        });
    }

    private static Func<TestRequest, Action<TestModel>, CancellationToken, Task<TestAssemblyModel>> CreateDefaultPassingHandler()
    {
        return (req, emitTest, _) =>
        {
            var test = new TestModel
            {
                Name = "SmokeTest",
                FullName = "Ns.Fixture.SmokeTest",
                Success = true,
                Time = 12.0
            };
            emitTest(test);
            return Task.FromResult(BuildAssemblyModel(req, test));
        };
    }

    private static TestAssemblyModel BuildAssemblyModel(TestRequest req, params TestModel[] tests)
    {
        return new TestAssemblyModel
        {
            FileName = req.TestPathFile ?? string.Empty,
            Name = "Synthetic.RevitTests",
            Success = tests.All(t => t.Success || t.Skipped),
            Tests = tests.Length == 0
                ? new List<TestTypeModel>()
                : new List<TestTypeModel>
                {
                    new()
                    {
                        Name = "Fixture",
                        FullName = "Ns.Fixture",
                        Success = tests.All(t => t.Success || t.Skipped),
                        Tests = tests.ToList()
                    }
                }
        };
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

    private static string CreateUniquePipeName()
    {
        return $"Synthetic_PoolTest_{Guid.NewGuid():N}";
    }

    private sealed class CountingDisposable : IDisposable
    {
        private readonly Action _onDispose;
        private int _disposed;

        public CountingDisposable(Action onDispose)
        {
            _onDispose = onDispose;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                _onDispose();
            }
        }
    }

    private sealed class ThrowingWorkerProcessLauncher : IWorkerProcessLauncher
    {
        public bool UsesCleanShim => false;

        public Task<LaunchedWorkerProcess> LaunchAsync(
            WorkerLaunchRequest request,
            CancellationToken cancellationToken = default)
        {
            return Task.FromException<LaunchedWorkerProcess>(
                new InvalidOperationException("Simulated launch failure."));
        }
    }

    private sealed class CountingThrowingWorkerProcessLauncher : IWorkerProcessLauncher
    {
        private int _launchAttempts;

        public bool UsesCleanShim => false;

        public int LaunchAttempts => Volatile.Read(ref _launchAttempts);

        public Task<LaunchedWorkerProcess> LaunchAsync(
            WorkerLaunchRequest request,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _launchAttempts);
            return Task.FromException<LaunchedWorkerProcess>(
                new InvalidOperationException("Simulated launch failure."));
        }
    }

    private sealed class DelegatingWorkerProcessLauncher : IWorkerProcessLauncher
    {
        private readonly Func<WorkerLaunchRequest, CancellationToken, Task<LaunchedWorkerProcess>> _launchAsync;

        public DelegatingWorkerProcessLauncher(
            bool usesCleanShim,
            Func<WorkerLaunchRequest, CancellationToken, Task<LaunchedWorkerProcess>> launchAsync)
        {
            UsesCleanShim = usesCleanShim;
            _launchAsync = launchAsync;
        }

        public bool UsesCleanShim { get; }

        public Task<LaunchedWorkerProcess> LaunchAsync(
            WorkerLaunchRequest request,
            CancellationToken cancellationToken = default)
        {
            return _launchAsync(request, cancellationToken);
        }
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private readonly object _sync = new();
        private readonly List<ManualTimer> _timers = new();
        private DateTimeOffset _utcNow;

        public ManualTimeProvider(DateTimeOffset initialUtc)
        {
            _utcNow = initialUtc;
        }

        public override DateTimeOffset GetUtcNow()
        {
            lock (_sync)
            {
                return _utcNow;
            }
        }

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new ManualTimer(this, callback, state);
            lock (_sync)
            {
                _timers.Add(timer);
                timer.SetDueLocked(_utcNow, dueTime);
            }

            return timer;
        }

        public void Advance(TimeSpan delta)
        {
            List<(TimerCallback Callback, object? State)> due = new();
            lock (_sync)
            {
                _utcNow += delta;
                foreach (ManualTimer timer in _timers.ToArray())
                {
                    if (!timer.Disposed && timer.DueUtc is DateTimeOffset dueAt && dueAt <= _utcNow)
                    {
                        timer.DueUtc = null;
                        due.Add((timer.Callback, timer.State));
                    }
                }
            }

            foreach ((TimerCallback callback, object? state) in due)
            {
                callback(state);
            }
        }

        private sealed class ManualTimer : ITimer
        {
            private readonly ManualTimeProvider _provider;

            public ManualTimer(ManualTimeProvider provider, TimerCallback callback, object? state)
            {
                _provider = provider;
                Callback = callback;
                State = state;
            }

            public TimerCallback Callback { get; }

            public object? State { get; }

            public DateTimeOffset? DueUtc { get; set; }

            public bool Disposed { get; private set; }

            public void SetDueLocked(DateTimeOffset now, TimeSpan dueTime)
            {
                DueUtc = dueTime == Timeout.InfiniteTimeSpan
                    ? null
                    : (dueTime <= TimeSpan.Zero ? now : now + dueTime);
            }

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                lock (_provider._sync)
                {
                    if (Disposed)
                    {
                        return false;
                    }

                    SetDueLocked(_provider._utcNow, dueTime);
                    return true;
                }
            }

            public void Dispose()
            {
                lock (_provider._sync)
                {
                    Disposed = true;
                    DueUtc = null;
                    _provider._timers.Remove(this);
                }
            }

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }
}
