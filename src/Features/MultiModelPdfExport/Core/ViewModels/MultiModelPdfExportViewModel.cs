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
        private readonly ObservableCollection<MultiModelPdfExportRowViewModel> _rows =
            new ObservableCollection<MultiModelPdfExportRowViewModel>();
        private readonly ObservableCollection<MultiModelPdfSetupOption> _pdfSetups =
            new ObservableCollection<MultiModelPdfSetupOption>();

        private MultiModelPdfSetupOption _selectedPdfSetup;
        private MultiModelPdfFileMode _fileMode = MultiModelPdfFileMode.CombinedPerDocument;
        private string _outputFolder = string.Empty;
        private IReadOnlyList<MultiModelPdfValidationIssue> _validationErrors = Array.Empty<MultiModelPdfValidationIssue>();
        private int _checkedCount;
        private int _eligibleCount;
        private int _totalPrintableViews;
        private int _totalSkippedViews;
        private bool _canExport;
        private string _exportButtonText = "Export PDFs";
        private string _selectionSummaryText = "0 of 0 eligible";
        private string _outputPreviewText = string.Empty;
        private string _primaryErrorBannerText = string.Empty;
        private bool _hasErrorBanner;
        private bool _showFolderErrorBorder;
        private string _footerStatusText = "Select at least one project to export";
        private MultiModelPdfFooterStatusKind _footerStatusKind = MultiModelPdfFooterStatusKind.Muted;

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
            string? initialOutputFolder = null)
        {
            _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
            _folderDialogService = folderDialogService ?? throw new ArgumentNullException(nameof(folderDialogService));

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

            BrowseOutputFolderCommand = new RelayCommand(_ => ExecuteBrowseOutputFolder());
            ExportCommand = new RelayCommand(_ => ExecuteExport(), _ => CanExport);
            CancelCommand = new RelayCommand(_ => RequestClose?.Invoke(false));

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

            RequestClose?.Invoke(true);
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

            CheckedCount = checkedCount;
            EligibleCount = eligibleCount;
            TotalPrintableViews = totalPrintable;
            TotalSkippedViews = totalSkipped;
            SelectionSummaryText = $"{checkedCount} of {eligibleCount} eligible";
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
    }
}
