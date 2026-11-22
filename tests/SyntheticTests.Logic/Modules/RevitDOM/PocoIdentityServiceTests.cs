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

        [Test]
        public void AreSameIdentity_Overloads_WorkCorrectly()
        {
            var service = new PocoIdentityService();

            var idModel1 = new ElementIdModel { UniqueId = "UID-999", Name = "Column1", Class = "Autodesk.Revit.DB.FamilyInstance" };
            var idModel2 = new ElementIdModel { UniqueId = "uid-999", Name = "Column1_Alt", Class = "Autodesk.Revit.DB.FamilyInstance" };

            var elemModel1 = new ElementModel { ElementId = idModel1 };
            var elemModel2 = new ElementModel { ElementId = idModel2 };

            Assert.IsTrue(service.AreSameIdentity(idModel1, idModel2), "AreSameIdentity(ElementIdModel, ElementIdModel) overload should return true.");
            Assert.IsTrue(service.AreSameIdentity(elemModel1, elemModel2), "AreSameIdentity(ElementModel, ElementModel) overload should return true.");
            Assert.IsTrue(service.AreSameIdentity(idModel1, elemModel2), "AreSameIdentity(ElementIdModel, ElementModel) overload should return true.");
            Assert.IsTrue(service.AreSameIdentity(elemModel1, idModel2), "AreSameIdentity(ElementModel, ElementIdModel) overload should return true.");
        }

        [Test]
        public void AreSameIdentity_NullGuards_StandardizedBehavior()
        {
            var service = new PocoIdentityService();
            var idModel = new ElementIdModel { UniqueId = "UID-100", Class = "Autodesk.Revit.DB.Wall" };
            var elemModel = new ElementModel { ElementId = idModel };

            // Overload 1: (ElementIdModel?, ElementIdModel?)
            Assert.IsTrue(service.AreSameIdentity((ElementIdModel?)null, (ElementIdModel?)null));
            Assert.IsFalse(service.AreSameIdentity(idModel, (ElementIdModel?)null));
            Assert.IsFalse(service.AreSameIdentity((ElementIdModel?)null, idModel));

            // Overload 2: (ElementModel?, ElementModel?)
            Assert.IsTrue(service.AreSameIdentity((ElementModel?)null, (ElementModel?)null));
            Assert.IsFalse(service.AreSameIdentity(elemModel, (ElementModel?)null));
            Assert.IsFalse(service.AreSameIdentity((ElementModel?)null, elemModel));

            // Overload 3: (ElementIdModel?, ElementModel?)
            Assert.IsTrue(service.AreSameIdentity((ElementIdModel?)null, (ElementModel?)null));
            Assert.IsFalse(service.AreSameIdentity(idModel, (ElementModel?)null));
            Assert.IsFalse(service.AreSameIdentity((ElementIdModel?)null, elemModel));

            // Overload 4: (ElementModel?, ElementIdModel?)
            Assert.IsTrue(service.AreSameIdentity((ElementModel?)null, (ElementIdModel?)null));
            Assert.IsFalse(service.AreSameIdentity(elemModel, (ElementIdModel?)null));
            Assert.IsFalse(service.AreSameIdentity((ElementModel?)null, idModel));

            // ElementModel instances with null ElementId property
            var elemNullId1 = new ElementModel { ElementId = null };
            var elemNullId2 = new ElementModel { ElementId = null };

            // Same instance with null ElementId should return true via ReferenceEquals
            Assert.IsTrue(service.AreSameIdentity(elemNullId1, elemNullId1));

            // Distinct instances with null ElementId must NOT evaluate to true
            Assert.IsFalse(service.AreSameIdentity(elemNullId1, elemNullId2));
            Assert.IsFalse(service.AreSameIdentity(idModel, elemNullId1));
            Assert.IsFalse(service.AreSameIdentity(elemNullId1, idModel));
        }
    }
}

