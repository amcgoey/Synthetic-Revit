using System;
using System.IO;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using NUnit.Framework;
using Synthetic.Platform.Host;
using Synthetic.Modules.StandardsManagement.Views;
using Synthetic.Shared.RevitAPI;
using SyntheticTests.Helpers;

namespace SyntheticTests.Bootstrap
{
    [TestFixture]
    public class BootstrapWorkerIsolationRevitTests
    {
        private UIApplication? _uiapp;

        [OneTimeSetUp]
        public void Setup(UIApplication uiapp)
        {
            _uiapp = uiapp;
            TestWorkerJournalMarker.EnsureWritten(uiapp);
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

            string assemblyLocation = typeof(App).Assembly.Location;
            Assert.IsFalse(
                string.IsNullOrWhiteSpace(assemblyLocation),
                "Loaded Synthetic assembly must have a non-empty disk path.");

            string normalizedLocation = Path.GetFullPath(assemblyLocation);
            string expectedTempRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "RevitTest"))
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;

            Assert.IsTrue(
                normalizedLocation.StartsWith(expectedTempRoot, StringComparison.OrdinalIgnoreCase),
                $"Expected unshadowed payload under '{expectedTempRoot}', but loaded '{normalizedLocation}'.");
        }

        [Test]
        [RevitSmokeTest]
        public void TestWorker_ProjectStandardsDashboardWindow_InitializesXamlAndThemeCleanly()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");

            var window = new ProjectStandardsDashboardWindow(_uiapp!.MainWindowHandle);
            try
            {
                window.Show();
                Assert.IsTrue(
                    window.IsVisible,
                    "Modeless ProjectStandardsDashboardWindow should initialize XAML/theme and become visible without App.OnStartup.");
            }
            finally
            {
                window.Close();
            }
        }

        [Test]
        [RevitIsolatedDocTest]
        public void TestWorker_DocumentUtilPurge_ResolvesETransmitWithoutHostStartup()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");

            Document doc = OpenDetachedTestTemplate(_uiapp!.Application);
            try
            {
                bool purged = DocumentUtil.Purge(_uiapp.Application, doc);
                Assert.IsTrue(
                    purged,
                    "DocumentUtil.Purge should resolve eTransmitForRevitDB and succeed without App.OnStartup.");
            }
            finally
            {
                doc.Close(false);
            }
        }

        private static Document OpenDetachedTestTemplate(Autodesk.Revit.ApplicationServices.Application app)
        {
            string projectRoot = TestPathHelper.GetProjectRoot();
            string testModelName = "TestTemplate" + app.VersionNumber + ".rvt";
            string modelPathStr = Path.Combine(projectRoot, "tests", "test_models", testModelName);
            if (!File.Exists(modelPathStr))
            {
                throw new FileNotFoundException("Test template model not found: " + modelPathStr);
            }

            ModelPath modelPath = ModelPathUtils.ConvertUserVisiblePathToModelPath(modelPathStr);
            var openOptions = new OpenOptions
            {
                DetachFromCentralOption = DetachFromCentralOption.DetachAndDiscardWorksets
            };
            return app.OpenDocumentFile(modelPath, openOptions);
        }
    }
}
