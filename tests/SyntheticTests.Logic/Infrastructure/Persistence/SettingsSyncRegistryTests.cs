using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Synthetic.Infrastructure.Persistence;
using Synthetic.Modules.DetailItemFactory.Settings;
using Synthetic.Settings;

namespace SyntheticTests.Infrastructure.Persistence
{
    [TestFixture]
    public class SettingsSyncRegistryTests
    {
        private static readonly string[] ExpectedSyncedModuleKeys = new[]
        {
            WorksetSettings.Name,
            ViewAutoNumSettings.Name,
            MaterialLibrarySettings.Name,
            ProjectMaterialSettings.Name,
            FileUtilitySettings.Name,
            StandardsSettings.Name,
            DetailItemFactorySettings.Name
        };

        private static string FindTemplateJsonPath()
        {
            string? dir = TestContext.CurrentContext.TestDirectory;
            while (!string.IsNullOrEmpty(dir))
            {
                string candidate = Path.Combine(dir, "src", "SyntheticShared", "SyntheticSettings.template.json");
                if (File.Exists(candidate))
                {
                    return candidate;
                }
                dir = Path.GetDirectoryName(dir);
            }

            string? envRoot = Environment.GetEnvironmentVariable("SYNTHETIC_PROJECT_ROOT");
            if (!string.IsNullOrEmpty(envRoot))
            {
                string candidate = Path.Combine(envRoot, "src", "SyntheticShared", "SyntheticSettings.template.json");
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            throw new FileNotFoundException("Could not locate src/SyntheticShared/SyntheticSettings.template.json.");
        }

        private static Dictionary<string, object> CreateDefaultDocumentSettings()
        {
            var dict = new Dictionary<string, object>
            {
                { WorksetSettings.Name, new WorksetSettings().Defaults() },
                { ViewAutoNumSettings.Name, new ViewAutoNumSettings().Defaults() },
                { MaterialLibrarySettings.Name, new MaterialLibrarySettings().Defaults() },
                { ProjectMaterialSettings.Name, new ProjectMaterialSettings().Defaults() },
                { FileUtilitySettings.Name, new FileUtilitySettings().Defaults() },
                { StandardsSettings.Name, new StandardsSettings().Defaults() },
                { DetailItemFactorySettings.Name, new DetailItemFactorySettings().Defaults() }
            };
            return dict;
        }

        [Test]
        public void SyncedModules_ContainsExpectedSevenModules_AndExcludesSyncSettings()
        {
            var modules = SettingsModuleRegistry.SyncedModules;
            Assert.IsNotNull(modules);

            var actualKeys = modules.Select(m => m.ModuleKey).ToList();
            CollectionAssert.AreEquivalent(ExpectedSyncedModuleKeys, actualKeys);
            Assert.AreEqual(ExpectedSyncedModuleKeys.Length, actualKeys.Count);
            Assert.IsFalse(actualKeys.Contains(SyncSettings.Name));

            foreach (var descriptor in modules)
            {
                Assert.IsNotNull(descriptor.SettingsType);
                Assert.IsTrue(typeof(ISettingModule).IsAssignableFrom(descriptor.SettingsType),
                    $"{descriptor.SettingsType.Name} must implement ISettingModule.");

                ISettingModule created = descriptor.CreateDefault();
                Assert.IsNotNull(created);
                Assert.IsInstanceOf(descriptor.SettingsType, created);
                Assert.AreEqual(descriptor.ModuleKey, created.ModuleKey);
            }
        }

        [Test]
        public void HasSyncDrift_LegacyFourKeyJsonMatchingDocument_ReturnsFalse()
        {
            var docSettings = CreateDefaultDocumentSettings();
            var legacySubset = new Dictionary<string, object>
            {
                { WorksetSettings.Name, docSettings[WorksetSettings.Name] },
                { ViewAutoNumSettings.Name, docSettings[ViewAutoNumSettings.Name] },
                { MaterialLibrarySettings.Name, docSettings[MaterialLibrarySettings.Name] },
                { ProjectMaterialSettings.Name, docSettings[ProjectMaterialSettings.Name] }
            };

            string legacyJson = JsonConvert.SerializeObject(legacySubset, Formatting.Indented);

            bool hasDrift = SettingsModuleRegistry.HasSyncDrift(legacyJson, docSettings);
            Assert.IsFalse(hasDrift);
        }

        [Test]
        public void HasSyncDrift_ModifiedKeyInExternalJson_ReturnsTrue()
        {
            var docSettings = CreateDefaultDocumentSettings();
            var modifiedExternal = new Dictionary<string, object>(docSettings)
            {
                [StandardsSettings.Name] = new StandardsSettings
                {
                    StandardsFilePath = @"C:\Standards\firm-standards.json"
                }
            };

            string externalJson = JsonConvert.SerializeObject(modifiedExternal, Formatting.Indented);

            bool hasDrift = SettingsModuleRegistry.HasSyncDrift(externalJson, docSettings);
            Assert.IsTrue(hasDrift);
        }

        [Test]
        public void HasSyncDrift_ExternalJsonWithSyncSettingsOnlyDifference_ReturnsFalse()
        {
            var docSettings = CreateDefaultDocumentSettings();
            var externalDict = new Dictionary<string, object>(docSettings)
            {
                [SyncSettings.Name] = new SyncSettings
                {
                    LinkedFilePath = @"C:\Firm\other-linked.json"
                }
            };

            string externalJson = JsonConvert.SerializeObject(externalDict, Formatting.Indented);

            bool hasDrift = SettingsModuleRegistry.HasSyncDrift(externalJson, docSettings);
            Assert.IsFalse(hasDrift);
        }

        [TestCase("")]
        [TestCase("null")]
        [TestCase("{ invalid json")]
        public void HasSyncDrift_NullOrInvalidJson_ReturnsTrue(string externalJson)
        {
            var docSettings = CreateDefaultDocumentSettings();

            bool hasDrift = SettingsModuleRegistry.HasSyncDrift(externalJson, docSettings);
            Assert.IsTrue(hasDrift);
        }

        [Test]
        public void ConfigDefaults_PopulatesAllSevenSyncedModules()
        {
            var config = new Config().Defaults();

            Assert.IsNotNull(config.settings);
            CollectionAssert.AreEquivalent(ExpectedSyncedModuleKeys, config.settings.Keys);
            Assert.IsFalse(config.Contains(SyncSettings.Name));
        }

        [Test]
        public void TemplateJson_CoversAllSevenSyncedModules()
        {
            string templatePath = FindTemplateJsonPath();
            Config? templateConfig = Config.ReadFromFile(templatePath);

            Assert.IsNotNull(templateConfig);
            Assert.IsNotNull(templateConfig!.settings);
            foreach (string key in ExpectedSyncedModuleKeys)
            {
                Assert.IsTrue(templateConfig.Contains(key),
                    $"SyntheticSettings.template.json is missing '{key}'.");
            }
        }

        [Test]
        public void SettingsManagerClearCache_DoesNotThrowWhenCalledWithNull()
        {
            Assert.DoesNotThrow(() => SettingsManager.ClearCache());
        }
    }
}
