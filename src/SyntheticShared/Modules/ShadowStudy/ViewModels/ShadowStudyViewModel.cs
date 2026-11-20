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
        private readonly IShadowStudyAdapter _adapter;
        private readonly IFolderDialogService _folderDialogService;
        private readonly List<ShadowStudySelectableViewItemViewModel> _allViews;

        private ShadowStudyExportRangeMode _exportRangeMode;
        private string _viewSearchText = string.Empty;
        private DateTime _selectedDate;
        private int _timeStepMinutes = 60;
        private double _minSunAltitudeDegrees = 5.0;
        private int _maxCompositeDarknessPercent = 80;
        private int _pixelWidth = 3000;
        private bool _forceHiddenLine = true;
        private bool _forceWhiteBackground = true;
        private string _outputFolder = string.Empty;
        private DateSolarSchedule _currentSchedule = new DateSolarSchedule();

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
            DateTime? initialDate = null)
        {
            _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
            _folderDialogService = folderDialogService ?? throw new ArgumentNullException(nameof(folderDialogService));

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

            BrowseOutputFolderCommand = new RelayCommand(_ => ExecuteBrowseOutputFolder());
            SelectAllViewsCommand = new RelayCommand(_ => ExecuteSelectAllViews());
            SelectNoneViewsCommand = new RelayCommand(_ => ExecuteSelectNoneViews());
            ExportCommand = new RelayCommand(_ => ExecuteExport(), _ => CanExport);
            CancelCommand = new RelayCommand(_ => RequestClose?.Invoke(false));

            RecalculateSchedule();
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
                    RecalculateSchedule();
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
                    RecalculateSchedule();
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
                    RecalculateSchedule();
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
                    RaiseExportStateChanged();
                }
            }
        }

        /// <summary>
        /// Gets or sets a value indicating whether views are temporarily rendered in Hidden Line (<c>HLR</c>).
        /// </summary>
        public bool ForceHiddenLine
        {
            get => _forceHiddenLine;
            set => SetProperty(ref _forceHiddenLine, value);
        }

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
            ValidFrameCount > 0 &&
            PixelWidth > 0;

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
                KeepIndividualFrames = false
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
                RequestClose?.Invoke(true);
            }
        }

        private void RecalculateSchedule()
        {
            int safeStep = TimeStepMinutes > 0 ? TimeStepMinutes : 60;
            CurrentSchedule = SolarScheduleCalculator.BuildSchedule(
                new ShadowStudyDateEntry { Date = SelectedDate },
                SiteInfo,
                safeStep,
                MinSunAltitudeDegrees,
                MaxCompositeDarknessPercent);

            OnPropertyChanged(nameof(ValidFrameCount));
            OnPropertyChanged(nameof(HasZeroFrames));
            OnPropertyChanged(nameof(SchedulePreviewText));
            OnPropertyChanged(nameof(CompositeFileNamePreview));
            RaiseExportStateChanged();
        }

        private void RaiseExportStateChanged()
        {
            OnPropertyChanged(nameof(CanExport));
            CommandManager.InvalidateRequerySuggested();
        }
    }
}
