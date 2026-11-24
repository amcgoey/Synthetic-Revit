using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using RevitWorkerPool.Discovery;
using RevitWorkerPool.Execution;
using RevitWorkerPool.Launching;
using RevitWorkerPool.Pipe;
using RevitWorkerPool.Startup;
using RevitWorkerPool.Windowing;
using ricaun.NUnit.Models;

namespace RevitWorkerPool.Tests;

[TestFixture]
[SupportedOSPlatform("windows")]
public class WindowingAndStartupGateTests
{
    private const uint WsOverlappedWindow = 0x00CF0000;
    private const int SwShowNa = 8;

    private string _tempRoot = string.Empty;
    private string _guiFocusProbeExe = string.Empty;
    private string _fakeAssemblyPath = string.Empty;
    private RevitInstallationDiscovery _discovery = null!;

    [SetUp]
    public void SetUp()
    {
        _tempRoot = Path.Combine(
            Path.GetTempPath(),
            "RevitWorkerPool_WindowingStartupTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);

        string revit2026Dir = Path.Combine(_tempRoot, "Autodesk", "Revit 2026");
        Directory.CreateDirectory(revit2026Dir);
        File.WriteAllText(Path.Combine(revit2026Dir, "Revit.exe"), string.Empty);

        _fakeAssemblyPath = Path.Combine(_tempRoot, "Synthetic.RevitTests.2026.dll");
        File.WriteAllText(_fakeAssemblyPath, string.Empty);

        _discovery = new RevitInstallationDiscovery(
            () => new[] { Path.Combine(_tempRoot, "Autodesk") },
            () => Array.Empty<UninstallRegistryEntry>());

        _guiFocusProbeExe = ResolveGuiFocusProbeExecutable();
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
    public async Task OffScreenWindowingLauncher_MovesProbeOffScreenWithNoActivate_PreservesVisibilityAndForegroundWindow()
    {
        string statusFile = Path.Combine(_tempRoot, "probe_status.txt");
        string exitEventName = $@"Local\GuiFocusProbe_Exit_{Guid.NewGuid():N}";
        using var exitEvent = new EventWaitHandle(false, EventResetMode.ManualReset, exitEventName);

        using var tracker = new OffScreenWindowTracker();
        using var launcher = new OffScreenWindowingLauncher(new StandardProcessLauncher(), tracker);

        IntPtr foregroundBefore = NativeMethods.GetForegroundWindow();
        bool trackedBeforeResume = false;

        var request = new WorkerLaunchRequest
        {
            RevitVersion = 2026,
            ExecutablePath = _guiFocusProbeExe,
            Arguments = $"--mode window --title \"FocusProbe_{Guid.NewGuid():N}\" --status-file \"{statusFile}\" --exit-event \"{exitEventName}\"",
            WorkerId = $"w_probe_{Guid.NewGuid():N}",
            ProjectRoot = _tempRoot,
            OnProcessCreatedBeforeResume = pid => trackedBeforeResume = tracker.IsTracked(pid)
        };

        LaunchedWorkerProcess worker = await launcher.LaunchAsync(request);
        try
        {
            Assert.That(trackedBeforeResume, Is.True, "PID must be registered before ResumeThread.");
            Assert.That(tracker.IsTracked(worker.ProcessId), Is.True);

            IntPtr probeHwnd = IntPtr.Zero;
            NativeMethods.Rect probeRect = default;
            var sw = Stopwatch.StartNew();
            while (sw.Elapsed < TimeSpan.FromSeconds(3))
            {
                probeHwnd = FindFirstVisibleTopLevelWindowForPid(worker.ProcessId);
                if (probeHwnd != IntPtr.Zero &&
                    NativeMethods.GetWindowRect(probeHwnd, out probeRect) &&
                    probeRect.Left == OffScreenWindowTracker.DefaultOffScreenX &&
                    probeRect.Top == OffScreenWindowTracker.DefaultOffScreenY)
                {
                    break;
                }

                await Task.Delay(15);
            }

            Assert.That(probeHwnd, Is.Not.EqualTo(IntPtr.Zero), "GuiFocusProbe top-level window did not appear.");
            Assert.That(NativeMethods.IsWindowVisible(probeHwnd), Is.True, "Window must remain visible (IsWindowVisible == true).");
            Assert.That(NativeMethods.IsIconic(probeHwnd), Is.False, "Window should be moved off-screen, not minimized.");
            Assert.That(probeRect.Left, Is.EqualTo(OffScreenWindowTracker.DefaultOffScreenX));
            Assert.That(probeRect.Top, Is.EqualTo(OffScreenWindowTracker.DefaultOffScreenY));
            Assert.That(probeRect.Right - probeRect.Left, Is.EqualTo(320));
            Assert.That(probeRect.Bottom - probeRect.Top, Is.EqualTo(240));

            IntPtr foregroundDuring = NativeMethods.GetForegroundWindow();
            Assert.That(foregroundDuring, Is.EqualTo(foregroundBefore), "GetForegroundWindow() must not change.");
            Assert.That(foregroundDuring, Is.Not.EqualTo(probeHwnd));

            // Strict PID scoping: an untracked window in this test process must stay at (120, 120).
            IntPtr untrackedHwnd = CreateAndShowUntrackedLocalWindow(120, 120, 200, 150);
            try
            {
                PumpCurrentThreadMessages(30);
                Assert.That(NativeMethods.GetWindowRect(untrackedHwnd, out NativeMethods.Rect localRect), Is.True);
                Assert.That(localRect.Left, Is.EqualTo(120));
                Assert.That(localRect.Top, Is.EqualTo(120));
            }
            finally
            {
                NativeMethods.DestroyWindow(untrackedHwnd);
            }

            if (File.Exists(statusFile))
            {
                string statusText = File.ReadAllText(statusFile);
                Assert.That(statusText, Does.Contain("IsWindowVisible=True"));
            }

            exitEvent.Set();
            await worker.WaitForExitAsync();

            Assert.That(tracker.IsTracked(worker.ProcessId), Is.False, "Tracked PID must be removed upon process exit.");
        }
        finally
        {
            exitEvent.Set();
            await worker.DisposeAsync();
            Assert.That(tracker.IsTracked(worker.ProcessId), Is.False);
        }
    }

    [Test]
    public async Task OffScreenWindowingLauncher_ComposesOverStubAndUntracksOnInnerLaunchFailure()
    {
        using var tracker = new OffScreenWindowTracker();
        using var stub = new StubWorkerProcessLauncher();
        using var launcher = new OffScreenWindowingLauncher(stub, tracker);

        bool innerHookCalled = false;
        var req = new WorkerLaunchRequest
        {
            RevitVersion = 2026,
            ExecutablePath = _guiFocusProbeExe,
            WorkerId = "w_stub_compose",
            OnProcessCreatedBeforeResume = _ => innerHookCalled = true
        };

        LaunchedWorkerProcess worker = await launcher.LaunchAsync(req);
        Assert.That(innerHookCalled, Is.True);
        Assert.That(tracker.IsTracked(worker.ProcessId), Is.True);

        worker.Dispose();
        Assert.That(tracker.IsTracked(worker.ProcessId), Is.False);

        // Failure cleanup check
        var failingInner = new ThrowingBeforeResumeLauncher(pid: 88991);
        using var failingDecorator = new OffScreenWindowingLauncher(failingInner, tracker);
        Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await failingDecorator.LaunchAsync(new WorkerLaunchRequest
            {
                RevitVersion = 2026,
                ExecutablePath = _guiFocusProbeExe
            }));
        Assert.That(tracker.IsTracked(88991), Is.False);
    }

    [Test]
    public async Task OffScreenWindowTracker_InitialSweepMovesExistingOnScreenProbeOffScreen()
    {
        string exitEventName = $@"Local\GuiFocusProbe_SweepExit_{Guid.NewGuid():N}";
        using var exitEvent = new EventWaitHandle(false, EventResetMode.ManualReset, exitEventName);

        var plainLauncher = new StandardProcessLauncher();
        IntPtr foregroundBefore = NativeMethods.GetForegroundWindow();

        await using LaunchedWorkerProcess worker = await plainLauncher.LaunchAsync(new WorkerLaunchRequest
        {
            RevitVersion = 2026,
            ExecutablePath = _guiFocusProbeExe,
            Arguments = $"--mode window --title \"ControlProbe_{Guid.NewGuid():N}\" --exit-event \"{exitEventName}\"",
            WorkerId = "w_control_sweep",
            ProjectRoot = _tempRoot
        });

        IntPtr probeHwnd = IntPtr.Zero;
        NativeMethods.Rect initialRect = default;
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < TimeSpan.FromSeconds(3))
        {
            probeHwnd = FindFirstVisibleTopLevelWindowForPid(worker.ProcessId);
            if (probeHwnd != IntPtr.Zero && NativeMethods.GetWindowRect(probeHwnd, out initialRect))
            {
                break;
            }

            await Task.Delay(15);
        }

        Assert.That(probeHwnd, Is.Not.EqualTo(IntPtr.Zero));
        Assert.That(initialRect.Left, Is.EqualTo(100), "Control probe without tracker should start on-screen at x=100.");
        Assert.That(initialRect.Top, Is.EqualTo(100), "Control probe without tracker should start on-screen at y=100.");

        using var tracker = new OffScreenWindowTracker();
        tracker.TrackProcess(worker.ProcessId);

        Assert.That(NativeMethods.GetWindowRect(probeHwnd, out NativeMethods.Rect movedRect), Is.True);
        Assert.That(movedRect.Left, Is.EqualTo(OffScreenWindowTracker.DefaultOffScreenX));
        Assert.That(movedRect.Top, Is.EqualTo(OffScreenWindowTracker.DefaultOffScreenY));
        Assert.That(NativeMethods.IsWindowVisible(probeHwnd), Is.True);
        Assert.That(NativeMethods.GetForegroundWindow(), Is.EqualTo(foregroundBefore));

        exitEvent.Set();
        await worker.WaitForExitAsync();
    }

    [Test]
    public void StartupModalWatchdog_EnforcesWorkerIdBoundaries_DescendantPidTree_AndUtf16Journals()
    {
        string journalsDir = Path.Combine(_tempRoot, "JournalsUnit");
        Directory.CreateDirectory(journalsDir);

        // Journal for worker-10 (UTF-16 LE BOM) must NOT match worker-1
        string otherWorkerJournal = Path.Combine(journalsDir, "journal.0001.txt");
        File.WriteAllText(
            otherWorkerJournal,
            "' 0:< SYNTHETIC_WORKER_ID:worker-10\r\n'Id : TaskDialog_External_Tools_External_Tool_Failure\r\n",
            System.Text.Encoding.Unicode);

        var snapshot = new List<ProcessSnapshotEntry>
        {
            new(100, 1, "Revit.exe"),
            new(101, 100, "RevitWorker.exe"),
            new(102, 101, "DialogHost.exe"),
            new(999, 1, "Revit.exe")
        };

        var windows = new List<TopLevelDialogWindow>
        {
            new(new IntPtr(1), 999, "#32770", "External Tools - External Tool Failure", Array.Empty<string>()),
            new(new IntPtr(2), 102, "#32770", "Warning", new[] { "Revit cannot run the external application CTC" })
        };

        var watchdog = new StartupModalWatchdog(
            processSnapshotProvider: () => snapshot,
            windowEnumerator: _ => windows);

        Assert.That(watchdog.CheckJournals(100, "worker-1", journalsDir), Is.Null);
        StartupModalDetection? matched10 = watchdog.CheckJournals(100, "worker-10", journalsDir);
        Assert.That(matched10, Is.Not.Null);
        Assert.That(matched10!.Source, Is.EqualTo(StartupModalSource.Journal));

        StartupModalDetection? winMatch = watchdog.CheckWindows(100, "worker-1");
        Assert.That(winMatch, Is.Not.Null);
        Assert.That(winMatch!.Source, Is.EqualTo(StartupModalSource.Window));
        Assert.That(winMatch.ModalProcessId, Is.EqualTo(102));

        // Unrelated PID 500 must not match PID 999's modal dialog
        Assert.That(watchdog.CheckWindows(500, "worker-500"), Is.Null);
    }

    [Test]
    public async Task StartupGateMutex_EnforcesSameOsThreadRelease_CrossVersionNonBlocking_AndHoldCap()
    {
        Assert.That(StartupGateMutex.GetMutexName(2026), Is.EqualTo(@"Local\Synthetic_RevitStartupGate_2026"));

        string prefix = $@"Local\Synthetic_TestGate_{Guid.NewGuid():N}_";

        using var gate2026 = await StartupGateMutex.AcquireAsync(
            2026,
            prefix,
            acquireTimeout: TimeSpan.FromSeconds(5),
            holdCap: TimeSpan.FromSeconds(10));
        Assert.That(gate2026.IsHeld, Is.True);

        // Different version (2025) must NOT block.
        using (var gate2025 = await StartupGateMutex.AcquireAsync(
            2025,
            prefix,
            acquireTimeout: TimeSpan.FromMilliseconds(250),
            holdCap: TimeSpan.FromSeconds(5)))
        {
            Assert.That(gate2025.IsHeld, Is.True);
        }

        // Same version (2026) must block and time out while held.
        Assert.ThrowsAsync<TimeoutException>(async () =>
        {
            using var blocked = await StartupGateMutex.AcquireAsync(
                2026,
                prefix,
                acquireTimeout: TimeSpan.FromMilliseconds(80),
                holdCap: TimeSpan.FromSeconds(5));
        });

        // Release from a DIFFERENT ThreadPool thread — must still release on the dedicated holder OS thread.
        bool released = await Task.Run(() => gate2026.Release());
        Assert.That(released, Is.True);
        Assert.That(gate2026.IsHeld, Is.False);
        Assert.That(gate2026.AcquireOsThreadId, Is.Not.EqualTo(0u));
        Assert.That(gate2026.ReleaseOsThreadId, Is.EqualTo(gate2026.AcquireOsThreadId));
        Assert.That(gate2026.Release(), Is.False);

        // Next acquirer now succeeds immediately.
        using (var gateNext = await StartupGateMutex.AcquireAsync(
            2026,
            prefix,
            acquireTimeout: TimeSpan.FromSeconds(2),
            holdCap: TimeSpan.FromSeconds(5)))
        {
            Assert.That(gateNext.IsHeld, Is.True);
        }

        // Bounded hold cap auto-releases even without explicit Release().
        using var gateWithShortCap = await StartupGateMutex.AcquireAsync(
            2026,
            prefix,
            acquireTimeout: TimeSpan.FromSeconds(2),
            holdCap: TimeSpan.FromMilliseconds(120));

        using var secondAfterCap = await StartupGateMutex.AcquireAsync(
            2026,
            prefix,
            acquireTimeout: TimeSpan.FromSeconds(2),
            holdCap: TimeSpan.FromSeconds(5));

        Assert.That(secondAfterCap.IsHeld, Is.True);
        Assert.That(gateWithShortCap.ReleasedByHoldCap, Is.True);
        Assert.That(gateWithShortCap.IsHeld, Is.False);
        Assert.That(gateWithShortCap.Release(), Is.False);
    }

    [Test]
    public async Task ConcurrentUnshimmedBoots_SerializeStartupOnGate_AndRunTestsConcurrentlyAfterReadiness()
    {
        string prefix = $@"Local\Synthetic_ConcurrentBootGate_{Guid.NewGuid():N}_";
        string journalsDir = Path.Combine(_tempRoot, "JournalsConcurrent");
        Directory.CreateDirectory(journalsDir);

        var timestamps = new ConcurrentDictionary<string, long>();
        long t0 = Stopwatch.GetTimestamp();
        long NowTicks() => Stopwatch.GetTimestamp() - t0;

        using var stubLauncher = new StubWorkerProcessLauncher((server, req) =>
        {
            timestamps[$"{req.WorkerId}_launched"] = NowTicks();
            server.InitialIsBusy = true;
            if (req.WorkerId == "w_boot_1")
            {
                server.AutoReadyDelay = TimeSpan.FromMilliseconds(120);
                server.ExecutionHandler = async (testReq, emit, ct) =>
                {
                    await Task.Delay(240, ct).ConfigureAwait(false);
                    var test = new TestModel
                    {
                        Name = "Test1",
                        FullName = "Ns.Suite.Test1",
                        Success = true,
                        Time = 10.0
                    };
                    emit(test);
                    timestamps[$"{req.WorkerId}_execDone"] = NowTicks();
                    return BuildPassAssembly(testReq.TestPathFile ?? string.Empty, test);
                };
            }
            else
            {
                server.AutoReadyDelay = TimeSpan.FromMilliseconds(30);
                server.ExecutionHandler = async (testReq, emit, ct) =>
                {
                    await Task.Delay(30, ct).ConfigureAwait(false);
                    var test = new TestModel
                    {
                        Name = "Test2",
                        FullName = "Ns.Suite.Test2",
                        Success = true,
                        Time = 10.0
                    };
                    emit(test);
                    timestamps[$"{req.WorkerId}_execDone"] = NowTicks();
                    return BuildPassAssembly(testReq.TestPathFile ?? string.Empty, test);
                };
            }
        });

        var bootOptions = new ProtectedWorkerBootOptions
        {
            MutexPrefix = prefix,
            JournalsDirectory = journalsDir,
            GateHoldCap = TimeSpan.FromSeconds(10),
            GateAcquireTimeout = TimeSpan.FromSeconds(10),
            PollInterval = TimeSpan.FromMilliseconds(20),
            MaxRetries = 1
        };

        var protector = new StartupBootProtector(
            new StartupModalWatchdog(
                processSnapshotProvider: Array.Empty<ProcessSnapshotEntry>,
                windowEnumerator: _ => Array.Empty<TopLevelDialogWindow>()),
            bootOptions);

        var runner = new OneShotTestRunner(
            _discovery,
            stubLauncher,
            new RevitPipeTestClientFactory(),
            bundleLeaseFactory: _ => new NoOpDisposable(),
            startupProtector: protector);

        Task<OneShotRunResult> run1Task = runner.RunAsync(new OneShotRunOptions
        {
            RevitVersion = 2026,
            AssemblyPath = _fakeAssemblyPath,
            TrxPath = Path.Combine(_tempRoot, "out", "w1.trx"),
            WorkerId = "w_boot_1",
            BootTimeout = TimeSpan.FromSeconds(5),
            ExecutionTimeout = TimeSpan.FromSeconds(5)
        });

        // Wait until worker 1 has actually launched and is inside its 120ms startup phase.
        while (!timestamps.ContainsKey("w_boot_1_launched"))
        {
            await Task.Delay(5);
        }

        Task<OneShotRunResult> run2Task = runner.RunAsync(new OneShotRunOptions
        {
            RevitVersion = 2026,
            AssemblyPath = _fakeAssemblyPath,
            TrxPath = Path.Combine(_tempRoot, "out", "w2.trx"),
            WorkerId = "w_boot_2",
            BootTimeout = TimeSpan.FromSeconds(5),
            ExecutionTimeout = TimeSpan.FromSeconds(5)
        });

        OneShotRunResult[] results = await Task.WhenAll(run1Task, run2Task);
        Assert.That(results[0].Success, Is.True, results[0].Error);
        Assert.That(results[1].Success, Is.True, results[1].Error);

        long w1Launched = timestamps["w_boot_1_launched"];
        long w1ExecDone = timestamps["w_boot_1_execDone"];
        long w2Launched = timestamps["w_boot_2_launched"];

        // Worker 2 must serialize behind Worker 1's 120ms startup window...
        double w1ToW2LaunchMs = (w2Launched - w1Launched) * 1000.0 / Stopwatch.Frequency;
        Assert.That(w1ToW2LaunchMs, Is.GreaterThanOrEqualTo(90.0),
            $"Expected w_boot_2 launch to wait for w_boot_1 startup gate release, delta={w1ToW2LaunchMs:F1}ms");

        // ...yet Worker 2 must start BEFORE Worker 1 finishes its 240ms test execution.
        Assert.That(w2Launched, Is.LessThan(w1ExecDone),
            "Startup gate must release upon IsBusy == false so test execution runs concurrently.");
    }

    [Test]
    public async Task StartupModalWatchdog_JournalDetection_AbortsOnlyWedgedWorker_ReleasesGate_AndRetriesOnce()
    {
        string prefix = $@"Local\Synthetic_JournalWatchdog_{Guid.NewGuid():N}_";
        string journalsDir = Path.Combine(_tempRoot, "JournalsWatchdog");
        Directory.CreateDirectory(journalsDir);

        // Pre-existing stale modal journal and unrelated worker journal must both be ignored.
        File.WriteAllText(
            Path.Combine(journalsDir, "journal.0001.txt"),
            "'Id : TaskDialog_External_Tools_External_Tool_Failure\n");
        File.WriteAllText(
            Path.Combine(journalsDir, "journal.0002.txt"),
            "' 0:< SYNTHETIC_WORKER_ID:unrelated_worker\n'Id : TaskDialog_External_Tools_External_Tool_Failure\n");

        int launchAttempt = 0;
        using var stubLauncher = new StubWorkerProcessLauncher((server, req) =>
        {
            int attemptIndex = Interlocked.Increment(ref launchAttempt);
            server.InitialIsBusy = true;

            if (attemptIndex == 1)
            {
                string jPath = Path.Combine(journalsDir, "journal.0010.txt");
                _ = Task.Run(async () =>
                {
                    await Task.Delay(25);
                    File.WriteAllText(
                        jPath,
                        $"' 0:< SYNTHETIC_WORKER_ID:{req.WorkerId}\n'Id : TaskDialog_External_Tools_External_Tool_Failure\n");
                });
            }
            else
            {
                server.AutoReadyDelay = TimeSpan.FromMilliseconds(20);
                server.ExecutionHandler = (testReq, emit, _) =>
                {
                    var test = new TestModel
                    {
                        Name = "RecoveredTest",
                        FullName = "Ns.RecoverySuite.RecoveredTest",
                        Success = true,
                        Time = 12.0
                    };
                    emit(test);
                    return Task.FromResult(BuildPassAssembly(testReq.TestPathFile ?? string.Empty, test));
                };
            }
        });

        var protector = new StartupBootProtector(
            new StartupModalWatchdog(
                processSnapshotProvider: Array.Empty<ProcessSnapshotEntry>,
                windowEnumerator: _ => Array.Empty<TopLevelDialogWindow>()),
            new ProtectedWorkerBootOptions
            {
                MutexPrefix = prefix,
                JournalsDirectory = journalsDir,
                GateHoldCap = TimeSpan.FromSeconds(10),
                GateAcquireTimeout = TimeSpan.FromSeconds(5),
                PollInterval = TimeSpan.FromMilliseconds(20),
                MaxRetries = 1
            });

        var runner = new OneShotTestRunner(
            _discovery,
            stubLauncher,
            new RevitPipeTestClientFactory(),
            bundleLeaseFactory: _ => new NoOpDisposable(),
            startupProtector: protector);

        string trxPath = Path.Combine(_tempRoot, "out", "recovered.trx");
        OneShotRunResult result = await runner.RunAsync(new OneShotRunOptions
        {
            RevitVersion = 2026,
            AssemblyPath = _fakeAssemblyPath,
            TrxPath = trxPath,
            WorkerId = "w_modal_j",
            BootTimeout = TimeSpan.FromSeconds(5),
            ExecutionTimeout = TimeSpan.FromSeconds(5)
        });

        Assert.That(result.Success, Is.True, result.Error);
        Assert.That(result.PassedTests, Is.EqualTo(1));
        Assert.That(result.WorkerIds, Is.EqualTo(new[] { "w_modal_j_retry1" }));
        Assert.That(launchAttempt, Is.EqualTo(2));
        Assert.That(stubLauncher.Workers.Count, Is.EqualTo(2));
        Assert.That(stubLauncher.Workers.All(w => w.IsTerminated), Is.True);

        // Verify retry exhaustion (maxRetries = 1 -> 2 attempts max) fails cleanly and releases gate.
        int exhaustAttempts = 0;
        using var alwaysWedgedLauncher = new StubWorkerProcessLauncher((server, req) =>
        {
            int idx = Interlocked.Increment(ref exhaustAttempts);
            server.InitialIsBusy = true;
            string jPath = Path.Combine(journalsDir, $"journal.002{idx}.txt");
            string workerIdCopy = req.WorkerId;
            _ = Task.Run(async () =>
            {
                await Task.Delay(25);
                File.WriteAllText(
                    jPath,
                    $"' 0:< SYNTHETIC_WORKER_ID:{workerIdCopy}\n'Id : TaskDialog_External_Tools_External_Tool_Failure\n");
            });
        });

        var exhaustRunner = new OneShotTestRunner(
            _discovery,
            alwaysWedgedLauncher,
            new RevitPipeTestClientFactory(),
            bundleLeaseFactory: _ => new NoOpDisposable(),
            startupProtector: protector);

        OneShotRunResult exhaustResult = await exhaustRunner.RunAsync(new OneShotRunOptions
        {
            RevitVersion = 2026,
            AssemblyPath = _fakeAssemblyPath,
            TrxPath = Path.Combine(_tempRoot, "out", "exhausted.trx"),
            WorkerId = "w_exhaust",
            BootTimeout = TimeSpan.FromSeconds(5),
            ExecutionTimeout = TimeSpan.FromSeconds(5)
        });

        Assert.That(exhaustResult.Success, Is.False);
        Assert.That(exhaustResult.Error, Does.Contain(StartupModalWatchdog.StartupFailureDialogId));
        Assert.That(exhaustAttempts, Is.EqualTo(2));
        Assert.That(alwaysWedgedLauncher.Workers.All(w => w.IsTerminated), Is.True);

        // Gate must be released after failure.
        using var probeGate = await StartupGateMutex.AcquireAsync(
            2026,
            prefix,
            acquireTimeout: TimeSpan.FromMilliseconds(250),
            holdCap: TimeSpan.FromSeconds(2));
        Assert.That(probeGate.IsHeld, Is.True);
    }

    [Test]
    public async Task StartupModalWatchdog_Real32770DialogProcess_KillsOnlyWedgedPidTree_ReleasesGate_AndRetriesOnce()
    {
        string prefix = $@"Local\Synthetic_RealModal_{Guid.NewGuid():N}_";
        string journalsDir = Path.Combine(_tempRoot, "JournalsRealModal");
        Directory.CreateDirectory(journalsDir);

        using var windowingLauncher = new OffScreenWindowingLauncher(new StandardProcessLauncher());
        using var stubRetryLauncher = new StubWorkerProcessLauncher((server, _) =>
        {
            server.InitialIsBusy = true;
            server.AutoReadyDelay = TimeSpan.FromMilliseconds(25);
        });

        // Spawn an unrelated sibling GUI probe to prove ONLY the wedged worker PID tree is terminated.
        string unrelatedExitName = $@"Local\UnrelatedProbe_{Guid.NewGuid():N}";
        using var unrelatedExitEvent = new EventWaitHandle(false, EventResetMode.ManualReset, unrelatedExitName);
        await using LaunchedWorkerProcess unrelatedWorker = await windowingLauncher.LaunchAsync(
            new WorkerLaunchRequest
            {
                RevitVersion = 2026,
                ExecutablePath = _guiFocusProbeExe,
                Arguments = $"--mode window --title \"UnrelatedHealthyProbe\" --exit-event \"{unrelatedExitName}\"",
                WorkerId = "w_unrelated_probe",
                ProjectRoot = _tempRoot
            });

        LaunchedWorkerProcess? wedgedProcess = null;
        int attemptCount = 0;

        var compositeLauncher = new DelegateLauncher((req, ct) =>
        {
            int idx = Interlocked.Increment(ref attemptCount);
            if (idx == 1)
            {
                var modalRequest = new WorkerLaunchRequest
                {
                    RevitVersion = req.RevitVersion,
                    ExecutablePath = _guiFocusProbeExe,
                    Arguments = "--mode modal --title \"External Tools - External Tool Failure\" --body \"Revit cannot run the external application CTC BIM Project Suite\"",
                    WorkerId = req.WorkerId,
                    ProjectRoot = _tempRoot
                };

                return LaunchAndCaptureAsync(modalRequest, ct);
            }

            return stubRetryLauncher.LaunchAsync(req, ct);

            async Task<LaunchedWorkerProcess> LaunchAndCaptureAsync(WorkerLaunchRequest modalReq, CancellationToken token)
            {
                wedgedProcess = await windowingLauncher.LaunchAsync(modalReq, token).ConfigureAwait(false);
                return wedgedProcess;
            }
        });

        var protector = new StartupBootProtector(
            new StartupModalWatchdog(),
            new ProtectedWorkerBootOptions
            {
                MutexPrefix = prefix,
                JournalsDirectory = journalsDir,
                GateHoldCap = TimeSpan.FromSeconds(10),
                GateAcquireTimeout = TimeSpan.FromSeconds(5),
                PollInterval = TimeSpan.FromMilliseconds(20),
                MaxRetries = 1,
                BootTimeout = TimeSpan.FromSeconds(5)
            });

        ProtectedWorkerBootResult bootResult = await protector.BootWorkerWithStartupProtectionAsync(
            compositeLauncher,
            new RevitPipeTestClientFactory(),
            new WorkerLaunchRequest
            {
                RevitVersion = 2026,
                ExecutablePath = _guiFocusProbeExe,
                WorkerId = "w_real_modal"
            });

        try
        {
            Assert.That(attemptCount, Is.EqualTo(2));
            Assert.That(bootResult.AttemptsUsed, Is.EqualTo(2));
            Assert.That(bootResult.ModalAbortCount, Is.EqualTo(1));
            Assert.That(bootResult.AbortedWorkerIds, Is.EqualTo(new[] { "w_real_modal" }));
            Assert.That(bootResult.Worker.WorkerId, Is.EqualTo("w_real_modal_retry1"));

            Assert.That(wedgedProcess, Is.Not.Null);
            Assert.That(wedgedProcess!.HasExited, Is.True, "Wedged #32770 probe worker must be terminated immediately.");
            Assert.That(unrelatedWorker.HasExited, Is.False, "Unrelated sibling worker must remain alive.");

            // Startup Gate must already be released upon retry readiness.
            using var verifyGate = await StartupGateMutex.AcquireAsync(
                2026,
                prefix,
                acquireTimeout: TimeSpan.FromMilliseconds(200),
                holdCap: TimeSpan.FromSeconds(2));
            Assert.That(verifyGate.IsHeld, Is.True);
        }
        finally
        {
            unrelatedExitEvent.Set();
            await bootResult.PipeClient.DisposeAsync();
            await bootResult.Worker.DisposeAsync();
        }
    }

    private static TestAssemblyModel BuildPassAssembly(string path, TestModel test)
    {
        return new TestAssemblyModel
        {
            FileName = path,
            Name = "Synthetic.RevitTests.2026",
            Success = true,
            Tests = new List<TestTypeModel>
            {
                new()
                {
                    Name = "Suite",
                    FullName = "Ns.Suite",
                    Success = true,
                    Tests = new List<TestModel> { test }
                }
            }
        };
    }

    private static string ResolveGuiFocusProbeExecutable()
    {
        string? dir = AppContext.BaseDirectory;
        string? toolsDir = null;
        var current = new DirectoryInfo(dir);
        while (current is not null)
        {
            if (string.Equals(current.Name, "tools", StringComparison.OrdinalIgnoreCase) &&
                Directory.Exists(Path.Combine(current.FullName, "RevitCleanShim.Probes")))
            {
                toolsDir = current.FullName;
                break;
            }

            current = current.Parent;
        }

        Assert.That(toolsDir, Is.Not.Null, $"Could not locate tools directory above '{AppContext.BaseDirectory}'.");

        string probeRoot = Path.Combine(toolsDir!, "RevitCleanShim.Probes", "GuiFocusProbe", "bin");
        string preferredConfig = AppContext.BaseDirectory.Contains($"{Path.DirectorySeparatorChar}Release{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
            ? "Release"
            : "Debug";

        string[] candidates =
        {
            Path.Combine(probeRoot, preferredConfig, "net8.0-windows", "GuiFocusProbe.exe"),
            Path.Combine(probeRoot, "Release", "net8.0-windows", "GuiFocusProbe.exe"),
            Path.Combine(probeRoot, "Debug", "net8.0-windows", "GuiFocusProbe.exe")
        };

        string? found = candidates.FirstOrDefault(File.Exists);
        Assert.That(found, Is.Not.Null, $"GuiFocusProbe.exe not found in: {string.Join(", ", candidates)}");
        return found!;
    }

    private static IntPtr FindFirstVisibleTopLevelWindowForPid(int targetPid)
    {
        IntPtr found = IntPtr.Zero;
        NativeMethods.EnumWindowsProc proc = (hwnd, _) =>
        {
            NativeMethods.GetWindowThreadProcessId(hwnd, out uint rawPid);
            if (unchecked((int)rawPid) == targetPid && NativeMethods.IsWindowVisible(hwnd))
            {
                found = hwnd;
                return false;
            }

            return true;
        };

        NativeMethods.EnumWindows(proc, IntPtr.Zero);
        GC.KeepAlive(proc);
        return found;
    }

    private static IntPtr CreateAndShowUntrackedLocalWindow(int x, int y, int width, int height)
    {
        IntPtr hwnd = NativeMethods.CreateWindowExW(
            0,
            "STATIC",
            "UntrackedLocalProbe",
            WsOverlappedWindow,
            x,
            y,
            width,
            height,
            IntPtr.Zero,
            IntPtr.Zero,
            IntPtr.Zero,
            IntPtr.Zero);
        Assert.That(hwnd, Is.Not.EqualTo(IntPtr.Zero));
        NativeMethods.ShowWindow(hwnd, SwShowNa);
        return hwnd;
    }

    private static void PumpCurrentThreadMessages(int durationMs)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < durationMs)
        {
            while (NativeMethods.PeekMessageW(out NativeMethods.Msg msg, IntPtr.Zero, 0, 0, 0x0001))
            {
                NativeMethods.TranslateMessage(ref msg);
                NativeMethods.DispatchMessageW(ref msg);
            }

            Thread.Sleep(5);
        }
    }

    private sealed class NoOpDisposable : IDisposable
    {
        public void Dispose()
        {
        }
    }

    private sealed class ThrowingBeforeResumeLauncher : IWorkerProcessLauncher
    {
        private readonly int _pid;

        public ThrowingBeforeResumeLauncher(int pid)
        {
            _pid = pid;
        }

        public Task<LaunchedWorkerProcess> LaunchAsync(WorkerLaunchRequest request, CancellationToken cancellationToken = default)
        {
            request.OnProcessCreatedBeforeResume?.Invoke(_pid);
            throw new InvalidOperationException("Simulated launch failure after pre-resume hook.");
        }
    }

    private sealed class DelegateLauncher : IWorkerProcessLauncher
    {
        private readonly Func<WorkerLaunchRequest, CancellationToken, Task<LaunchedWorkerProcess>> _fn;

        public DelegateLauncher(Func<WorkerLaunchRequest, CancellationToken, Task<LaunchedWorkerProcess>> fn)
        {
            _fn = fn;
        }

        public Task<LaunchedWorkerProcess> LaunchAsync(WorkerLaunchRequest request, CancellationToken cancellationToken = default)
        {
            return _fn(request, cancellationToken);
        }
    }

    private static class NativeMethods
    {
        [return: MarshalAs(UnmanagedType.Bool)]
        public delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        public struct Point
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct Msg
        {
            public IntPtr Hwnd;
            public uint Message;
            public UIntPtr WParam;
            public IntPtr LParam;
            public uint Time;
            public Point Pt;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct Rect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetWindowRect(IntPtr hWnd, out Rect lpRect);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern IntPtr CreateWindowExW(
            uint dwExStyle,
            string lpClassName,
            string lpWindowName,
            uint dwStyle,
            int x,
            int y,
            int nWidth,
            int nHeight,
            IntPtr hWndParent,
            IntPtr hMenu,
            IntPtr hInstance,
            IntPtr lpParam);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DestroyWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool PeekMessageW(out Msg lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax, uint wRemoveMsg);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool TranslateMessage(ref Msg lpMsg);

        [DllImport("user32.dll")]
        public static extern IntPtr DispatchMessageW(ref Msg lpMsg);
    }
}
