using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows.Input;

using Synthetic.Modules.RevitDOM;
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
