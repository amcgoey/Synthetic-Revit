using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using Autodesk.Revit.DB;

using Synthetic.Shared.UI;

using Synthetic.Modules.RevitDOM;
using Synthetic.Shared.RevitAPI;
namespace Synthetic.Modules.StandardsManagement.ViewModels
{
    /// <summary>
    /// ViewModel managing the modular class-level selection settings.
    /// </summary>
    public class StandardsClassSelectionViewModel : ViewModelBase
    {
        private readonly Document _doc;

        #region Backing Fields
        private bool _exportTextNoteTypes;
        private bool _exportLabelTypes;
        private bool _exportDimensionTypes;
        private bool _exportFilledRegionTypes;
        private bool _exportMaterials;
        private bool _exportWallTypes;
        private bool _exportFloorTypes;
        private bool _exportRoofTypes;
        private bool _exportCeilingTypes;
        private bool _exportRailingTypes;
        private bool _exportStairsTypes;
        private bool _exportStandardViews;
        private bool _exportViewTemplates;
        private bool _exportLineStyles;
        private bool _exportModelCategories;
        private bool _exportAnnotationCategories;
        private bool _exportAnalyticalCategories;
        private bool _exportImportCategories;
        private bool _exportGridTypes;
        private bool _exportLevelTypes;
        private bool _exportFillPatterns;
        private bool _exportLinePatterns;
        private bool _exportAppearanceAssets;
        private bool _exportCurtainSystemTypes;
        private bool _exportMullionTypes;
        private bool _exportFasciaTypes;
        private bool _exportGutterTypes;
        private bool _exportTitleBlockTypes;
        private bool _exportViewFamilyTypes;
        private bool _exportBrowserOrganizations;
        private bool _exportParameterElements;
        private bool _exportParameterFilters;
        private bool _exportToposolidTypes;
        private bool _isSystemFamiliesEnabled = true;
        private bool _isViewsEnabled = true;
        #endregion

        #region Properties
        /// <summary>Gets or sets a value indicating whether to export TextNoteTypes.</summary>
        public bool ExportTextNoteTypes { get => _exportTextNoteTypes; set => SetProperty(ref _exportTextNoteTypes, value); }
        /// <summary>Gets or sets a value indicating whether to export LabelTypes.</summary>
        public bool ExportLabelTypes { get => _exportLabelTypes; set => SetProperty(ref _exportLabelTypes, value); }
        /// <summary>Gets or sets a value indicating whether to export DimensionTypes.</summary>
        public bool ExportDimensionTypes { get => _exportDimensionTypes; set => SetProperty(ref _exportDimensionTypes, value); }
        /// <summary>Gets or sets a value indicating whether to export FilledRegionTypes.</summary>
        public bool ExportFilledRegionTypes { get => _exportFilledRegionTypes; set => SetProperty(ref _exportFilledRegionTypes, value); }
        /// <summary>Gets or sets a value indicating whether to export Materials.</summary>
        public bool ExportMaterials { get => _exportMaterials; set => SetProperty(ref _exportMaterials, value); }
        /// <summary>Gets or sets a value indicating whether to export WallTypes.</summary>
        public bool ExportWallTypes { get => _exportWallTypes; set => SetProperty(ref _exportWallTypes, value); }
        /// <summary>Gets or sets a value indicating whether to export ToposolidTypes.</summary>
        public bool ExportToposolidTypes { get => _exportToposolidTypes; set => SetProperty(ref _exportToposolidTypes, value); }
        /// <summary>Gets or sets a value indicating whether to export FloorTypes.</summary>
        public bool ExportFloorTypes { get => _exportFloorTypes; set => SetProperty(ref _exportFloorTypes, value); }
        /// <summary>Gets or sets a value indicating whether to export RoofTypes.</summary>
        public bool ExportRoofTypes { get => _exportRoofTypes; set => SetProperty(ref _exportRoofTypes, value); }
        /// <summary>Gets or sets a value indicating whether to export CeilingTypes.</summary>
        public bool ExportCeilingTypes { get => _exportCeilingTypes; set => SetProperty(ref _exportCeilingTypes, value); }
        /// <summary>Gets or sets a value indicating whether to export RailingTypes.</summary>
        public bool ExportRailingTypes { get => _exportRailingTypes; set => SetProperty(ref _exportRailingTypes, value); }
        /// <summary>Gets or sets a value indicating whether to export StairsTypes.</summary>
        public bool ExportStairsTypes { get => _exportStairsTypes; set => SetProperty(ref _exportStairsTypes, value); }
        /// <summary>Gets or sets a value indicating whether to export StandardViews.</summary>
        public bool ExportStandardViews { get => _exportStandardViews; set => SetProperty(ref _exportStandardViews, value); }
        /// <summary>Gets or sets a value indicating whether to export ViewTemplates.</summary>
        public bool ExportViewTemplates { get => _exportViewTemplates; set => SetProperty(ref _exportViewTemplates, value); }
        /// <summary>Gets or sets a value indicating whether to export LineStyles.</summary>
        public bool ExportLineStyles { get => _exportLineStyles; set => SetProperty(ref _exportLineStyles, value); }
        /// <summary>Gets or sets a value indicating whether to export ModelCategories.</summary>
        public bool ExportModelCategories { get => _exportModelCategories; set => SetProperty(ref _exportModelCategories, value); }
        /// <summary>Gets or sets a value indicating whether to export AnnotationCategories.</summary>
        public bool ExportAnnotationCategories { get => _exportAnnotationCategories; set => SetProperty(ref _exportAnnotationCategories, value); }
        /// <summary>Gets or sets a value indicating whether to export AnalyticalCategories.</summary>
        public bool ExportAnalyticalCategories { get => _exportAnalyticalCategories; set => SetProperty(ref _exportAnalyticalCategories, value); }
        /// <summary>Gets or sets a value indicating whether to export ImportCategories.</summary>
        public bool ExportImportCategories { get => _exportImportCategories; set => SetProperty(ref _exportImportCategories, value); }
        /// <summary>Gets or sets a value indicating whether to export GridTypes.</summary>
        public bool ExportGridTypes { get => _exportGridTypes; set => SetProperty(ref _exportGridTypes, value); }
        /// <summary>Gets or sets a value indicating whether to export LevelTypes.</summary>
        public bool ExportLevelTypes { get => _exportLevelTypes; set => SetProperty(ref _exportLevelTypes, value); }
        /// <summary>Gets or sets a value indicating whether to export FillPatterns.</summary>
        public bool ExportFillPatterns { get => _exportFillPatterns; set => SetProperty(ref _exportFillPatterns, value); }
        /// <summary>Gets or sets a value indicating whether to export LinePatterns.</summary>
        public bool ExportLinePatterns { get => _exportLinePatterns; set => SetProperty(ref _exportLinePatterns, value); }
        /// <summary>Gets or sets a value indicating whether to export AppearanceAssets.</summary>
        public bool ExportAppearanceAssets { get => _exportAppearanceAssets; set => SetProperty(ref _exportAppearanceAssets, value); }
        /// <summary>Gets or sets a value indicating whether to export CurtainSystemTypes.</summary>
        public bool ExportCurtainSystemTypes { get => _exportCurtainSystemTypes; set => SetProperty(ref _exportCurtainSystemTypes, value); }
        /// <summary>Gets or sets a value indicating whether to export MullionTypes.</summary>
        public bool ExportMullionTypes { get => _exportMullionTypes; set => SetProperty(ref _exportMullionTypes, value); }
        /// <summary>Gets or sets a value indicating whether to export FasciaTypes.</summary>
        public bool ExportFasciaTypes { get => _exportFasciaTypes; set => SetProperty(ref _exportFasciaTypes, value); }
        /// <summary>Gets or sets a value indicating whether to export GutterTypes.</summary>
        public bool ExportGutterTypes { get => _exportGutterTypes; set => SetProperty(ref _exportGutterTypes, value); }
        /// <summary>Gets or sets a value indicating whether to export TitleBlockTypes.</summary>
        public bool ExportTitleBlockTypes { get => _exportTitleBlockTypes; set => SetProperty(ref _exportTitleBlockTypes, value); }
        /// <summary>Gets or sets a value indicating whether to export ViewFamilyTypes.</summary>
        public bool ExportViewFamilyTypes { get => _exportViewFamilyTypes; set => SetProperty(ref _exportViewFamilyTypes, value); }
        /// <summary>Gets or sets a value indicating whether to export BrowserOrganizations.</summary>
        public bool ExportBrowserOrganizations { get => _exportBrowserOrganizations; set => SetProperty(ref _exportBrowserOrganizations, value); }
        /// <summary>Gets or sets a value indicating whether to export ParameterElements.</summary>
        public bool ExportParameterElements { get => _exportParameterElements; set => SetProperty(ref _exportParameterElements, value); }
        /// <summary>Gets or sets a value indicating whether to export ParameterFilters.</summary>
        public bool ExportParameterFilters { get => _exportParameterFilters; set => SetProperty(ref _exportParameterFilters, value); }
        /// <summary>Gets or sets a value indicating whether system families export is enabled.</summary>
        public bool IsSystemFamiliesEnabled { get => _isSystemFamiliesEnabled; set => SetProperty(ref _isSystemFamiliesEnabled, value); }
        /// <summary>Gets or sets a value indicating whether views export is enabled.</summary>
        public bool IsViewsEnabled { get => _isViewsEnabled; set => SetProperty(ref _isViewsEnabled, value); }

        /// <summary>
        /// Gets the collection of loadable family categories.
        /// </summary>
        public ObservableCollection<CategorySelectionItem> LoadableFamilyCategories { get; } = new ObservableCollection<CategorySelectionItem>();
        #endregion

        #region Commands
        /// <summary>Command to select all annotation categories.</summary>
        public ICommand SelectAllAnnotationsCommand { get; }
        /// <summary>Command to deselect all annotation categories.</summary>
        public ICommand SelectNoneAnnotationsCommand { get; }
        /// <summary>Command to select all materials, patterns, and assets.</summary>
        public ICommand SelectAllMaterialsCommand { get; }
        /// <summary>Command to deselect all materials, patterns, and assets.</summary>
        public ICommand SelectNoneMaterialsCommand { get; }
        /// <summary>Command to select all system type classes.</summary>
        public ICommand SelectAllSystemTypesCommand { get; }
        /// <summary>Command to deselect all system type classes.</summary>
        public ICommand SelectNoneSystemTypesCommand { get; }
        /// <summary>Command to select all views and view template options.</summary>
        public ICommand SelectAllViewsCommand { get; }
        /// <summary>Command to deselect all views and view template options.</summary>
        public ICommand SelectNoneViewsCommand { get; }
        /// <summary>Command to select all standards categories (line styles, parameter elements, etc.).</summary>
        public ICommand SelectAllStandardsCommand { get; }
        /// <summary>Command to deselect all standards categories.</summary>
        public ICommand SelectNoneStandardsCommand { get; }
        /// <summary>Command to select all category list items.</summary>
        public ICommand SelectAllCategoriesCommand { get; }
        /// <summary>Command to deselect all category list items.</summary>
        public ICommand SelectNoneCategoriesCommand { get; }
        #endregion

        /// <summary>
        /// Initializes a new instance of the <see cref="StandardsClassSelectionViewModel"/> class.
        /// </summary>
        /// <param name="doc">The Revit Document context.</param>
        public StandardsClassSelectionViewModel(Document doc)
        {
            _doc = doc ?? throw new ArgumentNullException(nameof(doc));

            SelectAllAnnotationsCommand = new RelayCommand(_ => ToggleGroupAnnotations(true));
            SelectNoneAnnotationsCommand = new RelayCommand(_ => ToggleGroupAnnotations(false));
            SelectAllMaterialsCommand = new RelayCommand(_ => ToggleGroupMaterials(true));
            SelectNoneMaterialsCommand = new RelayCommand(_ => ToggleGroupMaterials(false));
            SelectAllSystemTypesCommand = new RelayCommand(_ => ToggleGroupSystemTypes(true));
            SelectNoneSystemTypesCommand = new RelayCommand(_ => ToggleGroupSystemTypes(false));
            SelectAllViewsCommand = new RelayCommand(_ => ToggleGroupViews(true));
            SelectNoneViewsCommand = new RelayCommand(_ => ToggleGroupViews(false));
            SelectAllStandardsCommand = new RelayCommand(_ => ToggleGroupStandards(true));
            SelectNoneStandardsCommand = new RelayCommand(_ => ToggleGroupStandards(false));

            SelectAllCategoriesCommand = new RelayCommand(_ => SelectAllCategories());
            SelectNoneCategoriesCommand = new RelayCommand(_ => SelectNoneCategories());

            PopulateLoadableFamilyCategories();

            IsSystemFamiliesEnabled = !_doc.IsFamilyDocument;
            IsViewsEnabled = !_doc.IsFamilyDocument;
        }

        /// <summary>
        /// Gets a list of currently checked category ElementIds.
        /// </summary>
        /// <returns>A list of ElementIds.</returns>
        public List<ElementId> GetCheckedCategoryIds()
        {
            return LoadableFamilyCategories
                .Where(c => c.IsChecked)
                .Select(c => c.Id)
                .ToList();
        }

        private void SelectAllCategories()
        {
            foreach (var cat in LoadableFamilyCategories)
            {
                cat.IsChecked = true;
            }
        }

        private void SelectNoneCategories()
        {
            foreach (var cat in LoadableFamilyCategories)
            {
                cat.IsChecked = false;
            }
        }

        private void PopulateLoadableFamilyCategories()
        {
            try
            {
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
                    LoadableFamilyCategories.Add(item);
                }
            }
            catch { }
        }

        private void ToggleGroupAnnotations(bool check)
        {
            ExportTextNoteTypes = check;
            ExportDimensionTypes = check;
            ExportFilledRegionTypes = check;
            ExportGridTypes = check;
            ExportLevelTypes = check;
            ExportLabelTypes = check;
        }

        private void ToggleGroupMaterials(bool check)
        {
            ExportMaterials = check;
            ExportFillPatterns = check;
            ExportLinePatterns = check;
            ExportAppearanceAssets = check;
        }

        private void ToggleGroupSystemTypes(bool check)
        {
            ExportWallTypes = check;
            ExportFloorTypes = check;
            ExportRoofTypes = check;
            ExportCeilingTypes = check;
            ExportRailingTypes = check;
            ExportStairsTypes = check;
            ExportCurtainSystemTypes = check;
            ExportMullionTypes = check;
            ExportFasciaTypes = check;
            ExportGutterTypes = check;
            ExportToposolidTypes = check;
        }

        private void ToggleGroupViews(bool check)
        {
            ExportStandardViews = check;
            ExportViewTemplates = check;
            ExportViewFamilyTypes = check;
            ExportBrowserOrganizations = check;
        }

        private void ToggleGroupStandards(bool check)
        {
            ExportLineStyles = check;
            ExportModelCategories = check;
            ExportAnnotationCategories = check;
            ExportAnalyticalCategories = check;
            ExportImportCategories = check;
            ExportParameterElements = check;
            ExportParameterFilters = check;
            ExportTitleBlockTypes = check;
        }
    }
}
