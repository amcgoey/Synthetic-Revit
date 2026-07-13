using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Windows.Input;

using Synthetic.Modules.RevitDOM;
using Synthetic.Infrastructure.Serialization;
using RevitDB = Autodesk.Revit.DB;

using Synthetic.Shared.UI;
using Synthetic.Modules.MergeDuplicates.Handlers;
using Synthetic.Shared.RevitAPI;
namespace Synthetic.Modules.StandardsManagement.ViewModels
{
    /// <summary>
    /// ViewModel for the nested data editor window, which handles editing compound layers, visibility overrides, or object properties.
    /// </summary>
    public class NestedDataEditorViewModel : ViewModelBase
    {
        private readonly object _nestedData;
        private readonly ObservableCollection<ElementTypeWrapperVM> _wrappedElements;
        private readonly Dictionary<ParameterWrapperVM, PropertyInfo> _pocoPropertiesMapping = new Dictionary<ParameterWrapperVM, PropertyInfo>();

        /// <summary>
        /// Gets a value indicating whether the editor is in compound layers mode.
        /// </summary>
        public bool IsLayersMode { get; }

        /// <summary>
        /// Gets a value indicating whether the editor is in visibility overrides mode.
        /// </summary>
        public bool IsOverridesMode { get; }

        /// <summary>
        /// Gets a value indicating whether the editor is in property grid mode.
        /// </summary>
        public bool IsPropertyGridMode { get; }

        /// <summary>
        /// Gets the collection of compound structure layer rows.
        /// </summary>
        public ObservableCollection<CompoundLayerRowVM> Layers { get; } = new ObservableCollection<CompoundLayerRowVM>();

        /// <summary>
        /// Gets the collection of visibility override rows.
        /// </summary>
        public ObservableCollection<VisibilityOverrideRowVM> Overrides { get; } = new ObservableCollection<VisibilityOverrideRowVM>();

        /// <summary>
        /// Gets the collection of properties shown in the property grid mode.
        /// </summary>
        public ObservableCollection<ParameterWrapperVM> PropertyGridProperties { get; private set; } = new ObservableCollection<ParameterWrapperVM>();

        /// <summary>
        /// Gets the list of available materials.
        /// </summary>
        public List<string> AvailableMaterials { get; } = new List<string> { "<By Category>" };

        /// <summary>
        /// Gets the list of available patterns.
        /// </summary>
        public List<string> AvailablePatterns { get; } = new List<string> { "<None>" };

        /// <summary>
        /// Gets the list of available line styles.
        /// </summary>
        public List<string> AvailableLineStyles { get; } = new List<string> { "<None>" };

        /// <summary>
        /// Gets the Save command.
        /// </summary>
        public ICommand SaveCommand { get; }

        /// <summary>
        /// Gets the Cancel command.
        /// </summary>
        public ICommand CancelCommand { get; }

        /// <summary>
        /// Gets or sets the close action for the window.
        /// </summary>
        public Action? CloseAction { get; set; }

        /// <summary>
        /// Gets a value indicating the dialog result status when the window is closed.
        /// </summary>
        public bool DialogResult { get; private set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="NestedDataEditorViewModel"/> class.
        /// </summary>
        /// <param name="nestedData">The nested data object to edit.</param>
        /// <param name="wrappedElements">A collection of wrapped elements for reference lookup.</param>
        public NestedDataEditorViewModel(object nestedData, ObservableCollection<ElementTypeWrapperVM> wrappedElements)
        {
            _nestedData = nestedData ?? throw new ArgumentNullException(nameof(nestedData));
            _wrappedElements = wrappedElements ?? new ObservableCollection<ElementTypeWrapperVM>();

            SaveCommand = new RelayCommand(_ => ExecuteSave());
            CancelCommand = new RelayCommand(_ => ExecuteCancel());

            // Populate references lookup tables
            PopulateAvailableReferences();

            // Determine active mode
            if (nestedData is CompoundStructureModel csModel)
            {
                IsLayersMode = true;
                if (csModel.Layers != null)
                {
                    foreach (var layer in csModel.Layers)
                    {
                        Layers.Add(new CompoundLayerRowVM(layer, AvailableMaterials));
                    }
                }
            }
            else if (nestedData is List<CategoryGraphicOverridesModel> overridesList)
            {
                IsOverridesMode = true;
                PopulateOverrides(overridesList);

                var cv = System.Windows.Data.CollectionViewSource.GetDefaultView(Overrides);
                if (cv != null)
                {
                    cv.GroupDescriptions.Add(new System.Windows.Data.PropertyGroupDescription("GroupName"));
                    cv.SortDescriptions.Add(new System.ComponentModel.SortDescription("GroupName", System.ComponentModel.ListSortDirection.Ascending));
                    cv.SortDescriptions.Add(new System.ComponentModel.SortDescription("IsSubcategory", System.ComponentModel.ListSortDirection.Ascending));
                    cv.SortDescriptions.Add(new System.ComponentModel.SortDescription("CategoryName", System.ComponentModel.ListSortDirection.Ascending));
                }
            }
            else if (nestedData is IEnumerable list && !(nestedData is string))
            {
                // Fallback for other lists (e.g. if they are lists of POCOs)
                IsPropertyGridMode = true;
                PopulatePropertyGridFromPoco(nestedData);
            }
            else
            {
                // Single nested complex object
                IsPropertyGridMode = true;
                PopulatePropertyGridFromPoco(nestedData);
            }
        }

        private void PopulateAvailableReferences()
        {
            var uiapp = ProjectStandardsDashboardViewModel.Instance?.UIApplication;
            var doc = uiapp?.ActiveUIDocument?.Document;

            if (doc != null)
            {
                var materials = Select.AllMaterials(doc);
                foreach (var mat in materials)
                {
                    if (!string.IsNullOrEmpty(mat.Name) && !AvailableMaterials.Contains(mat.Name))
                        AvailableMaterials.Add(mat.Name);
                }

                var fillPatterns = new RevitDB.FilteredElementCollector(doc)
                    .OfClass(typeof(RevitDB.FillPatternElement))
                    .Cast<RevitDB.FillPatternElement>();
                foreach (var fp in fillPatterns)
                {
                    if (!string.IsNullOrEmpty(fp.Name) && !AvailablePatterns.Contains(fp.Name))
                        AvailablePatterns.Add(fp.Name);
                }

                var linePatterns = new RevitDB.FilteredElementCollector(doc)
                    .OfClass(typeof(RevitDB.LinePatternElement))
                    .Cast<RevitDB.LinePatternElement>();
                foreach (var lp in linePatterns)
                {
                    if (!string.IsNullOrEmpty(lp.Name) && !AvailableLineStyles.Contains(lp.Name))
                        AvailableLineStyles.Add(lp.Name);
                }
            }

            foreach (var el in _wrappedElements)
            {
                if (string.Equals(el.Class, "Autodesk.Revit.DB.Material", StringComparison.OrdinalIgnoreCase))
                {
                    if (!string.IsNullOrEmpty(el.Name) && !AvailableMaterials.Contains(el.Name))
                        AvailableMaterials.Add(el.Name);
                }
                else if (string.Equals(el.Class, "Autodesk.Revit.DB.FillPatternElement", StringComparison.OrdinalIgnoreCase))
                {
                    if (!string.IsNullOrEmpty(el.Name) && !AvailablePatterns.Contains(el.Name))
                        AvailablePatterns.Add(el.Name);
                }
                else if (string.Equals(el.Class, "Autodesk.Revit.DB.LinePatternElement", StringComparison.OrdinalIgnoreCase))
                {
                    if (!string.IsNullOrEmpty(el.Name) && !AvailableLineStyles.Contains(el.Name))
                        AvailableLineStyles.Add(el.Name);
                }
            }

            // Distinct and sort (putting "<None>" or "<By Category>" at the top)
            SortReferenceList(AvailableMaterials);
            SortReferenceList(AvailablePatterns);
            SortReferenceList(AvailableLineStyles);
        }

        private void PopulateOverrides(List<CategoryGraphicOverridesModel> existingOverrides)
        {
            Overrides.Clear();

            // Try to get Revit document if online
            var uiapp = ProjectStandardsDashboardViewModel.Instance?.UIApplication;
            var doc = uiapp?.ActiveUIDocument?.Document;

            if (doc != null)
            {
                // Online mode: Harvest all UI-visible categories and subcategories from the active document
                var categories = doc.Settings.Categories;
                var existingLookup = new Dictionary<string, CategoryGraphicOverridesModel>(StringComparer.OrdinalIgnoreCase);
                if (existingOverrides != null)
                {
                    foreach (var ov in existingOverrides)
                    {
                        if (ov.Category?.Name != null)
                        {
                            existingLookup[ov.Category.Name] = ov;
                        }
                    }
                }

                foreach (RevitDB.Category category in categories)
                {
                    if (!category.IsVisibleInUI) continue;

                    // Parent category
                    CategoryGraphicOverridesModel parentOverride;
                    if (existingLookup.TryGetValue(category.Name, out var foundParent))
                    {
                        parentOverride = foundParent;
                    }
                    else
                    {
                        parentOverride = CreateDefaultOverride(category, doc);
                    }
                    Overrides.Add(new VisibilityOverrideRowVM(parentOverride, AvailablePatterns, AvailableLineStyles));

                    // Subcategories
                    foreach (RevitDB.Category subCategory in category.SubCategories)
                    {
                        CategoryGraphicOverridesModel subOverride;
                        if (existingLookup.TryGetValue(subCategory.Name, out var foundSub))
                        {
                            subOverride = foundSub;
                        }
                        else
                        {
                            subOverride = CreateDefaultOverride(subCategory, doc);
                            subOverride.ParentCategory = category.ToCategoryIdModel(doc, false);
                        }
                        Overrides.Add(new VisibilityOverrideRowVM(subOverride, AvailablePatterns, AvailableLineStyles));
                    }
                }
            }
            else
            {
                // Offline mode fallback: Only show overrides from loaded JSON
                if (existingOverrides != null)
                {
                    foreach (var overrideModel in existingOverrides)
                    {
                        Overrides.Add(new VisibilityOverrideRowVM(overrideModel, AvailablePatterns, AvailableLineStyles));
                    }
                }
            }
        }

        private CategoryGraphicOverridesModel CreateDefaultOverride(RevitDB.Category category, RevitDB.Document doc)
        {
            var catOverride = new CategoryGraphicOverridesModel
            {
                Category = category.ToCategoryIdModel(doc, false),
                IsHidden = false,
                GraphicOverride = new OverrideGraphicSettingsModel
                {
                    IsModified = false,
                    SurfaceBackgroundPatternId = new ElementIdModel { Class = "Autodesk.Revit.DB.FillPatternElement", Category = "" },
                    SurfaceForegroundPatternId = new ElementIdModel { Class = "Autodesk.Revit.DB.FillPatternElement", Category = "" },
                    ProjectionLinePatternId = new ElementIdModel { Class = "Autodesk.Revit.DB.LinePatternElement", Category = "" },
                    CutBackgroundPatternId = new ElementIdModel { Class = "Autodesk.Revit.DB.FillPatternElement", Category = "" },
                    CutForegroundPatternId = new ElementIdModel { Class = "Autodesk.Revit.DB.FillPatternElement", Category = "" },
                    CutLinePatternId = new ElementIdModel { Class = "Autodesk.Revit.DB.LinePatternElement", Category = "" }
                }
            };
            if (category.Parent != null)
            {
                catOverride.ParentCategory = category.Parent.ToCategoryIdModel(doc, false);
            }
            return catOverride;
        }

        private void SortReferenceList(List<string> list)
        {
            var sorted = list.Distinct().OrderBy(s => (s == "<None>" || s == "<By Category>") ? "" : s).ToList();
            list.Clear();
            list.AddRange(sorted);
        }

        private void PopulatePropertyGridFromPoco(object poco)
        {
            PropertyGridProperties = new ObservableCollection<ParameterWrapperVM>();
            if (poco == null) return;

            foreach (var prop in poco.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (prop.GetIndexParameters().Length > 0 || !prop.CanRead) continue;

                string name = prop.Name;
                string valueStr = "";
                object? val = prop.GetValue(poco);
                ElementIdModel? valElemId = null;
                string storageType = "String";

                if (val != null)
                {
                    if (val is ElementIdModel eId)
                    {
                        valElemId = eId;
                        valueStr = eId.Name ?? "";
                        storageType = "ElementId";
                    }
                    else
                    {
                        valueStr = val.ToString() ?? "";
                        if (val is int || val is long) storageType = "Integer";
                        else if (val is double || val is float) storageType = "Double";
                        else if (val is bool) storageType = "Boolean";
                    }
                }

                bool isReadOnly = !prop.CanWrite;
                var paramModel = new ParameterModel(name, valueStr, valElemId, storageType, 0, null, false, isReadOnly);
                var wrapper = new ParameterWrapperVM(paramModel);
                PropertyGridProperties.Add(wrapper);
                _pocoPropertiesMapping[wrapper] = prop;
            }
        }

        private void ExecuteSave()
        {
            // Sync properties back to models
            if (IsLayersMode && _nestedData is CompoundStructureModel csModel)
            {
                csModel.Layers.Clear();
                foreach (var row in Layers)
                {
                    csModel.Layers.Add(row.GetUpdatedLayer());
                }
            }
            else if (IsOverridesMode && _nestedData is List<CategoryGraphicOverridesModel> overridesList)
            {
                overridesList.Clear();
                foreach (var row in Overrides)
                {
                    row.SyncBackToOverride();
                    if (row.IsModelModified())
                    {
                        overridesList.Add(row.GetOverrideModel());
                    }
                }
            }
            else if (IsPropertyGridMode)
            {
                SavePropertyGridChanges(_nestedData);
            }

            DialogResult = true;
            CloseAction?.Invoke();
        }

        private void SavePropertyGridChanges(object poco)
        {
            if (poco == null) return;
            foreach (var wrapper in PropertyGridProperties)
            {
                if (wrapper.IsReadOnly) continue;
                if (_pocoPropertiesMapping.TryGetValue(wrapper, out var prop))
                {
                    try
                    {
                        if (prop.PropertyType == typeof(ElementIdModel))
                        {
                            prop.SetValue(poco, wrapper.ValueElemId);
                        }
                        else if (prop.PropertyType == typeof(string))
                        {
                            prop.SetValue(poco, wrapper.Value);
                        }
                        else if (prop.PropertyType == typeof(bool))
                        {
                            prop.SetValue(poco, wrapper.Value == "True" || wrapper.Value == "true" || wrapper.Value == "1");
                        }
                        else if (prop.PropertyType == typeof(int))
                        {
                            prop.SetValue(poco, int.Parse(wrapper.Value));
                        }
                        else if (prop.PropertyType == typeof(double))
                        {
                            prop.SetValue(poco, double.Parse(wrapper.Value));
                        }
                    }
                    catch {}
                }
            }
        }

        private void ExecuteCancel()
        {
            DialogResult = false;
            CloseAction?.Invoke();
        }
    }

    /// <summary>
    /// Wrapper for compound structure layers.
    /// </summary>
    public class CompoundLayerRowVM : ViewModelBase
    {
        private readonly SerialCompoundStructureLayer _layer;

        /// <summary>
        /// Gets or sets the function of the layer.
        /// </summary>
        public string Function { get; set; }

        /// <summary>
        /// Gets or sets the width of the layer.
        /// </summary>
        public double Width { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the layer cap flag is set.
        /// </summary>
        public bool LayerCapFlag { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the structural material flag is set.
        /// </summary>
        public bool StructuralMaterial { get; set; }

        private int _priority;
        /// <summary>
        /// Gets or sets the priority of the layer.
        /// </summary>
        public int Priority
        {
            get => _priority;
            set => SetProperty(ref _priority, value);
        }

        private string _selectedMaterial = string.Empty;
        /// <summary>
        /// Gets or sets the name of the selected material for this layer.
        /// </summary>
        public string SelectedMaterial
        {
            get => _selectedMaterial;
            set => SetProperty(ref _selectedMaterial, value);
        }

        /// <summary>
        /// Gets the list of available materials.
        /// </summary>
        public List<string> MaterialsList { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="CompoundLayerRowVM"/> class.
        /// </summary>
        /// <param name="layer">The serial compound structure layer model.</param>
        /// <param name="materialsList">The list of available materials.</param>
        public CompoundLayerRowVM(SerialCompoundStructureLayer layer, List<string> materialsList)
        {
            _layer = layer ?? throw new ArgumentNullException(nameof(layer));
            MaterialsList = materialsList;

            Function = layer.Function;
            Width = layer.Width;
            LayerCapFlag = layer.LayerCapFlag;
            StructuralMaterial = layer.StructuralMaterial;
#if REVIT2026
            Priority = layer.Priority;
#endif

            SelectedMaterial = (layer.MaterialId == null || string.IsNullOrEmpty(layer.MaterialId.Name))
                ? "<By Category>"
                : layer.MaterialId.Name;
        }

        /// <summary>
        /// Gets the updated compound structure layer model with modified properties synced back.
        /// </summary>
        /// <returns>The updated layer model.</returns>
        public SerialCompoundStructureLayer GetUpdatedLayer()
        {
            _layer.Function = Function;
            _layer.Width = Width;
            _layer.LayerCapFlag = LayerCapFlag;
            _layer.StructuralMaterial = StructuralMaterial;
#if REVIT2026
            _layer.Priority = Priority;
#endif

            if (_layer.MaterialId == null)
            {
                _layer.MaterialId = new ElementIdModel { Class = "Autodesk.Revit.DB.Material", Category = "Materials" };
            }

            if (SelectedMaterial == "<By Category>" || SelectedMaterial == "<None>" || string.IsNullOrEmpty(SelectedMaterial))
            {
                _layer.MaterialId.Name = "";
                _layer.MaterialId.Id = 0;
                _layer.MaterialId.UniqueId = null;
            }
            else
            {
                _layer.MaterialId.Name = SelectedMaterial;
                _layer.MaterialId.Id = 0;
                _layer.MaterialId.UniqueId = null;
            }

            return _layer;
        }
    }

    /// <summary>
    /// Wrapper for category graphic overrides.
    /// </summary>
    public class VisibilityOverrideRowVM : ViewModelBase
    {
        private readonly CategoryGraphicOverridesModel _override;

        /// <summary>
        /// Gets the name of the category.
        /// </summary>
        public string CategoryName => _override.Category?.Name ?? "Unknown";

        /// <summary>
        /// Gets the name of the parent category.
        /// </summary>
        public string? ParentCategoryName => _override.ParentCategory?.Name;

        /// <summary>
        /// Gets the group name, which is either the parent category name or the category name if it has no parent.
        /// </summary>
        public string GroupName => string.IsNullOrEmpty(ParentCategoryName) ? CategoryName : ParentCategoryName!;

        /// <summary>
        /// Gets a value indicating whether this category is a subcategory.
        /// </summary>
        public bool IsSubcategory => !string.IsNullOrEmpty(ParentCategoryName);

        private bool _isVisible;
        /// <summary>
        /// Gets or sets a value indicating whether the category is visible.
        /// </summary>
        public bool IsVisible
        {
            get => _isVisible;
            set => SetProperty(ref _isVisible, value);
        }

        private bool _halftone;
        /// <summary>
        /// Gets or sets a value indicating whether the category is drawn in halftone.
        /// </summary>
        public bool Halftone
        {
            get => _halftone;
            set => SetProperty(ref _halftone, value);
        }

        private int _transparency;
        /// <summary>
        /// Gets or sets the transparency of the category.
        /// </summary>
        public int Transparency
        {
            get => _transparency;
            set => SetProperty(ref _transparency, value);
        }

        private int _projectionLineWeight;
        /// <summary>
        /// Gets or sets the projection line weight.
        /// </summary>
        public int ProjectionLineWeight
        {
            get => _projectionLineWeight;
            set => SetProperty(ref _projectionLineWeight, value);
        }

        private string _projectionLineColor = string.Empty;
        /// <summary>
        /// Gets or sets the projection line color.
        /// </summary>
        public string ProjectionLineColor
        {
            get => _projectionLineColor;
            set => SetProperty(ref _projectionLineColor, value);
        }

        private string _selectedProjectionLinePattern = string.Empty;
        /// <summary>
        /// Gets or sets the selected projection line pattern.
        /// </summary>
        public string SelectedProjectionLinePattern
        {
            get => _selectedProjectionLinePattern;
            set => SetProperty(ref _selectedProjectionLinePattern, value);
        }

        private int _cutLineWeight;
        /// <summary>
        /// Gets or sets the cut line weight.
        /// </summary>
        public int CutLineWeight
        {
            get => _cutLineWeight;
            set => SetProperty(ref _cutLineWeight, value);
        }

        private string _cutLineColor = string.Empty;
        /// <summary>
        /// Gets or sets the cut line color.
        /// </summary>
        public string CutLineColor
        {
            get => _cutLineColor;
            set => SetProperty(ref _cutLineColor, value);
        }

        private string _selectedCutLinePattern = string.Empty;
        /// <summary>
        /// Gets or sets the selected cut line pattern.
        /// </summary>
        public string SelectedCutLinePattern
        {
            get => _selectedCutLinePattern;
            set => SetProperty(ref _selectedCutLinePattern, value);
        }

        private string _detailLevel = string.Empty;
        /// <summary>
        /// Gets or sets the detail level.
        /// </summary>
        public string DetailLevel
        {
            get => _detailLevel;
            set => SetProperty(ref _detailLevel, value);
        }

        /// <summary>
        /// Gets the list of available patterns.
        /// </summary>
        public List<string> PatternsList { get; }

        /// <summary>
        /// Gets the list of available line styles.
        /// </summary>
        public List<string> LineStylesList { get; }

        /// <summary>
        /// Gets the list of detail levels.
        /// </summary>
        public List<string> DetailLevels { get; } = new List<string> { "Undefined", "Coarse", "Medium", "Fine" };

        /// <summary>
        /// Initializes a new instance of the <see cref="VisibilityOverrideRowVM"/> class.
        /// </summary>
        /// <param name="overrideModel">The override settings model.</param>
        /// <param name="patternsList">The list of available fill patterns.</param>
        /// <param name="lineStylesList">The list of available line styles.</param>
        public VisibilityOverrideRowVM(CategoryGraphicOverridesModel overrideModel, List<string> patternsList, List<string> lineStylesList)
        {
            _override = overrideModel ?? throw new ArgumentNullException(nameof(overrideModel));
            PatternsList = patternsList;
            LineStylesList = lineStylesList;

            IsVisible = !overrideModel.IsHidden;

            // Ensure GraphicOverride is initialized
            if (overrideModel.GraphicOverride == null)
            {
                overrideModel.GraphicOverride = new OverrideGraphicSettingsModel
                {
                    IsModified = true,
                    SurfaceBackgroundPatternId = new ElementIdModel { Class = "Autodesk.Revit.DB.FillPatternElement", Category = "" },
                    SurfaceForegroundPatternId = new ElementIdModel { Class = "Autodesk.Revit.DB.FillPatternElement", Category = "" },
                    ProjectionLinePatternId = new ElementIdModel { Class = "Autodesk.Revit.DB.LinePatternElement", Category = "" },
                    CutBackgroundPatternId = new ElementIdModel { Class = "Autodesk.Revit.DB.FillPatternElement", Category = "" },
                    CutForegroundPatternId = new ElementIdModel { Class = "Autodesk.Revit.DB.FillPatternElement", Category = "" },
                    CutLinePatternId = new ElementIdModel { Class = "Autodesk.Revit.DB.LinePatternElement", Category = "" }
                };
            }

            var go = overrideModel.GraphicOverride;
            SelectedProjectionLinePattern = (go.ProjectionLinePatternId == null || string.IsNullOrEmpty(go.ProjectionLinePatternId.Name))
                ? "<None>"
                : go.ProjectionLinePatternId.Name;

            SelectedCutLinePattern = (go.CutLinePatternId == null || string.IsNullOrEmpty(go.CutLinePatternId.Name))
                ? "<None>"
                : go.CutLinePatternId.Name;

            Halftone = go.Halftone;
            Transparency = go.Transparency;
            ProjectionLineWeight = go.ProjectionLineWeight == -1 ? 0 : go.ProjectionLineWeight;
            ProjectionLineColor = go.ProjectionLineColor != null && go.ProjectionLineColor.IsValid
                ? $"#{go.ProjectionLineColor.Red:X2}{go.ProjectionLineColor.Green:X2}{go.ProjectionLineColor.Blue:X2}"
                : "#FFFFFF";

            CutLineWeight = go.CutLineWeight == -1 ? 0 : go.CutLineWeight;
            CutLineColor = go.CutLineColor != null && go.CutLineColor.IsValid
                ? $"#{go.CutLineColor.Red:X2}{go.CutLineColor.Green:X2}{go.CutLineColor.Blue:X2}"
                : "#FFFFFF";

            DetailLevel = go.DetailLevel != null ? go.DetailLevel.Value : "Undefined";
        }

        /// <summary>
        /// Checks if the model has been modified.
        /// </summary>
        /// <returns>True if modified, false otherwise.</returns>
        public bool IsModelModified()
        {
            if (!IsVisible) return true;
            if (Halftone) return true;
            if (Transparency != 0) return true;
            if (ProjectionLineWeight != 0) return true;
            if (!string.IsNullOrEmpty(ProjectionLineColor) && !string.Equals(ProjectionLineColor, "#FFFFFF", StringComparison.OrdinalIgnoreCase)) return true;
            if (!string.IsNullOrEmpty(SelectedProjectionLinePattern) && SelectedProjectionLinePattern != "<None>") return true;
            if (CutLineWeight != 0) return true;
            if (!string.IsNullOrEmpty(CutLineColor) && !string.Equals(CutLineColor, "#FFFFFF", StringComparison.OrdinalIgnoreCase)) return true;
            if (!string.IsNullOrEmpty(SelectedCutLinePattern) && SelectedCutLinePattern != "<None>") return true;
            if (!string.IsNullOrEmpty(DetailLevel) && DetailLevel != "Undefined") return true;
            return false;
        }

        /// <summary>
        /// Gets the override model.
        /// </summary>
        /// <returns>The category graphic overrides model.</returns>
        public CategoryGraphicOverridesModel GetOverrideModel()
        {
            return _override;
        }

        /// <summary>
        /// Synchronizes settings from the VM back to the override model.
        /// </summary>
        public void SyncBackToOverride()
        {
            _override.IsHidden = !IsVisible;

            var go = _override.GraphicOverride;
            if (go != null)
            {
                go.IsModified = IsModelModified();
                go.Halftone = Halftone;
                go.Transparency = Transparency;
                go.ProjectionLineWeight = ProjectionLineWeight == 0 ? -1 : ProjectionLineWeight;
                go.CutLineWeight = CutLineWeight == 0 ? -1 : CutLineWeight;

                // Projection Line Pattern
                if (go.ProjectionLinePatternId == null)
                {
                    go.ProjectionLinePatternId = new ElementIdModel { Class = "Autodesk.Revit.DB.LinePatternElement" };
                }
                if (SelectedProjectionLinePattern == "<None>" || string.IsNullOrEmpty(SelectedProjectionLinePattern))
                {
                    go.ProjectionLinePatternId.Name = "";
                    go.ProjectionLinePatternId.Id = 0;
                    go.ProjectionLinePatternId.UniqueId = null;
                }
                else
                {
                    go.ProjectionLinePatternId.Name = SelectedProjectionLinePattern;
                    go.ProjectionLinePatternId.Id = 0;
                    go.ProjectionLinePatternId.UniqueId = null;
                }

                // Cut Line Pattern
                if (go.CutLinePatternId == null)
                {
                    go.CutLinePatternId = new ElementIdModel { Class = "Autodesk.Revit.DB.LinePatternElement" };
                }
                if (SelectedCutLinePattern == "<None>" || string.IsNullOrEmpty(SelectedCutLinePattern))
                {
                    go.CutLinePatternId.Name = "";
                    go.CutLinePatternId.Id = 0;
                    go.CutLinePatternId.UniqueId = null;
                }
                else
                {
                    go.CutLinePatternId.Name = SelectedCutLinePattern;
                    go.CutLinePatternId.Id = 0;
                    go.CutLinePatternId.UniqueId = null;
                }

                // Projection Line Color
                if (!string.IsNullOrEmpty(ProjectionLineColor) && ProjectionLineColor.StartsWith("#"))
                {
                    try
                    {
                        var hex = ProjectionLineColor.Substring(1);
                        byte r = Convert.ToByte(hex.Substring(0, 2), 16);
                        byte g = Convert.ToByte(hex.Substring(2, 2), 16);
                        byte b = Convert.ToByte(hex.Substring(4, 2), 16);
                        go.ProjectionLineColor = new ColorModel { Red = r, Green = g, Blue = b, IsValid = true };
                    }
                    catch { go.ProjectionLineColor = new ColorModel { IsValid = false }; }
                }
                else
                {
                    go.ProjectionLineColor = new ColorModel { IsValid = false };
                }

                // Cut Line Color
                if (!string.IsNullOrEmpty(CutLineColor) && CutLineColor.StartsWith("#"))
                {
                    try
                    {
                        var hex = CutLineColor.Substring(1);
                        byte r = Convert.ToByte(hex.Substring(0, 2), 16);
                        byte g = Convert.ToByte(hex.Substring(2, 2), 16);
                        byte b = Convert.ToByte(hex.Substring(4, 2), 16);
                        go.CutLineColor = new ColorModel { Red = r, Green = g, Blue = b, IsValid = true };
                    }
                    catch { go.CutLineColor = new ColorModel { IsValid = false }; }
                }
                else
                {
                    go.CutLineColor = new ColorModel { IsValid = false };
                }

                // Detail Level
                if (go.DetailLevel == null)
                {
                    go.DetailLevel = new EnumModel();
                }
                go.DetailLevel.Value = DetailLevel;
                go.DetailLevel.Type = "Autodesk.Revit.DB.ViewDetailLevel";
            }
        }
    }
}
