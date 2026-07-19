using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;

namespace Synthetic.RevitDOM.Operations.Standards
{
    /// <summary>
    /// Service interface responsible for managing the recursive extraction and dependency harvesting of Revit standards.
    /// </summary>
    public interface IStandardsExtractionOrchestrator
    {
        /// <summary>
        /// Recursively extracts elements and all their nested dependencies as standard ObjectModels.
        /// </summary>
        /// <param name="doc">The active Revit document.</param>
        /// <param name="rootElements">The starting elements chosen for extraction.</param>
        /// <param name="progress">Optional progress reporting delegate.</param>
        /// <returns>A flat list of extracted ObjectModels including nested dependencies.</returns>
        List<ObjectModel> Extract(Document doc, IEnumerable<Element> rootElements, IProgress<string>? progress = null);

        /// <summary>
        /// Recursively extracts elements and all their nested dependencies as standard ObjectModels, allowing to specify if they are templates.
        /// </summary>
        /// <param name="doc">The active Revit document.</param>
        /// <param name="rootElements">The starting elements chosen for extraction.</param>
        /// <param name="progress">Optional progress reporting delegate.</param>
        /// <param name="isTemplate">Whether to extract elements as document-agnostic templates.</param>
        /// <returns>A flat list of extracted ObjectModels including nested dependencies.</returns>
        List<ObjectModel> Extract(Document doc, IEnumerable<Element> rootElements, IProgress<string>? progress = null, bool isTemplate = false);
    }
}
