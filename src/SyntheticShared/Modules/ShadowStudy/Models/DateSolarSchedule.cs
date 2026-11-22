using System;
using System.Collections.Generic;
using System.Globalization;

namespace Synthetic.Modules.ShadowStudy.Models
{
    /// <summary>
    /// Computed daylight solar schedule and calibrated ShadowIntensity for a single study date.
    /// </summary>
    public class DateSolarSchedule
    {
        /// <summary>
        /// Gets or sets the target date entry.
        /// </summary>
        public ShadowStudyDateEntry DateEntry { get; set; } = new ShadowStudyDateEntry();

        /// <summary>
        /// Gets or sets the ordered list of valid daylight frame slots meeting the altitude threshold.
        /// </summary>
        public IReadOnlyList<SolarFrameSlot> Frames { get; set; } = Array.Empty<SolarFrameSlot>();

        /// <summary>
        /// Gets or sets the peak solar altitude in degrees observed across the 24-hour evaluation window.
        /// </summary>
        public double PeakSolarAltitudeDegrees { get; set; }

        /// <summary>
        /// Gets or sets the calibrated Revit <c>View.ShadowIntensity</c> value (<c>1..100</c>, or <c>0</c> if no valid frames exist).
        /// </summary>
        public int ShadowIntensity { get; set; }

        /// <summary>
        /// Gets a value indicating whether at least one valid daylight frame exists.
        /// </summary>
        public bool HasValidFrames => Frames != null && Frames.Count > 0;

        /// <summary>
        /// Gets a concise human-readable preview of the valid daylight window or 0-frame warning.
        /// </summary>
        public string WindowSummary
        {
            get
            {
                if (!HasValidFrames)
                {
                    return string.Format(
                        CultureInfo.InvariantCulture,
                        "0 valid daylight frames (Peak solar altitude: {0:F1}°)",
                        PeakSolarAltitudeDegrees);
                }

                string first = Frames[0].ProjectLocalTime.ToString("HH:mm", CultureInfo.InvariantCulture);
                string last = Frames[Frames.Count - 1].ProjectLocalTime.ToString("HH:mm", CultureInfo.InvariantCulture);
                return string.Format(
                    CultureInfo.InvariantCulture,
                    "{0} – {1} ({2} {3}, Shadow Intensity: {4})",
                    first,
                    last,
                    Frames.Count,
                    Frames.Count == 1 ? "frame" : "frames",
                    ShadowIntensity);
            }
        }
    }
}
