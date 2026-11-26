using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using RevitWorkerPool.Discovery;
using RevitWorkerPool.Execution;
using RevitWorkerPool.Ipc;
using RevitWorkerPool.Sharding;

namespace RevitWorkerPool.Tests;

/// <summary>
/// Explicit repeatable live end-to-end verification of the assembled Worker Pool,
/// Clean-Launch Shim, off-screen windowing, duration-weighted LPT sharding, per-shard
/// journal cleanliness gating, host invariance, and <c>--no-shim</c>/<c>--no-pool</c>
/// fallbacks across installed Revit 2024 (<c>net48</c>) and Revit 2026 (<c>net8.0</c>).
/// </summary>
[TestFixture]
[Explicit("Live end-to-end smoke suite requiring installed Revit 2024/2026, signed Debug Bootstrap/v1, and signed RevitCleanShim binaries.")]
[Category("LiveRevitSmoke")]
[SupportedOSPlatform("windows")]
public class LiveRevitEndToEndSmokeTests
{
    private static readonly Regex RunDirPattern = new(
        @"^\d{8}_\d{6}_\d+_[0-9a-fA-F]+$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ThirdPartyAddinLineTokenRegex = new(
        @"CTC|Enscape|Bluebeam|Kinship|KM\+|Keynote Manager|RDES\.|eTransmit|BIM 42|AcPerfMon",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly string[] AddinLoadUiErrorPrefixes =
    {
        "Starting External DB Application:",
        "Starting External Application:",
        "Jrn.AddInManifest",
        "Added pushbutton",
        "API_ERROR {"
    };

    private static readonly string[] ForbiddenWholeJournalTokens =
    {
        "CTC-BIM-Project-Suite",
        "CTC BIM Project Suite",
        "CTC.RET.",
        "0_Enscape.addin",
        "Enscape.Revit.Plugin",
        "BluebeamAddin",
        "BbRevitAddin",
        "bluebeamAddin",
        "Kinship.addin",
        "KinshipPlugin",
        "KM+ Keynote DB Server",
        "Keynote Manager +",
        "RDES.KMPlus",
        "RDES.KeynoteManager",
        "Autodesk.eTransmitApplication",
        "eTransmitForRevit.eTransmitApplication",
        "eTransmit for Revit"
    };

    private static readonly string[] Phase2ExpectedMethods =
    {
        "TestWorker_BypassesHostStartup_AndLoadsUnshadowedTempPayload",
        "TestWorker_ProjectStandardsDashboardWindow_InitializesXamlAndThemeCleanly",
        "TestWorker_DocumentUtilPurge_ResolvesETransmitWithoutHostStartup"
    };

    [TestCase(2024)]
    [TestCase(2026)]
    public async Task LiveEndToEnd_CleanPool_Sharded_WarmLatency_AndFallbacks(int revitVersion)
    {
        string? priorCleanShimEnv = Environment.GetEnvironmentVariable("SYNTHETIC_CLEAN_SHIM");
        string? priorPoolCmdEnv = Environment.GetEnvironmentVariable("SYNTHETIC_WORKER_POOL_CMD");
        string? priorPipeNameEnv = Environment.GetEnvironmentVariable("SYNTHETIC_WORKER_POOL_PIPE_NAME");
        string testPipeName = $"Synthetic_RevitWorkerPool_LiveSmoke_{revitVersion}_{Guid.NewGuid():N}";
        var isolatedPipeEnv = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["SYNTHETIC_WORKER_POOL_PIPE_NAME"] = testPipeName
        };

        // Pre-flight discovery & artifact verification
        string repoRoot = RevitInstallationDiscovery.FindRepositoryRoot()
            ?? throw new DirectoryNotFoundException("Could not locate repository root (.git).");
        string poolExePath = Path.Combine(repoRoot, "tools", "bin", "RevitWorkerPool", "RevitWorkerPool.exe");
        string shimDllPath = Path.Combine(repoRoot, "tools", "bin", "RevitWorkerPool", "RevitCleanShim.dll");
        string launcherDllPath = Path.Combine(repoRoot, "tools", "bin", "RevitWorkerPool", "RevitCleanLauncher.dll");
        string testExecutorPath = Path.Combine(repoRoot, ".agents", "skills", "revit-tests", "scripts", "test_executor.py");
        string journalToolPath = Path.Combine(repoRoot, ".agents", "skills", "revit-journal-telemetry", "scripts", "revit_journal_tool.py");
        string testResultsRoot = Path.Combine(repoRoot, "tests", "TestResults");

        Assert.That(File.Exists(poolExePath), Is.True, $"Missing RevitWorkerPool.exe at '{poolExePath}'.");
        Assert.That(File.Exists(shimDllPath), Is.True, $"Missing RevitCleanShim.dll at '{shimDllPath}'.");
        Assert.That(File.Exists(launcherDllPath), Is.True, $"Missing RevitCleanLauncher.dll at '{launcherDllPath}'.");
        Assert.That(File.Exists(testExecutorPath), Is.True, $"Missing test_executor.py at '{testExecutorPath}'.");
        Assert.That(File.Exists(journalToolPath), Is.True, $"Missing revit_journal_tool.py at '{journalToolPath}'.");

        RevitInstallation? installation = new RevitInstallationDiscovery().TryFindInstallation(revitVersion);
        Assert.That(installation, Is.Not.Null, $"Revit {revitVersion} installation was not discovered.");
        Assert.That(File.Exists(installation!.ExecutablePath), Is.True, $"Missing Revit.exe at '{installation.ExecutablePath}'.");

        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);

        string userAddinManifest = Path.Combine(
            appData,
            "Autodesk",
            "Revit",
            "Addins",
            revitVersion.ToString(CultureInfo.InvariantCulture),
            $"Synthetic{revitVersion}.addin");
        Assert.That(File.Exists(userAddinManifest), Is.True, $"Missing Debug manifest at '{userAddinManifest}'.");
        VerifyDebugBootstrapManifest(userAddinManifest, revitVersion);

        Environment.SetEnvironmentVariable("SYNTHETIC_CLEAN_SHIM", null);
        Environment.SetEnvironmentVariable("SYNTHETIC_WORKER_POOL_CMD", null);
        Environment.SetEnvironmentVariable("SYNTHETIC_WORKER_POOL_PIPE_NAME", null);

        HashSet<int> preExistingRevitPids = GetRunningRevitPids();
        IReadOnlyDictionary<string, FileSnapshot> addinsSettingsBefore = SnapshotHostAddInsSettings(appData, revitVersion);
        IReadOnlyDictionary<string, AddInsSettingsPolicySnapshot> addinsPoliciesBefore =
            SnapshotAddInsSettingsPolicies(addinsSettingsBefore.Keys);
        string programDataSyntheticManifest = Path.Combine(
            programData,
            "Autodesk",
            "Revit",
            "Addins",
            revitVersion.ToString(CultureInfo.InvariantCulture),
            $"Synthetic{revitVersion}.addin");
        FileSnapshot programDataManifestBefore = CaptureFileSnapshot(programDataSyntheticManifest);
        bool hasLegacyProgramDataManifest = HasLegacyNonBootstrapProgramDataManifest(programData, revitVersion);

        TestContext.Progress.WriteLine(
            $"[LiveSmoke {revitVersion}] Pre-flight OK: Revit='{installation.ExecutablePath}', pipe='{testPipeName}', " +
            $"preExistingRevitPids=[{string.Join(",", preExistingRevitPids)}], " +
            $"hasLegacyProgramDataManifest={hasLegacyProgramDataManifest}");

        try
        {
            // Drain any leftover isolated test pool before Phase 1 without touching the production supervisor.
            DrainResult initialDrain = await RunPoolDrainAsync(poolExePath, repoRoot, testPipeName, force: true).ConfigureAwait(false);
            Assert.That(initialDrain.Success, Is.True, initialDrain.Error);
            Assert.That(initialDrain.Status?.TotalResident ?? 0, Is.EqualTo(0));

            // -----------------------------------------------------------------
            // Phase 1: warm --version <Year> --count 2 --json + GetForegroundWindow() polling
            // -----------------------------------------------------------------
            TestContext.Progress.WriteLine($"[LiveSmoke {revitVersion}] Phase 1: warm --count 2 + 15ms foreground polling...");
            using (var pollCts = new CancellationTokenSource())
            {
                var stolenFocusEvents = new ConcurrentQueue<string>();
                long pollSampleCount = 0;
                Task pollTask = StartForegroundPollingLoop(
                    preExistingRevitPids,
                    stolenFocusEvents,
                    () => Interlocked.Increment(ref pollSampleCount),
                    pollCts.Token);

                ProcessRunOutput warmOut;
                try
                {
                    warmOut = await RunProcessAsync(
                        poolExePath,
                        new[]
                        {
                            "warm",
                            "--version", revitVersion.ToString(CultureInfo.InvariantCulture),
                            "--count", "2",
                            "--pipe-name", testPipeName,
                            "--json",
                            "--diagnostics"
                        },
                        repoRoot,
                        TimeSpan.FromSeconds(180),
                        clearCleanShimEnv: true).ConfigureAwait(false);
                }
                finally
                {
                    pollCts.Cancel();
                    await pollTask.ConfigureAwait(false);
                }

                Assert.That(warmOut.TimedOut, Is.False, "Phase 1 warm timed out.");
                Assert.That(warmOut.ExitCode, Is.EqualTo(0), $"Phase 1 warm failed:\nSTDERR:\n{warmOut.Stderr}\nSTDOUT:\n{warmOut.Stdout}");

                WarmResult? warmResult = ParseLastJsonObject<WarmResult>(warmOut.Stdout);
                Assert.That(warmResult, Is.Not.Null, $"Could not parse WarmResult JSON from:\n{warmOut.Stdout}");
                Assert.That(warmResult!.Success, Is.True, warmResult.Error);
                Assert.That(warmResult.ExitCode, Is.EqualTo(0));
                Assert.That(warmResult.ReadyCount, Is.EqualTo(2));
                Assert.That(warmResult.Status, Is.Not.Null);
                Assert.That(warmResult.Status!.ShimEnabled, Is.True);
                Assert.That(warmResult.Status.TotalResident, Is.EqualTo(2));

                Assert.That(Interlocked.Read(ref pollSampleCount), Is.GreaterThan(0));
                Assert.That(
                    stolenFocusEvents.ToArray(),
                    Is.Empty,
                    $"Newly spawned Revit {revitVersion} worker stole foreground focus:\n{string.Join(Environment.NewLine, stolenFocusEvents)}");
                TestContext.Progress.WriteLine(
                    $"[LiveSmoke {revitVersion}] Phase 1 PASS: readyCount={warmResult.ReadyCount}, " +
                    $"totalResident={warmResult.Status.TotalResident}, pollSamples={Interlocked.Read(ref pollSampleCount)}, stolenFocus=0");
            }

            // -----------------------------------------------------------------
            // Phase 2: Warm single-fixture BootstrapWorkerIsolationRevitTests + latency + journal proof
            // -----------------------------------------------------------------
            TestContext.Progress.WriteLine($"[LiveSmoke {revitVersion}] Phase 2: warm single-fixture BootstrapWorkerIsolationRevitTests...");
            HashSet<string> beforePhase2Dirs = SnapshotDirectoryNames(testResultsRoot);
            string dispatchMarker = $"[TESTS] Executing Revit {revitVersion} tests via RevitWorkerPool";
            TimeSpan? dispatchStartOffset = null;

            ProcessRunOutput phase2Exec = await RunProcessAsync(
                "python",
                new[]
                {
                    "-u",
                    testExecutorPath,
                    "--version", revitVersion.ToString(CultureInfo.InvariantCulture),
                    "--filter", "BootstrapWorkerIsolationRevitTests"
                },
                repoRoot,
                TimeSpan.FromSeconds(180),
                clearCleanShimEnv: true,
                onStdoutLine: (elapsed, line) =>
                {
                    if (dispatchStartOffset is null && line.Contains(dispatchMarker, StringComparison.Ordinal))
                    {
                        dispatchStartOffset = elapsed;
                    }
                },
                extraEnv: isolatedPipeEnv).ConfigureAwait(false);

            Assert.That(phase2Exec.TimedOut, Is.False, "Phase 2 test_executor timed out.");
            Assert.That(
                phase2Exec.ExitCode,
                Is.EqualTo(0),
                $"Phase 2 test_executor failed:\nSTDOUT:\n{phase2Exec.Stdout}\nSTDERR:\n{phase2Exec.Stderr}");
            Assert.That(
                dispatchStartOffset,
                Is.Not.Null,
                $"Did not observe dispatch marker '{dispatchMarker}' in stdout:\n{phase2Exec.Stdout}");

            TimeSpan postBuildElapsed = phase2Exec.TotalElapsed - dispatchStartOffset!.Value;
            string phase2RunDir = FindSingleNewRunDirectory(testResultsRoot, beforePhase2Dirs);

            OneShotRunResult phase2PoolResult = ParseOutputLogPoolResult(
                Path.Combine(phase2RunDir, $"output_{revitVersion}.log"));
            Assert.That(phase2PoolResult.Success, Is.True, phase2PoolResult.Error);
            Assert.That(phase2PoolResult.ExitCode, Is.EqualTo(0));
            Assert.That(phase2PoolResult.ShimEnabled, Is.True);
            Assert.That(phase2PoolResult.ShardCount, Is.EqualTo(1));
            Assert.That(phase2PoolResult.WorkerIds.Count, Is.EqualTo(1));
            Assert.That(phase2PoolResult.TotalTests, Is.GreaterThanOrEqualTo(Phase2ExpectedMethods.Length));
            Assert.That(phase2PoolResult.PassedTests, Is.EqualTo(phase2PoolResult.TotalTests));
            Assert.That(phase2PoolResult.FailedTests, Is.EqualTo(0));

            TrxSummaryInspection phase2Trx = InspectTrxFile(Path.Combine(phase2RunDir, $"results_{revitVersion}.trx"));
            Assert.That(phase2Trx.Total, Is.EqualTo(phase2PoolResult.TotalTests));
            Assert.That(phase2Trx.Passed, Is.EqualTo(phase2Trx.Total));
            Assert.That(phase2Trx.Failed, Is.EqualTo(0));
            foreach (string expectedMethod in Phase2ExpectedMethods)
            {
                Assert.That(
                    phase2Trx.TestNames.Any(name => name.Contains(expectedMethod, StringComparison.Ordinal)),
                    Is.True,
                    $"TRX missing expected test method '{expectedMethod}'. Found: [{string.Join(", ", phase2Trx.TestNames)}]");
            }

            TimeSpan poolDispatchOverhead = postBuildElapsed - TimeSpan.FromMilliseconds(phase2Trx.TotalDurationMs);
            TestContext.Progress.WriteLine(
                $"[LiveSmoke {revitVersion}] Phase 2 Latency: postBuildElapsed={postBuildElapsed.TotalSeconds:F3}s, " +
                $"inRevitTestMs={phase2Trx.TotalDurationMs:F1}ms, poolDispatchOverhead={poolDispatchOverhead.TotalSeconds:F3}s");

            Assert.That(
                postBuildElapsed,
                Is.LessThan(TimeSpan.FromSeconds(25)),
                $"Expected warm single-fixture postBuildElapsed < 25s, observed {postBuildElapsed.TotalSeconds:F3}s.");
            Assert.That(
                poolDispatchOverhead,
                Is.LessThan(TimeSpan.FromSeconds(8)),
                $"Expected poolDispatchOverhead < 8s, observed {poolDispatchOverhead.TotalSeconds:F3}s.");

            string phase2WorkerId = phase2PoolResult.WorkerIds.Single();
            string phase2JournalPath = await AssertCleanShimmedJournalAsync(
                journalToolPath,
                repoRoot,
                revitVersion,
                phase2WorkerId).ConfigureAwait(false);

            TestContext.Progress.WriteLine(
                $"[LiveSmoke {revitVersion}] Phase 2 PASS: workerId={phase2WorkerId}, journal='{phase2JournalPath}'");

            // -----------------------------------------------------------------
            // Phase 3: Multi-worker sharded --category RevitSmoke --shards 2
            // -----------------------------------------------------------------
            TestContext.Progress.WriteLine($"[LiveSmoke {revitVersion}] Phase 3: sharded --category RevitSmoke --shards 2...");
            HashSet<string> beforePhase3Dirs = SnapshotDirectoryNames(testResultsRoot);

            ProcessRunOutput phase3Exec = await RunProcessAsync(
                "python",
                new[]
                {
                    "-u",
                    testExecutorPath,
                    "--version", revitVersion.ToString(CultureInfo.InvariantCulture),
                    "--category", "RevitSmoke",
                    "--shards", "2"
                },
                repoRoot,
                TimeSpan.FromSeconds(240),
                clearCleanShimEnv: true,
                extraEnv: isolatedPipeEnv).ConfigureAwait(false);

            Assert.That(phase3Exec.TimedOut, Is.False, "Phase 3 sharded RevitSmoke run timed out.");
            Assert.That(
                phase3Exec.ExitCode,
                Is.EqualTo(0),
                $"Phase 3 sharded run failed:\nSTDOUT:\n{phase3Exec.Stdout}\nSTDERR:\n{phase3Exec.Stderr}");

            string phase3RunDir = FindSingleNewRunDirectory(testResultsRoot, beforePhase3Dirs);
            OneShotRunResult phase3PoolResult = ParseOutputLogPoolResult(
                Path.Combine(phase3RunDir, $"output_{revitVersion}.log"));
            Assert.That(phase3PoolResult.Success, Is.True, phase3PoolResult.Error);
            Assert.That(phase3PoolResult.ExitCode, Is.EqualTo(0));
            Assert.That(phase3PoolResult.ShimEnabled, Is.True);
            Assert.That(phase3PoolResult.ShardCount, Is.EqualTo(2));
            Assert.That(phase3PoolResult.WorkerIds.Count, Is.EqualTo(2));
            Assert.That(
                phase3PoolResult.WorkerIds.Distinct(StringComparer.Ordinal).Count(),
                Is.EqualTo(2),
                "Sharded run must execute across 2 distinct worker IDs.");
            Assert.That(phase3PoolResult.TotalTests, Is.GreaterThanOrEqualTo(14));
            Assert.That(phase3PoolResult.PassedTests, Is.EqualTo(phase3PoolResult.TotalTests));
            Assert.That(phase3PoolResult.FailedTests, Is.EqualTo(0));

            TrxSummaryInspection phase3Trx = InspectTrxFile(Path.Combine(phase3RunDir, $"results_{revitVersion}.trx"));
            Assert.That(phase3Trx.Total, Is.EqualTo(phase3PoolResult.TotalTests));
            Assert.That(phase3Trx.Passed, Is.EqualTo(phase3Trx.Total));
            Assert.That(phase3Trx.Failed, Is.EqualTo(0));
            Assert.That(phase3Trx.UnitTestResultCount, Is.EqualTo(phase3Trx.Total));

            var phase3Journals = new List<string>();
            foreach (string wid in phase3PoolResult.WorkerIds)
            {
                string jPath = await AssertCleanShimmedJournalAsync(
                    journalToolPath,
                    repoRoot,
                    revitVersion,
                    wid).ConfigureAwait(false);
                phase3Journals.Add(jPath);
            }

            Assert.That(
                phase3Journals.Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                Is.EqualTo(2),
                $"Expected 2 distinct worker journals, got: [{string.Join(", ", phase3Journals)}]");

            var timingCache = new FixtureTimingCache();
            string cachePath = timingCache.GetCacheFilePath(revitVersion);
            Assert.That(File.Exists(cachePath), Is.True, $"Expected fixture timing cache at '{cachePath}'.");
            FixtureTimingCacheDocument timingDoc = timingCache.LoadDocument(revitVersion);
            Assert.That(timingDoc.RevitVersion, Is.EqualTo(revitVersion));
            Assert.That(
                timingDoc.Fixtures.Count,
                Is.GreaterThanOrEqualTo(10),
                $"Expected >= 10 cached fixtures in '{cachePath}', got {timingDoc.Fixtures.Count}.");
            foreach ((string fixtureName, FixtureTimingEntry entry) in timingDoc.Fixtures)
            {
                Assert.That(
                    double.IsFinite(entry.EmaMs) && entry.EmaMs >= 0.0,
                    Is.True,
                    $"Invalid EmaMs for fixture '{fixtureName}': {entry.EmaMs}");
                Assert.That(
                    double.IsFinite(entry.LastObservedMs) && entry.LastObservedMs >= 0.0,
                    Is.True,
                    $"Invalid LastObservedMs for fixture '{fixtureName}': {entry.LastObservedMs}");
                Assert.That(entry.SampleCount, Is.GreaterThanOrEqualTo(1), $"Expected SampleCount >= 1 for '{fixtureName}'.");
            }

            TestContext.Progress.WriteLine(
                $"[LiveSmoke {revitVersion}] Phase 3 PASS: shards={phase3PoolResult.ShardCount}, " +
                $"totalTests={phase3PoolResult.TotalTests}, workerIds=[{string.Join(",", phase3PoolResult.WorkerIds)}], " +
                $"cachedFixtures={timingDoc.Fixtures.Count}, elapsed={phase3Exec.TotalElapsed.TotalSeconds:F2}s");

            // -----------------------------------------------------------------
            // Phase 4: Clean drain + status --json
            // -----------------------------------------------------------------
            TestContext.Progress.WriteLine($"[LiveSmoke {revitVersion}] Phase 4: drain + status verification...");
            DrainResult phase4Drain = await RunPoolDrainAsync(poolExePath, repoRoot, testPipeName).ConfigureAwait(false);
            Assert.That(phase4Drain.Success, Is.True, phase4Drain.Error);
            Assert.That(phase4Drain.ExitCode, Is.EqualTo(0));
            Assert.That(phase4Drain.Status?.TotalResident ?? -1, Is.EqualTo(0));

            PoolStatusSnapshot phase4Status = await RunPoolStatusAsync(poolExePath, repoRoot, testPipeName).ConfigureAwait(false);
            Assert.That(phase4Status.TotalResident, Is.EqualTo(0));
            Assert.That(phase4Status.Running, Is.False);

            AssertOnlyPreExistingRevitProcessesRunning(preExistingRevitPids);
            VerifyHostAddInsSettingsUnchanged(appData, revitVersion, addinsSettingsBefore);
            FileSnapshot programDataAfterPhase4 = CaptureFileSnapshot(programDataSyntheticManifest);
            Assert.That(
                programDataAfterPhase4,
                Is.EqualTo(programDataManifestBefore),
                $"Host ProgramData manifest '{programDataSyntheticManifest}' mutated during shimmed phases.");
            TestContext.Progress.WriteLine($"[LiveSmoke {revitVersion}] Phase 4 PASS: totalResident=0, running=false, shimmed host AddInsSettings.json & ProgramData byte-identical.");

            // -----------------------------------------------------------------
            // Phase 5: --no-shim fallback + drain
            // -----------------------------------------------------------------
            TestContext.Progress.WriteLine($"[LiveSmoke {revitVersion}] Phase 5: --no-shim fallback...");
            HashSet<string> beforePhase5Dirs = SnapshotDirectoryNames(testResultsRoot);

            ProcessRunOutput phase5Exec = await RunProcessAsync(
                "python",
                new[]
                {
                    "-u",
                    testExecutorPath,
                    "--version", revitVersion.ToString(CultureInfo.InvariantCulture),
                    "--filter", "BootstrapWorkerIsolationRevitTests",
                    "--no-shim"
                },
                repoRoot,
                TimeSpan.FromSeconds(240),
                clearCleanShimEnv: true,
                extraEnv: isolatedPipeEnv).ConfigureAwait(false);

            Assert.That(phase5Exec.TimedOut, Is.False, "Phase 5 --no-shim run timed out.");
            Assert.That(
                phase5Exec.ExitCode,
                Is.EqualTo(0),
                $"Phase 5 --no-shim run failed:\nSTDOUT:\n{phase5Exec.Stdout}\nSTDERR:\n{phase5Exec.Stderr}");

            string phase5RunDir = FindSingleNewRunDirectory(testResultsRoot, beforePhase5Dirs);
            OneShotRunResult phase5PoolResult = ParseOutputLogPoolResult(
                Path.Combine(phase5RunDir, $"output_{revitVersion}.log"));
            Assert.That(phase5PoolResult.Success, Is.True, phase5PoolResult.Error);
            Assert.That(phase5PoolResult.ExitCode, Is.EqualTo(0));
            Assert.That(phase5PoolResult.ShimEnabled, Is.False);
            Assert.That(phase5PoolResult.TotalTests, Is.GreaterThanOrEqualTo(Phase2ExpectedMethods.Length));
            Assert.That(phase5PoolResult.PassedTests, Is.EqualTo(phase5PoolResult.TotalTests));
            Assert.That(phase5PoolResult.FailedTests, Is.EqualTo(0));
            Assert.That(phase5PoolResult.WorkerIds.Count, Is.EqualTo(1));

            string phase5WorkerId = phase5PoolResult.WorkerIds.Single();
            await AssertJournalCleanViaToolAsync(
                journalToolPath,
                repoRoot,
                revitVersion,
                workerId: phase5WorkerId).ConfigureAwait(false);

            if (hasLegacyProgramDataManifest)
            {
                Assert.That(
                    phase5Exec.Stdout,
                    Does.Contain($"[WARN] Legacy machine-wide manifest detected for Revit {revitVersion}"));
            }

            DrainResult phase5Drain = await RunPoolDrainAsync(poolExePath, repoRoot, testPipeName).ConfigureAwait(false);
            Assert.That(phase5Drain.Success, Is.True, phase5Drain.Error);
            Assert.That(phase5Drain.Status?.TotalResident ?? -1, Is.EqualTo(0));
            AssertOnlyPreExistingRevitProcessesRunning(preExistingRevitPids);

            TestContext.Progress.WriteLine(
                $"[LiveSmoke {revitVersion}] Phase 5 PASS (--no-shim): workerId={phase5WorkerId}, elapsed={phase5Exec.TotalElapsed.TotalSeconds:F2}s");

            // -----------------------------------------------------------------
            // Phase 6: --no-pool cold fallback
            // -----------------------------------------------------------------
            TestContext.Progress.WriteLine($"[LiveSmoke {revitVersion}] Phase 6: --no-pool cold fallback...");
            HashSet<string> beforePhase6Dirs = SnapshotDirectoryNames(testResultsRoot);

            ProcessRunOutput phase6Exec = await RunProcessAsync(
                "python",
                new[]
                {
                    "-u",
                    testExecutorPath,
                    "--version", revitVersion.ToString(CultureInfo.InvariantCulture),
                    "--filter", "BootstrapWorkerIsolationRevitTests",
                    "--no-pool"
                },
                repoRoot,
                TimeSpan.FromSeconds(240),
                clearCleanShimEnv: true,
                extraEnv: isolatedPipeEnv).ConfigureAwait(false);

            Assert.That(phase6Exec.TimedOut, Is.False, "Phase 6 --no-pool run timed out.");
            Assert.That(
                phase6Exec.ExitCode,
                Is.EqualTo(0),
                $"Phase 6 --no-pool run failed:\nSTDOUT:\n{phase6Exec.Stdout}\nSTDERR:\n{phase6Exec.Stderr}");
            Assert.That(
                phase6Exec.Stdout,
                Does.Contain("--no-pool specified; using isolated cold Revit workers."));
            Assert.That(
                phase6Exec.Stdout,
                Does.Contain("Cleanliness Gate Result: CLEAN"));

            if (hasLegacyProgramDataManifest)
            {
                Assert.That(
                    phase6Exec.Stdout,
                    Does.Contain($"[WARN] Legacy machine-wide manifest detected for Revit {revitVersion}"));
            }

            string phase6RunDir = FindSingleNewRunDirectory(testResultsRoot, beforePhase6Dirs);
            TrxSummaryInspection phase6Trx = InspectTrxFile(Path.Combine(phase6RunDir, $"results_{revitVersion}.trx"));
            Assert.That(phase6Trx.Total, Is.GreaterThanOrEqualTo(Phase2ExpectedMethods.Length));
            Assert.That(phase6Trx.Passed, Is.EqualTo(phase6Trx.Total));
            Assert.That(phase6Trx.Failed, Is.EqualTo(0));

            PoolStatusSnapshot phase6Status = await RunPoolStatusAsync(poolExePath, repoRoot, testPipeName).ConfigureAwait(false);
            Assert.That(phase6Status.Running, Is.False);
            Assert.That(phase6Status.TotalResident, Is.EqualTo(0));
            AssertOnlyPreExistingRevitProcessesRunning(preExistingRevitPids);

            TestContext.Progress.WriteLine(
                $"[LiveSmoke {revitVersion}] Phase 6 PASS (--no-pool): {phase6Trx.Passed}/{phase6Trx.Total} passed, elapsed={phase6Exec.TotalElapsed.TotalSeconds:F2}s");

            // -----------------------------------------------------------------
            // Phase 7: Host Invariance Verification
            // -----------------------------------------------------------------
            VerifyHostAddInsSettingsPostFallback(
                appData,
                revitVersion,
                addinsSettingsBefore,
                addinsPoliciesBefore);
            FileSnapshot programDataManifestAfter = CaptureFileSnapshot(programDataSyntheticManifest);
            Assert.That(
                programDataManifestAfter,
                Is.EqualTo(programDataManifestBefore),
                $"Host ProgramData manifest '{programDataSyntheticManifest}' was mutated.");
            AssertOnlyPreExistingRevitProcessesRunning(preExistingRevitPids);

            TestContext.Progress.WriteLine(
                $"[LiveSmoke {revitVersion}] Phase 7 PASS: host AddInsSettings.json policy/backups, ProgramData manifest, and pre-existing Revit PIDs untouched.");
        }
        finally
        {
            try
            {
                await RunPoolDrainAsync(poolExePath, repoRoot, testPipeName, force: true).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                TestContext.Progress.WriteLine($"[LiveSmoke {revitVersion}] Warning during finally drain: {ex.Message}");
            }

            Environment.SetEnvironmentVariable("SYNTHETIC_CLEAN_SHIM", priorCleanShimEnv);
            Environment.SetEnvironmentVariable("SYNTHETIC_WORKER_POOL_CMD", priorPoolCmdEnv);
            Environment.SetEnvironmentVariable("SYNTHETIC_WORKER_POOL_PIPE_NAME", priorPipeNameEnv);
        }
    }

    private static void VerifyDebugBootstrapManifest(string manifestPath, int revitVersion)
    {
        XDocument doc = XDocument.Load(manifestPath);
        string[] fullClasses = doc.Descendants()
            .Where(e => string.Equals(e.Name.LocalName, "FullClassName", StringComparison.Ordinal))
            .Select(e => e.Value.Trim())
            .ToArray();
        Assert.That(
            fullClasses,
            Contains.Item("Synthetic.Bootstrap.BootstrapApp"),
            $"Expected Synthetic.Bootstrap.BootstrapApp in '{manifestPath}'.");

        string[] assemblyPaths = doc.Descendants()
            .Where(e => string.Equals(e.Name.LocalName, "Assembly", StringComparison.Ordinal))
            .Select(e => e.Value.Trim())
            .Where(s => !string.IsNullOrEmpty(s))
            .ToArray();
        Assert.That(assemblyPaths, Is.Not.Empty, $"No <Assembly> found in '{manifestPath}'.");

        string expectedDllName = $"Synthetic.Bootstrap{revitVersion}.dll";
        string? bootstrapAssembly = assemblyPaths.FirstOrDefault(p =>
            string.Equals(Path.GetFileName(p), expectedDllName, StringComparison.OrdinalIgnoreCase));
        Assert.That(
            bootstrapAssembly,
            Is.Not.Null,
            $"Expected <Assembly> targeting '{expectedDllName}' in '{manifestPath}'.");
        Assert.That(
            File.Exists(bootstrapAssembly!),
            Is.True,
            $"BootstrapApp DLL missing at '{bootstrapAssembly}'.");
    }

    private static bool HasLegacyNonBootstrapProgramDataManifest(string programDataRoot, int revitVersion)
    {
        string addinsDir = Path.Combine(
            programDataRoot,
            "Autodesk",
            "Revit",
            "Addins",
            revitVersion.ToString(CultureInfo.InvariantCulture));
        if (!Directory.Exists(addinsDir))
        {
            return false;
        }

        foreach (string file in Directory.GetFiles(addinsDir, "Synthetic*.addin"))
        {
            try
            {
                XDocument doc = XDocument.Load(file);
                bool hasNonBootstrap = doc.Descendants()
                    .Where(e => string.Equals(e.Name.LocalName, "FullClassName", StringComparison.Ordinal))
                    .Any(e => !string.Equals(e.Value.Trim(), "Synthetic.Bootstrap.BootstrapApp", StringComparison.Ordinal));
                if (hasNonBootstrap)
                {
                    return true;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or XmlException)
            {
                // Ignore unparseable manifests during preflight discovery.
                TestContext.Progress.WriteLine($"[smoke] Warning: failed to inspect manifest '{file}': {ex.Message}");
            }
        }

        return false;
    }

    private static IReadOnlyDictionary<string, FileSnapshot> SnapshotHostAddInsSettings(string appDataRoot, int revitVersion)
    {
        var snapshots = new Dictionary<string, FileSnapshot>(StringComparer.OrdinalIgnoreCase);
        string autodeskRevitRoot = Path.Combine(appDataRoot, "Autodesk", "Revit");

        var candidateDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Path.Combine(autodeskRevitRoot, $"Autodesk Revit {revitVersion}")
        };

        if (Directory.Exists(autodeskRevitRoot))
        {
            foreach (string dir in Directory.GetDirectories(autodeskRevitRoot, "Autodesk Revit *"))
            {
                candidateDirs.Add(dir);
            }
        }

        foreach (string dir in candidateDirs)
        {
            string settingsPath = Path.Combine(dir, "AddinsData", "AddInsSettings.json");
            string backupPath = settingsPath + ".bak";
            Assert.That(File.Exists(backupPath), Is.False, $"Orphaned AddInsSettings backup detected at '{backupPath}'.");
            snapshots[Path.GetFullPath(settingsPath)] = CaptureFileSnapshot(settingsPath);
        }

        return snapshots;
    }

    private static void VerifyHostAddInsSettingsUnchanged(
        string appDataRoot,
        int revitVersion,
        IReadOnlyDictionary<string, FileSnapshot> beforeSnapshots)
    {
        IReadOnlyDictionary<string, FileSnapshot> afterSnapshots = SnapshotHostAddInsSettings(appDataRoot, revitVersion);

        foreach ((string path, FileSnapshot before) in beforeSnapshots)
        {
            Assert.That(afterSnapshots.TryGetValue(path, out FileSnapshot? after), Is.True, $"Missing post-run snapshot for '{path}'.");
            Assert.That(after, Is.EqualTo(before), $"Host AddInsSettings.json changed at '{path}' (before={before}, after={after}).");
        }

        foreach ((string path, FileSnapshot after) in afterSnapshots)
        {
            if (!beforeSnapshots.ContainsKey(path))
            {
                Assert.That(after.Exists, Is.False, $"Unexpected new AddInsSettings.json created at '{path}'.");
            }
        }
    }

    private static void VerifyHostAddInsSettingsPostFallback(
        string appDataRoot,
        int revitVersion,
        IReadOnlyDictionary<string, FileSnapshot> beforeSnapshots,
        IReadOnlyDictionary<string, AddInsSettingsPolicySnapshot> beforePolicies)
    {
        IReadOnlyDictionary<string, FileSnapshot> afterSnapshots = SnapshotHostAddInsSettings(appDataRoot, revitVersion);
        string activeVersionSettingsPath = Path.GetFullPath(
            Path.Combine(appDataRoot, "Autodesk", "Revit", $"Autodesk Revit {revitVersion}", "AddinsData", "AddInsSettings.json"));

        foreach ((string path, FileSnapshot before) in beforeSnapshots)
        {
            Assert.That(afterSnapshots.TryGetValue(path, out FileSnapshot? after), Is.True, $"Missing post-run snapshot for '{path}'.");

            bool isActiveUnshimmedTelemetryFile =
                revitVersion >= 2025 &&
                string.Equals(path, activeVersionSettingsPath, StringComparison.OrdinalIgnoreCase);

            if (!isActiveUnshimmedTelemetryFile)
            {
                Assert.That(after, Is.EqualTo(before), $"Host AddInsSettings.json changed at '{path}' (before={before}, after={after}).");
                continue;
            }

            // Unshimmed Revit 2025+ (--no-shim / --no-pool) natively runs APIInterop!updateAddInsSettings
            // to update LoadTime telemetry on startup, so verify policy invariance (DisableAllAddIns & Disabled flags).
            if (before.Exists && after!.Exists)
            {
                AddInsSettingsPolicySnapshot beforePolicy = beforePolicies[path];
                AddInsSettingsPolicySnapshot afterPolicy = ReadAddInsSettingsPolicy(path);
                Assert.That(
                    afterPolicy.DisableAllAddIns,
                    Is.EqualTo(beforePolicy.DisableAllAddIns),
                    $"Host DisableAllAddIns changed at '{path}'.");

                foreach ((string guid, bool wasDisabled) in beforePolicy.DisabledByGuid)
                {
                    Assert.That(
                        afterPolicy.DisabledByGuid.TryGetValue(guid, out bool nowDisabled),
                        Is.True,
                        $"Pre-existing add-in GUID '{guid}' disappeared from '{path}'.");
                    Assert.That(
                        nowDisabled,
                        Is.EqualTo(wasDisabled),
                        $"Pre-existing add-in GUID '{guid}' Disabled state changed in '{path}'.");
                }
            }
        }

        foreach ((string path, FileSnapshot after) in afterSnapshots)
        {
            if (!beforeSnapshots.ContainsKey(path))
            {
                Assert.That(after.Exists, Is.False, $"Unexpected new AddInsSettings.json created at '{path}'.");
            }
        }
    }

    private static IReadOnlyDictionary<string, AddInsSettingsPolicySnapshot> SnapshotAddInsSettingsPolicies(IEnumerable<string> paths)
    {
        var policies = new Dictionary<string, AddInsSettingsPolicySnapshot>(StringComparer.OrdinalIgnoreCase);
        foreach (string path in paths)
        {
            policies[path] = ReadAddInsSettingsPolicy(path);
        }

        return policies;
    }

    private static AddInsSettingsPolicySnapshot ReadAddInsSettingsPolicy(string path)
    {
        if (!File.Exists(path))
        {
            return new AddInsSettingsPolicySnapshot(
                DisableAllAddIns: false,
                new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase));
        }

        JObject obj = JObject.Parse(File.ReadAllText(path));
        bool disableAll = obj.Value<bool?>("DisableAllAddIns") ?? false;
        var disabledByGuid = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        if (obj["AddInItemSettings"] is JArray items)
        {
            foreach (JToken item in items)
            {
                string? guid = item.Value<string>("GUID");
                if (!string.IsNullOrWhiteSpace(guid))
                {
                    disabledByGuid[guid.Trim()] = item.Value<bool?>("Disabled") ?? false;
                }
            }
        }

        return new AddInsSettingsPolicySnapshot(disableAll, disabledByGuid);
    }

    private static FileSnapshot CaptureFileSnapshot(string filePath)
    {
        string fullPath = Path.GetFullPath(filePath);
        if (!File.Exists(fullPath))
        {
            return new FileSnapshot(Exists: false, LastWriteTimeUtc: DateTime.MinValue, Sha256Hex: string.Empty);
        }

        DateTime lastWriteUtc = File.GetLastWriteTimeUtc(fullPath);
        byte[] bytes = File.ReadAllBytes(fullPath);
        string sha256Hex = Convert.ToHexString(SHA256.HashData(bytes));
        return new FileSnapshot(Exists: true, LastWriteTimeUtc: lastWriteUtc, Sha256Hex: sha256Hex);
    }

    private static HashSet<int> GetRunningRevitPids()
    {
        var pids = new HashSet<int>();
        foreach (Process proc in Process.GetProcessesByName("Revit"))
        {
            try
            {
                pids.Add(proc.Id);
            }
            finally
            {
                proc.Dispose();
            }
        }

        return pids;
    }

    private static void AssertOnlyPreExistingRevitProcessesRunning(IReadOnlySet<int> preExistingRevitPids)
    {
        HashSet<int> current = GetRunningRevitPids();
        int[] lingeringWorkerPids = current.Where(pid => !preExistingRevitPids.Contains(pid)).OrderBy(p => p).ToArray();
        Assert.That(
            lingeringWorkerPids,
            Is.Empty,
            $"Lingering newly-spawned Revit.exe process(es) remain after drain: [{string.Join(", ", lingeringWorkerPids)}]");

        int[] missingPreExisting = preExistingRevitPids.Where(pid => !current.Contains(pid)).OrderBy(p => p).ToArray();
        Assert.That(
            missingPreExisting,
            Is.Empty,
            $"Pre-existing Revit.exe process(es) disappeared during test: [{string.Join(", ", missingPreExisting)}]");
    }

    private static Task StartForegroundPollingLoop(
        IReadOnlySet<int> preExistingRevitPids,
        ConcurrentQueue<string> stolenFocusEvents,
        Action onSample,
        CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            var pidNameCache = new Dictionary<int, string>();
            while (!cancellationToken.IsCancellationRequested)
            {
                onSample();
                IntPtr fgHwnd = NativeMethods.GetForegroundWindow();
                if (fgHwnd != IntPtr.Zero)
                {
                    NativeMethods.GetWindowThreadProcessId(fgHwnd, out uint rawPid);
                    int pid = unchecked((int)rawPid);
                    if (pid > 0 && !preExistingRevitPids.Contains(pid))
                    {
                        if (!pidNameCache.TryGetValue(pid, out string? procName))
                        {
                            procName = TryGetProcessName(pid);
                            pidNameCache[pid] = procName;
                        }

                        if (string.Equals(procName, "Revit", StringComparison.OrdinalIgnoreCase))
                        {
                            stolenFocusEvents.Enqueue(
                                $"[{DateTimeOffset.UtcNow:O}] PID={pid} HWND=0x{fgHwnd.ToInt64():X} Title='{GetWindowTitle(fgHwnd)}'");
                        }
                    }
                }

                Thread.Sleep(15);
            }
        }, CancellationToken.None);
    }

    private static string TryGetProcessName(int pid)
    {
        try
        {
            using Process p = Process.GetProcessById(pid);
            return p.ProcessName;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // Process may have exited between GetWindowThreadProcessId and GetProcessById.
            _ = ex;
            return string.Empty;
        }
    }

    private static string GetWindowTitle(IntPtr hwnd)
    {
        var sb = new StringBuilder(256);
        _ = NativeMethods.GetWindowTextW(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    private static async Task<DrainResult> RunPoolDrainAsync(
        string poolExePath,
        string repoRoot,
        string pipeName,
        bool force = false)
    {
        var args = new List<string> { "drain", "--pipe-name", pipeName, "--json" };
        if (force)
        {
            args.Add("--force");
        }

        ProcessRunOutput drainOut = await RunProcessAsync(
            poolExePath,
            args,
            repoRoot,
            TimeSpan.FromSeconds(30),
            clearCleanShimEnv: true).ConfigureAwait(false);

        Assert.That(drainOut.TimedOut, Is.False, "RevitWorkerPool drain timed out.");
        Assert.That(drainOut.ExitCode, Is.EqualTo(0), $"RevitWorkerPool drain failed:\n{drainOut.Stderr}\n{drainOut.Stdout}");
        DrainResult? parsed = ParseLastJsonObject<DrainResult>(drainOut.Stdout);
        Assert.That(parsed, Is.Not.Null, $"Could not parse DrainResult JSON:\n{drainOut.Stdout}");
        return parsed!;
    }

    private static async Task<PoolStatusSnapshot> RunPoolStatusAsync(
        string poolExePath,
        string repoRoot,
        string pipeName)
    {
        ProcessRunOutput statusOut = await RunProcessAsync(
            poolExePath,
            new[] { "status", "--pipe-name", pipeName, "--json" },
            repoRoot,
            TimeSpan.FromSeconds(30),
            clearCleanShimEnv: true).ConfigureAwait(false);

        Assert.That(statusOut.TimedOut, Is.False, "RevitWorkerPool status timed out.");
        Assert.That(statusOut.ExitCode, Is.EqualTo(0), $"RevitWorkerPool status failed:\n{statusOut.Stderr}\n{statusOut.Stdout}");
        PoolStatusSnapshot? parsed = ParseLastJsonObject<PoolStatusSnapshot>(statusOut.Stdout);
        Assert.That(parsed, Is.Not.Null, $"Could not parse PoolStatusSnapshot JSON:\n{statusOut.Stdout}");
        return parsed!;
    }

    private static async Task<string> AssertJournalCleanViaToolAsync(
        string journalToolPath,
        string repoRoot,
        int revitVersion,
        string workerId)
    {
        ProcessRunOutput gateOut = await RunProcessAsync(
            "python",
            new[]
            {
                "-u",
                journalToolPath,
                "--version", revitVersion.ToString(CultureInfo.InvariantCulture),
                "--worker-id", workerId,
                "--assert-clean"
            },
            repoRoot,
            TimeSpan.FromSeconds(30),
            clearCleanShimEnv: true).ConfigureAwait(false);

        Assert.That(gateOut.TimedOut, Is.False, $"revit_journal_tool.py timed out for worker '{workerId}'.");
        Assert.That(
            gateOut.ExitCode,
            Is.EqualTo(0),
            $"revit_journal_tool.py --assert-clean failed for worker '{workerId}':\n{gateOut.Stdout}\n{gateOut.Stderr}");
        Assert.That(gateOut.Stdout, Does.Contain("Cleanliness Gate Result: CLEAN"));

        string? journalPath = null;
        foreach (string line in gateOut.Stdout.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            string trimmed = line.Trim();
            if (trimmed.StartsWith("Target Journal:", StringComparison.Ordinal))
            {
                journalPath = trimmed["Target Journal:".Length..].Trim();
                break;
            }
        }

        Assert.That(journalPath, Is.Not.Null.And.Not.Empty, $"Could not parse Target Journal path from:\n{gateOut.Stdout}");
        Assert.That(File.Exists(journalPath!), Is.True, $"Resolved journal file does not exist: '{journalPath}'.");
        return journalPath!;
    }

    private static async Task<string> AssertCleanShimmedJournalAsync(
        string journalToolPath,
        string repoRoot,
        int revitVersion,
        string workerId)
    {
        string journalPath = await AssertJournalCleanViaToolAsync(
            journalToolPath,
            repoRoot,
            revitVersion,
            workerId).ConfigureAwait(false);

        string[] lines = ReadJournalLines(journalPath);
        string fullText = string.Join(Environment.NewLine, lines);

        string workerMarkerRegex = $@"SYNTHETIC_WORKER_ID:{Regex.Escape(workerId)}(?![A-Za-z0-9_.-])";
        Assert.That(
            Regex.IsMatch(fullText, workerMarkerRegex, RegexOptions.CultureInvariant),
            Is.True,
            $"Journal '{journalPath}' missing SYNTHETIC_WORKER_ID:{workerId}.");

        Assert.That(
            fullText,
            Does.Contain("Starting External Application: Synthetic, Class: Synthetic.Bootstrap.BootstrapApp"),
            $"Journal '{journalPath}' missing BootstrapApp startup line.");

        Assert.That(
            fullText,
            Does.Not.Contain("SYNTHETIC_BOOTSTRAP: Loaded"),
            $"Journal '{journalPath}' unexpectedly loaded host payload during test worker boot.");

        var suspiciousAddinLines = new List<string>();
        var forbiddenWholeJournalMatches = new List<string>();

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];
            if (AddinLoadUiErrorPrefixes.Any(prefix => line.Contains(prefix, StringComparison.OrdinalIgnoreCase)) &&
                ThirdPartyAddinLineTokenRegex.IsMatch(line))
            {
                suspiciousAddinLines.Add($"Line {i + 1}: {line.Trim()}");
            }

            foreach (string token in ForbiddenWholeJournalTokens)
            {
                if (line.Contains(token, StringComparison.OrdinalIgnoreCase))
                {
                    forbiddenWholeJournalMatches.Add($"Line {i + 1} [{token}]: {line.Trim()}");
                }
            }
        }

        Assert.That(
            suspiciousAddinLines,
            Is.Empty,
            $"Third-party/eTransmit add-in activity detected in shimmed journal '{journalPath}':\n{string.Join(Environment.NewLine, suspiciousAddinLines)}");

        Assert.That(
            forbiddenWholeJournalMatches,
            Is.Empty,
            $"Forbidden third-party/eTransmit manifest or assembly name detected in shimmed journal '{journalPath}':\n{string.Join(Environment.NewLine, forbiddenWholeJournalMatches)}");

        return journalPath;
    }

    private static string[] ReadJournalLines(string journalPath)
    {
        using var reader = new StreamReader(journalPath, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var lines = new List<string>();
        while (reader.ReadLine() is string line)
        {
            lines.Add(line);
        }

        return lines.ToArray();
    }

    private static HashSet<string> SnapshotDirectoryNames(string rootDir)
    {
        if (!Directory.Exists(rootDir))
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        return Directory.GetDirectories(rootDir)
            .Where(d => RunDirPattern.IsMatch(Path.GetFileName(d)))
            .Select(Path.GetFullPath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static string FindSingleNewRunDirectory(string rootDir, IReadOnlySet<string> priorDirs)
    {
        Assert.That(Directory.Exists(rootDir), Is.True, $"Missing TestResults directory '{rootDir}'.");
        string[] newDirs = Directory.GetDirectories(rootDir)
            .Where(d => RunDirPattern.IsMatch(Path.GetFileName(d)))
            .Select(Path.GetFullPath)
            .Where(d => !priorDirs.Contains(d))
            .ToArray();

        Assert.That(
            newDirs.Length,
            Is.EqualTo(1),
            $"Expected exactly 1 new TestResults run directory under '{rootDir}', found {newDirs.Length}: [{string.Join(", ", newDirs)}]");
        return newDirs[0];
    }

    private static OneShotRunResult ParseOutputLogPoolResult(string outputLogPath)
    {
        Assert.That(File.Exists(outputLogPath), Is.True, $"Missing pool output log at '{outputLogPath}'.");
        string text = File.ReadAllText(outputLogPath);

        const string marker = "=== Worker Pool Result Summary ===";
        int markerIndex = text.IndexOf(marker, StringComparison.Ordinal);
        Assert.That(markerIndex, Is.GreaterThanOrEqualTo(0), $"Marker '{marker}' not found in '{outputLogPath}'.");

        string tail = text[(markerIndex + marker.Length)..];
        int braceIndex = tail.IndexOf('{');
        Assert.That(braceIndex, Is.GreaterThanOrEqualTo(0), $"JSON object not found after '{marker}' in '{outputLogPath}'.");

        OneShotRunResult? result = JsonConvert.DeserializeObject<OneShotRunResult>(tail[braceIndex..]);
        Assert.That(result, Is.Not.Null, $"Failed to deserialize OneShotRunResult from '{outputLogPath}'.");
        return result!;
    }

    private static TrxSummaryInspection InspectTrxFile(string trxPath)
    {
        Assert.That(File.Exists(trxPath), Is.True, $"Missing TRX report at '{trxPath}'.");
        XDocument doc = XDocument.Load(trxPath);
        XNamespace ns = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";

        XElement counters = doc.Descendants(ns + "Counters").Single();
        int total = int.Parse((string?)counters.Attribute("total") ?? "0", CultureInfo.InvariantCulture);
        int passed = int.Parse((string?)counters.Attribute("passed") ?? "0", CultureInfo.InvariantCulture);
        int failed = int.Parse((string?)counters.Attribute("failed") ?? "0", CultureInfo.InvariantCulture);

        List<XElement> unitTestResults = doc.Descendants(ns + "UnitTestResult").ToList();
        var testNames = new List<string>(unitTestResults.Count);
        double totalDurationMs = 0.0;

        foreach (XElement ut in unitTestResults)
        {
            string testName = (string?)ut.Attribute("testName") ?? string.Empty;
            testNames.Add(testName);

            string? durStr = (string?)ut.Attribute("duration");
            if (!string.IsNullOrWhiteSpace(durStr) &&
                TimeSpan.TryParse(durStr, CultureInfo.InvariantCulture, out TimeSpan dur))
            {
                totalDurationMs += dur.TotalMilliseconds;
            }
        }

        return new TrxSummaryInspection(total, passed, failed, unitTestResults.Count, totalDurationMs, testNames);
    }

    private static T? ParseLastJsonObject<T>(string stdoutText) where T : class
    {
        if (string.IsNullOrWhiteSpace(stdoutText))
        {
            return null;
        }

        string[] lines = stdoutText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        for (int i = lines.Length - 1; i >= 0; i--)
        {
            string trimmed = lines[i].Trim();
            if (trimmed.StartsWith("{", StringComparison.Ordinal))
            {
                return JsonConvert.DeserializeObject<T>(trimmed);
            }
        }

        return null;
    }

    private static async Task<ProcessRunOutput> RunProcessAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        TimeSpan timeout,
        bool clearCleanShimEnv,
        Action<TimeSpan, string>? onStdoutLine = null,
        IReadOnlyDictionary<string, string?>? extraEnv = null)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        foreach (string arg in arguments)
        {
            psi.ArgumentList.Add(arg);
        }

        psi.Environment["PYTHONUNBUFFERED"] = "1";
        if (clearCleanShimEnv)
        {
            psi.Environment.Remove("SYNTHETIC_CLEAN_SHIM");
            psi.Environment.Remove("SYNTHETIC_WORKER_POOL_CMD");
            psi.Environment.Remove("SYNTHETIC_WORKER_POOL_PIPE_NAME");
        }

        if (extraEnv is not null)
        {
            foreach ((string key, string? val) in extraEnv)
            {
                if (val is null)
                {
                    psi.Environment.Remove(key);
                }
                else
                {
                    psi.Environment[key] = val;
                }
            }
        }

        var stdoutBuilder = new StringBuilder();
        var stderrBuilder = new StringBuilder();
        var stdoutClosed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var stderrClosed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var sw = Stopwatch.StartNew();

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null)
            {
                stdoutClosed.TrySetResult(true);
                return;
            }

            TimeSpan elapsed = sw.Elapsed;
            lock (stdoutBuilder)
            {
                stdoutBuilder.AppendLine(e.Data);
            }

            onStdoutLine?.Invoke(elapsed, e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null)
            {
                stderrClosed.TrySetResult(true);
                return;
            }

            lock (stderrBuilder)
            {
                stderrBuilder.AppendLine(e.Data);
            }
        };

        if (!process.Start())
        {
            throw new InvalidOperationException($"Failed to start process '{fileName}'.");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        bool timedOut = false;
        using var timeoutCts = new CancellationTokenSource(timeout);
        try
        {
            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            timedOut = true;
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                // Best-effort process tree termination on test timeout.
                TestContext.Progress.WriteLine($"[smoke] Warning: failed to kill timed-out process '{fileName}': {ex.Message}");
            }

            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
        }

        TimeSpan totalElapsed = sw.Elapsed;
        await Task.WhenAll(stdoutClosed.Task, stderrClosed.Task).WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);

        string stdoutText;
        lock (stdoutBuilder)
        {
            stdoutText = stdoutBuilder.ToString();
        }

        string stderrText;
        lock (stderrBuilder)
        {
            stderrText = stderrBuilder.ToString();
        }

        return new ProcessRunOutput(process.ExitCode, timedOut, totalElapsed, stdoutText, stderrText);
    }

    private sealed record FileSnapshot(bool Exists, DateTime LastWriteTimeUtc, string Sha256Hex);

    private sealed record AddInsSettingsPolicySnapshot(
        bool DisableAllAddIns,
        IReadOnlyDictionary<string, bool> DisabledByGuid);

    private sealed record TrxSummaryInspection(
        int Total,
        int Passed,
        int Failed,
        int UnitTestResultCount,
        double TotalDurationMs,
        IReadOnlyList<string> TestNames);

    private sealed record ProcessRunOutput(
        int ExitCode,
        bool TimedOut,
        TimeSpan TotalElapsed,
        string Stdout,
        string Stderr);

    private static class NativeMethods
    {
        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetWindowTextW(IntPtr hWnd, StringBuilder lpString, int nMaxCount);
    }
}
