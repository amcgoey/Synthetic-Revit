using System;
using System.IO;
using NUnit.Framework;
using Synthetic.Features.MultiModelPdfExport.Models;
using Synthetic.Features.MultiModelPdfExport.Services;
using SyntheticTests;

namespace SyntheticTests.Features.MultiModelPdfExport
{
    [TestFixture]
    [UnitTest]
    [FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
    public class MultiModelPdfExportUserPrefsTests
    {
        private string _tempDir = string.Empty;

        [SetUp]
        public void SetUp()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "MultiModelPdfPrefsTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, true);
            }
        }

        [Test]
        public void DefaultFilePath_PointsUnderRoamingAppDataSyntheticFolder()
        {
            string expectedSuffix = Path.Combine("Synthetic", "MultiModelPdfExportUserPrefs.json");
            StringAssert.EndsWith(expectedSuffix, MultiModelPdfExportUserPrefsStore.DefaultFilePath);
            StringAssert.StartsWith(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                MultiModelPdfExportUserPrefsStore.DefaultFilePath);
        }

        [Test]
        public void Load_WhenFileDoesNotExist_ReturnsExpectedDefaults()
        {
            string prefsPath = Path.Combine(_tempDir, "missing", "MultiModelPdfExportUserPrefs.json");
            var store = new MultiModelPdfExportUserPrefsStore(prefsPath);

            MultiModelPdfExportUserPrefs prefs = store.Load();

            Assert.AreEqual(string.Empty, prefs.LastOutputFolder);
            Assert.AreEqual(MultiModelPdfFileMode.CombinedPerDocument, prefs.FileMode);
            Assert.AreEqual(string.Empty, prefs.PdfSetupName);
            Assert.AreEqual(string.Empty, prefs.LastBulkPrintSetName);
        }

        [Test]
        public void SaveAndLoad_RoundTripsAllPreferencesAcrossNestedDirectories()
        {
            string prefsPath = Path.Combine(_tempDir, "nested", "sub", "MultiModelPdfExportUserPrefs.json");
            var store = new MultiModelPdfExportUserPrefsStore(prefsPath);

            store.Save(new MultiModelPdfExportUserPrefs
            {
                LastOutputFolder = @"D:\Projects\Deliverables\PDFs",
                FileMode = MultiModelPdfFileMode.SeparatePerViewSheet,
                PdfSetupName = "INC_FullSize",
                LastBulkPrintSetName = "CD Set"
            });

            Assert.IsTrue(File.Exists(prefsPath));
            MultiModelPdfExportUserPrefs loaded = store.Load();

            Assert.AreEqual(@"D:\Projects\Deliverables\PDFs", loaded.LastOutputFolder);
            Assert.AreEqual(MultiModelPdfFileMode.SeparatePerViewSheet, loaded.FileMode);
            Assert.AreEqual("INC_FullSize", loaded.PdfSetupName);
            Assert.AreEqual("CD Set", loaded.LastBulkPrintSetName);
        }

        [Test]
        public void Load_WhenJsonIsCorruptOrBlank_FallsBackToDefaultsGracefully()
        {
            string corruptPath = Path.Combine(_tempDir, "corrupt.json");
            File.WriteAllText(corruptPath, "{ not valid json !!!");
            var corruptStore = new MultiModelPdfExportUserPrefsStore(corruptPath);

            MultiModelPdfExportUserPrefs fromCorrupt = corruptStore.Load();
            Assert.AreEqual(string.Empty, fromCorrupt.LastOutputFolder);
            Assert.AreEqual(MultiModelPdfFileMode.CombinedPerDocument, fromCorrupt.FileMode);
            Assert.AreEqual(string.Empty, fromCorrupt.PdfSetupName);
            Assert.AreEqual(string.Empty, fromCorrupt.LastBulkPrintSetName);

            string blankPath = Path.Combine(_tempDir, "blank.json");
            File.WriteAllText(blankPath, "   \r\n  ");
            var blankStore = new MultiModelPdfExportUserPrefsStore(blankPath);

            MultiModelPdfExportUserPrefs fromBlank = blankStore.Load();
            Assert.AreEqual(string.Empty, fromBlank.LastOutputFolder);
            Assert.AreEqual(MultiModelPdfFileMode.CombinedPerDocument, fromBlank.FileMode);
            Assert.AreEqual(string.Empty, fromBlank.PdfSetupName);
            Assert.AreEqual(string.Empty, fromBlank.LastBulkPrintSetName);
        }

        [Test]
        public void Load_NormalizesNullStringsInvalidFileModeAndUnknownJsonFields()
        {
            string prefsPath = Path.Combine(_tempDir, "normalized.json");
            File.WriteAllText(
                prefsPath,
                @"{ ""LastOutputFolder"": null, ""FileMode"": 999, ""PdfSetupName"": ""  INC_FullSize  "", ""LastBulkPrintSetName"": null, ""ObsoleteField"": true }");

            var store = new MultiModelPdfExportUserPrefsStore(prefsPath);
            MultiModelPdfExportUserPrefs loaded = store.Load();

            Assert.AreEqual(string.Empty, loaded.LastOutputFolder);
            Assert.AreEqual(MultiModelPdfFileMode.CombinedPerDocument, loaded.FileMode);
            Assert.AreEqual("INC_FullSize", loaded.PdfSetupName);
            Assert.AreEqual(string.Empty, loaded.LastBulkPrintSetName);
        }

        [Test]
        public void Save_WhenPrefsIsNull_ThrowsArgumentNullException()
        {
            string prefsPath = Path.Combine(_tempDir, "prefs.json");
            var store = new MultiModelPdfExportUserPrefsStore(prefsPath);

            Assert.Throws<ArgumentNullException>(() => store.Save(null!));
            Assert.IsFalse(File.Exists(prefsPath));
        }

        [Test]
        public void TrySave_RoundTripsValidPrefsAndReturnsFalseWhenPathIsUnwritable()
        {
            string prefsPath = Path.Combine(_tempDir, "trysave", "MultiModelPdfExportUserPrefs.json");
            var store = new MultiModelPdfExportUserPrefsStore(prefsPath);

            bool saved = store.TrySave(new MultiModelPdfExportUserPrefs
            {
                LastOutputFolder = @"D:\Projects\PDFs",
                FileMode = MultiModelPdfFileMode.SeparatePerViewSheet,
                PdfSetupName = "INC_FullSize",
                LastBulkPrintSetName = "CD Set"
            });

            Assert.IsTrue(saved);
            MultiModelPdfExportUserPrefs loaded = store.Load();
            Assert.AreEqual(@"D:\Projects\PDFs", loaded.LastOutputFolder);
            Assert.AreEqual(MultiModelPdfFileMode.SeparatePerViewSheet, loaded.FileMode);
            Assert.AreEqual("INC_FullSize", loaded.PdfSetupName);
            Assert.AreEqual("CD Set", loaded.LastBulkPrintSetName);

            // Point FilePath at an existing directory so File.WriteAllText raises UnauthorizedAccessException/IOException
            var directoryTargetStore = new MultiModelPdfExportUserPrefsStore(_tempDir);
            Assert.IsFalse(directoryTargetStore.TrySave(new MultiModelPdfExportUserPrefs()));
            Assert.Throws<ArgumentNullException>(() => store.TrySave(null!));
        }
    }
}
