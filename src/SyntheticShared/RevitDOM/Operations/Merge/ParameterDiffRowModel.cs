using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Newtonsoft.Json;
using Synthetic.RevitDOM.Models;

namespace Synthetic.RevitDOM.Operations.Merge
{
    /// <summary>
    /// Represents a specific value option for a parameter.
    /// </summary>
    public class ParameterValueOption
    {
        /// <summary>
        /// Gets or sets the ElementId of the option.
        /// </summary>
        public ElementIdModel? ElementId { get; set; }

        /// <summary>
        /// Gets or sets the display text for the option.
        /// </summary>
        public string DisplayText { get; set; } = string.Empty;
    }

    /// <summary>
    /// Represents a single parameter row in the Diff Tool.
    /// </summary>
    public class ParameterDiffRowModel : ObjectModel, INotifyPropertyChanged
    {
        private string _parameterName = string.Empty;
        private Dictionary<ElementIdModel, string> _values;
        private bool _isSchemaMismatch;
        private ElementIdModel _winningValueElementId;
        private bool _isApproved = true;

        /// <summary>
        /// Gets or sets a value indicating whether this parameter override is approved by the user.
        /// </summary>
        public bool IsApproved
        {
            get => _isApproved;
            set => SetProperty(ref _isApproved, value);
        }

        // Compatibility fields
        private List<string> _valueList;
        private List<ParameterValueOption> _options;
        private bool _hasConflict;

        /// <summary>
        /// Initializes a new instance of the ParameterDiffRowModel class.
        /// </summary>
        public ParameterDiffRowModel()
        {
            _winningValueElementId = new ElementIdModel { Id = -1 };
            _values = new Dictionary<ElementIdModel, string>();
            _valueList = new List<string>();
            _options = new List<ParameterValueOption>();
        }

        /// <summary>
        /// The name of the parameter.
        /// </summary>
        public string ParameterName
        {
            get => _parameterName;
            set => SetProperty(ref _parameterName, value);
        }

        /// <summary>
        /// Maps the type/symbol ElementId to the parameter value string.
        /// </summary>
        public Dictionary<ElementIdModel, string> Values
        {
            get => _values;
            set => SetProperty(ref _values, value);
        }

        /// <summary>
        /// True if there is a schema storage type mismatch for this parameter across duplicate types.
        /// </summary>
        public bool IsSchemaMismatch
        {
            get => _isSchemaMismatch;
            set => SetProperty(ref _isSchemaMismatch, value);
        }

        /// <summary>
        /// The ElementId of the type whose parameter value should win during merge.
        /// </summary>
        public ElementIdModel WinningValueElementId
        {
            get => _winningValueElementId;
            set
            {
                SetProperty(ref _winningValueElementId, value);
                OnPropertyChanged(nameof(IsSourceWinning));
                OnPropertyChanged(nameof(IsTargetWinning));
                OnPropertyChanged(nameof(WinningValue));
            }
        }

        private bool IsOptionWinning(int index)
        {
            return Options != null && Options.Count > index && Options[index].ElementId != null && WinningValueElementId != null && WinningValueElementId == Options[index].ElementId;
        }

        private void SetOptionWinning(int index, bool value)
        {
            if (value && Options != null && Options.Count > index && Options[index].ElementId != null)
            {
                WinningValueElementId = Options[index].ElementId;
            }
        }

        /// <summary>
        /// Gets or sets whether the source value is selected as the winning value.
        /// </summary>
        [JsonIgnore]
        public bool IsSourceWinning
        {
            get => IsOptionWinning(0);
            set => SetOptionWinning(0, value);
        }

        /// <summary>
        /// Gets or sets whether the target value is selected as the winning value.
        /// </summary>
        [JsonIgnore]
        public bool IsTargetWinning
        {
            get => IsOptionWinning(1);
            set => SetOptionWinning(1, value);
        }

        /// <summary>
        /// Safely retrieves the parameter value associated with the specified <see cref="ElementIdModel"/> key,
        /// using value-equality fallback search if standard dictionary lookup fails.
        /// Prevents <see cref="KeyNotFoundException"/> when queried with distinct <see cref="ElementIdModel"/> instances.
        /// </summary>
        /// <param name="elementId">The element ID key to query.</param>
        /// <returns>The parameter value string, or empty string if not found.</returns>
        public string GetValueForElement(ElementIdModel? elementId)
        {
            if (elementId == null || _values == null) return string.Empty;
            if (_values.TryGetValue(elementId, out string? value))
            {
                return value ?? string.Empty;
            }

            // Fallback: value equality search across dictionary keys
            foreach (var kvp in _values)
            {
                if (kvp.Key == elementId || (kvp.Key != null && kvp.Key.Equals(elementId)))
                {
                    return kvp.Value ?? string.Empty;
                }
            }

            return string.Empty;
        }

        /// <summary>
        /// Gets the parameter value associated with the current <see cref="WinningValueElementId"/>.
        /// </summary>
        [JsonIgnore]
        public string? WinningValue
        {
            get
            {
                if (WinningValueElementId != null && _values != null && _values.TryGetValue(WinningValueElementId, out string? value))
                {
                    return value;
                }
                return null;
            }
        }

        private bool _injectParameter;

        /// <summary>
        /// True if the parameter should be dynamically injected into the target family during merge.
        /// </summary>
        public bool InjectParameter
        {
            get => _injectParameter;
            set => SetProperty(ref _injectParameter, value);
        }

        private bool _isInjectEnabled = true;

        /// <summary>
        /// True if the parameter can/should be injected (i.e. it does not already exist in the primary family).
        /// </summary>
        public bool IsInjectEnabled
        {
            get => _isInjectEnabled;
            set => SetProperty(ref _isInjectEnabled, value);
        }

        #region Compatibility Properties

        /// <summary>
        /// Gets or sets the list of parameter values for compatibility.
        /// </summary>
        public List<string> ValueList
        {
            get => _valueList;
            set => SetProperty(ref _valueList, value);
        }

        /// <summary>
        /// Gets or sets the list of value options for compatibility.
        /// </summary>
        public List<ParameterValueOption> Options
        {
            get => _options;
            set
            {
                if (SetProperty(ref _options, value))
                {
                    OnPropertyChanged(nameof(IsSourceWinning));
                    OnPropertyChanged(nameof(IsTargetWinning));
                    OnPropertyChanged(nameof(WinningValue));
                }
            }
        }

        /// <summary>
        /// Gets or sets a value indicating whether there is a conflict in values for compatibility.
        /// </summary>
        public bool HasConflict
        {
            get => _hasConflict;
            set => SetProperty(ref _hasConflict, value);
        }

        #endregion

        #region INotifyPropertyChanged Implementation

        /// <summary>
        /// Occurs when a property value changes.
        /// </summary>
        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>
        /// Raises the <see cref="PropertyChanged"/> event.
        /// </summary>
        /// <param name="propertyName">The name of the property that changed.</param>
        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        /// <summary>
        /// Sets a property value and raises the <see cref="PropertyChanged"/> event if the value changed.
        /// </summary>
        /// <typeparam name="T">The type of the property.</typeparam>
        /// <param name="storage">A reference to the backing field of the property.</param>
        /// <param name="value">The new value to set.</param>
        /// <param name="propertyName">The name of the property.</param>
        /// <returns>True if the value changed; otherwise, false.</returns>
        protected virtual bool SetProperty<T>(ref T storage, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(storage, value)) return false;
            storage = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        #endregion
    }
}
