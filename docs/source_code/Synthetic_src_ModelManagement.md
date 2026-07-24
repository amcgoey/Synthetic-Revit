### File: StandardsManagement/Commands/CmdProjectStandards.cs
```csharp
using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

using Synthetic.Shared.UI;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.Modules.StandardsManagement.Views;
using Synthetic.RevitDOM.Operations.Standards;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;
using Synthetic.RevitDOM.Operations.Standards;
using Synthetic.RevitDOM.Operations.Diffing;

namespace Synthetic.Modules.StandardsManagement.Commands
{
    /// <summary>
    /// Command to display the Modeless Project Standards Dashboard window.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class CmdProjectStandards : IExternalCommand
    {
        private static ProjectStandardsDashboardWindow? _windowInstance;

        /// <summary>
        /// Executes the command.
        /// </summary>
        public Result Execute(
          ExternalCommandData commandData,
          ref string message,
          ElementSet elements)
        {
            UIApplication uiapp = commandData.Application;

            try
            {
                // If the window is already open, bring it to front
                if (_windowInstance != null && _windowInstance.IsLoaded)
                {
                    _windowInstance.Focus();
                    return Result.Succeeded;
                }

                // Initialize ViewModel with uiapp context and injected services
                var fileDialog = new WindowsFileDialogService();
                var guardrail = new WindowsGuardrailPromptService();
                var exportService = new StandardsExportService(guardrail, fileDialog);
                var userPromptService = new WindowsUserPromptService();
                var findReplaceService = new FindReplaceService();
                var serializationEngine = new StandardSerializationEngine();
                var orchestrator = new StandardsExtractionOrchestrator(new RevitIdentityService(), serializationEngine);
                var pocoIdentityService = new PocoIdentityService();
                var diffEngine = new PocoToRevitDiffEngine(new RevitIdentityService());
                var pipeline = new StandardsExecutionPipeline(serializationEngine, exportService, new RevitFamilyEnforcer(serializationEngine));

                var vm = new ProjectStandardsDashboardViewModel(
                    uiapp,
                    fileDialog,
                    exportService,
                    null, // settings
                    userPromptService,
                    findReplaceService,
                    orchestrator,
                    pocoIdentityService,
                    diffEngine,
                    serializationEngine,
                    pipeline);

                // Create external event for modeless execution
                var handler = new ProjectStandardsExternalEventHandler();
                var externalEvent = ExternalEvent.Create(handler);
                vm.SetExternalEvent(externalEvent, handler);

                // Create the modeless window instance
                _windowInstance = new ProjectStandardsDashboardWindow(uiapp.MainWindowHandle) { DataContext = vm };
                
                // Clear instance reference on close to allow reopening later
                _windowInstance.Closed += (s, e) => { _windowInstance = null; };

                _windowInstance.Show(); // Modeless execution!
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}

```

### File: StandardsManagement/ViewModels/CategorySelectionViewModel.cs
```csharp
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Autodesk.Revit.DB;

using Synthetic.Shared.UI;

using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;
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
```

### File: StandardsManagement/ViewModels/ElementTypeWrapperVM.cs
```csharp
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;

using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;
using Synthetic.Infrastructure.Serialization;

using Synthetic.Shared.UI;
using Synthetic.Modules.MergeDuplicates.Handlers;
using Synthetic.Shared.RevitAPI;
namespace Synthetic.Modules.StandardsManagement.ViewModels
{
    /// <summary>
    /// ViewModel wrapper for the ElementTypeModel POCO.
    /// Provides change notification, data binding, and editable/read-only field policies.
    /// </summary>
    public class ElementTypeWrapperVM : ViewModelBase, INotifyDataErrorInfo
    {
        private readonly ElementModel _model;
        private readonly ElementModel? _baselineModel;
        private readonly Dictionary<string, List<string>> _errors = new Dictionary<string, List<string>>();
        private string _originalName = string.Empty;
        private List<ParameterWrapperVM>? _harvestedProperties;

        /// <summary>
        /// Event raised when validation errors change for a property.
        /// </summary>
        public event EventHandler<DataErrorsChangedEventArgs>? ErrorsChanged;

        /// <summary>
        /// Gets or sets the group name for categorization in the UI.
        /// </summary>
        public string Group { get; set; } = string.Empty;

        private bool _isSelected;

        /// <summary>
        /// Gets or sets whether this element is selected in the TreeView.
        /// </summary>
        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }

        /// <summary>
        /// Gets a value indicating whether this element or any of its parameters has unsaved changes.
        /// </summary>
        public bool IsDirty
        {
            get
            {
                string original = _baselineModel?.Name ?? _originalName;
                bool harvestedDirty = _harvestedProperties != null && _harvestedProperties.Any(p => p.IsDirty);
                return Name != original || Parameters.Any(p => p.IsDirty) || harvestedDirty;
            }
        }

        /// <summary>
        /// Gets the collection of parameter ViewModels.
        /// </summary>
        public ObservableCollection<ParameterWrapperVM> Parameters { get; }

        /// <summary>
        /// Callback function to check if this name is a duplicate.
        /// </summary>
        public Func<ElementTypeWrapperVM, string, bool>? IsNameDuplicateCallback { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="ElementTypeWrapperVM"/> class.
        /// </summary>
        /// <param name="model">The underlying ElementModel POCO.</param>
        /// <param name="baselineModel">The matching baseline ElementModel for on-the-fly dirty tracking.</param>
        public ElementTypeWrapperVM(ElementModel model, ElementModel? baselineModel = null)
        {
            _model = model ?? throw new ArgumentNullException(nameof(model));
            _baselineModel = baselineModel;

            // Populate the Parameters collection by iterating over the POCO's parameters
            Parameters = new ObservableCollection<ParameterWrapperVM>();
            if (_model.Parameters != null)
            {
                foreach (var param in _model.Parameters)
                {
                    ParameterModel? baselineParam = null;
                    if (_baselineModel?.Parameters != null)
                    {
                        baselineParam = _baselineModel.Parameters.FirstOrDefault(p => p.Name == param.Name);
                    }
                    Parameters.Add(new ParameterWrapperVM(param, _model, baselineParam));
                }
            }

            // Add pseudo-parameters for complex structures
            if (_model is HostObjTypeModel)
            {
                Parameters.Add(new ParameterWrapperVM(new ParameterModel(
                    "CompoundStructure",
                    "Complex structure data",
                    null,
                    "ComplexNested",
                    0,
                    null,
                    false,
                    false
                )));
            }
            else if (_model is ViewModel)
            {
                Parameters.Add(new ParameterWrapperVM(new ParameterModel(
                    "VisibilityGraphics",
                    "Complex graphics override data",
                    null,
                    "ComplexNested",
                    0,
                    null,
                    false,
                    false
                )));
            }

            // Expose native readable properties for special classes:
            if (string.Equals(_model.Class, "Autodesk.Revit.DB.FillPatternElement", StringComparison.OrdinalIgnoreCase))
            {
                if (_model is FillPatternElementModel fpModel)
                {
                    Parameters.Add(new ParameterWrapperVM(new ParameterModel(
                        "Target",
                        fpModel.Pattern?.Target.ToString() ?? "",
                        null, "String", 0, "", false, true
                    )));
                    Parameters.Add(new ParameterWrapperVM(new ParameterModel(
                        "HostOrientation",
                        fpModel.Pattern?.HostOrientation.ToString() ?? "",
                        null, "String", 0, "", false, true
                    )));
                    Parameters.Add(new ParameterWrapperVM(new ParameterModel(
                        "GridCount",
                        fpModel.Pattern?.FillGrids?.Count.ToString() ?? "0",
                        null, "Integer", 0, "", false, true
                    )));
                }
            }
            else if (string.Equals(_model.Class, "Autodesk.Revit.DB.LinePatternElement", StringComparison.OrdinalIgnoreCase))
            {
                if (_model is LinePatternElementModel lpModel)
                {
                    Parameters.Add(new ParameterWrapperVM(new ParameterModel(
                        "SegmentCount",
                        lpModel.Segments?.Count.ToString() ?? "0",
                        null, "Integer", 0, "", false, true
                    )));
                    if (lpModel.Segments != null)
                    {
                        string desc = string.Join(", ", lpModel.Segments.Select(s => $"{s.Type}: {s.Length}"));
                        Parameters.Add(new ParameterWrapperVM(new ParameterModel(
                            "Segments",
                            desc,
                            null, "String", 0, "", false, true
                        )));
                    }
                }
            }
            else if (string.Equals(_model.Class, "Autodesk.Revit.DB.ParameterFilterElement", StringComparison.OrdinalIgnoreCase))
            {
                if (_model.Element is Autodesk.Revit.DB.ParameterFilterElement filter)
                {
                    try
                    {
                        var catNames = new List<string>();
                        foreach (var catId in filter.GetCategories())
                        {
                            var cat = Autodesk.Revit.DB.Category.GetCategory(filter.Document, catId);
                            if (cat != null) catNames.Add(cat.Name);
                        }
                        Parameters.Add(new ParameterWrapperVM(new ParameterModel(
                            "FilterCategories",
                            string.Join(", ", catNames),
                            null, "String", 0, "", false, true
                        )));
                    }
                    catch {}
                }
                else
                {
                    Parameters.Add(new ParameterWrapperVM(new ParameterModel(
                        "FilterCategories",
                        "Offline / Not loaded",
                        null, "String", 0, "", false, true
                    )));
                }
            }
            else if (string.Equals(_model.Class, "Autodesk.Revit.DB.SharedParameterElement", StringComparison.OrdinalIgnoreCase) ||
                     _model.Element is Autodesk.Revit.DB.SharedParameterElement)
            {
                if (_model.Element is Autodesk.Revit.DB.SharedParameterElement spe)
                {
                    try
                    {
                        Parameters.Add(new ParameterWrapperVM(new ParameterModel(
                            "GUID",
                            spe.GuidValue.ToString(),
                            null, "String", 0, "", false, true
                        )));
                        var def = spe.GetDefinition();
                        if (def != null)
                        {
                            Parameters.Add(new ParameterWrapperVM(new ParameterModel(
                                "ParameterName",
                                def.Name,
                                null, "String", 0, "", false, true
                            )));
                            string groupStr = "";
                            try
                            {
                                var groupProp = def.GetType().GetProperty("ParameterGroup");
                                if (groupProp != null)
                                {
                                    groupStr = groupProp.GetValue(def)?.ToString() ?? "";
                                }
                            }
                            catch {}
                            if (string.IsNullOrEmpty(groupStr))
                            {
                                try
                                {
                                    var getGroupMethod = def.GetType().GetMethod("GetGroupTypeId");
                                    if (getGroupMethod != null)
                                    {
                                        var gt = getGroupMethod.Invoke(def, null);
                                        groupStr = gt?.GetType().GetProperty("TypeId")?.GetValue(gt)?.ToString() ?? "";
                                    }
                                }
                                catch {}
                            }

                            Parameters.Add(new ParameterWrapperVM(new ParameterModel(
                                "ParameterGroup",
                                groupStr,
                                null, "String", 0, "", false, true
                            )));
                            
                            string pType = "";
                            try
                            {
                                var typeProp = def.GetType().GetProperty("ParameterType");
                                if (typeProp != null)
                                {
                                    pType = typeProp.GetValue(def)?.ToString() ?? "";
                                }
                            }
                            catch {}
                            if (string.IsNullOrEmpty(pType))
                            {
                                try
                                {
                                    var getDataType = def.GetType().GetMethod("GetDataType");
                                    if (getDataType != null)
                                    {
                                        var dt = getDataType.Invoke(def, null);
                                        pType = dt?.GetType().GetProperty("TypeId")?.GetValue(dt)?.ToString() ?? "";
                                    }
                                }
                                catch {}
                            }
                            Parameters.Add(new ParameterWrapperVM(new ParameterModel(
                                "ParameterType",
                                pType,
                                null, "String", 0, "", false, true
                            )));
                        }
                    }
                    catch {}
                }
                else
                {
                    Parameters.Add(new ParameterWrapperVM(new ParameterModel(
                        "GUID",
                        "Offline / Not loaded",
                        null, "String", 0, "", false, true
                    )));
                }
            }

            // Harvest and prepend POCO properties to the top of the Parameters collection
            var harvested = HarvestPocoProperties();
            for (int i = harvested.Count - 1; i >= 0; i--)
            {
                var item = harvested[i];
                if (!Parameters.Any(p => string.Equals(p.Name, item.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    Parameters.Insert(0, item);
                }
            }

            _originalName = _baselineModel?.Name ?? model.Name ?? string.Empty;
            foreach (var param in Parameters)
            {
                param.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName == nameof(ParameterWrapperVM.IsDirty))
                    {
                        OnPropertyChanged(nameof(IsDirty));
                    }
                };
            }
        }

        /// <summary>
        /// Gets whether the element's name is editable.
        /// Root/built-in categories and subcategories cannot be renamed.
        /// </summary>
        public bool IsNameEditable
        {
            get
            {
                if (_model is CategoryModel cat)
                {
                    return cat.Id >= 0;
                }
                return true;
            }
        }

        /// <summary>
        /// Gets or sets the name of the Element Type.
        /// Updates the underlying POCO and raises PropertyChanged.
        /// </summary>
        public string Name
        {
            get => _model.Name;
            set
            {
                if (_model.Name != value)
                {
                    _model.Name = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsDirty));
                    ValidateName();
                }
            }
        }

        /// <summary>
        /// Gets or sets the element aliases as a comma-separated string.
        /// </summary>
        public string AliasesString
        {
            get
            {
                if (_model.Aliases == null || _model.Aliases.Count == 0)
                {
                    return string.Empty;
                }
                return string.Join(", ", _model.Aliases);
            }
            set
            {
                var list = string.IsNullOrWhiteSpace(value)
                    ? new List<string>()
                    : value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                           .Select(s => s.Trim())
                           .Where(s => !string.IsNullOrEmpty(s))
                           .ToList();

                bool isEqual = _model.Aliases != null && _model.Aliases.SequenceEqual(list);
                if (!isEqual)
                {
                    _model.Aliases = list;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Gets the class of the Element Type.
        /// This field is read-only.
        /// </summary>
        public string Class => _model.Class;

        /// <summary>
        /// Gets the category of the Element Type.
        /// This field is read-only.
        /// </summary>
        public string Category => _model.Category;

        /// <summary>
        /// Gets the Level 1 category/type grouping.
        /// </summary>
        public string CategoryGroupLevel1
        {
            get
            {
                if (_model is CategoryModel cat)
                {
                    return cat.CategoryGroupLevel1;
                }
                return this.Group;
            }
        }

        /// <summary>
        /// Gets the Level 2 parent category/type grouping.
        /// </summary>
        public string CategoryGroupLevel2
        {
            get
            {
                if (_model is CategoryModel cat)
                {
                    return cat.CategoryGroupLevel2;
                }
                return this.Class;
            }
        }

        /// <summary>
        /// Synchronizes and returns the updated ElementTypeModel POCO for future serialization.
        /// </summary>
        /// <returns>The updated ElementTypeModel POCO.</returns>
        public ElementModel GetUpdatedModel()
        {
            // Sync parameters list from ObservableCollection back to the model list
            _model.Parameters = Parameters.Select(p => p.GetModel()).ToList();
            return _model;
        }

        #region INotifyDataErrorInfo Implementation

        /// <summary>
        /// Gets whether there are any validation errors.
        /// </summary>
        public bool HasErrors => _errors.Any(kvp => kvp.Value != null && kvp.Value.Count > 0);

        /// <summary>
        /// Gets the validation errors for a specific property.
        /// </summary>
        /// <param name="propertyName">The name of the property to get errors for.</param>
        /// <returns>A collection of error messages.</returns>
        public IEnumerable GetErrors(string? propertyName)
        {
            if (string.IsNullOrEmpty(propertyName))
            {
                return _errors.Values.SelectMany(x => x);
            }

            if (_errors.TryGetValue(propertyName!, out var errors))
            {
                return errors;
            }

            return Enumerable.Empty<string>();
        }

        /// <summary>
        /// Raises the ErrorsChanged event and notifies that HasErrors has changed.
        /// </summary>
        /// <param name="propertyName">The name of the property whose validation errors changed.</param>
        protected virtual void OnErrorsChanged(string propertyName)
        {
            ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(propertyName));
            OnPropertyChanged(nameof(HasErrors));
        }

        /// <summary>
        /// Validates the Name property to check for duplicate names within the same category.
        /// </summary>
        public void ValidateName()
        {
            // Clear existing errors for Name first
            if (_errors.ContainsKey(nameof(Name)))
            {
                _errors.Remove(nameof(Name));
                OnErrorsChanged(nameof(Name));
            }

            if (string.IsNullOrWhiteSpace(Name))
            {
                AddError(nameof(Name), "Name cannot be empty.");
            }
            else if (IsNameDuplicateCallback != null && IsNameDuplicateCallback(this, Name))
            {
                AddError(nameof(Name), $"An item with the name '{Name}' already exists in this category.");
            }
        }

        /// <summary>
        /// Adds a validation error to the dictionary and triggers ErrorsChanged.
        /// </summary>
        private void AddError(string propertyName, string error)
        {
            if (!_errors.TryGetValue(propertyName, out var errors))
            {
                errors = new List<string>();
                _errors[propertyName] = errors;
            }

            if (!errors.Contains(error))
            {
                errors.Add(error);
                OnErrorsChanged(propertyName);
            }
        }

        #endregion

        /// <summary>
        /// Reflection sweep to harvest all public properties on the underlying POCO, excluding Name, Class, Aliases, and [JsonIgnore] properties.
        /// </summary>
        public List<ParameterWrapperVM> HarvestPocoProperties()
        {
            if (_harvestedProperties != null)
            {
                return _harvestedProperties;
            }

            var list = new List<ParameterWrapperVM>();
            var type = _model.GetType();
            var properties = type.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

            foreach (var prop in properties)
            {
                // Exclude base properties of ElementModel and basic metadata
                if (prop.DeclaringType == typeof(ElementModel) ||
                    prop.Name == "Name" || 
                    prop.Name == "Class" || 
                    prop.Name == "Aliases" || 
                    prop.Name == "Id" || 
                    prop.Name == "UniqueId" || 
                    prop.Name == "Parameters" || 
                    prop.Name == "Category" || 
                    prop.Name == "ElementId" || 
                    prop.Name == "Element" || 
                    prop.Name == "Document" || 
                    prop.Name == "IsTemplate")
                {
                    continue;
                }

                // Check for [JsonIgnore] attribute
                if (prop.GetCustomAttributes(typeof(Newtonsoft.Json.JsonIgnoreAttribute), true).Any())
                {
                    continue;
                }

                // Get getter and setter
                var getMethod = prop.GetMethod;
                var setMethod = prop.SetMethod;

                if (getMethod == null) continue; // Property must be readable

                bool isReadOnly = setMethod == null || !setMethod.IsPublic;

                // Let's determine if it's primitive or complex
                var propType = prop.PropertyType;
                var underlyingType = Nullable.GetUnderlyingType(propType);
                var targetType = underlyingType ?? propType;

                bool isPrimitive = targetType.IsPrimitive || 
                                   targetType.IsEnum || 
                                   targetType == typeof(string) || 
                                   targetType == typeof(decimal) || 
                                   targetType == typeof(DateTime) || 
                                   targetType == typeof(Guid);

                if (isPrimitive)
                {
                    // Construct a dummy ParameterModel for UI compatibility
                    var val = prop.GetValue(_model);
                    var valStr = val?.ToString() ?? string.Empty;

                    var dummyParam = new ParameterModel(
                        Name: prop.Name,
                        Value: valStr,
                        ValueElemId: null,
                        StorageType: targetType.Name,
                        Id: 0,
                        GUID: null,
                        IsShared: false,
                        IsReadOnly: isReadOnly
                    );

                    // Find corresponding baseline value for dirty tracking if available
                    ParameterModel? baselineParam = null;
                    if (_baselineModel != null)
                    {
                        var baselineProp = _baselineModel.GetType().GetProperty(prop.Name);
                        if (baselineProp != null)
                        {
                            var baseVal = baselineProp.GetValue(_baselineModel);
                            baselineParam = new ParameterModel(
                                Name: prop.Name,
                                Value: baseVal?.ToString() ?? string.Empty,
                                ValueElemId: null,
                                StorageType: targetType.Name,
                                Id: 0,
                                GUID: null,
                                IsShared: false,
                                IsReadOnly: isReadOnly
                            );
                        }
                    }

                    var wrapper = new ParameterWrapperVM(dummyParam, _model, baselineParam);
                    
                    // Event-driven primitive syncing: listen to wrapper's PropertyChanged event
                    wrapper.PropertyChanged += (sender, e) =>
                    {
                        if (e.PropertyName == nameof(ParameterWrapperVM.Value))
                        {
                            try
                            {
                                object? parsedVal = null;
                                string strVal = wrapper.Value;

                                if (string.IsNullOrEmpty(strVal))
                                {
                                    if (propType.IsValueType && underlyingType == null)
                                    {
                                        parsedVal = Activator.CreateInstance(targetType);
                                    }
                                    else
                                    {
                                        parsedVal = null;
                                    }
                                }
                                else if (targetType == typeof(string))
                                {
                                    parsedVal = strVal;
                                }
                                else if (targetType.IsEnum)
                                {
                                    parsedVal = Enum.Parse(targetType, strVal, true);
                                }
                                else if (targetType == typeof(Guid))
                                {
                                    parsedVal = Guid.Parse(strVal);
                                }
                                else
                                {
                                    parsedVal = Convert.ChangeType(strVal, targetType);
                                }

                                prop.SetValue(_model, parsedVal);
                                OnPropertyChanged(nameof(IsDirty));
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"Failed to parse value for reflection property {prop.Name}: {ex.Message}");
                            }
                        }
                    };

                    list.Add(wrapper);
                }
                else
                {
                    // Complex object/collection wrapping
                    var val = prop.GetValue(_model);
                    if (val != null)
                    {
                        var wrapper = new ParameterWrapperVM(prop.Name, val);
                        list.Add(wrapper);
                    }
                }
            }

            _harvestedProperties = list;
            return list;
        }
    }
}
```

### File: StandardsManagement/ViewModels/ImportSummaryViewModel.cs
```csharp
using System.Collections.ObjectModel;
using System.Linq;

using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;
using Synthetic.Infrastructure.Serialization;

using Synthetic.Shared.UI;
using Synthetic.RevitDOM.Operations.Standards;
using Synthetic.RevitDOM.Operations.Standards;

namespace Synthetic.Modules.StandardsManagement.ViewModels
{
    /// <summary>
    /// ViewModel for the import summary view, summarizing the results of an import operation.
    /// </summary>
    public class ImportSummaryViewModel : ViewModelBase
    {
        /// <summary>
        /// Gets the collection of import log items.
        /// </summary>
        public ObservableCollection<ImportLogItem> LogItems { get; }

        /// <summary>
        /// Gets the count of items that were created during import.
        /// </summary>
        public int CreatedCount => LogItems.Count(item => item.Action == "Created");

        /// <summary>
        /// Gets the count of items that were updated during import.
        /// </summary>
        public int UpdatedCount => LogItems.Count(item => item.Action == "Updated");

        /// <summary>
        /// Gets the count of items that were renamed during import.
        /// </summary>
        public int RenamedCount => LogItems.Count(item => item.Action == "Renamed");

        /// <summary>
        /// Gets the count of items that were unchanged during import.
        /// </summary>
        public int UnchangedCount => LogItems.Count(item => item.Action == "Unchanged");

        private readonly IFileDialogService? _dialogService;

        /// <summary>
        /// Gets the count of failed import operations.
        /// </summary>
        public int ErrorsCount => LogItems.Count(item => item.Action == "Failed");

        /// <summary>
        /// Gets the command to export the execution log to a markdown file.
        /// </summary>
        public System.Windows.Input.ICommand ExportLogCommand { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="ImportSummaryViewModel"/> class.
        /// </summary>
        /// <param name="logItems">The list of import log items to summarize.</param>
        /// <param name="dialogService">The dialog service for choosing file save paths.</param>
        public ImportSummaryViewModel(ObservableCollection<ImportLogItem> logItems, IFileDialogService? dialogService = null)
        {
            LogItems = logItems;
            _dialogService = dialogService;
            ExportLogCommand = new RelayCommand(ExecuteExportLog, CanExecuteExportLog);
        }

        /// <summary>
        /// Generates a formatted markdown report from the summary log items.
        /// </summary>
        public string GenerateMarkdown()
        {
            return StandardsReportGenerator.GenerateMarkdown(LogItems);
        }

        private void ExecuteExportLog(object parameter)
        {
            if (_dialogService == null) return;

            string? path = _dialogService.SaveFileDialog("Markdown Files (*.md)|*.md", "Export Execution Log", "ExecutionSummary.md");
            if (!string.IsNullOrEmpty(path))
            {
                try
                {
                    string markdown = GenerateMarkdown();
                    System.IO.File.WriteAllText(path, markdown);
                }
                catch (System.Exception ex)
                {
                    System.Console.WriteLine($"Failed to write markdown log: {ex.Message}");
                }
            }
        }

        private bool CanExecuteExportLog(object parameter)
        {
            return _dialogService != null && LogItems != null && LogItems.Count > 0;
        }
    }
}
```

### File: StandardsManagement/ViewModels/IProjectStandardsDashboard.cs
```csharp
using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;
using Synthetic.RevitDOM.Operations.Standards;
using Synthetic.RevitDOM.Operations.Standards;

namespace Synthetic.Modules.StandardsManagement.ViewModels
{
    /// <summary>
    /// Defines the contract for the parent Project Standards Dashboard ViewModel.
    /// Breaks tight coupling and reduces Feature Envy/Inappropriate Intimacy between sub-ViewModels.
    /// </summary>
    public interface IProjectStandardsDashboard
    {
        /// <summary>
        /// Gets or sets the currently active source standard tab.
        /// </summary>
        ProjectStandardsSourceViewModel? SelectedSource { get; set; }

        /// <summary>
        /// Gets or sets the active panel mode in the right column sub-workspace.
        /// </summary>
        WorkspaceMode ActiveWorkspace { get; set; }

        /// <summary>
        /// Gets the active Revit document.
        /// </summary>
        Document? Document { get; }

        /// <summary>
        /// Gets the find and replace utility service.
        /// </summary>
        IFindReplaceService FindReplaceService { get; }

        /// <summary>
        /// Gets or sets the mock open documents list for headless testing.
        /// </summary>
        List<Document>? MockOpenDocuments { get; set; }

        /// <summary>
        /// Shows the document selection dialog callback.
        /// </summary>
        Func<SelectRevitDocumentViewModel, bool?>? ShowDocumentSelectionDialog { get; set; }

        /// <summary>
        /// Shows the consolidation merge dialog callback.
        /// </summary>
        Func<Synthetic.Shared.UI.SingleItemSelectionViewModel<QueueItemModel>, bool?>? ShowMergeDialog { get; set; }

        /// <summary>
        /// Traverses a source tree node hierarchy recursively and populates a flat list of ElementModels.
        /// </summary>
        void GetElementModelsFromHierarchy(SourceTreeItemViewModel node, List<ElementModel> list);

        /// <summary>
        /// Gathers the flat list of all checked element nodes in the active source tree.
        /// </summary>
        List<StandardElementModel> GetCheckedElements();

        /// <summary>
        /// Updates parameter value name references targeting an old name to the new name.
        /// </summary>
        void ReplaceReferences(ObjectModel oldElement, string oldName, string newName);

        /// <summary>
        /// Replaces name references targeting a list of consolidated old queue items to the new survivor name.
        /// </summary>
        void ReplaceQueueReferences(List<QueueItemModel> oldElements, string newName);
    }
}
```

### File: StandardsManagement/ViewModels/NestedDataEditorViewModel.cs
```csharp
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Windows.Input;

using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;
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
```

### File: StandardsManagement/ViewModels/ParameterWrapperVM.cs
```csharp
﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows.Input;

using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;
using Synthetic.Infrastructure.Serialization;
using Autodesk.Revit.DB;
using Select = Synthetic.Shared.RevitAPI.Select;

using Synthetic.Shared.UI;
using Synthetic.Modules.MergeDuplicates.Handlers;
using Synthetic.Modules.StandardsManagement.Views;
using Synthetic.Shared.RevitAPI;
namespace Synthetic.Modules.StandardsManagement.ViewModels
{
    /// <summary>
    /// ViewModel wrapper for the ParameterModel POCO.
    /// Supports two-way data binding, field-locking, &lt;Varies&gt; state, and validation.
    /// </summary>
    public class ParameterWrapperVM : ViewModelBase, INotifyDataErrorInfo
    {
        private readonly ParameterModel _model;
        private readonly ElementModel? _parentModel;
        private readonly ParameterModel? _baselineParamModel;
        private bool _isMixedValue;
        private readonly Dictionary<string, List<string>> _errors = new Dictionary<string, List<string>>();
        private ICommand? _editNestedDataCommand;
        private readonly object? _nestedDataTarget;
        private readonly IUserPromptService _userPromptService;

        /// <summary>
        /// Event raised when validation errors change for a property.
        /// </summary>
        public event EventHandler<DataErrorsChangedEventArgs>? ErrorsChanged;

        /// <summary>
        /// Initializes a new instance of the <see cref="ParameterWrapperVM"/> class.
        /// </summary>
        /// <param name="model">The underlying ParameterModel POCO.</param>
        /// <param name="parentModel">The parent element model of this parameter.</param>
        /// <param name="baselineParamModel">The matching baseline parameter model for on-the-fly dirty tracking.</param>
        public ParameterWrapperVM(ParameterModel model, ElementModel? parentModel = null, ParameterModel? baselineParamModel = null, IUserPromptService? userPromptService = null)
        {
            _model = model ?? throw new ArgumentNullException(nameof(model));
            _parentModel = parentModel;
            _baselineParamModel = baselineParamModel;
            _userPromptService = userPromptService ?? new WindowsUserPromptService();
            
            // If the model's initial value is "<Varies>", reflect that in IsMixedValue
            if (_model.Value == "<Varies>")
            {
                _isMixedValue = true;
            }

            // Run initial validation
            ValidateValue();
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ParameterWrapperVM"/> class for complex nested data.
        /// </summary>
        /// <param name="propertyName">The name of the complex property.</param>
        /// <param name="nestedDataTarget">The nested data object target.</param>
        public ParameterWrapperVM(string propertyName, object nestedDataTarget, IUserPromptService? userPromptService = null)
        {
            _model = new ParameterModel(
                Name: propertyName,
                Value: nestedDataTarget != null ? "[Complex Nested Data]" : string.Empty,
                ValueElemId: null,
                StorageType: "String",
                Id: 0,
                GUID: null,
                IsShared: false,
                IsReadOnly: false
            );
            _nestedDataTarget = nestedDataTarget ?? throw new ArgumentNullException(nameof(nestedDataTarget));
            _userPromptService = userPromptService ?? new WindowsUserPromptService();
        }

        /// <summary>
        /// Gets the underlying ParameterModel POCO reference.
        /// </summary>
        /// <returns>The ParameterModel POCO.</returns>
        public ParameterModel GetModel()
        {
            return _model;
        }

        #region Wrapper Properties

        /// <summary>
        /// Gets the parameter Name. (Read-Only)
        /// </summary>
        public string Name => _model.Name;

        /// <summary>
        /// Gets the parameter StorageType (e.g. Integer, Double, ElementId, String). (Read-Only)
        /// </summary>
        public string StorageType => _model.StorageType;

        /// <summary>
        /// Gets the parameter Id. (Read-Only)
        /// </summary>
        public long Id => _model.Id;

        /// <summary>
        /// Gets the parameter GUID if it is a shared parameter. (Read-Only)
        /// </summary>
        public string? GUID => _model.GUID;

        /// <summary>
        /// Gets whether the parameter is a shared parameter. (Read-Only)
        /// </summary>
        public bool IsShared => _model.IsShared;

        private bool _isDirty;
        /// <summary>
        /// Gets or sets a value indicating whether this parameter has unsaved changes.
        /// </summary>
        public bool IsDirty
        {
            get
            {
                if (_baselineParamModel != null)
                {
                    return _model.Value != _baselineParamModel.Value;
                }
                return _isDirty;
            }
            set
            {
                if (_baselineParamModel != null)
                {
                    _isDirty = value;
                    OnPropertyChanged();
                }
                else
                {
                    SetProperty(ref _isDirty, value);
                }
            }
        }

        /// <summary>
        /// Gets whether the parameter is read-only. (Read-Only)
        /// </summary>
        public bool IsReadOnly
        {
            get
            {
                if (_model.IsReadOnly) return true;
                
                if (_parentModel is CategoryModel cat)
                {
                    // Cuttable Validation: If category is not cuttable, lock cut weight and pattern parameters
                    if (!cat.IsCuttable)
                    {
                        if (Name == "LineWeightCut" || Name == "LinePatternCut")
                        {
                            return true;
                        }
                    }

                    // Built-in validation: If category ID is built-in (ID < 0), lock the Name parameter
                    if (Name == "Name" && cat.Id < 0)
                    {
                        return true;
                    }
                }
                
                return false;
            }
        }

        /// <summary>
        /// Gets the full ElementIdModel for reference element parameters. (Read-Only)
        /// </summary>
        public ElementIdModel? ValueElemId => _model.ValueElemId;

        /// <summary>
        /// Gets the Class of the reference element parameter. (Read-Only)
        /// </summary>
        public string? ValueElemIdClass => _model.ValueElemId?.Class;

        /// <summary>
        /// Gets the Category of the reference element parameter. (Read-Only)
        /// </summary>
        public string? ValueElemIdCategory => _model.ValueElemId?.Category;

        /// <summary>
        /// Gets the Name of the reference element parameter. (Read-Only)
        /// </summary>
        public string? ValueElemIdName => _model.ValueElemId?.Name;

        #endregion

        #region Editable & Varies Properties

        /// <summary>
        /// Gets or sets the value of the parameter.
        /// If <see cref="IsMixedValue"/> is true, this returns "&lt;Varies&gt;".
        /// If set to a different value, <see cref="IsMixedValue"/> is automatically set to false.
        /// </summary>
        public string Value
        {
            get
            {
                if (IsMixedValue) return "<Varies>";

                bool isLinePattern = (string.Equals(ValueElemIdClass, "Autodesk.Revit.DB.LinePatternElement", StringComparison.OrdinalIgnoreCase) ||
                                      Name.IndexOf("LinePattern", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                      Name.IndexOf("Line Pattern", StringComparison.OrdinalIgnoreCase) >= 0);
                bool isMaterial = (string.Equals(ValueElemIdClass, "Autodesk.Revit.DB.Material", StringComparison.OrdinalIgnoreCase) ||
                                   string.Equals(ValueElemIdCategory, "Materials", StringComparison.OrdinalIgnoreCase) ||
                                   Name.IndexOf("Material", StringComparison.OrdinalIgnoreCase) >= 0);

                if (StorageType == "ElementId")
                {
                    if (string.IsNullOrEmpty(_model.Value) || _model.ValueElemId == null || string.IsNullOrEmpty(_model.ValueElemId.Name))
                    {
                        if (isLinePattern) return "Solid";
                        if (isMaterial) return "<By Category>";
                        return "<None>";
                    }
                }
                return _model.Value ?? string.Empty;
            }
            set
            {
                if (value == "<Varies>")
                {
                    IsMixedValue = true;
                    return;
                }

                // If currently in a mixed value state, clear it as we are setting a specific value
                if (IsMixedValue)
                {
                    _isMixedValue = false;
                    OnPropertyChanged(nameof(IsMixedValue));
                }

                string cleanValue = value;
                if (cleanValue == "<None>" || cleanValue == "<By Category>" || cleanValue == "Solid")
                {
                    cleanValue = "";
                }

                if (_model.Value != cleanValue)
                {
                    _model.Value = cleanValue;
                    IsDirty = true;
                    if (StorageType == "ElementId")
                    {
                        if (_model.ValueElemId == null)
                        {
                            _model.ValueElemId = new ElementIdModel { Class = "", Category = "" };
                        }
                        
                        // Keep Name as what the user selected so mapping functions have a reference to defaults
                        _model.ValueElemId.Name = value; 

                        bool isLinePattern = (string.Equals(ValueElemIdClass, "Autodesk.Revit.DB.LinePatternElement", StringComparison.OrdinalIgnoreCase) ||
                                              Name.IndexOf("LinePattern", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                              Name.IndexOf("Line Pattern", StringComparison.OrdinalIgnoreCase) >= 0);
                        if (isLinePattern && value == "Solid")
                        {
#if REVIT2022 || REVIT2023
                            _model.ValueElemId.Id = LinePatternElement.GetSolidPatternId().IntegerValue;
#else
                            _model.ValueElemId.Id = LinePatternElement.GetSolidPatternId().Value;
#endif
                        }
                        else
                        {
                            _model.ValueElemId.Id = 0;
                        }
                        _model.ValueElemId.UniqueId = null;

                        OnPropertyChanged(nameof(ValueElemId));
                        OnPropertyChanged(nameof(ValueElemIdName));
                    }
                    OnPropertyChanged(nameof(Value));
                    ValidateValue();
                }
            }
        }

        /// <summary>
        /// Gets or sets whether this parameter represents multiple varying values (mixed value state).
        /// If true, the <see cref="Value"/> property returns "&lt;Varies&gt;".
        /// </summary>
        public bool IsMixedValue
        {
            get => _isMixedValue;
            set
            {
                if (SetProperty(ref _isMixedValue, value))
                {
                    // Raise property change for Value since its output depends on IsMixedValue
                    OnPropertyChanged(nameof(Value));
                    // Revalidate since "<Varies>" skips validation, but non-varies needs it.
                    ValidateValue();
                }
            }
        }

        /// <summary>
        /// Gets the available references matching the parameter spec class/category.
        /// </summary>
        public IEnumerable<string> AvailableReferences
        {
            get
            {
                if (StorageType != "ElementId")
                {
                    return Enumerable.Empty<string>();
                }

                string? targetClass = ValueElemIdClass;
                string? targetCategory = ValueElemIdCategory;

                if (string.IsNullOrEmpty(targetClass))
                {
                    if (Name.IndexOf("Material", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        targetClass = "Autodesk.Revit.DB.Material";
                    }
                    else if (Name.IndexOf("LinePattern", StringComparison.OrdinalIgnoreCase) >= 0 || Name.IndexOf("Line Pattern", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        targetClass = "Autodesk.Revit.DB.LinePatternElement";
                    }
                    else if (Name.IndexOf("Pattern", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        targetClass = "Autodesk.Revit.DB.FillPatternElement";
                    }
                }

                bool isLinePattern = (string.Equals(targetClass, "Autodesk.Revit.DB.LinePatternElement", StringComparison.OrdinalIgnoreCase) ||
                                      Name.IndexOf("LinePattern", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                      Name.IndexOf("Line Pattern", StringComparison.OrdinalIgnoreCase) >= 0);
                bool isMaterial = (string.Equals(targetClass, "Autodesk.Revit.DB.Material", StringComparison.OrdinalIgnoreCase) ||
                                   string.Equals(targetCategory, "Materials", StringComparison.OrdinalIgnoreCase) ||
                                   Name.IndexOf("Material", StringComparison.OrdinalIgnoreCase) >= 0);

                var list = new List<string>();
                if (isLinePattern)
                {
                    list.Add("Solid");
                }
                else if (isMaterial)
                {
                    list.Add("<By Category>");
                }
                else
                {
                    list.Add("<None>");
                }

                // 1. Harvest from Revit document if online
                var uiapp = ProjectStandardsDashboardViewModel.Instance?.UIApplication;
                var doc = uiapp?.ActiveUIDocument?.Document;
                if (doc != null && !string.IsNullOrEmpty(targetClass))
                {
                    if (string.Equals(targetClass, "Autodesk.Revit.DB.Material", StringComparison.OrdinalIgnoreCase))
                    {
                        foreach (var mat in Select.AllMaterials(doc))
                        {
                            if (!string.IsNullOrEmpty(mat.Name)) list.Add(mat.Name);
                        }
                    }
                    else if (string.Equals(targetClass, "Autodesk.Revit.DB.FillPatternElement", StringComparison.OrdinalIgnoreCase))
                    {
                        var fillPatterns = new FilteredElementCollector(doc)
                            .OfClass(typeof(FillPatternElement))
                            .Cast<FillPatternElement>();
                        foreach (var fp in fillPatterns)
                        {
                            if (!string.IsNullOrEmpty(fp.Name)) list.Add(fp.Name);
                        }
                    }
                    else if (string.Equals(targetClass, "Autodesk.Revit.DB.LinePatternElement", StringComparison.OrdinalIgnoreCase))
                    {
                        var linePatterns = new FilteredElementCollector(doc)
                            .OfClass(typeof(LinePatternElement))
                            .Cast<LinePatternElement>();
                        foreach (var lp in linePatterns)
                        {
                            if (!string.IsNullOrEmpty(lp.Name)) list.Add(lp.Name);
                        }
                    }
                    else
                    {
                        try
                        {
                            System.Reflection.Assembly assembly = typeof(Element).Assembly;
                            Type? t = assembly.GetType(targetClass!);
                            if (t != null)
                            {
                                var elements = new FilteredElementCollector(doc)
                                    .OfClass(t)
                                    .ToElements();
                                foreach (var e in elements)
                                {
                                    if (!string.IsNullOrEmpty(e.Name)) list.Add(e.Name);
                                }
                            }
                        }
                        catch {}
                    }
                }

                // 2. Also harvest from JSON pool
                if (ProjectStandardsDashboardViewModel.Instance != null && ProjectStandardsDashboardViewModel.Instance.WrappedElements != null)
                {
                    foreach (var el in ProjectStandardsDashboardViewModel.Instance.WrappedElements)
                    {
                        bool isMatch = false;
                        if (!string.IsNullOrEmpty(targetClass) && string.Equals(el.Class, targetClass, StringComparison.OrdinalIgnoreCase))
                        {
                            isMatch = true;
                        }
                        else if (!string.IsNullOrEmpty(targetCategory) && string.Equals(el.Category, targetCategory, StringComparison.OrdinalIgnoreCase))
                        {
                            isMatch = true;
                        }

                        if (isMatch && !string.IsNullOrEmpty(el.Name))
                        {
                            list.Add(el.Name);
                        }
                    }
                }

                if (ProjectStandardsDashboardViewModel.Instance != null)
                {
                    var activeSource = ProjectStandardsDashboardViewModel.Instance.SelectedSource;
                    if (activeSource != null && !string.IsNullOrEmpty(targetClass))
                    {
                        foreach (var group in activeSource.SourceHierarchy)
                        {
                            GetElementsFromHierarchy(group, targetClass, list);
                        }
                    }

                    foreach (var qItem in ProjectStandardsDashboardViewModel.Instance.StagingQueue)
                    {
                        if (qItem.Model is ElementModel el && string.Equals(el.Class, targetClass, StringComparison.OrdinalIgnoreCase))
                        {
                            if (!string.IsNullOrEmpty(el.Name)) list.Add(el.Name);
                        }
                    }
                }

                return list.Distinct().OrderBy(s => (s == "<None>" || s == "<By Category>" || s == "Solid") ? "" : s).ToList();
            }
        }

        private void GetElementsFromHierarchy(SourceTreeItemViewModel node, string targetClass, List<string> names)
        {
            if (node == null) return;
            if (node is StandardElementModel sem && sem.Element != null)
            {
                if (string.Equals(sem.Element.Class, targetClass, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(sem.Element.Name))
                {
                    names.Add(sem.Element.Name);
                }
            }
            if (node.Children != null)
            {
                foreach (var child in node.Children)
                {
                    GetElementsFromHierarchy(child, targetClass, names);
                }
            }
        }

        /// <summary>
        /// Command to edit complex nested data structures.
        /// </summary>
        public ICommand EditNestedDataCommand
        {
            get
            {
                if (_editNestedDataCommand == null)
                {
                    _editNestedDataCommand = new RelayCommand(o => EditNestedData());
                }
                return _editNestedDataCommand;
            }
        }

        private void EditNestedData()
        {
            var dashboardVM = ProjectStandardsDashboardViewModel.Instance;
            if (dashboardVM == null) return;

            IntPtr ownerHandle = dashboardVM.MainWindowHandle;
            
            ElementTypeWrapperVM? selectedWrapper = null;
            if (_parentModel != null)
            {
                selectedWrapper = new ElementTypeWrapperVM(_parentModel);
            }

            if (selectedWrapper == null) return;

            object? targetData = null;

            if (_nestedDataTarget is CompoundStructureModel structureModel)
            {
                targetData = structureModel;
            }
            else if (_nestedDataTarget is List<CategoryGraphicOverridesModel> graphicOverrides)
            {
                targetData = graphicOverrides;
            }
            else if (Name == "CompoundStructure")
            {
                var model = selectedWrapper.GetUpdatedModel();
                if (model is HostObjTypeModel hostModel)
                {
                    if (hostModel.Structure == null)
                    {
                        hostModel.Structure = new CompoundStructureModel { Layers = new List<SerialCompoundStructureLayer>() };
                    }
                    targetData = hostModel.Structure;
                }
            }
            else if (Name == "VisibilityGraphics")
            {
                var model = selectedWrapper.GetUpdatedModel();
                if (model is ViewModel viewModel)
                {
                    if (viewModel.CategoryGraphicOverrides == null)
                    {
                        viewModel.CategoryGraphicOverrides = new List<CategoryGraphicOverridesModel>();
                    }
                    targetData = viewModel.CategoryGraphicOverrides;
                }
            }

            if (targetData != null)
            {
                var elementsPool = new System.Collections.ObjectModel.ObservableCollection<ElementTypeWrapperVM>(dashboardVM.AllWrappedElements);
                var nestedVM = new NestedDataEditorViewModel(targetData, elementsPool);
                var window = new Views.NestedDataEditorWindow(ownerHandle, nestedVM);
                
                bool? result = window.ShowDialog();
                if (result == true)
                {
                    OnPropertyChanged(nameof(Value));
                    selectedWrapper.GetUpdatedModel();
                }
            }
            else
            {
                _userPromptService.ShowMessage(
                    $"Editing nested structures (like compound layers or visibility-graphics overrides) for '{Name}' is not supported in the offline editor.",
                    "Nested Data Editor");
            }
        }

        #endregion

        #region INotifyDataErrorInfo Implementation

        /// <summary>
        /// Gets whether there are any validation errors.
        /// </summary>
        public bool HasErrors => _errors.Any(kvp => kvp.Value != null && kvp.Value.Count > 0);

        /// <summary>
        /// Gets the validation errors for a specific property.
        /// </summary>
        /// <param name="propertyName">The name of the property to get errors for.</param>
        /// <returns>A collection of error messages.</returns>
        public IEnumerable GetErrors(string? propertyName)
        {
            if (string.IsNullOrEmpty(propertyName))
            {
                return _errors.Values.SelectMany(x => x);
            }

            if (_errors.TryGetValue(propertyName!, out var errors))
            {
                return errors;
            }

            return Enumerable.Empty<string>();
        }

        /// <summary>
        /// Raises the ErrorsChanged event and notifies that HasErrors has changed.
        /// </summary>
        /// <param name="propertyName">The name of the property whose validation errors changed.</param>
        protected virtual void OnErrorsChanged(string propertyName)
        {
            ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(propertyName));
            OnPropertyChanged(nameof(HasErrors));
        }

        /// <summary>
        /// Validates the Value property based on StorageType.
        /// </summary>
        private void ValidateValue()
        {
            string currentValue = Value;

            // Clear existing errors for Value first
            if (_errors.ContainsKey(nameof(Value)))
            {
                _errors.Remove(nameof(Value));
                OnErrorsChanged(nameof(Value));
            }

            // If Value is "<Varies>", it is always considered valid (skip validation)
            if (currentValue == "<Varies>")
            {
                return;
            }

            // Validate based on the StorageType string from the POCO
            if (StorageType == "Integer")
            {
                if (!int.TryParse(currentValue, out _))
                {
                    AddError(nameof(Value), "Value must be a whole number.");
                }
            }
            else if (StorageType == "Double")
            {
                if (!double.TryParse(currentValue, out _))
                {
                    AddError(nameof(Value), "Value must be a valid double precision number.");
                }
            }
        }

        /// <summary>
        /// Adds a validation error to the dictionary and triggers ErrorsChanged.
        /// </summary>
        private void AddError(string propertyName, string error)
        {
            if (!_errors.TryGetValue(propertyName, out var errors))
            {
                errors = new List<string>();
                _errors[propertyName] = errors;
            }

            if (!errors.Contains(error))
            {
                errors.Add(error);
                OnErrorsChanged(propertyName);
            }
        }

        #endregion
    }
}
```

### File: StandardsManagement/ViewModels/ProjectStandardsDashboardViewModel.cs
```csharp
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Input;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Synthetic.RevitDOM.Operations.Merge;
using Synthetic.RevitDOM.Operations.Standards;
using Synthetic.Core;

using Synthetic.Shared.UI;
using Synthetic.Shared.RevitAPI;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;
using Synthetic.Settings;
using Synthetic.Infrastructure.Persistence;
using Synthetic.RevitDOM.Operations.Standards;
using Synthetic.RevitDOM.Operations.Diffing;

namespace Synthetic.Modules.StandardsManagement.ViewModels
{
    /// <summary>
    /// Specifies the active panel mode in the right column sub-workspace.
    /// </summary>
    public enum WorkspaceMode
    {
        Idle,
        Edit,
        Diff
    }

    /// <summary>
    /// Specifies the scope of a find and replace operation.
    /// </summary>
    public enum SearchScope
    {
        /// <summary>
        /// Search within element names.
        /// </summary>
        ElementNames,

        /// <summary>
        /// Search within parameter values.
        /// </summary>
        ParameterValues,

        /// <summary>
        /// Search within both element names and parameter values.
        /// </summary>
        Both
    }

    /// <summary>
    /// ViewModel that manages the Project Standards Dashboard modeless window.
    /// Orchestrates multiple source tabs, hierarchical trees, search filtering, and staging.
    /// </summary>
    public class ProjectStandardsDashboardViewModel : ViewModelBase, IProjectStandardsDashboard
    {
        private readonly UIApplication? _uiapp;
        private readonly Document? _doc;
        private readonly IFileDialogService _dialogService;
        private readonly IStandardsExportService _exportService;
        internal readonly IUserPromptService _userPromptService;
        private readonly IFindReplaceService _findReplaceService;
        private readonly IStandardsExtractionOrchestrator _orchestrator;
        private readonly IPocoIdentityService _pocoIdentityService;
        private readonly IStandardSerializationEngine _serializationEngine;
        private readonly IStandardsExecutionPipeline _pipeline;
        public ISummaryDisplayService SummaryDisplayService { get; set; }
        public IPocoIdentityService PocoIdentityService => _pocoIdentityService;
        public IFindReplaceService FindReplaceService => _findReplaceService;
        public IStandardSerializationEngine SerializationEngine => _serializationEngine;
        public IFileDialogService DialogService => _dialogService;
        public IStandardsExecutionPipeline Pipeline => _pipeline;
        private StandardsSettings? _settings;
        private ExternalEvent? _externalEvent;
        private ProjectStandardsExternalEventHandler? _eventHandler;
        public ExternalEvent? ExternalEvent => _externalEvent;
        public StandardsSettings? Settings => _settings;
        private readonly StandardsSourceTreeViewModel _sourceTreeViewModel;
        private readonly StagingQueueViewModel _stagingQueueViewModel;
        public StagingQueueViewModel StagingQueueViewModel => _stagingQueueViewModel;
        private readonly StandardsExecutionPipelineViewModel _standardsExecutionViewModel;
        public StandardsExecutionPipelineViewModel StandardsExecutionPipelineViewModel => _standardsExecutionViewModel;

        /// <summary>
        /// Gets the staging queue collection.
        /// </summary>
        public ObservableCollection<QueueItemModel> StagingQueue => _stagingQueueViewModel.StagingQueue;

        /// <summary>
        /// Gets the grouped collection view of the staging queue.
        /// </summary>
        public ICollectionView StagingQueueView => _stagingQueueViewModel.StagingQueueView;

        public static ProjectStandardsDashboardViewModel? Instance { get; set; }
        public ObservableCollection<ParameterWrapperVM> DisplayParameters => _stagingQueueViewModel.DisplayParameters;

        /// <summary>
        /// Gets the single selected element wrapper when exactly one element is selected.
        /// </summary>
        public ElementTypeWrapperVM? SelectedElement => _stagingQueueViewModel.SelectedElement;

        /// <summary>
        /// Gets whether exactly one element is currently selected.
        /// </summary>
        public bool IsSingleElementSelected => _stagingQueueViewModel.IsSingleElementSelected;

        /// <summary>
        /// Gets or sets the name of the selected element, or a count description if multiple elements are selected.
        /// </summary>
        public string SelectedNameOrCount
        {
            get => _stagingQueueViewModel.SelectedNameOrCount;
            set => _stagingQueueViewModel.SelectedNameOrCount = value;
        }

        /// <summary>
        /// Gets a comma-separated concatenated list of stripped classes for the selected elements.
        /// </summary>
        public string SelectedDisplayClass => _stagingQueueViewModel.SelectedDisplayClass;

        /// <summary>
        /// Gets or sets the aliases string of the selected element, or &lt;Varies&gt; if multiple elements are selected.
        /// </summary>
        public string SelectedAliasesString
        {
            get => _stagingQueueViewModel.SelectedAliasesString;
            set => _stagingQueueViewModel.SelectedAliasesString = value;
        }

        private void RaiseIdentityHeaderStateChanged()
        {
            OnPropertyChanged(nameof(IsSingleElementSelected));
            OnPropertyChanged(nameof(SelectedNameOrCount));
            OnPropertyChanged(nameof(SelectedDisplayClass));
            OnPropertyChanged(nameof(SelectedAliasesString));
        }

        public UIApplication? UIApplication => _uiapp;
        public Document? Document => _doc;
        public IEnumerable<ElementTypeWrapperVM> WrappedElements => AllWrappedElements;

        public IntPtr MainWindowHandle => _uiapp != null ? _uiapp.MainWindowHandle : IntPtr.Zero;

        public IEnumerable<ElementTypeWrapperVM> AllWrappedElements
        {
            get
            {
                var list = new List<ElementTypeWrapperVM>();
                foreach (var qItem in StagingQueue)
                {
                    list.Add(qItem.GetWrapper());
                }
                if (SelectedSource != null)
                {
                    var sourceElements = new List<ElementModel>();
                    foreach (var node in SelectedSource.SourceHierarchy)
                    {
                        GetElementModelsFromHierarchy(node, sourceElements);
                    }
                    foreach (var elem in sourceElements)
                    {
                        list.Add(new ElementTypeWrapperVM(elem));
                    }
                }
                return list;
            }
        }

        public void GetElementModelsFromHierarchy(SourceTreeItemViewModel node, List<ElementModel> list)
        {
            if (node == null) return;
            if (node is StandardElementModel sem && sem.Element != null)
            {
                list.Add(sem.Element);
            }
            if (node.Children != null)
            {
                foreach (var child in node.Children)
                {
                    GetElementModelsFromHierarchy(child, list);
                }
            }
        }

        private WorkspaceMode _activeWorkspace = WorkspaceMode.Idle;


        /// <summary>
        /// Gets or sets the active right pane sub-workspace mode.
        /// </summary>
        public WorkspaceMode ActiveWorkspace
        {
            get => _activeWorkspace;
            set => SetProperty(ref _activeWorkspace, value);
        }

        /// <summary>
        /// Gets or sets the target file path for save actions.
        /// </summary>
        public string? SaveFilePath
        {
            get => _standardsExecutionViewModel.SaveFilePath;
            set => _standardsExecutionViewModel.SaveFilePath = value;
        }

        /// <summary>
        /// Gets whether the save path panel should be active/visible in the UI.
        /// </summary>
        public bool IsSavePathActive => _standardsExecutionViewModel.IsSavePathActive;

        /// <summary>
        /// Gets or sets the search string for batch find-and-replace edits.
        /// </summary>
        public string FindText
        {
            get => _stagingQueueViewModel.FindText;
            set => _stagingQueueViewModel.FindText = value;
        }

        /// <summary>
        /// Gets or sets the replacement string for batch find-and-replace edits.
        /// </summary>
        public string ReplaceText
        {
            get => _stagingQueueViewModel.ReplaceText;
            set => _stagingQueueViewModel.ReplaceText = value;
        }

        /// <summary>
        /// Gets or sets the search scope for batch find-and-replace edits.
        /// </summary>
        public SearchScope FindReplaceScope
        {
            get => _stagingQueueViewModel.FindReplaceScope;
            set => _stagingQueueViewModel.FindReplaceScope = value;
        }

        /// <summary>
        /// Gets the available search scopes for binding in the UI.
        /// </summary>
        public IEnumerable<SearchScope> AvailableSearchScopes => Enum.GetValues(typeof(SearchScope)).Cast<SearchScope>();

        /// <summary>
        /// Gets or sets the name of the selected item in the queue.
        /// </summary>
        public string SelectedItemName
        {
            get => _stagingQueueViewModel.SelectedItemName;
            set => _stagingQueueViewModel.SelectedItemName = value;
        }

        /// <summary>
        /// Gets the error message of the currently selected queue item if it has an error.
        /// </summary>
        public string? SelectedItemErrorMessage => _stagingQueueViewModel.SelectedItemErrorMessage;

        /// <summary>
        /// Gets the collection of staged elements currently selected for editing/diffing.
        /// </summary>
        public ObservableCollection<QueueItemModel> SelectedQueueItems => _stagingQueueViewModel.SelectedQueueItems;

        /// <summary>
        /// Gets the collection of duplicate/diff clusters populated by the comparison engine.
        /// </summary>
        public ObservableCollection<DuplicateClusterModel> ActiveDiffClusters => _stagingQueueViewModel.ActiveDiffClusters;

        /// <summary>
        /// Gets the combined list of results from the last Run Queue execution.
        /// </summary>
        public List<SerializationResultModel> LastExecutionResults { get; } = new List<SerializationResultModel>();

        /// <summary>
        /// Gets the source tree sub-ViewModel.
        /// </summary>
        public StandardsSourceTreeViewModel SourceTreeViewModel => _sourceTreeViewModel;

        /// <summary>
        /// Gets or sets the collection of loaded standard sources (tabs).
        /// </summary>
        public ObservableCollection<ProjectStandardsSourceViewModel> AvailableSources
        {
            get => _sourceTreeViewModel.AvailableSources;
            set => _sourceTreeViewModel.AvailableSources = value;
        }

        /// <summary>
        /// Gets or sets the currently active source tab.
        /// </summary>
        public ProjectStandardsSourceViewModel? SelectedSource
        {
            get => _sourceTreeViewModel.SelectedSource;
            set => _sourceTreeViewModel.SelectedSource = value;
        }

        /// <summary>
        /// Gets or sets the search filter text.
        /// </summary>
        public string SearchText
        {
            get => _sourceTreeViewModel.SearchText;
            set => _sourceTreeViewModel.SearchText = value;
        }



        /// <summary>
        /// Gets or sets whether to update loaded families during queue execution.
        /// </summary>
        public bool UpdateFamilies
        {
            get => _standardsExecutionViewModel.UpdateFamilies;
            set => _standardsExecutionViewModel.UpdateFamilies = value;
        }

        /// <summary>
        /// Gets or sets whether to process nested families recursively.
        /// </summary>
        public bool ProcessNestedRecursive
        {
            get => _standardsExecutionViewModel.ProcessNestedRecursive;
            set => _standardsExecutionViewModel.ProcessNestedRecursive = value;
        }

        /// <summary>
        /// Gets or sets whether to purge unused style types in family documents.
        /// </summary>
        public bool PurgeUnusedStyleTypes
        {
            get => _standardsExecutionViewModel.PurgeUnusedStyleTypes;
            set => _standardsExecutionViewModel.PurgeUnusedStyleTypes = value;
        }

        /// <summary>
        /// Gets or sets the category filter for family updates.
        /// </summary>
        public string CategoryFilter
        {
            get => _standardsExecutionViewModel.CategoryFilter;
            set => _standardsExecutionViewModel.CategoryFilter = value;
        }

        /// <summary>
        /// Gets the list of available category filters.
        /// </summary>
        public List<string> AvailableCategoryFilters => _standardsExecutionViewModel.AvailableCategoryFilters;

        public List<Document>? MockOpenDocuments { get; set; }
        public Func<SelectRevitDocumentViewModel, bool?>? ShowDocumentSelectionDialog { get; set; }
        public Func<Synthetic.Shared.UI.SingleItemSelectionViewModel<QueueItemModel>, bool?>? ShowMergeDialog { get; set; }

        #region Commands
        public ICommand AddFileSourceCommand => _sourceTreeViewModel.AddFileSourceCommand;
        public ICommand AddRevitModelCommand => _sourceTreeViewModel.AddRevitModelCommand;
        public ICommand CloseSourceCommand => _sourceTreeViewModel.CloseSourceCommand;
        public ICommand EnforceCommand => _standardsExecutionViewModel.EnforceCommand;
        public ICommand SaveCommand => _standardsExecutionViewModel.SaveCommand;
        public ICommand SaveAndEnforceCommand => _standardsExecutionViewModel.SaveAndEnforceCommand;
        public ICommand BrowseSavePathCommand => _standardsExecutionViewModel.BrowseSavePathCommand;
        public ICommand PushToQueueCommand => _stagingQueueViewModel.PushToQueueCommand;
        public ICommand RemoveFromQueueCommand => _stagingQueueViewModel.RemoveFromQueueCommand;
        public ICommand MergeQueueCommand => _stagingQueueViewModel.MergeQueueCommand;
        public ICommand EditCommand => _stagingQueueViewModel.EditCommand;
        public ICommand DiffCommand => _stagingQueueViewModel.DiffCommand;
        public ICommand RunQueueCommand => _standardsExecutionViewModel.RunQueueCommand;
        public ICommand BatchFindReplaceCommand => _stagingQueueViewModel.BatchFindReplaceCommand;
        public ICommand ApplyEditsCommand => _stagingQueueViewModel.ApplyEditsCommand;
        public ICommand CancelEditsCommand => _stagingQueueViewModel.CancelEditsCommand;
        public ICommand ResolveConflictCommand => _stagingQueueViewModel.ResolveConflictCommand;
        #endregion

        /// <summary>
        /// Initializes a new instance of the <see cref="ProjectStandardsDashboardViewModel"/> class for live Revit environment.
        /// </summary>
        public ProjectStandardsDashboardViewModel(
            UIApplication uiapp, 
            IFileDialogService dialogService, 
            IStandardsExportService exportService, 
            StandardsSettings? settings,
            IUserPromptService userPromptService,
            IFindReplaceService findReplaceService,
            IStandardsExtractionOrchestrator orchestrator,
            IPocoIdentityService pocoIdentityService,
            IDiffEngine<IEnumerable<ObjectModel>, Document> diffEngine,
            IStandardSerializationEngine serializationEngine,
            IStandardsExecutionPipeline pipeline)
        {
            _uiapp = uiapp ?? throw new ArgumentNullException(nameof(uiapp));
            _doc = uiapp.ActiveUIDocument?.Document;
            _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
            _exportService = exportService ?? throw new ArgumentNullException(nameof(exportService));
            _userPromptService = userPromptService ?? throw new ArgumentNullException(nameof(userPromptService));
            _findReplaceService = findReplaceService ?? throw new ArgumentNullException(nameof(findReplaceService));
            _serializationEngine = serializationEngine ?? throw new ArgumentNullException(nameof(serializationEngine));
            _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
            _pocoIdentityService = pocoIdentityService ?? throw new ArgumentNullException(nameof(pocoIdentityService));
            _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
            SummaryDisplayService = new WindowsSummaryDisplayService();
            Instance = this;

            _sourceTreeViewModel = new StandardsSourceTreeViewModel(
                this,
                uiapp,
                uiapp?.ActiveUIDocument?.Document,
                dialogService,
                orchestrator,
                serializationEngine);

            _stagingQueueViewModel = new StagingQueueViewModel(this, _pocoIdentityService, diffEngine);
            _standardsExecutionViewModel = new StandardsExecutionPipelineViewModel(this, _pipeline);

            RegisterPropertyChangedHandlers();



            ShowDocumentSelectionDialog = vm =>
            {
                var window = new Views.SelectRevitDocumentWindow(vm);
                try
                {
                    if (System.Windows.Application.Current?.MainWindow != null)
                    {
                        window.Owner = System.Windows.Application.Current.MainWindow;
                    }
                }
                catch {}
                return window.ShowDialog();
            };

            ShowMergeDialog = vm =>
            {
                try
                {
                    IntPtr handle = _uiapp != null ? _uiapp.MainWindowHandle : IntPtr.Zero;
                    var view = new Synthetic.Shared.UI.SingleItemSelectionWindow(handle)
                    {
                        DataContext = vm
                    };

                    try
                    {
                        if (System.Windows.Application.Current?.MainWindow != null)
                        {
                            view.Owner = System.Windows.Application.Current.MainWindow;
                        }
                    }
                    catch {}

                    return view.ShowDialog();
                }
                catch (Exception ex)
                {
                    _userPromptService.ShowMessage($"Error opening Merge Dialog:\n{ex.Message}\n\n{ex.StackTrace}", "Error");
                    return false;
                }
            };



            Initialize(settings);
        }



        /// <summary>
        /// Initializes a new instance of the <see cref="ProjectStandardsDashboardViewModel"/> class for headless testing.
        /// </summary>
        public ProjectStandardsDashboardViewModel(
            Document doc, 
            IFileDialogService dialogService, 
            IStandardsExportService exportService, 
            StandardsSettings? settings,
            IUserPromptService userPromptService,
            IFindReplaceService findReplaceService,
            IStandardsExtractionOrchestrator orchestrator,
            IPocoIdentityService pocoIdentityService,
            IDiffEngine<IEnumerable<ObjectModel>, Document> diffEngine,
            IStandardSerializationEngine serializationEngine,
            IStandardsExecutionPipeline pipeline)
        {
            _doc = doc;
            _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
            _exportService = exportService ?? throw new ArgumentNullException(nameof(exportService));
            _userPromptService = userPromptService ?? throw new ArgumentNullException(nameof(userPromptService));
            _findReplaceService = findReplaceService ?? throw new ArgumentNullException(nameof(findReplaceService));
            _serializationEngine = serializationEngine ?? throw new ArgumentNullException(nameof(serializationEngine));
            _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
            _pocoIdentityService = pocoIdentityService ?? throw new ArgumentNullException(nameof(pocoIdentityService));
            _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
            SummaryDisplayService = new NoOpSummaryDisplayService();
            Instance = this;

            _sourceTreeViewModel = new StandardsSourceTreeViewModel(
                this,
                null,
                doc,
                dialogService,
                orchestrator,
                serializationEngine);

            _stagingQueueViewModel = new StagingQueueViewModel(this, _pocoIdentityService, diffEngine);
            _standardsExecutionViewModel = new StandardsExecutionPipelineViewModel(this, _pipeline);

            RegisterPropertyChangedHandlers();



            ShowDocumentSelectionDialog = vm =>
            {
                foreach (var docItem in vm.OpenDocuments)
                {
                    docItem.IsSelected = true;
                }
                return true;
            };

            ShowMergeDialog = vm =>
            {
                vm.SelectedItem = vm.Items?.FirstOrDefault();
                return true;
            };

            Initialize(settings);
        }





        /// <summary>
        /// Registers the external event and handler to run database transactions on the Revit API thread.
        /// </summary>
        public void SetExternalEvent(ExternalEvent externalEvent, ProjectStandardsExternalEventHandler eventHandler)
        {
            _externalEvent = externalEvent;
            _eventHandler = eventHandler;
            _eventHandler.SetViewModel(this);
        }

        private void Initialize(StandardsSettings? settings)
        {
            Instance = this;
            if (settings == null && _doc != null)
            {
                try
                {
                    // Query StandardsSettings via SettingsManager
                    settings = SettingsManager.Get<StandardsSettings>(_doc);
                }
                catch (Exception)
                {
                    // Catch setup issues gracefully during headless test runs
                }
            }

            _settings = settings;

            if (settings != null && !string.IsNullOrEmpty(settings.StandardsFilePath))
            {
                _sourceTreeViewModel.LoadFileSource(settings.StandardsFilePath, "Default Firm Standard");
                // Preselect all checkboxes for the default firm standard on launch
                if (SelectedSource != null)
                {
                    foreach (var group in SelectedSource.SourceHierarchy)
                    {
                        group.IsChecked = true;
                    }
                }
            }

            if (_doc != null)
            {
                string documentTitle = "";
                try
                {
                    var titleProp = _doc.GetType().GetProperty("Title");
                    documentTitle = titleProp?.GetValue(_doc) as string ?? "";
                }
                catch (Exception) { }

                bool isModelInCloud = false;
                bool isWorkshared = false;
                string? centralModelPathString = null;
                string? localPathName = null;

                try
                {
                    var prop = _doc.GetType().GetProperty("IsModelInCloud");
                    if (prop != null)
                    {
                        isModelInCloud = (bool)prop.GetValue(_doc)!;
                    }
                }
                catch (Exception) { }

                try
                {
                    var prop = _doc.GetType().GetProperty("IsWorkshared");
                    if (prop != null)
                    {
                        isWorkshared = (bool)prop.GetValue(_doc)!;
                    }
                }
                catch (Exception) { }

                if (isWorkshared)
                {
                    try
                    {
                        var getPathMethod = _doc.GetType().GetMethod("GetWorksharingCentralModelPath");
                        if (getPathMethod != null)
                        {
                            object? centralModelPath = getPathMethod.Invoke(_doc, null);
                            if (centralModelPath != null)
                            {
                                var modelPathUtilsType = AppDomain.CurrentDomain.GetAssemblies()
                                    .Select(a => a.GetType("Autodesk.Revit.DB.ModelPathUtils"))
                                    .FirstOrDefault(t => t != null);
                                if (modelPathUtilsType != null)
                                {
                                    var convertMethod = modelPathUtilsType.GetMethod("ConvertModelPathToUserVisiblePath");
                                    if (convertMethod != null)
                                    {
                                        centralModelPathString = convertMethod.Invoke(null, new object[] { centralModelPath }) as string;
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception) { }
                }

                try
                {
                    var prop = _doc.GetType().GetProperty("PathName");
                    if (prop != null)
                    {
                        localPathName = prop.GetValue(_doc) as string;
                    }
                }
                catch (Exception) { }

                string fallbackDirectory = _sourceTreeViewModel.GetRevitLocalFileSaveLocation();
                SaveFilePath = PathResolutionUtility.GetDefaultSavePath(documentTitle, isModelInCloud, isWorkshared, centralModelPathString, localPathName, fallbackDirectory);
            }
            else
            {
                // Fallback directly to the Revit local file save location when no document is active
                string fallbackDirectory = _sourceTreeViewModel.GetRevitLocalFileSaveLocation();
                SaveFilePath = Path.Combine(fallbackDirectory, "Project Standards.json");
            }
        }



        private bool CanExecuteActions(object parameter) => SelectedSource != null;

        private bool CanExecuteQueueActions(object parameter) => StagingQueue.Count > 0;







        public void ReplaceReferences(ObjectModel oldElement, string oldName, string newName)
        {
            var nameAndAliases = new List<string> { oldName };
            if (oldElement is ElementModel oldEl && oldEl.Aliases != null)
            {
                nameAndAliases.AddRange(oldEl.Aliases);
            }

            foreach (var qItem in StagingQueue)
            {
                var el = qItem.TargetModel;
                if (el == oldElement) continue;

                if (el is ElementModel elModel && elModel.Parameters != null)
                {
                    foreach (var param in elModel.Parameters)
                    {
                        if (param.StorageType == "ElementId" && nameAndAliases.Contains(param.Value, StringComparer.OrdinalIgnoreCase))
                        {
                            param.Value = newName;
                        }
                    }
                }

                if (el is HostObjTypeModel hostObj && hostObj.Structure != null && hostObj.Structure.Layers != null)
                {
                    foreach (var layer in hostObj.Structure.Layers)
                    {
                        if (layer.MaterialId != null && nameAndAliases.Contains(layer.MaterialId.Name, StringComparer.OrdinalIgnoreCase))
                        {
                            layer.MaterialId.Name = newName;
                            layer.MaterialId.Id = 0;
                            layer.MaterialId.UniqueId = null;
                        }
                    }
                }

                if (el is MaterialModel mat)
                {
                    if (mat.SurfaceForegroundPatternId != null && nameAndAliases.Contains(mat.SurfaceForegroundPatternId.Name, StringComparer.OrdinalIgnoreCase))
                    {
                        mat.SurfaceForegroundPatternId.Name = newName;
                        mat.SurfaceForegroundPatternId.Id = 0;
                        mat.SurfaceForegroundPatternId.UniqueId = null;
                    }
                    if (mat.SurfaceBackgroundPatternId != null && nameAndAliases.Contains(mat.SurfaceBackgroundPatternId.Name, StringComparer.OrdinalIgnoreCase))
                    {
                        mat.SurfaceBackgroundPatternId.Name = newName;
                        mat.SurfaceBackgroundPatternId.Id = 0;
                        mat.SurfaceBackgroundPatternId.UniqueId = null;
                    }
                    if (mat.CutForegroundPatternId != null && nameAndAliases.Contains(mat.CutForegroundPatternId.Name, StringComparer.OrdinalIgnoreCase))
                    {
                        mat.CutForegroundPatternId.Name = newName;
                        mat.CutForegroundPatternId.Id = 0;
                        mat.CutForegroundPatternId.UniqueId = null;
                    }
                    if (mat.CutBackgroundPatternId != null && nameAndAliases.Contains(mat.CutBackgroundPatternId.Name, StringComparer.OrdinalIgnoreCase))
                    {
                        mat.CutBackgroundPatternId.Name = newName;
                        mat.CutBackgroundPatternId.Id = 0;
                        mat.CutBackgroundPatternId.UniqueId = null;
                    }
                    if (mat.AppearanceAssetId != null && nameAndAliases.Contains(mat.AppearanceAssetId.Name, StringComparer.OrdinalIgnoreCase))
                    {
                        mat.AppearanceAssetId.Name = newName;
                        mat.AppearanceAssetId.Id = 0;
                        mat.AppearanceAssetId.UniqueId = null;
                    }
                }
            }
        }



        public List<StandardElementModel> GetCheckedElements()
        {
            var list = new List<StandardElementModel>();
            if (SelectedSource == null) return list;

            foreach (var group in SelectedSource.SourceHierarchy)
            {
                foreach (var classNode in group.Children.OfType<StandardClassModel>())
                {
                    foreach (var elemNode in classNode.Children.OfType<StandardElementModel>())
                    {
                        if (elemNode.IsChecked == true)
                        {
                            list.Add(elemNode);
                        }
                    }
                }
            }

            return list;
        }






        public void ReplaceQueueReferences(List<QueueItemModel> oldElements, string newName)
        {
            var nameAndAliases = new List<string>();
            foreach (var oldEl in oldElements)
            {
                nameAndAliases.Add(oldEl.Name);
                if (oldEl.Model is ElementModel oldElementModel && oldElementModel.Aliases != null)
                {
                    nameAndAliases.AddRange(oldElementModel.Aliases);
                }
            }

            void UpdateReferencedId(ElementIdModel? idModel)
            {
                if (idModel != null && nameAndAliases.Contains(idModel.Name, StringComparer.OrdinalIgnoreCase))
                {
                    idModel.Name = newName;
                    idModel.Id = 0;
                    idModel.UniqueId = null;
                }
            }

            foreach (var qItem in StagingQueue)
            {
                var el = qItem.TargetModel;
                if (oldElements.Contains(qItem)) continue;

                if (el is ElementModel elModel && elModel.Parameters != null)
                {
                    foreach (var param in elModel.Parameters)
                    {
                        if (param.StorageType == "ElementId" && nameAndAliases.Contains(param.Value, StringComparer.OrdinalIgnoreCase))
                        {
                            param.Value = newName;
                        }
                    }
                }

                if (el is HostObjTypeModel hostObj && hostObj.Structure != null && hostObj.Structure.Layers != null)
                {
                    foreach (var layer in hostObj.Structure.Layers)
                    {
                        UpdateReferencedId(layer.MaterialId);
                    }
                }

                if (el is MaterialModel mat)
                {
                    UpdateReferencedId(mat.SurfaceForegroundPatternId);
                    UpdateReferencedId(mat.SurfaceBackgroundPatternId);
                    UpdateReferencedId(mat.CutForegroundPatternId);
                    UpdateReferencedId(mat.CutBackgroundPatternId);
                    UpdateReferencedId(mat.AppearanceAssetId);
                }
            }
        }

        private void RegisterPropertyChangedHandlers()
        {
            _sourceTreeViewModel.PropertyChanged += OnSubViewModelPropertyChanged;
            _stagingQueueViewModel.PropertyChanged += OnSubViewModelPropertyChanged;
            _standardsExecutionViewModel.PropertyChanged += OnSubViewModelPropertyChanged;
        }

        private void OnSubViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (sender == _sourceTreeViewModel)
            {
                if (e.PropertyName == nameof(StandardsSourceTreeViewModel.AvailableSources) ||
                    e.PropertyName == nameof(StandardsSourceTreeViewModel.SelectedSource) ||
                    e.PropertyName == nameof(StandardsSourceTreeViewModel.SearchText))
                {
                    OnPropertyChanged(e.PropertyName);
                }
            }
            else if (sender == _stagingQueueViewModel)
            {
                if (e.PropertyName == nameof(StagingQueueViewModel.StagingQueue) ||
                    e.PropertyName == nameof(StagingQueueViewModel.StagingQueueView) ||
                    e.PropertyName == nameof(StagingQueueViewModel.SelectedQueueItems) ||
                    e.PropertyName == nameof(StagingQueueViewModel.SelectedNameOrCount) ||
                    e.PropertyName == nameof(StagingQueueViewModel.SelectedDisplayClass) ||
                    e.PropertyName == nameof(StagingQueueViewModel.SelectedAliasesString) ||
                    e.PropertyName == nameof(StagingQueueViewModel.SelectedElement) ||
                    e.PropertyName == nameof(StagingQueueViewModel.IsSingleElementSelected) ||
                    e.PropertyName == nameof(StagingQueueViewModel.SelectedItemErrorMessage))
                {
                    OnPropertyChanged(e.PropertyName);
                }
            }
            else if (sender == _standardsExecutionViewModel)
            {
                if (e.PropertyName == nameof(StandardsExecutionPipelineViewModel.UpdateFamilies) ||
                    e.PropertyName == nameof(StandardsExecutionPipelineViewModel.ProcessNestedRecursive) ||
                    e.PropertyName == nameof(StandardsExecutionPipelineViewModel.PurgeUnusedStyleTypes) ||
                    e.PropertyName == nameof(StandardsExecutionPipelineViewModel.CategoryFilter) ||
                    e.PropertyName == nameof(StandardsExecutionPipelineViewModel.AvailableCategoryFilters) ||
                    e.PropertyName == nameof(StandardsExecutionPipelineViewModel.SaveFilePath) ||
                    e.PropertyName == nameof(StandardsExecutionPipelineViewModel.IsSavePathActive))
                {
                    OnPropertyChanged(e.PropertyName);
                }
            }
        }

        public void RunQueueInternal()
        {
            _standardsExecutionViewModel.RunQueueInternal();
        }
    }

    /// <summary>
    /// Event handler to execute Revit database writes on the Revit API thread from the modeless window.
    /// </summary>
    public class ProjectStandardsExternalEventHandler : IExternalEventHandler
    {
        private ProjectStandardsDashboardViewModel? _viewModel;

        /// <summary>
        /// Sets the view model instance.
        /// </summary>
        public void SetViewModel(ProjectStandardsDashboardViewModel viewModel)
        {
            _viewModel = viewModel;
        }

        /// <summary>
        /// Executes the queued run-queue logic.
        /// </summary>
        public void Execute(UIApplication app)
        {
            if (_viewModel != null)
            {
                try
                {
                    _viewModel.RunQueueInternal();
                }
                catch (Exception ex)
                {
                    if (!ProgressCoordinator.SuppressUI)
                    {
                        _viewModel._userPromptService.ShowMessage(
                            $"Error running queue: {ex.Message}",
                            "Revit API Execution Error");
                    }
                }
            }
        }

        /// <summary>
        /// Gets the handler name.
        /// </summary>
        public string GetName()
        {
            return "Project Standards Dashboard Modeless Handler";
        }
    }
}

```

### File: StandardsManagement/ViewModels/ProjectStandardsSourceViewModel.cs
```csharp
using System;
using System.Collections.ObjectModel;
using Synthetic.Shared.UI;

namespace Synthetic.Modules.StandardsManagement.ViewModels
{
    /// <summary>
    /// Represents a tabbed source standard file or Revit model context loaded inside the Project Standards workspace.
    /// </summary>
    public class ProjectStandardsSourceViewModel : ViewModelBase
    {
        private string _displayName = string.Empty;
        private string _sourcePath = string.Empty;
        private bool _isRevitSource;


        /// <summary>
        /// Gets or sets the display name shown on the source tab.
        /// </summary>
        public string DisplayName
        {
            get => _displayName;
            set => SetProperty(ref _displayName, value);
        }

        /// <summary>
        /// Gets or sets the absolute path or identifier of the source data.
        /// </summary>
        public string SourcePath
        {
            get => _sourcePath;
            set => SetProperty(ref _sourcePath, value);
        }

        /// <summary>
        /// Gets or sets a value indicating whether this source is an active Revit model.
        /// </summary>
        public bool IsRevitSource
        {
            get => _isRevitSource;
            set => SetProperty(ref _isRevitSource, value);
        }



        /// <summary>
        /// Gets the hierarchical tree view dataset for this source.
        /// </summary>
        public ObservableCollection<StandardGroupModel> SourceHierarchy { get; } = new ObservableCollection<StandardGroupModel>();

        /// <summary>
        /// Gets the command to select all elements in the source hierarchy.
        /// </summary>
        public System.Windows.Input.ICommand SelectAllCommand { get; }

        /// <summary>
        /// Gets the command to deselect all elements in the source hierarchy.
        /// </summary>
        public System.Windows.Input.ICommand SelectNoneCommand { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="ProjectStandardsSourceViewModel"/> class.
        /// </summary>
        public ProjectStandardsSourceViewModel()
        {
            SelectAllCommand = new RelayCommand(ExecuteSelectAll);
            SelectNoneCommand = new RelayCommand(ExecuteSelectNone);
        }

        private void ExecuteSelectAll(object parameter)
        {
            foreach (var group in SourceHierarchy)
            {
                group.IsChecked = true;
            }
        }

        private void ExecuteSelectNone(object parameter)
        {
            foreach (var group in SourceHierarchy)
            {
                group.IsChecked = false;
            }
        }
    }
}
```

### File: StandardsManagement/ViewModels/QueueItemModel.cs
```csharp
using System;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;
using Synthetic.Shared.UI;

namespace Synthetic.Modules.StandardsManagement.ViewModels
{
    /// <summary>
    /// Wrapper ViewModel for deep-copied elements staged in the Staging Queue.
    /// </summary>
    public class QueueItemModel : ViewModelBase
    {
        private bool _willEnforce;
        private bool _willSave;
        private bool _isEdited;
        private bool _isDiffed;

        /// <summary>
        /// Gets the immutable historical snapshot at the moment of queuing.
        /// </summary>
        public ObjectModel BaselineModel { get; }

        /// <summary>
        /// Gets the active, mutable state of the staged element.
        /// </summary>
        public ObjectModel TargetModel { get; private set; }

        /// <summary>
        /// Gets the underlying cloned standard object (redirects to TargetModel).
        /// </summary>
        public ObjectModel Model => TargetModel;

        /// <summary>
        /// Gets the display name of the staged element.
        /// </summary>
        public string Name
        {
            get
            {
                if (Model is ElementModel elementModel)
                {
                    return elementModel.Name ?? string.Empty;
                }
                return string.Empty;
            }
        }

        /// <summary>
        /// Gets the class name / category of the element used for UI grouping.
        /// </summary>
        public string ClassName
        {
            get
            {
                if (Model is ElementModel elementModel)
                {
                    return elementModel.Class ?? "Unknown Class";
                }
                return Model.GetType().Name;
            }
        }

        /// <summary>
        /// Gets the category of the element.
        /// </summary>
        public string Category
        {
            get
            {
                if (Model is ElementModel elementModel)
                {
                    return elementModel.Category ?? string.Empty;
                }
                return string.Empty;
            }
        }

        /// <summary>
        /// Gets or sets whether this item will be enforced in the Revit database.
        /// </summary>
        public bool WillEnforce
        {
            get => _willEnforce;
            set => SetProperty(ref _willEnforce, value);
        }

        /// <summary>
        /// Gets or sets whether this item will be saved to a JSON standards file.
        /// </summary>
        public bool WillSave
        {
            get => _willSave;
            set => SetProperty(ref _willSave, value);
        }

        /// <summary>
        /// Gets or sets whether this item has been edited.
        /// </summary>
        public bool IsEdited
        {
            get => _isEdited;
            set => SetProperty(ref _isEdited, value);
        }

        /// <summary>
        /// Gets or sets whether this item has been diffed.
        /// </summary>
        public bool IsDiffed
        {
            get => _isDiffed;
            set => SetProperty(ref _isDiffed, value);
        }

        private string? _errorMessage;

        /// <summary>
        /// Gets or sets the execution error message if this item failed database or file operations.
        /// </summary>
        public string? ErrorMessage
        {
            get => _errorMessage;
            set
            {
                if (SetProperty(ref _errorMessage, value))
                {
                    OnPropertyChanged(nameof(HasError));
                }
            }
        }

        /// <summary>
        /// Gets a value indicating whether this item has an execution error.
        /// </summary>
        public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

        private string? _dependencyOrigin;

        /// <summary>
        /// Gets or sets the name of the parent element that triggered the harvesting of this dependency.
        /// </summary>
        public string? DependencyOrigin
        {
            get => _dependencyOrigin;
            set
            {
                if (SetProperty(ref _dependencyOrigin, value))
                {
                    OnPropertyChanged(nameof(IsDependency));
                }
            }
        }

        /// <summary>
        /// Gets a value indicating whether this item was harvested as a dependency.
        /// </summary>
        public bool IsDependency => !string.IsNullOrEmpty(DependencyOrigin);

        /// <summary>
        /// Initializes a new instance of the <see cref="QueueItemModel"/> class with explicit execution flags.
        /// </summary>
        /// <param name="model">The original model POCO.</param>
        /// <param name="willEnforce">True to enforce this item.</param>
        /// <param name="willSave">True to save this item.</param>
        public QueueItemModel(ObjectModel model, bool willEnforce, bool willSave)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));

            // Deep clone using the runtime type of the model to preserve subclasses
            string json = Newtonsoft.Json.JsonConvert.SerializeObject(model, Newtonsoft.Json.Formatting.None);

            BaselineModel = (ObjectModel)Newtonsoft.Json.JsonConvert.DeserializeObject(json, model.GetType())!;
            TargetModel = (ObjectModel)Newtonsoft.Json.JsonConvert.DeserializeObject(json, model.GetType())!;

            WillEnforce = willEnforce;
            WillSave = willSave;
        }

        /// <summary>
        /// Generates a fresh ElementTypeWrapperVM on-the-fly.
        /// </summary>
        public ElementTypeWrapperVM GetWrapper()
        {
            if (TargetModel is ElementModel targetElement)
            {
                var baselineElement = BaselineModel as ElementModel;
                return new ElementTypeWrapperVM(targetElement, baselineElement);
            }
            throw new InvalidOperationException("Model is not an ElementModel.");
        }

        /// <summary>
        /// Reverts the mutable TargetModel to match the immutable BaselineModel.
        /// </summary>
        public void Revert()
        {
            string json = Newtonsoft.Json.JsonConvert.SerializeObject(BaselineModel, Newtonsoft.Json.Formatting.None);
            TargetModel = (ObjectModel)Newtonsoft.Json.JsonConvert.DeserializeObject(json, BaselineModel.GetType())!;
            OnPropertyChanged(nameof(TargetModel));
            OnPropertyChanged(nameof(Model));
            OnPropertyChanged(nameof(Name));
        }

        /// <summary>
        /// Raises a property changed notification for a given property.
        /// </summary>
        public void RaisePropertyChanged(string propertyName)
        {
            OnPropertyChanged(propertyName);
        }
    }
}
```

### File: StandardsManagement/ViewModels/SelectRevitDocumentViewModel.cs
```csharp
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Synthetic.Shared.UI;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Synthetic.RevitDOM.Operations.Standards;

namespace Synthetic.Modules.StandardsManagement.ViewModels
{
    public class RevitDocumentItem : ViewModelBase
    {
        private bool _isSelected;
        public string Title { get; set; } = string.Empty;
        public Document Document { get; set; }
        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }
    }

    public class SelectRevitDocumentViewModel : ViewModelBase
    {
        private bool _scanFamilies = false;
        private bool _includeNestedFamilies = false;

        public ObservableCollection<RevitDocumentItem> OpenDocuments { get; } = new ObservableCollection<RevitDocumentItem>();
        
        /// <summary>
        /// Gets the hierarchical checkable standards filter tree.
        /// </summary>
        public ObservableCollection<StandardGroupModel> FilterHierarchy { get; }

        /// <summary>
        /// Gets or sets a value indicating whether families should be scanned.
        /// </summary>
        public bool ScanFamilies
        {
            get => _scanFamilies;
            set
            {
                if (SetProperty(ref _scanFamilies, value))
                {
                    if (!value)
                    {
                        IncludeNestedFamilies = false;
                    }
                }
            }
        }

        /// <summary>
        /// Gets or sets a value indicating whether nested families should be included in family scanning.
        /// </summary>
        public bool IncludeNestedFamilies
        {
            get => _includeNestedFamilies;
            set => SetProperty(ref _includeNestedFamilies, value);
        }

        public SelectRevitDocumentViewModel(UIApplication? uiapp, List<Document>? mockDocs = null)
        {
            FilterHierarchy = StandardsHierarchyUtility.GenerateTemplateHierarchy();
            
            // Check all nodes by default on load
            foreach (var group in FilterHierarchy)
            {
                group.IsChecked = true;
            }

            if (mockDocs != null)
            {
                foreach (var doc in mockDocs)
                {
                    OpenDocuments.Add(new RevitDocumentItem { Title = doc.Title, Document = doc, IsSelected = false });
                }
            }
            else if (uiapp != null)
            {
                foreach (Document doc in uiapp.Application.Documents)
                {
                    OpenDocuments.Add(new RevitDocumentItem { Title = doc.Title, Document = doc, IsSelected = false });
                }
            }
        }

        public List<Document> SelectedDocuments => OpenDocuments
            .Where(d => d.IsSelected)
            .Select(d => d.Document)
            .ToList();

        /// <summary>
        /// Gets the list of names of all checked classes in the filter hierarchy.
        /// </summary>
        public List<string> SelectedFamilyGroupings => FilterHierarchy
            .SelectMany(g => g.Children.OfType<StandardClassModel>())
            .Where(c => c.IsChecked == true)
            .Select(c => c.Name)
            .ToList();
    }
}
```

### File: StandardsManagement/ViewModels/SourceTreeItemViewModel.cs
```csharp
using System;
using System.Collections.ObjectModel;
using Synthetic.Shared.UI;

namespace Synthetic.Modules.StandardsManagement.ViewModels
{
    /// <summary>
    /// Base ViewModel class for items in the hierarchical standards selection TreeView.
    /// Manages cascading checked/unchecked/indeterminate states recursively.
    /// </summary>
    public abstract class SourceTreeItemViewModel : ViewModelBase
    {
        private string _name = string.Empty;
        private bool? _isChecked = false;
        private SourceTreeItemViewModel? _parent;

        /// <summary>
        /// Gets or sets the display name of the tree node.
        /// </summary>
        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }

        /// <summary>
        /// Gets or sets the checked state of the node.
        /// Supports nullable bool (true = Checked, false = Unchecked, null = Indeterminate).
        /// </summary>
        public bool? IsChecked
        {
            get => _isChecked;
            set => SetIsChecked(value, true, true);
        }

        private bool _isVisible = true;
        private bool _isExpanded;

        /// <summary>
        /// Gets or sets a value indicating whether this tree node is visible.
        /// </summary>
        public bool IsVisible
        {
            get => _isVisible;
            set => SetProperty(ref _isVisible, value);
        }

        /// <summary>
        /// Gets or sets a value indicating whether this tree node is expanded.
        /// </summary>
        public bool IsExpanded
        {
            get => _isExpanded;
            set => SetProperty(ref _isExpanded, value);
        }

        /// <summary>
        /// Gets or sets the parent node reference.
        /// </summary>
        public SourceTreeItemViewModel? Parent
        {
            get => _parent;
            set => _parent = value;
        }

        /// <summary>
        /// Gets the children of this node.
        /// </summary>
        public ObservableCollection<SourceTreeItemViewModel> Children { get; } = new ObservableCollection<SourceTreeItemViewModel>();

        /// <summary>
        /// Updates the checkbox state, propagating updates to parents or children recursively.
        /// </summary>
        public void SetIsChecked(bool? value, bool updateChildren, bool updateParent)
        {
            if (_isChecked == value) return;

            _isChecked = value;
            OnPropertyChanged(nameof(IsChecked));

            if (updateChildren && value.HasValue)
            {
                foreach (var child in Children)
                {
                    child.SetIsChecked(value, true, false);
                }
            }

            if (updateParent && Parent != null)
            {
                Parent.VerifyCheckedState();
            }
        }

        /// <summary>
        /// Examines child states to update this parent node's checked state accordingly.
        /// </summary>
        public void VerifyCheckedState()
        {
            bool? state = null;

            if (Children.Count > 0)
            {
                bool allChecked = true;
                bool allUnchecked = true;

                foreach (var child in Children)
                {
                    if (child.IsChecked == true)
                    {
                        allUnchecked = false;
                    }
                    else if (child.IsChecked == false)
                    {
                        allChecked = false;
                    }
                    else
                    {
                        allChecked = false;
                        allUnchecked = false;
                    }
                }

                if (allChecked) state = true;
                else if (allUnchecked) state = false;
                else state = null; // Indeterminate
            }

            SetIsChecked(state, false, true);
        }
    }
}
```

### File: StandardsManagement/ViewModels/StagingQueueViewModel.cs
```csharp
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows.Data;
using System.Windows.Input;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;
using Synthetic.RevitDOM.Operations.Standards;
using Synthetic.Shared.UI;
using Synthetic.RevitDOM.Operations.Merge;
using Synthetic.RevitDOM.Operations.Standards;
using Synthetic.RevitDOM.Operations.Diffing;
using Autodesk.Revit.DB;

namespace Synthetic.Modules.StandardsManagement.ViewModels
{
    /// <summary>
    /// ViewModel that manages the staging queue collection, queue manipulation commands,
    /// and the staging elements editing/batch find-replace workflow.
    /// </summary>
    public class StagingQueueViewModel : ViewModelBase
    {
        private readonly IProjectStandardsDashboard _parent;
        private readonly IPocoIdentityService _pocoIdentityService;
        private readonly IDiffEngine<IEnumerable<ObjectModel>, Document> _diffEngine;

        private ObservableCollection<QueueItemModel> _stagingQueue = new ObservableCollection<QueueItemModel>();
        private ObservableCollection<QueueItemModel> _selectedQueueItems = new ObservableCollection<QueueItemModel>();
        private ObservableCollection<ParameterWrapperVM> _displayParameters = new ObservableCollection<ParameterWrapperVM>();
        private ObservableCollection<DuplicateClusterModel> _activeDiffClusters = new ObservableCollection<DuplicateClusterModel>();

        private List<ElementTypeWrapperVM> _activeWrappers = new List<ElementTypeWrapperVM>();
        private class QueueItemStateBackup
        {
            public bool WillEnforce { get; set; }
            public bool WillSave { get; set; }
            public bool IsEdited { get; set; }
            public bool IsDiffed { get; set; }
        }
        private Dictionary<QueueItemModel, QueueItemStateBackup> _originalIntents = new Dictionary<QueueItemModel, QueueItemStateBackup>();

        private string _lastSelectedName = string.Empty;
        private ElementTypeWrapperVM? _subscribedWrapper;

        private string _findText = string.Empty;
        private string _replaceText = string.Empty;
        private SearchScope _findReplaceScope = SearchScope.Both;

        /// <summary>
        /// Gets the staging queue collection.
        /// </summary>
        public ObservableCollection<QueueItemModel> StagingQueue => _stagingQueue;

        /// <summary>
        /// Gets the grouped collection view of the staging queue.
        /// </summary>
        public ICollectionView StagingQueueView { get; }

        /// <summary>
        /// Gets the collection of staged elements currently selected for editing/diffing.
        /// </summary>
        public ObservableCollection<QueueItemModel> SelectedQueueItems => _selectedQueueItems;

        /// <summary>
        /// Gets the parameters collection currently displayed in the property grid.
        /// </summary>
        public ObservableCollection<ParameterWrapperVM> DisplayParameters => _displayParameters;

        /// <summary>
        /// Gets the duplicate and diff clusters populated by the comparison engine.
        /// </summary>
        public ObservableCollection<DuplicateClusterModel> ActiveDiffClusters => _activeDiffClusters;

        public ICommand PushToQueueCommand { get; }
        public ICommand RemoveFromQueueCommand { get; }
        public ICommand MergeQueueCommand { get; }
        public ICommand EditCommand { get; }
        public ICommand DiffCommand { get; }
        public ICommand BatchFindReplaceCommand { get; }
        public ICommand ApplyEditsCommand { get; }
        public ICommand CancelEditsCommand { get; }
        public ICommand ResolveConflictCommand { get; }

        /// <summary>
        /// Gets or sets the search string for batch find-and-replace edits.
        /// </summary>
        public string FindText
        {
            get => _findText;
            set => SetProperty(ref _findText, value);
        }

        /// <summary>
        /// Gets or sets the replacement string for batch find-and-replace edits.
        /// </summary>
        public string ReplaceText
        {
            get => _replaceText;
            set => SetProperty(ref _replaceText, value);
        }

        /// <summary>
        /// Gets or sets the search scope for batch find-and-replace edits.
        /// </summary>
        public SearchScope FindReplaceScope
        {
            get => _findReplaceScope;
            set => SetProperty(ref _findReplaceScope, value);
        }

        /// <summary>
        /// Gets the single selected element wrapper when exactly one element is selected.
        /// </summary>
        public ElementTypeWrapperVM? SelectedElement
        {
            get
            {
                if (_activeWrappers != null && _activeWrappers.Count == 1)
                {
                    return _activeWrappers[0];
                }
                return null;
            }
        }

        /// <summary>
        /// Gets whether exactly one element is currently selected.
        /// </summary>
        public bool IsSingleElementSelected => SelectedQueueItems.Count == 1;

        /// <summary>
        /// Gets or sets the name of the selected element, or a count description if multiple elements are selected.
        /// </summary>
        public string SelectedNameOrCount
        {
            get
            {
                if (SelectedQueueItems.Count == 1)
                {
                    return SelectedElement?.Name ?? string.Empty;
                }
                return SelectedQueueItems.Count > 1 ? $"Editing {SelectedQueueItems.Count} elements" : string.Empty;
            }
            set
            {
                if (SelectedQueueItems.Count == 1 && SelectedElement != null)
                {
                    SelectedElement.Name = value;
                    OnPropertyChanged(nameof(SelectedNameOrCount));
                }
            }
        }

        /// <summary>
        /// Gets a comma-separated concatenated list of stripped classes for the selected elements.
        /// </summary>
        public string SelectedDisplayClass
        {
            get
            {
                if (SelectedQueueItems.Count == 0) return string.Empty;

                var classes = SelectedQueueItems
                    .Select(q => q.ClassName)
                    .Select(c => c.StartsWith("Autodesk.Revit.DB.") ? c.Substring("Autodesk.Revit.DB.".Length) : c)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                return string.Join(", ", classes);
            }
        }

        /// <summary>
        /// Gets or sets the aliases string of the selected element, or &lt;Varies&gt; if multiple elements are selected.
        /// </summary>
        public string SelectedAliasesString
        {
            get
            {
                if (SelectedQueueItems.Count == 1)
                {
                    return SelectedElement?.AliasesString ?? string.Empty;
                }
                return SelectedQueueItems.Count > 1 ? "<Varies>" : string.Empty;
            }
            set
            {
                if (SelectedQueueItems.Count == 1 && SelectedElement != null)
                {
                    SelectedElement.AliasesString = value;
                    OnPropertyChanged(nameof(SelectedAliasesString));
                }
            }
        }

        /// <summary>
        /// Gets or sets the name of the selected item in the queue.
        /// </summary>
        public string SelectedItemName
        {
            get
            {
                if (SelectedQueueItems.Count == 1)
                {
                    return SelectedQueueItems[0].Name;
                }
                return SelectedQueueItems.Count > 1 ? "<Varies>" : string.Empty;
            }
            set
            {
                if (SelectedQueueItems.Count == 1 && SelectedQueueItems[0].Name != value)
                {
                    string oldName = SelectedQueueItems[0].Name;
                    _parent.ReplaceReferences(SelectedQueueItems[0].TargetModel, oldName, value);

                    if (SelectedQueueItems[0].TargetModel is ElementModel el)
                    {
                        el.Name = value;
                    }
                    
                    SelectedQueueItems[0].IsEdited = true;
                    SelectedQueueItems[0].ErrorMessage = null;
                    SelectedQueueItems[0].RaisePropertyChanged(nameof(QueueItemModel.Name));
                    SelectedQueueItems[0].RaisePropertyChanged(nameof(QueueItemModel.Model));
                    
                    RefreshUIState(false);
                }
            }
        }

        /// <summary>
        /// Gets the error message of the currently selected queue item if it has an error.
        /// </summary>
        public string? SelectedItemErrorMessage
        {
            get
            {
                if (SelectedQueueItems.Count == 1)
                {
                    return SelectedQueueItems[0].ErrorMessage;
                }
                return null;
            }
        }

        public StagingQueueViewModel(IProjectStandardsDashboard parent, IPocoIdentityService pocoIdentityService, IDiffEngine<IEnumerable<ObjectModel>, Document> diffEngine)
        {
            _parent = parent ?? throw new ArgumentNullException(nameof(parent));
            _pocoIdentityService = pocoIdentityService ?? throw new ArgumentNullException(nameof(pocoIdentityService));
            _diffEngine = diffEngine ?? throw new ArgumentNullException(nameof(diffEngine));

            StagingQueueView = CollectionViewSource.GetDefaultView(StagingQueue);
            StagingQueueView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(QueueItemModel.ClassName)));

            PushToQueueCommand = new RelayCommand(ExecutePushToQueue, CanExecuteActions);
            RemoveFromQueueCommand = new RelayCommand(ExecuteRemoveFromQueue, CanExecuteRemove);
            MergeQueueCommand = new RelayCommand(ExecuteMergeQueue, CanExecuteMergeQueue);
            EditCommand = new RelayCommand(ExecuteEdit, CanExecuteQueueActions);
            DiffCommand = new RelayCommand(ExecuteDiff, CanExecuteQueueActions);
            BatchFindReplaceCommand = new RelayCommand(ExecuteBatchFindReplace, CanExecuteQueueActions);
            ApplyEditsCommand = new RelayCommand(ExecuteApplyEdits, CanExecuteQueueActions);
            CancelEditsCommand = new RelayCommand(ExecuteCancelEdits);
            ResolveConflictCommand = new RelayCommand(ExecuteResolveConflict, CanExecuteQueueActions);
        }

        private bool CanExecuteActions(object parameter) => _parent.SelectedSource != null;
        private bool CanExecuteQueueActions(object parameter) => StagingQueue.Count > 0;

        private void ExecutePushToQueue(object parameter)
        {
            bool willEnforce = true;
            bool willSave = false;

            string action = parameter?.ToString() ?? "Enforce";
            if (action.Equals("Save", StringComparison.OrdinalIgnoreCase))
            {
                willEnforce = false;
                willSave = true;
            }
            else if (action.Equals("SaveAndEnforce", StringComparison.OrdinalIgnoreCase) || action.Equals("Save & Enforce", StringComparison.OrdinalIgnoreCase))
            {
                willEnforce = true;
                willSave = true;
            }
            else // Default or "Enforce"
            {
                willEnforce = true;
                willSave = false;
            }

            var checkedItems = _parent.GetCheckedElements();
            if (checkedItems == null || checkedItems.Count == 0) return;

            // 1. Flatten SelectedSource.SourceHierarchy to build sourceElements list
            var sourceElements = new List<ElementModel>();
            if (_parent.SelectedSource != null)
            {
                foreach (var node in _parent.SelectedSource.SourceHierarchy)
                {
                    _parent.GetElementModelsFromHierarchy(node, sourceElements);
                }
            }

            // 2. Track already staged keys in StagingQueue to prevent duplicates
            var existingKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in StagingQueue)
            {
                if (item.Model is ElementModel elem)
                {
                    if (!string.IsNullOrEmpty(elem.UniqueId))
                    {
                        existingKeys.Add(elem.UniqueId);
                    }
                    existingKeys.Add($"{elem.Class}|{elem.Name}");
                }
            }

            // A queue of newly staged items to scan recursively
            var stagingQueue = new Queue<QueueItemModel>();

            // First, stage explicitly checked items
            foreach (var item in checkedItems)
            {
                if (item.Element == null) continue;

                string key = !string.IsNullOrEmpty(item.Element.UniqueId) 
                    ? item.Element.UniqueId 
                    : $"{item.Element.Class}|{item.Element.Name}";

                if (!existingKeys.Contains(key))
                {
                    var clonedPoco = item.Element.DeepClone();
                    if (clonedPoco != null)
                    {
                        var queueItem = new QueueItemModel(clonedPoco, willEnforce, willSave);
                        StagingQueue.Add(queueItem);
                        
                        if (!string.IsNullOrEmpty(item.Element.UniqueId))
                        {
                            existingKeys.Add(item.Element.UniqueId);
                        }
                        existingKeys.Add($"{item.Element.Class}|{item.Element.Name}");
                        
                        stagingQueue.Enqueue(queueItem);
                    }
                }
            }

            // Recursively stage dependencies
            while (stagingQueue.Count > 0)
            {
                var stagedItem = stagingQueue.Dequeue();

                // Scan the staged item's model for dependencies
                var dependencies = RevitDomDependencyScanner.Scan(stagedItem.Model);

                foreach (var dep in dependencies)
                {
                    if (dep == null) continue;

                    // Resolve the dependency using the 5-step fallback PocoIdentityService
                    var sourcePoco = _pocoIdentityService.ResolveElement(dep, sourceElements);
                    if (sourcePoco != null)
                    {
                        string depKey = !string.IsNullOrEmpty(sourcePoco.UniqueId) 
                            ? sourcePoco.UniqueId 
                            : $"{sourcePoco.Class}|{sourcePoco.Name}";

                        if (!existingKeys.Contains(depKey))
                        {
                            var clonedDep = sourcePoco.DeepClone();
                            if (clonedDep != null)
                            {
                                string parentName = string.Empty;
                                if (stagedItem.Model is ElementModel em)
                                {
                                    parentName = em.Name;
                                }

                                clonedDep.DependencyOrigin = parentName;

                                var queueItem = new QueueItemModel(clonedDep, willEnforce, willSave);
                                queueItem.DependencyOrigin = parentName;
                                StagingQueue.Add(queueItem);

                                if (!string.IsNullOrEmpty(sourcePoco.UniqueId))
                                {
                                    existingKeys.Add(sourcePoco.UniqueId);
                                }
                                existingKeys.Add($"{sourcePoco.Class}|{sourcePoco.Name}");

                                stagingQueue.Enqueue(queueItem);
                            }
                        }
                    }
                }
            }
        }

        private bool CanExecuteRemove(object parameter)
        {
            if (parameter is System.Collections.IList list)
            {
                return list.Count > 0;
            }
            return false;
        }

        private void ExecuteRemoveFromQueue(object parameter)
        {
            if (parameter is System.Collections.IList list)
            {
                var itemsToRemove = list.Cast<QueueItemModel>().ToList();
                foreach (var item in itemsToRemove)
                {
                    StagingQueue.Remove(item);
                }
            }
        }

        private bool CanExecuteMergeQueue(object parameter)
        {
            if (parameter is System.Collections.IList list && list.Count >= 2)
            {
                var items = list.Cast<QueueItemModel>().ToList();
                
                // Block merge if any selected item is a root/built-in category (CategoryModel with ParentCategoryName null/empty)
                if (items.Any(e => e.Model is CategoryModel cat && string.IsNullOrEmpty(cat.ParentCategoryName)))
                {
                    return false;
                }

                string firstCategory = items[0].Category;
                string firstClass = items[0].ClassName;

                return items.All(e => string.Equals(e.Category, firstCategory, StringComparison.OrdinalIgnoreCase) &&
                                     string.Equals(e.ClassName, firstClass, StringComparison.OrdinalIgnoreCase));
            }
            return false;
        }

        private void ExecuteMergeQueue(object parameter)
        {
            if (parameter is System.Collections.IList list && CanExecuteMergeQueue(parameter))
            {
                var selectedItems = list.Cast<QueueItemModel>().ToList();
                var vm = new Synthetic.Shared.UI.SingleItemSelectionViewModel<QueueItemModel>(
                    selectedItems,
                    "Select the primary survivor element. The other selected elements will be deleted, and their names will be appended to the survivor's Aliases list.",
                    q => q.Name)
                {
                    Title = "Consolidate Element Types"
                };

                bool? dialogResult = _parent.ShowMergeDialog?.Invoke(vm);
                if (dialogResult == true)
                {
                    var primaryItem = vm.SelectedItem;
                    if (primaryItem == null) return;

                    var nonPrimaries = selectedItems.Where(q => q != primaryItem).ToList();

                    if (primaryItem.Model is ElementModel primaryElementModel)
                    {
                        var currentAliases = primaryElementModel.Aliases ?? new List<string>();
                        var aliasesList = new List<string>(currentAliases);

                        foreach (var np in nonPrimaries)
                        {
                            if (!aliasesList.Contains(np.Name, StringComparer.OrdinalIgnoreCase))
                            {
                                aliasesList.Add(np.Name);
                            }

                            if (np.Model is ElementModel npElementModel && npElementModel.Aliases != null)
                            {
                                foreach (var npAlias in npElementModel.Aliases)
                                {
                                    if (!aliasesList.Contains(npAlias, StringComparer.OrdinalIgnoreCase))
                                    {
                                        aliasesList.Add(npAlias);
                                    }
                                }
                            }
                        }

                        // Update the primary's Aliases property
                        primaryElementModel.Aliases = aliasesList;
                        primaryItem.IsEdited = true;

                        // Merge execution actions (WillEnforce, WillSave)
                        foreach (var np in nonPrimaries)
                        {
                            if (np.WillEnforce)
                            {
                                primaryItem.WillEnforce = true;
                            }
                            if (np.WillSave)
                            {
                                primaryItem.WillSave = true;
                            }
                        }

                        primaryItem.RaisePropertyChanged(nameof(QueueItemModel.Model));

                        // Scan all other elements in the Staging Queue and replace references!
                        _parent.ReplaceQueueReferences(nonPrimaries, primaryItem.Name);

                        // Purge consumed items
                        foreach (var np in nonPrimaries)
                        {
                            StagingQueue.Remove(np);
                        }

                        // Reset workspace back to idle post-merge to prevent ghost references
                        _parent.ActiveWorkspace = WorkspaceMode.Idle;
                    }
                }
            }
        }

        private void ExecuteEdit(object parameter)
        {
            if (parameter is System.Collections.IList list)
            {
                SelectedQueueItems.Clear();
                foreach (var item in list.Cast<QueueItemModel>())
                {
                    SelectedQueueItems.Add(item);
                }

                _originalIntents.Clear();
                foreach (var item in SelectedQueueItems)
                {
                    _originalIntents[item] = new QueueItemStateBackup
                    {
                        WillEnforce = item.WillEnforce,
                        WillSave = item.WillSave,
                        IsEdited = item.IsEdited,
                        IsDiffed = item.IsDiffed
                    };
                }

                RefreshUIState(false);
            }
            _parent.ActiveWorkspace = WorkspaceMode.Edit;
        }

        private void ExecuteDiff(object parameter)
        {
            if (parameter is System.Collections.IList list)
            {
                SelectedQueueItems.Clear();
                foreach (var item in list.Cast<QueueItemModel>())
                {
                    SelectedQueueItems.Add(item);
                }

                if (_parent.Document != null)
                {
                    try
                    {
                        var elementPocos = SelectedQueueItems.Select(q => q.Model).OfType<ElementModel>().ToList();
                        var clusters = _diffEngine.Compare(elementPocos, _parent.Document);

                        ActiveDiffClusters.Clear();
                        foreach (var cluster in clusters)
                        {
                            ActiveDiffClusters.Add(cluster);
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Diff scan execution skipped or failed: {ex.Message}");
                    }
                }

                _parent.ActiveWorkspace = WorkspaceMode.Diff;
            }
        }

        private void ExecuteResolveConflict(object parameter)
        {
            foreach (var cluster in ActiveDiffClusters)
            {
                foreach (var mapping in cluster.TypeMappings)
                {
                    var targetName = mapping.TargetType?.Name;
                    var queueItem = SelectedQueueItems.FirstOrDefault(q => q.Name == targetName);
                    if (queueItem != null && queueItem.Model is ElementModel element)
                    {
                        foreach (var row in mapping.ParameterResolutions)
                        {
                            if (row.IsSourceWinning && row.Options != null && row.Options.Count > 0)
                            {
                                var param = element.Parameters?.FirstOrDefault(p => p.Name == row.ParameterName);
                                if (param != null)
                                {
                                    param.Value = row.Options[0].DisplayText;
                                }
                            }
                        }
                        queueItem.IsDiffed = true;
                    }
                }
            }

            _parent.ActiveWorkspace = WorkspaceMode.Idle;
            ActiveDiffClusters.Clear();
        }

        private void ExecuteBatchFindReplace(object parameter)
        {
            if (string.IsNullOrEmpty(FindText)) return;

            string findText = FindText;
            string replaceText = ReplaceText ?? string.Empty;
            var scope = FindReplaceScope;
            
            bool searchNames = scope == SearchScope.ElementNames || scope == SearchScope.Both;
            bool searchParams = scope == SearchScope.ParameterValues || scope == SearchScope.Both;

            var elements = SelectedQueueItems.Select(q => q.Model).OfType<ElementModel>().ToList();
            var modifiedElements = _parent.FindReplaceService.Execute(elements, findText, replaceText, searchNames, searchParams);

            foreach (var item in SelectedQueueItems)
            {
                if (item.Model is ElementModel el && modifiedElements.Contains(el))
                {
                    item.IsEdited = true;
                    item.RaisePropertyChanged(nameof(QueueItemModel.Name));
                    item.RaisePropertyChanged(nameof(QueueItemModel.Model));
                }
            }

            RefreshUIState(false);
            StagingQueueView?.Refresh();
        }

        private void ExecuteApplyEdits(object parameter)
        {
            foreach (var item in SelectedQueueItems)
            {
                item.IsEdited = true;
            }
            RefreshUIState(true);
            _parent.ActiveWorkspace = WorkspaceMode.Idle;
        }

        private void ExecuteCancelEdits(object parameter)
        {
            foreach (var item in SelectedQueueItems)
            {
                item.Revert();
                if (_originalIntents.TryGetValue(item, out var backup))
                {
                    item.WillEnforce = backup.WillEnforce;
                    item.WillSave = backup.WillSave;
                    item.IsEdited = backup.IsEdited;
                    item.IsDiffed = backup.IsDiffed;
                }
            }
            RefreshUIState(true);
            _parent.ActiveWorkspace = WorkspaceMode.Idle;
        }

        private void RefreshUIState(bool clearWrappers = false)
        {
            if (clearWrappers)
            {
                _activeWrappers.Clear();
                foreach (var param in DisplayParameters)
                {
                    param.PropertyChanged -= DisplayParam_PropertyChanged;
                }
                DisplayParameters.Clear();
            }
            else
            {
                _activeWrappers = SelectedQueueItems.Select(q => q.GetWrapper()).ToList();
            }

            OnPropertyChanged(nameof(SelectedElement));
            UpdateSelectedElementSubscription();
            RaiseIdentityHeaderStateChanged();

            if (!clearWrappers)
            {
                CalculateParameterIntersection();
            }

            OnPropertyChanged(nameof(SelectedItemName));
            OnPropertyChanged(nameof(SelectedItemErrorMessage));
        }

        private void UpdateSelectedElementSubscription()
        {
            if (_subscribedWrapper != null)
            {
                _subscribedWrapper.PropertyChanged -= SelectedElementWrapper_PropertyChanged;
            }

            _subscribedWrapper = SelectedElement;

            if (_subscribedWrapper != null)
            {
                _subscribedWrapper.PropertyChanged += SelectedElementWrapper_PropertyChanged;
                _lastSelectedName = _subscribedWrapper.Name;
            }
            else
            {
                _lastSelectedName = string.Empty;
            }
        }

        private void SelectedElementWrapper_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (sender is ElementTypeWrapperVM wrapper && wrapper == _subscribedWrapper)
            {
                var queueItem = SelectedQueueItems.FirstOrDefault(q => q.Model == wrapper.GetUpdatedModel());
                if (queueItem != null)
                {
                    if (e.PropertyName == nameof(ElementTypeWrapperVM.Name))
                    {
                        string oldName = _lastSelectedName;
                        string newName = wrapper.Name;
                        if (oldName != newName)
                        {
                            _parent.ReplaceReferences(queueItem.TargetModel, oldName, newName);
                            _lastSelectedName = newName;
                            
                            queueItem.IsEdited = true;
                            queueItem.ErrorMessage = null;
                            queueItem.RaisePropertyChanged(nameof(QueueItemModel.Name));
                            queueItem.RaisePropertyChanged(nameof(QueueItemModel.Model));
                            
                            OnPropertyChanged(nameof(SelectedItemName));
                            OnPropertyChanged(nameof(SelectedNameOrCount));
                        }
                    }
                    else if (e.PropertyName == nameof(ElementTypeWrapperVM.AliasesString))
                    {
                        queueItem.IsEdited = true;
                        queueItem.ErrorMessage = null;
                        queueItem.RaisePropertyChanged(nameof(QueueItemModel.Model));
                        OnPropertyChanged(nameof(SelectedAliasesString));
                    }
                }
            }
        }

        private void RaiseIdentityHeaderStateChanged()
        {
            OnPropertyChanged(nameof(IsSingleElementSelected));
            OnPropertyChanged(nameof(SelectedNameOrCount));
            OnPropertyChanged(nameof(SelectedDisplayClass));
            OnPropertyChanged(nameof(SelectedAliasesString));
        }

        private void CalculateParameterIntersection()
        {
            foreach (var param in DisplayParameters)
            {
                param.PropertyChanged -= DisplayParam_PropertyChanged;
            }
            DisplayParameters.Clear();

            if (SelectedQueueItems.Count == 0 || _activeWrappers.Count == 0)
            {
                return;
            }

            if (_activeWrappers.Count == 1)
            {
                foreach (var param in _activeWrappers[0].Parameters)
                {
                    param.PropertyChanged += DisplayParam_PropertyChanged;
                    DisplayParameters.Add(param);
                }
                return;
            }

            // Multiple elements selected: compute parameter intersection matching Name and StorageType
            var firstElement = _activeWrappers[0];
            var commonParams = firstElement.Parameters
                .Select(p => new { p.Name, p.StorageType })
                .ToList();

            for (int i = 1; i < _activeWrappers.Count; i++)
            {
                var currentElement = _activeWrappers[i];
                commonParams = commonParams
                    .Intersect(currentElement.Parameters.Select(p => new { p.Name, p.StorageType }))
                    .ToList();
            }

            // Create display wrappers representing the intersection states
            foreach (var common in commonParams)
            {
                var matchingParams = _activeWrappers
                    .Select(w => w.Parameters.First(p => p.Name == common.Name))
                    .ToList();

                string firstVal = matchingParams[0].Value;
                bool isMixed = matchingParams.Any(p => p.Value != firstVal || p.IsMixedValue);
                bool isReadOnly = matchingParams.Any(p => p.IsReadOnly);
                
                string? guid = matchingParams[0].GUID;
                long id = matchingParams[0].Id;
                bool isShared = matchingParams[0].IsShared;

                // Create dummy ParameterModel representing the intersection
                var dummyModel = new ParameterModel(
                    common.Name,
                    isMixed ? "<Varies>" : firstVal,
                    null, // ValueElemId matched simple
                    common.StorageType,
                    (int)id,
                    guid,
                    isShared,
                    isReadOnly
                );

                var displayParam = new ParameterWrapperVM(dummyModel, matchingParams[0].GetModel() != null ? _activeWrappers[0].GetUpdatedModel() : null);
                if (isMixed)
                {
                    displayParam.IsMixedValue = true;
                }

                displayParam.PropertyChanged += DisplayParam_PropertyChanged;
                DisplayParameters.Add(displayParam);
            }
        }

        private void DisplayParam_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ParameterWrapperVM.Value) || e.PropertyName == nameof(ParameterWrapperVM.IsMixedValue))
            {
                if (sender is ParameterWrapperVM displayParam)
                {
                    string newValue = displayParam.Value;
                    string paramName = displayParam.Name;

                    // Propagate modified parameter value to all selected elements in editing wrappers
                    foreach (var wrapper in _activeWrappers)
                    {
                        var targetParam = wrapper.Parameters.FirstOrDefault(p => p.Name == paramName);
                        if (targetParam != null && !targetParam.IsReadOnly)
                        {
                            string oldVal = targetParam.Value;
                            targetParam.Value = newValue;
                            targetParam.IsMixedValue = false;

                            // Cascading element ID updates for swapped values (like Pattern, Material, etc.)
                            if (paramName.EndsWith("Id") && oldVal != newValue)
                            {
                                string oldName = oldVal;
                                _parent.ReplaceReferences(wrapper.GetUpdatedModel(), oldName, newValue);
                            }
                        }
                    }

                    // Update UI state
                    displayParam.IsMixedValue = false;
                    foreach (var item in SelectedQueueItems)
                    {
                        item.IsEdited = true;
                        item.ErrorMessage = null;
                    }
                    OnPropertyChanged(nameof(SelectedItemErrorMessage));
                }
            }
        }
    }
}

```

### File: StandardsManagement/ViewModels/StandardClassModel.cs
```csharp
using System;

namespace Synthetic.Modules.StandardsManagement.ViewModels
{
    /// <summary>
    /// TreeView node representing a standard class (e.g. Text Note Types, Wall Types).
    /// </summary>
    public class StandardClassModel : SourceTreeItemViewModel
    {
    }
}
```

### File: StandardsManagement/ViewModels/StandardElementModel.cs
```csharp
using System;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;

namespace Synthetic.Modules.StandardsManagement.ViewModels
{
    /// <summary>
    /// TreeView leaf node wrapping a specific ElementModel POCO representation.
    /// </summary>
    public class StandardElementModel : SourceTreeItemViewModel
    {
        /// <summary>
        /// Gets the underlying ElementModel.
        /// </summary>
        public ElementModel Element { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="StandardElementModel"/> class.
        /// </summary>
        /// <param name="element">The element model to wrap.</param>
        public StandardElementModel(ElementModel element)
        {
            Element = element ?? throw new ArgumentNullException(nameof(element));
            Name = element.Name ?? string.Empty;
        }
    }
}
```

### File: StandardsManagement/ViewModels/StandardGroupModel.cs
```csharp
using System;

namespace Synthetic.Modules.StandardsManagement.ViewModels
{
    /// <summary>
    /// TreeView node representing a standard group (e.g. Annotations, Materials & Assets, System Types).
    /// </summary>
    public class StandardGroupModel : SourceTreeItemViewModel
    {
    }
}
```

### File: StandardsManagement/ViewModels/StandardsClassSelectionViewModel.cs
```csharp
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using Autodesk.Revit.DB;

using Synthetic.Shared.UI;

using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;
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
```

### File: StandardsManagement/ViewModels/StandardsExecutionPipelineViewModel.cs
```csharp
﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows.Input;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;
using Synthetic.RevitDOM.Operations.Standards;
using Synthetic.RevitDOM.Operations.Standards;
using Synthetic.RevitDOM.Operations.Standards;
using Synthetic.Shared.UI;
using Synthetic.Infrastructure.Persistence;
using Synthetic.Core;
using Synthetic.Settings;

namespace Synthetic.Modules.StandardsManagement.ViewModels
{
    /// <summary>
    /// ViewModel that manages the options configuration and final save/enforce execution workflow.
    /// </summary>
    public class StandardsExecutionPipelineViewModel : ViewModelBase
    {
        private readonly ProjectStandardsDashboardViewModel _parent;
        private readonly IFileDialogService _dialogService;
        private readonly IStandardsExecutionPipeline _pipeline;

        private bool _updateFamilies = false;
        private bool _processNestedRecursive = false;
        private bool _purgeUnusedStyleTypes = false;
        private string _categoryFilter = "All Categories";
        private string? _saveFilePath;

        /// <summary>
        /// Gets or sets whether to update loaded families during queue execution.
        /// </summary>
        public bool UpdateFamilies
        {
            get => _updateFamilies;
            set => SetProperty(ref _updateFamilies, value);
        }

        /// <summary>
        /// Gets or sets whether to process nested families recursively.
        /// </summary>
        public bool ProcessNestedRecursive
        {
            get => _processNestedRecursive;
            set => SetProperty(ref _processNestedRecursive, value);
        }

        /// <summary>
        /// Gets or sets whether to purge unused style types in family documents.
        /// </summary>
        public bool PurgeUnusedStyleTypes
        {
            get => _purgeUnusedStyleTypes;
            set => SetProperty(ref _purgeUnusedStyleTypes, value);
        }

        /// <summary>
        /// Gets or sets the category filter for family updates.
        /// </summary>
        public string CategoryFilter
        {
            get => _categoryFilter;
            set => SetProperty(ref _categoryFilter, value);
        }

        /// <summary>
        /// Gets the list of available category filters.
        /// </summary>
        public List<string> AvailableCategoryFilters { get; } = new List<string>
        {
            "All Categories",
            "Annotations Only",
            "Title Blocks Only"
        };

        /// <summary>
        /// Gets or sets the target file path for save actions.
        /// </summary>
        public string? SaveFilePath
        {
            get => _saveFilePath;
            set
            {
                if (SetProperty(ref _saveFilePath, value))
                {
                    OnPropertyChanged(nameof(IsSavePathActive));
                }
            }
        }

        /// <summary>
        /// Gets whether the save path panel should be active/visible in the UI.
        /// </summary>
        public bool IsSavePathActive => _parent.StagingQueue.Any(item => item.WillSave);

        public ICommand EnforceCommand { get; }
        public ICommand SaveCommand { get; }
        public ICommand SaveAndEnforceCommand { get; }
        public ICommand BrowseSavePathCommand { get; }
        public ICommand RunQueueCommand { get; }

        public StandardsExecutionPipelineViewModel(ProjectStandardsDashboardViewModel parent, IStandardsExecutionPipeline pipeline)
        {
            _parent = parent ?? throw new ArgumentNullException(nameof(parent));
            _dialogService = parent.DialogService;
            _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));

            EnforceCommand = new RelayCommand(ExecuteEnforce, CanExecuteActions);
            SaveCommand = new RelayCommand(ExecuteSave, CanExecuteActions);
            SaveAndEnforceCommand = new RelayCommand(ExecuteSaveAndEnforce, CanExecuteActions);
            BrowseSavePathCommand = new RelayCommand(ExecuteBrowseSavePath);
            RunQueueCommand = new RelayCommand(ExecuteRunQueue, CanExecuteQueueActions);

            _parent.StagingQueueViewModel.StagingQueue.CollectionChanged += (s, e) =>
            {
                OnPropertyChanged(nameof(IsSavePathActive));
            };
        }

        private bool CanExecuteActions(object parameter) => _parent.SelectedSource != null;
        private bool CanExecuteQueueActions(object parameter) => _parent.StagingQueue.Count > 0;

        private void ExecuteEnforce(object parameter)
        {
            // Placeholder: out of scope for this slice
        }

        private void ExecuteSave(object parameter)
        {
            // Placeholder: out of scope for this slice
        }

        private void ExecuteSaveAndEnforce(object parameter)
        {
            // Placeholder: out of scope for this slice
        }

        private void ExecuteBrowseSavePath(object parameter)
        {
            string? defaultFileName = "ProjectStandards.json";
            if (!string.IsNullOrEmpty(SaveFilePath))
            {
                defaultFileName = System.IO.Path.GetFileName(SaveFilePath);
            }
            string? newPath = _dialogService.SaveFileDialog("JSON Files (*.json)|*.json", "Save Standards JSON File", defaultFileName);
            if (!string.IsNullOrEmpty(newPath))
            {
                SaveFilePath = newPath;
            }
        }

        private void ExecuteRunQueue(object parameter)
        {
            if (_parent.ExternalEvent != null)
            {
                _parent.ExternalEvent.Raise();
            }
            else
            {
                RunQueueInternal();
            }
        }

        public void RunQueueInternal()
        {
            if (_parent.Document == null) return;

            _parent.LastExecutionResults.Clear();

            // Map staging queue items
            var pipelineItems = _parent.StagingQueue.Select(item => new StandardsExecutionItem(item.Model)
            {
                WillEnforce = item.WillEnforce,
                WillSave = item.WillSave
            }).ToList();

            // Determine target path
            string? targetPath = null;
            if (!string.IsNullOrEmpty(SaveFilePath))
            {
                targetPath = SaveFilePath;
            }
            else if (_parent.SelectedSource != null && !_parent.SelectedSource.IsRevitSource && !string.IsNullOrEmpty(_parent.SelectedSource.SourcePath))
            {
                targetPath = _parent.SelectedSource.SourcePath;
            }
            else
            {
                string? projectSettingsPath = GetProjectSettingsPath();
                if (!string.IsNullOrEmpty(projectSettingsPath))
                {
                    targetPath = projectSettingsPath;
                }
            }

            // Initialize options
            var options = new StandardsExecutionOptions
            {
                ProcessFamilies = UpdateFamilies,
                CategoryFilter = CategoryFilter,
                PurgeUnusedStyleTypes = PurgeUnusedStyleTypes,
                StandardsFilePath = targetPath ?? string.Empty,
                WriteRevitDatabase = _parent.StagingQueue.Any(i => i.WillEnforce),
                SaveLocalFiles = _parent.StagingQueue.Any(i => i.WillSave),
                UseTransactionGroup = true,
                ProtectedPaths = GetProtectedPaths().ToList()
            };

            // Invoke the pipeline
            var progressReporter = ProgressCoordinator.AsProgressReporter();
            var result = _pipeline.Execute(_parent.Document, pipelineItems, options, progressReporter, ProgressCoordinator.Token);

            // Populate LastExecutionResults
            _parent.LastExecutionResults.AddRange(result.RawResults);

            // Build summary tracker log items from LastExecutionResults
            var tracker = new ObservableCollection<ImportLogItem>();
            foreach (var res in _parent.LastExecutionResults)
            {
                var model = res.Model;
                string action = "Updated";
                if (!res.Success)
                {
                    if (res.Action == "Alias Swap Failed")
                    {
                        action = "Alias Fail";
                    }
                    else
                    {
                        action = res.OperationTarget == "File" ? "Save Failed" : "Failed";
                    }
                }
                else
                {
                    if (!string.IsNullOrEmpty(res.Action))
                    {
                        action = res.Action;
                    }
                    else if (res.OperationTarget == "File")
                    {
                        action = "Saved";
                    }
                    else
                    {
                        // Find the enqueued item to determine if it was Enforced/Saved
                        var queueItem = _parent.StagingQueue.FirstOrDefault(qi => qi.Model == model);
                        if (queueItem != null && queueItem.WillEnforce)
                        {
                            action = "Created";
                        }
                        else
                        {
                            action = "Updated";
                        }
                    }
                }

                string name = (model is ElementModel em) ? (em.Name ?? "Unnamed") : model.GetType().Name;
                string className = (model is ElementModel emClass) ? (emClass.Class ?? "Unknown") : model.GetType().Name;
                if (className.Contains("."))
                {
                    className = className.Split('.').Last();
                }

                string message = res.Success ? "Operation completed successfully." : (res.ErrorMessage ?? "Unknown error occurred.");
                if (!string.IsNullOrEmpty(res.Message))
                {
                    message = res.Message;
                }
                if (res.Warnings != null && res.Warnings.Count > 0)
                {
                    message += " Warnings: " + string.Join(", ", res.Warnings);
                }

                tracker.Add(new ImportLogItem
                {
                    Action = action,
                    Class = className,
                    ElementName = name,
                    Message = message
                });
            }

            Action updateUI = () =>
            {
                // Display the summary dialog modal
                if (tracker.Count > 0 && _parent.SummaryDisplayService != null)
                {
                    var summaryVM = new ImportSummaryViewModel(tracker, _dialogService);
                    IntPtr parentHandle = _parent.UIApplication != null ? _parent.UIApplication.MainWindowHandle : IntPtr.Zero;
                    _parent.SummaryDisplayService.ShowSummary(summaryVM, parentHandle);
                }

                // Systematic queue purging and error message hydration
                var successfulItems = new List<QueueItemModel>();
                foreach (var item in _parent.StagingQueue.ToList())
                {
                    var resultsForItem = _parent.LastExecutionResults.Where(r => r.Model == item.Model).ToList();
                    if (resultsForItem.Count > 0 && resultsForItem.All(r => r.Success))
                    {
                        successfulItems.Add(item);
                    }
                    else
                    {
                        var failedResult = resultsForItem.FirstOrDefault(r => !r.Success);
                        if (failedResult != null)
                        {
                            item.ErrorMessage = failedResult.ErrorMessage ?? "Execution failed.";
                        }
                        else
                        {
                            item.ErrorMessage = "Execution was not completed.";
                        }
                    }
                }

                foreach (var item in successfulItems)
                {
                    _parent.StagingQueue.Remove(item);
                }

                _parent.ActiveWorkspace = WorkspaceMode.Idle;
            };

            if (System.Windows.Application.Current != null)
            {
                System.Windows.Application.Current.Dispatcher.Invoke(updateUI);
            }
            else
            {
                updateUI();
            }
        }

        private HashSet<string> GetProtectedPaths()
        {
            var protectedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            string? projectSettingsPath = GetProjectSettingsPath();
            if (!string.IsNullOrEmpty(projectSettingsPath))
            {
                protectedPaths.Add(Path.GetFullPath(projectSettingsPath));
            }

            try
            {
                string? appSettingsPath = GetAppConfiguredPath();
                if (!string.IsNullOrEmpty(appSettingsPath))
                {
                    protectedPaths.Add(Path.GetFullPath(appSettingsPath));
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"App configurations retrieval JIT compilation skipped: {ex.Message}");
            }

            try
            {
                string defaultPath = Path.Combine(Config.addinPath, "SyntheticSettings.json");
                if (File.Exists(defaultPath))
                {
                    Config? defaultConfig = Config.ReadFromFile(defaultPath);
                    if (defaultConfig != null && defaultConfig.Contains(StandardsSettings.Name))
                    {
                        var appSettings = defaultConfig.GetSettings<StandardsSettings>(StandardsSettings.Name);
                        if (appSettings != null && !string.IsNullOrEmpty(appSettings.StandardsFilePath))
                        {
                            protectedPaths.Add(Path.GetFullPath(appSettings.StandardsFilePath));
                        }
                    }
                }
            }
            catch { }

            return protectedPaths;
        }

        private string? GetProjectSettingsPath()
        {
            if (_parent.Settings != null)
            {
                return _parent.Settings.StandardsFilePath;
            }
            if (_parent.Document == null) return null;
            try
            {
                var settings = SettingsManager.Get<StandardsSettings>(_parent.Document);
                return settings?.StandardsFilePath;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Settings retrieval skipped or failed: {ex.Message}");
                return null;
            }
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private string? GetAppConfiguredPath()
        {
            var appConfig = App.Configurations?.GetAppConfig();
            if (appConfig != null && appConfig.Contains(StandardsSettings.Name))
            {
                var appSettings = appConfig.GetSettings<StandardsSettings>(StandardsSettings.Name);
                return appSettings?.StandardsFilePath;
            }
            return null;
        }
    }
}
```

### File: StandardsManagement/ViewModels/StandardsReviewViewModel.cs
```csharp
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using Autodesk.Revit.DB;

using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;
using Synthetic.Infrastructure.Serialization;

using Synthetic.Shared.UI;
using Synthetic.RevitDOM.Operations.Merge;
using Synthetic.Shared.RevitAPI;
namespace Synthetic.Modules.StandardsManagement.ViewModels
{
    /// <summary>
    /// ViewModel for the standards review window.
    /// Manages the resolution and enforcement of style conflicts between JSON standards and the document.
    /// </summary>
    public class StandardsReviewViewModel : ViewModelBase
    {
        private readonly Document _doc;
        private ObservableCollection<DuplicateClusterModel> _clusters;
        private DuplicateClusterModel? _selectedCluster;
        private TypeMappingModel? _selectedTypeMapping;
        private Action? _closeAction;

        /// <summary>
        /// Initializes a new instance of the <see cref="StandardsReviewViewModel"/> class.
        /// </summary>
        /// <param name="doc">The active Revit document.</param>
        /// <param name="clusters">The collection of duplicate clusters showing standards differences.</param>
        public StandardsReviewViewModel(Document doc, ObservableCollection<DuplicateClusterModel> clusters)
        {
            _doc = doc ?? throw new ArgumentNullException(nameof(doc));
            _clusters = clusters ?? new ObservableCollection<DuplicateClusterModel>();

            EnforceApprovedCommand = new RelayCommand(ExecuteEnforceApproved);
            CancelCommand = new RelayCommand(ExecuteCancel);

            SelectedCluster = _clusters.FirstOrDefault();
        }

        /// <summary>
        /// Gets or sets the collection of standard comparison clusters.
        /// </summary>
        public ObservableCollection<DuplicateClusterModel> Clusters
        {
            get => _clusters;
            set => SetProperty(ref _clusters, value);
        }

        /// <summary>
        /// Gets or sets the currently selected cluster (category) in the sidebar.
        /// </summary>
        public DuplicateClusterModel? SelectedCluster
        {
            get => _selectedCluster;
            set
            {
                if (SetProperty(ref _selectedCluster, value))
                {
                    SelectedTypeMapping = _selectedCluster?.TypeMappings?.FirstOrDefault();
                    OnPropertyChanged(nameof(SelectedClusterTypeMappings));
                }
            }
        }

        /// <summary>
        /// Gets the collection of type mappings for the selected cluster.
        /// </summary>
        public ObservableCollection<TypeMappingModel>? SelectedClusterTypeMappings => SelectedCluster?.TypeMappings;

        /// <summary>
        /// Gets or sets the currently selected type mapping in the element grid.
        /// </summary>
        public TypeMappingModel? SelectedTypeMapping
        {
            get => _selectedTypeMapping;
            set
            {
                if (SetProperty(ref _selectedTypeMapping, value))
                {
                    OnPropertyChanged(nameof(Rows));
                }
            }
        }

        /// <summary>
        /// Gets the collection of parameter resolutions for the selected type mapping.
        /// </summary>
        public ObservableCollection<ParameterDiffRowModel>? Rows => SelectedTypeMapping?.ParameterResolutions;

        /// <summary>
        /// Gets the command to enforce all approved overrides.
        /// </summary>
        public ICommand EnforceApprovedCommand { get; }

        /// <summary>
        /// Gets the command to cancel the operation.
        /// </summary>
        public ICommand CancelCommand { get; }

        /// <summary>
        /// Gets or sets the action to close the window.
        /// </summary>
        public Action? CloseAction
        {
            get => _closeAction;
            set => _closeAction = value;
        }

        private void ExecuteEnforceApproved(object parameter)
        {
            int appliedCount = 0;
            int skippedCount = 0;

            using (Transaction trans = new Transaction(_doc, "Enforce Project Standards"))
            {
                trans.Start();

                foreach (var cluster in Clusters)
                {
                    if (cluster.TypeMappings == null) continue;

                    foreach (var mapping in cluster.TypeMappings)
                    {
                        if (mapping.ParameterResolutions == null) continue;

                        if (mapping.SourceType == null) continue;
                        Element? liveElement = _doc.GetElement(mapping.SourceType.RevitTypeId.ToElementId());
                        if (liveElement == null)
                        {
                            skippedCount += mapping.ParameterResolutions.Count;
                            continue;
                        }

                        foreach (var row in mapping.ParameterResolutions)
                        {
                            // Only process approved overrides
                            if (!row.IsApproved)
                            {
                                skippedCount++;
                                continue;
                            }

                            string paramName = row.ParameterName;
                            string? winningValue = row.WinningValue;

                            // Retrieve specific parameter by name
                            Parameter p = liveElement.LookupParameter(paramName);
                            if (p == null)
                            {
                                foreach (Parameter param in liveElement.Parameters)
                                {
                                    if (param.Definition != null && param.Definition.Name == paramName)
                                    {
                                        p = param;
                                        break;
                                    }
                                }
                            }

                            try
                            {
                                if (p != null && !p.IsReadOnly)
                                {
                                    bool success = false;
                                    switch (p.StorageType)
                                    {
                                        case StorageType.Double:
                                            if (double.TryParse(winningValue, out double dVal))
                                            {
                                                success = p.Set(dVal);
                                            }
                                            break;
                                        case StorageType.Integer:
                                            if (int.TryParse(winningValue, out int iVal))
                                            {
                                                success = p.Set(iVal);
                                            }
                                            break;
                                        case StorageType.String:
                                            success = p.Set(winningValue);
                                            break;
                                        case StorageType.ElementId:
#if REVIT2022 || REVIT2023
                                            if (int.TryParse(winningValue, out int idInt))
                                            {
                                                success = p.Set(new ElementId(idInt));
                                            }
#else
                                            if (long.TryParse(winningValue, out long idLong))
                                            {
                                                success = p.Set(new ElementId(idLong));
                                            }
#endif
                                            break;
                                    }

                                    if (success)
                                    {
                                        appliedCount++;
                                    }
                                    else
                                    {
                                        skippedCount++;
                                    }
                                }
                                else
                                {
                                    skippedCount++;
                                }
                            }
                            catch (Exception)
                            {
                                skippedCount++;
                            }
                        }
                    }
                }

                trans.Commit();
            }

            // [AG2_TEST_START: StandardsReviewWindowQA]
            // REVERT_METHOD: To remove, safely delete this entire block.
            Console.WriteLine($"Jrn.Directive \"SyntheticQA\", \"AppliedStandardOverrides: [{appliedCount}]\"");
            Console.WriteLine($"Jrn.Directive \"SyntheticQA\", \"SkippedStandardOverrides: [{skippedCount}]\"");
            // [AG2_TEST_END: StandardsReviewWindowQA]

            CloseAction?.Invoke();
        }

        private void ExecuteCancel(object parameter)
        {
            CloseAction?.Invoke();
        }
    }
}
```

### File: StandardsManagement/ViewModels/StandardsSourceTreeViewModel.cs
```csharp
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows.Input;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Synthetic.Core;
using Synthetic.Shared.UI;
using Synthetic.RevitDOM.Operations.Standards;
using Synthetic.RevitDOM.Operations.Standards;
using Synthetic.RevitDOM.Operations.Standards;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;

namespace Synthetic.Modules.StandardsManagement.ViewModels
{
    /// <summary>
    /// ViewModel that manages the standard source tabs, tree hierarchies, search filtering, and document extraction.
    /// </summary>
    public class StandardsSourceTreeViewModel : ViewModelBase
    {
        private readonly ProjectStandardsDashboardViewModel _parent;
        private readonly UIApplication? _uiapp;
        private readonly Document? _doc;
        private readonly IFileDialogService _dialogService;
        private readonly IStandardsExtractionOrchestrator _orchestrator;
        private readonly IStandardSerializationEngine _serializationEngine;

        private ObservableCollection<ProjectStandardsSourceViewModel> _availableSources = new ObservableCollection<ProjectStandardsSourceViewModel>();
        private ProjectStandardsSourceViewModel? _selectedSource;
        private string _searchText = string.Empty;

        /// <summary>
        /// Gets or sets the collection of loaded standard sources (tabs).
        /// </summary>
        public ObservableCollection<ProjectStandardsSourceViewModel> AvailableSources
        {
            get => _availableSources;
            set => SetProperty(ref _availableSources, value);
        }

        /// <summary>
        /// Gets or sets the currently active source tab.
        /// </summary>
        public ProjectStandardsSourceViewModel? SelectedSource
        {
            get => _selectedSource;
            set => SetProperty(ref _selectedSource, value);
        }

        /// <summary>
        /// Gets or sets the search filter text.
        /// </summary>
        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetProperty(ref _searchText, value))
                {
                    ApplySearchFilter();
                }
            }
        }

        public ICommand AddFileSourceCommand { get; }
        public ICommand AddRevitModelCommand { get; }
        public ICommand CloseSourceCommand { get; }

        public StandardsSourceTreeViewModel(
            ProjectStandardsDashboardViewModel parent,
            UIApplication? uiapp,
            Document? doc,
            IFileDialogService dialogService,
            IStandardsExtractionOrchestrator orchestrator,
            IStandardSerializationEngine serializationEngine)
        {
            _parent = parent ?? throw new ArgumentNullException(nameof(parent));
            _uiapp = uiapp;
            _doc = doc;
            _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
            _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
            _serializationEngine = serializationEngine ?? throw new ArgumentNullException(nameof(serializationEngine));

            AddFileSourceCommand = new RelayCommand(ExecuteAddFileSource);
            AddRevitModelCommand = new RelayCommand(ExecuteAddRevitModel);
            CloseSourceCommand = new RelayCommand(ExecuteCloseSource, CanExecuteCloseSource);
        }

        /// <summary>
        /// Loads a JSON standard file and appends it as a new source tab.
        /// </summary>
        public void LoadFileSource(string path, string displayName)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;

            try
            {
                string json = File.ReadAllText(path);
                var elements = ModelsToSerialize.DeserializeByJson(json);
                var hierarchy = StandardsHierarchyUtility.BuildHierarchy(elements);

                var source = new ProjectStandardsSourceViewModel
                {
                    DisplayName = displayName,
                    SourcePath = path,
                    IsRevitSource = false
                };

                foreach (var group in hierarchy)
                {
                    source.SourceHierarchy.Add(group);
                }

                AvailableSources.Add(source);
                SelectedSource = source;
            }
            catch (Exception)
            {
                // Handle I/O or deserialization errors silently per Robustness directive
            }
        }

        private void ExecuteAddFileSource(object parameter)
        {
            string? path = _dialogService.OpenFileDialog("JSON files (*.json)|*.json", "Load Standards File", "");
            if (!string.IsNullOrEmpty(path))
            {
                LoadFileSource(path, Path.GetFileName(path));
            }
        }

        private void ExecuteAddRevitModel(object parameter)
        {
            if (_doc == null) return;

            var dialogVM = new SelectRevitDocumentViewModel(_uiapp, _parent.MockOpenDocuments);
            bool? dialogResult = _parent.ShowDocumentSelectionDialog?.Invoke(dialogVM);
            if (dialogResult == true)
            {
                var selectedDocs = dialogVM.SelectedDocuments;
                var selectedGroupings = dialogVM.SelectedFamilyGroupings;
                var scanFamilies = dialogVM.ScanFamilies;
                var scanNested = dialogVM.IncludeNestedFamilies;

                try
                {
                    ProgressCoordinator.Initialize("Extracting Project Standards", "Extracting Revit standards...", selectedDocs.Count);

                    foreach (var doc in selectedDocs)
                    {
                        if (ProgressCoordinator.IsCancelled()) break;

                        string title = "Linked Revit Model";
                        try
                        {
                            title = doc.Title;
                        }
                        catch { }

                        ProgressCoordinator.UpdateProgress($"Extracting from: {title}");

                        var elements = ExtractRevitElements(doc, scanFamilies, scanNested, selectedGroupings);
                        var hierarchy = StandardsHierarchyUtility.BuildHierarchy(elements);

                        var source = new ProjectStandardsSourceViewModel
                        {
                            DisplayName = title,
                            SourcePath = doc.PathName ?? "ActiveDoc",
                            IsRevitSource = true
                        };

                        foreach (var group in hierarchy)
                        {
                            source.SourceHierarchy.Add(group);
                        }

                        AvailableSources.Add(source);
                        SelectedSource = source;
                    }
                }
                finally
                {
                    ProgressCoordinator.Close();
                }
            }
        }

        private class ProgressReporter : IProgress<string>
        {
            private readonly Action<string> _reportAction;
            public ProgressReporter(Action<string> reportAction)
            {
                _reportAction = reportAction;
            }
            public void Report(string value)
            {
                _reportAction(value);
            }
        }

        internal List<ElementModel> ExtractRevitElements(Document doc, bool scanFamilies, bool scanNestedFamilies, List<string> selectedGroupings)
        {
            var list = new List<ElementModel>();
            if (doc == null) return list;

            // 1. Extract categories directly since they are not Elements and don't have nested dependencies
            if (selectedGroupings == null || selectedGroupings.Contains("Categories"))
            {
                var engine = _serializationEngine;
                foreach (Category cat in doc.Settings.Categories)
                {
                    if (ProgressCoordinator.IsCancelled()) break;
                    try
                    {
                        var model = engine.ExtractCategory(cat, doc, false);
                        if (model is CategoryModel categoryModel)
                        {
                            list.Add(categoryModel);
                        }
                    }
                    catch { }
                }
            }

            // 2. Gather all other root Elements
            var rootElements = GatherRootElements(doc, selectedGroupings);

            // 3. Extract recursively using the Orchestrator
            IProgress<string>? progress = null;
            if (!ProgressCoordinator.SuppressUI)
            {
                progress = new ProgressReporter(msg => ProgressCoordinator.UpdateStatus(msg));
            }

            var extractedPocos = _orchestrator.Extract(doc, rootElements, progress, false);

            foreach (var poco in extractedPocos)
            {
                if (poco is ElementModel elementModel)
                {
                    // Clone the model to construct a representation DTO without mutative side-effects on the original extracted model
                    var representation = (ElementModel)elementModel.Clone();
                    representation.Element = null;
                    representation.Document = null;
                    
                    list.Add(representation);
                }
            }

            return list;
        }

        private List<Element> GatherRootElements(Document doc, List<string> selectedGroupings)
        {
            var rootElements = new List<Element>();
            var seenIds = new HashSet<ElementId>();

            void TryGather<T>(string className = null) where T : Element
            {
                try
                {
                    if (ProgressCoordinator.IsCancelled()) return;
                    if (className != null && selectedGroupings != null && !selectedGroupings.Contains(className)) return;

                    var elements = new FilteredElementCollector(doc)
                        .OfClass(typeof(T))
                        .ToElements();
                    foreach (var elem in elements)
                    {
                        if (ProgressCoordinator.IsCancelled()) return;
                        if (elem != null && !seenIds.Contains(elem.Id))
                        {
                            if (elem is Autodesk.Revit.DB.View view)
                            {
                                string viewClass = view.IsTemplate ? "View Templates" : "Views";
                                if (selectedGroupings != null && !selectedGroupings.Contains(viewClass)) continue;
                            }

                            seenIds.Add(elem.Id);
                            rootElements.Add(elem);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error gathering {typeof(T).Name}: {ex}");
                }
            }

            // Gather always-supported system standards
            TryGather<LinePatternElement>("Line Patterns");
            TryGather<FillPatternElement>("Fill Patterns");
            TryGather<ParameterFilterElement>("Filters");
            TryGather<FilledRegionType>("Filled Region Types");
            TryGather<ParameterElement>("Shared Parameters");
            TryGather<BrowserOrganization>("Browser Organizations");

            // System / Host Object Types
            TryGather<WallType>("Wall Types");
            TryGather<FloorType>("Floor Types");
            TryGather<RoofType>("Roof Types");
            TryGather<CeilingType>("Ceiling Types");
            TryGather<BuildingPadType>("Host Object Types");
            TryGather<CurtainSystemType>("Curtain System Types");
            TryGather<MullionType>("Mullion Types");
            TryGather<Autodesk.Revit.DB.Architecture.FasciaType>("Fascia Types");
            TryGather<Autodesk.Revit.DB.Architecture.GutterType>("Gutter Types");
#if REVIT2022 || REVIT2023
            // ToposolidType not available in older versions
#elif REVIT2024 || REVIT2025
            try
            {
                if (!ProgressCoordinator.IsCancelled() && (selectedGroupings == null || selectedGroupings.Contains("Toposolid Types")))
                {
                    var toposolidType = typeof(Document).Assembly.GetType("Autodesk.Revit.DB.ToposolidType");
                    if (toposolidType != null)
                    {
                        var elements = new FilteredElementCollector(doc)
                            .OfClass(toposolidType)
                            .ToElements();
                        foreach (var elem in elements)
                        {
                            if (ProgressCoordinator.IsCancelled()) return rootElements;
                            if (elem != null && !seenIds.Contains(elem.Id))
                            {
                                seenIds.Add(elem.Id);
                                rootElements.Add(elem);
                            }
                        }
                    }
                }
            }
            catch { }
#else
            try
            {
                if (!ProgressCoordinator.IsCancelled() && (selectedGroupings == null || selectedGroupings.Contains("Toposolid Types")))
                {
                    var toposolidType = typeof(Document).Assembly.GetType("Autodesk.Revit.DB.ToposolidType");
                    if (toposolidType != null)
                    {
                        var elements = new FilteredElementCollector(doc)
                            .OfClass(toposolidType)
                            .ToElements();
                        foreach (var elem in elements)
                        {
                            if (ProgressCoordinator.IsCancelled()) return rootElements;
                            if (elem != null && !seenIds.Contains(elem.Id))
                            {
                                seenIds.Add(elem.Id);
                                rootElements.Add(elem);
                            }
                        }
                    }
                }
            }
            catch { }
#endif

            // View Types
            TryGather<ViewDrafting>();
            TryGather<ViewSection>();
            TryGather<ViewPlan>();
            TryGather<ViewSheet>();
            TryGather<ViewSchedule>();

            // Materials
            TryGather<Material>("Materials");

            // Dimension & Grid & Level Types
            TryGather<GridType>("Grid Types");
            TryGather<LevelType>("Level Types");

            // Annotation styles (only gather if selected)
            TryGather<DimensionType>("Dimension Types");
            TryGather<TextNoteType>("Text Note Types");
            TryGather<TextElementType>("Label Types");
            TryGather<ModelTextType>("Model Text Types");
            TryGather<SpotDimensionType>("Spot Dimension Types");

            // Family Symbols matching selected groupings
            if (selectedGroupings != null && selectedGroupings.Any())
            {
                try
                {
                    if (ProgressCoordinator.IsCancelled()) return rootElements;
                    var familySymbols = new FilteredElementCollector(doc)
                        .OfClass(typeof(FamilySymbol))
                        .Cast<FamilySymbol>();

                    foreach (var fs in familySymbols)
                    {
                        if (ProgressCoordinator.IsCancelled()) return rootElements;
                        if (fs == null || seenIds.Contains(fs.Id)) continue;

                        bool include = false;
                        var category = fs.Category;
                        if (category == null) continue;

                        long catIdVal;
#if REVIT2022 || REVIT2023
                        catIdVal = category.Id.IntegerValue;
#else
                        catIdVal = category.Id.Value;
#endif

                        if (selectedGroupings.Contains("Label Types") && category.CategoryType == CategoryType.Annotation)
                        {
                            include = true;
                        }
                        if (selectedGroupings.Contains("Title Blocks") && catIdVal == (long)BuiltInCategory.OST_TitleBlocks)
                        {
                            include = true;
                        }
                        if (selectedGroupings.Contains("Detail Items") && catIdVal == (long)BuiltInCategory.OST_DetailComponents)
                        {
                            include = true;
                        }
                        if (selectedGroupings.Contains("Profiles") && catIdVal == (long)BuiltInCategory.OST_ProfileFamilies)
                        {
                            include = true;
                        }
                        if (selectedGroupings.Contains("Element Types"))
                        {
                            if (category.CategoryType != CategoryType.Annotation &&
                                catIdVal != (long)BuiltInCategory.OST_TitleBlocks &&
                                catIdVal != (long)BuiltInCategory.OST_DetailComponents &&
                                catIdVal != (long)BuiltInCategory.OST_ProfileFamilies)
                            {
                                include = true;
                            }
                        }

                        if (include)
                        {
                            seenIds.Add(fs.Id);
                            rootElements.Add(fs);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error gathering family symbols: {ex}");
                }
            }

            return rootElements;
        }

        private bool CanExecuteCloseSource(object parameter)
        {
            if (parameter is ProjectStandardsSourceViewModel source)
            {
                return source.DisplayName != "Default Firm Standard";
            }
            return false;
        }

        private void ExecuteCloseSource(object parameter)
        {
            if (parameter is ProjectStandardsSourceViewModel source)
            {
                // Clear source hierarchy and recursive items to disperse memory
                ClearSourceDataRecursive(source);
                AvailableSources.Remove(source);

                if (SelectedSource == source)
                {
                    SelectedSource = AvailableSources.FirstOrDefault();
                }
            }
        }

        private void ClearSourceDataRecursive(ProjectStandardsSourceViewModel source)
        {
            foreach (var group in source.SourceHierarchy)
            {
                ClearTreeItemRecursive(group);
            }
            source.SourceHierarchy.Clear();
        }

        private void ClearTreeItemRecursive(SourceTreeItemViewModel item)
        {
            foreach (var child in item.Children)
            {
                ClearTreeItemRecursive(child);
            }
            item.Children.Clear();
            item.Parent = null;
        }

        private void ApplySearchFilter()
        {
            foreach (var source in AvailableSources)
            {
                if (source != null)
                {
                    foreach (var group in source.SourceHierarchy)
                    {
                        UpdateVisibilityRecursive(group, SearchText);
                    }
                }
            }
        }

        private bool UpdateVisibilityRecursive(SourceTreeItemViewModel node, string query)
        {
            if (string.IsNullOrEmpty(query))
            {
                node.IsVisible = true;
                node.IsExpanded = node is StandardGroupModel;
                foreach (var child in node.Children)
                {
                    UpdateVisibilityRecursive(child, query);
                }
                return true;
            }

            bool anyChildVisible = false;
            foreach (var child in node.Children)
            {
                if (UpdateVisibilityRecursive(child, query))
                {
                    anyChildVisible = true;
                }
            }

            bool selfMatches = node.Name != null && node.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
            node.IsVisible = selfMatches || anyChildVisible;
            if (anyChildVisible && !string.IsNullOrEmpty(query))
            {
                node.IsExpanded = true;
            }
            return node.IsVisible;
        }

        internal string GetRevitLocalFileSaveLocation()
        {
            string? fallbackPath = null;
            object? target = _doc ?? (object?)_uiapp;
            
            if (target != null)
            {
                try
                {
                    var appProp = target.GetType().GetProperty("Application");
                    var appObj = appProp?.GetValue(target);
                    if (appObj != null)
                    {
                        var defaultPathProp = appObj.GetType().GetProperty("DefaultUserFilePath");
                        fallbackPath = defaultPathProp?.GetValue(appObj) as string;
                    }
                }
                catch (Exception) { }
            }

            if (string.IsNullOrEmpty(fallbackPath) || !Directory.Exists(fallbackPath))
            {
                fallbackPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            }

            return fallbackPath;
        }
    }
}
```

### File: StandardsManagement/Views/CategorySelectionControl.xaml
```xml
<UserControl x:Class="Synthetic.Modules.StandardsManagement.Views.CategorySelectionControl"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             Background="Transparent" Foreground="#F1F1F1">
    <StackPanel>
        <TextBlock Text="Family Categories Filter:" Foreground="#808080" Margin="0,0,0,5" FontSize="11"/>
        <ComboBox ItemsSource="{Binding CategoryFilters}" 
                  SelectedItem="{Binding SelectedCategoryFilter}" 
                  DisplayMemberPath="Name"
                  VerticalContentAlignment="Center"/>
    </StackPanel>
</UserControl>
```

### File: StandardsManagement/Views/CategorySelectionControl.xaml.cs
```csharp
using System.Windows.Controls;
using Synthetic.Modules.StandardsManagement.ViewModels;

namespace Synthetic.Modules.StandardsManagement.Views
{
    /// <summary>
    /// Interaction logic for CategorySelectionControl.xaml.
    /// </summary>
    public partial class CategorySelectionControl : System.Windows.Controls.UserControl
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="CategorySelectionControl"/> class.
        /// </summary>
        public CategorySelectionControl()
        {
            InitializeComponent();
        }
    }
}
```

### File: StandardsManagement/Views/ImportSummaryWindow.xaml
```xml
<Window x:Class="Synthetic.Modules.StandardsManagement.Views.ImportSummaryWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:local="clr-namespace:Synthetic.Shared.UI"
        Title="Import Styles Summary" Height="500" Width="550" MinHeight="400" MinWidth="500"
        WindowStartupLocation="CenterOwner"
        Style="{DynamicResource SyntheticWindowStyle}"
        local:WindowChromeBehavior.EnableWindowCommands="True">

    <Window.Resources>
        <ResourceDictionary>
            <ResourceDictionary.MergedDictionaries>
                <ResourceDictionary Source="../../../Shared/UI/SyntheticTheme.xaml"/>
            </ResourceDictionary.MergedDictionaries>
        </ResourceDictionary>
    </Window.Resources>
    
    <Grid Margin="15">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/> <!-- Title -->
            <RowDefinition Height="Auto"/> <!-- High-Level Stats Cards -->
            <RowDefinition Height="*"/>    <!-- DataGrid -->
            <RowDefinition Height="Auto"/> <!-- Action Buttons -->
        </Grid.RowDefinitions>

        <!-- Title -->
        <TextBlock Grid.Row="0" Text="Import Process Results" FontSize="16" FontWeight="Bold" Margin="0,0,0,15" Foreground="{DynamicResource Synthetic.Brushes.TextPrimary}"/>

        <!-- High-level stats panel -->
        <UniformGrid Grid.Row="1" Columns="5" Margin="0,0,0,15">
            <!-- Created Card -->
            <Border Background="{DynamicResource Synthetic.Brushes.ControlSurface}" BorderBrush="{DynamicResource Synthetic.Brushes.BorderNormal}" BorderThickness="1" CornerRadius="4" Margin="0,0,8,0" Padding="10">
                <StackPanel HorizontalAlignment="Center">
                    <TextBlock Text="{Binding CreatedCount}" FontSize="24" FontWeight="Bold" Foreground="{DynamicResource Synthetic.Brushes.Success}" HorizontalAlignment="Center"/>
                    <TextBlock Text="Elements Created" FontSize="11" Foreground="{DynamicResource Synthetic.Brushes.TextSecondary}" HorizontalAlignment="Center"/>
                </StackPanel>
            </Border>

            <!-- Updated Card -->
            <Border Background="{DynamicResource Synthetic.Brushes.ControlSurface}" BorderBrush="{DynamicResource Synthetic.Brushes.BorderNormal}" BorderThickness="1" CornerRadius="4" Margin="0,0,8,0" Padding="10">
                <StackPanel HorizontalAlignment="Center">
                    <TextBlock Text="{Binding UpdatedCount}" FontSize="24" FontWeight="Bold" Foreground="{DynamicResource Synthetic.Brushes.AccentActive}" HorizontalAlignment="Center"/>
                    <TextBlock Text="Elements Updated" FontSize="11" Foreground="{DynamicResource Synthetic.Brushes.TextSecondary}" HorizontalAlignment="Center"/>
                </StackPanel>
            </Border>

            <!-- Unchanged Card -->
            <Border Background="{DynamicResource Synthetic.Brushes.ControlSurface}" BorderBrush="{DynamicResource Synthetic.Brushes.BorderNormal}" BorderThickness="1" CornerRadius="4" Margin="0,0,8,0" Padding="10">
                <StackPanel HorizontalAlignment="Center">
                    <TextBlock Text="{Binding UnchangedCount}" FontSize="24" FontWeight="Bold" Foreground="{DynamicResource Synthetic.Brushes.TextSecondary}" HorizontalAlignment="Center"/>
                    <TextBlock Text="Elements Unchanged" FontSize="11" Foreground="{DynamicResource Synthetic.Brushes.TextSecondary}" HorizontalAlignment="Center"/>
                </StackPanel>
            </Border>

            <!-- Renamed Card -->
            <Border Background="{DynamicResource Synthetic.Brushes.ControlSurface}" BorderBrush="{DynamicResource Synthetic.Brushes.BorderNormal}" BorderThickness="1" CornerRadius="4" Margin="0,0,8,0" Padding="10">
                <StackPanel HorizontalAlignment="Center">
                    <TextBlock Text="{Binding RenamedCount}" FontSize="24" FontWeight="Bold" Foreground="{DynamicResource Synthetic.Brushes.Warning}" HorizontalAlignment="Center"/>
                    <TextBlock Text="Aliases Renamed" FontSize="11" Foreground="{DynamicResource Synthetic.Brushes.TextSecondary}" HorizontalAlignment="Center"/>
                </StackPanel>
            </Border>

            <!-- Failed Card -->
            <Border Background="{DynamicResource Synthetic.Brushes.ControlSurface}" BorderBrush="{DynamicResource Synthetic.Brushes.BorderNormal}" BorderThickness="1" CornerRadius="4" Padding="10">
                <StackPanel HorizontalAlignment="Center">
                    <TextBlock Text="{Binding ErrorsCount}" FontSize="24" FontWeight="Bold" Foreground="{DynamicResource Synthetic.Brushes.Error}" HorizontalAlignment="Center"/>
                    <TextBlock Text="Errors / Failed" FontSize="11" Foreground="{DynamicResource Synthetic.Brushes.TextSecondary}" HorizontalAlignment="Center"/>
                </StackPanel>
            </Border>
        </UniformGrid>

        <!-- Detailed Log Grid -->
        <DataGrid Grid.Row="2" ItemsSource="{Binding LogItems}" AutoGenerateColumns="False" 
                  CanUserAddRows="False" CanUserDeleteRows="False" IsReadOnly="True"
                  HeadersVisibility="Column" GridLinesVisibility="Horizontal"
                  RowHeaderWidth="0" Margin="0,0,0,15">
            <DataGrid.Columns>
                <!-- Action Column -->
                <DataGridTemplateColumn Header="Action" Width="1.2*">
                    <DataGridTemplateColumn.CellTemplate>
                        <DataTemplate>
                            <TextBlock Text="{Binding Action}" FontWeight="Bold" Padding="8,4" VerticalAlignment="Center">
                                <TextBlock.Style>
                                    <Style TargetType="TextBlock">
                                        <Style.Triggers>
                                            <DataTrigger Binding="{Binding Action}" Value="Created">
                                                <Setter Property="Foreground" Value="{DynamicResource Synthetic.Brushes.Success}"/>
                                            </DataTrigger>
                                            <DataTrigger Binding="{Binding Action}" Value="Updated">
                                                <Setter Property="Foreground" Value="{DynamicResource Synthetic.Brushes.AccentActive}"/>
                                            </DataTrigger>
                                            <DataTrigger Binding="{Binding Action}" Value="Renamed">
                                                <Setter Property="Foreground" Value="{DynamicResource Synthetic.Brushes.Warning}"/>
                                            </DataTrigger>
                                            <DataTrigger Binding="{Binding Action}" Value="Failed">
                                                <Setter Property="Foreground" Value="{DynamicResource Synthetic.Brushes.Error}"/>
                                            </DataTrigger>
                                            <DataTrigger Binding="{Binding Action}" Value="Unchanged">
                                                <Setter Property="Foreground" Value="{DynamicResource Synthetic.Brushes.TextSecondary}"/>
                                            </DataTrigger>
                                            <DataTrigger Binding="{Binding Action}" Value="Canceled">
                                                <Setter Property="Foreground" Value="{DynamicResource Synthetic.Brushes.TextSecondary}"/>
                                            </DataTrigger>
                                        </Style.Triggers>
                                    </Style>
                                </TextBlock.Style>
                            </TextBlock>
                        </DataTemplate>
                    </DataGridTemplateColumn.CellTemplate>
                </DataGridTemplateColumn>

                <!-- Class Column -->
                <DataGridTextColumn Header="Class" Binding="{Binding Class}" Width="1.5*">
                    <DataGridTextColumn.ElementStyle>
                        <Style TargetType="TextBlock">
                            <Setter Property="Padding" Value="8,4"/>
                            <Setter Property="VerticalAlignment" Value="Center"/>
                            <Setter Property="Foreground" Value="{DynamicResource Synthetic.Brushes.TextSecondary}"/>
                        </Style>
                    </DataGridTextColumn.ElementStyle>
                </DataGridTextColumn>

                <!-- ElementName Column -->
                <DataGridTextColumn Header="Element Name" Binding="{Binding ElementName}" Width="2*">
                    <DataGridTextColumn.ElementStyle>
                        <Style TargetType="TextBlock">
                            <Setter Property="Padding" Value="8,4"/>
                            <Setter Property="VerticalAlignment" Value="Center"/>
                            <Setter Property="Foreground" Value="{DynamicResource Synthetic.Brushes.TextPrimary}"/>
                        </Style>
                    </DataGridTextColumn.ElementStyle>
                </DataGridTextColumn>

                <!-- Message Column -->
                <DataGridTextColumn Header="Details / Message" Binding="{Binding Message}" Width="3.5*">
                    <DataGridTextColumn.ElementStyle>
                        <Style TargetType="TextBlock">
                            <Setter Property="Padding" Value="8,4"/>
                            <Setter Property="VerticalAlignment" Value="Center"/>
                            <Setter Property="Foreground" Value="{DynamicResource Synthetic.Brushes.TextSecondary}"/>
                            <Setter Property="TextWrapping" Value="Wrap"/>
                        </Style>
                    </DataGridTextColumn.ElementStyle>
                </DataGridTextColumn>
            </DataGrid.Columns>
        </DataGrid>

        <!-- Action Buttons -->
        <StackPanel Grid.Row="3" Orientation="Horizontal" HorizontalAlignment="Right">
            <Button Content="Export Log" Command="{Binding ExportLogCommand}" Margin="0,0,8,0"/>
            <Button Content="Close" Click="CloseButton_Click"/>
        </StackPanel>
    </Grid>
</Window>
```

### File: StandardsManagement/Views/ImportSummaryWindow.xaml.cs
```csharp
using System;
using System.Windows;

using Synthetic.Shared.UI;
using Synthetic.Core;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;
using Synthetic.Modules.StandardsManagement.ViewModels;

namespace Synthetic.Modules.StandardsManagement.Views
{
    /// <summary>
    /// Interaction logic for ImportSummaryWindow.xaml.
    /// </summary>
    public partial class ImportSummaryWindow : Window
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ImportSummaryWindow"/> class.
        /// </summary>
        /// <param name="parentMainWindowHandle">The handle to the main Revit application window, used to set the owner of this window.</param>
        /// <param name="viewModel">The view model associated with this summary window.</param>
        public ImportSummaryWindow(IntPtr parentMainWindowHandle, object viewModel)
        {
            InitializeComponent();
            RevitWindowHelper.SetOwner(this, parentMainWindowHandle);
            DataContext = viewModel;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}
```

### File: StandardsManagement/Views/NestedDataEditorWindow.xaml
```xml
<Window x:Class="Synthetic.Modules.StandardsManagement.Views.NestedDataEditorWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:vm="clr-namespace:Synthetic.Modules.StandardsManagement.ViewModels"
        xmlns:local="clr-namespace:Synthetic.Shared.UI"
        Title="Nested Data Editor" 
        Height="600" Width="1050" MinHeight="400" MinWidth="850"
        WindowStartupLocation="CenterOwner"
        Style="{DynamicResource SyntheticWindowStyle}"
        local:WindowChromeBehavior.EnableWindowCommands="True">

    <Window.Resources>
        <ResourceDictionary>
            <ResourceDictionary.MergedDictionaries>
                <ResourceDictionary Source="../../../Shared/UI/SyntheticTheme.xaml"/>
            </ResourceDictionary.MergedDictionaries>
            
            <BooleanToVisibilityConverter x:Key="BooleanToVisibilityConverter" />

            <!-- Common ComboBox style for DataGrid cells -->
            <Style x:Key="CellComboStyle" TargetType="ComboBox" BasedOn="{StaticResource {x:Type ComboBox}}">
                <Setter Property="Padding" Value="4,2"/>
                <Setter Property="VerticalAlignment" Value="Center"/>
                <Setter Property="IsEditable" Value="True"/>
                <Setter Property="IsReadOnly" Value="True"/>
            </Style>

            <!-- Common CheckBox style for DataGrid cells -->
            <Style x:Key="CellCheckStyle" TargetType="CheckBox" BasedOn="{StaticResource {x:Type CheckBox}}">
                <Setter Property="HorizontalAlignment" Value="Center"/>
                <Setter Property="VerticalAlignment" Value="Center"/>
            </Style>

            <!-- Common TextBox style for DataGrid cells -->
            <Style x:Key="CellTextStyle" TargetType="TextBox" BasedOn="{StaticResource {x:Type TextBox}}">
                <Setter Property="Padding" Value="4,2"/>
                <Setter Property="VerticalAlignment" Value="Center"/>
            </Style>
        </ResourceDictionary>
    </Window.Resources>

    <Grid Margin="15">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/> <!-- Title / Description -->
            <RowDefinition Height="*"/>    <!-- Editor Workspace -->
            <RowDefinition Height="Auto"/> <!-- Footer actions -->
        </Grid.RowDefinitions>

        <!-- Header -->
        <TextBlock Grid.Row="0" Text="Edit Nested Data Properties" FontSize="16" FontWeight="SemiBold" Foreground="{DynamicResource Synthetic.Brushes.TextPrimary}" Margin="0,0,0,12"/>

        <!-- Workspace area -->
        <Grid Grid.Row="1" Margin="0,0,0,15">

            <!-- MODE 1: Compound Structure Layers -->
            <DataGrid ItemsSource="{Binding Layers}" 
                      Visibility="{Binding IsLayersMode, Converter={StaticResource BooleanToVisibilityConverter}}">
                <DataGrid.Columns>
                    <!-- Function -->
                    <DataGridTemplateColumn Header="Function" Width="2*">
                        <DataGridTemplateColumn.CellTemplate>
                            <DataTemplate>
                                <TextBox Text="{Binding Function, UpdateSourceTrigger=PropertyChanged}" Style="{StaticResource CellTextStyle}"/>
                            </DataTemplate>
                        </DataGridTemplateColumn.CellTemplate>
                    </DataGridTemplateColumn>

                    <!-- Priority (Revit 2026+) -->
                    <DataGridTemplateColumn x:Name="PriorityColumn" Header="Priority" Width="1*">
                        <DataGridTemplateColumn.CellTemplate>
                            <DataTemplate>
                                <TextBox Text="{Binding Priority, UpdateSourceTrigger=PropertyChanged}" Style="{StaticResource CellTextStyle}"/>
                            </DataTemplate>
                        </DataGridTemplateColumn.CellTemplate>
                    </DataGridTemplateColumn>

                    <!-- Material -->
                    <DataGridTemplateColumn Header="Material" Width="3*">
                        <DataGridTemplateColumn.CellTemplate>
                            <DataTemplate>
                                <ComboBox ItemsSource="{Binding MaterialsList}" 
                                          SelectedItem="{Binding SelectedMaterial, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}" 
                                          IsEditable="True"
                                          IsReadOnly="True"
                                          Style="{StaticResource CellComboStyle}"/>
                            </DataTemplate>
                        </DataGridTemplateColumn.CellTemplate>
                    </DataGridTemplateColumn>

                    <!-- Width / Thickness -->
                    <DataGridTemplateColumn Header="Thickness" Width="1.5*">
                        <DataGridTemplateColumn.CellTemplate>
                            <DataTemplate>
                                <TextBox Text="{Binding Width, UpdateSourceTrigger=PropertyChanged}" Style="{StaticResource CellTextStyle}"/>
                            </DataTemplate>
                        </DataGridTemplateColumn.CellTemplate>
                    </DataGridTemplateColumn>

                    <!-- Wraps -->
                    <DataGridTemplateColumn Header="Wraps" Width="1*">
                        <DataGridTemplateColumn.CellTemplate>
                            <DataTemplate>
                                <CheckBox IsChecked="{Binding LayerCapFlag, UpdateSourceTrigger=PropertyChanged}" Style="{StaticResource CellCheckStyle}"/>
                            </DataTemplate>
                        </DataGridTemplateColumn.CellTemplate>
                    </DataGridTemplateColumn>

                    <!-- Structural -->
                    <DataGridTemplateColumn Header="Structural" Width="1.2*">
                        <DataGridTemplateColumn.CellTemplate>
                            <DataTemplate>
                                <CheckBox IsChecked="{Binding StructuralMaterial, UpdateSourceTrigger=PropertyChanged}" Style="{StaticResource CellCheckStyle}"/>
                            </DataTemplate>
                        </DataGridTemplateColumn.CellTemplate>
                    </DataGridTemplateColumn>
                </DataGrid.Columns>
            </DataGrid>

            <!-- MODE 2: Category Graphic Overrides -->
            <DataGrid ItemsSource="{Binding Overrides}" 
                      Visibility="{Binding IsOverridesMode, Converter={StaticResource BooleanToVisibilityConverter}}">
                <DataGrid.GroupStyle>
                    <GroupStyle>
                        <GroupStyle.ContainerStyle>
                            <Style TargetType="{x:Type GroupItem}">
                                <Setter Property="Template">
                                    <Setter.Value>
                                        <ControlTemplate TargetType="{x:Type GroupItem}">
                                            <Border BorderBrush="{DynamicResource Synthetic.Brushes.BorderNormal}" BorderThickness="0,0,0,1" Margin="0,0,0,8">
                                                <Expander IsExpanded="True" Background="Transparent">
                                                    <Expander.Header>
                                                        <Border Background="{DynamicResource Synthetic.Brushes.ControlSurfaceLighter}" CornerRadius="3" Padding="8,4" Margin="0,2">
                                                            <TextBlock Text="{Binding Name}" Foreground="{DynamicResource Synthetic.Brushes.TextPrimary}" FontWeight="Bold" FontSize="13"/>
                                                        </Border>
                                                    </Expander.Header>
                                                    <ItemsPresenter />
                                                </Expander>
                                            </Border>
                                        </ControlTemplate>
                                    </Setter.Value>
                                </Setter>
                            </Style>
                        </GroupStyle.ContainerStyle>
                    </GroupStyle>
                </DataGrid.GroupStyle>
                <DataGrid.Columns>
                    <!-- Category Name -->
                    <DataGridTemplateColumn Header="Category" Width="2.5*" IsReadOnly="True">
                        <DataGridTemplateColumn.CellTemplate>
                            <DataTemplate>
                                <TextBlock Text="{Binding CategoryName}" VerticalAlignment="Center">
                                    <TextBlock.Style>
                                        <Style TargetType="TextBlock">
                                            <Setter Property="Padding" Value="6,4"/>
                                            <Setter Property="Foreground" Value="{DynamicResource Synthetic.Brushes.TextPrimary}"/>
                                            <Setter Property="FontWeight" Value="SemiBold"/>
                                            <Style.Triggers>
                                                <DataTrigger Binding="{Binding IsSubcategory}" Value="True">
                                                    <Setter Property="Padding" Value="24,4,6,4"/>
                                                    <Setter Property="Foreground" Value="{DynamicResource Synthetic.Brushes.TextSecondary}"/>
                                                    <Setter Property="FontWeight" Value="Normal"/>
                                                    <Setter Property="FontStyle" Value="Italic"/>
                                                </DataTrigger>
                                            </Style.Triggers>
                                        </Style>
                                    </TextBlock.Style>
                                </TextBlock>
                            </DataTemplate>
                        </DataGridTemplateColumn.CellTemplate>
                    </DataGridTemplateColumn>

                    <!-- Visible -->
                    <DataGridTemplateColumn Header="Visible" Width="0.8*">
                        <DataGridTemplateColumn.CellTemplate>
                            <DataTemplate>
                                <CheckBox IsChecked="{Binding IsVisible, UpdateSourceTrigger=PropertyChanged}" Style="{StaticResource CellCheckStyle}"/>
                            </DataTemplate>
                        </DataGridTemplateColumn.CellTemplate>
                    </DataGridTemplateColumn>

                    <!-- Halftone -->
                    <DataGridTemplateColumn Header="Halftone" Width="0.8*">
                        <DataGridTemplateColumn.CellTemplate>
                            <DataTemplate>
                                <CheckBox IsChecked="{Binding Halftone, UpdateSourceTrigger=PropertyChanged}" Style="{StaticResource CellCheckStyle}"/>
                            </DataTemplate>
                        </DataGridTemplateColumn.CellTemplate>
                    </DataGridTemplateColumn>

                    <!-- Transparency -->
                    <DataGridTemplateColumn Header="Transparency" Width="1*">
                        <DataGridTemplateColumn.CellTemplate>
                            <DataTemplate>
                                <TextBox Text="{Binding Transparency, UpdateSourceTrigger=PropertyChanged}" Style="{StaticResource CellTextStyle}"/>
                            </DataTemplate>
                        </DataGridTemplateColumn.CellTemplate>
                    </DataGridTemplateColumn>

                    <!-- Projection Weight -->
                    <DataGridTemplateColumn Header="Proj Weight" Width="1*">
                        <DataGridTemplateColumn.CellTemplate>
                            <DataTemplate>
                                <TextBox Text="{Binding ProjectionLineWeight, UpdateSourceTrigger=PropertyChanged}" Style="{StaticResource CellTextStyle}"/>
                            </DataTemplate>
                        </DataGridTemplateColumn.CellTemplate>
                    </DataGridTemplateColumn>

                    <!-- Projection Color -->
                    <DataGridTemplateColumn Header="Proj Color" Width="1.2*">
                        <DataGridTemplateColumn.CellTemplate>
                            <DataTemplate>
                                <TextBox Text="{Binding ProjectionLineColor, UpdateSourceTrigger=PropertyChanged}" Style="{StaticResource CellTextStyle}"/>
                            </DataTemplate>
                        </DataGridTemplateColumn.CellTemplate>
                    </DataGridTemplateColumn>

                    <!-- Projection Pattern -->
                    <DataGridTemplateColumn Header="Proj Pattern" Width="1.8*">
                        <DataGridTemplateColumn.CellTemplate>
                            <DataTemplate>
                                <ComboBox ItemsSource="{Binding LineStylesList}" 
                                          SelectedItem="{Binding SelectedProjectionLinePattern, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}" 
                                          Style="{StaticResource CellComboStyle}"/>
                            </DataTemplate>
                        </DataGridTemplateColumn.CellTemplate>
                    </DataGridTemplateColumn>

                    <!-- Cut Weight -->
                    <DataGridTemplateColumn Header="Cut Weight" Width="1*">
                        <DataGridTemplateColumn.CellTemplate>
                            <DataTemplate>
                                <TextBox Text="{Binding CutLineWeight, UpdateSourceTrigger=PropertyChanged}" Style="{StaticResource CellTextStyle}"/>
                            </DataTemplate>
                        </DataGridTemplateColumn.CellTemplate>
                    </DataGridTemplateColumn>

                    <!-- Cut Color -->
                    <DataGridTemplateColumn Header="Cut Color" Width="1.2*">
                        <DataGridTemplateColumn.CellTemplate>
                            <DataTemplate>
                                <TextBox Text="{Binding CutLineColor, UpdateSourceTrigger=PropertyChanged}" Style="{StaticResource CellTextStyle}"/>
                            </DataTemplate>
                        </DataGridTemplateColumn.CellTemplate>
                    </DataGridTemplateColumn>

                    <!-- Cut Pattern -->
                    <DataGridTemplateColumn Header="Cut Pattern" Width="1.8*">
                        <DataGridTemplateColumn.CellTemplate>
                            <DataTemplate>
                                <ComboBox ItemsSource="{Binding LineStylesList}" 
                                          SelectedItem="{Binding SelectedCutLinePattern, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}" 
                                          Style="{StaticResource CellComboStyle}"/>
                            </DataTemplate>
                        </DataGridTemplateColumn.CellTemplate>
                    </DataGridTemplateColumn>

                    <!-- Detail Level -->
                    <DataGridTemplateColumn Header="Detail Level" Width="1.2*">
                        <DataGridTemplateColumn.CellTemplate>
                            <DataTemplate>
                                <ComboBox ItemsSource="{Binding DetailLevels}" 
                                          SelectedItem="{Binding DetailLevel, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}" 
                                          Style="{StaticResource CellComboStyle}"/>
                            </DataTemplate>
                        </DataGridTemplateColumn.CellTemplate>
                    </DataGridTemplateColumn>
                </DataGrid.Columns>
            </DataGrid>

            <!-- MODE 3: Generic Property Grid -->
            <DataGrid ItemsSource="{Binding PropertyGridProperties}" 
                      Visibility="{Binding IsPropertyGridMode, Converter={StaticResource BooleanToVisibilityConverter}}">
                <DataGrid.Columns>
                    <!-- Property Name -->
                    <DataGridTextColumn Header="Property Name" Binding="{Binding Name}" IsReadOnly="True" Width="2*">
                        <DataGridTextColumn.ElementStyle>
                            <Style TargetType="TextBlock">
                                <Setter Property="Padding" Value="6,4"/>
                                <Setter Property="Foreground" Value="{DynamicResource Synthetic.Brushes.TextSecondary}"/>
                            </Style>
                        </DataGridTextColumn.ElementStyle>
                    </DataGridTextColumn>

                    <!-- Value -->
                    <DataGridTemplateColumn Header="Value" Width="3*">
                        <DataGridTemplateColumn.CellTemplate>
                            <DataTemplate>
                                <Grid>
                                    <!-- ComboBox for ElementId references -->
                                    <ComboBox ItemsSource="{Binding AvailableReferences}"
                                              SelectedItem="{Binding Value, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}"
                                              IsEditable="True"
                                              IsReadOnly="True">
                                        <ComboBox.Style>
                                            <Style TargetType="ComboBox" BasedOn="{StaticResource CellComboStyle}">
                                                <Setter Property="Visibility" Value="Collapsed"/>
                                                <Style.Triggers>
                                                    <DataTrigger Binding="{Binding StorageType}" Value="ElementId">
                                                        <Setter Property="Visibility" Value="Visible"/>
                                                    </DataTrigger>
                                                </Style.Triggers>
                                            </Style>
                                        </ComboBox.Style>
                                    </ComboBox>

                                    <!-- TextBox for other types -->
                                    <TextBox Text="{Binding Value, UpdateSourceTrigger=PropertyChanged}">
                                        <TextBox.Style>
                                            <Style TargetType="TextBox" BasedOn="{StaticResource CellTextStyle}">
                                                <Setter Property="Visibility" Value="Visible"/>
                                                <Style.Triggers>
                                                    <DataTrigger Binding="{Binding StorageType}" Value="ElementId">
                                                        <Setter Property="Visibility" Value="Collapsed"/>
                                                    </DataTrigger>
                                                    <DataTrigger Binding="{Binding IsReadOnly}" Value="True">
                                                        <Setter Property="IsEnabled" Value="False"/>
                                                        <Setter Property="Background" Value="Transparent"/>
                                                        <Setter Property="BorderBrush" Value="Transparent"/>
                                                        <Setter Property="Foreground" Value="{DynamicResource Synthetic.Brushes.TextSecondary}"/>
                                                        <Setter Property="FontStyle" Value="Italic"/>
                                                    </DataTrigger>
                                                </Style.Triggers>
                                            </Style>
                                        </TextBox.Style>
                                    </TextBox>
                                </Grid>
                            </DataTemplate>
                        </DataGridTemplateColumn.CellTemplate>
                    </DataGridTemplateColumn>
                </DataGrid.Columns>
            </DataGrid>

        </Grid>

        <!-- Footer Actions -->
        <StackPanel Grid.Row="2" Orientation="Horizontal" HorizontalAlignment="Right">
            <Button Content="Save" Command="{Binding SaveCommand}" Style="{DynamicResource Synthetic.Styles.PrimaryButton}" Margin="0,0,10,0"/>
            <Button Content="Cancel" Command="{Binding CancelCommand}" Style="{DynamicResource Synthetic.Styles.SecondaryButton.Right}"/>
        </StackPanel>
    </Grid>
</Window>
```

### File: StandardsManagement/Views/NestedDataEditorWindow.xaml.cs
```csharp
using System;
using System.Windows;

using Synthetic.Shared.UI;
using Synthetic.Core;

using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;
using Synthetic.Modules.StandardsManagement.ViewModels;

namespace Synthetic.Modules.StandardsManagement.Views
{
    /// <summary>
    /// Interaction logic for NestedDataEditorWindow.xaml
    /// </summary>
    public partial class NestedDataEditorWindow : Window
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="NestedDataEditorWindow"/> class.
        /// </summary>
        /// <param name="ownerHandle">The handle to the main Revit application window, used to set the owner of this window.</param>
        /// <param name="vm">The view model associated with this editor window.</param>
        public NestedDataEditorWindow(IntPtr ownerHandle, NestedDataEditorViewModel vm)
        {
            InitializeComponent();
            DataContext = vm;

            if (ownerHandle != IntPtr.Zero)
            {
                RevitWindowHelper.SetOwner(this, ownerHandle);
            }

#if !REVIT2026
            try
            {
                PriorityColumn.Visibility = Visibility.Collapsed;
            }
            catch {}
#endif

            vm.CloseAction = () =>
            {
                try
                {
                    this.DialogResult = vm.DialogResult;
                }
                catch {}
                this.Close();
            };
        }
    }
}
```

### File: StandardsManagement/Views/ProjectStandardsDashboardWindow.xaml
```xml
<Window x:Class="Synthetic.Modules.StandardsManagement.Views.ProjectStandardsDashboardWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:vm="clr-namespace:Synthetic.Modules.StandardsManagement.ViewModels"
        xmlns:views="clr-namespace:Synthetic.Modules.StandardsManagement.Views"
        xmlns:m="clr-namespace:Synthetic.RevitDOM.Operations.Merge"
        xmlns:local="clr-namespace:Synthetic.Shared.UI"
        Title="Project Standards Dashboard"
        Height="700" Width="1100" MinHeight="600" MinWidth="900"
        WindowStartupLocation="CenterScreen"
        Style="{DynamicResource SyntheticWindowStyle}"
        local:WindowChromeBehavior.EnableWindowCommands="True">

    <Window.Resources>
        <ResourceDictionary>
            <ResourceDictionary.MergedDictionaries>
                <ResourceDictionary Source="../../../Shared/UI/SyntheticTheme.xaml"/>
            </ResourceDictionary.MergedDictionaries>
            <BooleanToVisibilityConverter x:Key="BooleanToVisibilityConverter"/>

            <!-- Edit Workspace Template -->
            <DataTemplate x:Key="WorkspaceEditTemplate">
                <Border BorderBrush="{DynamicResource Synthetic.Brushes.BorderNormal}" BorderThickness="1" 
                        Background="{DynamicResource Synthetic.Brushes.ControlSurface}" CornerRadius="4" Padding="15">
                    <Grid>
                        <Grid.RowDefinitions>
                            <RowDefinition Height="Auto"/> <!-- Title -->
                            <RowDefinition Height="Auto"/> <!-- Selection Info -->
                            <RowDefinition Height="*"/>    <!-- Writable inputs -->
                            <RowDefinition Height="Auto"/> <!-- Action Footer -->
                        </Grid.RowDefinitions>

                        <!-- Title -->
                        <TextBlock Grid.Row="0" Text="Edit Elements" FontWeight="Bold" FontSize="14" Margin="0,0,0,5"
                                   Foreground="{DynamicResource Synthetic.Brushes.TextPrimary}"/>

                        <!-- Selection Info -->
                        <TextBlock Grid.Row="1" Foreground="{DynamicResource Synthetic.Brushes.TextSecondary}" FontSize="12" Margin="0,0,0,15">
                            <Run Text="Editing "/>
                            <Run Text="{Binding DataContext.SelectedQueueItems.Count, RelativeSource={RelativeSource AncestorType=Window}, Mode=OneWay}" FontWeight="Bold"/>
                            <Run Text=" staged elements."/>
                        </TextBlock>

                        <!-- Input fields and Property Grid -->
                        <Grid Grid.Row="2">
                            <Grid.RowDefinitions>
                                <RowDefinition Height="Auto"/> <!-- Error Banner -->
                                <RowDefinition Height="Auto"/> <!-- Identity Header -->
                                <RowDefinition Height="*"/>    <!-- Properties DataGrid -->
                                <RowDefinition Height="Auto"/> <!-- Find & Replace -->
                            </Grid.RowDefinitions>

                            <!-- Error Banner -->
                            <Border Grid.Row="0" Background="{DynamicResource Synthetic.Brushes.ControlSurfaceLighter}"
                                    BorderBrush="{DynamicResource Synthetic.Brushes.Error}" BorderThickness="1"
                                    CornerRadius="3" Padding="10" Margin="0,0,0,15">
                                <Border.Style>
                                    <Style TargetType="Border">
                                        <Setter Property="Visibility" Value="Visible"/>
                                        <Style.Triggers>
                                            <DataTrigger Binding="{Binding DataContext.SelectedItemErrorMessage, RelativeSource={RelativeSource AncestorType=Window}}" Value="{x:Null}">
                                                <Setter Property="Visibility" Value="Collapsed"/>
                                            </DataTrigger>
                                            <DataTrigger Binding="{Binding DataContext.SelectedItemErrorMessage, RelativeSource={RelativeSource AncestorType=Window}}" Value="">
                                                <Setter Property="Visibility" Value="Collapsed"/>
                                            </DataTrigger>
                                        </Style.Triggers>
                                    </Style>
                                </Border.Style>
                                <Grid>
                                    <Grid.ColumnDefinitions>
                                        <ColumnDefinition Width="Auto"/>
                                        <ColumnDefinition Width="*"/>
                                    </Grid.ColumnDefinitions>
                                    <Path Grid.Column="0" Data="M12,2L2,22H22L12,2M12,17A1,1 0 1,1 11,18A1,1 0 0,1 12,17M11,10H13V15H11V10Z"
                                          Width="16" Height="16" Fill="{DynamicResource Synthetic.Brushes.Error}"
                                          VerticalAlignment="Top" Margin="0,0,8,0"/>
                                    <StackPanel Grid.Column="1">
                                        <TextBlock Text="Execution Failure Details" FontWeight="Bold" FontSize="11"
                                                   Foreground="{DynamicResource Synthetic.Brushes.Error}" Margin="0,0,0,4"/>
                                        <TextBlock Text="{Binding DataContext.SelectedItemErrorMessage, RelativeSource={RelativeSource AncestorType=Window}}"
                                                   Foreground="{DynamicResource Synthetic.Brushes.TextPrimary}" TextWrapping="Wrap" FontSize="11"/>
                                    </StackPanel>
                                </Grid>
                            </Border>

                            <!-- Identity Header -->
                            <Border Grid.Row="1" BorderBrush="{DynamicResource Synthetic.Brushes.BorderNormal}" BorderThickness="1"
                                    Background="{DynamicResource Synthetic.Brushes.ControlSurface}" CornerRadius="4" Padding="15" Margin="0,0,0,15">
                                <Grid>
                                    <Grid.ColumnDefinitions>
                                        <ColumnDefinition Width="Auto"/>
                                        <ColumnDefinition Width="*"/>
                                    </Grid.ColumnDefinitions>
                                    <Grid.RowDefinitions>
                                        <RowDefinition Height="Auto"/>
                                        <RowDefinition Height="Auto"/>
                                        <RowDefinition Height="Auto"/>
                                    </Grid.RowDefinitions>

                                    <!-- Row 0: Name -->
                                    <TextBlock Grid.Row="0" Grid.Column="0" Text="Name:" Foreground="{DynamicResource Synthetic.Brushes.TextSecondary}"
                                               VerticalAlignment="Center" HorizontalAlignment="Right" Margin="0,0,10,10"/>
                                    <TextBox Grid.Row="0" Grid.Column="1" Text="{Binding DataContext.SelectedNameOrCount, RelativeSource={RelativeSource AncestorType=Window}, UpdateSourceTrigger=PropertyChanged}"
                                             Height="28" VerticalContentAlignment="Center" Margin="0,0,0,10">
                                        <TextBox.Style>
                                            <Style TargetType="TextBox" BasedOn="{StaticResource {x:Type TextBox}}">
                                                <Style.Triggers>
                                                    <DataTrigger Binding="{Binding DataContext.IsSingleElementSelected, RelativeSource={RelativeSource AncestorType=Window}}" Value="False">
                                                        <Setter Property="IsReadOnly" Value="True"/>
                                                        <Setter Property="BorderThickness" Value="0"/>
                                                        <Setter Property="Background" Value="Transparent"/>
                                                        <Setter Property="Focusable" Value="False"/>
                                                    </DataTrigger>
                                                </Style.Triggers>
                                            </Style>
                                        </TextBox.Style>
                                    </TextBox>

                                    <!-- Row 1: Class -->
                                    <TextBlock Grid.Row="1" Grid.Column="0" Text="Class:" Foreground="{DynamicResource Synthetic.Brushes.TextSecondary}"
                                               VerticalAlignment="Center" HorizontalAlignment="Right" Margin="0,0,10,10"/>
                                    <TextBlock Grid.Row="1" Grid.Column="1" Text="{Binding DataContext.SelectedDisplayClass, RelativeSource={RelativeSource AncestorType=Window}}"
                                               Foreground="{DynamicResource Synthetic.Brushes.TextPrimary}" TextWrapping="Wrap"
                                               VerticalAlignment="Center" Margin="5,0,0,10"/>

                                    <!-- Row 2: Aliases -->
                                    <TextBlock Grid.Row="2" Grid.Column="0" Text="Aliases:" Foreground="{DynamicResource Synthetic.Brushes.TextSecondary}"
                                               VerticalAlignment="Center" HorizontalAlignment="Right" Margin="0,0,10,0"/>
                                    <TextBox Grid.Row="2" Grid.Column="1" Text="{Binding DataContext.SelectedAliasesString, RelativeSource={RelativeSource AncestorType=Window}, UpdateSourceTrigger=PropertyChanged}"
                                             Height="28" VerticalContentAlignment="Center" Margin="0,0,0,0">
                                        <TextBox.Style>
                                            <Style TargetType="TextBox" BasedOn="{StaticResource {x:Type TextBox}}">
                                                <Style.Triggers>
                                                    <DataTrigger Binding="{Binding DataContext.IsSingleElementSelected, RelativeSource={RelativeSource AncestorType=Window}}" Value="False">
                                                        <Setter Property="IsEnabled" Value="False"/>
                                                    </DataTrigger>
                                                </Style.Triggers>
                                            </Style>
                                        </TextBox.Style>
                                    </TextBox>
                                </Grid>
                            </Border>

                            <!-- Properties DataGrid -->
                            <DataGrid Grid.Row="2"
                                      ItemsSource="{Binding DataContext.DisplayParameters, RelativeSource={RelativeSource AncestorType=Window}}"
                                      SelectionMode="Single"
                                      AutoGenerateColumns="False"
                                      CanUserAddRows="False"
                                      CanUserDeleteRows="False"
                                      GridLinesVisibility="Horizontal"
                                      HeadersVisibility="Column">
                                <DataGrid.Columns>
                                    <!-- Parameter Name -->
                                    <DataGridTextColumn Header="Parameter Name"
                                                        Binding="{Binding Name}"
                                                        IsReadOnly="True"
                                                        Width="1.2*">
                                        <DataGridTextColumn.ElementStyle>
                                            <Style TargetType="TextBlock">
                                                <Setter Property="Padding"          Value="10,6"/>
                                                <Setter Property="VerticalAlignment" Value="Center"/>
                                                <Setter Property="Foreground"       Value="{DynamicResource Synthetic.Brushes.TextPrimary}"/>
                                                <Setter Property="FontWeight"       Value="Medium"/>
                                            </Style>
                                        </DataGridTextColumn.ElementStyle>
                                    </DataGridTextColumn>

                                    <!-- Parameter Value -->
                                    <DataGridTemplateColumn Header="Value" Width="2*">
                                        <DataGridTemplateColumn.CellTemplate>
                                            <DataTemplate>
                                                <Grid>
                                                    <!-- Default TextBox -->
                                                    <TextBox x:Name="DefaultValueBox"
                                                             Text="{Binding Value, UpdateSourceTrigger=PropertyChanged, ValidatesOnNotifyDataErrors=True}"
                                                             Padding="6,2" Margin="4,2" VerticalAlignment="Center">
                                                        <TextBox.Style>
                                                            <Style TargetType="TextBox" BasedOn="{StaticResource {x:Type TextBox}}">
                                                                <Setter Property="Visibility" Value="Visible"/>
                                                                <Style.Triggers>
                                                                    <DataTrigger Binding="{Binding IsReadOnly}" Value="True">
                                                                        <Setter Property="IsEnabled"   Value="False"/>
                                                                        <Setter Property="Background"  Value="Transparent"/>
                                                                        <Setter Property="Foreground"  Value="{DynamicResource Synthetic.Brushes.TextSecondary}"/>
                                                                        <Setter Property="BorderBrush" Value="Transparent"/>
                                                                        <Setter Property="FontStyle"   Value="Italic"/>
                                                                    </DataTrigger>
                                                                    <DataTrigger Binding="{Binding StorageType}" Value="ElementId">
                                                                        <Setter Property="Visibility" Value="Collapsed"/>
                                                                    </DataTrigger>
                                                                    <DataTrigger Binding="{Binding StorageType}" Value="ComplexNested">
                                                                        <Setter Property="Visibility" Value="Collapsed"/>
                                                                    </DataTrigger>
                                                                </Style.Triggers>
                                                            </Style>
                                                        </TextBox.Style>
                                                    </TextBox>

                                                    <!-- ElementId ComboBox -->
                                                    <ComboBox x:Name="ReferenceCombo"
                                                              ItemsSource="{Binding AvailableReferences}"
                                                              SelectedItem="{Binding Value, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}"
                                                              IsEditable="True" IsReadOnly="True"
                                                              Padding="6,2" Margin="4,2" VerticalAlignment="Center">
                                                        <ComboBox.Style>
                                                            <Style TargetType="ComboBox" BasedOn="{StaticResource {x:Type ComboBox}}">
                                                                <Setter Property="Visibility" Value="Collapsed"/>
                                                                <Style.Triggers>
                                                                    <DataTrigger Binding="{Binding StorageType}" Value="ElementId">
                                                                        <Setter Property="Visibility" Value="Visible"/>
                                                                    </DataTrigger>
                                                                    <DataTrigger Binding="{Binding IsReadOnly}" Value="True">
                                                                        <Setter Property="IsEnabled" Value="False"/>
                                                                    </DataTrigger>
                                                                </Style.Triggers>
                                                            </Style>
                                                        </ComboBox.Style>
                                                    </ComboBox>

                                                    <!-- ComplexNested Button -->
                                                    <Button x:Name="NestedEditButton"
                                                            Content="[ Edit Nested Data... ]"
                                                            Command="{Binding EditNestedDataCommand}"
                                                            Padding="6,2" Margin="4,2" VerticalAlignment="Center">
                                                        <Button.Style>
                                                            <Style TargetType="Button" BasedOn="{StaticResource {x:Type Button}}">
                                                                <Setter Property="Visibility" Value="Collapsed"/>
                                                                <Style.Triggers>
                                                                    <DataTrigger Binding="{Binding StorageType}" Value="ComplexNested">
                                                                        <Setter Property="Visibility" Value="Visible"/>
                                                                    </DataTrigger>
                                                                </Style.Triggers>
                                                            </Style>
                                                        </Button.Style>
                                                    </Button>
                                                </Grid>
                                            </DataTemplate>
                                        </DataGridTemplateColumn.CellTemplate>
                                    </DataGridTemplateColumn>
                                </DataGrid.Columns>
                            </DataGrid>

                            <!-- Find & Replace -->
                            <Expander Grid.Row="3" Header="Find &amp; Replace" IsExpanded="False" Margin="0,10,0,15">
                                <StackPanel Margin="0,10,0,0">
                                    <TextBlock Text="Find Text" Foreground="{DynamicResource Synthetic.Brushes.TextPrimary}" Margin="0,0,0,4"/>
                                    <TextBox Text="{Binding DataContext.FindText, RelativeSource={RelativeSource AncestorType=Window}, UpdateSourceTrigger=PropertyChanged}" 
                                             Height="28" Margin="0,0,0,12" VerticalContentAlignment="Center"/>

                                    <TextBlock Text="Replace With" Foreground="{DynamicResource Synthetic.Brushes.TextPrimary}" Margin="0,0,0,4"/>
                                    <TextBox Text="{Binding DataContext.ReplaceText, RelativeSource={RelativeSource AncestorType=Window}, UpdateSourceTrigger=PropertyChanged}" 
                                             Height="28" Margin="0,0,0,12" VerticalContentAlignment="Center"/>

                                    <TextBlock Text="Search Scope" Foreground="{DynamicResource Synthetic.Brushes.TextPrimary}" Margin="0,0,0,4"/>
                                    <ComboBox SelectedItem="{Binding DataContext.FindReplaceScope, RelativeSource={RelativeSource AncestorType=Window}}"
                                              ItemsSource="{Binding DataContext.AvailableSearchScopes, RelativeSource={RelativeSource AncestorType=Window}}"
                                              Height="28" Margin="0,0,0,15" VerticalContentAlignment="Center"/>

                                    <Button Content="Find &amp; Replace" 
                                            Command="{Binding DataContext.BatchFindReplaceCommand, RelativeSource={RelativeSource AncestorType=Window}}"
                                            Style="{DynamicResource Synthetic.Styles.NeutralButton}" HorizontalAlignment="Left" Width="120" Height="28"/>
                                </StackPanel>
                            </Expander>
                        </Grid>

                        <!-- Action Footer -->
                        <Border Grid.Row="3" BorderThickness="0,1,0,0" BorderBrush="{DynamicResource Synthetic.Brushes.BorderNormal}" Padding="0,10,0,0">
                            <StackPanel Orientation="Horizontal" HorizontalAlignment="Right">
                                <Button Content="Merge" 
                                        Command="{Binding DataContext.MergeQueueCommand, RelativeSource={RelativeSource AncestorType=Window}}"
                                        CommandParameter="{Binding DataContext.SelectedQueueItems, RelativeSource={RelativeSource AncestorType=Window}}"
                                        Style="{DynamicResource Synthetic.Styles.NeutralButton}" Margin="0,0,8,0" Width="75"/>
                                <Button Content="Cancel" 
                                        Command="{Binding DataContext.CancelEditsCommand, RelativeSource={RelativeSource AncestorType=Window}}"
                                        Style="{DynamicResource Synthetic.Styles.NeutralButton}" Margin="0,0,8,0" Width="75"/>
                                <Button Content="Apply" 
                                        Command="{Binding DataContext.ApplyEditsCommand, RelativeSource={RelativeSource AncestorType=Window}}"
                                        Style="{DynamicResource Synthetic.Styles.PrimaryButton.Right}" Width="75"/>
                            </StackPanel>
                        </Border>
                    </Grid>
                </Border>
            </DataTemplate>

            <!-- Diff Workspace Template -->
            <DataTemplate x:Key="WorkspaceDiffTemplate">
                <Border BorderBrush="{DynamicResource Synthetic.Brushes.BorderNormal}" BorderThickness="1" 
                        Background="{DynamicResource Synthetic.Brushes.ControlSurface}" CornerRadius="4" Padding="15">
                    <Grid>
                        <Grid.RowDefinitions>
                            <RowDefinition Height="Auto"/> <!-- Title -->
                            <RowDefinition Height="Auto"/> <!-- Selection Info -->
                            <RowDefinition Height="*"/>    <!-- Comparison list -->
                            <RowDefinition Height="Auto"/> <!-- Action Footer -->
                        </Grid.RowDefinitions>

                        <!-- Title -->
                        <TextBlock Grid.Row="0" Text="Resolve Parameter Conflicts" FontWeight="Bold" FontSize="14" Margin="0,0,0,5"
                                   Foreground="{DynamicResource Synthetic.Brushes.TextPrimary}"/>

                        <!-- Selection Info -->
                        <TextBlock Grid.Row="1" Foreground="{DynamicResource Synthetic.Brushes.TextSecondary}" FontSize="12" Margin="0,0,0,15">
                            <Run Text="Comparing staged elements with active Revit document."/>
                        </TextBlock>

                        <!-- Comparison Content: Scrollable list of clusters and parameter conflicts -->
                        <ScrollViewer Grid.Row="2" VerticalScrollBarVisibility="Auto" Margin="0,0,0,10">
                            <ScrollViewer.Style>
                                <Style TargetType="ScrollViewer">
                                    <Setter Property="Visibility" Value="Visible"/>
                                    <Style.Triggers>
                                        <DataTrigger Binding="{Binding DataContext.ActiveDiffClusters.Count, RelativeSource={RelativeSource AncestorType=Window}}" Value="0">
                                            <Setter Property="Visibility" Value="Collapsed"/>
                                        </DataTrigger>
                                    </Style.Triggers>
                                </Style>
                            </ScrollViewer.Style>
                            <ItemsControl ItemsSource="{Binding DataContext.ActiveDiffClusters, RelativeSource={RelativeSource AncestorType=Window}}">
                                <ItemsControl.ItemTemplate>
                                    <DataTemplate DataType="{x:Type m:DuplicateClusterModel}">
                                        <StackPanel Margin="0,0,0,15">
                                            <!-- Cluster Header -->
                                            <TextBlock Text="{Binding ClusterName}" FontWeight="Bold" 
                                                       Foreground="{DynamicResource Synthetic.Brushes.TextPrimary}" Margin="0,0,5,5"/>
                                            
                                            <!-- List of Type Mappings inside the cluster -->
                                            <ItemsControl ItemsSource="{Binding TypeMappings}">
                                                <ItemsControl.ItemTemplate>
                                                    <DataTemplate DataType="{x:Type m:TypeMappingModel}">
                                                        <StackPanel Margin="10,0,0,10">
                                                            <!-- Element Name Header -->
                                                            <TextBlock Text="{Binding TargetType.Name}" FontWeight="SemiBold" 
                                                                       Foreground="{DynamicResource Synthetic.Brushes.TextSecondary}" Margin="0,0,0,6"/>

                                                            <!-- Parameter conflict rows -->
                                                            <ItemsControl ItemsSource="{Binding ParameterResolutions}">
                                                                <ItemsControl.ItemTemplate>
                                                                    <DataTemplate DataType="{x:Type m:ParameterDiffRowModel}">
                                                                        <Border BorderBrush="{DynamicResource Synthetic.Brushes.BorderNormal}" 
                                                                                BorderThickness="1" CornerRadius="3" Padding="8" Margin="0,2">
                                                                            <Grid>
                                                                                <Grid.RowDefinitions>
                                                                                    <RowDefinition Height="Auto"/>
                                                                                    <RowDefinition Height="Auto"/>
                                                                                </Grid.RowDefinitions>
                                                                                
                                                                                <!-- Parameter name and conflict type -->
                                                                                <TextBlock Grid.Row="0" Text="{Binding ParameterName}" FontWeight="SemiBold" 
                                                                                           Foreground="{DynamicResource Synthetic.Brushes.TextPrimary}" Margin="0,0,0,6"/>
                                                                                
                                                                                <!-- Side by Side Values selection -->
                                                                                <Grid Grid.Row="1">
                                                                                    <Grid.ColumnDefinitions>
                                                                                        <ColumnDefinition Width="*"/>
                                                                                        <ColumnDefinition Width="*"/>
                                                                                    </Grid.ColumnDefinitions>
                                                                                    
                                                                                    <!-- Option 0: Live Document Value -->
                                                                                    <RadioButton Grid.Column="0" IsChecked="{Binding IsSourceWinning}" Margin="0,0,10,0">
                                                                                        <StackPanel>
                                                                                            <TextBlock Text="Local Document Value:" FontSize="10" Foreground="{DynamicResource Synthetic.Brushes.TextSecondary}"/>
                                                                                            <TextBlock Text="{Binding Options[0].DisplayText}" FontWeight="SemiBold" Foreground="{DynamicResource Synthetic.Brushes.Warning}"/>
                                                                                        </StackPanel>
                                                                                    </RadioButton>

                                                                                    <!-- Option 1: Source Value -->
                                                                                    <RadioButton Grid.Column="1" IsChecked="{Binding IsTargetWinning}">
                                                                                        <StackPanel>
                                                                                            <TextBlock Text="Source Value:" FontSize="10" Foreground="{DynamicResource Synthetic.Brushes.TextSecondary}"/>
                                                                                            <TextBlock Text="{Binding Options[1].DisplayText}" FontWeight="SemiBold" Foreground="{DynamicResource Synthetic.Brushes.Success}"/>
                                                                                        </StackPanel>
                                                                                    </RadioButton>
                                                                                </Grid>
                                                                            </Grid>
                                                                        </Border>
                                                                    </DataTemplate>
                                                                </ItemsControl.ItemTemplate>
                                                            </ItemsControl>
                                                        </StackPanel>
                                                    </DataTemplate>
                                                </ItemsControl.ItemTemplate>
                                            </ItemsControl>
                                        </StackPanel>
                                    </DataTemplate>
                                </ItemsControl.ItemTemplate>
                            </ItemsControl>
                        </ScrollViewer>

                        <!-- Fallback message when there are no conflicts -->
                        <TextBlock Grid.Row="2" Text="No parameter conflicts detected. Selected staged elements match the active document perfectly, or do not exist." 
                                   Foreground="{DynamicResource Synthetic.Brushes.TextSecondary}"
                                   HorizontalAlignment="Center" VerticalAlignment="Center" TextWrapping="Wrap" TextAlignment="Center" Margin="20">
                            <TextBlock.Style>
                                <Style TargetType="TextBlock">
                                    <Setter Property="Visibility" Value="Collapsed"/>
                                    <Style.Triggers>
                                        <DataTrigger Binding="{Binding DataContext.ActiveDiffClusters.Count, RelativeSource={RelativeSource AncestorType=Window}}" Value="0">
                                            <Setter Property="Visibility" Value="Visible"/>
                                        </DataTrigger>
                                    </Style.Triggers>
                                </Style>
                            </TextBlock.Style>
                        </TextBlock>

                        <!-- Action Footer -->
                        <Border Grid.Row="3" BorderThickness="0,1,0,0" BorderBrush="{DynamicResource Synthetic.Brushes.BorderNormal}" Padding="0,10,0,0">
                            <StackPanel Orientation="Horizontal" HorizontalAlignment="Right">
                                <Button Content="Cancel" 
                                        Command="{Binding DataContext.CancelEditsCommand, RelativeSource={RelativeSource AncestorType=Window}}"
                                        Style="{DynamicResource Synthetic.Styles.NeutralButton}" Margin="0,0,8,0" Width="75"/>
                                <Button Content="Apply Resolution" 
                                        Command="{Binding DataContext.ResolveConflictCommand, RelativeSource={RelativeSource AncestorType=Window}}"
                                        Style="{DynamicResource Synthetic.Styles.PrimaryButton.Right}" Width="120"/>
                            </StackPanel>
                        </Border>
                    </Grid>
                </Border>
            </DataTemplate>
        </ResourceDictionary>
    </Window.Resources>

    <Grid Margin="15">
        <Grid.ColumnDefinitions>
            <!-- Left Pane: Source Pane -->
            <ColumnDefinition Width="380" MinWidth="320"/>
            <!-- GridSplitter 1 -->
            <ColumnDefinition Width="5"/>
            <!-- Center Pane: Staging Queue -->
            <ColumnDefinition Width="*" MinWidth="300"/>
            <!-- GridSplitter 2 -->
            <ColumnDefinition Width="5"/>
            <!-- Right Pane: Sub-Workspaces -->
            <ColumnDefinition Width="*" MinWidth="300"/>
        </Grid.ColumnDefinitions>

        <!-- ================= LEFT COLUMN: SOURCE PANE ================= -->
        <Grid Grid.Column="0">
            <Grid.RowDefinitions>
                <RowDefinition Height="Auto"/> <!-- Title & Add buttons -->
                <RowDefinition Height="Auto"/> <!-- Search Box -->
                <RowDefinition Height="*"/>    <!-- Tab Control & Tree View -->
                <RowDefinition Height="Auto"/> <!-- Action Footer -->
            </Grid.RowDefinitions>

            <!-- Title & Add Buttons -->
            <Grid Grid.Row="0" Margin="0,0,0,10">
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="*"/>
                    <ColumnDefinition Width="Auto"/>
                </Grid.ColumnDefinitions>
                <TextBlock Text="STANDARDS SOURCE" 
                           FontWeight="Bold" FontSize="14" 
                           Foreground="{DynamicResource Synthetic.Brushes.TextPrimary}"
                           VerticalAlignment="Center"/>
                <StackPanel Grid.Column="1" Orientation="Horizontal">
                    <Button Content="+ File" Command="{Binding AddFileSourceCommand}" Margin="0,0,5,0"
                            ToolTip="Add custom JSON standards file source"/>
                    <Button Content="+ Model" Command="{Binding AddRevitModelCommand}"
                            ToolTip="Add active Revit document standards source"/>
                </StackPanel>
            </Grid>

            <!-- Search Bar -->
            <Grid Grid.Row="1" Margin="0,0,0,10">
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="Auto"/>
                    <ColumnDefinition Width="*"/>
                </Grid.ColumnDefinitions>
                <TextBlock Grid.Column="0" Text="Filter:" VerticalAlignment="Center" Margin="0,0,8,0" Foreground="{DynamicResource Synthetic.Brushes.TextSecondary}" FontSize="11"/>
                <TextBox Grid.Column="1" Text="{Binding SearchText, UpdateSourceTrigger=PropertyChanged}" 
                         Tag="Search standards..."
                         Height="26" VerticalContentAlignment="Center"
                         ToolTip="Enter query to filter the standards tree view by name"/>
            </Grid>

            <!-- Tabs and TreeView -->
            <TabControl Grid.Row="2" 
                        ItemsSource="{Binding AvailableSources}" 
                        SelectedItem="{Binding SelectedSource}"
                        Background="{DynamicResource Synthetic.Brushes.ControlSurface}"
                        BorderBrush="{DynamicResource Synthetic.Brushes.BorderNormal}">
                <TabControl.ItemTemplate>
                    <DataTemplate DataType="{x:Type vm:ProjectStandardsSourceViewModel}">
                        <StackPanel Orientation="Horizontal" Margin="2">
                            <TextBlock Text="{Binding DisplayName}" VerticalAlignment="Center" Margin="0,0,5,0"
                                       Foreground="{DynamicResource Synthetic.Brushes.TextPrimary}"/>
                            <!-- Close button -->
                            <Button Content="✕" 
                                    Command="{Binding DataContext.CloseSourceCommand, RelativeSource={RelativeSource AncestorType=Window}}"
                                    CommandParameter="{Binding}"
                                    Width="16" Height="16" FontSize="8" Padding="0" VerticalAlignment="Center"
                                    ToolTip="Close Tab">
                                <Button.Style>
                                    <Style TargetType="Button" BasedOn="{StaticResource Synthetic.Styles.NeutralButton}">
                                        <Style.Triggers>
                                            <DataTrigger Binding="{Binding DisplayName}" Value="Default Firm Standard">
                                                <Setter Property="Visibility" Value="Collapsed"/>
                                            </DataTrigger>
                                        </Style.Triggers>
                                    </Style>
                                </Button.Style>
                            </Button>
                        </StackPanel>
                    </DataTemplate>
                </TabControl.ItemTemplate>
                
                <TabControl.ContentTemplate>
                    <DataTemplate DataType="{x:Type vm:ProjectStandardsSourceViewModel}">
                        <Grid Margin="5">
                            <Grid.RowDefinitions>
                                <RowDefinition Height="Auto"/> <!-- Select All / Select None toolbar -->
                                <RowDefinition Height="*"/>    <!-- Tree View -->
                            </Grid.RowDefinitions>

                            <!-- Selection Toolbar -->
                            <StackPanel Grid.Row="0" Orientation="Horizontal" Margin="0,0,0,8">
                                <Button Content="Select All" Command="{Binding SelectAllCommand}" 
                                        Style="{DynamicResource Synthetic.Styles.NeutralButton}"
                                        Margin="0,0,8,0" Padding="8,2" FontSize="10"/>
                                <Button Content="Select None" Command="{Binding SelectNoneCommand}" 
                                        Style="{DynamicResource Synthetic.Styles.NeutralButton}"
                                        Padding="8,2" FontSize="10"/>
                            </StackPanel>

                            <!-- Tree View -->
                            <TreeView Grid.Row="1" 
                                      ItemsSource="{Binding SourceHierarchy}"
                                      Background="Transparent"
                                      BorderThickness="0">
                                <TreeView.ItemContainerStyle>
                                    <Style TargetType="TreeViewItem" BasedOn="{StaticResource {x:Type TreeViewItem}}">
                                        <Setter Property="IsExpanded" Value="{Binding IsExpanded, Mode=TwoWay}"/>
                                        <Setter Property="Visibility" Value="Visible"/>
                                        <Style.Triggers>
                                            <DataTrigger Binding="{Binding IsVisible}" Value="False">
                                                <Setter Property="Visibility" Value="Collapsed"/>
                                            </DataTrigger>
                                        </Style.Triggers>
                                    </Style>
                                </TreeView.ItemContainerStyle>
                                <TreeView.Resources>
                                    <HierarchicalDataTemplate DataType="{x:Type vm:StandardGroupModel}" ItemsSource="{Binding Children}">
                                        <StackPanel Orientation="Horizontal" Margin="0,2">
                                            <CheckBox IsChecked="{Binding IsChecked}" IsThreeState="True" Margin="0,0,5,0" Focusable="False"/>
                                            <TextBlock Text="{Binding Name}" Foreground="{DynamicResource Synthetic.Brushes.TextPrimary}" FontWeight="SemiBold"/>
                                        </StackPanel>
                                    </HierarchicalDataTemplate>
                                    <HierarchicalDataTemplate DataType="{x:Type vm:StandardClassModel}" ItemsSource="{Binding Children}">
                                        <StackPanel Orientation="Horizontal" Margin="0,2">
                                            <CheckBox IsChecked="{Binding IsChecked}" IsThreeState="True" Margin="0,0,5,0" Focusable="False"/>
                                            <TextBlock Text="{Binding Name}" Foreground="{DynamicResource Synthetic.Brushes.TextPrimary}"/>
                                        </StackPanel>
                                    </HierarchicalDataTemplate>
                                    <DataTemplate DataType="{x:Type vm:StandardElementModel}">
                                        <StackPanel Orientation="Horizontal" Margin="0,1">
                                            <CheckBox IsChecked="{Binding IsChecked}" Margin="0,0,5,0" Focusable="False"/>
                                            <TextBlock Text="{Binding Name}" Foreground="{DynamicResource Synthetic.Brushes.TextSecondary}"/>
                                        </StackPanel>
                                    </DataTemplate>
                                </TreeView.Resources>
                            </TreeView>
                        </Grid>
                    </DataTemplate>
                </TabControl.ContentTemplate>
            </TabControl>

            <!-- Footer Action Buttons -->
            <Border Grid.Row="3" BorderThickness="0,1,0,0" BorderBrush="{DynamicResource Synthetic.Brushes.BorderNormal}" Padding="0,10,0,0" Margin="0,10,0,0">
                <Grid>
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="*"/>
                        <ColumnDefinition Width="Auto"/>
                    </Grid.ColumnDefinitions>
                    
                    <StackPanel Grid.Column="1" Orientation="Horizontal">
                        <Button Content="Enforce" Command="{Binding PushToQueueCommand}" CommandParameter="Enforce" Margin="0,0,8,0"
                                Style="{DynamicResource Synthetic.Styles.PrimaryButton}" Width="85"/>
                        <Button Content="Save" Command="{Binding PushToQueueCommand}" CommandParameter="Save" Margin="0,0,8,0"
                                Style="{DynamicResource Synthetic.Styles.NeutralButton}" Width="85"/>
                        <Button Content="Save &amp; Enforce" Command="{Binding PushToQueueCommand}" CommandParameter="SaveAndEnforce"
                                Style="{DynamicResource Synthetic.Styles.NeutralButton.Right}" Width="120"/>
                    </StackPanel>
                </Grid>
            </Border>
        </Grid>

        <!-- Grid Splitter 1 -->
        <GridSplitter Grid.Column="1" HorizontalAlignment="Stretch" VerticalAlignment="Stretch" Background="{DynamicResource Synthetic.Brushes.BorderNormal}"/>

        <!-- ================= CENTER COLUMN: STAGING QUEUE PANE ================= -->
        <Grid Grid.Column="2">
            <Grid.RowDefinitions>
                <RowDefinition Height="Auto"/> <!-- Title -->
                <RowDefinition Height="*"/>    <!-- Queue ListBox -->
                <RowDefinition Height="Auto"/> <!-- Family processing panel -->
                <RowDefinition Height="Auto"/> <!-- Action Footer -->
            </Grid.RowDefinitions>

            <!-- Header -->
            <TextBlock Grid.Row="0" Text="Queue" FontWeight="Bold" FontSize="14" Margin="0,0,0,10"
                       Foreground="{DynamicResource Synthetic.Brushes.TextPrimary}"/>

            <!-- Grouped ListBox -->
            <ListBox Grid.Row="1" x:Name="QueueListBox"
                     ItemsSource="{Binding StagingQueueView}"
                     Background="Transparent"
                     BorderBrush="{DynamicResource Synthetic.Brushes.BorderNormal}"
                     BorderThickness="1"
                     SelectionMode="Extended"
                     Margin="0,0,0,10">
                <ListBox.GroupStyle>
                    <GroupStyle>
                        <GroupStyle.HeaderTemplate>
                            <DataTemplate>
                                <Border Background="{DynamicResource Synthetic.Brushes.ControlSurfaceLighter}"
                                        BorderBrush="{DynamicResource Synthetic.Brushes.BorderNormal}"
                                        BorderThickness="0,0,0,1"
                                        Padding="6,4">
                                    <TextBlock Text="{Binding Name}" FontWeight="SemiBold"
                                               Foreground="{DynamicResource Synthetic.Brushes.TextPrimary}"/>
                                </Border>
                            </DataTemplate>
                        </GroupStyle.HeaderTemplate>
                    </GroupStyle>
                </ListBox.GroupStyle>
                <ListBox.ItemTemplate>
                    <DataTemplate DataType="{x:Type vm:QueueItemModel}">
                        <Grid Margin="0,2">
                            <Grid.ColumnDefinitions>
                                <ColumnDefinition Width="*"/>
                                <ColumnDefinition Width="Auto"/>
                                <ColumnDefinition Width="Auto"/>
                            </Grid.ColumnDefinitions>
                            <StackPanel Grid.Column="0" Orientation="Horizontal" VerticalAlignment="Center">
                                <TextBlock Text="{Binding Name}"
                                           Foreground="{DynamicResource Synthetic.Brushes.TextSecondary}"
                                           VerticalAlignment="Center"/>
                                <!-- Dependency Origin Badge -->
                                <Border Margin="6,0,0,0" Padding="4,1" CornerRadius="3"
                                        Background="{DynamicResource Synthetic.Brushes.ControlSurfaceLighter}"
                                        BorderBrush="{DynamicResource Synthetic.Brushes.BorderNormal}"
                                        BorderThickness="1"
                                        VerticalAlignment="Center">
                                    <Border.Style>
                                        <Style TargetType="Border">
                                            <Setter Property="Visibility" Value="Visible"/>
                                            <Style.Triggers>
                                                <DataTrigger Binding="{Binding IsDependency}" Value="False">
                                                    <Setter Property="Visibility" Value="Collapsed"/>
                                                </DataTrigger>
                                            </Style.Triggers>
                                        </Style>
                                    </Border.Style>
                                    <TextBlock FontSize="9" Foreground="{DynamicResource Synthetic.Brushes.TextSecondary}" FontWeight="SemiBold" VerticalAlignment="Center">
                                        <Run Text="via "/>
                                        <Run Text="{Binding DependencyOrigin, Mode=OneWay}"/>
                                    </TextBlock>
                                </Border>
                            </StackPanel>
                            <Path Grid.Column="1" Data="M12,2L2,22H22L12,2M12,17A1,1 0 1,1 11,18A1,1 0 0,1 12,17M11,10H13V15H11V10Z"
                                  Width="12" Height="12" Fill="{DynamicResource Synthetic.Brushes.Error}"
                                  VerticalAlignment="Center" Margin="5,0,5,0"
                                  ToolTip="{Binding ErrorMessage}">
                                <Path.Style>
                                    <Style TargetType="Path">
                                        <Setter Property="Visibility" Value="Collapsed"/>
                                        <Style.Triggers>
                                            <DataTrigger Binding="{Binding HasError}" Value="True">
                                                <Setter Property="Visibility" Value="Visible"/>
                                            </DataTrigger>
                                        </Style.Triggers>
                                    </Style>
                                </Path.Style>
                            </Path>
                            <StackPanel Grid.Column="2" Orientation="Horizontal" VerticalAlignment="Center">
                                <Border BorderThickness="1" CornerRadius="3" Padding="4,1" Margin="4,0,0,0"
                                        BorderBrush="{DynamicResource Synthetic.Brushes.Success}"
                                        Visibility="{Binding WillEnforce, Converter={StaticResource BooleanToVisibilityConverter}}">
                                    <TextBlock Text="[Enforce]" FontSize="10" FontWeight="SemiBold"
                                               Foreground="{DynamicResource Synthetic.Brushes.Success}"/>
                                </Border>
                                <Border BorderThickness="1" CornerRadius="3" Padding="4,1" Margin="4,0,0,0"
                                        BorderBrush="{DynamicResource Synthetic.Brushes.Warning}"
                                        Visibility="{Binding WillSave, Converter={StaticResource BooleanToVisibilityConverter}}">
                                    <TextBlock Text="[Save]" FontSize="10" FontWeight="SemiBold"
                                               Foreground="{DynamicResource Synthetic.Brushes.Warning}"/>
                                </Border>
                                <Border BorderThickness="1" CornerRadius="3" Padding="4,1" Margin="4,0,0,0"
                                        BorderBrush="{DynamicResource Synthetic.Brushes.AccentActive}"
                                        Visibility="{Binding IsEdited, Converter={StaticResource BooleanToVisibilityConverter}}">
                                    <TextBlock Text="[Edited]" FontSize="10" FontWeight="SemiBold"
                                               Foreground="{DynamicResource Synthetic.Brushes.AccentActive}"/>
                                </Border>
                                <Border BorderThickness="1" CornerRadius="3" Padding="4,1" Margin="4,0,0,0"
                                        BorderBrush="{DynamicResource Synthetic.Brushes.BorderNormal}"
                                        Visibility="{Binding IsDiffed, Converter={StaticResource BooleanToVisibilityConverter}}">
                                    <TextBlock Text="[Diffed]" FontSize="10" FontWeight="SemiBold"
                                               Foreground="{DynamicResource Synthetic.Brushes.BorderNormal}"/>
                                </Border>
                            </StackPanel>
                        </Grid>
                    </DataTemplate>
                </ListBox.ItemTemplate>
            </ListBox>

            <!-- Bottom Stack Panel for Configuration Expanders -->
            <StackPanel Grid.Row="2" Margin="0,0,0,10">
                <!-- Save File Path Expander -->
                <Expander Header="Save Target JSON File" Margin="0,0,0,8" IsExpanded="True" Padding="8">
                    <Expander.Style>
                        <Style TargetType="Expander" BasedOn="{StaticResource {x:Type Expander}}">
                            <Setter Property="Visibility" Value="Collapsed"/>
                            <Style.Triggers>
                                <DataTrigger Binding="{Binding IsSavePathActive}" Value="True">
                                    <Setter Property="Visibility" Value="Visible"/>
                                </DataTrigger>
                            </Style.Triggers>
                        </Style>
                    </Expander.Style>
                    <Grid>
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="*"/>
                            <ColumnDefinition Width="Auto"/>
                        </Grid.ColumnDefinitions>
                        <StackPanel Grid.Column="0">
                            <TextBlock Text="Save Target File Path" FontWeight="SemiBold" FontSize="11" Margin="0,0,0,5"
                                       Foreground="{DynamicResource Synthetic.Brushes.TextSecondary}"/>
                            <TextBox Text="{Binding SaveFilePath, UpdateSourceTrigger=PropertyChanged}" Height="22" VerticalContentAlignment="Center" FontSize="10"/>
                        </StackPanel>
                        <Button Grid.Column="1" Content="Browse..." Command="{Binding BrowseSavePathCommand}" VerticalAlignment="Bottom" Height="22" Margin="8,0,0,0" Padding="8,0" Style="{DynamicResource Synthetic.Styles.NeutralButton}"/>
                    </Grid>
                </Expander>

                <!-- Family Processing Options Expander -->
                <Expander Header="Family Processing Options" Margin="0,0,0,0" IsExpanded="False" Padding="8">
                    <Grid>
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="*"/>
                            <ColumnDefinition Width="Auto"/>
                        </Grid.ColumnDefinitions>
                        <StackPanel Grid.Column="0">
                            <CheckBox Content="Update Families" IsChecked="{Binding UpdateFamilies}" Margin="0,0,0,4" FontSize="10"/>
                            <CheckBox Content="Process Nested Families" IsChecked="{Binding ProcessNestedRecursive}" Margin="0,0,0,4" FontSize="10"
                                      IsEnabled="{Binding UpdateFamilies}"/>
                            <CheckBox Content="Purge Unused Style Types" IsChecked="{Binding PurgeUnusedStyleTypes}" FontSize="10"
                                      IsEnabled="{Binding UpdateFamilies}"/>
                        </StackPanel>
                        <StackPanel Grid.Column="1" VerticalAlignment="Center" Margin="10,0,0,0">
                            <TextBlock Text="Category Filter" FontSize="9" Margin="0,0,0,2" Foreground="{DynamicResource Synthetic.Brushes.TextSecondary}"/>
                            <ComboBox SelectedItem="{Binding CategoryFilter}"
                                      ItemsSource="{Binding AvailableCategoryFilters}"
                                      Width="120" Height="22" FontSize="9" VerticalContentAlignment="Center"
                                      IsEnabled="{Binding UpdateFamilies}"/>
                        </StackPanel>
                    </Grid>
                </Expander>
            </StackPanel>

            <!-- Footer Action Buttons -->
            <Border Grid.Row="3" BorderThickness="0,1,0,0" BorderBrush="{DynamicResource Synthetic.Brushes.BorderNormal}" Padding="0,10,0,0">
                <Grid>
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="Auto"/>
                        <ColumnDefinition Width="*"/>
                        <ColumnDefinition Width="Auto"/>
                    </Grid.ColumnDefinitions>
                    
                    <StackPanel Grid.Column="0" Orientation="Horizontal">
                        <Button Content="Remove" 
                                Command="{Binding RemoveFromQueueCommand}"
                                CommandParameter="{Binding SelectedItems, ElementName=QueueListBox}"
                                Style="{DynamicResource Synthetic.Styles.NeutralButton}" Width="75"/>
                    </StackPanel>

                    <StackPanel Grid.Column="2" Orientation="Horizontal">
                        <Button Content="Edit" Command="{Binding EditCommand}" CommandParameter="{Binding SelectedItems, ElementName=QueueListBox}" Margin="0,0,8,0"
                                Style="{DynamicResource Synthetic.Styles.NeutralButton}" Width="65"/>
                        <Button Content="Diff" Command="{Binding DiffCommand}" CommandParameter="{Binding SelectedItems, ElementName=QueueListBox}" Margin="0,0,8,0"
                                Style="{DynamicResource Synthetic.Styles.NeutralButton}" Width="65"/>
                        <Button Content="Run Queue" Command="{Binding RunQueueCommand}"
                                Style="{DynamicResource Synthetic.Styles.PrimaryButton.Right}" Width="90"/>
                    </StackPanel>
                </Grid>
            </Border>
        </Grid>

        <!-- Grid Splitter 2 -->
        <GridSplitter Grid.Column="3" HorizontalAlignment="Stretch" VerticalAlignment="Stretch" Background="{DynamicResource Synthetic.Brushes.BorderNormal}"/>

        <!-- ================= RIGHT COLUMN: SUB-WORKSPACE PANE ================= -->
        <Grid Grid.Column="4">
            <ContentControl Content="{Binding ActiveWorkspace}">
                <ContentControl.Style>
                    <Style TargetType="ContentControl">
                        <!-- Default template is Idle -->
                        <Setter Property="ContentTemplate">
                            <Setter.Value>
                                <DataTemplate>
                                    <Border BorderBrush="{DynamicResource Synthetic.Brushes.BorderNormal}" BorderThickness="1" 
                                            Background="{DynamicResource Synthetic.Brushes.ControlSurface}" CornerRadius="4" Padding="15">
                                        <StackPanel VerticalAlignment="Center" HorizontalAlignment="Center">
                                            <TextBlock Text="DYNAMIC SUB-WORKSPACE PANE" FontWeight="Bold" FontSize="14" Margin="0,0,0,10"
                                                       Foreground="{DynamicResource Synthetic.Brushes.TextPrimary}" HorizontalAlignment="Center"/>
                                            <TextBlock Text="Select items in the queue and click 'Edit' to begin editing." 
                                                       Foreground="{DynamicResource Synthetic.Brushes.TextSecondary}" HorizontalAlignment="Center"/>
                                        </StackPanel>
                                    </Border>
                                </DataTemplate>
                            </Setter.Value>
                        </Setter>
                        <Style.Triggers>
                            <!-- Edit Mode Trigger -->
                            <DataTrigger Binding="{Binding ActiveWorkspace}" Value="{x:Static vm:WorkspaceMode.Edit}">
                                <Setter Property="ContentTemplate" Value="{StaticResource WorkspaceEditTemplate}"/>
                            </DataTrigger>
                            <!-- Diff Mode Trigger -->
                            <DataTrigger Binding="{Binding ActiveWorkspace}" Value="{x:Static vm:WorkspaceMode.Diff}">
                                <Setter Property="ContentTemplate" Value="{StaticResource WorkspaceDiffTemplate}"/>
                            </DataTrigger>
                        </Style.Triggers>
                    </Style>
                </ContentControl.Style>
            </ContentControl>
        </Grid>
    </Grid>
</Window>
```

### File: StandardsManagement/Views/ProjectStandardsDashboardWindow.xaml.cs
```csharp
using System;
using System.Windows;
using Synthetic.Shared.UI;

namespace Synthetic.Modules.StandardsManagement.Views
{
    /// <summary>
    /// Interaction logic for ProjectStandardsDashboardWindow.xaml
    /// </summary>
    public partial class ProjectStandardsDashboardWindow : Window
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ProjectStandardsDashboardWindow"/> class.
        /// </summary>
        /// <param name="mainWindowHandle">The handle to the main Revit application window, used to set the owner of this window.</param>
        public ProjectStandardsDashboardWindow(IntPtr mainWindowHandle)
        {
            InitializeComponent();
            RevitWindowHelper.SetOwner(this, mainWindowHandle);
        }
    }
}
```

### File: StandardsManagement/Views/SelectRevitDocumentWindow.xaml
```xml
<Window x:Class="Synthetic.Modules.StandardsManagement.Views.SelectRevitDocumentWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:local="clr-namespace:Synthetic.Shared.UI"
        xmlns:vm="clr-namespace:Synthetic.Modules.StandardsManagement.ViewModels"
        Title="Select Source Revit Models" 
        Height="450" Width="650" MinHeight="400" MinWidth="600"
        WindowStartupLocation="CenterOwner"
        Style="{DynamicResource SyntheticWindowStyle}"
        local:WindowChromeBehavior.EnableWindowCommands="True">

    <Window.Resources>
        <ResourceDictionary>
            <ResourceDictionary.MergedDictionaries>
                <ResourceDictionary Source="../../../Shared/UI/SyntheticTheme.xaml"/>
            </ResourceDictionary.MergedDictionaries>
        </ResourceDictionary>
    </Window.Resources>

    <Grid Margin="15">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/> <!-- Title -->
            <RowDefinition Height="*"/>    <!-- Document list and configurations -->
            <RowDefinition Height="Auto"/> <!-- Buttons footer -->
        </Grid.RowDefinitions>

        <!-- Title -->
        <TextBlock Grid.Row="0" Text="Select Revit models and family processing options to extract" 
                   FontWeight="Bold" FontSize="14" Margin="0,0,0,15"
                   Foreground="{DynamicResource Synthetic.Brushes.TextPrimary}"/>

        <!-- Main split content: Documents list & Options vs Filter Hierarchy tree -->
        <Grid Grid.Row="1" Margin="0,0,0,15">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="*"/> <!-- Documents & Family Options -->
                <ColumnDefinition Width="*"/> <!-- Standards Filter Hierarchy -->
            </Grid.ColumnDefinitions>

            <!-- Left column: Documents List (top) + Family Options (bottom) -->
            <Grid Grid.Column="0" Margin="0,0,15,0">
                <Grid.RowDefinitions>
                    <RowDefinition Height="*"/>
                    <RowDefinition Height="Auto"/>
                </Grid.RowDefinitions>

                <!-- Document selection ListBox -->
                <Grid Grid.Row="0">
                    <Grid.RowDefinitions>
                        <RowDefinition Height="Auto"/>
                        <RowDefinition Height="*"/>
                    </Grid.RowDefinitions>
                    <TextBlock Grid.Row="0" Text="Open Revit Documents:" FontWeight="SemiBold" Margin="0,0,0,6"
                               Foreground="{DynamicResource Synthetic.Brushes.TextSecondary}"/>
                    <ListBox Grid.Row="1" ItemsSource="{Binding OpenDocuments}" 
                             Background="Transparent" BorderBrush="{DynamicResource Synthetic.Brushes.BorderNormal}" BorderThickness="1">
                        <ListBox.ItemTemplate>
                            <DataTemplate DataType="{x:Type vm:RevitDocumentItem}">
                                <CheckBox IsChecked="{Binding IsSelected}" VerticalAlignment="Center" Margin="5,2">
                                    <TextBlock Text="{Binding Title}" VerticalAlignment="Center" Foreground="{DynamicResource Synthetic.Brushes.TextPrimary}"/>
                                </CheckBox>
                            </DataTemplate>
                        </ListBox.ItemTemplate>
                    </ListBox>
                </Grid>

                <!-- Family Processing Options -->
                <Grid Grid.Row="1" Margin="0,15,0,0">
                    <Grid.RowDefinitions>
                        <RowDefinition Height="Auto"/>
                        <RowDefinition Height="Auto"/>
                    </Grid.RowDefinitions>
                    <TextBlock Grid.Row="0" Text="Family Processing Options:" FontWeight="SemiBold" Margin="0,0,0,6"
                               Foreground="{DynamicResource Synthetic.Brushes.TextSecondary}"/>
                    <Border Grid.Row="1" BorderBrush="{DynamicResource Synthetic.Brushes.BorderNormal}" BorderThickness="1" Padding="8">
                        <StackPanel Orientation="Vertical">
                            <CheckBox IsChecked="{Binding ScanFamilies}" Content="Scan Families" Margin="0,2"
                                      Foreground="{DynamicResource Synthetic.Brushes.TextPrimary}"/>
                            <CheckBox IsChecked="{Binding IncludeNestedFamilies}" Content="Include Nested Families" Margin="0,2"
                                      IsEnabled="{Binding ScanFamilies}"
                                      Foreground="{DynamicResource Synthetic.Brushes.TextPrimary}"/>
                        </StackPanel>
                    </Border>
                </Grid>
            </Grid>

            <!-- Right column: Filter Hierarchy TreeView -->
            <Grid Grid.Column="1">
                <Grid.RowDefinitions>
                    <RowDefinition Height="Auto"/>
                    <RowDefinition Height="*"/>
                </Grid.RowDefinitions>
                <TextBlock Grid.Row="0" Text="Standards Filter Hierarchy:" FontWeight="SemiBold" Margin="0,0,0,6"
                           Foreground="{DynamicResource Synthetic.Brushes.TextSecondary}"/>
                <Border Grid.Row="1" BorderBrush="{DynamicResource Synthetic.Brushes.BorderNormal}" BorderThickness="1" Padding="8">
                    <TreeView ItemsSource="{Binding FilterHierarchy}"
                              Background="Transparent"
                              BorderThickness="0">
                        <TreeView.ItemContainerStyle>
                            <Style TargetType="TreeViewItem" BasedOn="{StaticResource {x:Type TreeViewItem}}">
                                <Setter Property="IsExpanded" Value="{Binding IsExpanded, Mode=TwoWay}"/>
                            </Style>
                        </TreeView.ItemContainerStyle>
                        <TreeView.Resources>
                            <HierarchicalDataTemplate DataType="{x:Type vm:StandardGroupModel}" ItemsSource="{Binding Children}">
                                <StackPanel Orientation="Horizontal" Margin="0,2">
                                    <CheckBox IsChecked="{Binding IsChecked}" IsThreeState="True" Margin="0,0,5,0" Focusable="False"/>
                                    <TextBlock Text="{Binding Name}" Foreground="{DynamicResource Synthetic.Brushes.TextPrimary}" FontWeight="SemiBold"/>
                                </StackPanel>
                            </HierarchicalDataTemplate>
                            <HierarchicalDataTemplate DataType="{x:Type vm:StandardClassModel}" ItemsSource="{Binding Children}">
                                <StackPanel Orientation="Horizontal" Margin="0,2">
                                    <CheckBox IsChecked="{Binding IsChecked}" IsThreeState="True" Margin="0,0,5,0" Focusable="False"/>
                                    <TextBlock Text="{Binding Name}" Foreground="{DynamicResource Synthetic.Brushes.TextPrimary}"/>
                                </StackPanel>
                            </HierarchicalDataTemplate>
                        </TreeView.Resources>
                    </TreeView>
                </Border>
            </Grid>
        </Grid>

        <!-- Footer buttons -->
        <Border Grid.Row="2" BorderThickness="0,1,0,0" BorderBrush="{DynamicResource Synthetic.Brushes.BorderNormal}" Padding="0,10,0,0">
            <StackPanel Orientation="Horizontal" HorizontalAlignment="Right">
                <Button Content="Cancel" Click="CancelButton_Click" 
                        Style="{DynamicResource Synthetic.Styles.NeutralButton}" Margin="0,0,8,0" Width="75"/>
                <Button Content="OK" Click="OkButton_Click" 
                        Style="{DynamicResource Synthetic.Styles.PrimaryButton.Right}" Width="75"/>
            </StackPanel>
        </Border>
    </Grid>
</Window>
```

### File: StandardsManagement/Views/SelectRevitDocumentWindow.xaml.cs
```csharp
using System;
using System.Windows;
using Synthetic.Modules.StandardsManagement.ViewModels;

namespace Synthetic.Modules.StandardsManagement.Views
{
    public partial class SelectRevitDocumentWindow : Window
    {
        public SelectRevitDocumentWindow(SelectRevitDocumentViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
```

### File: StandardsManagement/Views/StandardsClassSelectionControl.xaml
```xml
<UserControl x:Class="Synthetic.Modules.StandardsManagement.Views.StandardsClassSelectionControl"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             Background="Transparent" Foreground="#F1F1F1">
    <Grid>
        <!-- Class-Level Selection TabControl — inherits implicit theme style -->
        <TabControl Padding="10">
            <!-- Tab 1: Annotations & Detailing -->
            <TabItem>
                <TabItem.Header>
                    <StackPanel Orientation="Horizontal">
                        <TextBlock Text="Annotations &amp; Detailing" VerticalAlignment="Center"/>
                        <TextBlock Margin="8,0,0,0" VerticalAlignment="Center" FontSize="10">
                            <Hyperlink Command="{Binding SelectAllAnnotationsCommand}" Foreground="#569CD6" TextDecorations="None">All</Hyperlink>
                            <Run Text="|" Foreground="#555555"/>
                            <Hyperlink Command="{Binding SelectNoneAnnotationsCommand}" Foreground="#569CD6" TextDecorations="None">None</Hyperlink>
                        </TextBlock>
                    </StackPanel>
                </TabItem.Header>
                <StackPanel Margin="5">
                    <CheckBox Content="Text &amp; Label Types (TextNoteType &amp; TextElementType)" IsChecked="{Binding ExportTextNoteTypes}" Foreground="#FFF" Margin="0,4"/>
                    <CheckBox Content="Dimension Types (DimensionType)" IsChecked="{Binding ExportDimensionTypes}" Foreground="#FFF" Margin="0,4"/>
                    <CheckBox Content="Filled Region Types (FilledRegionType)" IsChecked="{Binding ExportFilledRegionTypes}" Foreground="#FFF" Margin="0,4"/>
                    <CheckBox Content="Grid Types (GridType)" IsChecked="{Binding ExportGridTypes}" IsEnabled="{Binding IsSystemFamiliesEnabled}" Foreground="#FFF" Margin="0,4"/>
                    <CheckBox Content="Level Types (LevelType)" IsChecked="{Binding ExportLevelTypes}" IsEnabled="{Binding IsSystemFamiliesEnabled}" Foreground="#FFF" Margin="0,4"/>
                    <CheckBox Content="Label Types (LabelType)" IsChecked="{Binding ExportLabelTypes}" Foreground="#FFF" Margin="0,4"/>
                </StackPanel>
            </TabItem>
            
            <!-- Tab 2: Materials & Patterns -->
            <TabItem>
                <TabItem.Header>
                    <StackPanel Orientation="Horizontal">
                        <TextBlock Text="Materials &amp; Patterns" VerticalAlignment="Center"/>
                        <TextBlock Margin="8,0,0,0" VerticalAlignment="Center" FontSize="10">
                            <Hyperlink Command="{Binding SelectAllMaterialsCommand}" Foreground="#569CD6" TextDecorations="None">All</Hyperlink>
                            <Run Text="|" Foreground="#555555"/>
                            <Hyperlink Command="{Binding SelectNoneMaterialsCommand}" Foreground="#569CD6" TextDecorations="None">None</Hyperlink>
                        </TextBlock>
                    </StackPanel>
                </TabItem.Header>
                <StackPanel Margin="5">
                    <CheckBox Content="Materials (Material)" IsChecked="{Binding ExportMaterials}" Foreground="#FFF" Margin="0,4"/>
                    <CheckBox Content="Fill Patterns (FillPatternElement)" IsChecked="{Binding ExportFillPatterns}" Foreground="#FFF" Margin="0,4"/>
                    <CheckBox Content="Line Patterns (LinePatternElement)" IsChecked="{Binding ExportLinePatterns}" Foreground="#FFF" Margin="0,4"/>
                    <CheckBox Content="Appearance Assets (PropertySetElement)" IsChecked="{Binding ExportAppearanceAssets}" Foreground="#FFF" Margin="0,4"/>
                </StackPanel>
            </TabItem>
            
            <!-- Tab 3: System Family Types -->
            <TabItem IsEnabled="{Binding IsSystemFamiliesEnabled}">
                <TabItem.Header>
                    <StackPanel Orientation="Horizontal">
                        <TextBlock Text="System Family Types" VerticalAlignment="Center"/>
                        <TextBlock Margin="8,0,0,0" VerticalAlignment="Center" FontSize="10">
                            <Hyperlink Command="{Binding SelectAllSystemTypesCommand}" Foreground="#569CD6" TextDecorations="None">All</Hyperlink>
                            <Run Text="|" Foreground="#555555"/>
                            <Hyperlink Command="{Binding SelectNoneSystemTypesCommand}" Foreground="#569CD6" TextDecorations="None">None</Hyperlink>
                        </TextBlock>
                    </StackPanel>
                </TabItem.Header>
                <WrapPanel Margin="5">
                    <CheckBox Content="Wall Types" IsChecked="{Binding ExportWallTypes}" Foreground="#FFF" Margin="0,4,15,4" Width="120"/>
                    <CheckBox Content="Floor Types" IsChecked="{Binding ExportFloorTypes}" Foreground="#FFF" Margin="0,4,15,4" Width="120"/>
                    <CheckBox Content="Roof Types" IsChecked="{Binding ExportRoofTypes}" Foreground="#FFF" Margin="0,4,15,4" Width="120"/>
                    <CheckBox Content="Ceiling Types" IsChecked="{Binding ExportCeilingTypes}" Foreground="#FFF" Margin="0,4,15,4" Width="120"/>
                    <CheckBox Content="Railing Types" IsChecked="{Binding ExportRailingTypes}" Foreground="#FFF" Margin="0,4,15,4" Width="120"/>
                    <CheckBox Content="Stair Types" IsChecked="{Binding ExportStairsTypes}" Foreground="#FFF" Margin="0,4,15,4" Width="120"/>
                    <CheckBox Content="Curtain System" IsChecked="{Binding ExportCurtainSystemTypes}" Foreground="#FFF" Margin="0,4,15,4" Width="120"/>
                    <CheckBox Content="Mullion Types" IsChecked="{Binding ExportMullionTypes}" Foreground="#FFF" Margin="0,4,15,4" Width="120"/>
                    <CheckBox Content="Fascia Types" IsChecked="{Binding ExportFasciaTypes}" Foreground="#FFF" Margin="0,4,15,4" Width="120"/>
                    <CheckBox Content="Gutter Types" IsChecked="{Binding ExportGutterTypes}" Foreground="#FFF" Margin="0,4,15,4" Width="120"/>
                </WrapPanel>
            </TabItem>

            <!-- Tab 4: Loadable Family Categories -->
            <TabItem>
                <TabItem.Header>
                    <StackPanel Orientation="Horizontal">
                        <TextBlock Text="Loadable Family Categories" VerticalAlignment="Center"/>
                        <TextBlock Margin="8,0,0,0" VerticalAlignment="Center" FontSize="10">
                            <Hyperlink Command="{Binding SelectAllCategoriesCommand}" Foreground="#569CD6" TextDecorations="None">All</Hyperlink>
                            <Run Text="|" Foreground="#555555"/>
                            <Hyperlink Command="{Binding SelectNoneCategoriesCommand}" Foreground="#569CD6" TextDecorations="None">None</Hyperlink>
                        </TextBlock>
                    </StackPanel>
                </TabItem.Header>
                <Grid Margin="5">
                    <Grid.RowDefinitions>
                        <RowDefinition Height="Auto"/>
                        <RowDefinition Height="*"/>
                    </Grid.RowDefinitions>
                    <StackPanel Grid.Row="0" Orientation="Horizontal" Margin="0,0,0,6">
                        <Button Content="Select All" Command="{Binding SelectAllCategoriesCommand}" Margin="0,0,8,0"/>
                        <Button Content="Select None" Command="{Binding SelectNoneCategoriesCommand}"/>
                    </StackPanel>
                    <ScrollViewer Grid.Row="1" VerticalScrollBarVisibility="Auto" BorderBrush="#3F3F46" BorderThickness="1" Background="#1E1E1E" Padding="5">
                        <ItemsControl ItemsSource="{Binding LoadableFamilyCategories}">
                            <ItemsControl.ItemsPanel>
                                <ItemsPanelTemplate>
                                    <WrapPanel Orientation="Horizontal"/>
                                </ItemsPanelTemplate>
                            </ItemsControl.ItemsPanel>
                            <ItemsControl.ItemTemplate>
                                <DataTemplate>
                                    <CheckBox Content="{Binding Name}" IsChecked="{Binding IsChecked}" Foreground="#FFF" Margin="0,3,15,3" Width="130"/>
                                </DataTemplate>
                            </ItemsControl.ItemTemplate>
                        </ItemsControl>
                    </ScrollViewer>
                </Grid>
            </TabItem>
            
            <!-- Tab 5: Views & Organization -->
            <TabItem IsEnabled="{Binding IsViewsEnabled}">
                <TabItem.Header>
                    <StackPanel Orientation="Horizontal">
                        <TextBlock Text="Views &amp; Organization" VerticalAlignment="Center"/>
                        <TextBlock Margin="8,0,0,0" VerticalAlignment="Center" FontSize="10">
                            <Hyperlink Command="{Binding SelectAllViewsCommand}" Foreground="#569CD6" TextDecorations="None">All</Hyperlink>
                            <Run Text="|" Foreground="#555555"/>
                            <Hyperlink Command="{Binding SelectNoneViewsCommand}" Foreground="#569CD6" TextDecorations="None">None</Hyperlink>
                        </TextBlock>
                    </StackPanel>
                </TabItem.Header>
                <StackPanel Margin="5">
                    <CheckBox Content="Standard Views (View)" IsChecked="{Binding ExportStandardViews}" Foreground="#FFF" Margin="0,4"/>
                    <CheckBox Content="View Templates (View)" IsChecked="{Binding ExportViewTemplates}" Foreground="#FFF" Margin="0,4"/>
                    <CheckBox Content="View Family Types (ViewFamilyType)" IsChecked="{Binding ExportViewFamilyTypes}" Foreground="#FFF" Margin="0,4"/>
                    <CheckBox Content="Browser Organization (BrowserOrganization)" IsChecked="{Binding ExportBrowserOrganizations}" Foreground="#FFF" Margin="0,4"/>
                </StackPanel>
            </TabItem>
            
            <!-- Tab 6: Project Standards -->
            <TabItem>
                <TabItem.Header>
                    <StackPanel Orientation="Horizontal">
                        <TextBlock Text="Project Standards" VerticalAlignment="Center"/>
                        <TextBlock Margin="8,0,0,0" VerticalAlignment="Center" FontSize="10">
                            <Hyperlink Command="{Binding SelectAllStandardsCommand}" Foreground="#569CD6" TextDecorations="None">All</Hyperlink>
                            <Run Text="|" Foreground="#555555"/>
                            <Hyperlink Command="{Binding SelectNoneStandardsCommand}" Foreground="#569CD6" TextDecorations="None">None</Hyperlink>
                        </TextBlock>
                    </StackPanel>
                </TabItem.Header>
                <StackPanel Margin="5">
                    <Border BorderBrush="#3F3F46" BorderThickness="1" CornerRadius="3" Padding="8" Margin="0,2,0,8" Background="#1E1E1E">
                        <StackPanel>
                            <TextBlock Text="Categories &amp; Object Styles By Type" Foreground="#808080" FontWeight="SemiBold" Margin="0,0,0,6" FontSize="11"/>
                            <WrapPanel Orientation="Horizontal">
                                <CheckBox Content="Line Styles" IsChecked="{Binding ExportLineStyles}" Foreground="#FFF" Margin="0,3,15,3" Width="180"/>
                                <CheckBox Content="Model Categories" IsChecked="{Binding ExportModelCategories}" Foreground="#FFF" Margin="0,3,15,3" Width="180"/>
                                <CheckBox Content="Annotation Categories" IsChecked="{Binding ExportAnnotationCategories}" Foreground="#FFF" Margin="0,3,15,3" Width="180"/>
                                <CheckBox Content="Analytical Categories" IsChecked="{Binding ExportAnalyticalCategories}" Foreground="#FFF" Margin="0,3,15,3" Width="180"/>
                                <CheckBox Content="Imported Categories" IsChecked="{Binding ExportImportCategories}" Foreground="#FFF" Margin="0,3,15,3" Width="180"/>
                            </WrapPanel>
                        </StackPanel>
                    </Border>
                    <CheckBox Content="Shared/Project Parameters (ParameterElement)" IsChecked="{Binding ExportParameterElements}" Foreground="#FFF" Margin="0,4"/>
                    <CheckBox Content="View Filters (ParameterFilterElement)" IsChecked="{Binding ExportParameterFilters}" Foreground="#FFF" Margin="0,4"/>
                    <CheckBox Content="Title Blocks (TitleBlockType / OST_TitleBlocks)" IsChecked="{Binding ExportTitleBlockTypes}" Foreground="#FFF" Margin="0,4"/>
                </StackPanel>
            </TabItem>
        </TabControl>
    </Grid>
</UserControl>
```

### File: StandardsManagement/Views/StandardsClassSelectionControl.xaml.cs
```csharp
using System.Windows.Controls;
using Synthetic.Modules.StandardsManagement.ViewModels;

namespace Synthetic.Modules.StandardsManagement.Views
{
    /// <summary>
    /// Interaction logic for StandardsClassSelectionControl.xaml.
    /// </summary>
    public partial class StandardsClassSelectionControl : System.Windows.Controls.UserControl
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="StandardsClassSelectionControl"/> class.
        /// </summary>
        public StandardsClassSelectionControl()
        {
            InitializeComponent();
        }
    }
}
```

### File: StandardsManagement/Views/StandardsReviewWindow.xaml
```xml
<Window x:Class="Synthetic.Modules.StandardsManagement.Views.StandardsReviewWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:vm="clr-namespace:Synthetic.Modules.StandardsManagement.ViewModels"
        xmlns:models="clr-namespace:Synthetic.RevitDOM.Operations.Merge"
        xmlns:local="clr-namespace:Synthetic.Modules.StandardsManagement.Views"
        xmlns:sys="clr-namespace:System;assembly=mscorlib"
        xmlns:ui="clr-namespace:Synthetic.Shared.UI"
        Title="Enforce Standards - Review &amp; Analysis Window" Height="650" Width="1000" MinHeight="500" MinWidth="850"
        WindowStartupLocation="CenterOwner" ResizeMode="CanResizeWithGrip" ShowInTaskbar="False"
        Style="{DynamicResource SyntheticWindowStyle}"
        ui:WindowChromeBehavior.EnableWindowCommands="True">

    <Window.Resources>
        <ResourceDictionary>
            <ResourceDictionary.MergedDictionaries>
                <ResourceDictionary Source="../../../Shared/UI/SyntheticTheme.xaml"/>
            </ResourceDictionary.MergedDictionaries>
            <BooleanToVisibilityConverter x:Key="BoolToVis" />
        </ResourceDictionary>
    </Window.Resources>

    <Grid Margin="15">
        <Grid.ColumnDefinitions>
            <ColumnDefinition Width="250"/> <!-- Sidebar: Clusters/Categories -->
            <ColumnDefinition Width="Auto"/> <!-- Splitter -->
            <ColumnDefinition Width="*"/>   <!-- Detail Pane -->
        </Grid.ColumnDefinitions>

        <!-- SIDEBAR: CATEGORIES / CLUSTERS LIST -->
        <Grid Grid.Column="0" Margin="0,0,10,0">
            <Grid.RowDefinitions>
                <RowDefinition Height="Auto"/>
                <RowDefinition Height="*"/>
            </Grid.RowDefinitions>
            
            <TextBlock Grid.Row="0" Text="Style Categories" FontSize="12" FontWeight="Bold" Foreground="{DynamicResource Synthetic.Brushes.TextSecondary}" Margin="0,0,0,6"/>
            
            <ListBox Grid.Row="1" ItemsSource="{Binding Clusters}" SelectedItem="{Binding SelectedCluster, Mode=TwoWay}">
                <ListBox.ItemTemplate>
                    <DataTemplate DataType="{x:Type models:DuplicateClusterModel}">
                        <Border Padding="5,6">
                            <TextBlock Text="{Binding ClusterName}" FontSize="12" VerticalAlignment="Center"/>
                        </Border>
                    </DataTemplate>
                </ListBox.ItemTemplate>
            </ListBox>
        </Grid>

        <!-- SPLITTER -->
        <GridSplitter Grid.Column="1" Width="5" HorizontalAlignment="Stretch" VerticalAlignment="Stretch" Margin="5,0"/>

        <!-- DETAIL PANE -->
        <Grid Grid.Column="2" Margin="10,0,0,0">
            <Grid.RowDefinitions>
                <RowDefinition Height="Auto"/> <!-- Header Section -->
                <RowDefinition Height="1.2*"/>  <!-- Top Pane: Element Types Mapping -->
                <RowDefinition Height="Auto"/> <!-- Splitter -->
                <RowDefinition Height="1.5*"/>  <!-- Bottom Pane: Parameter Comparer Grid -->
                <RowDefinition Height="Auto"/> <!-- Footer Buttons -->
            </Grid.RowDefinitions>

            <!-- 1. HEADER SECTION -->
            <Border Grid.Row="0" Background="{DynamicResource Synthetic.Brushes.ControlSurface}" BorderBrush="{DynamicResource Synthetic.Brushes.BorderNormal}" BorderThickness="1" CornerRadius="4" Padding="12" Margin="0,0,0,12">
                <StackPanel>
                    <TextBlock Text="Project Standards Audit &amp; Analysis" FontSize="11" Foreground="{DynamicResource Synthetic.Brushes.AccentActive}" FontWeight="Bold" Margin="0,0,0,2"/>
                    <TextBlock Text="{Binding SelectedCluster.ClusterName}" FontSize="16" FontWeight="SemiBold" Foreground="{DynamicResource Synthetic.Brushes.TextPrimary}"/>
                </StackPanel>
            </Border>

            <!-- 2. TOP PANE: ELEMENT TYPES MAPPING -->
            <Grid Grid.Row="1" Margin="0,0,0,5">
                <Grid.RowDefinitions>
                    <RowDefinition Height="Auto"/>
                    <RowDefinition Height="*"/>
                </Grid.RowDefinitions>

                <TextBlock Grid.Row="0" Text="Element Types (Select a Type to review parameters)" FontSize="12" FontWeight="Bold" Foreground="{DynamicResource Synthetic.Brushes.TextSecondary}" Margin="0,0,0,6"/>
                
                <DataGrid Grid.Row="1" ItemsSource="{Binding SelectedClusterTypeMappings}" SelectedItem="{Binding SelectedTypeMapping, Mode=TwoWay}">
                    <DataGrid.Columns>
                        <DataGridTextColumn Header="Revit Element Name" Binding="{Binding SourceType.Name}" Width="2*" IsReadOnly="True">
                            <DataGridTextColumn.ElementStyle>
                                <Style TargetType="TextBlock" BasedOn="{StaticResource {x:Type TextBlock}}">
                                    <Setter Property="VerticalAlignment" Value="Center"/>
                                    <Setter Property="Margin" Value="10,0"/>
                                </Style>
                            </DataGridTextColumn.ElementStyle>
                        </DataGridTextColumn>

                        <DataGridTextColumn Header="Proposed Target Standard" Binding="{Binding TargetType.Name}" Width="2*" IsReadOnly="True">
                            <DataGridTextColumn.ElementStyle>
                                <Style TargetType="TextBlock" BasedOn="{StaticResource {x:Type TextBlock}}">
                                    <Setter Property="VerticalAlignment" Value="Center"/>
                                    <Setter Property="Margin" Value="10,0"/>
                                </Style>
                            </DataGridTextColumn.ElementStyle>
                        </DataGridTextColumn>
                    </DataGrid.Columns>
                </DataGrid>
            </Grid>

            <!-- SPLITTER -->
            <GridSplitter Grid.Row="2" Height="5" HorizontalAlignment="Stretch" VerticalAlignment="Center" Margin="0,5"/>

            <!-- 3. BOTTOM PANE: PARAMETER COMPARER GRID -->
            <Grid Grid.Row="3" Margin="0,5,0,15">
                <Grid.RowDefinitions>
                    <RowDefinition Height="Auto"/>
                    <RowDefinition Height="*"/>
                </Grid.RowDefinitions>
                
                <TextBlock Grid.Row="0" Text="Comparative Parameter Matrix (Select Overrides to Enforce)" FontSize="12" FontWeight="Bold" Foreground="{DynamicResource Synthetic.Brushes.TextSecondary}" Margin="0,0,0,6"/>
                
                <DataGrid Grid.Row="1" ItemsSource="{Binding Rows}">
                    <DataGrid.RowStyle>
                        <Style TargetType="DataGridRow" BasedOn="{StaticResource {x:Type DataGridRow}}">
                            <Style.Triggers>
                                <DataTrigger Binding="{Binding HasConflict}" Value="True">
                                    <Setter Property="FontWeight" Value="Bold"/>
                                    <Setter Property="Foreground" Value="{DynamicResource Synthetic.Brushes.Warning}"/>
                                </DataTrigger>
                                <DataTrigger Binding="{Binding IsSchemaMismatch}" Value="True">
                                    <Setter Property="FontWeight" Value="Bold"/>
                                    <Setter Property="Foreground" Value="{DynamicResource Synthetic.Brushes.Error}"/>
                                </DataTrigger>
                            </Style.Triggers>
                        </Style>
                    </DataGrid.RowStyle>

                    <DataGrid.Columns>
                        <DataGridTemplateColumn Header="Approve" Width="Auto">
                            <DataGridTemplateColumn.CellTemplate>
                                <DataTemplate DataType="{x:Type models:ParameterDiffRowModel}">
                                    <CheckBox IsChecked="{Binding IsApproved, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}"
                                              HorizontalAlignment="Center" VerticalAlignment="Center"/>
                                </DataTemplate>
                            </DataGridTemplateColumn.CellTemplate>
                        </DataGridTemplateColumn>

                        <DataGridTextColumn Header="Parameter Name" Binding="{Binding ParameterName}" Width="1.8*" IsReadOnly="True">
                            <DataGridTextColumn.ElementStyle>
                                <Style TargetType="TextBlock" BasedOn="{StaticResource {x:Type TextBlock}}">
                                    <Setter Property="VerticalAlignment" Value="Center"/>
                                    <Setter Property="Margin" Value="10,0"/>
                                    <Setter Property="FontWeight" Value="SemiBold"/>
                                </Style>
                            </DataGridTextColumn.ElementStyle>
                        </DataGridTextColumn>

                        <DataGridTextColumn Header="Existing Value (Live Revit)" Binding="{Binding ValueList[0]}" Width="1.8*" IsReadOnly="True">
                            <DataGridTextColumn.ElementStyle>
                                <Style TargetType="TextBlock" BasedOn="{StaticResource {x:Type TextBlock}}">
                                    <Setter Property="VerticalAlignment" Value="Center"/>
                                    <Setter Property="Margin" Value="10,0"/>
                                </Style>
                            </DataGridTextColumn.ElementStyle>
                        </DataGridTextColumn>

                        <DataGridTextColumn Header="Winning Standard Value (JSON)" Binding="{Binding ValueList[1]}" Width="1.8*" IsReadOnly="True">
                            <DataGridTextColumn.ElementStyle>
                                <Style TargetType="TextBlock" BasedOn="{StaticResource {x:Type TextBlock}}">
                                    <Setter Property="VerticalAlignment" Value="Center"/>
                                    <Setter Property="Margin" Value="10,0"/>
                                </Style>
                            </DataGridTextColumn.ElementStyle>
                        </DataGridTextColumn>
                    </DataGrid.Columns>
                </DataGrid>
            </Grid>

            <!-- 4. FOOTER BUTTONS -->
            <Grid Grid.Row="4" VerticalAlignment="Bottom">
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="*"/>
                    <ColumnDefinition Width="Auto"/>
                </Grid.ColumnDefinitions>

                <StackPanel Grid.Column="1" Orientation="Horizontal" HorizontalAlignment="Right">
                    <Button x:Name="BtnCancel" Content="Cancel" Click="BtnCancel_Click" Margin="0,0,10,0"/>
                    <Button Content="Enforce Approved Changes" Command="{Binding EnforceApprovedCommand}"
                            Style="{DynamicResource Synthetic.Styles.PrimaryButton.Right}"/>
                </StackPanel>
            </Grid>
        </Grid>
    </Grid>
</Window>
```

### File: StandardsManagement/Views/StandardsReviewWindow.xaml.cs
```csharp
using System;
using System.Windows;

using Synthetic.Shared.UI;
using Synthetic.Core;

using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;

namespace Synthetic.Modules.StandardsManagement.Views
{
    /// <summary>
    /// Interaction logic for StandardsReviewWindow.xaml.
    /// Displays comparison analysis between standard definitions and live document elements.
    /// </summary>
    public partial class StandardsReviewWindow : Window
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="StandardsReviewWindow"/> class.
        /// </summary>
        /// <param name="mainWindowHandle">The parent Revit main window handle.</param>
        public StandardsReviewWindow(IntPtr mainWindowHandle)
        {
            InitializeComponent();
            RevitWindowHelper.SetOwner(this, mainWindowHandle);

            DataContextChanged += (sender, args) =>
            {
                if (DataContext is StandardsReviewViewModel vm)
                {
                    vm.CloseAction = () =>
                    {
                        try
                        {
                            this.DialogResult = true;
                        }
                        catch (InvalidOperationException)
                        {
                            // Modeless close fallback
                        }
                        this.Close();
                    };
                }
            };
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                this.DialogResult = false;
            }
            catch (InvalidOperationException)
            {
                // Modeless close fallback
            }
            this.Close();
        }
    }
}
```

### File: MergeDuplicates/Commands/CmdMergeDuplicates.cs
```csharp
using Synthetic.Modules.MergeDuplicates.Services;
using System;
using System.Threading;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;
using Synthetic.Infrastructure.Serialization;

using Synthetic.Shared.UI;

using Synthetic.Shared.RevitAPI;
using Synthetic.Infrastructure.IO;
using Synthetic.RevitDOM.Operations.Merge;
using Synthetic.Modules.MergeDuplicates.ViewModels;
using Synthetic.Modules.MergeDuplicates.Views;
using Synthetic.Modules.StandardsManagement.ViewModels;

namespace Synthetic.Modules.MergeDuplicates.Commands
{
    /// <summary>
    /// Revit command to scan model for duplicate families and display the Merge Duplicates Modeless UI.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class CmdMergeDuplicates : IExternalCommand
    {
        /// <summary>
        /// Executes the merge duplicates command.
        /// </summary>
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                UIApplication uiapp = commandData.Application;
                Document? doc = uiapp.ActiveUIDocument?.Document;
                if (doc == null)
                {
                    message = "No active document found.";
                    return Result.Failed;
                }

                // 1. Run Fast Scan
                var clusters = RevitMergeDataCollector.RunFastScan(doc, CancellationToken.None);

                if (clusters.Count == 0)
                {
                    Autodesk.Revit.UI.TaskDialog.Show("Merge Duplicates", "No duplicate clusters found in the project.");
                    return Result.Succeeded;
                }

                // Run deep scan and generate recommendations for all discovered clusters
                foreach (var cluster in clusters)
                {
                    MergeAnalysisEngine.RunDeepScan(cluster, CancellationToken.None);
                    MergeAnalysisEngine.GenerateRecommendations(cluster);
                }

                // 2. Initialize ViewModel
                var vm = new MergeDuplicatesViewModel
                {
                    MainWindowHandle = uiapp.MainWindowHandle,
                    Document = doc,
                    ScannedClusters = clusters
                };

                // 3. Open main window modelessly
                var window = new MergeDuplicatesWindow(uiapp.MainWindowHandle)
                {
                    DataContext = vm
                };
                window.Show();

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message + "\n" + ex.StackTrace;
                return Result.Failed;
            }
        }
    }
}
```

### File: MergeDuplicates/Handlers/ProcessMergeEventHandler.cs
```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;
using Synthetic.Infrastructure.Serialization;
using Synthetic.Shared.UI;
using Synthetic.Modules.MergeDuplicates.Handlers;
using Synthetic.Modules.MergeDuplicates.ViewModels;
using Synthetic.RevitDOM.Operations.Merge;
using Synthetic.RevitDOM.Operations.Merge;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.Shared.RevitAPI;
namespace Synthetic.Modules.MergeDuplicates.Handlers
{
    /// <summary>
    /// Tracks the summary results of a merge cluster execution for Step 6.
    /// </summary>
    public class MergeExecutionReport
    {
        /// <summary>
        /// Gets or sets the name of the merge cluster.
        /// </summary>
        public string ClusterName { get; set; } = string.Empty;
        /// <summary>
        /// Gets or sets the count of instances swapped during the merge.
        /// </summary>
        public int InstancesSwappedCount { get; set; }
        /// <summary>
        /// Gets or sets the count of duplicate types purged.
        /// </summary>
        public int DuplicateTypesPurged { get; set; }
        /// <summary>
        /// Gets or sets a value indicating whether the merge execution succeeded.
        /// </summary>
        public bool ExecutionSuccessStatus { get; set; }
        /// <summary>
        /// Gets or sets the error message if the execution failed.
        /// </summary>
        public string? ErrorMessage { get; set; }
    }
    /// <summary>
    /// Standard family load options to automatically overwrite parameter values.
    /// </summary>
    public class ProcessMergeFamilyLoadOptions : IFamilyLoadOptions
    {
        /// <summary>
        /// Called when a family is found in the target document. Overwrites parameter values.
        /// </summary>
        /// <param name="familyInUse">Indicates if the family is currently in use.</param>
        /// <param name="overwriteParameterValues">Output parameter indicating if parameter values should be overwritten.</param>
        /// <returns>True to load the family.</returns>
        public bool OnFamilyFound(bool familyInUse, out bool overwriteParameterValues)
        {
            overwriteParameterValues = true;
            return true;
        }
        /// <summary>
        /// Called when a shared family is found in the target document. Overwrites parameter values.
        /// </summary>
        /// <param name="sharedFamily">The shared family being loaded.</param>
        /// <param name="familyInUse">Indicates if the shared family is currently in use.</param>
        /// <param name="source">Output parameter indicating the source of the family.</param>
        /// <param name="overwriteParameterValues">Output parameter indicating if parameter values should be overwritten.</param>
        /// <returns>True to load the shared family.</returns>
        public bool OnSharedFamilyFound(Family sharedFamily, bool familyInUse, out FamilySource source, out bool overwriteParameterValues)
        {
            source = FamilySource.Family;
            overwriteParameterValues = true;
            return true;
        }
    }
    /// <summary>
    /// External event handler to process the active merge queue on the Revit API thread.
    /// </summary>
    public class ProcessMergeEventHandler : IExternalEventHandler
    {
        private readonly object _lock = new object();
        private MergeQueueViewModel? _queueVM;
        private Window? _parentWindow;
        private MergeDuplicatesViewModel? _mainVM;
        private CancellationToken _cancellationToken;
        /// <summary>
        /// Queues a merge execution request with its corresponding UI/VM contexts.
        /// </summary>
        public void QueueRequest(MergeQueueViewModel? queueVM, Window? window, MergeDuplicatesViewModel? mainVM, CancellationToken cancellationToken)
        {
            lock (_lock)
            {
                _queueVM = queueVM;
                _parentWindow = window;
                _mainVM = mainVM;
                _cancellationToken = cancellationToken;
            }
        }
        /// <summary>
        /// Executes the merge processing on the Revit API thread.
        /// </summary>
        public void Execute(UIApplication app)
        {
            MergeQueueViewModel? currentQueue;
            Window? currentWindow;
            MergeDuplicatesViewModel? currentMainVM;
            CancellationToken currentToken;
            lock (_lock)
            {
                currentQueue = _queueVM;
                currentWindow = _parentWindow;
                currentMainVM = _mainVM;
                currentToken = _cancellationToken;
                // Reset state
                _queueVM = null;
                _parentWindow = null;
                _mainVM = null;
                _cancellationToken = CancellationToken.None;
            }
            if (currentQueue == null || currentQueue.QueuedClusters.Count == 0) return;
            Document? doc = null;
            var primItem = currentQueue.QueuedClusters.FirstOrDefault()?.SelectedPrimary;
            if (primItem != null && app.Application.Documents != null)
            {
                var targetId = primItem.RevitElementId.ToElementId();
                foreach (Document openDoc in app.Application.Documents)
                {
                    if (!openDoc.IsFamilyDocument)
                    {
                        try
                        {
                            if (openDoc.GetElement(targetId) != null)
                            {
                                doc = openDoc;
                                break;
                            }
                        }
                        catch {}
                    }
                }
            }
            if (doc == null)
            {
                doc = app.ActiveUIDocument?.Document;
            }
            if (doc == null && app.Application.Documents != null)
            {
                foreach (Document openDoc in app.Application.Documents)
                {
                    if (!openDoc.IsFamilyDocument)
                    {
                        doc = openDoc;
                        break;
                    }
                }
            }
            if (doc == null) return;
            // 1. Disable UI during processing to prevent concurrent modifications
            if (currentWindow != null)
            {
                currentWindow.Dispatcher.Invoke(() => currentWindow.IsEnabled = false);
            }
            var reports = new List<MergeExecutionReport>();
            var clustersToProcess = currentQueue.QueuedClusters.ToList();
            bool wasCancelled = false;
            using (var transGroup = new TransactionGroup(doc, "Merge Duplicates"))
            {
                transGroup.Start();
                foreach (var cluster in clustersToProcess)
                {
                    // Check cancellation token boundary
                    if (currentToken.IsCancellationRequested)
                    {
                        wasCancelled = true;
                        transGroup.RollBack();
                        break;
                    }
                    var report = new MergeExecutionReport
                    {
                        ClusterName = cluster.ClusterName,
                        ExecutionSuccessStatus = false
                    };
                    var primaryItem = cluster.SelectedPrimary;
                    if (primaryItem == null)
                    {
                        reports.Add(report);
                        continue;
                    }
                    var duplicateItems = cluster.Items
                        .Where(i => i != primaryItem && i.IsIncludedForMerge)
                        .ToList();
                    var typeMap = new Dictionary<ElementId, ElementId>();
                    using (var trans = new Transaction(doc, $"Merge Cluster Step 1: {cluster.ClusterName}"))
                    {
                        trans.Start();
                        try
                        {
                            Element primElement = doc.GetElement(primaryItem.RevitElementId.ToElementId());
                            if (primElement != null)
                            {
                                // 1. Schema Parameter Injection
                                if (primElement is Family primFamily)
                                {
                                    var parametersToInject = new List<string>();
                                    foreach (var mapping in cluster.TypeMappings)
                                    {
                                        if (mapping.RecommendedAction == RecommendedAction.Exclude) continue;
                                        foreach (var row in mapping.ParameterResolutions)
                                        {
                                            if (row.InjectParameter && !parametersToInject.Contains(row.ParameterName))
                                            {
                                                parametersToInject.Add(row.ParameterName);
                                            }
                                        }
                                    }
                                    if (parametersToInject.Count > 0)
                                    {
                                        Document famDoc = doc.EditFamily(primFamily);
                                        if (famDoc != null)
                                        {
                                            bool anyInjected = false;
                                            using (Transaction famTrans = new Transaction(famDoc, "Inject Parameters"))
                                            {
                                                famTrans.Start();
                                                FamilyManager famManager = famDoc.FamilyManager;
                                                foreach (var paramName in parametersToInject)
                                                {
                                                    bool paramExists = false;
                                                    foreach (FamilyParameter fp in famManager.Parameters)
                                                    {
                                                        if (fp.Definition.Name.Equals(paramName, StringComparison.OrdinalIgnoreCase))
                                                        {
                                                            paramExists = true;
                                                            break;
                                                        }
                                                    }
                                                    if (!paramExists)
                                                    {
                                                        ParameterDefinitionSpec? spec = null; bool foundSource = false;
                                                        foreach (var mapping in cluster.TypeMappings)
                                                        {
                                                            if (mapping.RecommendedAction == RecommendedAction.Exclude) continue;
                                                            if (mapping.SourceType == null) continue;
                                                            FamilySymbol? sourceSymbol = doc.GetElement(mapping.SourceType.RevitTypeId.ToElementId()) as FamilySymbol;
                                                            if (sourceSymbol != null)
                                                            {
                                                                Parameter sourceParam = sourceSymbol.LookupParameter(paramName);
                                                                if (sourceParam != null)
                                                                {
                                                                    spec = GetParamGroupAndTypeFromSource(sourceParam); if (spec != null && spec.Group != null && spec.SpecType != null) { foundSource = true; break; }
                                                                    if (foundSource) break;
                                                                }
                                                            }
                                                        }
                                                        if (!foundSource || spec == null || spec.Group == null || spec.SpecType == null) { spec = GetDefaultGroupAndType(); }
                                                        if (InjectParameterToFamily(famManager, paramName, spec))
                                                        {
                                                            anyInjected = true;
                                                        }
                                                    }
                                                }
                                                famTrans.Commit();
                                            }
                                            if (anyInjected)
                                            {
                                                primFamily = famDoc.LoadFamily(doc, new ProcessMergeFamilyLoadOptions());
                                            }
                                            famDoc.Close(false);
                                            // Re-retrieve primary element reference in case LoadFamily updated it
                                            primElement = doc.GetElement(primaryItem.RevitElementId.ToElementId());
                                        }
                                    }
                                }
                                // 2. Type Mapping & Value Copying
                                foreach (var mapping in cluster.TypeMappings)
                                {
                                    if (mapping.RecommendedAction == RecommendedAction.Exclude) continue;
                                    if (mapping.SourceType == null || mapping.TargetType == null) continue;
                                    ElementId resolvedTargetTypeId = mapping.TargetType.RevitTypeId.ToElementId();
                                    if (mapping.RecommendedAction == RecommendedAction.Migrate && primElement is Family)
                                    {
                                        ElementType? targetTypeElement = doc.GetElement(mapping.TargetType.RevitTypeId.ToElementId()) as ElementType;
                                        if (targetTypeElement != null)
                                        {
                                            string targetName = mapping.MigrateRenameText;
                                            if (string.IsNullOrWhiteSpace(targetName))
                                            {
                                                targetName = mapping.SourceType.Name;
                                            }
                                            targetName = SanitizeRevitTypeName(targetName);
                                            FamilySymbol? existingSymbol = null;
                                            if (primElement is Family f)
                                            {
                                                foreach (ElementId symbolId in f.GetFamilySymbolIds())
                                                {
                                                    FamilySymbol? sym = doc.GetElement(symbolId) as FamilySymbol;
                                                    if (sym != null && sym.Name.Equals(targetName, StringComparison.OrdinalIgnoreCase))
                                                    {
                                                        existingSymbol = sym;
                                                        break;
                                                    }
                                                }
                                            }
                                            if (existingSymbol != null)
                                            {
                                                resolvedTargetTypeId = existingSymbol.Id;
                                            }
                                            else
                                            {
                                                ElementType? duplicatedType = null;
                                                try
                                                {
                                                    duplicatedType = targetTypeElement.Duplicate(targetName);
                                                }
                                                catch (Exception)
                                                {
                                                    string fallbackName = SanitizeRevitTypeName(targetName + "_Migrated");
                                                    try
                                                    {
                                                        duplicatedType = targetTypeElement.Duplicate(fallbackName);
                                                    }
                                                    catch (Exception exInner)
                                                    {
                                                        int suffixCounter = 1;
                                                        while (duplicatedType == null && suffixCounter <= 10)
                                                        {
                                                            try
                                                            {
                                                                duplicatedType = targetTypeElement.Duplicate($"{targetName}_Migrated_{suffixCounter}");
                                                            }
                                                            catch
                                                            {
                                                                suffixCounter++;
                                                            }
                                                        }
                                                        if (duplicatedType == null)
                                                        {
                                                            throw new InvalidOperationException($"Failed to duplicate type '{targetName}' after multiple suffix attempts.", exInner);
                                                        }
                                                    }
                                                }
                                                if (duplicatedType != null)
                                                {
                                                    resolvedTargetTypeId = duplicatedType.Id;
                                                    // Copy all parameters from source type to the duplicated type
                                                    Element sourceSymbol = doc.GetElement(mapping.SourceType.RevitTypeId.ToElementId());
                                                    if (sourceSymbol != null)
                                                    {
                                                        foreach (Parameter sourceParam in sourceSymbol.Parameters)
                                                        {
                                                            if (sourceParam.IsReadOnly || !sourceParam.HasValue) continue;
                                                            Parameter targetParam = duplicatedType.LookupParameter(sourceParam.Definition.Name);
                                                            if (targetParam != null && !targetParam.IsReadOnly)
                                                            {
                                                                CopyParameterValue(sourceParam, targetParam);
                                                            }
                                                        }
                                                    }
                                                }
                                            }
                                        }
                                    }
                                    // Apply resolved parameters
                                    Element targetTypeObj = doc.GetElement(resolvedTargetTypeId);
                                    if (targetTypeObj != null)
                                    {
                                        foreach (var row in mapping.ParameterResolutions)
                                        {
                                            Element winningElement = doc.GetElement(row.WinningValueElementId.ToElementId());
                                            if (winningElement != null)
                                            {
                                                Parameter winningParam = winningElement.LookupParameter(row.ParameterName);
                                                Parameter targetParam = targetTypeObj.LookupParameter(row.ParameterName);
                                                if (winningParam != null && targetParam != null && !targetParam.IsReadOnly)
                                                {
                                                    CopyParameterValue(winningParam, targetParam);
                                                }
                                            }
                                        }
                                    }
                                    typeMap[mapping.SourceType.RevitTypeId.ToElementId()] = resolvedTargetTypeId;
                                }
                                // 3. Swap Instances
                                int instancesSwapped = 0;
                                var dupTypeIds = typeMap.Keys.ToList();
                                if (dupTypeIds.Count > 0)
                                {
                                    if (primElement is Family)
                                    {
                                        var instances = new FilteredElementCollector(doc)
                                            .OfClass(typeof(FamilyInstance))
                                            .Cast<FamilyInstance>()
                                            .Where(fi => dupTypeIds.Contains(fi.GetTypeId()))
                                            .ToList();
                                        foreach (var instance in instances)
                                        {
                                            ElementId targetTypeId = typeMap[instance.GetTypeId()];
                                            FamilySymbol? targetSymbol = doc.GetElement(targetTypeId) as FamilySymbol;
                                            if (targetSymbol != null)
                                            {
                                                if (!targetSymbol.IsActive)
                                                {
                                                    targetSymbol.Activate();
                                                }
                                                instance.Symbol = targetSymbol;
                                                instancesSwapped++;
                                            }
                                        }
                                    }
                                    else if (primElement is GroupType)
                                    {
                                        var groups = new FilteredElementCollector(doc)
                                            .OfClass(typeof(Group))
                                            .Cast<Group>()
                                            .Where(g => dupTypeIds.Contains(g.GetTypeId()))
                                            .ToList();
                                        foreach (var group in groups)
                                        {
                                            ElementId targetTypeId = typeMap[group.GetTypeId()];
                                            GroupType? targetGroupType = doc.GetElement(targetTypeId) as GroupType;
                                            if (targetGroupType != null)
                                            {
                                                group.GroupType = targetGroupType;
                                                instancesSwapped++;
                                            }
                                        }
                                    }
                                    else if (primElement is AssemblyType)
                                    {
                                        var assemblies = new FilteredElementCollector(doc)
                                            .OfClass(typeof(AssemblyInstance))
                                            .Cast<AssemblyInstance>()
                                            .Where(a => dupTypeIds.Contains(a.GetTypeId()))
                                            .ToList();
                                        foreach (var assembly in assemblies)
                                        {
                                            ElementId targetTypeId = typeMap[assembly.GetTypeId()];
                                            assembly.ChangeTypeId(targetTypeId);
                                            instancesSwapped++;
                                        }
                                    }
                                }
                                report.InstancesSwappedCount = instancesSwapped;
                            }
                            trans.Commit();
                        }
                        catch (Exception ex)
                        {
                            trans.RollBack();
                            report.ExecutionSuccessStatus = false;
                            report.ErrorMessage = ex.Message;
                            app.Application.WriteJournalComment($"[MERGE_ERROR] Error processing cluster step 1 '{cluster.ClusterName}': {ex.Message}", true);
                            reports.Add(report);
                            continue;
                        }
                    }
                    // 3.5 AliasSwapEngine deep scan and swap (outside any active transaction!)
                    try
                    {
                        foreach (var kvp in typeMap)
                        {
                            AliasSwapEngine.SwapElementReferences(doc, kvp.Key, kvp.Value);
                        }
                    }
                    catch (Exception ex)
                    {
                        app.Application.WriteJournalComment($"[MERGE_ERROR] Error swapping alias references outside transaction: {ex.Message}", true);
                    }
                    // 4. Redundancy Deletion in Transaction 2
                    using (var trans2 = new Transaction(doc, $"Merge Cluster Step 2: {cluster.ClusterName}"))
                    {
                        trans2.Start();
                        try
                        {
                            Element primElement = doc.GetElement(primaryItem.RevitElementId.ToElementId());
                            int purgedCount = 0;
                            foreach (var dupItem in duplicateItems)
                            {
                                bool allTypesMergedOrMigrated = true;
                                foreach (var t in dupItem.Types)
                                {
                                    var mapping = cluster.TypeMappings.FirstOrDefault(m => m.SourceType != null && m.SourceType.RevitTypeId.ToElementId() == t.RevitTypeId.ToElementId());
                                    if (mapping != null && mapping.RecommendedAction == RecommendedAction.Exclude)
                                    {
                                        allTypesMergedOrMigrated = false;
                                        break;
                                    }
                                }
                                if (allTypesMergedOrMigrated)
                                {
                                    try
                                    {
                                        doc.Delete(dupItem.RevitElementId.ToElementId());
                                        purgedCount++;
                                    }
                                     catch (Exception ex)
                                     {
                                         try
                                         {
                                             System.IO.File.WriteAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "synthetic_test_error.txt"), ex.ToString());
                                         }
                                         catch {}
                                         report.ExecutionSuccessStatus = false;
                                         report.ErrorMessage = "Family deletion failed: " + ex.Message + " | " + ex.StackTrace;
                                     }
                                }
                                else
                                {
                                    if (primElement is Family || primElement is GroupType || primElement is AssemblyType)
                                    {
                                        foreach (var t in dupItem.Types)
                                        {
                                            var mapping = cluster.TypeMappings.FirstOrDefault(m => m.SourceType != null && m.SourceType.RevitTypeId.ToElementId() == t.RevitTypeId.ToElementId());
                                            if (mapping != null && mapping.RecommendedAction != RecommendedAction.Exclude)
                                            {
                                                try
                                                {
                                                    doc.Delete(t.RevitTypeId.ToElementId());
                                                    purgedCount++;
                                                }
                                                catch (Exception)
                                                {
                                                    // Silently handle symbol deletion error
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                            report.DuplicateTypesPurged = purgedCount;
                            trans2.Commit();
                            report.ExecutionSuccessStatus = true;
                        }
                        catch (Exception ex)
                        {
                            trans2.RollBack();
                            report.ExecutionSuccessStatus = false;
                            report.ErrorMessage = ex.Message;
                            app.Application.WriteJournalComment($"[MERGE_ERROR] Error processing cluster step 2 '{cluster.ClusterName}': {ex.Message}", true);
                        }
                    }
                    reports.Add(report);
                    // Safely update UI Queue collections on Dispatcher
                    if (currentWindow != null && report.ExecutionSuccessStatus)
                    {
                        currentWindow.Dispatcher.Invoke(() =>
                        {
                            currentQueue.QueuedClusters.Remove(cluster);
                            if (currentMainVM != null && currentMainVM.ScannedClusters.Contains(cluster))
                            {
                                currentMainVM.ScannedClusters.Remove(cluster);
                            }
                        });
                    }
                }
                if (!wasCancelled)
                {
                    transGroup.Assimilate();
                }
            }
            // Export execution summary to JSON file
            ExportStep6ResultsSummary(reports);
            // Re-enable UI window
            if (currentWindow != null)
            {
                currentWindow.Dispatcher.Invoke(() =>
                {
                    currentWindow.IsEnabled = true;
                    if (wasCancelled)
                    {
                        Autodesk.Revit.UI.TaskDialog.Show("Merge Process Cancelled", "The merge process was cancelled. All changes in this batch have been rolled back.");
                    }
                    else
                    {
                        int successCount = reports.Count(r => r.ExecutionSuccessStatus);
                        Autodesk.Revit.UI.TaskDialog.Show("Merge Duplicates Completed", $"{successCount} cluster(s) merged successfully.\nResults written to:\nMergeDuplicates_Step6_ExecutionResult.json");
                    }
                });
            }
        }
        private static void CopyParameterValue(Parameter sourceParam, Parameter targetParam)
        {
            if (sourceParam == null || targetParam == null || targetParam.IsReadOnly || !sourceParam.HasValue) return;
            switch (sourceParam.StorageType)
            {
                case StorageType.Double:
                    targetParam.Set(sourceParam.AsDouble());
                    break;
                case StorageType.Integer:
                    targetParam.Set(sourceParam.AsInteger());
                    break;
                case StorageType.String:
                    targetParam.Set(sourceParam.AsString());
                    break;
                case StorageType.ElementId:
                    targetParam.Set(sourceParam.AsElementId());
                    break;
            }
        }
                private static ParameterDefinitionSpec GetDefaultGroupAndType()
        {
            return ParameterDefinitionSpec.CreateDefault();
        }

        private static ParameterDefinitionSpec GetParamGroupAndTypeFromSource(Parameter sourceParam)
        {
            if (sourceParam == null) return GetDefaultGroupAndType();

            object? group = null;
            object? specType = null;

            Definition def = sourceParam.Definition;
            if (def != null)
            {
#if REVIT2022 || REVIT2023
                group = def.ParameterGroup;
                specType = def.ParameterType;
#else
                // Default baseline: Revit 2024+
                group = def.GetGroupTypeId();
                specType = def.GetDataType();
#endif

                // Reflection fallbacks for cross-version / mock execution contexts
                if (group == null)
                {
                    var groupProp = def.GetType().GetProperty("ParameterGroup");
                    if (groupProp != null)
                    {
                        group = groupProp.GetValue(def);
                    }
                    else
                    {
                        var groupMethod = def.GetType().GetMethod("GetGroupTypeId");
                        if (groupMethod != null)
                        {
                            group = groupMethod.Invoke(def, null);
                        }
                    }
                }

                if (specType == null)
                {
                    var typeProp = def.GetType().GetProperty("ParameterType");
                    if (typeProp != null)
                    {
                        specType = typeProp.GetValue(def);
                    }
                    else
                    {
                        var typeMethod = def.GetType().GetMethod("GetDataType");
                        if (typeMethod != null)
                        {
                            specType = typeMethod.Invoke(def, null);
                        }
                    }
                }
            }

            if (group == null || specType == null)
            {
                var defaultSpec = GetDefaultGroupAndType();
                group ??= defaultSpec.Group;
                specType ??= defaultSpec.SpecType;
            }

            return new ParameterDefinitionSpec(group, specType);
        }

        private static bool InjectParameterToFamily(FamilyManager famManager, string paramName, ParameterDefinitionSpec spec)
        {
            if (famManager == null || spec == null || spec.Group == null || spec.SpecType == null) return false;

            var addParamMethod = famManager.GetType().GetMethods()
                .FirstOrDefault(m => m.Name == "AddParameter" && m.GetParameters().Length == 4);

            if (addParamMethod != null)
            {
                try
                {
                    addParamMethod.Invoke(famManager, new object[] { paramName, spec.Group, spec.SpecType, false });
                    return true;
                }
                catch (Exception)
                {
                    return false;
                }
            }
            return false;
        }

        private void ExportStep6ResultsSummary(List<MergeExecutionReport> summaries)
        {
            try
            {
                var settings = new JsonSerializerSettings
                {
                    Formatting = Formatting.Indented
                };
                string json = JsonConvert.SerializeObject(summaries, settings);
                string assemblyPath = System.Reflection.Assembly.GetExecutingAssembly().Location;
                string? assemblyDir = Path.GetDirectoryName(assemblyPath);
                if (assemblyDir != null)
                {
                    string logPath = Path.Combine(assemblyDir, "MergeDuplicates_Step6_ExecutionResult.json");
                    File.WriteAllText(logPath, json);
                }
            }
            catch (Exception)
            {
                // Fail silently
            }
        }
        private string SanitizeRevitTypeName(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return "Unnamed_Type";
            char[] illegalChars = new char[] { '\\', ':', '{', '}', '[', ']', '|', ';', '<', '>', '?', '\'', '~' };
            string sanitized = input;
            foreach (char c in illegalChars)
            {
                sanitized = sanitized.Replace(c.ToString(), string.Empty);
            }
            sanitized = sanitized.Trim();
            return string.IsNullOrWhiteSpace(sanitized) ? "Unnamed_Type" : sanitized;
        }
        /// <summary>
        /// Gets the name of the external event handler.
        /// </summary>
        /// <returns>A string name of the handler.</returns>
        public string GetName()
        {
            return "Process Merge External Event Handler";
        }
    }
}



```

### File: MergeDuplicates/Services/RevitMergeDataCollector.cs
```csharp
using Synthetic.Shared.RevitAPI;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using Autodesk.Revit.DB;

using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations.Merge;

namespace Synthetic.Modules.MergeDuplicates.Services
{
    /// <summary>
    /// Service for querying the Revit document and extracting ElementModel POCOs for duplicate merging analysis.
    /// </summary>
    public static class RevitMergeDataCollector
    {
        /// <summary>
        /// Gets a string representation of a Revit Parameter's storage type and value.
        /// </summary>
        public static string GetParameterValueString(Parameter parameter, Document? doc = null)
        {
            if (parameter == null) return string.Empty;
            string storageType = parameter.StorageType.ToString();
            string valueString = string.Empty;
            switch (parameter.StorageType)
            {
                case StorageType.Double:
                    valueString = parameter.AsDouble().ToString();
                    break;
                case StorageType.Integer:
                    valueString = parameter.AsInteger().ToString();
                    break;
                case StorageType.String:
                    valueString = parameter.AsString() ?? string.Empty;
                    break;
                case StorageType.ElementId:
                    ElementId elementId = parameter.AsElementId();
                    if (elementId != null && elementId != ElementId.InvalidElementId)
                    {
                        if (doc != null)
                        {
                            Element element = doc.GetElement(elementId);
                            if (element != null)
                            {
                                valueString = element.Name;
                            }
                            else
                            {
#if REVIT2022 || REVIT2023
                                valueString = elementId.IntegerValue.ToString();
#else
                                valueString = elementId.Value.ToString();
#endif
                            }
                        }
                        else
                        {
#if REVIT2022 || REVIT2023
                            valueString = elementId.IntegerValue.ToString();
#else
                            valueString = elementId.Value.ToString();
#endif
                        }
                    }
                    break;
            }
            return $"{storageType}:{valueString}";
        }

        /// <summary>
        /// Gets the category name of the specified Revit element.
        /// </summary>
        public static string GetElementCategoryName(Document doc, Element element)
        {
            if (element == null) return "Unknown Category";

            if (element is Family family)
            {
                if (family.FamilyCategory != null) return family.FamilyCategory.Name;
                var symbolIds = family.GetFamilySymbolIds();
                if (symbolIds != null && symbolIds.Count > 0)
                {
                    var firstSymbol = doc.GetElement(symbolIds.First()) as FamilySymbol;
                    if (firstSymbol?.Category != null)
                    {
                        return firstSymbol.Category.Name;
                    }
                }
                return "Unknown Category";
            }
            else if (element is GroupType groupType)
            {
                return groupType.Category?.Name ?? "Model Groups";
            }
            else if (element is Autodesk.Revit.DB.Group group)
            {
                return group.GroupType?.Category?.Name ?? "Model Groups";
            }
            else if (element is AssemblyType assemblyType)
            {
                return assemblyType.Category?.Name ?? "Assemblies";
            }
            else if (element is AssemblyInstance assemblyInstance)
            {
                var typeElementId = assemblyInstance.GetTypeId();
                if (typeElementId != null && typeElementId != ElementId.InvalidElementId)
                {
                    var typeElement = doc.GetElement(typeElementId) as AssemblyType;
                    if (typeElement != null)
                    {
                        return typeElement.Category?.Name ?? "Assemblies";
                    }
                }
                return "Assemblies";
            }

            return element.Category?.Name ?? "Unknown Category";
        }

        /// <summary>
        /// Retrieves the target mergeable element (e.g. Family, GroupType, AssemblyType) from a given ElementId.
        /// </summary>
        public static Element? GetTargetElement(Document doc, ElementId elementId)
        {
            if (elementId == null || elementId == ElementId.InvalidElementId) return null;
            Element element = doc.GetElement(elementId);
            if (element == null) return null;

            // 1. Direct checks (typically selected in Project Browser or direct types)
            if (element is Family) return element;
            if (element is GroupType) return element;
            if (element is AssemblyType) return element;
            if (element is FamilySymbol patternFamilySymbol) return patternFamilySymbol.Family;
            if (element is ElementType elementType) return element;

            // 2. Instance checks (typically selected in Canvas)
            if (element is FamilyInstance familyInstance)
            {
                var symbolId = familyInstance.GetTypeId();
                if (symbolId != null && symbolId != ElementId.InvalidElementId)
                {
                    var familySymbolObj = doc.GetElement(symbolId) as FamilySymbol;
                    return familySymbolObj?.Family;
                }
            }
            if (element is Autodesk.Revit.DB.Group group)
            {
                return group.GroupType;
            }
            if (element is AssemblyInstance assemblyInstance)
            {
                var typeElementId = assemblyInstance.GetTypeId();
                if (typeElementId != null && typeElementId != ElementId.InvalidElementId)
                {
                    return doc.GetElement(typeElementId) as AssemblyType;
                }
            }

            // 3. Fallback using GetTypeId() for other instance elements
            var elemTypeId = element.GetTypeId();
            if (elemTypeId != null && elemTypeId != ElementId.InvalidElementId)
            {
                var typeElem = doc.GetElement(elemTypeId);
                if (typeElem is FamilySymbol fs) return fs.Family;
                if (typeElem is GroupType gt) return gt;
                if (typeElem is AssemblyType at) return at;
                if (typeElem is ElementType et2) return et2;
            }

            return null;
        }

        /// <summary>
        /// Converts a Revit Element to an ElementModel POCO, including instance counts, location, bounding box, and nested types.
        /// </summary>
        public static ElementModel ConvertToPoco(Document doc, Element elem)
        {
            var model = elem.ToModel(false);

            // Get instances to calculate count, location, bounding box using native quick filters first
            var instances = new List<Element>();
            if (elem is Family family)
            {
                var symbolIds = family.GetFamilySymbolIds();
                if (symbolIds != null && symbolIds.Count > 0)
                {
                    var symbolIdSet = new HashSet<ElementId>(symbolIds);
                    var familyInstances = new FilteredElementCollector(doc)
                        .OfClass(typeof(FamilyInstance))
                        .WhereElementIsNotElementType()
                        .Where(inst => symbolIdSet.Contains(inst.GetTypeId()))
                        .ToList();
                    instances.AddRange(familyInstances);
                }
            }
            else if (elem is GroupType gt)
            {
                var groupInstances = Select.GetInstancesFromElemType(gt, doc).ToList();
                instances.AddRange(groupInstances);
            }
            else if (elem is AssemblyType at)
            {
                var assemblyInstances = Select.GetInstancesFromElemType(at, doc).ToList();
                instances.AddRange(assemblyInstances);
            }
            else if (elem is ElementType et)
            {
                var typeInstances = Select.GetInstancesFromElemType(et, doc).ToList();
                instances.AddRange(typeInstances);
            }

            model.InstanceCount = instances.Count;

            var firstInstance = instances.FirstOrDefault();
            if (firstInstance != null)
            {
                if (firstInstance.Location is LocationPoint lp)
                {
                    model.Location = lp.Point.ToModel();
                }
                else if (firstInstance is FamilyInstance fi)
                {
                    model.Location = fi.GetTransform().Origin.ToModel();
                }
                else
                {
                    var bbox = firstInstance.get_BoundingBox(null);
                    if (bbox != null)
                    {
                        model.Location = ((bbox.Max + bbox.Min) * 0.5).ToModel();
                    }
                }
                model.BoundingBox = firstInstance.get_BoundingBox(null).ToModel();
            }

            // Populate NestedTypes if it is a Family
            if (elem is Family fam)
            {
                var symbolIds = fam.GetFamilySymbolIds();
                if (symbolIds != null)
                {
                    foreach (var sId in symbolIds)
                    {
                        var symbol = doc.GetElement(sId) as FamilySymbol;
                        if (symbol != null)
                        {
                            var nestedModel = symbol.ToModel(false);
                            model.NestedTypes.Add(nestedModel);
                        }
                    }
                }
            }

            return model;
        }

        /// <summary>
        /// Scans the Revit document quickly for duplicate elements.
        /// </summary>
        public static ObservableCollection<DuplicateClusterModel> RunFastScan(Document doc, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var elements = new List<Element>();

            // Collect Family, GroupType, and AssemblyType elements using ElementMulticlassFilter
            var classes = new List<Type> { typeof(Family), typeof(GroupType), typeof(AssemblyType) };
            var filter = new ElementMulticlassFilter(classes);
            var collector = new FilteredElementCollector(doc)
                .WherePasses(filter);

            foreach (var elem in collector)
            {
                token.ThrowIfCancellationRequested();
                if (elem is Family f)
                {
                    if (!f.IsInPlace)
                    {
                        elements.Add(f);
                    }
                }
                else
                {
                    elements.Add(elem);
                }
            }

            var models = new List<ElementModel>();
            foreach (var elem in elements)
            {
                token.ThrowIfCancellationRequested();
                models.Add(ConvertToPoco(doc, elem));
            }

            return MergeAnalysisEngine.BuildClustersFromModels(models, token);
        }

        /// <summary>
        /// Performs a targeted scan on specific selected element IDs in the Revit document.
        /// </summary>
        public static ObservableCollection<DuplicateClusterModel> RunTargetedScan(Document doc, ICollection<ElementId> selectedIds, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var clusters = new ObservableCollection<DuplicateClusterModel>();

            if (selectedIds == null || selectedIds.Count == 0) return clusters;

            var selectedCategories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var selectedBaseNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var id in selectedIds)
            {
                token.ThrowIfCancellationRequested();
                var originalElem = doc.GetElement(id);
                if (originalElem == null) continue;

                var target = GetTargetElement(doc, id);
                if (target == null) continue;

                string categoryName = GetElementCategoryName(doc, originalElem);
                selectedCategories.Add(categoryName);
                selectedBaseNames.Add(MergeAnalysisEngine.GetBaseName(target.Name));
            }

            if (selectedCategories.Count == 0 || selectedBaseNames.Count == 0)
            {
                return clusters;
            }

            var elements = new List<Element>();

            // Collect Family, GroupType, and AssemblyType elements matching selection parameters using ElementMulticlassFilter
            var classes = new List<Type> { typeof(Family), typeof(GroupType), typeof(AssemblyType) };
            var filter = new ElementMulticlassFilter(classes);
            var collector = new FilteredElementCollector(doc)
                .WherePasses(filter);

            foreach (var elem in collector)
            {
                token.ThrowIfCancellationRequested();
                if (elem is Family fam)
                {
                    if (fam.IsInPlace) continue;
                    string cat = GetElementCategoryName(doc, fam);
                    if (selectedCategories.Contains(cat) && selectedBaseNames.Contains(MergeAnalysisEngine.GetBaseName(fam.Name)))
                    {
                        elements.Add(fam);
                    }
                }
                else
                {
                    string cat = GetElementCategoryName(doc, elem);
                    if (selectedCategories.Contains(cat) && selectedBaseNames.Contains(MergeAnalysisEngine.GetBaseName(elem.Name)))
                    {
                        elements.Add(elem);
                    }
                }
            }

            // Collect generic ElementTypes matching selection parameters (e.g. WallType)
            foreach (var id in selectedIds)
            {
                token.ThrowIfCancellationRequested();
                var target = GetTargetElement(doc, id);
                if (target == null) continue;

                if (target is ElementType && !(target is GroupType) && !(target is AssemblyType) && !(target is FamilySymbol))
                {
                    var typeClass = target.GetType();
                    var matchingTypes = new FilteredElementCollector(doc)
                        .WhereElementIsElementType()
                        .OfClass(typeClass)
                        .Cast<ElementType>();
                    foreach (var et in matchingTypes)
                    {
                        token.ThrowIfCancellationRequested();
                        string cat = GetElementCategoryName(doc, et);
                        if (selectedCategories.Contains(cat) && selectedBaseNames.Contains(MergeAnalysisEngine.GetBaseName(et.Name)))
                        {
                            if (!elements.Any(x => x.Id == et.Id))
                            {
                                elements.Add(et);
                            }
                        }
                    }
                }
            }

            var models = new List<ElementModel>();
            foreach (var elem in elements)
            {
                token.ThrowIfCancellationRequested();
                models.Add(ConvertToPoco(doc, elem));
            }

            return MergeAnalysisEngine.BuildClustersFromModels(models, token);
        }

        /// <summary>
        /// Collects candidate straggler elements matching a given category name from the Revit document.
        /// </summary>
        public static List<Tuple<string, ElementId>> GetCandidateStragglers(Document doc, string categoryName, HashSet<ElementId> existingIds)
        {
            var candidateList = new List<Tuple<string, ElementId>>();

            // Use ElementMulticlassFilter to collect Families, GroupTypes, and AssemblyTypes
            var classes = new List<Type> { typeof(Family), typeof(GroupType), typeof(AssemblyType) };
            var filter = new ElementMulticlassFilter(classes);
            var collector = new FilteredElementCollector(doc)
                .WherePasses(filter);

            foreach (var elem in collector)
            {
                if (existingIds.Contains(elem.Id)) continue;
                if (elem is Family fam && fam.IsInPlace) continue;

                string cat = GetElementCategoryName(doc, elem);
                if (cat.Equals(categoryName, StringComparison.OrdinalIgnoreCase))
                {
                    candidateList.Add(Tuple.Create($"{elem.Name} ({elem.GetType().Name})", elem.Id));
                }
            }

            // Also collect generic ElementTypes if the category matches
            var elementTypes = new FilteredElementCollector(doc)
                .WhereElementIsElementType()
                .Cast<ElementType>();
            foreach (var et in elementTypes)
            {
                if (existingIds.Contains(et.Id)) continue;
                if (et is GroupType || et is AssemblyType || et is FamilySymbol) continue; // Exclude since already handled or nested in families

                string cat = GetElementCategoryName(doc, et);
                if (cat.Equals(categoryName, StringComparison.OrdinalIgnoreCase))
                {
                    candidateList.Add(Tuple.Create($"{et.Name} ({et.GetType().Name})", et.Id));
                }
            }

            return candidateList;
        }
    }
}


```

### File: MergeDuplicates/ViewModels/MergeDetailedReviewViewModel.cs
```csharp
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using Newtonsoft.Json;
using Autodesk.Revit.DB;

using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;
using Synthetic.Infrastructure.Serialization;

using Synthetic.Shared.RevitAPI;
using Synthetic.Infrastructure.IO;

using Synthetic.Shared.UI;

using Synthetic.RevitDOM.Operations.Merge;
using Synthetic.Modules.StandardsManagement.ViewModels;
namespace Synthetic.Modules.MergeDuplicates.ViewModels
{
    /// <summary>
    /// ViewModel for the detailed merge review view.
    /// </summary>
    public class MergeDetailedReviewViewModel : ViewModelBase
    {
        private DuplicateClusterModel _cluster;
        private TypeMappingModel? _selectedTypeMapping;

        private Action<DuplicateClusterModel>? _commitCallback;
        private Action<DuplicateClusterModel>? _moveItemCallback;

        /// <summary>
        /// Initializes a new instance of the <see cref="MergeDetailedReviewViewModel"/> class.
        /// </summary>
        /// <param name="cluster">The duplicate cluster model to review.</param>
        /// <param name="commitCallback">Optional callback when committing a cluster.</param>
        /// <param name="moveItemCallback">Optional callback when moving an item.</param>
        public MergeDetailedReviewViewModel(
            DuplicateClusterModel cluster, 
            Action<DuplicateClusterModel>? commitCallback = null,
            Action<DuplicateClusterModel>? moveItemCallback = null)
        {
            _cluster = cluster;
            _commitCallback = commitCallback;
            _moveItemCallback = moveItemCallback;

            CmdCommitToQueue = new RelayCommand(ExecuteCommitToQueue);
            CmdMoveItem = new RelayCommand(ExecuteMoveItem);

            if (cluster != null)
            {
                // Set up default view grouping by Family
                var view = System.Windows.Data.CollectionViewSource.GetDefaultView(cluster.TypeMappings);
                if (view != null)
                {
                    view.GroupDescriptions.Clear();
                    view.GroupDescriptions.Add(new System.Windows.Data.PropertyGroupDescription("SourceFamily.ItemName"));
                }

                // Subscribe to SelectedPrimary changes to re-evaluate recommendations
                cluster.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName == nameof(DuplicateClusterModel.SelectedPrimary) || e.PropertyName == nameof(DuplicateClusterModel.IsBlocked))
                    {
                        MergeAnalysisEngine.GenerateRecommendations(cluster);
                        if (cluster.TypeMappings.Count > 0)
                        {
                            SelectedTypeMapping = cluster.TypeMappings[0];
                        }
                        else
                        {
                            SelectedTypeMapping = null;
                        }
                        System.Windows.Data.CollectionViewSource.GetDefaultView(cluster.TypeMappings)?.Refresh();
                        OnPropertyChanged(nameof(Rows));
                        OnPropertyChanged(nameof(IsBlockedMessageVisible));
                        OnPropertyChanged(nameof(BlockedReasonMessage));
                    }
                };

                if (cluster.TypeMappings.Count > 0)
                {
                    SelectedTypeMapping = cluster.TypeMappings[0];
                }
            }
        }

        /// <summary>
        /// Gets or sets the duplicate cluster model under review.
        /// </summary>
        public DuplicateClusterModel Cluster
        {
            get => _cluster;
            set
            {
                if (SetProperty(ref _cluster, value))
                {
                    OnPropertyChanged(nameof(IsBlockedMessageVisible));
                    OnPropertyChanged(nameof(BlockedReasonMessage));
                }
            }
        }

        /// <summary>
        /// Gets a value indicating whether the blocked message is visible.
        /// </summary>
        public bool IsBlockedMessageVisible
        {
            get => Cluster != null && Cluster.IsBlocked;
        }

        /// <summary>
        /// Gets the reason message why the cluster is blocked from being queued.
        /// </summary>
        public string BlockedReasonMessage
        {
            get
            {
                if (Cluster == null) return string.Empty;

                bool hasSchema = Cluster.HasSchemaMismatch;
                bool hasConflicts = false;
                if (Cluster.TypeMappings != null)
                {
                    foreach (var mapping in Cluster.TypeMappings)
                    {
                        if (mapping.ParameterResolutions != null && mapping.ParameterResolutions.Any(r => r.HasConflict || r.IsSchemaMismatch))
                        {
                            hasConflicts = true;
                            break;
                        }
                    }
                }

                if (hasSchema && hasConflicts)
                {
                    return "Blocked from Queue: Schema mismatches and parameter value conflicts detected. Resolve them below.";
                }
                if (hasSchema)
                {
                    return "Blocked from Queue: Schema mismatch detected. Please select compatible type mappings.";
                }
                if (hasConflicts)
                {
                    return "Blocked from Queue: Parameter value conflicts detected. Choose winning values under Winning Value Selection.";
                }

                return "Blocked from Queue: Conflicts or mismatches must be resolved.";
            }
        }

        /// <summary>
        /// Gets or sets the currently selected type mapping.
        /// </summary>
        public TypeMappingModel? SelectedTypeMapping
        {
            get => _selectedTypeMapping;
            set
            {
                var oldMapping = _selectedTypeMapping;
                if (SetProperty(ref _selectedTypeMapping, value))
                {
                    if (oldMapping != null)
                    {
                        oldMapping.PropertyChanged -= SelectedTypeMapping_PropertyChanged;
                    }
                    if (_selectedTypeMapping != null)
                    {
                        _selectedTypeMapping.PropertyChanged += SelectedTypeMapping_PropertyChanged;
                        // Reload parameters for the newly selected mapping
                        MergeAnalysisEngine.UpdateParameterResolutions(_selectedTypeMapping);
                    }
                    OnPropertyChanged(nameof(Rows));
                    OnPropertyChanged(nameof(DiffComparisonContext));
                }
            }
        }

        private void SelectedTypeMapping_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(TypeMappingModel.TargetType) ||
                e.PropertyName == nameof(TypeMappingModel.RecommendedAction) ||
                e.PropertyName == nameof(TypeMappingModel.MigrateRenameText))
            {
                if (SelectedTypeMapping != null)
                {
                    MergeAnalysisEngine.UpdateParameterResolutions(SelectedTypeMapping);
                }
                OnPropertyChanged(nameof(Rows));
                OnPropertyChanged(nameof(DiffComparisonContext));
            }
        }

        /// <summary>
        /// Gets the text context of the parameter difference comparison.
        /// </summary>
        public string DiffComparisonContext
        {
            get
            {
                if (SelectedTypeMapping == null) return string.Empty;

                string target = string.Empty;
                if (SelectedTypeMapping.RecommendedAction == RecommendedAction.Merge)
                {
                    target = SelectedTypeMapping.TargetType?.Name ?? string.Empty;
                }
                else if (SelectedTypeMapping.RecommendedAction == RecommendedAction.Migrate)
                {
                    target = SelectedTypeMapping.MigrateRenameText ?? string.Empty;
                }
                else if (SelectedTypeMapping.RecommendedAction == RecommendedAction.Exclude)
                {
                    target = "[Excluded]";
                }

                return $"Comparing: {SelectedTypeMapping.SourceType?.Name} to {target}";
            }
        }

        /// <summary>
        /// Gets the parameter diff rows for the selected type mapping.
        /// </summary>
        public ObservableCollection<ParameterDiffRowModel>? Rows
        {
            get => SelectedTypeMapping?.ParameterResolutions;
        }

        /// <summary>
        /// Gets the command to commit the cluster to the queue.
        /// </summary>
        public ICommand CmdCommitToQueue { get; }

        /// <summary>
        /// Gets the command to move the duplicate item out of this cluster.
        /// </summary>
        public ICommand CmdMoveItem { get; }

        /// <summary>
        /// Gets or sets the action to close the window.
        /// </summary>
        [JsonIgnore]
        public Action? CloseAction { get; set; }

        private void ExecuteCommitToQueue(object parameter)
        {
            if (Cluster != null)
            {
                Cluster.IsBlocked = false;
                Cluster.ParameterResolutions = new Dictionary<string, ElementIdModel>();
                foreach (var mapping in Cluster.TypeMappings)
                {
                    if (mapping.ParameterResolutions != null)
                    {
                        foreach (var row in mapping.ParameterResolutions)
                        {
                            if (row.WinningValueElementId != null)
                            {
                                Cluster.ParameterResolutions[row.ParameterName] = row.WinningValueElementId;
                            }
                        }
                    }
                }
                _commitCallback?.Invoke(Cluster);
            }
            CloseAction?.Invoke();
        }

        private void ExecuteMoveItem(object parameter)
        {
            if (Cluster != null)
            {
                _moveItemCallback?.Invoke(Cluster);

                if (Cluster.Items == null || Cluster.Items.Count <= 1)
                {
                    CloseAction?.Invoke();
                    return;
                }

                MergeAnalysisEngine.GenerateRecommendations(Cluster);
                if (Cluster.TypeMappings.Count > 0)
                {
                    SelectedTypeMapping = Cluster.TypeMappings[0];
                }
                else
                {
                    SelectedTypeMapping = null;
                }
                System.Windows.Data.CollectionViewSource.GetDefaultView(Cluster.TypeMappings)?.Refresh();
                OnPropertyChanged(nameof(Rows));
            }
        }
    }
}
```

### File: MergeDuplicates/ViewModels/MergeDuplicatesViewModel.cs
```csharp
using Synthetic.Modules.MergeDuplicates.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using Newtonsoft.Json;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;
using Synthetic.Infrastructure.Serialization;

using Synthetic.Shared.RevitAPI;
using Synthetic.Infrastructure.IO;

using Synthetic.Shared.UI;
using Synthetic.Modules.MergeDuplicates.Handlers;

using Synthetic.Modules.MergeDuplicates.Views;
using Synthetic.RevitDOM.Operations.Merge;
using Synthetic.RevitDOM.Operations.Merge;
using Synthetic.Modules.StandardsManagement.ViewModels;
namespace Synthetic.Modules.MergeDuplicates.ViewModels
{
    /// <summary>
    /// ViewModel for scanning and merging duplicate elements in the Revit database.
    /// </summary>
    public class MergeDuplicatesViewModel : ViewModelBase
    {
        private ObservableCollection<DuplicateClusterModel> _scannedClusters;
        private MergeQueueViewModel _queueVM;
        private double _progressValue;
        private double _progressMax = 100;
        private IntPtr _mainWindowHandle;
        private Document? _document;
        private ProcessMergeEventHandler? _mergeHandler;
        private ExternalEvent? _mergeEvent;
        private System.Threading.CancellationTokenSource _cts = new System.Threading.CancellationTokenSource();

        /// <summary>
        /// Initializes a new instance of the <see cref="MergeDuplicatesViewModel"/> class.
        /// </summary>
        public MergeDuplicatesViewModel()
        {
            _scannedClusters = new ObservableCollection<DuplicateClusterModel>();
            _queueVM = new MergeQueueViewModel(cluster =>
            {
                if (cluster != null && !_scannedClusters.Contains(cluster))
                {
                    _scannedClusters.Add(cluster);
                }
            });

            CmdScanModel = new RelayCommand(ExecuteScanModel);
            CmdLoadSelection = new RelayCommand(ExecuteLoadSelection);
            CmdAddToQueue = new RelayCommand(ExecuteAddToQueue);
            CmdProcessMerge = new RelayCommand(ExecuteProcessMerge);
            CmdCancelScan = new RelayCommand(ExecuteCancelScan);
            CmdMoveItem = new RelayCommand(ExecuteMoveItem);
            CmdDetailedReview = new RelayCommand(ExecuteDetailedReview);
            CmdAddStragglers = new RelayCommand(ExecuteAddStragglers);
            CmdRemoveFromQueue = new RelayCommand(ExecuteRemoveFromQueue);

            try
            {
                _mergeHandler = new ProcessMergeEventHandler();
                _mergeEvent = ExternalEvent.Create(_mergeHandler);
            }
            catch (Exception)
            {
                // Safety fallback if instantiated outside Revit API thread/session context
            }
        }

        /// <summary>
        /// Gets or sets the main window handle.
        /// </summary>
        [JsonIgnore]
        public IntPtr MainWindowHandle
        {
            get => _mainWindowHandle;
            set => SetProperty(ref _mainWindowHandle, value);
        }

        /// <summary>
        /// Gets or sets the active Revit document.
        /// </summary>
        [JsonIgnore]
        public Document? Document
        {
            get => _document;
            set => SetProperty(ref _document, value);
        }

        /// <summary>
        /// Gets or sets the list of scanned duplicate clusters.
        /// </summary>
        public ObservableCollection<DuplicateClusterModel> ScannedClusters
        {
            get => _scannedClusters;
            set => SetProperty(ref _scannedClusters, value);
        }

        /// <summary>
        /// Gets or sets the merge queue view model.
        /// </summary>
        public MergeQueueViewModel QueueVM
        {
            get => _queueVM;
            set
            {
                if (SetProperty(ref _queueVM, value))
                {
                    OnPropertyChanged(nameof(QueuedClusters));
                }
            }
        }

        /// <summary>
        /// Gets the collection of duplicate clusters queued for merging.
        /// </summary>
        public ObservableCollection<DuplicateClusterModel>? QueuedClusters => QueueVM?.QueuedClusters;

        /// <summary>
        /// Gets or sets the current progress value of the scan.
        /// </summary>
        public double ProgressValue
        {
            get => _progressValue;
            set => SetProperty(ref _progressValue, value);
        }

        /// <summary>
        /// Gets or sets the maximum progress value of the scan.
        /// </summary>
        public double ProgressMax
        {
            get => _progressMax;
            set => SetProperty(ref _progressMax, value);
        }

        /// <summary>
        /// Gets the command to scan the model for duplicates.
        /// </summary>
        public ICommand CmdScanModel { get; }

        /// <summary>
        /// Gets the command to load the current Revit selection.
        /// </summary>
        public ICommand CmdLoadSelection { get; }

        /// <summary>
        /// Gets the command to add a cluster to the merge queue.
        /// </summary>
        public ICommand CmdAddToQueue { get; }

        /// <summary>
        /// Gets the command to remove a cluster from the merge queue.
        /// </summary>
        public ICommand CmdRemoveFromQueue { get; }

        /// <summary>
        /// Gets the command to execute the merge queue.
        /// </summary>
        public ICommand CmdProcessMerge { get; }

        /// <summary>
        /// Gets the command to cancel an active scan.
        /// </summary>
        public ICommand CmdCancelScan { get; }

        /// <summary>
        /// Gets the command to move an item from one cluster to another.
        /// </summary>
        public ICommand CmdMoveItem { get; }

        /// <summary>
        /// Gets the command to launch the detailed review window for a cluster.
        /// </summary>
        public ICommand CmdDetailedReview { get; }
        /// <summary>
        /// Gets the command to add straggler elements to the duplicate cluster.
        /// </summary>
        public ICommand CmdAddStragglers { get; }

        private void ExecuteScanModel(object parameter)
        {
            // Stub: Revit API logic & Analysis Engine will be implemented in subsequent steps.
        }

        private void ExecuteLoadSelection(object parameter)
        {
            // Stub: Revit API logic & Analysis Engine will be implemented in subsequent steps.
        }

        private void ExecuteAddToQueue(object parameter)
        {
            if (Document == null) return;
            if (parameter is DuplicateClusterModel cluster)
            {
                // Run deep scan to ensure current state is up-to-date
                MergeAnalysisEngine.RunDeepScan(cluster, System.Threading.CancellationToken.None);

                // Run GenerateRecommendations to analyze parameter conflicts
                MergeAnalysisEngine.GenerateRecommendations(cluster);

                // Check if there are schema mismatches or parameter conflicts
                bool hasSchemaMismatch = cluster.HasSchemaMismatch;
                bool hasConflicts = false;

                foreach (var mapping in cluster.TypeMappings)
                {
                    if (mapping.ParameterResolutions.Any(r => r.HasConflict || r.IsSchemaMismatch))
                    {
                        hasConflicts = true;
                    }
                }

                // Block if mismatches/conflicts exist and no manual resolutions have been committed
                int resolutionsCount = cluster.ParameterResolutions?.Count ?? 0;
                bool isBlocked = (hasSchemaMismatch || hasConflicts) && (resolutionsCount == 0);

                if (isBlocked)
                {
                    cluster.IsBlocked = true;
                    ExecuteDetailedReview(cluster);
                    return;
                }

                cluster.IsBlocked = false;
                if (QueueVM != null && !QueueVM.QueuedClusters.Contains(cluster))
                {
                    QueueVM.QueuedClusters.Add(cluster);
                    ScannedClusters.Remove(cluster);
                }
            }
        }

        private void ExecuteMoveItem(object parameter)
        {
            if (!(parameter is DuplicateClusterModel sourceCluster)) return;
            if (sourceCluster.Items == null || sourceCluster.Items.Count == 0) return;

            // Prompt 1: Select item to move
            var itemNames = sourceCluster.Items.Select(i => i.ItemName).ToList();
            var itemSelectVM = new DropdownSelectionViewModel
            {
                Title = "Move Item - Select Item",
                Instruction = $"Select an item to move from '{sourceCluster.ClusterName}':",
                ItemLabel = "Item to Move:",
                Items = itemNames,
                IsSorted = true
            };

            var itemSelectView = new Synthetic.Shared.UI.DropdownSelectionView(MainWindowHandle)
            {
                DataContext = itemSelectVM
            };

            if (itemSelectView.ShowDialog() != true) return;

            string? selectedItemName = itemSelectVM.SelectedItem;
            var itemToMove = sourceCluster.Items.FirstOrDefault(i => i.ItemName == selectedItemName);
            if (itemToMove == null) return;

            // Prompt 2: Select target cluster
            var targetClusterNames = ScannedClusters
                .Where(c => c != sourceCluster)
                .Select(c => c.ClusterName)
                .ToList();

            if (targetClusterNames.Count == 0)
            {
                Autodesk.Revit.UI.TaskDialog.Show("Move Item", "There are no other clusters available to move this item to.");
                return;
            }

            var clusterSelectVM = new DropdownSelectionViewModel
            {
                Title = "Move Item - Select Target Cluster",
                Instruction = $"Select target cluster for item '{itemToMove.ItemName}':",
                ItemLabel = "Target Cluster:",
                Items = targetClusterNames,
                IsSorted = true
            };

            var clusterSelectView = new Synthetic.Shared.UI.DropdownSelectionView(MainWindowHandle)
            {
                DataContext = clusterSelectVM
            };

            if (clusterSelectView.ShowDialog() != true) return;

            string? selectedClusterName = clusterSelectVM.SelectedItem;
            var targetCluster = ScannedClusters.FirstOrDefault(c => c.ClusterName == selectedClusterName);
            if (targetCluster == null) return;

            // Perform movement
            sourceCluster.Items.Remove(itemToMove);
            targetCluster.Items.Add(itemToMove);

            // Re-evaluate primary item for source cluster if the item we moved was primary
            if (itemToMove.IsPrimary && sourceCluster.Items.Count > 0)
            {
                var newPrimary = sourceCluster.Items.OrderBy(i => i.ItemName.Length).First();
                sourceCluster.UpdatePrimaryItem(newPrimary);
            }

            // Set primary for target cluster (coordinates sync)
            if (itemToMove.IsPrimary)
            {
                targetCluster.UpdatePrimaryItem(itemToMove);
            }
            else
            {
                if (targetCluster.SelectedPrimary == null && targetCluster.Items.Count > 0)
                {
                    var newPrimary = targetCluster.Items.OrderBy(i => i.ItemName.Length).First();
                    targetCluster.UpdatePrimaryItem(newPrimary);
                }
            }

            // Run deep scan on both affected clusters
            MergeAnalysisEngine.RunDeepScan(sourceCluster, System.Threading.CancellationToken.None);
            MergeAnalysisEngine.RunDeepScan(targetCluster, System.Threading.CancellationToken.None);

            // If source cluster now has 1 or fewer items, remove it from list
            if (sourceCluster.Items.Count <= 1)
            {
                ScannedClusters.Remove(sourceCluster);
            }
        }

        private void ExecuteDetailedReview(object parameter)
        {
            if (Document == null) return;
            if (parameter is DuplicateClusterModel cluster)
            {
                if (cluster.TypeMappings == null || cluster.TypeMappings.Count == 0)
                {
                    MergeAnalysisEngine.RunDeepScan(cluster, System.Threading.CancellationToken.None);
                    MergeAnalysisEngine.GenerateRecommendations(cluster);
                }

                var vm = new MergeDetailedReviewViewModel(cluster, c =>
                {
                    // Callback when confirmed: Add directly to queue, bypassing the gatekeeper since it has been resolved
                    c.IsBlocked = false;
                    if (QueueVM != null && !QueueVM.QueuedClusters.Contains(c))
                    {
                        QueueVM.QueuedClusters.Add(c);
                        ScannedClusters.Remove(c);
                    }
                }, c =>
                {
                    // Callback when Move item is requested from Detailed Review
                    ExecuteMoveItem(c);
                });
                var view = new Views.MergeDetailedReviewWindow(MainWindowHandle)
                {
                    DataContext = vm
                };
                view.Show();
            }
        }

        private void ExecuteProcessMerge(object parameter)
        {
            if (QueueVM == null || QueueVM.QueuedClusters.Count == 0)
            {
                Autodesk.Revit.UI.TaskDialog.Show("Merge Duplicates", "No merges in the queue to process.");
                return;
            }

            if (_mergeHandler != null && _mergeEvent != null)
            {
                var window = parameter as System.Windows.Window;
                // Pass _cts.Token to external event handler request
                _mergeHandler.QueueRequest(QueueVM, window, this, _cts.Token);
                _mergeEvent.Raise();
            }
        }

        private void ExecuteCancelScan(object parameter)
        {
            if (_cts != null)
            {
                _cts.Cancel();
                _cts.Dispose();
            }
            _cts = new System.Threading.CancellationTokenSource();
        }


        private void ExecuteRemoveFromQueue(object parameter)
        {
            if (parameter is DuplicateClusterModel cluster)
            {
                if (QueueVM != null && QueueVM.QueuedClusters.Contains(cluster))
                {
                    QueueVM.QueuedClusters.Remove(cluster);
                    if (!ScannedClusters.Contains(cluster))
                    {
                        ScannedClusters.Add(cluster);
                    }
                }
            }
        }

        private void ExecuteAddStragglers(object parameter)
        {
            if (parameter is DuplicateClusterModel cluster)
            {
                if (Document == null) return;

                // Determine category name from first item or default to CategoryName of cluster items
                string? categoryName = cluster.Items.FirstOrDefault()?.CategoryName;
                if (string.IsNullOrEmpty(categoryName)) return;

                // Find candidate elements in the document of the same category
                var candidateList = new List<Tuple<string, ElementId>>();
                var existingIds = new HashSet<ElementId>(cluster.Items.Where(i => i.RevitElementId != null).Select(i => i.RevitElementId!.ToElementId()));

                candidateList = RevitMergeDataCollector.GetCandidateStragglers(Document!, categoryName, existingIds);

                if (candidateList.Count == 0)
                {
                    Autodesk.Revit.UI.TaskDialog.Show("Add Stragglers", $"No other elements of category '{categoryName}' found in the document.");
                    return;
                }

                var listVM = new ListByCheckboxViewModel
                {
                    Title = "Add Stragglers",
                    Instruction = $"Select elements of category '{categoryName}' to force into cluster '{cluster.ClusterName}':",
                    IsSorted = true
                };
                listVM.SetItems(candidateList);

                var listWindow = new Synthetic.Shared.UI.ListByCheckboxView(MainWindowHandle)
                {
                    DataContext = listVM
                };

                if (listWindow.ShowDialog() == true)
                {
                    var selectedIds = listVM.CheckedElementIds;
                    foreach (var id in selectedIds)
                    {
                        var elem = Document!.GetElement(id);
                        if (elem == null) continue;

                        // Convert to POCO first and use the shared engine to create the item model
                        var elementModel = RevitMergeDataCollector.ConvertToPoco(Document!, elem);
                        var item = MergeAnalysisEngine.CreateItemFromModel(elementModel, categoryName!);

                        cluster.Items.Add(item);
                    }

                    // Run deep scan and regenerate recommendations on updated cluster
                    MergeAnalysisEngine.RunDeepScan(cluster, System.Threading.CancellationToken.None);
                    MergeAnalysisEngine.GenerateRecommendations(cluster);
                    cluster.IsBlocked = false; // Reset block status so they can attempt queueing again
                }
            }
        }
    }
}
```

### File: MergeDuplicates/ViewModels/MergeQueueViewModel.cs
```csharp
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;

using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;
using Synthetic.Infrastructure.Serialization;

using Synthetic.Shared.UI;
using Synthetic.RevitDOM.Operations.Merge;
using Synthetic.Modules.StandardsManagement.ViewModels;
namespace Synthetic.Modules.MergeDuplicates.ViewModels
{
    /// <summary>
    /// ViewModel for managing a queue of elements to merge.
    /// </summary>
    public class MergeQueueViewModel : ViewModelBase
    {
        private ObservableCollection<DuplicateClusterModel> _queuedClusters;
        private readonly Action<DuplicateClusterModel>? _onRemoved;

        /// <summary>
        /// Initializes a new instance of the <see cref="MergeQueueViewModel"/> class.
        /// </summary>
        public MergeQueueViewModel() : this(null)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="MergeQueueViewModel"/> class.
        /// </summary>
        /// <param name="onRemoved">Callback triggered when an item is removed from the queue.</param>
        public MergeQueueViewModel(Action<DuplicateClusterModel>? onRemoved)
        {
            _queuedClusters = new ObservableCollection<DuplicateClusterModel>();
            _onRemoved = onRemoved;

            CmdRemoveFromQueue = new RelayCommand(OnRemoveFromQueue);
            CmdClearQueue = new RelayCommand(OnClearQueue);
        }

        /// <summary>
        /// Gets or sets the collection of duplicate clusters queued for merging.
        /// </summary>
        public ObservableCollection<DuplicateClusterModel> QueuedClusters
        {
            get => _queuedClusters;
            set => SetProperty(ref _queuedClusters, value);
        }

        /// <summary>
        /// Gets the command to remove a cluster from the merge queue.
        /// </summary>
        public ICommand CmdRemoveFromQueue { get; }

        /// <summary>
        /// Gets the command to clear the entire merge queue.
        /// </summary>
        public ICommand CmdClearQueue { get; }

        /// <summary>
        /// Removes a duplicate cluster from the queue.
        /// </summary>
        /// <param name="cluster">The duplicate cluster to remove.</param>
        public void RemoveFromQueue(DuplicateClusterModel cluster)
        {
            if (cluster != null && _queuedClusters.Contains(cluster))
            {
                _queuedClusters.Remove(cluster);
                _onRemoved?.Invoke(cluster);
            }
        }

        /// <summary>
        /// Clears all duplicate clusters from the queue.
        /// </summary>
        public void ClearQueue()
        {
            var clusters = _queuedClusters.ToList();
            _queuedClusters.Clear();
            foreach (var cluster in clusters)
            {
                _onRemoved?.Invoke(cluster);
            }
        }

        private void OnRemoveFromQueue(object parameter)
        {
            if (parameter is DuplicateClusterModel cluster)
            {
                RemoveFromQueue(cluster);
            }
        }

        private void OnClearQueue(object parameter)
        {
            ClearQueue();
        }
    }
}
```

### File: MergeDuplicates/Views/MergeDetailedReviewWindow.xaml
```xml
<Window x:Class="Synthetic.Modules.MergeDuplicates.Views.MergeDetailedReviewWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:vm="clr-namespace:Synthetic.Modules.MergeDuplicates.ViewModels"
        xmlns:models="clr-namespace:Synthetic.RevitDOM.Operations.Merge"
        xmlns:local="clr-namespace:Synthetic.Shared.UI"
        xmlns:sys="clr-namespace:System;assembly=mscorlib"
        Title="Merge Duplicates - Detailed Review &amp; Conflict Diff Tool"
        Height="650" Width="950" MinHeight="500" MinWidth="800"
        WindowStartupLocation="CenterOwner" ResizeMode="CanResizeWithGrip" ShowInTaskbar="False"
        Style="{DynamicResource SyntheticWindowStyle}"
        local:WindowChromeBehavior.EnableWindowCommands="True">

    <Window.Resources>
        <ResourceDictionary>
            <ResourceDictionary.MergedDictionaries>
                <ResourceDictionary Source="../../../Shared/UI/SyntheticTheme.xaml"/>
            </ResourceDictionary.MergedDictionaries>

            <!-- Non-visual converters/providers that must remain local -->
            <local:EnumToBooleanConverter x:Key="EnumToBooleanConverter"/>
            <BooleanToVisibilityConverter x:Key="BoolToVis"/>

            <ObjectDataProvider x:Key="RecommendedActionEnumProvider" MethodName="GetValues"
                                ObjectType="{x:Type sys:Enum}">
                <ObjectDataProvider.MethodParameters>
                    <x:Type TypeName="models:RecommendedAction"/>
                </ObjectDataProvider.MethodParameters>
            </ObjectDataProvider>
        </ResourceDictionary>
    </Window.Resources>

    <Grid Margin="15">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>  <!-- Header -->
            <RowDefinition Height="1.2*"/> <!-- Type Mapping Matrix -->
            <RowDefinition Height="Auto"/>  <!-- Splitter -->
            <RowDefinition Height="1.5*"/> <!-- Parameter Diff Matrix -->
            <RowDefinition Height="Auto"/>  <!-- Footer -->
        </Grid.RowDefinitions>

        <!-- 1. HEADER SECTION -->
        <Border Grid.Row="0"
                Background="{DynamicResource Synthetic.Brushes.ControlSurface}"
                BorderBrush="{DynamicResource Synthetic.Brushes.BorderNormal}"
                BorderThickness="1" CornerRadius="4" Padding="12" Margin="0,0,0,12">
            <Grid>
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="*"/>
                    <ColumnDefinition Width="Auto"/>
                </Grid.ColumnDefinitions>

                <StackPanel Grid.Column="0">
                    <TextBlock Text="Granular Conflict Resolution Diff Tool"
                               FontSize="11" FontWeight="Bold"
                               Foreground="{DynamicResource Synthetic.Brushes.AccentActive}"
                               Margin="0,0,0,2"/>
                    <TextBlock Text="{Binding Cluster.ClusterName}"
                               FontSize="16" FontWeight="SemiBold"
                               Foreground="{DynamicResource Synthetic.Brushes.TextPrimary}"/>
                </StackPanel>

                <StackPanel Grid.Column="1" VerticalAlignment="Center">
                    <TextBlock Text="Designated Primary Survivor Family:"
                               FontSize="11"
                               Foreground="{DynamicResource Synthetic.Brushes.TextSecondary}"
                               FontWeight="Bold" Margin="0,0,0,2"/>
                    <ComboBox ItemsSource="{Binding Cluster.Items}"
                              SelectedItem="{Binding Cluster.SelectedPrimary, Mode=TwoWay}"
                              DisplayMemberPath="ItemName"/>
                </StackPanel>
            </Grid>
        </Border>

        <!-- 2. TOP HALF: TYPE MAPPING MATRIX -->
        <Grid Grid.Row="1" Margin="0,0,0,5">
            <Grid.RowDefinitions>
                <RowDefinition Height="Auto"/> <!-- Block warning banner -->
                <RowDefinition Height="Auto"/> <!-- Title row -->
                <RowDefinition Height="*"/>    <!-- DataGrid -->
            </Grid.RowDefinitions>

            <!-- Block Warning Banner — uses Error semantic brush -->
            <Border Grid.Row="0"
                    Background="#1AD37B75"
                    BorderBrush="{DynamicResource Synthetic.Brushes.Error}"
                    BorderThickness="1" CornerRadius="4" Padding="12,10" Margin="0,0,0,10"
                    Visibility="{Binding IsBlockedMessageVisible, Converter={StaticResource BoolToVis}}">
                <Grid>
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="Auto"/>
                        <ColumnDefinition Width="*"/>
                    </Grid.ColumnDefinitions>
                    <Path Grid.Column="0"
                          Data="{StaticResource Synthetic.Geometries.Warning}"
                          Fill="{DynamicResource Synthetic.Brushes.Error}"
                          Width="20" Height="20" Stretch="Uniform"
                          VerticalAlignment="Center" Margin="0,0,12,0"/>
                    <TextBlock Grid.Column="1"
                               Text="{Binding BlockedReasonMessage}"
                               Foreground="{DynamicResource Synthetic.Brushes.Error}"
                               FontWeight="SemiBold" FontSize="12"
                               VerticalAlignment="Center" TextWrapping="Wrap"/>
                </Grid>
            </Border>

            <!-- Move Clusters Button & Title -->
            <StackPanel Grid.Row="1" Orientation="Horizontal" VerticalAlignment="Center" Margin="0,0,0,6">
                <!-- Icon-only move button — uses implicit Secondary button style -->
                <Button Command="{Binding CmdMoveItem}"
                        Width="24" Height="22" Margin="0,0,8,0"
                        Padding="0"
                        ToolTip="Move Clusters: Moves a selected duplicate element/type from this cluster to another cluster to manually adjust groupings.">
                    <Path Data="M3,7 H17 M17,7 L13,3 M17,7 L13,11 M21,15 H7 M7,15 L11,11 M7,15 L11,19"
                          Stroke="{DynamicResource Synthetic.Brushes.TextPrimary}"
                          StrokeThickness="1.5" Width="14" Height="14"
                          Stretch="Uniform" HorizontalAlignment="Center" VerticalAlignment="Center"/>
                </Button>
                <TextBlock Text="Type Mapping Matrix (Select a Type to compare parameters)"
                           FontSize="12" FontWeight="Bold"
                           Foreground="{DynamicResource Synthetic.Brushes.TextSecondary}"
                           VerticalAlignment="Center"/>
            </StackPanel>

            <!-- Type Mapping DataGrid — inherits implicit theme DataGrid style -->
            <DataGrid Grid.Row="2"
                      ItemsSource="{Binding Cluster.TypeMappings}"
                      SelectedItem="{Binding SelectedTypeMapping, Mode=TwoWay}">
                <DataGrid.GroupStyle>
                    <GroupStyle>
                        <GroupStyle.ContainerStyle>
                            <Style TargetType="{x:Type GroupItem}">
                                <Setter Property="Template">
                                    <Setter.Value>
                                        <ControlTemplate TargetType="{x:Type GroupItem}">
                                            <Expander IsExpanded="True"
                                                      Background="Transparent"
                                                      BorderBrush="{DynamicResource Synthetic.Brushes.BorderNormal}"
                                                      BorderThickness="0,0,0,1"
                                                      Margin="0,0,0,4">
                                                <Expander.Header>
                                                    <StackPanel Orientation="Horizontal" Margin="5,2">
                                                        <TextBlock Text="Family: "
                                                                   Foreground="{DynamicResource Synthetic.Brushes.TextSecondary}"
                                                                   FontWeight="Bold"/>
                                                        <TextBlock Text="{Binding Name}"
                                                                   Foreground="{DynamicResource Synthetic.Brushes.AccentActive}"
                                                                   FontWeight="Bold"/>
                                                    </StackPanel>
                                                </Expander.Header>
                                                <ItemsPresenter/>
                                            </Expander>
                                        </ControlTemplate>
                                    </Setter.Value>
                                </Setter>
                            </Style>
                        </GroupStyle.ContainerStyle>
                    </GroupStyle>
                </DataGrid.GroupStyle>

                <DataGrid.Columns>
                    <DataGridTextColumn Header="Duplicate Type Name"
                                        Binding="{Binding SourceType.Name}"
                                        Width="2*" IsReadOnly="True">
                        <DataGridTextColumn.ElementStyle>
                            <Style TargetType="TextBlock">
                                <Setter Property="VerticalAlignment" Value="Center"/>
                                <Setter Property="Margin" Value="10,0"/>
                            </Style>
                        </DataGridTextColumn.ElementStyle>
                    </DataGridTextColumn>

                    <DataGridTemplateColumn Header="Mapping Target" Width="2.5*">
                        <DataGridTemplateColumn.CellTemplate>
                            <DataTemplate DataType="{x:Type models:TypeMappingModel}">
                                <Grid Margin="5,2">
                                    <!-- ComboBox for Merge — inherits implicit ComboBox style -->
                                    <ComboBox ItemsSource="{Binding AvailablePrimaryTypes}"
                                              SelectedItem="{Binding TargetType, Mode=TwoWay}"
                                              DisplayMemberPath="Name">
                                        <ComboBox.Style>
                                            <Style TargetType="ComboBox" BasedOn="{StaticResource {x:Type ComboBox}}">
                                                <Setter Property="Visibility" Value="Collapsed"/>
                                                <Style.Triggers>
                                                    <DataTrigger Binding="{Binding RecommendedAction}"
                                                                 Value="{x:Static models:RecommendedAction.Merge}">
                                                        <Setter Property="Visibility" Value="Visible"/>
                                                    </DataTrigger>
                                                </Style.Triggers>
                                            </Style>
                                        </ComboBox.Style>
                                    </ComboBox>

                                    <!-- TextBox for Migrate — inherits implicit TextBox style -->
                                    <TextBox Text="{Binding MigrateRenameText, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}"
                                             PreviewTextInput="RenameTextBox_PreviewTextInput">
                                        <TextBox.Style>
                                            <Style TargetType="TextBox" BasedOn="{StaticResource {x:Type TextBox}}">
                                                <Setter Property="Visibility" Value="Collapsed"/>
                                                <Style.Triggers>
                                                    <DataTrigger Binding="{Binding RecommendedAction}"
                                                                 Value="{x:Static models:RecommendedAction.Migrate}">
                                                        <Setter Property="Visibility" Value="Visible"/>
                                                    </DataTrigger>
                                                </Style.Triggers>
                                            </Style>
                                        </TextBox.Style>
                                    </TextBox>

                                    <!-- TextBlock for Exclude -->
                                    <TextBlock Text="Excluded from Merge"
                                               Foreground="{DynamicResource Synthetic.Brushes.TextSecondary}"
                                               VerticalAlignment="Center" IsEnabled="False">
                                        <TextBlock.Style>
                                            <Style TargetType="TextBlock">
                                                <Setter Property="Visibility" Value="Collapsed"/>
                                                <Style.Triggers>
                                                    <DataTrigger Binding="{Binding RecommendedAction}"
                                                                 Value="{x:Static models:RecommendedAction.Exclude}">
                                                        <Setter Property="Visibility" Value="Visible"/>
                                                    </DataTrigger>
                                                </Style.Triggers>
                                            </Style>
                                        </TextBlock.Style>
                                    </TextBlock>
                                </Grid>
                            </DataTemplate>
                        </DataGridTemplateColumn.CellTemplate>
                    </DataGridTemplateColumn>

                    <DataGridTemplateColumn Header="Action Override" Width="2.2*">
                        <DataGridTemplateColumn.CellTemplate>
                            <DataTemplate DataType="{x:Type models:TypeMappingModel}">
                                <StackPanel Orientation="Horizontal" VerticalAlignment="Center" Margin="5,0">
                                    <RadioButton Content="Merge"
                                                 IsChecked="{Binding RecommendedAction, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged,
                                                             Converter={StaticResource EnumToBooleanConverter},
                                                             ConverterParameter={x:Static models:RecommendedAction.Merge}}"
                                                 Margin="0,0,10,0"
                                                 Foreground="{DynamicResource Synthetic.Brushes.TextPrimary}"
                                                 ToolTip="Merge: Consolidates duplicate types by swapping instances and copying parameters to the primary type."/>
                                    <RadioButton Content="Migrate"
                                                 IsChecked="{Binding RecommendedAction, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged,
                                                             Converter={StaticResource EnumToBooleanConverter},
                                                             ConverterParameter={x:Static models:RecommendedAction.Migrate}}"
                                                 Margin="0,0,10,0"
                                                 Foreground="{DynamicResource Synthetic.Brushes.TextPrimary}"
                                                 ToolTip="Migrate: Retains the duplicate type as a new independent symbol while swapping instances."/>
                                    <RadioButton Content="Exclude"
                                                 IsChecked="{Binding RecommendedAction, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged,
                                                             Converter={StaticResource EnumToBooleanConverter},
                                                             ConverterParameter={x:Static models:RecommendedAction.Exclude}}"
                                                 Foreground="{DynamicResource Synthetic.Brushes.TextPrimary}"
                                                 ToolTip="Exclude: Skips this type during the execution process."/>
                                </StackPanel>
                            </DataTemplate>
                        </DataGridTemplateColumn.CellTemplate>
                    </DataGridTemplateColumn>
                </DataGrid.Columns>
            </DataGrid>
        </Grid>

        <!-- Splitter -->
        <GridSplitter Grid.Row="2" Height="5" HorizontalAlignment="Stretch" VerticalAlignment="Center"
                      Background="{DynamicResource Synthetic.Brushes.BorderNormal}" Margin="0,5"/>

        <!-- 3. BOTTOM HALF: PARAMETER DIFF MATRIX -->
        <Grid Grid.Row="3" Margin="0,5,0,15">
            <Grid.RowDefinitions>
                <RowDefinition Height="Auto"/>
                <RowDefinition Height="Auto"/>
                <RowDefinition Height="*"/>
            </Grid.RowDefinitions>

            <TextBlock Grid.Row="0"
                       Text="Comparative Parameter Matrix (Red is Schema Mismatch, Yellow is Value Conflict)"
                       FontSize="12" FontWeight="Bold"
                       Foreground="{DynamicResource Synthetic.Brushes.TextSecondary}"
                       Margin="0,0,0,4"/>
            <TextBlock Grid.Row="1"
                       Text="{Binding DiffComparisonContext}"
                       FontSize="12" FontWeight="Bold"
                       Foreground="{DynamicResource Synthetic.Brushes.AccentActive}"
                       Margin="0,0,0,6"/>

            <!-- Parameter Diff DataGrid — inherits implicit style; row-level semantic overrides remain -->
            <DataGrid Grid.Row="2" ItemsSource="{Binding Rows}">
                <DataGrid.RowStyle>
                    <Style TargetType="DataGridRow" BasedOn="{StaticResource {x:Type DataGridRow}}">
                        <Style.Triggers>
                            <!-- Value conflict: Warning gold -->
                            <DataTrigger Binding="{Binding HasConflict}" Value="True">
                                <Setter Property="FontWeight" Value="Bold"/>
                                <Setter Property="Foreground"
                                        Value="{DynamicResource Synthetic.Brushes.Warning}"/>
                            </DataTrigger>
                            <!-- Schema mismatch: Error rust-red -->
                            <DataTrigger Binding="{Binding IsSchemaMismatch}" Value="True">
                                <Setter Property="FontWeight" Value="Bold"/>
                                <Setter Property="Foreground"
                                        Value="{DynamicResource Synthetic.Brushes.Error}"/>
                            </DataTrigger>
                        </Style.Triggers>
                    </Style>
                </DataGrid.RowStyle>

                <DataGrid.Columns>
                    <DataGridTextColumn Header="Parameter Name"
                                        Binding="{Binding ParameterName}"
                                        Width="1.8*" IsReadOnly="True">
                        <DataGridTextColumn.ElementStyle>
                            <Style TargetType="TextBlock">
                                <Setter Property="VerticalAlignment" Value="Center"/>
                                <Setter Property="Margin" Value="10,0"/>
                                <Setter Property="FontWeight" Value="SemiBold"/>
                            </Style>
                        </DataGridTextColumn.ElementStyle>
                    </DataGridTextColumn>

                    <DataGridTextColumn Header="Value (Secondary)"
                                        Binding="{Binding ValueList[0]}"
                                        Width="1.5*" IsReadOnly="True">
                        <DataGridTextColumn.ElementStyle>
                            <Style TargetType="TextBlock">
                                <Setter Property="VerticalAlignment" Value="Center"/>
                                <Setter Property="Margin" Value="10,0"/>
                            </Style>
                        </DataGridTextColumn.ElementStyle>
                    </DataGridTextColumn>

                    <DataGridTextColumn Header="Value (Primary)"
                                        Binding="{Binding ValueList[1]}"
                                        Width="1.5*" IsReadOnly="True">
                        <DataGridTextColumn.ElementStyle>
                            <Style TargetType="TextBlock">
                                <Setter Property="VerticalAlignment" Value="Center"/>
                                <Setter Property="Margin" Value="10,0"/>
                            </Style>
                        </DataGridTextColumn.ElementStyle>
                    </DataGridTextColumn>

                    <DataGridTemplateColumn Header="Winning Value Selection" Width="2.2*">
                        <DataGridTemplateColumn.CellTemplate>
                            <DataTemplate DataType="{x:Type models:ParameterDiffRowModel}">
                                <StackPanel Orientation="Horizontal" VerticalAlignment="Center">
                                    <RadioButton Content="Secondary"
                                                 GroupName="{Binding ParameterName}"
                                                 IsChecked="{Binding IsSourceWinning, Mode=TwoWay}"
                                                 Margin="0,0,15,0"
                                                 Foreground="{DynamicResource Synthetic.Brushes.TextPrimary}"/>
                                    <RadioButton Content="Primary"
                                                 GroupName="{Binding ParameterName}"
                                                 IsChecked="{Binding IsTargetWinning, Mode=TwoWay}"
                                                 Foreground="{DynamicResource Synthetic.Brushes.TextPrimary}"/>
                                </StackPanel>
                            </DataTemplate>
                        </DataGridTemplateColumn.CellTemplate>
                    </DataGridTemplateColumn>

                    <DataGridTemplateColumn Header="Inject Parameter" Width="Auto">
                        <DataGridTemplateColumn.CellTemplate>
                            <DataTemplate DataType="{x:Type models:ParameterDiffRowModel}">
                                <!-- CheckBox inherits implicit theme style -->
                                <CheckBox IsChecked="{Binding InjectParameter, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}"
                                          IsEnabled="{Binding IsInjectEnabled}"
                                          HorizontalAlignment="Center" VerticalAlignment="Center"/>
                            </DataTemplate>
                        </DataGridTemplateColumn.CellTemplate>
                    </DataGridTemplateColumn>
                </DataGrid.Columns>
            </DataGrid>
        </Grid>

        <!-- 4. FOOTER SECTION -->
        <Grid Grid.Row="4" VerticalAlignment="Bottom">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="*"/>
                <ColumnDefinition Width="Auto"/>
            </Grid.ColumnDefinitions>

            <StackPanel Grid.Column="1" Orientation="Horizontal" HorizontalAlignment="Right">
                <!-- Cancel: Secondary implicit style -->
                <Button x:Name="BtnCancel" Content="Cancel" Click="BtnCancel_Click"
                        Margin="0,0,10,0"/>
                <!-- Confirm: Primary accent style -->
                <Button Content="Confirm &amp; Add to Queue"
                        Command="{Binding CmdCommitToQueue}"
                        Style="{DynamicResource Synthetic.Styles.PrimaryButton.Right}"/>
            </StackPanel>
        </Grid>
    </Grid>
</Window>
```

### File: MergeDuplicates/Views/MergeDetailedReviewWindow.xaml.cs
```csharp
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

using Synthetic.Shared.UI;
using Synthetic.Core;

using Synthetic.Modules.MergeDuplicates.ViewModels;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;

namespace Synthetic.Modules.MergeDuplicates.Views
{
    /// <summary>
    /// Modeless window that displays side-by-side parameter differences for a selected cluster.
    /// </summary>
    public partial class MergeDetailedReviewWindow : Window
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="MergeDetailedReviewWindow"/> class.
        /// </summary>
        /// <param name="mainWindowHandle">The parent window handle.</param>
        public MergeDetailedReviewWindow(IntPtr mainWindowHandle)
        {
            InitializeComponent();
            RevitWindowHelper.SetOwner(this, mainWindowHandle);

            DataContextChanged += (sender, args) =>
            {
                if (DataContext is MergeDetailedReviewViewModel vm)
                {
                    vm.CloseAction = () =>
                    {
                        try
                        {
                            this.DialogResult = true;
                        }
                        catch (InvalidOperationException)
                        {
                            // Window was not shown as a dialog (modeless)
                        }
                        this.Close();
                    };
                }
            };
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                this.DialogResult = false;
            }
            catch (InvalidOperationException)
            {
                // Window was not shown as a dialog (modeless)
            }
            this.Close();
        }

        private void RenameTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            if (string.IsNullOrEmpty(e.Text)) return;

            char[] illegalChars = new char[] { '\\', ':', '{', '}', '[', ']', '|', ';', '<', '>', '?', '\'', '~' };
            if (e.Text.IndexOfAny(illegalChars) >= 0)
            {
                e.Handled = true;

                if (sender is System.Windows.Controls.TextBox textBox)
                {
                    var originalToolTip = textBox.ToolTip;
                    var toolTip = new System.Windows.Controls.ToolTip
                    {
                        Content = "Family names cannot contain any of the following characters:\n\\ : { } [ ] | ; < > ? ' ~",
                        IsOpen = true,
                        PlacementTarget = textBox,
                        Placement = System.Windows.Controls.Primitives.PlacementMode.Top,
                        StaysOpen = false
                    };

                    textBox.ToolTip = toolTip;

                    var timer = new DispatcherTimer
                    {
                        Interval = TimeSpan.FromSeconds(3)
                    };
                    timer.Tick += (s, args) =>
                    {
                        toolTip.IsOpen = false;
                        textBox.ToolTip = originalToolTip;
                        timer.Stop();
                    };
                    timer.Start();
                }
            }
        }
    }
}
```

### File: MergeDuplicates/Views/MergeDuplicatesWindow.xaml
```xml
<Window x:Class="Synthetic.Modules.MergeDuplicates.Views.MergeDuplicatesWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:vm="clr-namespace:Synthetic.Modules.MergeDuplicates.ViewModels"
        xmlns:models="clr-namespace:Synthetic.RevitDOM.Operations.Merge"
        xmlns:merge="clr-namespace:Synthetic.RevitDOM.Operations.Merge"
        xmlns:local="clr-namespace:Synthetic.Shared.UI"
        Title="Merge Duplicates - Triage &amp; Queue"
        Height="600" Width="850" MinHeight="500" MinWidth="700"
        WindowStartupLocation="CenterOwner" ResizeMode="CanResizeWithGrip" ShowInTaskbar="False"
        Style="{DynamicResource SyntheticWindowStyle}"
        local:WindowChromeBehavior.EnableWindowCommands="True">

    <Window.Resources>
        <ResourceDictionary>
            <ResourceDictionary.MergedDictionaries>
                <ResourceDictionary Source="../../../Shared/UI/SyntheticTheme.xaml"/>
            </ResourceDictionary.MergedDictionaries>

            <BooleanToVisibilityConverter x:Key="BooleanToVisibilityConverter"/>
        </ResourceDictionary>
    </Window.Resources>

    <Grid Margin="15">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/> <!-- Top Bar / Initiation Area -->
            <RowDefinition Height="*"/>    <!-- Triage (Top) -->
            <RowDefinition Height="Auto"/> <!-- Splitter / Spacer -->
            <RowDefinition Height="*"/>    <!-- Queue (Bottom) -->
            <RowDefinition Height="Auto"/> <!-- Footer -->
        </Grid.RowDefinitions>

        <!-- 0. TOP BAR / INITIATION AREA -->
        <StackPanel Grid.Row="0" Orientation="Horizontal" Margin="0,0,0,15">
            <Button Content="Scan Entire Model"
                    Command="{Binding CmdScanModel}"
                    Style="{DynamicResource Synthetic.Styles.PrimaryButton}"
                    Margin="0,0,10,0"/>
            <Button Content="Load Current Selection"
                    Command="{Binding CmdLoadSelection}"
                    Style="{DynamicResource Synthetic.Styles.PrimaryButton}"/>
        </StackPanel>

        <!-- 1. TRIAGE GRID AREA -->
        <Grid Grid.Row="1" Margin="0,0,0,10">
            <Grid.RowDefinitions>
                <RowDefinition Height="Auto"/>
                <RowDefinition Height="*"/>
            </Grid.RowDefinitions>

            <TextBlock Grid.Row="0" Text="Duplicate Clusters (Triage Grid)"
                       FontSize="14" FontWeight="Bold"
                       Foreground="{DynamicResource Synthetic.Brushes.TextPrimary}"
                       Margin="0,0,0,8"/>

            <!-- Implicit styling applies to DataGrid -->
            <DataGrid Grid.Row="1" ItemsSource="{Binding ScannedClusters}" RowDetailsVisibilityMode="VisibleWhenSelected">
                <DataGrid.RowDetailsTemplate>
                    <DataTemplate DataType="{x:Type models:DuplicateClusterModel}">
                        <Border Background="{DynamicResource Synthetic.Brushes.BackgroundBase}"
                                BorderBrush="{DynamicResource Synthetic.Brushes.BorderNormal}"
                                BorderThickness="1,0,1,1" Padding="15,10">
                            <StackPanel>
                                <TextBlock Text="Include Items for Merge:" FontSize="11"
                                           Foreground="{DynamicResource Synthetic.Brushes.AccentActive}"
                                           FontWeight="Bold" Margin="0,0,0,6"/>
                                <ItemsControl ItemsSource="{Binding Items}">
                                    <ItemsControl.ItemsPanel>
                                        <ItemsPanelTemplate>
                                            <WrapPanel Orientation="Horizontal"/>
                                        </ItemsPanelTemplate>
                                    </ItemsControl.ItemsPanel>
                                    <ItemsControl.ItemTemplate>
                                        <DataTemplate DataType="{x:Type merge:DuplicateItemModel}">
                                            <!-- CheckBox inherits implicit style -->
                                            <CheckBox Content="{Binding ItemName}"
                                                      IsChecked="{Binding IsIncludedForMerge, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}"
                                                      Margin="0,0,20,5" VerticalAlignment="Center" FontWeight="SemiBold"/>
                                        </DataTemplate>
                                    </ItemsControl.ItemTemplate>
                                </ItemsControl>
                            </StackPanel>
                        </Border>
                    </DataTemplate>
                </DataGrid.RowDetailsTemplate>

                <DataGrid.Columns>
                    <DataGridTextColumn Header="Duplicate Group Name" Binding="{Binding ClusterName}" Width="2*" IsReadOnly="True">
                        <DataGridTextColumn.ElementStyle>
                            <Style TargetType="TextBlock">
                                <Setter Property="VerticalAlignment" Value="Center"/>
                                <Setter Property="Margin" Value="5,0"/>
                            </Style>
                        </DataGridTextColumn.ElementStyle>
                    </DataGridTextColumn>

                    <!-- Mismatches / Status: migrated emojis to XAML vector paths -->
                    <DataGridTemplateColumn Header="Status Warnings" Width="1.5*">
                        <DataGridTemplateColumn.CellTemplate>
                            <DataTemplate DataType="{x:Type models:DuplicateClusterModel}">
                                <StackPanel Orientation="Horizontal" VerticalAlignment="Center" Margin="5,0">
                                    <!-- Blocked icon & label -->
                                    <StackPanel Orientation="Horizontal" Margin="0,0,8,0"
                                                Visibility="{Binding IsBlocked, Converter={StaticResource BooleanToVisibilityConverter}}">
                                        <Path Data="{StaticResource Synthetic.Geometries.Blocked}"
                                              Fill="{DynamicResource Synthetic.Brushes.Error}"
                                              Width="14" Height="14" Stretch="Uniform" VerticalAlignment="Center" Margin="0,0,4,0"/>
                                        <TextBlock Text="Blocked: Conflicts" Foreground="{DynamicResource Synthetic.Brushes.Error}" FontWeight="Bold" VerticalAlignment="Center"/>
                                    </StackPanel>

                                    <!-- Parameter Mismatch icon & label -->
                                    <StackPanel Orientation="Horizontal" Margin="0,0,8,0"
                                                Visibility="{Binding HasSchemaMismatch, Converter={StaticResource BooleanToVisibilityConverter}}">
                                        <Path Data="{StaticResource Synthetic.Geometries.Warning}"
                                              Fill="{DynamicResource Synthetic.Brushes.Warning}"
                                              Width="14" Height="14" Stretch="Uniform" VerticalAlignment="Center" Margin="0,0,4,0"/>
                                        <TextBlock Text="Parameter Mismatch" Foreground="{DynamicResource Synthetic.Brushes.Warning}" FontWeight="SemiBold" VerticalAlignment="Center"/>
                                    </StackPanel>

                                    <!-- Origin Mismatch icon & label -->
                                    <StackPanel Orientation="Horizontal" Margin="0,0,8,0"
                                                Visibility="{Binding HasOriginMismatch, Converter={StaticResource BooleanToVisibilityConverter}}">
                                        <Path Data="{StaticResource Synthetic.Geometries.Warning}"
                                              Fill="{DynamicResource Synthetic.Brushes.Warning}"
                                              Width="14" Height="14" Stretch="Uniform" VerticalAlignment="Center" Margin="0,0,4,0"/>
                                        <TextBlock Text="Origin Mismatch" Foreground="{DynamicResource Synthetic.Brushes.Warning}" FontWeight="SemiBold" VerticalAlignment="Center"/>
                                    </StackPanel>

                                    <!-- Success/Harmonious icon & label -->
                                    <StackPanel Orientation="Horizontal">
                                        <StackPanel.Style>
                                            <Style TargetType="StackPanel">
                                                <Setter Property="Visibility" Value="Collapsed"/>
                                                <Style.Triggers>
                                                    <MultiDataTrigger>
                                                        <MultiDataTrigger.Conditions>
                                                            <Condition Binding="{Binding HasSchemaMismatch}" Value="False"/>
                                                            <Condition Binding="{Binding HasOriginMismatch}" Value="False"/>
                                                            <Condition Binding="{Binding IsBlocked}" Value="False"/>
                                                        </MultiDataTrigger.Conditions>
                                                        <Setter Property="Visibility" Value="Visible"/>
                                                    </MultiDataTrigger>
                                                </Style.Triggers>
                                            </Style>
                                        </StackPanel.Style>
                                        <Path Data="{StaticResource Synthetic.Geometries.Success}"
                                              Fill="{DynamicResource Synthetic.Brushes.Success}"
                                              Width="14" Height="14" Stretch="Uniform" VerticalAlignment="Center" Margin="0,0,4,0"/>
                                        <TextBlock Text="Harmonious" Foreground="{DynamicResource Synthetic.Brushes.Success}" FontWeight="SemiBold" VerticalAlignment="Center"/>
                                    </StackPanel>
                                </StackPanel>
                            </DataTemplate>
                        </DataGridTemplateColumn.CellTemplate>
                    </DataGridTemplateColumn>

                    <!-- ComboBox to choose Primary Survivor -->
                    <DataGridTemplateColumn Header="Primary Survivor (Bold is Default)" Width="2.5*">
                        <DataGridTemplateColumn.CellTemplate>
                            <DataTemplate DataType="{x:Type models:DuplicateClusterModel}">
                                <!-- ComboBox inherits implicit style -->
                                <ComboBox ItemsSource="{Binding Items}"
                                          SelectedItem="{Binding SelectedPrimary, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}"
                                          Margin="5,0">
                                    <ComboBox.ItemTemplate>
                                        <DataTemplate DataType="{x:Type merge:DuplicateItemModel}">
                                            <TextBlock Text="{Binding ItemName}">
                                                <TextBlock.Style>
                                                    <Style TargetType="TextBlock">
                                                        <Style.Triggers>
                                                            <DataTrigger Binding="{Binding IsPrimary}" Value="True">
                                                                <Setter Property="FontWeight" Value="Bold"/>
                                                                <Setter Property="Foreground" Value="{DynamicResource Synthetic.Brushes.AccentActive}"/>
                                                            </DataTrigger>
                                                        </Style.Triggers>
                                                    </Style>
                                                </TextBlock.Style>
                                            </TextBlock>
                                        </DataTemplate>
                                    </ComboBox.ItemTemplate>
                                </ComboBox>
                            </DataTemplate>
                        </DataGridTemplateColumn.CellTemplate>
                    </DataGridTemplateColumn>

                    <!-- Add Straggler Button (+) -->
                    <DataGridTemplateColumn Header="Stragglers" Width="Auto">
                        <DataGridTemplateColumn.CellTemplate>
                            <DataTemplate DataType="{x:Type models:DuplicateClusterModel}">
                                <Button Content="+"
                                        Command="{Binding DataContext.CmdAddStragglers, RelativeSource={RelativeSource AncestorType=DataGrid}}"
                                        CommandParameter="{Binding}"
                                        Width="28" Margin="5,0"
                                        FontWeight="Bold" FontSize="14"
                                        ToolTip="Force missing straggler elements of the same category into this cluster."/>
                            </DataTemplate>
                        </DataGridTemplateColumn.CellTemplate>
                    </DataGridTemplateColumn>

                    <!-- Actions (Queue, Detailed Analysis, Move) -->
                    <DataGridTemplateColumn Header="Actions" Width="Auto">
                        <DataGridTemplateColumn.CellTemplate>
                            <DataTemplate DataType="{x:Type models:DuplicateClusterModel}">
                                <StackPanel Orientation="Horizontal">
                                    <Button Content="Queue"
                                            Command="{Binding DataContext.CmdAddToQueue, RelativeSource={RelativeSource AncestorType=DataGrid}}"
                                            CommandParameter="{Binding}"
                                            Style="{DynamicResource Synthetic.Styles.PrimaryButton}"
                                            Margin="3,0"
                                            ToolTip="Add this cluster to the execution merge queue."/>

                                    <Button Content="Detailed Analysis"
                                            Command="{Binding DataContext.CmdDetailedReview, RelativeSource={RelativeSource AncestorType=DataGrid}}"
                                            CommandParameter="{Binding}"
                                            Margin="3,0"
                                            ToolTip="Launch granular side-by-side comparison and override individual parameters."/>

                                    <Button Command="{Binding DataContext.CmdMoveItem, RelativeSource={RelativeSource AncestorType=DataGrid}}"
                                            CommandParameter="{Binding}"
                                            Width="24" Margin="3,0"
                                            ToolTip="Move Clusters: Moves a selected duplicate element/type from this cluster to another cluster to manually adjust groupings.">
                                        <Path Data="M3,7 H17 M17,7 L13,3 M17,7 L13,11 M21,15 H7 M7,15 L11,11 M7,15 L11,19"
                                              Stroke="{DynamicResource Synthetic.Brushes.TextPrimary}"
                                              StrokeThickness="1.5" Width="14" Height="14" Stretch="Uniform" HorizontalAlignment="Center" VerticalAlignment="Center"/>
                                    </Button>
                                </StackPanel>
                            </DataTemplate>
                        </DataGridTemplateColumn.CellTemplate>
                    </DataGridTemplateColumn>
                </DataGrid.Columns>
            </DataGrid>
        </Grid>

        <!-- Grid Splitter -->
        <GridSplitter Grid.Row="2" Height="4" HorizontalAlignment="Stretch" VerticalAlignment="Center"
                      Background="{DynamicResource Synthetic.Brushes.BorderNormal}" Margin="0,5"/>

        <!-- 2. QUEUE GRID AREA -->
        <Grid Grid.Row="3" Margin="0,10,0,15">
            <Grid.RowDefinitions>
                <RowDefinition Height="Auto"/>
                <RowDefinition Height="*"/>
            </Grid.RowDefinitions>

            <TextBlock Grid.Row="0" Text="Queued Merges (Active Queue)"
                       FontSize="14" FontWeight="Bold"
                       Foreground="{DynamicResource Synthetic.Brushes.TextPrimary}"
                       Margin="0,0,0,8"/>

            <!-- Implicit style applies to Queue DataGrid -->
            <DataGrid Grid.Row="1" ItemsSource="{Binding QueuedClusters}">
                <DataGrid.Columns>
                    <DataGridTextColumn Header="Duplicate Group Name" Binding="{Binding ClusterName}" Width="2*" IsReadOnly="True">
                        <DataGridTextColumn.ElementStyle>
                            <Style TargetType="TextBlock">
                                <Setter Property="VerticalAlignment" Value="Center"/>
                                <Setter Property="Margin" Value="5,0"/>
                            </Style>
                        </DataGridTextColumn.ElementStyle>
                    </DataGridTextColumn>

                    <DataGridTextColumn Header="Survivor Name" Binding="{Binding SelectedPrimary.ItemName}" Width="2.5*" IsReadOnly="True">
                        <DataGridTextColumn.ElementStyle>
                            <Style TargetType="TextBlock">
                                <Setter Property="VerticalAlignment" Value="Center"/>
                                <Setter Property="Margin" Value="5,0"/>
                            </Style>
                        </DataGridTextColumn.ElementStyle>
                    </DataGridTextColumn>

                    <DataGridTextColumn Header="Items Count" Binding="{Binding IncludedItemsCount}" Width="1*" IsReadOnly="True">
                        <DataGridTextColumn.ElementStyle>
                            <Style TargetType="TextBlock">
                                <Setter Property="VerticalAlignment" Value="Center"/>
                                <Setter Property="Margin" Value="5,0"/>
                            </Style>
                        </DataGridTextColumn.ElementStyle>
                    </DataGridTextColumn>

                    <!-- Remove Action -->
                    <DataGridTemplateColumn Header="Action" Width="Auto">
                        <DataGridTemplateColumn.CellTemplate>
                            <DataTemplate DataType="{x:Type models:DuplicateClusterModel}">
                                <Button Content="Remove"
                                        Command="{Binding DataContext.CmdRemoveFromQueue, RelativeSource={RelativeSource AncestorType=DataGrid}}"
                                        CommandParameter="{Binding}"
                                        Margin="5,0"
                                        ToolTip="Remove this cluster from the execution queue."/>
                            </DataTemplate>
                        </DataGridTemplateColumn.CellTemplate>
                    </DataGridTemplateColumn>
                </DataGrid.Columns>
            </DataGrid>
        </Grid>

        <!-- 3. FOOTER AREA -->
        <Grid Grid.Row="4" VerticalAlignment="Bottom">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="*"/>    <!-- Progress Bar -->
                <ColumnDefinition Width="Auto"/> <!-- Buttons -->
            </Grid.ColumnDefinitions>

            <!-- Progress Indicator -->
            <StackPanel Grid.Column="0" Orientation="Horizontal" VerticalAlignment="Center" Margin="0,0,20,0">
                <TextBlock Text="Progress: " VerticalAlignment="Center"
                           Foreground="{DynamicResource Synthetic.Brushes.TextSecondary}" Margin="0,0,8,0"/>
                <ProgressBar Value="{Binding ProgressValue}" Maximum="{Binding ProgressMax}" Width="200" Height="14"
                             Foreground="{DynamicResource Synthetic.Brushes.AccentActive}"
                             Background="{DynamicResource Synthetic.Brushes.BackgroundBase}"
                             BorderBrush="{DynamicResource Synthetic.Brushes.BorderNormal}"/>
            </StackPanel>

            <!-- Operations / Actions -->
            <StackPanel Grid.Column="1" Orientation="Horizontal" HorizontalAlignment="Right">
                <Button Content="Cancel Scan" Command="{Binding CmdCancelScan}" Margin="0,0,10,0"/>

                <Button Content="Process Merge"
                        Command="{Binding CmdProcessMerge}"
                        CommandParameter="{Binding RelativeSource={RelativeSource AncestorType=Window}}"
                        Style="{DynamicResource Synthetic.Styles.PrimaryButton.Right}"/>
            </StackPanel>
        </Grid>
    </Grid>
</Window>
```

### File: MergeDuplicates/Views/MergeDuplicatesWindow.xaml.cs
```csharp
using System;
using System.Windows;

using Synthetic.Shared.UI;
using Synthetic.Core;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;

namespace Synthetic.Modules.MergeDuplicates.Views
{
    /// <summary>
    /// Interaction logic for MergeDuplicatesWindow.xaml.
    /// </summary>
    public partial class MergeDuplicatesWindow : Window
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="MergeDuplicatesWindow"/> class.
        /// </summary>
        /// <param name="mainWindowHandle">The handle to the main Revit application window, used to set the owner of this window.</param>
        public MergeDuplicatesWindow(IntPtr mainWindowHandle)
        {
            InitializeComponent();
            RevitWindowHelper.SetOwner(this, mainWindowHandle);
        }
    }
}
```

### File: MaterialManagement/Commands/MaterialImagesPackage.cs
```csharp
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

using Synthetic.Shared.RevitAPI;
using Synthetic.Infrastructure.IO;
using SFileUtil = Synthetic.Infrastructure.IO.FileUtil;

using Synthetic.Shared.UI;
using Synthetic.Core;
using Synthetic.Modules.MaterialManagement.Commands;
using Synthetic.Modules.MaterialManagement.Utilities;

using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using System.IO;
using Application = Autodesk.Revit.ApplicationServices.Application;
using Synthetic.Settings;
using Synthetic.Infrastructure.Persistence;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;

namespace Synthetic.Modules.MaterialManagement.Commands
{
    /// <summary>
    /// Revit external command to package and transmit all material bitmap images to a new directory.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class MaterialImagesPackage : IExternalCommand
    {
        /// <summary>
        /// The button label text for this command in the Revit UI.
        /// </summary>
        public const string CommandButton = " Transmit \nMaterial \nImages ";

        /// <summary>
        /// The tooltip message for this command in the Revit UI.
        /// </summary>
        public const string CommandTooltip = "Copy all material bitmap images to a new folder location.";

        /// <summary>
        /// The full class path/name of this command.
        /// </summary>
        public static string? CommandPath = typeof(MaterialImagesPackage).FullName;

        /// <summary>
        /// Execute a Revit Command
        /// </summary>
        /// <param name="commandData">commandData</param>
        /// <param name="message">message</param>
        /// <param name="elements">Currently selected elements</param>
        /// <returns>A Autodesk.Revit.UI.Result</returns>
        public Result Execute(
          ExternalCommandData commandData,
          ref string message,
          ElementSet elements)
        {
            UIApplication uiapp = commandData.Application;
            UIDocument uidoc = uiapp.ActiveUIDocument;
            Application app = uiapp.Application;
            Document doc = uidoc.Document;

            Result commandResult = Result.Succeeded;

            MaterialLibrarySettings libSettings = SettingsManager.Get<MaterialLibrarySettings>(doc);
            ProjectMaterialSettings projSettings = SettingsManager.Get<ProjectMaterialSettings>(doc);

            string? LibraryPath = libSettings.LibraryFolderPath;
            string? ProjectPath = projSettings.GetResolvedPath(doc);
            List<string> defaultPaths = new List<string>();
            if (ProjectPath != null) defaultPaths.Add(ProjectPath);
            if (LibraryPath != null) defaultPaths.Add(LibraryPath);
            string? path = SelectPath(uiapp.MainWindowHandle);

            Dictionary<string, Dictionary<string, object>>? results = null;

            if (path != null && path != string.Empty)
            {
                List<Material> allMaterials = MaterialUtil.GetAllMaterials(doc).Cast<Material>().ToList();
                List<Material> selectedMaterials = SelectMaterials(allMaterials, uiapp.MainWindowHandle);

                List<string> pathList = new List<string>();
                pathList.Add(path);

                if (selectedMaterials != null && selectedMaterials.Count > 0)
                {
                    List<string> filePaths = new List<string>();
                    foreach (Material material in selectedMaterials)
                    {
                        List<string>? tempPaths = MaterialUtil.GetMaterialBitmapPaths(material);
                        if (tempPaths != null && tempPaths.Count > 0)
                        {
                            filePaths.AddRange(tempPaths);
                        }
                    }
                    List<string> rootNames = new List<string>() { "Materials","INC Material Maps", "Maps", "_Maps", "Substance" };
                    results = SFileUtil.CopyFiles(filePaths, path, rootNames);
                }
                else { commandResult = Result.Failed; }
            }
            else { commandResult = Result.Failed; }
            
            if (results != null)
            {
                CommandUtil.SaveResults(results, doc, "Transmit Material Images", path);
            }

            return commandResult;
        }

        /// <summary>
        /// Allows the user to browse for a folder location
        /// </summary>
        internal string? SelectPath(IntPtr ownerHandle)
        {
            return FileDialogHelper.SelectFolder(ownerHandle, "Select Folder to Transmit Images To");
        }

        /// <summary>
        /// Allows the user to add paths and select from a list of default paths for constructing a SearchPath
        /// </summary>
        internal List<string> SelectSearchPaths(List<string> defaultPaths, IntPtr mainWindowHandle)
        {
            SelectSearchPathsViewModel viewModel = new SelectSearchPathsViewModel();
            viewModel.Title = "List of Paths to Exclude";
            viewModel.Instruction = "Image files located in the checked User Paths and Default Paths will be excluded from being copied.  This is to allow";
            
            foreach (var p in defaultPaths)
            {
                viewModel.DefaultPaths.Add(new CheckableItem(p, false));
            }

            SelectSearchPathsView dialog = new SelectSearchPathsView(mainWindowHandle) { DataContext = viewModel };

            bool? dResult = dialog.ShowDialog();

            List<string> pathList = new List<string>();
            if (dResult == true)
            {
                if (viewModel.CheckedItems != null)
                {
                    pathList = viewModel.CheckedItems;
                }
            }
            return pathList;
        }

        /// <summary>
        /// Selects materials via checklist dialog
        /// </summary>
        internal List<Material> SelectMaterials(List<Material> materials, IntPtr mainWindowHandle)
        {
            List<string> itemList = new List<string>();
            List<Material> selectedMaterials = new List<Material>();

            ListByCheckboxViewModel viewModel = new ListByCheckboxViewModel();
            viewModel.Title = "Select Materials";
            viewModel.Instruction = "The selected Materials will have their images repathed based on the Search Paths previously selected.";
            viewModel.IsSingleSelection = false;

            foreach (Material material in materials)
            {
                itemList.Add(material.Name);
            }
            viewModel.SetItems(itemList, true);

            ListByCheckboxView dialog = new ListByCheckboxView(mainWindowHandle) { DataContext = viewModel };

            bool? dResult = dialog.ShowDialog();

            if (dResult == true)
            {
                List<string> selectedList = viewModel.CheckedItems;
                foreach (string selectedItem in selectedList)
                {
                    Material m = materials.First(s => s.Name == selectedItem);
                    selectedMaterials.Add(m);
                }
            }
            return selectedMaterials;
        }
    }
}
```

### File: MaterialManagement/Commands/MaterialsRepathAll.cs
```csharp
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

using Synthetic.Shared.RevitAPI;
using Synthetic.Infrastructure.IO;

using Synthetic.Shared.UI;
using Synthetic.Modules.MaterialManagement.Commands;
using Synthetic.Modules.MaterialManagement.Utilities;

using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using Application = Autodesk.Revit.ApplicationServices.Application;
using Synthetic.Settings;
using Synthetic.Infrastructure.Persistence;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;

namespace Synthetic.Modules.MaterialManagement.Commands
{
    /// <summary>
    /// Revit external command to repath material bitmap images based on search paths.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class MaterialsRepathAll : IExternalCommand
    {
        /// <summary>
        /// Execute a Revit Command
        /// </summary>
        /// <param name="commandData">commandData</param>
        /// <param name="message">message</param>
        /// <param name="elements">Currently selected elements</param>
        /// <returns>A Autodesk.Revit.UI.Result</returns>
        public Result Execute(
          ExternalCommandData commandData,
          ref string message,
          ElementSet elements)
        {
            UIApplication uiapp = commandData.Application;
            UIDocument uidoc = uiapp.ActiveUIDocument;
            Application app = uiapp.Application;
            Document doc = uidoc.Document;

            Result commandResult = Result.Succeeded;

            MaterialLibrarySettings libSettings = SettingsManager.Get<MaterialLibrarySettings>(doc);
            ProjectMaterialSettings projSettings = SettingsManager.Get<ProjectMaterialSettings>(doc);

            string? LibraryPath = libSettings.LibraryFolderPath;
            string? ProjectPath = projSettings.GetResolvedPath(doc);
            List<string> defaultPaths = new List<string>();
            if (ProjectPath != null) defaultPaths.Add(ProjectPath);
            if (LibraryPath != null) defaultPaths.Add(LibraryPath);
            List<string>? pathList = SelectSearchPaths(defaultPaths, uiapp.MainWindowHandle);

            Dictionary<string, object>? resutls = null;

            if (pathList != null && pathList.Count > 0)
            {
                List<Material> allMaterials = MaterialUtil.GetAllMaterials(doc).Cast<Material>().ToList();
                List<Material> selectedMaterials = SelectMaterials(allMaterials, uiapp.MainWindowHandle);

                SearchPaths searchPaths = new SearchPaths(pathList);

                if (selectedMaterials != null && selectedMaterials.Count > 0)
                {
                    string transactionName = "Replace Bitmap Paths on Materials";
                    using (Autodesk.Revit.DB.Transaction trans = new Autodesk.Revit.DB.Transaction(doc))
                    {
                        trans.Start(transactionName);
                        resutls = MaterialUtil.ReplaceBitmapPaths(selectedMaterials, searchPaths, true, false);
                        trans.Commit();
                    }
                }
                else { commandResult = Result.Failed; }
            }
            else { commandResult = Result.Failed; }
            
            if (resutls != null)
            {
                CommandUtil.SaveResults(resutls, doc, "Material Repath Results");
            }
            
            return commandResult;
        }

        /// <summary>
        /// Allows the user to add paths and select from a list of default paths for constructing a SearchPath
        /// </summary>
        internal List<string> SelectSearchPaths(List<string> defaultPaths, IntPtr mainWindowHandle)
        {
            SelectSearchPathsViewModel viewModel = new SelectSearchPathsViewModel();
            viewModel.Title = "List of Paths to Search";
            viewModel.Instruction = "Add and select the folder paths that you want to search for files.  If there are duplicate file names, only the first file will be used.  This means the order of the paths is important.  User Selected paths will be searched in order before Default Paths.";
            
            foreach (var p in defaultPaths)
            {
                viewModel.DefaultPaths.Add(new CheckableItem(p, false));
            }
            viewModel.CheckAllDefaults();

            SelectSearchPathsView dialog = new SelectSearchPathsView(mainWindowHandle) { DataContext = viewModel };

            bool? dResult = dialog.ShowDialog();

            List<string> pathList = new List<string>();
            if (dResult == true)
            {
                if (viewModel.CheckedItems != null)
                {
                    pathList = viewModel.CheckedItems;
                }
            }
            return pathList;
        }

        /// <summary>
        /// Selects materials via checklist dialog
        /// </summary>
        internal List<Material> SelectMaterials(List<Material> materials, IntPtr mainWindowHandle)
        {
            List<string> itemList = new List<string>();
            List<Material> selectedMaterials = new List<Material>();

            ListByCheckboxViewModel viewModel = new ListByCheckboxViewModel();
            viewModel.Title = "Select Materials";
            viewModel.Instruction = "The selected Materials will have their images repathed based on the Search Paths previously selected.";
            viewModel.IsSingleSelection = false;

            foreach (Material material in materials)
            {
                itemList.Add(material.Name);
            }
            viewModel.SetItems(itemList, true);

            ListByCheckboxView dialog = new ListByCheckboxView(mainWindowHandle) { DataContext = viewModel };

            bool? dResult = dialog.ShowDialog();

            if (dResult == true)
            {
                List<string> selectedList = viewModel.CheckedItems;
                foreach (string selectedItem in selectedList)
                {
                    Material m = materials.First(s => s.Name == selectedItem);
                    selectedMaterials.Add(m);
                }
            }
            return selectedMaterials;
        }
    }
}
```

### File: MaterialManagement/Commands/PaintElements.cs
```csharp
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using View = Autodesk.Revit.DB.View;
using Autodesk.Revit.UI;
using Synthetic.Modules.MaterialManagement.Commands;
using Synthetic.Modules.MaterialManagement.Utilities;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Synthetic.Shared.UI;

using Synthetic.Shared.RevitAPI;
using Synthetic.Infrastructure.IO;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;
using Synthetic.Modules.StandardsManagement.ViewModels;

namespace Synthetic.Modules.MaterialManagement.Commands
{
    /// <summary>
    /// Paints all faces of selected elements with a material
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class PaintElements : IExternalCommand
    {
        /// <summary>
        /// Execute a Revit Command
        /// </summary>
        /// <param name="commandData">commandData</param>
        /// <param name="message">message</param>
        /// <param name="elementSet">Currently selected elements</param>
        /// <returns>A Autodesk.Revit.UI.Result</returns>
        public Result Execute(
          ExternalCommandData commandData,
          ref string message,
          ElementSet elementSet)
        {
            UIApplication uiapp = commandData.Application;
            UIDocument uidoc = uiapp.ActiveUIDocument;
            Document document = uidoc.Document;

            ICollection<ElementId> elemIds = uidoc.Selection.GetElementIds();

            if (elemIds.Count > 0)
            {
                FilteredElementCollector collector = new FilteredElementCollector(document)
                    .OfClass(typeof(Material));

                List<string> names = collector.Select(x => x.Name).ToList();
                string? selectedMaterial = SelectMaterial(names, uiapp.MainWindowHandle);
                
                if (string.IsNullOrEmpty(selectedMaterial))
                {
                    return Result.Cancelled;
                }

                Material? material = MaterialUtil.GetByNameDocument(selectedMaterial!, document);
                if (material == null)
                {
                    return Result.Failed;
                }
                ElementId materialId = material.Id;

                string transactionName = "Paint Elements with material " + selectedMaterial;
                using (Autodesk.Revit.DB.Transaction trans = new Autodesk.Revit.DB.Transaction(document))
                {
                    trans.Start(transactionName);
                    try
                    {
                        foreach (ElementId id in elemIds)
                        {
                            Element element = document.GetElement(id);
                            ElementUtil.PaintElement(element, materialId);
                        }
                        trans.Commit();
                    }
                    catch { trans.RollBack(); }
                }
            }

            return Result.Succeeded;
        }

        internal string? SelectMaterial(List<string> materialNames, IntPtr mainWindowHandle)
        {
            DropdownSelectionViewModel viewModel = new DropdownSelectionViewModel();
            viewModel.Title = "Select a single material";
            viewModel.Instruction = "Elements will be painted with selected material";
            viewModel.ItemLabel = "Materials";
            viewModel.Items = materialNames;
            viewModel.IsSorted = true;

            DropdownSelectionView dialog = new DropdownSelectionView(mainWindowHandle) { DataContext = viewModel };
            bool? dResult = dialog.ShowDialog();

            return dResult == true ? viewModel.SelectedItem : null;
        }
    }
}
```

### File: MaterialManagement/Utilities/MaterialPathUtils.cs
```csharp
using Autodesk.Revit.DB;
using RevitDoc = Autodesk.Revit.DB.Document;
using Material = Autodesk.Revit.DB.Material;
using Autodesk.Revit.DB.Visual;
using Synthetic.Modules.MaterialManagement.Commands;
using Synthetic.Modules.MaterialManagement.Utilities;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Synthetic.Modules.StandardsManagement.ViewModels;


namespace Synthetic.Modules.MaterialManagement.Utilities
{
    /// <summary>
    /// Utility methods for resolving paths of Revit Material assets.
    /// </summary>
    public static class MaterialPathUtils
    {
        
    }
}
```

### File: MaterialManagement/Utilities/MaterialUtil.cs
```csharp
using Synthetic.Modules.MaterialManagement.Commands;
using Synthetic.Modules.MaterialManagement.Utilities;

using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text;
using System.Threading.Tasks;

using revitDB = Autodesk.Revit.DB;
using revitDoc = Autodesk.Revit.DB.Document;
using revitMaterial = Autodesk.Revit.DB.Material;
using Autodesk.Revit.DB.Visual;
using Autodesk.Revit.DB;
using System.Globalization;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.Infrastructure.IO;

namespace Synthetic.Modules.MaterialManagement.Utilities
{
    /// <summary>
    /// Extensions of Dynamo Revit
    /// </summary>
    public class MaterialUtil
    {
        /// <summary>
        /// The default path to the Autodesk Shared Materials Textures directory.
        /// </summary>
        public const string AutodeskMaterialLibrary = "C:\\Program Files (x86)\\Common Files\\Autodesk Shared\\Materials\\Textures";

        internal MaterialUtil() { }

        /// <summary>
        /// Gets a material given its name and document
        /// </summary>
        /// <param name="Name">Name of a material</param>
        /// <param name="Document">Document to get the material from</param>
        /// <returns name="Material">A Autodeks.Revit.DB.Material</returns>
        public static revitMaterial? GetByNameDocument(string Name, revitDoc Document)
        {
            revitDB.FilteredElementCollector collector
                = new revitDB.FilteredElementCollector(Document);

            collector
                .OfClass(typeof(revitDB.Material))
                .OfType<revitDB.Material>();

            return collector
                .OfType<revitDB.Material>()
                .FirstOrDefault(
                m => m.Name.Equals(Name));
        }

        /// <summary>
        /// Retrieves all material elements in the specified document.
        /// </summary>
        /// <param name="Document">The Revit document.</param>
        /// <returns>A FilteredElementCollector containing the materials.</returns>
        public static FilteredElementCollector GetAllMaterials(revitDoc Document)
        {
            revitDB.FilteredElementCollector collector
                = new revitDB.FilteredElementCollector(Document);

            collector
                .OfClass(typeof(revitDB.Material))
                .OfType<revitDB.Material>();

            return collector;
        }

        /// <summary>
        /// Gets all the connected files with paths associated with a material.
        /// </summary>
        /// <param name="Material">A revit material</param>
        /// <returns name="Paths">Full file names with paths</returns>
        public static List<string>? GetMaterialBitmapPaths(revitMaterial Material)
        {
            List<string>? paths = null;

            revitDB.ElementId appearanceAssetID = Material.AppearanceAssetId;

#if REVIT2022 || REVIT2023
            if (appearanceAssetID.IntegerValue != -1)
#else
            if (appearanceAssetID.Value != -1)
#endif
                {
                    paths = new List<string>();
                revitDB.AppearanceAssetElement? assetElem = Material.Document.GetElement(appearanceAssetID) as revitDB.AppearanceAssetElement;

                if (assetElem != null)
                {
                    Asset renderingAsset = assetElem.GetRenderingAsset();

                    for (int idx = 0; idx < renderingAsset.Size; idx++)
                    {
                        AssetProperty property = renderingAsset.Get(idx);
                        List<string> tempPath = _ReadAssetPropertyPaths(property);
                        if (tempPath != null && tempPath.Count > 0)
                        {
                            paths.AddRange(tempPath);
                        }
                    }
                }
            }
            return paths;
        }

        /// <summary>
        /// Given a material list and a SearchPaths object, the method will replace the path of any bitmaps in the material based on the SearchPaths.
        /// </summary>
        /// <param name="Materials">A list of Autodesk.Revit.DB.Material elements</param>
        /// <param name="searchPaths">A SearchPaths object that includes a prioritzed list of search paths</param>
        /// <param name="ReplaceRelativePaths">If True, replace files that are Relative Paths, otherwise don't replace.</param>
        /// <param name="RunTest">If False, replace the paths. If True, the method runs a test replacement instead but doesn't actually edit the material.</param>
        /// <returns name="Paths Replaced">A list of the paths replaced</returns>
        /// <returns name="Paths NOT Replaced">A list of the paths that were left unchanged.</returns>
        public static Dictionary<string, object> ReplaceBitmapPaths(List<revitMaterial> Materials,
            SearchPaths searchPaths,
            bool ReplaceRelativePaths = false,
            bool RunTest = false
            )
        {
            List<List<string>>? results = null;

            if (searchPaths != null && Materials != null && Materials.Count > 0)
            {
                foreach (revitMaterial Material in Materials)
                {
                    revitDB.ElementId appearanceAssetID = Material.AppearanceAssetId;


#if REVIT2022 || REVIT2023
                    if (appearanceAssetID.IntegerValue != -1)
#else
                    if (appearanceAssetID.Value != -1)
#endif

                    {
                        revitDB.AppearanceAssetElement? assetElem 
                            = Material.Document.GetElement(appearanceAssetID) as revitDB.AppearanceAssetElement;

                        if (assetElem != null)
                        {
                            revitDoc document = Material.Document;

                            if (!RunTest)
                            {
                                results = _ReplaceBitmapPaths(assetElem, searchPaths, ReplaceRelativePaths, RunTest);
                            }
                        }
                    }
                }
            }
            if (results != null)
            {
                return new Dictionary<string, object>
            {
                {"Paths Replaced", results[0]},
                {"Paths NOT Replaced", results[1] }
            };
            }
            else
            {
                return new Dictionary<string, object>
            {
                {"Paths Replaced", new List<string>()},
                {"Paths NOT Replaced", new List<string>()}
            };
            }
        }

        private static List<List<string>> _ReplaceBitmapPaths(
            revitDB.AppearanceAssetElement assetElem,
            SearchPaths searchPaths,
            bool ReplaceRelativePaths,
            bool RunTest
            )
        {
            List<string> pathsReplaced = new List<string>();
            List<string> pathsNotReplaced = new List<string>();
            string? file = null;
            string? filePath = null;
            string? newFilePath = null;

            using (AppearanceAssetEditScope editScope = new AppearanceAssetEditScope(assetElem.Document))
            {
                Asset? renderAsset = null;

                if (!RunTest)
                {
                    // returns an editable copy of the appearance asset
                    renderAsset = editScope.Start(assetElem.Id);
                }
                else
                {
                    renderAsset = assetElem.GetRenderingAsset();
                }

                if (renderAsset != null)
                {
                    for (int idx = 0; idx < renderAsset.Size; idx++)
                    {
                        AssetProperty? property = renderAsset.Get(idx);
                        Asset? connectedAsset = property?.GetSingleConnectedAsset();

                        if (connectedAsset != null)
                        {
                            AssetPropertyString? bitmapProperty = connectedAsset.FindByName(UnifiedBitmap.UnifiedbitmapBitmap) as AssetPropertyString;

                            if (bitmapProperty == null)
                            {
                                bitmapProperty = connectedAsset.FindByName(BumpMap.BumpmapBitmap) as AssetPropertyString;
                            }
                            if (bitmapProperty != null)
                            {
                                char[] separator = { '|' };

                                filePath = bitmapProperty.Value;

                                if (filePath != null && filePath != String.Empty)
                                {
                                    // If the Path is from the Revit Library it will contain a '|' character.
                                    // If ReplaceRelativePaths is True, then generate a new path
                                    // Otherwise, if the path is absolute, then generate a new path
                                    if (filePath.Contains(separator[0]) && ReplaceRelativePaths)
                                    {
                                        filePath = filePath.Split(separator).Last();
                                        file = Path.GetFileName(filePath);
                                        newFilePath = searchPaths.GetFilePath(file);
                                    }
                                    else if ((MaterialUtil.IsPathFullyQualified(filePath)
                                        && Uri.IsWellFormedUriString(filePath, UriKind.RelativeOrAbsolute))
                                        || ReplaceRelativePaths)
                                    {
                                        file = Path.GetFileName(filePath);
                                        newFilePath = searchPaths.GetFilePath(file);
                                    }

                                    // If a new path is found and it is a valid property value, then edit the path.
                                    // Else, record that the path was not changed.
                                    if (newFilePath != null && bitmapProperty.Value != newFilePath)
                                    {
                                        // Only make the change is Execute is True
                                        if (!RunTest && bitmapProperty.IsValidValue(newFilePath))
                                        { bitmapProperty.Value = newFilePath; }

                                        pathsReplaced.Add(newFilePath);
                                    }
                                    else
                                    {
                                        pathsNotReplaced.Add(filePath);
                                    }
                                }
                            }
                        }
                    }
                }
                if (!RunTest)
                {
                    editScope.Commit(true);
                }
            }
            return new List<List<string>> { { pathsReplaced }, { pathsNotReplaced } };
        }

        /// <summary>
        /// Checks if a AssetProperty has connected properties and if those connected properties have a bitmap path, it returns the path.
        /// </summary>
        /// <param name="assetProperty">A Revit AssetProperty element</param>
        /// <returns name="paths">Bitmap paths</returns>
        private static List<string> _ReadAssetPropertyPaths(AssetProperty assetProperty)
        {
            List<string> paths = new List<string>();

            if (assetProperty.NumberOfConnectedProperties == 1)
            {
                Asset connectedAsset = assetProperty.GetSingleConnectedAsset();
                if (connectedAsset != null)
                {
                    AssetPropertyString? bitmapProperty = connectedAsset.FindByName(UnifiedBitmap.UnifiedbitmapBitmap) as AssetPropertyString;

                    if (bitmapProperty == null)
                    {
                        bitmapProperty = connectedAsset.FindByName(BumpMap.BumpmapBitmap) as AssetPropertyString;
                    }
                    if (bitmapProperty != null && bitmapProperty.Value != "")
                    {
                        string? path = bitmapProperty.Value;
                        if (path != null && (path.StartsWith("1\\", true, CultureInfo.CurrentCulture) ||
                            path.StartsWith("2\\", true, CultureInfo.CurrentCulture) ||
                            path.StartsWith("3\\", true, CultureInfo.CurrentCulture)))
                        {
                            path = AutodeskMaterialLibrary + "\\" + path;
                        }
                        if (path != null)
                        {
                            paths.Add(path);
                        }
                        path = null;
                    }
                }
            }
            else
            {
                int num = assetProperty.NumberOfConnectedProperties;
            }

            return paths;
        }

        private static bool IsPathFullyQualified(string path)
        {
            if (path == null) throw new ArgumentNullException(nameof(path));
            if (path.Length < 2) return false; //There is no way to specify a fixed path with one character (or less).
            if (path.Length == 2 && IsValidDriveChar(path[0]) && path[1] == System.IO.Path.VolumeSeparatorChar) return true; //Drive Root C:
            if (path.Length >= 3 && IsValidDriveChar(path[0]) && path[1] == System.IO.Path.VolumeSeparatorChar && IsDirectorySeperator(path[2])) return true; //Check for standard paths. C:\
            if (path.Length >= 3 && IsDirectorySeperator(path[0]) && IsDirectorySeperator(path[1])) return true; //This is start of a UNC path
            return false; //Default
        }

        private static bool IsDirectorySeperator(char c) => c == System.IO.Path.DirectorySeparatorChar | c == System.IO.Path.AltDirectorySeparatorChar;
        private static bool IsValidDriveChar(char c) => c >= 'A' && c <= 'Z' || c >= 'a' && c <= 'z';
    }
}
```

### File: FamilyManagement/Commands/AuditPurgeAllFamilies.cs
```csharp
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Application = Autodesk.Revit.ApplicationServices.Application;
using Synthetic.Modules.FamilyManagement.Commands;

using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using Autodesk.Revit.Attributes;
using System.Diagnostics;
using System.IO;
using Newtonsoft.Json;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;
using Synthetic.Shared.UI;
using Synthetic.Shared.RevitAPI;
using Synthetic.Modules.FamilyManagement.Handlers;

namespace Synthetic.Modules.FamilyManagement.Commands
{
    /// <summary>
    /// Revit external command to audit and purge all families loaded in the current project.
    /// Provides options to audit only, purge unused elements, and delete schema options.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class AuditPurgeAllFamilies : IExternalCommand
    {
        private static Handlers.AuditPurgeEventHandler? _eventHandler;
        private static ExternalEvent? _externalEvent;

        /// <summary>
        /// Execute a Revit Command
        /// </summary>
        /// <param name="commandData">commandData</param>
        /// <param name="message">message</param>
        /// <param name="elements">Currently selected elements</param>
        /// <returns>A Autodesk.Revit.UI.Result</returns>
        public Result Execute(
          ExternalCommandData commandData,
          ref string message,
          ElementSet elements)
        {
            UIApplication uiapp = commandData.Application;
            UIDocument uidoc = uiapp.ActiveUIDocument;
            Document doc = uidoc.Document;

            TaskDialogResult dialogResult = PurgeOptions();

            bool purge = false;
            bool purgeSchema = false;
            List<string>? schemaExceptions = null;

            switch (dialogResult)
            {
                case TaskDialogResult.Cancel:
                    return Result.Cancelled;
                case TaskDialogResult.CommandLink1:
                    purge = false;
                    purgeSchema = false;
                    break;
                case TaskDialogResult.CommandLink2:
                    purge = true;
                    purgeSchema = false;
                    break;
                case TaskDialogResult.CommandLink3:
                    purge = true;
                    purgeSchema = true;
                    schemaExceptions = new List<string>() { "Enscape", "Kinship", "CTC", "Synthetic" };
                    break;
                case TaskDialogResult.CommandLink4:
                    purge = true;
                    purgeSchema = true;
                    schemaExceptions = null;
                    break;
                default:
                    return Result.Failed;
            }

            // Calculate total families to be processed
            IList<Family> families = new FilteredElementCollector(doc)
                .OfClass(typeof(Family))
                .Cast<Family>()
                .Where(f => f.IsEditable && !f.IsInPlace)
                .ToList();

            int totalFamilies = families.Count;

            // Initialize Progress Coordinator (modeless progress window)
            Synthetic.Shared.UI.ProgressCoordinator.Initialize("Audit & Purge Families", "Purging loaded families...", totalFamilies);

            // Register and raise the external event
            _eventHandler = new Handlers.AuditPurgeEventHandler
            {
                Purge = purge,
                PurgeSchema = purgeSchema,
                SchemaExceptions = schemaExceptions
            };

            _externalEvent = ExternalEvent.Create(_eventHandler);
            if (_externalEvent == null)
            {
                Synthetic.Shared.UI.ProgressCoordinator.Close();
                Autodesk.Revit.UI.TaskDialog.Show("Audit & Purge Families", "Failed to create the external event handler.");
                return Result.Failed;
            }

            // Raise the event to start processing on the main thread
            _externalEvent.Raise();

            return Result.Succeeded;
        }

        internal TaskDialogResult PurgeOptions()
        {
            Autodesk.Revit.UI.TaskDialog taskDialog = new Autodesk.Revit.UI.TaskDialog("Audit & Purge Families");
            taskDialog.MainInstruction = "Select Options to Audit and Purge All the Families in the Project";
            taskDialog.MainContent = "Command will open each family in the project, audit it and based on the options below, purge any unused elements and delete all schema.  Note that purging unused elements could impact the options available for nested families.  In addition, deleting all scheme from each family, deletes it from all open documents including the current project.  This will delete Enscape, Kinship, CTC, and Synthetic settings.  To keep these schema, only delete Typical Schema ";
            taskDialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Audit All Families Only");
            taskDialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Audit and Purge Unused");
            taskDialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "Audit, Purge and Delete Schema Except Typical");
            taskDialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink4, "Audit, Purge and Delete All Schema");

            taskDialog.CommonButtons = TaskDialogCommonButtons.Cancel;
            taskDialog.DefaultButton = TaskDialogResult.CommandLink1;

            TaskDialogResult result = taskDialog.Show();
            taskDialog.Dispose();
            return result;
        }
    }
}
```

### File: FamilyManagement/Commands/FamiliesForceReinsert.cs
```csharp
#region Namespaces
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Synthetic.Modules.FamilyManagement.Commands;

using System;
using System.IO;
using System.Collections.Generic;
using System.Windows;
using Synthetic.Infrastructure.Serialization;
using Synthetic.Infrastructure.IO;
using Synthetic.Settings;
using Synthetic.Infrastructure.Persistence;
using Application = Autodesk.Revit.ApplicationServices.Application;
using View = Autodesk.Revit.DB.View;
using System.Linq;
using System.Runtime;
using Autodesk.Revit.DB.Events;

using Synthetic.Shared.RevitAPI;
using SFamilyUtil = Synthetic.Shared.RevitAPI.FamilyUtil;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;
using Synthetic.Modules.StandardsManagement.ViewModels;

#endregion

namespace Synthetic.Modules.FamilyManagement.Commands
{
    /// <summary>
    /// Sets the path and sheet to an Excel file with Worksets.  Stores the setting in an Extensible Storage.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class FamiliesForceReinsert : IExternalCommand
    {
        /// <summary>
        /// Execute a Revit Command
        /// </summary>
        /// <param name="commandData">commandData</param>
        /// <param name="message">message</param>
        /// <param name="elements">Currently selected elements</param>
        /// <returns>A Autodesk.Revit.UI.Result</returns>
        public Result Execute(
          ExternalCommandData commandData,
          ref string message,
          ElementSet elements)
        {
            UIApplication uiapp = commandData.Application;
            UIDocument uidoc = uiapp.ActiveUIDocument;
            Application app = uiapp.Application;
            Document doc = uidoc.Document;

            SFamilyUtil.ForceReinsertAnnotation(doc);
            
            return Result.Succeeded;
        }
    }
}
```

### File: FamilyManagement/Handlers/AuditPurgeEventHandler.cs
```csharp
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.DB.ExtensibleStorage;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Synthetic.Shared.UI;
using Synthetic.Core;
using Synthetic;

using Synthetic.Shared.RevitAPI;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;
using Synthetic.Modules.FamilyManagement.Handlers;
using Synthetic.Modules.FamilyManagement.Utilities;
namespace Synthetic.Modules.FamilyManagement.Handlers
{
    /// <summary>
    /// External event handler to process Audit and Purge on all editable families in the active document.
    /// </summary>
    public class AuditPurgeEventHandler : IExternalEventHandler
    {
        /// <summary>
        /// Gets or sets whether to purge unused elements.
        /// </summary>
        public bool Purge { get; set; } = false;

        /// <summary>
        /// Gets or sets whether to purge extensible storage schemas.
        /// </summary>
        public bool PurgeSchema { get; set; } = false;

        /// <summary>
        /// Gets or sets the list of schema names to exclude from purging.
        /// </summary>
        public List<string>? SchemaExceptions { get; set; }

        /// <summary>
        /// Executes the audit and purge loop on the Revit API thread.
        /// </summary>
        /// <param name="app">The Revit UIApplication context.</param>
        public void Execute(UIApplication app)
        {
            if (app == null) return;
            UIDocument uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;
            Document doc = uidoc.Document;

            List<List<string>> results = new List<List<string>>();
            List<List<string>> errors = new List<List<string>>();

            // Collect editable, non-inplace families
            IList<Family> families = new FilteredElementCollector(doc)
                .OfClass(typeof(Family))
                .Cast<Family>()
                .Where(f => f.IsEditable && !f.IsInPlace)
                .ToList();

            // Checkout worksets if workshared
            if (doc.IsWorkshared && families.Count > 0)
            {
                try
                {
                    IList<WorksetId> worksetIds = families.Select(f => f.WorksetId).Distinct().ToList();
                    WorksharingUtils.CheckoutWorksets(doc, worksetIds);
                }
                catch (Exception ex)
                {
                    List<string> checkoutErr = new List<string> { "Worksets Checkout", "", "", $"Error checking out worksets: {ex.Message}" };
                    errors.Add(checkoutErr);
                }
            }

            try
            {
                foreach (Family family in families)
                {
                    if (ProgressCoordinator.IsCancelled())
                    {
                        break;
                    }

                    string familyName = family.Name;
                    List<string> familyResult = new List<string>
                    {
                        family.Name,
                        family.Id.ToString(),
                        family.UniqueId
                    };

                    Document? familyDoc = null;
                    try
                    {
                        familyDoc = doc.EditFamily(family);
                        familyResult.Add("Opened successfully");

                        // Get warnings
                        IList<FailureMessage> warnings = familyDoc.GetWarnings();
                        if (warnings.Count > 0)
                        {
                            StringBuilder warningString = new StringBuilder();
                            foreach (FailureMessage warning in warnings)
                            {
                                warningString.AppendLine(warning.GetDescriptionText());
                            }
                            familyResult.Add(warningString.ToString());
                        }

                        // Purge unused
                        if (Purge)
                        {
#if !REVIT2022
                            DocumentUtil.Purge(doc.Application, familyDoc);
                            familyResult.Add("Unused Purged");
#endif
                        }

                        // Purge Extensible Storage schemas
                        if (PurgeSchema && StorageUtil.DoesAnyStorageExist(familyDoc))
                        {
                            List<Schema>? schemas = StorageUtil.GetDocumentSchemas(doc);
                            List<Schema> filteredSchemas = new List<Schema>();
                            if (schemas != null)
                            {
                                if (SchemaExceptions != null)
                                {
                                    foreach (Schema schema in schemas)
                                    {
                                        if (schema != null && schema.SchemaName != null && !SchemaExceptions.Any(schema.SchemaName.Contains))
                                        {
                                            filteredSchemas.Add(schema);
                                        }
                                    }
                                }
                                else
                                {
                                    filteredSchemas.AddRange(schemas);
                                }
                            }

                            List<string>? schemaResults = StorageUtil.PurgeSchema(filteredSchemas, doc);
                            if (schemaResults != null)
                            {
                                if (schemaResults.Count > 0)
                                {
                                    familyResult.Add(string.Join(", ", schemaResults));
                                }
                                else
                                {
                                    familyResult.Add("No schemas were purged");
                                }
                            }
                            else
                            {
                                familyResult.Add("No matching schemas found to purge");
                            }
                        }

                        // Reload family using SafeFamilyLoadOptions inside a Transaction utilizing PurgeFailuresPreprocessor
                        using (Transaction trans = new Transaction(doc, $"Reload Family: {familyName}"))
                        {
                            FailureHandlingOptions options = trans.GetFailureHandlingOptions();
                            options.SetFailuresPreprocessor(new PurgeFailuresPreprocessor());
                            trans.SetFailureHandlingOptions(options);

                            trans.Start();
                            familyDoc.LoadFamily(doc, new SafeFamilyLoadOptions());
                            trans.Commit();
                        }

                        results.Add(familyResult);
                    }
                    catch (Exception ex)
                    {
                        familyResult.Add("Error: " + ex.Message);
                        errors.Add(familyResult);
                    }
                    finally
                    {
                        if (familyDoc != null)
                        {
                            familyDoc.Close(false);
                            familyDoc.Dispose();
                        }
                    }

                    ProgressCoordinator.UpdateProgress(familyName);
                }
            }
            finally
            {
                // Explicitly collect memory and close coordinator
                GC.Collect();
                GC.WaitForPendingFinalizers();

                ProgressCoordinator.Close();

                if (results.Count > 0 || errors.Count > 0)
                {
                    var finalResults = new Dictionary<string, object>
                    {
                        { "Results", results },
                        { "Errors", errors }
                    };
                    CommandUtil.SaveResults(finalResults, doc, "Audit and Purge Family Results");
                }
            }
        }

        /// <summary>
        /// Gets the name of the external event handler.
        /// </summary>
        public string GetName()
        {
            return "Audit & Purge Families Async Handler";
        }
    }
}
```

### File: FamilyManagement/Utilities/PurgeFailuresPreprocessor.cs
```csharp
using Autodesk.Revit.DB;
using System.Collections.Generic;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;

namespace Synthetic.Modules.FamilyManagement.Utilities
{
    /// <summary>
    /// Silently preprocesses and handles failures triggered during family audit and purge operations.
    /// </summary>
    public class PurgeFailuresPreprocessor : IFailuresPreprocessor
    {
        /// <summary>
        /// Preprocesses failures to delete warnings and attempt resolution of errors.
        /// </summary>
        /// <param name="failuresAccessor">The failures accessor.</param>
        /// <returns>FailureProcessingResult.</returns>
        public FailureProcessingResult PreprocessFailures(FailuresAccessor failuresAccessor)
        {
            IList<FailureMessageAccessor> failureMessages = failuresAccessor.GetFailureMessages();

            foreach (FailureMessageAccessor failure in failureMessages)
            {
                FailureSeverity severity = failure.GetSeverity();
                if (severity == FailureSeverity.Warning)
                {
                    failuresAccessor.DeleteWarning(failure);
                }
                else if (severity == FailureSeverity.Error)
                {
                    failuresAccessor.ResolveFailure(failure);
                }
            }
            return FailureProcessingResult.ProceedWithCommit;
        }
    }
}
```

### File: FamilyManagement/Utilities/SafeFamilyLoadOptions.cs
```csharp
using Autodesk.Revit.DB;

namespace Synthetic.Modules.FamilyManagement.Utilities
{
    /// <summary>
    /// Safe family load options that prevent overwriting existing parameter values in the project.
    /// </summary>
    public class SafeFamilyLoadOptions : IFamilyLoadOptions
    {
        /// <summary>
        /// Called when a family is found in the project.
        /// </summary>
        public bool OnFamilyFound(
          bool familyInUse,
          out bool overwriteParameterValues)
        {
            overwriteParameterValues = false;
            return true;
        }

        /// <summary>
        /// Called when a shared nested family is found in the project.
        /// </summary>
        public bool OnSharedFamilyFound(
          Family sharedFamily,
          bool familyInUse,
          out FamilySource source,
          out bool overwriteParameterValues)
        {
            source = FamilySource.Family;
            overwriteParameterValues = false;
            return true;
        }
    }
}
```

