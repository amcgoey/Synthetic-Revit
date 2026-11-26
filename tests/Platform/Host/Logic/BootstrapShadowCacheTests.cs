using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using Synthetic.Bootstrap;
using SyntheticTests.Helpers;

namespace SyntheticTests.Platform.Host
{
    [TestFixture]
    [UnitTest]
    [NonParallelizable]
    public class BootstrapShadowCacheTests
    {
        private string _tempRoot = string.Empty;
        private string _bootstrapDllPath = string.Empty;
        private string _payloadDir = string.Empty;
        private string _localAppDataDir = string.Empty;

        [SetUp]
        public void SetUp()
        {
            _tempRoot = Path.Combine(Path.GetTempPath(), "SyntheticShadowCacheTests", Guid.NewGuid().ToString("N"));
            _bootstrapDllPath = Path.Combine(_tempRoot, "Bootstrap", "v1", "Synthetic.Bootstrap2026.dll");
            _payloadDir = Path.Combine(_tempRoot, "Payload", "2026");
            _localAppDataDir = Path.Combine(_tempRoot, "LocalAppData");

            Directory.CreateDirectory(Path.GetDirectoryName(_bootstrapDllPath)!);
            Directory.CreateDirectory(_localAppDataDir);
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                if (Directory.Exists(_tempRoot))
                {
                    Directory.Delete(_tempRoot, recursive: true);
                }
            }
            catch
            {
                // Best-effort temp cleanup
            }
        }

        private void SeedPayload(bool includeAssets = true)
        {
            Directory.CreateDirectory(_payloadDir);
            File.WriteAllText(Path.Combine(_payloadDir, "Synthetic2026.dll"), "payload-dll-bytes");
            File.WriteAllText(Path.Combine(_payloadDir, "Synthetic.Core.dll"), "core-dll-bytes");
            File.WriteAllText(Path.Combine(_payloadDir, "Newtonsoft.Json.dll"), "newtonsoft-bytes");
            if (includeAssets)
            {
                string assetsDir = Path.Combine(_payloadDir, "Assets", "Icons");
                Directory.CreateDirectory(assetsDir);
                File.WriteAllText(Path.Combine(_payloadDir, "Assets", "ribbon_config.json"), "{}");
                File.WriteAllText(Path.Combine(assetsDir, "icon.png"), "png-bytes");
            }
        }

        private static Func<string, string?> EnvMap(IDictionary<string, string?> vars)
        {
            return key => vars.TryGetValue(key, out string? val) ? val : null;
        }

        private static object? GetAddinRootData()
        {
#if NETFRAMEWORK
            return AppDomain.CurrentDomain.GetData("SYNTHETIC_ADDIN_ROOT");
#else
            return AppContext.GetData("SYNTHETIC_ADDIN_ROOT");
#endif
        }

        [Test]
        public void PrepareLaunch_TestWorkerBypass_ZeroCopyAndZeroScavenge()
        {
            SeedPayload();
            string deadSibling = Path.Combine(_localAppDataDir, "Synthetic", "ShadowCache", "2026", "999999_123456");
            Directory.CreateDirectory(deadSibling);
            File.WriteAllText(Path.Combine(deadSibling, "stale.txt"), "keep-me");

            object? priorAppContext = GetAddinRootData();

            LaunchPlan plan = ShadowCacheManager.PrepareLaunch(
                _bootstrapDllPath,
                "2026",
                new ShadowCacheHooks
                {
                    GetEnvironmentVariable = EnvMap(new Dictionary<string, string?>
                    {
                        { "SYNTHETIC_TEST_WORKER", "1" }
                    }),
                    LocalAppDataDir = _localAppDataDir,
                    CurrentProcessId = 4242,
                    CurrentProcessUtcStartTicks = 987654321L
                });

            Assert.AreEqual(LaunchMode.TestWorkerBypass, plan.Mode);
            Assert.IsEmpty(plan.ShadowDir);
            Assert.IsEmpty(plan.TargetAssemblyPath);
            Assert.IsTrue(Directory.Exists(deadSibling), "TestWorkerBypass must not scavenge sibling shadow directories.");
            Assert.IsFalse(Directory.Exists(Path.Combine(_localAppDataDir, "Synthetic", "ShadowCache", "2026", "4242_987654321")));
            Assert.AreEqual(priorAppContext, GetAddinRootData());
        }

        [Test]
        public void PrepareLaunch_ShadowCopy_StagesPayloadRecursivelyIntoPidAndTicksDir()
        {
            SeedPayload(includeAssets: true);
            object? priorAppContext = GetAddinRootData();

            LaunchPlan plan = ShadowCacheManager.PrepareLaunch(
                _bootstrapDllPath,
                "2026",
                new ShadowCacheHooks
                {
                    GetEnvironmentVariable = EnvMap(new Dictionary<string, string?>()),
                    LocalAppDataDir = _localAppDataDir,
                    CurrentProcessId = 1234,
                    CurrentProcessUtcStartTicks = 638999999999999999L
                });

            string expectedShadowDir = Path.Combine(
                _localAppDataDir,
                "Synthetic",
                "ShadowCache",
                "2026",
                "1234_638999999999999999");

            Assert.AreEqual(LaunchMode.ShadowCopy, plan.Mode);
            Assert.AreEqual(Path.GetFullPath(_payloadDir), plan.SourcePayloadDir);
            Assert.AreEqual(expectedShadowDir, plan.ShadowDir);
            Assert.AreEqual(Path.Combine(expectedShadowDir, "Synthetic2026.dll"), plan.TargetAssemblyPath);
            Assert.IsTrue(File.Exists(plan.TargetAssemblyPath));
            Assert.IsTrue(File.Exists(Path.Combine(expectedShadowDir, "Synthetic.Core.dll")));
            Assert.IsTrue(File.Exists(Path.Combine(expectedShadowDir, "Newtonsoft.Json.dll")));
            Assert.IsTrue(File.Exists(Path.Combine(expectedShadowDir, "Assets", "ribbon_config.json")));
            Assert.IsTrue(File.Exists(Path.Combine(expectedShadowDir, "Assets", "Icons", "icon.png")));
            Assert.AreEqual(priorAppContext, GetAddinRootData(), "ShadowCacheManager must not mutate AppContext.");
        }

        [Test]
        public void PrepareLaunch_ScavengesDeadAndTickMismatchedSiblings_PreservesLiveAndAccessDenied()
        {
            SeedPayload();
            string yearCacheRoot = Path.Combine(_localAppDataDir, "Synthetic", "ShadowCache", "2026");

            string deadPidDir = Path.Combine(yearCacheRoot, "1001_111111");
            string reusedPidDir = Path.Combine(yearCacheRoot, "1002_222222");
            string livePidDir = Path.Combine(yearCacheRoot, "1003_333333");
            string elevatedWin32Dir = Path.Combine(yearCacheRoot, "1004_444444");
            string elevatedUnauthDir = Path.Combine(yearCacheRoot, "1005_555555");
            string nonSessionDir = Path.Combine(yearCacheRoot, "custom_notes");

            foreach (string dir in new[] { deadPidDir, reusedPidDir, livePidDir, elevatedWin32Dir, elevatedUnauthDir, nonSessionDir })
            {
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "marker.txt"), "x");
            }

            LaunchPlan plan = ShadowCacheManager.PrepareLaunch(
                _bootstrapDllPath,
                "2026",
                new ShadowCacheHooks
                {
                    GetEnvironmentVariable = EnvMap(new Dictionary<string, string?>()),
                    LocalAppDataDir = _localAppDataDir,
                    CurrentProcessId = 2000,
                    CurrentProcessUtcStartTicks = 999999L,
                    GetProcessUtcStartTicks = pid =>
                    {
                        switch (pid)
                        {
                            case 1001:
                                throw new ArgumentException("Process not running.");
                            case 1002:
                                return 777777L; // Mismatch -> PID reused
                            case 1003:
                                return 333333L; // Live match -> preserve
                            case 1004:
                                throw new Win32Exception(5, "Access is denied");
                            case 1005:
                                throw new UnauthorizedAccessException("Access denied");
                            default:
                                throw new InvalidOperationException($"Unexpected PID {pid}");
                        }
                    }
                });

            Assert.AreEqual(LaunchMode.ShadowCopy, plan.Mode);
            Assert.IsFalse(Directory.Exists(deadPidDir), "Dead PID sibling folder should be scavenged.");
            Assert.IsFalse(Directory.Exists(reusedPidDir), "Tick-mismatched (reused PID) sibling folder should be scavenged.");
            Assert.IsTrue(Directory.Exists(livePidDir), "Live matching PID folder must be preserved.");
            Assert.IsTrue(Directory.Exists(elevatedWin32Dir), "Elevated PID throwing Win32Exception(5) must be conservatively preserved.");
            Assert.IsTrue(Directory.Exists(elevatedUnauthDir), "Elevated PID throwing UnauthorizedAccessException must be conservatively preserved.");
            Assert.IsTrue(Directory.Exists(nonSessionDir), "Non-session folder must not be deleted.");
        }

        [Test]
        public void PrepareLaunch_CopyFailure_CleansUpPartialShadowDirAndReturnsFailed()
        {
            SeedPayload(includeAssets: true);
            string expectedShadowDir = Path.Combine(_localAppDataDir, "Synthetic", "ShadowCache", "2026", "5555_12345");
            int copyCount = 0;

            LaunchPlan plan = ShadowCacheManager.PrepareLaunch(
                _bootstrapDllPath,
                "2026",
                new ShadowCacheHooks
                {
                    GetEnvironmentVariable = EnvMap(new Dictionary<string, string?>()),
                    LocalAppDataDir = _localAppDataDir,
                    CurrentProcessId = 5555,
                    CurrentProcessUtcStartTicks = 12345L,
                    CopyFile = (src, dst) =>
                    {
                        copyCount++;
                        if (copyCount == 1)
                        {
                            File.Copy(src, dst, overwrite: true);
                            return;
                        }
                        throw new IOException("Simulated disk write failure.");
                    }
                });

            Assert.AreEqual(LaunchMode.Failed, plan.Mode);
            Assert.IsEmpty(plan.ShadowDir);
            Assert.IsEmpty(plan.TargetAssemblyPath);
            StringAssert.Contains("Simulated disk write failure", plan.ErrorMessage);
            Assert.IsFalse(Directory.Exists(expectedShadowDir), "Partial shadow directory must be deleted on copy failure.");
        }

        [Test]
        public void PrepareLaunch_DisableShadowCopy_ResolvesDirectInPlace()
        {
            SeedPayload();

            LaunchPlan plan = ShadowCacheManager.PrepareLaunch(
                _bootstrapDllPath,
                "2026",
                new ShadowCacheHooks
                {
                    GetEnvironmentVariable = EnvMap(new Dictionary<string, string?>
                    {
                        { "SYNTHETIC_DISABLE_SHADOW_COPY", "1" }
                    }),
                    LocalAppDataDir = _localAppDataDir,
                    CurrentProcessId = 7777,
                    CurrentProcessUtcStartTicks = 8888L
                });

            Assert.AreEqual(LaunchMode.DirectInPlace, plan.Mode);
            Assert.AreEqual(Path.GetFullPath(_payloadDir), plan.SourcePayloadDir);
            Assert.IsEmpty(plan.ShadowDir);
            Assert.AreEqual(Path.Combine(Path.GetFullPath(_payloadDir), "Synthetic2026.dll"), plan.TargetAssemblyPath);
            Assert.IsFalse(Directory.Exists(Path.Combine(_localAppDataDir, "Synthetic", "ShadowCache")));
        }

        [Test]
        public void PrepareLaunch_MissingPayload_ReturnsFailedWithZeroDirectoryCreation()
        {
            LaunchPlan shadowPlan = ShadowCacheManager.PrepareLaunch(
                _bootstrapDllPath,
                "2026",
                new ShadowCacheHooks
                {
                    GetEnvironmentVariable = EnvMap(new Dictionary<string, string?>()),
                    LocalAppDataDir = _localAppDataDir,
                    CurrentProcessId = 8888,
                    CurrentProcessUtcStartTicks = 9999L
                });

            Assert.AreEqual(LaunchMode.Failed, shadowPlan.Mode);
            StringAssert.Contains("Synthetic2026.dll", shadowPlan.ErrorMessage);
            Assert.IsFalse(Directory.Exists(Path.Combine(_localAppDataDir, "Synthetic")));

            LaunchPlan directPlan = ShadowCacheManager.PrepareLaunch(
                _bootstrapDllPath,
                "2026",
                new ShadowCacheHooks
                {
                    GetEnvironmentVariable = EnvMap(new Dictionary<string, string?>
                    {
                        { "SYNTHETIC_DISABLE_SHADOW_COPY", "1" }
                    }),
                    LocalAppDataDir = _localAppDataDir
                });

            Assert.AreEqual(LaunchMode.Failed, directPlan.Mode);
            StringAssert.Contains("Synthetic2026.dll", directPlan.ErrorMessage);
            Assert.IsFalse(Directory.Exists(Path.Combine(_localAppDataDir, "Synthetic")));
        }

        [Test]
        public void TryResolveRevitVersion_PrioritizesHostVersion_FallsBackToMetadata_AndFailsFast()
        {
            Assembly metadata2027 = DynamicAssemblyTestHelper.CreateDynamicAssembly(null, ("RevitVersion", " 2027 "));

            // 1. Live host version overrides assembly metadata
            Assert.IsTrue(ShadowCacheManager.TryResolveRevitVersion(" 2026 ", metadata2027, out string resolvedHost, out string hostErr));
            Assert.AreEqual("2026", resolvedHost);
            Assert.IsEmpty(hostErr);

            // 2. Null or whitespace host version falls back to assembly metadata
            Assert.IsTrue(ShadowCacheManager.TryResolveRevitVersion(null, metadata2027, out string resolvedNull, out string nullErr));
            Assert.AreEqual("2027", resolvedNull);
            Assert.IsEmpty(nullErr);

            Assert.IsTrue(ShadowCacheManager.TryResolveRevitVersion("   ", metadata2027, out string resolvedBlank, out string blankErr));
            Assert.AreEqual("2027", resolvedBlank);
            Assert.IsEmpty(blankErr);

            // 3. Invalid host version falls back to assembly metadata
            Assert.IsTrue(ShadowCacheManager.TryResolveRevitVersion("26", metadata2027, out string resolvedInvalidHost, out string invalidHostErr));
            Assert.AreEqual("2027", resolvedInvalidHost);
            Assert.IsEmpty(invalidHostErr);

            // 4. Missing or unparseable metadata fails cleanly without silent fallback
            Assembly missingMetadata = DynamicAssemblyTestHelper.CreateDynamicAssembly(null, ("OtherKey", "2027"));
            Assembly badAlphaMetadata = DynamicAssemblyTestHelper.CreateDynamicAssembly(null, ("RevitVersion", "not-a-year"));
            Assembly twoDigitMetadata = DynamicAssemblyTestHelper.CreateDynamicAssembly(null, ("RevitVersion", "27"));
            Assembly blankMetadata = DynamicAssemblyTestHelper.CreateDynamicAssembly(null, ("RevitVersion", "   "));

            foreach (Assembly brokenAsm in new[] { missingMetadata, badAlphaMetadata, twoDigitMetadata, blankMetadata })
            {
                Assert.IsFalse(ShadowCacheManager.TryResolveRevitVersion("   ", brokenAsm, out string failedVersion, out string err));
                Assert.IsEmpty(failedVersion);
                StringAssert.Contains("RevitVersion", err);
            }
        }

        [Test]
        public void PrepareHostLaunch_PrioritizesHost_FallsBackToMetadata_AndFailsWithDiagnostics()
        {
            SeedPayload();
            string payload2027Dir = Path.Combine(_tempRoot, "Payload", "2027");
            Directory.CreateDirectory(payload2027Dir);
            File.WriteAllText(Path.Combine(payload2027Dir, "Synthetic2027.dll"), "payload-2027-bytes");

            Assembly metadata2027 = DynamicAssemblyTestHelper.CreateDynamicAssembly(null, ("RevitVersion", "2027"));

            ShadowCacheHooks CreateHooks() => new ShadowCacheHooks
            {
                GetEnvironmentVariable = EnvMap(new Dictionary<string, string?>()),
                LocalAppDataDir = _localAppDataDir,
                CurrentProcessId = 31337,
                CurrentProcessUtcStartTicks = 638999999999999999L
            };

            // 1. Host version overrides metadata ("2026" wins over "2027")
            LaunchPlan hostOverridePlan = ShadowCacheManager.PrepareHostLaunch(
                "2026",
                metadata2027,
                _bootstrapDllPath,
                out string overrideVersion,
                CreateHooks());

            Assert.AreEqual("2026", overrideVersion);
            Assert.AreEqual(LaunchMode.ShadowCopy, hostOverridePlan.Mode);
            StringAssert.EndsWith("Synthetic2026.dll", hostOverridePlan.TargetAssemblyPath);

            // 2. Null or whitespace host version falls back to metadata ("2027")
            LaunchPlan fallbackPlan = ShadowCacheManager.PrepareHostLaunch(
                "   ",
                metadata2027,
                _bootstrapDllPath,
                out string fallbackVersion,
                CreateHooks());

            Assert.AreEqual("2027", fallbackVersion);
            Assert.AreEqual(LaunchMode.ShadowCopy, fallbackPlan.Mode);
            StringAssert.EndsWith("Synthetic2027.dll", fallbackPlan.TargetAssemblyPath);

            // 3. Missing or unparseable metadata returns Failed LaunchPlan with diagnostics
            Assembly missingMetadata = DynamicAssemblyTestHelper.CreateDynamicAssembly(null, ("OtherKey", "2027"));
            Assembly badMetadata = DynamicAssemblyTestHelper.CreateDynamicAssembly(null, ("RevitVersion", "bad-year"));

            foreach (Assembly brokenAsm in new[] { missingMetadata, badMetadata })
            {
                LaunchPlan failedPlan = ShadowCacheManager.PrepareHostLaunch(
                    null,
                    brokenAsm,
                    _bootstrapDllPath,
                    out string failedVersion,
                    CreateHooks());

                Assert.AreEqual(string.Empty, failedVersion);
                Assert.AreEqual(LaunchMode.Failed, failedPlan.Mode);
                StringAssert.Contains("RevitVersion", failedPlan.ErrorMessage);
                Assert.IsNotEmpty(failedPlan.Diagnostics);
            }
        }
    }
}
