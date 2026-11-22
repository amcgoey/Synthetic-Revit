using System;
using System.Collections.Generic;
using NUnit.Framework;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Operations.Merge;

namespace SyntheticTests.Modules.StandardsManagement
{
    [TestFixture]
    public class ParameterDiffRowModelTests
    {
        [Test]
        public void RadioButtonBindings_EvaluateCorrectlyOnLoad_UsingValueEquality()
        {
            // Arrange: distinct ElementIdModel instances representing same elements
            var sourceId = new ElementIdModel { Id = 1001, Name = "SourceType", Class = "Autodesk.Revit.DB.FamilySymbol" };
            var targetId = new ElementIdModel { Id = 2002, Name = "TargetType", Class = "Autodesk.Revit.DB.FamilySymbol" };

            var sourceIdDistinct = new ElementIdModel { Id = 1001, Name = "SourceType", Class = "Autodesk.Revit.DB.FamilySymbol" };
            var targetIdDistinct = new ElementIdModel { Id = 2002, Name = "TargetType", Class = "Autodesk.Revit.DB.FamilySymbol" };

            var row = new ParameterDiffRowModel
            {
                ParameterName = "Comments",
                Options = new List<ParameterValueOption>
                {
                    new ParameterValueOption { ElementId = sourceId, DisplayText = "Source Comment" },
                    new ParameterValueOption { ElementId = targetId, DisplayText = "Target Comment" }
                },
                // Set winning value to distinct instance equal to sourceId
                WinningValueElementId = sourceIdDistinct
            };

            // Assert
            Assert.IsTrue(row.IsSourceWinning, "IsSourceWinning should evaluate to true when WinningValueElementId is value-equal to Options[0].ElementId.");
            Assert.IsFalse(row.IsTargetWinning, "IsTargetWinning should evaluate to false when source is winning.");

            // Act: change winning value to distinct instance equal to targetId
            row.WinningValueElementId = targetIdDistinct;

            // Assert
            Assert.IsFalse(row.IsSourceWinning, "IsSourceWinning should evaluate to false when target is winning.");
            Assert.IsTrue(row.IsTargetWinning, "IsTargetWinning should evaluate to true when WinningValueElementId is value-equal to Options[1].ElementId.");
        }

        [Test]
        public void RadioButtonBindings_RespondToUserSelectionChanges()
        {
            var sourceId = new ElementIdModel { Id = 1001, Name = "SourceType" };
            var targetId = new ElementIdModel { Id = 2002, Name = "TargetType" };

            var row = new ParameterDiffRowModel
            {
                ParameterName = "Width",
                Options = new List<ParameterValueOption>
                {
                    new ParameterValueOption { ElementId = sourceId, DisplayText = "100" },
                    new ParameterValueOption { ElementId = targetId, DisplayText = "200" }
                },
                WinningValueElementId = sourceId
            };

            List<string> changedProperties = new List<string>();
            row.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName != null) changedProperties.Add(e.PropertyName);
            };

            // Act: simulate user clicking Target RadioButton (TwoWay binding sets IsTargetWinning = true)
            row.IsTargetWinning = true;

            // Assert
            Assert.IsTrue(row.IsTargetWinning, "IsTargetWinning should be true after user selection.");
            Assert.IsFalse(row.IsSourceWinning, "IsSourceWinning should be false after target selection.");
            Assert.AreEqual(targetId, row.WinningValueElementId, "WinningValueElementId should be updated to targetId.");
            Assert.Contains(nameof(row.IsSourceWinning), changedProperties, "PropertyChanged should fire for IsSourceWinning.");
            Assert.Contains(nameof(row.IsTargetWinning), changedProperties, "PropertyChanged should fire for IsTargetWinning.");

            // Act: simulate user clicking Source RadioButton
            changedProperties.Clear();
            row.IsSourceWinning = true;

            // Assert
            Assert.IsTrue(row.IsSourceWinning, "IsSourceWinning should be true after user selection.");
            Assert.IsFalse(row.IsTargetWinning, "IsTargetWinning should be false after source selection.");
            Assert.AreEqual(sourceId, row.WinningValueElementId, "WinningValueElementId should be updated to sourceId.");
            Assert.Contains(nameof(row.IsSourceWinning), changedProperties, "PropertyChanged should fire for IsSourceWinning.");
            Assert.Contains(nameof(row.IsTargetWinning), changedProperties, "PropertyChanged should fire for IsTargetWinning.");
        }

        [Test]
        public void GetValueForElement_DistinctElementIdModelInstances_DoesNotThrowKeyNotFoundException()
        {
            var keyInDict = new ElementIdModel { Id = 5005, UniqueId = "uid-5005", Name = "DoorType", Class = "Autodesk.Revit.DB.FamilySymbol" };
            var distinctQueryKey = new ElementIdModel { Id = 5005, UniqueId = "uid-5005", Name = "DoorType", Class = "Autodesk.Revit.DB.FamilySymbol" };

            var row = new ParameterDiffRowModel
            {
                ParameterName = "Cost"
            };

            row.Values[keyInDict] = "150.00";
            row.WinningValueElementId = distinctQueryKey;

            // Act & Assert
            Assert.DoesNotThrow(() =>
            {
                string value = row.GetValueForElement(distinctQueryKey);
                Assert.AreEqual("150.00", value, "GetValueForElement should retrieve the dictionary value using value equality.");
            }, "GetValueForElement must not throw KeyNotFoundException when queried with distinct ElementIdModel instances.");

            Assert.AreEqual("150.00", row.WinningValue, "WinningValue property must return the winning value without throwing KeyNotFoundException.");
        }

        [Test]
        public void DictionaryKeyLookup_AcrossElementIdModelKeys_SucceedsForDistinctInstances()
        {
            var keyAdded = new ElementIdModel { Id = 7007, Name = "Window", Class = "Autodesk.Revit.DB.FamilySymbol" };
            var keyQueried = new ElementIdModel { Id = 7007, Name = "Window", Class = "Autodesk.Revit.DB.FamilySymbol" };

            var dict = new Dictionary<ElementIdModel, string>();
            dict[keyAdded] = "SampleValue";

            Assert.IsTrue(dict.ContainsKey(keyQueried), "Dictionary.ContainsKey must return true for value-equal distinct ElementIdModel key.");
            Assert.DoesNotThrow(() =>
            {
                string val = dict[keyQueried];
                Assert.AreEqual("SampleValue", val, "Dictionary indexer retrieval must return value for distinct ElementIdModel key.");
            });
        }
    }
}
