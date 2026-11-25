using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using Synthetic.Features.MultiModelPdfExport.Adapters;
using Synthetic.Features.MultiModelPdfExport.Models;
using Synthetic.Features.MultiModelPdfExport.Services;
using Synthetic.Platform.UI;

namespace Synthetic.Features.MultiModelPdfExport.ViewModels
{
    /// <summary>
    /// Semantic tone for the Multi-Model PDF Export footer readiness summary.
    /// </summary>
    public enum MultiModelPdfFooterStatusKind
    {
        Muted = 0,
        Warning = 1,
        Success = 2
    }

    /// <summary>
    /// Presentation ViewModel for the Variant A Multi-Model PDF Export modal dialog.
    /// </summary>
    public sealed class MultiModelPdfExportViewModel : ViewModelBase
    {
        private readonly IMultiModelPdfExportAdapter _adapter;
        private readonly IFolderDialogService _folderDialogService;
        private readonly MultiModelPdfExportUserPrefsStore? _userPrefsStore;
        private readonly ObservableCollection<MultiModelPdfExportRowViewModel> _rows =
            new ObservableCollection<MultiModelPdfExportRowViewModel>();
        private readonly ObservableCollection<MultiModelPdfExportRowViewModel> _visibleRows =
            new ObservableCollection<MultiModelPdfExportRowViewModel>();
        private readonly ObservableCollection<MultiModelPdfBulkPrintSetOption> _bulkPrintSetOptions =
            new ObservableCollection<MultiModelPdfBulkPrintSetOption>();
        private readonly ObservableCollection<MultiModelPdfSetupOption> _pdfSetups =
            new ObservableCollection<MultiModelPdfSetupOption>();

        private MultiModelPdfBulkPrintSetOption? _selectedBulkPrintSetOption;
        private MultiModelPdfSetupOption _selectedPdfSetup;
        private MultiModelPdfFileMode _fileMode = MultiModelPdfFileMode.CombinedPerDocument;
        private string _outputFolder = string.Empty;
        private string _filterText = string.Empty;
        private IReadOnlyList<MultiModelPdfValidationIssue> _validationErrors = Array.Empty<MultiModelPdfValidationIssue>();
        private int _checkedCount;
        private int _eligibleCount;
        private int _hiddenCheckedCount;
        private int _totalPrintableViews;
        private int _totalSkippedViews;
        private bool _canExport;
        private bool _hasVisibleEligibleRows;
        private bool _hasNoVisibleRows;
        private bool? _allVisibleChecked = false;
        private string _exportButtonText = "Export PDFs";
        private string _selectionSummaryText = "0 of 0 eligible";
        private string _outputPreviewText = string.Empty;
        private string _primaryErrorBannerText = string.Empty;
        private bool _hasErrorBanner;
        private bool _showFolderErrorBorder;
        private string _footerStatusText = "Select at least one project to export";
        private MultiModelPdfFooterStatusKind _footerStatusKind = MultiModelPdfFooterStatusKind.Muted;
        private bool _hasBulkFeedbackBanner;
        private MultiModelPdfBulkFeedbackKind _bulkFeedbackKind = MultiModelPdfBulkFeedbackKind.None;
        private string _bulkFeedbackMessage = string.Empty;
        private bool _showUncheckUnmatchedButton;
        private string? _lastAppliedBulkPrintSetName;

        /// <summary>
        /// Raised when the modal dialog should close (<c>true</c> = Export confirmed, <c>false</c> = Cancelled).
        /// </summary>
        public event Action<bool>? RequestClose;

        /// <summary>
        /// Initializes a new instance of the <see cref="MultiModelPdfExportViewModel"/> class.
        /// </summary>
        public MultiModelPdfExportViewModel(
            IMultiModelPdfExportAdapter adapter,
            IFolderDialogService folderDialogService,
            string? initialOutputFolder = null,
            MultiModelPdfExportUserPrefsStore? userPrefsStore = null)
        {
            _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
            _folderDialogService = folderDialogService ?? throw new ArgumentNullException(nameof(folderDialogService));
            _userPrefsStore = userPrefsStore;

            MultiModelPdfSessionSnapshot snapshot = _adapter.CaptureSessionSnapshot()
                ?? new MultiModelPdfSessionSnapshot(string.Empty);

            IOrderedEnumerable<MultiModelPdfDocumentSnapshot> orderedDocs = snapshot.Documents
                .OrderBy(d => GetOriginOrder(d.Origin));

            foreach (MultiModelPdfDocumentSnapshot doc in orderedDocs)
            {
                bool checkByDefault = doc.Origin == MultiModelPdfDocumentOrigin.Active ||
                                      doc.Origin == MultiModelPdfDocumentOrigin.Linked;
                _rows.Add(new MultiModelPdfExportRowViewModel(doc, checkByDefault, OnRowChanged));
            }

            ActiveDocumentTitle = !string.IsNullOrWhiteSpace(snapshot.ActiveDocumentTitle)
                ? snapshot.ActiveDocumentTitle
                : (_rows.FirstOrDefault(r => r.Origin == MultiModelPdfDocumentOrigin.Active)?.DocumentTitle ?? string.Empty);

            if (snapshot.PdfSetups.Count == 0 || !snapshot.PdfSetups.Any(s => s.IsDefault))
            {
                _pdfSetups.Add(MultiModelPdfSetupOption.CreateDefault());
            }

            foreach (MultiModelPdfSetupOption setup in snapshot.PdfSetups)
            {
                _pdfSetups.Add(setup);
            }

            _selectedPdfSetup = _pdfSetups.FirstOrDefault(s => s.IsDefault) ?? _pdfSetups[0];
            _outputFolder = initialOutputFolder ?? string.Empty;

            foreach (MultiModelPdfBulkPrintSetOption option in BuildBulkPrintSetOptions(_rows))
            {
                _bulkPrintSetOptions.Add(option);
            }

            _selectedBulkPrintSetOption = _bulkPrintSetOptions.FirstOrDefault();

            LoadUserPrefs();

            ApplyBulkPrintSetCommand = new RelayCommand(ExecuteApplyBulkPrintSet, CanExecuteApplyBulkPrintSet);
            UncheckUnmatchedProjectsCommand = new RelayCommand(_ => ExecuteUncheckUnmatchedProjects(), _ => ShowUncheckUnmatchedButton);
            DismissBulkFeedbackCommand = new RelayCommand(_ => ExecuteDismissBulkFeedback(), _ => HasBulkFeedbackBanner);
            ToggleAllVisibleCommand = new RelayCommand(_ => SetVisibleEligibleChecked(_allVisibleChecked != true), _ => HasVisibleEligibleRows);
            SelectAllVisibleCommand = new RelayCommand(_ => SetVisibleEligibleChecked(true));
            SelectActiveAndLinksVisibleCommand = new RelayCommand(_ => ExecuteSelectActiveAndLinksVisible());
            SelectNoneVisibleCommand = new RelayCommand(_ => SetVisibleEligibleChecked(false));
            BrowseOutputFolderCommand = new RelayCommand(_ => ExecuteBrowseOutputFolder());
            ExportCommand = new RelayCommand(_ => ExecuteExport(), _ => CanExport);
            CancelCommand = new RelayCommand(_ => RequestClose?.Invoke(false));

            RefreshVisibleRows(recompute: false);
            RecomputeValidationAndSummary();
        }

        /// <summary>Gets the active document title displayed in the setup source hint.</summary>
        public string ActiveDocumentTitle { get; }

        /// <summary>Gets the source caption for the active PDF Export Setup dropdown.</summary>
        public string ActiveSetupSourceSummary => string.IsNullOrWhiteSpace(ActiveDocumentTitle)
            ? "Applies to all selected projects"
            : $"From {ActiveDocumentTitle} · applies to all";

        /// <summary>Gets all discovered project row ViewModels ordered Active, Linked, Open.</summary>
        public ObservableCollection<MultiModelPdfExportRowViewModel> Rows => _rows;

        /// <summary>Gets the filtered subset of <see cref="Rows"/> currently visible in the table.</summary>
        public ObservableCollection<MultiModelPdfExportRowViewModel> VisibleRows => _visibleRows;

        /// <summary>Gets or sets the live project filter text matching title, origin, or storage kind.</summary>
        public string FilterText
        {
            get => _filterText;
            set
            {
                string next = value ?? string.Empty;
                if (SetProperty(ref _filterText, next))
                {
                    RefreshVisibleRows(recompute: true);
                }
            }
        }

        /// <summary>Gets the count of checked project rows currently hidden by <see cref="FilterText"/>.</summary>
        public int HiddenCheckedCount
        {
            get => _hiddenCheckedCount;
            private set => SetProperty(ref _hiddenCheckedCount, value);
        }

        /// <summary>Gets whether at least one eligible project row is currently visible.</summary>
        public bool HasVisibleEligibleRows
        {
            get => _hasVisibleEligibleRows;
            private set
            {
                if (SetProperty(ref _hasVisibleEligibleRows, value))
                {
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        /// <summary>Gets whether the active <see cref="FilterText"/> matches zero project rows.</summary>
        public bool HasNoVisibleRows
        {
            get => _hasNoVisibleRows;
            private set => SetProperty(ref _hasNoVisibleRows, value);
        }

        /// <summary>
        /// Gets or sets the tri-state master header selection across currently visible eligible rows
        /// (<c>true</c> = all checked, <c>false</c> = none checked, <c>null</c> = partial/indeterminate).
        /// </summary>
        public bool? AllVisibleChecked
        {
            get => _allVisibleChecked;
            set
            {
                bool targetChecked = value == true;
                if (!SetVisibleEligibleChecked(targetChecked))
                {
                    OnPropertyChanged(nameof(AllVisibleChecked));
                }
            }
        }

        /// <summary>Gets bulk Print Set options sorted by active presence, match count descending, and name.</summary>
        public ObservableCollection<MultiModelPdfBulkPrintSetOption> BulkPrintSetOptions => _bulkPrintSetOptions;

        /// <summary>Gets a value indicating whether any eligible Print Set exists for bulk assignment.</summary>
        public bool HasBulkPrintSetOptions => _bulkPrintSetOptions.Count > 0;

        /// <summary>Gets or sets the selected bulk Print Set dropdown option.</summary>
        public MultiModelPdfBulkPrintSetOption? SelectedBulkPrintSetOption
        {
            get => _selectedBulkPrintSetOption;
            set
            {
                if (SetProperty(ref _selectedBulkPrintSetOption, value))
                {
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        /// <summary>Gets available PDF Export Setup options.</summary>
        public ObservableCollection<MultiModelPdfSetupOption> PdfSetups => _pdfSetups;

        /// <summary>Gets or sets the selected PDF Export Setup.</summary>
        public MultiModelPdfSetupOption SelectedPdfSetup
        {
            get => _selectedPdfSetup;
            set
            {
                MultiModelPdfSetupOption next = value ?? _pdfSetups.FirstOrDefault(s => s.IsDefault) ?? _pdfSetups[0];
                if (SetProperty(ref _selectedPdfSetup, next))
                {
                    RecomputeValidationAndSummary();
                }
            }
        }

        /// <summary>Gets or sets the PDF file output structure mode.</summary>
        public MultiModelPdfFileMode FileMode
        {
            get => _fileMode;
            set
            {
                if (SetProperty(ref _fileMode, value))
                {
                    RecomputeValidationAndSummary();
                }
            }
        }

        /// <summary>Gets or sets the destination Output Folder path.</summary>
        public string OutputFolder
        {
            get => _outputFolder;
            set
            {
                string next = value ?? string.Empty;
                if (SetProperty(ref _outputFolder, next))
                {
                    RecomputeValidationAndSummary();
                }
            }
        }

        /// <summary>Gets all active blocking validation issues across selection, Print Sets, and output path.</summary>
        public IReadOnlyList<MultiModelPdfValidationIssue> ValidationErrors
        {
            get => _validationErrors;
            private set => SetProperty(ref _validationErrors, value);
        }

        /// <summary>Gets the count of currently checked project rows.</summary>
        public int CheckedCount
        {
            get => _checkedCount;
            private set => SetProperty(ref _checkedCount, value);
        }

        /// <summary>Gets the count of eligible project rows.</summary>
        public int EligibleCount
        {
            get => _eligibleCount;
            private set => SetProperty(ref _eligibleCount, value);
        }

        /// <summary>Gets the total printable views across checked rows.</summary>
        public int TotalPrintableViews
        {
            get => _totalPrintableViews;
            private set => SetProperty(ref _totalPrintableViews, value);
        }

        /// <summary>Gets the total skipped views across checked rows.</summary>
        public int TotalSkippedViews
        {
            get => _totalSkippedViews;
            private set => SetProperty(ref _totalSkippedViews, value);
        }

        /// <summary>Gets whether export is currently valid and enabled.</summary>
        public bool CanExport
        {
            get => _canExport;
            private set
            {
                if (SetProperty(ref _canExport, value))
                {
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        /// <summary>Gets the dynamic primary action button label.</summary>
        public string ExportButtonText
        {
            get => _exportButtonText;
            private set => SetProperty(ref _exportButtonText, value);
        }

        /// <summary>Gets the header selection summary caption.</summary>
        public string SelectionSummaryText
        {
            get => _selectionSummaryText;
            private set => SetProperty(ref _selectionSummaryText, value);
        }

        /// <summary>Gets the live resolved output path preview line.</summary>
        public string OutputPreviewText
        {
            get => _outputPreviewText;
            private set => SetProperty(ref _outputPreviewText, value);
        }

        /// <summary>Gets the primary validation error banner text (excluding suppressed NO_PROJECTS).</summary>
        public string PrimaryErrorBannerText
        {
            get => _primaryErrorBannerText;
            private set => SetProperty(ref _primaryErrorBannerText, value);
        }

        /// <summary>Gets whether the Coral Red validation error banner should be visible.</summary>
        public bool HasErrorBanner
        {
            get => _hasErrorBanner;
            private set => SetProperty(ref _hasErrorBanner, value);
        }

        /// <summary>Gets whether the Output Folder input should show a Coral Red error border.</summary>
        public bool ShowFolderErrorBorder
        {
            get => _showFolderErrorBorder;
            private set => SetProperty(ref _showFolderErrorBorder, value);
        }

        /// <summary>Gets the pinned footer readiness summary text.</summary>
        public string FooterStatusText
        {
            get => _footerStatusText;
            private set => SetProperty(ref _footerStatusText, value);
        }

        /// <summary>Gets the semantic visual tone for <see cref="FooterStatusText"/>.</summary>
        public MultiModelPdfFooterStatusKind FooterStatusKind
        {
            get => _footerStatusKind;
            private set => SetProperty(ref _footerStatusKind, value);
        }

        /// <summary>Gets whether the inline dismissible bulk Print Set feedback banner is visible.</summary>
        public bool HasBulkFeedbackBanner
        {
            get => _hasBulkFeedbackBanner;
            private set
            {
                if (SetProperty(ref _hasBulkFeedbackBanner, value))
                {
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        /// <summary>Gets the semantic visual tone for the bulk Print Set feedback banner.</summary>
        public MultiModelPdfBulkFeedbackKind BulkFeedbackKind
        {
            get => _bulkFeedbackKind;
            private set => SetProperty(ref _bulkFeedbackKind, value);
        }

        /// <summary>Gets the message shown inside the bulk Print Set feedback banner.</summary>
        public string BulkFeedbackMessage
        {
            get => _bulkFeedbackMessage;
            private set => SetProperty(ref _bulkFeedbackMessage, value);
        }

        /// <summary>Gets whether the one-click <c>Uncheck unmatched</c> action button should be shown.</summary>
        public bool ShowUncheckUnmatchedButton
        {
            get => _showUncheckUnmatchedButton;
            private set
            {
                if (SetProperty(ref _showUncheckUnmatchedButton, value))
                {
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        /// <summary>Gets the command that applies the selected bulk Print Set across all matching eligible rows.</summary>
        public ICommand ApplyBulkPrintSetCommand { get; }

        /// <summary>Gets the command that unchecks checked rows mismatched by the last bulk Print Set operation.</summary>
        public ICommand UncheckUnmatchedProjectsCommand { get; }

        /// <summary>Gets the command that dismisses the bulk feedback banner and clears mismatch accent stripes.</summary>
        public ICommand DismissBulkFeedbackCommand { get; }

        /// <summary>Gets the command that toggles all visible eligible rows.</summary>
        public ICommand ToggleAllVisibleCommand { get; }

        /// <summary>Gets the command that checks all visible eligible rows.</summary>
        public ICommand SelectAllVisibleCommand { get; }

        /// <summary>Gets the command that checks visible eligible Active and Linked rows and unchecks visible Open tabs.</summary>
        public ICommand SelectActiveAndLinksVisibleCommand { get; }

        /// <summary>Gets the command that unchecks all visible rows.</summary>
        public ICommand SelectNoneVisibleCommand { get; }

        /// <summary>Gets the command that opens the folder picker dialog.</summary>
        public ICommand BrowseOutputFolderCommand { get; }

        /// <summary>Gets the command that confirms and launches export.</summary>
        public ICommand ExportCommand { get; }

        /// <summary>Gets the command that cancels without side effects.</summary>
        public ICommand CancelCommand { get; }

        /// <summary>
        /// Builds an immutable <see cref="MultiModelPdfRunRequest"/> from the current validated selection.
        /// </summary>
        public MultiModelPdfRunRequest BuildRunRequest()
        {
            if (!CanExport)
            {
                string reason = !string.IsNullOrWhiteSpace(PrimaryErrorBannerText)
                    ? PrimaryErrorBannerText
                    : "Cannot build Multi-Model PDF Export request while validation errors remain.";
                throw new InvalidOperationException(reason);
            }

            string normalizedFolder = MultiModelPdfPathHelper.NormalizeFolderPath(OutputFolder);
            var jobs = new List<MultiModelPdfDocumentJob>();
            foreach (MultiModelPdfExportRowViewModel row in _rows)
            {
                if (!row.IsChecked || row.SelectedSet == null)
                {
                    continue;
                }

                jobs.Add(new MultiModelPdfDocumentJob(
                    documentKey: row.DocumentKey,
                    documentTitle: row.DocumentTitle,
                    origin: row.Origin,
                    storage: row.Storage,
                    printSetName: row.SelectedSet.Name,
                    pdfSetupId: SelectedPdfSetup.Id,
                    fileMode: FileMode,
                    outputFolder: normalizedFolder,
                    outputFileToken: row.ResolvedOutputToken));
            }

            return new MultiModelPdfRunRequest(jobs, normalizedFolder, SelectedPdfSetup.Id, FileMode);
        }

        private bool CanExecuteApplyBulkPrintSet(object? parameter)
        {
            if (EligibleCount <= 0)
            {
                return false;
            }

            if (parameter is string setName)
            {
                return !string.IsNullOrWhiteSpace(setName);
            }

            return SelectedBulkPrintSetOption != null;
        }

        private void ExecuteApplyBulkPrintSet(object? parameter)
        {
            string? requestedName = parameter is string rawName && !string.IsNullOrWhiteSpace(rawName)
                ? rawName.Trim()
                : SelectedBulkPrintSetOption?.Name;

            if (string.IsNullOrWhiteSpace(requestedName))
            {
                return;
            }

            MultiModelPdfBulkPrintSetOption? matchingOption = _bulkPrintSetOptions.FirstOrDefault(
                o => string.Equals(o.Name, requestedName, StringComparison.OrdinalIgnoreCase));
            if (matchingOption != null && !ReferenceEquals(_selectedBulkPrintSetOption, matchingOption))
            {
                _selectedBulkPrintSetOption = matchingOption;
                OnPropertyChanged(nameof(SelectedBulkPrintSetOption));
            }

            string displaySetName = matchingOption?.Name ?? requestedName!;
            _lastAppliedBulkPrintSetName = displaySetName;

            int totalCheckedEligible = 0;
            int matchedChecked = 0;
            int matchedUnchecked = 0;
            var unmatchedCheckedTitles = new List<string>();

            foreach (MultiModelPdfExportRowViewModel row in _rows)
            {
                if (!row.IsEligible)
                {
                    row.SetBulkMismatchAccent(false);
                    continue;
                }

                if (row.IsChecked)
                {
                    totalCheckedEligible++;
                }

                MultiModelPdfPrintSetChoice? matchChoice = row.PrintSetOptions.FirstOrDefault(
                    c => !c.IsPlaceholder && string.Equals(c.Name, displaySetName, StringComparison.OrdinalIgnoreCase));

                if (matchChoice != null)
                {
                    row.ApplyBulkMatchedChoice(matchChoice);
                    if (row.IsChecked)
                    {
                        matchedChecked++;
                    }
                    else
                    {
                        matchedUnchecked++;
                    }
                }
                else if (row.IsChecked)
                {
                    row.SetBulkMismatchAccent(true);
                    unmatchedCheckedTitles.Add(row.DocumentTitle);
                }
                else
                {
                    row.SetBulkMismatchAccent(false);
                }
            }

            string uncheckedSuffix = matchedUnchecked > 0 ? $" (+{matchedUnchecked} unchecked)" : string.Empty;

            if (totalCheckedEligible > 0 && unmatchedCheckedTitles.Count == 0)
            {
                string projectWord = matchedChecked == 1 ? "project" : "projects";
                BulkFeedbackKind = MultiModelPdfBulkFeedbackKind.Success;
                BulkFeedbackMessage = $"Applied \"{displaySetName}\" to all {matchedChecked} checked {projectWord}{uncheckedSuffix}.";
                HasBulkFeedbackBanner = true;
            }
            else if (totalCheckedEligible > 0)
            {
                BulkFeedbackKind = MultiModelPdfBulkFeedbackKind.Warning;
                BulkFeedbackMessage = $"Applied \"{displaySetName}\" to {matchedChecked} checked{uncheckedSuffix}. No \"{displaySetName}\" in: {string.Join(", ", unmatchedCheckedTitles)}.";
                HasBulkFeedbackBanner = true;
            }
            else if (matchedUnchecked > 0)
            {
                string projectWord = matchedUnchecked == 1 ? "project" : "projects";
                BulkFeedbackKind = MultiModelPdfBulkFeedbackKind.Success;
                BulkFeedbackMessage = $"Applied \"{displaySetName}\" to {matchedUnchecked} unchecked {projectWord} (0 checked).";
                HasBulkFeedbackBanner = true;
            }
            else
            {
                BulkFeedbackKind = MultiModelPdfBulkFeedbackKind.Warning;
                BulkFeedbackMessage = $"No eligible projects contain \"{displaySetName}\".";
                HasBulkFeedbackBanner = true;
            }

            RecomputeValidationAndSummary();
        }

        private void ExecuteUncheckUnmatchedProjects()
        {
            foreach (MultiModelPdfExportRowViewModel row in _rows)
            {
                if (row.IsChecked && row.HasBulkMismatchAccent)
                {
                    row.SetCheckedBatch(false);
                }
                else
                {
                    row.SetBulkMismatchAccent(false);
                }
            }

            ClearBulkFeedbackBanner();
            RecomputeValidationAndSummary();
        }

        private void ExecuteDismissBulkFeedback()
        {
            foreach (MultiModelPdfExportRowViewModel row in _rows)
            {
                row.SetBulkMismatchAccent(false);
            }

            ClearBulkFeedbackBanner();
            RecomputeValidationAndSummary();
        }

        private void ClearBulkFeedbackBanner()
        {
            HasBulkFeedbackBanner = false;
            BulkFeedbackKind = MultiModelPdfBulkFeedbackKind.None;
            BulkFeedbackMessage = string.Empty;
        }

        private bool SetVisibleEligibleChecked(bool isChecked)
        {
            bool anyChanged = false;
            foreach (MultiModelPdfExportRowViewModel row in _visibleRows)
            {
                if (!row.IsEligible)
                {
                    continue;
                }

                if (row.SetCheckedBatch(isChecked))
                {
                    anyChanged = true;
                }
            }

            if (anyChanged)
            {
                RecomputeValidationAndSummary();
            }

            return anyChanged;
        }

        private void ExecuteSelectActiveAndLinksVisible()
        {
            bool anyChanged = false;
            foreach (MultiModelPdfExportRowViewModel row in _visibleRows)
            {
                if (!row.IsEligible)
                {
                    continue;
                }

                bool target = row.Origin == MultiModelPdfDocumentOrigin.Active ||
                              row.Origin == MultiModelPdfDocumentOrigin.Linked;
                if (row.SetCheckedBatch(target))
                {
                    anyChanged = true;
                }
            }

            if (anyChanged)
            {
                RecomputeValidationAndSummary();
            }
        }

        private void RefreshVisibleRows(bool recompute)
        {
            string query = FilterText.Trim();
            var matching = _rows.Where(r => MatchesFilter(r, query)).ToList();

            for (int i = _visibleRows.Count - 1; i >= 0; i--)
            {
                if (!matching.Contains(_visibleRows[i]))
                {
                    _visibleRows.RemoveAt(i);
                }
            }

            for (int targetIndex = 0; targetIndex < matching.Count; targetIndex++)
            {
                MultiModelPdfExportRowViewModel item = matching[targetIndex];
                if (targetIndex < _visibleRows.Count && ReferenceEquals(_visibleRows[targetIndex], item))
                {
                    continue;
                }

                int existingIndex = _visibleRows.IndexOf(item);
                if (existingIndex >= 0)
                {
                    _visibleRows.Move(existingIndex, targetIndex);
                }
                else
                {
                    _visibleRows.Insert(targetIndex, item);
                }
            }

            if (recompute)
            {
                RecomputeValidationAndSummary();
            }
        }

        private static bool MatchesFilter(MultiModelPdfExportRowViewModel row, string trimmedQuery)
        {
            if (string.IsNullOrEmpty(trimmedQuery))
            {
                return true;
            }

            return ContainsIgnoreCase(row.DocumentTitle, trimmedQuery) ||
                   ContainsIgnoreCase(row.OriginBadgeText, trimmedQuery) ||
                   ContainsIgnoreCase(row.StorageDisplayText, trimmedQuery) ||
                   ContainsIgnoreCase(row.Storage.ToString(), trimmedQuery);
        }

        private static bool ContainsIgnoreCase(string? source, string query) =>
            !string.IsNullOrEmpty(source) &&
            source!.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;

        private void ExecuteBrowseOutputFolder()
        {
            string? initialPath = MultiModelPdfPathHelper.IsValidOutputFolder(OutputFolder)
                ? MultiModelPdfPathHelper.NormalizeFolderPath(OutputFolder)
                : null;
            string? selected = _folderDialogService.SelectFolder("Select PDF Output Folder", initialPath);
            if (!string.IsNullOrWhiteSpace(selected))
            {
                OutputFolder = selected!;
            }
        }

        private void ExecuteExport()
        {
            if (!CanExport)
            {
                return;
            }

            SaveUserPrefs();
            RequestClose?.Invoke(true);
        }

        private void LoadUserPrefs()
        {
            if (_userPrefsStore == null)
            {
                return;
            }

            MultiModelPdfExportUserPrefs prefs = _userPrefsStore.Load();

            if (string.IsNullOrWhiteSpace(_outputFolder) && !string.IsNullOrWhiteSpace(prefs.LastOutputFolder))
            {
                _outputFolder = prefs.LastOutputFolder.Trim();
            }

            if (Enum.IsDefined(typeof(MultiModelPdfFileMode), prefs.FileMode))
            {
                _fileMode = prefs.FileMode;
            }

            if (!string.IsNullOrWhiteSpace(prefs.PdfSetupName))
            {
                string targetSetupName = prefs.PdfSetupName.Trim();
                MultiModelPdfSetupOption? matchedSetup = _pdfSetups.FirstOrDefault(s =>
                    string.Equals(s.DisplayName, targetSetupName, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(s.Id, targetSetupName, StringComparison.OrdinalIgnoreCase));
                if (matchedSetup != null)
                {
                    _selectedPdfSetup = matchedSetup;
                }
            }

            if (!string.IsNullOrWhiteSpace(prefs.LastBulkPrintSetName))
            {
                string targetPrintSetName = prefs.LastBulkPrintSetName.Trim();
                _lastAppliedBulkPrintSetName = targetPrintSetName;

                MultiModelPdfBulkPrintSetOption? matchedBulkOption = _bulkPrintSetOptions.FirstOrDefault(
                    o => string.Equals(o.Name, targetPrintSetName, StringComparison.OrdinalIgnoreCase));
                if (matchedBulkOption != null)
                {
                    _selectedBulkPrintSetOption = matchedBulkOption;
                }

                foreach (MultiModelPdfExportRowViewModel row in _rows)
                {
                    if (!row.IsEligible)
                    {
                        continue;
                    }

                    MultiModelPdfPrintSetChoice? matchedChoice = row.PrintSetOptions.FirstOrDefault(
                        c => !c.IsPlaceholder &&
                             string.Equals(c.Name, targetPrintSetName, StringComparison.OrdinalIgnoreCase));
                    if (matchedChoice != null)
                    {
                        row.ApplyBulkMatchedChoice(matchedChoice);
                    }
                }
            }
        }

        private void SaveUserPrefs()
        {
            if (_userPrefsStore == null)
            {
                return;
            }

            try
            {
                _userPrefsStore.Save(new MultiModelPdfExportUserPrefs
                {
                    LastOutputFolder = MultiModelPdfPathHelper.NormalizeFolderPath(OutputFolder),
                    FileMode = FileMode,
                    PdfSetupName = SelectedPdfSetup.IsDefault ? string.Empty : SelectedPdfSetup.DisplayName,
                    LastBulkPrintSetName = ResolveBulkPrintSetNameForSave()
                });
            }
            catch (Exception)
            {
                // Local preferences persistence failure must never block export execution.
            }
        }

        private string ResolveBulkPrintSetNameForSave()
        {
            List<string> distinctCheckedSets = _rows
                .Where(r => r.IsChecked && !string.IsNullOrWhiteSpace(r.SelectedPrintSetName))
                .Select(r => r.SelectedPrintSetName!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (!string.IsNullOrWhiteSpace(_lastAppliedBulkPrintSetName))
            {
                if (distinctCheckedSets.Count == 1 &&
                    !string.Equals(distinctCheckedSets[0], _lastAppliedBulkPrintSetName, StringComparison.OrdinalIgnoreCase))
                {
                    return distinctCheckedSets[0];
                }

                return _lastAppliedBulkPrintSetName!;
            }

            return distinctCheckedSets.Count == 1 ? distinctCheckedSets[0] : string.Empty;
        }

        private void OnRowChanged()
        {
            RecomputeValidationAndSummary();
        }

        private void RecomputeValidationAndSummary()
        {
            var checkedRows = _rows.Where(r => r.IsChecked).ToList();
            int eligibleCount = _rows.Count(r => r.IsEligible);
            int checkedCount = checkedRows.Count;

            IReadOnlyDictionary<string, MultiModelPdfResolvedToken> resolvedTokens =
                MultiModelPdfPathHelper.ResolveOutputTokens(
                    checkedRows.Select(r => new MultiModelPdfTokenCandidate(r.DocumentKey, r.DocumentTitle, r.Origin)));

            foreach (MultiModelPdfExportRowViewModel row in _rows)
            {
                if (row.IsChecked && resolvedTokens.TryGetValue(row.DocumentKey, out MultiModelPdfResolvedToken? resolved))
                {
                    row.UpdateCollisionResolution(resolved, FileMode);
                }
                else
                {
                    row.UpdateCollisionResolution(null, FileMode);
                }
            }

            int totalPrintable = 0;
            int totalSkipped = 0;
            foreach (MultiModelPdfExportRowViewModel row in checkedRows)
            {
                totalPrintable += row.PrintableViewCount;
                totalSkipped += row.SkippedViewCount;
            }

            var errors = new List<MultiModelPdfValidationIssue>();
            if (checkedCount == 0)
            {
                errors.Add(new MultiModelPdfValidationIssue(
                    MultiModelPdfValidationCode.NoProjects,
                    "Select at least one project to export."));
            }

            bool folderValid = MultiModelPdfPathHelper.TryValidateOutputFolder(OutputFolder, out string folderError);
            if (!folderValid)
            {
                errors.Add(new MultiModelPdfValidationIssue(
                    MultiModelPdfValidationCode.InvalidFolder,
                    folderError,
                    "OUTPUT_FOLDER"));
            }

            foreach (MultiModelPdfExportRowViewModel row in checkedRows)
            {
                if (row.SelectedSet == null)
                {
                    errors.Add(new MultiModelPdfValidationIssue(
                        MultiModelPdfValidationCode.MissingPrintSet,
                        $"Select a Print Set for {row.DocumentTitle} (or uncheck it).",
                        row.DocumentKey));
                }
                else if (row.SelectedSet.PrintableViewCount == 0)
                {
                    errors.Add(new MultiModelPdfValidationIssue(
                        MultiModelPdfValidationCode.EmptyPrintSet,
                        $"{row.DocumentTitle}: Print Set \"{row.SelectedSet.Name}\" has 0 printable views ({row.SkippedViewCount} skipped).",
                        row.DocumentKey));
                }
                else if (folderValid)
                {
                    string token = row.ResolvedOutputToken;
                    string targetPath = FileMode == MultiModelPdfFileMode.CombinedPerDocument
                        ? MultiModelPdfPathHelper.BuildCombinedPdfPath(OutputFolder, token)
                        : MultiModelPdfPathHelper.BuildSeparateDocumentFolderPath(OutputFolder, token);
                    if (!MultiModelPdfPathHelper.IsPathWithinSafeLimit(targetPath))
                    {
                        errors.Add(new MultiModelPdfValidationIssue(
                            MultiModelPdfValidationCode.PathTooLong,
                            $"Output path exceeds {MultiModelPdfPathHelper.SafeMaxPathLength - 1} characters for {row.DocumentTitle}.",
                            row.DocumentKey));
                    }
                }
            }

            var visibleSet = new HashSet<MultiModelPdfExportRowViewModel>(_visibleRows);
            int hiddenCheckedCount = checkedRows.Count(r => !visibleSet.Contains(r));
            int visibleEligibleCount = 0;
            int checkedVisibleEligibleCount = 0;
            foreach (MultiModelPdfExportRowViewModel row in _visibleRows)
            {
                if (!row.IsEligible)
                {
                    continue;
                }

                visibleEligibleCount++;
                if (row.IsChecked)
                {
                    checkedVisibleEligibleCount++;
                }
            }

            bool hasVisibleEligibleRows = visibleEligibleCount > 0;
            bool? nextAllVisibleChecked;
            if (!hasVisibleEligibleRows || checkedVisibleEligibleCount == 0)
            {
                nextAllVisibleChecked = false;
            }
            else if (checkedVisibleEligibleCount == visibleEligibleCount)
            {
                nextAllVisibleChecked = true;
            }
            else
            {
                nextAllVisibleChecked = null;
            }

            CheckedCount = checkedCount;
            EligibleCount = eligibleCount;
            HiddenCheckedCount = hiddenCheckedCount;
            HasVisibleEligibleRows = hasVisibleEligibleRows;
            HasNoVisibleRows = _visibleRows.Count == 0;
            SetProperty(ref _allVisibleChecked, nextAllVisibleChecked, nameof(AllVisibleChecked));
            TotalPrintableViews = totalPrintable;
            TotalSkippedViews = totalSkipped;
            SelectionSummaryText = hiddenCheckedCount > 0
                ? $"{checkedCount} selected ({hiddenCheckedCount} hidden)"
                : $"{checkedCount} of {eligibleCount} eligible";
            ValidationErrors = errors.AsReadOnly();

            var bannerErrors = errors
                .Where(e => e.Code != MultiModelPdfValidationCode.NoProjects &&
                            !(checkedCount == 0 &&
                              e.Code == MultiModelPdfValidationCode.InvalidFolder &&
                              string.IsNullOrWhiteSpace(OutputFolder)))
                .ToList();

            HasErrorBanner = bannerErrors.Count > 0;
            PrimaryErrorBannerText = bannerErrors.Count == 0
                ? string.Empty
                : (bannerErrors.Count == 1
                    ? bannerErrors[0].Message
                    : $"{bannerErrors[0].Message} (+{bannerErrors.Count - 1} more)");

            ShowFolderErrorBorder = !folderValid &&
                                    (checkedCount > 0 || !string.IsNullOrWhiteSpace(OutputFolder));

            bool canExport = checkedCount > 0 && errors.Count == 0;
            CanExport = canExport;

            int plannedPdfCount = FileMode == MultiModelPdfFileMode.CombinedPerDocument
                ? checkedCount
                : totalPrintable;

            ExportButtonText = canExport
                ? (plannedPdfCount == 1 ? "Export 1 PDF" : $"Export {plannedPdfCount} PDFs")
                : "Export PDFs";

            string projectWord = checkedCount == 1 ? "project" : "projects";
            string viewWord = totalPrintable == 1 ? "view" : "views";

            if (checkedCount == 0)
            {
                FooterStatusKind = MultiModelPdfFooterStatusKind.Muted;
                FooterStatusText = "Select at least one project to export";
            }
            else if (totalSkipped > 0)
            {
                FooterStatusKind = MultiModelPdfFooterStatusKind.Warning;
                FooterStatusText = $"{checkedCount} {projectWord} · {totalPrintable} {viewWord} · {totalSkipped} skipped";
            }
            else
            {
                FooterStatusKind = MultiModelPdfFooterStatusKind.Success;
                FooterStatusText = $"{checkedCount} {projectWord} · {totalPrintable} {viewWord} ready";
            }

            OutputPreviewText = BuildOutputPreviewText(checkedRows, folderValid, totalPrintable);

            ShowUncheckUnmatchedButton = HasBulkFeedbackBanner &&
                                         BulkFeedbackKind == MultiModelPdfBulkFeedbackKind.Warning &&
                                         _rows.Any(r => r.IsChecked && r.HasBulkMismatchAccent);
            CommandManager.InvalidateRequerySuggested();
        }

        private string BuildOutputPreviewText(
            IReadOnlyList<MultiModelPdfExportRowViewModel> checkedRows,
            bool folderValid,
            int totalPrintable)
        {
            if (checkedRows.Count == 0)
            {
                return "Check at least one project to preview output path.";
            }

            string firstToken = checkedRows[0].ResolvedOutputToken;
            string folderDisplay = folderValid
                ? MultiModelPdfPathHelper.NormalizeFolderPath(OutputFolder)
                : (string.IsNullOrWhiteSpace(OutputFolder)
                    ? "<OutputFolder>"
                    : OutputFolder.Trim().TrimEnd('\\', '/'));
            string separator = folderDisplay.EndsWith("\\", StringComparison.Ordinal) ? string.Empty : "\\";

            if (FileMode == MultiModelPdfFileMode.CombinedPerDocument)
            {
                string extra = checkedRows.Count > 1
                    ? $" (+{checkedRows.Count - 1} more PDF{(checkedRows.Count - 1 == 1 ? "" : "s")})"
                    : string.Empty;
                return $"{folderDisplay}{separator}{firstToken}.pdf{extra}";
            }

            return $"{folderDisplay}{separator}{firstToken}\\<NamingRule>.pdf ({totalPrintable} PDF{(totalPrintable == 1 ? "" : "s")} in {checkedRows.Count} folder{(checkedRows.Count == 1 ? "" : "s")})";
        }

        private static int GetOriginOrder(MultiModelPdfDocumentOrigin origin) => origin switch
        {
            MultiModelPdfDocumentOrigin.Active => 0,
            MultiModelPdfDocumentOrigin.Linked => 1,
            _ => 2
        };

        private static IReadOnlyList<MultiModelPdfBulkPrintSetOption> BuildBulkPrintSetOptions(
            IReadOnlyList<MultiModelPdfExportRowViewModel> rows)
        {
            int totalCount = rows.Count;
            if (totalCount == 0)
            {
                return Array.Empty<MultiModelPdfBulkPrintSetOption>();
            }

            var activeSetNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            MultiModelPdfExportRowViewModel? activeRow = rows.FirstOrDefault(r => r.Origin == MultiModelPdfDocumentOrigin.Active);
            if (activeRow != null && activeRow.IsEligible)
            {
                foreach (MultiModelPdfSetSnapshot set in activeRow.PrintSets)
                {
                    activeSetNames.Add(set.Name);
                }
            }

            var canonicalNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var matchCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (MultiModelPdfExportRowViewModel row in rows)
            {
                if (!row.IsEligible)
                {
                    continue;
                }

                var seenInRow = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (MultiModelPdfSetSnapshot set in row.PrintSets)
                {
                    if (!seenInRow.Add(set.Name))
                    {
                        continue;
                    }

                    if (!canonicalNames.ContainsKey(set.Name))
                    {
                        canonicalNames[set.Name] = set.Name;
                        matchCounts[set.Name] = 0;
                    }

                    matchCounts[set.Name]++;
                }
            }

            return canonicalNames.Values
                .Select(name => new MultiModelPdfBulkPrintSetOption(
                    name: name,
                    matchCount: matchCounts[name],
                    totalCount: totalCount,
                    isInActiveDocument: activeSetNames.Contains(name)))
                .OrderByDescending(o => o.IsInActiveDocument)
                .ThenByDescending(o => o.MatchCount)
                .ThenBy(o => o.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(o => o.Name, StringComparer.Ordinal)
                .ToList();
        }
    }
}
