using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows.Input;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;
using Synthetic.Modules.StandardsManagement.Engine;
using Synthetic.Modules.StandardsManagement.Models;
using Synthetic.Modules.StandardsManagement.Utilities;
using Synthetic.Shared.UI;
using Synthetic.Infrastructure.Persistence;
using Synthetic.Core;
using Synthetic.Settings;

namespace Synthetic.Modules.StandardsManagement.ViewModels
{
    /// <summary>
    /// ViewModel that manages the options configuration and final save/enforce execution workflow.
    /// </summary>
    public class StandardsExecutionPipelineViewModel : ViewModelBase
    {
        private readonly ProjectStandardsDashboardViewModel _parent;
        private readonly IFileDialogService _dialogService;
        private readonly IStandardsExecutionPipeline _pipeline;

        private bool _updateFamilies = false;
        private bool _processNestedRecursive = false;
        private bool _purgeUnusedStyleTypes = false;
        private string _categoryFilter = "All Categories";
        private string? _saveFilePath;

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

        /// <summary>
        /// Gets or sets the target file path for save actions.
        /// </summary>
        public string? SaveFilePath
        {
            get => _saveFilePath;
            set
            {
                if (SetProperty(ref _saveFilePath, value))
                {
                    OnPropertyChanged(nameof(IsSavePathActive));
                }
            }
        }

        /// <summary>
        /// Gets whether the save path panel should be active/visible in the UI.
        /// </summary>
        public bool IsSavePathActive => _parent.StagingQueue.Any(item => item.WillSave);

        public ICommand EnforceCommand { get; }
        public ICommand SaveCommand { get; }
        public ICommand SaveAndEnforceCommand { get; }
        public ICommand BrowseSavePathCommand { get; }
        public ICommand RunQueueCommand { get; }

        public StandardsExecutionPipelineViewModel(ProjectStandardsDashboardViewModel parent, IStandardsExecutionPipeline pipeline)
        {
            _parent = parent ?? throw new ArgumentNullException(nameof(parent));
            _dialogService = parent.DialogService;
            _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));

            EnforceCommand = new RelayCommand(ExecuteEnforce, CanExecuteActions);
            SaveCommand = new RelayCommand(ExecuteSave, CanExecuteActions);
            SaveAndEnforceCommand = new RelayCommand(ExecuteSaveAndEnforce, CanExecuteActions);
            BrowseSavePathCommand = new RelayCommand(ExecuteBrowseSavePath);
            RunQueueCommand = new RelayCommand(ExecuteRunQueue, CanExecuteQueueActions);

            _parent.StagingQueueViewModel.StagingQueue.CollectionChanged += (s, e) =>
            {
                OnPropertyChanged(nameof(IsSavePathActive));
            };
        }

        private bool CanExecuteActions(object parameter) => _parent.SelectedSource != null;
        private bool CanExecuteQueueActions(object parameter) => _parent.StagingQueue.Count > 0;

        private void ExecuteEnforce(object parameter)
        {
            // Placeholder: out of scope for this slice
        }

        private void ExecuteSave(object parameter)
        {
            // Placeholder: out of scope for this slice
        }

        private void ExecuteSaveAndEnforce(object parameter)
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

        private void ExecuteRunQueue(object parameter)
        {
            if (_parent.ExternalEvent != null)
            {
                _parent.ExternalEvent.Raise();
            }
            else
            {
                RunQueueInternal();
            }
        }

        public void RunQueueInternal()
        {
            if (_parent.Document == null) return;

            _parent.LastExecutionResults.Clear();

            // Map staging queue items
            var pipelineItems = _parent.StagingQueue.Select(item => new StandardsExecutionItem(item.Model)
            {
                WillEnforce = item.WillEnforce,
                WillSave = item.WillSave
            }).ToList();

            // Determine target path
            string? targetPath = null;
            if (!string.IsNullOrEmpty(SaveFilePath))
            {
                targetPath = SaveFilePath;
            }
            else if (_parent.SelectedSource != null && !_parent.SelectedSource.IsRevitSource && !string.IsNullOrEmpty(_parent.SelectedSource.SourcePath))
            {
                targetPath = _parent.SelectedSource.SourcePath;
            }
            else
            {
                string? projectSettingsPath = GetProjectSettingsPath();
                if (!string.IsNullOrEmpty(projectSettingsPath))
                {
                    targetPath = projectSettingsPath;
                }
            }

            // Initialize options
            var options = new StandardsExecutionOptions
            {
                ProcessFamilies = UpdateFamilies,
                CategoryFilter = CategoryFilter,
                PurgeUnusedStyleTypes = PurgeUnusedStyleTypes,
                StandardsFilePath = targetPath ?? string.Empty,
                WriteRevitDatabase = _parent.StagingQueue.Any(i => i.WillEnforce),
                SaveLocalFiles = _parent.StagingQueue.Any(i => i.WillSave),
                UseTransactionGroup = true,
                ProtectedPaths = GetProtectedPaths().ToList()
            };

            // Invoke the pipeline
            var progressReporter = ProgressCoordinator.AsProgressReporter();
            var result = _pipeline.Execute(_parent.Document, pipelineItems, options, progressReporter, ProgressCoordinator.Token);

            // Populate LastExecutionResults
            _parent.LastExecutionResults.AddRange(result.RawResults);

            // Build summary tracker log items from LastExecutionResults
            var tracker = new ObservableCollection<ImportLogItem>();
            foreach (var res in _parent.LastExecutionResults)
            {
                var model = res.Model;
                string action = "Updated";
                if (!res.Success)
                {
                    if (res.Action == "Alias Swap Failed")
                    {
                        action = "Alias Fail";
                    }
                    else
                    {
                        action = res.OperationTarget == "File" ? "Save Failed" : "Failed";
                    }
                }
                else
                {
                    if (!string.IsNullOrEmpty(res.Action))
                    {
                        action = res.Action;
                    }
                    else if (res.OperationTarget == "File")
                    {
                        action = "Saved";
                    }
                    else
                    {
                        // Find the enqueued item to determine if it was Enforced/Saved
                        var queueItem = _parent.StagingQueue.FirstOrDefault(qi => qi.Model == model);
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

                string message = res.Success ? "Operation completed successfully." : (res.ErrorMessage ?? "Unknown error occurred.");
                if (!string.IsNullOrEmpty(res.Message))
                {
                    message = res.Message;
                }
                if (res.Warnings != null && res.Warnings.Count > 0)
                {
                    message += " Warnings: " + string.Join(", ", res.Warnings);
                }

                tracker.Add(new ImportLogItem
                {
                    Action = action,
                    Class = className,
                    ElementName = name,
                    Message = message
                });
            }

            Action updateUI = () =>
            {
                // Display the summary dialog modal
                if (tracker.Count > 0 && _parent.SummaryDisplayService != null)
                {
                    var summaryVM = new ImportSummaryViewModel(tracker, _dialogService);
                    IntPtr parentHandle = _parent.UIApplication != null ? _parent.UIApplication.MainWindowHandle : IntPtr.Zero;
                    _parent.SummaryDisplayService.ShowSummary(summaryVM, parentHandle);
                }

                // Systematic queue purging and error message hydration
                var successfulItems = new List<QueueItemModel>();
                foreach (var item in _parent.StagingQueue.ToList())
                {
                    var resultsForItem = _parent.LastExecutionResults.Where(r => r.Model == item.Model).ToList();
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
                    _parent.StagingQueue.Remove(item);
                }

                _parent.ActiveWorkspace = WorkspaceMode.Idle;
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

        private string? GetProjectSettingsPath()
        {
            if (_parent.Settings != null)
            {
                return _parent.Settings.StandardsFilePath;
            }
            if (_parent.Document == null) return null;
            try
            {
                var settings = SettingsManager.Get<StandardsSettings>(_parent.Document);
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
    }
}
