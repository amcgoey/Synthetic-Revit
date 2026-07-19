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
                if (SetProperty(ref _winningValueElementId, value))
                {
                    OnPropertyChanged(nameof(IsSourceWinning));
                    OnPropertyChanged(nameof(IsTargetWinning));
                }
            }
        }

        /// <summary>
        /// Gets or sets whether the source value is selected as the winning value.
        /// </summary>
        [JsonIgnore]
        public bool IsSourceWinning
        {
            get => Options != null && Options.Count > 0 && WinningValueElementId == Options[0].ElementId;
            set
            {
                if (value && Options != null && Options.Count > 0)
                {
                    WinningValueElementId = Options[0].ElementId ?? new ElementIdModel { Id = -1 };
                }
            }
        }

        /// <summary>
        /// Gets or sets whether the target value is selected as the winning value.
        /// </summary>
        [JsonIgnore]
        public bool IsTargetWinning
        {
            get => Options != null && Options.Count > 1 && WinningValueElementId == Options[1].ElementId;
            set
            {
                if (value && Options != null && Options.Count > 1)
                {
                    WinningValueElementId = Options[1].ElementId ?? new ElementIdModel { Id = -1 };
                }
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
            set => SetProperty(ref _options, value);
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
