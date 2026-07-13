using System;
using NUnit.Framework;
using Autodesk.Revit.DB;
using Synthetic.Modules.RevitDOM;

namespace SyntheticTests.Modules.RevitDOM
{
    [TestFixture]
    public class FakeIdentityServiceTests
    {
        [Test]
        public void ToModel_WhenMapped_ReturnsMappedModel()
        {
            // Arrange
            var service = new FakeIdentityService();
#if REVIT2022 || REVIT2023
            var id = new ElementId(555);
#else
            var id = new ElementId(555L);
#endif
            var expectedModel = new ElementIdModel
            {
                Id = 555,
                Name = "SpecialElement",
                Class = "Autodesk.Revit.DB.Wall",
                UniqueId = "special-uid-123"
            };
            service.SetupMapping(id, expectedModel);

            // Act
            var result = service.ToModel(id, null!);

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(expectedModel.Id, result.Id);
            Assert.AreEqual(expectedModel.Name, result.Name);
            Assert.AreEqual(expectedModel.Class, result.Class);
            Assert.AreEqual(expectedModel.UniqueId, result.UniqueId);
        }

        [Test]
        public void ToModel_WhenNotMapped_ReturnsDefaultModel()
        {
            // Arrange
            var service = new FakeIdentityService();
#if REVIT2022 || REVIT2023
            var id = new ElementId(999);
#else
            var id = new ElementId(999L);
#endif

            // Act
            var result = service.ToModel(id, null!);

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(999, result.Id);
            Assert.AreEqual("FakeElement_999", result.Name);
            Assert.AreEqual("Autodesk.Revit.DB.Element", result.Class);
            Assert.AreEqual("fake-uid-999", result.UniqueId);
        }

        [Test]
        public void ResolveElementId_WhenMapped_ReturnsMappedId()
        {
            // Arrange
            var service = new FakeIdentityService();
#if REVIT2022 || REVIT2023
            var id = new ElementId(777);
#else
            var id = new ElementId(777L);
#endif
            var model = new ElementIdModel
            {
                Id = 111, // different ID in model
                UniqueId = "uid-777"
            };
            service.SetupMapping(id, new ElementIdModel { Id = 777, UniqueId = "uid-777" });

            // Act
            var result = service.ResolveElementId(model, null!);

            // Assert
#if REVIT2022 || REVIT2023
            Assert.AreEqual(777, result.IntegerValue);
#else
            Assert.AreEqual(777, result.Value);
#endif
        }

        [Test]
        public void ResolveElementId_WhenNotMapped_ReturnsModelId()
        {
            // Arrange
            var service = new FakeIdentityService();
            var model = new ElementIdModel
            {
                Id = 1234,
                UniqueId = "uid-1234"
            };

            // Act
            var result = service.ResolveElementId(model, null!);

            // Assert
#if REVIT2022 || REVIT2023
            Assert.AreEqual(1234, result.IntegerValue);
#else
            Assert.AreEqual(1234, result.Value);
#endif
        }
    }
}
