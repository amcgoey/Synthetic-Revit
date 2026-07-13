using System.Collections.Generic;

namespace Synthetic.Modules.RevitDOM
{
    /// <summary>
    /// Service contract for resolving relationships and dependencies between ObjectModels (POCOs)
    /// without relying on the active Revit Document or native element IDs.
    /// </summary>
    public interface IPocoIdentityService
    {
        /// <summary>
        /// Resolves a single ElementIdModel reference to its matching ElementModel from a candidate pool.
        /// </summary>
        /// <param name="model">The dependency element reference to resolve.</param>
        /// <param name="pool">The pool of candidate element models.</param>
        /// <returns>The resolved ElementModel, or null if no match was found.</returns>
        ElementModel? ResolveElement(ElementIdModel model, IEnumerable<ElementModel> pool);

        /// <summary>
        /// Resolves a collection of ElementIdModel references to their matching ElementModels.
        /// </summary>
        /// <param name="models">The collection of dependency references to resolve.</param>
        /// <param name="pool">The pool of candidate element models.</param>
        /// <returns>A collection of successfully resolved ElementModel instances.</returns>
        IEnumerable<ElementModel> ResolveElements(IEnumerable<ElementIdModel> models, IEnumerable<ElementModel> pool);
    }
}
