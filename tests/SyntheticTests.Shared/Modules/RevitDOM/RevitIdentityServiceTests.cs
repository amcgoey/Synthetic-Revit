using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Autodesk.Revit.UI;
using Autodesk.Revit.DB;
using Synthetic.Modules.RevitDOM;

namespace SyntheticTests
{
    [TestFixture]
    public class RevitIdentityServiceTests
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
        public void ResolveElementId_ByBuiltInParameterIntercept_ResolvesCorrectElementId()
        {
            var model = new ElementIdModel
            {
                Name = "BuiltInParameter.WALL_USER_HEIGHT_PARAM",
                Class = "Autodesk.Revit.DB.BuiltInParameter",
                Id = -1
            };

            var service = new RevitIdentityService();

            // Act
            ElementId resolvedId = service.ResolveElementId(model, null!);

            // Assert
#if REVIT2022 || REVIT2023
            Assert.AreEqual((int)BuiltInParameter.WALL_USER_HEIGHT_PARAM, resolvedId.IntegerValue);
#else
            Assert.AreEqual((long)BuiltInParameter.WALL_USER_HEIGHT_PARAM, resolvedId.Value);
#endif
        }

        [Test]
        public void ToModel_FromElementId_PopulatesCorrectModelFields()
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

                // Act
                ElementIdModel model = service.ToModel(defaultLevel!.Id, doc);

                // Assert
                Assert.IsNotNull(model);
                Assert.AreEqual(defaultLevel.Name, model.Name);
                Assert.AreEqual(defaultLevel.GetType().FullName, model.Class);
                Assert.AreEqual(defaultLevel.UniqueId, model.UniqueId);
            }
            finally
            {
                doc.Close(false);
            }
        }
    }
}
