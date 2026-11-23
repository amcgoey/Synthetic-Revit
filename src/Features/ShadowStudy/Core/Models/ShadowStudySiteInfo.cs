using System;
using System.Globalization;

namespace Synthetic.Features.ShadowStudy.Models
{
    /// <summary>
    /// Project geographic location and timezone metadata extracted from Revit for solar schedule calculation.
    /// </summary>
    public class ShadowStudySiteInfo
    {
        /// <summary>
        /// Gets or sets the site place name (city/address description).
        /// </summary>
        public string PlaceName { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the site latitude in decimal degrees (positive = North, negative = South).
        /// </summary>
        public double LatitudeDegrees { get; set; }

        /// <summary>
        /// Gets or sets the site longitude in decimal degrees (positive = East, negative = West).
        /// </summary>
        public double LongitudeDegrees { get; set; }

        /// <summary>
        /// Gets or sets the project standard timezone offset from UTC in hours (for example, -5.0 or +5.5).
        /// </summary>
        public double TimeZoneHours { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether Daylight Saving Time (+1.0 hr) is enabled.
        /// </summary>
        public bool UsesDst { get; set; }

        /// <summary>
        /// Gets the effective UTC offset in hours including Daylight Saving Time.
        /// </summary>
        public double EffectiveUtcOffsetHours => TimeZoneHours + (UsesDst ? 1.0 : 0.0);

        /// <summary>
        /// Gets a value indicating whether the site is in the Southern Hemisphere.
        /// </summary>
        public bool IsSouthernHemisphere => LatitudeDegrees < 0.0;

        /// <summary>
        /// Gets the hemisphere display label.
        /// </summary>
        public string HemisphereLabel => IsSouthernHemisphere ? "Southern Hemisphere" : "Northern Hemisphere";

        /// <summary>
        /// Gets a formatted latitude string with N/S indicator.
        /// </summary>
        public string FormattedLatitude => string.Format(
            CultureInfo.InvariantCulture,
            "{0:F4}° {1}",
            Math.Abs(LatitudeDegrees),
            LatitudeDegrees >= 0.0 ? "N" : "S");

        /// <summary>
        /// Gets a formatted longitude string with E/W indicator.
        /// </summary>
        public string FormattedLongitude => string.Format(
            CultureInfo.InvariantCulture,
            "{0:F4}° {1}",
            Math.Abs(LongitudeDegrees),
            LongitudeDegrees >= 0.0 ? "E" : "W");

        /// <summary>
        /// Gets a formatted UTC offset string (for example, UTC-05:00 or UTC+05:30).
        /// </summary>
        public string FormattedTimeZone
        {
            get
            {
                string sign = TimeZoneHours >= 0.0 ? "+" : "-";
                double absHours = Math.Abs(TimeZoneHours);
                int wholeHours = (int)Math.Floor(absHours);
                int minutes = (int)Math.Round((absHours - wholeHours) * 60.0);
                if (minutes == 60)
                {
                    wholeHours += 1;
                    minutes = 0;
                }
                return string.Format(CultureInfo.InvariantCulture, "UTC{0}{1:D2}:{2:D2}", sign, wholeHours, minutes);
            }
        }
    }
}
