using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Synthetic.Features.MultiModelPdfExport.Models
{
    /// <summary>
    /// Immutable snapshot of a Revit project document discovered for Multi-Model PDF Export.
    /// </summary>
    public sealed class MultiModelPdfDocumentSnapshot
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="MultiModelPdfDocumentSnapshot"/> class.
        /// </summary>
        public MultiModelPdfDocumentSnapshot(
            string documentKey,
            string documentTitle,
            string? displayPath,
            MultiModelPdfDocumentOrigin origin,
            MultiModelPdfStorageKind storage,
            bool isUnloadedLink = false,
            IEnumerable<MultiModelPdfSetSnapshot>? printSets = null)
        {
            if (string.IsNullOrWhiteSpace(documentKey))
            {
                throw new ArgumentException("Document key cannot be blank.", nameof(documentKey));
            }

            DocumentKey = documentKey;
            DocumentTitle = documentTitle ?? string.Empty;
            DisplayPath = displayPath ?? string.Empty;
            Origin = origin;
            Storage = storage;
            IsUnloadedLink = isUnloadedLink;

            var sets = printSets != null
                ? printSets.Where(s => s != null).ToList()
                : new List<MultiModelPdfSetSnapshot>();
            PrintSets = sets.Count == 0
                ? Array.Empty<MultiModelPdfSetSnapshot>()
                : (IReadOnlyList<MultiModelPdfSetSnapshot>)new ReadOnlyCollection<MultiModelPdfSetSnapshot>(sets);
        }

        /// <summary>Gets the deterministic document identity key.</summary>
        public string DocumentKey { get; }

        /// <summary>Gets the Revit document title.</summary>
        public string DocumentTitle { get; }

        /// <summary>Gets the display path shown in row tooltips.</summary>
        public string DisplayPath { get; }

        /// <summary>Gets how the document entered the session.</summary>
        public MultiModelPdfDocumentOrigin Origin { get; }

        /// <summary>Gets the backing storage classification.</summary>
        public MultiModelPdfStorageKind Storage { get; }

        /// <summary>Gets a value indicating whether this row represents an unloaded Revit link.</summary>
        public bool IsUnloadedLink { get; }

        /// <summary>Gets the saved Print Sets harvested from the document.</summary>
        public IReadOnlyList<MultiModelPdfSetSnapshot> PrintSets { get; }
    }
}
