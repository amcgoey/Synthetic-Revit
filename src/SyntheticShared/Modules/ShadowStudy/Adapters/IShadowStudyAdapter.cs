using System;
using System.Collections.Generic;
using System.Threading;
using Synthetic.Modules.ShadowStudy.Models;
using Synthetic.Shared.UI;

namespace Synthetic.Modules.ShadowStudy.Adapters
{
    /// <summary>
    /// Narrow Revit port abstracting geographic site retrieval, graphical view discovery,
    /// and non-mutating raw Shadow Frame PNG export.
    /// </summary>
    public interface IShadowStudyAdapter
    {
        /// <summary>
        /// Extracts project site coordinates, timezone, and DST status from the active Revit document.
        /// </summary>
        ShadowStudySiteInfo GetSiteInfo();

        /// <summary>
        /// Returns the active Revit view as a <see cref="ShadowStudyViewItem"/> if it is shadow-capable and eligible,
        /// or <c>null</c> if there is no active view or the active view is ineligible.
        /// </summary>
        ShadowStudyViewItem? GetActiveViewInfo();

        /// <summary>
        /// Returns all eligible shadow-capable graphical views in the active document.
        /// </summary>
        IReadOnlyList<ShadowStudyViewItem> GetEligibleViews();

        /// <summary>
        /// Exports unshadowed reference pass(es) and calibrated Shadow Frame PNGs into temporary subdirectories
        /// inside a single unconditionally rolled-back transaction group.
        /// </summary>
        RawFrameExportResult ExportRawFrames(
            RawFrameExportRequest request,
            IProgress<ProgressState>? progress = null,
            CancellationToken cancellationToken = default);
    }
}
