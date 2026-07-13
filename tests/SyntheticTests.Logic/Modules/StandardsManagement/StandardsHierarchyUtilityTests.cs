using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using System.Collections.ObjectModel;
using Synthetic.Modules.RevitDOM;
using Synthetic.Modules.StandardsManagement.Utilities;
using Synthetic.Modules.StandardsManagement.ViewModels;

namespace SyntheticTests.Modules.StandardsManagement
{
    [TestFixture]
    public class StandardsHierarchyUtilityTests
    {
        [Test]
        public void BuildHierarchy_ShouldGroupAndSortHeterogeneousListCorrectly()
        {
            // Arrange
            var elements = new List<ElementModel>
            {
                new MaterialModel { Class = "Autodesk.Revit.DB.Material", Name = "Cherry wood" },
                new MaterialModel { Class = "Autodesk.Revit.DB.Material", Name = "Oak wood" },
                new LinePatternElementModel { Class = "Autodesk.Revit.DB.LinePatternElement", Name = "DashDot" },
                new FilledRegionTypeModel { Class = "Autodesk.Revit.DB.FilledRegionType", Name = "Diagonal Cross" },
                new CurtainSystemTypeModel { Class = "Autodesk.Revit.DB.CurtainSystemType", Name = "5x10 system" },
                new ParameterElementModel { Class = "Autodesk.Revit.DB.SharedParameterElement", Name = "BIM_Maint_Schedule" },
                new ElementTypeModel { Class = "Autodesk.Revit.DB.TextNoteType", Name = "1/8 Arial" },
                new ElementModel { Class = "Autodesk.Revit.DB.DirectShapeType", Name = "Custom Shape" } // Fallback to "Other" -> "DirectShapeType"
            };

            // Act
            var hierarchy = StandardsHierarchyUtility.BuildHierarchy(elements);

            // Assert
            Assert.IsNotNull(hierarchy);
            
            // Expected group names in alphabetical order:
            // Annotations, Materials & Assets, Other, Standards & Categories, System Types
            var expectedGroups = new[] { "Annotations", "Materials & Assets", "Other", "Standards & Categories", "System Types" };
            var actualGroups = hierarchy.Select(g => g.Name).ToArray();
            Assert.AreEqual(expectedGroups, actualGroups, "Groups should be sorted alphabetically.");

            // 1. Check "Annotations" group
            var annotationsGroup = hierarchy[0];
            Assert.AreEqual("Annotations", annotationsGroup.Name);
            Assert.IsTrue(annotationsGroup.IsExpanded);
            // Classes inside: "Filled Region Types", "Text Note Types" (alphabetically sorted)
            var annotationsClasses = annotationsGroup.Children.Cast<StandardClassModel>().Select(c => c.Name).ToArray();
            Assert.AreEqual(new[] { "Filled Region Types", "Text Note Types" }, annotationsClasses);
            
            var filledRegionClass = annotationsGroup.Children.Cast<StandardClassModel>().First(c => c.Name == "Filled Region Types");
            Assert.AreEqual(annotationsGroup, filledRegionClass.Parent);
            Assert.AreEqual(1, filledRegionClass.Children.Count);
            Assert.AreEqual("Diagonal Cross", filledRegionClass.Children[0].Name);
            Assert.AreEqual(filledRegionClass, filledRegionClass.Children[0].Parent);

            // 2. Check "Materials & Assets" group
            var matGroup = hierarchy[1];
            Assert.AreEqual("Materials & Assets", matGroup.Name);
            // Classes inside: "Line Patterns", "Materials"
            var matClasses = matGroup.Children.Cast<StandardClassModel>().Select(c => c.Name).ToArray();
            Assert.AreEqual(new[] { "Line Patterns", "Materials" }, matClasses);
            
            var materialClass = matGroup.Children.Cast<StandardClassModel>().First(c => c.Name == "Materials");
            // Elements inside sorted alphabetically: "Cherry wood", "Oak wood"
            var materialsElements = materialClass.Children.Cast<StandardElementModel>().Select(e => e.Name).ToArray();
            Assert.AreEqual(new[] { "Cherry wood", "Oak wood" }, materialsElements);

            // 3. Check "Other" group (DirectShapeType falls to Other -> DirectShapeType)
            var otherGroup = hierarchy[2];
            Assert.AreEqual("Other", otherGroup.Name);
            var otherClasses = otherGroup.Children.Cast<StandardClassModel>().Select(c => c.Name).ToArray();
            Assert.AreEqual(new[] { "Autodesk.Revit.DB.DirectShapeType" }, otherClasses);

            // 4. Check "Standards & Categories" group
            var stdGroup = hierarchy[3];
            Assert.AreEqual("Standards & Categories", stdGroup.Name);
            var stdClasses = stdGroup.Children.Cast<StandardClassModel>().Select(c => c.Name).ToArray();
            Assert.AreEqual(new[] { "Shared Parameters" }, stdClasses);

            // 5. Check "System Types" group
            var sysGroup = hierarchy[4];
            Assert.AreEqual("System Types", sysGroup.Name);
            var sysClasses = sysGroup.Children.Cast<StandardClassModel>().Select(c => c.Name).ToArray();
            Assert.AreEqual(new[] { "Curtain System Types" }, sysClasses);
        }

        [Test]
        public void GenerateTemplateHierarchy_ShouldCreateCorrectStructure()
        {
            // Act
            var hierarchy = StandardsHierarchyUtility.GenerateTemplateHierarchy();

            // Assert
            Assert.IsNotNull(hierarchy);
            
            // Expected group names in alphabetical order:
            // Annotations, Materials & Assets, Other, Standards & Categories, System Types, Views
            var expectedGroups = new[] { "Annotations", "Materials & Assets", "Other", "Standards & Categories", "System Types", "Views" };
            var actualGroups = hierarchy.Select(g => g.Name).ToArray();
            Assert.AreEqual(expectedGroups, actualGroups);

            // Verify Annotations class count and names (8 classes)
            var annotationsGroup = hierarchy[0];
            Assert.AreEqual("Annotations", annotationsGroup.Name);
            Assert.IsTrue(annotationsGroup.IsExpanded);
            var annotationsClasses = annotationsGroup.Children.Cast<StandardClassModel>().Select(c => c.Name).ToArray();
            var expectedAnnotationsClasses = new[]
            {
                "Detail Items",
                "Dimension Types",
                "Filled Region Types",
                "Grid Types",
                "Label Types",
                "Level Types",
                "Model Text Types",
                "Spot Dimension Types",
                "Text Note Types",
                "Title Blocks"
            };
            Assert.AreEqual(expectedAnnotationsClasses, annotationsClasses);

            // Verify child parent link
            foreach (var child in annotationsGroup.Children)
            {
                Assert.AreEqual(annotationsGroup, child.Parent);
                Assert.AreEqual(0, child.Children.Count, "Template classes should have no element leaves.");
            }

            // Verify Materials & Assets (4 classes)
            var matGroup = hierarchy[1];
            var matClasses = matGroup.Children.Cast<StandardClassModel>().Select(c => c.Name).ToArray();
            Assert.AreEqual(new[] { "Appearance Assets", "Fill Patterns", "Line Patterns", "Materials" }, matClasses);

            // Verify Other (1 class)
            var otherGroup = hierarchy[2];
            var otherClasses = otherGroup.Children.Cast<StandardClassModel>().Select(c => c.Name).ToArray();
            Assert.AreEqual(new[] { "Unknown Class" }, otherClasses);

            // Verify Standards & Categories (4 classes)
            var stdGroup = hierarchy[3];
            var stdClasses = stdGroup.Children.Cast<StandardClassModel>().Select(c => c.Name).ToArray();
            Assert.AreEqual(new[] { "Categories", "Element Types", "Filters", "Shared Parameters" }, stdClasses);

            // Verify System Types (13 classes)
            var sysGroup = hierarchy[4];
            var sysClasses = sysGroup.Children.Cast<StandardClassModel>().Select(c => c.Name).ToArray();
            var expectedSysClasses = new[]
            {
                "Ceiling Types",
                "Curtain System Types",
                "Fascia Types",
                "Floor Types",
                "Gutter Types",
                "Host Object Types",
                "Mullion Types",
                "Profiles",
                "Railing Types",
                "Roof Types",
                "Stairs Types",
                "Toposolid Types",
                "Wall Types"
            };
            Assert.AreEqual(expectedSysClasses, sysClasses);

            // Verify Views (4 classes)
            var viewsGroup = hierarchy[5];
            var viewsClasses = viewsGroup.Children.Cast<StandardClassModel>().Select(c => c.Name).ToArray();
            Assert.AreEqual(new[] { "Browser Organizations", "View Family Types", "View Templates", "Views" }, viewsClasses);
        }
    }
}
