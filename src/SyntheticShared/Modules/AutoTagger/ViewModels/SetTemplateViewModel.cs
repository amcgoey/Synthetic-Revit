using Synthetic.Modules.AutoTagger.Commands;
using Synthetic.Modules.AutoTagger.Models;
using Synthetic.Modules.AutoTagger.ViewModels;
using Synthetic.Modules.AutoTagger.Views;
using Synthetic.Modules.AutoTagger.Repositories;

using System;
using Autodesk.Revit.DB;

using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;
using Synthetic.Infrastructure.Serialization;

using Synthetic.Shared.UI;
using Synthetic.Modules.MergeDuplicates.Handlers;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.Shared.RevitAPI;
namespace Synthetic.Modules.AutoTagger.ViewModels
{
    /// <summary>
    /// Specifies the matching scope for a tag template (Category-level, Family-level, or Type-level).
    /// </summary>
    public enum TemplateScope
    {
        /// <summary>
        /// Applies to all elements in the category.
        /// </summary>
        Category,

        /// <summary>
        /// Applies to all types under a specific family.
        /// </summary>
        Family,

        /// <summary>
        /// Applies only to a specific element type.
        /// </summary>
        Type
    }

    /// <summary>
    /// ViewModel controlling the logic for capturing and saving a new TagTemplate.
    /// </summary>
    public class SetTemplateViewModel : ViewModelBase
    {
        private readonly Document _doc;
        private readonly FamilyInstance _host;
        private readonly XYZ _localOffset;
        private readonly TagOrientation _tagOrientation;
        private readonly TagTemplate? _editingTemplate;

        private string _templateName = string.Empty;
        /// <summary>
        /// Gets or sets the name of the template.
        /// </summary>
        public string TemplateName 
        { 
            get => _templateName; 
            set 
            { 
                SetProperty(ref _templateName, value); 
                System.Windows.Input.CommandManager.InvalidateRequerySuggested(); 
            }
        }

        private bool _allowOrientationChange;
        /// <summary>
        /// Gets or sets a value indicating whether orientation change is allowed.
        /// </summary>
        public bool AllowOrientationChange
        {
            get => _allowOrientationChange;
            set
            {
                SetProperty(ref _allowOrientationChange, value);
            }
        }

        /// <summary>
        /// Gets the category name of the host element.
        /// </summary>
        public string CategoryName { get; }

        /// <summary>
        /// Gets the family name of the host element.
        /// </summary>
        public string FamilyName { get; }

        /// <summary>
        /// Gets the type name of the host element.
        /// </summary>
        public string TypeName { get; }

        private TemplateScope _selectedScope = TemplateScope.Type;
        /// <summary>
        /// Gets or sets the selected template matching scope.
        /// </summary>
        public TemplateScope SelectedScope 
        { 
            get => _selectedScope; 
            set 
            { 
                if (SetProperty(ref _selectedScope, value))
                {
                    OnPropertyChanged(nameof(IsCategoryScope));
                    OnPropertyChanged(nameof(IsFamilyScope));
                    OnPropertyChanged(nameof(IsTypeScope));
                }
            }
        }

        /// <summary>
        /// Gets or sets a value indicating whether the scope is set to Category.
        /// </summary>
        public bool IsCategoryScope { get => SelectedScope == TemplateScope.Category; set { if(value) SelectedScope = TemplateScope.Category; } }

        /// <summary>
        /// Gets or sets a value indicating whether the scope is set to Family.
        /// </summary>
        public bool IsFamilyScope { get => SelectedScope == TemplateScope.Family; set { if(value) SelectedScope = TemplateScope.Family; } }

        /// <summary>
        /// Gets or sets a value indicating whether the scope is set to Type.
        /// </summary>
        public bool IsTypeScope { get => SelectedScope == TemplateScope.Type; set { if(value) SelectedScope = TemplateScope.Type; } }

        /// <summary>
        /// Gets the Save template command.
        /// </summary>
        public System.Windows.Input.ICommand SaveCommand { get; }

        /// <summary>
        /// Gets the Cancel command.
        /// </summary>
        public System.Windows.Input.ICommand CancelCommand { get; }
        
        /// <summary>
        /// Delegate assigned by the View to allow the ViewModel to request closure without violating MVVM.
        /// </summary>
        public Action? CloseAction { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="SetTemplateViewModel"/> class.
        /// </summary>
        /// <param name="doc">The active Revit document.</param>
        /// <param name="host">The host family instance.</param>
        /// <param name="localOffset">The tag offset coordinates.</param>
        /// <param name="tagOrientation">The tag orientation.</param>
        /// <param name="editingTemplate">Optional existing template being edited.</param>
        public SetTemplateViewModel(Document doc, FamilyInstance host, XYZ localOffset, TagOrientation tagOrientation, TagTemplate? editingTemplate = null)
        {
            _doc = doc;
            _host = host;
            _localOffset = localOffset;
            _tagOrientation = tagOrientation;
            _editingTemplate = editingTemplate;

            CategoryName = host.Category?.Name ?? "Unknown Category";
            FamilyName = host.Symbol?.FamilyName ?? "Unknown Family";
            TypeName = host.Name;
            
            SaveCommand = new RelayCommand(ExecuteSave, CanExecuteSave);
            CancelCommand = new RelayCommand(ExecuteCancel);

            if (_editingTemplate != null)
            {
                TemplateName = _editingTemplate.TemplateName;
                AllowOrientationChange = _editingTemplate.AllowOrientationChange;
                if (!string.IsNullOrEmpty(_editingTemplate.TargetType))
                    SelectedScope = TemplateScope.Type;
                else if (!string.IsNullOrEmpty(_editingTemplate.TargetFamily))
                    SelectedScope = TemplateScope.Family;
                else
                    SelectedScope = TemplateScope.Category;
            }
            else
            {
                TemplateName = $"{FamilyName} - Auto Tag";
                AllowOrientationChange = false;
                SelectedScope = TemplateScope.Type;
            }
        }

        private bool CanExecuteSave(object obj) => !string.IsNullOrWhiteSpace(TemplateName);

        private void ExecuteSave(object obj)
        {
            var template = _editingTemplate ?? new TagTemplate();
            
            template.TemplateName = this.TemplateName;
            template.TargetCategory = this.CategoryName;
            template.TargetFamily = this.SelectedScope >= TemplateScope.Family ? this.FamilyName : null;
            template.TargetType = this.SelectedScope == TemplateScope.Type ? this.TypeName : null;
            template.OffsetX = _localOffset.X;
            template.OffsetY = _localOffset.Y;
            template.OffsetZ = _localOffset.Z;
            template.Orientation = _tagOrientation;
            template.AllowOrientationChange = this.AllowOrientationChange;
            
            if (_host != null)
            {
                template.HostHandX = _host.HandOrientation.X;
                template.HostHandY = _host.HandOrientation.Y;
                template.HostHandZ = _host.HandOrientation.Z;
            }

            var repo = new TemplateStorageRepository();
            repo.SaveTemplate(_doc, template);

            CloseAction?.Invoke();
        }

        private void ExecuteCancel(object obj) => CloseAction?.Invoke();
    }
}
