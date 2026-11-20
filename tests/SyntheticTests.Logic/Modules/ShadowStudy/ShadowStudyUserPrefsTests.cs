using System;
using System.IO;
using NUnit.Framework;
using Synthetic.Modules.ShadowStudy.Models;
using Synthetic.Modules.ShadowStudy.Services;

namespace SyntheticTests.Modules.ShadowStudy
{
    [TestFixture]
    public class ShadowStudyUserPrefsTests
    {
        private string _tempDir = string.Empty;

        [SetUp]
        public void SetUp()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "ShadowPrefsTests_" + Guid.NewGuid().ToString("N"));
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
        public void DefaultFilePath_PointsUnderAppDataSyntheticFolder()
        {
            StringAssert.EndsWith(
                Path.Combine("Synthetic", "ShadowStudyUserPrefs.json"),
                ShadowStudyUserPrefsStore.DefaultFilePath);
        }

        [Test]
        public void Load_WhenFileDoesNotExist_ReturnsExpectedDefaults()
        {
            string prefsPath = Path.Combine(_tempDir, "missing", "ShadowStudyUserPrefs.json");
            var store = new ShadowStudyUserPrefsStore(prefsPath);

            ShadowStudyUserPrefs prefs = store.Load();

            Assert.AreEqual(string.Empty, prefs.LastOutputFolder);
            Assert.AreEqual(3000, prefs.PixelWidth);
            Assert.IsTrue(prefs.ExportIndividualFrames);
        }

        [Test]
        public void SaveAndLoad_RoundTripsLocalPrefsWithoutTouchingRevitDocument()
        {
            string prefsPath = Path.Combine(_tempDir, "nested", "ShadowStudyUserPrefs.json");
            var store = new ShadowStudyUserPrefsStore(prefsPath);

            store.Save(new ShadowStudyUserPrefs
            {
                LastOutputFolder = @"D:\Projects\Shadows\Out",
                PixelWidth = 1920,
                ExportIndividualFrames = false
            });

            Assert.IsTrue(File.Exists(prefsPath));
            ShadowStudyUserPrefs loaded = store.Load();

            Assert.AreEqual(@"D:\Projects\Shadows\Out", loaded.LastOutputFolder);
            Assert.AreEqual(1920, loaded.PixelWidth);
            Assert.IsFalse(loaded.ExportIndividualFrames);
        }

        [Test]
        public void Load_WhenJsonIsCorrupt_FallsBackToDefaultsGracefully()
        {
            string prefsPath = Path.Combine(_tempDir, "corrupt.json");
            File.WriteAllText(prefsPath, "{ not valid json !!!");
            var store = new ShadowStudyUserPrefsStore(prefsPath);

            ShadowStudyUserPrefs loaded = store.Load();

            Assert.AreEqual(string.Empty, loaded.LastOutputFolder);
            Assert.AreEqual(3000, loaded.PixelWidth);
            Assert.IsTrue(loaded.ExportIndividualFrames);
        }
    }
}
