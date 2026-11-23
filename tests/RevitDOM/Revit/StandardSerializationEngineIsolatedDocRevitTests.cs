using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Autodesk.Revit.DB;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;
using SyntheticTests.Fixtures;

namespace SyntheticTests.RevitDOM
{
    [TestFixture]
    [RevitIsolatedDocTest]
    public class StandardSerializationEngineIsolatedDocRevitTests : RevitIsolatedDocumentFixture
    {
        private (Document ProjectDoc, Document FamilyDoc) OpenProjectAndEditableFamilyDocs()
        {
            Document projDoc = NewTrackedProjectDocument(UnitSystem.Metric);
            Family? editableFamily = new FilteredElementCollector(projDoc)
                .OfClass(typeof(Family))
                .Cast<Family>()
                .FirstOrDefault(f => f.IsEditable);

            if (editableFamily != null)
            {
                Document famDoc = projDoc.EditFamily(editableFamily);
                if (famDoc != null)
                {
                    TrackDocument(famDoc);
                    return (projDoc, famDoc);
                }
            }

            projDoc.Close(false);

            projDoc = OpenIsolatedTestTemplateCopy();
            editableFamily = new FilteredElementCollector(projDoc)
                .OfClass(typeof(Family))
                .Cast<Family>()
                .FirstOrDefault(f => f.IsEditable);

            if (editableFamily != null)
            {
                Document famDoc = projDoc.EditFamily(editableFamily);
                if (famDoc != null)
                {
                    TrackDocument(famDoc);
                    return (projDoc, famDoc);
                }
            }

            throw new InvalidOperationException("Could not find an editable family in test document to open family document.");
        }

        [Test]
        public void ToRevit_DocumentScope_EnforcesUniversalFamilyAndProjectScopes()
        {
            var (projDoc, famDoc) = OpenProjectAndEditableFamilyDocs();

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

        [Test]
        public void Analyze_DocumentScope_OmitsOutOfScopeClustersInProjectAndFamilyDocs()
        {
            var (projDoc, famDoc) = OpenProjectAndEditableFamilyDocs();

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

        [Test]
        public void ToRevit_FamilySymbol_TwoDoc_LoadedSiblingDuplication_RoundTrip()
        {
            Document docA = OpenIsolatedTestTemplateCopy();
            Document docB = OpenIsolatedTestTemplateCopy();

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

        [Test]
        public void ToRevit_FamilySymbol_TwoDoc_ConflictingFamilyDoesNotOverwriteUnrelatedFamily()
        {
            Document docA = OpenIsolatedTestTemplateCopy();
            Document docB = OpenIsolatedTestTemplateCopy();

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

        [Test]
        public void ToRevit_FamilySymbol_TwoDoc_UnloadedFamilyRollsBackInnerTransactionOnly()
        {
            Document docA = OpenIsolatedTestTemplateCopy();
            Document docB = NewTrackedProjectDocument(UnitSystem.Metric);

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
    }
}
