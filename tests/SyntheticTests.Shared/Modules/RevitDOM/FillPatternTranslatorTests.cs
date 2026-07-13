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
    public class FillPatternTranslatorTests
    {
        private UIApplication? _uiapp;

        [OneTimeSetUp]
        public void Setup(UIApplication uiapp)
        {
            _uiapp = uiapp;
        }

        [Test]
        public void ExtractSpecifics_CorrectlyPopulatesGridsList()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                // Find a default fill pattern element
                FillPatternElement? fpElem = new FilteredElementCollector(doc)
                    .OfClass(typeof(FillPatternElement))
                    .Cast<FillPatternElement>()
                    .FirstOrDefault();

                if (fpElem == null)
                {
                    Assert.Ignore("No FillPatternElement found in the document to test extraction.");
                    return;
                }

                var translator = new FillPatternTranslator();
                var model = new FillPatternElementModel();

                // Act
                translator.ExtractSpecifics(fpElem, model, doc);

                // Assert
                Assert.IsNotNull(model.Pattern);
                var pat = fpElem.GetFillPattern();
                Assert.AreEqual(pat.Name, model.Pattern!.Name);
                Assert.AreEqual(pat.Target.ToString(), model.Pattern.Target);
                Assert.AreEqual(pat.HostOrientation.ToString(), model.Pattern.HostOrientation);

                var expectedGrids = pat.GetFillGrids();
                Assert.AreEqual(expectedGrids.Count, model.Pattern.FillGrids.Count);
                for (int i = 0; i < expectedGrids.Count; i++)
                {
                    Assert.AreEqual(expectedGrids[i].Angle, model.Pattern.FillGrids[i].Angle, 1e-6);
                    Assert.AreEqual(expectedGrids[i].Offset, model.Pattern.FillGrids[i].Offset, 1e-6);
                    Assert.AreEqual(expectedGrids[i].Shift, model.Pattern.FillGrids[i].Shift, 1e-6);
                }
            }
            finally
            {
                doc.Close(false);
            }
        }

        [Test]
        public void InjectSpecifics_CreatesNewPatternElement()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                var translator = new FillPatternTranslator();
                var model = new FillPatternElementModel
                {
                    Name = "TestFillPattern_Create",
                    Pattern = new FillPatternModel
                    {
                        Name = "TestFillPattern_Create",
                        Target = "Drafting",
                        HostOrientation = "ToHost",
                        FillGrids = new List<FillGridModel>
                        {
                            new FillGridModel
                            {
                                Angle = 0.0,
                                Offset = 0.1,
                                Origin = new UVModel { U = 0.0, V = 0.0 },
                                Shift = 0.0
                            }
                        }
                    }
                };

                FillPatternElement? createdElem = null;

                using (Transaction trans = new Transaction(doc, "Test Create FillPattern"))
                {
                    trans.Start();

                    // Act
                    createdElem = translator.InjectSpecifics(model, null, doc);

                    trans.Commit();
                }

                // Assert
                Assert.IsNotNull(createdElem);
                Assert.AreEqual("TestFillPattern_Create", createdElem.Name);
                var pat = createdElem.GetFillPattern();
                Assert.AreEqual(FillPatternTarget.Drafting, pat.Target);
                Assert.AreEqual(FillPatternHostOrientation.ToHost, pat.HostOrientation);
                var grids = pat.GetFillGrids();
                Assert.AreEqual(1, grids.Count);
                Assert.AreEqual(0.0, grids[0].Angle, 1e-6);
                Assert.AreEqual(0.1, grids[0].Offset, 1e-6);
            }
            finally
            {
                doc.Close(false);
            }
        }

        [Test]
        public void InjectSpecifics_ModifiesExistingPatternElement()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                var translator = new FillPatternTranslator();
                FillPatternElement? testElem = null;

                using (Transaction trans = new Transaction(doc, "Test Modify Setup"))
                {
                    trans.Start();

                    FillPattern fp = new FillPattern("TestFillPattern_ModifyTarget", FillPatternTarget.Drafting, FillPatternHostOrientation.ToHost);
                    var grids = new List<FillGrid> { new FillGrid(0.0, 0.05) };
                    fp.SetFillGrids(grids);
                    testElem = FillPatternElement.Create(doc, fp);

                    trans.Commit();
                }

                Assert.IsNotNull(testElem);

                var model = new FillPatternElementModel
                {
                    Name = "TestFillPattern_ModifyTarget",
                    Pattern = new FillPatternModel
                    {
                        Name = "TestFillPattern_ModifyTarget",
                        Target = "Drafting",
                        HostOrientation = "ToHost",
                        FillGrids = new List<FillGridModel>
                        {
                            new FillGridModel
                            {
                                Angle = 1.57079632679, // ~90 degrees
                                Offset = 0.2,
                                Origin = new UVModel { U = 0.0, V = 0.0 },
                                Shift = 0.0
                            }
                        }
                    }
                };

                using (Transaction trans = new Transaction(doc, "Test Modify FillPattern"))
                {
                    trans.Start();

                    // Act
                    var result = translator.InjectSpecifics(model, testElem, doc);

                    trans.Commit();
                }

                // Assert
                var pat = testElem.GetFillPattern();
                var resultGrids = pat.GetFillGrids();
                Assert.AreEqual(1, resultGrids.Count);
                Assert.AreEqual(1.57079632679, resultGrids[0].Angle, 1e-5);
                Assert.AreEqual(0.2, resultGrids[0].Offset, 1e-6);
            }
            finally
            {
                doc.Close(false);
            }
        }
    }
}
