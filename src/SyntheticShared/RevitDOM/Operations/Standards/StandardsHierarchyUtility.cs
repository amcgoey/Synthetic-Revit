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
            var templateTaxonomies = StandardTypeRegistry.GetAllTemplateTaxonomies();

            var groups = templateTaxonomies
                .GroupBy(t => t.Group)
                .OrderBy(g => g.Key, StringComparer.Ordinal);

            foreach (var groupEntry in groups)
            {
                var group = new StandardGroupModel { Name = groupEntry.Key, IsExpanded = true };

                var sortedClasses = groupEntry
                    .Select(t => t.Class)
                    .Distinct()
                    .OrderBy(c => c, StringComparer.Ordinal);

                foreach (var className in sortedClasses)
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
            return StandardTypeRegistry.GetTaxonomy(element).Group;
        }

        /// <summary>
        /// Gets the class name that an element should belong to.
        /// </summary>
        public static string GetClassName(ElementModel element)
        {
            if (element == null) throw new ArgumentNullException(nameof(element));
            return StandardTypeRegistry.GetTaxonomy(element).Class;
        }
    }
}
