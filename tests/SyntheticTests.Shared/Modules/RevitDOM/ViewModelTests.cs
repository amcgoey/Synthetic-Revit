using System;
using System.Linq;
using NUnit.Framework;
using Autodesk.Revit.UI;
using Autodesk.Revit.DB;
using Synthetic.Modules.RevitDOM;

namespace SyntheticTests
{
    [TestFixture]
    public class ViewModelTests
    {
        private UIApplication? _uiapp;

        [OneTimeSetUp]
        public void Setup(UIApplication uiapp)
        {
            _uiapp = uiapp;
        }

        [Test]
        public void ViewModelHydration_ActiveView_HydratesCorrectly()
        {
            // Arrange
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Assert.IsNotNull(app, "Revit Application should not be null.");

            Document doc = app.NewProjectDocument(UnitSystem.Metric);
            Assert.IsNotNull(doc, "New project document should be created.");

            try
            {
                // Find a graphical view (like FloorPlan) that is not a template
                View? view = new FilteredElementCollector(doc)
                    .OfClass(typeof(View))
                    .Cast<View>()
                    .FirstOrDefault(v => !v.IsTemplate && (v.ViewType == ViewType.FloorPlan || v.ViewType == ViewType.Elevation || v.ViewType == ViewType.Section));

                if (view == null)
                {
                    view = new FilteredElementCollector(doc)
                        .OfClass(typeof(View))
                        .Cast<View>()
                        .FirstOrDefault(v => !v.IsTemplate);
                }

                Assert.IsNotNull(view, "A non-template view should exist in the default document.");

                // Act
                var model = view!.ToModel(false);

                // Assert
                Assert.IsNotNull(model, "ViewModel should be successfully instantiated.");
                Assert.AreEqual(view!.Scale, model.Scale, "Scale property should match.");
                Assert.AreEqual(view.IsTemplate, model.IsTemplate, "IsTemplate flag should match.");
                Assert.IsFalse(model.IsTemplate, "IsTemplate flag should be false for a live active/default view.");

                // DetailLevel Assertion
                Assert.IsNotNull(model.DetailLevel, "DetailLevel should not be null.");
                Assert.AreEqual("Autodesk.Revit.DB.ViewDetailLevel", model.DetailLevel!.Type, "DetailLevel type name should match.");
                Assert.AreEqual(view.DetailLevel.ToString(), model.DetailLevel.Value, "DetailLevel value string should match.");

                // DisplayStyle Assertion
                Assert.IsNotNull(model.DisplayStyle, "DisplayStyle should not be null.");
                Assert.AreEqual("Autodesk.Revit.DB.DisplayStyle", model.DisplayStyle.Type, "DisplayStyle type name should match.");
                Assert.AreEqual(view.DisplayStyle.ToString(), model.DisplayStyle.Value, "DisplayStyle value string should match.");

                // Parameters Assertion
                Assert.IsNotNull(model.Parameters, "Parameters collection should not be null.");
                Assert.IsNotEmpty(model.Parameters, "Parameters collection should contain items.");
                foreach (var paramModel in model.Parameters)
                {
                    Assert.IsNotNull(paramModel.Name, "Parameter name should not be null.");
                    Assert.IsNotNull(paramModel.StorageType, "Parameter StorageType should not be null.");
                }
            }
            finally
            {
                doc.Close(false);
            }
        }
    }
}
