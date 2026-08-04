using System;
using System.Reflection;
using NUnit.Framework;
using Autodesk.Revit.DB;
using Synthetic.Modules.MergeDuplicates.Services;

namespace SyntheticTests.Logic.Modules.MergeDuplicates
{
    [TestFixture]
    public class RevitMergeDataCollectorTests
    {
        private T CreateElement<T>() where T : class
        {
            return (T)Activator.CreateInstance(typeof(T), true)!;
        }

        private void SetPropertyValue(object obj, string propertyName, object value)
        {
            PropertyInfo? prop = obj.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (prop != null && prop.CanWrite)
            {
                prop.SetValue(obj, value);
            }
            else if (prop != null)
            {
                FieldInfo? backingField = obj.GetType().GetField($"<{propertyName}>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance);
                if (backingField != null)
                {
                    backingField.SetValue(obj, value);
                }
            }
        }

        [Test]
        public void IsSystemElement_ReturnsFalseForLoadableEditableFamily()
        {
            var family = CreateElement<Family>();
            SetPropertyValue(family, "Name", "Single-Flush Door");
            SetPropertyValue(family, "IsEditable", true);
            SetPropertyValue(family, "IsInPlace", false);

            bool isSystem = RevitMergeDataCollector.IsSystemElement(family);
            Assert.IsFalse(isSystem, "Loadable editable family should not be classified as system element.");
        }

        [Test]
        public void IsSystemElement_ReturnsTrueForInPlaceFamily()
        {
            var inPlaceFamily = CreateElement<Family>();
            SetPropertyValue(inPlaceFamily, "Name", "Custom In-Place Desk");
            SetPropertyValue(inPlaceFamily, "IsEditable", true);
            SetPropertyValue(inPlaceFamily, "IsInPlace", true);

            bool isSystem = RevitMergeDataCollector.IsSystemElement(inPlaceFamily);
            Assert.IsTrue(isSystem, "In-place family should be classified as system element.");
        }

        [Test]
        public void IsSystemElement_ReturnsTrueForNonEditableFamily()
        {
            var nonEditableFamily = CreateElement<Family>();
            SetPropertyValue(nonEditableFamily, "Name", "System Built-in Family");
            SetPropertyValue(nonEditableFamily, "IsEditable", false);
            SetPropertyValue(nonEditableFamily, "IsInPlace", false);

            bool isSystem = RevitMergeDataCollector.IsSystemElement(nonEditableFamily);
            Assert.IsTrue(isSystem, "Non-editable family should be classified as system element.");
        }

        [Test]
        public void IsSystemElement_ReturnsTrueForSystemTypes()
        {
            var wallType = CreateElement<WallType>();
            SetPropertyValue(wallType, "Name", "Generic - 8\" Masonry");

            var viewFamilyType = CreateElement<ViewFamilyType>();
            SetPropertyValue(viewFamilyType, "Name", "Floor Plan");

            var elevationMarker = CreateElement<ElevationMarker>();
            SetPropertyValue(elevationMarker, "Name", "Elevation Mark Body");

            var groupType = CreateElement<GroupType>();
            SetPropertyValue(groupType, "Name", "Standard Office Layout");

            var assemblyType = CreateElement<AssemblyType>();
            SetPropertyValue(assemblyType, "Name", "Precast Panel A");

            var textNoteType = CreateElement<TextNoteType>();
            SetPropertyValue(textNoteType, "Name", "3/32\" Arial");

            var dimensionType = CreateElement<DimensionType>();
            SetPropertyValue(dimensionType, "Name", "Linear - 3/32\"");

            Assert.IsTrue(RevitMergeDataCollector.IsSystemElement(wallType), "WallType should be classified as system element.");
            Assert.IsTrue(RevitMergeDataCollector.IsSystemElement(viewFamilyType), "ViewFamilyType should be classified as system element.");
            Assert.IsTrue(RevitMergeDataCollector.IsSystemElement(elevationMarker), "ElevationMarker should be classified as system element.");
            Assert.IsTrue(RevitMergeDataCollector.IsSystemElement(groupType), "GroupType should be classified as system element.");
            Assert.IsTrue(RevitMergeDataCollector.IsSystemElement(assemblyType), "AssemblyType should be classified as system element.");
            Assert.IsTrue(RevitMergeDataCollector.IsSystemElement(textNoteType), "TextNoteType should be classified as system element.");
            Assert.IsTrue(RevitMergeDataCollector.IsSystemElement(dimensionType), "DimensionType should be classified as system element.");
        }

        [Test]
        public void IsSystemElement_HandlesFamilySymbolsCorrectly()
        {
            var loadableFam = CreateElement<Family>();
            SetPropertyValue(loadableFam, "IsEditable", true);
            SetPropertyValue(loadableFam, "IsInPlace", false);

            var validSymbol = CreateElement<FamilySymbol>();
            SetPropertyValue(validSymbol, "Family", loadableFam);
            Assert.IsFalse(RevitMergeDataCollector.IsSystemElement(validSymbol), "FamilySymbol from editable loadable family should not be system element.");

            var inPlaceFam = CreateElement<Family>();
            SetPropertyValue(inPlaceFam, "IsEditable", true);
            SetPropertyValue(inPlaceFam, "IsInPlace", true);

            var inPlaceSymbol = CreateElement<FamilySymbol>();
            SetPropertyValue(inPlaceSymbol, "Family", inPlaceFam);
            Assert.IsTrue(RevitMergeDataCollector.IsSystemElement(inPlaceSymbol), "FamilySymbol from in-place family should be system element.");

            var nullFamSymbol = CreateElement<FamilySymbol>();
            SetPropertyValue(nullFamSymbol, "Family", null);
            Assert.IsTrue(RevitMergeDataCollector.IsSystemElement(nullFamSymbol), "FamilySymbol with null Family should be classified as system element.");
        }

        [Test]
        public void IsSystemElement_ReturnsTrueForNullElement()
        {
            Assert.IsTrue(RevitMergeDataCollector.IsSystemElement(null), "Null element should be classified as system element (safe fallback).");
        }
    }
}
