using System;
using System.Globalization;

namespace Synthetic.Modules.ShadowStudy.Models
{
    /// <summary>
    /// Represents a single target calendar date (and optional preset name) in a Shadow Study run.
    /// </summary>
    public class ShadowStudyDateEntry
    {
        /// <summary>
        /// Gets or sets the calendar date (time component ignored).
        /// </summary>
        public DateTime Date { get; set; } = DateTime.Today;

        /// <summary>
        /// Gets or sets the optional preset label associated with this date.
        /// </summary>
        public string PresetName { get; set; } = string.Empty;

        /// <summary>
        /// Gets the formatted date segment (<c>yyMMdd</c> or <c>yyMMdd - PresetName</c>).
        /// </summary>
        public string DateLabel
        {
            get
            {
                string yyMmDd = Date.ToString("yyMMdd", CultureInfo.InvariantCulture);
                return string.IsNullOrWhiteSpace(PresetName)
                    ? yyMmDd
                    : $"{yyMmDd} - {PresetName.Trim()}";
            }
        }
    }
}
