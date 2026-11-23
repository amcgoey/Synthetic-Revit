using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using NUnit.Framework;
using Synthetic.Platform.Settings;

namespace SyntheticTests.Platform.Settings
{
    [TestFixture]
    [UnitTest]
    public class SettingsSyncDriftTests
    {
        private sealed class FakeSettingModule : ISettingModule
        {
            public FakeSettingModule()
            {
            }

            public FakeSettingModule(string moduleKey, string value)
            {
                ModuleKey = moduleKey;
                Value = value;
            }

            [JsonIgnore]
            public string ModuleKey { get; set; } = string.Empty;

            public string Value { get; set; } = string.Empty;
        }

        private static Dictionary<string, object> CreateBaselineDocumentSettings()
        {
            return new Dictionary<string, object>
            {
                { "Worksets", new { WorksetFile = "SyntheticWorksets.xlsx", WorksetGroup = "Worksets" } },
                { "ViewAutoNumber", new { viewAutoNumFamily = "Titleblock Grid Location Marker" } },
                { "MaterialLibrary", new { LibraryFolderPath = @"C:\Materials\" } },
                { "ProjectMaterials", new { DefaultRelativePath = @".\Materials\" } },
                { "Standards", new { StandardsFilePath = string.Empty } },
                { "ShadowStudy", new { SchemaVersion = 1, DefaultTimeIncrementMinutes = 60 } }
            };
        }

        [Test]
        public void SettingsModuleKeys_SyncMatchesExpectedContract()
        {
            Assert.AreEqual("SyncSettings", SettingsModuleKeys.Sync);
        }

        [Test]
        public void HasSyncDrift_LegacySubsetMatchingDocument_ReturnsFalse()
        {
            Dictionary<string, object> docSettings = CreateBaselineDocumentSettings();
            var legacySubset = new Dictionary<string, object>
            {
                { "Worksets", docSettings["Worksets"] },
                { "ViewAutoNumber", docSettings["ViewAutoNumber"] },
                { "MaterialLibrary", docSettings["MaterialLibrary"] },
                { "ProjectMaterials", docSettings["ProjectMaterials"] }
            };

            string externalJson = JsonConvert.SerializeObject(legacySubset, Formatting.Indented);
            Assert.IsFalse(SyncDriftComparer.HasSyncDrift(externalJson, docSettings));
        }

        [Test]
        public void HasSyncDrift_ModifiedValueInExternalJson_ReturnsTrue()
        {
            Dictionary<string, object> docSettings = CreateBaselineDocumentSettings();
            var modifiedExternal = new Dictionary<string, object>(docSettings)
            {
                ["Standards"] = new { StandardsFilePath = @"C:\Standards\firm-standards.json" }
            };

            string externalJson = JsonConvert.SerializeObject(modifiedExternal, Formatting.Indented);
            Assert.IsTrue(SyncDriftComparer.HasSyncDrift(externalJson, docSettings));
        }

        [Test]
        public void HasSyncDrift_ExternalJsonDifferingOnlyInSyncSettings_ReturnsFalse()
        {
            Dictionary<string, object> docSettings = CreateBaselineDocumentSettings();
            var externalDict = new Dictionary<string, object>(docSettings)
            {
                [SettingsModuleKeys.Sync] = new { LinkedFilePath = @"C:\Firm\other-linked.json" }
            };

            string externalJson = JsonConvert.SerializeObject(externalDict, Formatting.Indented);
            Assert.IsFalse(SyncDriftComparer.HasSyncDrift(externalJson, docSettings));
        }

        [TestCase("")]
        [TestCase("   ")]
        [TestCase("null")]
        [TestCase("{ invalid json")]
        public void HasSyncDrift_NullWhitespaceOrInvalidJson_ReturnsTrue(string externalJson)
        {
            Dictionary<string, object> docSettings = CreateBaselineDocumentSettings();
            Assert.IsTrue(SyncDriftComparer.HasSyncDrift(externalJson, docSettings));
        }

        [Test]
        public void HasSyncDrift_NullDocumentSettings_ReturnsTrue()
        {
            string externalJson = JsonConvert.SerializeObject(CreateBaselineDocumentSettings());
            Assert.IsTrue(SyncDriftComparer.HasSyncDrift(externalJson, null!));
        }

        [Test]
        public void ConfigDefaults_WithExplicitFactories_PopulatesAllSuppliedModules()
        {
            var factories = new List<KeyValuePair<string, Func<ISettingModule>>>
            {
                new KeyValuePair<string, Func<ISettingModule>>("Alpha", () => new FakeSettingModule("Alpha", "one")),
                new KeyValuePair<string, Func<ISettingModule>>("Beta", () => new FakeSettingModule("Beta", "two")),
                new KeyValuePair<string, Func<ISettingModule>>("", () => new FakeSettingModule("Ignored", "bad")),
                new KeyValuePair<string, Func<ISettingModule>>("NullFactory", null!)
            };

            Config config = new Config().Defaults(factories);

            Assert.IsTrue(config.Contains("Alpha"));
            Assert.IsTrue(config.Contains("Beta"));
            Assert.IsFalse(config.Contains(""));
            Assert.IsFalse(config.Contains("NullFactory"));
            Assert.AreEqual("one", config.GetSettings<FakeSettingModule>("Alpha").Value);
            Assert.AreEqual("two", config.GetSettings<FakeSettingModule>("Beta").Value);
        }

        [Test]
        public void ConfigDefaults_WithNullFactories_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => new Config().Defaults(null!));
        }
    }
}
