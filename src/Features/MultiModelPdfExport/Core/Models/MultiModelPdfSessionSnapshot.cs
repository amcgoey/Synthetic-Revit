using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Synthetic.Features.MultiModelPdfExport.Models
{
    /// <summary>
    /// Immutable session snapshot harvested at dialog startup across discovered documents and PDF Export Setups.
    /// </summary>
    public sealed class MultiModelPdfSessionSnapshot
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="MultiModelPdfSessionSnapshot"/> class.
        /// </summary>
        public MultiModelPdfSessionSnapshot(
            string? activeDocumentTitle,
            IEnumerable<MultiModelPdfDocumentSnapshot>? documents = null,
            IEnumerable<MultiModelPdfSetupOption>? pdfSetups = null)
        {
            ActiveDocumentTitle = activeDocumentTitle ?? string.Empty;

            var docList = documents != null
                ? documents.Where(d => d != null).ToList()
                : new List<MultiModelPdfDocumentSnapshot>();
            Documents = docList.Count == 0
                ? Array.Empty<MultiModelPdfDocumentSnapshot>()
                : (IReadOnlyList<MultiModelPdfDocumentSnapshot>)new ReadOnlyCollection<MultiModelPdfDocumentSnapshot>(docList);

            var setupList = pdfSetups != null
                ? pdfSetups.Where(s => s != null).ToList()
                : new List<MultiModelPdfSetupOption>();
            PdfSetups = setupList.Count == 0
                ? Array.Empty<MultiModelPdfSetupOption>()
                : (IReadOnlyList<MultiModelPdfSetupOption>)new ReadOnlyCollection<MultiModelPdfSetupOption>(setupList);
        }

        /// <summary>Gets the active project document title.</summary>
        public string ActiveDocumentTitle { get; }

        /// <summary>Gets all discovered project document snapshots.</summary>
        public IReadOnlyList<MultiModelPdfDocumentSnapshot> Documents { get; }

        /// <summary>Gets the active document PDF Export Setup options.</summary>
        public IReadOnlyList<MultiModelPdfSetupOption> PdfSetups { get; }
    }
}
