using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Synthetic.Modules.RevitDOM;
using Synthetic.Settings;
using Synthetic.Infrastructure.Persistence;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.Modules.StandardsManagement.Utilities;
using Synthetic.Modules.MergeDuplicates.Models;
using Synthetic.Modules.StandardsManagement.Engine;
using SyntheticTests.Modules.RevitDOM;
using Synthetic.Shared.UI;

namespace SyntheticTests.Modules.StandardsManagement
{
    [TestFixture]
    public class ProjectStandardsDashboardTests
    {
        private Document _doc = null!;
        private FakeFileDialogService _fakeDialogService = null!;

        [SetUp]
        public void Setup()
        {
            _doc = (Document)Activator.CreateInstance(typeof(Document), true)!;
            _fakeDialogService = new FakeFileDialogService();
            ProgressCoordinator.SuppressUI = true;
        }

        [Test]
        public void VerifyCascadingCheckboxStates_CheckGroup_ChecksChildren()
        {
            // Arrange
            var group = new StandardGroupModel { Name = "Annotations" };
            var classModel = new StandardClassModel { Name = "Text Note Types" };
            var elementPOCO = new ElementModel { Name = "Arial 3/32" };
            var elementNode = new StandardElementModel(elementPOCO);

            group.Children.Add(classModel);
            classModel.Parent = group;

            classModel.Children.Add(elementNode);
            elementNode.Parent = classModel;

            Assert.IsFalse(group.IsChecked == true);
            Assert.IsFalse(classModel.IsChecked == true);
            Assert.IsFalse(elementNode.IsChecked == true);

            // Act
            group.IsChecked = true;

            // Assert
            Assert.IsTrue(group.IsChecked == true, "Group should be checked.");
            Assert.IsTrue(classModel.IsChecked == true, "Child Class should cascade check.");
            Assert.IsTrue(elementNode.IsChecked == true, "Leaf Element should cascade check.");
        }

        [Test]
        public void VerifyCascadingCheckboxStates_UncheckElement_IndeterminateParent()
        {
            // Arrange
            var group = new StandardGroupModel { Name = "Annotations" };
            var classModel = new StandardClassModel { Name = "Text Note Types" };
            var elemNodeA = new StandardElementModel(new ElementModel { Name = "Type A" });
            var elemNodeB = new StandardElementModel(new ElementModel { Name = "Type B" });

            group.Children.Add(classModel);
            classModel.Parent = group;

            classModel.Children.Add(elemNodeA);
            elemNodeA.Parent = classModel;
            classModel.Children.Add(elemNodeB);
            elemNodeB.Parent = classModel;

            group.IsChecked = true;
            Assert.IsTrue(group.IsChecked == true);

            // Act
            elemNodeA.IsChecked = false;

            // Assert
            Assert.IsNull(classModel.IsChecked, "Class should be indeterminate (null).");
            Assert.IsNull(group.IsChecked, "Group should be indeterminate (null).");
            Assert.IsTrue(elemNodeB.IsChecked == true, "Sibling node should remain checked.");
        }

        [Test]
        public void VerifyDashboardStartup_LoadsDefaultStandardsFromSettings()
        {
            // Arrange
            string tempFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".json");
            File.WriteAllText(tempFile, "{}"); // Empty JSON representing empty ModelsToSerialize

            var settings = new StandardsSettings { StandardsFilePath = tempFile };

            try
            {
                // Act
                var vm = DashboardTestFactory.Create(_doc, _fakeDialogService, settings: settings);

                // Assert
                Assert.AreEqual(1, vm.AvailableSources.Count, "Dashboard should load the default standards source on startup.");
                Assert.AreEqual("Default Firm Standard", vm.AvailableSources[0].DisplayName, "Tab display name should match default label.");
                Assert.AreEqual(tempFile, vm.AvailableSources[0].SourcePath, "Source path should match configuration file path.");
            }
            finally
            {
                if (File.Exists(tempFile))
                {
                    File.Delete(tempFile);
                }
            }
        }

        [Test]
        public void VerifyDashboardStartup_InitializesSmartDefaultSavePathEvenWithSettings()
        {
            // Arrange
            string tempFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".json");
            File.WriteAllText(tempFile, "{}");

            var settings = new StandardsSettings { StandardsFilePath = tempFile };
            
            _doc.GetType().GetProperty("Title")?.SetValue(_doc, "ProjectModel.rvt");
            _doc.GetType().GetProperty("PathName")?.SetValue(_doc, Path.Combine(Path.GetTempPath(), "ProjectModel.rvt"));

            try
            {
                // Act
                var vm = DashboardTestFactory.Create(_doc, _fakeDialogService, settings: settings);

                // Assert
                // 1. Should load the default firm standards source tab
                Assert.AreEqual(1, vm.AvailableSources.Count);
                Assert.AreEqual("Default Firm Standard", vm.AvailableSources[0].DisplayName);
                
                // 2. Should initialize SaveFilePath to the smart default path, NOT the settings path
                string expectedSmartPath = Path.Combine(Path.GetDirectoryName(_doc.PathName)!, "ProjectModel Standards.json");
                Assert.AreEqual(expectedSmartPath, vm.SaveFilePath);
            }
            finally
            {
                if (File.Exists(tempFile))
                {
                    File.Delete(tempFile);
                }
            }
        }

        [Test]
        public void VerifyDashboardStartup_FallsBackToRevitLocalSavePathIfDocumentIsNull()
        {
            // Arrange
            string tempFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".json");
            File.WriteAllText(tempFile, "{}");

            var settings = new StandardsSettings { StandardsFilePath = tempFile };

            try
            {
                // Act - passing null for doc
                var vm = DashboardTestFactory.Create((Document)null!, _fakeDialogService, settings: settings);

                // Assert
                string expectedFallbackDir = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                string expectedPath = Path.Combine(expectedFallbackDir, "Project Standards.json");
                Assert.AreEqual(expectedPath, vm.SaveFilePath);
            }
            finally
            {
                if (File.Exists(tempFile))
                {
                    File.Delete(tempFile);
                }
            }
        }

        [Test]
        public void VerifyAddFileSource_AppendsNewTab()
        {
            // Arrange
            string tempFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".json");
            File.WriteAllText(tempFile, "{}");

            _fakeDialogService.PresetPath = tempFile;
            var vm = DashboardTestFactory.Create(_doc, _fakeDialogService);
            int initialCount = vm.AvailableSources.Count;

            try
            {
                // Act
                vm.AddFileSourceCommand.Execute(null);

                // Assert
                Assert.AreEqual(initialCount + 1, vm.AvailableSources.Count, "New source tab should be appended.");
                Assert.AreEqual(Path.GetFileName(tempFile), vm.AvailableSources[initialCount].DisplayName, "Tab name should match file name.");
                Assert.AreEqual(tempFile, vm.AvailableSources[initialCount].SourcePath);
            }
            finally
            {
                if (File.Exists(tempFile))
                {
                    File.Delete(tempFile);
                }
            }
        }

        [Test]
        public void PushToQueue_CorrectlyStagesCheckedItemsWithIntent()
        {
            // Arrange
            var vm = DashboardTestFactory.Create(_doc, _fakeDialogService);
            
            // Create a fake source tab with some elements
            var source = new ProjectStandardsSourceViewModel { DisplayName = "Test Source" };
            var group = new StandardGroupModel { Name = "Materials & Assets" };
            var classModel = new StandardClassModel { Name = "Materials" };
            var elem1 = new ElementModel { Name = "Steel", Class = "Autodesk.Revit.DB.Material" };
            var elem2 = new ElementModel { Name = "Concrete", Class = "Autodesk.Revit.DB.Material" };
            
            var elemNode1 = new StandardElementModel(elem1);
            var elemNode2 = new StandardElementModel(elem2);
            
            classModel.Children.Add(elemNode1);
            classModel.Children.Add(elemNode2);
            elemNode1.Parent = classModel;
            elemNode2.Parent = classModel;
            
            group.Children.Add(classModel);
            classModel.Parent = group;
            
            source.SourceHierarchy.Add(group);
            vm.AvailableSources.Add(source);
            vm.SelectedSource = source;

            // Check elemNode1
            elemNode1.IsChecked = true;

            // Act
            vm.PushToQueueCommand.Execute("Save");

            // Assert
            Assert.AreEqual(1, vm.StagingQueue.Count, "One item should be staged in the Action Queue.");
            Assert.AreEqual("Steel", vm.StagingQueue[0].Name, "Staged item name should match the checked source element.");
            Assert.IsTrue(vm.StagingQueue[0].WillSave && !vm.StagingQueue[0].WillEnforce, "Staged item execution intent should match parameter.");
        }

        [Test]
        public void PushToQueue_StagesOfflineDependenciesFromJSON()
        {
            // Arrange
            var vm = DashboardTestFactory.Create(_doc, _fakeDialogService);

            // Create a fake JSON/offline source tab with a WallType and a Material
            var source = new ProjectStandardsSourceViewModel { DisplayName = "Test JSON Source", IsRevitSource = false };

            // WallType
            var wallGroup = new StandardGroupModel { Name = "Walls" };
            var wallClass = new StandardClassModel { Name = "Walls" };

            // Material dependency
            var materialId = new ElementIdModel { Name = "StagingTestMaterial", Class = "Autodesk.Revit.DB.Material" };

            // WallType inherits from HostObjTypeModel which has a Structure property containing layers
            var wallModel = new HostObjTypeModel
            {
                Name = "StagingTestWallType",
                Class = "Autodesk.Revit.DB.WallType",
                Structure = new CompoundStructureModel
                {
                    Layers = new List<SerialCompoundStructureLayer>
                    {
                        new SerialCompoundStructureLayer
                        {
                            MaterialId = materialId
                        }
                    }
                }
            };

            var wallNode = new StandardElementModel(wallModel);
            wallClass.Children.Add(wallNode);
            wallNode.Parent = wallClass;
            wallGroup.Children.Add(wallClass);
            wallClass.Parent = wallGroup;
            source.SourceHierarchy.Add(wallGroup);

            // Material
            var matGroup = new StandardGroupModel { Name = "Materials & Assets" };
            var matClass = new StandardClassModel { Name = "Materials" };
            var matModel = new MaterialModel
            {
                Name = "StagingTestMaterial",
                Class = "Autodesk.Revit.DB.Material"
            };
            var matNode = new StandardElementModel(matModel);
            matClass.Children.Add(matNode);
            matNode.Parent = matClass;
            matGroup.Children.Add(matClass);
            matClass.Parent = matGroup;
            source.SourceHierarchy.Add(matGroup);

            vm.AvailableSources.Add(source);
            vm.SelectedSource = source;

            // Check only the WallType
            wallNode.IsChecked = true;

            // Act
            vm.PushToQueueCommand.Execute("Enforce");

            // Assert
            Assert.AreEqual(2, vm.StagingQueue.Count, "Both WallType and Material should be staged in the Action Queue.");

            var wallQueueItem = vm.StagingQueue.FirstOrDefault(q => q.Name == "StagingTestWallType");
            var matQueueItem = vm.StagingQueue.FirstOrDefault(q => q.Name == "StagingTestMaterial");

            Assert.IsNotNull(wallQueueItem, "WallType should be staged.");
            Assert.IsNotNull(matQueueItem, "Material dependency should be harvested and staged.");

            Assert.IsNull(wallQueueItem!.DependencyOrigin, "WallType should have null DependencyOrigin because it was explicitly checked.");
            Assert.AreEqual("StagingTestWallType", matQueueItem!.DependencyOrigin, "Material's DependencyOrigin should match the WallType parent name.");
        }

        [Test]
        public void PushToQueue_StagesLiveDependenciesUsingPocoIdentityResolution()
        {
            // Arrange
            var vm = DashboardTestFactory.Create(_doc, _fakeDialogService);

            // Create a fake live Revit model source tab with a WallType and a Material, both having UniqueIds
            var source = new ProjectStandardsSourceViewModel { DisplayName = "Test Revit Model", IsRevitSource = true };

            // WallType
            var wallGroup = new StandardGroupModel { Name = "Walls" };
            var wallClass = new StandardClassModel { Name = "Walls" };

            // Material dependency
            var materialId = new ElementIdModel 
            { 
                Name = "Concrete-Live", 
                Class = "Autodesk.Revit.DB.Material", 
                UniqueId = "material-guid-live-1",
                Id = 8881,
                IsTemplate = false 
            };

            // WallType inherits from HostObjTypeModel
            var wallModel = new HostObjTypeModel
            {
                Name = "Wall-Live",
                Class = "Autodesk.Revit.DB.WallType",
                UniqueId = "wall-guid-live-1",
                Id = 9991,
                IsTemplate = false,
                Structure = new CompoundStructureModel
                {
                    Layers = new List<SerialCompoundStructureLayer>
                    {
                        new SerialCompoundStructureLayer
                        {
                            MaterialId = materialId
                        }
                    }
                }
            };

            var wallNode = new StandardElementModel(wallModel);
            wallClass.Children.Add(wallNode);
            wallNode.Parent = wallClass;
            wallGroup.Children.Add(wallClass);
            wallClass.Parent = wallGroup;
            source.SourceHierarchy.Add(wallGroup);

            // Material element in the source tab
            var matGroup = new StandardGroupModel { Name = "Materials & Assets" };
            var matClass = new StandardClassModel { Name = "Materials" };
            var matModel = new MaterialModel
            {
                Name = "Concrete-Live",
                Class = "Autodesk.Revit.DB.Material",
                UniqueId = "material-guid-live-1",
                Id = 8881,
                IsTemplate = false
            };
            var matNode = new StandardElementModel(matModel);
            matClass.Children.Add(matNode);
            matNode.Parent = matClass;
            matGroup.Children.Add(matClass);
            matClass.Parent = matGroup;
            source.SourceHierarchy.Add(matGroup);

            vm.AvailableSources.Add(source);
            vm.SelectedSource = source;

            // Check only the WallType
            wallNode.IsChecked = true;

            // Act
            vm.PushToQueueCommand.Execute("Enforce");

            // Assert
            Assert.AreEqual(2, vm.StagingQueue.Count, "Both WallType and Material should be staged in the Action Queue.");

            var wallQueueItem = vm.StagingQueue.FirstOrDefault(q => q.Name == "Wall-Live");
            var matQueueItem = vm.StagingQueue.FirstOrDefault(q => q.Name == "Concrete-Live");

            Assert.IsNotNull(wallQueueItem, "WallType should be staged.");
            Assert.IsNotNull(matQueueItem, "Material dependency should be resolved via PocoIdentityService and staged.");

            // Confirm that IsTemplate is false by default so UniqueId/Id are preserved on queue items
            Assert.IsFalse(((ElementModel)wallQueueItem!.Model).IsTemplate, "Queue item IsTemplate should be false.");
            Assert.AreEqual("wall-guid-live-1", ((ElementModel)wallQueueItem.Model).UniqueId, "UniqueId should be preserved.");
            Assert.AreEqual("material-guid-live-1", ((ElementModel)matQueueItem!.Model).UniqueId, "Material UniqueId should be preserved.");
            Assert.AreEqual("Wall-Live", matQueueItem!.DependencyOrigin, "Material's DependencyOrigin should match parent.");
        }

        [Test]
        public void PushToQueue_EnforcesDeepCopyIsolation()
        {
            // Arrange
            var vm = DashboardTestFactory.Create(_doc, _fakeDialogService);
            
            var source = new ProjectStandardsSourceViewModel { DisplayName = "Test Source" };
            var group = new StandardGroupModel { Name = "Materials & Assets" };
            var classModel = new StandardClassModel { Name = "Materials" };
            var elem = new ElementModel { Name = "Steel", Class = "Autodesk.Revit.DB.Material" };
            var elemNode = new StandardElementModel(elem);
            
            classModel.Children.Add(elemNode);
            elemNode.Parent = classModel;
            group.Children.Add(classModel);
            classModel.Parent = group;
            source.SourceHierarchy.Add(group);
            vm.AvailableSources.Add(source);
            vm.SelectedSource = source;

            elemNode.IsChecked = true;

            // Act
            vm.PushToQueueCommand.Execute("Enforce");
            
            // Modify name on the staged queue item's model
            var stagedItem = vm.StagingQueue[0];
            if (stagedItem.Model is ElementModel stagedElem)
            {
                stagedElem.Name = "Modified Steel";
            }

            // Assert
            Assert.AreEqual("Steel", elem.Name, "Original source element POCO should remain unchanged (DeepClone isolation).");
        }

        [Test]
        public void RemoveFromQueue_PurgesTargetedStagedItems()
        {
            // Arrange
            var vm = DashboardTestFactory.Create(_doc, _fakeDialogService);
            
            var source = new ProjectStandardsSourceViewModel { DisplayName = "Test Source" };
            var group = new StandardGroupModel { Name = "Materials & Assets" };
            var classModel = new StandardClassModel { Name = "Materials" };
            var elem1 = new ElementModel { Name = "Steel", Class = "Autodesk.Revit.DB.Material" };
            var elem2 = new ElementModel { Name = "Concrete", Class = "Autodesk.Revit.DB.Material" };
            
            var elemNode1 = new StandardElementModel(elem1);
            var elemNode2 = new StandardElementModel(elem2);
            
            classModel.Children.Add(elemNode1);
            classModel.Children.Add(elemNode2);
            elemNode1.Parent = classModel;
            elemNode2.Parent = classModel;
            group.Children.Add(classModel);
            classModel.Parent = group;
            source.SourceHierarchy.Add(group);
            vm.AvailableSources.Add(source);
            vm.SelectedSource = source;

            elemNode1.IsChecked = true;
            elemNode2.IsChecked = true;

            // Push both to queue
            vm.PushToQueueCommand.Execute("SaveAndEnforce");
            Assert.AreEqual(2, vm.StagingQueue.Count);

            // Act - remove item 1
            var listToRemove = new System.Collections.ArrayList { vm.StagingQueue[0] };
            vm.RemoveFromQueueCommand.Execute(listToRemove);

            // Assert
            Assert.AreEqual(1, vm.StagingQueue.Count, "Action queue should contain exactly 1 element after removal.");
            Assert.AreEqual("Concrete", vm.StagingQueue[0].Name, "Remaining staged element should be Concrete.");
        }

        [Test]
        public void EditCommand_SetsActiveWorkspaceToEdit()
        {
            // Arrange
            var vm = DashboardTestFactory.Create(_doc, _fakeDialogService);
            var source = new ProjectStandardsSourceViewModel { DisplayName = "Test Source" };
            var group = new StandardGroupModel { Name = "Materials & Assets" };
            var classModel = new StandardClassModel { Name = "Materials" };
            var elem = new ElementModel { Name = "Steel", Class = "Autodesk.Revit.DB.Material" };
            var elemNode = new StandardElementModel(elem);
            classModel.Children.Add(elemNode);
            group.Children.Add(classModel);
            source.SourceHierarchy.Add(group);
            vm.AvailableSources.Add(source);
            vm.SelectedSource = source;

            elemNode.IsChecked = true;
            vm.PushToQueueCommand.Execute("Enforce");

            var item = vm.StagingQueue[0];
            var listToEdit = new System.Collections.ArrayList { item };

            // Act
            vm.EditCommand.Execute(listToEdit);

            // Assert
            Assert.AreEqual(WorkspaceMode.Edit, vm.ActiveWorkspace, "ActiveWorkspace mode should transition to Edit.");
            Assert.AreEqual(1, vm.SelectedQueueItems.Count, "SelectedQueueItems should contain 1 staged item.");
            Assert.AreSame(item, vm.SelectedQueueItems[0], "The staged item in SelectedQueueItems should match the selection.");
        }

        [Test]
        public void BatchFindReplace_MutatesSelectedQueueElementNamesAndParameters()
        {
            // Arrange
            var vm = DashboardTestFactory.Create(_doc, _fakeDialogService);
            var source = new ProjectStandardsSourceViewModel { DisplayName = "Test Source" };
            var group = new StandardGroupModel { Name = "Materials & Assets" };
            var classModel = new StandardClassModel { Name = "Materials" };
            
            var elem = new ElementModel 
            { 
                Name = "Steel Column", 
                Class = "Autodesk.Revit.DB.Material",
                Parameters = new List<ParameterModel>
                {
                    new ParameterModel { Name = "Description", Value = "Steel material for columns", IsReadOnly = false }
                }
            };
            var elemNode = new StandardElementModel(elem);
            classModel.Children.Add(elemNode);
            group.Children.Add(classModel);
            source.SourceHierarchy.Add(group);
            vm.AvailableSources.Add(source);
            vm.SelectedSource = source;

            elemNode.IsChecked = true;
            vm.PushToQueueCommand.Execute("Enforce");

            var item = vm.StagingQueue[0];
            var listToEdit = new System.Collections.ArrayList { item };
            vm.EditCommand.Execute(listToEdit);

            vm.FindText = "Steel";
            vm.ReplaceText = "Iron";

            // Act
            vm.BatchFindReplaceCommand.Execute(null);

            // Assert
            var editedModel = (ElementModel)item.Model;
            Assert.AreEqual("Iron Column", editedModel.Name, "Element Name should undergo find-and-replace.");
            Assert.AreEqual("Iron material for columns", editedModel.Parameters[0].Value, "Writable Parameter Value should undergo find-and-replace.");
        }

        [Test]
        public void ApplyEdits_SetsIntentToEditedAndResetsWorkspace()
        {
            // Arrange
            var vm = DashboardTestFactory.Create(_doc, _fakeDialogService);
            var source = new ProjectStandardsSourceViewModel { DisplayName = "Test Source" };
            var group = new StandardGroupModel { Name = "Materials & Assets" };
            var classModel = new StandardClassModel { Name = "Materials" };
            var elem = new ElementModel { Name = "Steel", Class = "Autodesk.Revit.DB.Material" };
            var elemNode = new StandardElementModel(elem);
            classModel.Children.Add(elemNode);
            group.Children.Add(classModel);
            source.SourceHierarchy.Add(group);
            vm.AvailableSources.Add(source);
            vm.SelectedSource = source;

            elemNode.IsChecked = true;
            vm.PushToQueueCommand.Execute("Enforce");

            var item = vm.StagingQueue[0];
            var listToEdit = new System.Collections.ArrayList { item };
            vm.EditCommand.Execute(listToEdit);

            // Act
            vm.ApplyEditsCommand.Execute(null);

            // Assert
            Assert.IsTrue(item.IsEdited, "Staged item's IsEdited should be true.");
            Assert.AreEqual(WorkspaceMode.Idle, vm.ActiveWorkspace, "ActiveWorkspace mode should transition back to Idle.");
        }

        [Test]
        public void DiffCommand_InvokesScanAndTransitionsToDiff()
        {
            // Arrange
            var vm = DashboardTestFactory.Create(_doc, _fakeDialogService);
            var source = new ProjectStandardsSourceViewModel { DisplayName = "Test Source" };
            var group = new StandardGroupModel { Name = "Materials & Assets" };
            var classModel = new StandardClassModel { Name = "Materials" };
            var elem = new ElementModel { Name = "Steel", Class = "Autodesk.Revit.DB.Material" };
            var elemNode = new StandardElementModel(elem);
            classModel.Children.Add(elemNode);
            group.Children.Add(classModel);
            source.SourceHierarchy.Add(group);
            vm.AvailableSources.Add(source);
            vm.SelectedSource = source;

            elemNode.IsChecked = true;
            vm.PushToQueueCommand.Execute("Enforce");

            var item = vm.StagingQueue[0];
            var listToDiff = new System.Collections.ArrayList { item };

            // Act
            vm.DiffCommand.Execute(listToDiff);

            // Assert
            Assert.AreEqual(WorkspaceMode.Diff, vm.ActiveWorkspace, "ActiveWorkspace mode should transition to Diff.");
            Assert.AreEqual(1, vm.SelectedQueueItems.Count, "SelectedQueueItems should contain the item.");
        }

        [Test]
        public void ResolveConflict_MutatesStagedPOCOWithWinningValuesAndSetsDiffedIntent()
        {
            // Arrange
            var vm = DashboardTestFactory.Create(_doc, _fakeDialogService);
            
            var elem = new ElementModel 
            { 
                Name = "Steel", 
                Class = "Autodesk.Revit.DB.Material",
                Parameters = new List<ParameterModel>
                {
                    new ParameterModel { Name = "Description", Value = "Staged Standard Value", IsReadOnly = false }
                }
            };
            var item = new QueueItemModel(elem, true, false);
            vm.SelectedQueueItems.Add(item);

            // Create a fake cluster mapping to simulate a diff match
            var cluster = new DuplicateClusterModel { ClusterName = "Materials: Standards Comparison" };
            var targetType = new DuplicateTypeModel { Name = "Steel" };
            var mapping = new TypeMappingModel
            {
                TargetType = targetType
            };

            var sourceId = new ElementId(101);
            var targetId = new ElementId(102);

            var diffRow = new ParameterDiffRowModel
            {
                ParameterName = "Description",
                Options = new List<ParameterValueOption>
                {
                    // Option[0] is Local Document value
                    new ParameterValueOption { ElementId = sourceId, DisplayText = "Local Document Value" },
                    // Option[1] is Staged value
                    new ParameterValueOption { ElementId = targetId, DisplayText = "Staged Standard Value" }
                }
            };
            // Set local document value as the winner
            diffRow.IsSourceWinning = true;

            mapping.ParameterResolutions.Add(diffRow);
            cluster.TypeMappings.Add(mapping);
            vm.ActiveDiffClusters.Add(cluster);

            // Act
            vm.ResolveConflictCommand.Execute(null);

            // Assert
            Assert.AreEqual("Local Document Value", ((ElementModel)item.Model).Parameters[0].Value, "Staged POCO parameter value should be mutated to the winning local value.");
            Assert.IsTrue(item.IsDiffed, "IsDiffed should be true.");
            Assert.AreEqual(WorkspaceMode.Idle, vm.ActiveWorkspace, "ActiveWorkspace should return to Idle.");
            Assert.AreEqual(0, vm.ActiveDiffClusters.Count, "ActiveDiffClusters should be cleared.");
        }

        [Test]
        public void RunQueue_BypassesPhase1ForSaveOnlyItems()
        {
            // Arrange
            var fakeFileDialog = new FakeFileDialogService();
            string tempFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".json");
            fakeFileDialog.PresetPath = tempFile;

            var vm = DashboardTestFactory.Create(_doc, fakeFileDialog);
            vm.SaveFilePath = tempFile;
            var elem = new ElementModel { Name = "Steel", Class = "Autodesk.Revit.DB.Material" };
            
            // Item is tagged Save only (should completely bypass Phase 1)
            var item = new QueueItemModel(elem, false, true);
            vm.StagingQueue.Add(item);

            try
            {
                // Act
                vm.RunQueueCommand.Execute(null);

                // Assert
                Assert.AreEqual(0, vm.StagingQueue.Count, "Queue should be cleared.");
                Assert.IsTrue(File.Exists(tempFile), "Save should write to the JSON file.");
            }
            finally
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
            }
        }

        [Test]
        public void RunQueue_TriggersGuardrailForProtectedFiles()
        {
            // Arrange
            string tempFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".json");
            File.WriteAllText(tempFile, "{}");

            var settings = new StandardsSettings { StandardsFilePath = tempFile };
            try
            {
                SettingsManager.Save(_doc, settings);
            }
            catch { }

            var fakeGuardrail = new FakeGuardrailPromptService(GuardrailResult.Skip);
            var fakeFileDialog = new FakeFileDialogService();
            var vm = DashboardTestFactory.Create(_doc, fakeFileDialog, new StandardsExportService(fakeGuardrail, fakeFileDialog), settings);
            vm.SaveFilePath = tempFile;

            var elem = new ElementModel { Name = "Steel", Class = "Autodesk.Revit.DB.Material" };
            var item = new QueueItemModel(elem, false, true);
            vm.StagingQueue.Add(item);

            try
            {
                // Act
                vm.RunQueueCommand.Execute(null);

                // Assert
                Assert.AreEqual(1, fakeGuardrail.PromptedPaths.Count, "Guardrail prompt should be invoked.");
                Assert.AreEqual(Path.GetFullPath(tempFile), Path.GetFullPath(fakeGuardrail.PromptedPaths[0]), "Prompted path should match the protected master file.");
            }
            finally
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
            }
        }

        [Test]
        public void RunQueue_RedirectsOnSaveAsSelection()
        {
            // Arrange
            string protectedFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + "_protected.json");
            string redirectedFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + "_redirected.json");
            File.WriteAllText(protectedFile, "{}");

            var settings = new StandardsSettings { StandardsFilePath = protectedFile };
            try
            {
                SettingsManager.Save(_doc, settings);
            }
            catch { }

            var fakeGuardrail = new FakeGuardrailPromptService(GuardrailResult.SaveAs);
            var fakeFileDialog = new FakeFileDialogService();
            fakeFileDialog.PresetPath = redirectedFile;

            var vm = DashboardTestFactory.Create(_doc, fakeFileDialog, new StandardsExportService(fakeGuardrail, fakeFileDialog), settings);
            vm.SaveFilePath = protectedFile;
            var elem = new ElementModel { Name = "Steel", Class = "Autodesk.Revit.DB.Material" };
            var item = new QueueItemModel(elem, false, true);
            vm.StagingQueue.Add(item);

            try
            {
                // Act
                vm.RunQueueCommand.Execute(null);

                // Assert
                Assert.AreEqual(1, fakeGuardrail.PromptedPaths.Count, "Guardrail prompt should be invoked.");
                Assert.IsTrue(File.Exists(redirectedFile), "File should be saved to the redirected path.");
                Assert.IsFalse(File.Exists(protectedFile) && File.ReadAllText(protectedFile) != "{}", "Protected file should not be overwritten.");
            }
            finally
            {
                if (File.Exists(protectedFile)) File.Delete(protectedFile);
                if (File.Exists(redirectedFile)) File.Delete(redirectedFile);
            }
        }

        [Test]
        public void RunQueue_AbortsOnSkipSelection()
        {
            // Arrange
            string protectedFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + "_protected.json");
            File.WriteAllText(protectedFile, "{}");

            var settings = new StandardsSettings { StandardsFilePath = protectedFile };
            try
            {
                SettingsManager.Save(_doc, settings);
            }
            catch { }

            var fakeGuardrail = new FakeGuardrailPromptService(GuardrailResult.Skip);
            var fakeFileDialog = new FakeFileDialogService();

            var vm = DashboardTestFactory.Create(_doc, fakeFileDialog, new StandardsExportService(fakeGuardrail, fakeFileDialog), settings);
            vm.SaveFilePath = protectedFile;
            var elem = new ElementModel { Name = "Steel", Class = "Autodesk.Revit.DB.Material" };
            var item = new QueueItemModel(elem, false, true);
            vm.StagingQueue.Add(item);

            try
            {
                // Act
                vm.RunQueueCommand.Execute(null);

                // Assert
                Assert.AreEqual(1, fakeGuardrail.PromptedPaths.Count, "Guardrail prompt should be invoked.");
                Assert.AreEqual("{}", File.ReadAllText(protectedFile), "Protected file content should remain untouched.");
            }
            finally
            {
                if (File.Exists(protectedFile)) File.Delete(protectedFile);
            }
        }

        [Test]
        public void RunQueue_InvokesSummaryDisplayService_UponExecution()
        {
            // Arrange
            var fakeFileDialog = new FakeFileDialogService();
            string tempFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + "_test.json");
            fakeFileDialog.PresetPath = tempFile;

            var fakeSummaryService = new FakeSummaryDisplayService();
            var vm = DashboardTestFactory.Create(_doc, fakeFileDialog, null, null);
            vm.SummaryDisplayService = fakeSummaryService;

            var elem = new ElementModel { Name = "Steel", Class = "Autodesk.Revit.DB.Material" };
            var item = new QueueItemModel(elem, false, true);
            vm.StagingQueue.Add(item);

            try
            {
                // Act
                vm.RunQueueCommand.Execute(null);

                // Assert
                Assert.AreEqual(1, fakeSummaryService.ShowCallCount, "Summary display service should be invoked exactly once.");
                Assert.IsNotNull(fakeSummaryService.LastViewModel, "Passed view model should not be null.");
                Assert.IsInstanceOf<ImportSummaryViewModel>(fakeSummaryService.LastViewModel, "Passed view model should be ImportSummaryViewModel.");

                var summaryVM = (ImportSummaryViewModel)fakeSummaryService.LastViewModel;
                Assert.AreEqual(1, summaryVM.LogItems.Count, "There should be exactly 1 log item in the summary.");
                Assert.AreEqual("Steel", summaryVM.LogItems[0].ElementName, "Log item name should match the enqueued element.");
            }
            finally
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
                string logFile = Path.ChangeExtension(tempFile, ".log.md");
                if (File.Exists(logFile)) File.Delete(logFile);
            }
        }

        [Test]
        public void RunQueue_WritesAutomaticMarkdownLog_NextToTargetPath()
        {
            // Arrange
            var fakeFileDialog = new FakeFileDialogService();
            string tempFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + "_test.json");
            string expectedLogFile = Path.ChangeExtension(tempFile, ".log.md");
            fakeFileDialog.PresetPath = tempFile;

            var fakeSummaryService = new FakeSummaryDisplayService();
            var vm = DashboardTestFactory.Create(_doc, fakeFileDialog, null, null);
            vm.SaveFilePath = tempFile;
            vm.SummaryDisplayService = fakeSummaryService;

            var elem = new ElementModel { Name = "Steel", Class = "Autodesk.Revit.DB.Material" };
            var item = new QueueItemModel(elem, false, true);
            vm.StagingQueue.Add(item);

            try
            {
                // Act
                vm.RunQueueCommand.Execute(null);

                // Assert
                Assert.IsTrue(File.Exists(tempFile), "Target JSON file should be created.");
                Assert.IsTrue(File.Exists(expectedLogFile), "Automatic Markdown log file should be created next to JSON file.");

                string logContent = File.ReadAllText(expectedLogFile);
                Assert.IsTrue(logContent.Contains("# Project Standards Consolidation Execution Report"), "Log should contain title header.");
                Assert.IsTrue(logContent.Contains("| Metric | Count |"), "Log should contain summary table.");
                Assert.IsTrue(logContent.Contains("| Steel |"), "Log should contain the elements table row.");
            }
            finally
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
                if (File.Exists(expectedLogFile)) File.Delete(expectedLogFile);
            }
        }

        [Test]
        public void BuildHierarchy_ShouldExpandGroupsAndCollapseClassesByDefault()
        {
            // Arrange
            var elements = new List<ElementModel>
            {
                new MaterialModel { Class = "Autodesk.Revit.DB.Material", Name = "Material - Oak", Category = "Materials" },
                new MaterialModel { Class = "Autodesk.Revit.DB.Material", Name = "Material - Pine", Category = "Materials" }
            };

            // Act
            var hierarchy = StandardsHierarchyUtility.BuildHierarchy(elements);

            // Assert
            Assert.AreEqual(1, hierarchy.Count, "Should contain 1 group.");
            var group = hierarchy[0];
            Assert.AreEqual("Materials & Assets", group.Name);
            Assert.IsTrue(group.IsExpanded, "Top level group should be expanded by default.");

            Assert.AreEqual(1, group.Children.Count, "Group should contain 1 class.");
            var classModel = group.Children[0] as StandardClassModel;
            Assert.IsNotNull(classModel);
            Assert.AreEqual("Materials", classModel.Name);
            Assert.IsFalse(classModel.IsExpanded, "Class should be collapsed by default.");
        }

        [Test]
        public void AddRevitModel_ExtractsNestedDependencies_BasedOnOrchestration()
        {
            // Arrange
            var mainDoc = (Document)Activator.CreateInstance(typeof(Document), true)!;
            dynamic mainDocDyn = mainDoc;

            // Create a mock WallType
            var wallType = (WallType)Activator.CreateInstance(typeof(WallType), true)!;
            wallType.Name = "Orchestration_Test_WallType";
            typeof(Element).GetProperty("Id")?.SetValue(wallType, new ElementId(1001));
            mainDocDyn.AddElement(wallType, new ElementId(1001));

            // Create a mock Material
            var material = (Material)Activator.CreateInstance(typeof(Material), true)!;
            material.Name = "Orchestration_Test_Material";
            typeof(Element).GetProperty("Id")?.SetValue(material, new ElementId(2001));
            mainDocDyn.AddElement(material, new ElementId(2001));

            // Set up mock compound structure on WallType referencing the Material
            var layer = new CompoundStructureLayer();
            layer.MaterialId = new ElementId(2001);
            var cs = CompoundStructure.CreateSimpleCompoundStructure(new List<CompoundStructureLayer> { layer });
            wallType.SetCompoundStructure(cs);

            // Setup a fake identity service and orchestrator
            var fakeIdentity = new FakeIdentityService();
            
            // Map the material ID to model mapping
            var matModel = new ElementIdModel
            {
                Id = 2001,
                Name = "Orchestration_Test_Material",
                Class = "Autodesk.Revit.DB.Material"
            };
            fakeIdentity.SetupMapping(new ElementId(2001), matModel);
            fakeIdentity.SetupElement("Orchestration_Test_Material", material);

            var orchestrator = new StandardsExtractionOrchestrator(fakeIdentity);

            var fakeFileDialog = new FakeFileDialogService();
            var fakeGuardrail = new FakeGuardrailPromptService(GuardrailResult.Overwrite);
            var settings = new StandardsSettings();

            var vm = DashboardTestFactory.Create(
                mainDoc, 
                dialogService: fakeFileDialog, 
                exportService: new StandardsExportService(fakeGuardrail, fakeFileDialog), 
                settings: settings, 
                orchestrator: orchestrator);
            vm.MockOpenDocuments = new List<Document> { mainDoc };

            // Setup document selection mock: select "Wall Types" only
            vm.ShowDocumentSelectionDialog = (dialogVM) =>
            {
                foreach (var item in dialogVM.OpenDocuments)
                {
                    item.IsSelected = true;
                }
                dialogVM.ScanFamilies = false;
                dialogVM.IncludeNestedFamilies = false;

                foreach (var group in dialogVM.FilterHierarchy)
                {
                    if (group.Name == "System Types")
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

            bool foundWallType = false;
            bool foundMaterial = false;

            foreach (var group in source.SourceHierarchy)
            {
                foreach (var cls in group.Children)
                {
                    foreach (var elem in cls.Children)
                    {
                        if (elem.Name == "Orchestration_Test_WallType") foundWallType = true;
                        if (elem.Name == "Orchestration_Test_Material") foundMaterial = true;
                    }
                }
            }

            Assert.IsTrue(foundWallType, "The root WallType should be extracted.");
            Assert.IsTrue(foundMaterial, "The nested Material should be recursively extracted as a dependency via the orchestrator.");
        }

        [Test]
        public void SelectedElement_UpdateName_PropagatesToQueueItemAndTriggersRenameCascading()
        {
            // Arrange
            var mainDoc = (Document)Activator.CreateInstance(typeof(Document), true)!;
            var fakeFileDialog = new FakeFileDialogService();
            var fakeGuardrail = new FakeGuardrailPromptService();
            var settings = new StandardsSettings();
            
            var vm = DashboardTestFactory.Create(mainDoc, fakeFileDialog, new StandardsExportService(fakeGuardrail, fakeFileDialog), settings);
            
            // Add a test queue item
            var model = new ElementModel { Name = "OriginalName", Class = "Autodesk.Revit.DB.LinePatternElement" };
            model.Parameters = new List<ParameterModel>
            {
                new ParameterModel("Param1", "Val1", null, "String", 0, null, false, false)
            };
            
            var queueItem = new QueueItemModel(model, false, true);
            vm.StagingQueue.Add(queueItem);
            
            // Execute Edit
            vm.EditCommand.Execute(new List<QueueItemModel> { queueItem });
            
            // Assert SelectedElement is set
            Assert.IsNotNull(vm.SelectedElement, "SelectedElement should not be null.");
            Assert.AreEqual("OriginalName", vm.SelectedElement.Name);
            
            // Act: Update Name on SelectedElement wrapper
            vm.SelectedElement.Name = "NewName";
            
            // Assert Name updates on queue item and model
            Assert.AreEqual("NewName", queueItem.Name, "Queue item name should update.");
            Assert.AreEqual("NewName", ((ElementModel)queueItem.Model).Name, "Model name should update.");
            Assert.IsTrue(queueItem.IsEdited, "IsEdited should be true.");
        }

        [Test]
        public void IsSingleElementSelected_TogglesCorrectly_BasedOnSelectionCount()
        {
            // Arrange
            var mainDoc = (Document)Activator.CreateInstance(typeof(Document), true)!;
            var fakeFileDialog = new FakeFileDialogService();
            var fakeGuardrail = new FakeGuardrailPromptService();
            var settings = new StandardsSettings();
            
            var vm = DashboardTestFactory.Create(mainDoc, fakeFileDialog, new StandardsExportService(fakeGuardrail, fakeFileDialog), settings);
            
            var item1 = new QueueItemModel(new ElementModel { Name = "Elem1", Class = "Autodesk.Revit.DB.LinePatternElement" }, false, true);
            var item2 = new QueueItemModel(new ElementModel { Name = "Elem2", Class = "Autodesk.Revit.DB.LinePatternElement" }, false, true);
            
            vm.StagingQueue.Add(item1);
            vm.StagingQueue.Add(item2);
            
            // Edit single item
            vm.EditCommand.Execute(new List<QueueItemModel> { item1 });
            Assert.IsTrue(vm.IsSingleElementSelected);
            Assert.AreEqual("Elem1", vm.SelectedNameOrCount);
            
            // Edit multiple items
            vm.EditCommand.Execute(new List<QueueItemModel> { item1, item2 });
            Assert.IsFalse(vm.IsSingleElementSelected);
            Assert.AreEqual("Editing 2 elements", vm.SelectedNameOrCount);
        }

        [Test]
        public void SelectedDisplayClass_StripsNamespacePrefix_AndConcatenatesDistinctValues()
        {
            // Arrange
            var mainDoc = (Document)Activator.CreateInstance(typeof(Document), true)!;
            var fakeFileDialog = new FakeFileDialogService();
            var fakeGuardrail = new FakeGuardrailPromptService();
            var settings = new StandardsSettings();
            
            var vm = DashboardTestFactory.Create(mainDoc, fakeFileDialog, new StandardsExportService(fakeGuardrail, fakeFileDialog), settings);
            
            var item1 = new QueueItemModel(new ElementModel { Name = "Elem1", Class = "Autodesk.Revit.DB.LinePatternElement" }, false, true);
            var item2 = new QueueItemModel(new ElementModel { Name = "Elem2", Class = "Autodesk.Revit.DB.TextNoteType" }, false, true);
            var item3 = new QueueItemModel(new ElementModel { Name = "Elem3", Class = "Autodesk.Revit.DB.LinePatternElement" }, false, true);
            
            vm.StagingQueue.Add(item1);
            vm.StagingQueue.Add(item2);
            vm.StagingQueue.Add(item3);
            
            // Act: Edit multiple items
            vm.EditCommand.Execute(new List<QueueItemModel> { item1, item2, item3 });
            
            // Assert: Prefix stripped, distinct, concatenated
            Assert.AreEqual("LinePatternElement, TextNoteType", vm.SelectedDisplayClass);
        }

        [Test]
        public void SelectedAliasesString_ReturnsVariesForMultiSelection_AndPropagatesSingleSelectionEdits()
        {
            // Arrange
            var mainDoc = (Document)Activator.CreateInstance(typeof(Document), true)!;
            var fakeFileDialog = new FakeFileDialogService();
            var fakeGuardrail = new FakeGuardrailPromptService();
            var settings = new StandardsSettings();
            
            var vm = DashboardTestFactory.Create(mainDoc, fakeFileDialog, new StandardsExportService(fakeGuardrail, fakeFileDialog), settings);
            
            var item1 = new QueueItemModel(new ElementModel { Name = "Elem1", Class = "Autodesk.Revit.DB.LinePatternElement", Aliases = new List<string> { "Alias1A", "Alias1B" } }, false, true);
            var item2 = new QueueItemModel(new ElementModel { Name = "Elem2", Class = "Autodesk.Revit.DB.LinePatternElement", Aliases = new List<string> { "Alias2A" } }, false, true);
            
            vm.StagingQueue.Add(item1);
            vm.StagingQueue.Add(item2);
            
            // Act: Edit single item
            vm.EditCommand.Execute(new List<QueueItemModel> { item1 });
            Assert.AreEqual("Alias1A, Alias1B", vm.SelectedAliasesString);
            
            // Mutate aliases via property setter
            vm.SelectedAliasesString = "NewAlias1, NewAlias2, ";
            Assert.AreEqual("NewAlias1, NewAlias2", vm.SelectedAliasesString);
            CollectionAssert.AreEqual(new List<string> { "NewAlias1", "NewAlias2" }, ((ElementModel)item1.Model).Aliases);
            Assert.IsTrue(item1.IsEdited);
            
            // Act: Edit multiple items
            vm.EditCommand.Execute(new List<QueueItemModel> { item1, item2 });
            Assert.AreEqual("<Varies>", vm.SelectedAliasesString);
            
            // Attempt mutate on multiple items (should do nothing)
            vm.SelectedAliasesString = "ShouldNotChange";
            Assert.AreEqual("<Varies>", vm.SelectedAliasesString);
        }

        [Test]
        public void HarvestPocoProperties_CorrectlySweepsAndSyncsPocoProperties()
        {
            // Arrange
            var mockPoco = new MockPocoModel
            {
                Name = "TestMock",
                Class = "Autodesk.Revit.DB.MockPocoModel",
                Aliases = new List<string> { "MockAlias" },
                WritableString = "OriginalWritable",
                NullableDouble = 1.23,
                ComplexStructure = new CompoundStructureModel { Layers = new List<SerialCompoundStructureLayer>() }
            };

            var wrapper = new ElementTypeWrapperVM(mockPoco);

            // Act: Sweep properties
            var harvested = wrapper.HarvestPocoProperties();

            // Assert: verify exclusions (Name, Class, Aliases, IgnoredProperty are skipped)
            Assert.IsFalse(harvested.Any(p => p.Name == "Name"));
            Assert.IsFalse(harvested.Any(p => p.Name == "Class"));
            Assert.IsFalse(harvested.Any(p => p.Name == "Aliases"));
            Assert.IsFalse(harvested.Any(p => p.Name == "IgnoredProperty"));

            // Assert: verify read-write primitive
            var writableStrParam = harvested.FirstOrDefault(p => p.Name == "WritableString");
            Assert.IsNotNull(writableStrParam);
            Assert.IsFalse(writableStrParam.IsReadOnly);
            Assert.AreEqual("OriginalWritable", writableStrParam.Value);

            // Assert: verify read-only primitive
            var readOnlyIntParam = harvested.FirstOrDefault(p => p.Name == "ReadOnlyInt");
            Assert.IsNotNull(readOnlyIntParam);
            Assert.IsTrue(readOnlyIntParam.IsReadOnly);
            Assert.AreEqual("42", readOnlyIntParam.Value);

            // Assert: verify complex nested structure
            var complexParam = harvested.FirstOrDefault(p => p.Name == "ComplexStructure");
            Assert.IsNotNull(complexParam);
            Assert.IsFalse(complexParam.IsReadOnly);
            Assert.AreEqual("[Complex Nested Data]", complexParam.Value);

            // Act: Update primitive value and verify synchronization
            writableStrParam.Value = "UpdatedWritable";
            Assert.AreEqual("UpdatedWritable", mockPoco.WritableString, "Poco property should update when wrapper value changes.");
            Assert.IsTrue(wrapper.IsDirty, "Wrapper should be marked as dirty when harvested property is edited.");
        }

        [Test]
        public void ParametersCollection_HarvestedPropertiesPrependedToTop()
        {
            var mockPoco = new MockPocoModel
            {
                Name = "TestMock",
                Class = "Autodesk.Revit.DB.MockPocoModel",
                WritableString = "WritableVal",
                NullableDouble = 1.23
            };

            mockPoco.Parameters = new List<ParameterModel>
            {
                new ParameterModel(Name: "NativeParam", Value: "NativeVal", ValueElemId: null, StorageType: "String", Id: 10, GUID: null, IsShared: false, IsReadOnly: false)
            };

            var wrapper = new ElementTypeWrapperVM(mockPoco);

            Assert.AreEqual("WritableString", wrapper.Parameters[0].Name);
            Assert.AreEqual("ReadOnlyInt", wrapper.Parameters[1].Name);
            Assert.AreEqual("NullableDouble", wrapper.Parameters[2].Name);
            Assert.AreEqual("ComplexStructure", wrapper.Parameters[3].Name);
            Assert.AreEqual("NativeParam", wrapper.Parameters[4].Name);
        }

        [Test]
        public void ParametersCollection_HarvestedPropertiesRaiseParentIsDirty()
        {
            var mockPoco = new MockPocoModel
            {
                Name = "TestMock",
                Class = "Autodesk.Revit.DB.MockPocoModel",
                WritableString = "WritableVal"
            };

            var wrapper = new ElementTypeWrapperVM(mockPoco);
            Assert.IsFalse(wrapper.IsDirty);

            var writableParam = wrapper.Parameters.First(p => p.Name == "WritableString");
            writableParam.Value = "NewVal";

            Assert.IsTrue(wrapper.IsDirty, "Parent wrapper should become dirty when a POCO parameter in the unified list is modified.");
        }

        [Test]
        public void MultiSelect_PocoPropertiesParticipateInIntersectionAndVariesState()
        {
            var mainDoc = (Document)Activator.CreateInstance(typeof(Document), true)!;
            var fakeFileDialog = new FakeFileDialogService();
            var fakeGuardrail = new FakeGuardrailPromptService();
            var settings = new StandardsSettings();
            
            var vm = DashboardTestFactory.Create(mainDoc, fakeFileDialog, new StandardsExportService(fakeGuardrail, fakeFileDialog), settings);
            
            var poco1 = new MockPocoModel { Name = "Poco1", Class = "Autodesk.Revit.DB.MockPocoModel", WritableString = "CommonVal", NullableDouble = 1.0 };
            var poco2 = new MockPocoModel { Name = "Poco2", Class = "Autodesk.Revit.DB.MockPocoModel", WritableString = "CommonVal", NullableDouble = 2.0 };
            
            var item1 = new QueueItemModel(poco1, false, true);
            var item2 = new QueueItemModel(poco2, false, true);
            
            vm.StagingQueue.Add(item1);
            vm.StagingQueue.Add(item2);
            
            vm.EditCommand.Execute(new List<QueueItemModel> { item1, item2 });
            
            var writableParam = vm.DisplayParameters.FirstOrDefault(p => p.Name == "WritableString");
            Assert.IsNotNull(writableParam);
            Assert.AreEqual("CommonVal", writableParam.Value);
            Assert.IsFalse(writableParam.IsMixedValue);

            var nullableParam = vm.DisplayParameters.FirstOrDefault(p => p.Name == "NullableDouble");
            Assert.IsNotNull(nullableParam);
            Assert.AreEqual("<Varies>", nullableParam.Value);
            Assert.IsTrue(nullableParam.IsMixedValue);

            writableParam.Value = "BulkUpdatedVal";
            
            Assert.AreEqual("BulkUpdatedVal", ((MockPocoModel)item1.Model).WritableString);
            Assert.AreEqual("BulkUpdatedVal", ((MockPocoModel)item2.Model).WritableString);
        }

        private class FakeStandardsExtractionOrchestrator : IStandardsExtractionOrchestrator
        {
            public bool ExtractCalled { get; private set; }
            public List<ObjectModel> Extract(Document doc, IEnumerable<Element> rootElements, IProgress<string>? progress = null)
            {
                ExtractCalled = true;
                return new List<ObjectModel>();
            }

            public List<ObjectModel> Extract(Document doc, IEnumerable<Element> rootElements, IProgress<string>? progress = null, bool isTemplate = false)
            {
                ExtractCalled = true;
                return new List<ObjectModel>();
            }
        }

        [Test]
        public void VerifyHeadlessConstruction_WithMockedOrchestrator()
        {
            // Arrange
            var doc = (Document)Activator.CreateInstance(typeof(Document), true)!;
            var fakeFileDialog = new FakeFileDialogService();
            var fakeGuardrail = new FakeGuardrailPromptService();
            var settings = new StandardsSettings();
            var fakeOrchestrator = new FakeStandardsExtractionOrchestrator();

            // Act
            var vm = DashboardTestFactory.Create(
                doc: doc, 
                dialogService: fakeFileDialog, 
                exportService: null, 
                settings: settings, 
                userPromptService: null, 
                findReplaceService: null,
                orchestrator: fakeOrchestrator);

            // Assert
            Assert.IsNotNull(vm);
            var orchestratorField = typeof(ProjectStandardsDashboardViewModel)
                .GetField("_orchestrator", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert.IsNotNull(orchestratorField);
            var orchestratorObj = orchestratorField!.GetValue(vm);
            Assert.AreSame(fakeOrchestrator, orchestratorObj);
        }

        [Test]
        public void VerifyReplaceQueueReferences_UpdatesPatternAndAppearanceAssetIds()
        {
            // Arrange
            var vm = DashboardTestFactory.Create(_doc, _fakeDialogService);

            // 1. Create the old element to be replaced
            var oldPoco = new ElementModel { Name = "OldAsset", Class = "Autodesk.Revit.DB.AppearanceAssetElement" };
            var oldItem = new QueueItemModel(oldPoco, true, true);

            // 2. Create a Material in the staging queue that references "OldAsset"
            var matPoco = new MaterialModel
            {
                Name = "TestMaterial",
                AppearanceAssetId = new ElementIdModel
                {
                    Name = "OldAsset",
                    Id = 12345,
                    UniqueId = "SomeUniqueId-Asset"
                },
                SurfaceForegroundPatternId = new ElementIdModel
                {
                    Name = "OldAsset",
                    Id = 67890,
                    UniqueId = "SomeUniqueId-Pattern"
                }
            };
            var matItem = new QueueItemModel(matPoco, true, true);
            vm.StagingQueue.Add(matItem);

            // Act
            vm.ReplaceQueueReferences(new List<QueueItemModel> { oldItem }, "NewAsset");

            // Assert
            var updatedMat = (MaterialModel)matItem.TargetModel;
            
            // Verify AppearanceAssetId was updated
            Assert.IsNotNull(updatedMat.AppearanceAssetId);
            Assert.AreEqual("NewAsset", updatedMat.AppearanceAssetId!.Name);
            Assert.AreEqual(0, updatedMat.AppearanceAssetId.Id);
            Assert.IsNull(updatedMat.AppearanceAssetId.UniqueId, "AppearanceAssetId.UniqueId should be null after replacement.");

            // Verify SurfaceForegroundPatternId was updated
            Assert.IsNotNull(updatedMat.SurfaceForegroundPatternId);
            Assert.AreEqual("NewAsset", updatedMat.SurfaceForegroundPatternId!.Name);
            Assert.AreEqual(0, updatedMat.SurfaceForegroundPatternId.Id);
            Assert.IsNull(updatedMat.SurfaceForegroundPatternId.UniqueId, "SurfaceForegroundPatternId.UniqueId should be null after replacement.");
        }

        [Test]
        public void VerifyPropertyChangedForwarding_FromSubViewModels()
        {
            // Arrange
            var vm = DashboardTestFactory.Create(_doc, _fakeDialogService);
            var receivedProperties = new List<string>();
            vm.PropertyChanged += (sender, e) =>
            {
                if (e.PropertyName != null)
                {
                    receivedProperties.Add(e.PropertyName);
                }
            };

            var onPropertyChangedMethod = typeof(ViewModelBase)
                .GetMethod("OnPropertyChanged", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert.IsNotNull(onPropertyChangedMethod);

            // Act - Trigger PropertyChanged on each sub-VM via reflection
            // 1. SourceTreeViewModel
            onPropertyChangedMethod!.Invoke(vm.SourceTreeViewModel, new object[] { nameof(StandardsSourceTreeViewModel.SearchText) });

            // 2. StagingQueueViewModel
            onPropertyChangedMethod!.Invoke(vm.StagingQueueViewModel, new object[] { nameof(StagingQueueViewModel.SelectedAliasesString) });

            // 3. StandardsExecutionPipelineViewModel
            onPropertyChangedMethod!.Invoke(vm.StandardsExecutionPipelineViewModel, new object[] { nameof(StandardsExecutionPipelineViewModel.SaveFilePath) });

            // Assert
            Assert.Contains(nameof(StandardsSourceTreeViewModel.SearchText), receivedProperties);
            Assert.Contains(nameof(StagingQueueViewModel.SelectedAliasesString), receivedProperties);
            Assert.Contains(nameof(StandardsExecutionPipelineViewModel.SaveFilePath), receivedProperties);
        }
    }

    public class MockPocoModel : ElementModel
    {
        [Newtonsoft.Json.JsonIgnore]
        public string IgnoredProperty { get; set; } = "Ignored";

        public string WritableString { get; set; } = "Writable";
        
        public int ReadOnlyInt => 42;
        
        public double? NullableDouble { get; set; } = 3.14;
        
        public CompoundStructureModel ComplexStructure { get; set; } = new CompoundStructureModel();
    }
}
