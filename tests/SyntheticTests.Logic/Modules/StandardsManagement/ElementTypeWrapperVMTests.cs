using System;
using System.Collections.Generic;
using NUnit.Framework;
using Synthetic.Modules.RevitDOM;
using Synthetic.Modules.StandardsManagement.ViewModels;

namespace SyntheticTests.Modules.StandardsManagement
{
    [TestFixture]
    public class ElementTypeWrapperVMTests
    {
        [Test]
        public void IsDirty_ShouldBeTrue_WhenParameterValueAltered()
        {
            // Arrange
            var paramModel = new ParameterModel("LineWidth", "1", null, "Integer", 12345, null, false, false);
            var elementModel = new ElementModel
            {
                Class = "Autodesk.Revit.DB.LinePatternElement",
                Name = "Dash",
                Parameters = new List<ParameterModel> { paramModel }
            };

            var wrapperVM = new ElementTypeWrapperVM(elementModel);
            Assert.IsFalse(wrapperVM.IsDirty, "Initially the wrapper should not be dirty.");

            // Act
            wrapperVM.Parameters[0].Value = "2";

            // Assert
            Assert.IsTrue(wrapperVM.Parameters[0].IsDirty, "Parameter should be marked dirty.");
            Assert.IsTrue(wrapperVM.IsDirty, "ElementTypeWrapperVM should be dirty when a parameter is dirty.");
        }

        [Test]
        public void IsDirty_ShouldBeTrue_WhenNameAltered()
        {
            // Arrange
            var elementModel = new ElementModel
            {
                Class = "Autodesk.Revit.DB.LinePatternElement",
                Name = "Dash",
                Parameters = new List<ParameterModel>()
            };

            var wrapperVM = new ElementTypeWrapperVM(elementModel);
            Assert.IsFalse(wrapperVM.IsDirty, "Initially the wrapper should not be dirty.");

            // Act
            wrapperVM.Name = "New Dash Name";

            // Assert
            Assert.IsTrue(wrapperVM.IsDirty, "ElementTypeWrapperVM should be dirty when the name is altered.");
        }
    }
}
