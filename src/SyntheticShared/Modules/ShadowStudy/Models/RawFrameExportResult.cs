using System;
using System.Collections.Generic;

namespace Synthetic.Modules.ShadowStudy.Models
{
    /// <summary>
    /// Aggregate result returned by <see cref="Adapters.IShadowStudyAdapter.ExportRawFrames"/>.
    /// </summary>
    public class RawFrameExportResult
    {
        /// <summary>
        /// Gets or sets the exported view-date frame groups.
        /// </summary>
        public IReadOnlyList<RawExportedDateFrames> DateFrameGroups { get; set; } = Array.Empty<RawExportedDateFrames>();

        /// <summary>
        /// Gets or sets non-blocking warning notices emitted during raw export.
        /// </summary>
        public IReadOnlyList<string> Warnings { get; set; } = Array.Empty<string>();
    }
}
