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
    public class ParameterModelTests
    {
        private UIApplication? _uiapp;

        [OneTimeSetUp]
        public void Setup(UIApplication uiapp)
        {
            _uiapp = uiapp;
        }

        [Test]
        public void ParameterModelHydration_StringStorageType_HydratesCorrectly()
        {
            // Arrange
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Assert.IsNotNull(app, "Revit Application should not be null.");

            Document doc = app.NewProjectDocument(UnitSystem.Metric);
            Assert.IsNotNull(doc, "New project document should be created.");

            try
            {
                Parameter? stringParam = FindParameterOfStorageType(doc, StorageType.String);
                Assert.IsNotNull(stringParam, "A parameter with String storage type should exist in the default document.");

                // Act
                var model = stringParam!.ToModel(doc, false);

                // Assert
                Assert.IsNotNull(model, "ParameterModel should be successfully instantiated.");
                Assert.AreEqual(stringParam!.Definition.Name, model.Name, "Parameter name should match.");
                Assert.AreEqual("String", model.StorageType, "StorageType should be 'String'.");
                Assert.AreEqual(stringParam.AsString(), model.Value, "Value should match string parameter AsString().");
                Assert.AreEqual(stringParam.IsReadOnly, model.IsReadOnly, "IsReadOnly flag should match.");
            }
            finally
            {
                doc.Close(false);
            }
        }

        [Test]
        public void ParameterModelHydration_IntegerStorageType_HydratesCorrectly()
        {
            // Arrange
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Assert.IsNotNull(app, "Revit Application should not be null.");

            Document doc = app.NewProjectDocument(UnitSystem.Metric);
            Assert.IsNotNull(doc, "New project document should be created.");

            try
            {
                Parameter? intParam = FindParameterOfStorageType(doc, StorageType.Integer);
                Assert.IsNotNull(intParam, "A parameter with Integer storage type should exist in the default document.");

                // Act
                var model = intParam!.ToModel(doc, false);

                // Assert
                Assert.IsNotNull(model, "ParameterModel should be successfully instantiated.");
                Assert.AreEqual(intParam!.Definition.Name, model.Name, "Parameter name should match.");
                Assert.AreEqual("Integer", model.StorageType, "StorageType should be 'Integer'.");
                Assert.AreEqual(intParam.AsInteger().ToString(), model.Value, "Value should match stringified Integer value.");
                Assert.AreEqual(intParam.IsReadOnly, model.IsReadOnly, "IsReadOnly flag should match.");
            }
            finally
            {
                doc.Close(false);
            }
        }

        [Test]
        public void ParameterModelHydration_DoubleStorageType_HydratesCorrectly()
        {
            // Arrange
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Assert.IsNotNull(app, "Revit Application should not be null.");

            Document doc = app.NewProjectDocument(UnitSystem.Metric);
            Assert.IsNotNull(doc, "New project document should be created.");

            try
            {
                Parameter? doubleParam = FindParameterOfStorageType(doc, StorageType.Double);
                Assert.IsNotNull(doubleParam, "A parameter with Double storage type should exist in the default document.");

                // Act
                var model = doubleParam!.ToModel(doc, false);

                // Assert
                Assert.IsNotNull(model, "ParameterModel should be successfully instantiated.");
                Assert.AreEqual(doubleParam!.Definition.Name, model.Name, "Parameter name should match.");
                Assert.AreEqual("Double", model.StorageType, "StorageType should be 'Double'.");
                Assert.AreEqual(doubleParam.AsDouble().ToString(), model.Value, "Value should match stringified Double value.");
                Assert.AreEqual(doubleParam.IsReadOnly, model.IsReadOnly, "IsReadOnly flag should match.");
            }
            finally
            {
                doc.Close(false);
            }
        }

        [Test]
        public void ParameterModelHydration_ElementIdStorageType_HydratesCorrectly()
        {
            // Arrange
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Assert.IsNotNull(app, "Revit Application should not be null.");

            Document doc = app.NewProjectDocument(UnitSystem.Metric);
            Assert.IsNotNull(doc, "New project document should be created.");

            try
            {
                Parameter? elemIdParam = FindParameterOfStorageType(doc, StorageType.ElementId);
                Assert.IsNotNull(elemIdParam, "A parameter with ElementId storage type should exist in the default document.");

                // Act
                var model = elemIdParam!.ToModel(doc, false);

                // Assert
                Assert.IsNotNull(model, "ParameterModel should be successfully instantiated.");
                Assert.AreEqual(elemIdParam!.Definition.Name, model.Name, "Parameter name should match.");
                Assert.AreEqual("ElementId", model.StorageType, "StorageType should be 'ElementId'.");
                Assert.IsNotNull(model.ValueElemId, "ValueElemId model should be populated.");
                
                long expectedId;
                ElementId liveId = elemIdParam.AsElementId();
                var valueProp = liveId.GetType().GetProperty("Value");
                if (valueProp != null)
                {
                    expectedId = (long)valueProp.GetValue(liveId)!;
                }
                else
                {
                    var intValueProp = liveId.GetType().GetProperty("IntegerValue")!;
                    expectedId = Convert.ToInt64(intValueProp.GetValue(liveId));
                }
                Assert.AreEqual(expectedId, model.ValueElemId!.Id, "Deserialized ValueElemId.Id should match the native parameter's ElementId value.");
                Assert.AreEqual(elemIdParam.IsReadOnly, model.IsReadOnly, "IsReadOnly flag should match.");
            }
            finally
            {
                doc.Close(false);
            }
        }

        #region Helper Methods

        private Parameter? FindParameterOfStorageType(Document doc, StorageType storageType)
        {
            // Collect project information, views, and levels as safe sources for built-in parameters
            var elements = new List<Element> { doc.ProjectInformation };
            elements.AddRange(new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<Element>());
            elements.AddRange(new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Element>());

            foreach (var elem in elements)
            {
                foreach (Parameter param in elem.Parameters)
                {
                    if (param.StorageType == storageType)
                    {
                        return param;
                    }
                }
            }
            return null;
        }

        #endregion
    }
}
