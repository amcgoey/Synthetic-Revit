using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows.Input;
using Synthetic.Modules.ShadowStudy.Adapters;
using Synthetic.Modules.ShadowStudy.Models;
using Synthetic.Modules.ShadowStudy.Services;
using Synthetic.Shared.UI;

namespace Synthetic.Modules.ShadowStudy.ViewModels
{
    /// <summary>
    /// Presentation ViewModel for the Shadow Study composite exporter dialog, supporting both
    /// Current View and multi-view Selected Views batch export.
    /// </summary>
    public class ShadowStudyViewModel : ViewModelBase
    {
        /// <summary>
        /// Represents a selectable clock-aligned time increment option.
        /// </summary>
        public sealed class ShadowStudyTimeIncrementOption
        {
            /// <summary>
            /// Initializes a new instance of the <see cref="ShadowStudyTimeIncrementOption"/> class.
            /// </summary>
            public ShadowStudyTimeIncrementOption(int minutes, string label)
            {
                Minutes = minutes;
                Label = label;
            }

            /// <summary>Gets the step duration in minutes.</summary>
            public int Minutes { get; }

            /// <summary>Gets the human-readable label.</summary>
            public string Label { get; }
        }

        /// <summary>Minimum allowed solar altitude threshold in degrees.</summary>
        public const double MinAllowedSunAltitudeDegrees = 0.0;

        /// <summary>Maximum allowed solar altitude threshold in degrees.</summary>
        public const double MaxAllowedSunAltitudeDegrees = 90.0;

        /// <summary>Minimum allowed composite darkness percentage.</summary>
        public const int MinAllowedDarknessPercent = 1;

        /// <summary>Maximum allowed composite darkness percentage.</summary>
        public const int MaxAllowedDarknessPercent = 95;

        /// <summary>Minimum allowed horizontal export pixel width.</summary>
        public const int MinAllowedPixelWidth = 64;

        /// <summary>Maximum allowed horizontal export pixel width.</summary>
        public const int MaxAllowedPixelWidth = 15000;

        private static readonly IReadOnlyList<ShadowStudyTimeIncrementOption> DefaultTimeIncrements =
            new[]
            {
                new ShadowStudyTimeIncrementOption(15, "15 min"),
                new ShadowStudyTimeIncrementOption(30, "30 min"),
                new ShadowStudyTimeIncrementOption(60, "1 hr"),
                new ShadowStudyTimeIncrementOption(120, "2 hr")
            };

        private readonly IShadowStudyAdapter _adapter;
        private readonly IFolderDialogService _folderDialogService;
        private readonly List<ShadowStudySelectableViewItemViewModel> _allViews;
        private readonly ShadowStudyUserPrefsStore? _userPrefsStore;

        private ShadowStudyExportRangeMode _exportRangeMode;
        private string _viewSearchText = string.Empty;
        private DateTime _selectedDate;
        private int _timeStepMinutes = 60;
        private double _minSunAltitudeDegrees = 5.0;
        private string _minSunAltitudeText = "5";
        private int _maxCompositeDarknessPercent = 80;
        private string _maxCompositeDarknessText = "80";
        private int _pixelWidth = 3000;
        private string _pixelWidthText = "3000";
        private bool _forceHiddenLine = true;
        private bool _forceWhiteBackground = true;
        private bool _exportIndividualFrames = true;
        private string _outputFolder = string.Empty;
        private DateSolarSchedule _currentSchedule = new DateSolarSchedule();
        private string _timeStepError = string.Empty;
        private string _minSunAltitudeError = string.Empty;
        private string _maxCompositeDarknessError = string.Empty;
        private string _pixelWidthError = string.Empty;

        /// <summary>
        /// Raised when the dialog should close (<c>true</c> = Export confirmed, <c>false</c> = Cancelled).
        /// </summary>
        public event Action<bool>? RequestClose;

        /// <summary>
        /// Initializes a new instance of the <see cref="ShadowStudyViewModel"/> class.
        /// </summary>
        public ShadowStudyViewModel(
            IShadowStudyAdapter adapter,
            IFolderDialogService folderDialogService,
            DateTime? initialDate = null,
            ShadowStudyUserPrefsStore? userPrefsStore = null)
        {
            _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
            _folderDialogService = folderDialogService ?? throw new ArgumentNullException(nameof(folderDialogService));
            _userPrefsStore = userPrefsStore ?? (ProgressCoordinator.SuppressUI ? null : new ShadowStudyUserPrefsStore());

            SiteInfo = _adapter.GetSiteInfo() ?? new ShadowStudySiteInfo();
            ActiveView = _adapter.GetActiveViewInfo();
            if (ActiveView != null)
            {
                ShadowStudyPathHelper.AssignViewLabels(new[] { ActiveView });
            }

            IReadOnlyList<ShadowStudyViewItem> eligibleViews = _adapter.GetEligibleViews() ?? Array.Empty<ShadowStudyViewItem>();
            _allViews = eligibleViews
                .Where(v => v != null)
                .Select(v => new ShadowStudySelectableViewItemViewModel(v, OnViewSelectionChanged))
                .ToList();

            IsCurrentViewEligible = ActiveView != null;
            _exportRangeMode = IsCurrentViewEligible
                ? ShadowStudyExportRangeMode.CurrentView
                : ShadowStudyExportRangeMode.SelectedViews;

            FilteredViews = new ObservableCollection<ShadowStudySelectableViewItemViewModel>();
            RefreshFilteredViews();

            _selectedDate = (initialDate ?? DateTime.Today).Date;
            LoadUserPrefs();

            BrowseOutputFolderCommand = new RelayCommand(_ => ExecuteBrowseOutputFolder());
            SelectAllViewsCommand = new RelayCommand(_ => ExecuteSelectAllViews());
            SelectNoneViewsCommand = new RelayCommand(_ => ExecuteSelectNoneViews());
            ExportCommand = new RelayCommand(_ => ExecuteExport(), _ => CanExport);
            CancelCommand = new RelayCommand(_ => RequestClose?.Invoke(false));

            RecalculateSchedule();
        }

        /// <summary>
        /// Gets the available clock-aligned time increment choices.
        /// </summary>
        public IReadOnlyList<ShadowStudyTimeIncrementOption> TimeIncrementOptions => DefaultTimeIncrements;

        /// <summary>
        /// Gets or sets the editable minimum sun altitude text.
        /// </summary>
        public string MinSunAltitudeText
        {
            get => _minSunAltitudeText;
            set
            {
                string normalized = value ?? string.Empty;
                if (SetProperty(ref _minSunAltitudeText, normalized))
                {
                    ApplyMinSunAltitudeText(normalized);
                }
            }
        }

        /// <summary>
        /// Gets or sets the editable maximum composite darkness text.
        /// </summary>
        public string MaxCompositeDarknessText
        {
            get => _maxCompositeDarknessText;
            set
            {
                string normalized = value ?? string.Empty;
                if (SetProperty(ref _maxCompositeDarknessText, normalized))
                {
                    ApplyMaxCompositeDarknessText(normalized);
                }
            }
        }

        /// <summary>
        /// Gets or sets the editable pixel width text.
        /// </summary>
        public string PixelWidthText
        {
            get => _pixelWidthText;
            set
            {
                string normalized = value ?? string.Empty;
                if (SetProperty(ref _pixelWidthText, normalized))
                {
                    ApplyPixelWidthText(normalized);
                }
            }
        }

        /// <summary>
        /// Gets or sets a value indicating whether individual Shadow Frame PNGs are exported (<c>true</c> default).
        /// </summary>
        public bool ExportIndividualFrames
        {
            get => _exportIndividualFrames;
            set => SetProperty(ref _exportIndividualFrames, value);
        }

        /// <summary>
        /// Gets a value indicating whether any schedule or output input has validation errors.
        /// </summary>
        public bool HasValidationErrors =>
            !string.IsNullOrEmpty(_timeStepError) ||
            !string.IsNullOrEmpty(_minSunAltitudeError) ||
            !string.IsNullOrEmpty(_maxCompositeDarknessError) ||
            !string.IsNullOrEmpty(_pixelWidthError);

        /// <summary>
        /// Gets the inline validation error message when <see cref="HasValidationErrors"/> is true.
        /// </summary>
        public string ValidationErrorMessage
        {
            get
            {
                var errors = new List<string>(4);
                if (!string.IsNullOrEmpty(_timeStepError)) errors.Add(_timeStepError);
                if (!string.IsNullOrEmpty(_minSunAltitudeError)) errors.Add(_minSunAltitudeError);
                if (!string.IsNullOrEmpty(_maxCompositeDarknessError)) errors.Add(_maxCompositeDarknessError);
                if (!string.IsNullOrEmpty(_pixelWidthError)) errors.Add(_pixelWidthError);
                return string.Join(" ", errors);
            }
        }

        /// <summary>
        /// Gets the peak solar altitude in degrees for <see cref="SelectedDate"/>.
        /// </summary>
        public double PeakSolarAltitudeDegrees => CurrentSchedule.PeakSolarAltitudeDegrees;

        /// <summary>
        /// Gets the zero-frames warning text including peak solar altitude.
        /// </summary>
        public string ZeroFramesWarningText => string.Format(
            CultureInfo.InvariantCulture,
            "0 valid daylight frames meet >= {0:0.##}° (Peak solar altitude: {1:F1}°). Export is disabled.",
            MinSunAltitudeDegrees,
            PeakSolarAltitudeDegrees);

        /// <summary>
        /// Gets a value indicating whether the normal schedule preview should be shown.
        /// </summary>
        public bool ShowSchedulePreview => !HasZeroFrames && !HasValidationErrors;

        /// <summary>
        /// Gets a value indicating whether the zero-frames warning badge should be shown.
        /// </summary>
        public bool ShowZeroFramesWarning => HasZeroFrames && !HasValidationErrors;

        /// <summary>
        /// Gets the previewed individual frame subfolder path segment.
        /// </summary>
        public string FrameFolderPreview
        {
            get
            {
                string dateLabel = ShadowStudyPathHelper.FormatDateLabel(SelectedDate);
                if (ExportRangeMode == ShadowStudyExportRangeMode.CurrentView)
                {
                    string viewLabel = ActiveView?.ViewLabel ?? "View";
                    return ShadowStudyPathHelper.BuildFrameFolderName(viewLabel, dateLabel) + "\\";
                }

                var selectedCopies = _allViews
                    .Where(v => v.IsSelected)
                    .Select(v => CloneViewItem(v.ViewItem))
                    .ToList();

                if (selectedCopies.Count == 0)
                {
                    return ShadowStudyPathHelper.BuildFrameFolderName("View", dateLabel) + "\\";
                }

                ShadowStudyPathHelper.AssignViewLabels(selectedCopies);
                string firstFolder = ShadowStudyPathHelper.BuildFrameFolderName(selectedCopies[0].ViewLabel, dateLabel) + "\\";
                return selectedCopies.Count == 1
                    ? firstFolder
                    : $"{firstFolder} (+{selectedCopies.Count - 1} more)";
            }
        }

        /// <summary>
        /// Gets the active shadow-capable view item, or <c>null</c> when launched from an ineligible view (such as a Sheet or Schedule).
        /// </summary>
        public ShadowStudyViewItem? ActiveView { get; }

        /// <summary>
        /// Gets a value indicating whether the active view is eligible for Current View export.
        /// </summary>
        public bool IsCurrentViewEligible { get; }

        /// <summary>
        /// Gets or sets the active export range mode (<see cref="ShadowStudyExportRangeMode.CurrentView"/> or <see cref="ShadowStudyExportRangeMode.SelectedViews"/>).
        /// Switching to <see cref="ShadowStudyExportRangeMode.CurrentView"/> is ignored when <see cref="IsCurrentViewEligible"/> is <c>false</c>.
        /// </summary>
        public ShadowStudyExportRangeMode ExportRangeMode
        {
            get => _exportRangeMode;
            set
            {
                if (value == ShadowStudyExportRangeMode.CurrentView && !IsCurrentViewEligible)
                {
                    return;
                }

                if (SetProperty(ref _exportRangeMode, value))
                {
                    OnPropertyChanged(nameof(IsCurrentViewMode));
                    OnPropertyChanged(nameof(IsSelectedViewsMode));
                    OnPropertyChanged(nameof(CompositeFileNamePreview));
                    OnPropertyChanged(nameof(FrameFolderPreview));
                    RaiseExportStateChanged();
                }
            }
        }

        /// <summary>
        /// Gets a value indicating whether <see cref="ExportRangeMode"/> is <see cref="ShadowStudyExportRangeMode.CurrentView"/>.
        /// </summary>
        public bool IsCurrentViewMode => ExportRangeMode == ShadowStudyExportRangeMode.CurrentView;

        /// <summary>
        /// Gets a value indicating whether <see cref="ExportRangeMode"/> is <see cref="ShadowStudyExportRangeMode.SelectedViews"/>.
        /// </summary>
        public bool IsSelectedViewsMode => ExportRangeMode == ShadowStudyExportRangeMode.SelectedViews;

        /// <summary>
        /// Gets all eligible views wrapped for multi-view selection.
        /// </summary>
        public IReadOnlyList<ShadowStudySelectableViewItemViewModel> AllViews => _allViews.AsReadOnly();

        /// <summary>
        /// Gets the eligible views matching <see cref="ViewSearchText"/>.
        /// </summary>
        public ObservableCollection<ShadowStudySelectableViewItemViewModel> FilteredViews { get; }

        /// <summary>
        /// Gets or sets the case-insensitive search filter applied to view names and view types.
        /// </summary>
        public string ViewSearchText
        {
            get => _viewSearchText;
            set
            {
                string normalized = value ?? string.Empty;
                if (SetProperty(ref _viewSearchText, normalized))
                {
                    RefreshFilteredViews();
                }
            }
        }

        /// <summary>
        /// Gets the number of views currently checked in the Selected Views checklist.
        /// </summary>
        public int SelectedViewsCount => _allViews.Count(v => v.IsSelected);

        /// <summary>
        /// Gets the total number of eligible shadow-capable views in the project.
        /// </summary>
        public int TotalEligibleViewsCount => _allViews.Count;

        /// <summary>
        /// Gets a summary string showing how many eligible views are selected.
        /// </summary>
        public string SelectedViewsSummary => $"{SelectedViewsCount} of {TotalEligibleViewsCount} selected";

        /// <summary>
        /// Gets the project site location metadata.
        /// </summary>
        public ShadowStudySiteInfo SiteInfo { get; }

        /// <summary>
        /// Gets the formatted display string for the active view.
        /// </summary>
        public string ActiveViewDisplayName => ActiveView == null
            ? "No eligible active view"
            : $"{ActiveView.Name} ({ActiveView.ViewType})";

        /// <summary>
        /// Gets non-blocking view warning badges (such as Uncropped or Wireframe/Rendering).
        /// </summary>
        public IReadOnlyList<ShadowStudyViewWarningBadge> ViewWarnings =>
            ActiveView?.Warnings ?? (IReadOnlyList<ShadowStudyViewWarningBadge>)Array.Empty<ShadowStudyViewWarningBadge>();

        /// <summary>
        /// Gets a value indicating whether the active view has any non-blocking warnings.
        /// </summary>
        public bool HasViewWarnings => ActiveView != null && ActiveView.HasWarnings;

        /// <summary>
        /// Gets the displayable project site place name.
        /// </summary>
        public string SitePlaceName => string.IsNullOrWhiteSpace(SiteInfo.PlaceName) ? "(Unnamed Project Site)" : SiteInfo.PlaceName;

        /// <summary>
        /// Gets the formatted latitude string in degrees.
        /// </summary>
        public string LatitudeDisplay => SiteInfo.FormattedLatitude;

        /// <summary>
        /// Gets the formatted longitude string in degrees.
        /// </summary>
        public string LongitudeDisplay => SiteInfo.FormattedLongitude;

        /// <summary>
        /// Gets the site hemisphere label.
        /// </summary>
        public string HemisphereDisplay => SiteInfo.HemisphereLabel;

        /// <summary>
        /// Gets the formatted UTC timezone offset string.
        /// </summary>
        public string TimeZoneDisplay => SiteInfo.FormattedTimeZone;

        /// <summary>
        /// Gets the Daylight Saving Time status display string.
        /// </summary>
        public string DstStatusDisplay => SiteInfo.UsesDst ? "Enabled (+1 hr)" : "Disabled";

        /// <summary>
        /// Gets or sets the selected study calendar date.
        /// </summary>
        public DateTime SelectedDate
        {
            get => _selectedDate;
            set
            {
                DateTime dateOnly = value.Date;
                if (SetProperty(ref _selectedDate, dateOnly))
                {
                    RecalculateSchedule();
                }
            }
        }

        /// <summary>
        /// Gets or sets the frame step increment in minutes (<c>60</c> default).
        /// </summary>
        public int TimeStepMinutes
        {
            get => _timeStepMinutes;
            set
            {
                if (SetProperty(ref _timeStepMinutes, value))
                {
                    ValidateTimeStep(value);
                    if (string.IsNullOrEmpty(_timeStepError))
                    {
                        RecalculateSchedule();
                    }
                    else
                    {
                        RaiseValidationStateChanged();
                    }
                }
            }
        }

        /// <summary>
        /// Gets or sets the minimum solar altitude threshold in degrees (<c>5.0</c> default).
        /// </summary>
        public double MinSunAltitudeDegrees
        {
            get => _minSunAltitudeDegrees;
            set
            {
                if (SetProperty(ref _minSunAltitudeDegrees, value))
                {
                    _minSunAltitudeText = double.IsNaN(value) || double.IsInfinity(value)
                        ? string.Empty
                        : value.ToString("0.##", CultureInfo.InvariantCulture);
                    OnPropertyChanged(nameof(MinSunAltitudeText));
                    ValidateMinSunAltitude(value);
                    if (string.IsNullOrEmpty(_minSunAltitudeError))
                    {
                        RecalculateSchedule();
                    }
                    else
                    {
                        RaiseValidationStateChanged();
                    }
                }
            }
        }

        /// <summary>
        /// Gets or sets the target maximum composite darkness percentage (<c>80</c> default).
        /// </summary>
        public int MaxCompositeDarknessPercent
        {
            get => _maxCompositeDarknessPercent;
            set
            {
                if (SetProperty(ref _maxCompositeDarknessPercent, value))
                {
                    _maxCompositeDarknessText = value.ToString(CultureInfo.InvariantCulture);
                    OnPropertyChanged(nameof(MaxCompositeDarknessText));
                    ValidateMaxCompositeDarkness(value);
                    if (string.IsNullOrEmpty(_maxCompositeDarknessError))
                    {
                        RecalculateSchedule();
                    }
                    else
                    {
                        RaiseValidationStateChanged();
                    }
                }
            }
        }

        /// <summary>
        /// Gets or sets the horizontal export pixel width (<c>3000</c> default).
        /// </summary>
        public int PixelWidth
        {
            get => _pixelWidth;
            set
            {
                if (SetProperty(ref _pixelWidth, value))
                {
                    _pixelWidthText = value.ToString(CultureInfo.InvariantCulture);
                    OnPropertyChanged(nameof(PixelWidthText));
                    ValidatePixelWidth(value);
                    RaiseValidationStateChanged();
                }
            }
        }

        /// <summary>
        /// Gets or sets a value indicating whether each view's existing Visual Style (and background) is preserved during export (<c>false</c> default).
        /// Wireframe and Rendering views still fall back to Hidden Line on a pure white background.
        /// </summary>
        public bool PreserveVisualStyle
        {
            get => !_forceHiddenLine;
            set
            {
                bool forceHlr = !value;
                if (SetProperty(ref _forceHiddenLine, forceHlr, nameof(ForceHiddenLine)))
                {
                    OnPropertyChanged(nameof(PreserveVisualStyle));
                    OnPropertyChanged(nameof(VisualStyleSummaryText));
                }
            }
        }

        /// <summary>
        /// Gets or sets a value indicating whether views are temporarily rendered in Hidden Line (<c>HLR</c>).
        /// </summary>
        public bool ForceHiddenLine
        {
            get => _forceHiddenLine;
            set
            {
                if (SetProperty(ref _forceHiddenLine, value))
                {
                    OnPropertyChanged(nameof(PreserveVisualStyle));
                    OnPropertyChanged(nameof(VisualStyleSummaryText));
                }
            }
        }

        /// <summary>
        /// Gets the concise visual style summary shown beneath Output Settings.
        /// </summary>
        public string VisualStyleSummaryText => PreserveVisualStyle
            ? "Preserves each view's Visual Style and background (Wireframe/Rendering still use Hidden Line on pure white)."
            : "Hidden Line (HLR) · Pure white background (3D/Section/Elevation)";

        /// <summary>
        /// Gets or sets a value indicating whether supported views use a pure white background.
        /// </summary>
        public bool ForceWhiteBackground
        {
            get => _forceWhiteBackground;
            set => SetProperty(ref _forceWhiteBackground, value);
        }

        /// <summary>
        /// Gets the computed solar schedule for <see cref="SelectedDate"/>.
        /// </summary>
        public DateSolarSchedule CurrentSchedule
        {
            get => _currentSchedule;
            private set => SetProperty(ref _currentSchedule, value);
        }

        /// <summary>
        /// Gets the count of valid daylight frames in <see cref="CurrentSchedule"/>.
        /// </summary>
        public int ValidFrameCount => CurrentSchedule.Frames.Count;

        /// <summary>
        /// Gets a value indicating whether zero valid daylight frames exist for the selected date.
        /// </summary>
        public bool HasZeroFrames => ValidFrameCount == 0;

        /// <summary>
        /// Gets the live schedule preview summary text.
        /// </summary>
        public string SchedulePreviewText => CurrentSchedule.WindowSummary;

        /// <summary>
        /// Gets the previewed output composite filename.
        /// </summary>
        public string CompositeFileNamePreview
        {
            get
            {
                string dateLabel = ShadowStudyPathHelper.FormatDateLabel(SelectedDate);
                if (ExportRangeMode == ShadowStudyExportRangeMode.CurrentView)
                {
                    string viewLabel = ActiveView?.ViewLabel ?? "View";
                    return ShadowStudyPathHelper.BuildCompositeFileName(viewLabel, dateLabel);
                }

                var selectedCopies = _allViews
                    .Where(v => v.IsSelected)
                    .Select(v => CloneViewItem(v.ViewItem))
                    .ToList();

                if (selectedCopies.Count == 0)
                {
                    return "Select at least 1 view to preview output filename";
                }

                ShadowStudyPathHelper.AssignViewLabels(selectedCopies);
                string firstFileName = ShadowStudyPathHelper.BuildCompositeFileName(selectedCopies[0].ViewLabel, dateLabel);
                return selectedCopies.Count == 1
                    ? firstFileName
                    : $"{firstFileName} (+{selectedCopies.Count - 1} more)";
            }
        }

        /// <summary>
        /// Gets or sets the destination output folder path.
        /// </summary>
        public string OutputFolder
        {
            get => _outputFolder;
            set
            {
                string normalized = value ?? string.Empty;
                if (SetProperty(ref _outputFolder, normalized))
                {
                    RaiseExportStateChanged();
                }
            }
        }

        /// <summary>
        /// Gets a value indicating whether Export can currently execute.
        /// Requires at least one valid target view for the active mode, a non-blank output folder,
        /// positive pixel width, and at least one valid daylight frame.
        /// </summary>
        public bool CanExport =>
            HasValidTargetViews() &&
            !string.IsNullOrWhiteSpace(OutputFolder) &&
            !HasValidationErrors &&
            ValidFrameCount > 0;

        /// <summary>
        /// Gets the command that opens the folder browser dialog.
        /// </summary>
        public ICommand BrowseOutputFolderCommand { get; }

        /// <summary>
        /// Gets the command that checks all views currently visible in <see cref="FilteredViews"/>.
        /// </summary>
        public ICommand SelectAllViewsCommand { get; }

        /// <summary>
        /// Gets the command that unchecks all views currently visible in <see cref="FilteredViews"/>.
        /// </summary>
        public ICommand SelectNoneViewsCommand { get; }

        /// <summary>
        /// Gets the command that confirms settings and starts the export.
        /// </summary>
        public ICommand ExportCommand { get; }

        /// <summary>
        /// Gets the command that cancels and closes the dialog.
        /// </summary>
        public ICommand CancelCommand { get; }

        /// <summary>
        /// Builds the headless <see cref="ShadowStudyRunRequest"/> for the configured view(s), date, and output settings.
        /// </summary>
        public ShadowStudyRunRequest BuildRunRequest()
        {
            IReadOnlyList<ShadowStudyViewItem> targetViews;
            if (ExportRangeMode == ShadowStudyExportRangeMode.CurrentView)
            {
                if (ActiveView == null)
                {
                    throw new InvalidOperationException("Cannot build a Shadow Study run request without an eligible active view.");
                }

                ShadowStudyViewItem single = CloneViewItem(ActiveView);
                ShadowStudyPathHelper.AssignViewLabels(new[] { single });
                targetViews = new[] { single };
            }
            else
            {
                var checkedViews = _allViews
                    .Where(v => v.IsSelected)
                    .Select(v => CloneViewItem(v.ViewItem))
                    .ToList();

                if (checkedViews.Count == 0)
                {
                    throw new InvalidOperationException("Cannot build a Shadow Study run request without at least one selected view.");
                }

                ShadowStudyPathHelper.AssignViewLabels(checkedViews);
                targetViews = checkedViews.AsReadOnly();
            }

            return new ShadowStudyRunRequest
            {
                Views = targetViews,
                DateSchedules = new[] { CurrentSchedule },
                OutputFolder = OutputFolder.Trim(),
                PixelWidth = PixelWidth,
                MaxCompositeDarknessPercent = MaxCompositeDarknessPercent,
                ForceHiddenLine = ForceHiddenLine,
                ForceWhiteBackground = ForceWhiteBackground,
                KeepIndividualFrames = ExportIndividualFrames
            };
        }

        private bool HasValidTargetViews()
        {
            return ExportRangeMode == ShadowStudyExportRangeMode.CurrentView
                ? ActiveView != null
                : SelectedViewsCount > 0;
        }

        private void RefreshFilteredViews()
        {
            FilteredViews.Clear();
            string term = _viewSearchText.Trim();
            foreach (var item in _allViews)
            {
                if (string.IsNullOrEmpty(term) ||
                    item.Name.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    item.ViewType.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    FilteredViews.Add(item);
                }
            }
        }

        private void ExecuteSelectAllViews()
        {
            foreach (var view in FilteredViews)
            {
                view.IsSelected = true;
            }
        }

        private void ExecuteSelectNoneViews()
        {
            foreach (var view in FilteredViews)
            {
                view.IsSelected = false;
            }
        }

        private void OnViewSelectionChanged()
        {
            OnPropertyChanged(nameof(SelectedViewsCount));
            OnPropertyChanged(nameof(SelectedViewsSummary));
            OnPropertyChanged(nameof(CompositeFileNamePreview));
            OnPropertyChanged(nameof(FrameFolderPreview));
            RaiseExportStateChanged();
        }

        private static ShadowStudyViewItem CloneViewItem(ShadowStudyViewItem source)
        {
            return new ShadowStudyViewItem
            {
                Id = source.Id,
                Name = source.Name,
                ViewType = source.ViewType,
                ViewLabel = source.ViewLabel,
                IsCropped = source.IsCropped,
                DisplayStyleName = source.DisplayStyleName,
                HasControllingTemplate = source.HasControllingTemplate,
                IsBackgroundSupported = source.IsBackgroundSupported,
                Warnings = source.Warnings != null
                    ? new List<ShadowStudyViewWarningBadge>(source.Warnings)
                    : new List<ShadowStudyViewWarningBadge>()
            };
        }

        private void ExecuteBrowseOutputFolder()
        {
            string? initialPath = string.IsNullOrWhiteSpace(OutputFolder) ? null : OutputFolder.Trim();
            string? selected = _folderDialogService.SelectFolder("Select Output Folder for Shadow Composite", initialPath);
            if (!string.IsNullOrWhiteSpace(selected))
            {
                OutputFolder = selected!;
            }
        }

        private void ExecuteExport()
        {
            if (CanExport)
            {
                SaveUserPrefs();
                RequestClose?.Invoke(true);
            }
        }

        private void LoadUserPrefs()
        {
            if (_userPrefsStore == null)
            {
                return;
            }

            ShadowStudyUserPrefs prefs = _userPrefsStore.Load();
            _outputFolder = prefs.LastOutputFolder ?? string.Empty;
            _pixelWidth = (prefs.PixelWidth >= MinAllowedPixelWidth && prefs.PixelWidth <= MaxAllowedPixelWidth)
                ? prefs.PixelWidth
                : 3000;
            _pixelWidthText = _pixelWidth.ToString(CultureInfo.InvariantCulture);
            _exportIndividualFrames = prefs.ExportIndividualFrames;
            _forceHiddenLine = !prefs.PreserveVisualStyle;
        }

        private void SaveUserPrefs()
        {
            if (_userPrefsStore == null)
            {
                return;
            }

            try
            {
                _userPrefsStore.Save(new ShadowStudyUserPrefs
                {
                    LastOutputFolder = OutputFolder.Trim(),
                    PixelWidth = PixelWidth,
                    ExportIndividualFrames = ExportIndividualFrames,
                    PreserveVisualStyle = PreserveVisualStyle
                });
            }
            catch
            {
                // Local preferences persistence failure must never block export execution
            }
        }

        private void ApplyMinSunAltitudeText(string text)
        {
            if (!TryParseDouble(text, out double parsed))
            {
                _minSunAltitudeError = $"Minimum sun altitude must be a number between {MinAllowedSunAltitudeDegrees:0}° and {MaxAllowedSunAltitudeDegrees:0}°.";
                RaiseValidationStateChanged();
                return;
            }

            if (SetProperty(ref _minSunAltitudeDegrees, parsed, nameof(MinSunAltitudeDegrees)))
            {
                // Keep user text untouched while typing
            }

            ValidateMinSunAltitude(parsed);
            if (string.IsNullOrEmpty(_minSunAltitudeError))
            {
                RecalculateSchedule();
            }
            else
            {
                RaiseValidationStateChanged();
            }
        }

        private void ApplyMaxCompositeDarknessText(string text)
        {
            if (!int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
            {
                _maxCompositeDarknessError = $"Maximum composite darkness must be an integer between {MinAllowedDarknessPercent}% and {MaxAllowedDarknessPercent}%.";
                RaiseValidationStateChanged();
                return;
            }

            SetProperty(ref _maxCompositeDarknessPercent, parsed, nameof(MaxCompositeDarknessPercent));
            ValidateMaxCompositeDarkness(parsed);
            if (string.IsNullOrEmpty(_maxCompositeDarknessError))
            {
                RecalculateSchedule();
            }
            else
            {
                RaiseValidationStateChanged();
            }
        }

        private void ApplyPixelWidthText(string text)
        {
            if (!int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
            {
                _pixelWidthError = $"Pixel width must be an integer between {MinAllowedPixelWidth} and {MaxAllowedPixelWidth} px.";
                RaiseValidationStateChanged();
                return;
            }

            SetProperty(ref _pixelWidth, parsed, nameof(PixelWidth));
            ValidatePixelWidth(parsed);
            RaiseValidationStateChanged();
        }

        private void ValidateTimeStep(int stepMinutes)
        {
            _timeStepError = (stepMinutes <= 0 || stepMinutes > 1440)
                ? "Time increment must be between 1 and 1440 minutes."
                : string.Empty;
        }

        private void ValidateMinSunAltitude(double value)
        {
            _minSunAltitudeError = (double.IsNaN(value) || double.IsInfinity(value) ||
                                    value < MinAllowedSunAltitudeDegrees || value > MaxAllowedSunAltitudeDegrees)
                ? $"Minimum sun altitude must be between {MinAllowedSunAltitudeDegrees:0}° and {MaxAllowedSunAltitudeDegrees:0}°."
                : string.Empty;
        }

        private void ValidateMaxCompositeDarkness(int value)
        {
            _maxCompositeDarknessError = (value < MinAllowedDarknessPercent || value > MaxAllowedDarknessPercent)
                ? $"Maximum composite darkness must be between {MinAllowedDarknessPercent}% and {MaxAllowedDarknessPercent}%."
                : string.Empty;
        }

        private void ValidatePixelWidth(int value)
        {
            _pixelWidthError = (value < MinAllowedPixelWidth || value > MaxAllowedPixelWidth)
                ? $"Pixel width must be between {MinAllowedPixelWidth} and {MaxAllowedPixelWidth} px."
                : string.Empty;
        }

        private static bool TryParseDouble(string text, out double parsed)
        {
            string trimmed = text.Trim();
            if (double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed) ||
                double.TryParse(trimmed, NumberStyles.Float, CultureInfo.CurrentCulture, out parsed))
            {
                return !double.IsNaN(parsed) && !double.IsInfinity(parsed);
            }

            parsed = 0.0;
            return false;
        }

        private void RecalculateSchedule()
        {
            if (HasValidationErrors)
            {
                RaiseValidationStateChanged();
                return;
            }

            CurrentSchedule = SolarScheduleCalculator.BuildSchedule(
                new ShadowStudyDateEntry { Date = SelectedDate },
                SiteInfo,
                TimeStepMinutes,
                MinSunAltitudeDegrees,
                MaxCompositeDarknessPercent);

            OnPropertyChanged(nameof(ValidFrameCount));
            OnPropertyChanged(nameof(HasZeroFrames));
            OnPropertyChanged(nameof(PeakSolarAltitudeDegrees));
            OnPropertyChanged(nameof(SchedulePreviewText));
            OnPropertyChanged(nameof(ZeroFramesWarningText));
            OnPropertyChanged(nameof(CompositeFileNamePreview));
            OnPropertyChanged(nameof(FrameFolderPreview));
            RaiseValidationStateChanged();
        }

        private void RaiseValidationStateChanged()
        {
            OnPropertyChanged(nameof(HasValidationErrors));
            OnPropertyChanged(nameof(ValidationErrorMessage));
            OnPropertyChanged(nameof(ShowSchedulePreview));
            OnPropertyChanged(nameof(ShowZeroFramesWarning));
            RaiseExportStateChanged();
        }

        private void RaiseExportStateChanged()
        {
            OnPropertyChanged(nameof(CanExport));
            CommandManager.InvalidateRequerySuggested();
        }
    }
}
