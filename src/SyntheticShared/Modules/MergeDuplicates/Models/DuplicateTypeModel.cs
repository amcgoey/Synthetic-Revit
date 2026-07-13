using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Autodesk.Revit.DB;

using Synthetic.Modules.RevitDOM;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.Shared.RevitAPI;
namespace Synthetic.Modules.MergeDuplicates.Models
{
    /// <summary>
    /// Represents a specific duplicate FamilySymbol, GroupType, or AssemblyType.
    /// </summary>
    public class DuplicateTypeModel : ObjectModel, INotifyPropertyChanged
    {
        private ElementId _revitTypeId = ElementId.InvalidElementId;
        private string _name = string.Empty;
        private Dictionary<string, string> _parameters;

        /// <summary>
        /// Initializes a new instance of the DuplicateTypeModel class.
        /// </summary>
        public DuplicateTypeModel()
        {
            _parameters = new Dictionary<string, string>();
        }

        /// <summary>
        /// The Revit ElementId of this type.
        /// </summary>
        public ElementId RevitTypeId
        {
            get => _revitTypeId;
            set => SetProperty(ref _revitTypeId, value);
        }

        /// <summary>
        /// The name of this type.
        /// </summary>
        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }

        /// <summary>
        /// Dictionary of parameters mapping parameter name to its storage type or value string.
        /// </summary>
        public Dictionary<string, string> Parameters
        {
            get => _parameters;
            set => SetProperty(ref _parameters, value);
        }

        #region INotifyPropertyChanged Implementation

        /// <summary>
        /// Occurs when a property value changes.
        /// </summary>
        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>
        /// Raises the PropertyChanged event.
        /// </summary>
        /// <param name="propertyName">The name of the property that changed.</param>
        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        /// <summary>
        /// Sets a property value and raises the PropertyChanged event if the value changed.
        /// </summary>
        /// <typeparam name="T">The type of the property.</typeparam>
        /// <param name="storage">Reference to the backing field.</param>
        /// <param name="value">The new value to set.</param>
        /// <param name="propertyName">The name of the property (automatically populated).</param>
        /// <returns>True if the value was modified; otherwise, false.</returns>
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
