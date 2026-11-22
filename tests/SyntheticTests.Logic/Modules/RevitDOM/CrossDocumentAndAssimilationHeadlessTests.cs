using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using NUnit.Framework;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM.Translation;

namespace SyntheticTests.Modules.RevitDOM
{
    [TestFixture]
    public class CrossDocumentAndAssimilationHeadlessTests
    {
        [Test]
        public void ToRevit_WhenTargetingDifferentDocument_ClearsStaleReferencesAndGuardsPositiveId()
        {
            var docA = new Document { Title = "DocA" };
            var docB = new Document { Title = "DocB" };

            // In docA, create an element with positive ID 555
            var matA = new Material { Name = "StandardConcrete" };
            docA.AddElement(matA, new ElementId(555));

            // In docB, create an unrelated element with the same positive ID 555
            var matB = new Material { Name = "UnrelatedWood" };
            docB.AddElement(matB, new ElementId(555));

            // Incoming model was extracted from docA as a live element (not a template)
            var modelA = new MaterialModel
            {
                Name = "StandardConcrete",
                Class = "Autodesk.Revit.DB.Material",
                Id = 555,
                Element = matA,
                Document = docA,
                IsTemplate = false
            };

            var engine = new StandardSerializationEngine();

            // Act: import into docB
            var results = engine.ToRevit(new[] { modelA }, docB).ToList();

            // Assert:
            // 1. Unrelated element in docB was NOT matched or overwritten
            Assert.AreEqual("UnrelatedWood", matB.Name, "Unrelated element with same ID in docB must not be overwritten.");

            // 2. A new element was created in docB for StandardConcrete
            var createdElem = docB.GetElements().OfType<Material>().FirstOrDefault(m => m.Name == "StandardConcrete");
            Assert.IsNotNull(createdElem, "StandardConcrete should be created in docB.");
            Assert.AreNotEqual(555, createdElem!.Id.IntegerValue, "Created element must have its own ID in docB.");

            // 3. Stale references to docA were cleared and rebound to docB
            Assert.AreEqual(docB, modelA.Document, "Model document should be updated to docB.");
            Assert.IsNotNull(modelA.Element, "Model element should not be null.");
            var boundElem = (Material)modelA.Element!;
            Assert.AreEqual(createdElem.Id, boundElem.Id, "Model element must be rebound to docB's element.");
            Assert.AreEqual(docB, boundElem.Document, "Model element must belong to docB.");

            // 4. Result Action is Created
            Assert.AreEqual(1, results.Count);
            Assert.IsTrue(results[0].Success);
            Assert.AreEqual("Created", results[0].Action);
        }

        [Test]
        public void ToRevit_WhenTargetingDifferentDocument_ClearsSubclassLiveReferences()
        {
            var docA = new Document { Title = "DocA" };
            var docB = new Document { Title = "DocB" };

            var dummyObj = new object();

            var elemTypeModel = new ElementTypeModel
            {
                Name = "TypeA",
                Class = "Autodesk.Revit.DB.ElementType",
                Document = docA,
                Element = dummyObj,
                ElementType = dummyObj,
                IsTemplate = false
            };

            var dimTypeModel = new DimensionTypeModel
            {
                Name = "DimA",
                Class = "Autodesk.Revit.DB.DimensionType",
                Document = docA,
                Element = dummyObj,
                DimensionType = dummyObj,
                IsTemplate = false
            };

            var hostTypeModel = new HostObjTypeModel
            {
                Name = "HostA",
                Class = "Autodesk.Revit.DB.WallType",
                Document = docA,
                Element = dummyObj,
                WallType = dummyObj,
                IsTemplate = false
            };

            var viewModel = new ViewModel
            {
                Name = "ViewA",
                Class = "Autodesk.Revit.DB.View",
                Document = docA,
                Element = dummyObj,
                View = dummyObj,
                IsTemplate = false
            };

            var catModel = new CategoryModel
            {
                Name = "CatA",
                Class = "Autodesk.Revit.DB.Category",
                Document = docA,
                Element = dummyObj,
                RevitCategory = dummyObj,
                IsTemplate = false
            };

            var engine = new StandardSerializationEngine();

            // Run ToRevit against docB
            engine.ToRevit(new ElementModel[] { elemTypeModel, dimTypeModel, hostTypeModel, viewModel, catModel }, docB);

            // Assert that live references to docA were cleared before resolving/injecting
            Assert.IsNull(elemTypeModel.ElementType, "ElementTypeModel.ElementType should be cleared.");
            Assert.IsNull(dimTypeModel.DimensionType, "DimensionTypeModel.DimensionType should be cleared.");
            Assert.IsNull(hostTypeModel.WallType, "HostObjTypeModel.WallType should be cleared.");
            Assert.IsNull(viewModel.View, "ViewModel.View should be cleared.");
            Assert.IsNull(catModel.RevitCategory, "CategoryModel.RevitCategory should be cleared.");
        }

        [Test]
        public void RevitIdentityService_ResolveElement_WhenIsTemplateTrue_IgnoresPositiveId_AllowsNegativeId()
        {
            var doc = new Document { Title = "Doc" };
            var elemPos = new Material { Name = "PosElem" };
            doc.AddElement(elemPos, new ElementId(777));

            var elemNeg = new Material { Name = "NegElem" };
            doc.AddElement(elemNeg, new ElementId(-777));

            var service = new RevitIdentityService();

            // Template with positive ID: must return null (guard prevents ID lookup)
            var modelPosTemplate = new ElementIdModel { Id = 777, IsTemplate = true };
            var resPosTemplate = service.ResolveElement(modelPosTemplate, doc);
            Assert.IsNull(resPosTemplate, "Template positive ID lookup must be ignored.");

            // Non-template with positive ID: must resolve
            var modelPosNonTemplate = new ElementIdModel { Id = 777, IsTemplate = false };
            var resPosNonTemplate = service.ResolveElement(modelPosNonTemplate, doc);
            Assert.AreEqual(elemPos, resPosNonTemplate, "Non-template positive ID lookup must succeed.");

            // Template with negative ID: must resolve (built-in IDs are universal)
            var modelNegTemplate = new ElementIdModel { Id = -777, IsTemplate = true };
            var resNegTemplate = service.ResolveElement(modelNegTemplate, doc);
            Assert.AreEqual(elemNeg, resNegTemplate, "Template negative built-in ID lookup must succeed.");
        }

        [Test]
        public void ToRevit_AliasSwappingAndPurging_CommitsInsideTransactionGroupBeforeAssimilate()
        {
            var doc = new Document { Title = "TestDoc" };

            // Create an alias element in doc
            var aliasMat = new Material { Name = "OldAliasMat" };
            doc.AddElement(aliasMat, new ElementId(888));

            var primaryModel = new MaterialModel
            {
                Name = "NewPrimaryMat",
                Class = "Autodesk.Revit.DB.Material",
                Aliases = new List<string> { "OldAliasMat" }
            };

            var engine = new StandardSerializationEngine();
            var results = engine.ToRevit(new[] { primaryModel }, doc).ToList();

            var failRes = results.FirstOrDefault(r => r.Action == "Alias Swap Failed");
            if (failRes != null)
            {
                Assert.Fail($"Alias swap failed with: {failRes.ErrorMessage} (Exception: {failRes.Exception})");
            }

            // Check TransactionGroup tracking in MockRevitAPI
            var tg = TransactionGroup.LastCreatedGroup;
            Assert.IsNotNull(tg, "TransactionGroup should have been created.");
            Assert.IsTrue(tg!.WasAssimilated, "TransactionGroup must be assimilated.");
            Assert.IsTrue(tg.CommittedTransactionNames.Contains("Import NewPrimaryMat"), "Primary import transaction should have committed inside TransactionGroup.");
            Assert.IsTrue(tg.CommittedTransactionNames.Contains("Purge Alias Element"), "Purge Alias Element transaction should have committed inside TransactionGroup.");

            int primaryIdx = tg.CommittedTransactionNames.IndexOf("Import NewPrimaryMat");
            int purgeIdx = tg.CommittedTransactionNames.IndexOf("Purge Alias Element");
            Assert.Less(primaryIdx, purgeIdx, "Primary import must commit before Purge Alias Element.");

            // Assert alias element was deleted
            Assert.IsNull(doc.GetElement(new ElementId(888)), "Alias element must be deleted.");

            // Assert results
            Assert.IsTrue(results.Any(r => r.Success && r.Action == "Created"));
            Assert.IsTrue(results.Any(r => r.Success && r.Action == "Merged Alias"));
        }

        [Test]
        public void ToRevit_AliasSwapFailure_DegradesGracefullyAndStillAssimilates()
        {
            var doc = new Document { Title = "TestDoc" };

            // Create an alias element in doc
            var aliasMat = new Material { Name = "FailAliasMat" };
            doc.AddElement(aliasMat, new ElementId(999));

            var primaryModel = new MaterialModel
            {
                Name = "PrimaryMat_Degrade",
                Class = "Autodesk.Revit.DB.Material",
                Aliases = new List<string> { "FailAliasMat" }
            };

            // Custom IdentityService that throws when resolving alias for swap/purge
            var engine = new StandardSerializationEngine(new ThrowingAliasIdentityService(new RevitIdentityService(), new ElementId(999)));

            var results = engine.ToRevit(new[] { primaryModel }, doc).ToList();

            var tg = TransactionGroup.LastCreatedGroup;
            Assert.IsNotNull(tg, "TransactionGroup should have been created.");
            Assert.IsTrue(tg!.WasAssimilated, "TransactionGroup must still be assimilated despite alias failure.");

            // Primary creation succeeded
            var primaryResult = results.FirstOrDefault(r => r.Action == "Created");
            Assert.IsNotNull(primaryResult, "Primary element creation should succeed.");
            Assert.IsTrue(primaryResult!.Success);

            // Alias swap failure is recorded
            var aliasResult = results.FirstOrDefault(r => r.Action == "Alias Swap Failed");
            Assert.IsNotNull(aliasResult, "Alias failure should be reported with Action = 'Alias Swap Failed'.");
            Assert.IsFalse(aliasResult!.Success);
            Assert.AreEqual("Failed", aliasResult.Message.Substring(0, 6));
        }

        private class ThrowingAliasIdentityService : IIdentityService
        {
            private readonly IIdentityService _inner;
            private readonly ElementId _failId;

            public ThrowingAliasIdentityService(IIdentityService inner, ElementId failId)
            {
                _inner = inner;
                _failId = failId;
            }

            public ElementId ResolveElementId(ElementIdModel model, Document doc)
            {
                if (model?.Name == "FailAliasMat")
                {
                    throw new InvalidOperationException("Simulated alias resolution failure.");
                }
                return _inner.ResolveElementId(model, doc);
            }

            public Element ResolveElement(ElementIdModel model, Document doc) => _inner.ResolveElement(model, doc);
            public IEnumerable<Element> GetElementsByElementIdModels(Document doc, IEnumerable<ElementIdModel> identifiers) => _inner.GetElementsByElementIdModels(doc, identifiers);
            public ElementIdModel ToModel(ElementId id, Document doc, bool isTemplate = false) => _inner.ToModel(id, doc, isTemplate);
        }

        [Test]
        public void ToRevit_BatchIsolation_MissingFamilySymbolAndNonCopiableViewFamilyType_RollsBackInnerTransactionsOnly()
        {
            var doc = new Document { Title = "TargetProject" };

            // Load an unrelated family symbol and a non-copiable ViewFamilyType
            var doorCat = new Category { Name = "Doors" };
            var loadedFam = new Family { Name = "Loaded-Door", FamilyCategory = doorCat };
            var loadedSeed = new FamilySymbol { Name = "30x84", Category = doorCat, Family = loadedFam };
            doc.AddElement(loadedSeed, new ElementId(10));

            var scheduleSeed = new ViewFamilyType { Name = "Working Schedule", ViewFamily = ViewFamily.Schedule, CanBeCopied = false };
            doc.AddElement(scheduleSeed, new ElementId(20));

            var missingFamilySymbol = new ElementTypeModel
            {
                Name = "72x84",
                Class = "Autodesk.Revit.DB.FamilySymbol",
                Category = "Doors",
                FamilyName = "Unloaded-Revolving"
            };
            var nonCopiableVft = new ElementTypeModel
            {
                Name = "New Schedule",
                Class = "Autodesk.Revit.DB.ViewFamilyType",
                ViewFamily = "Schedule"
            };
            var validMaterial = new MaterialModel
            {
                Name = "ValidBatchMaterial",
                Class = "Autodesk.Revit.DB.Material"
            };

            var engine = new StandardSerializationEngine();
            var results = engine.ToRevit(new ObjectModel[] { missingFamilySymbol, nonCopiableVft, validMaterial }, doc).ToList();

            var tg = TransactionGroup.LastCreatedGroup;
            Assert.IsNotNull(tg);
            Assert.IsTrue(tg!.WasAssimilated, "Outer TransactionGroup must assimilate cleanly.");

            Assert.AreEqual(3, results.Count);

            var fsResult = results.First(r => r.Model == missingFamilySymbol);
            Assert.IsFalse(fsResult.Success);
            Assert.AreEqual("Failed", fsResult.Action);
            Assert.IsTrue(fsResult.Message.Contains("Unloaded-Revolving"), "Missing FamilySymbol error must name missing FamilyName.");

            var vftResult = results.First(r => r.Model == nonCopiableVft);
            Assert.IsFalse(vftResult.Success);
            Assert.AreEqual("Failed", vftResult.Action);

            var matResult = results.First(r => r.Model == validMaterial);
            Assert.IsTrue(matResult.Success, "Valid sibling item must commit cleanly.");
            Assert.AreEqual("Created", matResult.Action);
            Assert.IsTrue(doc.GetElements().OfType<Material>().Any(m => m.Name == "ValidBatchMaterial"));
        }

        [Test]
        public void GatherRootElements_CollectsViewFamilyTypes_ToposolidTypes_FiveWayFamilySymbols_AndRespectsCancellation()
        {
            var doc = new Document { Title = "RootGatherDoc" };

            var vft = new ViewFamilyType { Name = "Floor Plan VFT", ViewFamily = ViewFamily.FloorPlan };
            doc.AddElement(vft, new ElementId(101));

            var topo = new ToposolidType { Name = "Topo 1" };
            doc.AddElement(topo, new ElementId(102));

            var tbCat = new Category { Name = "Title Blocks", Id = new ElementId((long)BuiltInCategory.OST_TitleBlocks), CategoryType = CategoryType.Annotation };
            var diCat = new Category { Name = "Detail Items", Id = new ElementId((long)BuiltInCategory.OST_DetailComponents), CategoryType = CategoryType.Model };
            var profCat = new Category { Name = "Profiles", Id = new ElementId((long)BuiltInCategory.OST_ProfileFamilies), CategoryType = CategoryType.Model };
            var tagCat = new Category { Name = "Door Tags", Id = new ElementId(-2000450), CategoryType = CategoryType.Annotation };
            var modelCat = new Category { Name = "Doors", Id = new ElementId(-2000023), CategoryType = CategoryType.Model };

            var tbSym = new FamilySymbol { Name = "E1 30x42", Category = tbCat };
            var diSym = new FamilySymbol { Name = "Brick Course", Category = diCat };
            var profSym = new FamilySymbol { Name = "C-Channel", Category = profCat };
            var tagSym = new FamilySymbol { Name = "Door Tag 1", Category = tagCat };
            var doorSym = new FamilySymbol { Name = "36x84", Category = modelCat };

            doc.AddElement(tbSym, new ElementId(201));
            doc.AddElement(diSym, new ElementId(202));
            doc.AddElement(profSym, new ElementId(203));
            doc.AddElement(tagSym, new ElementId(204));
            doc.AddElement(doorSym, new ElementId(205));

            var engine = new StandardSerializationEngine();

            // 1. selectedGroupings == null gathers all including ViewFamilyType, ToposolidType, and all 5 FamilySymbol buckets
            var allElements = engine.GatherRootElements(doc, selectedGroupings: null).ToList();
            Assert.IsTrue(allElements.Contains(vft), "ViewFamilyType must be gathered when selectedGroupings is null.");
            Assert.IsTrue(allElements.Contains(topo), "ToposolidType must be gathered on 2024+ when selectedGroupings is null.");
            Assert.IsTrue(allElements.Contains(tbSym));
            Assert.IsTrue(allElements.Contains(diSym));
            Assert.IsTrue(allElements.Contains(profSym));
            Assert.IsTrue(allElements.Contains(tagSym));
            Assert.IsTrue(allElements.Contains(doorSym));

            // 2. Filtered selection gathers only matching buckets
            var onlyVftAndProfiles = engine.GatherRootElements(doc, new[] { "View Family Types", "Profiles" }).ToList();
            Assert.AreEqual(2, onlyVftAndProfiles.Count);
            Assert.IsTrue(onlyVftAndProfiles.Contains(vft));
            Assert.IsTrue(onlyVftAndProfiles.Contains(profSym));

            // 3. CancellationToken is honored
            using var cts = new System.Threading.CancellationTokenSource();
            cts.Cancel();
            Assert.Throws<OperationCanceledException>(() => engine.GatherRootElements(doc, null, cts.Token).ToList());
        }
    }
}
