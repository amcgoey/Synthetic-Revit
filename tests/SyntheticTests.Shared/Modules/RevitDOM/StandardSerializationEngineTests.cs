using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Autodesk.Revit.UI;
using Autodesk.Revit.DB;
using System.Threading;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;
using SyntheticTests.Helpers;

namespace SyntheticTests
{
    [TestFixture]
    public class StandardSerializationEngineTests
    {
        private UIApplication? _uiapp;

        [OneTimeSetUp]
        public void Setup(UIApplication uiapp)
        {
            _uiapp = uiapp;
        }

        private class StubLevelTranslator : IModelTranslator
        {
            public bool WasInjected { get; private set; }

            public void ExtractSpecifics(object revitElement, ObjectModel model, Document doc) { }

            public object? InjectSpecifics(ObjectModel model, object? revitElement, Document doc)
            {
                WasInjected = true;
                if (revitElement is Element elem)
                {
                    elem.Name = "ModifiedByStubTranslator";
                    return elem;
                }
                return revitElement;
            }
        }

        private class UnregisteredDummyModel : ElementModel
        {
        }

        [Test]
        public void ToRevit_GracefulDegradation_CapturesFailedAndSuccessfulTransactions()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                // Find a default level to modify
                Level? defaultLevel = new FilteredElementCollector(doc)
                    .OfClass(typeof(Level))
                    .Cast<Level>()
                    .FirstOrDefault();

                Assert.IsNotNull(defaultLevel, "A default Level should exist.");

                // Create a successful model using ElementModel targeting the level
                var successModel = new ElementModel
                {
                    ElementId = defaultLevel!.Id.ToModel(doc),
                    Name = "SomeDifferentName"
                };

                // Create a failed model (e.g. UnregisteredDummyModel) targeting a level or fake element,
                // but we won't register any translator for UnregisteredDummyModel so it will fail with NotSupportedException.
                var failModel = new UnregisteredDummyModel
                {
                    Name = "FakeHost"
                };

                var engine = new StandardSerializationEngine();
                
                // Register our stub translator for ElementModel
                var stubTranslator = new StubLevelTranslator();
                engine.Dispatcher.Register<ElementModel, StubLevelTranslator>(stubTranslator, typeof(Level));

                var modelsBatch = new List<ObjectModel> { successModel, failModel };

                // Act
                IEnumerable<SerializationResultModel> results = engine.ToRevit(modelsBatch, doc);
                var resultsList = results.ToList();

                // Assert
                Assert.AreEqual(2, resultsList.Count, "Should return results for both models.");

                var successResult = resultsList.FirstOrDefault(r => r.Model == successModel);
                Assert.IsNotNull(successResult, "Success result should exist.");
                Assert.IsTrue(successResult!.Success, "The valid model should report success.");
                Assert.IsNull(successResult.ErrorMessage, "Success result should have no error message.");

                var failResult = resultsList.FirstOrDefault(r => r.Model == failModel);
                Assert.IsNotNull(failResult, "Failure result should exist.");
                Assert.IsFalse(failResult!.Success, "The invalid model should report failure.");
                Assert.IsNotNull(failResult.ErrorMessage, "Failure result should contain an error message.");
                Assert.IsTrue(failResult.ErrorMessage!.Contains("No translator registered"), "Error message should mention registration failure.");

                // Assert that the successful element was actually modified and committed
                Assert.AreEqual("ModifiedByStubTranslator", defaultLevel.Name, "The successful model modifications should be committed.");
                Assert.IsTrue(stubTranslator.WasInjected, "Translator InjectSpecifics should have run.");
            }
            finally
            {
                doc.Close(false);
            }
        }

        [Test]
        public void ToRevit_Cancellation_RollsBackAllChangesAndThrowsException()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                // Find default level
                Level? defaultLevel = new FilteredElementCollector(doc)
                    .OfClass(typeof(Level))
                    .Cast<Level>()
                    .FirstOrDefault();

                Assert.IsNotNull(defaultLevel, "A default Level should exist.");
                string originalName = defaultLevel.Name;

                // Create a valid model targeting the level
                var successModel = new ElementModel
                {
                    ElementId = defaultLevel!.Id.ToModel(doc),
                    Name = defaultLevel.Name
                };

                var engine = new StandardSerializationEngine();
                var stubTranslator = new StubLevelTranslator();
                engine.Dispatcher.Register<ElementModel, StubLevelTranslator>(stubTranslator, typeof(Level));

                // Create a cancellation token that is canceled
                var cts = new CancellationTokenSource();
                cts.Cancel();

                // Act & Assert
                Assert.Throws<OperationCanceledException>(() =>
                {
                    engine.ToRevit(new List<ObjectModel> { successModel }, doc, cancellationToken: cts.Token);
                }, "Should throw OperationCanceledException when token is cancelled.");

                // Assert that the level was NOT modified because the outer transaction group was rolled back
                Assert.AreEqual(originalName, defaultLevel.Name, "Changes must be rolled back on cancellation.");
            }
            finally
            {
                doc.Close(false);
            }
        }

        [Test]
        public void ToRevit_UnchangedElement_SkipsModificationsAndReturnsUnchanged()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                // Find a default level to target
                Level? defaultLevel = new FilteredElementCollector(doc)
                    .OfClass(typeof(Level))
                    .Cast<Level>()
                    .FirstOrDefault();

                Assert.IsNotNull(defaultLevel, "A default Level should exist.");
                string originalName = defaultLevel.Name;

                // Create a model matching the level exactly
                var unchangedModel = new ElementModel
                {
                    ElementId = defaultLevel!.Id.ToModel(doc),
                    Name = defaultLevel.Name
                };

                var engine = new StandardSerializationEngine();
                
                // Register our stub translator for ElementModel
                var stubTranslator = new StubLevelTranslator();
                engine.Dispatcher.Register<ElementModel, StubLevelTranslator>(stubTranslator, typeof(Level));

                // Act
                var resultsList = engine.ToRevit(new List<ObjectModel> { unchangedModel }, doc).ToList();

                // Assert
                Assert.AreEqual(1, resultsList.Count);
                var result = resultsList[0];
                Assert.IsTrue(result.Success);
                Assert.AreEqual("Unchanged", result.Action, "Should be flagged as Unchanged.");
                Assert.IsFalse(stubTranslator.WasInjected, "InjectSpecifics should be skipped when element is unchanged.");
                Assert.AreEqual(originalName, defaultLevel.Name, "Level name should not have changed.");
            }
            finally
            {
                doc.Close(false);
            }
        }

        [Test]
        public void ToRevit_CompositeRouting_DispatchesAllStandardTypesToRegisteredTranslatorsWithoutOverwrite()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                var engine = new StandardSerializationEngine();

                var textNote = new ElementTypeModel
                {
                    Class = "Autodesk.Revit.DB.TextNoteType",
                    Name = "TestTextNote_Dispatch"
                };
                var spotDim = new ElementTypeModel
                {
                    Class = "Autodesk.Revit.DB.SpotDimensionType",
                    Name = "TestSpotDim_Dispatch"
                };
                var gridType = new ElementTypeModel
                {
                    Class = "Autodesk.Revit.DB.GridType",
                    Name = "TestGrid_Dispatch"
                };
                var levelType = new ElementTypeModel
                {
                    Class = "Autodesk.Revit.DB.LevelType",
                    Name = "TestLevel_Dispatch"
                };
                var curtainSystem = new HostObjTypeModel
                {
                    Class = "Autodesk.Revit.DB.CurtainSystemType",
                    Name = "TestCurtainSystem_Dispatch"
                };
                var mullion = new HostObjTypeModel
                {
                    Class = "Autodesk.Revit.DB.MullionType",
                    Name = "TestMullion_Dispatch"
                };
                var fascia = new HostObjTypeModel
                {
                    Class = "Autodesk.Revit.DB.Architecture.FasciaType",
                    Name = "TestFascia_Dispatch"
                };
                var gutter = new HostObjTypeModel
                {
                    Class = "Autodesk.Revit.DB.Architecture.GutterType",
                    Name = "TestGutter_Dispatch"
                };

                // Verify translator bindings directly on Dispatcher
                var tText = engine.Dispatcher.GetTranslator(textNote);
                var tSpot = engine.Dispatcher.GetTranslator(spotDim);
                var tGrid = engine.Dispatcher.GetTranslator(gridType);
                var tLevel = engine.Dispatcher.GetTranslator(levelType);
                var tCurtain = engine.Dispatcher.GetTranslator(curtainSystem);
                var tMullion = engine.Dispatcher.GetTranslator(mullion);
                var tFascia = engine.Dispatcher.GetTranslator(fascia);
                var tGutter = engine.Dispatcher.GetTranslator(gutter);

                Assert.IsInstanceOf<TextElementTypeTranslator>(tText, "TextNoteType must resolve to TextElementTypeTranslator.");
                Assert.IsInstanceOf<SpotDimensionTypeTranslator>(tSpot, "SpotDimensionType must resolve to SpotDimensionTypeTranslator.");
                Assert.IsInstanceOf<GridTypeTranslator>(tGrid, "GridType must resolve to GridTypeTranslator.");
                Assert.IsInstanceOf<LevelTypeTranslator>(tLevel, "LevelType must resolve to LevelTypeTranslator.");
                Assert.IsInstanceOf<HostObjTypeTranslator>(tCurtain, "CurtainSystemType must resolve to HostObjTypeTranslator.");
                Assert.IsInstanceOf<HostObjTypeTranslator>(tMullion, "MullionType must resolve to HostObjTypeTranslator.");
                Assert.IsInstanceOf<HostObjTypeTranslator>(tFascia, "FasciaType must resolve to HostObjTypeTranslator.");
                Assert.IsInstanceOf<HostObjTypeTranslator>(tGutter, "GutterType must resolve to HostObjTypeTranslator.");

#if REVIT2022 || REVIT2023
                // ToposolidType does not exist in Revit 2022/2023
#else
                var toposolid = new HostObjTypeModel
                {
                    Class = "Autodesk.Revit.DB.ToposolidType",
                    Name = "TestToposolid_Dispatch"
                };
                var tTopo = engine.Dispatcher.GetTranslator(toposolid);
                Assert.IsInstanceOf<HostObjTypeTranslator>(tTopo, "ToposolidType must resolve to HostObjTypeTranslator in Revit 2024+.");
#endif

                // Act - run ToRevit on batch
                var batch = new List<ObjectModel> { textNote, spotDim, gridType, levelType };
                var results = engine.ToRevit(batch, doc).ToList();

                // Assert: None of these should fail with "No translator registered"
                foreach (var r in results)
                {
                    if (!r.Success)
                    {
                        Assert.IsFalse(r.ErrorMessage != null && r.ErrorMessage.Contains("No translator registered"),
                            $"Model {r.Model?.GetType().Name} failed with missing translator: {r.ErrorMessage}");
                    }
                }
            }
            finally
            {
                doc.Close(false);
            }
        }

        [Test]
        public void ToRevit_CrossDocumentTransfer_GuardsPositiveIdAndClearsStaleReferences()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document docA = app.NewProjectDocument(UnitSystem.Metric);
            Document docB = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                // In docA, create Material_DocA
                Material matA;
                using (var tA = new Transaction(docA, "Create MatA"))
                {
                    tA.Start();
                    ElementId matAId = Material.Create(docA, "Material_DocA");
                    matA = (Material)docA.GetElement(matAId);
                    tA.Commit();
                }

                // In docB, create Material_DocB that has its own positive ID
                Material matB;
                using (var tB = new Transaction(docB, "Create MatB"))
                {
                    tB.Start();
                    ElementId matBId = Material.Create(docB, "Material_DocB");
                    matB = (Material)docB.GetElement(matBId);
                    tB.Commit();
                }

                // Extract matA from docA without template flag so it has live Document and positive Id
                var engine = new StandardSerializationEngine();
                var modelsFromDocA = engine.ByRevit(new[] { matA }, docA, isTemplate: false).ToList();
                Assert.AreEqual(1, modelsFromDocA.Count);
                var modelA = (MaterialModel)modelsFromDocA[0];
                Assert.AreEqual(matA.Name, modelA.Name);
                Assert.AreEqual(docA, modelA.Document);
                Assert.AreEqual(matA, modelA.Element);
                Assert.Greater(modelA.Id, 0);

                // Artificially match modelA.Id to matB.Id to simulate colliding sequential IDs across documents!
#if REVIT2022 || REVIT2023
                modelA.Id = matB.Id.IntegerValue;
#else
                modelA.Id = matB.Id.Value;
#endif

                // Act: import modelA into docB
                var results = engine.ToRevit(new[] { modelA }, docB).ToList();

                // Assert:
                // 1. matB in docB was NOT modified or overwritten
                Assert.AreEqual("Material_DocB", matB.Name, "Colliding positive Id from docA must never match or overwrite matB in docB.");

                // 2. Material_DocA was created in docB
                var createdInDocB = new FilteredElementCollector(docB)
                    .OfClass(typeof(Material))
                    .Cast<Material>()
                    .FirstOrDefault(m => m.Name == "Material_DocA");
                Assert.IsNotNull(createdInDocB, "Material_DocA should be created in docB.");
                Assert.AreNotEqual(matB.Id, createdInDocB!.Id);

                // 3. Stale references on modelA were cleared and rebound to docB
                Assert.AreEqual(docB, modelA.Document, "modelA.Document must be rebound to docB.");
                Assert.IsNotNull(modelA.Element, "modelA.Element should not be null.");
                var boundMat = modelA.Element as Material;
                Assert.IsNotNull(boundMat, "modelA.Element should be a Material.");
                Assert.AreEqual(createdInDocB!.Id, boundMat!.Id, "modelA.Element must be rebound to docB's created element.");
                Assert.AreEqual(docB.Title, boundMat.Document.Title, "modelA.Element must belong to docB.");

                // 4. Result Action is Created
                Assert.AreEqual(1, results.Count);
                Assert.IsTrue(results[0].Success);
                Assert.AreEqual("Created", results[0].Action);
            }
            finally
            {
                docA.Close(false);
                docB.Close(false);
            }
        }

        [Test]
        public void ToRevit_DimensionType_DisambiguatesLinearAndAngularRoundTrip()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document docA = app.NewProjectDocument(UnitSystem.Metric);
            Document docB = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                // In docA, find or create Linear and Angular dimension types
                var allDimsDocA = new FilteredElementCollector(docA)
                    .OfClass(typeof(DimensionType))
                    .Cast<DimensionType>()
                    .Where(d => d.Name != DimensionTypeModel.InternalDimStyleName)
                    .ToList();

                var linearDim = allDimsDocA.FirstOrDefault(d => d.StyleType == DimensionStyleType.Linear);
                var angularDim = allDimsDocA.FirstOrDefault(d => d.StyleType == DimensionStyleType.Angular);

                Assert.IsNotNull(linearDim, "Linear DimensionType must exist in docA.");
                Assert.IsNotNull(angularDim, "Angular DimensionType must exist in docA.");

                // Duplicate both in docA to share the same name across styles: "Disambig_Standard"
                DimensionType customLinear;
                DimensionType customAngular;
                using (var tA = new Transaction(docA, "Create Colliding Dims in DocA"))
                {
                    tA.Start();
                    customLinear = (DimensionType)linearDim!.Duplicate("Disambig_Standard");
                    customAngular = (DimensionType)angularDim!.Duplicate("Disambig_Standard");
                    tA.Commit();
                }

                // Act 1: ByRevit extraction
                var engine = new StandardSerializationEngine();
                var extractedModels = engine.ByRevit(new[] { customLinear, customAngular }, docA, isTemplate: true).ToList();

                // Assert 1: Extraction details
                Assert.AreEqual(2, extractedModels.Count);
                var dimModels = extractedModels.OfType<DimensionTypeModel>().ToList();
                Assert.AreEqual(2, dimModels.Count);
                Assert.IsTrue(dimModels.Any(m => m.DimensionStyle == "Linear" && m.Name == "Disambig_Standard"));
                Assert.IsTrue(dimModels.Any(m => m.DimensionStyle == "Angular" && m.Name == "Disambig_Standard"));

                // Act 2: ToJson -> FromJson roundtrip
                string json = StandardSerializationEngine.ToJson(dimModels);
                var rehydrated = StandardSerializationEngine.FromJson(json).ToList();
                Assert.AreEqual(2, rehydrated.Count);

                // Act 3: Analyze against docB (initially neither exists in docB)
                var clusters = engine.Analyze(rehydrated, docB).ToList();

                // Act 4: ToRevit into docB
                var results = engine.ToRevit(rehydrated, docB).ToList();

                // Assert 4: Both created successfully without overwriting one another
                Assert.AreEqual(2, results.Count);
                Assert.IsTrue(results.All(r => r.Success), "Both dimension types should succeed.");

                var docBDims = new FilteredElementCollector(docB)
                    .OfClass(typeof(DimensionType))
                    .Cast<DimensionType>()
                    .Where(d => d.Name == "Disambig_Standard")
                    .ToList();

                Assert.AreEqual(2, docBDims.Count, "Both Linear and Angular Disambig_Standard must exist in docB.");
                Assert.IsTrue(docBDims.Any(d => d.StyleType == DimensionStyleType.Linear));
                Assert.IsTrue(docBDims.Any(d => d.StyleType == DimensionStyleType.Angular));
            }
            finally
            {
                docA.Close(false);
                docB.Close(false);
            }
        }

        [Test]
        public void ToRevit_FillPatternElement_DisambiguatesDraftingAndModelRoundTrip()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document docA = app.NewProjectDocument(UnitSystem.Metric);
            Document docB = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                // In docA, create colliding Drafting and Model fill pattern elements named "Crosshatch_Collide"
                FillPatternElement fpDrafting;
                FillPatternElement fpModel;
                using (var tA = new Transaction(docA, "Create Patterns DocA"))
                {
                    tA.Start();
                    var patDrafting = new FillPattern("Crosshatch_Collide", FillPatternTarget.Drafting, FillPatternHostOrientation.ToHost);
                    patDrafting.SetFillGrids(new List<FillGrid> { new FillGrid(0.0, 0.1) });
                    fpDrafting = FillPatternElement.Create(docA, patDrafting);

                    var patModel = new FillPattern("Crosshatch_Collide", FillPatternTarget.Model, FillPatternHostOrientation.ToHost);
                    patModel.SetFillGrids(new List<FillGrid> { new FillGrid(0.0, 0.2) });
                    fpModel = FillPatternElement.Create(docA, patModel);
                    tA.Commit();
                }

                // Act 1: ByRevit extraction
                var engine = new StandardSerializationEngine();
                var extractedModels = engine.ByRevit(new[] { fpDrafting, fpModel }, docA, isTemplate: true).ToList();

                // Assert 1: Extraction details
                Assert.AreEqual(2, extractedModels.Count);
                var fpModels = extractedModels.OfType<FillPatternElementModel>().ToList();
                Assert.AreEqual(2, fpModels.Count);
                Assert.IsTrue(fpModels.Any(m => m.Pattern?.Target == "Drafting" && m.Name == "Crosshatch_Collide"));
                Assert.IsTrue(fpModels.Any(m => m.Pattern?.Target == "Model" && m.Name == "Crosshatch_Collide"));

                // Act 2: ToJson -> FromJson roundtrip
                string json = StandardSerializationEngine.ToJson(fpModels);
                var rehydrated = StandardSerializationEngine.FromJson(json).ToList();
                Assert.AreEqual(2, rehydrated.Count);

                // Act 3: ToRevit into docB
                var results = engine.ToRevit(rehydrated, docB).ToList();
                Assert.AreEqual(2, results.Count);
                Assert.IsTrue(results.All(r => r.Success), "Both fill patterns should succeed.");

                // Assert 3: Both targets exist in docB without overwriting
                var docBFps = new FilteredElementCollector(docB)
                    .OfClass(typeof(FillPatternElement))
                    .Cast<FillPatternElement>()
                    .Where(f => f.Name == "Crosshatch_Collide")
                    .ToList();

                Assert.AreEqual(2, docBFps.Count, "Both Drafting and Model Crosshatch_Collide must exist in docB.");
                Assert.IsTrue(docBFps.Any(f => f.GetFillPattern()?.Target == FillPatternTarget.Drafting));
                Assert.IsTrue(docBFps.Any(f => f.GetFillPattern()?.Target == FillPatternTarget.Model));

                // Act 4: Re-enforcing same models into docB results in Unchanged
                var secondResults = engine.ToRevit(rehydrated, docB).ToList();
                Assert.AreEqual(2, secondResults.Count);
                Assert.IsTrue(secondResults.All(r => r.Action == "Unchanged"), "Both identical patterns should report Unchanged.");
            }
            finally
            {
                docA.Close(false);
                docB.Close(false);
            }
        }

        [Test]
        public void ToRevit_MultiFamilySystemTypes_DisambiguatesFamilyNameAndWildcards()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document docA = app.NewProjectDocument(UnitSystem.Metric);
            Document docB = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                // In docA, find WallTypes across Basic Wall and Curtain Wall
                var wallTypesDocA = new FilteredElementCollector(docA)
                    .OfClass(typeof(WallType))
                    .Cast<WallType>()
                    .ToList();

                var basicWall = wallTypesDocA.FirstOrDefault(w => string.Equals(w.FamilyName, "Basic Wall", StringComparison.OrdinalIgnoreCase));
                var curtainWall = wallTypesDocA.FirstOrDefault(w => string.Equals(w.FamilyName, "Curtain Wall", StringComparison.OrdinalIgnoreCase));

                Assert.IsNotNull(basicWall, "Basic Wall must exist in docA.");
                Assert.IsNotNull(curtainWall, "Curtain Wall must exist in docA.");

                // Act 1: ByRevit populates FamilyName
                var engine = new StandardSerializationEngine();
                var extracted = engine.ByRevit(new[] { basicWall!, curtainWall! }, docA, isTemplate: true).ToList();

                Assert.AreEqual(2, extracted.Count);
                var hostModels = extracted.OfType<HostObjTypeModel>().ToList();
                Assert.AreEqual(2, hostModels.Count);
                Assert.AreEqual("Basic Wall", hostModels.First(h => h.Name == basicWall!.Name).FamilyName);
                Assert.AreEqual("Curtain Wall", hostModels.First(h => h.Name == curtainWall!.Name).FamilyName);

                // Act 2: Create colliding models in docB with same name "Disambig_Wall", plus legacy empty-FamilyName wildcard
                var modelBasic = new HostObjTypeModel
                {
                    Name = "Disambig_Wall",
                    Class = "Autodesk.Revit.DB.WallType",
                    FamilyName = "Basic Wall"
                };
                var modelCurtain = new HostObjTypeModel
                {
                    Name = "Disambig_Wall",
                    Class = "Autodesk.Revit.DB.WallType",
                    FamilyName = "Curtain Wall"
                };
                var modelWildcard = new HostObjTypeModel
                {
                    Name = "Disambig_WildcardWall",
                    Class = "Autodesk.Revit.DB.WallType",
                    FamilyName = "" // Legacy wildcard
                };

                var results = engine.ToRevit(new[] { modelBasic, modelCurtain, modelWildcard }, docB).ToList();

                Assert.AreEqual(3, results.Count);
                Assert.IsTrue(results.All(r => r.Success), "All 3 wall models must import successfully.");

                // Verify elements in docB
                var docBWalls = new FilteredElementCollector(docB)
                    .OfClass(typeof(WallType))
                    .Cast<WallType>()
                    .Where(w => w.Name == "Disambig_Wall")
                    .ToList();

                Assert.AreEqual(2, docBWalls.Count, "Both Basic Wall and Curtain Wall Disambig_Wall must exist in docB.");
                Assert.IsTrue(docBWalls.Any(w => string.Equals(w.FamilyName, "Basic Wall", StringComparison.OrdinalIgnoreCase)));
                Assert.IsTrue(docBWalls.Any(w => string.Equals(w.FamilyName, "Curtain Wall", StringComparison.OrdinalIgnoreCase)));

                var wildcardWall = new FilteredElementCollector(docB)
                    .OfClass(typeof(WallType))
                    .Cast<WallType>()
                    .FirstOrDefault(w => w.Name == "Disambig_WildcardWall");
                Assert.IsNotNull(wildcardWall, "Legacy empty-FamilyName wildcard model must create/duplicate successfully.");
            }
            finally
            {
                docA.Close(false);
                docB.Close(false);
            }
        }

        private (Document ProjectDoc, Document FamilyDoc, bool IsTemplateDoc) OpenProjectAndEditableFamilyDocs(Autodesk.Revit.ApplicationServices.Application app)
        {
            Document projDoc = app.NewProjectDocument(UnitSystem.Metric);
            Family? editableFamily = new FilteredElementCollector(projDoc)
                .OfClass(typeof(Family))
                .Cast<Family>()
                .FirstOrDefault(f => f.IsEditable);

            if (editableFamily != null)
            {
                Document famDoc = projDoc.EditFamily(editableFamily);
                if (famDoc != null)
                {
                    return (projDoc, famDoc, false);
                }
            }

            projDoc.Close(false);

            string projectRoot = TestPathHelper.GetProjectRoot();
            string testModelName = "TestTemplate" + app.VersionNumber + ".rvt";
            string modelPathStr = Path.Combine(projectRoot, "tests", "test_models", testModelName);
            if (File.Exists(modelPathStr))
            {
                ModelPath modelPath = ModelPathUtils.ConvertUserVisiblePathToModelPath(modelPathStr);
                OpenOptions openOptions = new OpenOptions
                {
                    DetachFromCentralOption = DetachFromCentralOption.DetachAndDiscardWorksets
                };
                projDoc = app.OpenDocumentFile(modelPath, openOptions);
                editableFamily = new FilteredElementCollector(projDoc)
                    .OfClass(typeof(Family))
                    .Cast<Family>()
                    .FirstOrDefault(f => f.IsEditable);

                if (editableFamily != null)
                {
                    Document famDoc = projDoc.EditFamily(editableFamily);
                    if (famDoc != null)
                    {
                        return (projDoc, famDoc, true);
                    }
                }
            }

            throw new InvalidOperationException("Could not find an editable family in test document to open family document.");
        }

        [Test]
        public void ToRevit_DocumentScope_EnforcesUniversalFamilyAndProjectScopes()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            var (projDoc, famDoc, _) = OpenProjectAndEditableFamilyDocs(app);

            try
            {
                var engine = new StandardSerializationEngine();

                // 1. In Family Document: ProjectOnly types + unknown classes are skipped with Success = true, Action = "Skipped"
                var wallType = new HostObjTypeModel { Name = "Generic Wall", Class = "Autodesk.Revit.DB.WallType" };
                var gridType = new ElementTypeModel { Name = "1/4 Circle", Class = "Autodesk.Revit.DB.GridType" };
                var paramElement = new ParameterElementModel { Name = "CostCode", Class = "Autodesk.Revit.DB.ParameterElement" };
                var familySymbol = new ElementTypeModel { Name = "Door 36x84", Class = "Autodesk.Revit.DB.FamilySymbol" };
                var viewFamilyType = new ElementTypeModel { Name = "Floor Plan", Class = "Autodesk.Revit.DB.ViewFamilyType" };
                var unknownClass = new ElementModel { Name = "CustomWidget", Class = "Custom.Unknown.WidgetType" };

                var outOfScopeFamilyBatch = new List<ObjectModel> { wallType, gridType, paramElement, familySymbol, viewFamilyType, unknownClass };
                var familyResults = engine.ToRevit(outOfScopeFamilyBatch, famDoc).ToList();

                Assert.AreEqual(6, familyResults.Count);
                foreach (var r in familyResults)
                {
                    Assert.IsTrue(r.Success, $"Model {r.Model} must succeed with Skipped action.");
                    Assert.AreEqual("Skipped", r.Action, $"Model {r.Model} must have Action = Skipped.");
                    Assert.IsTrue(r.Message!.Contains("out of scope for family document"), $"Message should state out of scope for family document: {r.Message}");
                }

                // Verify Universal + FamilyOnly scope checks in family document
                var universalModels = new ObjectModel[]
                {
                    new LinePatternElementModel { Class = "Autodesk.Revit.DB.LinePatternElement" },
                    new FillPatternElementModel { Class = "Autodesk.Revit.DB.FillPatternElement" },
                    new PropertySetElementModel { Class = "Autodesk.Revit.DB.PropertySetElement" },
                    new MaterialModel { Class = "Autodesk.Revit.DB.Material" },
                    new CategoryModel { Class = "Autodesk.Revit.DB.Category" },
                    new DimensionTypeModel { Class = "Autodesk.Revit.DB.DimensionType" },
                    new FilledRegionTypeModel { Class = "Autodesk.Revit.DB.FilledRegionType" },
                    new ElementTypeModel { Class = "Autodesk.Revit.DB.TextNoteType" },
                    new ElementTypeModel { Class = "Autodesk.Revit.DB.ModelTextType" },
                    new ElementTypeModel { Class = "Autodesk.Revit.DB.TextElementType" } // FamilyOnly
                };

                foreach (var m in universalModels)
                {
                    Assert.IsTrue(StandardTypeRegistry.IsInDocumentScope(m, famDoc.IsFamilyDocument),
                        $"Model {m.GetType().Name} with class {(m as ElementModel)?.Class} must be in scope for family document.");
                }

                // 2. In Project Document: FamilyOnly type (TextElementType) is skipped with Success = true, Action = "Skipped"
                var textElementType = new ElementTypeModel { Name = "Family Label 1/8", Class = "Autodesk.Revit.DB.TextElementType" };
                var projResults = engine.ToRevit(new[] { textElementType }, projDoc).ToList();

                Assert.AreEqual(1, projResults.Count);
                Assert.IsTrue(projResults[0].Success);
                Assert.AreEqual("Skipped", projResults[0].Action);
                Assert.IsTrue(projResults[0].Message!.Contains("out of scope for project document"));
            }
            finally
            {
                famDoc.Close(false);
                projDoc.Close(false);
            }
        }

        [Test]
        public void Analyze_DocumentScope_OmitsOutOfScopeClustersInProjectAndFamilyDocs()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            var (projDoc, famDoc, _) = OpenProjectAndEditableFamilyDocs(app);

            try
            {
                var engine = new StandardSerializationEngine();

                var linePattern = new LinePatternElementModel { Name = "Dash 1/8", Class = "Autodesk.Revit.DB.LinePatternElement", Category = "Line Patterns" };
                var textElementType = new ElementTypeModel { Name = "FamLabel", Class = "Autodesk.Revit.DB.TextElementType", Category = "Text Element Types" };
                var wallType = new HostObjTypeModel { Name = "Generic 8\"", Class = "Autodesk.Revit.DB.WallType", Category = "Walls" };
                var unknownType = new ElementModel { Name = "UnknownThing", Class = "Custom.Unknown.Class", Category = "Custom" };

                var allModels = new List<ObjectModel> { linePattern, textElementType, wallType, unknownType };

                // 1. Analyze against family doc: WallType and unknown types must be omitted from diff clusters
                var familyClusters = engine.Analyze(allModels, famDoc).ToList();
                var familyCategories = familyClusters.SelectMany(c => c.Items).Select(i => i.CategoryName).Distinct().ToList();

                Assert.IsFalse(familyCategories.Contains("Walls"), "WallType must be omitted from family diff clusters.");
                Assert.IsFalse(familyCategories.Contains("Custom"), "Unknown classes must be omitted from family diff clusters.");

                // 2. Analyze against project doc: TextElementType must be omitted from diff clusters
                var projectClusters = engine.Analyze(allModels, projDoc).ToList();
                var projectCategories = projectClusters.SelectMany(c => c.Items).Select(i => i.CategoryName).Distinct().ToList();

                Assert.IsFalse(projectCategories.Contains("Text Element Types"), "TextElementType must be omitted from project diff clusters.");
            }
            finally
            {
                famDoc.Close(false);
                projDoc.Close(false);
            }
        }

        private (Document Doc, string TempPath) OpenDetachedTestTemplateCopy(Autodesk.Revit.ApplicationServices.Application app)
        {
            string projectRoot = TestPathHelper.GetProjectRoot();
            string testModelName = "TestTemplate" + app.VersionNumber + ".rvt";
            string sourcePath = Path.Combine(projectRoot, "tests", "test_models", testModelName);
            Assert.IsTrue(File.Exists(sourcePath), $"Test template not found at {sourcePath}");

            string tempCopyPath = Path.Combine(Path.GetTempPath(), $"SyntheticTestTemplate_{Guid.NewGuid():N}.rvt");
            File.Copy(sourcePath, tempCopyPath, overwrite: true);

            ModelPath modelPath = ModelPathUtils.ConvertUserVisiblePathToModelPath(tempCopyPath);
            var openOptions = new OpenOptions
            {
                DetachFromCentralOption = DetachFromCentralOption.DetachAndDiscardWorksets
            };
            return (app.OpenDocumentFile(modelPath, openOptions), tempCopyPath);
        }

        [Test]
        public void ToRevit_FamilySymbol_TwoDoc_LoadedSiblingDuplication_RoundTrip()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            var (docA, pathA) = OpenDetachedTestTemplateCopy(app);
            var (docB, pathB) = OpenDetachedTestTemplateCopy(app);

            try
            {
                FamilySymbol? seedA = new FilteredElementCollector(docA)
                    .OfClass(typeof(FamilySymbol))
                    .Cast<FamilySymbol>()
                    .FirstOrDefault(s => s.GetType() == typeof(FamilySymbol) && s.Family != null && !s.Family.IsInPlace && s.Category != null && !string.IsNullOrEmpty(s.Family.Name));
                Assert.IsNotNull(seedA, "TestTemplate must contain a loadable FamilySymbol.");

                string targetFamilyName = seedA!.Family.Name;
                string newSymbolName = "SiblingSymbol_143_" + Guid.NewGuid().ToString("N").Substring(0, 6);

                FamilySymbol duplicatedA;
                using (var tA = new Transaction(docA, "Create Sibling FamilySymbol"))
                {
                    tA.Start();
                    duplicatedA = (FamilySymbol)seedA.Duplicate(newSymbolName);
                    tA.Commit();
                }

                var engine = new StandardSerializationEngine();
                var extracted = engine.ByRevit(new Element[] { duplicatedA }, docA, isTemplate: true).ToList();
                Assert.AreEqual(1, extracted.Count);

                string json = StandardSerializationEngine.ToJson(extracted);
                var rehydrated = StandardSerializationEngine.FromJson(json).ToList();
                Assert.AreEqual(1, rehydrated.Count);

                var fsModel = rehydrated[0] as ElementTypeModel;
                Assert.IsNotNull(fsModel);
                Assert.AreEqual(targetFamilyName, fsModel!.FamilyName);

                var clusters = engine.Analyze(rehydrated, docB).ToList();
                Assert.IsFalse(clusters.SelectMany(c => c.Items).Any(i => i.ItemName == newSymbolName),
                    "New sibling symbol should not yet exist in docB.");

                var results = engine.ToRevit(rehydrated, docB).ToList();
                Assert.AreEqual(1, results.Count);
                Assert.IsTrue(results[0].Success, $"ToRevit failed: {results[0].Message}");
                Assert.AreEqual("Created", results[0].Action);

                var createdInDocB = new FilteredElementCollector(docB)
                    .OfClass(typeof(FamilySymbol))
                    .Cast<FamilySymbol>()
                    .FirstOrDefault(s => s.Name == newSymbolName && string.Equals(s.Family?.Name, targetFamilyName, StringComparison.OrdinalIgnoreCase));
                Assert.IsNotNull(createdInDocB, $"Symbol '{newSymbolName}' must be duplicated strictly inside loaded family '{targetFamilyName}'.");
            }
            finally
            {
                docA.Close(false);
                docB.Close(false);
                try { File.Delete(pathA); } catch { }
                try { File.Delete(pathB); } catch { }
            }
        }

        [Test]
        public void ToRevit_FamilySymbol_TwoDoc_ConflictingFamilyDoesNotOverwriteUnrelatedFamily()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            var (docA, pathA) = OpenDetachedTestTemplateCopy(app);
            var (docB, pathB) = OpenDetachedTestTemplateCopy(app);

            try
            {
                var symbolsA = new FilteredElementCollector(docA)
                    .OfClass(typeof(FamilySymbol))
                    .Cast<FamilySymbol>()
                    .Where(s => s.GetType() == typeof(FamilySymbol) && s.Family != null && !s.Family.IsInPlace && s.Category != null && !string.IsNullOrEmpty(s.Family.Name))
                    .GroupBy(s => s.Family.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.First())
                    .ToList();

                Assert.GreaterOrEqual(symbolsA.Count, 2, "TestTemplate must contain at least 2 distinct loadable families.");
                FamilySymbol fam1SeedA = symbolsA[0];
                string fam1Name = fam1SeedA.Family.Name;
                string fam2Name = symbolsA[1].Family.Name;

                string collidingName = "CollideSymbol_143_" + Guid.NewGuid().ToString("N").Substring(0, 6);

                FamilySymbol createdInA;
                using (var tA = new Transaction(docA, "Create Fam1 Symbol in DocA"))
                {
                    tA.Start();
                    createdInA = (FamilySymbol)fam1SeedA.Duplicate(collidingName);
                    tA.Commit();
                }

                // In docB, create a same-named symbol under fam2 (NOT fam1)
                FamilySymbol? fam2SeedB = new FilteredElementCollector(docB)
                    .OfClass(typeof(FamilySymbol))
                    .Cast<FamilySymbol>()
                    .FirstOrDefault(s => string.Equals(s.Family?.Name, fam2Name, StringComparison.OrdinalIgnoreCase));
                Assert.IsNotNull(fam2SeedB);

                ElementId fam2SymbolIdB;
                using (var tB = new Transaction(docB, "Create Conflicting Fam2 Symbol in DocB"))
                {
                    tB.Start();
                    var fam2SymB = (FamilySymbol)fam2SeedB!.Duplicate(collidingName);
                    fam2SymbolIdB = fam2SymB.Id;
                    tB.Commit();
                }

                var engine = new StandardSerializationEngine();
                var rehydrated = StandardSerializationEngine.FromJson(
                    StandardSerializationEngine.ToJson(engine.ByRevit(new Element[] { createdInA }, docA, isTemplate: true))).ToList();

                var results = engine.ToRevit(rehydrated, docB).ToList();
                Assert.AreEqual(1, results.Count);
                Assert.IsTrue(results[0].Success, $"ToRevit failed: {results[0].Message}");
                Assert.AreEqual("Created", results[0].Action, "Conflicting family in docB must not match; must duplicate inside fam1 instead.");

                var docBColliders = new FilteredElementCollector(docB)
                    .OfClass(typeof(FamilySymbol))
                    .Cast<FamilySymbol>()
                    .Where(s => s.Name == collidingName)
                    .ToList();

                Assert.IsTrue(docBColliders.Any(s => string.Equals(s.Family?.Name, fam1Name, StringComparison.OrdinalIgnoreCase)),
                    "fam1 must receive the newly duplicated symbol.");
                var untouchedFam2 = docB.GetElement(fam2SymbolIdB) as FamilySymbol;
                Assert.IsNotNull(untouchedFam2, "Unrelated fam2 symbol in docB must remain intact.");
                Assert.AreEqual(collidingName, untouchedFam2!.Name);
                Assert.AreEqual(fam2Name, untouchedFam2.Family?.Name);
            }
            finally
            {
                docA.Close(false);
                docB.Close(false);
                try { File.Delete(pathA); } catch { }
                try { File.Delete(pathB); } catch { }
            }
        }

        [Test]
        public void ToRevit_FamilySymbol_TwoDoc_UnloadedFamilyRollsBackInnerTransactionOnly()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            var (docA, pathA) = OpenDetachedTestTemplateCopy(app);
            Document docB = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                var docBFamilyNames = new HashSet<string>(
                    new FilteredElementCollector(docB).OfClass(typeof(Family)).Cast<Family>().Select(f => f.Name),
                    StringComparer.OrdinalIgnoreCase);

                FamilySymbol? missingInDocB = new FilteredElementCollector(docA)
                    .OfClass(typeof(FamilySymbol))
                    .Cast<FamilySymbol>()
                    .FirstOrDefault(s => s.GetType() == typeof(FamilySymbol) && s.Family != null && !s.Family.IsInPlace && !string.IsNullOrEmpty(s.Family.Name) && !docBFamilyNames.Contains(s.Family.Name));
                Assert.IsNotNull(missingInDocB, "Need a FamilySymbol in TestTemplate whose Family is not in blank metric project.");

                string missingFamilyName = missingInDocB!.Family.Name;

                var engine = new StandardSerializationEngine();
                var rehydratedFs = StandardSerializationEngine.FromJson(
                    StandardSerializationEngine.ToJson(engine.ByRevit(new Element[] { missingInDocB }, docA, isTemplate: true))).ToList();
                Assert.AreEqual(1, rehydratedFs.Count);

                var healthyMat = new MaterialModel
                {
                    Name = "HealthyBatchMat_143_" + Guid.NewGuid().ToString("N").Substring(0, 6),
                    Class = "Autodesk.Revit.DB.Material"
                };

                var results = engine.ToRevit(new ObjectModel[] { rehydratedFs[0], healthyMat }, docB).ToList();
                Assert.AreEqual(2, results.Count);

                var fsRes = results.First(r => r.Model == rehydratedFs[0]);
                Assert.IsFalse(fsRes.Success, "Missing parent family must fail.");
                Assert.AreEqual("Failed", fsRes.Action);
                Assert.IsTrue(fsRes.Message.Contains(missingFamilyName), $"Failure message must name missing FamilyName '{missingFamilyName}', got: {fsRes.Message}");

                var matRes = results.First(r => r.Model == healthyMat);
                Assert.IsTrue(matRes.Success, "Healthy sibling model in same TransactionGroup must commit.");
                Assert.AreEqual("Created", matRes.Action);

                Assert.IsTrue(new FilteredElementCollector(docB).OfClass(typeof(Material)).Any(m => m.Name == healthyMat.Name));
            }
            finally
            {
                docA.Close(false);
                docB.Close(false);
                try { File.Delete(pathA); } catch { }
            }
        }

        [Test]
        public void GatherRootElements_And_ToRevit_ViewFamilyType_TwoDocRoundTrip_AndNonCopiableSeedFailure()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document docA = app.NewProjectDocument(UnitSystem.Metric);
            Document docB = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                var copiableSeedA = new FilteredElementCollector(docA)
                    .OfClass(typeof(ViewFamilyType))
                    .Cast<ViewFamilyType>()
                    .FirstOrDefault(v => v.CanBeCopied && v.ViewFamily == ViewFamily.FloorPlan);
                Assert.IsNotNull(copiableSeedA, "Must find a copiable FloorPlan ViewFamilyType in docA.");

                string customVftName = "Custom_FloorPlan_144_" + Guid.NewGuid().ToString("N").Substring(0, 6);
                using (var tA = new Transaction(docA, "Create Custom ViewFamilyType"))
                {
                    tA.Start();
                    copiableSeedA!.Duplicate(customVftName);
                    tA.Commit();
                }

                var engine = new StandardSerializationEngine();

                // 1. GatherRootElements collects ViewFamilyType
                var gathered = engine.GatherRootElements(docA, new[] { "View Family Types" }).ToList();
                var gatheredCustom = gathered.OfType<ViewFamilyType>().FirstOrDefault(v => v.Name == customVftName);
                Assert.IsNotNull(gatheredCustom, "GatherRootElements must collect custom ViewFamilyType.");

                // 2. ByRevit -> ToJson -> FromJson
                var extracted = engine.ByRevit(new Element[] { gatheredCustom! }, docA, isTemplate: true).ToList();
                Assert.AreEqual(1, extracted.Count);
                var vftModel = extracted[0] as ElementTypeModel;
                Assert.IsNotNull(vftModel);
                Assert.AreEqual("FloorPlan", vftModel!.ViewFamily);

                string json = StandardSerializationEngine.ToJson(extracted);
                var rehydrated = StandardSerializationEngine.FromJson(json).ToList();
                Assert.AreEqual(1, rehydrated.Count);

                // 3. Analyze & ToRevit into docB
                var clusters = engine.Analyze(rehydrated, docB).ToList();
                Assert.IsFalse(clusters.SelectMany(c => c.Items).Any(i => i.ItemName == customVftName));

                var results = engine.ToRevit(rehydrated, docB).ToList();
                Assert.AreEqual(1, results.Count);
                Assert.IsTrue(results[0].Success, $"ToRevit for ViewFamilyType failed: {results[0].Message}");
                Assert.AreEqual("Created", results[0].Action);

                var createdInDocB = new FilteredElementCollector(docB)
                    .OfClass(typeof(ViewFamilyType))
                    .Cast<ViewFamilyType>()
                    .FirstOrDefault(v => v.Name == customVftName && v.ViewFamily == ViewFamily.FloorPlan);
                Assert.IsNotNull(createdInDocB, "Created ViewFamilyType must exist in docB with ViewFamily.FloorPlan.");

                // 4. Non-duplicatable ViewFamilyType (!v.CanBeCopied) records Action = "Failed" without crashing batch
                var nonCopiableVft = new FilteredElementCollector(docA)
                    .OfClass(typeof(ViewFamilyType))
                    .Cast<ViewFamilyType>()
                    .FirstOrDefault(v => !v.CanBeCopied);
                if (nonCopiableVft != null)
                {
                    var failModel = new ElementTypeModel
                    {
                        Name = "Attempted_NonCopiable_144_" + Guid.NewGuid().ToString("N").Substring(0, 6),
                        Class = "Autodesk.Revit.DB.ViewFamilyType",
                        ViewFamily = nonCopiableVft.ViewFamily.ToString()
                    };
                    var healthyMat = new MaterialModel
                    {
                        Name = "HealthyBatchMat_144_" + Guid.NewGuid().ToString("N").Substring(0, 6),
                        Class = "Autodesk.Revit.DB.Material"
                    };

                    var batchResults = engine.ToRevit(new ObjectModel[] { failModel, healthyMat }, docB).ToList();
                    Assert.AreEqual(2, batchResults.Count);

                    var vftFail = batchResults.First(r => r.Model == failModel);
                    Assert.IsFalse(vftFail.Success);
                    Assert.AreEqual("Failed", vftFail.Action);

                    var matOk = batchResults.First(r => r.Model == healthyMat);
                    Assert.IsTrue(matOk.Success);
                    Assert.AreEqual("Created", matOk.Action);
                }
            }
            finally
            {
                docA.Close(false);
                docB.Close(false);
            }
        }
    }
}
