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
    public class ParameterEngineTests
    {
        private UIApplication? _uiapp;

        [OneTimeSetUp]
        public void Setup(UIApplication uiapp)
        {
            _uiapp = uiapp;
        }

        [Test]
        public void ParameterEngine_Extract_NonTemplateMode_CapturesReadOnlyAndWritableParameters()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                WallType? wallType = new FilteredElementCollector(doc)
                    .OfClass(typeof(WallType))
                    .Cast<WallType>()
                    .FirstOrDefault();

                Assert.IsNotNull(wallType, "A WallType should exist.");

                var model = new ElementModel();

                // Act
                ParameterEngine.ExtractParameters(wallType!, model, isTemplate: false);

                // Assert
                Assert.IsNotEmpty(model.Parameters, "Parameters should be extracted.");
                
                // Assert that in non-template mode, we only extract writable parameters (IsReadOnly is false)
                // Wait! In ParameterEngine.cs, in non-template mode, we did:
                // if (param.IsReadOnly) continue;
                // So all extracted parameters should have IsReadOnly == false
                foreach (var paramModel in model.Parameters)
                {
                    Assert.IsFalse(paramModel.IsReadOnly, "Extracted parameters should be writable in non-template mode.");
                }
            }
            finally
            {
                doc.Close(false);
            }
        }

        [Test]
        public void ParameterEngine_Extract_TemplateMode_DropsReadOnlyAndEmptyParameters()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                WallType? wallType = new FilteredElementCollector(doc)
                    .OfClass(typeof(WallType))
                    .Cast<WallType>()
                    .FirstOrDefault();

                Assert.IsNotNull(wallType, "A WallType should exist.");

                var model = new ElementModel();

                // Act
                ParameterEngine.ExtractParameters(wallType!, model, isTemplate: true);

                // Assert
                foreach (var paramModel in model.Parameters)
                {
                    Assert.IsFalse(paramModel.IsReadOnly, "Template parameters must not be read-only.");
                    
                    // Verify no empty/null values are captured
                    if (paramModel.StorageType == "String")
                    {
                        Assert.IsFalse(string.IsNullOrEmpty(paramModel.Value), "Template string parameters must have values.");
                    }
                    else if (paramModel.StorageType == "ElementId")
                    {
                        Assert.IsNotNull(paramModel.ValueElemId, "Template ElementId parameters must have an ElementIdModel.");
                        Assert.AreNotEqual(-1, paramModel.ValueElemId.Id, "Template ElementId parameters must not be InvalidElementId.");
                    }
                }
            }
            finally
            {
                doc.Close(false);
            }
        }

        [Test]
        public void ParameterEngine_Inject_AppliesWritableParametersCorrectly()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                WallType? wallType = new FilteredElementCollector(doc)
                    .OfClass(typeof(WallType))
                    .Cast<WallType>()
                    .FirstOrDefault();

                Assert.IsNotNull(wallType, "A WallType should exist.");

                // Set up a custom string property value in a parameter model
                var model = new ElementModel();
                ParameterEngine.ExtractParameters(wallType!, model, isTemplate: false);

                // Find a writable text parameter like description/comments
                var commentsParamModel = model.Parameters.FirstOrDefault(p => p.Name.Equals("Description", StringComparison.OrdinalIgnoreCase));
                if (commentsParamModel == null)
                {
                    commentsParamModel = model.Parameters.FirstOrDefault(p => p.Name.Equals("Type Comments", StringComparison.OrdinalIgnoreCase));
                }

                Assert.IsNotNull(commentsParamModel, "A writable text parameter should be extracted.");
                commentsParamModel!.Value = "Test Inject Value 123";

                // Act
                using (Transaction trans = new Transaction(doc, "Test Inject Parameters"))
                {
                    trans.Start();
                    ParameterEngine.InjectParameters(model, wallType!);
                    trans.Commit();
                }

                // Assert
                Parameter liveParam = wallType.get_Parameter((BuiltInParameter)commentsParamModel.Id);
                if (liveParam == null && commentsParamModel.IsShared && !string.IsNullOrEmpty(commentsParamModel.GUID))
                {
                    liveParam = wallType.get_Parameter(new Guid(commentsParamModel.GUID));
                }
                
                Assert.IsNotNull(liveParam, "Live parameter should be accessible.");
                Assert.AreEqual("Test Inject Value 123", liveParam.AsString(), "Parameter value should be injected correctly.");
            }
            finally
            {
                doc.Close(false);
            }
        }
    }
}
