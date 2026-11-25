using System;

namespace Synthetic.Features.MultiModelPdfExport.Models
{
    /// <summary>
    /// Immutable per-document export job dispatched from <see cref="Services.MultiModelPdfExportPipeline"/>
    /// to <see cref="Adapters.IMultiModelPdfExportAdapter"/>.
    /// </summary>
    public sealed class MultiModelPdfDocumentJob
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="MultiModelPdfDocumentJob"/> class.
        /// </summary>
        public MultiModelPdfDocumentJob(
            string documentKey,
            string documentTitle,
            MultiModelPdfDocumentOrigin origin,
            MultiModelPdfStorageKind storage,
            string printSetName,
            string pdfSetupId,
            MultiModelPdfFileMode fileMode,
            string outputFolder,
            string outputFileToken)
        {
            if (string.IsNullOrWhiteSpace(documentKey))
                throw new ArgumentException("Document key cannot be blank.", nameof(documentKey));
            if (string.IsNullOrWhiteSpace(printSetName))
                throw new ArgumentException("Print Set name cannot be blank.", nameof(printSetName));
            if (string.IsNullOrWhiteSpace(pdfSetupId))
                throw new ArgumentException("PDF setup ID cannot be blank.", nameof(pdfSetupId));
            if (string.IsNullOrWhiteSpace(outputFolder))
                throw new ArgumentException("Output folder cannot be blank.", nameof(outputFolder));
            if (string.IsNullOrWhiteSpace(outputFileToken))
                throw new ArgumentException("Output file token cannot be blank.", nameof(outputFileToken));

            DocumentKey = documentKey;
            DocumentTitle = documentTitle ?? string.Empty;
            Origin = origin;
            Storage = storage;
            PrintSetName = printSetName;
            PdfSetupId = pdfSetupId;
            FileMode = fileMode;
            OutputFolder = outputFolder;
            OutputFileToken = outputFileToken;
        }

        /// <summary>Gets the deterministic document key.</summary>
        public string DocumentKey { get; }

        /// <summary>Gets the document title.</summary>
        public string DocumentTitle { get; }

        /// <summary>Gets the document origin.</summary>
        public MultiModelPdfDocumentOrigin Origin { get; }

        /// <summary>Gets the document storage classification.</summary>
        public MultiModelPdfStorageKind Storage { get; }

        /// <summary>Gets the selected Print Set name to export.</summary>
        public string PrintSetName { get; }

        /// <summary>Gets the selected PDF Export Setup identifier.</summary>
        public string PdfSetupId { get; }

        /// <summary>Gets the PDF output structure mode.</summary>
        public MultiModelPdfFileMode FileMode { get; }

        /// <summary>Gets the normalized destination Output Folder path.</summary>
        public string OutputFolder { get; }

        /// <summary>Gets the sanitized base filename token (without <c>.pdf</c> extension).</summary>
        public string OutputFileToken { get; }
    }
}
