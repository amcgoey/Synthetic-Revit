using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows.Input;
using Synthetic.Modules.ShadowStudy.Adapters;
using Synthetic.Modules.ShadowStudy.Models;
using Synthetic.Modules.ShadowStudy.Services;
using Synthetic.Modules.ShadowStudy.Settings;
using Synthetic.Shared.UI;

namespace Synthetic.Modules.ShadowStudy.ViewModels
{
    /// <summary>
    /// Presentation ViewModel for the Shadow Study composite exporter dialog supporting
    /// multi-view Export Range selection, hemisphere-aware built-in presets, and multi-date batches.
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

        /// <summary>Minimum allowed target calendar year for astronomical preset resolution.</summary>
        public const int MinAllowedTargetYear = 1900;

        /// <summary>Maximum allowed target calendar year for astronomical preset resolution.</summary>
        public const int MaxAllowedTargetYear = 2100;

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

        internal static readonly IReadOnlyList<ShadowStudyTimeIncrementOption> DefaultTimeIncrements =
            new[]
            {
                new ShadowStudyTimeIncrementOption(15, "15 min"),
                new ShadowStudyTimeIncrementOption(30, "30 min"),
                new ShadowStudyTimeIncrementOption(60, "1 hr"),
                new ShadowStudyTimeIncrementOption(120, "2 hr")
            };

        private readonly IShadowStudyAdapter _adapter;
        private readonly IFolderDialogService _folderDialogService;
        private readonly ITextInputPromptService? _textInputPromptService;
        private readonly List<ShadowStudySelectableViewItemViewModel> _allViews;
        private readonly ShadowStudyUserPrefsStore? _userPrefsStore;
        private readonly ObservableCollection<ShadowStudyPresetDefinition> _availablePresets;
        private readonly ObservableCollection<ShadowStudyDateScheduleItemViewModel> _studyDates =
            new ObservableCollection<ShadowStudyDateScheduleItemViewModel>();

        private ShadowStudySettings _settings;
        private ShadowStudyExportRangeMode _exportRangeMode;
        private ShadowStudyPrintSetItem _selectedPrintSet = ShadowStudyPrintSetItem.CreateInSession();
        private bool _isApplyingPrintSetSelection;
        private bool _isPrintSetModified;
        private string _printSetStatusMessage = string.Empty;
        private string _printSetErrorMessage = string.Empty;
        private string _viewSearchText = string.Empty;
        private ShadowStudyPresetDefinition? _selectedPreset;
        private bool _isApplyingPreset;
        private bool _isPresetModified;
        private string _presetStatusMessage = string.Empty;
        private string _presetErrorMessage = string.Empty;
        private int _targetYear;
        private string _targetYearText;
        private AstronomicalEventKind _selectedAstronomicalEventToAdd = AstronomicalEventKind.SummerSolstice;
        private DateTime _customDateToAdd = DateTime.Today;
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
        private string _targetYearError = string.Empty;
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
        /// When <paramref name="initialDate"/> is <c>null</c>, defaults to the 3-date built-in Seasonal Study preset
        /// for the current calendar year.
        /// </summary>
        public ShadowStudyViewModel(
            IShadowStudyAdapter adapter,
            IFolderDialogService folderDialogService,
            DateTime? initialDate = null,
            ShadowStudyUserPrefsStore? userPrefsStore = null,
            ITextInputPromptService? textInputPromptService = null)
        {
            _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
            _folderDialogService = folderDialogService ?? throw new ArgumentNullException(nameof(folderDialogService));
            _userPrefsStore = userPrefsStore;
            _textInputPromptService = textInputPromptService;

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

            PrintSets = new ObservableCollection<ShadowStudyPrintSetItem>();
            LoadPrintSets(selectedPrintSetId: null, applySelectionToChecklist: false);

            FilteredViews = new ObservableCollection<ShadowStudySelectableViewItemViewModel>();
            RefreshFilteredViews();

            _availablePresets = new ObservableCollection<ShadowStudyPresetDefinition>();
            _settings = (_adapter.LoadShadowStudySettings() ?? new ShadowStudySettings()).DeepClone();
            if (_settings.UserPresets == null)
            {
                _settings.UserPresets = new List<ShadowStudyPresetModel>();
            }

            ApplySettingsDefaultsToBackingFields();
            RebuildAvailablePresets();

            _targetYear = initialDate.HasValue ? initialDate.Value.Year : DateTime.Today.Year;
            _targetYearText = _targetYear.ToString(CultureInfo.InvariantCulture);

            LoadUserPrefs();

            BrowseOutputFolderCommand = new RelayCommand(_ => ExecuteBrowseOutputFolder());
            SelectAllViewsCommand = new RelayCommand(_ => ExecuteSelectAllViews());
            SelectNoneViewsCommand = new RelayCommand(_ => ExecuteSelectNoneViews());
            SavePrintSetCommand = new RelayCommand(_ => ExecuteSavePrintSet(), _ => CanSavePrintSet);
            SaveAsPrintSetCommand = new RelayCommand(_ => ExecuteSaveAsPrintSet(), _ => CanSavePrintSet);
            SavePresetCommand = new RelayCommand(_ => ExecuteSavePreset(), _ => CanSavePreset);
            SavePresetAsCommand = new RelayCommand(_ => ExecuteSavePresetAs(), _ => CanSavePresetAs);
            RenamePresetCommand = new RelayCommand(_ => ExecuteRenamePreset(), _ => CanRenamePreset);
            DeletePresetCommand = new RelayCommand(_ => ExecuteDeletePreset(), _ => CanDeletePreset);
            AddAstronomicalDateCommand = new RelayCommand(_ => ExecuteAddAstronomicalDate(), _ => CanAddSelectedAstronomicalDate);
            AddCustomDateCommand = new RelayCommand(_ => ExecuteAddCustomDate(), _ => CanAddCustomDate);
            RemoveDateCommand = new RelayCommand(ExecuteRemoveDate);
            ExportCommand = new RelayCommand(_ => ExecuteExport(), _ => CanExport);
            CancelCommand = new RelayCommand(_ => RequestClose?.Invoke(false));

            if (initialDate.HasValue)
            {
                SetSingleCustomDate(initialDate.Value.Date);
            }
            else
            {
                SelectedPreset = _availablePresets.FirstOrDefault(p => p.Id == BuiltInShadowStudyPresets.SeasonalStudyId)
                                 ?? BuiltInShadowStudyPresets.SeasonalStudy;
            }
        }

        /// <summary>
        /// Gets the available built-in (and user-defined) Shadow Study Presets.
        /// </summary>
        public ObservableCollection<ShadowStudyPresetDefinition> AvailablePresets => _availablePresets;

        /// <summary>
        /// Gets or sets the currently selected Shadow Study Preset (<c>null</c> when initialized with an explicit date).
        /// </summary>
        public ShadowStudyPresetDefinition? SelectedPreset
        {
            get => _selectedPreset;
            set
            {
                if (SetProperty(ref _selectedPreset, value))
                {
                    if (value != null)
                    {
                        ApplyPreset(value);
                    }
                    else
                    {
                        RaisePresetStateChanged();
                    }
                }
            }
        }

        /// <summary>
        /// Gets a value indicating whether the active preset has unsaved parameter or date modifications.
        /// </summary>
        public bool IsPresetModified
        {
            get => _isPresetModified;
            private set
            {
                if (SetProperty(ref _isPresetModified, value))
                {
                    OnPropertyChanged(nameof(PresetBadgeText));
                }
            }
        }

        /// <summary>
        /// Gets a value indicating whether the currently selected preset is a read-only built-in preset.
        /// </summary>
        public bool IsSelectedPresetBuiltIn => SelectedPreset != null && SelectedPreset.IsBuiltIn;

        /// <summary>
        /// Gets a value indicating whether the currently selected preset is a user-defined preset.
        /// </summary>
        public bool IsSelectedPresetUserDefined => SelectedPreset != null && !SelectedPreset.IsBuiltIn;

        /// <summary>
        /// Gets a concise indicator label for the active preset's read-only or modified state.
        /// </summary>
        public string PresetBadgeText
        {
            get
            {
                if (SelectedPreset == null)
                {
                    return IsPresetModified ? "Custom" : string.Empty;
                }

                if (SelectedPreset.IsBuiltIn)
                {
                    return IsPresetModified ? "Built-in (modified — Save forks)" : "Built-in (read-only)";
                }

                return IsPresetModified ? "User preset (modified)" : "User preset";
            }
        }

        /// <summary>
        /// Gets the informational status message for Shadow Study Preset operations.
        /// </summary>
        public string PresetStatusMessage
        {
            get => _presetStatusMessage;
            private set
            {
                if (SetProperty(ref _presetStatusMessage, value ?? string.Empty))
                {
                    OnPropertyChanged(nameof(HasPresetStatus));
                }
            }
        }

        /// <summary>
        /// Gets a value indicating whether <see cref="PresetStatusMessage"/> is non-empty.
        /// </summary>
        public bool HasPresetStatus => !string.IsNullOrEmpty(_presetStatusMessage);

        /// <summary>
        /// Gets the inline error message for Shadow Study Preset operations.
        /// </summary>
        public string PresetErrorMessage
        {
            get => _presetErrorMessage;
            private set
            {
                if (SetProperty(ref _presetErrorMessage, value ?? string.Empty))
                {
                    OnPropertyChanged(nameof(HasPresetError));
                }
            }
        }

        /// <summary>
        /// Gets a value indicating whether <see cref="PresetErrorMessage"/> is non-empty.
        /// </summary>
        public bool HasPresetError => !string.IsNullOrEmpty(_presetErrorMessage);

        /// <summary>
        /// Gets a value indicating whether Save can persist the active user preset or fork a built-in preset via Save As.
        /// </summary>
        public bool CanSavePreset => !HasValidationErrors && _studyDates.Count > 0;

        /// <summary>
        /// Gets a value indicating whether Save As can create a new user-defined preset.
        /// </summary>
        public bool CanSavePresetAs => !HasValidationErrors && _studyDates.Count > 0;

        /// <summary>
        /// Gets a value indicating whether the selected preset can be renamed (user-defined presets only).
        /// </summary>
        public bool CanRenamePreset => SelectedPreset != null && !SelectedPreset.IsBuiltIn;

        /// <summary>
        /// Gets a value indicating whether the selected preset can be deleted (user-defined presets only).
        /// </summary>
        public bool CanDeletePreset => SelectedPreset != null && !SelectedPreset.IsBuiltIn;

        /// <summary>
        /// Gets or sets the target calendar year used to resolve astronomical preset dates.
        /// </summary>
        public int TargetYear
        {
            get => _targetYear;
            set
            {
                if (SetProperty(ref _targetYear, value))
                {
                    _targetYearText = value.ToString(CultureInfo.InvariantCulture);
                    OnPropertyChanged(nameof(TargetYearText));
                    ValidateTargetYear(value);
                    if (string.IsNullOrEmpty(_targetYearError))
                    {
                        UpdateAstronomicalDatesForTargetYear(value);
                        RecalculateAllSchedules();
                    }
                    else
                    {
                        RaiseValidationStateChanged();
                    }
                }
            }
        }

        /// <summary>
        /// Gets or sets the editable target calendar year text.
        /// </summary>
        public string TargetYearText
        {
            get => _targetYearText;
            set
            {
                string normalized = value ?? string.Empty;
                if (SetProperty(ref _targetYearText, normalized))
                {
                    ApplyTargetYearText(normalized);
                }
            }
        }

        /// <summary>
        /// Gets the selectable solstice/equinox astronomical event options.
        /// </summary>
        public IReadOnlyList<AstronomicalEventOption> AstronomicalEventOptions =>
            ShadowStudySettingsViewModel.DefaultAstronomicalEvents;

        /// <summary>
        /// Gets or sets the astronomical event staged for addition via <see cref="AddAstronomicalDateCommand"/>.
        /// </summary>
        public AstronomicalEventKind SelectedAstronomicalEventToAdd
        {
            get => _selectedAstronomicalEventToAdd;
            set
            {
                if (SetProperty(ref _selectedAstronomicalEventToAdd, value))
                {
                    OnPropertyChanged(nameof(CanAddSelectedAstronomicalDate));
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        /// <summary>
        /// Gets a value indicating whether <see cref="SelectedAstronomicalEventToAdd"/> can be added to <see cref="StudyDates"/>.
        /// </summary>
        public bool CanAddSelectedAstronomicalDate
        {
            get
            {
                if (_selectedAstronomicalEventToAdd == AstronomicalEventKind.None ||
                    _studyDates.Any(d => d.AstronomicalEvent == _selectedAstronomicalEventToAdd))
                {
                    return false;
                }

                DateTime resolvedDate = BuiltInShadowStudyPresets.ResolveDate(
                    _selectedAstronomicalEventToAdd,
                    GetEffectiveTargetYear(),
                    SiteInfo);
                return !ContainsCalendarDate(resolvedDate);
            }
        }

        /// <summary>
        /// Gets or sets the custom calendar date staged for addition via <see cref="AddCustomDateCommand"/>.
        /// </summary>
        public DateTime CustomDateToAdd
        {
            get => _customDateToAdd;
            set
            {
                DateTime dateOnly = value.Date;
                if (SetProperty(ref _customDateToAdd, dateOnly))
                {
                    OnPropertyChanged(nameof(CanAddCustomDate));
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        /// <summary>
        /// Gets a value indicating whether <see cref="CustomDateToAdd"/> is not already in <see cref="StudyDates"/>.
        /// </summary>
        public bool CanAddCustomDate => !ContainsCalendarDate(_customDateToAdd.Date);

        /// <summary>
        /// Gets the selected study date entries with live per-date solar schedules.
        /// </summary>
        public ObservableCollection<ShadowStudyDateScheduleItemViewModel> StudyDates => _studyDates;

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
            !string.IsNullOrEmpty(_targetYearError) ||
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
                var errors = new List<string>(5);
                if (!string.IsNullOrEmpty(_targetYearError)) errors.Add(_targetYearError);
                if (!string.IsNullOrEmpty(_timeStepError)) errors.Add(_timeStepError);
                if (!string.IsNullOrEmpty(_minSunAltitudeError)) errors.Add(_minSunAltitudeError);
                if (!string.IsNullOrEmpty(_maxCompositeDarknessError)) errors.Add(_maxCompositeDarknessError);
                if (!string.IsNullOrEmpty(_pixelWidthError)) errors.Add(_pixelWidthError);
                return string.Join(" ", errors);
            }
        }

        /// <summary>
        /// Gets the peak solar altitude in degrees for the primary selected date.
        /// </summary>
        public double PeakSolarAltitudeDegrees => CurrentSchedule.PeakSolarAltitudeDegrees;

        /// <summary>
        /// Gets the zero-frames warning text including peak solar altitude when all selected dates yield 0 frames.
        /// </summary>
        public string ZeroFramesWarningText
        {
            get
            {
                if (_studyDates.Count == 0)
                {
                    return "No study dates selected. Select a preset or add a custom date to enable Export.";
                }

                if (_studyDates.Count == 1)
                {
                    return string.Format(
                        CultureInfo.InvariantCulture,
                        "0 valid daylight frames meet >= {0:0.##}° (Peak solar altitude: {1:F1}°). Export is disabled.",
                        MinSunAltitudeDegrees,
                        PeakSolarAltitudeDegrees);
                }

                return string.Format(
                    CultureInfo.InvariantCulture,
                    "All {0} selected study dates have 0 valid daylight frames meeting >= {1:0.##}°. Export is disabled.",
                    _studyDates.Count,
                    MinSunAltitudeDegrees);
            }
        }

        /// <summary>
        /// Gets a value indicating whether any study dates are selected.
        /// </summary>
        public bool HasNoDates => _studyDates.Count == 0;

        /// <summary>
        /// Gets the total valid daylight frame count summed across all selected study dates.
        /// </summary>
        public int TotalValidFrameCount => _studyDates.Sum(d => d.ValidFrameCount);

        /// <summary>
        /// Gets a value indicating whether at least one selected date has 0 valid frames while other dates remain valid.
        /// </summary>
        public bool HasSkippedZeroFrameDates => TotalValidFrameCount > 0 && _studyDates.Any(d => d.HasZeroFrames);

        /// <summary>
        /// Gets a value indicating whether the partial skipped-date warning banner should be shown.
        /// </summary>
        public bool ShowPartialZeroFramesNotice => HasSkippedZeroFrameDates && !HasValidationErrors;

        /// <summary>
        /// Gets the summary text describing dates that will be skipped during export.
        /// </summary>
        public string PartialZeroFramesNoticeText
        {
            get
            {
                int skippedCount = _studyDates.Count(d => d.HasZeroFrames);
                if (skippedCount <= 0)
                {
                    return string.Empty;
                }

                return skippedCount == 1
                    ? "1 selected date has 0 valid daylight frames and will be skipped during export."
                    : string.Format(
                        CultureInfo.InvariantCulture,
                        "{0} selected dates have 0 valid daylight frames and will be skipped during export.",
                        skippedCount);
            }
        }

        /// <summary>
        /// Gets a concise count summary for the Study Dates section header.
        /// </summary>
        public string StudyDatesSummaryText
        {
            get
            {
                if (_studyDates.Count == 0)
                {
                    return "0 dates";
                }

                int totalFrames = TotalValidFrameCount;
                int skippedCount = _studyDates.Count(d => d.HasZeroFrames);
                string dateNoun = _studyDates.Count == 1 ? "date" : "dates";
                string frameNoun = totalFrames == 1 ? "frame" : "frames";

                return skippedCount > 0 && totalFrames > 0
                    ? string.Format(
                        CultureInfo.InvariantCulture,
                        "{0} {1} ({2} skipped) · {3} {4}",
                        _studyDates.Count,
                        dateNoun,
                        skippedCount,
                        totalFrames,
                        frameNoun)
                    : string.Format(
                        CultureInfo.InvariantCulture,
                        "{0} {1} · {2} {3}",
                        _studyDates.Count,
                        dateNoun,
                        totalFrames,
                        frameNoun);
            }
        }

        /// <summary>
        /// Gets a value indicating whether the normal schedule preview should be shown.
        /// </summary>
        public bool ShowSchedulePreview => !HasZeroFrames && !HasValidationErrors;

        /// <summary>
        /// Gets a value indicating whether the all-zero-frames warning badge should be shown.
        /// </summary>
        public bool ShowZeroFramesWarning => HasZeroFrames && !HasValidationErrors;

        /// <summary>
        /// Gets the previewed individual frame subfolder path segment.
        /// </summary>
        public string FrameFolderPreview
        {
            get
            {
                if (_studyDates.Count == 0)
                {
                    return "—";
                }

                IReadOnlyList<ShadowStudyDateScheduleItemViewModel> activeDates = _studyDates.Where(d => d.HasValidFrames).ToList();
                string? singleDateLabel = _studyDates.Count == 1
                    ? _studyDates[0].DateLabel
                    : (activeDates.Count == 1 ? activeDates[0].DateLabel : null);

                if (ExportRangeMode == ShadowStudyExportRangeMode.CurrentView)
                {
                    string viewLabel = ActiveView?.ViewLabel ?? "View";
                    return singleDateLabel != null
                        ? ShadowStudyPathHelper.BuildFrameFolderName(viewLabel, singleDateLabel) + "\\"
                        : $"{viewLabel} - <DateLabel>\\";
                }

                var selectedCopies = GetLabeledSelectedViewCopies();

                if (selectedCopies.Count == 0)
                {
                    return ShadowStudyPathHelper.BuildFrameFolderName("View", singleDateLabel ?? "<DateLabel>") + "\\";
                }

                if (singleDateLabel != null)
                {
                    string firstFolder = ShadowStudyPathHelper.BuildFrameFolderName(selectedCopies[0].ViewLabel, singleDateLabel) + "\\";
                    return selectedCopies.Count == 1
                        ? firstFolder
                        : $"{firstFolder} (+{selectedCopies.Count - 1} more)";
                }

                int totalComposites = selectedCopies.Count * Math.Max(1, activeDates.Count);
                return $"{selectedCopies[0].ViewLabel} - <DateLabel>\\ (+{Math.Max(0, totalComposites - 1)} more)";
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
                    OnPropertyChanged(nameof(CanSavePrintSet));
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
        /// Gets the Print Sets available in Selected Views mode, starting with <c>&lt;In-Session&gt;</c>
        /// followed by saved document Print Sets ordered case-insensitively by name.
        /// </summary>
        public ObservableCollection<ShadowStudyPrintSetItem> PrintSets { get; }

        /// <summary>
        /// Gets or sets the currently selected Print Set in Selected Views mode.
        /// </summary>
        public ShadowStudyPrintSetItem SelectedPrintSet
        {
            get => _selectedPrintSet;
            set
            {
                ShadowStudyPrintSetItem target = value ?? PrintSets.FirstOrDefault() ?? ShadowStudyPrintSetItem.CreateInSession();
                if (SetProperty(ref _selectedPrintSet, target))
                {
                    ApplySelectedPrintSet(target);
                    OnPropertyChanged(nameof(CanSavePrintSet));
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        /// <summary>
        /// Gets a value indicating whether the view checklist has been modified since loading the active saved Print Set.
        /// </summary>
        public bool IsPrintSetModified
        {
            get => _isPrintSetModified;
            private set => SetProperty(ref _isPrintSetModified, value);
        }

        /// <summary>
        /// Gets the informational status message for Print Set loading or saving (for example, ignored non-shadow members).
        /// </summary>
        public string PrintSetStatusMessage
        {
            get => _printSetStatusMessage;
            private set
            {
                if (SetProperty(ref _printSetStatusMessage, value ?? string.Empty))
                {
                    OnPropertyChanged(nameof(HasPrintSetStatus));
                }
            }
        }

        /// <summary>
        /// Gets a value indicating whether <see cref="PrintSetStatusMessage"/> is non-empty.
        /// </summary>
        public bool HasPrintSetStatus => !string.IsNullOrEmpty(_printSetStatusMessage);

        /// <summary>
        /// Gets the inline validation or save error message for Print Set operations.
        /// </summary>
        public string PrintSetErrorMessage
        {
            get => _printSetErrorMessage;
            private set
            {
                if (SetProperty(ref _printSetErrorMessage, value ?? string.Empty))
                {
                    OnPropertyChanged(nameof(HasPrintSetError));
                }
            }
        }

        /// <summary>
        /// Gets a value indicating whether <see cref="PrintSetErrorMessage"/> is non-empty.
        /// </summary>
        public bool HasPrintSetError => !string.IsNullOrEmpty(_printSetErrorMessage);

        /// <summary>
        /// Gets a value indicating whether Save or Save As can persist a non-empty Print Set.
        /// </summary>
        public bool CanSavePrintSet => IsSelectedViewsMode && HasSavablePrintSetMembers();

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
        /// Gets or sets the primary selected study calendar date (replaces the list with a single custom date when set).
        /// </summary>
        public DateTime SelectedDate
        {
            get => _studyDates.Count > 0 ? _studyDates[0].Date : _customDateToAdd;
            set
            {
                DateTime dateOnly = value.Date;
                SetSingleCustomDate(dateOnly);
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
                    MarkPresetModifiedIfInteractive();
                    ValidateTimeStep(value);
                    if (string.IsNullOrEmpty(_timeStepError))
                    {
                        RecalculateAllSchedules();
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
                    MarkPresetModifiedIfInteractive();
                    _minSunAltitudeText = double.IsNaN(value) || double.IsInfinity(value)
                        ? string.Empty
                        : value.ToString("0.##", CultureInfo.InvariantCulture);
                    OnPropertyChanged(nameof(MinSunAltitudeText));
                    ValidateMinSunAltitude(value);
                    if (string.IsNullOrEmpty(_minSunAltitudeError))
                    {
                        RecalculateAllSchedules();
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
                    MarkPresetModifiedIfInteractive();
                    _maxCompositeDarknessText = value.ToString(CultureInfo.InvariantCulture);
                    OnPropertyChanged(nameof(MaxCompositeDarknessText));
                    ValidateMaxCompositeDarkness(value);
                    if (string.IsNullOrEmpty(_maxCompositeDarknessError))
                    {
                        RecalculateAllSchedules();
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
                    MarkPresetModifiedIfInteractive();
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
                    MarkPresetModifiedIfInteractive();
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
        /// Gets the computed solar schedule for the primary selected date.
        /// </summary>
        public DateSolarSchedule CurrentSchedule =>
            _studyDates.Count > 0 ? _studyDates[0].Schedule : new DateSolarSchedule();

        /// <summary>
        /// Gets the total count of valid daylight frames across all selected study dates.
        /// </summary>
        public int ValidFrameCount => TotalValidFrameCount;

        /// <summary>
        /// Gets a value indicating whether total valid daylight frames across all selected dates is zero.
        /// </summary>
        public bool HasZeroFrames => _studyDates.Count == 0 || TotalValidFrameCount == 0;

        /// <summary>
        /// Gets the live schedule preview summary text for the primary selected date.
        /// </summary>
        public string SchedulePreviewText => CurrentSchedule.WindowSummary;

        /// <summary>
        /// Gets the previewed output composite filename or multi-view/multi-date summary.
        /// </summary>
        public string CompositeFileNamePreview
        {
            get
            {
                if (_studyDates.Count == 0)
                {
                    return "Select at least one study date";
                }

                IReadOnlyList<ShadowStudyDateScheduleItemViewModel> activeDates = _studyDates.Where(d => d.HasValidFrames).ToList();
                if (ExportRangeMode == ShadowStudyExportRangeMode.CurrentView)
                {
                    string viewLabel = ActiveView?.ViewLabel ?? "View";
                    if (_studyDates.Count == 1)
                    {
                        return ShadowStudyPathHelper.BuildCompositeFileName(viewLabel, _studyDates[0].DateLabel);
                    }

                    if (activeDates.Count == 0)
                    {
                        return "0 composites (all selected dates yield 0 valid frames)";
                    }

                    return activeDates.Count == 1
                        ? ShadowStudyPathHelper.BuildCompositeFileName(viewLabel, activeDates[0].DateLabel)
                        : string.Format(
                            CultureInfo.InvariantCulture,
                            "{0} composites ({1} - <DateLabel> - Composite.png)",
                            activeDates.Count,
                            viewLabel);
                }

                var selectedCopies = GetLabeledSelectedViewCopies();

                if (selectedCopies.Count == 0)
                {
                    return "Select at least 1 view to preview output filename";
                }

                if (activeDates.Count == 0)
                {
                    return "0 composites (all selected dates yield 0 valid frames)";
                }

                string firstFileName = ShadowStudyPathHelper.BuildCompositeFileName(selectedCopies[0].ViewLabel, activeDates[0].DateLabel);
                if (activeDates.Count == 1)
                {
                    return selectedCopies.Count == 1
                        ? firstFileName
                        : $"{firstFileName} (+{selectedCopies.Count - 1} more)";
                }

                int totalComposites = selectedCopies.Count * activeDates.Count;
                return $"{totalComposites} composites (e.g. {firstFileName})";
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
        /// no validation errors, and at least one valid daylight frame across the selected study dates.
        /// </summary>
        public bool CanExport =>
            HasValidTargetViews() &&
            !string.IsNullOrWhiteSpace(OutputFolder) &&
            !HasValidationErrors &&
            TotalValidFrameCount > 0;

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
        /// Gets the command that saves updates to the selected saved Print Set, or routes through Save As when
        /// <c>&lt;In-Session&gt;</c> is selected.
        /// </summary>
        public ICommand SavePrintSetCommand { get; }

        /// <summary>
        /// Gets the command that prompts for a unique name and saves a new Print Set in the Revit document.
        /// </summary>
        public ICommand SaveAsPrintSetCommand { get; }

        /// <summary>
        /// Gets the command that saves updates to the selected user-defined preset, or forks a built-in preset via Save As.
        /// </summary>
        public ICommand SavePresetCommand { get; }

        /// <summary>
        /// Gets the command that prompts for a unique name and saves a new user-defined Shadow Study Preset.
        /// </summary>
        public ICommand SavePresetAsCommand { get; }

        /// <summary>
        /// Gets the command that prompts for a new name and renames the selected user-defined preset.
        /// </summary>
        public ICommand RenamePresetCommand { get; }

        /// <summary>
        /// Gets the command that deletes the selected user-defined preset and falls back to Seasonal Study.
        /// </summary>
        public ICommand DeletePresetCommand { get; }

        /// <summary>
        /// Gets the command that adds <see cref="SelectedAstronomicalEventToAdd"/> to <see cref="StudyDates"/>.
        /// </summary>
        public ICommand AddAstronomicalDateCommand { get; }

        /// <summary>
        /// Gets the command that adds <see cref="CustomDateToAdd"/> to <see cref="StudyDates"/>.
        /// </summary>
        public ICommand AddCustomDateCommand { get; }

        /// <summary>
        /// Gets the command that removes a study date item from <see cref="StudyDates"/>.
        /// </summary>
        public ICommand RemoveDateCommand { get; }

        /// <summary>
        /// Gets the command that confirms settings and starts the export.
        /// </summary>
        public ICommand ExportCommand { get; }

        /// <summary>
        /// Gets the command that cancels and closes the dialog.
        /// </summary>
        public ICommand CancelCommand { get; }

        /// <summary>
        /// Builds the headless <see cref="ShadowStudyRunRequest"/> for the configured view(s), study dates, and output settings.
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
                var checkedViews = GetLabeledSelectedViewCopies();

                if (checkedViews.Count == 0)
                {
                    throw new InvalidOperationException("Cannot build a Shadow Study run request without at least one selected view.");
                }

                targetViews = checkedViews.AsReadOnly();
            }

            return new ShadowStudyRunRequest
            {
                Views = targetViews,
                DateSchedules = _studyDates.Select(d => d.Schedule).ToList().AsReadOnly(),
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
            if (!_isApplyingPrintSetSelection)
            {
                PrintSetErrorMessage = string.Empty;
                if (SelectedPrintSet != null && !SelectedPrintSet.IsInSession)
                {
                    IsPrintSetModified = true;
                }
            }

            OnPropertyChanged(nameof(SelectedViewsCount));
            OnPropertyChanged(nameof(SelectedViewsSummary));
            OnPropertyChanged(nameof(CompositeFileNamePreview));
            OnPropertyChanged(nameof(FrameFolderPreview));
            OnPropertyChanged(nameof(CanSavePrintSet));
            RaiseExportStateChanged();
        }

        private bool HasSavablePrintSetMembers()
        {
            if (SelectedViewsCount > 0)
            {
                return true;
            }

            return SelectedPrintSet != null &&
                   !SelectedPrintSet.IsInSession &&
                   SelectedPrintSet.IgnoredNonShadowMemberCount > 0;
        }

        private void LoadPrintSets(string? selectedPrintSetId, bool applySelectionToChecklist)
        {
            PrintSets.Clear();
            PrintSets.Add(ShadowStudyPrintSetItem.CreateInSession());

            IReadOnlyList<ShadowStudyPrintSetItem> savedSets = _adapter.GetPrintSets() ?? Array.Empty<ShadowStudyPrintSetItem>();
            foreach (var set in savedSets
                .Where(s => s != null && !s.IsInSession && !string.IsNullOrWhiteSpace(s.Name))
                .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase))
            {
                PrintSets.Add(set);
            }

            ShadowStudyPrintSetItem matched = !string.IsNullOrEmpty(selectedPrintSetId)
                ? PrintSets.FirstOrDefault(p => string.Equals(p.Id, selectedPrintSetId, StringComparison.Ordinal)) ?? PrintSets[0]
                : PrintSets[0];

            _selectedPrintSet = matched;
            OnPropertyChanged(nameof(SelectedPrintSet));

            if (applySelectionToChecklist)
            {
                ApplySelectedPrintSet(matched);
            }
        }

        private void ApplySelectedPrintSet(ShadowStudyPrintSetItem printSet)
        {
            PrintSetErrorMessage = string.Empty;
            IsPrintSetModified = false;

            if (printSet == null || printSet.IsInSession)
            {
                PrintSetStatusMessage = string.Empty;
                return;
            }

            var targetIds = new HashSet<string>(
                printSet.ShadowViewIds ?? Array.Empty<string>(),
                StringComparer.Ordinal);

            _isApplyingPrintSetSelection = true;
            try
            {
                foreach (var viewItem in _allViews)
                {
                    viewItem.IsSelected = targetIds.Contains(viewItem.ViewId);
                }
            }
            finally
            {
                _isApplyingPrintSetSelection = false;
            }

            int shadowCount = _allViews.Count(v => v.IsSelected);
            PrintSetStatusMessage = printSet.IgnoredNonShadowMemberCount > 0
                ? $"Loaded '{printSet.Name}' ({shadowCount} shadow view(s); {printSet.IgnoredNonShadowMemberCount} non-shadow member(s) ignored for export and preserved on Save)."
                : string.Empty;
        }

        private void ExecuteSavePrintSet()
        {
            PrintSetErrorMessage = string.Empty;
            if (SelectedPrintSet == null || SelectedPrintSet.IsInSession)
            {
                ExecuteSaveAsPrintSet();
                return;
            }

            if (!HasSavablePrintSetMembers())
            {
                PrintSetErrorMessage = "Select at least one view before saving a Print Set.";
                return;
            }

            List<string> selectedIds = _allViews
                .Where(v => v.IsSelected)
                .Select(v => v.ViewId)
                .ToList();

            try
            {
                ShadowStudyPrintSetItem saved = _adapter.SavePrintSet(
                    SelectedPrintSet.Name,
                    selectedIds,
                    saveAsNew: false,
                    sourcePrintSetId: SelectedPrintSet.Id);

                RefreshAfterPrintSetSave(saved);
            }
            catch (Exception ex)
            {
                PrintSetErrorMessage = ex.Message;
            }
        }

        private void ExecuteSaveAsPrintSet()
        {
            PrintSetErrorMessage = string.Empty;
            if (!HasSavablePrintSetMembers())
            {
                PrintSetErrorMessage = "Select at least one view before saving a Print Set.";
                return;
            }

            if (_textInputPromptService == null)
            {
                PrintSetErrorMessage = "Print Set name prompt service is unavailable.";
                return;
            }

            string suggestedName = (SelectedPrintSet != null && !SelectedPrintSet.IsInSession)
                ? $"{SelectedPrintSet.Name} Copy"
                : string.Empty;

            string? enteredName = _textInputPromptService.PromptText(
                "Save Print Set As",
                "Enter a unique name for the Print Set:",
                suggestedName,
                ValidatePrintSetName);

            if (enteredName == null)
            {
                return;
            }

            string? validationError = ValidatePrintSetName(enteredName);
            if (!string.IsNullOrEmpty(validationError))
            {
                PrintSetErrorMessage = validationError!;
                return;
            }

            string trimmedName = enteredName.Trim();
            string? sourceId = (SelectedPrintSet != null && !SelectedPrintSet.IsInSession)
                ? SelectedPrintSet.Id
                : null;

            List<string> selectedIds = _allViews
                .Where(v => v.IsSelected)
                .Select(v => v.ViewId)
                .ToList();

            try
            {
                ShadowStudyPrintSetItem saved = _adapter.SavePrintSet(
                    trimmedName,
                    selectedIds,
                    saveAsNew: true,
                    sourcePrintSetId: sourceId);

                RefreshAfterPrintSetSave(saved);
            }
            catch (Exception ex)
            {
                PrintSetErrorMessage = ex.Message;
            }
        }

        private void RefreshAfterPrintSetSave(ShadowStudyPrintSetItem saved)
        {
            LoadPrintSets(selectedPrintSetId: saved.Id, applySelectionToChecklist: false);
            IsPrintSetModified = false;
            PrintSetErrorMessage = string.Empty;

            int shadowCount = saved.ShadowViewIds != null ? saved.ShadowViewIds.Count : 0;
            PrintSetStatusMessage = saved.IgnoredNonShadowMemberCount > 0
                ? $"Saved Print Set '{saved.Name}' ({shadowCount} shadow view(s); {saved.IgnoredNonShadowMemberCount} non-shadow member(s) preserved)."
                : $"Saved Print Set '{saved.Name}' ({shadowCount} shadow view(s)).";

            OnPropertyChanged(nameof(CanSavePrintSet));
            CommandManager.InvalidateRequerySuggested();
        }

        private string? ValidatePrintSetName(string? candidateName)
        {
            if (string.IsNullOrWhiteSpace(candidateName))
            {
                return "Print Set name cannot be blank.";
            }

            string trimmed = candidateName!.Trim();
            if (string.Equals(trimmed, ShadowStudyPrintSetItem.InSessionName, StringComparison.OrdinalIgnoreCase))
            {
                return "Print Set name cannot be '<In-Session>'.";
            }

            bool exists = PrintSets.Any(p =>
                !p.IsInSession &&
                string.Equals((p.Name ?? string.Empty).Trim(), trimmed, StringComparison.OrdinalIgnoreCase));

            if (exists)
            {
                return $"A Print Set named '{trimmed}' already exists. Enter a unique name.";
            }

            return null;
        }

        private List<ShadowStudyViewItem> GetLabeledSelectedViewCopies()
        {
            var selectedCopies = _allViews
                .Where(v => v.IsSelected)
                .Select(v => CloneViewItem(v.ViewItem))
                .ToList();

            if (selectedCopies.Count > 0)
            {
                ShadowStudyPathHelper.AssignViewLabels(selectedCopies);
            }

            return selectedCopies;
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
                    ExportIndividualFrames = ExportIndividualFrames
                });
            }
            catch
            {
                // Local preferences persistence failure must never block export execution
            }
        }

        private void ApplyTargetYearText(string text)
        {
            if (!int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
            {
                _targetYearError = $"Target year must be an integer between {MinAllowedTargetYear} and {MaxAllowedTargetYear}.";
                RaiseValidationStateChanged();
                return;
            }

            SetProperty(ref _targetYear, parsed, nameof(TargetYear));
            ValidateTargetYear(parsed);
            if (string.IsNullOrEmpty(_targetYearError))
            {
                UpdateAstronomicalDatesForTargetYear(parsed);
                RecalculateAllSchedules();
            }
            else
            {
                RaiseValidationStateChanged();
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
                MarkPresetModifiedIfInteractive();
            }

            ValidateMinSunAltitude(parsed);
            if (string.IsNullOrEmpty(_minSunAltitudeError))
            {
                RecalculateAllSchedules();
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

            if (SetProperty(ref _maxCompositeDarknessPercent, parsed, nameof(MaxCompositeDarknessPercent)))
            {
                MarkPresetModifiedIfInteractive();
            }
            ValidateMaxCompositeDarkness(parsed);
            if (string.IsNullOrEmpty(_maxCompositeDarknessError))
            {
                RecalculateAllSchedules();
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

        private void ValidateTargetYear(int year)
        {
            _targetYearError = (year < MinAllowedTargetYear || year > MaxAllowedTargetYear)
                ? $"Target year must be between {MinAllowedTargetYear} and {MaxAllowedTargetYear}."
                : string.Empty;
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
            return ShadowStudySettings.TryParseFiniteDouble(text, out parsed);
        }

        private void SetSingleCustomDate(DateTime dateOnly)
        {
            _isApplyingPreset = true;
            try
            {
                _selectedPreset = null;
                OnPropertyChanged(nameof(SelectedPreset));

                _studyDates.Clear();
                AddDateEntryInternal(new ShadowStudyDateEntry
                {
                    Date = dateOnly.Date,
                    AstronomicalEvent = AstronomicalEventKind.None,
                    PresetName = string.Empty
                });

                RaiseStudyDatesChanged();
            }
            finally
            {
                _isApplyingPreset = false;
            }

            IsPresetModified = false;
            RaisePresetStateChanged();
        }

        private void ApplySettingsDefaultsToBackingFields()
        {
            _timeStepMinutes = ShadowStudySettings.IsValidTimeIncrement(_settings.DefaultTimeIncrementMinutes)
                ? _settings.DefaultTimeIncrementMinutes
                : 60;

            _minSunAltitudeDegrees = ShadowStudySettings.IsValidSunAltitude(_settings.DefaultMinSunAltitudeDegrees)
                ? _settings.DefaultMinSunAltitudeDegrees
                : 5.0;
            _minSunAltitudeText = _minSunAltitudeDegrees.ToString("0.##", CultureInfo.InvariantCulture);

            _maxCompositeDarknessPercent = ShadowStudySettings.IsValidDarknessPercent(_settings.DefaultMaxCompositeDarknessPercent)
                ? _settings.DefaultMaxCompositeDarknessPercent
                : 80;
            _maxCompositeDarknessText = _maxCompositeDarknessPercent.ToString(CultureInfo.InvariantCulture);

            _forceHiddenLine = !_settings.DefaultPreserveVisualStyle;
            _forceWhiteBackground = true;
        }

        private void RebuildAvailablePresets()
        {
            _availablePresets.Clear();
            foreach (ShadowStudyPresetDefinition builtIn in BuiltInShadowStudyPresets.All)
            {
                _availablePresets.Add(builtIn);
            }

            if (_settings.UserPresets != null)
            {
                foreach (ShadowStudyPresetModel userPreset in _settings.UserPresets
                    .Where(p => p != null && !string.IsNullOrWhiteSpace(p.Name))
                    .OrderBy(p => p.Name.Trim(), StringComparer.OrdinalIgnoreCase))
                {
                    _availablePresets.Add(MapToPresetDefinition(userPreset));
                }
            }
        }

        private static ShadowStudyPresetDefinition MapToPresetDefinition(ShadowStudyPresetModel model)
        {
            string trimmedName = (model.Name ?? string.Empty).Trim();
            IReadOnlyList<ShadowStudyPresetDateSpec> specs = (model.Dates ?? new List<ShadowStudyPresetDateSpec>())
                .Where(d => d != null)
                .Select(d => d.DeepClone())
                .ToList()
                .AsReadOnly();

            return new ShadowStudyPresetDefinition
            {
                Id = "user:" + trimmedName,
                Name = trimmedName,
                IsBuiltIn = false,
                IsDefault = false,
                Description = string.Format(CultureInfo.InvariantCulture, "{0} study date(s)", specs.Count),
                DateSpecs = specs,
                TimeStepMinutes = model.TimeIncrementMinutes,
                MinSunAltitudeDegrees = model.MinSunAltitudeDegrees,
                MaxCompositeDarknessPercent = model.MaxCompositeDarknessPercent,
                ForceHiddenLine = !model.PreserveVisualStyle,
                ForceWhiteBackground = true
            };
        }

        private void ApplyPreset(ShadowStudyPresetDefinition preset)
        {
            _isApplyingPreset = true;
            try
            {
                int targetTimeStep = preset.IsBuiltIn
                    ? (ShadowStudySettings.IsValidTimeIncrement(_settings.DefaultTimeIncrementMinutes)
                        ? _settings.DefaultTimeIncrementMinutes
                        : 60)
                    : (preset.TimeStepMinutes ?? _settings.DefaultTimeIncrementMinutes);

                double targetMinAlt = preset.IsBuiltIn
                    ? (ShadowStudySettings.IsValidSunAltitude(_settings.DefaultMinSunAltitudeDegrees)
                        ? _settings.DefaultMinSunAltitudeDegrees
                        : 5.0)
                    : (preset.MinSunAltitudeDegrees ?? _settings.DefaultMinSunAltitudeDegrees);

                int targetDarkness = preset.IsBuiltIn
                    ? (ShadowStudySettings.IsValidDarknessPercent(_settings.DefaultMaxCompositeDarknessPercent)
                        ? _settings.DefaultMaxCompositeDarknessPercent
                        : 80)
                    : (preset.MaxCompositeDarknessPercent ?? _settings.DefaultMaxCompositeDarknessPercent);

                bool targetForceHiddenLine = preset.IsBuiltIn
                    ? !_settings.DefaultPreserveVisualStyle
                    : (preset.ForceHiddenLine ?? !_settings.DefaultPreserveVisualStyle);

                if (SetProperty(ref _timeStepMinutes, targetTimeStep, nameof(TimeStepMinutes)))
                {
                    ValidateTimeStep(_timeStepMinutes);
                }

                _minSunAltitudeDegrees = targetMinAlt;
                _minSunAltitudeText = _minSunAltitudeDegrees.ToString("0.##", CultureInfo.InvariantCulture);
                ValidateMinSunAltitude(_minSunAltitudeDegrees);
                OnPropertyChanged(nameof(MinSunAltitudeDegrees));
                OnPropertyChanged(nameof(MinSunAltitudeText));

                _maxCompositeDarknessPercent = targetDarkness;
                _maxCompositeDarknessText = _maxCompositeDarknessPercent.ToString(CultureInfo.InvariantCulture);
                ValidateMaxCompositeDarkness(_maxCompositeDarknessPercent);
                OnPropertyChanged(nameof(MaxCompositeDarknessPercent));
                OnPropertyChanged(nameof(MaxCompositeDarknessText));

                if (SetProperty(ref _forceHiddenLine, targetForceHiddenLine, nameof(ForceHiddenLine)))
                {
                    OnPropertyChanged(nameof(PreserveVisualStyle));
                    OnPropertyChanged(nameof(VisualStyleSummaryText));
                }

                if (preset.ForceWhiteBackground.HasValue)
                {
                    ForceWhiteBackground = preset.ForceWhiteBackground.Value;
                }
                else
                {
                    ForceWhiteBackground = true;
                }

                int effectiveYear = GetEffectiveTargetYear();
                IReadOnlyList<ShadowStudyDateEntry> expanded = BuiltInShadowStudyPresets.ExpandPreset(
                    preset,
                    effectiveYear,
                    SiteInfo);

                _studyDates.Clear();
                foreach (ShadowStudyDateEntry entry in expanded)
                {
                    if (!ContainsCalendarDate(entry.Date))
                    {
                        AddDateEntryInternal(entry);
                    }
                }

                RaiseStudyDatesChanged();
            }
            finally
            {
                _isApplyingPreset = false;
            }

            IsPresetModified = false;
            PresetErrorMessage = string.Empty;
            RaisePresetStateChanged();
        }

        private void ExecuteSavePreset()
        {
            PresetErrorMessage = string.Empty;
            if (HasValidationErrors || _studyDates.Count == 0)
            {
                PresetErrorMessage = "Select at least one study date and resolve validation errors before saving a preset.";
                return;
            }

            // Built-in presets remain read-only: Save forks into a new user-defined preset via Save As.
            if (SelectedPreset == null || SelectedPreset.IsBuiltIn)
            {
                ExecuteSavePresetAs();
                return;
            }

            string targetName = (SelectedPreset.Name ?? string.Empty).Trim();
            ShadowStudySettings candidate = _settings.DeepClone();
            int existingIndex = candidate.UserPresets.FindIndex(
                p => p != null && string.Equals((p.Name ?? string.Empty).Trim(), targetName, StringComparison.OrdinalIgnoreCase));

            ShadowStudyPresetModel updatedModel = BuildPresetModelFromCurrentState(targetName);
            if (existingIndex >= 0)
            {
                candidate.UserPresets[existingIndex] = updatedModel;
            }
            else
            {
                candidate.UserPresets.Add(updatedModel);
            }

            try
            {
                _adapter.SaveShadowStudySettings(candidate);
                _settings = candidate;
                RefreshAndSelectUserPreset(targetName, $"Saved preset '{targetName}'.");
            }
            catch (Exception ex)
            {
                PresetErrorMessage = ex.Message;
            }
        }

        private void ExecuteSavePresetAs()
        {
            PresetErrorMessage = string.Empty;
            if (HasValidationErrors || _studyDates.Count == 0)
            {
                PresetErrorMessage = "Select at least one study date and resolve validation errors before saving a preset.";
                return;
            }

            if (_textInputPromptService == null)
            {
                PresetErrorMessage = "Preset name prompt service is unavailable.";
                return;
            }

            string suggestedName = SelectedPreset != null ? $"{SelectedPreset.Name} Copy" : string.Empty;
            string? enteredName = _textInputPromptService.PromptText(
                "Save Shadow Study Preset As",
                "Enter a unique name for the Shadow Study Preset:",
                suggestedName,
                candidate => ValidatePresetName(candidate, excludingUserPresetName: null));

            if (enteredName == null)
            {
                return;
            }

            string? validationError = ValidatePresetName(enteredName, excludingUserPresetName: null);
            if (!string.IsNullOrEmpty(validationError))
            {
                PresetErrorMessage = validationError!;
                return;
            }

            string trimmedName = enteredName.Trim();
            ShadowStudySettings candidateSettings = _settings.DeepClone();
            candidateSettings.UserPresets.Add(BuildPresetModelFromCurrentState(trimmedName));

            try
            {
                _adapter.SaveShadowStudySettings(candidateSettings);
                _settings = candidateSettings;
                RefreshAndSelectUserPreset(trimmedName, $"Saved preset '{trimmedName}'.");
            }
            catch (Exception ex)
            {
                PresetErrorMessage = ex.Message;
            }
        }

        private void ExecuteRenamePreset()
        {
            PresetErrorMessage = string.Empty;
            if (SelectedPreset == null || SelectedPreset.IsBuiltIn)
            {
                return;
            }

            if (_textInputPromptService == null)
            {
                PresetErrorMessage = "Preset name prompt service is unavailable.";
                return;
            }

            string currentName = (SelectedPreset.Name ?? string.Empty).Trim();
            string? enteredName = _textInputPromptService.PromptText(
                "Rename Shadow Study Preset",
                "Enter a new name for the Shadow Study Preset:",
                currentName,
                candidate => ValidatePresetName(candidate, excludingUserPresetName: currentName));

            if (enteredName == null)
            {
                return;
            }

            string? validationError = ValidatePresetName(enteredName, excludingUserPresetName: currentName);
            if (!string.IsNullOrEmpty(validationError))
            {
                PresetErrorMessage = validationError!;
                return;
            }

            string trimmedNewName = enteredName.Trim();
            if (string.Equals(trimmedNewName, currentName, StringComparison.Ordinal))
            {
                return;
            }

            ShadowStudySettings candidateSettings = _settings.DeepClone();
            ShadowStudyPresetModel? existing = candidateSettings.UserPresets.FirstOrDefault(
                p => p != null && string.Equals((p.Name ?? string.Empty).Trim(), currentName, StringComparison.OrdinalIgnoreCase));
            if (existing == null)
            {
                PresetErrorMessage = $"Preset '{currentName}' was not found.";
                return;
            }

            existing.Name = trimmedNewName;

            try
            {
                _adapter.SaveShadowStudySettings(candidateSettings);
                _settings = candidateSettings;
                RefreshAndSelectUserPreset(trimmedNewName, $"Renamed preset to '{trimmedNewName}'.");
            }
            catch (Exception ex)
            {
                PresetErrorMessage = ex.Message;
            }
        }

        private void ExecuteDeletePreset()
        {
            PresetErrorMessage = string.Empty;
            if (SelectedPreset == null || SelectedPreset.IsBuiltIn)
            {
                return;
            }

            string currentName = (SelectedPreset.Name ?? string.Empty).Trim();
            ShadowStudySettings candidateSettings = _settings.DeepClone();
            int removed = candidateSettings.UserPresets.RemoveAll(
                p => p != null && string.Equals((p.Name ?? string.Empty).Trim(), currentName, StringComparison.OrdinalIgnoreCase));

            if (removed == 0)
            {
                PresetErrorMessage = $"Preset '{currentName}' was not found.";
                return;
            }

            try
            {
                _adapter.SaveShadowStudySettings(candidateSettings);
                _settings = candidateSettings;
                RebuildAvailablePresets();

                ShadowStudyPresetDefinition seasonal = _availablePresets.FirstOrDefault(
                    p => p.Id == BuiltInShadowStudyPresets.SeasonalStudyId) ?? BuiltInShadowStudyPresets.SeasonalStudy;
                SelectedPreset = seasonal;
                PresetStatusMessage = $"Deleted preset '{currentName}'.";
                RaisePresetStateChanged();
            }
            catch (Exception ex)
            {
                PresetErrorMessage = ex.Message;
            }
        }

        private void RefreshAndSelectUserPreset(string presetName, string statusMessage)
        {
            RebuildAvailablePresets();
            ShadowStudyPresetDefinition? matched = _availablePresets.FirstOrDefault(
                p => !p.IsBuiltIn && string.Equals(p.Name, presetName, StringComparison.OrdinalIgnoreCase));

            if (matched != null)
            {
                SelectedPreset = matched;
            }

            PresetStatusMessage = statusMessage;
            RaisePresetStateChanged();
        }

        private ShadowStudyPresetModel BuildPresetModelFromCurrentState(string presetName)
        {
            var dateSpecs = new List<ShadowStudyPresetDateSpec>(_studyDates.Count);
            foreach (ShadowStudyDateScheduleItemViewModel item in _studyDates)
            {
                if (item.AstronomicalEvent != AstronomicalEventKind.None)
                {
                    string canonicalLabel = BuiltInShadowStudyPresets.GetEventLabel(item.AstronomicalEvent);
                    string entryLabel = (item.DateEntry.PresetName ?? string.Empty).Trim();
                    string customLabel = string.Equals(entryLabel, canonicalLabel, StringComparison.Ordinal)
                        ? string.Empty
                        : entryLabel;

                    dateSpecs.Add(new ShadowStudyPresetDateSpec
                    {
                        AstronomicalEvent = item.AstronomicalEvent,
                        CustomDate = null,
                        CustomLabel = customLabel
                    });
                }
                else
                {
                    dateSpecs.Add(new ShadowStudyPresetDateSpec
                    {
                        AstronomicalEvent = AstronomicalEventKind.None,
                        CustomDate = item.Date.Date,
                        CustomLabel = string.Empty
                    });
                }
            }

            return new ShadowStudyPresetModel
            {
                Name = presetName,
                Dates = dateSpecs,
                TimeIncrementMinutes = TimeStepMinutes,
                MinSunAltitudeDegrees = MinSunAltitudeDegrees,
                MaxCompositeDarknessPercent = MaxCompositeDarknessPercent,
                PreserveVisualStyle = PreserveVisualStyle
            };
        }

        private string? ValidatePresetName(string? candidateName, string? excludingUserPresetName)
        {
            return ShadowStudySettings.ValidatePresetName(candidateName, _settings.UserPresets, excludingUserPresetName);
        }

        private void MarkPresetModifiedIfInteractive()
        {
            if (_isApplyingPreset)
            {
                return;
            }

            PresetErrorMessage = string.Empty;
            PresetStatusMessage = string.Empty;
            IsPresetModified = true;
        }

        private int GetEffectiveTargetYear()
        {
            return string.IsNullOrEmpty(_targetYearError) ? _targetYear : DateTime.Today.Year;
        }

        private void ExecuteAddAstronomicalDate()
        {
            if (!CanAddSelectedAstronomicalDate)
            {
                return;
            }

            DateTime resolvedDate = BuiltInShadowStudyPresets.ResolveDate(
                SelectedAstronomicalEventToAdd,
                GetEffectiveTargetYear(),
                SiteInfo);

            MarkPresetModifiedIfInteractive();

            AddDateEntryInternal(new ShadowStudyDateEntry
            {
                Date = resolvedDate,
                AstronomicalEvent = SelectedAstronomicalEventToAdd,
                PresetName = BuiltInShadowStudyPresets.GetEventLabel(SelectedAstronomicalEventToAdd)
            });

            RaiseStudyDatesChanged();
        }

        private void ExecuteAddCustomDate()
        {
            DateTime dateOnly = _customDateToAdd.Date;
            if (ContainsCalendarDate(dateOnly))
            {
                return;
            }

            MarkPresetModifiedIfInteractive();

            AddDateEntryInternal(new ShadowStudyDateEntry
            {
                Date = dateOnly,
                AstronomicalEvent = AstronomicalEventKind.None,
                PresetName = string.Empty
            });

            RaiseStudyDatesChanged();
        }

        private void ExecuteRemoveDate(object? parameter)
        {
            if (parameter is ShadowStudyDateScheduleItemViewModel item)
            {
                RemoveDateItem(item);
            }
        }

        private void RemoveDateItem(ShadowStudyDateScheduleItemViewModel item)
        {
            if (_studyDates.Remove(item))
            {
                MarkPresetModifiedIfInteractive();
                RaiseStudyDatesChanged();
            }
        }

        private bool ContainsCalendarDate(DateTime date)
        {
            DateTime dateOnly = date.Date;
            return _studyDates.Any(d => d.Date.Date == dateOnly);
        }

        private void AddDateEntryInternal(ShadowStudyDateEntry entry)
        {
            DateSolarSchedule schedule = BuildScheduleForEntry(entry);
            var row = new ShadowStudyDateScheduleItemViewModel(entry, schedule, RemoveDateItem);
            _studyDates.Add(row);
        }

        private void UpdateAstronomicalDatesForTargetYear(int year)
        {
            if (year < MinAllowedTargetYear || year > MaxAllowedTargetYear)
            {
                return;
            }

            foreach (ShadowStudyDateScheduleItemViewModel item in _studyDates)
            {
                if (item.AstronomicalEvent != AstronomicalEventKind.None)
                {
                    DateTime resolved = BuiltInShadowStudyPresets.ResolveDate(
                        item.AstronomicalEvent,
                        year,
                        SiteInfo);
                    item.DateEntry.Date = resolved;
                }
            }
        }

        private DateSolarSchedule BuildScheduleForEntry(ShadowStudyDateEntry entry)
        {
            int safeStep = (TimeStepMinutes > 0 && TimeStepMinutes <= 1440) ? TimeStepMinutes : 60;
            double safeMinAlt = (!double.IsNaN(MinSunAltitudeDegrees) && !double.IsInfinity(MinSunAltitudeDegrees) &&
                                 MinSunAltitudeDegrees >= MinAllowedSunAltitudeDegrees && MinSunAltitudeDegrees <= MaxAllowedSunAltitudeDegrees)
                ? MinSunAltitudeDegrees
                : 5.0;
            int safeDarkness = Math.Max(MinAllowedDarknessPercent, Math.Min(MaxAllowedDarknessPercent, MaxCompositeDarknessPercent));

            return SolarScheduleCalculator.BuildSchedule(
                entry,
                SiteInfo,
                safeStep,
                safeMinAlt,
                safeDarkness);
        }

        private void RecalculateAllSchedules()
        {
            if (HasValidationErrors)
            {
                RaiseValidationStateChanged();
                return;
            }

            foreach (ShadowStudyDateScheduleItemViewModel item in _studyDates)
            {
                DateSolarSchedule updated = BuildScheduleForEntry(item.DateEntry);
                item.UpdateSchedule(item.DateEntry.Date, updated);
            }

            RaiseStudyDatesChanged();
        }

        private void RaiseStudyDatesChanged()
        {
            OnPropertyChanged(nameof(SelectedDate));
            OnPropertyChanged(nameof(CurrentSchedule));
            OnPropertyChanged(nameof(ValidFrameCount));
            OnPropertyChanged(nameof(TotalValidFrameCount));
            OnPropertyChanged(nameof(HasNoDates));
            OnPropertyChanged(nameof(HasZeroFrames));
            OnPropertyChanged(nameof(HasSkippedZeroFrameDates));
            OnPropertyChanged(nameof(ShowPartialZeroFramesNotice));
            OnPropertyChanged(nameof(PartialZeroFramesNoticeText));
            OnPropertyChanged(nameof(StudyDatesSummaryText));
            OnPropertyChanged(nameof(PeakSolarAltitudeDegrees));
            OnPropertyChanged(nameof(SchedulePreviewText));
            OnPropertyChanged(nameof(ZeroFramesWarningText));
            OnPropertyChanged(nameof(CompositeFileNamePreview));
            OnPropertyChanged(nameof(FrameFolderPreview));
            OnPropertyChanged(nameof(CanAddSelectedAstronomicalDate));
            OnPropertyChanged(nameof(CanAddCustomDate));
            RaiseValidationStateChanged();
        }

        private void RaiseValidationStateChanged()
        {
            OnPropertyChanged(nameof(HasValidationErrors));
            OnPropertyChanged(nameof(ValidationErrorMessage));
            OnPropertyChanged(nameof(ShowSchedulePreview));
            OnPropertyChanged(nameof(ShowZeroFramesWarning));
            OnPropertyChanged(nameof(ShowPartialZeroFramesNotice));
            OnPropertyChanged(nameof(CanAddSelectedAstronomicalDate));
            RaisePresetStateChanged();
            RaiseExportStateChanged();
        }

        private void RaisePresetStateChanged()
        {
            OnPropertyChanged(nameof(IsSelectedPresetBuiltIn));
            OnPropertyChanged(nameof(IsSelectedPresetUserDefined));
            OnPropertyChanged(nameof(PresetBadgeText));
            OnPropertyChanged(nameof(CanSavePreset));
            OnPropertyChanged(nameof(CanSavePresetAs));
            OnPropertyChanged(nameof(CanRenamePreset));
            OnPropertyChanged(nameof(CanDeletePreset));
            CommandManager.InvalidateRequerySuggested();
        }

        private void RaiseExportStateChanged()
        {
            OnPropertyChanged(nameof(CanExport));
            CommandManager.InvalidateRequerySuggested();
        }
    }
}
