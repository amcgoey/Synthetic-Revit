using Synthetic.Features.MultiModelPdfExport.Models;

namespace Synthetic.Features.MultiModelPdfExport.Adapters
{
    /// <summary>
    /// Single-Seam Adapter separating Revit API document harvesting and native PDF export
    /// from headless <c>Core/</c> presentation and orchestration logic.
    /// </summary>
    public interface IMultiModelPdfExportAdapter
    {
        /// <summary>
        /// Captures a read-only snapshot of eligible project documents, Print Sets, and PDF Export Setups.
        /// </summary>
        MultiModelPdfSessionSnapshot CaptureSessionSnapshot();

        /// <summary>
        /// Executes native PDF export for a single project document job and returns a typed outcome.
        /// </summary>
        MultiModelPdfDocumentOutcome ExportDocument(MultiModelPdfDocumentJob job);
    }
}
