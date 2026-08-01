using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Autodesk.Revit.UI;
using Autodesk.Revit.DB;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;

namespace SyntheticTests
{
    [TestFixture]
    public class AliasSwapEngineTests
    {
        private UIApplication? _uiapp;

        [OneTimeSetUp]
        public void Setup(UIApplication uiapp)
        {
            _uiapp = uiapp;
        }

        #region Helper Methods

        private long GetRawIdValue(ElementId id)
        {
            var prop = id.GetType().GetProperty("Value");
            if (prop != null)
            {
                return (long)prop.GetValue(id);
            }
            var intProp = id.GetType().GetProperty("IntegerValue");
            return Convert.ToInt64(intProp!.GetValue(id));
        }

        private Wall CreateWall(Document doc, out Level level1, out Level level2)
        {
            level1 = Level.Create(doc, 0.0);
            level1.Name = "Test Level 1 " + Guid.NewGuid().ToString().Substring(0, 8);
            level2 = Level.Create(doc, 10.0);
            level2.Name = "Test Level 2 " + Guid.NewGuid().ToString().Substring(0, 8);

            var wallType = new FilteredElementCollector(doc)
                .OfClass(typeof(WallType))
                .Cast<WallType>()
                .First();

            Line line = Line.CreateBound(XYZ.Zero, new XYZ(10, 0, 0));
            Wall wall = Wall.Create(doc, line, wallType.Id, level1.Id, 10.0, 0.0, false, false);
            return wall;
        }

        private LinePatternElement CreateLinePattern(Document doc, string name)
        {
            LinePattern pattern = new LinePattern(name);
            pattern.SetSegments(new List<LinePatternSegment>
            {
                new LinePatternSegment(LinePatternSegmentType.Dash, 0.1),
                new LinePatternSegment(LinePatternSegmentType.Space, 0.1)
            });
            return LinePatternElement.Create(doc, pattern);
        }

        private Material CreateMaterial(Document doc, string name)
        {
            ElementId matId = Material.Create(doc, name);
            return (Material)doc.GetElement(matId);
        }

        #endregion

        #region AliasSwapEngine Tests

        [Test]
        public void SwapElementReferences_UpdatesStandardParameters()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                using (var tg = new TransactionGroup(doc, "Swap Standard Param Test"))
                {
                    tg.Start();

                    Wall wall;
                    Level level1, level2;
                    using (var t = new Transaction(doc, "Create Wall"))
                    {
                        t.Start();
                        wall = CreateWall(doc, out level1, out level2);
                        t.Commit();
                    }

                    var baseConstraintParam = wall.get_Parameter(BuiltInParameter.WALL_BASE_CONSTRAINT);
                    Assert.AreEqual(level1.Id, baseConstraintParam.AsElementId());

                    // Act - swap Level 1 ID with Level 2 ID using AliasSwapEngine
                    AliasSwapEngine.SwapElementReferences(doc, level1.Id, level2.Id);

                    // Assert
                    Assert.AreEqual(level2.Id, baseConstraintParam.AsElementId());

                    tg.RollBack();
                }
            }
            finally
            {
                doc.Close(false);
            }
        }

        [Test]
        public void SwapElementReferences_UpdatesCategoryDefaultStyles()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                using (var tg = new TransactionGroup(doc, "Swap Category Styles"))
                {
                    tg.Start();

                    // Create line patterns & materials
                    LinePatternElement lp1, lp2;
                    Material mat1, mat2;
                    using (var t = new Transaction(doc, "Create Resources"))
                    {
                        t.Start();
                        lp1 = CreateLinePattern(doc, "Pattern A");
                        lp2 = CreateLinePattern(doc, "Pattern B");
                        mat1 = CreateMaterial(doc, "Material A");
                        mat2 = CreateMaterial(doc, "Material B");
                        t.Commit();
                    }

                    Category parentCat = doc.Settings.Categories.get_Item(BuiltInCategory.OST_Walls);
                    Category subCat;
                    using (var t = new Transaction(doc, "Create Subcategory"))
                    {
                        t.Start();
                        subCat = doc.Settings.Categories.NewSubcategory(parentCat, "TestSubcat_" + Guid.NewGuid().ToString().Substring(0, 8));
                        subCat.SetLinePatternId(lp1.Id, GraphicsStyleType.Projection);
                        subCat.Material = mat1;
                        t.Commit();
                    }

                    Assert.AreEqual(lp1.Id, subCat.GetLinePatternId(GraphicsStyleType.Projection));
                    Assert.AreEqual(mat1.Id, subCat.Material.Id);

                    // Swap using AliasSwapEngine
                    AliasSwapEngine.SwapElementReferences(doc, lp1.Id, lp2.Id);
                    AliasSwapEngine.SwapElementReferences(doc, mat1.Id, mat2.Id);

                    // Assert
                    Assert.AreEqual(lp2.Id, subCat.GetLinePatternId(GraphicsStyleType.Projection));
                    Assert.AreEqual(mat2.Id, subCat.Material.Id);

                    tg.RollBack();
                }
            }
            finally
            {
                doc.Close(false);
            }
        }

        [Test]
        public void SwapElementReferences_UpdatesCompoundStructureLayers()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                using (var tg = new TransactionGroup(doc, "Swap Compound Structures"))
                {
                    tg.Start();

                    Material mat1, mat2;
                    using (var t = new Transaction(doc, "Create Materials"))
                    {
                        t.Start();
                        mat1 = CreateMaterial(doc, "Mat 1");
                        mat2 = CreateMaterial(doc, "Mat 2");
                        t.Commit();
                    }

                    WallType defaultWallType = new FilteredElementCollector(doc)
                        .OfClass(typeof(WallType))
                        .Cast<WallType>()
                        .First();
                    WallType newWallType;
                    using (var t = new Transaction(doc, "Duplicate Wall Type"))
                    {
                        t.Start();
                        newWallType = (WallType)defaultWallType.Duplicate("TestCompoundWallType_" + Guid.NewGuid().ToString().Substring(0, 8));

                        CompoundStructure cs = newWallType.GetCompoundStructure();
                        if (cs == null)
                        {
                            cs = CompoundStructure.CreateSimpleCompoundStructure(new List<CompoundStructureLayer>
                            {
                                new CompoundStructureLayer(0.5, MaterialFunctionAssignment.Structure, mat1.Id)
                            });
                        }
                        else
                        {
                            var layers = cs.GetLayers();
                            if (layers.Count > 0)
                            {
                                layers[0].MaterialId = mat1.Id;
                                cs.SetLayers(layers);
                            }
                        }
                        newWallType.SetCompoundStructure(cs);
                        t.Commit();
                    }

                    // Swap using AliasSwapEngine
                    AliasSwapEngine.SwapElementReferences(doc, mat1.Id, mat2.Id);

                    // Assert
                    CompoundStructure csChecked = newWallType.GetCompoundStructure();
                    Assert.IsNotNull(csChecked);
                    Assert.AreEqual(mat2.Id, csChecked.GetLayers()[0].MaterialId);

                    tg.RollBack();
                }
            }
            finally
            {
                doc.Close(false);
            }
        }

        [Test]
        public void SwapElementReferences_UpdatesViewGraphicOverrides()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                using (var tg = new TransactionGroup(doc, "Swap View Overrides"))
                {
                    tg.Start();

                    // Create line patterns
                    LinePatternElement lp1, lp2;
                    using (var t = new Transaction(doc, "Create Patterns"))
                    {
                        t.Start();
                        lp1 = CreateLinePattern(doc, "Pattern A");
                        lp2 = CreateLinePattern(doc, "Pattern B");
                        t.Commit();
                    }

                    View view = new FilteredElementCollector(doc)
                        .OfClass(typeof(View))
                        .Cast<View>()
                        .FirstOrDefault(v => !v.IsTemplate && v.ViewType == ViewType.FloorPlan);

                    Assert.IsNotNull(view, "Floor plan view should exist.");

                    Category wallsCat = doc.Settings.Categories.get_Item(BuiltInCategory.OST_Walls);

                    using (var t = new Transaction(doc, "Set Category Override"))
                    {
                        t.Start();
                        OverrideGraphicSettings ogs = new OverrideGraphicSettings();
                        ogs.SetProjectionLinePatternId(lp1.Id);
                        view.SetCategoryOverrides(wallsCat.Id, ogs);
                        t.Commit();
                    }

                    // Swap using AliasSwapEngine
                    var result = AliasSwapEngine.SwapElementReferences(doc, lp1.Id, lp2.Id);

                    // Assert
                    var settings = view.GetCategoryOverrides(wallsCat.Id);
                    Assert.AreEqual(lp2.Id, settings.ProjectionLinePatternId);
                    Assert.IsNotNull(result, "RedirectionResultModel should not be null.");
                    Assert.IsTrue(result.ViewGraphicOverridesCount > 0, "ViewGraphicOverridesCount should be greater than 0.");

                    tg.RollBack();
                }
            }
            finally
            {
                doc.Close(false);
            }
        }

        [Test]
        public void SwapElementReferences_ReturnsRedirectionResultModel_WithAccurateCounts()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                using (var tg = new TransactionGroup(doc, "RedirectionResult Metrics Test"))
                {
                    tg.Start();

                    Wall wall;
                    Level level1, level2;
                    using (var t = new Transaction(doc, "Create Wall"))
                    {
                        t.Start();
                        wall = CreateWall(doc, out level1, out level2);
                        t.Commit();
                    }

                    // Act
                    RedirectionResultModel result = AliasSwapEngine.SwapElementReferences(doc, level1.Id, level2.Id);

                    // Assert
                    Assert.IsNotNull(result, "RedirectionResultModel should not be null.");
                    Assert.IsTrue(result.ParametersCount > 0, "ParametersCount should be greater than 0.");
                    Assert.IsTrue(result.InstancesCount > 0, "InstancesCount should be greater than 0.");
                    Assert.AreEqual(result.TotalSwappedCount, result.ParametersCount + result.CategoryStylesCount + result.CompoundStructureLayersCount + result.ViewGraphicOverridesCount);

                    tg.RollBack();
                }
            }
            finally
            {
                doc.Close(false);
            }
        }

        [Test]
        public void SwapElementReferences_SupportsCallerManagedTransaction()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                using (var tg = new TransactionGroup(doc, "Caller Managed Transaction Test"))
                {
                    tg.Start();

                    Wall wall;
                    Level level1, level2;
                    using (var t = new Transaction(doc, "Create Wall"))
                    {
                        t.Start();
                        wall = CreateWall(doc, out level1, out level2);
                        t.Commit();
                    }

                    var baseConstraintParam = wall.get_Parameter(BuiltInParameter.WALL_BASE_CONSTRAINT);
                    Assert.AreEqual(level1.Id, baseConstraintParam.AsElementId());

                    // Act - Pass active caller-managed transaction
                    RedirectionResultModel result;
                    using (var callerTrans = new Transaction(doc, "Caller Active Trans"))
                    {
                        callerTrans.Start();
                        result = AliasSwapEngine.SwapElementReferences(doc, level1.Id, level2.Id, callerTrans);
                        callerTrans.Commit();
                    }

                    // Assert
                    Assert.IsNotNull(result, "RedirectionResultModel should not be null.");
                    Assert.AreEqual(level2.Id, baseConstraintParam.AsElementId());
                    Assert.IsTrue(result.ParametersCount > 0);

                    tg.RollBack();
                }
            }
            finally
            {
                doc.Close(false);
            }
        }

        private Document OpenTestTemplate(Autodesk.Revit.ApplicationServices.Application app)
        {
            string projectRoot = SyntheticTests.Helpers.TestPathHelper.GetProjectRoot();
            string testModelName = "TestTemplate" + app.VersionNumber + ".rvt";
            string modelPathStr = System.IO.Path.Combine(projectRoot, "tests", "test_models", testModelName);
            if (!System.IO.File.Exists(modelPathStr))
            {
                throw new System.IO.FileNotFoundException("Test template model not found: " + modelPathStr);
            }

            ModelPath modelPath = ModelPathUtils.ConvertUserVisiblePathToModelPath(modelPathStr);
            OpenOptions openOptions = new OpenOptions
            {
                DetachFromCentralOption = DetachFromCentralOption.DetachAndDiscardWorksets
            };
            return app.OpenDocumentFile(modelPath, openOptions);
        }

        [Test]
        public void SwapElementReferences_RemapsFamilyInstanceSymbols()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = OpenTestTemplate(app);

            try
            {
                using (var tg = new TransactionGroup(doc, "Swap FamilyInstance Symbol Test"))
                {
                    tg.Start();

                    FamilySymbol symbol1, symbol2;
                    FamilyInstance instance;

                    using (var t = new Transaction(doc, "Create Family Symbols and Instance"))
                    {
                        t.Start();
                        symbol1 = new FilteredElementCollector(doc)
                            .OfClass(typeof(FamilySymbol))
                            .Cast<FamilySymbol>()
                            .FirstOrDefault(s => s.Family != null && s.Family.IsEditable && !s.Family.IsInPlace);

                        if (symbol1 == null)
                        {
                            symbol1 = new FilteredElementCollector(doc)
                                .OfClass(typeof(FamilySymbol))
                                .Cast<FamilySymbol>()
                                .FirstOrDefault();
                        }

                        Assert.IsNotNull(symbol1, "A FamilySymbol should exist in TestTemplate document.");

                        symbol2 = (FamilySymbol)symbol1.Duplicate("DuplicatedSymbol_" + Guid.NewGuid().ToString().Substring(0, 8));

                        if (!symbol1.IsActive)
                        {
                            symbol1.Activate();
                        }

                        instance = new FilteredElementCollector(doc)
                            .OfClass(typeof(FamilyInstance))
                            .Cast<FamilyInstance>()
                            .FirstOrDefault(fi => fi.GetTypeId() == symbol1.Id);

                        if (instance == null)
                        {
                            Category? cat = symbol1.Category;
                            if (cat != null && cat.Id == new ElementId(BuiltInCategory.OST_TitleBlocks))
                            {
                                ViewSheet sheet = ViewSheet.Create(doc, ElementId.InvalidElementId);
                                instance = doc.Create.NewFamilyInstance(XYZ.Zero, symbol1, sheet);
                            }
                            else if (symbol1.Family != null && symbol1.Family.FamilyCategory != null &&
                                (symbol1.Family.FamilyCategory.CategoryType == CategoryType.Annotation ||
                                symbol1.Family.FamilyCategory.Id == new ElementId(BuiltInCategory.OST_DetailComponents)))
                            {
                                ViewDrafting draftingView = ViewDrafting.Create(doc, ElementId.InvalidElementId);
                                instance = doc.Create.NewFamilyInstance(XYZ.Zero, symbol1, draftingView);
                            }
                            else
                            {
                                Level level = new FilteredElementCollector(doc)
                                    .OfClass(typeof(Level))
                                    .Cast<Level>()
                                    .FirstOrDefault() ?? Level.Create(doc, 0.0);
                                instance = doc.Create.NewFamilyInstance(XYZ.Zero, symbol1, level, Autodesk.Revit.DB.Structure.StructuralType.NonStructural);
                            }
                        }

                        t.Commit();
                    }

                    Assert.IsNotNull(instance, "Placed family instance should not be null.");
                    Assert.AreEqual(symbol1.Id, instance.GetTypeId());

                    // Act - Swap symbol1.Id with symbol2.Id using AliasSwapEngine
                    RedirectionResultModel result = AliasSwapEngine.SwapElementReferences(doc, symbol1.Id, symbol2.Id);

                    // Assert
                    Assert.IsNotNull(result, "RedirectionResultModel should not be null.");
                    Assert.IsEmpty(result.Errors, "Errors: " + string.Join("; ", result.Errors));
                    Assert.IsEmpty(result.Warnings, "Warnings: " + string.Join("; ", result.Warnings));
                    Assert.AreEqual(symbol2.Id, instance.GetTypeId());
                    Assert.IsNotNull(instance.Symbol, "FamilyInstance.Symbol should not be null.");
                    Assert.AreEqual(symbol2.Id, instance.Symbol.Id);
                    Assert.IsTrue(result.InstancesCount > 0, "InstancesCount should be greater than 0.");

                    tg.RollBack();
                }
            }
            finally
            {
                doc.Close(false);
            }
        }

        #endregion
    }
}

