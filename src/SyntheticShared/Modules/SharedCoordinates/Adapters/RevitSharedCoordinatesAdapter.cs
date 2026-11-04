using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Synthetic.Modules.SharedCoordinates.Models;

namespace Synthetic.Modules.SharedCoordinates.Adapters
{
    /// <summary>
    /// Concrete Revit database adapter implementing <see cref="ISharedCoordinatesAdapter"/>.
    /// Handles all FilteredElementCollector queries and executes database mutations
    /// within a single atomic Revit transaction.
    /// </summary>
    public class RevitSharedCoordinatesAdapter : ISharedCoordinatesAdapter
    {
        private readonly Document _doc;
        private readonly UIDocument? _uidoc;

        /// <summary>
        /// Initializes a new instance of the <see cref="RevitSharedCoordinatesAdapter"/> class.
        /// </summary>
        /// <param name="doc">The active Revit document.</param>
        /// <param name="uidoc">The optional active UIDocument for detecting viewport preselection.</param>
        public RevitSharedCoordinatesAdapter(Document doc, UIDocument? uidoc = null)
        {
            _doc = doc ?? throw new ArgumentNullException(nameof(doc));
            _uidoc = uidoc;
        }

        /// <inheritdoc />
        public IReadOnlyList<LinkedModelItem> GetLoadedLinkedModels()
        {
            var linkInstances = new FilteredElementCollector(_doc)
                .OfClass(typeof(RevitLinkInstance))
                .Cast<RevitLinkInstance>()
                .ToList();

            ICollection<ElementId>? selectedIds = null;
            try
            {
                selectedIds = _uidoc?.Selection?.GetElementIds();
            }
            catch
            {
                // Headless test or non-interactive context where Selection is unavailable
            }

            var result = new List<LinkedModelItem>();

            foreach (var instance in linkInstances)
            {
                var linkDoc = instance.GetLinkDocument();
                if (linkDoc == null) continue; // Skip unloaded linked models

                var linkType = _doc.GetElement(instance.GetTypeId()) as RevitLinkType;
                string displayName = linkType?.Name ?? instance.Name;

                bool isSelected = selectedIds != null && selectedIds.Contains(instance.Id);

                result.Add(new LinkedModelItem
                {
                    Id = instance.Id.ToString(),
                    Name = displayName,
                    IsSelected = isSelected
                });
            }

            return result;
        }

        /// <inheritdoc />
        public IReadOnlyList<LevelItem> GetActiveModelLevels()
        {
            return new FilteredElementCollector(_doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .Select(l => new LevelItem
                {
                    Id = l.Id.ToString(),
                    Name = l.Name,
                    Elevation = l.ProjectElevation,
                    SharedElevation = l.ProjectElevation
                })
                .OrderBy(l => l.Elevation)
                .ToList();
        }

        /// <inheritdoc />
        public IReadOnlyList<LevelItem> GetLinkedModelLevels(string linkedModelId)
        {
            var linkInstance = new FilteredElementCollector(_doc)
                .OfClass(typeof(RevitLinkInstance))
                .Cast<RevitLinkInstance>()
                .FirstOrDefault(inst => inst.Id.ToString() == linkedModelId);

            if (linkInstance == null) return new List<LevelItem>();

            var linkDoc = linkInstance.GetLinkDocument();
            if (linkDoc == null) return new List<LevelItem>();

            var activeLoc = linkDoc.ActiveProjectLocation;
            Transform linkToSharedTransform = activeLoc != null ? activeLoc.GetTotalTransform() : Transform.Identity;

            return new FilteredElementCollector(linkDoc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .Select(l =>
                {
                    XYZ internalPt = new XYZ(0, 0, l.ProjectElevation);
                    XYZ sharedPt = linkToSharedTransform.OfPoint(internalPt);
                    return new LevelItem
                    {
                        Id = l.Id.ToString(),
                        Name = l.Name,
                        Elevation = l.ProjectElevation,
                        SharedElevation = sharedPt.Z
                    };
                })
                .OrderBy(l => l.SharedElevation)
                .ToList();
        }

        /// <inheritdoc />
        public IReadOnlyList<string> GetExistingSharedSiteNames()
        {
            return new FilteredElementCollector(_doc)
                .OfClass(typeof(ProjectLocation))
                .Cast<ProjectLocation>()
                .Select(loc => loc.Name)
                .ToList();
        }

        /// <inheritdoc />
        public SyncSharedSitesSummary SyncSharedSites(SyncSharedSitesRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (request.ReferenceDatumLevel == null) throw new ArgumentException("ReferenceDatumLevel must be specified.", nameof(request));

            var summary = new SyncSharedSitesSummary();
            var createdNames = new List<string>();
            var updatedNames = new List<string>();
            var errors = new List<string>();

            using (var trans = new Transaction(_doc, "Sync Shared Sites to Levels"))
            {
                trans.Start();

                try
                {
                    var existingLocations = new FilteredElementCollector(_doc)
                        .OfClass(typeof(ProjectLocation))
                        .Cast<ProjectLocation>()
                        .ToDictionary(loc => loc.Name, loc => loc, StringComparer.OrdinalIgnoreCase);

                    var activeLoc = _doc.ActiveProjectLocation;
                    if (activeLoc == null)
                    {
                        trans.RollBack();
                        errors.Add("The active document does not have an ActiveProjectLocation.");
                        summary.ErrorMessages = errors;
                        return summary;
                    }

                    double refDatumInternalElevation = request.ReferenceDatumLevel.Elevation;

                    foreach (var target in request.TargetSites)
                    {
                        if (!target.IsChecked) continue;

                        // Target elevation at internal origin = TargetLevelSharedElevation - ReferenceDatumLevelInternalElevation
                        double targetElevation = target.SharedElevation - refDatumInternalElevation;

                        if (existingLocations.TryGetValue(target.LevelName, out var existingLoc))
                        {
                            // Update existing site: modify only Elevation, preserving EastWest, NorthSouth, Angle
                            var curPos = existingLoc.GetProjectPosition(XYZ.Zero);
                            var newPos = new ProjectPosition(
                                curPos.EastWest,
                                curPos.NorthSouth,
                                targetElevation,
                                curPos.Angle);
                            existingLoc.SetProjectPosition(XYZ.Zero, newPos);
                            updatedNames.Add(target.LevelName);
                        }
                        else
                        {
                            // Create new site: duplicate active ProjectLocation with target level name
                            var duplicatedLoc = activeLoc.Duplicate(target.LevelName);
                            var curPos = duplicatedLoc.GetProjectPosition(XYZ.Zero);
                            var newPos = new ProjectPosition(
                                curPos.EastWest,
                                curPos.NorthSouth,
                                targetElevation,
                                curPos.Angle);
                            duplicatedLoc.SetProjectPosition(XYZ.Zero, newPos);
                            existingLocations[target.LevelName] = duplicatedLoc;
                            createdNames.Add(target.LevelName);
                        }
                    }

                    trans.Commit();
                }
                catch (Exception ex)
                {
                    if (trans.GetStatus() == TransactionStatus.Started)
                    {
                        trans.RollBack();
                    }
                    errors.Add(ex.Message);
                }
            }

            summary.CreatedCount = createdNames.Count;
            summary.UpdatedCount = updatedNames.Count;
            summary.CreatedSiteNames = createdNames;
            summary.UpdatedSiteNames = updatedNames;
            summary.ErrorMessages = errors;
            return summary;
        }

        /// <inheritdoc />
        public IReadOnlyList<LinkRepositionGroupItem> GetLinkRepositionGroups()
        {
            // Placeholder stub for Ticket #128 (Shared Site Link Repositioning)
            return new List<LinkRepositionGroupItem>();
        }

        /// <inheritdoc />
        public RepositionLinksSummary RepositionLinks(RepositionLinksRequest request)
        {
            // Placeholder stub for Ticket #128 (Shared Site Link Repositioning)
            return new RepositionLinksSummary();
        }
    }
}
