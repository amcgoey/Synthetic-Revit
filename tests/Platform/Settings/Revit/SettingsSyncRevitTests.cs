using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Events;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Synthetic.Features.MaterialManagement.Settings;
using Synthetic.Features.SettingsDashboard.Handlers;
using Synthetic.Features.StandardsManagement.Settings;
using Synthetic.Features.ViewManagement.Settings;
using Synthetic.Features.Worksets.Settings;
using Synthetic.Features.DetailItemFactory.Adapters;
using Synthetic.Features.DetailItemFactory.Settings;
using Synthetic.Features.DetailItemFactory.ViewModels;
using Synthetic.Features.SettingsDashboard.ViewModels;
using Synthetic.Features.ShadowStudy.Models;
using Synthetic.Features.ShadowStudy.Settings;
using Synthetic.Platform.Host;
using Synthetic.Platform.Settings;
using Synthetic.Platform.UI;
using SyntheticTests.Fixtures;

namespace SyntheticTests.Platform.Settings
{
    [TestFixture]
    [RevitAdapterTest]
    public class SettingsSyncRevitTests : RevitTemplateRollbackFixture
    {
        private bool _previousSuppressUi;
        private bool _previousSuppressDiskFallback;

        protected override RevitBaselineDocumentKind BaselineKind => RevitBaselineDocumentKind.ImperialProject;

        protected override void OnTestSetUp()
        {
            _previousSuppressUi = ProgressCoordinator.SuppressUI;
            _previousSuppressDiskFallback = SettingsRuntimeOptions.SuppressFirmwideDiskFallback;
            ProgressCoordinator.SuppressUI = true;
            SettingsRuntimeOptions.SuppressFirmwideDiskFallback = true;
            SettingsManager.ClearCache();
        }

        protected override void OnTestTearDown()
        {
            SettingsManager.ClearCache();
            ProgressCoordinator.SuppressUI = _previousSuppressUi;
            SettingsRuntimeOptions.SuppressFirmwideDiskFallback = _previousSuppressDiskFallback;
        }

        private static string CreateTempJsonPath()
        {
            return Path.Combine(Path.GetTempPath(), $"SyntheticSettingsSyncTest_{Guid.NewGuid():N}.json");
        }

        private static void SaveDistinctSettingsToDocument(Document doc, string linkedFilePath)
        {
            using (Transaction trans = new Transaction(doc, "Seed Settings Sync Test Data"))
            {
                trans.Start();

                SettingsManager.Save(doc, new WorksetSettings("CustomWorksets.xlsx", @"C:\Worksets", "CustomGroup"));
                SettingsManager.Save(doc, new ViewAutoNumSettings("CustomMarker", "CustomType", "CustomX", "CustomY"));
                SettingsManager.Save(doc, new MaterialLibrarySettings { LibraryFolderPath = @"C:\CustomMaterialLibrary" });
                SettingsManager.Save(doc, new ProjectMaterialSettings
                {
                    DefaultRelativePath = @".\CustomProjectMaterials\",
                    OverrideFolderPath = @"C:\OverrideMaterials"
                });
                SettingsManager.Save(doc, new FileUtilitySettings
                {
                    ArchiveDirectories = new List<string> { @"C:\ArchiveA", @"C:\ArchiveB" },
                    AlternatePaths = new Dictionary<string, List<string>>
                    {
                        { @"C:\Source", new List<string> { @"C:\Alt1" } }
                    }
                });
                SettingsManager.Save(doc, new StandardsSettings
                {
                    StandardsFilePath = @"C:\Standards\project-standards.json"
                });
                SettingsManager.Save(doc, new DetailItemFactorySettings
                {
                    OutputPath = @"C:\DetailItems\Output",
                    TargetSubcategory = "Detail Items"
                });
                SettingsManager.Save(doc, new ShadowStudySettings
                {
                    SchemaVersion = 1,
                    DefaultTimeIncrementMinutes = 30,
                    DefaultMinSunAltitudeDegrees = 9.5,
                    DefaultMaxCompositeDarknessPercent = 70,
                    DefaultPreserveVisualStyle = true,
                    UserPresets = new List<ShadowStudyPresetModel>
                    {
                        new ShadowStudyPresetModel
                        {
                            Name = "Firmwide Equinox",
                            TimeIncrementMinutes = 15,
                            MinSunAltitudeDegrees = 10.0,
                            MaxCompositeDarknessPercent = 65,
                            PreserveVisualStyle = true,
                            Dates = new List<ShadowStudyPresetDateSpec>
                            {
                                new ShadowStudyPresetDateSpec { AstronomicalEvent = AstronomicalEventKind.SpringEquinox },
                                new ShadowStudyPresetDateSpec
                                {
                                    AstronomicalEvent = AstronomicalEventKind.None,
                                    CustomDate = new DateTime(2026, 7, 4),
                                    CustomLabel = "Site Open"
                                }
                            }
                        }
                    }
                });
                SettingsManager.Save(doc, new SyncSettings
                {
                    LinkedFilePath = linkedFilePath
                });

                trans.Commit();
            }
        }

        private static void AssertAllEightModulesMatchSeededValues(Document doc)
        {
            var worksets = SettingsManager.Get<WorksetSettings>(doc);
            Assert.AreEqual("CustomWorksets.xlsx", worksets.WorksetFile);
            Assert.AreEqual(@"C:\Worksets", worksets.WorksetPath);
            Assert.AreEqual("CustomGroup", worksets.WorksetGroup);

            var viewAutoNum = SettingsManager.Get<ViewAutoNumSettings>(doc);
            Assert.AreEqual("CustomMarker", viewAutoNum.ViewAutoNumFamily);
            Assert.AreEqual("CustomType", viewAutoNum.ViewAutoNumFamilyType);
            Assert.AreEqual("CustomX", viewAutoNum.ViewAutoNumXGridName);
            Assert.AreEqual("CustomY", viewAutoNum.ViewAutoNumYGridName);

            var materialLib = SettingsManager.Get<MaterialLibrarySettings>(doc);
            Assert.AreEqual(@"C:\CustomMaterialLibrary", materialLib.LibraryFolderPath);

            var projectMat = SettingsManager.Get<ProjectMaterialSettings>(doc);
            Assert.AreEqual(@".\CustomProjectMaterials\", projectMat.DefaultRelativePath);
            Assert.AreEqual(@"C:\OverrideMaterials", projectMat.OverrideFolderPath);

            var fileUtil = SettingsManager.Get<FileUtilitySettings>(doc);
            CollectionAssert.AreEqual(new[] { @"C:\ArchiveA", @"C:\ArchiveB" }, fileUtil.ArchiveDirectories);
            Assert.IsTrue(fileUtil.AlternatePaths.ContainsKey(@"C:\Source"));
            CollectionAssert.AreEqual(new[] { @"C:\Alt1" }, fileUtil.AlternatePaths[@"C:\Source"]);

            var standards = SettingsManager.Get<StandardsSettings>(doc);
            Assert.AreEqual(@"C:\Standards\project-standards.json", standards.StandardsFilePath);

            var detailItem = SettingsManager.Get<DetailItemFactorySettings>(doc);
            Assert.AreEqual(@"C:\DetailItems\Output", detailItem.OutputPath);
            Assert.AreEqual("Detail Items", detailItem.TargetSubcategory);

            var shadowStudy = SettingsManager.Get<ShadowStudySettings>(doc);
            Assert.AreEqual(30, shadowStudy.DefaultTimeIncrementMinutes);
            Assert.AreEqual(9.5, shadowStudy.DefaultMinSunAltitudeDegrees, 1e-6);
            Assert.AreEqual(70, shadowStudy.DefaultMaxCompositeDarknessPercent);
            Assert.IsTrue(shadowStudy.DefaultPreserveVisualStyle);
            Assert.AreEqual(1, shadowStudy.UserPresets.Count);
            Assert.AreEqual("Firmwide Equinox", shadowStudy.UserPresets[0].Name);
            Assert.AreEqual(2, shadowStudy.UserPresets[0].Dates.Count);
            Assert.AreEqual(AstronomicalEventKind.SpringEquinox, shadowStudy.UserPresets[0].Dates[0].AstronomicalEvent);
            Assert.AreEqual(new DateTime(2026, 7, 4), shadowStudy.UserPresets[0].Dates[1].CustomDate);
        }

        [Test]
        public void PushAndPull_RoundTripsAllEightSyncedModules_AcrossDocuments()
        {
            string tempJsonPath = CreateTempJsonPath();

            try
            {
                WithRolledBackDocumentPair((sourceDoc, targetDoc) =>
                {
                    SaveDistinctSettingsToDocument(sourceDoc, tempJsonPath);
                    SettingsManager.PushToLinkedFile(sourceDoc, tempJsonPath);

                    Assert.IsTrue(File.Exists(tempJsonPath));
                    JObject pushedJson = JObject.Parse(File.ReadAllText(tempJsonPath));
                    Assert.IsNotNull(pushedJson[SyncSettings.Name]);
                    foreach (var descriptor in SettingsModuleRegistry.SyncedModules)
                    {
                        Assert.IsNotNull(pushedJson[descriptor.ModuleKey],
                            $"Pushed JSON must include module '{descriptor.ModuleKey}'.");
                    }

                    SettingsManager.PullFromLinkedFile(targetDoc, tempJsonPath);
                    SettingsManager.ClearCache(targetDoc);

                    AssertAllEightModulesMatchSeededValues(targetDoc);
                });
            }
            finally
            {
                if (File.Exists(tempJsonPath)) File.Delete(tempJsonPath);
            }
        }

        [Test]
        public void ExportAllToFile_And_ImportAllFromFile_RoundTripsAllEightModules()
        {
            string tempJsonPath = CreateTempJsonPath();

            try
            {
                WithRolledBackDocumentPair((sourceDoc, targetDoc) =>
                {
                    SaveDistinctSettingsToDocument(sourceDoc, tempJsonPath);
                    SettingsManager.ExportAllToFile(sourceDoc, tempJsonPath);

                    JObject exportedJson = JObject.Parse(File.ReadAllText(tempJsonPath));
                    Assert.IsNull(exportedJson[SyncSettings.Name], "ExportAllToFile must exclude SyncSettings.");
                    foreach (var descriptor in SettingsModuleRegistry.SyncedModules)
                    {
                        Assert.IsNotNull(exportedJson[descriptor.ModuleKey],
                            $"Exported JSON must include module '{descriptor.ModuleKey}'.");
                    }

                    SettingsManager.ImportAllFromFile(targetDoc, tempJsonPath);
                    SettingsManager.ClearCache(targetDoc);

                    AssertAllEightModulesMatchSeededValues(targetDoc);
                });
            }
            finally
            {
                if (File.Exists(tempJsonPath)) File.Delete(tempJsonPath);
            }
        }

        [Test]
        public void CheckSyncDrift_LegacyFourKeyJson_ReturnsFalse_AndModifiedReturnsTrue()
        {
            string tempJsonPath = CreateTempJsonPath();

            try
            {
                WithRolledBackDocument(doc =>
                {
                    SaveDistinctSettingsToDocument(doc, tempJsonPath);

                    var legacyFourKeys = new Dictionary<string, object>
                    {
                        { WorksetSettings.Name, SettingsManager.Get<WorksetSettings>(doc) },
                        { ViewAutoNumSettings.Name, SettingsManager.Get<ViewAutoNumSettings>(doc) },
                        { MaterialLibrarySettings.Name, SettingsManager.Get<MaterialLibrarySettings>(doc) },
                        { ProjectMaterialSettings.Name, SettingsManager.Get<ProjectMaterialSettings>(doc) }
                    };
                    File.WriteAllText(tempJsonPath, JsonConvert.SerializeObject(legacyFourKeys, Formatting.Indented));

                    bool legacyDrift = SettingsManager.CheckSyncDrift(doc, out string linkedPath);
                    Assert.IsFalse(legacyDrift, "Legacy 4-key JSON without newer module keys must not report false-positive drift.");
                    Assert.AreEqual(tempJsonPath, linkedPath);

                    var driftedJson = new Dictionary<string, object>(legacyFourKeys)
                    {
                        { StandardsSettings.Name, new StandardsSettings { StandardsFilePath = @"C:\Standards\different.json" } }
                    };
                    File.WriteAllText(tempJsonPath, JsonConvert.SerializeObject(driftedJson, Formatting.Indented));

                    bool modifiedDrift = SettingsManager.CheckSyncDrift(doc, out string driftedLinkedPath);
                    Assert.IsTrue(modifiedDrift, "Modified Standards value in linked JSON must report sync drift.");
                    Assert.AreEqual(tempJsonPath, driftedLinkedPath);
                });
            }
            finally
            {
                if (File.Exists(tempJsonPath)) File.Delete(tempJsonPath);
            }
        }

        [Test]
        public void SyncExternalEventHandler_PushAndPull_SyncsAllEightModulesWithoutUiPrompt()
        {
            string tempJsonPath = CreateTempJsonPath();

            try
            {
                WithRolledBackDocumentPair((sourceDoc, targetDoc) =>
                {
                    SaveDistinctSettingsToDocument(sourceDoc, tempJsonPath);

                    var handler = new SyncExternalEventHandler();
                    handler.QueueRequest(SyncRequestType.Push, sourceDoc, tempJsonPath);
                    handler.Execute(UiApp);

                    Assert.IsTrue(File.Exists(tempJsonPath));

                    handler.QueueRequest(SyncRequestType.Pull, targetDoc, tempJsonPath);
                    handler.Execute(UiApp);

                    SettingsManager.ClearCache(targetDoc);
                    AssertAllEightModulesMatchSeededValues(targetDoc);
                });
            }
            finally
            {
                if (File.Exists(tempJsonPath)) File.Delete(tempJsonPath);
            }
        }

        [Test]
        public void DetailItemFactorySettingsViewModel_Reload_RefreshesIsOverriddenFromDataStorage()
        {
            WithRolledBackDocument(doc =>
            {
                var vm = RevitDetailItemFactoryAdapter.CreateSettingsViewModel(doc, IntPtr.Zero);
                Assert.IsFalse(vm.IsOverridden);
                StringAssert.Contains("(Default Dynamic Document Path)", vm.SummaryText);

                SettingsManager.Save(doc, new DetailItemFactorySettings
                {
                    OutputPath = @"C:\DetailOut",
                    TargetSubcategory = "Detail Items"
                });

                vm.Reload();
                Assert.IsTrue(vm.IsOverridden, "Reload() must detect newly created DataStorage override.");
                Assert.IsFalse(vm.IsDirty);
                Assert.AreEqual(@"C:\DetailOut", vm.OutputPath);

                SettingsManager.Delete(doc, DetailItemFactorySettings.Name);

                vm.Reload();
                Assert.IsFalse(vm.IsOverridden, "Reload() must clear IsOverridden when DataStorage is deleted.");
                Assert.IsFalse(vm.IsDirty);
            });
        }

        [Test]
        public void DetailItemFactorySettingsViewModel_CreatedViaAdapter_RespectsSuppressFirmwideDiskFallback()
        {
            string? originalEnvRoot = Environment.GetEnvironmentVariable("SYNTHETIC_ADDIN_ROOT");
            string tempRoot = Path.Combine(
                Path.GetTempPath(),
                "SyntheticSettingsSyncNoDisk_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);

            try
            {
                var diskConfig = new Config(
                    DetailItemFactorySettings.Name,
                    new DetailItemFactorySettings
                    {
                        OutputPath = @"C:\Firmwide\DiskFallbackShouldBeSuppressed",
                        TargetSubcategory = "CustomDiskSubcategory"
                    });
                File.WriteAllText(
                    Path.Combine(tempRoot, Config.defaultSettingsFile),
                    JsonConvert.SerializeObject(diskConfig));

                Environment.SetEnvironmentVariable("SYNTHETIC_ADDIN_ROOT", tempRoot);

                WithRolledBackDocument(doc =>
                {
                    SettingsRuntimeOptions.SuppressFirmwideDiskFallback = true;
                    DetailItemFactorySettingsViewModel suppressedVm =
                        RevitDetailItemFactoryAdapter.CreateSettingsViewModel(doc, IntPtr.Zero);

                    Assert.IsFalse(suppressedVm.IsOverridden);
                    StringAssert.Contains("(Default Dynamic Document Path)", suppressedVm.SummaryText);
                    StringAssert.Contains("Detail Items", suppressedVm.SummaryText);
                    StringAssert.DoesNotContain(@"C:\Firmwide\DiskFallbackShouldBeSuppressed", suppressedVm.SummaryText);
                    StringAssert.DoesNotContain("CustomDiskSubcategory", suppressedVm.SummaryText);

                    SettingsRuntimeOptions.SuppressFirmwideDiskFallback = false;
                    DetailItemFactorySettingsViewModel unsuppressedVm =
                        RevitDetailItemFactoryAdapter.CreateSettingsViewModel(doc, IntPtr.Zero);

                    Assert.IsFalse(unsuppressedVm.IsOverridden);
                    StringAssert.Contains(@"C:\Firmwide\DiskFallbackShouldBeSuppressed", unsuppressedVm.SummaryText);
                    StringAssert.Contains("CustomDiskSubcategory", unsuppressedVm.SummaryText);
                });
            }
            finally
            {
                SettingsRuntimeOptions.SuppressFirmwideDiskFallback = true;
                Environment.SetEnvironmentVariable("SYNTHETIC_ADDIN_ROOT", originalEnvRoot);
                if (Directory.Exists(tempRoot))
                {
                    Directory.Delete(tempRoot, recursive: true);
                }
            }
        }

        [Test]
        public void SettingsDashboardViewModel_DetailItemFactoryPage_CancelIsolatesCache_AndSavePersistsOfflineUncPath()
        {
            WithRolledBackDocument(doc =>
            {
                SettingsManager.Save(doc, new DetailItemFactorySettings
                {
                    OutputPath = @"C:\Initial\DetailItems",
                    TargetSubcategory = "Detail Items"
                });

                var dashboardCancel = new SettingsDashboardViewModel(doc, IntPtr.Zero);
                DetailItemFactorySettingsViewModel? detailPage = dashboardCancel.SettingModules
                    .OfType<DetailItemFactorySettingsViewModel>()
                    .FirstOrDefault();

                Assert.IsNotNull(detailPage, "SettingsDashboardViewModel must register DetailItemFactorySettingsViewModel.");
                Assert.IsTrue(detailPage!.IsOverridden);
                CollectionAssert.Contains(detailPage.AvailableSubcategories, "Detail Items");

                detailPage.OutputPath = @"C:\Mutated\OnCancel";
                Assert.IsTrue(detailPage.IsDirty);
                Assert.IsTrue(detailPage.IsValid);
                Assert.IsTrue(dashboardCancel.SaveCommand.CanExecute(null));
                dashboardCancel.CancelCommand.Execute(null);

                DetailItemFactorySettings cachedAfterCancel = SettingsManager.Get<DetailItemFactorySettings>(doc);
                Assert.AreEqual(@"C:\Initial\DetailItems", cachedAfterCancel.OutputPath);

                var dashboardSave = new SettingsDashboardViewModel(doc, IntPtr.Zero);
                DetailItemFactorySettingsViewModel detailSavePage = dashboardSave.SettingModules
                    .OfType<DetailItemFactorySettingsViewModel>()
                    .Single();

                detailSavePage.OutputPath = @"\\offline-studio-server\bim\DetailItems";
                Assert.IsTrue(detailSavePage.IsValid, "Offline UNC paths must pass pure syntax validation.");
                Assert.IsTrue(dashboardSave.SaveCommand.CanExecute(null));
                dashboardSave.SaveCommand.Execute(null);

                SettingsManager.ClearCache(doc);
                DetailItemFactorySettings persisted = SettingsManager.Get<DetailItemFactorySettings>(doc);
                Assert.AreEqual(@"\\offline-studio-server\bim\DetailItems", persisted.OutputPath);
                Assert.AreEqual("Detail Items", persisted.TargetSubcategory);
            });
        }

        [Test]
        public void BeginSuppressSyncDriftNotifications_IsReentrantIdempotent_AndSuppressesCheckSyncDriftAndOnDocumentOpenToasts()
        {
            string tempJsonPath = CreateTempJsonPath();
            string tempRvtPath = Path.Combine(Path.GetTempPath(), $"SyntheticToastSuppress_{Guid.NewGuid():N}.rvt");

            try
            {
                Assert.IsFalse(SettingsRuntimeOptions.SuppressSyncDriftNotifications);
                Assert.AreEqual(0, SettingsRuntimeOptions.SyncDriftSuppressionDepth);

                WithRolledBackDocument(doc =>
                {
                    SaveDistinctSettingsToDocument(doc, tempJsonPath);
                    var driftedJson = new Dictionary<string, object>
                    {
                        { StandardsSettings.Name, new StandardsSettings { StandardsFilePath = @"C:\Standards\drifted.json" } }
                    };
                    File.WriteAllText(tempJsonPath, JsonConvert.SerializeObject(driftedJson, Formatting.Indented));

                    Assert.IsTrue(SettingsManager.CheckSyncDrift(doc, out string unsuppressedPath), "Control check should report sync drift.");
                    Assert.AreEqual(tempJsonPath, unsuppressedPath);

                    using (IDisposable outer = SettingsRuntimeOptions.BeginSuppressSyncDriftNotifications())
                    {
                        Assert.IsTrue(SettingsRuntimeOptions.SuppressSyncDriftNotifications);
                        Assert.AreEqual(1, SettingsRuntimeOptions.SyncDriftSuppressionDepth);
                        Assert.IsFalse(SettingsManager.CheckSyncDrift(doc, out string suppressedPath), "CheckSyncDrift must return false while suppressed.");
                        Assert.AreEqual(string.Empty, suppressedPath);

                        using (IDisposable inner = SettingsRuntimeOptions.BeginSuppressSyncDriftNotifications())
                        {
                            Assert.IsTrue(SettingsRuntimeOptions.SuppressSyncDriftNotifications);
                            Assert.AreEqual(2, SettingsRuntimeOptions.SyncDriftSuppressionDepth);

                            // Idempotent double-dispose must decrement only once
                            inner.Dispose();
                            Assert.AreEqual(1, SettingsRuntimeOptions.SyncDriftSuppressionDepth);
                            Assert.IsTrue(SettingsRuntimeOptions.SuppressSyncDriftNotifications);
                        }

                        Assert.AreEqual(1, SettingsRuntimeOptions.SyncDriftSuppressionDepth);
                        Assert.IsTrue(SettingsRuntimeOptions.SuppressSyncDriftNotifications);
                        Assert.IsFalse(SettingsManager.CheckSyncDrift(doc, out _));
                    }

                    Assert.AreEqual(0, SettingsRuntimeOptions.SyncDriftSuppressionDepth);
                    Assert.IsFalse(SettingsRuntimeOptions.SuppressSyncDriftNotifications);
                    Assert.IsTrue(SettingsManager.CheckSyncDrift(doc, out _), "CheckSyncDrift must resume once outermost scope is disposed.");
                });

                // Verify App.OnDocumentOpen spawns SyncToastNotification without suppression, and spawns 0 toasts while suppressed
                Document seedDoc = App.NewProjectDocument(UnitSystem.Imperial);
                try
                {
                    SaveDistinctSettingsToDocument(seedDoc, tempJsonPath);
                    seedDoc.SaveAs(tempRvtPath, new SaveAsOptions { OverwriteExistingFile = true });
                }
                finally
                {
                    if (seedDoc.IsValidObject)
                    {
                        seedDoc.Close(false);
                    }
                }

                var driftedFileJson = new Dictionary<string, object>
                {
                    { StandardsSettings.Name, new StandardsSettings { StandardsFilePath = @"C:\Standards\toast-drift.json" } }
                };
                File.WriteAllText(tempJsonPath, JsonConvert.SerializeObject(driftedFileJson, Formatting.Indented));

                var hostApp = new App();
                EventHandler<DocumentOpenedEventArgs> openHandler = hostApp.OnDocumentOpen;
                App.DocumentOpened += openHandler;

                try
                {
                    // 1. Suppressed open -> 0 SyncToastNotification windows
                    int toastsBeforeSuppressed = CountOpenSyncToasts();
                    Document? openedSuppressed = null;
                    try
                    {
                        using (SettingsRuntimeOptions.BeginSuppressSyncDriftNotifications())
                        {
                            openedSuppressed = App.OpenDocumentFile(tempRvtPath);
                        }
                        Assert.AreEqual(toastsBeforeSuppressed, CountOpenSyncToasts(), "Suppressed open must spawn 0 SyncToastNotification windows.");
                    }
                    finally
                    {
                        if (openedSuppressed != null && openedSuppressed.IsValidObject)
                        {
                            openedSuppressed.Close(false);
                        }
                    }

                    // 2. Unsuppressed open -> positive control verifies OnDocumentOpen would otherwise spawn a toast
                    int toastsBeforeControl = CountOpenSyncToasts();
                    Document? openedUnsuppressed = null;
                    try
                    {
                        openedUnsuppressed = App.OpenDocumentFile(tempRvtPath);
                        Assert.Greater(CountOpenSyncToasts(), toastsBeforeControl, "Positive control: unsuppressed drifted open must trigger SyncToastNotification.");
                    }
                    finally
                    {
                        CloseAllSyncToasts();
                        if (openedUnsuppressed != null && openedUnsuppressed.IsValidObject)
                        {
                            openedUnsuppressed.Close(false);
                        }
                    }
                }
                finally
                {
                    App.DocumentOpened -= openHandler;
                    CloseAllSyncToasts();
                }
            }
            finally
            {
                if (File.Exists(tempJsonPath)) File.Delete(tempJsonPath);
                if (File.Exists(tempRvtPath)) File.Delete(tempRvtPath);
            }
        }

        private static List<Synthetic.Features.SettingsDashboard.Views.SyncToastNotification> GetOpenSyncToasts()
        {
            var openToasts = new HashSet<Synthetic.Features.SettingsDashboard.Views.SyncToastNotification>();

            if (System.Windows.Application.Current != null)
            {
                foreach (var toast in System.Windows.Application.Current.Windows
                    .OfType<Synthetic.Features.SettingsDashboard.Views.SyncToastNotification>())
                {
                    openToasts.Add(toast);
                }
            }

            foreach (System.Windows.PresentationSource source in System.Windows.PresentationSource.CurrentSources)
            {
                if (!source.IsDisposed &&
                    source.RootVisual is Synthetic.Features.SettingsDashboard.Views.SyncToastNotification toast &&
                    toast.IsVisible)
                {
                    openToasts.Add(toast);
                }
            }

            return openToasts.ToList();
        }

        private static int CountOpenSyncToasts()
        {
            return GetOpenSyncToasts().Count;
        }

        private static void CloseAllSyncToasts()
        {
            foreach (var toast in GetOpenSyncToasts())
            {
                toast.Close();
            }
        }
    }
}
