using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows.Data;
using System.Windows.Input;
using Synthetic.Modules.RevitDOM;
using Synthetic.Modules.StandardsManagement.Models;
using Synthetic.Shared.UI;
using Synthetic.Modules.MergeDuplicates.Models;
using Synthetic.Modules.StandardsManagement.Utilities;
using Synthetic.Modules.StandardsManagement.Engine;

namespace Synthetic.Modules.StandardsManagement.ViewModels
{
    /// <summary>
    /// ViewModel that manages the staging action queue collection, queue manipulation commands,
    /// and the staging elements editing/batch find-replace workflow.
    /// </summary>
    public class ActionQueueViewModel : ViewModelBase
    {
        private readonly ProjectStandardsDashboardViewModel _parent;
        private readonly IPocoIdentityService _pocoIdentityService;

        private ObservableCollection<QueueItemModel> _actionQueue = new ObservableCollection<QueueItemModel>();
        private ObservableCollection<QueueItemModel> _selectedQueueItems = new ObservableCollection<QueueItemModel>();
        private ObservableCollection<ParameterWrapperVM> _displayParameters = new ObservableCollection<ParameterWrapperVM>();
        private ObservableCollection<DuplicateClusterModel> _activeDiffClusters = new ObservableCollection<DuplicateClusterModel>();

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

        private string _findText = string.Empty;
        private string _replaceText = string.Empty;
        private SearchScope _findReplaceScope = SearchScope.Both;

        /// <summary>
        /// Gets the staging action queue collection.
        /// </summary>
        public ObservableCollection<QueueItemModel> ActionQueue => _actionQueue;

        /// <summary>
        /// Gets the grouped collection view of the action queue.
        /// </summary>
        public ICollectionView ActionQueueView { get; }

        /// <summary>
        /// Gets the collection of staged elements currently selected for editing/diffing.
        /// </summary>
        public ObservableCollection<QueueItemModel> SelectedQueueItems => _selectedQueueItems;

        /// <summary>
        /// Gets the parameters collection currently displayed in the property grid.
        /// </summary>
        public ObservableCollection<ParameterWrapperVM> DisplayParameters => _displayParameters;

        /// <summary>
        /// Gets the duplicate and diff clusters populated by the comparison engine.
        /// </summary>
        public ObservableCollection<DuplicateClusterModel> ActiveDiffClusters => _activeDiffClusters;

        public ICommand PushToQueueCommand { get; }
        public ICommand RemoveFromQueueCommand { get; }
        public ICommand MergeQueueCommand { get; }
        public ICommand EditCommand { get; }
        public ICommand DiffCommand { get; }
        public ICommand BatchFindReplaceCommand { get; }
        public ICommand ApplyEditsCommand { get; }
        public ICommand CancelEditsCommand { get; }
        public ICommand ResolveConflictCommand { get; }

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

        /// <summary>
        /// Gets or sets the search scope for batch find-and-replace edits.
        /// </summary>
        public SearchScope FindReplaceScope
        {
            get => _findReplaceScope;
            set => SetProperty(ref _findReplaceScope, value);
        }

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
                    _parent.ReplaceReferences(SelectedQueueItems[0].TargetModel, oldName, value);

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

        public ActionQueueViewModel(ProjectStandardsDashboardViewModel parent, IPocoIdentityService pocoIdentityService)
        {
            _parent = parent ?? throw new ArgumentNullException(nameof(parent));
            _pocoIdentityService = pocoIdentityService ?? throw new ArgumentNullException(nameof(pocoIdentityService));

            ActionQueueView = CollectionViewSource.GetDefaultView(ActionQueue);
            ActionQueueView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(QueueItemModel.ClassName)));

            PushToQueueCommand = new RelayCommand(ExecutePushToQueue, CanExecuteActions);
            RemoveFromQueueCommand = new RelayCommand(ExecuteRemoveFromQueue, CanExecuteRemove);
            MergeQueueCommand = new RelayCommand(ExecuteMergeQueue, CanExecuteMergeQueue);
            EditCommand = new RelayCommand(ExecuteEdit, CanExecuteQueueActions);
            DiffCommand = new RelayCommand(ExecuteDiff, CanExecuteQueueActions);
            BatchFindReplaceCommand = new RelayCommand(ExecuteBatchFindReplace, CanExecuteQueueActions);
            ApplyEditsCommand = new RelayCommand(ExecuteApplyEdits, CanExecuteQueueActions);
            CancelEditsCommand = new RelayCommand(ExecuteCancelEdits);
            ResolveConflictCommand = new RelayCommand(ExecuteResolveConflict, CanExecuteQueueActions);
        }

        private bool CanExecuteActions(object parameter) => _parent.SelectedSource != null;
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

            var checkedItems = _parent.GetCheckedElements();
            if (checkedItems == null || checkedItems.Count == 0) return;

            // 1. Flatten SelectedSource.SourceHierarchy to build sourceElements list
            var sourceElements = new List<ElementModel>();
            if (_parent.SelectedSource != null)
            {
                foreach (var node in _parent.SelectedSource.SourceHierarchy)
                {
                    _parent.GetElementModelsFromHierarchy(node, sourceElements);
                }
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

        private bool CanExecuteRemove(object parameter)
        {
            if (parameter is System.Collections.IList list)
            {
                return list.Count > 0;
            }
            return false;
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

                bool? dialogResult = _parent.ShowMergeDialog?.Invoke(vm);
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
                        _parent.ReplaceQueueReferences(nonPrimaries, primaryItem.Name);

                        // Purge consumed items
                        foreach (var np in nonPrimaries)
                        {
                            ActionQueue.Remove(np);
                        }

                        // Reset workspace back to idle post-merge to prevent ghost references
                        _parent.ActiveWorkspace = WorkspaceMode.Idle;
                    }
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

                _parent.ActiveWorkspace = WorkspaceMode.Edit;
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

                if (_parent.Document != null)
                {
                    try
                    {
                        var elementPocos = SelectedQueueItems.Select(q => q.Model).OfType<ElementModel>().ToList();
                        var clusters = StandardsDiffEngine.RunDeepScan(_parent.Document, elementPocos, _parent.SerializationEngine);

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

                _parent.ActiveWorkspace = WorkspaceMode.Diff;
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

            _parent.ActiveWorkspace = WorkspaceMode.Idle;
            ActiveDiffClusters.Clear();
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
            var modifiedElements = _parent.FindReplaceService.Execute(elements, findText, replaceText, searchNames, searchParams);

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
            _parent.ActiveWorkspace = WorkspaceMode.Idle;
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
            _parent.ActiveWorkspace = WorkspaceMode.Idle;
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
                            _parent.ReplaceReferences(queueItem.TargetModel, oldName, newName);
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

        private void RaiseIdentityHeaderStateChanged()
        {
            OnPropertyChanged(nameof(IsSingleElementSelected));
            OnPropertyChanged(nameof(SelectedNameOrCount));
            OnPropertyChanged(nameof(SelectedDisplayClass));
            OnPropertyChanged(nameof(SelectedAliasesString));
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

        private void DisplayParam_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ParameterWrapperVM.Value) || e.PropertyName == nameof(ParameterWrapperVM.IsMixedValue))
            {
                if (sender is ParameterWrapperVM displayParam)
                {
                    string newValue = displayParam.Value;
                    string paramName = displayParam.Name;

                    // Propagate modified parameter value to all selected elements in editing wrappers
                    foreach (var wrapper in _activeWrappers)
                    {
                        var targetParam = wrapper.Parameters.FirstOrDefault(p => p.Name == paramName);
                        if (targetParam != null && !targetParam.IsReadOnly)
                        {
                            string oldVal = targetParam.Value;
                            targetParam.Value = newValue;
                            targetParam.IsMixedValue = false;

                            // Cascading element ID updates for swapped values (like Pattern, Material, etc.)
                            if (paramName.EndsWith("Id") && oldVal != newValue)
                            {
                                string oldName = oldVal;
                                _parent.ReplaceReferences(wrapper.GetUpdatedModel(), oldName, newValue);
                            }
                        }
                    }

                    // Update UI state
                    displayParam.IsMixedValue = false;
                    foreach (var item in SelectedQueueItems)
                    {
                        item.IsEdited = true;
                        item.ErrorMessage = null;
                    }
                    OnPropertyChanged(nameof(SelectedItemErrorMessage));
                }
            }
        }
    }
}
