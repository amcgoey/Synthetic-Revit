using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using NUnit.Framework;
using Synthetic.Features.StandardsManagement.Views;
using Synthetic.Platform.Host;
using Synthetic.Platform.Revit;
using SyntheticTests.Fixtures;
using SyntheticTests.Helpers;

namespace SyntheticTests.Platform.Host
{
    [TestFixture]
    public class BootstrapWorkerIsolationRevitTests
    {
        private UIApplication? _uiapp;
        private int _initialDocCount;

        [OneTimeSetUp]
        public void Setup(UIApplication uiapp)
        {
            _uiapp = uiapp;
            HostSettingsRegistrar.EnsureRegistered();
            TestWorkerJournalMarker.EnsureWritten(uiapp);
            _initialDocCount = uiapp.Application.Documents.Size;
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            if (_uiapp?.Application != null)
            {
                Assert.AreEqual(
                    _initialDocCount,
                    _uiapp.Application.Documents.Size,
                    "BootstrapWorkerIsolationRevitTests leaked open Revit document(s).");
            }
        }

        [Test]
        [RevitSmokeTest]
        public void TestWorker_BypassesHostStartup_AndLoadsUnshadowedTempPayload()
        {
            Assert.AreEqual(
                "1",
                Environment.GetEnvironmentVariable("SYNTHETIC_TEST_WORKER"),
                "Revit test workers must run with SYNTHETIC_TEST_WORKER=1.");

            Assert.IsNull(
                App.AppControlled,
                "App.AppControlled must remain null when BootstrapApp bypasses host startup in test workers.");

            string expectedTempRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "RevitTest"))
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;

            string addinLocation = typeof(App).Assembly.Location;
            Assert.IsFalse(
                string.IsNullOrWhiteSpace(addinLocation),
                "Loaded Synthetic add-in payload assembly must have a non-empty disk path.");
            Assert.IsTrue(
                Path.GetFullPath(addinLocation).StartsWith(expectedTempRoot, StringComparison.OrdinalIgnoreCase),
                $"Expected unshadowed payload under '{expectedTempRoot}', but loaded '{addinLocation}'.");

            string coreLocation = typeof(RibbonItemFilter).Assembly.Location;
            Assert.IsFalse(
                string.IsNullOrWhiteSpace(coreLocation),
                "Loaded Synthetic.Core assembly must have a non-empty disk path.");
            Assert.IsTrue(
                Path.GetFullPath(coreLocation).StartsWith(expectedTempRoot, StringComparison.OrdinalIgnoreCase),
                $"Expected unshadowed Synthetic.Core under '{expectedTempRoot}', but loaded '{coreLocation}'.");
        }

        [Test]
        [RevitSmokeTest]
        public void TestWorker_ProjectStandardsDashboardWindow_InitializesXamlAndThemeCleanly()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");

            using (var harness = new WpfBindingSmokeHarness())
            {
                var window = new ProjectStandardsDashboardWindow(_uiapp!.MainWindowHandle);
                WpfBindingSmokeHarness.ExerciseWindow(window);
                harness.AssertNoBindingErrors("ProjectStandardsDashboardWindow");
            }
        }

        [Test]
        [RevitIsolatedDocTest]
        public void TestWorker_DocumentUtilPurge_ResolvesETransmitWithoutHostStartup()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");

            string tempDir = RevitIsolatedDocumentFixture.CreateIsolatedRunDirectory();
            Document? doc = null;
            try
            {
                string templatePath = RevitIsolatedDocumentFixture.GetTestTemplatePath(_uiapp!.Application);
                doc = RevitIsolatedDocumentFixture.OpenIsolatedModelCopy(
                    _uiapp.Application,
                    templatePath,
                    tempDir,
                    detachFromCentral: true);

                bool purged = DocumentUtil.Purge(_uiapp.Application, doc);
                Assert.IsTrue(
                    purged,
                    "DocumentUtil.Purge should resolve eTransmitForRevitDB and succeed without App.OnStartup.");
            }
            finally
            {
                if (doc != null && doc.IsValidObject)
                {
                    doc.Close(false);
                }

                try
                {
                    if (Directory.Exists(tempDir))
                    {
                        Directory.Delete(tempDir, recursive: true);
                    }
                }
                catch
                {
                    // Best-effort temp cleanup
                }
            }
        }

        [Test]
        [RevitSmokeTest]
        public void PayloadAssembly_EmitsRevitVersionMetadata_AndConsumersResolveLiveVersionWithFailFastFallback()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            string liveVersion = _uiapp!.Application.VersionNumber;
            Assert.IsTrue(int.TryParse(liveVersion, out int liveYear), $"Unexpected Revit VersionNumber '{liveVersion}'.");

            Assembly payloadAssembly = typeof(App).Assembly;
            AssemblyMetadataAttribute? metadata = payloadAssembly
                .GetCustomAttributes<AssemblyMetadataAttribute>()
                .FirstOrDefault(attr => string.Equals(attr.Key, RevitVersionMetadata.MetadataKey, StringComparison.Ordinal));

            Assert.IsNotNull(metadata, "Payload assembly must emit [AssemblyMetadata(\"RevitVersion\", ...)].");
            Assert.AreEqual(liveVersion, metadata!.Value);

            // ETransmitAssemblyResolver: live version, 2-digit & 4-digit API assembly versions, 1.0.0.0 fallback, whitespace trimming, fail-fast
            Assert.AreEqual(liveVersion, ETransmitAssemblyResolver.ResolveRevitYear());

            Assembly liveRevitApi27 = DynamicAssemblyTestHelper.CreateDynamicAssembly(new Version(27, 3, 0, 0));
            Assembly liveRevitApi2027 = DynamicAssemblyTestHelper.CreateDynamicAssembly(new Version(2027, 0, 0, 0));
            Assembly mockRevitApi = DynamicAssemblyTestHelper.CreateDynamicAssembly(new Version(1, 0, 0, 0));
            Assembly metadata2026 = DynamicAssemblyTestHelper.CreateDynamicAssembly(null, (RevitVersionMetadata.MetadataKey, "2026"));
            Assembly metadataWhitespace = DynamicAssemblyTestHelper.CreateDynamicAssembly(null, (RevitVersionMetadata.MetadataKey, " 2027 "));
            Assembly missingMetadata = DynamicAssemblyTestHelper.CreateDynamicAssembly(null, ("OtherKey", "2027"));
            Assembly invalidAlphaMetadata = DynamicAssemblyTestHelper.CreateDynamicAssembly(null, (RevitVersionMetadata.MetadataKey, "not-a-year"));
            Assembly twoDigitMetadata = DynamicAssemblyTestHelper.CreateDynamicAssembly(null, (RevitVersionMetadata.MetadataKey, "27"));
            Assembly whitespaceMetadata = DynamicAssemblyTestHelper.CreateDynamicAssembly(null, (RevitVersionMetadata.MetadataKey, "   "));

            Assert.AreEqual("2027", ETransmitAssemblyResolver.ResolveRevitYear(liveRevitApi27, metadata2026));
            Assert.AreEqual("2027", ETransmitAssemblyResolver.ResolveRevitYear(liveRevitApi2027, metadata2026));
            Assert.AreEqual(liveVersion, ETransmitAssemblyResolver.ResolveRevitYear(mockRevitApi, payloadAssembly));
            Assert.AreEqual("2027", ETransmitAssemblyResolver.ResolveRevitYear(mockRevitApi, metadataWhitespace));

            foreach (Assembly badAsm in new[] { missingMetadata, invalidAlphaMetadata, twoDigitMetadata, whitespaceMetadata })
            {
                InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                    () => ETransmitAssemblyResolver.ResolveRevitYear(mockRevitApi, badAsm))!;
                StringAssert.Contains(RevitVersionMetadata.MetadataKey, ex.Message);
            }

            // RibbonManager: host priority, whitespace/null fallback, fail-fast on missing/invalid/2-digit metadata
            int overrideYear = liveYear == 2026 ? 2025 : 2026;
            string overrideVersion = overrideYear.ToString(CultureInfo.InvariantCulture);

            Assert.AreEqual(overrideYear, RibbonManager.ResolveActiveVersion(overrideVersion, payloadAssembly));
            Assert.AreEqual(liveYear, RibbonManager.ResolveActiveVersion("   ", payloadAssembly));
            Assert.AreEqual(liveYear, RibbonManager.ResolveActiveVersion((string?)null, payloadAssembly));

            foreach (Assembly badAsm in new[] { missingMetadata, invalidAlphaMetadata, twoDigitMetadata, whitespaceMetadata })
            {
                InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                    () => RibbonManager.ResolveActiveVersion((string?)null, badAsm))!;
                StringAssert.Contains(RevitVersionMetadata.MetadataKey, ex.Message);
            }
        }

        [Test]
        [RevitSmokeTest]
        public void BootstrapAssembly_EmitsRevitVersionMetadata_AndOnStartupCoreFailsFastWithDiagnostics()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            string liveVersion = _uiapp!.Application.VersionNumber;
            Assert.IsTrue(int.TryParse(liveVersion, out int liveYear), $"Unexpected Revit VersionNumber '{liveVersion}'.");

            Assembly bootstrapAssembly = LoadBootstrapAssembly(liveVersion);

            AssemblyMetadataAttribute? metadata = bootstrapAssembly
                .GetCustomAttributes<AssemblyMetadataAttribute>()
                .FirstOrDefault(attr => string.Equals(attr.Key, RevitVersionMetadata.MetadataKey, StringComparison.Ordinal));
            Assert.IsNotNull(metadata, $"Synthetic.Bootstrap{liveVersion} must emit [AssemblyMetadata(\"RevitVersion\", \"{liveVersion}\")].");
            Assert.AreEqual(liveVersion, metadata!.Value);

            Type shadowCacheManagerType = bootstrapAssembly.GetType("Synthetic.Bootstrap.ShadowCacheManager", throwOnError: true)!;
            Type bootstrapAppType = bootstrapAssembly.GetType("Synthetic.Bootstrap.BootstrapApp", throwOnError: true)!;
            Type hooksType = bootstrapAssembly.GetType("Synthetic.Bootstrap.ShadowCacheHooks", throwOnError: true)!;
            Type launchPlanType = bootstrapAssembly.GetType("Synthetic.Bootstrap.LaunchPlan", throwOnError: true)!;

            MethodInfo prepareHostLaunch = shadowCacheManagerType.GetMethod(
                "PrepareHostLaunch",
                BindingFlags.Public | BindingFlags.Static)!;
            MethodInfo onStartupCore = bootstrapAppType.GetMethod(
                "OnStartupCore",
                BindingFlags.NonPublic | BindingFlags.Instance)!;

            string overrideVersion = (liveYear == 2026 ? 2025 : 2026).ToString(CultureInfo.InvariantCulture);
            string tempRoot = Path.Combine(Path.GetTempPath(), "SyntheticBootstrapRevitTests", Guid.NewGuid().ToString("N"));
            try
            {
                string payloadLiveDir = Path.Combine(tempRoot, "Payload", liveVersion);
                string payloadOverrideDir = Path.Combine(tempRoot, "Payload", overrideVersion);
                string localAppDataDir = Path.Combine(tempRoot, "LocalAppData");
                Directory.CreateDirectory(payloadLiveDir);
                Directory.CreateDirectory(payloadOverrideDir);
                Directory.CreateDirectory(localAppDataDir);
                File.WriteAllText(Path.Combine(payloadLiveDir, $"Synthetic{liveVersion}.dll"), "payload-live");
                File.WriteAllText(Path.Combine(payloadOverrideDir, $"Synthetic{overrideVersion}.dll"), "payload-override");

                string fakeBootstrapPath = Path.Combine(tempRoot, "Bootstrap", "v1", $"Synthetic.Bootstrap{liveVersion}.dll");
                Directory.CreateDirectory(Path.GetDirectoryName(fakeBootstrapPath)!);

                object CreateHooks(List<string>? capturedComments = null)
                {
                    object hooks = Activator.CreateInstance(hooksType)!;
                    hooksType.GetProperty("LocalAppDataDir")!.SetValue(hooks, localAppDataDir);
                    hooksType.GetProperty("CurrentProcessId")!.SetValue(hooks, 12345);
                    hooksType.GetProperty("CurrentProcessUtcStartTicks")!.SetValue(hooks, 638999999999999999L);
                    hooksType.GetProperty("GetEnvironmentVariable")!.SetValue(
                        hooks,
                        (Func<string, string?>)(_ => null));
                    if (capturedComments != null)
                    {
                        hooksType.GetProperty("WriteJournalComment")!.SetValue(
                            hooks,
                            (Action<string>)(comment => capturedComments.Add(comment)));
                    }
                    return hooks;
                }

                // 1. Live host version takes priority over compiled metadata
                object?[] liveHostArgs = { overrideVersion, bootstrapAssembly, fakeBootstrapPath, null, CreateHooks() };
                object livePlan = prepareHostLaunch.Invoke(null, liveHostArgs)!;
                Assert.AreEqual(overrideVersion, liveHostArgs[3]);
                Assert.AreEqual("ShadowCopy", launchPlanType.GetProperty("Mode")!.GetValue(livePlan)!.ToString());
                StringAssert.EndsWith($"Synthetic{overrideVersion}.dll", (string)launchPlanType.GetProperty("TargetAssemblyPath")!.GetValue(livePlan)!);

                // 2. Missing/whitespace host version falls back cleanly to assembly metadata
                object?[] fallbackArgs = { "   ", bootstrapAssembly, fakeBootstrapPath, null, CreateHooks() };
                object fallbackPlan = prepareHostLaunch.Invoke(null, fallbackArgs)!;
                Assert.AreEqual(liveVersion, fallbackArgs[3]);
                Assert.AreEqual("ShadowCopy", launchPlanType.GetProperty("Mode")!.GetValue(fallbackPlan)!.ToString());
                StringAssert.EndsWith($"Synthetic{liveVersion}.dll", (string)launchPlanType.GetProperty("TargetAssemblyPath")!.GetValue(fallbackPlan)!);

                // 3. Missing or unparseable metadata returns Failed LaunchPlan with diagnostics
                Assembly missingMetaAsm = DynamicAssemblyTestHelper.CreateDynamicAssembly(null, ("Other", liveVersion));
                Assembly badMetaAsm = DynamicAssemblyTestHelper.CreateDynamicAssembly(null, (RevitVersionMetadata.MetadataKey, "bad-year"));

                foreach (Assembly brokenAsm in new[] { missingMetaAsm, badMetaAsm })
                {
                    object?[] failArgs = { null, brokenAsm, fakeBootstrapPath, null, CreateHooks() };
                    object failPlan = prepareHostLaunch.Invoke(null, failArgs)!;
                    Assert.AreEqual(string.Empty, failArgs[3]);
                    Assert.AreEqual("Failed", launchPlanType.GetProperty("Mode")!.GetValue(failPlan)!.ToString());
                    string errorMessage = (string)launchPlanType.GetProperty("ErrorMessage")!.GetValue(failPlan)!;
                    StringAssert.Contains(RevitVersionMetadata.MetadataKey, errorMessage);
                    var diagnostics = (IReadOnlyList<string>)launchPlanType.GetProperty("Diagnostics")!.GetValue(failPlan)!;
                    Assert.IsNotEmpty(diagnostics);
                }

                // 4. OnStartupCore(null, missingMetaAsm, hooks) returns Result.Failed (-1), emits journal error, and writes diagnostic log
                object bootstrapInstance = Activator.CreateInstance(bootstrapAppType)!;
                var journalComments = new List<string>();
                int startupResultCode = Convert.ToInt32(onStartupCore.Invoke(
                    bootstrapInstance,
                    new object?[] { null, missingMetaAsm, CreateHooks(journalComments) })!);

                Assert.AreEqual((int)Result.Failed, startupResultCode);
                Assert.AreEqual(-1, startupResultCode);
                Assert.IsTrue(
                    journalComments.Any(c => c.StartsWith("SYNTHETIC_BOOTSTRAP_ERROR:", StringComparison.Ordinal)),
                    "Expected SYNTHETIC_BOOTSTRAP_ERROR journal comment via hooks.WriteJournalComment.");

                string logsDir = Path.Combine(localAppDataDir, "Synthetic", "Logs");
                Assert.IsTrue(Directory.Exists(logsDir), "Expected diagnostic logs directory to be created.");
                string[] logFiles = Directory.GetFiles(logsDir, "Synthetic_Bootstrap_unknown_*.log");
                Assert.IsNotEmpty(logFiles, "Expected Synthetic_Bootstrap_unknown_*.log on missing metadata failure.");
                string logText = File.ReadAllText(logFiles[0]);
                StringAssert.Contains("RevitVersion: unknown", logText);
                StringAssert.Contains(RevitVersionMetadata.MetadataKey, logText);
            }
            finally
            {
                try
                {
                    if (Directory.Exists(tempRoot))
                    {
                        Directory.Delete(tempRoot, recursive: true);
                    }
                }
                catch
                {
                    // Best-effort temp cleanup
                }
            }
        }

        private static Assembly LoadBootstrapAssembly(string liveVersion)
        {
            string expectedName = $"Synthetic.Bootstrap{liveVersion}";
            foreach (Assembly loaded in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (string.Equals(loaded.GetName().Name, expectedName, StringComparison.OrdinalIgnoreCase))
                {
                    return loaded;
                }
            }

            string? assemblyDir = Path.GetDirectoryName(typeof(BootstrapWorkerIsolationRevitTests).Assembly.Location);
            string? testDir = TestContext.CurrentContext?.TestDirectory;
            foreach (string? dir in new[] { assemblyDir, testDir })
            {
                if (string.IsNullOrWhiteSpace(dir))
                {
                    continue;
                }

                string markerPath = Path.Combine(dir, "synthetic_bootstrap_path.txt");
                if (File.Exists(markerPath))
                {
                    string candidatePath = File.ReadAllText(markerPath).Trim();
                    if (!string.IsNullOrEmpty(candidatePath) && File.Exists(candidatePath))
                    {
                        return Assembly.LoadFrom(candidatePath);
                    }
                }
            }

            throw new FileNotFoundException(
                $"Could not locate loaded assembly '{expectedName}' or resolve it via synthetic_bootstrap_path.txt.");
        }
    }
}
