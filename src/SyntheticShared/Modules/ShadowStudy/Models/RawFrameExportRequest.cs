using System;
using System.Collections.Generic;

namespace Synthetic.Modules.ShadowStudy.Models
{
    /// <summary>
    /// Batch request passed to <see cref="Adapters.IShadowStudyAdapter.ExportRawFrames"/>
    /// shaped around a matrix of target views and date solar schedules.
    /// </summary>
    public class RawFrameExportRequest
    {
        /// <summary>
        /// Gets or sets the target graphical views to export.
        /// </summary>
        public IReadOnlyList<ShadowStudyViewItem> Views { get; set; } = Array.Empty<ShadowStudyViewItem>();

        /// <summary>
        /// Gets or sets the non-empty date solar schedules to export per view.
        /// </summary>
        public IReadOnlyList<DateSolarSchedule> DateSchedules { get; set; } = Array.Empty<DateSolarSchedule>();

        /// <summary>
        /// Gets or sets the isolated temporary root directory for raw frame PNG outputs.
        /// </summary>
        public string TempRootDirectory { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the horizontal export pixel width.
        /// </summary>
        public int PixelWidth { get; set; } = 3000;

        /// <summary>
        /// Gets or sets a value indicating whether views are temporarily switched to Hidden Line (<c>HLR</c>).
        /// </summary>
        public bool ForceHiddenLine { get; set; } = true;

        /// <summary>
        /// Gets or sets a value indicating whether pure white backgrounds are forced on supported views.
        /// </summary>
        public bool ForceWhiteBackground { get; set; } = true;

        /// <summary>
        /// Gets or sets a value indicating whether an unshadowed reference pass is exported per frame
        /// rather than once per view (for shaded styles with hourly directional lighting variation).
        /// </summary>
        public bool ExportPerFrameReference { get; set; }
    }
}
