using System.Collections.Generic;
using Synthetic.Modules.SharedCoordinates.Models;

namespace Synthetic.Modules.SharedCoordinates.Adapters
{
    /// <summary>
    /// Single seam interface isolating all Revit database operations for Shared Coordinates workflows.
    /// In accordance with ADR 023, decouples ViewModel logic, UI interactions, and coordinate math
    /// from native Revit API dependencies to enable comprehensive headless testability.
    /// </summary>
    public interface ISharedCoordinatesAdapter
    {
        /// <summary>
        /// Retrieves all loaded linked models in the active document.
        /// </summary>
        /// <returns>A list of loaded linked models with their identifiers and selection status.</returns>
        IReadOnlyList<LinkedModelItem> GetLoadedLinkedModels();

        /// <summary>
        /// Retrieves all levels defined in the active unit model.
        /// </summary>
        /// <returns>A list of active model levels.</returns>
        IReadOnlyList<LevelItem> GetActiveModelLevels();

        /// <summary>
        /// Retrieves all levels defined in the specified linked model, including their shared coordinate elevations.
        /// </summary>
        /// <param name="linkedModelId">The unique identifier of the linked model.</param>
        /// <returns>A list of linked model levels sorted by shared coordinate elevation.</returns>
        IReadOnlyList<LevelItem> GetLinkedModelLevels(string linkedModelId);

        /// <summary>
        /// Retrieves the names of all existing ProjectLocation elements (Level-Based Shared Sites) in the active document.
        /// </summary>
        /// <returns>A list of existing shared site names.</returns>
        IReadOnlyList<string> GetExistingSharedSiteNames();

        /// <summary>
        /// Synchronizes Level-Based Shared Sites in the active unit model from the selected linked host model levels.
        /// Executes all creations and updates within a single atomic Revit transaction.
        /// </summary>
        /// <param name="request">The synchronization request containing the selected linked model, Reference Datum Level, and target sites.</param>
        /// <returns>A summary detailing created and updated Level-Based Shared Sites.</returns>
        SyncSharedSitesSummary SyncSharedSites(SyncSharedSitesRequest request);

        /// <summary>
        /// Retrieves grouped link instance summaries for Shared Site Link Repositioning.
        /// </summary>
        /// <returns>A list of link reposition group items.</returns>
        IReadOnlyList<LinkRepositionGroupItem> GetLinkRepositionGroups();

        /// <summary>
        /// Repositions out-of-sync linked model instances to align with their assigned Shared Sites in 3D.
        /// </summary>
        /// <param name="request">The repositioning request containing selected link types.</param>
        /// <returns>A summary detailing repositioned, already aligned, and skipped instances.</returns>
        RepositionLinksSummary RepositionLinks(RepositionLinksRequest request);
    }
}
