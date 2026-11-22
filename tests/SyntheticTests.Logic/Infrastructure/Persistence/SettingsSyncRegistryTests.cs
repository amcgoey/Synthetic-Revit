using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Synthetic.Infrastructure.Persistence;
using Synthetic.Modules.DetailItemFactory.Settings;
using Synthetic.Modules.ShadowStudy.Models;
using Synthetic.Modules.ShadowStudy.Settings;
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
            DetailItemFactorySettings.Name,
            ShadowStudySettings.Name
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
                { DetailItemFactorySettings.Name, new DetailItemFactorySettings().Defaults() },
                { ShadowStudySettings.Name, new ShadowStudySettings().Defaults() }
            };
            return dict;
        }

        [Test]
        public void SyncedModules_ContainsExpectedEightModules_AndExcludesSyncSettings()
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
        public void AssemblyReflection_AllConcreteSettingModules_AreRegisteredInSyncedModulesConfigAndTemplate()
        {
            var settingModuleInterface = typeof(ISettingModule);
            System.Reflection.Assembly assembly = typeof(Synthetic.Core.App).Assembly;
            Type?[] rawTypes;
            try
            {
                rawTypes = assembly.GetTypes();
            }
            catch (System.Reflection.ReflectionTypeLoadException ex)
            {
                rawTypes = ex.Types;
            }

            List<Type> concreteSettingTypes = rawTypes
                .Where(t => t != null && !t.IsAbstract && !t.IsInterface && settingModuleInterface.IsAssignableFrom(t))
                .Cast<Type>()
                .Where(t => t != typeof(SyncSettings))
                .ToList();

            Assert.IsNotEmpty(concreteSettingTypes, "Expected at least one concrete ISettingModule type.");

            var expectedKeysByType = new List<string>(concreteSettingTypes.Count);
            foreach (Type type in concreteSettingTypes)
            {
                ISettingModule? instance = Activator.CreateInstance(type) as ISettingModule;
                Assert.IsNotNull(instance, $"Failed to instantiate {type.FullName}.");
                Assert.IsFalse(string.IsNullOrWhiteSpace(instance!.ModuleKey),
                    $"{type.FullName}.ModuleKey must not be blank.");
                expectedKeysByType.Add(instance.ModuleKey);
            }

            List<string> registryKeys = SettingsModuleRegistry.SyncedModules
                .Select(d => d.ModuleKey)
                .ToList();
            CollectionAssert.AreEquivalent(expectedKeysByType, registryKeys,
                "SettingsModuleRegistry.SyncedModules must match every concrete ISettingModule in the assembly (excluding SyncSettings).");

            var config = new Config().Defaults();
            Assert.IsNotNull(config.settings);
            CollectionAssert.AreEquivalent(expectedKeysByType, config.settings.Keys,
                "Config.Defaults() must populate every concrete ISettingModule in the assembly (excluding SyncSettings).");

            string templatePath = FindTemplateJsonPath();
            Config? templateConfig = Config.ReadFromFile(templatePath);
            Assert.IsNotNull(templateConfig);
            Assert.IsNotNull(templateConfig!.settings);
            CollectionAssert.AreEquivalent(expectedKeysByType, templateConfig.settings.Keys,
                "SyntheticSettings.template.json must cover every concrete ISettingModule in the assembly (excluding SyncSettings).");
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
        public void HasSyncDrift_LegacyJsonMissingShadowStudy_ReturnsFalse_AndModifiedShadowStudyReturnsTrue()
        {
            var docSettings = CreateDefaultDocumentSettings();
            var legacyWithoutShadowStudy = new Dictionary<string, object>(docSettings);
            legacyWithoutShadowStudy.Remove(ShadowStudySettings.Name);

            string legacyJson = JsonConvert.SerializeObject(legacyWithoutShadowStudy, Formatting.Indented);
            Assert.IsFalse(SettingsModuleRegistry.HasSyncDrift(legacyJson, docSettings));

            var modifiedShadowStudy = new Dictionary<string, object>(docSettings)
            {
                [ShadowStudySettings.Name] = new ShadowStudySettings
                {
                    DefaultTimeIncrementMinutes = 30,
                    DefaultMinSunAltitudeDegrees = 10.0,
                    DefaultMaxCompositeDarknessPercent = 65,
                    DefaultPreserveVisualStyle = true,
                    UserPresets = new List<ShadowStudyPresetModel>
                    {
                        new ShadowStudyPresetModel
                        {
                            Name = "Studio Firm Preset",
                            TimeIncrementMinutes = 15,
                            MinSunAltitudeDegrees = 8.0,
                            MaxCompositeDarknessPercent = 70,
                            PreserveVisualStyle = true,
                            Dates = new List<ShadowStudyPresetDateSpec>
                            {
                                new ShadowStudyPresetDateSpec { AstronomicalEvent = AstronomicalEventKind.SummerSolstice }
                            }
                        }
                    }
                }
            };

            string driftedJson = JsonConvert.SerializeObject(modifiedShadowStudy, Formatting.Indented);
            Assert.IsTrue(SettingsModuleRegistry.HasSyncDrift(driftedJson, docSettings));
        }

        [Test]
        public void ConfigDefaults_PopulatesAllEightSyncedModules()
        {
            var config = new Config().Defaults();

            Assert.IsNotNull(config.settings);
            CollectionAssert.AreEquivalent(ExpectedSyncedModuleKeys, config.settings.Keys);
            Assert.IsFalse(config.Contains(SyncSettings.Name));
        }

        [Test]
        public void TemplateJson_CoversAllEightSyncedModules()
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

            ShadowStudySettings shadowDefaults = templateConfig.GetSettings<ShadowStudySettings>(ShadowStudySettings.Name);
            Assert.AreEqual(1, shadowDefaults.SchemaVersion);
            Assert.AreEqual(60, shadowDefaults.DefaultTimeIncrementMinutes);
            Assert.AreEqual(5.0, shadowDefaults.DefaultMinSunAltitudeDegrees);
            Assert.AreEqual(80, shadowDefaults.DefaultMaxCompositeDarknessPercent);
            Assert.IsFalse(shadowDefaults.DefaultPreserveVisualStyle);
            Assert.IsNotNull(shadowDefaults.UserPresets);
            Assert.AreEqual(0, shadowDefaults.UserPresets.Count);
        }

        [Test]
        public void SettingsManagerClearCache_DoesNotThrowWhenCalledWithNull()
        {
            Assert.DoesNotThrow(() => SettingsManager.ClearCache());
        }
    }
}
