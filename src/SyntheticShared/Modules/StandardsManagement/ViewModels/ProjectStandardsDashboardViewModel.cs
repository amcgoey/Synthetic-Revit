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
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;
using Synthetic.Settings;
using Synthetic.Infrastructure.Persistence;
using Synthetic.Modules.StandardsManagement.Utilities;
using Synthetic.Modules.DiffEngine;

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
    public class ProjectStandardsDashboardViewModel : ViewModelBase, IProjectStandardsDashboard
    {
        private readonly UIApplication? _uiapp;
        private readonly Document? _doc;
        private readonly IFileDialogService _dialogService;
        private readonly IStandardsExportService _exportService;
        internal readonly IUserPromptService _userPromptService;
        private readonly IFindReplaceService _findReplaceService;
        private readonly IStandardsExtractionOrchestrator _orchestrator;
        private readonly IPocoIdentityService _pocoIdentityService;
        private readonly IStandardSerializationEngine _serializationEngine;
        private readonly IStandardsExecutionPipeline _pipeline;
        public ISummaryDisplayService SummaryDisplayService { get; set; }
        public IPocoIdentityService PocoIdentityService => _pocoIdentityService;
        public IFindReplaceService FindReplaceService => _findReplaceService;
        public IStandardSerializationEngine SerializationEngine => _serializationEngine;
        public IFileDialogService DialogService => _dialogService;
        public IStandardsExecutionPipeline Pipeline => _pipeline;
        private StandardsSettings? _settings;
        private ExternalEvent? _externalEvent;
        private ProjectStandardsExternalEventHandler? _eventHandler;
        public ExternalEvent? ExternalEvent => _externalEvent;
        public StandardsSettings? Settings => _settings;
        private readonly StandardsSourceTreeViewModel _sourceTreeViewModel;
        private readonly StagingQueueViewModel _stagingQueueViewModel;
        public StagingQueueViewModel StagingQueueViewModel => _stagingQueueViewModel;
        private readonly StandardsExecutionPipelineViewModel _standardsExecutionViewModel;
        public StandardsExecutionPipelineViewModel StandardsExecutionPipelineViewModel => _standardsExecutionViewModel;

        /// <summary>
        /// Gets the staging queue collection.
        /// </summary>
        public ObservableCollection<QueueItemModel> StagingQueue => _stagingQueueViewModel.StagingQueue;

        /// <summary>
        /// Gets the grouped collection view of the staging queue.
        /// </summary>
        public ICollectionView StagingQueueView => _stagingQueueViewModel.StagingQueueView;

        public static ProjectStandardsDashboardViewModel? Instance { get; set; }
        public ObservableCollection<ParameterWrapperVM> DisplayParameters => _stagingQueueViewModel.DisplayParameters;

        /// <summary>
        /// Gets the single selected element wrapper when exactly one element is selected.
        /// </summary>
        public ElementTypeWrapperVM? SelectedElement => _stagingQueueViewModel.SelectedElement;

        /// <summary>
        /// Gets whether exactly one element is currently selected.
        /// </summary>
        public bool IsSingleElementSelected => _stagingQueueViewModel.IsSingleElementSelected;

        /// <summary>
        /// Gets or sets the name of the selected element, or a count description if multiple elements are selected.
        /// </summary>
        public string SelectedNameOrCount
        {
            get => _stagingQueueViewModel.SelectedNameOrCount;
            set => _stagingQueueViewModel.SelectedNameOrCount = value;
        }

        /// <summary>
        /// Gets a comma-separated concatenated list of stripped classes for the selected elements.
        /// </summary>
        public string SelectedDisplayClass => _stagingQueueViewModel.SelectedDisplayClass;

        /// <summary>
        /// Gets or sets the aliases string of the selected element, or &lt;Varies&gt; if multiple elements are selected.
        /// </summary>
        public string SelectedAliasesString
        {
            get => _stagingQueueViewModel.SelectedAliasesString;
            set => _stagingQueueViewModel.SelectedAliasesString = value;
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
                foreach (var qItem in StagingQueue)
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

        public void GetElementModelsFromHierarchy(SourceTreeItemViewModel node, List<ElementModel> list)
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


        /// <summary>
        /// Gets or sets the active right pane sub-workspace mode.
        /// </summary>
        public WorkspaceMode ActiveWorkspace
        {
            get => _activeWorkspace;
            set => SetProperty(ref _activeWorkspace, value);
        }

        /// <summary>
        /// Gets or sets the target file path for save actions.
        /// </summary>
        public string? SaveFilePath
        {
            get => _standardsExecutionViewModel.SaveFilePath;
            set => _standardsExecutionViewModel.SaveFilePath = value;
        }

        /// <summary>
        /// Gets whether the save path panel should be active/visible in the UI.
        /// </summary>
        public bool IsSavePathActive => _standardsExecutionViewModel.IsSavePathActive;

        /// <summary>
        /// Gets or sets the search string for batch find-and-replace edits.
        /// </summary>
        public string FindText
        {
            get => _stagingQueueViewModel.FindText;
            set => _stagingQueueViewModel.FindText = value;
        }

        /// <summary>
        /// Gets or sets the replacement string for batch find-and-replace edits.
        /// </summary>
        public string ReplaceText
        {
            get => _stagingQueueViewModel.ReplaceText;
            set => _stagingQueueViewModel.ReplaceText = value;
        }

        /// <summary>
        /// Gets or sets the search scope for batch find-and-replace edits.
        /// </summary>
        public SearchScope FindReplaceScope
        {
            get => _stagingQueueViewModel.FindReplaceScope;
            set => _stagingQueueViewModel.FindReplaceScope = value;
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
            get => _stagingQueueViewModel.SelectedItemName;
            set => _stagingQueueViewModel.SelectedItemName = value;
        }

        /// <summary>
        /// Gets the error message of the currently selected queue item if it has an error.
        /// </summary>
        public string? SelectedItemErrorMessage => _stagingQueueViewModel.SelectedItemErrorMessage;

        /// <summary>
        /// Gets the collection of staged elements currently selected for editing/diffing.
        /// </summary>
        public ObservableCollection<QueueItemModel> SelectedQueueItems => _stagingQueueViewModel.SelectedQueueItems;

        /// <summary>
        /// Gets the collection of duplicate/diff clusters populated by the comparison engine.
        /// </summary>
        public ObservableCollection<DuplicateClusterModel> ActiveDiffClusters => _stagingQueueViewModel.ActiveDiffClusters;

        /// <summary>
        /// Gets the combined list of results from the last Run Queue execution.
        /// </summary>
        public List<SerializationResultModel> LastExecutionResults { get; } = new List<SerializationResultModel>();

        /// <summary>
        /// Gets the source tree sub-ViewModel.
        /// </summary>
        public StandardsSourceTreeViewModel SourceTreeViewModel => _sourceTreeViewModel;

        /// <summary>
        /// Gets or sets the collection of loaded standard sources (tabs).
        /// </summary>
        public ObservableCollection<ProjectStandardsSourceViewModel> AvailableSources
        {
            get => _sourceTreeViewModel.AvailableSources;
            set => _sourceTreeViewModel.AvailableSources = value;
        }

        /// <summary>
        /// Gets or sets the currently active source tab.
        /// </summary>
        public ProjectStandardsSourceViewModel? SelectedSource
        {
            get => _sourceTreeViewModel.SelectedSource;
            set => _sourceTreeViewModel.SelectedSource = value;
        }

        /// <summary>
        /// Gets or sets the search filter text.
        /// </summary>
        public string SearchText
        {
            get => _sourceTreeViewModel.SearchText;
            set => _sourceTreeViewModel.SearchText = value;
        }



        /// <summary>
        /// Gets or sets whether to update loaded families during queue execution.
        /// </summary>
        public bool UpdateFamilies
        {
            get => _standardsExecutionViewModel.UpdateFamilies;
            set => _standardsExecutionViewModel.UpdateFamilies = value;
        }

        /// <summary>
        /// Gets or sets whether to process nested families recursively.
        /// </summary>
        public bool ProcessNestedRecursive
        {
            get => _standardsExecutionViewModel.ProcessNestedRecursive;
            set => _standardsExecutionViewModel.ProcessNestedRecursive = value;
        }

        /// <summary>
        /// Gets or sets whether to purge unused style types in family documents.
        /// </summary>
        public bool PurgeUnusedStyleTypes
        {
            get => _standardsExecutionViewModel.PurgeUnusedStyleTypes;
            set => _standardsExecutionViewModel.PurgeUnusedStyleTypes = value;
        }

        /// <summary>
        /// Gets or sets the category filter for family updates.
        /// </summary>
        public string CategoryFilter
        {
            get => _standardsExecutionViewModel.CategoryFilter;
            set => _standardsExecutionViewModel.CategoryFilter = value;
        }

        /// <summary>
        /// Gets the list of available category filters.
        /// </summary>
        public List<string> AvailableCategoryFilters => _standardsExecutionViewModel.AvailableCategoryFilters;

        public List<Document>? MockOpenDocuments { get; set; }
        public Func<SelectRevitDocumentViewModel, bool?>? ShowDocumentSelectionDialog { get; set; }
        public Func<Synthetic.Shared.UI.SingleItemSelectionViewModel<QueueItemModel>, bool?>? ShowMergeDialog { get; set; }

        #region Commands
        public ICommand AddFileSourceCommand => _sourceTreeViewModel.AddFileSourceCommand;
        public ICommand AddRevitModelCommand => _sourceTreeViewModel.AddRevitModelCommand;
        public ICommand CloseSourceCommand => _sourceTreeViewModel.CloseSourceCommand;
        public ICommand EnforceCommand => _standardsExecutionViewModel.EnforceCommand;
        public ICommand SaveCommand => _standardsExecutionViewModel.SaveCommand;
        public ICommand SaveAndEnforceCommand => _standardsExecutionViewModel.SaveAndEnforceCommand;
        public ICommand BrowseSavePathCommand => _standardsExecutionViewModel.BrowseSavePathCommand;
        public ICommand PushToQueueCommand => _stagingQueueViewModel.PushToQueueCommand;
        public ICommand RemoveFromQueueCommand => _stagingQueueViewModel.RemoveFromQueueCommand;
        public ICommand MergeQueueCommand => _stagingQueueViewModel.MergeQueueCommand;
        public ICommand EditCommand => _stagingQueueViewModel.EditCommand;
        public ICommand DiffCommand => _stagingQueueViewModel.DiffCommand;
        public ICommand RunQueueCommand => _standardsExecutionViewModel.RunQueueCommand;
        public ICommand BatchFindReplaceCommand => _stagingQueueViewModel.BatchFindReplaceCommand;
        public ICommand ApplyEditsCommand => _stagingQueueViewModel.ApplyEditsCommand;
        public ICommand CancelEditsCommand => _stagingQueueViewModel.CancelEditsCommand;
        public ICommand ResolveConflictCommand => _stagingQueueViewModel.ResolveConflictCommand;
        #endregion

        /// <summary>
        /// Initializes a new instance of the <see cref="ProjectStandardsDashboardViewModel"/> class for live Revit environment.
        /// </summary>
        public ProjectStandardsDashboardViewModel(
            UIApplication uiapp, 
            IFileDialogService dialogService, 
            IStandardsExportService exportService, 
            StandardsSettings? settings,
            IUserPromptService userPromptService,
            IFindReplaceService findReplaceService,
            IStandardsExtractionOrchestrator orchestrator,
            IPocoIdentityService pocoIdentityService,
            IDiffEngine<IEnumerable<ObjectModel>, Document> diffEngine,
            IStandardSerializationEngine serializationEngine,
            IStandardsExecutionPipeline pipeline)
        {
            _uiapp = uiapp ?? throw new ArgumentNullException(nameof(uiapp));
            _doc = uiapp.ActiveUIDocument?.Document;
            _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
            _exportService = exportService ?? throw new ArgumentNullException(nameof(exportService));
            _userPromptService = userPromptService ?? throw new ArgumentNullException(nameof(userPromptService));
            _findReplaceService = findReplaceService ?? throw new ArgumentNullException(nameof(findReplaceService));
            _serializationEngine = serializationEngine ?? throw new ArgumentNullException(nameof(serializationEngine));
            _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
            _pocoIdentityService = pocoIdentityService ?? throw new ArgumentNullException(nameof(pocoIdentityService));
            _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
            SummaryDisplayService = new WindowsSummaryDisplayService();
            Instance = this;

            _sourceTreeViewModel = new StandardsSourceTreeViewModel(
                this,
                uiapp,
                uiapp?.ActiveUIDocument?.Document,
                dialogService,
                orchestrator,
                serializationEngine);

            _stagingQueueViewModel = new StagingQueueViewModel(this, _pocoIdentityService, diffEngine);
            _standardsExecutionViewModel = new StandardsExecutionPipelineViewModel(this, _pipeline);

            RegisterPropertyChangedHandlers();



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



            Initialize(settings);
        }



        /// <summary>
        /// Initializes a new instance of the <see cref="ProjectStandardsDashboardViewModel"/> class for headless testing.
        /// </summary>
        public ProjectStandardsDashboardViewModel(
            Document doc, 
            IFileDialogService dialogService, 
            IStandardsExportService exportService, 
            StandardsSettings? settings,
            IUserPromptService userPromptService,
            IFindReplaceService findReplaceService,
            IStandardsExtractionOrchestrator orchestrator,
            IPocoIdentityService pocoIdentityService,
            IDiffEngine<IEnumerable<ObjectModel>, Document> diffEngine,
            IStandardSerializationEngine serializationEngine,
            IStandardsExecutionPipeline pipeline)
        {
            _doc = doc;
            _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
            _exportService = exportService ?? throw new ArgumentNullException(nameof(exportService));
            _userPromptService = userPromptService ?? throw new ArgumentNullException(nameof(userPromptService));
            _findReplaceService = findReplaceService ?? throw new ArgumentNullException(nameof(findReplaceService));
            _serializationEngine = serializationEngine ?? throw new ArgumentNullException(nameof(serializationEngine));
            _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
            _pocoIdentityService = pocoIdentityService ?? throw new ArgumentNullException(nameof(pocoIdentityService));
            _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
            SummaryDisplayService = new NoOpSummaryDisplayService();
            Instance = this;

            _sourceTreeViewModel = new StandardsSourceTreeViewModel(
                this,
                null,
                doc,
                dialogService,
                orchestrator,
                serializationEngine);

            _stagingQueueViewModel = new StagingQueueViewModel(this, _pocoIdentityService, diffEngine);
            _standardsExecutionViewModel = new StandardsExecutionPipelineViewModel(this, _pipeline);

            RegisterPropertyChangedHandlers();



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

            Initialize(settings);
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
                _sourceTreeViewModel.LoadFileSource(settings.StandardsFilePath, "Default Firm Standard");
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

                string fallbackDirectory = _sourceTreeViewModel.GetRevitLocalFileSaveLocation();
                SaveFilePath = PathResolutionUtility.GetDefaultSavePath(documentTitle, isModelInCloud, isWorkshared, centralModelPathString, localPathName, fallbackDirectory);
            }
            else
            {
                // Fallback directly to the Revit local file save location when no document is active
                string fallbackDirectory = _sourceTreeViewModel.GetRevitLocalFileSaveLocation();
                SaveFilePath = Path.Combine(fallbackDirectory, "Project Standards.json");
            }
        }



        private bool CanExecuteActions(object parameter) => SelectedSource != null;

        private bool CanExecuteQueueActions(object parameter) => StagingQueue.Count > 0;







        public void ReplaceReferences(ObjectModel oldElement, string oldName, string newName)
        {
            var nameAndAliases = new List<string> { oldName };
            if (oldElement is ElementModel oldEl && oldEl.Aliases != null)
            {
                nameAndAliases.AddRange(oldEl.Aliases);
            }

            foreach (var qItem in StagingQueue)
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



        public List<StandardElementModel> GetCheckedElements()
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






        public void ReplaceQueueReferences(List<QueueItemModel> oldElements, string newName)
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

            void UpdateReferencedId(ElementIdModel? idModel)
            {
                if (idModel != null && nameAndAliases.Contains(idModel.Name, StringComparer.OrdinalIgnoreCase))
                {
                    idModel.Name = newName;
                    idModel.Id = 0;
                    idModel.UniqueId = null;
                }
            }

            foreach (var qItem in StagingQueue)
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
                        UpdateReferencedId(layer.MaterialId);
                    }
                }

                if (el is MaterialModel mat)
                {
                    UpdateReferencedId(mat.SurfaceForegroundPatternId);
                    UpdateReferencedId(mat.SurfaceBackgroundPatternId);
                    UpdateReferencedId(mat.CutForegroundPatternId);
                    UpdateReferencedId(mat.CutBackgroundPatternId);
                    UpdateReferencedId(mat.AppearanceAssetId);
                }
            }
        }

        private void RegisterPropertyChangedHandlers()
        {
            _sourceTreeViewModel.PropertyChanged += OnSubViewModelPropertyChanged;
            _stagingQueueViewModel.PropertyChanged += OnSubViewModelPropertyChanged;
            _standardsExecutionViewModel.PropertyChanged += OnSubViewModelPropertyChanged;
        }

        private void OnSubViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (sender == _sourceTreeViewModel)
            {
                if (e.PropertyName == nameof(StandardsSourceTreeViewModel.AvailableSources) ||
                    e.PropertyName == nameof(StandardsSourceTreeViewModel.SelectedSource) ||
                    e.PropertyName == nameof(StandardsSourceTreeViewModel.SearchText))
                {
                    OnPropertyChanged(e.PropertyName);
                }
            }
            else if (sender == _stagingQueueViewModel)
            {
                if (e.PropertyName == nameof(StagingQueueViewModel.StagingQueue) ||
                    e.PropertyName == nameof(StagingQueueViewModel.StagingQueueView) ||
                    e.PropertyName == nameof(StagingQueueViewModel.SelectedQueueItems) ||
                    e.PropertyName == nameof(StagingQueueViewModel.SelectedNameOrCount) ||
                    e.PropertyName == nameof(StagingQueueViewModel.SelectedDisplayClass) ||
                    e.PropertyName == nameof(StagingQueueViewModel.SelectedAliasesString) ||
                    e.PropertyName == nameof(StagingQueueViewModel.SelectedElement) ||
                    e.PropertyName == nameof(StagingQueueViewModel.IsSingleElementSelected) ||
                    e.PropertyName == nameof(StagingQueueViewModel.SelectedItemErrorMessage))
                {
                    OnPropertyChanged(e.PropertyName);
                }
            }
            else if (sender == _standardsExecutionViewModel)
            {
                if (e.PropertyName == nameof(StandardsExecutionPipelineViewModel.UpdateFamilies) ||
                    e.PropertyName == nameof(StandardsExecutionPipelineViewModel.ProcessNestedRecursive) ||
                    e.PropertyName == nameof(StandardsExecutionPipelineViewModel.PurgeUnusedStyleTypes) ||
                    e.PropertyName == nameof(StandardsExecutionPipelineViewModel.CategoryFilter) ||
                    e.PropertyName == nameof(StandardsExecutionPipelineViewModel.AvailableCategoryFilters) ||
                    e.PropertyName == nameof(StandardsExecutionPipelineViewModel.SaveFilePath) ||
                    e.PropertyName == nameof(StandardsExecutionPipelineViewModel.IsSavePathActive))
                {
                    OnPropertyChanged(e.PropertyName);
                }
            }
        }

        public void RunQueueInternal()
        {
            _standardsExecutionViewModel.RunQueueInternal();
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
