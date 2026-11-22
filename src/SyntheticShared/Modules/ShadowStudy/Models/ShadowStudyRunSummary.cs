using System;
using System.Collections.Generic;

namespace Synthetic.Modules.ShadowStudy.Models
{
    /// <summary>
    /// Structured summary produced by <see cref="Services.ShadowStudyPipeline"/>.
    /// </summary>
    public class ShadowStudyRunSummary
    {
        /// <summary>
        /// Gets or sets the destination folder for exported Shadow Composites.
        /// </summary>
        public string OutputFolder { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the full paths of all generated Shadow Composite PNG files.
        /// </summary>
        public IReadOnlyList<string> CompositePaths { get; set; } = Array.Empty<string>();

        /// <summary>
        /// Gets or sets the full paths of retained individual Shadow Frame PNGs, if requested.
        /// </summary>
        public IReadOnlyList<string> SavedFramePaths { get; set; } = Array.Empty<string>();

        /// <summary>
        /// Gets or sets non-blocking warnings or skipped-date notices from the run.
        /// </summary>
        public IReadOnlyList<string> Warnings { get; set; } = Array.Empty<string>();

        /// <summary>
        /// Gets or sets the formatted labels of study dates skipped because they yielded 0 valid frames.
        /// </summary>
        public IReadOnlyList<string> SkippedDateLabels { get; set; } = Array.Empty<string>();

        /// <summary>
        /// Gets or sets a value indicating whether the run was cancelled before completion.
        /// </summary>
        public bool WasCancelled { get; set; }

        /// <summary>
        /// Gets the count of generated Shadow Composite files.
        /// </summary>
        public int GeneratedCount => CompositePaths.Count;

        /// <summary>
        /// Gets the first generated Shadow Composite path, or <c>null</c> if none were produced.
        /// </summary>
        public string? PrimaryCompositePath => CompositePaths.Count > 0 ? CompositePaths[0] : null;
    }
}
