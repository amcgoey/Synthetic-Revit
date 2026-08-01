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

        [Test]
        public void SwapElementReferences_SwapsElementViewGraphicOverrides()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                using (var tg = new TransactionGroup(doc, "Swap Element View Overrides"))
                {
                    tg.Start();

                    // Create line patterns & wall
                    LinePatternElement sourceLinePattern, targetLinePattern;
                    Wall wall;
                    Level level1, level2;
                    using (var t = new Transaction(doc, "Create Resources"))
                    {
                        t.Start();
                        sourceLinePattern = CreateLinePattern(doc, "Pattern E1");
                        targetLinePattern = CreateLinePattern(doc, "Pattern E2");
                        wall = CreateWall(doc, out level1, out level2);
                        t.Commit();
                    }

                    View view = new FilteredElementCollector(doc)
                        .OfClass(typeof(View))
                        .Cast<View>()
                        .FirstOrDefault(v => !v.IsTemplate && v.ViewType == ViewType.FloorPlan);

                    Assert.IsNotNull(view, "Floor plan view should exist.");

                    using (var t = new Transaction(doc, "Set Element Override"))
                    {
                        t.Start();
                        OverrideGraphicSettings ogs = new OverrideGraphicSettings();
                        ogs.SetProjectionLinePatternId(sourceLinePattern.Id);
                        ogs.SetCutLinePatternId(sourceLinePattern.Id);
                        view.SetElementOverrides(wall.Id, ogs);
                        t.Commit();
                    }

                    // Swap using AliasSwapEngine
                    var result = AliasSwapEngine.SwapElementReferences(doc, sourceLinePattern.Id, targetLinePattern.Id);

                    // Assert
                    var settings = view.GetElementOverrides(wall.Id);
                    Assert.AreEqual(targetLinePattern.Id, settings.ProjectionLinePatternId);
                    Assert.AreEqual(targetLinePattern.Id, settings.CutLinePatternId);
                    Assert.IsNotNull(result, "RedirectionResultModel should not be null.");
                    Assert.AreEqual(2, result.ViewGraphicOverridesCount, "ViewGraphicOverridesCount should reflect both swapped patterns.");

                    tg.RollBack();
                }
            }
            finally
            {
                doc.Close(false);
            }
        }

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

        [Test]
        public void SwapElementReferences_RemapsGroupTypes()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                using (var tg = new TransactionGroup(doc, "Remap Group Types Test"))
                {
                    tg.Start();

                    Group group1;
                    GroupType oldGroupType, newGroupType;

                    using (var t = new Transaction(doc, "Create Group"))
                    {
                        t.Start();
                        Level level1, level2;
                        Wall wall = CreateWall(doc, out level1, out level2);
                        group1 = doc.Create.NewGroup(new List<ElementId> { wall.Id });
                        oldGroupType = group1.GroupType;
                        newGroupType = oldGroupType.Duplicate("Group Type B " + Guid.NewGuid().ToString().Substring(0, 8)) as GroupType;
                        t.Commit();
                    }

                    Assert.IsNotNull(group1, "Group should be created.");
                    Assert.IsNotNull(oldGroupType, "Old GroupType should exist.");
                    Assert.IsNotNull(newGroupType, "New GroupType should exist.");
                    Assert.AreEqual(oldGroupType.Id, group1.GroupType.Id);

                    // Act
                    RedirectionResultModel result = AliasSwapEngine.SwapElementReferences(doc, oldGroupType.Id, newGroupType.Id);

                    // Assert
                    Assert.IsNotNull(result, "RedirectionResultModel should not be null.");
                    Assert.AreEqual(newGroupType.Id, group1.GroupType.Id, "Group.GroupType should be updated to target group type.");
                    Assert.AreEqual(newGroupType.Id, group1.GetTypeId(), "Group.GetTypeId() should be updated to target group type.");
                    Assert.IsTrue(result.InstancesCount > 0, "InstancesCount should be incremented.");

                    tg.RollBack();
                }
            }
            finally
            {
                doc.Close(false);
            }
        }

        [Test]
        public void SwapElementReferences_RemapsSystemFamilyTypes()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                using (var tg = new TransactionGroup(doc, "Remap System Family Test"))
                {
                    tg.Start();

                    WallType wallType1, wallType2;
                    Wall wall;
                    using (var t = new Transaction(doc, "Setup Wall Types and Wall"))
                    {
                        t.Start();
                        var wallTypes = new FilteredElementCollector(doc)
                            .OfClass(typeof(WallType))
                            .Cast<WallType>()
                            .ToList();

                        Assert.IsTrue(wallTypes.Count >= 1, "Expected at least one WallType in default project document.");
                        wallType1 = wallTypes[0];
                        wallType2 = wallType1.Duplicate("Duplicated Wall Type " + Guid.NewGuid().ToString().Substring(0, 8)) as WallType;
                        Assert.IsNotNull(wallType2);

                        Level level = Level.Create(doc, 0.0);
                        Line line = Line.CreateBound(XYZ.Zero, new XYZ(10, 0, 0));
                        wall = Wall.Create(doc, line, wallType1.Id, level.Id, 10.0, 0.0, false, false);
                        t.Commit();
                    }

                    Assert.AreEqual(wallType1.Id, wall.GetTypeId());

                    // Act - Swap wallType1.Id with wallType2.Id
                    RedirectionResultModel result = AliasSwapEngine.SwapElementReferences(doc, wallType1.Id, wallType2.Id);

                    // Assert
                    Assert.IsNotNull(result);
                    Assert.AreEqual(wallType2.Id, wall.GetTypeId(), "Wall instance type should be remapped to wallType2.");
                    Assert.IsTrue(result.InstancesCount > 0, "InstancesCount should be incremented for remapped system family instance.");

                    tg.RollBack();
                }
            }
            finally
            {
                doc.Close(false);
            }
        }

        private (Wall w1, Wall w2) CreateConnectedWallsAt(Document doc, double yOffset)
        {
            Level level1 = Level.Create(doc, 0.0);
            level1.Name = "Test Level " + Guid.NewGuid().ToString().Substring(0, 8);

            var wallType = new FilteredElementCollector(doc)
                .OfClass(typeof(WallType))
                .Cast<WallType>()
                .First();

            Line line1 = Line.CreateBound(new XYZ(0, yOffset, 0), new XYZ(10, yOffset, 0));
            Line line2 = Line.CreateBound(new XYZ(10, yOffset, 0), new XYZ(10, yOffset + 10, 0));

            Wall w1 = Wall.Create(doc, line1, wallType.Id, level1.Id, 10.0, 0.0, false, false);
            Wall w2 = Wall.Create(doc, line2, wallType.Id, level1.Id, 10.0, 0.0, false, false);
            return (w1, w2);
        }

        [Test]
        public void SwapElementReferences_RemapsAssemblyTypes()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                using (var tg = new TransactionGroup(doc, "Remap Assembly Types Test"))
                {
                    tg.Start();

                    AssemblyInstance assembly1 = null!;
                    AssemblyInstance assembly2 = null!;

                    using (var t = new Transaction(doc, "Create Assemblies"))
                    {
                        t.Start();
                        var set1 = CreateConnectedWallsAt(doc, 0.0);
                        var set2 = CreateConnectedWallsAt(doc, 50.0);

                        doc.Regenerate();

                        var memberIds1 = new List<ElementId> { set1.w1.Id, set1.w2.Id };
                        var memberIds2 = new List<ElementId> { set2.w1.Id, set2.w2.Id };

                        assembly1 = AssemblyInstance.Create(doc, memberIds1, set1.w1.Category.Id);
                        assembly2 = AssemblyInstance.Create(doc, memberIds2, set2.w1.Category.Id);
                        t.Commit();
                    }

                    Assert.IsNotNull(assembly1, "Assembly 1 should be created.");
                    Assert.IsNotNull(assembly2, "Assembly 2 should be created.");

                    ElementId oldAssemblyTypeId = assembly1.GetTypeId();
                    ElementId newAssemblyTypeId = assembly2.GetTypeId();

                    // Act
                    RedirectionResultModel result = AliasSwapEngine.SwapElementReferences(doc, oldAssemblyTypeId, newAssemblyTypeId);

                    // Assert
                    Assert.IsNotNull(result, "RedirectionResultModel should not be null.");
                    Assert.AreEqual(newAssemblyTypeId, assembly1.GetTypeId(), "AssemblyInstance.GetTypeId() should be updated to target assembly type.");
                    Assert.IsTrue(result.InstancesCount > 0, "InstancesCount should be incremented.");

                    tg.RollBack();
                }
            }
            finally
            {
                doc.Close(false);
            }
        }

        [Test]
        public void SwapElementReferences_RemapsAnnotationTypes()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                using (var tg = new TransactionGroup(doc, "Remap Annotation Type Test"))
                {
                    tg.Start();

                    TextNoteType textType1, textType2;
                    TextNote textNote;
                    using (var t = new Transaction(doc, "Setup TextNote Types and TextNote"))
                    {
                        t.Start();
                        var textTypes = new FilteredElementCollector(doc)
                            .OfClass(typeof(TextNoteType))
                            .Cast<TextNoteType>()
                            .ToList();

                        Assert.IsTrue(textTypes.Count >= 1, "Expected at least one TextNoteType in default project document.");
                        textType1 = textTypes[0];
                        textType2 = textType1.Duplicate("Duplicated Text Type " + Guid.NewGuid().ToString().Substring(0, 8)) as TextNoteType;
                        Assert.IsNotNull(textType2);

                        View view = new FilteredElementCollector(doc)
                            .OfClass(typeof(View))
                            .Cast<View>()
                            .First(v => !v.IsTemplate && v.ViewType == ViewType.FloorPlan);

                        textNote = TextNote.Create(doc, view.Id, XYZ.Zero, "Sample Text Note", textType1.Id);
                        t.Commit();
                    }

                    Assert.AreEqual(textType1.Id, textNote.GetTypeId());

                    // Act - Swap textType1.Id with textType2.Id
                    RedirectionResultModel result = AliasSwapEngine.SwapElementReferences(doc, textType1.Id, textType2.Id);

                    // Assert
                    Assert.IsNotNull(result);
                    Assert.AreEqual(textType2.Id, textNote.GetTypeId(), "TextNote instance type should be remapped to textType2.");
                    tg.RollBack();
                }
            }
            finally
            {
                doc.Close(false);
            }
        }

        [Test]
        public void SwapElementReferences_UpdatesViewFilterGraphicOverrides()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                using (var tg = new TransactionGroup(doc, "Swap View Filter Overrides"))
                {
                    tg.Start();

                    // Create line patterns & fetch fill patterns
                    LinePatternElement sourceLinePattern, targetLinePattern;
                    FillPatternElement sourceFillPattern = null, targetFillPattern = null;

                    using (var t = new Transaction(doc, "Create Patterns"))
                    {
                        t.Start();
                        sourceLinePattern = CreateLinePattern(doc, "Filter Pattern A");
                        targetLinePattern = CreateLinePattern(doc, "Filter Pattern B");

                        var fillPatterns = new FilteredElementCollector(doc)
                            .OfClass(typeof(FillPatternElement))
                            .Cast<FillPatternElement>()
                            .Take(2)
                            .ToList();

                        if (fillPatterns.Count >= 2)
                        {
                            sourceFillPattern = fillPatterns[0];
                            targetFillPattern = fillPatterns[1];
                        }

                        t.Commit();
                    }

                    View view = new FilteredElementCollector(doc)
                        .OfClass(typeof(View))
                        .Cast<View>()
                        .FirstOrDefault(v => !v.IsTemplate && v.ViewType == ViewType.FloorPlan);

                    Assert.IsNotNull(view, "Floor plan view should exist.");

                    Category wallsCategory = doc.Settings.Categories.get_Item(BuiltInCategory.OST_Walls);
                    List<ElementId> categoryIds = new List<ElementId> { wallsCategory.Id };

                    ParameterFilterElement filter;
                    using (var t = new Transaction(doc, "Create Filter"))
                    {
                        t.Start();
                        filter = ParameterFilterElement.Create(doc, "Test Filter " + Guid.NewGuid().ToString().Substring(0, 8), categoryIds);
                        view.AddFilter(filter.Id);

                        OverrideGraphicSettings graphicSettings = new OverrideGraphicSettings();
                        graphicSettings.SetProjectionLinePatternId(sourceLinePattern.Id);
                        if (sourceFillPattern != null)
                        {
                            graphicSettings.SetSurfaceForegroundPatternId(sourceFillPattern.Id);
                        }
                        view.SetFilterOverrides(filter.Id, graphicSettings);
                        t.Commit();
                    }

                    // Swap Line Pattern using AliasSwapEngine
                    var resultLine = AliasSwapEngine.SwapElementReferences(doc, sourceLinePattern.Id, targetLinePattern.Id);

                    // Assert Line Pattern swap
                    var updatedSettings = view.GetFilterOverrides(filter.Id);
                    Assert.AreEqual(targetLinePattern.Id, updatedSettings.ProjectionLinePatternId);
                    Assert.IsTrue(resultLine.ViewGraphicOverridesCount > 0, "ViewGraphicOverridesCount should be incremented for line pattern swap.");

                    // Swap Fill Pattern if available
                    if (sourceFillPattern != null && targetFillPattern != null)
                    {
                        var resultFill = AliasSwapEngine.SwapElementReferences(doc, sourceFillPattern.Id, targetFillPattern.Id);
                        updatedSettings = view.GetFilterOverrides(filter.Id);
                        Assert.AreEqual(targetFillPattern.Id, updatedSettings.SurfaceForegroundPatternId);
                        Assert.IsTrue(resultFill.ViewGraphicOverridesCount > 0, "ViewGraphicOverridesCount should be incremented for fill pattern swap.");
                    }

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

