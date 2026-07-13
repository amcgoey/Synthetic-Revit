using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Autodesk.Revit.DB;

using Synthetic.Shared.UI;

using Synthetic.Modules.RevitDOM;
using Synthetic.Shared.RevitAPI;
namespace Synthetic.Modules.StandardsManagement.ViewModels
{
    /// <summary>
    /// ViewModel that handles Revit category filtering.
    /// </summary>
    public class CategorySelectionViewModel : ViewModelBase
    {
        private readonly Document _doc;
        private CategorySelectionItem? _selectedCategoryFilter;

        /// <summary>
        /// Gets the list of category filter options.
        /// </summary>
        public ObservableCollection<CategorySelectionItem> CategoryFilters { get; } = new ObservableCollection<CategorySelectionItem>();

        /// <summary>
        /// Gets or sets the selected category filter.
        /// </summary>
        public CategorySelectionItem? SelectedCategoryFilter
        {
            get => _selectedCategoryFilter;
            set => SetProperty(ref _selectedCategoryFilter, value);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="CategorySelectionViewModel"/> class.
        /// </summary>
        /// <param name="doc">The Revit Document context.</param>
        public CategorySelectionViewModel(Document doc)
        {
            _doc = doc ?? throw new ArgumentNullException(nameof(doc));
            PopulateCategoryFilters();
        }

        private void PopulateCategoryFilters()
        {
            try
            {
                CategoryFilters.Clear();
                CategoryFilters.Add(new CategorySelectionItem { Name = "All Categories", Id = ElementId.InvalidElementId });
                CategoryFilters.Add(new CategorySelectionItem { Name = "Annotations Only", Id = ElementId.InvalidElementId });
                CategoryFilters.Add(new CategorySelectionItem { Name = "Title Blocks Only", Id = ElementId.InvalidElementId });

                var familySymbols = new FilteredElementCollector(_doc)
                    .OfClass(typeof(FamilySymbol))
                    .Cast<FamilySymbol>()
                    .ToList();

                var uniqueCats = familySymbols
                    .Select(fs => fs.Category)
                    .Where(c => c != null)
                    .GroupBy(c => c.Name)
                    .Select(g => g.First())
                    .OrderBy(c => c.Name)
                    .ToList();

                foreach (var cat in uniqueCats)
                {
                    var item = new CategorySelectionItem
                    {
                        Name = cat.Name,
                        Id = cat.Id,
                        IsChecked = false
                    };
                    CategoryFilters.Add(item);
                }

                SelectedCategoryFilter = CategoryFilters.FirstOrDefault(cf => cf.Name == "Annotations Only") ?? CategoryFilters.FirstOrDefault();
            }
            catch {}
        }
    }

    /// <summary>
    /// Represents a category that can be checked or unchecked for export.
    /// </summary>
    public class CategorySelectionItem : ViewModelBase
    {
        private bool _isChecked;
        /// <summary>
        /// Gets or sets a value indicating whether the category is selected.
        /// </summary>
        public bool IsChecked
        {
            get => _isChecked;
            set => SetProperty(ref _isChecked, value);
        }

        /// <summary>
        /// Gets or sets the name of the category.
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the ElementId of the category.
        /// </summary>
        public ElementId Id { get; set; } = ElementId.InvalidElementId;
    }
}
