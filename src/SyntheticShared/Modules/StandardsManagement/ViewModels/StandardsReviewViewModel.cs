using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using Autodesk.Revit.DB;

using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;
using Synthetic.Infrastructure.Serialization;

using Synthetic.Shared.UI;
using Synthetic.RevitDOM.Operations.Merge;
using Synthetic.Shared.RevitAPI;
namespace Synthetic.Modules.StandardsManagement.ViewModels
{
    /// <summary>
    /// ViewModel for the standards review window.
    /// Manages the resolution and enforcement of style conflicts between JSON standards and the document.
    /// </summary>
    public class StandardsReviewViewModel : ViewModelBase
    {
        private readonly Document _doc;
        private ObservableCollection<DuplicateClusterModel> _clusters;
        private DuplicateClusterModel? _selectedCluster;
        private TypeMappingModel? _selectedTypeMapping;
        private Action? _closeAction;

        /// <summary>
        /// Initializes a new instance of the <see cref="StandardsReviewViewModel"/> class.
        /// </summary>
        /// <param name="doc">The active Revit document.</param>
        /// <param name="clusters">The collection of duplicate clusters showing standards differences.</param>
        public StandardsReviewViewModel(Document doc, ObservableCollection<DuplicateClusterModel> clusters)
        {
            _doc = doc ?? throw new ArgumentNullException(nameof(doc));
            _clusters = clusters ?? new ObservableCollection<DuplicateClusterModel>();

            EnforceApprovedCommand = new RelayCommand(ExecuteEnforceApproved);
            CancelCommand = new RelayCommand(ExecuteCancel);

            SelectedCluster = _clusters.FirstOrDefault();
        }

        /// <summary>
        /// Gets or sets the collection of standard comparison clusters.
        /// </summary>
        public ObservableCollection<DuplicateClusterModel> Clusters
        {
            get => _clusters;
            set => SetProperty(ref _clusters, value);
        }

        /// <summary>
        /// Gets or sets the currently selected cluster (category) in the sidebar.
        /// </summary>
        public DuplicateClusterModel? SelectedCluster
        {
            get => _selectedCluster;
            set
            {
                if (SetProperty(ref _selectedCluster, value))
                {
                    SelectedTypeMapping = _selectedCluster?.TypeMappings?.FirstOrDefault();
                    OnPropertyChanged(nameof(SelectedClusterTypeMappings));
                }
            }
        }

        /// <summary>
        /// Gets the collection of type mappings for the selected cluster.
        /// </summary>
        public ObservableCollection<TypeMappingModel>? SelectedClusterTypeMappings => SelectedCluster?.TypeMappings;

        /// <summary>
        /// Gets or sets the currently selected type mapping in the element grid.
        /// </summary>
        public TypeMappingModel? SelectedTypeMapping
        {
            get => _selectedTypeMapping;
            set
            {
                if (SetProperty(ref _selectedTypeMapping, value))
                {
                    OnPropertyChanged(nameof(Rows));
                }
            }
        }

        /// <summary>
        /// Gets the collection of parameter resolutions for the selected type mapping.
        /// </summary>
        public ObservableCollection<ParameterDiffRowModel>? Rows => SelectedTypeMapping?.ParameterResolutions;

        /// <summary>
        /// Gets the command to enforce all approved overrides.
        /// </summary>
        public ICommand EnforceApprovedCommand { get; }

        /// <summary>
        /// Gets the command to cancel the operation.
        /// </summary>
        public ICommand CancelCommand { get; }

        /// <summary>
        /// Gets or sets the action to close the window.
        /// </summary>
        public Action? CloseAction
        {
            get => _closeAction;
            set => _closeAction = value;
        }

        private void ExecuteEnforceApproved(object parameter)
        {
            int appliedCount = 0;
            int skippedCount = 0;

            using (Transaction trans = new Transaction(_doc, "Enforce Project Standards"))
            {
                trans.Start();

                foreach (var cluster in Clusters)
                {
                    if (cluster.TypeMappings == null) continue;

                    foreach (var mapping in cluster.TypeMappings)
                    {
                        if (mapping.ParameterResolutions == null) continue;

                        if (mapping.SourceType == null) continue;
                        Element? liveElement = _doc.GetElement(mapping.SourceType.RevitTypeId.ToElementId());
                        if (liveElement == null)
                        {
                            skippedCount += mapping.ParameterResolutions.Count;
                            continue;
                        }

                        foreach (var row in mapping.ParameterResolutions)
                        {
                            // Only process approved overrides
                            if (!row.IsApproved)
                            {
                                skippedCount++;
                                continue;
                            }

                            string paramName = row.ParameterName;
                            string winningValue = row.GetValueForElement(row.WinningValueElementId);

                            // Retrieve specific parameter by name
                            Parameter p = liveElement.LookupParameter(paramName);
                            if (p == null)
                            {
                                foreach (Parameter param in liveElement.Parameters)
                                {
                                    if (param.Definition != null && param.Definition.Name == paramName)
                                    {
                                        p = param;
                                        break;
                                    }
                                }
                            }

                            try
                            {
                                if (p != null && !p.IsReadOnly)
                                {
                                    bool success = false;
                                    switch (p.StorageType)
                                    {
                                        case StorageType.Double:
                                            if (double.TryParse(winningValue, out double dVal))
                                            {
                                                success = p.Set(dVal);
                                            }
                                            break;
                                        case StorageType.Integer:
                                            if (int.TryParse(winningValue, out int iVal))
                                            {
                                                success = p.Set(iVal);
                                            }
                                            break;
                                        case StorageType.String:
                                            success = p.Set(winningValue);
                                            break;
                                        case StorageType.ElementId:
#if REVIT2022 || REVIT2023
                                            if (int.TryParse(winningValue, out int idInt))
                                            {
                                                success = p.Set(new ElementId(idInt));
                                            }
#else
                                            if (long.TryParse(winningValue, out long idLong))
                                            {
                                                success = p.Set(new ElementId(idLong));
                                            }
#endif
                                            break;
                                    }

                                    if (success)
                                    {
                                        appliedCount++;
                                    }
                                    else
                                    {
                                        skippedCount++;
                                    }
                                }
                                else
                                {
                                    skippedCount++;
                                }
                            }
                            catch (Exception)
                            {
                                skippedCount++;
                            }
                        }
                    }
                }

                trans.Commit();
            }

            // [AG2_TEST_START: StandardsReviewWindowQA]
            // REVERT_METHOD: To remove, safely delete this entire block.
            Console.WriteLine($"Jrn.Directive \"SyntheticQA\", \"AppliedStandardOverrides: [{appliedCount}]\"");
            Console.WriteLine($"Jrn.Directive \"SyntheticQA\", \"SkippedStandardOverrides: [{skippedCount}]\"");
            // [AG2_TEST_END: StandardsReviewWindowQA]

            CloseAction?.Invoke();
        }

        private void ExecuteCancel(object parameter)
        {
            CloseAction?.Invoke();
        }
    }
}
