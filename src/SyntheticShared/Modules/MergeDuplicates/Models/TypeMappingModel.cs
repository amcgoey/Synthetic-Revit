using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.Shared.RevitAPI;
namespace Synthetic.Modules.MergeDuplicates.Models
{
    /// <summary>
    /// Manages the resolution state between two duplicate types.
    /// </summary>
    public class TypeMappingModel : ObjectModel, INotifyPropertyChanged
    {
        private DuplicateTypeModel? _sourceType;
        private DuplicateTypeModel? _targetType;
        private DuplicateItemModel? _sourceFamily;
        private DuplicateItemModel? _targetFamily;
        private RecommendedAction _recommendedAction;
        private ObservableCollection<ParameterDiffRowModel> _parameterResolutions;
        private ObservableCollection<DuplicateTypeModel> _availablePrimaryTypes;
        private string _migrateRenameText = string.Empty;

        /// <summary>
        /// Initializes a new instance of the TypeMappingModel class.
        /// </summary>
        public TypeMappingModel()
        {
            _parameterResolutions = new ObservableCollection<ParameterDiffRowModel>();
            _availablePrimaryTypes = new ObservableCollection<DuplicateTypeModel>();
        }

        /// <summary>
        /// The family container that this source type belongs to.
        /// </summary>
        public DuplicateItemModel? SourceFamily
        {
            get => _sourceFamily;
            set => SetProperty(ref _sourceFamily, value);
        }

        /// <summary>
        /// The primary/target family container that the target type belongs to.
        /// </summary>
        public DuplicateItemModel? TargetFamily
        {
            get => _targetFamily;
            set => SetProperty(ref _targetFamily, value);
        }

        /// <summary>
        /// The duplicate type to be merged or migrated from.
        /// </summary>
        public DuplicateTypeModel? SourceType
        {
            get => _sourceType;
            set => SetProperty(ref _sourceType, value);
        }

        /// <summary>
        /// The target type to merge or migrate into.
        /// </summary>
        public DuplicateTypeModel? TargetType
        {
            get => _targetType;
            set => SetProperty(ref _targetType, value);
        }

        /// <summary>
        /// The action recommendation for this type mapping (Merge, Migrate, Exclude).
        /// </summary>
        public RecommendedAction RecommendedAction
        {
            get => _recommendedAction;
            set => SetProperty(ref _recommendedAction, value);
        }

        /// <summary>
        /// Collection of parameter diff rows and winning value resolutions.
        /// </summary>
        public ObservableCollection<ParameterDiffRowModel> ParameterResolutions
        {
            get => _parameterResolutions;
            set => SetProperty(ref _parameterResolutions, value);
        }

        /// <summary>
        /// Collection of available types in the primary target family.
        /// </summary>
        public ObservableCollection<DuplicateTypeModel> AvailablePrimaryTypes
        {
            get => _availablePrimaryTypes;
            set => SetProperty(ref _availablePrimaryTypes, value);
        }

        /// <summary>
        /// The new name of the type if it is migrated.
        /// </summary>
        public string MigrateRenameText
        {
            get => _migrateRenameText;
            set => SetProperty(ref _migrateRenameText, value);
        }

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
