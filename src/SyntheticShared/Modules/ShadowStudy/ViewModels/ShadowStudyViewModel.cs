using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Input;
using Synthetic.Modules.ShadowStudy.Adapters;
using Synthetic.Modules.ShadowStudy.Models;
using Synthetic.Modules.ShadowStudy.Services;
using Synthetic.Shared.UI;

namespace Synthetic.Modules.ShadowStudy.ViewModels
{
    /// <summary>
    /// Presentation ViewModel for the single-view, single-date Hidden Line Shadow Composite dialog.
    /// </summary>
    public class ShadowStudyViewModel : ViewModelBase
    {
        private readonly IShadowStudyAdapter _adapter;
        private readonly IFolderDialogService _folderDialogService;

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

            _selectedDate = (initialDate ?? DateTime.Today).Date;

            BrowseOutputFolderCommand = new RelayCommand(_ => ExecuteBrowseOutputFolder());
            ExportCommand = new RelayCommand(_ => ExecuteExport(), _ => CanExport);
            CancelCommand = new RelayCommand(_ => RequestClose?.Invoke(false));

            RecalculateSchedule();
        }

        /// <summary>
        /// Gets the active shadow-capable view item.
        /// </summary>
        public ShadowStudyViewItem? ActiveView { get; }

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
                string viewLabel = ActiveView?.ViewLabel ?? "View";
                string dateLabel = ShadowStudyPathHelper.FormatDateLabel(SelectedDate);
                return ShadowStudyPathHelper.BuildCompositeFileName(viewLabel, dateLabel);
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
        /// Requires an eligible active view, a non-blank output folder, positive pixel width, and at least one valid daylight frame.
        /// </summary>
        public bool CanExport =>
            ActiveView != null &&
            !string.IsNullOrWhiteSpace(OutputFolder) &&
            ValidFrameCount > 0 &&
            PixelWidth > 0;

        /// <summary>
        /// Gets the command that opens the folder browser dialog.
        /// </summary>
        public ICommand BrowseOutputFolderCommand { get; }

        /// <summary>
        /// Gets the command that confirms settings and starts the export.
        /// </summary>
        public ICommand ExportCommand { get; }

        /// <summary>
        /// Gets the command that cancels and closes the dialog.
        /// </summary>
        public ICommand CancelCommand { get; }

        /// <summary>
        /// Builds the headless <see cref="ShadowStudyRunRequest"/> for the configured view, date, and output settings.
        /// </summary>
        public ShadowStudyRunRequest BuildRunRequest()
        {
            if (ActiveView == null)
            {
                throw new InvalidOperationException("Cannot build a Shadow Study run request without an eligible active view.");
            }

            return new ShadowStudyRunRequest
            {
                Views = new[] { ActiveView },
                DateSchedules = new[] { CurrentSchedule },
                OutputFolder = OutputFolder.Trim(),
                PixelWidth = PixelWidth,
                MaxCompositeDarknessPercent = MaxCompositeDarknessPercent,
                ForceHiddenLine = ForceHiddenLine,
                ForceWhiteBackground = ForceWhiteBackground,
                KeepIndividualFrames = false
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
