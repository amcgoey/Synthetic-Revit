using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Autodesk.Revit.UI;
using Autodesk.Revit.DB;
using System.Threading;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;

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
    }
}
