using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Autodesk.Revit.DB;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM.Operations.Diffing;

namespace SyntheticTests.Modules.RevitDOM
{
    [TestFixture]
    public class DocumentScopeTests
    {
        [Test]
        public void GetDocumentScope_ResolvesCorrectScopeForEachStandardType()
        {
            // Universal types
            Assert.AreEqual(DocumentScope.Universal, StandardTypeRegistry.GetDocumentScope(new LinePatternElementModel { Class = "Autodesk.Revit.DB.LinePatternElement" }));
            Assert.AreEqual(DocumentScope.Universal, StandardTypeRegistry.GetDocumentScope(new FillPatternElementModel { Class = "Autodesk.Revit.DB.FillPatternElement" }));
            Assert.AreEqual(DocumentScope.Universal, StandardTypeRegistry.GetDocumentScope(new PropertySetElementModel { Class = "Autodesk.Revit.DB.PropertySetElement" }));
            Assert.AreEqual(DocumentScope.Universal, StandardTypeRegistry.GetDocumentScope(new MaterialModel { Class = "Autodesk.Revit.DB.Material" }));
            Assert.AreEqual(DocumentScope.Universal, StandardTypeRegistry.GetDocumentScope(new CategoryModel { Class = "Autodesk.Revit.DB.Category" }));
            Assert.AreEqual(DocumentScope.Universal, StandardTypeRegistry.GetDocumentScope(new DimensionTypeModel { Class = "Autodesk.Revit.DB.DimensionType" }));
            Assert.AreEqual(DocumentScope.Universal, StandardTypeRegistry.GetDocumentScope(new FilledRegionTypeModel { Class = "Autodesk.Revit.DB.FilledRegionType" }));
            Assert.AreEqual(DocumentScope.Universal, StandardTypeRegistry.GetDocumentScope(new ElementTypeModel { Class = "Autodesk.Revit.DB.TextNoteType" }));
            Assert.AreEqual(DocumentScope.Universal, StandardTypeRegistry.GetDocumentScope(new ElementTypeModel { Class = "Autodesk.Revit.DB.ModelTextType" }));

            // FamilyOnly types
            Assert.AreEqual(DocumentScope.FamilyOnly, StandardTypeRegistry.GetDocumentScope(new ElementTypeModel { Class = "Autodesk.Revit.DB.TextElementType" }));

            // ProjectOnly types
            Assert.AreEqual(DocumentScope.ProjectOnly, StandardTypeRegistry.GetDocumentScope(new HostObjTypeModel { Class = "Autodesk.Revit.DB.WallType" }));
            Assert.AreEqual(DocumentScope.ProjectOnly, StandardTypeRegistry.GetDocumentScope(new HostObjTypeModel { Class = "Autodesk.Revit.DB.FloorType" }));
            Assert.AreEqual(DocumentScope.ProjectOnly, StandardTypeRegistry.GetDocumentScope(new HostObjTypeModel { Class = "Autodesk.Revit.DB.RoofType" }));
            Assert.AreEqual(DocumentScope.ProjectOnly, StandardTypeRegistry.GetDocumentScope(new HostObjTypeModel { Class = "Autodesk.Revit.DB.CeilingType" }));
            Assert.AreEqual(DocumentScope.ProjectOnly, StandardTypeRegistry.GetDocumentScope(new HostObjTypeModel { Class = "Autodesk.Revit.DB.BuildingPadType" }));
            Assert.AreEqual(DocumentScope.ProjectOnly, StandardTypeRegistry.GetDocumentScope(new HostObjTypeModel { Class = "Autodesk.Revit.DB.ToposolidType" }));
            Assert.AreEqual(DocumentScope.ProjectOnly, StandardTypeRegistry.GetDocumentScope(new HostObjTypeModel { Class = "Autodesk.Revit.DB.CurtainSystemType" }));
            Assert.AreEqual(DocumentScope.ProjectOnly, StandardTypeRegistry.GetDocumentScope(new HostObjTypeModel { Class = "Autodesk.Revit.DB.MullionType" }));
            Assert.AreEqual(DocumentScope.ProjectOnly, StandardTypeRegistry.GetDocumentScope(new HostObjTypeModel { Class = "Autodesk.Revit.DB.Architecture.FasciaType" }));
            Assert.AreEqual(DocumentScope.ProjectOnly, StandardTypeRegistry.GetDocumentScope(new HostObjTypeModel { Class = "Autodesk.Revit.DB.Architecture.GutterType" }));
            Assert.AreEqual(DocumentScope.ProjectOnly, StandardTypeRegistry.GetDocumentScope(new ElementTypeModel { Class = "Autodesk.Revit.DB.GridType" }));
            Assert.AreEqual(DocumentScope.ProjectOnly, StandardTypeRegistry.GetDocumentScope(new ElementTypeModel { Class = "Autodesk.Revit.DB.LevelType" }));
            Assert.AreEqual(DocumentScope.ProjectOnly, StandardTypeRegistry.GetDocumentScope(new ElementTypeModel { Class = "Autodesk.Revit.DB.SpotDimensionType" }));
            Assert.AreEqual(DocumentScope.ProjectOnly, StandardTypeRegistry.GetDocumentScope(new ParameterElementModel { Class = "Autodesk.Revit.DB.ParameterElement" }));
            Assert.AreEqual(DocumentScope.ProjectOnly, StandardTypeRegistry.GetDocumentScope(new ParameterElementModel { Class = "Autodesk.Revit.DB.SharedParameterElement" }));
            Assert.AreEqual(DocumentScope.ProjectOnly, StandardTypeRegistry.GetDocumentScope(new ElementTypeModel { Class = "Autodesk.Revit.DB.FamilySymbol" }));
            Assert.AreEqual(DocumentScope.ProjectOnly, StandardTypeRegistry.GetDocumentScope(new ElementTypeModel { Class = "Autodesk.Revit.DB.ViewFamilyType" }));
            Assert.AreEqual(DocumentScope.ProjectOnly, StandardTypeRegistry.GetDocumentScope(new ParameterFilterElementModel { Class = "Autodesk.Revit.DB.ParameterFilterElement" }));
            Assert.AreEqual(DocumentScope.ProjectOnly, StandardTypeRegistry.GetDocumentScope(new BrowserOrganizationModel { Class = "Autodesk.Revit.DB.BrowserOrganization" }));
            Assert.AreEqual(DocumentScope.ProjectOnly, StandardTypeRegistry.GetDocumentScope(new ViewModel { Class = "Autodesk.Revit.DB.View" }));
            Assert.AreEqual(DocumentScope.ProjectOnly, StandardTypeRegistry.GetDocumentScope(new ViewModel { Class = "Autodesk.Revit.DB.ViewDrafting" }));
            Assert.AreEqual(DocumentScope.ProjectOnly, StandardTypeRegistry.GetDocumentScope(new ViewModel { Class = "Autodesk.Revit.DB.ViewSection" }));
            Assert.AreEqual(DocumentScope.ProjectOnly, StandardTypeRegistry.GetDocumentScope(new ViewModel { Class = "Autodesk.Revit.DB.ViewPlan" }));
            Assert.AreEqual(DocumentScope.ProjectOnly, StandardTypeRegistry.GetDocumentScope(new ViewModel { Class = "Autodesk.Revit.DB.ViewSheet" }));
            Assert.AreEqual(DocumentScope.ProjectOnly, StandardTypeRegistry.GetDocumentScope(new ViewModel { Class = "Autodesk.Revit.DB.ViewSchedule" }));
            Assert.AreEqual(DocumentScope.ProjectOnly, StandardTypeRegistry.GetDocumentScope(new ViewModel { Class = "Autodesk.Revit.DB.View3D" }));

            // Unknown / fallback classes treat as ProjectOnly
            Assert.AreEqual(DocumentScope.ProjectOnly, StandardTypeRegistry.GetDocumentScope(new ElementModel { Class = "Custom.Unknown.LegacyClass" }));
            Assert.AreEqual(DocumentScope.ProjectOnly, StandardTypeRegistry.GetDocumentScope(new ElementModel { Class = null! }));
            Assert.AreEqual(DocumentScope.ProjectOnly, StandardTypeRegistry.GetDocumentScope(null));
        }

        [Test]
        public void IsInDocumentScope_CorrectlyFiltersForProjectAndFamilyDocuments()
        {
            var universalModel = new MaterialModel { Class = "Autodesk.Revit.DB.Material" };
            var familyOnlyModel = new ElementTypeModel { Class = "Autodesk.Revit.DB.TextElementType" };
            var projectOnlyModel = new HostObjTypeModel { Class = "Autodesk.Revit.DB.WallType" };
            var unknownModel = new ElementModel { Class = "Unknown.Class" };
            var nullClassModel = new ElementModel { Class = null! };

            // Family Document (isFamilyDocument = true)
            Assert.IsTrue(StandardTypeRegistry.IsInDocumentScope(universalModel, isFamilyDocument: true), "Universal models must be in scope in family documents.");
            Assert.IsTrue(StandardTypeRegistry.IsInDocumentScope(familyOnlyModel, isFamilyDocument: true), "FamilyOnly models must be in scope in family documents.");
            Assert.IsFalse(StandardTypeRegistry.IsInDocumentScope(projectOnlyModel, isFamilyDocument: true), "ProjectOnly models must NOT be in scope in family documents.");
            Assert.IsFalse(StandardTypeRegistry.IsInDocumentScope(unknownModel, isFamilyDocument: true), "Unknown classes must NOT be in scope in family documents.");
            Assert.IsFalse(StandardTypeRegistry.IsInDocumentScope(nullClassModel, isFamilyDocument: true), "Null-class models must NOT be in scope in family documents.");

            // Project Document (isFamilyDocument = false)
            Assert.IsTrue(StandardTypeRegistry.IsInDocumentScope(universalModel, isFamilyDocument: false), "Universal models must be in scope in project documents.");
            Assert.IsFalse(StandardTypeRegistry.IsInDocumentScope(familyOnlyModel, isFamilyDocument: false), "FamilyOnly models must NOT be in scope in project documents.");
            Assert.IsTrue(StandardTypeRegistry.IsInDocumentScope(projectOnlyModel, isFamilyDocument: false), "ProjectOnly models must be in scope in project documents.");
            Assert.IsTrue(StandardTypeRegistry.IsInDocumentScope(unknownModel, isFamilyDocument: false), "Unknown classes remain in scope in project documents.");
            Assert.IsTrue(StandardTypeRegistry.IsInDocumentScope(nullClassModel, isFamilyDocument: false), "Null-class models remain in scope in project documents.");
        }

        [Test]
        public void ToRevit_InFamilyDocument_SkipsProjectOnlyAndUnknownTypesWithZeroTransactions()
        {
            var doc = (Document)Activator.CreateInstance(typeof(Document), true)!;
            doc.IsFamilyDocument = true;

            var engine = new StandardSerializationEngine();

            var wallType = new HostObjTypeModel { Name = "Generic Wall", Class = "Autodesk.Revit.DB.WallType" };
            var gridType = new ElementTypeModel { Name = "1/4 Circle", Class = "Autodesk.Revit.DB.GridType" };
            var paramElement = new ParameterElementModel { Name = "CostCode", Class = "Autodesk.Revit.DB.ParameterElement" };
            var familySymbol = new ElementTypeModel { Name = "Door 36x84", Class = "Autodesk.Revit.DB.FamilySymbol" };
            var viewFamilyType = new ElementTypeModel { Name = "Floor Plan", Class = "Autodesk.Revit.DB.ViewFamilyType" };
            var unknownClass = new ElementModel { Name = "CustomWidget", Class = "Custom.Unknown.WidgetType" };

            var models = new List<ObjectModel> { wallType, gridType, paramElement, familySymbol, viewFamilyType, unknownClass };

            // Act
            var results = engine.ToRevit(models, doc).ToList();

            // Assert
            Assert.AreEqual(6, results.Count);
            foreach (var r in results)
            {
                Assert.IsTrue(r.Success, $"Model {r.Model} should succeed with Skipped action.");
                Assert.AreEqual("Skipped", r.Action, $"Model {r.Model} must have Action = Skipped.");
                Assert.IsNotNull(r.Message);
                Assert.IsTrue(r.Message!.Contains("out of scope for family document"), $"Message must state out of scope for family document: {r.Message}");
            }
        }

        [Test]
        public void ToRevit_InProjectDocument_SkipsFamilyOnlyTypesAndFailsUnknownTranslators()
        {
            var doc = (Document)Activator.CreateInstance(typeof(Document), true)!;
            doc.IsFamilyDocument = false;

            var engine = new StandardSerializationEngine();

            var textElementType = new ElementTypeModel { Name = "Family Label 1/8", Class = "Autodesk.Revit.DB.TextElementType" };
            var unknownClass = new ElementModel { Name = "UnregisteredProjModel", Class = "Custom.Unknown.ProjectModel" };

            var models = new List<ObjectModel> { textElementType, unknownClass };

            // Act
            var results = engine.ToRevit(models, doc).ToList();

            // Assert
            Assert.AreEqual(2, results.Count);

            // 1. TextElementType must be skipped because it is FamilyOnly
            var textElementResult = results.First(r => r.Model == textElementType);
            Assert.IsTrue(textElementResult.Success);
            Assert.AreEqual("Skipped", textElementResult.Action);
            Assert.IsTrue(textElementResult.Message!.Contains("out of scope for project document"));

            // 2. Unknown class is in project scope, but fails translator lookup cleanly
            var unknownResult = results.First(r => r.Model == unknownClass);
            Assert.IsFalse(unknownResult.Success);
            Assert.AreEqual("Failed", unknownResult.Action);
            Assert.IsTrue(unknownResult.Message!.Contains("No translator registered"));
        }

        [Test]
        public void Analyze_OmitsOutOfScopeClustersInFamilyAndProjectDocuments()
        {
            var familyDoc = new Document { IsFamilyDocument = true };
            var projectDoc = new Document { IsFamilyDocument = false };

            // Set up live elements in familyDoc
            var dimFam = new DimensionType { Name = "Standard", StyleType = DimensionStyleType.Linear, Category = new Category { Name = "Dimensions" } };
            familyDoc.AddElement(dimFam, new ElementId(101));

            var labelFam = new TextElementType { Name = "FamLabel", Category = new Category { Name = "Text Element Types" } };
            familyDoc.AddElement(labelFam, new ElementId(102));

            var wallFam = new WallType { Name = "Generic 8\"", FamilyName = "Basic Wall", Category = new Category { Name = "Walls" } };
            familyDoc.AddElement(wallFam, new ElementId(103));

            // Set up live elements in projectDoc
            var dimProj = new DimensionType { Name = "Standard", StyleType = DimensionStyleType.Linear, Category = new Category { Name = "Dimensions" } };
            projectDoc.AddElement(dimProj, new ElementId(201));

            var labelProj = new TextElementType { Name = "FamLabel", Category = new Category { Name = "Text Element Types" } };
            projectDoc.AddElement(labelProj, new ElementId(202));

            var wallProj = new WallType { Name = "Generic 8\"", FamilyName = "Basic Wall", Category = new Category { Name = "Walls" } };
            projectDoc.AddElement(wallProj, new ElementId(203));

            var engine = new StandardSerializationEngine();

            var dimModel = new DimensionTypeModel
            {
                Name = "Standard",
                Class = "Autodesk.Revit.DB.DimensionType",
                Category = "Dimensions",
                DimensionStyle = "Linear",
                Parameters = new List<ParameterModel> { new ParameterModel { Name = "Param", StorageType = "String", Value = "NewVal" } }
            };
            var labelModel = new ElementTypeModel
            {
                Name = "FamLabel",
                Class = "Autodesk.Revit.DB.TextElementType",
                Category = "Text Element Types",
                Parameters = new List<ParameterModel> { new ParameterModel { Name = "Param", StorageType = "String", Value = "NewVal" } }
            };
            var wallModel = new HostObjTypeModel
            {
                Name = "Generic 8\"",
                Class = "Autodesk.Revit.DB.WallType",
                Category = "Walls",
                FamilyName = "Basic Wall",
                Parameters = new List<ParameterModel> { new ParameterModel { Name = "Param", StorageType = "String", Value = "NewVal" } }
            };
            var unknownModel = new ElementModel
            {
                Name = "UnknownThing",
                Class = "Custom.Unknown.Class",
                Category = "Custom",
                Parameters = new List<ParameterModel> { new ParameterModel { Name = "Param", StorageType = "String", Value = "NewVal" } }
            };

            var allModels = new List<ObjectModel> { dimModel, labelModel, wallModel, unknownModel };

            // Act 1: Analyze against family document
            var familyClusters = engine.Analyze(allModels, familyDoc).ToList();
            var familyCategories = familyClusters.SelectMany(c => c.Items).Select(i => i.CategoryName).Distinct().ToList();

            Assert.IsFalse(familyCategories.Contains("Walls"), "WallType must be excluded from family diff clusters.");
            Assert.IsFalse(familyCategories.Contains("Custom"), "Unknown classes must be excluded from family diff clusters.");
            CollectionAssert.Contains(familyCategories, "Dimensions", "DimensionType must be included in family diff clusters.");
            CollectionAssert.Contains(familyCategories, "Text Element Types", "TextElementType must be included in family diff clusters.");

            // Act 2: Analyze against project document
            var projectClusters = engine.Analyze(allModels, projectDoc).ToList();
            var projectCategories = projectClusters.SelectMany(c => c.Items).Select(i => i.CategoryName).Distinct().ToList();

            Assert.IsFalse(projectCategories.Contains("Text Element Types"), "TextElementType must be excluded from project diff clusters.");
            CollectionAssert.Contains(projectCategories, "Dimensions", "DimensionType must be included in project diff clusters.");
            CollectionAssert.Contains(projectCategories, "Walls", "WallType must be included in project diff clusters.");
        }
    }
}
