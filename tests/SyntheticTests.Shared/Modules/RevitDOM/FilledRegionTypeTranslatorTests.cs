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
    public class FilledRegionTypeTranslatorTests
    {
        private UIApplication? _uiapp;

        [OneTimeSetUp]
        public void Setup(UIApplication uiapp)
        {
            _uiapp = uiapp;
        }

        [Test]
        public void FilledRegionTypeTranslator_ExtractSpecifics_PopulatesPropertiesAndPurgesParameters()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                using (var tg = new TransactionGroup(doc, "Test Group"))
                {
                    tg.Start();

                    // Find template fill pattern and color to assign
                    var fillPatternElem = new FilteredElementCollector(doc)
                        .OfClass(typeof(FillPatternElement))
                        .Cast<FillPatternElement>()
                        .FirstOrDefault(x => x.GetFillPattern().Target == FillPatternTarget.Drafting);

                    Assert.IsNotNull(fillPatternElem, "No drafting fill pattern found in document.");

                    // Find template filled region type to duplicate
                    var templateType = new FilteredElementCollector(doc)
                        .OfClass(typeof(FilledRegionType))
                        .Cast<FilledRegionType>()
                        .FirstOrDefault();

                    Assert.IsNotNull(templateType, "No FilledRegionType template found in document.");

                    FilledRegionType? testRegionType = null;
                    using (var t = new Transaction(doc, "Create test FilledRegionType"))
                    {
                        t.Start();
                        testRegionType = templateType.Duplicate("Test_Extraction_Region_Type") as FilledRegionType;
                        Assert.IsNotNull(testRegionType);

                        testRegionType!.ForegroundPatternColor = new Color(255, 0, 0); // Red
                        testRegionType.BackgroundPatternColor = new Color(0, 0, 255); // Blue
                        testRegionType.ForegroundPatternId = fillPatternElem.Id;
                        testRegionType.BackgroundPatternId = fillPatternElem.Id;

                        t.Commit();
                    }

                    // Act
                    var model = testRegionType.ToModel(false);

                    // Assert
                    Assert.AreEqual("Test_Extraction_Region_Type", model.Name);
                    
                    // Verify elevated properties
                    Assert.IsNotNull(model.ForegroundPatternColor);
                    Assert.AreEqual(255, model.ForegroundPatternColor!.Red);

                    Assert.IsNotNull(model.BackgroundPatternColor);
                    Assert.AreEqual(255, model.BackgroundPatternColor!.Blue);

                    Assert.IsNotNull(model.ForegroundPatternId);
                    Assert.AreEqual(fillPatternElem.Name, model.ForegroundPatternId!.Name);

                    Assert.IsNotNull(model.BackgroundPatternId);
                    Assert.AreEqual(fillPatternElem.Name, model.BackgroundPatternId!.Name);

                    // Verify Purge Rule
                    Assert.IsNotNull(model.Parameters);
                    var overlappingParams = model.Parameters.Where(p =>
                        p.Id == (long)BuiltInParameter.FOREGROUND_PATTERN_COLOR_PARAM ||
                        p.Id == (long)BuiltInParameter.BACKGROUND_PATTERN_COLOR_PARAM ||
                        p.Id == (long)BuiltInParameter.FOREGROUND_ANY_PATTERN_ID_PARAM ||
                        p.Id == (long)BuiltInParameter.BACKGROUND_DRAFT_PATTERN_ID_PARAM
                    ).ToList();

                    Assert.IsEmpty(overlappingParams, "Redundant built-in parameters were not purged from parameters collection.");

                    tg.RollBack();
                }
            }
            finally
            {
                doc.Close(false);
            }
        }

        [Test]
        public void FilledRegionTypeTranslator_InjectSpecifics_AppliesPropertiesCorrectly()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                using (var tg = new TransactionGroup(doc, "Test Group"))
                {
                    tg.Start();

                    // Retrieve valid fill pattern element
                    var fillPatternElem = new FilteredElementCollector(doc)
                        .OfClass(typeof(FillPatternElement))
                        .Cast<FillPatternElement>()
                        .FirstOrDefault(x => x.GetFillPattern().Target == FillPatternTarget.Drafting);

                    Assert.IsNotNull(fillPatternElem);

                    var model = new FilledRegionTypeModel
                    {
                        Name = "Test_Injection_Region_Type",
                        ForegroundPatternColor = new ColorModel(0, 255, 0), // Green
                        BackgroundPatternColor = new ColorModel(255, 255, 0), // Yellow
                        ForegroundPatternId = fillPatternElem.Id.ToModel(doc, false),
                        BackgroundPatternId = fillPatternElem.Id.ToModel(doc, false)
                    };

                    FilledRegionType? resultType = null;
                    using (var t = new Transaction(doc, "Inject FilledRegionType"))
                    {
                        t.Start();

                        var translator = new FilledRegionTypeTranslator(new RevitIdentityService());
                        resultType = translator.InjectSpecifics(model, null, doc);

                        Assert.IsNotNull(resultType);
                        t.Commit();
                    }

                    // Assert
                    Assert.AreEqual("Test_Injection_Region_Type", resultType!.Name);
                    Assert.AreEqual(0, resultType.ForegroundPatternColor.Red);
                    Assert.AreEqual(255, resultType.ForegroundPatternColor.Green);
                    Assert.AreEqual(255, resultType.BackgroundPatternColor.Red);
                    Assert.AreEqual(255, resultType.BackgroundPatternColor.Green);
                    Assert.AreEqual(fillPatternElem.Id, resultType.ForegroundPatternId);
                    Assert.AreEqual(fillPatternElem.Id, resultType.BackgroundPatternId);

                    tg.RollBack();
                }
            }
            finally
            {
                doc.Close(false);
            }
        }

        [Test]
        public void FilledRegionTypeTranslator_InjectSpecifics_MissingPattern_LogsWarningToResultModel()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                using (var tg = new TransactionGroup(doc, "Test Group"))
                {
                    tg.Start();

                    // Find template filled region type to duplicate
                    var templateType = new FilteredElementCollector(doc)
                        .OfClass(typeof(FilledRegionType))
                        .Cast<FilledRegionType>()
                        .FirstOrDefault();

                    Assert.IsNotNull(templateType);

                    // Create a model with a non-existent fill pattern reference
                    var model = new FilledRegionTypeModel
                    {
                        Name = "Test_Missing_Pattern_Region_Type",
                        ForegroundPatternId = new ElementIdModel
                        {
                            Name = "NonExistentPatternName_xyz",
                            Class = "Autodesk.Revit.DB.FillPatternElement"
                        }
                    };

                    SerializationResultModel.ClearWarnings();

                    FilledRegionType? resultType = null;
                    using (var t = new Transaction(doc, "Inject FilledRegionType with missing pattern"))
                    {
                        t.Start();

                        var translator = new FilledRegionTypeTranslator(new RevitIdentityService());
                        resultType = translator.InjectSpecifics(model, null, doc);

                        Assert.IsNotNull(resultType);
                        t.Commit();
                    }

                    // Assert: Should log a warning and fallback/skip assignment
                    Assert.AreEqual("Test_Missing_Pattern_Region_Type", resultType!.Name);
                    Assert.AreEqual(templateType.ForegroundPatternId, resultType.ForegroundPatternId);

                    var warnings = SerializationResultModel.CurrentThreadWarnings;
                    Assert.IsTrue(warnings.Any(w => w.Contains("Could not resolve Foreground Pattern: NonExistentPatternName_xyz")),
                        "Expected warning was not logged to SerializationResultModel.");

                    tg.RollBack();
                }
            }
            finally
            {
                doc.Close(false);
            }
        }
    }
}
