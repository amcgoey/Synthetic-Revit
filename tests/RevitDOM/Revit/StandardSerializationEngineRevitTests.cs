using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Autodesk.Revit.UI;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Visual;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Threading;
using Synthetic.Platform.Revit;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM.Operations.Diffing;
using Synthetic.RevitDOM;
using SyntheticTests.Fixtures;

namespace SyntheticTests.RevitDOM
{
    [TestFixture]
    [RevitAdapterTest]
    public class StandardSerializationEngineRevitTests : RevitTemplateRollbackFixture
    {
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
            WithRolledBackDocument(doc =>
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
            });
        }

        [Test]
        public void ToRevit_Cancellation_RollsBackAllChangesAndThrowsException()
        {
            WithRolledBackDocument(doc =>
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
            });
        }

        [Test]
        public void ToRevit_UnchangedElement_SkipsModificationsAndReturnsUnchanged()
        {
            WithRolledBackDocument(doc =>
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
            });
        }

        [Test]
        public void ToRevit_CompositeRouting_DispatchesAllStandardTypesToRegisteredTranslatorsWithoutOverwrite()
        {
            WithRolledBackDocument(doc =>
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

#if REVIT2023
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
            });
        }

        [Test]
        public void ToRevit_CrossDocumentTransfer_GuardsPositiveIdAndClearsStaleReferences()
        {
            WithRolledBackDocumentPair((docA, docB) =>
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
#if REVIT2023
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
            });
        }

        [Test]
        public void ToRevit_DimensionType_DisambiguatesLinearAndAngularRoundTrip()
        {
            WithRolledBackDocumentPair((docA, docB) =>
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
            });
        }

        [Test]
        public void ToRevit_FillPatternElement_DisambiguatesDraftingAndModelRoundTrip()
        {
            WithRolledBackDocumentPair((docA, docB) =>
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
            });
        }

        [Test]
        public void ToRevit_MultiFamilySystemTypes_DisambiguatesFamilyNameAndWildcards()
        {
            WithRolledBackDocumentPair((docA, docB) =>
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
            });
        }

        [Test]
        public void GatherRootElements_And_ToRevit_ViewFamilyType_TwoDocRoundTrip_AndNonCopiableSeedFailure()
        {
            WithRolledBackDocumentPair((docA, docB) =>
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
            });
        }

        [Test]
        public void ToRevit_MultiCategoryTwoDocumentRoundTrip_HasZeroDriftAndNonNullResolution()
        {
            WithRolledBackDocumentPair((docA, docB) =>
            {
                const string suffix = "RoundTrip206";
                string fillPatternName = suffix + "_Crosshatch";
                string materialName = suffix + "_Material";
                string appearanceName = suffix + "_Appearance";
                string structuralName = suffix + "_Structural";
                string thermalName = suffix + "_Thermal";
                string dimTypeName = suffix + "_LinearDim";
                string filterName = suffix + "_WallFilter";
                string wallTypeName = suffix + "_WallType";
                string viewName = suffix + "_FloorPlan";

                FillPatternElement? fpElemA = null;
                Material? matA = null;
                DimensionType? dimTypeA = null;
                ParameterFilterElement? filterA = null;
                WallType? wallTypeA = null;
                ViewPlan? viewA = null;

                docA.ExecuteTransaction("Setup Multi-Category Elements in DocA", FailurePipelines.ObserveOnly(), () =>
                {
                    // 1. FillPatternElement
                    var fp = new FillPattern(fillPatternName, FillPatternTarget.Drafting, FillPatternHostOrientation.ToHost);
                    fp.SetFillGrids(new List<FillGrid> { new FillGrid(0.0, 0.25) });
                    fpElemA = FillPatternElement.Create(docA, fp);

                    // 2. Material with nested Appearance, Structural, and Thermal assets
                    AppearanceAssetElement appearanceTemplateA = EnsureAppearanceAssetTemplate(docA);
                    AppearanceAssetElement appearanceA = appearanceTemplateA.Duplicate(appearanceName);
                    using (var editScope = new AppearanceAssetEditScope(docA))
                    {
                        Asset editableAsset = editScope.Start(appearanceA.Id);
                        var colorProp = editableAsset.FindByName("generic_diffuse") as AssetPropertyDoubleArray4d;
                        if (colorProp != null && !colorProp.IsReadOnly)
                        {
                            colorProp.SetValueAsColor(new Color(150, 160, 170));
                        }
                        var transProp = editableAsset.FindByName("generic_transparency") as AssetPropertyDouble;
                        if (transProp != null && !transProp.IsReadOnly)
                        {
                            transProp.Value = 0.0;
                        }
                        var glossProp = editableAsset.FindByName("generic_glossiness") as AssetPropertyDouble;
                        if (glossProp != null && !glossProp.IsReadOnly)
                        {
                            glossProp.Value = 0.75;
                        }
                        editScope.Commit(true);
                    }

                    var structuralAsset = new StructuralAsset(structuralName, StructuralAssetClass.Metal);
                    structuralAsset.Behavior = StructuralBehavior.Isotropic;
                    structuralAsset.Density = 490.0;
                    structuralAsset.SetYoungModulus(29000000.0);
                    structuralAsset.SetPoissonRatio(0.3);
                    PropertySetElement structPseA = PropertySetElement.Create(docA, structuralAsset);

                    var thermalAsset = new ThermalAsset(thermalName, ThermalMaterialType.Solid);
                    thermalAsset.Density = 490.0;
                    thermalAsset.ThermalConductivity = 45.0;
                    thermalAsset.SpecificHeat = 0.5;
                    thermalAsset.Emissivity = 0.75;
                    PropertySetElement thermalPseA = PropertySetElement.Create(docA, thermalAsset);

                    ElementId matIdA = Material.Create(docA, materialName);
                    matA = (Material)docA.GetElement(matIdA);
                    matA.Color = new Color(140, 150, 160);
                    matA.SurfaceForegroundPatternColor = new Color(100, 110, 120);
                    matA.SurfaceForegroundPatternId = fpElemA.Id;
                    matA.CutForegroundPatternColor = new Color(80, 80, 80);
                    matA.CutForegroundPatternId = fpElemA.Id;
                    matA.AppearanceAssetId = appearanceA.Id;
                    matA.SetMaterialAspectByPropertySet(MaterialAspect.Structural, structPseA.Id);
                    matA.SetMaterialAspectByPropertySet(MaterialAspect.Thermal, thermalPseA.Id);

                    // 3. DimensionType (Linear)
                    DimensionType? linearSeed = new FilteredElementCollector(docA)
                        .OfClass(typeof(DimensionType))
                        .Cast<DimensionType>()
                        .FirstOrDefault(d => d.StyleType == DimensionStyleType.Linear &&
                                             !string.Equals(d.Name, DimensionTypeModel.InternalDimStyleName, StringComparison.Ordinal));
                    Assert.IsNotNull(linearSeed, "Linear DimensionType seed must exist in docA.");
                    dimTypeA = (DimensionType)linearSeed!.Duplicate(dimTypeName);

                    // 4. ParameterFilterElement
                    Category wallsCategory = docA.Settings.Categories.get_Item(BuiltInCategory.OST_Walls);
                    filterA = ParameterFilterElement.Create(docA, filterName, new List<ElementId> { wallsCategory.Id });
                    ElementId commentsParamId = new ElementId(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
#if REVIT2023
                    FilterRule rule = ParameterFilterRuleFactory.CreateContainsRule(commentsParamId, suffix, true);
                    filterA.SetElementFilter(new ElementParameterFilter(rule));
#else
                    var provider = new ParameterValueProvider(commentsParamId);
                    var rule = new FilterStringRule(provider, new FilterStringContains(), suffix);
                    filterA.SetElementFilter(new ElementParameterFilter(rule));
#endif

                    // 5. HostObjType (WallType with CompoundStructure)
                    WallType? wallSeed = new FilteredElementCollector(docA)
                        .OfClass(typeof(WallType))
                        .Cast<WallType>()
                        .FirstOrDefault(w => string.Equals(w.FamilyName, "Basic Wall", StringComparison.OrdinalIgnoreCase));
                    Assert.IsNotNull(wallSeed, "Basic Wall WallType seed must exist in docA.");
                    wallTypeA = (WallType)wallSeed!.Duplicate(wallTypeName);

                    var layers = new List<CompoundStructureLayer>
                    {
                        new CompoundStructureLayer(0.5, MaterialFunctionAssignment.Structure, matA.Id) { LayerCapFlag = true },
                        new CompoundStructureLayer(0.125, MaterialFunctionAssignment.Finish1, matA.Id) { LayerCapFlag = false }
                    };
                    CompoundStructure cs = CompoundStructure.CreateSimpleCompoundStructure(layers);
                    cs.StructuralMaterialIndex = 0;
#if REVIT2023 || REVIT2024 || REVIT2025
                    // Layer priority requires Revit 2026+
#else
                    cs.SetLayerPriority(0, 2);
                    cs.SetLayerPriority(1, 5);
#endif
                    wallTypeA.SetCompoundStructure(cs);
                    Parameter? typeCommentsParam = wallTypeA.get_Parameter(BuiltInParameter.ALL_MODEL_TYPE_COMMENTS);
                    if (typeCommentsParam != null && !typeCommentsParam.IsReadOnly)
                    {
                        typeCommentsParam.Set("Golden Wall Type RoundTrip");
                    }

                    // 6. View (ViewPlan referencing the ParameterFilterElement)
                    ViewFamilyType? floorPlanVft = new FilteredElementCollector(docA)
                        .OfClass(typeof(ViewFamilyType))
                        .Cast<ViewFamilyType>()
                        .FirstOrDefault(vft => vft.ViewFamily == ViewFamily.FloorPlan);
                    Assert.IsNotNull(floorPlanVft, "FloorPlan ViewFamilyType must exist in docA.");
                    Level? levelA = new FilteredElementCollector(docA)
                        .OfClass(typeof(Level))
                        .Cast<Level>()
                        .FirstOrDefault();
                    Assert.IsNotNull(levelA, "A Level must exist in docA.");

                    viewA = ViewPlan.Create(docA, floorPlanVft!.Id, levelA!.Id);
                    viewA.Name = viewName;
                    viewA.DetailLevel = ViewDetailLevel.Fine;
                    viewA.DisplayStyle = DisplayStyle.ShadingWithEdges;
                    viewA.Scale = 50;
                    viewA.AddFilter(filterA.Id);
                    viewA.SetFilterVisibility(filterA.Id, false);
                    var filterOgs = new OverrideGraphicSettings();
                    filterOgs.SetSurfaceTransparency(25);
                    filterOgs.SetHalftone(true);
                    viewA.SetFilterOverrides(filterA.Id, filterOgs);
                });

                // Ensure docB has an AppearanceAssetElement template if MetricProject starts without one
                docB.ExecuteTransaction("Ensure AppearanceAsset Template in DocB", FailurePipelines.ObserveOnly(), () =>
                {
                    EnsureAppearanceAssetTemplate(docB);
                });

                var engine = new StandardSerializationEngine();
                var sourceElementsA = new Element[]
                {
                    viewA!,
                    wallTypeA!,
                    filterA!,
                    dimTypeA!,
                    matA!,
                    fpElemA!
                };

                // Act 1: ByRevit(docA) -> ToJson -> FromJson
                List<ObjectModel> extractedA = engine.ByRevit(sourceElementsA, docA, isTemplate: true).ToList();
                Assert.AreEqual(6, extractedA.Count, "All 6 multi-category elements must extract from docA.");

                string jsonA = StandardSerializationEngine.ToJson(extractedA);
                List<ElementModel> rehydrated = StandardSerializationEngine.FromJson(jsonA).ToList();
                Assert.AreEqual(6, rehydrated.Count, "All 6 models must rehydrate from JSON.");

                // Act 2: ToRevit(docB)
                List<SerializationResultModel> results = engine.ToRevit(rehydrated, docB).ToList();

                // Assert 1: All imports succeed
                Assert.AreEqual(6, results.Count, "Expected 6 import results in docB.");
                Assert.IsTrue(
                    results.All(r => r.Success),
                    "All ToRevit imports must succeed. Failures: " +
                    string.Join("; ", results.Where(r => !r.Success).Select(r => $"{(r.Model as ElementModel)?.Name}: {r.Message ?? r.ErrorMessage}")));

                // Assert 2: Every freshly rehydrated model resolves to a non-null element in docB
                // (prevents silent false passes where PocoToRevitDiffEngine skips unresolved elements)
                List<ElementModel> verificationModels = StandardSerializationEngine.FromJson(jsonA).ToList();
                var resolvedElementsB = new List<Element>();
                foreach (ElementModel model in verificationModels)
                {
                    Element? resolved = StandardTypeRegistry.ResolveTargetElement(model, docB);
                    Assert.IsNotNull(resolved,
                        $"ResolveTargetElement returned null in docB for '{model.Class}' named '{model.Name}'.");
                    resolvedElementsB.Add(resolved!);
                }

                // Assert 3: PocoToRevitDiffEngine reports zero diff against docB
                var diffEngine = new PocoToRevitDiffEngine(engine.IdentityService);
                List<DuplicateClusterModel> diffs = diffEngine.Compare(verificationModels, docB).ToList();
                Assert.IsEmpty(
                    diffs,
                    "PocoToRevitDiffEngine must report empty diff after round-trip. Conflicts: " +
                    string.Join("; ", diffs.SelectMany(c => c.TypeMappings).SelectMany(m => m.ParameterResolutions).Select(p => $"{p.ParameterName}[{string.Join(" vs ", p.ValueList)}]")));

                // Assert 4: ByRevit(docB) -> ToJson parity with docA
                List<ObjectModel> extractedB = engine.ByRevit(resolvedElementsB, docB, isTemplate: true).ToList();
                Assert.AreEqual(6, extractedB.Count, "All 6 resolved elements in docB must re-extract cleanly.");
                string jsonB = StandardSerializationEngine.ToJson(extractedB);

                string normalizedJsonA = NormalizeCrossDocumentJson(jsonA);
                string normalizedJsonB = NormalizeCrossDocumentJson(jsonB);

                Assert.AreEqual(
                    normalizedJsonA,
                    normalizedJsonB,
                    "Normalized ByRevit(docA)->ToJson must match ByRevit(docB)->ToJson with zero drift.");
                Assert.IsTrue(
                    JToken.DeepEquals(JObject.Parse(normalizedJsonA), JObject.Parse(normalizedJsonB)),
                    "Normalized structural JSON parity failed between docA and docB.");
            });
        }

        private static AppearanceAssetElement EnsureAppearanceAssetTemplate(Document doc)
        {
            AppearanceAssetElement? existing = new FilteredElementCollector(doc)
                .OfClass(typeof(AppearanceAssetElement))
                .Cast<AppearanceAssetElement>()
                .FirstOrDefault();
            if (existing != null)
            {
                return existing;
            }

            IList<Asset> assets = doc.Application.GetAssets(AssetType.Appearance);
            Asset? asset = assets.FirstOrDefault(a => a.FindByName("generic_diffuse") != null)
                           ?? assets.FirstOrDefault();
            Assert.IsNotNull(asset, "Revit application appearance asset library must provide at least one asset.");
            return AppearanceAssetElement.Create(doc, "SeedAppearanceTemplate206", asset!);
        }

        private static string NormalizeCrossDocumentJson(string json)
        {
            JObject root = JObject.Parse(json);
            ScrubVolatileDocumentIds(root);
            return root.ToString(Formatting.Indented).Replace("\r\n", "\n").Trim();
        }

        private static void ScrubVolatileDocumentIds(JToken token)
        {
            if (token is JObject obj)
            {
                JProperty? idProp = obj.Property("Id");
                if (idProp != null && idProp.Value.Type == JTokenType.Integer)
                {
                    long idVal = idProp.Value.Value<long>();
                    if (idVal > 0)
                    {
                        idProp.Value = 0L;
                    }
                }

                JProperty? uniqueIdProp = obj.Property("UniqueId");
                if (uniqueIdProp != null && uniqueIdProp.Value.Type == JTokenType.String)
                {
                    if (!string.IsNullOrEmpty(uniqueIdProp.Value.Value<string>()))
                    {
                        uniqueIdProp.Value = JValue.CreateNull();
                    }
                }

                foreach (JProperty prop in obj.Properties())
                {
                    ScrubVolatileDocumentIds(prop.Value);
                }
            }
            else if (token is JArray array)
            {
                foreach (JToken item in array)
                {
                    ScrubVolatileDocumentIds(item);
                }
            }
        }
    }
}
