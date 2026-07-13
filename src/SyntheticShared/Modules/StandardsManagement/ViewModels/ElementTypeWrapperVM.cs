using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;

using Synthetic.Modules.RevitDOM;
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
