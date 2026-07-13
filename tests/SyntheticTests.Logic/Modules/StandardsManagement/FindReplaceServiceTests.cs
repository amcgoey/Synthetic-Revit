using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Synthetic.Modules.RevitDOM;
using Synthetic.Modules.StandardsManagement.Utilities;

namespace SyntheticTests.Modules.StandardsManagement
{
    [TestFixture]
    public class FindReplaceServiceTests
    {
        private FindReplaceService _service = null!;

        [SetUp]
        public void Setup()
        {
            _service = new FindReplaceService();
        }

        [Test]
        public void Execute_NullOrEmptyFindText_DoesNotModifyAndReturnsEmpty()
        {
            // Arrange
            var elements = new List<ElementModel>
            {
                new ElementModel { Name = "OldName" }
            };

            // Act
            var result = _service.Execute(elements, "", "NewName", true, true);

            // Assert
            Assert.IsEmpty(result);
            Assert.AreEqual("OldName", elements[0].Name);
        }

        [Test]
        public void Execute_SearchElementNames_ReplacesMatchCaseInsensitively()
        {
            // Arrange
            var elements = new List<ElementModel>
            {
                new ElementModel { Name = "Wall-Type-A" },
                new ElementModel { Name = "Other-Type" }
            };

            // Act
            var result = _service.Execute(elements, "type-a", "Type-X", true, false);

            // Assert
            Assert.AreEqual(1, result.Count);
            Assert.IsTrue(result.Contains(elements[0]));
            Assert.AreEqual("Wall-Type-X", elements[0].Name);
            Assert.AreEqual("Other-Type", elements[1].Name);
        }

        [Test]
        public void Execute_SearchParameterValues_ReplacesMatchInEditableParameters()
        {
            // Arrange
            var param1 = new ParameterModel("Comments", "Legacy value", null, "String", 1, null, false, false);
            var param2 = new ParameterModel("Mark", "ReadOnly value", null, "String", 2, null, false, true); // ReadOnly
            var element = new ElementModel
            {
                Name = "Wall",
                Parameters = new List<ParameterModel> { param1, param2 }
            };

            // Act
            var result = _service.Execute(new[] { element }, "value", "val", false, true);

            // Assert
            Assert.AreEqual(1, result.Count);
            Assert.AreEqual("Legacy val", param1.Value);
            Assert.AreEqual("ReadOnly value", param2.Value);
        }

        [Test]
        public void Execute_SearchBoth_ReplacesBothNamesAndParams()
        {
            // Arrange
            var param = new ParameterModel("Comments", "Text value", null, "String", 1, null, false, false);
            var element = new ElementModel
            {
                Name = "Wall-value",
                Parameters = new List<ParameterModel> { param }
            };

            // Act
            var result = _service.Execute(new[] { element }, "value", "val", true, true);

            // Assert
            Assert.AreEqual(1, result.Count);
            Assert.AreEqual("Wall-val", element.Name);
            Assert.AreEqual("Text val", param.Value);
        }
    }
}
