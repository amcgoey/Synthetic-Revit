using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Synthetic.Modules.RevitDOM;

namespace SyntheticTests.Modules.RevitDOM
{
    [TestFixture]
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
    }
}
