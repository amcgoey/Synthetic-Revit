using Synthetic.Modules.DetailItemFactory.Commands;
using Synthetic.Modules.DetailItemFactory.Handlers;
using Synthetic.Modules.DetailItemFactory.ViewModels;
using Synthetic.Modules.DetailItemFactory.Views;
using Synthetic.Modules.DetailItemFactory.Settings;

using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using View = Autodesk.Revit.DB.View;

using Synthetic.Shared.UI;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;
using Synthetic.Modules.MergeDuplicates.Handlers;
using Synthetic.Modules.StandardsManagement.ViewModels;
namespace Synthetic.Modules.DetailItemFactory.ViewModels
{
    /// <summary>
    /// ViewModel representing a single selected element for per-element configuration.
    /// </summary>
    public class SelectedElementItemViewModel : ViewModelBase
    {
        private string _selectedOrientation = string.Empty;

        /// <summary>
        /// Gets the Revit Element ID.
        /// </summary>
        public ElementId ElementId { get; }

        /// <summary>
        /// Gets the user-friendly display name.
        /// </summary>
        public string DisplayName { get; }

        /// <summary>
        /// Gets the list of available orientation options.
        /// </summary>
        public List<string> AvailableOrientations { get; } = new List<string>
        {
            "Plan",
            "Elev Front",
            "Elev Back",
            "Elev Side",
            "Elev Top",
            "Elev Bottom",
            "Elev Section"
        };

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
        /// <param name="element">The Revit element.</param>
        /// <param name="activeView">The active view context.</param>
        public SelectedElementItemViewModel(Element element, View activeView)
        {
            if (element == null) throw new ArgumentNullException(nameof(element));
            if (activeView == null) throw new ArgumentNullException(nameof(activeView));

            ElementId = element.Id;

            // Compute DisplayName: "[Category] - [Family Name] - [Type]"
            string categoryName = element.Category?.Name ?? "UnknownCategory";
            string familyName = "UnknownFamily";
            string typeName = "UnknownType";

            if (element is FamilyInstance fi)
            {
                familyName = fi.Symbol.Family.Name;
                typeName = fi.Symbol.Name;
            }
            else
            {
                ElementId typeId = element.GetTypeId();
                if (typeId != ElementId.InvalidElementId && element.Document.GetElement(typeId) is ElementType et)
                {
                    familyName = et.FamilyName;
                    typeName = et.Name;
                }
                else
                {
                    familyName = element.Category?.Name ?? "Model";
                    typeName = element.Name ?? "Element";
                }
            }

            DisplayName = $"{categoryName} - {familyName} - {typeName}";

            // Smart default logic
            bool isLegendView = activeView.ViewType == ViewType.Legend;
            bool isLegendComponent = false;
#if REVIT2024 || REVIT2025 || REVIT2026
            isLegendComponent = element.Category != null && element.Category.Id.Value == (long)BuiltInCategory.OST_LegendComponents;
#else
            isLegendComponent = element.Category != null && element.Category.Id.IntegerValue == (int)BuiltInCategory.OST_LegendComponents;
#endif

            if (isLegendView && isLegendComponent)
            {
                SelectedOrientation = GetLegendComponentDefaultOrientation(element);
            }
            else
            {
                if (activeView.ViewType == ViewType.FloorPlan ||
                    activeView.ViewType == ViewType.CeilingPlan ||
                    activeView.ViewType == ViewType.EngineeringPlan ||
                    activeView.ViewType == ViewType.AreaPlan)
                {
                    SelectedOrientation = "Plan";
                }
                else if (activeView.ViewType == ViewType.Elevation ||
                         activeView.ViewType == ViewType.Section)
                {
                    SelectedOrientation = "Elev Front";
                }
                else
                {
                    SelectedOrientation = "Plan";
                }
            }
        }

        private string GetLegendComponentDefaultOrientation(Element element)
        {
            Parameter param = element.get_Parameter(BuiltInParameter.LEGEND_COMPONENT_VIEW);
            if (param != null)
            {
                string valStr = param.AsValueString();
                if (!string.IsNullOrEmpty(valStr))
                {
                    if (valStr.IndexOf("plan", StringComparison.OrdinalIgnoreCase) >= 0)
                        return "Plan";
                    if (valStr.IndexOf("front", StringComparison.OrdinalIgnoreCase) >= 0)
                        return "Elev Front";
                    if (valStr.IndexOf("back", StringComparison.OrdinalIgnoreCase) >= 0)
                        return "Elev Back";
                    if (valStr.IndexOf("left", StringComparison.OrdinalIgnoreCase) >= 0 || 
                        valStr.IndexOf("right", StringComparison.OrdinalIgnoreCase) >= 0 || 
                        valStr.IndexOf("side", StringComparison.OrdinalIgnoreCase) >= 0)
                        return "Elev Side";
                    if (valStr.IndexOf("top", StringComparison.OrdinalIgnoreCase) >= 0)
                        return "Elev Top";
                    if (valStr.IndexOf("bottom", StringComparison.OrdinalIgnoreCase) >= 0)
                        return "Elev Bottom";
                    if (valStr.IndexOf("section", StringComparison.OrdinalIgnoreCase) >= 0)
                        return "Elev Section";
                }
            }
            return "Plan";
        }
    }
}
