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
        /// Returns all saved document Print Sets ordered case-insensitively by name without mutating
        /// the document or active <c>PrintManager</c> state.
        /// </summary>
        IReadOnlyList<ShadowStudyPrintSetItem> GetPrintSets();

        /// <summary>
        /// Creates or updates a saved document Print Set in an explicit Undo-visible committed transaction,
        /// preserving any existing non-shadow members (such as sheets, schedules, legends, or drafting views)
        /// from the target or source mixed Print Set.
        /// </summary>
        /// <param name="printSetName">Target Print Set name.</param>
        /// <param name="selectedShadowViewIds">Selected direct shadow-capable graphical view <c>UniqueId</c>s.</param>
        /// <param name="saveAsNew"><c>true</c> to save a new Print Set via Save As; <c>false</c> to update an existing saved Print Set.</param>
        /// <param name="sourcePrintSetId">Optional source Print Set <c>UniqueId</c> whose non-shadow members should be preserved.</param>
        ShadowStudyPrintSetItem SavePrintSet(
            string printSetName,
            IReadOnlyList<string> selectedShadowViewIds,
            bool saveAsNew,
            string? sourcePrintSetId = null);

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
