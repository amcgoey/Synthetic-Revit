using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Synthetic.Features.MultiModelPdfExport.Models
{
    /// <summary>
    /// Immutable snapshot of a saved Print Set harvested from a Revit project document.
    /// </summary>
    public sealed class MultiModelPdfSetSnapshot
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="MultiModelPdfSetSnapshot"/> class.
        /// </summary>
        public MultiModelPdfSetSnapshot(
            string name,
            int printableViewCount,
            IEnumerable<MultiModelPdfSkippedViewInfo>? skippedViews = null)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("Print Set name cannot be blank.", nameof(name));
            }

            Name = name;
            PrintableViewCount = Math.Max(0, printableViewCount);
            var list = skippedViews != null
                ? skippedViews.Where(v => v != null).ToList()
                : new List<MultiModelPdfSkippedViewInfo>();
            SkippedViews = list.Count == 0
                ? Array.Empty<MultiModelPdfSkippedViewInfo>()
                : (IReadOnlyList<MultiModelPdfSkippedViewInfo>)new ReadOnlyCollection<MultiModelPdfSkippedViewInfo>(list);
        }

        /// <summary>Gets the Print Set name.</summary>
        public string Name { get; }

        /// <summary>Gets the count of deduplicated printable views/sheets in the Print Set.</summary>
        public int PrintableViewCount { get; }

        /// <summary>Gets the views/sheets excluded from export with reasons.</summary>
        public IReadOnlyList<MultiModelPdfSkippedViewInfo> SkippedViews { get; }
    }
}
