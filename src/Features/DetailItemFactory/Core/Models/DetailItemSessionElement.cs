using System;

namespace Synthetic.Features.DetailItemFactory.Models
{
    /// <summary>
    /// Immutable Core snapshot of a single eligible element harvested from the active Revit selection.
    /// </summary>
    public sealed class DetailItemSessionElement
    {
        /// <summary>
        /// Gets the Revit <c>UniqueId</c> identifying the element across the seam.
        /// </summary>
        public string ElementUniqueId { get; }

        /// <summary>
        /// Gets the displayable Revit <c>ElementId</c> text.
        /// </summary>
        public string ElementId { get; }

        /// <summary>
        /// Gets the resolved category name.
        /// </summary>
        public string CategoryName { get; }

        /// <summary>
        /// Gets the resolved family name.
        /// </summary>
        public string FamilyName { get; }

        /// <summary>
        /// Gets the resolved type name.
        /// </summary>
        public string TypeName { get; }

        /// <summary>
        /// Gets the user-facing display label (<c>"Category - Family - Type"</c>).
        /// </summary>
        public string DisplayName { get; }

        /// <summary>
        /// Gets a value indicating whether the element is a Legend Component (<c>OST_LegendComponents</c>).
        /// </summary>
        public bool IsLegendComponent { get; }

        /// <summary>
        /// Gets the raw <c>LEGEND_COMPONENT_VIEW</c> value string when <see cref="IsLegendComponent"/> is <c>true</c>.
        /// </summary>
        public string? LegendComponentViewValue { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="DetailItemSessionElement"/> class.
        /// </summary>
        public DetailItemSessionElement(
            string elementUniqueId,
            string elementId,
            string categoryName,
            string familyName,
            string typeName,
            bool isLegendComponent = false,
            string? legendComponentViewValue = null)
        {
            if (string.IsNullOrWhiteSpace(elementUniqueId))
            {
                throw new ArgumentException("Element UniqueId cannot be blank.", nameof(elementUniqueId));
            }

            ElementUniqueId = elementUniqueId;
            ElementId = string.IsNullOrWhiteSpace(elementId) ? elementUniqueId : elementId;
            CategoryName = string.IsNullOrWhiteSpace(categoryName) ? "UnknownCategory" : categoryName;
            FamilyName = string.IsNullOrWhiteSpace(familyName) ? "UnknownFamily" : familyName;
            TypeName = string.IsNullOrWhiteSpace(typeName) ? "UnknownType" : typeName;
            DisplayName = $"{CategoryName} - {FamilyName} - {TypeName}";
            IsLegendComponent = isLegendComponent;
            LegendComponentViewValue = legendComponentViewValue;
        }
    }
}
