using System;
using System.Globalization;

namespace Synthetic.Modules.ShadowStudy.Models
{
    /// <summary>
    /// Represents one valid daylight timestamp slot for a Shadow Frame export.
    /// </summary>
    public class SolarFrameSlot
    {
        /// <summary>
        /// Gets or sets the project-local wall-clock timestamp (<see cref="DateTimeKind.Unspecified"/>).
        /// </summary>
        public DateTime ProjectLocalTime { get; set; }

        /// <summary>
        /// Gets or sets the equivalent UTC timestamp (<see cref="DateTimeKind.Utc"/>).
        /// </summary>
        public DateTime UtcTime { get; set; }

        /// <summary>
        /// Gets or sets the calculated solar altitude above the horizon in decimal degrees.
        /// </summary>
        public double SolarAltitudeDegrees { get; set; }

        /// <summary>
        /// Gets or sets the calculated solar azimuth clockwise from North in decimal degrees.
        /// </summary>
        public double SolarAzimuthDegrees { get; set; }

        /// <summary>
        /// Gets the 24-hour <c>HHmm</c> timestamp stem used for frame PNG naming.
        /// </summary>
        public string FrameFileStem => ProjectLocalTime.ToString("HHmm", CultureInfo.InvariantCulture);
    }
}
