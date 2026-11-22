using System;
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
    public class CategoryModelTests
    {
        private UIApplication? _uiapp;

        [OneTimeSetUp]
        public void Setup(UIApplication uiapp)
        {
            _uiapp = uiapp;
        }

        [Test]
        public void CategoryModelHydration_BuiltInWallsCategory_HydratesCorrectly()
        {
            // Arrange
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Assert.IsNotNull(app, "Revit Application should not be null.");

            Document doc = app.NewProjectDocument(UnitSystem.Metric);
            Assert.IsNotNull(doc, "New project document should be created.");

            try
            {
                Category liveCategory = Category.GetCategory(doc, BuiltInCategory.OST_Walls);
                Assert.IsNotNull(liveCategory, "Built-in OST_Walls category should exist.");

                // Act
                var model = liveCategory.ToCategoryModel(doc, false);

                // Assert
                Assert.IsNotNull(model, "CategoryModel should be successfully instantiated.");
                Assert.AreEqual(liveCategory.Name, model.Name, "Category name should match the live category.");
                Assert.AreEqual(liveCategory.IsCuttable, model.IsCuttable, "IsCuttable property should match.");
                Assert.IsNotNull(model.ElementId, "ElementId wrapper should not be null.");
                
                long expectedId;
                var valueProp = liveCategory.Id.GetType().GetProperty("Value");
                if (valueProp != null)
                {
                    expectedId = (long)valueProp.GetValue(liveCategory.Id)!;
                }
                else
                {
                    var intValueProp = liveCategory.Id.GetType().GetProperty("IntegerValue")!;
                    expectedId = Convert.ToInt64(intValueProp.GetValue(liveCategory.Id));
                }
                Assert.AreEqual(expectedId, model.ElementId.Id, "Category ID should match.");
                Assert.AreEqual("Model Categories", model.CategoryGroupLevel1, "CategoryGroupLevel1 should evaluate to Model Categories.");
            }
            finally
            {
                doc.Close(false);
            }
        }
    }
}
