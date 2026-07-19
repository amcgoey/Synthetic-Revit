using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;
using Synthetic.Modules.StandardsManagement.ViewModels;

namespace Synthetic.RevitDOM.Operations.Standards
{
    /// <summary>
    /// Stateless utility for building, sorting, and mapping the Revit standards hierarchy taxonomy.
    /// </summary>
    public static class StandardsHierarchyUtility
    {
        /// <summary>
        /// Builds the nested Group -> Class -> Element hierarchy from a flat list of models.
        /// </summary>
        /// <param name="elements">The flat collection of element models.</param>
        /// <returns>A sorted hierarchical collection of group models.</returns>
        public static ObservableCollection<StandardGroupModel> BuildHierarchy(IEnumerable<ElementModel> elements)
        {
            if (elements == null) throw new ArgumentNullException(nameof(elements));

            var hierarchy = new ObservableCollection<StandardGroupModel>();

            foreach (var element in elements)
            {
                string groupName = GetGroupName(element);
                string className = GetClassName(element);

                var group = hierarchy.FirstOrDefault(g => g.Name.Equals(groupName, StringComparison.Ordinal));
                if (group == null)
                {
                    group = new StandardGroupModel { Name = groupName, IsExpanded = true };
                    hierarchy.Add(group);
                }

                var classModel = group.Children.OfType<StandardClassModel>().FirstOrDefault(c => c.Name.Equals(className, StringComparison.Ordinal));
                if (classModel == null)
                {
                    classModel = new StandardClassModel { Name = className };
                    group.Children.Add(classModel);
                    classModel.Parent = group;
                }

                var elementModel = new StandardElementModel(element);
                classModel.Children.Add(elementModel);
                elementModel.Parent = classModel;
            }

            // Sort groups, classes, and elements alphabetically for presentation
            var sortedGroups = hierarchy.OrderBy(g => g.Name).ToList();
            hierarchy.Clear();
            foreach (var group in sortedGroups)
            {
                var sortedClasses = group.Children.OfType<StandardClassModel>().OrderBy(c => c.Name).ToList();
                group.Children.Clear();
                foreach (var classModel in sortedClasses)
                {
                    var sortedElements = classModel.Children.OfType<StandardElementModel>().OrderBy(e => e.Name).ToList();
                    classModel.Children.Clear();
                    foreach (var elementModel in sortedElements)
                    {
                        classModel.Children.Add(elementModel);
                    }
                    group.Children.Add(classModel);
                }
                hierarchy.Add(group);
            }

            return hierarchy;
        }

        /// <summary>
        /// Generates a completely empty template hierarchy containing all supported primary groups and classes.
        /// </summary>
        /// <returns>A sorted hierarchical collection of empty group models.</returns>
        public static ObservableCollection<StandardGroupModel> GenerateTemplateHierarchy()
        {
            var hierarchy = new ObservableCollection<StandardGroupModel>();

            var groupsAndClasses = new Dictionary<string, string[]>
            {
                {
                    "Materials & Assets", new[]
                    {
                        "Appearance Assets",
                        "Fill Patterns",
                        "Line Patterns",
                        "Materials"
                    }
                },
                {
                    "Annotations", new[]
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
                    }
                },
                {
                    "System Types", new[]
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
                    }
                },
                {
                    "Views", new[]
                    {
                        "Browser Organizations",
                        "View Family Types",
                        "View Templates",
                        "Views"
                    }
                },
                {
                    "Standards & Categories", new[]
                    {
                        "Categories",
                        "Element Types",
                        "Filters",
                        "Shared Parameters"
                    }
                },
                {
                    "Other", new[]
                    {
                        "Unknown Class"
                    }
                }
            };

            foreach (var kvp in groupsAndClasses.OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                var groupName = kvp.Key;
                var classNames = kvp.Value;

                var group = new StandardGroupModel { Name = groupName, IsExpanded = true };

                foreach (var className in classNames.OrderBy(c => c, StringComparer.Ordinal))
                {
                    var classModel = new StandardClassModel { Name = className };
                    group.Children.Add(classModel);
                    classModel.Parent = group;
                }

                hierarchy.Add(group);
            }

            return hierarchy;
        }

        /// <summary>
        /// Gets the primary group name that an element should belong to.
        /// </summary>
        public static string GetGroupName(ElementModel element)
        {
            if (element == null) throw new ArgumentNullException(nameof(element));

            if (element is LinePatternElementModel || element is FillPatternElementModel || element is PropertySetElementModel || element is MaterialModel)
            {
                return "Materials & Assets";
            }
            if (element is FilledRegionTypeModel || element is DimensionTypeModel || element is GridTypeModel || element is LevelTypeModel)
            {
                return "Annotations";
            }
            if (element is CurtainSystemTypeModel || element is MullionTypeModel || element is FasciaTypeModel || element is GutterTypeModel)
            {
                return "System Types";
            }
            if (element is ViewModel)
            {
                return "Views";
            }
            if (element is ParameterElementModel || element is ParameterFilterElementModel || element is CategoryModel)
            {
                return "Standards & Categories";
            }
            if (element is ElementTypeModel elemType)
            {
                if (string.Equals(elemType.Class, "Autodesk.Revit.DB.TextNoteType", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(elemType.Class, "Autodesk.Revit.DB.TextElementType", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(elemType.Class, "Autodesk.Revit.DB.ModelTextType", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(elemType.Class, "Autodesk.Revit.DB.SpotDimensionType", StringComparison.OrdinalIgnoreCase))
                {
                    return "Annotations";
                }

                string className = GetClassName(element);
                if (className == "Label Types" || className == "Title Blocks" || className == "Detail Items")
                {
                    return "Annotations";
                }
                if (className == "Profiles")
                {
                    return "System Types";
                }

                return "Standards & Categories";
            }
            if (element is HostObjTypeModel)
            {
                return "System Types";
            }
            return "Other";
        }

        /// <summary>
        /// Gets the class name that an element should belong to.
        /// </summary>
        public static string GetClassName(ElementModel element)
        {
            if (element == null) throw new ArgumentNullException(nameof(element));

            if (element is LinePatternElementModel) return "Line Patterns";
            if (element is FillPatternElementModel) return "Fill Patterns";
            if (element is PropertySetElementModel) return "Appearance Assets";
            if (element is MaterialModel) return "Materials";
            if (element is FilledRegionTypeModel) return "Filled Region Types";
            if (element is DimensionTypeModel) return "Dimension Types";
            if (element is GridTypeModel) return "Grid Types";
            if (element is LevelTypeModel) return "Level Types";
            if (element is CurtainSystemTypeModel) return "Curtain System Types";
            if (element is MullionTypeModel) return "Mullion Types";
            if (element is FasciaTypeModel) return "Fascia Types";
            if (element is GutterTypeModel) return "Gutter Types";
            if (element is ViewFamilyTypeModel) return "View Family Types";
            if (element is BrowserOrganizationModel) return "Browser Organizations";
            if (element is ParameterElementModel) return "Shared Parameters";
            if (element is ParameterFilterElementModel) return "Filters";
            if (element is CategoryModel) return "Categories";
            if (element is ViewModel vm)
            {
                return vm.IsTemplate ? "View Templates" : "Views";
            }
            if (element is ElementTypeModel elemType)
            {
                if (string.Equals(elemType.Class, "Autodesk.Revit.DB.TextNoteType", StringComparison.OrdinalIgnoreCase)) return "Text Note Types";
                if (string.Equals(elemType.Class, "Autodesk.Revit.DB.TextElementType", StringComparison.OrdinalIgnoreCase)) return "Label Types";
                if (string.Equals(elemType.Class, "Autodesk.Revit.DB.ModelTextType", StringComparison.OrdinalIgnoreCase)) return "Model Text Types";
                if (string.Equals(elemType.Class, "Autodesk.Revit.DB.SpotDimensionType", StringComparison.OrdinalIgnoreCase)) return "Spot Dimension Types";

                if (string.Equals(elemType.Class, "Autodesk.Revit.DB.FamilySymbol", StringComparison.OrdinalIgnoreCase))
                {
                    string category = elemType.Category ?? string.Empty;
                    if (string.Equals(category, "Title Blocks", StringComparison.OrdinalIgnoreCase)) return "Title Blocks";
                    if (string.Equals(category, "Detail Items", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(category, "Detail Components", StringComparison.OrdinalIgnoreCase)) return "Detail Items";
                    if (string.Equals(category, "Profiles", StringComparison.OrdinalIgnoreCase)) return "Profiles";

                    if (category.EndsWith("Tags", StringComparison.OrdinalIgnoreCase) ||
                        category.EndsWith("Tag", StringComparison.OrdinalIgnoreCase) ||
                        category.IndexOf("Annotation", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return "Label Types";
                    }
                }

                return "Element Types";
            }
            if (element is HostObjTypeModel hostObj)
            {
                string cls = hostObj.Class ?? string.Empty;
                if (cls.Contains("WallType")) return "Wall Types";
                if (cls.Contains("FloorType")) return "Floor Types";
                if (cls.Contains("RoofType")) return "Roof Types";
                if (cls.Contains("CeilingType")) return "Ceiling Types";
                if (cls.Contains("RailingType")) return "Railing Types";
                if (cls.Contains("StairsType")) return "Stairs Types";
                if (cls.Contains("ToposolidType")) return "Toposolid Types";
                return "Host Object Types";
            }
            return element.Class ?? "Unknown Class";
        }
    }
}
