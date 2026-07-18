using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Autodesk.Revit.DB;
using Synthetic.Modules.RevitDOM;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.Shared.UI;

namespace SyntheticTests.Modules.StandardsManagement
{
    [TestFixture]
    public class DashboardParametersEditorTests
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
        public void SpecializedParameterExtraction_ShouldPopulateSegmentAndOrientationMetadata()
        {
            // 1. LinePatternElementModel
            var lpModel = new LinePatternElementModel
            {
                Class = "Autodesk.Revit.DB.LinePatternElement",
                Name = "DashDot",
                Segments = new List<LinePatternSegmentModel>
                {
                    new LinePatternSegmentModel { Type = "Dash", Length = 0.5 },
                    new LinePatternSegmentModel { Type = "Space", Length = 0.25 }
                }
            };

            var lpWrapper = new ElementTypeWrapperVM(lpModel);
            var segmentCountParam = lpWrapper.Parameters.FirstOrDefault(p => p.Name == "SegmentCount");
            var segmentsParam = lpWrapper.Parameters.FirstOrDefault(p => p.Name == "Segments");

            Assert.IsNotNull(segmentCountParam, "SegmentCount parameter should be extracted.");
            Assert.AreEqual("2", segmentCountParam.Value);
            Assert.IsNotNull(segmentsParam, "Segments parameter should be extracted.");
            Assert.AreEqual("Dash: 0.5, Space: 0.25", segmentsParam.Value);

            // 2. FillPatternElementModel
            var fpModel = new FillPatternElementModel
            {
                Class = "Autodesk.Revit.DB.FillPatternElement",
                Name = "Diagonal",
                Pattern = new FillPatternModel
                {
                    Target = "Drafting",
                    HostOrientation = "ToHost",
                    FillGrids = new List<FillGridModel> { new FillGridModel(), new FillGridModel() }
                }
            };

            var fpWrapper = new ElementTypeWrapperVM(fpModel);
            var targetParam = fpWrapper.Parameters.FirstOrDefault(p => p.Name == "Target");
            var orientationParam = fpWrapper.Parameters.FirstOrDefault(p => p.Name == "HostOrientation");
            var gridCountParam = fpWrapper.Parameters.FirstOrDefault(p => p.Name == "GridCount");

            Assert.IsNotNull(targetParam, "Target parameter should be extracted.");
            Assert.AreEqual("Drafting", targetParam.Value);
            Assert.IsNotNull(orientationParam, "HostOrientation parameter should be extracted.");
            Assert.AreEqual("ToHost", orientationParam.Value);
            Assert.IsNotNull(gridCountParam, "GridCount parameter should be extracted.");
            Assert.AreEqual("2", gridCountParam.Value);
        }

        [Test]
        public void StagedMultiSelectIntersection_ShouldAggregateParametersAndDisplayVaries()
        {
            // Arrange
            var parent = DashboardTestFactory.Create(_doc, _fakeDialogService, null, null);
            var vm = parent.StagingQueueViewModel;

            var p1 = new ParameterModel("Comments", "ValueA", null, "String", 1, null, false, false);
            var el1 = new ElementModel { Class = "Autodesk.Revit.DB.LinePatternElement", Name = "Dash", Parameters = new List<ParameterModel> { p1 } };

            var p2 = new ParameterModel("Comments", "ValueB", null, "String", 1, null, false, false);
            var el2 = new ElementModel { Class = "Autodesk.Revit.DB.LinePatternElement", Name = "Dot", Parameters = new List<ParameterModel> { p2 } };

            var q1 = new QueueItemModel(el1, true, false);
            var q2 = new QueueItemModel(el2, true, false);

            vm.StagingQueue.Add(q1);
            vm.StagingQueue.Add(q2);

            // Act: Edit items to calculate intersection
            var itemsToEdit = new List<QueueItemModel> { q1, q2 };
            vm.EditCommand.Execute(itemsToEdit);

            // Assert intersection has Comments with <Varies>
            var commentsParam = vm.DisplayParameters.FirstOrDefault(p => p.Name == "Comments");
            Assert.IsNotNull(commentsParam, "Common parameter 'Comments' should be intersected.");
            Assert.IsTrue(commentsParam.IsMixedValue);
            Assert.AreEqual("<Varies>", commentsParam.Value);

            // Act: Update mixed value to "ValueShared"
            commentsParam.Value = "ValueShared";

            // Assert that the TargetModel of both queue items is updated and marked as Edited
            var targetEl1 = (ElementModel)q1.TargetModel;
            var targetEl2 = (ElementModel)q2.TargetModel;
            Assert.AreEqual("ValueShared", targetEl1.Parameters[0].Value);
            Assert.AreEqual("ValueShared", targetEl2.Parameters[0].Value);
            Assert.IsTrue(q1.IsEdited);
            Assert.IsTrue(q2.IsEdited);
 
            // Act: Cancel edits
            vm.CancelEditsCommand.Execute(null!);
 
            // Assert revert to original baseline values and original intents (Enforce)
            Assert.AreEqual("ValueA", ((ElementModel)q1.TargetModel).Parameters[0].Value);
            Assert.AreEqual("ValueB", ((ElementModel)q2.TargetModel).Parameters[0].Value);
            Assert.IsTrue(q1.WillEnforce);
            Assert.IsFalse(q1.IsEdited);
            Assert.IsTrue(q2.WillEnforce);
            Assert.IsFalse(q2.IsEdited);
        }

        [Test]
        public void CascadingRenameSafety_ShouldUpdateReferencesAcrossStagingQueue()
        {
            // Arrange
            var parent = DashboardTestFactory.Create(_doc, _fakeDialogService, null, null);
            var vm = parent.StagingQueueViewModel;

            var materialModel = new ElementModel
            {
                Class = "Autodesk.Revit.DB.Material",
                Name = "OldMaterialName",
                Parameters = new List<ParameterModel>()
            };

            var wallModel = new HostObjTypeModel
            {
                Class = "Autodesk.Revit.DB.WallType",
                Name = "Generic Wall",
                Structure = new CompoundStructureModel
                {
                    Layers = new List<SerialCompoundStructureLayer>
                    {
                        new SerialCompoundStructureLayer
                        {
                            MaterialId = new ElementIdModel { Name = "OldMaterialName" }
                        }
                    }
                }
            };

            var qMaterial = new QueueItemModel(materialModel, true, false);
            var qWall = new QueueItemModel(wallModel, true, false);

            vm.StagingQueue.Add(qMaterial);
            vm.StagingQueue.Add(qWall);

            // Act: Edit Material item to start session
            vm.EditCommand.Execute(new List<QueueItemModel> { qMaterial });

            // Trigger Name property change via SelectedItemName property
            vm.SelectedItemName = "NewMaterialName";

            // Assert references in wall compound structures are automatically updated to "NewMaterialName"
            var updatedWall = (HostObjTypeModel)qWall.TargetModel;
            Assert.AreEqual("NewMaterialName", updatedWall.Structure?.Layers[0].MaterialId?.Name);
        }

        [Test]
        public void NestedModalWiring_ShouldRetrievePoolFromAllWrappedElements()
        {
            // Arrange
            var vm = DashboardTestFactory.Create(_doc, _fakeDialogService, null, null);

            var mat = new ElementModel { Class = "Autodesk.Revit.DB.Material", Name = "Brick" };
            var wall = new HostObjTypeModel { Class = "Autodesk.Revit.DB.WallType", Name = "Brick Wall" };

            vm.StagingQueue.Add(new QueueItemModel(mat, true, false));
            vm.StagingQueue.Add(new QueueItemModel(wall, true, false));

            // Act
            var pool = vm.AllWrappedElements.ToList();

            // Assert
            Assert.AreEqual(2, pool.Count);
            Assert.IsTrue(pool.Any(w => w.Name == "Brick" && w.Class == "Autodesk.Revit.DB.Material"));
            Assert.IsTrue(pool.Any(w => w.Name == "Brick Wall" && w.Class == "Autodesk.Revit.DB.WallType"));
        }
    }
}
