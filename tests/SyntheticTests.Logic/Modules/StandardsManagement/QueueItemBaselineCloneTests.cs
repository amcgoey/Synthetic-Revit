using System;
using System.Collections.Generic;
using NUnit.Framework;
using Synthetic.Modules.RevitDOM;
using Synthetic.Modules.StandardsManagement.ViewModels;

namespace SyntheticTests.Modules.StandardsManagement
{
    [TestFixture]
    public class QueueItemBaselineCloneTests
    {
        private ElementModel _sourceElement;
        private ParameterModel _sourceParam;

        [SetUp]
        public void SetUp()
        {
            _sourceParam = new ParameterModel("LineWidth", "1", null, "Integer", 12345, null, false, false);
            _sourceElement = new ElementModel
            {
                Class = "Autodesk.Revit.DB.LinePatternElement",
                Name = "Dash",
                Parameters = new List<ParameterModel> { _sourceParam }
            };
        }

        [Test]
        public void Constructor_ShouldDecoupleAndDeepCloneIndependentCopies()
        {
            // Arrange & Act
            var queueItem = new QueueItemModel(_sourceElement, true, false);

            // Assert
            Assert.IsNotNull(queueItem.BaselineModel, "BaselineModel should be instantiated.");
            Assert.IsNotNull(queueItem.TargetModel, "TargetModel should be instantiated.");
            
            // Reference isolation assertions
            Assert.AreNotSame(_sourceElement, queueItem.BaselineModel, "BaselineModel must not share references with the source.");
            Assert.AreNotSame(_sourceElement, queueItem.TargetModel, "TargetModel must not share references with the source.");
            Assert.AreNotSame(queueItem.BaselineModel, queueItem.TargetModel, "BaselineModel and TargetModel must not share references.");
            Assert.AreSame(queueItem.TargetModel, queueItem.Model, "Model property must dynamically redirect to TargetModel.");

            // Verify inner parameter isolation
            var baselineElement = (ElementModel)queueItem.BaselineModel;
            var targetElement = (ElementModel)queueItem.TargetModel;

            Assert.AreNotSame(_sourceParam, baselineElement.Parameters[0], "Baseline parameters must not reference source parameters.");
            Assert.AreNotSame(_sourceParam, targetElement.Parameters[0], "Target parameters must not reference source parameters.");
            Assert.AreNotSame(baselineElement.Parameters[0], targetElement.Parameters[0], "Baseline parameters and Target parameters must be decoupled.");
        }

        [Test]
        public void WrapperMutations_ShouldUpdateTargetModelButLeaveBaselineAndSourceUnaltered()
        {
            // Arrange
            var queueItem = new QueueItemModel(_sourceElement, true, false);
            var wrapper = queueItem.GetWrapper();

            // Act
            wrapper.Parameters[0].Value = "99";
            wrapper.Name = "ModifiedName";

            // Assert
            var baselineElement = (ElementModel)queueItem.BaselineModel;
            var targetElement = (ElementModel)queueItem.TargetModel;

            // Target should be updated
            Assert.AreEqual("99", targetElement.Parameters[0].Value);
            Assert.AreEqual("ModifiedName", targetElement.Name);

            // Baseline should be unaltered
            Assert.AreEqual("1", baselineElement.Parameters[0].Value);
            Assert.AreEqual("Dash", baselineElement.Name);

            // Original source should be unaltered
            Assert.AreEqual("1", _sourceElement.Parameters[0].Value);
            Assert.AreEqual("Dash", _sourceElement.Name);
        }

        [Test]
        public void DynamicDirtyTracking_ShouldEvaluateOnTheFlyAndRevertCorrectly()
        {
            // Arrange
            var queueItem = new QueueItemModel(_sourceElement, true, false);
            var wrapper = queueItem.GetWrapper();

            // Assert Initial State
            Assert.IsFalse(wrapper.IsDirty, "Wrapper should initially not be dirty.");
            Assert.IsFalse(wrapper.Parameters[0].IsDirty, "Parameter should initially not be dirty.");

            // Act: Mutate Parameter
            wrapper.Parameters[0].Value = "5";

            // Assert Mutation State
            Assert.IsTrue(wrapper.Parameters[0].IsDirty, "Parameter should evaluate to dirty after modification.");
            Assert.IsTrue(wrapper.IsDirty, "Wrapper should evaluate to dirty when a parameter is dirty.");

            // Act: Revert Parameter
            wrapper.Parameters[0].Value = "1";

            // Assert Reversion State
            Assert.IsFalse(wrapper.Parameters[0].IsDirty, "Parameter should evaluate to not dirty when reverted to baseline.");
            Assert.IsFalse(wrapper.IsDirty, "Wrapper should evaluate to not dirty when all parameters are clean.");

            // Act: Mutate Name
            wrapper.Name = "RenamedDash";

            // Assert Name Mutation
            Assert.IsTrue(wrapper.IsDirty, "Wrapper should evaluate to dirty when Name is modified.");

            // Act: Revert Name
            wrapper.Name = "Dash";

            // Assert Name Reversion
            Assert.IsFalse(wrapper.IsDirty, "Wrapper should evaluate to not dirty when Name is reverted to baseline.");
        }
    }
}
