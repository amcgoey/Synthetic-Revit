using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.Modules.StandardsManagement.Utilities;

namespace SyntheticTests.Modules.StandardsManagement
{
    [TestFixture]
    public class SelectRevitDocumentTests
    {
        [Test]
        public void Constructor_ShouldInitializeFilterHierarchyAndCheckAllByDefault()
        {
            // Arrange & Act
            var vm = new SelectRevitDocumentViewModel(uiapp: null, mockDocs: null);

            // Assert
            Assert.IsNotNull(vm.FilterHierarchy);
            Assert.IsTrue(vm.FilterHierarchy.Count > 0);

            // Verify all groups and classes are checked by default
            foreach (var group in vm.FilterHierarchy)
            {
                Assert.AreEqual(true, group.IsChecked, $"Group '{group.Name}' should be checked by default.");
                foreach (var classNode in group.Children.OfType<StandardClassModel>())
                {
                    Assert.AreEqual(true, classNode.IsChecked, $"Class '{classNode.Name}' under Group '{group.Name}' should be checked by default.");
                }
            }

            // Verify default family processing values are false
            Assert.IsFalse(vm.ScanFamilies);
            Assert.IsFalse(vm.IncludeNestedFamilies);
        }

        [Test]
        public void TogglingOffScanFamilies_ShouldResetIncludeNestedFamiliesToFalse()
        {
            // Arrange
            var vm = new SelectRevitDocumentViewModel(uiapp: null, mockDocs: null);

            // Act - Enable both
            vm.ScanFamilies = true;
            vm.IncludeNestedFamilies = true;
            Assert.IsTrue(vm.ScanFamilies);
            Assert.IsTrue(vm.IncludeNestedFamilies);

            // Act - Disable primary scanning
            vm.ScanFamilies = false;

            // Assert - Nested scanning should be forced off
            Assert.IsFalse(vm.ScanFamilies);
            Assert.IsFalse(vm.IncludeNestedFamilies);
        }

        [Test]
        public void TogglingOnScanFamilies_ShouldPreserveIncludeNestedFamiliesState()
        {
            // Arrange
            var vm = new SelectRevitDocumentViewModel(uiapp: null, mockDocs: null);

            // Act
            vm.ScanFamilies = true;
            vm.IncludeNestedFamilies = true;

            // Assert
            Assert.IsTrue(vm.ScanFamilies);
            Assert.IsTrue(vm.IncludeNestedFamilies);
        }

        [Test]
        public void SelectedFamilyGroupings_ShouldReflectCheckedClasses()
        {
            // Arrange
            var vm = new SelectRevitDocumentViewModel(uiapp: null, mockDocs: null);

            // Act & Assert - Initially all should be selected
            var initialChecked = vm.SelectedFamilyGroupings;
            // The template has 36 classes (Annotations: 10, Materials & Assets: 4, Other: 1, Standards & Categories: 4, System Types: 13, Views: 4)
            Assert.AreEqual(36, initialChecked.Count);

            // Uncheck one class specifically (e.g. "Materials")
            var matGroup = vm.FilterHierarchy.First(g => g.Name == "Materials & Assets");
            var materialsClass = matGroup.Children.OfType<StandardClassModel>().First(c => c.Name == "Materials");
            
            materialsClass.IsChecked = false;

            // Assert - "Materials" should be missing, leaving 35 classes
            var afterUncheck = vm.SelectedFamilyGroupings;
            Assert.AreEqual(35, afterUncheck.Count);
            Assert.IsFalse(afterUncheck.Contains("Materials"));
            Assert.IsTrue(afterUncheck.Contains("Appearance Assets")); // Siblings remain checked

            // Uncheck entire group "Views"
            var viewsGroup = vm.FilterHierarchy.First(g => g.Name == "Views");
            viewsGroup.IsChecked = false; // Cascades to all child classes (Browser Organizations, View Family Types, View Templates, Views)

            // Assert - The 4 view classes should also be missing, leaving 31 checked classes
            var afterGroupUncheck = vm.SelectedFamilyGroupings;
            Assert.AreEqual(31, afterGroupUncheck.Count);
            Assert.IsFalse(afterGroupUncheck.Contains("Views"));
            Assert.IsFalse(afterGroupUncheck.Contains("View Templates"));
            Assert.IsFalse(afterGroupUncheck.Contains("View Family Types"));
            Assert.IsFalse(afterGroupUncheck.Contains("Browser Organizations"));
        }
    }
}
