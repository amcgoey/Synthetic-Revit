using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Synthetic.Features.ShadowStudy.Models;
using Synthetic.Features.ShadowStudy.Settings;
using Synthetic.Platform.Settings;
using SyntheticTests.Helpers;

namespace SyntheticTests.Platform.Settings
{
    [TestFixture]
    [UnitTest]
    public class SettingsTemplateAndDefaultsTests
    {
        private static readonly string[] ExpectedTemplateModuleKeys =
        {
            "Worksets",
            "ViewAutoNumber",
            "MaterialLibrary",
            "ProjectMaterials",
            "FileUtility",
            "Standards",
            "DetailItemFactorySettings",
            ShadowStudySettings.Name
        };

        private static string GetTemplateJsonPath()
        {
            string projectRoot = TestPathHelper.GetProjectRoot();
            string candidate = Path.Combine(
                projectRoot,
                "src",
                "Platform",
                "Settings",
                "Addin",
                "Defaults",
                "SyntheticSettings.template.json");

            Assert.IsTrue(File.Exists(candidate), $"Expected SyntheticSettings.template.json at '{candidate}'.");
            return candidate;
        }

        [Test]
        public void TemplateJson_StructuralRoundTrip_PreservesSettingsRootModuleKeysAndSubstructure()
        {
            string templatePath = GetTemplateJsonPath();
            string rawJson = File.ReadAllText(templatePath);
            JObject rawRoot = JObject.Parse(rawJson);

            JObject? rawSettings = rawRoot["Settings"] as JObject;
            Assert.IsNotNull(rawSettings, "SyntheticSettings.template.json must define a top-level 'Settings' object.");

            var actualKeys = new List<string>();
            foreach (JProperty property in rawSettings!.Properties())
            {
                actualKeys.Add(property.Name);
            }

            CollectionAssert.AreEquivalent(ExpectedTemplateModuleKeys, actualKeys);
            Assert.IsFalse(rawSettings.ContainsKey(SettingsModuleKeys.Sync), "Template must exclude SyncSettings.");

            AssertObjectHasProperties(rawSettings["Worksets"], "WorksetFile", "WorksetPath", "WorksetGroup");
            AssertObjectHasProperties(rawSettings["ViewAutoNumber"], "viewAutoNumFamily", "viewAutoNumFamilyType", "viewAutoNumXGridName", "viewAutoNumYGridName");
            AssertObjectHasProperties(rawSettings["MaterialLibrary"], "LibraryFolderPath");
            AssertObjectHasProperties(rawSettings["ProjectMaterials"], "DefaultRelativePath", "OverrideFolderPath");
            AssertObjectHasProperties(rawSettings["FileUtility"], "ArchiveDirectories", "AlternatePaths");
            Assert.AreEqual(JTokenType.Array, rawSettings["FileUtility"]!["ArchiveDirectories"]!.Type);
            Assert.AreEqual(JTokenType.Object, rawSettings["FileUtility"]!["AlternatePaths"]!.Type);
            AssertObjectHasProperties(rawSettings["Standards"], "StandardsFilePath");
            AssertObjectHasProperties(rawSettings["DetailItemFactorySettings"], "OutputPath", "TargetSubcategory");
            AssertObjectHasProperties(
                rawSettings[ShadowStudySettings.Name],
                "SchemaVersion",
                "DefaultTimeIncrementMinutes",
                "DefaultMinSunAltitudeDegrees",
                "DefaultMaxCompositeDarknessPercent",
                "DefaultPreserveVisualStyle",
                "UserPresets");
            Assert.AreEqual(JTokenType.Array, rawSettings[ShadowStudySettings.Name]!["UserPresets"]!.Type);

            Config? fileConfig = Config.ReadFromFile(templatePath);
            Config jsonConfig = Config.ByJson(rawJson);

            Assert.IsNotNull(fileConfig);
            Assert.IsNotNull(fileConfig!.settings);
            Assert.IsNotNull(jsonConfig.settings);

            foreach (string key in ExpectedTemplateModuleKeys)
            {
                Assert.IsTrue(fileConfig.Contains(key), $"Config.ReadFromFile missing module '{key}'.");
                Assert.IsTrue(jsonConfig.Contains(key), $"Config.ByJson missing module '{key}'.");
            }

            Assert.IsFalse(fileConfig.Contains(SettingsModuleKeys.Sync));
            Assert.IsFalse(jsonConfig.Contains(SettingsModuleKeys.Sync));

            string reserializedJson = JsonConvert.SerializeObject(fileConfig, Formatting.Indented);
            JObject reserializedRoot = JObject.Parse(reserializedJson);
            Assert.IsTrue(
                JToken.DeepEquals(rawRoot, reserializedRoot),
                "Config JSON round-trip must structurally preserve SyntheticSettings.template.json.");

            string tempPath = Path.Combine(Path.GetTempPath(), $"SyntheticTemplateRoundTrip_{Guid.NewGuid():N}.json");
            try
            {
                Config.WriteToFile(tempPath, jsonConfig);
                Config? roundTrippedFileConfig = Config.ReadFromFile(tempPath);

                Assert.IsNotNull(roundTrippedFileConfig);
                Assert.IsNotNull(roundTrippedFileConfig!.settings);
                CollectionAssert.AreEquivalent(ExpectedTemplateModuleKeys, roundTrippedFileConfig.settings.Keys);
                Assert.IsFalse(roundTrippedFileConfig.Contains(SettingsModuleKeys.Sync));

                JObject fileRoundTripRoot = JObject.Parse(File.ReadAllText(tempPath));
                Assert.IsTrue(
                    JToken.DeepEquals(rawRoot, fileRoundTripRoot),
                    "Config.WriteToFile -> Config.ReadFromFile must structurally preserve SyntheticSettings.template.json.");
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }

            var nonNullSettings = new Dictionary<string, object>();
            foreach (KeyValuePair<string, object?> kvp in fileConfig.settings)
            {
                if (kvp.Value != null)
                {
                    nonNullSettings[kvp.Key] = kvp.Value;
                }
            }

            Assert.IsFalse(
                SyncDriftComparer.HasSyncDrift(rawSettings.ToString(Formatting.None), nonNullSettings),
                "Template 'Settings' object must have zero sync drift against Config.ReadFromFile.");
        }

        [Test]
        public void TemplateJson_ShadowStudySection_MatchesCoreShadowStudySettingsDefaults()
        {
            string templatePath = GetTemplateJsonPath();
            JObject rawRoot = JObject.Parse(File.ReadAllText(templatePath));
            JToken? rawShadowToken = rawRoot["Settings"]?[ShadowStudySettings.Name];
            Assert.IsNotNull(rawShadowToken);

            Config? templateConfig = Config.ReadFromFile(templatePath);
            Assert.IsNotNull(templateConfig);

            ShadowStudySettings fromTemplate = templateConfig!.GetSettings<ShadowStudySettings>(ShadowStudySettings.Name);
            ShadowStudySettings coreDefaults = new ShadowStudySettings().Defaults();

            Assert.AreEqual(coreDefaults.SchemaVersion, fromTemplate.SchemaVersion);
            Assert.AreEqual(coreDefaults.DefaultTimeIncrementMinutes, fromTemplate.DefaultTimeIncrementMinutes);
            Assert.AreEqual(coreDefaults.DefaultMinSunAltitudeDegrees, fromTemplate.DefaultMinSunAltitudeDegrees, 1e-9);
            Assert.AreEqual(coreDefaults.DefaultMaxCompositeDarknessPercent, fromTemplate.DefaultMaxCompositeDarknessPercent);
            Assert.AreEqual(coreDefaults.DefaultPreserveVisualStyle, fromTemplate.DefaultPreserveVisualStyle);
            Assert.IsNotNull(fromTemplate.UserPresets);
            Assert.AreEqual(0, fromTemplate.UserPresets.Count);
            Assert.IsTrue(fromTemplate.IsValid());

            JObject expectedCoreShadowToken = JObject.FromObject(coreDefaults);
            Assert.IsTrue(
                JToken.DeepEquals(rawShadowToken, expectedCoreShadowToken),
                "Template 'ShadowStudy' JSON section must match Core ShadowStudySettings().Defaults() serialization.");
        }

        [Test]
        public void Config_CoreDefaultsAndJsonRoundTrip_PreservesTypedModulesAndWrappedLegacyPayloads()
        {
            var factories = new List<KeyValuePair<string, Func<ISettingModule>>>
            {
                new KeyValuePair<string, Func<ISettingModule>>(
                    ShadowStudySettings.Name,
                    () => new ShadowStudySettings().Defaults())
            };

            Config defaultConfig = new Config().Defaults(factories);
            Assert.IsTrue(defaultConfig.Contains(ShadowStudySettings.Name));
            Assert.IsFalse(defaultConfig.Contains(SettingsModuleKeys.Sync));

            string defaultJson = JsonConvert.SerializeObject(defaultConfig, Formatting.Indented);
            Config roundTrippedDefaults = Config.ByJson(defaultJson);
            ShadowStudySettings recoveredDefaults = roundTrippedDefaults.GetSettings<ShadowStudySettings>(ShadowStudySettings.Name);

            Assert.AreEqual(1, recoveredDefaults.SchemaVersion);
            Assert.AreEqual(60, recoveredDefaults.DefaultTimeIncrementMinutes);
            Assert.AreEqual(5.0, recoveredDefaults.DefaultMinSunAltitudeDegrees, 1e-9);
            Assert.AreEqual(80, recoveredDefaults.DefaultMaxCompositeDarknessPercent);
            Assert.IsFalse(recoveredDefaults.DefaultPreserveVisualStyle);
            Assert.IsNotNull(recoveredDefaults.UserPresets);
            Assert.AreEqual(0, recoveredDefaults.UserPresets.Count);
            Assert.IsTrue(recoveredDefaults.IsValid());

            var customShadow = new ShadowStudySettings
            {
                SchemaVersion = 1,
                DefaultTimeIncrementMinutes = 30,
                DefaultMinSunAltitudeDegrees = 11.5,
                DefaultMaxCompositeDarknessPercent = 65,
                DefaultPreserveVisualStyle = true,
                UserPresets = new List<ShadowStudyPresetModel>
                {
                    new ShadowStudyPresetModel
                    {
                        Name = "Core RoundTrip Preset",
                        TimeIncrementMinutes = 15,
                        MinSunAltitudeDegrees = 9.0,
                        MaxCompositeDarknessPercent = 70,
                        PreserveVisualStyle = true,
                        Dates = new List<ShadowStudyPresetDateSpec>
                        {
                            new ShadowStudyPresetDateSpec
                            {
                                AstronomicalEvent = AstronomicalEventKind.SummerSolstice
                            },
                            new ShadowStudyPresetDateSpec
                            {
                                AstronomicalEvent = AstronomicalEventKind.None,
                                CustomDate = new DateTime(2026, 8, 15),
                                CustomLabel = "Mid-August"
                            }
                        }
                    }
                }
            };

            var customConfig = new Config(ShadowStudySettings.Name, customShadow);
            string tempPath = Path.Combine(Path.GetTempPath(), $"SyntheticCoreConfigRoundTrip_{Guid.NewGuid():N}.json");
            try
            {
                Config.WriteToFile(tempPath, customConfig);
                Config? readBack = Config.ReadFromFile(tempPath);

                Assert.IsNotNull(readBack);
                ShadowStudySettings recoveredCustom = readBack!.GetSettings<ShadowStudySettings>(ShadowStudySettings.Name);

                Assert.IsTrue(recoveredCustom.IsValid());
                Assert.AreEqual(30, recoveredCustom.DefaultTimeIncrementMinutes);
                Assert.AreEqual(11.5, recoveredCustom.DefaultMinSunAltitudeDegrees, 1e-9);
                Assert.AreEqual(65, recoveredCustom.DefaultMaxCompositeDarknessPercent);
                Assert.IsTrue(recoveredCustom.DefaultPreserveVisualStyle);
                Assert.AreEqual(1, recoveredCustom.UserPresets.Count);
                Assert.AreEqual("Core RoundTrip Preset", recoveredCustom.UserPresets[0].Name);
                Assert.AreEqual(2, recoveredCustom.UserPresets[0].Dates.Count);
                Assert.AreEqual(AstronomicalEventKind.SummerSolstice, recoveredCustom.UserPresets[0].Dates[0].AstronomicalEvent);
                Assert.AreEqual(new DateTime(2026, 8, 15), recoveredCustom.UserPresets[0].Dates[1].CustomDate);
                Assert.AreEqual("Mid-August", recoveredCustom.UserPresets[0].Dates[1].CustomLabel);
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }

            const string legacyWrappedJson = @"{
                ""Settings"": {
                    ""ShadowStudy"": {
                        ""Data"": {
                            ""SchemaVersion"": 1,
                            ""DefaultTimeIncrementMinutes"": 120,
                            ""DefaultMinSunAltitudeDegrees"": 14.0,
                            ""DefaultMaxCompositeDarknessPercent"": 55,
                            ""DefaultPreserveVisualStyle"": true,
                            ""UserPresets"": []
                        }
                    }
                }
            }";

            Config legacyWrappedConfig = Config.ByJson(legacyWrappedJson);
            ShadowStudySettings unwrapped = legacyWrappedConfig.GetSettings<ShadowStudySettings>(ShadowStudySettings.Name);
            Assert.AreEqual(120, unwrapped.DefaultTimeIncrementMinutes);
            Assert.AreEqual(14.0, unwrapped.DefaultMinSunAltitudeDegrees, 1e-9);
            Assert.AreEqual(55, unwrapped.DefaultMaxCompositeDarknessPercent);
            Assert.IsTrue(unwrapped.DefaultPreserveVisualStyle);

            var fallbackDictConfig = new Config();
            fallbackDictConfig.SetSettings(
                ShadowStudySettings.Name,
                new Dictionary<string, object>
                {
                    { "DefaultTimeIncrementMinutes", 15 },
                    { "DefaultPreserveVisualStyle", true }
                });
            ShadowStudySettings fromFallbackDict = fallbackDictConfig.GetSettings<ShadowStudySettings>(ShadowStudySettings.Name);
            Assert.AreEqual(15, fromFallbackDict.DefaultTimeIncrementMinutes);
            Assert.IsTrue(fromFallbackDict.DefaultPreserveVisualStyle);

            ShadowStudySettings missingModule = new Config().GetSettings<ShadowStudySettings>("NonExistentModule");
            Assert.AreEqual(60, missingModule.DefaultTimeIncrementMinutes);
            Assert.IsTrue(missingModule.IsValid());

            string missingFilePath = Path.Combine(Path.GetTempPath(), $"SyntheticMissing_{Guid.NewGuid():N}.json");
            Assert.IsNull(Config.ReadFromFile(missingFilePath));
            StringAssert.Contains(ShadowStudySettings.Name, defaultConfig.ToString());
        }

        [Test]
        public void ShadowStudySettings_ConfigJsonDeserialization_NormalizesNullCollectionsAndDetectsDrift()
        {
            const string legacyPartialConfigJson = @"{
                ""Settings"": {
                    ""ShadowStudy"": {
                        ""SchemaVersion"": 0,
                        ""DefaultTimeIncrementMinutes"": 30,
                        ""DefaultMinSunAltitudeDegrees"": 8.0,
                        ""DefaultMaxCompositeDarknessPercent"": 75,
                        ""DefaultPreserveVisualStyle"": false,
                        ""UserPresets"": null
                    }
                }
            }";

            Config partialConfig = Config.ByJson(legacyPartialConfigJson);
            ShadowStudySettings normalized = partialConfig.GetSettings<ShadowStudySettings>(ShadowStudySettings.Name);
            Assert.AreEqual(1, normalized.SchemaVersion);
            Assert.IsNotNull(normalized.UserPresets);
            Assert.AreEqual(0, normalized.UserPresets.Count);
            Assert.IsTrue(normalized.IsValid());

            const string nullDatesConfigJson = @"{
                ""Settings"": {
                    ""ShadowStudy"": {
                        ""SchemaVersion"": 1,
                        ""UserPresets"": [
                            {
                                ""Name"": ""Null Dates Preset"",
                                ""Dates"": null
                            }
                        ]
                    }
                }
            }";

            ShadowStudySettings normalizedPreset = Config.ByJson(nullDatesConfigJson)
                .GetSettings<ShadowStudySettings>(ShadowStudySettings.Name);
            Assert.AreEqual(1, normalizedPreset.UserPresets.Count);
            Assert.IsNotNull(normalizedPreset.UserPresets[0].Dates);
            Assert.AreEqual(0, normalizedPreset.UserPresets[0].Dates.Count);
            Assert.IsFalse(normalizedPreset.IsValid(), "A preset with zero dates must fail IsValid().");

            var baselineSettings = new Dictionary<string, object>
            {
                { ShadowStudySettings.Name, new ShadowStudySettings().Defaults() }
            };
            string matchingJson = JsonConvert.SerializeObject(baselineSettings, Formatting.Indented);
            Assert.IsFalse(SyncDriftComparer.HasSyncDrift(matchingJson, baselineSettings));

            var changedSettings = new Dictionary<string, object>
            {
                {
                    ShadowStudySettings.Name,
                    new ShadowStudySettings
                    {
                        DefaultTimeIncrementMinutes = 15
                    }
                }
            };
            string changedJson = JsonConvert.SerializeObject(changedSettings, Formatting.Indented);
            Assert.IsTrue(SyncDriftComparer.HasSyncDrift(changedJson, baselineSettings));
        }

        private static void AssertObjectHasProperties(JToken? token, params string[] expectedPropertyNames)
        {
            JObject? obj = token as JObject;
            Assert.IsNotNull(obj, "Expected JSON token to be an object.");

            foreach (string propertyName in expectedPropertyNames)
            {
                Assert.IsTrue(
                    obj!.ContainsKey(propertyName),
                    $"Expected JSON object to contain property '{propertyName}'.");
            }
        }
    }
}
