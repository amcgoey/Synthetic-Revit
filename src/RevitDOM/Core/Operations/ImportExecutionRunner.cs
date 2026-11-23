using System;
using System.Collections.Generic;
using System.Linq;
using Synthetic.RevitDOM.Models;

namespace Synthetic.RevitDOM.Operations
{
    /// <summary>
    /// Enforces topological sort order on incoming ObjectModels to ensure dependency safety.
    /// Uses a bottom-up directed acyclic graph (DAG) sorting order.
    /// </summary>
    public static class ImportExecutionRunner
    {
        /// <summary>
        /// Sorts the incoming ObjectModels into a deterministic topological order.
        /// </summary>
        /// <param name="models">The incoming unsorted models.</param>
        /// <returns>A collection of sorted models.</returns>
        public static IEnumerable<ObjectModel> Sort(IEnumerable<ObjectModel> models)
        {
            if (models == null) throw new ArgumentNullException(nameof(models));

            return models
                .OrderBy(GetTypeTier)
                .ThenBy(m => (m is ElementModel em) ? (em.Class ?? string.Empty) : string.Empty, StringComparer.OrdinalIgnoreCase)
                .ThenBy(m => (m is ElementTypeModel et) ? (et.FamilyName ?? string.Empty) : string.Empty, StringComparer.OrdinalIgnoreCase)
                .ThenBy(m => (m is ElementModel em) ? (em.Name ?? string.Empty) : string.Empty, StringComparer.OrdinalIgnoreCase);
        }

        private static int GetTypeTier(ObjectModel model)
        {
            return StandardTypeMetadataRegistry.GetTier(model);
        }
    }
}
