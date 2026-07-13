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
    public class LinePatternTranslatorTests
    {
        private UIApplication? _uiapp;

        [OneTimeSetUp]
        public void Setup(UIApplication uiapp)
        {
            _uiapp = uiapp;
        }

        [Test]
        public void ExtractSpecifics_CorrectlyPopulatesSegmentList()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                // Find a default line pattern element
                LinePatternElement? lpElem = new FilteredElementCollector(doc)
                    .OfClass(typeof(LinePatternElement))
                    .Cast<LinePatternElement>()
                    .FirstOrDefault();

                if (lpElem == null)
                {
                    Assert.Ignore("No LinePatternElement found in the document to test extraction.");
                    return;
                }

                var translator = new LinePatternTranslator();
                var model = new LinePatternElementModel();

                // Act
                translator.ExtractSpecifics(lpElem, model, doc);

                // Assert
                Assert.IsNotNull(model.Segments);
                var lp = lpElem.GetLinePattern();
                var expectedSegments = lp.GetSegments();
                Assert.AreEqual(expectedSegments.Count, model.Segments.Count);
                for (int i = 0; i < expectedSegments.Count; i++)
                {
                    Assert.AreEqual(expectedSegments[i].Type.ToString(), model.Segments[i].Type);
                    Assert.AreEqual(expectedSegments[i].Length, model.Segments[i].Length, 1e-6);
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
                var translator = new LinePatternTranslator();
                var model = new LinePatternElementModel
                {
                    Name = "TestLinePattern_Create",
                    Segments = new List<LinePatternSegmentModel>
                    {
                        new LinePatternSegmentModel { Type = "Dash", Length = 0.02 },
                        new LinePatternSegmentModel { Type = "Space", Length = 0.01 }
                    }
                };

                LinePatternElement? createdElem = null;

                using (Transaction trans = new Transaction(doc, "Test Create LinePattern"))
                {
                    trans.Start();

                    // Act
                    createdElem = translator.InjectSpecifics(model, null, doc);

                    trans.Commit();
                }

                // Assert
                Assert.IsNotNull(createdElem);
                Assert.AreEqual("TestLinePattern_Create", createdElem.Name);
                var lp = createdElem.GetLinePattern();
                var segments = lp.GetSegments();
                Assert.AreEqual(2, segments.Count);
                Assert.AreEqual(LinePatternSegmentType.Dash, segments[0].Type);
                Assert.AreEqual(0.02, segments[0].Length, 1e-6);
                Assert.AreEqual(LinePatternSegmentType.Space, segments[1].Type);
                Assert.AreEqual(0.01, segments[1].Length, 1e-6);
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
                var translator = new LinePatternTranslator();
                
                LinePatternElement? testElem = null;
                
                using (Transaction trans = new Transaction(doc, "Test Modify Setup"))
                {
                    trans.Start();
                    
                    LinePattern initialLp = new LinePattern("TestLinePattern_ModifyTarget");
                    initialLp.SetSegments(new List<LinePatternSegment> 
                    {
                        new LinePatternSegment(LinePatternSegmentType.Dash, 0.01),
                        new LinePatternSegment(LinePatternSegmentType.Space, 0.01)
                    });
                    testElem = LinePatternElement.Create(doc, initialLp);
                    
                    trans.Commit();
                }

                Assert.IsNotNull(testElem);

                var model = new LinePatternElementModel
                {
                    Name = "TestLinePattern_ModifyTarget",
                    Segments = new List<LinePatternSegmentModel>
                    {
                        new LinePatternSegmentModel { Type = "Dash", Length = 0.05 },
                        new LinePatternSegmentModel { Type = "Space", Length = 0.02 }
                    }
                };

                using (Transaction trans = new Transaction(doc, "Test Modify LinePattern"))
                {
                    trans.Start();

                    // Act
                    var result = translator.InjectSpecifics(model, testElem, doc);

                    trans.Commit();
                }

                // Assert
                var lp = testElem.GetLinePattern();
                var segments = lp.GetSegments();
                Assert.AreEqual(2, segments.Count);
                Assert.AreEqual(LinePatternSegmentType.Dash, segments[0].Type);
                Assert.AreEqual(0.05, segments[0].Length, 1e-6);
                Assert.AreEqual(LinePatternSegmentType.Space, segments[1].Type);
                Assert.AreEqual(0.02, segments[1].Length, 1e-6);
            }
            finally
            {
                doc.Close(false);
            }
        }
    }
}
