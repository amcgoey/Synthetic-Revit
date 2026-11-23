using System;
using System.Globalization;
using System.Windows.Input;
using Synthetic.Features.ShadowStudy.Models;
using Synthetic.Platform.UI;

namespace Synthetic.Features.ShadowStudy.ViewModels
{
    /// <summary>
    /// Presentation row item representing a single selected study date and its live calculated solar schedule.
    /// </summary>
    public class ShadowStudyDateScheduleItemViewModel : ViewModelBase
    {
        private DateSolarSchedule _schedule;

        /// <summary>
        /// Initializes a new instance of the <see cref="ShadowStudyDateScheduleItemViewModel"/> class.
        /// </summary>
        public ShadowStudyDateScheduleItemViewModel(
            ShadowStudyDateEntry dateEntry,
            DateSolarSchedule schedule,
            Action<ShadowStudyDateScheduleItemViewModel> onRemove)
        {
            DateEntry = dateEntry ?? throw new ArgumentNullException(nameof(dateEntry));
            _schedule = schedule ?? throw new ArgumentNullException(nameof(schedule));
            if (onRemove == null) throw new ArgumentNullException(nameof(onRemove));

            RemoveCommand = new RelayCommand(_ => onRemove(this));
        }

        /// <summary>Gets the underlying study date entry.</summary>
        public ShadowStudyDateEntry DateEntry { get; }

        /// <summary>Gets the calendar date.</summary>
        public DateTime Date => DateEntry.Date;

        /// <summary>Gets the optional astronomical event identity.</summary>
        public AstronomicalEventKind AstronomicalEvent => DateEntry.AstronomicalEvent;

        /// <summary>Gets the optional preset or astronomical event label.</summary>
        public string PresetName => DateEntry.PresetName;

        /// <summary>Gets the formatted <c>&lt;DateLabel&gt;</c> output segment.</summary>
        public string DateLabel => DateEntry.DateLabel;

        /// <summary>Gets the concise display title combining formatted date and event/custom tag.</summary>
        public string DisplayTitle => string.IsNullOrWhiteSpace(PresetName)
            ? Date.ToString("MMM dd, yyyy", CultureInfo.InvariantCulture) + " (Custom)"
            : string.Format(
                CultureInfo.InvariantCulture,
                "{0} — {1}",
                Date.ToString("MMM dd, yyyy", CultureInfo.InvariantCulture),
                PresetName);

        /// <summary>Gets the computed solar schedule for this date.</summary>
        public DateSolarSchedule Schedule => _schedule;

        /// <summary>Gets the count of valid daylight frames for this date.</summary>
        public int ValidFrameCount => _schedule.Frames.Count;

        /// <summary>Gets the independently calibrated <c>ShadowIntensity</c> for this date.</summary>
        public int ShadowIntensity => _schedule.ShadowIntensity;

        /// <summary>Gets the peak solar altitude in degrees for this date.</summary>
        public double PeakSolarAltitudeDegrees => _schedule.PeakSolarAltitudeDegrees;

        /// <summary>Gets a value indicating whether at least one valid daylight frame exists.</summary>
        public bool HasValidFrames => _schedule.HasValidFrames;

        /// <summary>Gets a value indicating whether this date yields 0 valid daylight frames.</summary>
        public bool HasZeroFrames => !_schedule.HasValidFrames;

        /// <summary>Gets the live valid clock-window, frame-count, and intensity summary.</summary>
        public string WindowSummary => _schedule.WindowSummary;

        /// <summary>Gets the non-blocking 0-frame warning text with peak solar altitude.</summary>
        public string ZeroFrameWarningText => string.Format(
            CultureInfo.InvariantCulture,
            "0 valid frames (Peak solar altitude: {0:F1}°) — will be skipped during export",
            PeakSolarAltitudeDegrees);

        /// <summary>Gets the command that removes this date from the study date list.</summary>
        public ICommand RemoveCommand { get; }

        /// <summary>
        /// Updates the date and schedule after a <c>TargetYear</c> or solar parameter recalculation.
        /// </summary>
        public void UpdateSchedule(DateTime updatedDate, DateSolarSchedule updatedSchedule)
        {
            DateEntry.Date = updatedDate.Date;
            _schedule = updatedSchedule ?? throw new ArgumentNullException(nameof(updatedSchedule));

            OnPropertyChanged(nameof(Date));
            OnPropertyChanged(nameof(DateLabel));
            OnPropertyChanged(nameof(DisplayTitle));
            OnPropertyChanged(nameof(Schedule));
            OnPropertyChanged(nameof(ValidFrameCount));
            OnPropertyChanged(nameof(ShadowIntensity));
            OnPropertyChanged(nameof(PeakSolarAltitudeDegrees));
            OnPropertyChanged(nameof(HasValidFrames));
            OnPropertyChanged(nameof(HasZeroFrames));
            OnPropertyChanged(nameof(WindowSummary));
            OnPropertyChanged(nameof(ZeroFrameWarningText));
        }
    }
}
