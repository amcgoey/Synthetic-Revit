using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Input;
using Synthetic.Features.ShadowStudy.Models;
using Synthetic.Features.ShadowStudy.Services;
using Synthetic.Features.ShadowStudy.Settings;
using Synthetic.Platform.Settings;
using Synthetic.Platform.UI;

namespace Synthetic.Features.ShadowStudy.ViewModels
{
    /// <summary>
    /// Presentation row representing a single date specification inside a shared <see cref="ShadowStudyPresetModel"/>.
    /// </summary>
    public sealed class ShadowStudyPresetDateRowViewModel
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ShadowStudyPresetDateRowViewModel"/> class.
        /// </summary>
        public ShadowStudyPresetDateRowViewModel(
            ShadowStudyPresetDateSpec dateSpec,
            Action<ShadowStudyPresetDateRowViewModel> onRemove,
            Func<bool>? canRemove = null)
        {
            DateSpec = dateSpec ?? throw new ArgumentNullException(nameof(dateSpec));
            if (onRemove == null) throw new ArgumentNullException(nameof(onRemove));
            RemoveCommand = new RelayCommand(
                _ => onRemove(this),
                canRemove != null ? (Predicate<object>)(_ => canRemove()) : null);
        }

        /// <summary>Gets the underlying serializable date specification.</summary>
        public ShadowStudyPresetDateSpec DateSpec { get; }

        /// <summary>Gets the astronomical event kind (<see cref="AstronomicalEventKind.None"/> for custom dates).</summary>
        public AstronomicalEventKind AstronomicalEvent => DateSpec.AstronomicalEvent;

        /// <summary>Gets the custom calendar date when <see cref="AstronomicalEvent"/> is <see cref="AstronomicalEventKind.None"/>.</summary>
        public DateTime? CustomDate => DateSpec.CustomDate;

        /// <summary>Gets the optional custom label.</summary>
        public string CustomLabel => DateSpec.CustomLabel ?? string.Empty;

        /// <summary>Gets the human-readable category badge for this date entry.</summary>
        public string KindLabel => AstronomicalEvent != AstronomicalEventKind.None ? "Astronomical" : "Custom Date";

        /// <summary>Gets the formatted display title for this date entry.</summary>
        public string DisplayLabel
        {
            get
            {
                if (AstronomicalEvent != AstronomicalEventKind.None)
                {
                    string eventName = BuiltInShadowStudyPresets.GetEventLabel(AstronomicalEvent);
                    return string.IsNullOrWhiteSpace(CustomLabel)
                        ? eventName
                        : $"{eventName} — {CustomLabel.Trim()}";
                }

                return CustomDate.HasValue
                    ? CustomDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                    : "(Invalid date)";
            }
        }

        /// <summary>Gets the command that removes this date from the selected preset.</summary>
        public ICommand RemoveCommand { get; }
    }

    /// <summary>
    /// Selectable solstice/equinox option for configuring astronomical preset dates.
    /// </summary>
    public sealed class AstronomicalEventOption
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="AstronomicalEventOption"/> class.
        /// </summary>
        public AstronomicalEventOption(AstronomicalEventKind kind, string label)
        {
            Kind = kind;
            Label = label;
        }

        /// <summary>Gets the astronomical event kind.</summary>
        public AstronomicalEventKind Kind { get; }

        /// <summary>Gets the display label.</summary>
        public string Label { get; }
    }

    /// <summary>
    /// Settings Dashboard ViewModel for managing studio-wide Shadow Study defaults
    /// and shared user-defined Shadow Study Presets with deep-clone edit isolation.
    /// </summary>
    public class ShadowStudySettingsViewModel : ViewModelBase, ISettingModuleViewModel
    {
        internal static readonly IReadOnlyList<AstronomicalEventOption> DefaultAstronomicalEvents =
            new[]
            {
                new AstronomicalEventOption(AstronomicalEventKind.SpringEquinox, "Spring Equinox (Mar 20 N / Sep 22 S)"),
                new AstronomicalEventOption(AstronomicalEventKind.SummerSolstice, "Summer Solstice (Jun 21 N / Dec 21 S)"),
                new AstronomicalEventOption(AstronomicalEventKind.FallEquinox, "Fall Equinox (Sep 22 N / Mar 20 S)"),
                new AstronomicalEventOption(AstronomicalEventKind.WinterSolstice, "Winter Solstice (Dec 21 N / Jun 21 S)")
            };

        private readonly Func<ShadowStudySettings> _loadSettings;
        private readonly Action<ShadowStudySettings> _saveSettings;
        private readonly Action _deleteSettings;
        private readonly Func<bool> _isOverriddenProvider;
        private readonly ITextInputPromptService? _textInputPromptService;
        private readonly Func<ShadowStudySettings> _loadFirmwideDefaults;

        private ShadowStudySettings _settings = new ShadowStudySettings();
        private bool _isOverridden;
        private bool _isDirty;
        private bool _isSynchronizing;

        private int _defaultTimeIncrementMinutes = 60;
        private double _defaultMinSunAltitudeDegrees = 5.0;
        private string _defaultMinSunAltitudeText = "5";
        private int _defaultMaxCompositeDarknessPercent = 80;
        private string _defaultMaxCompositeDarknessText = "80";
        private bool _defaultPreserveVisualStyle;

        private string _defaultTimeStepError = string.Empty;
        private string _defaultMinSunAltitudeError = string.Empty;
        private string _defaultMaxCompositeDarknessError = string.Empty;

        private ShadowStudyPresetModel? _selectedPreset;
        private int _selectedPresetTimeIncrementMinutes = 60;
        private double _selectedPresetMinSunAltitudeDegrees = 5.0;
        private string _selectedPresetMinSunAltitudeText = "5";
        private int _selectedPresetMaxCompositeDarknessPercent = 80;
        private string _selectedPresetMaxCompositeDarknessText = "80";
        private bool _selectedPresetPreserveVisualStyle;

        private string _presetTimeStepError = string.Empty;
        private string _presetMinSunAltitudeError = string.Empty;
        private string _presetMaxCompositeDarknessError = string.Empty;

        private AstronomicalEventKind _selectedAstronomicalEventToAdd = AstronomicalEventKind.SummerSolstice;
        private DateTime _customDateToAdd = DateTime.Today;
        private string _presetStatusMessage = string.Empty;
        private string _presetErrorMessage = string.Empty;

        /// <summary>
        /// Initializes a new headless instance of the <see cref="ShadowStudySettingsViewModel"/> class
        /// using injected persistence delegates for settings management without Revit document storage dependencies.
        /// </summary>
        public ShadowStudySettingsViewModel(
            Func<ShadowStudySettings> loadSettings,
            Action<ShadowStudySettings> saveSettings,
            Action deleteSettings,
            Func<bool> isOverriddenProvider,
            ITextInputPromptService? textInputPromptService = null,
            Func<ShadowStudySettings>? loadFirmwideDefaults = null)
        {
            _loadSettings = loadSettings ?? throw new ArgumentNullException(nameof(loadSettings));
            _saveSettings = saveSettings ?? throw new ArgumentNullException(nameof(saveSettings));
            _deleteSettings = deleteSettings ?? throw new ArgumentNullException(nameof(deleteSettings));
            _isOverriddenProvider = isOverriddenProvider ?? throw new ArgumentNullException(nameof(isOverriddenProvider));
            _textInputPromptService = textInputPromptService;
            _loadFirmwideDefaults = loadFirmwideDefaults ?? LoadFirmwideDisplaySettings;

            UserPresets = new ObservableCollection<ShadowStudyPresetModel>();
            SelectedPresetDates = new ObservableCollection<ShadowStudyPresetDateRowViewModel>();

            ResetToDefaultsCommand = new RelayCommand(_ => ResetToDefaults(), _ => IsOverridden);
            AddPresetCommand = new RelayCommand(_ => ExecuteAddPreset(), _ => IsOverridden);
            RenamePresetCommand = new RelayCommand(_ => ExecuteRenamePreset(), _ => IsOverridden && SelectedPreset != null);
            DeletePresetCommand = new RelayCommand(_ => ExecuteDeletePreset(), _ => IsOverridden && SelectedPreset != null);
            AddAstronomicalDateCommand = new RelayCommand(_ => ExecuteAddAstronomicalDate(), _ => IsOverridden && CanAddSelectedAstronomicalDate);
            AddCustomDateCommand = new RelayCommand(_ => ExecuteAddCustomDate(), _ => IsOverridden && CanAddCustomDate);
            RemovePresetDateCommand = new RelayCommand(ExecuteRemovePresetDate, _ => IsOverridden && SelectedPreset != null);

            Reload();
        }

        /// <inheritdoc />
        public string ModuleName => "Shadow Study";

        /// <summary>
        /// Gets the unique settings module key (<c>"ShadowStudy"</c>).
        /// </summary>
        public string ModuleKey => ShadowStudySettings.Name;

        /// <inheritdoc />
        public bool IsDirty
        {
            get => _isDirty;
            set
            {
                if (SetProperty(ref _isDirty, value))
                {
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        /// <inheritdoc />
        public bool IsOverridden
        {
            get => _isOverridden;
            set
            {
                if (SetProperty(ref _isOverridden, value))
                {
                    IsDirty = true;
                    OnPropertyChanged(nameof(SummaryText));
                    OnPropertyChanged(nameof(IsValid));
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        /// <inheritdoc />
        public bool IsValid
        {
            get
            {
                if (!IsOverridden)
                {
                    return true;
                }

                return !HasValidationErrors && _settings != null && _settings.IsValid();
            }
        }

        /// <inheritdoc />
        public ICommand? ConfigureCommand => null;

        /// <summary>Gets the command that resets working settings to baseline studio defaults.</summary>
        public ICommand ResetToDefaultsCommand { get; }

        /// <summary>Gets the command that prompts for a unique name and adds a new shared user preset.</summary>
        public ICommand AddPresetCommand { get; }

        /// <summary>Gets the command that prompts for a new name and renames the selected user preset.</summary>
        public ICommand RenamePresetCommand { get; }

        /// <summary>Gets the command that deletes the selected user preset.</summary>
        public ICommand DeletePresetCommand { get; }

        /// <summary>Gets the command that adds the selected astronomical event to the active preset.</summary>
        public ICommand AddAstronomicalDateCommand { get; }

        /// <summary>Gets the command that adds <see cref="CustomDateToAdd"/> to the active preset.</summary>
        public ICommand AddCustomDateCommand { get; }

        /// <summary>Gets the command that removes a date row from the active preset.</summary>
        public ICommand RemovePresetDateCommand { get; }

        /// <summary>Gets the available clock-aligned time increment options.</summary>
        public IReadOnlyList<ShadowStudyViewModel.ShadowStudyTimeIncrementOption> TimeIncrementOptions =>
            ShadowStudyViewModel.DefaultTimeIncrements;

        /// <summary>Gets the selectable solstice/equinox astronomical event options.</summary>
        public IReadOnlyList<AstronomicalEventOption> AstronomicalEventOptions => DefaultAstronomicalEvents;

        /// <summary>Gets the observable collection of shared user-defined presets.</summary>
        public ObservableCollection<ShadowStudyPresetModel> UserPresets { get; }

        /// <summary>Gets the observable collection of date specifications for <see cref="SelectedPreset"/>.</summary>
        public ObservableCollection<ShadowStudyPresetDateRowViewModel> SelectedPresetDates { get; }

        /// <summary>
        /// Gets or sets the studio default clock-aligned time increment in minutes (<c>15</c>, <c>30</c>, <c>60</c>, or <c>120</c>).
        /// </summary>
        public int DefaultTimeIncrementMinutes
        {
            get => _defaultTimeIncrementMinutes;
            set
            {
                if (SetProperty(ref _defaultTimeIncrementMinutes, value))
                {
                    _defaultTimeStepError = ShadowStudySettings.IsValidTimeIncrement(value)
                        ? string.Empty
                        : "Default time increment must be 15, 30, 60, or 120 minutes.";
                    _settings.DefaultTimeIncrementMinutes = value;
                    MarkDirtyAndRefreshValidation();
                }
            }
        }

        /// <summary>
        /// Gets or sets the studio default minimum solar altitude threshold in degrees (<c>[0..90]</c>).
        /// </summary>
        public double DefaultMinSunAltitudeDegrees
        {
            get => _defaultMinSunAltitudeDegrees;
            set
            {
                if (SetProperty(ref _defaultMinSunAltitudeDegrees, value))
                {
                    _defaultMinSunAltitudeText = double.IsNaN(value) || double.IsInfinity(value)
                        ? string.Empty
                        : value.ToString("0.##", CultureInfo.InvariantCulture);
                    OnPropertyChanged(nameof(DefaultMinSunAltitudeText));
                    ValidateDefaultMinSunAltitude(value);
                    _settings.DefaultMinSunAltitudeDegrees = value;
                    MarkDirtyAndRefreshValidation();
                }
            }
        }

        /// <summary>
        /// Gets or sets the editable studio default minimum solar altitude text.
        /// </summary>
        public string DefaultMinSunAltitudeText
        {
            get => _defaultMinSunAltitudeText;
            set
            {
                string normalized = value ?? string.Empty;
                if (SetProperty(ref _defaultMinSunAltitudeText, normalized))
                {
                    if (!ShadowStudySettings.TryParseFiniteDouble(normalized, out double parsed))
                    {
                        _defaultMinSunAltitudeError = "Default min sun altitude must be a number between 0° and 90°.";
                        MarkDirtyAndRefreshValidation();
                        return;
                    }

                    SetProperty(ref _defaultMinSunAltitudeDegrees, parsed, nameof(DefaultMinSunAltitudeDegrees));
                    _settings.DefaultMinSunAltitudeDegrees = parsed;
                    ValidateDefaultMinSunAltitude(parsed);
                    MarkDirtyAndRefreshValidation();
                }
            }
        }

        /// <summary>
        /// Gets or sets the studio default maximum composite darkness percentage (<c>[1..95]</c>).
        /// </summary>
        public int DefaultMaxCompositeDarknessPercent
        {
            get => _defaultMaxCompositeDarknessPercent;
            set
            {
                if (SetProperty(ref _defaultMaxCompositeDarknessPercent, value))
                {
                    _defaultMaxCompositeDarknessText = value.ToString(CultureInfo.InvariantCulture);
                    OnPropertyChanged(nameof(DefaultMaxCompositeDarknessText));
                    ValidateDefaultMaxDarkness(value);
                    _settings.DefaultMaxCompositeDarknessPercent = value;
                    MarkDirtyAndRefreshValidation();
                }
            }
        }

        /// <summary>
        /// Gets or sets the editable studio default maximum composite darkness text.
        /// </summary>
        public string DefaultMaxCompositeDarknessText
        {
            get => _defaultMaxCompositeDarknessText;
            set
            {
                string normalized = value ?? string.Empty;
                if (SetProperty(ref _defaultMaxCompositeDarknessText, normalized))
                {
                    if (!int.TryParse(normalized.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
                    {
                        _defaultMaxCompositeDarknessError = "Default max composite darkness must be an integer between 1% and 95%.";
                        MarkDirtyAndRefreshValidation();
                        return;
                    }

                    SetProperty(ref _defaultMaxCompositeDarknessPercent, parsed, nameof(DefaultMaxCompositeDarknessPercent));
                    _settings.DefaultMaxCompositeDarknessPercent = parsed;
                    ValidateDefaultMaxDarkness(parsed);
                    MarkDirtyAndRefreshValidation();
                }
            }
        }

        /// <summary>
        /// Gets or sets a value indicating whether views preserve their existing Visual Style by default.
        /// </summary>
        public bool DefaultPreserveVisualStyle
        {
            get => _defaultPreserveVisualStyle;
            set
            {
                if (SetProperty(ref _defaultPreserveVisualStyle, value))
                {
                    _settings.DefaultPreserveVisualStyle = value;
                    MarkDirtyAndRefreshValidation();
                }
            }
        }

        /// <summary>
        /// Gets or sets the currently selected shared user-defined preset for inspection and editing.
        /// </summary>
        public ShadowStudyPresetModel? SelectedPreset
        {
            get => _selectedPreset;
            set
            {
                if (SetProperty(ref _selectedPreset, value))
                {
                    LoadSelectedPresetIntoEditor(value);
                    OnPropertyChanged(nameof(HasSelectedPreset));
                    OnPropertyChanged(nameof(SelectedPresetName));
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        /// <summary>Gets a value indicating whether a user-defined preset is currently selected.</summary>
        public bool HasSelectedPreset => _selectedPreset != null;

        /// <summary>Gets the display name of <see cref="SelectedPreset"/>.</summary>
        public string SelectedPresetName => _selectedPreset?.Name ?? string.Empty;

        /// <summary>Gets or sets the time increment in minutes for <see cref="SelectedPreset"/>.</summary>
        public int SelectedPresetTimeIncrementMinutes
        {
            get => _selectedPresetTimeIncrementMinutes;
            set
            {
                if (SetProperty(ref _selectedPresetTimeIncrementMinutes, value) && !_isSynchronizing && _selectedPreset != null)
                {
                    _presetTimeStepError = ShadowStudySettings.IsValidTimeIncrement(value)
                        ? string.Empty
                        : "Preset time increment must be 15, 30, 60, or 120 minutes.";
                    _selectedPreset.TimeIncrementMinutes = value;
                    MarkDirtyAndRefreshValidation();
                }
            }
        }

        /// <summary>Gets or sets the minimum sun altitude in degrees for <see cref="SelectedPreset"/>.</summary>
        public double SelectedPresetMinSunAltitudeDegrees
        {
            get => _selectedPresetMinSunAltitudeDegrees;
            set
            {
                if (SetProperty(ref _selectedPresetMinSunAltitudeDegrees, value) && !_isSynchronizing && _selectedPreset != null)
                {
                    _selectedPresetMinSunAltitudeText = double.IsNaN(value) || double.IsInfinity(value)
                        ? string.Empty
                        : value.ToString("0.##", CultureInfo.InvariantCulture);
                    OnPropertyChanged(nameof(SelectedPresetMinSunAltitudeText));
                    ValidatePresetMinSunAltitude(value);
                    _selectedPreset.MinSunAltitudeDegrees = value;
                    MarkDirtyAndRefreshValidation();
                }
            }
        }

        /// <summary>Gets or sets the editable minimum sun altitude text for <see cref="SelectedPreset"/>.</summary>
        public string SelectedPresetMinSunAltitudeText
        {
            get => _selectedPresetMinSunAltitudeText;
            set
            {
                string normalized = value ?? string.Empty;
                if (SetProperty(ref _selectedPresetMinSunAltitudeText, normalized) && !_isSynchronizing && _selectedPreset != null)
                {
                    if (!ShadowStudySettings.TryParseFiniteDouble(normalized, out double parsed))
                    {
                        _presetMinSunAltitudeError = "Preset min sun altitude must be a number between 0° and 90°.";
                        MarkDirtyAndRefreshValidation();
                        return;
                    }

                    SetProperty(ref _selectedPresetMinSunAltitudeDegrees, parsed, nameof(SelectedPresetMinSunAltitudeDegrees));
                    _selectedPreset.MinSunAltitudeDegrees = parsed;
                    ValidatePresetMinSunAltitude(parsed);
                    MarkDirtyAndRefreshValidation();
                }
            }
        }

        /// <summary>Gets or sets the maximum composite darkness percentage for <see cref="SelectedPreset"/>.</summary>
        public int SelectedPresetMaxCompositeDarknessPercent
        {
            get => _selectedPresetMaxCompositeDarknessPercent;
            set
            {
                if (SetProperty(ref _selectedPresetMaxCompositeDarknessPercent, value) && !_isSynchronizing && _selectedPreset != null)
                {
                    _selectedPresetMaxCompositeDarknessText = value.ToString(CultureInfo.InvariantCulture);
                    OnPropertyChanged(nameof(SelectedPresetMaxCompositeDarknessText));
                    ValidatePresetMaxDarkness(value);
                    _selectedPreset.MaxCompositeDarknessPercent = value;
                    MarkDirtyAndRefreshValidation();
                }
            }
        }

        /// <summary>Gets or sets the editable maximum composite darkness text for <see cref="SelectedPreset"/>.</summary>
        public string SelectedPresetMaxCompositeDarknessText
        {
            get => _selectedPresetMaxCompositeDarknessText;
            set
            {
                string normalized = value ?? string.Empty;
                if (SetProperty(ref _selectedPresetMaxCompositeDarknessText, normalized) && !_isSynchronizing && _selectedPreset != null)
                {
                    if (!int.TryParse(normalized.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
                    {
                        _presetMaxCompositeDarknessError = "Preset max composite darkness must be an integer between 1% and 95%.";
                        MarkDirtyAndRefreshValidation();
                        return;
                    }

                    SetProperty(ref _selectedPresetMaxCompositeDarknessPercent, parsed, nameof(SelectedPresetMaxCompositeDarknessPercent));
                    _selectedPreset.MaxCompositeDarknessPercent = parsed;
                    ValidatePresetMaxDarkness(parsed);
                    MarkDirtyAndRefreshValidation();
                }
            }
        }

        /// <summary>Gets or sets a value indicating whether <see cref="SelectedPreset"/> preserves visual styles.</summary>
        public bool SelectedPresetPreserveVisualStyle
        {
            get => _selectedPresetPreserveVisualStyle;
            set
            {
                if (SetProperty(ref _selectedPresetPreserveVisualStyle, value) && !_isSynchronizing && _selectedPreset != null)
                {
                    _selectedPreset.PreserveVisualStyle = value;
                    MarkDirtyAndRefreshValidation();
                }
            }
        }

        /// <summary>Gets or sets the astronomical event staged for addition to <see cref="SelectedPreset"/>.</summary>
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

        /// <summary>Gets or sets the custom calendar date staged for addition to <see cref="SelectedPreset"/>.</summary>
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

        /// <summary>Gets a value indicating whether <see cref="SelectedPresetDates"/> is empty.</summary>
        public bool HasNoSelectedPresetDates => SelectedPresetDates.Count == 0;

        /// <summary>Gets a value indicating whether <see cref="SelectedAstronomicalEventToAdd"/> can be added to <see cref="SelectedPreset"/>.</summary>
        public bool CanAddSelectedAstronomicalDate =>
            _selectedPreset != null &&
            _selectedAstronomicalEventToAdd != AstronomicalEventKind.None &&
            !SelectedPresetDates.Any(d => d.AstronomicalEvent == _selectedAstronomicalEventToAdd);

        /// <summary>Gets a value indicating whether <see cref="CustomDateToAdd"/> can be added to <see cref="SelectedPreset"/>.</summary>
        public bool CanAddCustomDate =>
            _selectedPreset != null &&
            !SelectedPresetDates.Any(d =>
                d.AstronomicalEvent == AstronomicalEventKind.None &&
                d.CustomDate.HasValue &&
                d.CustomDate.Value.Date == _customDateToAdd.Date);

        /// <summary>Gets the informational status message for preset CRUD operations.</summary>
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

        /// <summary>Gets a value indicating whether <see cref="PresetStatusMessage"/> is non-empty.</summary>
        public bool HasPresetStatus => !string.IsNullOrEmpty(_presetStatusMessage);

        /// <summary>Gets the inline error message for preset CRUD operations.</summary>
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

        /// <summary>Gets a value indicating whether <see cref="PresetErrorMessage"/> is non-empty.</summary>
        public bool HasPresetError => !string.IsNullOrEmpty(_presetErrorMessage);

        /// <summary>Gets a value indicating whether any studio default or preset field has validation errors.</summary>
        public bool HasValidationErrors =>
            !string.IsNullOrEmpty(_defaultTimeStepError) ||
            !string.IsNullOrEmpty(_defaultMinSunAltitudeError) ||
            !string.IsNullOrEmpty(_defaultMaxCompositeDarknessError) ||
            !string.IsNullOrEmpty(_presetTimeStepError) ||
            !string.IsNullOrEmpty(_presetMinSunAltitudeError) ||
            !string.IsNullOrEmpty(_presetMaxCompositeDarknessError) ||
            GetEmptyDatePresetErrors().Any();

        /// <summary>Gets the concatenated inline validation error message when <see cref="HasValidationErrors"/> is true.</summary>
        public string ValidationErrorMessage
        {
            get
            {
                var errors = new List<string>(8);
                if (!string.IsNullOrEmpty(_defaultTimeStepError)) errors.Add(_defaultTimeStepError);
                if (!string.IsNullOrEmpty(_defaultMinSunAltitudeError)) errors.Add(_defaultMinSunAltitudeError);
                if (!string.IsNullOrEmpty(_defaultMaxCompositeDarknessError)) errors.Add(_defaultMaxCompositeDarknessError);
                if (!string.IsNullOrEmpty(_presetTimeStepError)) errors.Add(_presetTimeStepError);
                if (!string.IsNullOrEmpty(_presetMinSunAltitudeError)) errors.Add(_presetMinSunAltitudeError);
                if (!string.IsNullOrEmpty(_presetMaxCompositeDarknessError)) errors.Add(_presetMaxCompositeDarknessError);
                errors.AddRange(GetEmptyDatePresetErrors());
                return string.Join(" ", errors);
            }
        }

        private IEnumerable<string> GetEmptyDatePresetErrors()
        {
            if (_settings?.UserPresets == null)
            {
                yield break;
            }

            foreach (ShadowStudyPresetModel preset in _settings.UserPresets)
            {
                if (preset != null && (preset.Dates == null || preset.Dates.Count == 0))
                {
                    string presetName = (preset.Name ?? string.Empty).Trim();
                    yield return $"Preset '{presetName}' must include at least one study date.";
                }
            }
        }

        /// <inheritdoc />
        public string SummaryText
        {
            get
            {
                ShadowStudySettings displaySettings;
                string prefix;

                if (!IsOverridden)
                {
                    prefix = "Using Firmwide Defaults:\n\n";
                    displaySettings = _loadFirmwideDefaults();
                }
                else
                {
                    prefix = "Overridden for Project:\n\n";
                    displaySettings = _settings ?? new ShadowStudySettings();
                }

                int presetCount = displaySettings.UserPresets?.Count ?? 0;
                string visualStyleText = displaySettings.DefaultPreserveVisualStyle ? "Preserve View Style" : "Hidden Line (HLR)";
                return string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}Default Time Increment: {1} min\nDefault Min Sun Altitude: {2:0.##}°\nDefault Max Darkness: {3}%\nDefault Visual Style: {4}\nShared User Presets: {5}",
                    prefix,
                    displaySettings.DefaultTimeIncrementMinutes,
                    displaySettings.DefaultMinSunAltitudeDegrees,
                    displaySettings.DefaultMaxCompositeDarknessPercent,
                    visualStyleText,
                    presetCount);
            }
        }

        /// <inheritdoc />
        public void Save()
        {
            if (IsOverridden)
            {
                _saveSettings(_settings.DeepClone());
            }
            else
            {
                _deleteSettings();
            }
        }

        /// <inheritdoc />
        public void Reload()
        {
            _settings = (_loadSettings() ?? new ShadowStudySettings()).DeepClone();
            if (_settings.UserPresets == null)
            {
                _settings.UserPresets = new List<ShadowStudyPresetModel>();
            }

            _isOverridden = _isOverriddenProvider();
            _isDirty = false;
            _presetStatusMessage = string.Empty;
            _presetErrorMessage = string.Empty;

            ApplySettingsToViewModel(_settings.UserPresets.FirstOrDefault()?.Name);

            OnPropertyChanged(nameof(IsOverridden));
            OnPropertyChanged(nameof(IsDirty));
            OnPropertyChanged(nameof(PresetStatusMessage));
            OnPropertyChanged(nameof(HasPresetStatus));
            OnPropertyChanged(nameof(PresetErrorMessage));
            OnPropertyChanged(nameof(HasPresetError));
            OnPropertyChanged(nameof(SummaryText));
            OnPropertyChanged(nameof(IsValid));
            CommandManager.InvalidateRequerySuggested();
        }

        /// <summary>
        /// Resets working studio defaults and clears working shared presets to baseline defaults
        /// without persisting until <see cref="Save"/> is called.
        /// </summary>
        public void ResetToDefaults()
        {
            _settings = new ShadowStudySettings().Defaults();
            _presetStatusMessage = "Reset Shadow Study settings to baseline studio defaults.";
            _presetErrorMessage = string.Empty;

            ApplySettingsToViewModel(selectedPresetName: null);
            IsDirty = true;

            OnPropertyChanged(nameof(PresetStatusMessage));
            OnPropertyChanged(nameof(HasPresetStatus));
            OnPropertyChanged(nameof(PresetErrorMessage));
            OnPropertyChanged(nameof(HasPresetError));
        }

        private void ApplySettingsToViewModel(string? selectedPresetName)
        {
            _isSynchronizing = true;
            try
            {
                _defaultTimeIncrementMinutes = _settings.DefaultTimeIncrementMinutes;
                _defaultTimeStepError = ShadowStudySettings.IsValidTimeIncrement(_defaultTimeIncrementMinutes)
                    ? string.Empty
                    : "Default time increment must be 15, 30, 60, or 120 minutes.";

                _defaultMinSunAltitudeDegrees = _settings.DefaultMinSunAltitudeDegrees;
                _defaultMinSunAltitudeText = _defaultMinSunAltitudeDegrees.ToString("0.##", CultureInfo.InvariantCulture);
                ValidateDefaultMinSunAltitude(_defaultMinSunAltitudeDegrees);

                _defaultMaxCompositeDarknessPercent = _settings.DefaultMaxCompositeDarknessPercent;
                _defaultMaxCompositeDarknessText = _defaultMaxCompositeDarknessPercent.ToString(CultureInfo.InvariantCulture);
                ValidateDefaultMaxDarkness(_defaultMaxCompositeDarknessPercent);

                _defaultPreserveVisualStyle = _settings.DefaultPreserveVisualStyle;

                RebuildUserPresets(selectedPresetName);
            }
            finally
            {
                _isSynchronizing = false;
            }

            OnPropertyChanged(nameof(DefaultTimeIncrementMinutes));
            OnPropertyChanged(nameof(DefaultMinSunAltitudeDegrees));
            OnPropertyChanged(nameof(DefaultMinSunAltitudeText));
            OnPropertyChanged(nameof(DefaultMaxCompositeDarknessPercent));
            OnPropertyChanged(nameof(DefaultMaxCompositeDarknessText));
            OnPropertyChanged(nameof(DefaultPreserveVisualStyle));
            OnPropertyChanged(nameof(HasValidationErrors));
            OnPropertyChanged(nameof(ValidationErrorMessage));
            OnPropertyChanged(nameof(SummaryText));
            OnPropertyChanged(nameof(IsValid));
        }

        private void RebuildUserPresets(string? selectedPresetName)
        {
            List<ShadowStudyPresetModel> sorted = (_settings.UserPresets ?? new List<ShadowStudyPresetModel>())
                .Where(p => p != null && !string.IsNullOrWhiteSpace(p.Name))
                .OrderBy(p => p.Name.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToList();

            UserPresets.Clear();
            foreach (ShadowStudyPresetModel preset in sorted)
            {
                UserPresets.Add(preset);
            }

            ShadowStudyPresetModel? target = !string.IsNullOrWhiteSpace(selectedPresetName)
                ? UserPresets.FirstOrDefault(p => string.Equals(p.Name.Trim(), selectedPresetName!.Trim(), StringComparison.OrdinalIgnoreCase))
                : UserPresets.FirstOrDefault();

            _selectedPreset = target;
            LoadSelectedPresetIntoEditor(target);

            OnPropertyChanged(nameof(SelectedPreset));
            OnPropertyChanged(nameof(HasSelectedPreset));
            OnPropertyChanged(nameof(SelectedPresetName));
        }

        private void LoadSelectedPresetIntoEditor(ShadowStudyPresetModel? preset)
        {
            _isSynchronizing = true;
            try
            {
                _presetTimeStepError = string.Empty;
                _presetMinSunAltitudeError = string.Empty;
                _presetMaxCompositeDarknessError = string.Empty;

                SelectedPresetDates.Clear();

                if (preset == null)
                {
                    _selectedPresetTimeIncrementMinutes = DefaultTimeIncrementMinutes;
                    _selectedPresetMinSunAltitudeDegrees = DefaultMinSunAltitudeDegrees;
                    _selectedPresetMinSunAltitudeText = _selectedPresetMinSunAltitudeDegrees.ToString("0.##", CultureInfo.InvariantCulture);
                    _selectedPresetMaxCompositeDarknessPercent = DefaultMaxCompositeDarknessPercent;
                    _selectedPresetMaxCompositeDarknessText = _selectedPresetMaxCompositeDarknessPercent.ToString(CultureInfo.InvariantCulture);
                    _selectedPresetPreserveVisualStyle = DefaultPreserveVisualStyle;
                }
                else
                {
                    if (preset.Dates == null)
                    {
                        preset.Dates = new List<ShadowStudyPresetDateSpec>();
                    }

                    _selectedPresetTimeIncrementMinutes = preset.TimeIncrementMinutes;
                    if (!ShadowStudySettings.IsValidTimeIncrement(_selectedPresetTimeIncrementMinutes))
                    {
                        _presetTimeStepError = "Preset time increment must be 15, 30, 60, or 120 minutes.";
                    }

                    _selectedPresetMinSunAltitudeDegrees = preset.MinSunAltitudeDegrees;
                    _selectedPresetMinSunAltitudeText = _selectedPresetMinSunAltitudeDegrees.ToString("0.##", CultureInfo.InvariantCulture);
                    ValidatePresetMinSunAltitude(_selectedPresetMinSunAltitudeDegrees);

                    _selectedPresetMaxCompositeDarknessPercent = preset.MaxCompositeDarknessPercent;
                    _selectedPresetMaxCompositeDarknessText = _selectedPresetMaxCompositeDarknessPercent.ToString(CultureInfo.InvariantCulture);
                    ValidatePresetMaxDarkness(_selectedPresetMaxCompositeDarknessPercent);

                    _selectedPresetPreserveVisualStyle = preset.PreserveVisualStyle;

                    foreach (ShadowStudyPresetDateSpec spec in preset.Dates.Where(d => d != null))
                    {
                        SelectedPresetDates.Add(new ShadowStudyPresetDateRowViewModel(spec, RemovePresetDate, () => IsOverridden));
                    }
                }
            }
            finally
            {
                _isSynchronizing = false;
            }

            OnPropertyChanged(nameof(SelectedPresetTimeIncrementMinutes));
            OnPropertyChanged(nameof(SelectedPresetMinSunAltitudeDegrees));
            OnPropertyChanged(nameof(SelectedPresetMinSunAltitudeText));
            OnPropertyChanged(nameof(SelectedPresetMaxCompositeDarknessPercent));
            OnPropertyChanged(nameof(SelectedPresetMaxCompositeDarknessText));
            OnPropertyChanged(nameof(SelectedPresetPreserveVisualStyle));
            OnPropertyChanged(nameof(HasNoSelectedPresetDates));
            OnPropertyChanged(nameof(CanAddSelectedAstronomicalDate));
            OnPropertyChanged(nameof(CanAddCustomDate));
            OnPropertyChanged(nameof(HasValidationErrors));
            OnPropertyChanged(nameof(ValidationErrorMessage));
            OnPropertyChanged(nameof(IsValid));
        }

        private void ExecuteAddPreset()
        {
            if (!IsOverridden)
            {
                return;
            }

            PresetErrorMessage = string.Empty;
            if (_textInputPromptService == null)
            {
                PresetErrorMessage = "Preset name prompt service is unavailable.";
                return;
            }

            string? enteredName = _textInputPromptService.PromptText(
                "Add Shadow Study Preset",
                "Enter a unique name for the Shadow Study Preset:",
                string.Empty,
                candidate => ShadowStudySettings.ValidatePresetName(candidate, _settings.UserPresets, null));

            if (enteredName == null)
            {
                return;
            }

            string? validationError = ShadowStudySettings.ValidatePresetName(enteredName, _settings.UserPresets, null);
            if (!string.IsNullOrEmpty(validationError))
            {
                PresetErrorMessage = validationError!;
                return;
            }

            string trimmedName = enteredName.Trim();
            var created = new ShadowStudyPresetModel
            {
                Name = trimmedName,
                TimeIncrementMinutes = ShadowStudySettings.IsValidTimeIncrement(DefaultTimeIncrementMinutes)
                    ? DefaultTimeIncrementMinutes
                    : 60,
                MinSunAltitudeDegrees = ShadowStudySettings.IsValidSunAltitude(DefaultMinSunAltitudeDegrees)
                    ? DefaultMinSunAltitudeDegrees
                    : 5.0,
                MaxCompositeDarknessPercent = ShadowStudySettings.IsValidDarknessPercent(DefaultMaxCompositeDarknessPercent)
                    ? DefaultMaxCompositeDarknessPercent
                    : 80,
                PreserveVisualStyle = DefaultPreserveVisualStyle,
                Dates = new List<ShadowStudyPresetDateSpec>()
            };

            _settings.UserPresets.Add(created);
            RebuildUserPresets(trimmedName);
            PresetStatusMessage = $"Added preset '{trimmedName}'.";
            MarkDirtyAndRefreshValidation();
        }

        private void ExecuteRenamePreset()
        {
            if (!IsOverridden)
            {
                return;
            }

            PresetErrorMessage = string.Empty;
            if (SelectedPreset == null)
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
                candidate => ShadowStudySettings.ValidatePresetName(candidate, _settings.UserPresets, currentName));

            if (enteredName == null)
            {
                return;
            }

            string? validationError = ShadowStudySettings.ValidatePresetName(enteredName, _settings.UserPresets, currentName);
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

            SelectedPreset.Name = trimmedNewName;
            RebuildUserPresets(trimmedNewName);
            PresetStatusMessage = $"Renamed preset to '{trimmedNewName}'.";
            MarkDirtyAndRefreshValidation();
        }

        private void ExecuteDeletePreset()
        {
            if (!IsOverridden)
            {
                return;
            }

            PresetErrorMessage = string.Empty;
            if (SelectedPreset == null)
            {
                return;
            }

            string removedName = (SelectedPreset.Name ?? string.Empty).Trim();
            _settings.UserPresets.Remove(SelectedPreset);
            RebuildUserPresets(selectedPresetName: null);
            PresetStatusMessage = $"Deleted preset '{removedName}'.";
            MarkDirtyAndRefreshValidation();
        }

        private void ExecuteAddAstronomicalDate()
        {
            if (!IsOverridden)
            {
                return;
            }

            PresetErrorMessage = string.Empty;
            if (SelectedPreset == null || !CanAddSelectedAstronomicalDate)
            {
                return;
            }

            var spec = new ShadowStudyPresetDateSpec
            {
                AstronomicalEvent = SelectedAstronomicalEventToAdd,
                CustomDate = null,
                CustomLabel = string.Empty
            };

            SelectedPreset.Dates.Add(spec);
            SelectedPresetDates.Add(new ShadowStudyPresetDateRowViewModel(spec, RemovePresetDate, () => IsOverridden));
            OnPropertyChanged(nameof(HasNoSelectedPresetDates));
            OnPropertyChanged(nameof(CanAddSelectedAstronomicalDate));
            MarkDirtyAndRefreshValidation();
        }

        private void ExecuteAddCustomDate()
        {
            if (!IsOverridden)
            {
                return;
            }

            PresetErrorMessage = string.Empty;
            if (SelectedPreset == null || !CanAddCustomDate)
            {
                return;
            }

            var spec = new ShadowStudyPresetDateSpec
            {
                AstronomicalEvent = AstronomicalEventKind.None,
                CustomDate = CustomDateToAdd.Date,
                CustomLabel = string.Empty
            };

            SelectedPreset.Dates.Add(spec);
            SelectedPresetDates.Add(new ShadowStudyPresetDateRowViewModel(spec, RemovePresetDate, () => IsOverridden));
            OnPropertyChanged(nameof(HasNoSelectedPresetDates));
            OnPropertyChanged(nameof(CanAddCustomDate));
            MarkDirtyAndRefreshValidation();
        }

        private void ExecuteRemovePresetDate(object? parameter)
        {
            if (parameter is ShadowStudyPresetDateRowViewModel row)
            {
                RemovePresetDate(row);
            }
        }

        private void RemovePresetDate(ShadowStudyPresetDateRowViewModel row)
        {
            if (!IsOverridden || SelectedPreset == null || row == null)
            {
                return;
            }

            SelectedPreset.Dates.Remove(row.DateSpec);
            SelectedPresetDates.Remove(row);
            OnPropertyChanged(nameof(HasNoSelectedPresetDates));
            OnPropertyChanged(nameof(CanAddSelectedAstronomicalDate));
            OnPropertyChanged(nameof(CanAddCustomDate));
            MarkDirtyAndRefreshValidation();
        }

        private void ValidateDefaultMinSunAltitude(double value)
        {
            _defaultMinSunAltitudeError = ShadowStudySettings.IsValidSunAltitude(value)
                ? string.Empty
                : "Default min sun altitude must be between 0° and 90°.";
        }

        private void ValidateDefaultMaxDarkness(int value)
        {
            _defaultMaxCompositeDarknessError = ShadowStudySettings.IsValidDarknessPercent(value)
                ? string.Empty
                : "Default max composite darkness must be between 1% and 95%.";
        }

        private void ValidatePresetMinSunAltitude(double value)
        {
            _presetMinSunAltitudeError = ShadowStudySettings.IsValidSunAltitude(value)
                ? string.Empty
                : "Preset min sun altitude must be between 0° and 90°.";
        }

        private void ValidatePresetMaxDarkness(int value)
        {
            _presetMaxCompositeDarknessError = ShadowStudySettings.IsValidDarknessPercent(value)
                ? string.Empty
                : "Preset max composite darkness must be between 1% and 95%.";
        }

        private void MarkDirtyAndRefreshValidation()
        {
            if (!_isSynchronizing)
            {
                IsDirty = true;
            }

            OnPropertyChanged(nameof(HasValidationErrors));
            OnPropertyChanged(nameof(ValidationErrorMessage));
            OnPropertyChanged(nameof(SummaryText));
            OnPropertyChanged(nameof(IsValid));
            CommandManager.InvalidateRequerySuggested();
        }

        private static ShadowStudySettings LoadFirmwideDisplaySettings()
        {
            var displaySettings = new ShadowStudySettings().Defaults();
            try
            {
                string defaultPath = Path.Combine(Config.addinPath, Config.defaultSettingsFile);
                if (File.Exists(defaultPath))
                {
                    Config? defaultConfig = Config.ReadFromFile(defaultPath);
                    if (defaultConfig != null && defaultConfig.Contains(ShadowStudySettings.Name))
                    {
                        displaySettings = defaultConfig.GetSettings<ShadowStudySettings>(ShadowStudySettings.Name);
                    }
                }
            }
            catch
            {
                // Fall back to baseline defaults when template/config is unreadable
            }

            return displaySettings ?? new ShadowStudySettings().Defaults();
        }
    }
}
