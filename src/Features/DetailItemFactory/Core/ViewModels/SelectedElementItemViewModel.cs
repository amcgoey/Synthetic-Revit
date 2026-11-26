using System;
using System.Collections.Generic;
using Synthetic.Features.DetailItemFactory.Models;
using Synthetic.Features.DetailItemFactory.Services;
using Synthetic.Platform.UI;

namespace Synthetic.Features.DetailItemFactory.ViewModels
{
    /// <summary>
    /// ViewModel representing a single selected element for per-element orientation configuration.
    /// </summary>
    public class SelectedElementItemViewModel : ViewModelBase
    {
        private string _selectedOrientation = DetailItemOrientations.Plan;

        /// <summary>
        /// Gets the Revit <c>UniqueId</c> of the element.
        /// </summary>
        public string ElementUniqueId { get; }

        /// <summary>
        /// Gets the displayable Revit <c>ElementId</c> text.
        /// </summary>
        public string ElementId { get; }

        /// <summary>
        /// Gets the element family name.
        /// </summary>
        public string FamilyName { get; }

        /// <summary>
        /// Gets the element type name.
        /// </summary>
        public string TypeName { get; }

        /// <summary>
        /// Gets the user-friendly display name.
        /// </summary>
        public string DisplayName { get; }

        /// <summary>
        /// Gets the list of available orientation options.
        /// </summary>
        public IReadOnlyList<string> AvailableOrientations { get; } = DetailItemOrientations.All;

        /// <summary>
        /// Gets or sets the selected orientation for this element.
        /// </summary>
        public string SelectedOrientation
        {
            get => _selectedOrientation;
            set => SetProperty(ref _selectedOrientation, value);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="SelectedElementItemViewModel"/> class.
        /// </summary>
        /// <param name="element">The harvested session element snapshot.</param>
        /// <param name="activeViewKind">The active 2D source view kind.</param>
        public SelectedElementItemViewModel(DetailItemSessionElement element, DetailItemSourceViewKind activeViewKind)
        {
            if (element == null)
            {
                throw new ArgumentNullException(nameof(element));
            }

            ElementUniqueId = element.ElementUniqueId;
            ElementId = element.ElementId;
            FamilyName = element.FamilyName;
            TypeName = element.TypeName;
            DisplayName = element.DisplayName;
            SelectedOrientation = DetailItemBatchPlanner.ResolveDefaultOrientation(
                activeViewKind,
                element.IsLegendComponent,
                element.LegendComponentViewValue);
        }
    }
}
