using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Synthetic.Features.MultiModelPdfExport.Models
{
    /// <summary>
    /// Immutable batch execution request built by <see cref="ViewModels.MultiModelPdfExportViewModel.BuildRunRequest"/>.
    /// </summary>
    public sealed class MultiModelPdfRunRequest
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="MultiModelPdfRunRequest"/> class.
        /// </summary>
        public MultiModelPdfRunRequest(
            IEnumerable<MultiModelPdfDocumentJob>? jobs,
            string outputFolder,
            string pdfSetupId,
            MultiModelPdfFileMode fileMode)
        {
            if (string.IsNullOrWhiteSpace(outputFolder))
                throw new ArgumentException("Output folder cannot be blank.", nameof(outputFolder));
            if (string.IsNullOrWhiteSpace(pdfSetupId))
                throw new ArgumentException("PDF setup ID cannot be blank.", nameof(pdfSetupId));

            var list = jobs != null
                ? jobs.Where(j => j != null).ToList()
                : new List<MultiModelPdfDocumentJob>();
            Jobs = list.Count == 0
                ? Array.Empty<MultiModelPdfDocumentJob>()
                : (IReadOnlyList<MultiModelPdfDocumentJob>)new ReadOnlyCollection<MultiModelPdfDocumentJob>(list);
            OutputFolder = outputFolder;
            PdfSetupId = pdfSetupId;
            FileMode = fileMode;
        }

        /// <summary>Gets the ordered document export jobs.</summary>
        public IReadOnlyList<MultiModelPdfDocumentJob> Jobs { get; }

        /// <summary>Gets the destination Output Folder.</summary>
        public string OutputFolder { get; }

        /// <summary>Gets the selected PDF Export Setup ID.</summary>
        public string PdfSetupId { get; }

        /// <summary>Gets the PDF output structure mode.</summary>
        public MultiModelPdfFileMode FileMode { get; }
    }
}
