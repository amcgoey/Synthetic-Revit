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

        #endregion
    }
}

