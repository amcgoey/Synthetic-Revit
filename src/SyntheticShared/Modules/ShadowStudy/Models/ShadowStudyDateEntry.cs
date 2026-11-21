using System;
using Synthetic.Modules.ShadowStudy.Services;

namespace Synthetic.Modules.ShadowStudy.Models
{
    /// <summary>
    /// Represents a single target calendar date (and optional astronomical event/preset label) in a Shadow Study run.
    /// </summary>
    public class ShadowStudyDateEntry
    {
        /// <summary>
        /// Gets or sets the calendar date (time component ignored).
        /// </summary>
        public DateTime Date { get; set; } = DateTime.Today;

        /// <summary>
        /// Gets or sets the optional astronomical event identity for dynamic year/hemisphere resolution.
        /// </summary>
        public AstronomicalEventKind AstronomicalEvent { get; set; } = AstronomicalEventKind.None;

        /// <summary>
        /// Gets or sets the optional preset or astronomical event label associated with this date.
        /// </summary>
        public string PresetName { get; set; } = string.Empty;

        /// <summary>
        /// Gets the sanitized formatted date segment (<c>yyMMdd</c> or <c>yyMMdd - PresetName</c>).
        /// </summary>
        public string DateLabel
        {
            get
            {
                string effectiveLabel = !string.IsNullOrWhiteSpace(PresetName)
                    ? PresetName.Trim()
                    : BuiltInShadowStudyPresets.GetEventLabel(AstronomicalEvent);

                return ShadowStudyPathHelper.FormatDateLabel(Date, effectiveLabel);
            }
        }
    }
}
