using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;

namespace SyntheticTests.Logic.Modules.RevitDOM
{
    [TestFixture]
    public class PocoIdentityServiceTests
    {
        private PocoIdentityService _service;
        private List<ElementModel> _pool;

        [SetUp]
        public void SetUp()
        {
            _service = new PocoIdentityService();

            // Populate a fake pool of ElementModels representing diverse standard types
            _pool = new List<ElementModel>
            {
                new MaterialModel
                {
                    UniqueId = "material-guid-1",
                    Id = 1001,
                    Name = "Structural Concrete",
                    Class = "Autodesk.Revit.DB.Material",
                    Aliases = new List<string> { "Concrete", "Cast-in-Place Concrete" }
                },
                new MaterialModel
                {
                    UniqueId = "material-guid-2",
                    Id = 1002,
                    Name = "Default Glass",
                    Class = "Autodesk.Revit.DB.Material"
                },
                new HostObjTypeModel
                {
                    UniqueId = "wall-guid-1",
                    Id = 2001,
                    Name = "Exterior - 12\" Concrete",
                    Class = "Autodesk.Revit.DB.WallType",
                    Aliases = new List<string> { "Ext Wall 12" }
                }
            };
        }

        [Test]
        public void ResolveElement_ByUniqueId_ReturnsCorrectMatch()
        {
            var reference = new ElementIdModel { UniqueId = "material-guid-1", Class = "Autodesk.Revit.DB.Material" };
            var result = _service.ResolveElement(reference, _pool);

            Assert.IsNotNull(result);
            Assert.AreEqual("Structural Concrete", result.Name);
        }

        [Test]
        public void ResolveElement_ById_ReturnsCorrectMatch()
        {
            var reference = new ElementIdModel { Id = 1002, Class = "Autodesk.Revit.DB.Material" };
            var result = _service.ResolveElement(reference, _pool);

            Assert.IsNotNull(result);
            Assert.AreEqual("Default Glass", result.Name);
        }

        [Test]
        public void ResolveElement_ByName_ReturnsCorrectMatch()
        {
            var reference = new ElementIdModel { Name = "Exterior - 12\" Concrete", Class = "Autodesk.Revit.DB.WallType" };
            var result = _service.ResolveElement(reference, _pool);

            Assert.IsNotNull(result);
            Assert.AreEqual("wall-guid-1", result.UniqueId);
        }

        [Test]
        public void ResolveElement_ByAlias_ReturnsCorrectMatch()
        {
            var reference = new ElementIdModel
            {
                Class = "Autodesk.Revit.DB.Material",
                Aliases = new List<string> { "Cast-in-Place Concrete" }
            };
            var result = _service.ResolveElement(reference, _pool);

            Assert.IsNotNull(result);
            Assert.AreEqual("material-guid-1", result.UniqueId);
        }

        [Test]
        public void ResolveElement_TypeMismatch_ReturnsNull()
        {
            // Attempt to resolve a wall-type ID using a Material reference (type mismatch)
            var reference = new ElementIdModel { UniqueId = "wall-guid-1", Class = "Autodesk.Revit.DB.Material" };
            var result = _service.ResolveElement(reference, _pool);

            Assert.IsNull(result, "Resolution should return null due to strict class verification mismatch.");
        }

        [Test]
        public void ResolveElements_BulkResolution_ReturnsSuccessfully()
        {
            var references = new List<ElementIdModel>
            {
                new ElementIdModel { UniqueId = "material-guid-1", Class = "Autodesk.Revit.DB.Material" },
                new ElementIdModel { Id = 2001, Class = "Autodesk.Revit.DB.WallType" },
                new ElementIdModel { UniqueId = "non-existent-guid", Class = "Autodesk.Revit.DB.Material" }
            };

            var results = _service.ResolveElements(references, _pool).ToList();

            Assert.AreEqual(2, results.Count);
            Assert.IsTrue(results.Any(r => r.Name == "Structural Concrete"));
            Assert.IsTrue(results.Any(r => r.Name == "Exterior - 12\" Concrete"));
        }
    }
}
