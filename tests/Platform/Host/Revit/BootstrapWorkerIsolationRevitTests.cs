using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
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
                .FirstOrDefault(attr => string.Equals(attr.Key, "RevitVersion", StringComparison.Ordinal));

            Assert.IsNotNull(metadata, "Payload assembly must emit [AssemblyMetadata(\"RevitVersion\", ...)].");
            Assert.AreEqual(liveVersion, metadata!.Value);

            Assert.AreEqual(liveVersion, ETransmitAssemblyResolver.ResolveRevitYear());
            Assert.AreEqual(liveYear, RibbonManager.ResolveActiveVersion(liveVersion, payloadAssembly));
            Assert.AreEqual(liveYear, RibbonManager.ResolveActiveVersion((string?)null, payloadAssembly));

            AssemblyName missingName = new AssemblyName("DynamicMissingMetadata_" + Guid.NewGuid().ToString("N"));
            Assembly missingMetadataAsm = AssemblyBuilder.DefineDynamicAssembly(missingName, AssemblyBuilderAccess.Run);
            AssemblyName mockApiName = new AssemblyName("DynamicMockRevitApi_" + Guid.NewGuid().ToString("N"))
            {
                Version = new Version(1, 0, 0, 0)
            };
            Assembly mockApiAsm = AssemblyBuilder.DefineDynamicAssembly(mockApiName, AssemblyBuilderAccess.Run);

            Assert.Throws<InvalidOperationException>(
                () => ETransmitAssemblyResolver.ResolveRevitYear(mockApiAsm, missingMetadataAsm));
            Assert.Throws<InvalidOperationException>(
                () => RibbonManager.ResolveActiveVersion((string?)null, missingMetadataAsm));
        }
    }
}
