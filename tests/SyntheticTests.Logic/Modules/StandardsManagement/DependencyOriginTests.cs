using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Autodesk.Revit.DB;
using Synthetic.Modules.RevitDOM;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.Shared.UI;

namespace SyntheticTests.Modules.StandardsManagement
{
    [TestFixture]
    public class DependencyOriginTests
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

        private T CreateMockElement<T>(Document doc, string name, int idVal) where T : Element
        {
            T elem = (T)Activator.CreateInstance(typeof(T), true)!;
            
            // Set Name
            var nameProp = typeof(T).GetProperty("Name");
            nameProp?.SetValue(elem, name);

            // Set Id
            var idProp = typeof(T).GetProperty("Id");
            if (idProp != null && idProp.CanWrite)
            {
                idProp.SetValue(elem, new ElementId(idVal));
            }

            // Add to doc
            var addElementMethod = doc.GetType().GetMethod("AddElement");
            if (addElementMethod != null)
            {
                addElementMethod.Invoke(doc, new object[] { elem, elem.Id });
            }

            return elem;
        }

        [Test]
        public void ExecutePushToQueue_ShouldPopulateDependencyOrigin_WhenDependencyIsHarvested()
        {
            // Arrange
            // Create a mock WallType
            var wallType = CreateMockElement<WallType>(_doc, "WallA", 1001);

            // Create a mock Material
            var material = CreateMockElement<Material>(_doc, "MaterialA", 2001);

            // Build simple compound structure linking WallType to Material
            var layer = new CompoundStructureLayer(0.2, MaterialFunctionAssignment.Structure, material.Id);
            var cs = CompoundStructure.CreateSimpleCompoundStructure(new List<CompoundStructureLayer> { layer });
            wallType.SetCompoundStructure(cs);

            var vm = new ProjectStandardsDashboardViewModel(_doc, _fakeDialogService);
            var source = new ProjectStandardsSourceViewModel { DisplayName = "Test Source" };
            
            var group = new StandardGroupModel { Name = "System / Host Object Types" };
            var classModel = new StandardClassModel { Name = "Wall Types" };

            // Wrap WallType into the hierarchy
            var wallPoco = wallType.ToModel(false);
            var wallNode = new StandardElementModel(wallPoco);

            classModel.Children.Add(wallNode);
            wallNode.Parent = classModel;
            group.Children.Add(classModel);
            classModel.Parent = group;
            source.SourceHierarchy.Add(group);

            // Wrap Material into the hierarchy as well so it's in the offline sourcePool lookup
            var matPoco = new MaterialModel { Name = "MaterialA", Class = "Autodesk.Revit.DB.Material" };
            var matNode = new StandardElementModel(matPoco);
            var matGroup = new StandardGroupModel { Name = "Materials & Assets" };
            var matClass = new StandardClassModel { Name = "Materials" };
            matClass.Children.Add(matNode);
            matNode.Parent = matClass;
            matGroup.Children.Add(matClass);
            matClass.Parent = matGroup;
            source.SourceHierarchy.Add(matGroup);

            vm.AvailableSources.Add(source);
            vm.SelectedSource = source;

            // Explicitly check ONLY the WallType
            wallNode.IsChecked = true;

            // Act
            vm.PushToQueueCommand.Execute("Save");

            // Assert
            // The queue should have WallA (explicitly checked) and MaterialA (harvested)
            Assert.AreEqual(2, vm.ActionQueue.Count, "Queue should contain both the WallType and its harvested Material dependency.");

            var wallQueueItem = vm.ActionQueue.FirstOrDefault(q => q.Name == "WallA");
            var matQueueItem = vm.ActionQueue.FirstOrDefault(q => q.Name == "MaterialA");

            Assert.IsNotNull(wallQueueItem, "WallA queue item should exist.");
            Assert.IsNotNull(matQueueItem, "MaterialA queue item should exist.");

            // WallA is explicitly checked, so its DependencyOrigin should be null/empty
            Assert.IsFalse(wallQueueItem.IsDependency, "WallA is explicitly checked and should not be flagged as a dependency.");
            Assert.IsNull(wallQueueItem.DependencyOrigin, "WallA should have null DependencyOrigin.");

            // MaterialA is harvested as dependency, so its DependencyOrigin should be WallA
            Assert.IsTrue(matQueueItem.IsDependency, "MaterialA should be flagged as a dependency.");
            Assert.AreEqual("WallA", matQueueItem.DependencyOrigin, "MaterialA's DependencyOrigin should match the root WallType name.");
        }
    }
}
