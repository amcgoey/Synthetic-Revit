using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Autodesk.Revit.UI;
using Autodesk.Revit.DB;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;

namespace SyntheticTests
{
    [TestFixture]
    public class RevitIdentityServiceIntegrationTests
    {
        private UIApplication? _uiapp;

        [OneTimeSetUp]
        public void Setup(UIApplication uiapp)
        {
            _uiapp = uiapp;
        }

        [Test]
        public void ResolveElement_ByUniqueId_ResolvesCorrectElement()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                Element? defaultLevel = new FilteredElementCollector(doc)
                    .OfClass(typeof(Level))
                    .FirstOrDefault();

                Assert.IsNotNull(defaultLevel, "A default Level element should exist in the project.");

                var service = new RevitIdentityService();
                ElementIdModel model = service.ToModel(defaultLevel!.Id, doc);

                // Act
                Element resolvedElem = service.ResolveElement(model, doc);

                // Assert
                Assert.IsNotNull(resolvedElem, "ResolveElement should resolve the element.");
                Assert.AreEqual(defaultLevel.UniqueId, resolvedElem.UniqueId, "Resolved element UniqueId should match.");
            }
            finally
            {
                doc.Close(false);
            }
        }

        [Test]
        public void ResolveElement_ByStandardIdFallback_ResolvesCorrectElement()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                Element? defaultLevel = new FilteredElementCollector(doc)
                    .OfClass(typeof(Level))
                    .FirstOrDefault();

                Assert.IsNotNull(defaultLevel, "A default Level element should exist.");

                long idVal = 0;
                var valueProp = defaultLevel!.Id.GetType().GetProperty("Value");
                if (valueProp != null)
                {
                    idVal = (long)valueProp.GetValue(defaultLevel.Id)!;
                }
                else
                {
                    var integerValueProp = defaultLevel.Id.GetType().GetProperty("IntegerValue");
                    if (integerValueProp != null)
                    {
                        idVal = Convert.ToInt64(integerValueProp.GetValue(defaultLevel.Id));
                    }
                }

                var model = new ElementIdModel
                {
                    Id = idVal,
                    Class = defaultLevel.GetType().FullName ?? string.Empty,
                    Name = defaultLevel.Name,
                    UniqueId = null // Force standard ID fallback
                };

                var service = new RevitIdentityService();

                // Act
                Element resolvedElem = service.ResolveElement(model, doc);

                // Assert
                Assert.IsNotNull(resolvedElem, "ResolveElement should resolve by standard ID fallback.");
                Assert.AreEqual(defaultLevel.Id, resolvedElem.Id, "Resolved element ID should match.");
            }
            finally
            {
                doc.Close(false);
            }
        }

        [Test]
        public void ResolveElement_ByNameAndClassFallback_ResolvesCorrectElement()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                WallType? defaultWallType = new FilteredElementCollector(doc)
                    .OfClass(typeof(WallType))
                    .Cast<WallType>()
                    .FirstOrDefault();

                Assert.IsNotNull(defaultWallType, "A WallType should exist.");

                var model = new ElementIdModel
                {
                    Id = 0,
                    UniqueId = null,
                    Name = defaultWallType!.Name,
                    Class = typeof(WallType).FullName ?? string.Empty
                };

                var service = new RevitIdentityService();

                // Act
                Element resolvedElem = service.ResolveElement(model, doc);

                // Assert
                Assert.IsNotNull(resolvedElem, "ResolveElement should resolve by Name and Class fallback.");
                Assert.AreEqual(defaultWallType.Id, resolvedElem.Id, "Resolved element ID should match.");
            }
            finally
            {
                doc.Close(false);
            }
        }

        [Test]
        public void ResolveElement_ByAliasesFallback_ResolvesCorrectElement()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                WallType? defaultWallType = new FilteredElementCollector(doc)
                    .OfClass(typeof(WallType))
                    .Cast<WallType>()
                    .FirstOrDefault();

                Assert.IsNotNull(defaultWallType, "A WallType should exist.");

                var model = new ElementIdModel
                {
                    Id = 0,
                    UniqueId = null,
                    Name = "NonExistentName",
                    Class = typeof(WallType).FullName ?? string.Empty,
                    Aliases = new List<string> { "FakeAlias1", defaultWallType!.Name, "FakeAlias2" }
                };

                var service = new RevitIdentityService();

                // Act
                Element resolvedElem = service.ResolveElement(model, doc);

                // Assert
                Assert.IsNotNull(resolvedElem, "ResolveElement should resolve by Aliases fallback.");
                Assert.AreEqual(defaultWallType.Id, resolvedElem.Id, "Resolved element ID should match.");
            }
            finally
            {
                doc.Close(false);
            }
        }

        [Test]
        public void ResolveElement_TypeGuardMismatch_ReturnsNull()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                Element? defaultLevel = new FilteredElementCollector(doc)
                    .OfClass(typeof(Level))
                    .FirstOrDefault();

                Assert.IsNotNull(defaultLevel, "A default Level element should exist.");

                long idVal = 0;
                var valueProp = defaultLevel!.Id.GetType().GetProperty("Value");
                if (valueProp != null)
                {
                    idVal = (long)valueProp.GetValue(defaultLevel.Id)!;
                }
                else
                {
                    var integerValueProp = defaultLevel.Id.GetType().GetProperty("IntegerValue");
                    if (integerValueProp != null)
                    {
                        idVal = Convert.ToInt64(integerValueProp.GetValue(defaultLevel.Id));
                    }
                }

                var model = new ElementIdModel
                {
                    Id = idVal,
                    UniqueId = defaultLevel.UniqueId,
                    Class = typeof(WallType).FullName ?? string.Empty, // Intentionally wrong class
                    Name = defaultLevel.Name
                };

                var service = new RevitIdentityService();

                // Act
                Element resolvedElem = service.ResolveElement(model, doc);

                // Assert
                Assert.IsNull(resolvedElem, "ResolveElement should return null due to Type Guard mismatch.");
            }
            finally
            {
                doc.Close(false);
            }
        }

        [Test]
        public void ResolveElementId_BuiltInCategory_OST_Walls_ReturnsCorrectId()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                var model = new ElementIdModel
                {
                    Name = "BuiltInCategory.OST_Walls"
                };

                var service = new RevitIdentityService();

                // Act
                ElementId resolvedId = service.ResolveElementId(model, doc);

                // Assert
                Assert.AreNotEqual(ElementId.InvalidElementId, resolvedId);
                long expectedInt = (long)BuiltInCategory.OST_Walls;
                long resolvedInt = GetElementIdValue(resolvedId);
                Assert.AreEqual(expectedInt, resolvedInt);
            }
            finally
            {
                doc.Close(false);
            }
        }

        [Test]
        public void ResolveElementId_BuiltInParameter_ALL_MODEL_MARK_ReturnsCorrectId()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                var model = new ElementIdModel
                {
                    Name = "BuiltInParameter.ALL_MODEL_MARK"
                };

                var service = new RevitIdentityService();

                // Act
                ElementId resolvedId = service.ResolveElementId(model, doc);

                // Assert
                Assert.AreNotEqual(ElementId.InvalidElementId, resolvedId);
                long expectedInt = (long)BuiltInParameter.ALL_MODEL_MARK;
                long resolvedInt = GetElementIdValue(resolvedId);
                Assert.AreEqual(expectedInt, resolvedInt);
            }
            finally
            {
                doc.Close(false);
            }
        }

        [Test]
        public void ResolveElementId_MalformedEnum_GracefulFallback()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                Element? defaultLevel = new FilteredElementCollector(doc)
                    .OfClass(typeof(Level))
                    .FirstOrDefault();

                Assert.IsNotNull(defaultLevel);

                var model = new ElementIdModel
                {
                    Name = "BuiltInCategory.OST_Walls_Malformed",
                    UniqueId = defaultLevel!.UniqueId // fallback target
                };

                var service = new RevitIdentityService();

                // Act
                ElementId resolvedId = service.ResolveElementId(model, doc);

                // Assert
                Assert.AreEqual(defaultLevel.Id, resolvedId, "Should gracefully fall back to UniqueId fallback.");
            }
            finally
            {
                doc.Close(false);
            }
        }

        [Test]
        public void ResolveElementId_JustEnumValue_ReturnsCorrectId()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                var model = new ElementIdModel
                {
                    Name = "OST_Walls"
                };

                var service = new RevitIdentityService();

                // Act
                ElementId resolvedId = service.ResolveElementId(model, doc);

                // Assert
                Assert.AreNotEqual(ElementId.InvalidElementId, resolvedId);
                long expectedInt = (long)BuiltInCategory.OST_Walls;
                long resolvedInt = GetElementIdValue(resolvedId);
                Assert.AreEqual(expectedInt, resolvedInt);
            }
            finally
            {
                doc.Close(false);
            }
        }

        [Test]
        public void GetElementsByElementIdModels_SuccessPath_ResolvesAllElements()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                Element? defaultLevel = new FilteredElementCollector(doc)
                    .OfClass(typeof(Level))
                    .FirstOrDefault();

                Element? defaultView = new FilteredElementCollector(doc)
                    .OfClass(typeof(View))
                    .FirstOrDefault();

                Assert.IsNotNull(defaultLevel, "A default Level element should exist.");
                Assert.IsNotNull(defaultView, "A default View element should exist.");

                var service = new RevitIdentityService();
                var modelLevel = service.ToModel(defaultLevel!.Id, doc);
                var modelView = service.ToModel(defaultView!.Id, doc);

                var identifiers = new List<ElementIdModel> { modelLevel, modelView };

                // Act
                var resolved = service.GetElementsByElementIdModels(doc, identifiers).ToList();

                // Assert
                Assert.AreEqual(2, resolved.Count);
                Assert.IsTrue(resolved.Any(e => e.UniqueId == defaultLevel.UniqueId));
                Assert.IsTrue(resolved.Any(e => e.UniqueId == defaultView.UniqueId));
            }
            finally
            {
                doc.Close(false);
            }
        }

        [Test]
        public void GetElementsByElementIdModels_DegradationPath_LogsWarningAndFiltersNulls()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                Element? defaultLevel = new FilteredElementCollector(doc)
                    .OfClass(typeof(Level))
                    .FirstOrDefault();

                Assert.IsNotNull(defaultLevel, "A default Level element should exist.");

                var service = new RevitIdentityService();
                var modelLevel = service.ToModel(defaultLevel!.Id, doc);

                var modelFictitious = new ElementIdModel
                {
                    Name = "Fictitious Level",
                    Class = "Autodesk.Revit.DB.Level",
                    UniqueId = "nonexistent-guid-value-12345"
                };

                var identifiers = new List<ElementIdModel> { modelLevel, modelFictitious };

                // Clear thread warnings beforehand
                SerializationResultModel.ClearWarnings();

                // Act
                var resolved = service.GetElementsByElementIdModels(doc, identifiers).ToList();

                // Assert
                Assert.AreEqual(1, resolved.Count);
                Assert.AreEqual(defaultLevel.UniqueId, resolved[0].UniqueId);

                var warnings = SerializationResultModel.CurrentThreadWarnings;
                Assert.AreEqual(1, warnings.Count);
                Assert.AreEqual("Dependency not found: Fictitious Level (Autodesk.Revit.DB.Level)", warnings[0]);
            }
            finally
            {
                doc.Close(false);
            }
        }

        private static long GetElementIdValue(ElementId id)
        {
            var valueProp = id.GetType().GetProperty("Value");
            if (valueProp != null)
            {
                return (long)valueProp.GetValue(id)!;
            }

            var integerValueProp = id.GetType().GetProperty("IntegerValue");
            if (integerValueProp != null)
            {
                return Convert.ToInt64(integerValueProp.GetValue(id));
            }

            return 0;
        }
    }
}
