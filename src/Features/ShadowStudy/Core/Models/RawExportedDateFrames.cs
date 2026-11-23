using System;
using System.Collections.Generic;

namespace Synthetic.Features.ShadowStudy.Models
{
    /// <summary>
    /// Raw exported frame set for one <see cref="ShadowStudyViewItem"/> and one <see cref="DateSolarSchedule"/>.
    /// </summary>
    public class RawExportedDateFrames
    {
        /// <summary>
        /// Gets or sets the target view.
        /// </summary>
        public ShadowStudyViewItem View { get; set; } = new ShadowStudyViewItem();

        /// <summary>
        /// Gets or sets the date solar schedule.
        /// </summary>
        public DateSolarSchedule DateSchedule { get; set; } = new DateSolarSchedule();

        /// <summary>
        /// Gets or sets the shared <c>ShadowIntensity = 0</c> reference PNG path for this view.
        /// </summary>
        public string SharedReferencePngPath { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the ordered exported raw frames for this view and date.
        /// </summary>
        public IReadOnlyList<RawExportedFrame> Frames { get; set; } = Array.Empty<RawExportedFrame>();
    }
}
