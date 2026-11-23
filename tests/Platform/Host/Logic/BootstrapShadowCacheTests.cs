using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using NUnit.Framework;
using Synthetic.Bootstrap;

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
    }
}
