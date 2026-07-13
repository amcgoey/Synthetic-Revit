using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Autodesk.Revit.UI;
using Autodesk.Revit.DB;
using Synthetic.Modules.RevitDOM;

namespace SyntheticTests
{
    internal static class ElementIdTestExtensions
    {
        public static long GetIdValue(this ElementId id)
        {
#if REVIT2022 || REVIT2023
            return id.IntegerValue;
#else
            return id.Value;
#endif
        }
    }

    [TestFixture]
    public class ViewTranslatorTests
    {
        private UIApplication? _uiapp;

        [OneTimeSetUp]
        public void Setup(UIApplication uiapp)
        {
            _uiapp = uiapp;
        }

        [Test]
        public void ViewPlan_ExtractAndInject_RoundtripsSuccessfully()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                using (var tg = new TransactionGroup(doc, "Test ViewPlan Group"))
                {
                    tg.Start();

                    using (var t = new Transaction(doc, "Setup and Roundtrip"))
                    {
                        t.Start();

                        // 1. Create a Level
                        Level level = Level.Create(doc, 10.0);
                        Assert.IsNotNull(level);

                        // 2. Find default FloorPlan ViewFamilyType
                        var floorPlanType = new FilteredElementCollector(doc)
                            .OfClass(typeof(ViewFamilyType))
                            .Cast<ViewFamilyType>()
                            .FirstOrDefault(vt => vt.ViewFamily == ViewFamily.FloorPlan);

                        Assert.IsNotNull(floorPlanType, "FloorPlan ViewFamilyType not found in document.");

                        // 3. Create initial ViewPlan
                        ViewPlan view = ViewPlan.Create(doc, floorPlanType!.Id, level.Id);
                        view.Name = "Initial_Test_FloorPlan";

                        // 4. Extract
                        var model = view.ToModel(false);
                        Assert.IsNotNull(model);
                        Assert.AreEqual("Autodesk.Revit.DB.ViewPlan", model.Class);
                        Assert.AreEqual("Initial_Test_FloorPlan", model.Name);
                        Assert.IsNotNull(model.LevelId);
                        Assert.AreEqual(level.Id.GetIdValue(), model.LevelId!.Id);
                        Assert.IsNotNull(model.ViewFamilyTypeId);
                        Assert.AreEqual(floorPlanType.Id.GetIdValue(), model.ViewFamilyTypeId!.Id);

                        // 5. Modify model for new creation
                        model.Name = "Duplicated_Test_FloorPlan";
                        model.UniqueId = null;
                        model.Id = 0;

                        // 6. Inject as new
                        var translator = new ViewTranslator(new RevitIdentityService());
                        var newView = translator.InjectSpecifics(model, null, doc) as ViewPlan;

                        Assert.IsNotNull(newView);
                        Assert.AreEqual("Duplicated_Test_FloorPlan", newView!.Name);
                        Assert.AreEqual(level.Id.GetIdValue(), newView.GenLevel.Id.GetIdValue());
                        Assert.AreEqual(floorPlanType.Id.GetIdValue(), newView.GetTypeId().GetIdValue());

                        t.Commit();
                    }

                    tg.Assimilate();
                }
            }
            finally
            {
                doc.Close(false);
            }
        }

        [Test]
        public void ViewDrafting_ExtractAndInject_RoundtripsSuccessfully()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                using (var tg = new TransactionGroup(doc, "Test ViewDrafting Group"))
                {
                    tg.Start();

                    using (var t = new Transaction(doc, "Setup and Roundtrip"))
                    {
                        t.Start();

                        // 1. Find default Drafting ViewFamilyType
                        var draftingType = new FilteredElementCollector(doc)
                            .OfClass(typeof(ViewFamilyType))
                            .Cast<ViewFamilyType>()
                            .FirstOrDefault(vt => vt.ViewFamily == ViewFamily.Drafting);

                        Assert.IsNotNull(draftingType, "Drafting ViewFamilyType not found in document.");

                        // 2. Create initial ViewDrafting
                        ViewDrafting view = ViewDrafting.Create(doc, draftingType!.Id);
                        view.Name = "Initial_Test_DraftingView";

                        // 3. Extract
                        var model = view.ToModel(false);
                        Assert.IsNotNull(model);
                        Assert.AreEqual("Autodesk.Revit.DB.ViewDrafting", model.Class);
                        Assert.AreEqual("Initial_Test_DraftingView", model.Name);
                        Assert.IsNull(model.LevelId);
                        Assert.IsNotNull(model.ViewFamilyTypeId);
                        Assert.AreEqual(draftingType.Id.GetIdValue(), model.ViewFamilyTypeId!.Id);

                        // 4. Modify model for new creation
                        model.Name = "Duplicated_Test_DraftingView";
                        model.UniqueId = null;
                        model.Id = 0;

                        // 5. Inject as new
                        var translator = new ViewTranslator(new RevitIdentityService());
                        var newView = translator.InjectSpecifics(model, null, doc) as ViewDrafting;

                        Assert.IsNotNull(newView);
                        Assert.AreEqual("Duplicated_Test_DraftingView", newView!.Name);
                        Assert.AreEqual(draftingType.Id.GetIdValue(), newView.GetTypeId().GetIdValue());

                        t.Commit();
                    }

                    tg.Assimilate();
                }
            }
            finally
            {
                doc.Close(false);
            }
        }

        [Test]
        public void ViewPlan_MissingLevel_ThrowsHardAbort()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                using (var t = new Transaction(doc, "Inject Invalid ViewPlan"))
                {
                    t.Start();

                    // Find floor plan type
                    var floorPlanType = new FilteredElementCollector(doc)
                        .OfClass(typeof(ViewFamilyType))
                        .Cast<ViewFamilyType>()
                        .FirstOrDefault(vt => vt.ViewFamily == ViewFamily.FloorPlan);

                    Assert.IsNotNull(floorPlanType);

                    var model = new ViewModel
                    {
                        Class = "Autodesk.Revit.DB.ViewPlan",
                        Name = "Invalid_FloorPlan_NoLevel",
                        ViewFamilyTypeId = floorPlanType!.Id.ToModel(doc, false),
                        LevelId = null // missing level
                    };

                    var translator = new ViewTranslator(new RevitIdentityService());
                    
                    // Assert - should throw InvalidOperationException
                    Assert.Throws<InvalidOperationException>(() =>
                    {
                        translator.InjectSpecifics(model, null, doc);
                    });

                    t.RollBack();
                }
            }
            finally
            {
                doc.Close(false);
            }
        }

        [Test]
        public void ViewPlan_MissingViewFamilyType_GracefulDegradation()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                using (var t = new Transaction(doc, "Inject ViewPlan with Missing Type"))
                {
                    t.Start();

                    // Create level
                    Level level = Level.Create(doc, 5.0);
                    Assert.IsNotNull(level);

                    var model = new ViewModel
                    {
                        Class = "Autodesk.Revit.DB.ViewPlan",
                        Name = "Degraded_FloorPlan",
                        LevelId = level.Id.ToModel(doc, false),
                        ViewFamilyTypeId = null // missing type
                    };

                    SerializationResultModel.ClearWarnings();

                    var translator = new ViewTranslator(new RevitIdentityService());
                    var view = translator.InjectSpecifics(model, null, doc) as ViewPlan;

                    // Assert
                    Assert.IsNotNull(view);
                    Assert.AreEqual("Degraded_FloorPlan", view!.Name);
                    Assert.AreEqual(level.Id.GetIdValue(), view.GenLevel.Id.GetIdValue());
                    
                    // Assert warning logged
                    Assert.IsNotEmpty(SerializationResultModel.CurrentThreadWarnings);
                    bool hasWarning = SerializationResultModel.CurrentThreadWarnings.Any(w => w.Contains("missing or unresolved") && w.Contains("Degraded_FloorPlan"));
                    Assert.IsTrue(hasWarning, "Warning should be logged about missing ViewFamilyType resolution.");

                    t.RollBack();
                }
            }
            finally
            {
                doc.Close(false);
            }
        }

        [Test]
        public void ViewSection_ExtractAndInject_RoundtripsSuccessfully()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                using (var tg = new TransactionGroup(doc, "Test ViewSection Group"))
                {
                    tg.Start();

                    using (var t = new Transaction(doc, "Setup and Roundtrip"))
                    {
                        t.Start();

                        // 1. Find default Section ViewFamilyType
                        var sectionType = new FilteredElementCollector(doc)
                            .OfClass(typeof(ViewFamilyType))
                            .Cast<ViewFamilyType>()
                            .FirstOrDefault(vt => vt.ViewFamily == ViewFamily.Section);

                        Assert.IsNotNull(sectionType, "Section ViewFamilyType not found in document.");

                        // 2. Define Section Box
                        BoundingBoxXYZ sectionBox = new BoundingBoxXYZ();
                        sectionBox.Min = new XYZ(-10, -10, -10);
                        sectionBox.Max = new XYZ(10, 10, 10);

                        // 3. Create initial ViewSection
                        ViewSection view = ViewSection.CreateSection(doc, sectionType!.Id, sectionBox);
                        view.Name = "Initial_Test_Section";

                        // 4. Extract
                        var model = view.ToModel(false);
                        Assert.IsNotNull(model);
                        Assert.AreEqual("Autodesk.Revit.DB.ViewSection", model.Class);
                        Assert.AreEqual("Initial_Test_Section", model.Name);
                        Assert.IsNotNull(model.CropBox);
                        Assert.IsNotNull(model.ViewFamilyTypeId);
                        Assert.AreEqual(sectionType.Id.GetIdValue(), model.ViewFamilyTypeId!.Id);

                        // 5. Modify model for new creation
                        model.Name = "Duplicated_Test_Section";
                        model.UniqueId = null;
                        model.Id = 0;

                        // 6. Inject as new
                        var translator = new ViewTranslator(new RevitIdentityService());
                        var newView = translator.InjectSpecifics(model, null, doc) as ViewSection;

                        Assert.IsNotNull(newView);
                        Assert.AreEqual("Duplicated_Test_Section", newView!.Name);
                        Assert.AreEqual(sectionType.Id.GetIdValue(), newView.GetTypeId().GetIdValue());

                        t.Commit();
                    }

                    tg.Assimilate();
                }
            }
            finally
            {
                doc.Close(false);
            }
        }

        [Test]
        public void ViewElevation_ExtractAndInject_RoundtripsSuccessfully()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                using (var tg = new TransactionGroup(doc, "Test ViewElevation Group"))
                {
                    tg.Start();

                    using (var t = new Transaction(doc, "Setup and Roundtrip"))
                    {
                        t.Start();

                        // 1. Create a Level & Plan View (needed to host Elevation marker)
                        Level level = Level.Create(doc, 0.0);
                        var floorPlanType = new FilteredElementCollector(doc)
                            .OfClass(typeof(ViewFamilyType))
                            .Cast<ViewFamilyType>()
                            .FirstOrDefault(vt => vt.ViewFamily == ViewFamily.FloorPlan);
                        ViewPlan planView = ViewPlan.Create(doc, floorPlanType!.Id, level.Id);

                        // 2. Find default Elevation ViewFamilyType
                        var elevationType = new FilteredElementCollector(doc)
                            .OfClass(typeof(ViewFamilyType))
                            .Cast<ViewFamilyType>()
                            .FirstOrDefault(vt => vt.ViewFamily == ViewFamily.Elevation);

                        Assert.IsNotNull(elevationType, "Elevation ViewFamilyType not found in document.");

                        // 3. Create Elevation View
                        ElevationMarker marker = ElevationMarker.CreateElevationMarker(doc, elevationType!.Id, XYZ.Zero, 100);
                        ViewSection view = marker.CreateElevation(doc, planView.Id, 0);
                        view.Name = "Initial_Test_Elevation";

                        // 4. Extract
                        var model = view.ToModel(false);
                        Assert.IsNotNull(model);
                        Assert.AreEqual("Autodesk.Revit.DB.ViewSection", model.Class);
                        Assert.AreEqual("Initial_Test_Elevation", model.Name);
                        Assert.IsNotNull(model.CropBox);
                        Assert.IsNotNull(model.ViewFamilyTypeId);
                        Assert.AreEqual(elevationType.Id.GetIdValue(), model.ViewFamilyTypeId!.Id);

                        // 5. Modify model for new creation
                        model.Name = "Duplicated_Test_Elevation";
                        model.UniqueId = null;
                        model.Id = 0;

                        // 6. Inject as new
                        var translator = new ViewTranslator(new RevitIdentityService());
                        var newView = translator.InjectSpecifics(model, null, doc) as ViewSection;

                        Assert.IsNotNull(newView);
                        Assert.AreEqual("Duplicated_Test_Elevation", newView!.Name);
                        Assert.AreEqual(elevationType.Id.GetIdValue(), newView.GetTypeId().GetIdValue());

                        t.Commit();
                    }

                    tg.Assimilate();
                }
            }
            finally
            {
                doc.Close(false);
            }
        }

        [Test]
        public void ViewSection_ScopeBoxFallback_WarningLogged()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                using (var t = new Transaction(doc, "Inject ViewSection with Missing Scope Box"))
                {
                    t.Start();

                    // Find section type
                    var sectionType = new FilteredElementCollector(doc)
                        .OfClass(typeof(ViewFamilyType))
                        .Cast<ViewFamilyType>()
                        .FirstOrDefault(vt => vt.ViewFamily == ViewFamily.Section);

                    var model = new ViewModel
                    {
                        Class = "Autodesk.Revit.DB.ViewSection",
                        Name = "ScopeBox_Fallback_Section",
                        ViewFamilyTypeId = sectionType!.Id.ToModel(doc, false),
                        ScopeBoxId = new ElementIdModel
                        {
                            Id = 999999, // fictional
                            Name = "FictionalScopeBox",
                            Class = "Autodesk.Revit.DB.ScopeBox"
                        }
                    };

                    SerializationResultModel.ClearWarnings();

                    var translator = new ViewTranslator(new RevitIdentityService());
                    var view = translator.InjectSpecifics(model, null, doc) as ViewSection;

                    // Assert creation succeeded despite missing Scope Box
                    Assert.IsNotNull(view);
                    Assert.AreEqual("ScopeBox_Fallback_Section", view!.Name);

                    // Assert warning was logged about missing scope box
                    Assert.IsNotEmpty(SerializationResultModel.CurrentThreadWarnings);
                    bool hasWarning = SerializationResultModel.CurrentThreadWarnings.Any(w => w.Contains("Scope Box") && w.Contains("missing or unresolved"));
                    Assert.IsTrue(hasWarning, "Warning should be logged about Scope Box resolution failure.");

                    t.RollBack();
                }
            }
            finally
            {
                doc.Close(false);
            }
        }

        [Test]
        public void ViewTemplate_Duplication_Strategy_SuccessfullyCreatesTemplate()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                using (var tg = new TransactionGroup(doc, "Test ViewTemplate Group"))
                {
                    tg.Start();

                    using (var t = new Transaction(doc, "Setup and Create Template"))
                    {
                        t.Start();

                        // 1. Find default FloorPlan ViewFamilyType
                        var floorPlanType = new FilteredElementCollector(doc)
                            .OfClass(typeof(ViewFamilyType))
                            .Cast<ViewFamilyType>()
                            .FirstOrDefault(vt => vt.ViewFamily == ViewFamily.FloorPlan);

                        // 2. Create a ViewModel representing a Floor Plan View Template
                        var model = new ViewModel
                        {
                            Class = "Autodesk.Revit.DB.ViewPlan",
                            Name = "My_FloorPlan_ViewTemplate",
                            IsTemplate = true,
                            ViewFamilyTypeId = floorPlanType!.Id.ToModel(doc, true)
                        };

                        // 3. Inject
                        var translator = new ViewTranslator(new RevitIdentityService());
                        var resultView = translator.InjectSpecifics(model, null, doc);

                        // 4. Assert
                        Assert.IsNotNull(resultView);
                        Assert.AreEqual("My_FloorPlan_ViewTemplate", resultView!.Name);
                        Assert.IsTrue(resultView.IsTemplate, "The created view must natively be a template.");

                        t.Commit();
                    }

                    tg.Assimilate();
                }
            }
            finally
            {
                doc.Close(false);
            }
        }

        [Test]
        public void NonGraphicalView_Serialization_ExtractsWithoutExceptions()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                using (var tg = new TransactionGroup(doc, "Test NonGraphical View Group"))
                {
                    tg.Start();

                    using (var t = new Transaction(doc, "Create NonGraphical Views"))
                    {
                        t.Start();

                        // 1. Create a ViewSheet
                        ViewSheet sheet = ViewSheet.Create(doc, ElementId.InvalidElementId);
                        sheet.Name = "Test Sheet";
                        
                        // 2. Create a ViewSchedule
                        var roomCategoryId = new ElementId((int)BuiltInCategory.OST_Rooms);
                        ViewSchedule schedule = ViewSchedule.CreateSchedule(doc, roomCategoryId);
                        schedule.Name = "Test Schedule";

                        var translator = new ViewTranslator(new RevitIdentityService());

                        // 3. Extract ViewSheet and verify zero exceptions
                        var sheetModel = new ViewModel();
                        Assert.DoesNotThrow(() => translator.ExtractSpecifics(sheet, sheetModel, doc));
                        
                        Assert.IsNull(sheetModel.DetailLevel);
                        Assert.IsNull(sheetModel.Scale);

                        // 4. Extract ViewSchedule and verify zero exceptions
                        var scheduleModel = new ViewModel();
                        Assert.DoesNotThrow(() => translator.ExtractSpecifics(schedule, scheduleModel, doc));

                        Assert.IsNull(scheduleModel.DetailLevel);
                        Assert.IsNull(scheduleModel.Scale);

                        t.Commit();
                    }

                    tg.Assimilate();
                }
            }
            finally
            {
                doc.Close(false);
            }
        }

        [Test]
        public void ViewPlan_Translation_RoundTripsSuccessfully()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                using (var tg = new TransactionGroup(doc, "Test ViewPlan Group"))
                {
                    tg.Start();

                    using (var t = new Transaction(doc, "Setup and Create ViewPlan"))
                    {
                        t.Start();

                        // 1. Create a Level
                        Level level = Level.Create(doc, 10.0);

                        // 2. Find default FloorPlan ViewFamilyType
                        var floorPlanType = new FilteredElementCollector(doc)
                            .OfClass(typeof(ViewFamilyType))
                            .Cast<ViewFamilyType>()
                            .FirstOrDefault(vt => vt.ViewFamily == ViewFamily.FloorPlan);

                        Assert.IsNotNull(floorPlanType, "FloorPlan ViewFamilyType not found.");

                        // 3. Create ViewPlan
                        ViewPlan view = ViewPlan.Create(doc, floorPlanType!.Id, level.Id);
                        view.Name = "Test_Polymorphic_FloorPlan";

                        // 4. Extract (Verify it is a ViewPlanModel polymorphically)
                        var extractedModel = view.ToModel(false);
                        Assert.IsNotNull(extractedModel);
                        Assert.IsInstanceOf<ViewPlanModel>(extractedModel, "Extracted model must be of type ViewPlanModel.");

                        var planModel = (ViewPlanModel)extractedModel;
                        Assert.IsNotNull(planModel.ViewRange, "ViewRange should not be null on extracted ViewPlanModel.");

                        // 5. Modify plan model
                        planModel.Name = "Injected_Polymorphic_FloorPlan";
                        planModel.UniqueId = null;
                        planModel.Id = 0;

                        // 6. Inject
                        var translator = new ViewPlanTranslator(new RevitIdentityService());
                        var injectedView = translator.InjectSpecifics(planModel, null, doc);

                        Assert.IsNotNull(injectedView);
                        Assert.AreEqual("Injected_Polymorphic_FloorPlan", injectedView.Name);
                        Assert.AreEqual(level.Id.GetIdValue(), injectedView.GenLevel.Id.GetIdValue());

                        t.Commit();
                    }

                    tg.Assimilate();
                }
            }
            finally
            {
                doc.Close(false);
            }
        }

        [Test]
        public void NonGraphicalView_Translation_PolymorphicallyRoundTripsSuccessfully()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                using (var tg = new TransactionGroup(doc, "Test NonGraphical Polymorphic Group"))
                {
                    tg.Start();

                    using (var t = new Transaction(doc, "Create Sheets and Schedules"))
                    {
                        t.Start();

                        // 1. Create native ViewSheet and ViewSchedule
                        ViewSheet sheet = ViewSheet.Create(doc, ElementId.InvalidElementId);
                        sheet.Name = "Polymorphic_Test_Sheet";

                        var roomCategoryId = new ElementId((int)BuiltInCategory.OST_Rooms);
                        ViewSchedule schedule = ViewSchedule.CreateSchedule(doc, roomCategoryId);
                        schedule.Name = "Polymorphic_Test_Schedule";

                        // 2. Extract polymorphically
                        var sheetModel = sheet.ToModel(false);
                        Assert.IsNotNull(sheetModel);
                        Assert.IsInstanceOf<ViewSheetModel>(sheetModel, "Extracted sheet must be ViewSheetModel.");

                        var scheduleModel = schedule.ToModel(false);
                        Assert.IsNotNull(scheduleModel);
                        Assert.IsInstanceOf<ViewScheduleModel>(scheduleModel, "Extracted schedule must be ViewScheduleModel.");

                        // 3. Serialize to JSON and check for graphical properties absence
                        var sheetJson = Synthetic.Infrastructure.Serialization.Json.Encode(sheetModel);
                        Assert.IsFalse(sheetJson.Contains("\"Scale\""));
                        Assert.IsFalse(sheetJson.Contains("\"DisplayStyle\""));

                        var scheduleJson = Synthetic.Infrastructure.Serialization.Json.Encode(scheduleModel);
                        Assert.IsFalse(scheduleJson.Contains("\"Scale\""));
                        Assert.IsFalse(scheduleJson.Contains("\"DisplayStyle\""));

                        // 4. Modify models
                        sheetModel.Name = "Injected_Polymorphic_Sheet";
                        sheetModel.UniqueId = null;
                        sheetModel.Id = 0;

                        scheduleModel.Name = "Injected_Polymorphic_Schedule";
                        scheduleModel.UniqueId = null;
                        scheduleModel.Id = 0;

                        // 5. Inject polymorphically
                        var sheetTranslator = new ViewSheetTranslator(new RevitIdentityService());
                        var injectedSheet = sheetTranslator.InjectSpecifics((ViewSheetModel)sheetModel, null, doc);
                        Assert.IsNotNull(injectedSheet);
                        Assert.AreEqual("Injected_Polymorphic_Sheet", injectedSheet.Name);

                        var scheduleTranslator = new ViewScheduleTranslator(new RevitIdentityService());
                        var injectedSchedule = scheduleTranslator.InjectSpecifics((ViewScheduleModel)scheduleModel, null, doc);
                        Assert.IsNotNull(injectedSchedule);
                        Assert.AreEqual("Injected_Polymorphic_Schedule", injectedSchedule.Name);

                        t.Commit();
                    }

                    tg.Assimilate();
                }
            }
            finally
            {
                doc.Close(false);
            }
        }
    }
}
