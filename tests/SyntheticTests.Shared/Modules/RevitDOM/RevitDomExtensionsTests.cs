using System;
using System.Linq;
using NUnit.Framework;
using Autodesk.Revit.UI;
using Autodesk.Revit.DB;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;
using SyntheticTests.Modules.RevitDOM;

namespace SyntheticTests
{
    [TestFixture]
    public class RevitDomExtensionsTests
    {
        private UIApplication? _uiapp;

        [OneTimeSetUp]
        public void Setup(UIApplication uiapp)
        {
            _uiapp = uiapp;
        }

        [Test]
        public void ToModel_Material_DecoupledIdentityService_MapsCorrectly()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                Material? material = null;
                using (Transaction trans = new Transaction(doc, "Create Material"))
                {
                    trans.Start();
                    var materialId = Material.Create(doc, "ConcreteTestMaterial");
                    material = (Material)doc.GetElement(materialId);
                    
                    // Modify some properties
                    material.Color = new Color(120, 150, 180);
                    trans.Commit();
                }

                Assert.IsNotNull(material);
                
                var fakeService = new FakeIdentityService();

                // Act
                var model = material.ToModel(doc, false, fakeService);

                // Assert
                Assert.IsNotNull(model);
                Assert.AreEqual("ConcreteTestMaterial", model.Name);
                Assert.AreEqual(120, model.Color.Red);
                Assert.AreEqual(150, model.Color.Green);
                Assert.AreEqual(180, model.Color.Blue);
            }
            finally
            {
                doc.Close(false);
            }
        }

        [Test]
        public void ToModel_PlanViewRange_MapsOffsetsAndLevelIds()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                var viewPlan = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewPlan))
                    .Cast<ViewPlan>()
                    .FirstOrDefault(v => !v.IsTemplate);

                Assert.IsNotNull(viewPlan, "A plan view should exist in the project.");
                PlanViewRange viewRange = viewPlan.GetViewRange();

                // Act
                var model = viewRange.ToModel(doc);

                // Assert
                Assert.IsNotNull(model);
                
                // Assert that the coordinates are mapped
                Assert.IsNotNull(model.TopLevelId);
                Assert.IsNotNull(model.CutPlaneLevelId);
                Assert.IsNotNull(model.BottomLevelId);
                Assert.IsNotNull(model.ViewDepthLevelId);
            }
            finally
            {
                doc.Close(false);
            }
        }
    }
}
