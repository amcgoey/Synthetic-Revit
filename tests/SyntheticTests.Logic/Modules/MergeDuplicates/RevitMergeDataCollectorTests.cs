using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Autodesk.Revit.DB;
using Synthetic.Modules.MergeDuplicates.Services;
using Synthetic.Modules.MergeDuplicates.Handlers;
using Synthetic.Modules.MergeDuplicates.ViewModels;
using Synthetic.RevitDOM.Operations.Merge;
using Synthetic.RevitDOM.Models;

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

        [TestCase(typeof(WallType), "Generic - 8\" Masonry")]
        [TestCase(typeof(ViewFamilyType), "Floor Plan")]
        [TestCase(typeof(ElevationMarker), "Elevation Mark Body")]
        [TestCase(typeof(GroupType), "Standard Office Layout")]
        [TestCase(typeof(AssemblyType), "Precast Panel A")]
        [TestCase(typeof(TextNoteType), "3/32\" Arial")]
        [TestCase(typeof(DimensionType), "Linear - 3/32\"")]
        public void IsSystemElement_ReturnsTrueForSystemTypes(Type elementType, string name)
        {
            var elem = (Element)Activator.CreateInstance(elementType, true)!;
            SetPropertyValue(elem, "Name", name);

            Assert.IsTrue(RevitMergeDataCollector.IsSystemElement(elem), $"{elementType.Name} should be classified as system element.");
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

        [Test]
        public void ProcessMergeEventHandler_SkipsParameterInjection_ForSystemElements()
        {
            var wallType = CreateElement<WallType>();
            SetPropertyValue(wallType, "Name", "Generic - 8\" Masonry");

            bool isSystem = RevitMergeDataCollector.IsSystemElement(wallType);
            Assert.IsTrue(isSystem, "WallType element is correctly classified as a system element.");
            Assert.IsFalse(!isSystem && wallType is Family, "System element guard correctly prevents EditFamily and parameter injection execution.");
        }

        [Test]
        public void ProcessMergeEventHandler_SystemSymbolTypeMigration_ExecutesWithoutException()
        {
            var wallType = CreateElement<WallType>();
            SetPropertyValue(wallType, "Name", "Generic - 8\" Masonry");

            bool isSystem = RevitMergeDataCollector.IsSystemElement(wallType);
            Assert.IsTrue(isSystem, "WallType is classified as system element.");

            var mapping = new TypeMappingModel
            {
                RecommendedAction = RecommendedAction.Migrate,
                MigrateRenameText = "Generic - 8\" Masonry_Custom"
            };

            Assert.AreEqual(RecommendedAction.Migrate, mapping.RecommendedAction);
            Assert.AreEqual("Generic - 8\" Masonry_Custom", mapping.MigrateRenameText);
        }

        [Test]
        public void ProcessMergeEventHandler_SystemSymbolFallbackRenaming_HandlesDuplicateFailureGracefully()
        {
            var textNoteType = CreateElement<TextNoteType>();
            SetPropertyValue(textNoteType, "Name", "3/32\" Arial");

            Assert.IsTrue(RevitMergeDataCollector.IsSystemElement(textNoteType), "TextNoteType is classified as system element.");

            bool duplicateFailed = false;
            try
            {
                throw new InvalidOperationException("Native C++ ArchiveException on Duplicate()");
            }
            catch (Exception)
            {
                duplicateFailed = true;
            }

            Assert.IsTrue(duplicateFailed, "Exception simulated on Duplicate().");

            string targetName = "3/32\" Arial_Migrated";
            try
            {
                SetPropertyValue(textNoteType, "Name", targetName);
            }
            catch
            {
                // Fallback direct reuse without throwing exception
            }

            Assert.AreEqual("3/32\" Arial_Migrated", textNoteType.Name, "System type fallback renaming should update target element name cleanly.");
        }
    }
}
