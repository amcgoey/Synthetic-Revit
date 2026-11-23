using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Synthetic.Features.SettingsDashboard.ViewModels;
using Synthetic.Features.StandardsManagement.Settings;
using Synthetic.Features.Worksets.Settings;
using Synthetic.Modules.ShadowStudy.Adapters;
using Synthetic.Modules.ShadowStudy.Models;
using Synthetic.Modules.ShadowStudy.Settings;
using Synthetic.Modules.ShadowStudy.ViewModels;
using Synthetic.Platform.Host;
using Synthetic.Platform.Settings;
using Synthetic.Platform.UI;
using SyntheticTests.Helpers;

namespace SyntheticTests.Modules.ShadowStudy
{
    /// <summary>
    /// Tier 2 live Revit integration tests for Ticket #156:
    /// End-to-end Settings Dashboard persistence and Cancel deep-clone cache isolation,
    /// Shadow Study dialog studio-default inheritance from Settings Dashboard saves,
    /// firmwide JSON Push/Pull and Export/Import round-tripping across all 8 synced modules,
    /// and legacy sync-drift tolerance for pre-ShadowStudy linked JSON files.
    /// </summary>
    [TestFixture]
    public class Tier2_ShadowStudyDashboardSyncTests
    {
        private UIApplication? _uiapp;
        private bool _previousSuppressUi;
        private bool _previousSuppressDiskFallback;

        [OneTimeSetUp]
        public void OneTimeSetup(UIApplication uiapp)
        {
            _uiapp = uiapp;
            HostSettingsRegistrar.EnsureRegistered();
            TestWorkerJournalMarker.EnsureWritten(uiapp);
        }

        [SetUp]
        public void SetUp()
        {
            _previousSuppressUi = ProgressCoordinator.SuppressUI;
            _previousSuppressDiskFallback = SettingsRuntimeOptions.SuppressFirmwideDiskFallback;
            ProgressCoordinator.SuppressUI = true;
            SettingsRuntimeOptions.SuppressFirmwideDiskFallback = true;
            SettingsManager.ClearCache();
        }

        [TearDown]
        public void TearDown()
        {
            SettingsManager.ClearCache();
            ProgressCoordinator.SuppressUI = _previousSuppressUi;
            SettingsRuntimeOptions.SuppressFirmwideDiskFallback = _previousSuppressDiskFallback;
        }

        private Document CreateFreshProjectDocument()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context must not be null.");
            return _uiapp!.Application.NewProjectDocument(UnitSystem.Imperial);
        }

        private static string CreateTempJsonPath()
        {
            return Path.Combine(Path.GetTempPath(), $"ShadowDashboardSyncTest_{Guid.NewGuid():N}.json");
        }

        [Test]
        public void SettingsDashboardViewModel_ShadowStudyPage_CancelIsolatesCache_AndSavePersistsStudioDefaultsToDialog()
        {
            Document? doc = null;

            try
            {
                doc = CreateFreshProjectDocument();

                // Seed initial settings in document storage
                SettingsManager.Save(doc, new ShadowStudySettings
                {
                    DefaultTimeIncrementMinutes = 60,
                    DefaultMinSunAltitudeDegrees = 5.0,
                    DefaultMaxCompositeDarknessPercent = 80,
                    DefaultPreserveVisualStyle = false,
                    UserPresets = new List<ShadowStudyPresetModel>
                    {
                        new ShadowStudyPresetModel
                        {
                            Name = "Initial Shared Preset",
                            TimeIncrementMinutes = 60,
                            MinSunAltitudeDegrees = 5.0,
                            MaxCompositeDarknessPercent = 80,
                            PreserveVisualStyle = false,
                            Dates = new List<ShadowStudyPresetDateSpec>
                            {
                                new ShadowStudyPresetDateSpec { AstronomicalEvent = AstronomicalEventKind.SummerSolstice }
                            }
                        }
                    }
                });

                // 1. Open SettingsDashboardViewModel, edit Shadow Study page, and Cancel -> cache & storage untouched
                var dashboardCancel = new SettingsDashboardViewModel(doc, IntPtr.Zero);
                ShadowStudySettingsViewModel? shadowPage = dashboardCancel.SettingModules
                    .OfType<ShadowStudySettingsViewModel>()
                    .FirstOrDefault();

                Assert.IsNotNull(shadowPage, "SettingsDashboardViewModel must register ShadowStudySettingsViewModel.");
                Assert.IsTrue(shadowPage!.IsOverridden);
                Assert.AreEqual(1, shadowPage.UserPresets.Count);

                shadowPage.DefaultTimeIncrementMinutes = 15;
                shadowPage.DefaultMinSunAltitudeText = "12.5";
                shadowPage.DefaultMaxCompositeDarknessText = "65";
                shadowPage.DefaultPreserveVisualStyle = true;
                shadowPage.SelectedPresetTimeIncrementMinutes = 120;
                shadowPage.CustomDateToAdd = new DateTime(2026, 8, 1);
                shadowPage.AddCustomDateCommand.Execute(null);

                Assert.IsTrue(shadowPage.IsDirty);
                Assert.IsTrue(dashboardCancel.SaveCommand.CanExecute(null));
                dashboardCancel.CancelCommand.Execute(null);

                // Verify SettingsManager cache was NEVER mutated by the cancelled edits
                ShadowStudySettings cachedAfterCancel = SettingsManager.Get<ShadowStudySettings>(doc);
                Assert.AreEqual(60, cachedAfterCancel.DefaultTimeIncrementMinutes);
                Assert.AreEqual(5.0, cachedAfterCancel.DefaultMinSunAltitudeDegrees, 1e-6);
                Assert.AreEqual(80, cachedAfterCancel.DefaultMaxCompositeDarknessPercent);
                Assert.IsFalse(cachedAfterCancel.DefaultPreserveVisualStyle);
                Assert.AreEqual(1, cachedAfterCancel.UserPresets[0].Dates.Count);
                Assert.AreEqual(60, cachedAfterCancel.UserPresets[0].TimeIncrementMinutes);

                // 2. Open fresh SettingsDashboardViewModel, edit Shadow Study page, and SaveCommand -> persists to DataStorage
                var dashboardSave = new SettingsDashboardViewModel(doc, IntPtr.Zero);
                ShadowStudySettingsViewModel shadowSavePage = dashboardSave.SettingModules
                    .OfType<ShadowStudySettingsViewModel>()
                    .Single();

                shadowSavePage.DefaultTimeIncrementMinutes = 15;
                shadowSavePage.DefaultMinSunAltitudeText = "11.5";
                shadowSavePage.DefaultMaxCompositeDarknessText = "65";
                shadowSavePage.DefaultPreserveVisualStyle = true;
                shadowSavePage.SelectedPresetTimeIncrementMinutes = 30;
                shadowSavePage.CustomDateToAdd = new DateTime(2026, 7, 4);
                shadowSavePage.AddCustomDateCommand.Execute(null);

                Assert.IsTrue(dashboardSave.SaveCommand.CanExecute(null));
                dashboardSave.SaveCommand.Execute(null);

                // Clear cache to prove DataStorage persistence
                SettingsManager.ClearCache(doc);
                ShadowStudySettings persisted = SettingsManager.Get<ShadowStudySettings>(doc);
                Assert.AreEqual(15, persisted.DefaultTimeIncrementMinutes);
                Assert.AreEqual(11.5, persisted.DefaultMinSunAltitudeDegrees, 1e-6);
                Assert.AreEqual(65, persisted.DefaultMaxCompositeDarknessPercent);
                Assert.IsTrue(persisted.DefaultPreserveVisualStyle);
                Assert.AreEqual(1, persisted.UserPresets.Count);
                Assert.AreEqual(30, persisted.UserPresets[0].TimeIncrementMinutes);
                Assert.AreEqual(2, persisted.UserPresets[0].Dates.Count);

                // 3. Verify ShadowStudyViewModel opened from this document inherits saved studio defaults & shared preset
                var adapter = new RevitShadowStudyAdapter(doc, null, _uiapp);
                var dialogVm = new ShadowStudyViewModel(adapter, new NoOpFolderDialogService());

                Assert.AreEqual(15, dialogVm.TimeStepMinutes);
                Assert.AreEqual(11.5, dialogVm.MinSunAltitudeDegrees, 1e-6);
                Assert.AreEqual(65, dialogVm.MaxCompositeDarknessPercent);
                Assert.IsTrue(dialogVm.PreserveVisualStyle);
                Assert.IsTrue(dialogVm.AvailablePresets.Any(p => p.Name == "Initial Shared Preset"));
            }
            finally
            {
                if (doc != null && doc.IsValidObject) doc.Close(false);
            }
        }

        [Test]
        public void ShadowStudySettingsViewModel_Reload_RefreshesIsOverriddenFromDataStorage()
        {
            Document? doc = null;

            try
            {
                doc = CreateFreshProjectDocument();

                var vm = RevitShadowStudyAdapter.CreateSettingsViewModel(doc, IntPtr.Zero);
                Assert.IsFalse(vm.IsOverridden);

                SettingsManager.Save(doc, new ShadowStudySettings
                {
                    DefaultTimeIncrementMinutes = 30,
                    DefaultMinSunAltitudeDegrees = 8.0,
                    DefaultMaxCompositeDarknessPercent = 70,
                    DefaultPreserveVisualStyle = true
                });

                vm.Reload();
                Assert.IsTrue(vm.IsOverridden);
                Assert.IsFalse(vm.IsDirty);
                Assert.AreEqual(30, vm.DefaultTimeIncrementMinutes);
                Assert.AreEqual(8.0, vm.DefaultMinSunAltitudeDegrees, 1e-6);
                Assert.AreEqual(70, vm.DefaultMaxCompositeDarknessPercent);
                Assert.IsTrue(vm.DefaultPreserveVisualStyle);

                SettingsManager.Delete(doc, ShadowStudySettings.Name);

                vm.Reload();
                Assert.IsFalse(vm.IsOverridden);
                Assert.IsFalse(vm.IsDirty);
            }
            finally
            {
                if (doc != null && doc.IsValidObject) doc.Close(false);
            }
        }

        [Test]
        public void FirmwideSync_RoundTripsAllEightModulesIncludingShadowStudy_AndToleratesLegacyJsonWithoutShadowStudy()
        {
            string tempJsonPath = CreateTempJsonPath();
            Document? sourceDoc = null;
            Document? targetDoc = null;

            try
            {
                sourceDoc = CreateFreshProjectDocument();
                targetDoc = CreateFreshProjectDocument();

                using (Transaction tx = new Transaction(sourceDoc, "Seed 8 Modules for Shadow Sync Test"))
                {
                    tx.Start();
                    SettingsManager.Save(sourceDoc, new WorksetSettings("FirmWorksets.xlsx", @"C:\Firm\Worksets", "FirmGroup"));
                    SettingsManager.Save(sourceDoc, new StandardsSettings { StandardsFilePath = @"C:\Firm\standards.json" });
                    SettingsManager.Save(sourceDoc, new ShadowStudySettings
                    {
                        SchemaVersion = 1,
                        DefaultTimeIncrementMinutes = 15,
                        DefaultMinSunAltitudeDegrees = 10.0,
                        DefaultMaxCompositeDarknessPercent = 60,
                        DefaultPreserveVisualStyle = true,
                        UserPresets = new List<ShadowStudyPresetModel>
                        {
                            new ShadowStudyPresetModel
                            {
                                Name = "Office Solstice Package",
                                TimeIncrementMinutes = 30,
                                MinSunAltitudeDegrees = 7.5,
                                MaxCompositeDarknessPercent = 75,
                                PreserveVisualStyle = false,
                                Dates = new List<ShadowStudyPresetDateSpec>
                                {
                                    new ShadowStudyPresetDateSpec { AstronomicalEvent = AstronomicalEventKind.SummerSolstice },
                                    new ShadowStudyPresetDateSpec
                                    {
                                        AstronomicalEvent = AstronomicalEventKind.None,
                                        CustomDate = new DateTime(2026, 5, 15),
                                        CustomLabel = "Milestone"
                                    }
                                }
                            }
                        }
                    });
                    SettingsManager.Save(sourceDoc, new SyncSettings { LinkedFilePath = tempJsonPath });
                    tx.Commit();
                }

                // 1. PushToLinkedFile includes all 8 synced modules + SyncSettings
                SettingsManager.PushToLinkedFile(sourceDoc, tempJsonPath);
                JObject pushedJson = JObject.Parse(File.ReadAllText(tempJsonPath));
                Assert.AreEqual(8, SettingsModuleRegistry.SyncedModules.Count);
                foreach (SettingsModuleDescriptor descriptor in SettingsModuleRegistry.SyncedModules)
                {
                    Assert.IsNotNull(pushedJson[descriptor.ModuleKey],
                        $"Pushed JSON must include '{descriptor.ModuleKey}'.");
                }

                // 2. PullFromLinkedFile round-trips ShadowStudySettings and existing modules without truncation
                SettingsManager.PullFromLinkedFile(targetDoc, tempJsonPath);
                SettingsManager.ClearCache(targetDoc);

                ShadowStudySettings pulledShadow = SettingsManager.Get<ShadowStudySettings>(targetDoc);
                Assert.AreEqual(15, pulledShadow.DefaultTimeIncrementMinutes);
                Assert.AreEqual(10.0, pulledShadow.DefaultMinSunAltitudeDegrees, 1e-6);
                Assert.AreEqual(60, pulledShadow.DefaultMaxCompositeDarknessPercent);
                Assert.IsTrue(pulledShadow.DefaultPreserveVisualStyle);
                Assert.AreEqual(1, pulledShadow.UserPresets.Count);
                Assert.AreEqual("Office Solstice Package", pulledShadow.UserPresets[0].Name);
                Assert.AreEqual(2, pulledShadow.UserPresets[0].Dates.Count);
                Assert.AreEqual(AstronomicalEventKind.SummerSolstice, pulledShadow.UserPresets[0].Dates[0].AstronomicalEvent);
                Assert.AreEqual(new DateTime(2026, 5, 15), pulledShadow.UserPresets[0].Dates[1].CustomDate);
                Assert.AreEqual("FirmWorksets.xlsx", SettingsManager.Get<WorksetSettings>(targetDoc).WorksetFile);
                Assert.AreEqual(@"C:\Firm\standards.json", SettingsManager.Get<StandardsSettings>(targetDoc).StandardsFilePath);

                // 3. ExportAllToFile / ImportAllFromFile round-trip
                SettingsManager.ExportAllToFile(sourceDoc, tempJsonPath);
                JObject exportedJson = JObject.Parse(File.ReadAllText(tempJsonPath));
                Assert.IsNull(exportedJson[SyncSettings.Name]);
                Assert.IsNotNull(exportedJson[ShadowStudySettings.Name]);

                // 4. Legacy drift tolerance: removing "ShadowStudy" from linked JSON returns false;
                //    modifying "ShadowStudy" in linked JSON returns true.
                var legacyDict = JsonConvert.DeserializeObject<Dictionary<string, JToken>>(File.ReadAllText(tempJsonPath))!;
                legacyDict.Remove(ShadowStudySettings.Name);
                File.WriteAllText(tempJsonPath, JsonConvert.SerializeObject(legacyDict, Formatting.Indented));

                Assert.IsFalse(SettingsManager.CheckSyncDrift(sourceDoc, out _),
                    "Legacy linked JSON predating 'ShadowStudy' must not trigger false-positive sync drift.");

                var modifiedShadow = new ShadowStudySettings
                {
                    DefaultTimeIncrementMinutes = 120,
                    DefaultMinSunAltitudeDegrees = 20.0
                };
                legacyDict[ShadowStudySettings.Name] = JToken.FromObject(modifiedShadow);
                File.WriteAllText(tempJsonPath, JsonConvert.SerializeObject(legacyDict, Formatting.Indented));

                Assert.IsTrue(SettingsManager.CheckSyncDrift(sourceDoc, out _),
                    "Modified 'ShadowStudy' key in linked JSON must trigger sync drift.");
            }
            finally
            {
                if (targetDoc != null && targetDoc.IsValidObject) targetDoc.Close(false);
                if (sourceDoc != null && sourceDoc.IsValidObject) sourceDoc.Close(false);
                if (File.Exists(tempJsonPath)) File.Delete(tempJsonPath);
            }
        }

        private sealed class NoOpFolderDialogService : IFolderDialogService
        {
            public string? SelectFolder(string title, string? initialPath = null) => null;
        }
    }
}
