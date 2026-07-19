using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows.Input;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Synthetic.Core;
using Synthetic.Shared.UI;
using Synthetic.Modules.StandardsManagement.Utilities;
using Synthetic.Modules.StandardsManagement.Models;
using Synthetic.Modules.StandardsManagement.Engine;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;

namespace Synthetic.Modules.StandardsManagement.ViewModels
{
    /// <summary>
    /// ViewModel that manages the standard source tabs, tree hierarchies, search filtering, and document extraction.
    /// </summary>
    public class StandardsSourceTreeViewModel : ViewModelBase
    {
        private readonly ProjectStandardsDashboardViewModel _parent;
        private readonly UIApplication? _uiapp;
        private readonly Document? _doc;
        private readonly IFileDialogService _dialogService;
        private readonly IStandardsExtractionOrchestrator _orchestrator;
        private readonly IStandardSerializationEngine _serializationEngine;

        private ObservableCollection<ProjectStandardsSourceViewModel> _availableSources = new ObservableCollection<ProjectStandardsSourceViewModel>();
        private ProjectStandardsSourceViewModel? _selectedSource;
        private string _searchText = string.Empty;

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

        /// <summary>
        /// Gets or sets the search filter text.
        /// </summary>
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

        public ICommand AddFileSourceCommand { get; }
        public ICommand AddRevitModelCommand { get; }
        public ICommand CloseSourceCommand { get; }

        public StandardsSourceTreeViewModel(
            ProjectStandardsDashboardViewModel parent,
            UIApplication? uiapp,
            Document? doc,
            IFileDialogService dialogService,
            IStandardsExtractionOrchestrator orchestrator,
            IStandardSerializationEngine serializationEngine)
        {
            _parent = parent ?? throw new ArgumentNullException(nameof(parent));
            _uiapp = uiapp;
            _doc = doc;
            _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
            _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
            _serializationEngine = serializationEngine ?? throw new ArgumentNullException(nameof(serializationEngine));

            AddFileSourceCommand = new RelayCommand(ExecuteAddFileSource);
            AddRevitModelCommand = new RelayCommand(ExecuteAddRevitModel);
            CloseSourceCommand = new RelayCommand(ExecuteCloseSource, CanExecuteCloseSource);
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

            var dialogVM = new SelectRevitDocumentViewModel(_uiapp, _parent.MockOpenDocuments);
            bool? dialogResult = _parent.ShowDocumentSelectionDialog?.Invoke(dialogVM);
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

        internal List<ElementModel> ExtractRevitElements(Document doc, bool scanFamilies, bool scanNestedFamilies, List<string> selectedGroupings)
        {
            var list = new List<ElementModel>();
            if (doc == null) return list;

            // 1. Extract categories directly since they are not Elements and don't have nested dependencies
            if (selectedGroupings == null || selectedGroupings.Contains("Categories"))
            {
                var engine = _serializationEngine;
                foreach (Category cat in doc.Settings.Categories)
                {
                    if (ProgressCoordinator.IsCancelled()) break;
                    try
                    {
                        var model = engine.ExtractCategory(cat, doc, false);
                        if (model is CategoryModel categoryModel)
                        {
                            list.Add(categoryModel);
                        }
                    }
                    catch { }
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
                    // Clone the model to construct a representation DTO without mutative side-effects on the original extracted model
                    var representation = (ElementModel)elementModel.Clone();
                    representation.Element = null;
                    representation.Document = null;
                    
                    list.Add(representation);
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
#if REVIT2022 || REVIT2023
            // ToposolidType not available in older versions
#elif REVIT2024 || REVIT2025
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
            catch { }
#else
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
            catch { }
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
                        .Cast<FamilySymbol>();

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

        internal string GetRevitLocalFileSaveLocation()
        {
            string? fallbackPath = null;
            object? target = _doc ?? (object?)_uiapp;
            
            if (target != null)
            {
                try
                {
                    var appProp = target.GetType().GetProperty("Application");
                    var appObj = appProp?.GetValue(target);
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
    }
}
