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
    public class DashboardFindReplaceTests
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
        public void FindReplace_HeterogeneousSelection_ShouldMutateSelectedNamesAndParameters()
        {
            // Arrange
            var parent = DashboardTestFactory.Create(_doc, _fakeDialogService, null, null);
            var vm = parent.StagingQueueViewModel;

            var matParam = new ParameterModel("Comments", "FindMe_MaterialVal", null, "String", 1, null, false, false);
            var material = new ElementModel { Class = "Autodesk.Revit.DB.Material", Name = "FindMe_Material", Parameters = new List<ParameterModel> { matParam } };

            var lpParam = new ParameterModel("Comments", "FindMe_LineVal", null, "String", 1, null, false, false);
            var linePattern = new ElementModel { Class = "Autodesk.Revit.DB.LinePatternElement", Name = "FindMe_LinePattern", Parameters = new List<ParameterModel> { lpParam } };

            var qMaterial = new QueueItemModel(material, true, false);
            var qLinePattern = new QueueItemModel(linePattern, true, false);

            vm.StagingQueue.Add(qMaterial);
            vm.StagingQueue.Add(qLinePattern);

            // Edit both
            var itemsToEdit = new List<QueueItemModel> { qMaterial, qLinePattern };
            vm.EditCommand.Execute(itemsToEdit);

            // Act
            vm.FindText = "FindMe";
            vm.ReplaceText = "Replaced";
            vm.FindReplaceScope = SearchScope.Both;

            vm.BatchFindReplaceCommand.Execute(null!);

            // Assert names updated
            var targetMat = (ElementModel)qMaterial.TargetModel;
            var targetLp = (ElementModel)qLinePattern.TargetModel;
            Assert.AreEqual("Replaced_Material", targetMat.Name);
            Assert.AreEqual("Replaced_LinePattern", targetLp.Name);

            // Assert parameters updated
            Assert.AreEqual("Replaced_MaterialVal", targetMat.Parameters[0].Value);
            Assert.AreEqual("Replaced_LineVal", targetLp.Parameters[0].Value);

            // Assert intent marked as Edited (dirty state)
            Assert.IsTrue(qMaterial.IsEdited);
            Assert.IsTrue(qLinePattern.IsEdited);
        }

        [Test]
        public void FindReplace_ReadOnlyProtection_ShouldNotModifyReadOnlyParameters()
        {
            // Arrange
            var parent = DashboardTestFactory.Create(_doc, _fakeDialogService, null, null);
            var vm = parent.StagingQueueViewModel;

            var writableParam = new ParameterModel("Comments", "FindMe_Writable", null, "String", 1, null, false, false);
            var readOnlyParam = new ParameterModel("Category", "FindMe_ReadOnly", null, "String", 2, null, false, true); // IsReadOnly = true
            var el = new ElementModel { Class = "Autodesk.Revit.DB.Material", Name = "Mat", Parameters = new List<ParameterModel> { writableParam, readOnlyParam } };

            var qItem = new QueueItemModel(el, true, false);
            vm.StagingQueue.Add(qItem);

            vm.EditCommand.Execute(new List<QueueItemModel> { qItem });

            // Act
            vm.FindText = "FindMe";
            vm.ReplaceText = "Replaced";
            vm.FindReplaceScope = SearchScope.ParameterValues;

            vm.BatchFindReplaceCommand.Execute(null!);

            // Assert
            var target = (ElementModel)qItem.TargetModel;
            Assert.AreEqual("Replaced_Writable", target.Parameters.First(p => p.Name == "Comments").Value);
            Assert.AreEqual("FindMe_ReadOnly", target.Parameters.First(p => p.Name == "Category").Value); // Unchanged!
        }

        [Test]
        public void FindReplace_DirtyStateRecalculation_ShouldReportIsDirtyPostReplacement()
        {
            // Arrange
            var parent = DashboardTestFactory.Create(_doc, _fakeDialogService, null, null);
            var vm = parent.StagingQueueViewModel;

            var param = new ParameterModel("Comments", "FindMe", null, "String", 1, null, false, false);
            var el = new ElementModel { Class = "Autodesk.Revit.DB.Material", Name = "Mat", Parameters = new List<ParameterModel> { param } };

            var qItem = new QueueItemModel(el, true, false);
            vm.StagingQueue.Add(qItem);

            vm.EditCommand.Execute(new List<QueueItemModel> { qItem });

            // Act
            vm.FindText = "FindMe";
            vm.ReplaceText = "Replaced";
            vm.FindReplaceScope = SearchScope.ParameterValues;

            vm.BatchFindReplaceCommand.Execute(null!);

            // Assert
            var wrapper = qItem.GetWrapper();
            Assert.IsTrue(wrapper.IsDirty, "Wrapper should evaluate to dirty after Find & Replace modifications.");
            Assert.IsTrue(wrapper.Parameters[0].IsDirty, "Parameter should evaluate to dirty after modification.");
        }

        [Test]
        public void FindReplace_ScopeControls_ShouldRespectConfiguredScope()
        {
            // Arrange
            var parent = DashboardTestFactory.Create(_doc, _fakeDialogService, null, null);
            var vm = parent.StagingQueueViewModel;

            // ElementNames Only Scope
            var param1 = new ParameterModel("Comments", "FindMe", null, "String", 1, null, false, false);
            var el1 = new ElementModel { Class = "Autodesk.Revit.DB.Material", Name = "FindMe_Name", Parameters = new List<ParameterModel> { param1 } };
            var q1 = new QueueItemModel(el1, true, false);
            vm.StagingQueue.Add(q1);

            // ParameterValues Only Scope
            var param2 = new ParameterModel("Comments", "FindMe", null, "String", 1, null, false, false);
            var el2 = new ElementModel { Class = "Autodesk.Revit.DB.Material", Name = "FindMe_Name", Parameters = new List<ParameterModel> { param2 } };
            var q2 = new QueueItemModel(el2, true, false);
            vm.StagingQueue.Add(q2);

            // Act: Run Find & Replace for q1 with ElementNames scope
            vm.EditCommand.Execute(new List<QueueItemModel> { q1 });
            vm.FindText = "FindMe";
            vm.ReplaceText = "Replaced";
            vm.FindReplaceScope = SearchScope.ElementNames;

            vm.BatchFindReplaceCommand.Execute(null!);

            // Act: Run Find & Replace for q2 with ParameterValues scope
            vm.EditCommand.Execute(new List<QueueItemModel> { q2 });
            vm.FindReplaceScope = SearchScope.ParameterValues;
            vm.BatchFindReplaceCommand.Execute(null!);

            // Assert q1: name updated, parameter unchanged
            var target1 = (ElementModel)q1.TargetModel;
            Assert.AreEqual("Replaced_Name", target1.Name);
            Assert.AreEqual("FindMe", target1.Parameters[0].Value);

            // Assert q2: name unchanged, parameter updated
            var target2 = (ElementModel)q2.TargetModel;
            Assert.AreEqual("FindMe_Name", target2.Name);
            Assert.AreEqual("Replaced", target2.Parameters[0].Value);
        }
    }
}
