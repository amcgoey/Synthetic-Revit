using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Operations;

namespace SyntheticTests.RevitDOM
{
    [TestFixture]
    [UnitTest]
    public class ImportExecutionRunnerTests
    {
        private class CustomTestObjectModel : ObjectModel
        {
            public CustomTestObjectModel() : base() { }
        }

        [Test]
        public void ImportExecutionRunner_Sort_OrdersRandomModelsChronologicallyByDAGTiers()
        {
            // Arrange - Create models belonging to each of the 6 defined tiers
            var linePattern = new LinePatternElementModel { Name = "LinePatternA" }; // Tier 1
            var material = new MaterialModel { Name = "MaterialA" }; // Tier 2
            var dimensionType = new DimensionTypeModel { Name = "DimensionTypeA" }; // Tier 3
            var category = new CategoryModel { Name = "CategoryA" }; // Tier 4
            var hostObj = new HostObjTypeModel { Name = "HostObjA" }; // Tier 5
            var view = new ViewModel { Name = "ViewA" }; // Tier 6

            // Shuffle the models
            var shuffledList = new List<ObjectModel> { view, hostObj, category, dimensionType, material, linePattern };

            // Act
            var sortedList = ImportExecutionRunner.Sort(shuffledList).ToList();

            // Assert
            Assert.AreEqual(6, sortedList.Count);
            Assert.AreSame(linePattern, sortedList[0], "LinePattern (Tier 1) should be sorted first.");
            Assert.AreSame(material, sortedList[1], "Material (Tier 2) should be sorted second.");
            Assert.AreSame(dimensionType, sortedList[2], "DimensionType (Tier 3) should be sorted third.");
            Assert.AreSame(category, sortedList[3], "Category (Tier 4) should be sorted fourth.");
            Assert.AreSame(hostObj, sortedList[4], "HostObj (Tier 5) should be sorted fifth.");
            Assert.AreSame(view, sortedList[5], "View (Tier 6) should be sorted sixth.");
        }

        [Test]
        public void ImportExecutionRunner_Sort_StableSecondarySortByName()
        {
            // Arrange - Create models in the same tier with different names
            var matB = new MaterialModel { Name = "MaterialB" };
            var matA = new MaterialModel { Name = "MaterialA" };
            var matC = new MaterialModel { Name = "MaterialC" };

            var shuffledList = new List<ObjectModel> { matC, matB, matA };

            // Act
            var sortedList = ImportExecutionRunner.Sort(shuffledList).ToList();

            // Assert
            Assert.AreEqual(3, sortedList.Count);
            Assert.AreSame(matA, sortedList[0], "MaterialA should be sorted first within its tier.");
            Assert.AreSame(matB, sortedList[1], "MaterialB should be sorted second within its tier.");
            Assert.AreSame(matC, sortedList[2], "MaterialC should be sorted third within its tier.");
        }

        [Test]
        public void ImportExecutionRunner_Sort_PreservesUnrecognizedModels()
        {
            // Arrange - Create recognized models and an unrecognized custom model
            var material = new MaterialModel { Name = "MaterialA" };
            var customModel = new CustomTestObjectModel();
            var linePattern = new LinePatternElementModel { Name = "LinePatternA" };

            var list = new List<ObjectModel> { customModel, material, linePattern };

            // Act
            var sortedList = ImportExecutionRunner.Sort(list).ToList();

            // Assert
            Assert.AreEqual(3, sortedList.Count);
            Assert.AreSame(linePattern, sortedList[0], "LinePattern (Tier 1) should still be first.");
            Assert.AreSame(material, sortedList[1], "Material (Tier 2) should still be second.");
            Assert.AreSame(customModel, sortedList[2], "Unrecognized CustomModel (Tier 8 fallback) should be sorted last.");
        }

        [Test]
        public void ImportExecutionRunner_Sort_OrdersSharedElementTypeAndHostTypesIntoCorrectTiers()
        {
            var textNote = new ElementTypeModel { Name = "TextNote", Class = "Autodesk.Revit.DB.TextNoteType" };
            var spotDim = new ElementTypeModel { Name = "SpotDim", Class = "Autodesk.Revit.DB.SpotDimensionType" };
            var filledRegion = new FilledRegionTypeModel { Name = "FilledRegion", Class = "Autodesk.Revit.DB.FilledRegionType" };
            var gridType = new ElementTypeModel { Name = "GridType", Class = "Autodesk.Revit.DB.GridType" };
            var curtainSystem = new HostObjTypeModel { Name = "CurtainSystem", Class = "Autodesk.Revit.DB.CurtainSystemType" };
            var toposolid = new HostObjTypeModel { Name = "Toposolid", Class = "Autodesk.Revit.DB.ToposolidType" };
            var filter = new ParameterFilterElementModel { Name = "Filter" };
            var view = new ViewModel { Name = "View" };
            var linePattern = new LinePatternElementModel { Name = "LinePattern" };

            var list = new List<ObjectModel> { view, toposolid, filter, curtainSystem, gridType, filledRegion, spotDim, textNote, linePattern };
            var sorted = ImportExecutionRunner.Sort(list).ToList();

            // Tier 1: linePattern
            // Tier 3: filledRegion, gridType, spotDim, textNote
            // Tier 4: filter
            // Tier 5: curtainSystem, toposolid
            // Tier 6: view
            Assert.AreSame(linePattern, sorted[0]);
            var tier3Items = sorted.Skip(1).Take(4).ToList();
            Assert.IsTrue(tier3Items.Contains(textNote), "TextNoteType must be Tier 3.");
            Assert.IsTrue(tier3Items.Contains(spotDim), "SpotDimensionType must be Tier 3.");
            Assert.IsTrue(tier3Items.Contains(filledRegion), "FilledRegionType must be Tier 3.");
            Assert.IsTrue(tier3Items.Contains(gridType), "GridType must be Tier 3.");

            Assert.AreSame(filter, sorted[5], "ParameterFilter must be Tier 4.");

            var tier5Items = sorted.Skip(6).Take(2).ToList();
            Assert.IsTrue(tier5Items.Contains(curtainSystem), "CurtainSystemType must be Tier 5.");
            Assert.IsTrue(tier5Items.Contains(toposolid), "ToposolidType must be Tier 5.");

            Assert.AreSame(view, sorted[8], "View must be Tier 6.");
        }

        [Test]
        public void ImportExecutionRunner_Sort_BreaksWithinTierTiesByClassFamilyNameAndName()
        {
            // All Tier 5 items:
            var wallCurtain = new HostObjTypeModel { Class = "Autodesk.Revit.DB.WallType", FamilyName = "Curtain Wall", Name = "Glass Front" };
            var wallBasicBrick = new HostObjTypeModel { Class = "Autodesk.Revit.DB.WallType", FamilyName = "Basic Wall", Name = "Brick on CMU" };
            var wallBasicConcrete = new HostObjTypeModel { Class = "Autodesk.Revit.DB.WallType", FamilyName = "Basic Wall", Name = "Concrete 8\"" };
            var floorWood = new HostObjTypeModel { Class = "Autodesk.Revit.DB.FloorType", FamilyName = "Floor", Name = "Wood Joist" };

            // Unsorted input
            var input = new List<ObjectModel> { wallCurtain, wallBasicConcrete, floorWood, wallBasicBrick };

            var sorted = ImportExecutionRunner.Sort(input).ToList();

            // Expected order:
            // 1. FloorType (Class "Autodesk.Revit.DB.FloorType" < "Autodesk.Revit.DB.WallType")
            // 2. WallType, "Basic Wall", "Brick on CMU"
            // 3. WallType, "Basic Wall", "Concrete 8\""
            // 4. WallType, "Curtain Wall", "Glass Front"
            Assert.AreSame(floorWood, sorted[0], "FloorType should precede WallType by Class.");
            Assert.AreSame(wallBasicBrick, sorted[1], "Basic Wall Brick should precede Basic Wall Concrete by Name.");
            Assert.AreSame(wallBasicConcrete, sorted[2], "Basic Wall Concrete should follow Basic Wall Brick.");
            Assert.AreSame(wallCurtain, sorted[3], "Curtain Wall should follow Basic Wall by FamilyName.");
        }
    }
}
