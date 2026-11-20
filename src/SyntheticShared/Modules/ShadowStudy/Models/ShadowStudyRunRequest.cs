using System;
using System.Collections.Generic;

namespace Synthetic.Modules.ShadowStudy.Models
{
    /// <summary>
    /// Headless execution request consumed by <see cref="Services.ShadowStudyPipeline"/>.
    /// </summary>
    public class ShadowStudyRunRequest
    {
        /// <summary>
        /// Gets or sets the target graphical views.
        /// </summary>
        public IReadOnlyList<ShadowStudyViewItem> Views { get; set; } = Array.Empty<ShadowStudyViewItem>();

        /// <summary>
        /// Gets or sets the study date solar schedules.
        /// </summary>
        public IReadOnlyList<DateSolarSchedule> DateSchedules { get; set; } = Array.Empty<DateSolarSchedule>();

        /// <summary>
        /// Gets or sets the destination folder where final Shadow Composite PNGs are written.
        /// </summary>
        public string OutputFolder { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the horizontal export pixel width.
        /// </summary>
        public int PixelWidth { get; set; } = 3000;

        /// <summary>
        /// Gets or sets the target maximum composite shadow darkness percentage (<c>1..95</c>).
        /// </summary>
        public int MaxCompositeDarknessPercent { get; set; } = 80;

        /// <summary>
        /// Gets or sets a value indicating whether views use Hidden Line (<c>HLR</c>) during export.
        /// </summary>
        public bool ForceHiddenLine { get; set; } = true;

        /// <summary>
        /// Gets or sets a value indicating whether supported views use a pure white gradient background.
        /// </summary>
        public bool ForceWhiteBackground { get; set; } = true;

        /// <summary>
        /// Gets or sets a value indicating whether individual frame PNGs are retained alongside composites.
        /// </summary>
        public bool KeepIndividualFrames { get; set; }
    }
}
