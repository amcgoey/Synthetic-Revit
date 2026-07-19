using Synthetic.Modules.AutoTagger.Commands;
using Synthetic.Modules.AutoTagger.Models;
using Synthetic.Modules.AutoTagger.ViewModels;
using Synthetic.Modules.AutoTagger.Views;
using Synthetic.Modules.AutoTagger.Repositories;

using System;
using System.Collections.ObjectModel;
using System.Linq;
using Autodesk.Revit.DB;
using Microsoft.Win32;

using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;
using Synthetic.Infrastructure.Serialization;

using Synthetic.Shared.UI;
using Synthetic.Modules.StandardsManagement.ViewModels;
namespace Synthetic.Modules.AutoTagger.ViewModels
{
    /// <summary>
    /// Specifies the actions that can be requested when managing templates.
    /// </summary>
    public enum ManageTemplatesAction
    {
        /// <summary>
        /// No action requested.
        /// </summary>
        None,

        /// <summary>
        /// Request to create a new template.
        /// </summary>
        NewTemplate,

        /// <summary>
        /// Request to edit an existing template.
        /// </summary>
        EditTemplate
    }

    /// <summary>
    /// ViewModel controlling the CRUD interface and state loop for managing templates.
    /// </summary>
    public class ManageTemplatesViewModel : ViewModelBase
    {
        private readonly Document _doc;
        private readonly TemplateStorageRepository _repository;
        private readonly IFileDialogService _fileDialogService;
        private readonly IUserPromptService _userPromptService;
        /// <summary>
        /// Gets or sets the collection of tag templates.
        /// </summary>
        public ObservableCollection<TagTemplate> Templates { get; set; } = new ObservableCollection<TagTemplate>();

        private TagTemplate? _selectedTemplate;
        /// <summary>
        /// Gets or sets the currently selected template.
        /// </summary>
        public TagTemplate? SelectedTemplate
        {
            get => _selectedTemplate;
            set
            {
                if (SetProperty(ref _selectedTemplate, value))
                {
                    System.Windows.Input.CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        /// <summary>
        /// Gets the action requested by the view model.
        /// </summary>
        public ManageTemplatesAction RequestedAction { get; private set; } = ManageTemplatesAction.None;

        /// <summary>
        /// Gets the template selected for editing.
        /// </summary>
        public TagTemplate? TemplateToEdit { get; private set; }

        /// <summary>
        /// Gets the delete template command.
        /// </summary>
        public System.Windows.Input.ICommand DeleteCommand { get; }

        /// <summary>
        /// Gets the export templates to JSON command.
        /// </summary>
        public System.Windows.Input.ICommand ExportJsonCommand { get; }

        /// <summary>
        /// Gets the import templates from JSON command.
        /// </summary>
        public System.Windows.Input.ICommand ImportJsonCommand { get; }

        /// <summary>
        /// Gets the edit selected template command.
        /// </summary>
        public System.Windows.Input.ICommand EditCommand { get; }

        /// <summary>
        /// Gets the command to create a new template.
        /// </summary>
        public System.Windows.Input.ICommand NewTemplateCommand { get; }

        /// <summary>
        /// Gets or sets the action to close the window.
        /// </summary>
        public Action? CloseAction { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="ManageTemplatesViewModel"/> class.
        /// </summary>
        /// <param name="doc">The active Revit document.</param>
        public ManageTemplatesViewModel(Document doc, IFileDialogService? fileDialogService = null, IUserPromptService? userPromptService = null)
        {
            _doc = doc;
            _repository = new TemplateStorageRepository();
            _fileDialogService = fileDialogService ?? new WindowsFileDialogService();
            _userPromptService = userPromptService ?? new WindowsUserPromptService();
            
            LoadTemplates();

            DeleteCommand = new RelayCommand(ExecuteDelete, CanExecuteSelectionBased);
            ExportJsonCommand = new RelayCommand(ExecuteExport, CanExecuteExport);
            ImportJsonCommand = new RelayCommand(ExecuteImport);
            EditCommand = new RelayCommand(ExecuteEdit, CanExecuteSelectionBased);
            NewTemplateCommand = new RelayCommand(ExecuteNewTemplate);
        }

        private void LoadTemplates()
        {
            var dbTemplates = _repository.GetTemplates(_doc);
            Templates = new ObservableCollection<TagTemplate>(dbTemplates);
            OnPropertyChanged(nameof(Templates));
        }

        private bool CanExecuteSelectionBased(object obj) => SelectedTemplate != null;
        private bool CanExecuteExport(object obj) => Templates != null && Templates.Any();

        private void ExecuteDelete(object obj)
        {
            if (SelectedTemplate != null)
            {
                _repository.DeleteTemplate(_doc, SelectedTemplate.Id);
                Templates.Remove(SelectedTemplate);
            }
        }

        private void ExecuteExport(object obj)
        {
            var filePath = _fileDialogService.SaveFileDialog("JSON Files (*.json)|*.json", "Export Templates", "AutoTagger_Templates.json");
            if (!string.IsNullOrEmpty(filePath))
            {
                _repository.ExportToJson(Templates.ToList(), filePath);
                _userPromptService.ShowMessage($"Templates exported to:\n{filePath}", "Export Successful");
            }
        }

        private void ExecuteImport(object obj)
        {
            var filePath = _fileDialogService.OpenFileDialog("JSON Files (*.json)|*.json", "Import Templates", "");
            if (!string.IsNullOrEmpty(filePath))
            {
                try
                {
                    var imported = _repository.ImportFromJson(filePath);
                    if (imported != null && imported.Any())
                    {
                        _repository.SaveAllTemplates(_doc, imported);
                        LoadTemplates();
                        _userPromptService.ShowMessage($"Imported {imported.Count} templates.", "Import Successful");
                    }
                }
                catch (Exception ex)
                {
                    _userPromptService.ShowMessage($"Failed to parse JSON file:\n{ex.Message}", "Import Error");
                }
            }
        }

        private void ExecuteEdit(object obj)
        {
            RequestedAction = ManageTemplatesAction.EditTemplate;
            TemplateToEdit = SelectedTemplate;
            CloseAction?.Invoke();
        }

        private void ExecuteNewTemplate(object obj)
        {
            RequestedAction = ManageTemplatesAction.NewTemplate;
            CloseAction?.Invoke();
        }
    }
}
