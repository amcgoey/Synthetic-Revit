using System.Collections.Generic;
using Synthetic.Modules.RevitDOM;
using Synthetic.Modules.StandardsManagement.ViewModels;

namespace Synthetic.Modules.StandardsManagement.Utilities
{
    /// <summary>
    /// Interface for executing Phase 2 file persistence and managing overwrite guardrails.
    /// </summary>
    public interface IStandardsExportService
    {
        /// <summary>
        /// Exports the queued standards to a JSON file.
        /// </summary>
        List<SerializationResultModel> Export(
            List<QueueItemModel> fileItems,
            string? targetPath,
            List<SerializationResultModel> dbResults,
            HashSet<string> protectedPaths,
            out string? finalPathUsed);
    }
}
