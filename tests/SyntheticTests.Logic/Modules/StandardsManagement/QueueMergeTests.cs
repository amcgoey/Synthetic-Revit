using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Autodesk.Revit.DB;
using Synthetic.Modules.RevitDOM;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.Modules.StandardsManagement.Utilities;

namespace SyntheticTests.Modules.StandardsManagement
{
    [TestFixture]
    public class QueueMergeTests
    {
        private Document _doc = null!;
        private FakeFileDialogService _fakeDialogService = null!;

        [SetUp]
        public void Setup()
        {
            _doc = (Document)Activator.CreateInstance(typeof(Document), true)!;
            _fakeDialogService = new FakeFileDialogService();
        }

        [Test]
        public void MergeQueueItems_ShouldAppendNonSurvivorNameToSurvivorAliasesAndPurgeConsumed()
        {
            // Arrange
            var vm = new ProjectStandardsDashboardViewModel(_doc, _fakeDialogService, new FakeGuardrailPromptService(), null);

            var oak = new MaterialModel { Class = "Autodesk.Revit.DB.Material", Name = "Material - Oak" };
            var pine = new MaterialModel { Class = "Autodesk.Revit.DB.Material", Name = "Material - Pine" };

            var qOak = new QueueItemModel(oak, true, false);
            var qPine = new QueueItemModel(pine, true, false);

            vm.ActionQueue.Add(qOak);
            vm.ActionQueue.Add(qPine);

            var selectedList = new List<QueueItemModel> { qOak, qPine };

            // Act
            vm.MergeQueueCommand.Execute(selectedList);

            // Assert
            // Oak (first item) should be SelectedPrimary by ShowMergeDialog mock.
            Assert.AreEqual(1, vm.ActionQueue.Count, "Non-survivor pine should be purged.");
            Assert.AreSame(qOak, vm.ActionQueue.First(), "Oak should remain.");

            var oakModel = qOak.Model as ElementModel;
            Assert.IsNotNull(oakModel);
            Assert.Contains("Material - Pine", oakModel.Aliases, "Oak aliases should contain Pine.");
            Assert.IsTrue(qOak.IsEdited, "Oak should be marked as edited.");
        }

        [Test]
        public void MergeQueueItems_ShouldCascadingRedirectReferences()
        {
            // Arrange
            var vm = new ProjectStandardsDashboardViewModel(_doc, _fakeDialogService, new FakeGuardrailPromptService(), null);

            var oak = new MaterialModel { Class = "Autodesk.Revit.DB.Material", Name = "Material - Oak" };
            var pine = new MaterialModel { Class = "Autodesk.Revit.DB.Material", Name = "Material - Pine" };

            // A WallType model that references Pine material in its layers
            var layer = new SerialCompoundStructureLayer
            {
                MaterialId = new ElementIdModel { Name = "Material - Pine" }
            };
            var wall = new HostObjTypeModel
            {
                Class = "Autodesk.Revit.DB.WallType",
                Name = "Timber Wall",
                Structure = new CompoundStructureModel
                {
                    Layers = new List<SerialCompoundStructureLayer> { layer }
                }
            };

            var qOak = new QueueItemModel(oak, true, false);
            var qPine = new QueueItemModel(pine, true, false);
            var qWall = new QueueItemModel(wall, true, false);

            vm.ActionQueue.Add(qOak);
            vm.ActionQueue.Add(qPine);
            vm.ActionQueue.Add(qWall);

            var selectedList = new List<QueueItemModel> { qOak, qPine };

            // Act
            vm.MergeQueueCommand.Execute(selectedList);

            // Assert
            Assert.AreEqual(2, vm.ActionQueue.Count, "Pine should be purged, Oak and Wall should remain.");
            
            var wallModel = qWall.Model as HostObjTypeModel;
            Assert.IsNotNull(wallModel);
            Assert.IsNotNull(wallModel.Structure);
            Assert.IsNotNull(wallModel.Structure.Layers);
            Assert.AreEqual("Material - Oak", wallModel.Structure.Layers[0].MaterialId?.Name, "Wall structure layer reference should be redirected to Oak.");
        }

        [Test]
        public void ExtractRevitElements_ShouldScanAndExtractAllSupportedTypesAndFamilySymbols()
        {
            // Arrange
            var vm = new ProjectStandardsDashboardViewModel(_doc, _fakeDialogService, new FakeGuardrailPromptService(), null);

            void AddToDoc(Element el, string name, int idVal)
            {
                el.GetType().GetProperty("Name")?.SetValue(el, name);
                el.GetType().GetProperty("Id")?.SetValue(el, new ElementId(idVal));
                _doc.GetType().GetMethod("AddElement")?.Invoke(_doc, new object[] { el, el.Id });
            }

            // Add mock elements to document
            var wallType = (WallType)Activator.CreateInstance(typeof(WallType), true)!;
            AddToDoc(wallType, "Mock Wall Type", 101);

            var linePattern = (LinePatternElement)Activator.CreateInstance(typeof(LinePatternElement), true)!;
            AddToDoc(linePattern, "Mock Line Pattern", 102);

            var material = (Material)Activator.CreateInstance(typeof(Material), true)!;
            AddToDoc(material, "Mock Material", 103);

            // FamilySymbol with Title Block category
            var fs = (FamilySymbol)Activator.CreateInstance(typeof(FamilySymbol), true)!;
            fs.GetType().GetProperty("Name")?.SetValue(fs, "Mock Title Block Symbol");
            fs.GetType().GetProperty("Id")?.SetValue(fs, new ElementId(104));

            var cat = (Category)Activator.CreateInstance(typeof(Category), true)!;
            cat.GetType().GetProperty("Id")?.SetValue(cat, new ElementId((int)BuiltInCategory.OST_TitleBlocks));
            cat.GetType().GetProperty("Name")?.SetValue(cat, "Title Blocks");
            cat.GetType().GetProperty("CategoryType")?.SetValue(cat, CategoryType.Annotation);
            fs.GetType().GetProperty("Category")?.SetValue(fs, cat);

            _doc.GetType().GetMethod("AddElement")?.Invoke(_doc, new object[] { fs, fs.Id });

            // Act: Private method invoke via reflection
            var extractMethod = typeof(ProjectStandardsDashboardViewModel)
                .GetMethod("ExtractRevitElements", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert.IsNotNull(extractMethod);

            var selectedGroupings = new List<string> { "Title Blocks", "Wall Types", "Line Patterns", "Materials" };
            var result = (List<ElementModel>)extractMethod.Invoke(vm, new object[] { _doc, false, false, selectedGroupings })!;

            // Assert
            Assert.IsNotNull(result);
            var names = result.Select(r => r.Name).ToList();

            Assert.Contains("Mock Wall Type", names);
            Assert.Contains("Mock Line Pattern", names);
            Assert.Contains("Mock Material", names);
            Assert.Contains("Mock Title Block Symbol", names);
        }

        [Test]
        public void MergeStandardsLists_ShouldOverwriteDuplicates_WhenOverwriteIsTrue()
        {
            // Arrange
            var existing = new List<ElementModel>
            {
                new MaterialModel { Class = "Autodesk.Revit.DB.Material", Name = "Steel", Category = "Existing Category" }
            };
            var newElements = new List<ElementModel>
            {
                new MaterialModel { Class = "Autodesk.Revit.DB.Material", Name = "Steel", Category = "New Category" },
                new MaterialModel { Class = "Autodesk.Revit.DB.Material", Name = "Concrete", Category = "New Category" }
            };

            // Act
            var result = StandardsMergeUtility.Merge(existing, newElements, overwriteDuplicates: true);

            // Assert
            Assert.AreEqual(2, result.Count);
            var steel = result.FirstOrDefault(x => x.Name == "Steel");
            Assert.IsNotNull(steel);
            Assert.AreEqual("New Category", steel.Category);
        }

        [Test]
        public void MergeStandardsLists_ShouldPreserveDuplicates_WhenOverwriteIsFalse()
        {
            // Arrange
            var existing = new List<ElementModel>
            {
                new MaterialModel { Class = "Autodesk.Revit.DB.Material", Name = "Steel", Category = "Existing Category" }
            };
            var newElements = new List<ElementModel>
            {
                new MaterialModel { Class = "Autodesk.Revit.DB.Material", Name = "Steel", Category = "New Category" },
                new MaterialModel { Class = "Autodesk.Revit.DB.Material", Name = "Concrete", Category = "New Category" }
            };

            // Act
            var result = StandardsMergeUtility.Merge(existing, newElements, overwriteDuplicates: false);

            // Assert
            Assert.AreEqual(2, result.Count);
            var steel = result.FirstOrDefault(x => x.Name == "Steel");
            Assert.IsNotNull(steel);
            Assert.AreEqual("Existing Category", steel.Category);
        }

        [Test]
        public void IsSavePathActive_ShouldReflectQueueIntent()
        {
            // Arrange
            var vm = new ProjectStandardsDashboardViewModel(_doc, _fakeDialogService, new FakeGuardrailPromptService(), null);
            Assert.IsFalse(vm.IsSavePathActive);

            // Act
            var item = new QueueItemModel(new MaterialModel { Class = "Material", Name = "Test" }, false, true);
            vm.ActionQueue.Add(item);

            // Assert
            Assert.IsTrue(vm.IsSavePathActive);

            // Act
            vm.ActionQueue.Remove(item);

            // Assert
            Assert.IsFalse(vm.IsSavePathActive);
        }

        [Test]
        public void MergeQueueItems_ShouldTransitionWorkspaceToIdle_WhenMergeIsSuccessfulInEditMode()
        {
            // Arrange
            var vm = new ProjectStandardsDashboardViewModel(_doc, _fakeDialogService, new FakeGuardrailPromptService(), null);

            var oak = new MaterialModel { Class = "Autodesk.Revit.DB.Material", Name = "Material - Oak" };
            var pine = new MaterialModel { Class = "Autodesk.Revit.DB.Material", Name = "Material - Pine" };

            var qOak = new QueueItemModel(oak, true, false);
            var qPine = new QueueItemModel(pine, true, false);

            vm.ActionQueue.Add(qOak);
            vm.ActionQueue.Add(qPine);

            var selectedList = new List<QueueItemModel> { qOak, qPine };

            // Transition workspace to Edit mode first
            vm.EditCommand.Execute(selectedList);
            Assert.AreEqual(WorkspaceMode.Edit, vm.ActiveWorkspace, "Workspace should be in Edit mode.");

            // Act
            vm.MergeQueueCommand.Execute(selectedList);

            // Assert
            // Oak (first item) should be SelectedPrimary by ShowMergeDialog mock.
            Assert.AreEqual(1, vm.ActionQueue.Count, "Non-survivor pine should be purged.");
            Assert.AreEqual(WorkspaceMode.Idle, vm.ActiveWorkspace, "Active workspace should transition to Idle post-merge.");
        }

        [Test]
        public void MergeQueueItems_ShouldCombineExecutionFlagsAndMergeAliasesOntoSurvivor()
        {
            // Arrange
            var vm = new ProjectStandardsDashboardViewModel(_doc, _fakeDialogService, new FakeGuardrailPromptService(), null);

            var oak = new MaterialModel { Class = "Autodesk.Revit.DB.Material", Name = "Material - Oak", Aliases = new List<string> { "OakAlias1" } };
            var pine = new MaterialModel { Class = "Autodesk.Revit.DB.Material", Name = "Material - Pine", Aliases = new List<string> { "PineAlias1" } };

            // Oak is Enforce, Pine is Save
            var qOak = new QueueItemModel(oak, true, false);
            var qPine = new QueueItemModel(pine, false, true);

            vm.ActionQueue.Add(qOak);
            vm.ActionQueue.Add(qPine);

            var selectedList = new List<QueueItemModel> { qOak, qPine };

            // Act
            vm.MergeQueueCommand.Execute(selectedList);

            // Assert
            Assert.AreEqual(1, vm.ActionQueue.Count, "Pine should be purged.");
            var survivor = vm.ActionQueue[0];
            Assert.AreSame(qOak, survivor, "Oak should be the survivor.");
            
            // Flags should be combined
            Assert.IsTrue(survivor.WillEnforce, "Survivor should have WillEnforce set to true.");
            Assert.IsTrue(survivor.WillSave, "Survivor should inherit WillSave from the merged Pine element.");

            // Aliases should be merged
            var survivorModel = survivor.Model as MaterialModel;
            Assert.IsNotNull(survivorModel);
            // Expected aliases: OakAlias1 (original), Material - Pine (NP name), PineAlias1 (NP alias)
            CollectionAssert.Contains(survivorModel.Aliases, "OakAlias1");
            CollectionAssert.Contains(survivorModel.Aliases, "Material - Pine");
            CollectionAssert.Contains(survivorModel.Aliases, "PineAlias1");
        }
    }
}
