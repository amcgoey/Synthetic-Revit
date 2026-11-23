using System;
using System.Collections.Generic;
using System.IO;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Synthetic.Features.MaterialManagement.Settings;
using Synthetic.Features.SettingsDashboard.Handlers;
using Synthetic.Features.StandardsManagement.Settings;
using Synthetic.Features.ViewManagement.Settings;
using Synthetic.Features.Worksets.Settings;
using Synthetic.Features.DetailItemFactory.Settings;
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
                var vm = new DetailItemFactorySettingsViewModel(doc, IntPtr.Zero);
                Assert.IsFalse(vm.IsOverridden);

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
    }
}
