using System;
using System.ComponentModel;
using System.Windows.Input;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;

namespace Synthetic.Modules.SettingsDashboard.ViewModels
{
    /// <summary>
    /// Contract defining the properties and behaviors for settings module view models.
    /// </summary>
    public interface ISettingModuleViewModel : INotifyPropertyChanged
    {
        /// <summary>
        /// Gets the name of the settings module for display in the sidebar.
        /// </summary>
        string ModuleName { get; }

        /// <summary>
        /// Gets or sets a value indicating whether firm settings are overridden for the project.
        /// </summary>
        bool IsOverridden { get; set; }

        /// <summary>
        /// Gets a value indicating whether the settings configured for this module are valid.
        /// </summary>
        bool IsValid { get; }

        /// <summary>
        /// Gets a read-only text summary of the current settings.
        /// </summary>
        string SummaryText { get; }

        /// <summary>
        /// Gets the command that launches the module's configuration wizard window.
        /// </summary>
        ICommand? ConfigureCommand { get; }

        /// <summary>
        /// Saves or updates the settings in the project document or deletes them if overrides are disabled.
        /// </summary>
        void Save();

        /// <summary>
        /// Reloads the settings state from the Revit document database.
        /// </summary>
        void Reload();

        /// <summary>
        /// Gets or sets a value indicating whether the settings module has unsaved changes.
        /// </summary>
        bool IsDirty { get; set; }
    }
}
