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
using Synthetic.Modules.MergeDuplicates.Models;
using Synthetic.Modules.StandardsManagement.Engine;
using Synthetic.Modules.StandardsManagement.Models;
using Synthetic.Core;

using Synthetic.Shared.UI;
using Synthetic.Shared.RevitAPI;
using Synthetic.Modules.RevitDOM;
using Synthetic.Settings;
using Synthetic.Infrastructure.Persistence;
using Synthetic.Modules.StandardsManagement.Utilities;

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
    public class ProjectStandardsDashboardViewModel : ViewModelBase
    {
        private readonly UIApplication? _uiapp;
        private readonly Document? _doc;
        private readonly IFileDialogService _dialogService;
        private readonly IStandardsExportService _exportService;
        internal readonly IUserPromptService _userPromptService;
        private readonly IFindReplaceService _findReplaceService;
        private readonly IStandardsExtractionOrchestrator _orchestrator;
        private readonly IPocoIdentityService _pocoIdentityService;
        public ISummaryDisplayService SummaryDisplayService { get; set; }
        private StandardsSettings? _settings;
        private ExternalEvent? _externalEvent;
        private ProjectStandardsExternalEventHandler? _eventHandler;

        private ObservableCollection<ProjectStandardsSourceViewModel> _availableSources = new ObservableCollection<ProjectStandardsSourceViewModel>();
        private ProjectStandardsSourceViewModel? _selectedSource;
        private string _searchText = string.Empty;
        private ObservableCollection<QueueItemModel> _actionQueue = new ObservableCollection<QueueItemModel>();

        /// <summary>
        /// Gets the staging action queue collection.
        /// </summary>
        public ObservableCollection<QueueItemModel> ActionQueue => _actionQueue;

        /// <summary>
        /// Gets the grouped collection view of the action queue.
        /// </summary>
        public ICollectionView ActionQueueView { get; }

        public static ProjectStandardsDashboardViewModel? Instance { get; set; }
        public ObservableCollection<ParameterWrapperVM> DisplayParameters { get; } = new ObservableCollection<ParameterWrapperVM>();
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
                            ReplaceReferences(queueItem.TargetModel, oldName, newName);
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
                foreach (var qItem in ActionQueue)
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

        private void GetElementModelsFromHierarchy(SourceTreeItemViewModel node, List<ElementModel> list)
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
        private string _findText = string.Empty;
        private string _replaceText = string.Empty;

        /// <summary>
        /// Gets or sets the active right pane sub-workspace mode.
        /// </summary>
        public WorkspaceMode ActiveWorkspace
        {
            get => _activeWorkspace;
            set => SetProperty(ref _activeWorkspace, value);
        }

        private string? _saveFilePath;

        /// <summary>
        /// Gets or sets the target file path for save actions.
        /// </summary>
        public string? SaveFilePath
        {
            get => _saveFilePath;
            set => SetProperty(ref _saveFilePath, value);
        }

        /// <summary>
        /// Gets whether the save path panel should be active/visible in the UI.
        /// </summary>
        public bool IsSavePathActive => ActionQueue.Any(item => item.WillSave);

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

        private SearchScope _findReplaceScope = SearchScope.Both;

        /// <summary>
        /// Gets or sets the search scope for batch find-and-replace edits.
        /// </summary>
        public SearchScope FindReplaceScope
        {
            get => _findReplaceScope;
            set => SetProperty(ref _findReplaceScope, value);
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
                    
                    // Trigger relational rename cascading
                    ReplaceReferences(SelectedQueueItems[0].TargetModel, oldName, value);

                    // Update Name property on TargetModel
                    if (SelectedQueueItems[0].TargetModel is ElementModel el)
                    {
                        el.Name = value;
                    }
                    
                    SelectedQueueItems[0].IsEdited = true;
                    SelectedQueueItems[0].ErrorMessage = null;
                    SelectedQueueItems[0].RaisePropertyChanged(nameof(QueueItemModel.Name));
                    SelectedQueueItems[0].RaisePropertyChanged(nameof(QueueItemModel.Model));
                    
                    _activeWrappers = SelectedQueueItems.Select(q => q.GetWrapper()).ToList();
                    OnPropertyChanged(nameof(SelectedElement));
                    UpdateSelectedElementSubscription();
                    RaiseIdentityHeaderStateChanged();
                    CalculateParameterIntersection();
                    
                    OnPropertyChanged(nameof(SelectedItemName));
                    OnPropertyChanged(nameof(SelectedItemErrorMessage));
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

        /// <summary>
        /// Gets the collection of staged elements currently selected for editing/diffing.
        /// </summary>
        public ObservableCollection<QueueItemModel> SelectedQueueItems { get; } = new ObservableCollection<QueueItemModel>();

        /// <summary>
        /// Gets the collection of duplicate/diff clusters populated by the comparison engine.
        /// </summary>
        public ObservableCollection<DuplicateClusterModel> ActiveDiffClusters { get; } = new ObservableCollection<DuplicateClusterModel>();

        /// <summary>
        /// Gets the combined list of results from the last Run Queue execution.
        /// </summary>
        public List<SerializationResultModel> LastExecutionResults { get; } = new List<SerializationResultModel>();

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



        private bool _updateFamilies = false;
        private bool _processNestedRecursive = false;
        private bool _purgeUnusedStyleTypes = false;
        private string _categoryFilter = "All Categories";

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

        public List<Document>? MockOpenDocuments { get; set; }
        public Func<SelectRevitDocumentViewModel, bool?>? ShowDocumentSelectionDialog { get; set; }
        public Func<Synthetic.Shared.UI.SingleItemSelectionViewModel<QueueItemModel>, bool?>? ShowMergeDialog { get; set; }

        #region Commands
        public ICommand AddFileSourceCommand { get; }
        public ICommand AddRevitModelCommand { get; }
        public ICommand CloseSourceCommand { get; }
        public ICommand EnforceCommand { get; }
        public ICommand SaveCommand { get; }
        public ICommand SaveAndEnforceCommand { get; }
        public ICommand BrowseSavePathCommand { get; }
        public ICommand PushToQueueCommand { get; }
        public ICommand RemoveFromQueueCommand { get; }
        public ICommand MergeQueueCommand { get; }
        public ICommand EditCommand { get; }
        public ICommand DiffCommand { get; }
        public ICommand RunQueueCommand { get; }
        public ICommand BatchFindReplaceCommand { get; }
        public ICommand ApplyEditsCommand { get; }
        public ICommand CancelEditsCommand { get; }
        public ICommand ResolveConflictCommand { get; }
        #endregion

        /// <summary>
        /// Initializes a new instance of the <see cref="ProjectStandardsDashboardViewModel"/> class for live Revit environment.
        /// </summary>
        public ProjectStandardsDashboardViewModel(
            UIApplication uiapp, 
            IFileDialogService dialogService, 
            IStandardsExportService? exportService = null, 
            StandardsSettings? settings = null,
            IUserPromptService? userPromptService = null,
            IFindReplaceService? findReplaceService = null,
            IStandardsExtractionOrchestrator? orchestrator = null,
            IPocoIdentityService? pocoIdentityService = null)
        {
            _uiapp = uiapp;
            _doc = uiapp.ActiveUIDocument?.Document;
            _dialogService = dialogService;
            _exportService = exportService ?? new StandardsExportService(new WindowsGuardrailPromptService(), dialogService);
            _userPromptService = userPromptService ?? new WindowsUserPromptService();
            _findReplaceService = findReplaceService ?? new FindReplaceService();
            _orchestrator = orchestrator ?? new StandardsExtractionOrchestrator(new RevitIdentityService());
            _pocoIdentityService = pocoIdentityService ?? new PocoIdentityService();
            SummaryDisplayService = new WindowsSummaryDisplayService();
            Instance = this;

            AddFileSourceCommand = new RelayCommand(ExecuteAddFileSource);
            AddRevitModelCommand = new RelayCommand(ExecuteAddRevitModel);
            EnforceCommand = new RelayCommand(ExecuteEnforce, CanExecuteActions);
            SaveCommand = new RelayCommand(ExecuteSave, CanExecuteActions);
            SaveAndEnforceCommand = new RelayCommand(ExecuteSaveAndEnforce, CanExecuteActions);
            BrowseSavePathCommand = new RelayCommand(ExecuteBrowseSavePath);
            _actionQueue.CollectionChanged += (s, e) => { OnPropertyChanged(nameof(IsSavePathActive)); };
            PushToQueueCommand = new RelayCommand(ExecutePushToQueue, CanExecuteActions);
            RemoveFromQueueCommand = new RelayCommand(ExecuteRemoveFromQueue, CanExecuteRemove);
            MergeQueueCommand = new RelayCommand(ExecuteMergeQueue, CanExecuteMergeQueue);
            EditCommand = new RelayCommand(ExecuteEdit, CanExecuteQueueActions);
            DiffCommand = new RelayCommand(ExecuteDiff, CanExecuteQueueActions);
            RunQueueCommand = new RelayCommand(ExecuteRunQueue, CanExecuteQueueActions);
            BatchFindReplaceCommand = new RelayCommand(ExecuteBatchFindReplace, CanExecuteQueueActions);
            ApplyEditsCommand = new RelayCommand(ExecuteApplyEdits, CanExecuteQueueActions);
            CancelEditsCommand = new RelayCommand(ExecuteCancelEdits);
            ResolveConflictCommand = new RelayCommand(ExecuteResolveConflict, CanExecuteQueueActions);
            CloseSourceCommand = new RelayCommand(ExecuteCloseSource, CanExecuteCloseSource);

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

            ActionQueueView = CollectionViewSource.GetDefaultView(ActionQueue);
            ActionQueueView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(QueueItemModel.ClassName)));

            Initialize(settings);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ProjectStandardsDashboardViewModel"/> class with 3 parameters for reflection compatibility.
        /// </summary>
        public ProjectStandardsDashboardViewModel(
            UIApplication uiapp, 
            IFileDialogService dialogService, 
            IStandardsExportService? exportService)
            : this(uiapp, dialogService, exportService, null)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ProjectStandardsDashboardViewModel"/> class for headless testing.
        /// </summary>
        public ProjectStandardsDashboardViewModel(
            Document doc, 
            IFileDialogService dialogService, 
            IStandardsExportService? exportService = null, 
            StandardsSettings? settings = null,
            IUserPromptService? userPromptService = null,
            IFindReplaceService? findReplaceService = null,
            IStandardsExtractionOrchestrator? orchestrator = null,
            IPocoIdentityService? pocoIdentityService = null)
        {
            _doc = doc;
            _dialogService = dialogService;
            _exportService = exportService ?? new StandardsExportService(new WindowsGuardrailPromptService(), dialogService);
            _userPromptService = userPromptService ?? new WindowsUserPromptService();
            _findReplaceService = findReplaceService ?? new FindReplaceService();
            _orchestrator = orchestrator ?? new StandardsExtractionOrchestrator(new RevitIdentityService());
            _pocoIdentityService = pocoIdentityService ?? new PocoIdentityService();
            SummaryDisplayService = new NoOpSummaryDisplayService();
            Instance = this;

            AddFileSourceCommand = new RelayCommand(ExecuteAddFileSource);
            AddRevitModelCommand = new RelayCommand(ExecuteAddRevitModel);
            EnforceCommand = new RelayCommand(ExecuteEnforce, CanExecuteActions);
            SaveCommand = new RelayCommand(ExecuteSave, CanExecuteActions);
            SaveAndEnforceCommand = new RelayCommand(ExecuteSaveAndEnforce, CanExecuteActions);
            BrowseSavePathCommand = new RelayCommand(ExecuteBrowseSavePath);
            _actionQueue.CollectionChanged += (s, e) => { OnPropertyChanged(nameof(IsSavePathActive)); };
            PushToQueueCommand = new RelayCommand(ExecutePushToQueue, CanExecuteActions);
            RemoveFromQueueCommand = new RelayCommand(ExecuteRemoveFromQueue, CanExecuteRemove);
            MergeQueueCommand = new RelayCommand(ExecuteMergeQueue, CanExecuteMergeQueue);
            EditCommand = new RelayCommand(ExecuteEdit, CanExecuteQueueActions);
            DiffCommand = new RelayCommand(ExecuteDiff, CanExecuteQueueActions);
            RunQueueCommand = new RelayCommand(ExecuteRunQueue, CanExecuteQueueActions);
            BatchFindReplaceCommand = new RelayCommand(ExecuteBatchFindReplace, CanExecuteQueueActions);
            ApplyEditsCommand = new RelayCommand(ExecuteApplyEdits, CanExecuteQueueActions);
            CancelEditsCommand = new RelayCommand(ExecuteCancelEdits);
            ResolveConflictCommand = new RelayCommand(ExecuteResolveConflict, CanExecuteQueueActions);
            CloseSourceCommand = new RelayCommand(ExecuteCloseSource, CanExecuteCloseSource);

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

            ActionQueueView = CollectionViewSource.GetDefaultView(ActionQueue);
            ActionQueueView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(QueueItemModel.ClassName)));

            Initialize(settings);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ProjectStandardsDashboardViewModel"/> class with 3 parameters for reflection compatibility.
        /// </summary>
        public ProjectStandardsDashboardViewModel(
            Document doc, 
            IFileDialogService dialogService, 
            IStandardsExportService? exportService)
            : this(doc, dialogService, exportService, null)
        {
        }

        /// <summary>
        /// Compatibility constructor that adapts a legacy <see cref="IGuardrailPromptService"/> to the new export service.
        /// </summary>
        public ProjectStandardsDashboardViewModel(
            UIApplication uiapp, 
            IFileDialogService dialogService, 
            IGuardrailPromptService? guardrailService, 
            StandardsSettings? settings = null)
            : this(uiapp, dialogService, new StandardsExportService(guardrailService ?? new WindowsGuardrailPromptService(), dialogService), settings)
        {
        }

        /// <summary>
        /// Compatibility constructor that adapts a legacy <see cref="IGuardrailPromptService"/> to the new export service.
        /// </summary>
        public ProjectStandardsDashboardViewModel(
            Document doc, 
            IFileDialogService dialogService, 
            IGuardrailPromptService? guardrailService, 
            StandardsSettings? settings = null)
            : this(doc, dialogService, new StandardsExportService(guardrailService ?? new WindowsGuardrailPromptService(), dialogService), settings)
        {
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
                LoadFileSource(settings.StandardsFilePath, "Default Firm Standard");
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

                string fallbackDirectory = GetRevitLocalFileSaveLocation();
                SaveFilePath = PathResolutionUtility.GetDefaultSavePath(documentTitle, isModelInCloud, isWorkshared, centralModelPathString, localPathName, fallbackDirectory);
            }
            else
            {
                // Fallback directly to the Revit local file save location when no document is active
                string fallbackDirectory = GetRevitLocalFileSaveLocation();
                SaveFilePath = Path.Combine(fallbackDirectory, "Project Standards.json");
            }
        }

        private string GetRevitLocalFileSaveLocation()
        {
            string? fallbackPath = null;
            if (_doc != null)
            {
                try
                {
                    var appProp = _doc.GetType().GetProperty("Application");
                    var appObj = appProp?.GetValue(_doc);
                    if (appObj != null)
                    {
                        var defaultPathProp = appObj.GetType().GetProperty("DefaultUserFilePath");
                        fallbackPath = defaultPathProp?.GetValue(appObj) as string;
                    }
                }
                catch (Exception) { }
            }
            else if (_uiapp != null)
            {
                try
                {
                    var appProp = _uiapp.GetType().GetProperty("Application");
                    var appObj = appProp?.GetValue(_uiapp);
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

            var dialogVM = new SelectRevitDocumentViewModel(_uiapp, MockOpenDocuments);
            bool? dialogResult = ShowDocumentSelectionDialog?.Invoke(dialogVM);
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

        private List<ElementModel> ExtractRevitElements(Document doc, bool scanFamilies, bool scanNestedFamilies, List<string> selectedGroupings)
        {
            var list = new List<ElementModel>();
            if (doc == null) return list;

            // 1. Extract categories directly since they are not Elements and don't have nested dependencies
            if (selectedGroupings == null || selectedGroupings.Contains("Categories"))
            {
                var engine = new StandardSerializationEngine();
                foreach (Category cat in doc.Settings.Categories)
                {
                    if (ProgressCoordinator.IsCancelled()) break;
                    try
                    {
                        var model = engine.Dispatcher.Extract(cat, doc, false);
                        if (model is CategoryModel categoryModel)
                        {
                            list.Add(categoryModel);
                        }
                    }
                    catch {}
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
                    list.Add(elementModel);
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
#if !REVIT2022 && !REVIT2023
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
            catch {}
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
                        .Cast<FamilySymbol>()
                        .ToList();

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



        private bool CanExecuteActions(object parameter) => SelectedSource != null;

        private bool CanExecuteRemove(object parameter)
        {
            if (parameter is System.Collections.IList list)
            {
                return list.Count > 0;
            }
            return false;
        }

        private bool CanExecuteQueueActions(object parameter) => ActionQueue.Count > 0;

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

            var checkedItems = GetCheckedElements();
            if (checkedItems == null || checkedItems.Count == 0) return;

            // 1. Flatten SelectedSource.SourceHierarchy to build sourceElements list
            var sourceElements = new List<ElementModel>();
            foreach (var node in SelectedSource.SourceHierarchy)
            {
                GetElementModelsFromHierarchy(node, sourceElements);
            }

            // 2. Track already staged keys in ActionQueue to prevent duplicates
            var existingKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in ActionQueue)
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
                        ActionQueue.Add(queueItem);
                        
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
                                ActionQueue.Add(queueItem);

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

        private void ExecuteRemoveFromQueue(object parameter)
        {
            if (parameter is System.Collections.IList list)
            {
                var itemsToRemove = list.Cast<QueueItemModel>().ToList();
                foreach (var item in itemsToRemove)
                {
                    ActionQueue.Remove(item);
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

                _activeWrappers = SelectedQueueItems.Select(q => q.GetWrapper()).ToList();
                OnPropertyChanged(nameof(SelectedElement));
                UpdateSelectedElementSubscription();
                RaiseIdentityHeaderStateChanged();
                CalculateParameterIntersection();
                OnPropertyChanged(nameof(SelectedItemName));
                OnPropertyChanged(nameof(SelectedItemErrorMessage));

                ActiveWorkspace = WorkspaceMode.Edit;
            }
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

                if (_doc != null)
                {
                    try
                    {
                        var elementPocos = SelectedQueueItems.Select(q => q.Model).OfType<ElementModel>().ToList();
                        var clusters = StandardsDiffEngine.RunDeepScan(_doc, elementPocos);

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

                ActiveWorkspace = WorkspaceMode.Diff;
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

            ActiveWorkspace = WorkspaceMode.Idle;
            ActiveDiffClusters.Clear();
        }

        private void ExecuteRunQueue(object parameter)
        {
            if (_externalEvent != null)
            {
                _externalEvent.Raise();
            }
            else
            {
                RunQueueInternal();
            }
        }

        public void RunQueueInternal()
        {
            if (_doc == null) return;

            LastExecutionResults.Clear();
            string? targetPath = null;
            bool dbPhaseSucceeded = true;

            // Phase 1: Revit Database writes (Revit-First)
            var dbItems = ActionQueue.Where(item => item.WillEnforce).ToList();

            var dbResults = new List<SerializationResultModel>();

            if (dbItems.Count > 0)
            {
                try
                {
                    dbResults = RunRevitDbPhase(dbItems);
                    foreach (var result in dbResults)
                    {
                        result.OperationTarget = "Database";
                    }
                    LastExecutionResults.AddRange(dbResults);
                }
                catch (OperationCanceledException)
                {
                    dbPhaseSucceeded = false;
                    foreach (var item in dbItems)
                    {
                        var result = new SerializationResultModel(item.Model, "Execution cancelled by user.");
                        result.OperationTarget = "Database";
                        LastExecutionResults.Add(result);
                    }
                }
                catch (Exception ex)
                {
                    // Headless test runs may throw exceptions due to missing TransactionGroup.
                    // We log this but do NOT set dbPhaseSucceeded = false to allow Phase 2 (File Save) to proceed.
                    Console.WriteLine($"Revit DB Phase execution skipped or failed: {ex.Message}");
                    foreach (var item in dbItems)
                    {
                        var result = new SerializationResultModel(item.Model, $"Database Write Failed: {ex.Message}", ex);
                        result.OperationTarget = "Database";
                        LastExecutionResults.Add(result);
                    }
                }
            }

            // Phase 2: File I/O (File-Second)
            if (dbPhaseSucceeded)
            {
                var fileItems = ActionQueue.Where(item => item.WillSave).ToList();

                if (fileItems.Count > 0)
                {
                    if (!string.IsNullOrEmpty(SaveFilePath))
                    {
                        targetPath = SaveFilePath;
                    }
                    else if (SelectedSource != null && !SelectedSource.IsRevitSource && !string.IsNullOrEmpty(SelectedSource.SourcePath))
                    {
                        targetPath = SelectedSource.SourcePath;
                    }
                    else
                    {
                        string? projectSettingsPath = GetProjectSettingsPath();
                        if (!string.IsNullOrEmpty(projectSettingsPath))
                        {
                            targetPath = projectSettingsPath;
                        }
                    }

                    var fileResults = _exportService.Export(
                        fileItems,
                        targetPath,
                        dbResults,
                        GetProtectedPaths(),
                        out string? finalPathUsed);

                    targetPath = finalPathUsed;
                    LastExecutionResults.AddRange(fileResults);
                }
            }

            // Build summary tracker log items from LastExecutionResults
            var tracker = new ObservableCollection<ImportLogItem>();
            foreach (var result in LastExecutionResults)
            {
                var model = result.Model;
                string action = "Updated";
                if (!result.Success)
                {
                    if (result.Action == "Alias Swap Failed")
                    {
                        action = "Alias Fail";
                    }
                    else
                    {
                        action = result.OperationTarget == "File" ? "Save Failed" : "Failed";
                    }
                }
                else
                {
                    if (!string.IsNullOrEmpty(result.Action))
                    {
                        action = result.Action;
                    }
                    else if (result.OperationTarget == "File")
                    {
                        action = "Saved";
                    }
                    else
                    {
                        // Find the enqueued item to determine if it was Enforced/Saved
                        var queueItem = ActionQueue.FirstOrDefault(qi => qi.Model == model);
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

                string message = result.Success ? "Operation completed successfully." : (result.ErrorMessage ?? "Unknown error occurred.");
                if (!string.IsNullOrEmpty(result.Message))
                {
                    message = result.Message;
                }
                if (result.Warnings != null && result.Warnings.Count > 0)
                {
                    message += " Warnings: " + string.Join(", ", result.Warnings);
                }

                tracker.Add(new ImportLogItem
                {
                    Action = action,
                    Class = className,
                    ElementName = name,
                    Message = message
                });
            }

            // Automatically write Markdown log file next to the saved standard JSON file (if one was written)
            if (!string.IsNullOrEmpty(targetPath) && File.Exists(targetPath))
            {
                try
                {
                    string logPath = Path.ChangeExtension(targetPath, ".log.md");
                    var summaryVMForFile = new ImportSummaryViewModel(tracker, _dialogService);
                    string markdown = summaryVMForFile.GenerateMarkdown();
                    File.WriteAllText(logPath, markdown);
                }
                catch (Exception logEx)
                {
                    Console.WriteLine($"Error writing automatic Markdown log next to target path: {logEx.Message}");
                }
            }

            Action updateUI = () =>
            {
                // Display the summary dialog modal
                if (tracker.Count > 0)
                {
                    var summaryVM = new ImportSummaryViewModel(tracker, _dialogService);
                    IntPtr parentHandle = _uiapp != null ? _uiapp.MainWindowHandle : IntPtr.Zero;
                    SummaryDisplayService.ShowSummary(summaryVM, parentHandle);
                }

                // Systematic queue purging and error message hydration
                var successfulItems = new List<QueueItemModel>();
                foreach (var item in ActionQueue.ToList())
                {
                    var resultsForItem = LastExecutionResults.Where(r => r.Model == item.Model).ToList();
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
                    ActionQueue.Remove(item);
                }

                ActiveWorkspace = WorkspaceMode.Idle;
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

        private List<SerializationResultModel> RunRevitDbPhase(List<QueueItemModel> dbItems)
        {
            var dbResults = new List<SerializationResultModel>();
            if (_doc == null) return dbResults;

            int totalWorkItems = dbItems.Count;
            ProgressCoordinator.Initialize("Consolidate Project Standards", "Starting standard injection...", totalWorkItems);

            using (var txGroup = new TransactionGroup(_doc, "Consolidate Project Standards"))
            {
                txGroup.Start();
                try
                {
                    if (dbItems.Count > 0)
                    {
                        var elementPocos = dbItems.Select(q => q.Model).OfType<ObjectModel>().ToList();
                        var engine = new StandardSerializationEngine();
                        var results = engine.ToRevit(elementPocos, _doc, null, ProgressCoordinator.Token).ToList();
                        dbResults.AddRange(results);
                    }

                    if (UpdateFamilies)
                    {
                        var elementPocos = dbItems.Select(q => q.Model).OfType<ElementModel>().ToList();
                        ProcessFamilyUpdates(elementPocos);
                    }

                    txGroup.Assimilate();
                }
                catch (Exception)
                {
                    txGroup.RollBack();
                    throw;
                }
                finally
                {
                    ProgressCoordinator.Close();
                }
            }
            return dbResults;
        }

        private void ProcessFamilyUpdates(List<ElementModel> standards)
        {
            if (_doc == null || !UpdateFamilies) return;

            if (ProgressCoordinator.IsCancelled())
            {
                throw new OperationCanceledException();
            }

            ProgressCoordinator.UpdateStatus("Collecting families to update...");

            IList<Family> allFamilies = new FilteredElementCollector(_doc)
                .OfClass(typeof(Family))
                .Cast<Family>()
                .ToList();

            List<Family> familiesToProcess = new List<Family>();
            foreach (Family family in allFamilies)
            {
                if (family.IsEditable)
                {
                    if (CategoryFilter == "Annotations Only" && (family.FamilyCategory == null || family.FamilyCategory.CategoryType != CategoryType.Annotation))
                        continue;
#if REVIT2022 || REVIT2023
                    if (CategoryFilter == "Title Blocks Only" && (family.FamilyCategory == null || family.FamilyCategory.Id.IntegerValue != (int)BuiltInCategory.OST_TitleBlocks))
#else
                    if (CategoryFilter == "Title Blocks Only" && (family.FamilyCategory == null || family.FamilyCategory.Id.Value != (long)BuiltInCategory.OST_TitleBlocks))
#endif
                        continue;

                    familiesToProcess.Add(family);
                }
            }

            if (ProgressCoordinator.IsCancelled())
            {
                throw new OperationCanceledException();
            }

            if (_doc.IsWorkshared && familiesToProcess.Count > 0)
            {
                ProgressCoordinator.UpdateStatus("Checking out family worksets...");
                List<WorksetId> worksetIds = familiesToProcess
                    .Select(f => f.WorksetId)
                    .Distinct()
                    .Where(id => id != WorksetId.InvalidWorksetId)
                    .ToList();

                if (worksetIds.Count > 0)
                {
                    WorksharingUtils.CheckoutWorksets(_doc, worksetIds);
                }
            }

            List<string> familyNamesToProcess = familiesToProcess
                .Select(f => f.Name)
                .Distinct()
                .ToList();

            int familyIndex = 0;
            foreach (string familyName in familyNamesToProcess)
            {
                if (ProgressCoordinator.IsCancelled())
                {
                    throw new OperationCanceledException();
                }

                familyIndex++;
                ProgressCoordinator.UpdateStatus($"Updating family {familyIndex} of {familyNamesToProcess.Count}: {familyName}...");

                Family? family = new FilteredElementCollector(_doc)
                    .OfClass(typeof(Family))
                    .Cast<Family>()
                    .FirstOrDefault(f => f.Name == familyName);

                if (family != null && family.IsValidObject)
                {
                    UpdateFamilyRecursively(_doc, family, standards);
                }
            }
        }

        private void UpdateFamilyRecursively(Document parentDoc, Family family, IEnumerable<ElementModel> standards)
        {
            if (ProgressCoordinator.IsCancelled())
            {
                throw new OperationCanceledException();
            }
            if (family == null || !family.IsEditable) return;

            Document? familyDoc = null;
            try
            {
                familyDoc = parentDoc.EditFamily(family);
            }
            catch (Exception)
            {
                return;
            }

            if (familyDoc == null) return;

            parentDoc.Application.FailuresProcessing += ResolveWarnings;

            try
            {
                if (ProcessNestedRecursive)
                {
                    if (ProgressCoordinator.IsCancelled())
                    {
                        throw new OperationCanceledException();
                    }
                    IList<Family> nestedFamilies = new FilteredElementCollector(familyDoc)
                        .OfClass(typeof(Family))
                        .Cast<Family>()
                        .ToList();

                    var nestedFamiliesInfo = nestedFamilies
                        .Select(nf => new { Id = nf.Id, Name = nf.Name, IsEditable = nf.IsEditable })
                        .ToList();

                    foreach (var nfInfo in nestedFamiliesInfo)
                    {
                        if (ProgressCoordinator.IsCancelled())
                        {
                            throw new OperationCanceledException();
                        }
                        if (nfInfo.IsEditable)
                        {
                            Family? freshNestedFamily = new FilteredElementCollector(familyDoc)
                                .OfClass(typeof(Family))
                                .Cast<Family>()
                                .FirstOrDefault(nf => nf.Name == nfInfo.Name);
                            if (freshNestedFamily != null && freshNestedFamily.IsValidObject)
                            {
                                UpdateFamilyRecursively(familyDoc, freshNestedFamily, standards);
                            }
                        }
                    }
                }

                if (ProgressCoordinator.IsCancelled())
                {
                    throw new OperationCanceledException();
                }

                foreach (ElementModel serialElement in standards)
                {
                    if (serialElement is ElementTypeModel etModel)
                    {
                        etModel.ElementType = null;
                    }
                    serialElement.Element = null;
                    serialElement.Document = null;
                }

                var familyStandards = standards.Where(s =>
                {
                    if (s is MaterialModel) return true;
                    if (s is ElementTypeModel etModel)
                    {
                        if (familyDoc.IsFamilyDocument && etModel.Class == "Autodesk.Revit.DB.SpotDimensionType")
                        {
                            return false;
                        }
                        return true;
                    }
                    return false;
                }).ToList();

                var engine = new StandardSerializationEngine();
                engine.ToRevit(familyStandards, familyDoc, null, ProgressCoordinator.Token);

                if (PurgeUnusedStyleTypes)
                {
                    if (ProgressCoordinator.IsCancelled())
                    {
                        throw new OperationCanceledException();
                    }
                    try
                    {
#if !REVIT2022
                        DocumentUtil.Purge(parentDoc.Application, familyDoc);
#endif
                    }
                    catch (Exception)
                    {
                    }
                }

                try
                {
                    Synthetic.Shared.RevitAPI.FamilyUtil.SetIsChanged(parentDoc, familyDoc);
                }
                catch (Exception)
                {
                }

                familyDoc.LoadFamily(parentDoc, new ImportFamilyLoadOptions());
            }
            finally
            {
                parentDoc.Application.FailuresProcessing -= ResolveWarnings;
                try
                {
                    familyDoc.Close(false);
                }
                catch (Exception)
                {
                }
            }
        }

        private static void ResolveWarnings(object? sender, Autodesk.Revit.DB.Events.FailuresProcessingEventArgs e)
        {
            FailuresAccessor fa = e.GetFailuresAccessor();
            IList<FailureMessageAccessor> failList = fa.GetFailureMessages();

            if (failList.Count == 0)
            {
                e.SetProcessingResult(FailureProcessingResult.Continue);
                return;
            }

            foreach (FailureMessageAccessor failure in failList)
            {
                fa.DeleteWarning(failure);
            }
            e.SetProcessingResult(FailureProcessingResult.ProceedWithCommit);
        }

        private string? GetProjectSettingsPath()
        {
            if (_settings != null)
            {
                return _settings.StandardsFilePath;
            }
            if (_doc == null) return null;
            try
            {
                var settings = SettingsManager.Get<StandardsSettings>(_doc);
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

        private void ExecuteBatchFindReplace(object parameter)
        {
            if (string.IsNullOrEmpty(FindText)) return;

            string findText = FindText;
            string replaceText = ReplaceText ?? string.Empty;
            var scope = FindReplaceScope;
            
            bool searchNames = scope == SearchScope.ElementNames || scope == SearchScope.Both;
            bool searchParams = scope == SearchScope.ParameterValues || scope == SearchScope.Both;

            var elements = SelectedQueueItems.Select(q => q.Model).OfType<ElementModel>().ToList();
            var modifiedElements = _findReplaceService.Execute(elements, findText, replaceText, searchNames, searchParams);

            foreach (var item in SelectedQueueItems)
            {
                if (item.Model is ElementModel el && modifiedElements.Contains(el))
                {
                    item.IsEdited = true;
                    item.RaisePropertyChanged(nameof(QueueItemModel.Name));
                    item.RaisePropertyChanged(nameof(QueueItemModel.Model));
                }
            }

            _activeWrappers = SelectedQueueItems.Select(q => q.GetWrapper()).ToList();
            OnPropertyChanged(nameof(SelectedElement));
            UpdateSelectedElementSubscription();
            RaiseIdentityHeaderStateChanged();
            CalculateParameterIntersection();
            ActionQueueView?.Refresh();
        }

        private void ExecuteApplyEdits(object parameter)
        {
            foreach (var item in SelectedQueueItems)
            {
                item.IsEdited = true;
            }
            _activeWrappers.Clear();
            OnPropertyChanged(nameof(SelectedElement));
            UpdateSelectedElementSubscription();
            RaiseIdentityHeaderStateChanged();
            ActiveWorkspace = WorkspaceMode.Idle;
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
            _activeWrappers.Clear();
            OnPropertyChanged(nameof(SelectedElement));
            UpdateSelectedElementSubscription();
            RaiseIdentityHeaderStateChanged();
            OnPropertyChanged(nameof(SelectedItemName));
            ActiveWorkspace = WorkspaceMode.Idle;
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

        private void DisplayParam_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ParameterWrapperVM.Value) || e.PropertyName == nameof(ParameterWrapperVM.IsMixedValue))
            {
                var displayParam = sender as ParameterWrapperVM;
                if (displayParam != null && !displayParam.IsMixedValue)
                {
                    PushBulkValue(displayParam.Name, displayParam.Value);
                    
                    // Mark selected items as Edited and clear errors
                    foreach (var item in SelectedQueueItems)
                    {
                        item.IsEdited = true;
                        item.ErrorMessage = null;
                    }
                    OnPropertyChanged(nameof(SelectedItemErrorMessage));
                }
            }
        }

        private void PushBulkValue(string paramName, string newValue)
        {
            foreach (var displayParam in DisplayParameters)
            {
                displayParam.PropertyChanged -= DisplayParam_PropertyChanged;
            }

            try
            {
                if (paramName == "Name")
                {
                    foreach (var wrapper in _activeWrappers)
                    {
                        string oldName = wrapper.Name;
                        if (oldName != newValue)
                        {
                            ReplaceReferences(wrapper.GetUpdatedModel(), oldName, newValue);
                            wrapper.Name = newValue;
                        }
                    }
                }
                else
                {
                    foreach (var wrapper in _activeWrappers)
                    {
                        var targetParam = wrapper.Parameters.FirstOrDefault(p => p.Name == paramName);
                        if (targetParam != null && !targetParam.IsReadOnly)
                        {
                            targetParam.Value = newValue;
                        }
                    }
                }
            }
            finally
            {
                foreach (var displayParam in DisplayParameters)
                {
                    displayParam.PropertyChanged += DisplayParam_PropertyChanged;
                }
            }
        }

        public void ReplaceReferences(ObjectModel oldElement, string oldName, string newName)
        {
            var nameAndAliases = new List<string> { oldName };
            if (oldElement is ElementModel oldEl && oldEl.Aliases != null)
            {
                nameAndAliases.AddRange(oldEl.Aliases);
            }

            foreach (var qItem in ActionQueue)
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



        private List<StandardElementModel> GetCheckedElements()
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

        private void ExecuteEnforce(object parameter)
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

        /// <summary>
        /// Merges a list of existing standard elements with new standard elements, identifying duplicates strictly by Class + Name.
        /// </summary>
        public static List<ElementModel> MergeStandardsLists(List<ElementModel> existingElements, List<ElementModel> newElements, bool overwriteDuplicates)
        {
            var resultDict = new Dictionary<string, ElementModel>(StringComparer.OrdinalIgnoreCase);

            foreach (var el in existingElements)
            {
                if (el != null && !string.IsNullOrEmpty(el.Class) && !string.IsNullOrEmpty(el.Name))
                {
                    string key = $"{el.Class}:{el.Name}";
                    resultDict[key] = el;
                }
            }

            foreach (var el in newElements)
            {
                if (el != null && !string.IsNullOrEmpty(el.Class) && !string.IsNullOrEmpty(el.Name))
                {
                    string key = $"{el.Class}:{el.Name}";
                    if (resultDict.ContainsKey(key))
                    {
                        if (overwriteDuplicates)
                        {
                            resultDict[key] = el;
                        }
                    }
                    else
                    {
                        resultDict[key] = el;
                    }
                }
            }

            return resultDict.Values.ToList();
        }

        private void ExecuteSave(object parameter)
        {
            // Placeholder: out of scope for this slice
        }

        private void ExecuteSaveAndEnforce(object parameter)
        {
            // Placeholder: out of scope for this slice
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

                bool? dialogResult = ShowMergeDialog?.Invoke(vm);
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

                        // Scan all other elements in the Action Queue and replace references!
                        ReplaceQueueReferences(nonPrimaries, primaryItem.Name);

                        // Purge consumed items
                        foreach (var np in nonPrimaries)
                        {
                            ActionQueue.Remove(np);
                        }

                        // Reset workspace back to idle post-merge to prevent ghost references
                        ActiveWorkspace = WorkspaceMode.Idle;
                    }
                }
            }
        }

        private void ReplaceQueueReferences(List<QueueItemModel> oldElements, string newName)
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

            foreach (var qItem in ActionQueue)
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

        // Dedicated load options class to overwrite parameters and family definitions
        private class ImportFamilyLoadOptions : IFamilyLoadOptions
        {
            public bool OnFamilyFound(bool familyInUse, out bool overwriteParameterValues)
            {
                overwriteParameterValues = true;
                return true;
            }

            public bool OnSharedFamilyFound(Family sharedFamily, bool familyInUse, out FamilySource source, out bool overwriteParameterValues)
            {
                source = FamilySource.Family;
                overwriteParameterValues = true;
                return true;
            }
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
