using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Autodesk.Revit.DB;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM.Operations.Diffing;

namespace SyntheticTests.Modules.RevitDOM
{
    [TestFixture]
    [QuarantineTest]
    public class StandardsDisambiguationTests
    {
        [Test]
        public void MatchesDisambiguators_DimensionType_ValidatesDimensionStyle()
        {
            var liveLinear = new DimensionType { Name = "Standard", StyleType = DimensionStyleType.Linear };
            var liveAngular = new DimensionType { Name = "Standard", StyleType = DimensionStyleType.Angular };

            var modelLinear = new DimensionTypeModel { Name = "Standard", DimensionStyle = "Linear" };
            var modelAngular = new DimensionTypeModel { Name = "Standard", DimensionStyle = "Angular" };
            var modelWildcard = new DimensionTypeModel { Name = "Standard", DimensionStyle = "" };

            Assert.IsTrue(StandardTypeRegistry.MatchesDisambiguators(modelLinear, liveLinear));
            Assert.IsFalse(StandardTypeRegistry.MatchesDisambiguators(modelLinear, liveAngular));

            Assert.IsFalse(StandardTypeRegistry.MatchesDisambiguators(modelAngular, liveLinear));
            Assert.IsTrue(StandardTypeRegistry.MatchesDisambiguators(modelAngular, liveAngular));

            // Legacy JSON empty DimensionStyle acts as wildcard
            Assert.IsTrue(StandardTypeRegistry.MatchesDisambiguators(modelWildcard, liveLinear));
            Assert.IsTrue(StandardTypeRegistry.MatchesDisambiguators(modelWildcard, liveAngular));
        }

        [Test]
        public void MatchesDisambiguators_FillPatternElement_ValidatesTarget()
        {
            var liveDrafting = new FillPatternElement { Name = "Hatch" };
            liveDrafting.SetFillPattern(new FillPattern("Hatch", FillPatternTarget.Drafting, FillPatternHostOrientation.ToHost));

            var liveModel = new FillPatternElement { Name = "Hatch" };
            liveModel.SetFillPattern(new FillPattern("Hatch", FillPatternTarget.Model, FillPatternHostOrientation.ToHost));

            var modelDrafting = new FillPatternElementModel
            {
                Name = "Hatch",
                Pattern = new FillPatternModel { Name = "Hatch", Target = "Drafting" }
            };
            var modelPatternModel = new FillPatternElementModel
            {
                Name = "Hatch",
                Pattern = new FillPatternModel { Name = "Hatch", Target = "Model" }
            };
            var modelWildcard = new FillPatternElementModel
            {
                Name = "Hatch",
                Pattern = null
            };

            Assert.IsTrue(StandardTypeRegistry.MatchesDisambiguators(modelDrafting, liveDrafting));
            Assert.IsFalse(StandardTypeRegistry.MatchesDisambiguators(modelDrafting, liveModel));

            Assert.IsFalse(StandardTypeRegistry.MatchesDisambiguators(modelPatternModel, liveDrafting));
            Assert.IsTrue(StandardTypeRegistry.MatchesDisambiguators(modelPatternModel, liveModel));

            // Legacy / empty pattern acts as wildcard
            Assert.IsTrue(StandardTypeRegistry.MatchesDisambiguators(modelWildcard, liveDrafting));
            Assert.IsTrue(StandardTypeRegistry.MatchesDisambiguators(modelWildcard, liveModel));
        }

        [Test]
        public void MatchesDisambiguators_ElementType_ValidatesFamilyNameAndCategory()
        {
            var liveBasicWall = new WallType { Name = "Partition", FamilyName = "Basic Wall" };
            liveBasicWall.Category = new Category { Name = "Walls" };

            var liveCurtainWall = new WallType { Name = "Partition", FamilyName = "Curtain Wall" };
            liveCurtainWall.Category = new Category { Name = "Walls" };

            var modelBasic = new HostObjTypeModel { Name = "Partition", FamilyName = "Basic Wall", Category = "Walls" };
            var modelCurtain = new HostObjTypeModel { Name = "Partition", FamilyName = "Curtain Wall", Category = "Walls" };
            var modelMismatchCat = new HostObjTypeModel { Name = "Partition", FamilyName = "Basic Wall", Category = "Floors" };
            var modelWildcardFamily = new HostObjTypeModel { Name = "Partition", FamilyName = "", Category = "Walls" };

            Assert.IsTrue(StandardTypeRegistry.MatchesDisambiguators(modelBasic, liveBasicWall));
            Assert.IsFalse(StandardTypeRegistry.MatchesDisambiguators(modelBasic, liveCurtainWall));

            Assert.IsFalse(StandardTypeRegistry.MatchesDisambiguators(modelCurtain, liveBasicWall));
            Assert.IsTrue(StandardTypeRegistry.MatchesDisambiguators(modelCurtain, liveCurtainWall));

            // Category mismatch rejects
            Assert.IsFalse(StandardTypeRegistry.MatchesDisambiguators(modelMismatchCat, liveBasicWall));

            // Empty FamilyName acts as wildcard
            Assert.IsTrue(StandardTypeRegistry.MatchesDisambiguators(modelWildcardFamily, liveBasicWall));
            Assert.IsTrue(StandardTypeRegistry.MatchesDisambiguators(modelWildcardFamily, liveCurtainWall));
        }

        [Test]
        public void ResolveTargetElement_DisambiguatesCollidingSameNameElements()
        {
            var doc = (Document)Activator.CreateInstance(typeof(Document), true)!;

            // Setup doc with colliding DimensionTypes
            var dimLinear = new DimensionType { Name = "Standard", StyleType = DimensionStyleType.Linear };
            doc.AddElement(dimLinear, new ElementId(101));

            var dimAngular = new DimensionType { Name = "Standard", StyleType = DimensionStyleType.Angular };
            doc.AddElement(dimAngular, new ElementId(102));

            // Setup doc with colliding FillPatternElements
            var fpDrafting = new FillPatternElement { Name = "Crosshatch" };
            fpDrafting.SetFillPattern(new FillPattern("Crosshatch", FillPatternTarget.Drafting, FillPatternHostOrientation.ToHost));
            doc.AddElement(fpDrafting, new ElementId(201));

            var fpModel = new FillPatternElement { Name = "Crosshatch" };
            fpModel.SetFillPattern(new FillPattern("Crosshatch", FillPatternTarget.Model, FillPatternHostOrientation.ToHost));
            doc.AddElement(fpModel, new ElementId(202));

            // Setup doc with colliding WallTypes
            var wallBasic = new WallType { Name = "Generic 8\"", FamilyName = "Basic Wall" };
            doc.AddElement(wallBasic, new ElementId(301));

            var wallCurtain = new WallType { Name = "Generic 8\"", FamilyName = "Curtain Wall" };
            doc.AddElement(wallCurtain, new ElementId(302));

            // Test 1: Resolve DimensionTypes
            var modelDimLinear = new DimensionTypeModel { Name = "Standard", Class = "Autodesk.Revit.DB.DimensionType", DimensionStyle = "Linear" };
            var resolvedDimLinear = StandardTypeRegistry.ResolveTargetElement(modelDimLinear, doc);
            Assert.AreSame(dimLinear, resolvedDimLinear);

            var modelDimAngular = new DimensionTypeModel { Name = "Standard", Class = "Autodesk.Revit.DB.DimensionType", DimensionStyle = "Angular" };
            var resolvedDimAngular = StandardTypeRegistry.ResolveTargetElement(modelDimAngular, doc);
            Assert.AreSame(dimAngular, resolvedDimAngular);

            // Test 2: Resolve FillPatternElements
            var modelFpDrafting = new FillPatternElementModel
            {
                Name = "Crosshatch",
                Class = "Autodesk.Revit.DB.FillPatternElement",
                Pattern = new FillPatternModel { Name = "Crosshatch", Target = "Drafting" }
            };
            var resolvedFpDrafting = StandardTypeRegistry.ResolveTargetElement(modelFpDrafting, doc);
            Assert.AreSame(fpDrafting, resolvedFpDrafting);

            var modelFpModel = new FillPatternElementModel
            {
                Name = "Crosshatch",
                Class = "Autodesk.Revit.DB.FillPatternElement",
                Pattern = new FillPatternModel { Name = "Crosshatch", Target = "Model" }
            };
            var resolvedFpModel = StandardTypeRegistry.ResolveTargetElement(modelFpModel, doc);
            Assert.AreSame(fpModel, resolvedFpModel);

            // Test 3: Resolve WallTypes
            var modelWallBasic = new HostObjTypeModel { Name = "Generic 8\"", Class = "Autodesk.Revit.DB.WallType", FamilyName = "Basic Wall" };
            var resolvedWallBasic = StandardTypeRegistry.ResolveTargetElement(modelWallBasic, doc);
            Assert.AreSame(wallBasic, resolvedWallBasic);

            var modelWallCurtain = new HostObjTypeModel { Name = "Generic 8\"", Class = "Autodesk.Revit.DB.WallType", FamilyName = "Curtain Wall" };
            var resolvedWallCurtain = StandardTypeRegistry.ResolveTargetElement(modelWallCurtain, doc);
            Assert.AreSame(wallCurtain, resolvedWallCurtain);

            // Test 4: Legacy JSON with empty FamilyName acts as wildcard matching first
            var modelWallLegacy = new HostObjTypeModel { Name = "Generic 8\"", Class = "Autodesk.Revit.DB.WallType", FamilyName = "" };
            var resolvedWallLegacy = StandardTypeRegistry.ResolveTargetElement(modelWallLegacy, doc);
            Assert.IsNotNull(resolvedWallLegacy);
            Assert.AreEqual("Generic 8\"", resolvedWallLegacy!.Name);
        }

        [Test]
        public void ResolveTargetElement_GuardedPositiveId_ConflictsDiscardCandidate()
        {
            var doc = (Document)Activator.CreateInstance(typeof(Document), true)!;

            // In doc, element with Id 500 is a Linear dimension style
            var liveLinear = new DimensionType { Name = "LinearStyle", StyleType = DimensionStyleType.Linear };
            doc.AddElement(liveLinear, new ElementId(500));

            // In doc, element with Id 600 is an Angular dimension style named "MyAngular"
            var liveAngular = new DimensionType { Name = "MyAngular", StyleType = DimensionStyleType.Angular };
            doc.AddElement(liveAngular, new ElementId(600));

            // Incoming model has Id = 500 from docA, but Name = "MyAngular" and DimensionStyle = "Angular"
            var incomingModel = new DimensionTypeModel
            {
                Id = 500,
                Name = "MyAngular",
                Class = "Autodesk.Revit.DB.DimensionType",
                DimensionStyle = "Angular"
            };

            var identityService = new RevitIdentityService();

            // When allowPositiveId is false (cross-document safety):
            // Positive Id 500 is ignored, resolves cleanly to liveAngular by (Class, Name, Disambiguator)
            var resolvedCrossDoc = StandardTypeRegistry.ResolveTargetElement(incomingModel, doc, identityService, allowPositiveId: false);
            Assert.AreSame(liveAngular, resolvedCrossDoc, "Cross-document resolution must ignore colliding positive Id.");

            // When allowPositiveId is true (same doc), but candidate 500 has conflicting StyleType (Linear vs Angular):
            // Disambiguator conflict discards candidate 500 and falls back to (Class, Name) matching liveAngular!
            var resolvedSameDoc = StandardTypeRegistry.ResolveTargetElement(incomingModel, doc, identityService, allowPositiveId: true);
            Assert.AreSame(liveAngular, resolvedSameDoc, "Disambiguator conflict must discard ID candidate and fall back cleanly.");
        }

        [Test]
        public void DimensionTypeTranslator_InjectSpecifics_ContextAwareTemplateDuplication()
        {
            var doc = (Document)Activator.CreateInstance(typeof(Document), true)!;

            // Template 1: Linear
            var linearTmpl = new DimensionType { Name = "Linear Seed", StyleType = DimensionStyleType.Linear };
            doc.AddElement(linearTmpl, new ElementId(10));

            // Template 2: Angular
            var angularTmpl = new DimensionType { Name = "Angular Seed", StyleType = DimensionStyleType.Angular };
            doc.AddElement(angularTmpl, new ElementId(20));

            // Template 3: Internal linear style (must be excluded)
            var internalTmpl = new DimensionType { Name = DimensionTypeModel.InternalDimStyleName, StyleType = DimensionStyleType.Linear };
            doc.AddElement(internalTmpl, new ElementId(30));

            var translator = new DimensionTypeTranslator();

            // Duplicating Angular style prefers Angular seed
            var angularModel = new DimensionTypeModel { Name = "NewAngular", DimensionStyle = "Angular" };
            var duplicatedAngular = translator.InjectSpecifics(angularModel, null, doc);
            Assert.IsNotNull(duplicatedAngular);
            Assert.AreEqual("NewAngular", duplicatedAngular!.Name);
            Assert.AreEqual(DimensionStyleType.Angular, duplicatedAngular.StyleType);

            // Legacy model with empty DimensionStyle falls back cleanly to first non-internal template
            var legacyModel = new DimensionTypeModel { Name = "NewLegacy", DimensionStyle = "" };
            var duplicatedLegacy = translator.InjectSpecifics(legacyModel, null, doc);
            Assert.IsNotNull(duplicatedLegacy);
            Assert.AreEqual("NewLegacy", duplicatedLegacy!.Name);
            Assert.AreNotEqual(DimensionTypeModel.InternalDimStyleName, duplicatedLegacy.Name);
        }

        [Test]
        public void TemplateDuplicatingTranslator_ContextAwareFamilyNameSeedPreference()
        {
            var doc = (Document)Activator.CreateInstance(typeof(Document), true)!;

            var basicSeed = new WallType { Name = "Basic Seed", FamilyName = "Basic Wall" };
            doc.AddElement(basicSeed, new ElementId(1));

            var curtainSeed = new WallType { Name = "Curtain Seed", FamilyName = "Curtain Wall" };
            doc.AddElement(curtainSeed, new ElementId(2));

            var translator = new HostObjTypeTranslator(new RevitIdentityService());

            // Model requesting Curtain Wall must duplicate from Curtain Seed
            var curtainModel = new HostObjTypeModel { Name = "NewCurtain", Class = "Autodesk.Revit.DB.WallType", FamilyName = "Curtain Wall" };
            var duplicatedCurtain = translator.InjectSpecifics(curtainModel, null, doc);
            Assert.IsNotNull(duplicatedCurtain);
            Assert.AreEqual("NewCurtain", duplicatedCurtain!.Name);
            Assert.AreEqual("Curtain Wall", duplicatedCurtain.FamilyName);

            // Model requesting Basic Wall must duplicate from Basic Seed
            var basicModel = new HostObjTypeModel { Name = "NewBasic", Class = "Autodesk.Revit.DB.WallType", FamilyName = "Basic Wall" };
            var duplicatedBasic = translator.InjectSpecifics(basicModel, null, doc);
            Assert.IsNotNull(duplicatedBasic);
            Assert.AreEqual("NewBasic", duplicatedBasic!.Name);
            Assert.AreEqual("Basic Wall", duplicatedBasic.FamilyName);

            // Legacy model with empty FamilyName falls back cleanly to first available candidate
            var legacyModel = new HostObjTypeModel { Name = "NewLegacy", Class = "Autodesk.Revit.DB.WallType", FamilyName = "" };
            var duplicatedLegacy = translator.InjectSpecifics(legacyModel, null, doc);
            Assert.IsNotNull(duplicatedLegacy);
            Assert.AreEqual("NewLegacy", duplicatedLegacy!.Name);
        }

        [Test]
        public void PocoToRevitDiffEngine_DisambiguatesCollidingStandardsAccurately()
        {
            var doc = (Document)Activator.CreateInstance(typeof(Document), true)!;

            // Live doc has Linear "Standard" and Angular "Standard"
            var dimLinear = new DimensionType { Name = "Standard", StyleType = DimensionStyleType.Linear };
            dimLinear.Category = new Category { Name = "Dimensions" };
            doc.AddElement(dimLinear, new ElementId(101));

            var dimAngular = new DimensionType { Name = "Standard", StyleType = DimensionStyleType.Angular };
            dimAngular.Category = new Category { Name = "Dimensions" };
            doc.AddElement(dimAngular, new ElementId(102));

            var diffEngine = new PocoToRevitDiffEngine();

            // Incoming Linear model with different parameter value
            var incomingLinear = new DimensionTypeModel
            {
                Name = "Standard",
                Class = "Autodesk.Revit.DB.DimensionType",
                Category = "Dimensions",
                DimensionStyle = "Linear",
                Parameters = new List<ParameterModel>
                {
                    new ParameterModel { Name = "TestParam", StorageType = "String", Value = "LinearUpdated" }
                }
            };

            // Incoming Angular model with different parameter value
            var incomingAngular = new DimensionTypeModel
            {
                Name = "Standard",
                Class = "Autodesk.Revit.DB.DimensionType",
                Category = "Dimensions",
                DimensionStyle = "Angular",
                Parameters = new List<ParameterModel>
                {
                    new ParameterModel { Name = "TestParam", StorageType = "String", Value = "AngularUpdated" }
                }
            };

            var clusters = diffEngine.Compare(new[] { incomingLinear, incomingAngular }, doc).ToList();

            Assert.AreEqual(1, clusters.Count);
            var mappings = clusters[0].TypeMappings;
            Assert.AreEqual(2, mappings.Count);

            // Verify mapping 1 mapped to liveLinear (Id 101) and mapping 2 mapped to liveAngular (Id 102)
            var mappedLinear = mappings.FirstOrDefault(m => m.SourceType.RevitTypeId.Id == 101);
            Assert.IsNotNull(mappedLinear, "Linear incoming model must map to liveLinear (Id 101).");

            var mappedAngular = mappings.FirstOrDefault(m => m.SourceType.RevitTypeId.Id == 102);
            Assert.IsNotNull(mappedAngular, "Angular incoming model must map to liveAngular (Id 102).");
        }

        [Test]
        public void TemplateDuplicatingTranslator_ExplicitMissingFamilyName_ThrowsInvalidOperationException()
        {
            var doc = (Document)Activator.CreateInstance(typeof(Document), true)!;

            var basicSeed = new WallType { Name = "Basic Seed", FamilyName = "Basic Wall" };
            doc.AddElement(basicSeed, new ElementId(1));

            var translator = new HostObjTypeTranslator(new RevitIdentityService());

            // Explicit non-empty FamilyName that does not exist in doc must throw (no cross-family fallback)
            var missingFamilyModel = new HostObjTypeModel
            {
                Name = "NewCurtain",
                Class = "Autodesk.Revit.DB.WallType",
                FamilyName = "Curtain Wall"
            };

            var ex = Assert.Throws<InvalidOperationException>(() => translator.InjectSpecifics(missingFamilyModel, null, doc));
            Assert.IsTrue(ex!.Message.Contains("Curtain Wall"), "Exception must name the missing FamilyName.");

            // Omitted FamilyName falls back cleanly to available candidate
            var omittedModel = new HostObjTypeModel
            {
                Name = "OmittedFallback",
                Class = "Autodesk.Revit.DB.WallType",
                FamilyName = ""
            };
            var duplicated = translator.InjectSpecifics(omittedModel, null, doc);
            Assert.IsNotNull(duplicated);
            Assert.AreEqual("OmittedFallback", duplicated!.Name);
        }

        [Test]
        public void FamilySymbol_MatchesDisambiguators_ValidatesCategoryAndFamilyName()
        {
            var doorCat = new Category { Name = "Doors" };
            var winCat = new Category { Name = "Windows" };

            var famA = new Family { Name = "Single-Flush", FamilyCategory = doorCat };
            var famB = new Family { Name = "Double-Flush", FamilyCategory = doorCat };
            var famC = new Family { Name = "Fixed", FamilyCategory = winCat };

            var symA = new FamilySymbol { Name = "Standard", Category = doorCat, Family = famA };
            var symB = new FamilySymbol { Name = "Standard", Category = doorCat, Family = famB };
            var symC = new FamilySymbol { Name = "Standard", Category = winCat, Family = famC };

            var modelA = new ElementTypeModel
            {
                Name = "Standard",
                Class = "Autodesk.Revit.DB.FamilySymbol",
                Category = "Doors",
                FamilyName = "Single-Flush"
            };

            Assert.IsTrue(StandardTypeRegistry.MatchesDisambiguators(modelA, symA));
            Assert.IsFalse(StandardTypeRegistry.MatchesDisambiguators(modelA, symB), "Different FamilyName must be rejected.");
            Assert.IsFalse(StandardTypeRegistry.MatchesDisambiguators(modelA, symC), "Different Category must be rejected.");
        }

        [Test]
        public void FamilySymbol_ResolveTargetElement_LegacyEmptyFamilyName_WildcardOnlyWhenUnambiguous()
        {
            var doc = (Document)Activator.CreateInstance(typeof(Document), true)!;
            var doorCat = new Category { Name = "Doors" };
            var winCat = new Category { Name = "Windows" };

            var famA = new Family { Name = "Single-Flush", FamilyCategory = doorCat };
            var famB = new Family { Name = "Double-Flush", FamilyCategory = doorCat };
            var famWin = new Family { Name = "Single-Window", FamilyCategory = winCat };

            var symA = new FamilySymbol { Name = "36x84", Category = doorCat, Family = famA };
            doc.AddElement(symA, new ElementId(101));

            var symB = new FamilySymbol { Name = "36x84", Category = doorCat, Family = famB };
            doc.AddElement(symB, new ElementId(102));

            var symWin = new FamilySymbol { Name = "UniqueWin", Category = winCat, Family = famWin };
            doc.AddElement(symWin, new ElementId(103));

            // Ambiguous across two families in same category with empty FamilyName -> must return null
            var ambiguousModel = new ElementTypeModel
            {
                Name = "36x84",
                Class = "Autodesk.Revit.DB.FamilySymbol",
                Category = "Doors",
                FamilyName = ""
            };
            Assert.IsNull(StandardTypeRegistry.ResolveTargetElement(ambiguousModel, doc), "Ambiguous legacy FamilySymbol must return null.");

            // Unambiguous with empty FamilyName -> resolves cleanly
            var unambiguousModel = new ElementTypeModel
            {
                Name = "UniqueWin",
                Class = "Autodesk.Revit.DB.FamilySymbol",
                Category = "Windows",
                FamilyName = ""
            };
            Assert.AreSame(symWin, StandardTypeRegistry.ResolveTargetElement(unambiguousModel, doc));
        }

        [Test]
        public void FamilySymbolTranslator_InjectSpecifics_DuplicatesSiblingInMatchingFamilyAndThrowsWhenMissing()
        {
            var doc = (Document)Activator.CreateInstance(typeof(Document), true)!;
            var doorCat = new Category { Name = "Doors" };
            var famA = new Family { Name = "Single-Flush", FamilyCategory = doorCat };
            var seedA = new FamilySymbol { Name = "30x84", Category = doorCat, Family = famA };
            doc.AddElement(seedA, new ElementId(201));

            var translator = new FamilySymbolTranslator();

            // 1. Sibling duplication within matching loaded family
            var validModel = new ElementTypeModel
            {
                Name = "36x84",
                Class = "Autodesk.Revit.DB.FamilySymbol",
                Category = "Doors",
                FamilyName = "Single-Flush"
            };
            var duplicated = translator.InjectSpecifics(validModel, null, doc);
            Assert.IsNotNull(duplicated);
            Assert.AreEqual("36x84", duplicated!.Name);
            Assert.AreSame(famA, duplicated.Family);
            Assert.AreEqual("Doors", duplicated.Category?.Name);

            // 2. Unloaded family must throw (never duplicate unrelated famA)
            var unloadedModel = new ElementTypeModel
            {
                Name = "Revolving 72",
                Class = "Autodesk.Revit.DB.FamilySymbol",
                Category = "Doors",
                FamilyName = "Revolving-Door"
            };
            var ex = Assert.Throws<InvalidOperationException>(() => translator.InjectSpecifics(unloadedModel, null, doc));
            Assert.IsTrue(ex!.Message.Contains("Revolving-Door"), "Exception must name the missing FamilyName.");
        }

        [Test]
        public void ViewFamilyType_MatchesDisambiguators_ValidatesViewFamilyAndFallbacks()
        {
            var vftFloor = new ViewFamilyType { Name = "Standard", ViewFamily = ViewFamily.FloorPlan, FamilyName = "Floor Plan" };
            var vftCeiling = new ViewFamilyType { Name = "Standard", ViewFamily = ViewFamily.CeilingPlan, FamilyName = "Ceiling Plan" };

            // 1. Explicit ViewFamily match
            var byViewFamily = new ElementTypeModel { Name = "Standard", Class = "Autodesk.Revit.DB.ViewFamilyType", ViewFamily = "CeilingPlan" };
            Assert.IsFalse(StandardTypeRegistry.MatchesDisambiguators(byViewFamily, vftFloor));
            Assert.IsTrue(StandardTypeRegistry.MatchesDisambiguators(byViewFamily, vftCeiling));

            // 2. Legacy FamilyName fallback when ViewFamily is null
            var byFamilyName = new ElementTypeModel { Name = "Standard", Class = "Autodesk.Revit.DB.ViewFamilyType", ViewFamily = null, FamilyName = "Floor Plan" };
            Assert.IsTrue(StandardTypeRegistry.MatchesDisambiguators(byFamilyName, vftFloor));
            Assert.IsFalse(StandardTypeRegistry.MatchesDisambiguators(byFamilyName, vftCeiling));

            // 3. Legacy wildcard when both ViewFamily and FamilyName are empty
            var wildcard = new ElementTypeModel { Name = "Standard", Class = "Autodesk.Revit.DB.ViewFamilyType", ViewFamily = null, FamilyName = "" };
            Assert.IsTrue(StandardTypeRegistry.MatchesDisambiguators(wildcard, vftFloor));
            Assert.IsTrue(StandardTypeRegistry.MatchesDisambiguators(wildcard, vftCeiling));
        }

        private class ThrowingOnDuplicateViewFamilyType : ViewFamilyType
        {
            public override ElementType Duplicate(string name)
            {
                throw new Autodesk.Revit.Exceptions.InvalidOperationException("ViewFamilyType cannot be duplicated.");
            }
        }

        [Test]
        public void ViewFamilyTypeTranslator_InjectSpecifics_ChecksCanBeCopiedAndCatchesRevitInvalidOperationException()
        {
            var doc = (Document)Activator.CreateInstance(typeof(Document), true)!;

            var nonCopiable = new ViewFamilyType { Name = "Schedule Seed", ViewFamily = ViewFamily.Schedule, CanBeCopied = false };
            doc.AddElement(nonCopiable, new ElementId(301));

            var throwingSeed = new ThrowingOnDuplicateViewFamilyType { Name = "Legend Seed", ViewFamily = ViewFamily.Legend, CanBeCopied = true };
            doc.AddElement(throwingSeed, new ElementId(302));

            var translator = new ViewFamilyTypeTranslator();

            // 1. CanBeCopied == false throws System.InvalidOperationException
            var scheduleModel = new ElementTypeModel { Name = "New Schedule", Class = "Autodesk.Revit.DB.ViewFamilyType", ViewFamily = "Schedule" };
            var ex1 = Assert.Throws<System.InvalidOperationException>(() => translator.InjectSpecifics(scheduleModel, null, doc));
            Assert.IsTrue(ex1!.Message.Contains("cannot be copied"), "Error should report non-copiable ViewFamilyType.");

            // 2. Revit InvalidOperationException during Duplicate is wrapped in System.InvalidOperationException
            var legendModel = new ElementTypeModel { Name = "New Legend", Class = "Autodesk.Revit.DB.ViewFamilyType", ViewFamily = "Legend" };
            var ex2 = Assert.Throws<System.InvalidOperationException>(() => translator.InjectSpecifics(legendModel, null, doc));
            Assert.IsInstanceOf<Autodesk.Revit.Exceptions.InvalidOperationException>(ex2!.InnerException);
        }
    }
}
