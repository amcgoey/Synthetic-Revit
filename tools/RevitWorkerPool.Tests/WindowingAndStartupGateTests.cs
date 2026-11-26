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
    public async Task IsolatedDesktopLauncher_StandardProbe_NeverExposesWindowsOnCallerDesktop_PreservesVisibilityOnIsolatedDesktop_AndClosesDesktopOnDispose()
    {
        string statusFile = Path.Combine(_tempRoot, "probe_status.txt");
        string exitEventName = $@"Local\GuiFocusProbe_Exit_{Guid.NewGuid():N}";
        using var exitEvent = new EventWaitHandle(false, EventResetMode.ManualReset, exitEventName);

        var launcher = new IsolatedDesktopLauncher(new StandardProcessLauncher());
        IntPtr callerDesktop = NativeMethods.GetThreadDesktop(NativeMethods.GetCurrentThreadId());
        IntPtr foregroundBefore = NativeMethods.GetForegroundWindow();
        bool hookCalledBeforeResume = false;

        var request = new WorkerLaunchRequest
        {
            RevitVersion = 2026,
            ExecutablePath = _guiFocusProbeExe,
            Arguments = $"--mode window --title \"FocusProbe_{Guid.NewGuid():N}\" --status-file \"{statusFile}\" --exit-event \"{exitEventName}\"",
            WorkerId = $"w_probe_{Guid.NewGuid():N}",
            ProjectRoot = _tempRoot,
            OnProcessCreatedBeforeResume = _ => hookCalledBeforeResume = true
        };

        string? createdDesktopName = null;
        LaunchedWorkerProcess worker = await launcher.LaunchAsync(request);
        try
        {
            createdDesktopName = worker.DesktopName;
            Assert.That(hookCalledBeforeResume, Is.True);
            Assert.That(worker.DesktopHandle, Is.Not.EqualTo(IntPtr.Zero));
            Assert.That(createdDesktopName, Is.Not.Null.And.Not.Empty);
            Assert.That(IsolatedDesktopHost.DesktopExists(createdDesktopName!), Is.True);

            IntPtr isolatedHwnd = IntPtr.Zero;
            var sw = Stopwatch.StartNew();
            while (sw.Elapsed < TimeSpan.FromSeconds(3))
            {
                AssertZeroWindowsForPidOnDesktop(callerDesktop, worker.ProcessId, "caller desktop");

                IReadOnlyList<IntPtr> isolatedWindows =
                    IsolatedDesktopHost.EnumerateTopLevelWindowsForProcess(worker.DesktopHandle, worker.ProcessId);
                isolatedHwnd = isolatedWindows.FirstOrDefault(h => NativeMethods.IsWindowVisible(h));
                if (isolatedHwnd != IntPtr.Zero)
                {
                    break;
                }

                await Task.Delay(10);
            }

            Assert.That(isolatedHwnd, Is.Not.EqualTo(IntPtr.Zero), "GuiFocusProbe top-level window did not appear on isolated desktop.");
            Assert.That(NativeMethods.IsWindowVisible(isolatedHwnd), Is.True, "Window must remain visible (IsWindowVisible == true).");
            Assert.That(NativeMethods.IsIconic(isolatedHwnd), Is.False, "Window on isolated desktop should not be forced minimized.");
            Assert.That(NativeMethods.GetForegroundWindow(), Is.EqualTo(foregroundBefore), "GetForegroundWindow() on caller desktop must not change.");

            // Keep polling caller desktop briefly to prove zero late window leakage.
            for (int i = 0; i < 10; i++)
            {
                AssertZeroWindowsForPidOnDesktop(callerDesktop, worker.ProcessId, "caller desktop");
                await Task.Delay(10);
            }

            if (File.Exists(statusFile))
            {
                string statusText = File.ReadAllText(statusFile);
                Assert.That(statusText, Does.Contain("IsWindowVisible=True"));
            }

            exitEvent.Set();
            await worker.WaitForExitAsync();
            Assert.That(IsolatedDesktopHost.DesktopExists(createdDesktopName!), Is.True, "Desktop handle stays open until worker disposal.");
        }
        finally
        {
            exitEvent.Set();
            await worker.DisposeAsync();
        }

        Assert.That(worker.DesktopHandle, Is.EqualTo(IntPtr.Zero));
        Assert.That(IsolatedDesktopHost.DesktopExists(createdDesktopName!), Is.False, "Desktop handle must close cleanly after worker/job exit with no zombie leak.");
    }

    [TestCase("--mode maximized")]
    [TestCase("--mode maximized --clamp-pos")]
    [TestCase("--mode window --reposition-after-ms 150")]
    public async Task IsolatedDesktopLauncher_AggressiveWindowModes_NeverTouchCallerDesktop_PreservesVisibilityAndClosesDesktop(
        string modeArgs)
    {
        string statusFile = Path.Combine(_tempRoot, $"probe_{Guid.NewGuid():N}.txt");
        string exitEventName = $@"Local\GuiFocusProbe_ModeExit_{Guid.NewGuid():N}";
        using var exitEvent = new EventWaitHandle(false, EventResetMode.ManualReset, exitEventName);

        var launcher = new IsolatedDesktopLauncher(new StandardProcessLauncher());
        IntPtr callerDesktop = NativeMethods.GetThreadDesktop(NativeMethods.GetCurrentThreadId());
        IntPtr foregroundBefore = NativeMethods.GetForegroundWindow();

        var request = new WorkerLaunchRequest
        {
            RevitVersion = 2026,
            ExecutablePath = _guiFocusProbeExe,
            Arguments = $"{modeArgs} --title \"ModeProbe_{Guid.NewGuid():N}\" --status-file \"{statusFile}\" --exit-event \"{exitEventName}\"",
            WorkerId = $"w_mode_{Guid.NewGuid():N}",
            ProjectRoot = _tempRoot
        };

        string? createdDesktopName = null;
        LaunchedWorkerProcess worker = await launcher.LaunchAsync(request);
        try
        {
            createdDesktopName = worker.DesktopName;
            Assert.That(worker.DesktopHandle, Is.Not.EqualTo(IntPtr.Zero));

            IntPtr isolatedHwnd = IntPtr.Zero;
            var sw = Stopwatch.StartNew();
            while (sw.Elapsed < TimeSpan.FromMilliseconds(500))
            {
                AssertZeroWindowsForPidOnDesktop(callerDesktop, worker.ProcessId, "caller desktop");

                if (isolatedHwnd == IntPtr.Zero)
                {
                    IReadOnlyList<IntPtr> isolatedWindows =
                        IsolatedDesktopHost.EnumerateTopLevelWindowsForProcess(worker.DesktopHandle, worker.ProcessId);
                    isolatedHwnd = isolatedWindows.FirstOrDefault(h => NativeMethods.IsWindowVisible(h));
                }

                await Task.Delay(10);
            }

            Assert.That(isolatedHwnd, Is.Not.EqualTo(IntPtr.Zero), "GuiFocusProbe window did not appear on isolated desktop.");
            Assert.That(NativeMethods.IsWindowVisible(isolatedHwnd), Is.True, "Window must remain visible (IsWindowVisible == true).");
            Assert.That(NativeMethods.GetForegroundWindow(), Is.EqualTo(foregroundBefore), "GetForegroundWindow() on caller desktop must not change.");

            exitEvent.Set();
            await worker.WaitForExitAsync();
        }
        finally
        {
            exitEvent.Set();
            await worker.DisposeAsync();
        }

        Assert.That(IsolatedDesktopHost.DesktopExists(createdDesktopName!), Is.False, "Isolated desktop must not leak after teardown.");
    }

    [Test]
    public async Task IsolatedDesktopLauncher_WithCleanDetoursLauncher_IsolatesGuiProbeFromCallerDesktop()
    {
        string toolsDir = ResolveToolsDirectory();
        string poolBinDir = Path.Combine(toolsDir, "bin", "RevitWorkerPool");
        string shimDllPath = Path.Combine(poolBinDir, "RevitCleanShim.dll");
        string launcherDllPath = Path.Combine(poolBinDir, "RevitCleanLauncher.dll");

        Assert.That(File.Exists(shimDllPath), Is.True, $"Run tools/build.ps1 first; missing {shimDllPath}");
        Assert.That(File.Exists(launcherDllPath), Is.True, $"Run tools/build.ps1 first; missing {launcherDllPath}");

        string statusFile = Path.Combine(_tempRoot, "probe_detours_status.txt");
        string exitEventName = $@"Local\GuiFocusProbe_DetoursExit_{Guid.NewGuid():N}";
        using var exitEvent = new EventWaitHandle(false, EventResetMode.ManualReset, exitEventName);

        var detoursLauncher = new CleanDetoursProcessLauncher(shimDllPath, launcherDllPath);
        var launcher = new IsolatedDesktopLauncher(detoursLauncher);
        Assert.That(launcher.UsesCleanShim, Is.True);

        IntPtr callerDesktop = NativeMethods.GetThreadDesktop(NativeMethods.GetCurrentThreadId());
        IntPtr foregroundBefore = NativeMethods.GetForegroundWindow();

        var request = new WorkerLaunchRequest
        {
            RevitVersion = 2026,
            ExecutablePath = _guiFocusProbeExe,
            Arguments = $"--mode maximized --clamp-pos --title \"DetoursProbe_{Guid.NewGuid():N}\" --status-file \"{statusFile}\" --exit-event \"{exitEventName}\"",
            WorkerId = $"w_detours_{Guid.NewGuid():N}",
            ProjectRoot = _tempRoot
        };

        string? createdDesktopName = null;
        LaunchedWorkerProcess worker = await launcher.LaunchAsync(request);
        try
        {
            createdDesktopName = worker.DesktopName;
            Assert.That(worker.DesktopHandle, Is.Not.EqualTo(IntPtr.Zero));
            Assert.That(IsolatedDesktopHost.DesktopExists(createdDesktopName!), Is.True);

            IntPtr isolatedHwnd = IntPtr.Zero;
            var sw = Stopwatch.StartNew();
            while (sw.Elapsed < TimeSpan.FromSeconds(3))
            {
                AssertZeroWindowsForPidOnDesktop(callerDesktop, worker.ProcessId, "caller desktop");

                IReadOnlyList<IntPtr> isolatedWindows =
                    IsolatedDesktopHost.EnumerateTopLevelWindowsForProcess(worker.DesktopHandle, worker.ProcessId);
                isolatedHwnd = isolatedWindows.FirstOrDefault(h => NativeMethods.IsWindowVisible(h));
                if (isolatedHwnd != IntPtr.Zero)
                {
                    break;
                }

                await Task.Delay(10);
            }

            Assert.That(isolatedHwnd, Is.Not.EqualTo(IntPtr.Zero), "Shimmed GuiFocusProbe window did not appear on isolated desktop.");
            Assert.That(NativeMethods.IsWindowVisible(isolatedHwnd), Is.True);
            Assert.That(NativeMethods.GetForegroundWindow(), Is.EqualTo(foregroundBefore));

            exitEvent.Set();
            await worker.WaitForExitAsync();
        }
        finally
        {
            exitEvent.Set();
            await worker.DisposeAsync();
        }

        Assert.That(IsolatedDesktopHost.DesktopExists(createdDesktopName!), Is.False, "Shimmed worker desktop must close cleanly after teardown.");
    }

    [Test]
    public async Task IsolatedDesktopLauncher_ComposesOverStub_AndCleansUpDesktopOnFailureAndDispose()
    {
        using var stub = new StubWorkerProcessLauncher();
        var launcher = new IsolatedDesktopLauncher(stub);
        Assert.That(launcher.UsesCleanShim, Is.False);

        using var shimmedStub = new StubWorkerProcessLauncher(usesCleanShim: true);
        var shimmedDecorator = new IsolatedDesktopLauncher(shimmedStub);
        Assert.That(shimmedDecorator.UsesCleanShim, Is.True);

        bool innerHookCalled = false;
        var req = new WorkerLaunchRequest
        {
            RevitVersion = 2026,
            ExecutablePath = _guiFocusProbeExe,
            WorkerId = "w_stub_compose",
            OnProcessCreatedBeforeResume = _ => innerHookCalled = true
        };

        LaunchedWorkerProcess worker = await launcher.LaunchAsync(req);
        string? desktopName = worker.DesktopName;
        Assert.That(innerHookCalled, Is.True);
        Assert.That(desktopName, Is.Not.Null.And.Not.Empty);
        Assert.That(stub.LaunchedRequests.Single().DesktopName, Is.EqualTo(desktopName));
        Assert.That(IsolatedDesktopHost.DesktopExists(desktopName!), Is.True);

        worker.Dispose();
        Assert.That(IsolatedDesktopHost.DesktopExists(desktopName!), Is.False);

        // Failure cleanup check: desktop created for failed launch must not leak.
        string? failingDesktopName = null;
        var failingInner = new DelegateLauncher((r, _) =>
        {
            failingDesktopName = r.DesktopName;
            r.OnProcessCreatedBeforeResume?.Invoke(88991);
            throw new InvalidOperationException("Simulated launch failure.");
        });
        var failingDecorator = new IsolatedDesktopLauncher(failingInner);
        Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await failingDecorator.LaunchAsync(new WorkerLaunchRequest
            {
                RevitVersion = 2026,
                ExecutablePath = _guiFocusProbeExe,
                WorkerId = "w_fail_clean"
            }));

        Assert.That(failingDesktopName, Is.Not.Null.And.Not.Empty);
        Assert.That(IsolatedDesktopHost.DesktopExists(failingDesktopName!), Is.False);
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

        var windowingLauncher = new IsolatedDesktopLauncher(new StandardProcessLauncher());
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

    private static string ResolveToolsDirectory()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (string.Equals(current.Name, "tools", StringComparison.OrdinalIgnoreCase) &&
                Directory.Exists(Path.Combine(current.FullName, "RevitCleanShim.Probes")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        Assert.Fail($"Could not locate tools directory above '{AppContext.BaseDirectory}'.");
        return string.Empty;
    }

    private static string ResolveGuiFocusProbeExecutable()
    {
        string toolsDir = ResolveToolsDirectory();
        string probeRoot = Path.Combine(toolsDir, "RevitCleanShim.Probes", "GuiFocusProbe", "bin");
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

    private static void AssertZeroWindowsForPidOnDesktop(IntPtr desktopHandle, int targetPid, string desktopLabel)
    {
        var found = new List<(IntPtr Hwnd, bool Visible)>();
        NativeMethods.EnumWindowsProc proc = (hwnd, _) =>
        {
            NativeMethods.GetWindowThreadProcessId(hwnd, out uint rawPid);
            if (unchecked((int)rawPid) == targetPid)
            {
                found.Add((hwnd, NativeMethods.IsWindowVisible(hwnd)));
            }

            return true;
        };

        NativeMethods.EnumDesktopWindows(desktopHandle, proc, IntPtr.Zero);
        GC.KeepAlive(proc);

        Assert.That(
            found,
            Is.Empty,
            $"Expected zero windows for worker PID {targetPid} on {desktopLabel}, but found {found.Count} HWND(s) (first=0x{found.FirstOrDefault().Hwnd.ToInt64():X}, visible={found.FirstOrDefault().Visible}).");
    }

    private sealed class NoOpDisposable : IDisposable
    {
        public void Dispose()
        {
        }
    }

    [Test]
    public async Task LaunchWithJournalCreationGateAsync_SerializesConcurrentLaunchesUntilJournalFileAppears_AndBypassesStub()
    {
        string journalsDir = Path.Combine(_tempRoot, "JournalsGate");
        Directory.CreateDirectory(journalsDir);
        string mutexPrefix = $@"Local\Synthetic_TestJournalGate_{Guid.NewGuid():N}_";

        // 1. Stub launcher without bypassExecutableCheck skips gating immediately.
        using var plainStub = new StubWorkerProcessLauncher();
        LaunchedWorkerProcess fastStubWorker = await TestExecutionHelper.LaunchWithJournalCreationGateAsync(
            plainStub,
            new WorkerLaunchRequest
            {
                RevitVersion = 2026,
                ExecutablePath = _fakeAssemblyPath,
                WorkerId = "w_stub_fast"
            },
            TimeSpan.FromSeconds(5),
            logger: null,
            CancellationToken.None);
        fastStubWorker.Dispose();

        // 2. Concurrent launches with bypassExecutableCheck serialize until journal.NNNN.txt is written.
        int launchSeq = 0;
        var launch1Started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        int launch2StartedFlag = 0;

        using var gatedLauncher = new StubWorkerProcessLauncher((_, req) =>
        {
            int seq = Interlocked.Increment(ref launchSeq);
            if (seq == 1)
            {
                launch1Started.TrySetResult(true);
            }
            else if (seq == 2)
            {
                Volatile.Write(ref launch2StartedFlag, 1);
                File.WriteAllText(Path.Combine(journalsDir, "journal.0002.txt"), "' second\r\n");
            }
        });

        var req1 = new WorkerLaunchRequest { RevitVersion = 2026, ExecutablePath = _fakeAssemblyPath, WorkerId = "w_jg_1" };
        var req2 = new WorkerLaunchRequest { RevitVersion = 2026, ExecutablePath = _fakeAssemblyPath, WorkerId = "w_jg_2" };

        Task<LaunchedWorkerProcess> task1 = TestExecutionHelper.LaunchWithJournalCreationGateAsync(
            gatedLauncher,
            req1,
            TimeSpan.FromSeconds(5),
            logger: null,
            CancellationToken.None,
            journalsDirectoryOverride: journalsDir,
            mutexPrefix: mutexPrefix,
            journalWaitCapOverride: TimeSpan.FromSeconds(5),
            bypassExecutableCheck: true);

        await launch1Started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Task<LaunchedWorkerProcess> task2 = TestExecutionHelper.LaunchWithJournalCreationGateAsync(
            gatedLauncher,
            req2,
            TimeSpan.FromSeconds(5),
            logger: null,
            CancellationToken.None,
            journalsDirectoryOverride: journalsDir,
            mutexPrefix: mutexPrefix,
            journalWaitCapOverride: TimeSpan.FromSeconds(5),
            bypassExecutableCheck: true);

        await Task.Delay(75);
        Assert.That(Volatile.Read(ref launch2StartedFlag), Is.EqualTo(0),
            "Second launch must wait until first worker creates its journal.NNNN.txt file.");

        File.WriteAllText(Path.Combine(journalsDir, "journal.0001.txt"), "' first\r\n");

        LaunchedWorkerProcess[] processes = await Task.WhenAll(task1, task2).WaitAsync(TimeSpan.FromSeconds(3));
        Assert.That(Volatile.Read(ref launch2StartedFlag), Is.EqualTo(1));
        Assert.That(processes.Length, Is.EqualTo(2));

        foreach (LaunchedWorkerProcess p in processes)
        {
            p.Dispose();
        }
    }

    [Test]
    public void LaunchWithJournalCreationGateAsync_TerminatesWorkerOnCancellationDuringJournalWait()
    {
        string journalsDir = Path.Combine(_tempRoot, "JournalsGateCancel");
        Directory.CreateDirectory(journalsDir);
        string mutexPrefix = $@"Local\Synthetic_TestJournalGateCancel_{Guid.NewGuid():N}_";

        using var launcher = new StubWorkerProcessLauncher();
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(60);

        Assert.CatchAsync<OperationCanceledException>(async () =>
            await TestExecutionHelper.LaunchWithJournalCreationGateAsync(
                launcher,
                new WorkerLaunchRequest { RevitVersion = 2026, ExecutablePath = _fakeAssemblyPath, WorkerId = "w_jg_cancel" },
                TimeSpan.FromSeconds(5),
                logger: null,
                cts.Token,
                journalsDirectoryOverride: journalsDir,
                mutexPrefix: mutexPrefix,
                journalWaitCapOverride: TimeSpan.FromSeconds(5),
                bypassExecutableCheck: true));

        Assert.That(launcher.Workers.Count, Is.EqualTo(1));
        Assert.That(launcher.Workers.Single().IsTerminated, Is.True);
    }

    private sealed class DelegateLauncher : IWorkerProcessLauncher
    {
        private readonly Func<WorkerLaunchRequest, CancellationToken, Task<LaunchedWorkerProcess>> _fn;

        public DelegateLauncher(Func<WorkerLaunchRequest, CancellationToken, Task<LaunchedWorkerProcess>> fn)
        {
            _fn = fn;
        }

        public bool UsesCleanShim => false;

        public Task<LaunchedWorkerProcess> LaunchAsync(WorkerLaunchRequest request, CancellationToken cancellationToken = default)
        {
            return _fn(request, cancellationToken);
        }
    }

    private static class NativeMethods
    {
        [return: MarshalAs(UnmanagedType.Bool)]
        public delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        public static extern IntPtr GetThreadDesktop(uint dwThreadId);

        [DllImport("kernel32.dll")]
        public static extern uint GetCurrentThreadId();

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool EnumDesktopWindows(IntPtr hDesktop, EnumWindowsProc lpfn, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsIconic(IntPtr hWnd);
    }
}
