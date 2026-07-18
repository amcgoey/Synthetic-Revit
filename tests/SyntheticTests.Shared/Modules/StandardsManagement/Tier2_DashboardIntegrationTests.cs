using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Autodesk.Revit.UI;
using Autodesk.Revit.DB;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.Shared.UI;
using Synthetic.Modules.RevitDOM;
using Synthetic.Settings;

namespace SyntheticTests
{
    [TestFixture]
    public class Tier2_DashboardIntegrationTests
    {
        private UIApplication? _uiapp;

        [OneTimeSetUp]
        public void Setup(UIApplication uiapp)
        {
            _uiapp = uiapp;
        }

        [Test]
        public void RunQueue_ExecutesDatabaseWritesAndFileSerialization_UnderTransactionGroup()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            string tempFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".json");
            var fakeFileDialog = new IntegrationFakeFileDialogService { PresetPath = tempFile };
            var fakeGuardrail = new IntegrationFakeGuardrailPromptService(GuardrailResult.Overwrite);

            var settings = new StandardsSettings { StandardsFilePath = tempFile };

            try
            {
                using (TransactionGroup txGroup = new TransactionGroup(doc, "Tier2_DashboardIntegrationTests"))
                {
                    txGroup.Start();

                    // Create a mock dashboard ViewModel using live Revit document
                    var vm = new ProjectStandardsDashboardViewModel(doc, fakeFileDialog, fakeGuardrail, settings);
                    vm.SaveFilePath = tempFile;

                    // Create standard POCO for a material to enforce in the live Revit DB
                    var materialModel = new MaterialModel
                    {
                        Name = "Synthetic_Test_Material_" + Guid.NewGuid().ToString().Substring(0, 8),
                        Class = "Autodesk.Revit.DB.Material"
                    };

                    // Setup parameters on the material POCO
                    materialModel.Parameters = new List<ParameterModel>
                    {
                        new ParameterModel
                        {
                            Name = "Description",
                            Value = "Synthetic Integration Test Material Description",
                            StorageType = "String",
                            Id = (int)BuiltInParameter.ALL_MODEL_DESCRIPTION
                        }
                    };

                    // Enqueue the item with SaveAndEnforce intent (Phase 1 + Phase 2)
                    var queueItem = new QueueItemModel(materialModel, true, true);
                    vm.StagingQueue.Add(queueItem);

                    // Execute
                    vm.RunQueueCommand.Execute(null);

                    // Assert execution result success
                    Assert.IsTrue(vm.LastExecutionResults.Count > 0, "LastExecutionResults should have at least 1 result.");
                    Assert.IsTrue(vm.LastExecutionResults[0].Success, $"Revit DB update failed: {vm.LastExecutionResults[0].ErrorMessage}\nException: {vm.LastExecutionResults[0].Exception}");

                    // Verify Revit DB modifications (Phase 1)
                    var createdMaterial = new FilteredElementCollector(doc)
                        .OfClass(typeof(Material))
                        .Cast<Material>()
                        .FirstOrDefault(m => m.Name == materialModel.Name);

                    Assert.IsNotNull(createdMaterial, "Enforce should successfully create the Material in the Revit Document.");
                    
                    string? liveDesc = createdMaterial!.LookupParameter("Description")?.AsString() ?? 
                                      createdMaterial.get_Parameter(BuiltInParameter.ALL_MODEL_DESCRIPTION)?.AsString();
                    
                    Assert.AreEqual("Synthetic Integration Test Material Description", liveDesc, "Material parameter should match enqueued POCO value.");

                    // Verify JSON serialization (Phase 2)
                    Assert.IsTrue(File.Exists(tempFile), "Save should successfully serialize enqueued item to JSON.");
                    string fileContent = File.ReadAllText(tempFile);
                    Assert.IsTrue(fileContent.Contains(materialModel.Name), "Serialized JSON should contain the enqueued material name.");

                    // Verify Markdown log generation
                    string expectedLogFile = Path.ChangeExtension(tempFile, ".log.md");
                    Assert.IsTrue(File.Exists(expectedLogFile), "Automatic Markdown log file should be created next to JSON file.");
                    string logContent = File.ReadAllText(expectedLogFile);
                    Assert.IsTrue(logContent.Contains("# Project Standards Consolidation Execution Report"), "Markdown log should contain the title.");
                    Assert.IsTrue(logContent.Contains("| Created |"), "Markdown log should contain the Created action.");
                    Assert.IsTrue(logContent.Contains(materialModel.Name), "Markdown log should contain the material name.");

                    // Rollback the TransactionGroup to ensure zero-leak test database execution
                    txGroup.RollBack();
                }
            }
            finally
            {
                if (File.Exists(tempFile))
                {
                    File.Delete(tempFile);
                }
                string expectedLogFile = Path.ChangeExtension(tempFile, ".log.md");
                if (File.Exists(expectedLogFile))
                {
                    File.Delete(expectedLogFile);
                }
            }
        }

        [Test]
        public void AddRevitModel_ExtractsFilteredElements_UnderTransactionGroup()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            var fakeFileDialog = new IntegrationFakeFileDialogService();
            var fakeGuardrail = new IntegrationFakeGuardrailPromptService(GuardrailResult.Overwrite);
            var settings = new StandardsSettings();

            try
            {
                using (TransactionGroup txGroup = new TransactionGroup(doc, "Tier2_ScanningTest"))
                {
                    txGroup.Start();

                    // Create test elements in the document to ensure they exist for scanning
                    Transaction t = new Transaction(doc, "Create Test Elements");
                    t.Start();

                    ElementId matId = Material.Create(doc, "Scanning_Test_Material");
                    
                    WallType defaultWallType = new FilteredElementCollector(doc)
                        .OfClass(typeof(WallType))
                        .Cast<WallType>()
                        .First();
                    WallType customWallType = (WallType)defaultWallType.Duplicate("Scanning_Test_WallType");

                    t.Commit();

                    // Create the Dashboard View Model
                    var vm = new ProjectStandardsDashboardViewModel(doc, fakeFileDialog, fakeGuardrail, settings);
                    vm.MockOpenDocuments = new List<Document> { doc };

                    // Setup dialog handler to only select "Materials & Assets" grouping
                    vm.ShowDocumentSelectionDialog = (dialogVM) =>
                    {
                        foreach (var item in dialogVM.OpenDocuments)
                        {
                            item.IsSelected = true;
                        }
                        dialogVM.ScanFamilies = false;
                        dialogVM.IncludeNestedFamilies = false;

                        // Check only "Materials & Assets" group
                        foreach (var group in dialogVM.FilterHierarchy)
                        {
                            if (group.Name == "Materials & Assets")
                            {
                                group.IsChecked = true;
                            }
                            else
                            {
                                group.IsChecked = false;
                            }
                        }
                        return true;
                    };

                    // Act
                    vm.AddRevitModelCommand.Execute(null);

                    // Assert
                    Assert.AreEqual(1, vm.AvailableSources.Count, "A Revit source should be added.");
                    var source = vm.AvailableSources[0];

                    bool foundMaterial = false;
                    bool foundWallType = false;

                    foreach (var group in source.SourceHierarchy)
                    {
                        foreach (var cls in group.Children)
                        {
                            foreach (var elem in cls.Children)
                            {
                                if (elem.Name == "Scanning_Test_Material")
                                {
                                    foundMaterial = true;
                                }
                                if (elem.Name == "Scanning_Test_WallType")
                                {
                                    foundWallType = true;
                                }
                            }
                        }
                    }

                    // Since only "Materials & Assets" was selected, the material should be extracted, but the wall type should be skipped.
                    Assert.IsTrue(foundMaterial, "The selected grouping 'Materials & Assets' should extract the custom material.");
                    Assert.IsFalse(foundWallType, "The excluded grouping 'System Types' should NOT extract the custom wall type.");

                    txGroup.RollBack();
                }
            }
            catch (Exception ex)
            {
                Assert.Fail($"Integration test failed with exception: {ex}");
            }
        }

        [Test]
        public void RunQueue_IncludesAliasMergeLogItems_InSummaryDialog()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            var fakeFileDialog = new IntegrationFakeFileDialogService();
            var fakeGuardrail = new IntegrationFakeGuardrailPromptService(GuardrailResult.Overwrite);
            var fakeSummaryService = new FakeSummaryDisplayService();

            try
            {
                using (TransactionGroup txGroup = new TransactionGroup(doc, "Tier2_DashboardAliasTest"))
                {
                    txGroup.Start();

                    // Create the alias material in a transaction
                    ElementId aliasMaterialId;
                    using (Transaction t = new Transaction(doc, "Create Alias Material"))
                    {
                        t.Start();
                        aliasMaterialId = Material.Create(doc, "AliasMaterial_DashboardTest");
                        t.Commit();
                    }

                    // Create the dashboard ViewModel
                    var vm = new ProjectStandardsDashboardViewModel(doc, fakeFileDialog, fakeGuardrail, null);
                    vm.SummaryDisplayService = fakeSummaryService;

                    // Enqueue the primary material with the alias
                    var primaryModel = new MaterialModel
                    {
                        Name = "PrimaryMaterial_DashboardTest",
                        Class = "Autodesk.Revit.DB.Material",
                        Aliases = new List<string> { "AliasMaterial_DashboardTest" }
                    };

                    var queueItem = new QueueItemModel(primaryModel, true, false);
                    vm.StagingQueue.Add(queueItem);

                    // Act
                    vm.RunQueueCommand.Execute(null);

                    // Assert
                    Assert.AreEqual(1, fakeSummaryService.ShowCallCount, "Summary dialog should be displayed.");
                    var summaryVM = fakeSummaryService.LastViewModel as ImportSummaryViewModel;
                    Assert.IsNotNull(summaryVM, "ViewModel should be ImportSummaryViewModel.");

                    // Check that the log items contain both the creation and the alias merge
                    var logItems = summaryVM!.LogItems.ToList();
                    Assert.IsTrue(logItems.Any(l => l.ElementName == "PrimaryMaterial_DashboardTest" && l.Action == "Created"), 
                        "Should contain a log item for the primary element creation.");
                    Assert.IsTrue(logItems.Any(l => l.ElementName == "PrimaryMaterial_DashboardTest" && l.Action == "Merged Alias" && l.Message.Contains("AliasMaterial_DashboardTest")), 
                        "Should contain a log item for the successful alias merge.");

                    txGroup.RollBack();
                }
            }
            finally
            {
                doc.Close(false);
            }
        }

        private class IntegrationFakeFileDialogService : IFileDialogService
        {
            public string? PresetPath { get; set; }

            public string? SaveFileDialog(string filter, string title, string defaultFileName)
            {
                return PresetPath;
            }

            public string? OpenFileDialog(string filter, string title, string defaultFileName)
            {
                return PresetPath;
            }
        }

        private class IntegrationFakeGuardrailPromptService : IGuardrailPromptService
        {
            private readonly GuardrailResult _result;

            public IntegrationFakeGuardrailPromptService(GuardrailResult result)
            {
                _result = result;
            }

            public GuardrailResult PromptProtectedFileOverwrite(string filePath)
            {
                return _result;
            }
        }

        private class FakeSummaryDisplayService : ISummaryDisplayService
        {
            public int ShowCallCount { get; set; }
            public object? LastViewModel { get; set; }

            public void ShowSummary(object viewModel, IntPtr parentWindowHandle)
            {
                ShowCallCount++;
                LastViewModel = viewModel;
            }
        }
    }
}
