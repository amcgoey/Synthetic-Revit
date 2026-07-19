using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Autodesk.Revit.DB;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.Shared.UI;

namespace SyntheticTests.Modules.StandardsManagement
{
    [TestFixture]
    public class PhasedExecutionTests
    {
        private Document _doc = null!;
        private FakeFileDialogService _fakeDialogService = null!;
        private string _tempSavePath = null!;

        [SetUp]
        public void Setup()
        {
            _doc = (Document)Activator.CreateInstance(typeof(Document), true)!;
            _fakeDialogService = new FakeFileDialogService();
            _tempSavePath = Path.Combine(Path.GetTempPath(), $"SyntheticTemp_{Guid.NewGuid():N}.json");
            _fakeDialogService.PresetPath = _tempSavePath;
            ProgressCoordinator.SuppressUI = true;
        }

        [TearDown]
        public void TearDown()
        {
            if (File.Exists(_tempSavePath))
            {
                try { File.Delete(_tempSavePath); } catch { }
            }
            ProgressCoordinator.SuppressUI = false;
            ProgressCoordinator.ForceCancel = false;
        }

        [Test]
        public void RunQueue_FailureStripping_ShouldSaveOnlySuccessfulPhase1ItemsToDisk()
        {
            // Arrange
            var parent = DashboardTestFactory.Create(_doc, _fakeDialogService, null, null);
            var vm = parent.StandardsExecutionPipelineViewModel;

            vm.SaveFilePath = _tempSavePath;

            // Item 1: Valid element (succeeds Phase 1)
            var validParam = new ParameterModel("Comments", "ValidVal", null, "String", 1, null, false, false);
            var validEl = new MaterialModel { Class = "Autodesk.Revit.DB.Material", Name = "ValidMaterial", Parameters = new List<ParameterModel> { validParam } };
            var qValid = new QueueItemModel(validEl, true, true);
            
            // Item 2: Invalid element (fails Phase 1 due to missing class or null properties)
            var invalidEl = new ElementModel { Class = null!, Name = "InvalidMaterial" };
            var qInvalid = new QueueItemModel(invalidEl, true, true);

            parent.StagingQueue.Add(qValid);
            parent.StagingQueue.Add(qInvalid);

            // Act
            vm.RunQueueCommand.Execute(null!);

            // Assert: valid element was processed, invalid failed and was stripped
            Assert.AreEqual(3, parent.LastExecutionResults.Count, "Should have 3 execution results logged (2 from Phase 1, 1 from Phase 2).");
            
            var validResult = parent.LastExecutionResults.First(r => r.Model == qValid.Model);
            var invalidResult = parent.LastExecutionResults.First(r => r.Model == qInvalid.Model);

            Assert.IsTrue(validResult.Success, "Valid element should succeed Phase 1.");
            Assert.IsFalse(invalidResult.Success, "Invalid element should fail Phase 1.");

            // Assert: Phase 2 JSON file exists and contains only the valid element
            Assert.IsTrue(File.Exists(_tempSavePath), "The target JSON file should be written.");
            string jsonContent = File.ReadAllText(_tempSavePath);
            Assert.IsTrue(jsonContent.Contains("ValidMaterial"), "JSON file must contain successful elements.");
            Assert.IsFalse(jsonContent.Contains("InvalidMaterial"), "JSON file must exclude stripped/failed elements.");
        }

        [Test]
        public void RunQueue_UserCancellation_ShouldRollbackAllRevitWritesAndSkipPhase2()
        {
            // Arrange
            var parent = DashboardTestFactory.Create(_doc, _fakeDialogService, null, null);
            var vm = parent.StandardsExecutionPipelineViewModel;
            vm.SaveFilePath = _tempSavePath;

            var param = new ParameterModel("Comments", "SomeValue", null, "String", 1, null, false, false);
            var el = new MaterialModel { Class = "Autodesk.Revit.DB.Material", Name = "MatToCancel", Parameters = new List<ParameterModel> { param } };
            var qItem = new QueueItemModel(el, true, true);
            parent.StagingQueue.Add(qItem);

            // Inject a mock cancellation state in the coordinator
            ProgressCoordinator.ForceCancel = true;

            // Act
            vm.RunQueueCommand.Execute(null!);

            // Assert
            Assert.IsFalse(File.Exists(_tempSavePath), "Phase 2 file writing must be skipped completely on user cancellation.");
            Assert.IsTrue(qItem.WillEnforce && qItem.WillSave, "QueueItem intent flags should remain unchanged on rollback.");
            
            var cancelResult = parent.LastExecutionResults.FirstOrDefault(r => r.Model == qItem.Model);
            Assert.IsNotNull(cancelResult, "Cancellation execution result should be registered.");
            Assert.IsFalse(cancelResult!.Success, "Cancelled results must show as failed.");
            Assert.AreEqual("Execution cancelled by user.", cancelResult.ErrorMessage);
        }

        [Test]
        public void ProcessFamilyUpdates_WhenDisabled_ShouldNotScanForFamilies()
        {
            // Arrange
            var parent = DashboardTestFactory.Create(_doc, _fakeDialogService, null, null);
            var vm = parent.StandardsExecutionPipelineViewModel;
            vm.UpdateFamilies = false;

            var param = new ParameterModel("Comments", "Val", null, "String", 1, null, false, false);
            var el = new MaterialModel { Class = "Autodesk.Revit.DB.Material", Name = "Mat", Parameters = new List<ParameterModel> { param } };
            var qItem = new QueueItemModel(el, true, false);
            parent.StagingQueue.Add(qItem);

            // Act
            vm.RunQueueCommand.Execute(null!);

            // Assert: Completed without trying to collect families (since mocked Revit doc throws on family queries in real run but passes here)
            Assert.AreEqual(1, parent.LastExecutionResults.Count);
            Assert.IsTrue(parent.LastExecutionResults[0].Success);
        }

        [Test]
        public void RunQueue_PostExecutionPurging_ShouldRemoveSuccessfulAndKeepFailedItems()
        {
            // Arrange
            var parent = DashboardTestFactory.Create(_doc, _fakeDialogService, null, null);
            var vm = parent.StandardsExecutionPipelineViewModel;

            var validParam = new ParameterModel("Comments", "ValidVal", null, "String", 1, null, false, false);
            var validEl = new MaterialModel { Class = "Autodesk.Revit.DB.Material", Name = "ValidMaterial", Parameters = new List<ParameterModel> { validParam } };
            var qValid = new QueueItemModel(validEl, true, true);

            var invalidEl = new ElementModel { Class = null!, Name = "InvalidMaterial" };
            var qInvalid = new QueueItemModel(invalidEl, true, true);

            parent.StagingQueue.Add(qValid);
            parent.StagingQueue.Add(qInvalid);

            // Act
            vm.RunQueueCommand.Execute(null!);

            // Assert
            Assert.AreEqual(1, parent.StagingQueue.Count, "Successful items should be purged, failed should remain.");
            Assert.AreSame(qInvalid, parent.StagingQueue[0], "The failed item should remain in the queue.");
            Assert.IsTrue(qInvalid.HasError, "The failed item should have error registered.");
            Assert.IsFalse(string.IsNullOrEmpty(qInvalid.ErrorMessage), "The error message should be populated.");
        }

        [Test]
        public void FailedItem_OnParameterEdit_ShouldClearErrorAndSetIntentToEdited()
        {
            // Arrange
            var parent = DashboardTestFactory.Create(_doc, _fakeDialogService, null, null);
            var queueVM = parent.StagingQueueViewModel;
            var paramModel = new ParameterModel("Comments", "SomeVal", null, "String", 1, null, false, false);
            var el = new MaterialModel { Class = "Autodesk.Revit.DB.Material", Name = "Mat", Parameters = new List<ParameterModel> { paramModel } };
            var qItem = new QueueItemModel(el, true, true);
            qItem.ErrorMessage = "Some error occurred";

            parent.StagingQueue.Add(qItem);

            // Select item and enter edit mode
            queueVM.EditCommand.Execute(new List<QueueItemModel> { qItem });

            // Act: Mutate parameter
            var param = queueVM.DisplayParameters.First();
            param.Value = "NewVal";

            // Assert
            Assert.IsFalse(qItem.HasError, "Error state should be cleared on edit.");
            Assert.IsNull(qItem.ErrorMessage, "ErrorMessage should be reset to null.");
            Assert.IsTrue(qItem.IsEdited, "IsEdited should be set to true on edit.");
            Assert.IsNull(queueVM.SelectedItemErrorMessage, "Selected item error message on VM should be cleared.");
        }
    }
}
