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
            var groupedInstances = linkInstances.GroupBy(inst => inst.GetTypeId());

            foreach (var group in groupedInstances)
            {
                if (!group.Any(i => i.GetLinkDocument() != null)) continue;

                bool isSelected = selectedIds != null && group.Any(i => selectedIds.Contains(i.Id));

                var repInstance = (selectedIds != null ? group.FirstOrDefault(i => selectedIds.Contains(i.Id) && i.GetLinkDocument() != null) : null)
                    ?? group.FirstOrDefault(i => i.GetLinkDocument() != null)
                    ?? group.First();

                var linkType = _doc.GetElement(group.Key) as RevitLinkType;
                string displayName = linkType?.Name ?? repInstance.Name;

                result.Add(new LinkedModelItem
                {
                    Id = repInstance.Id.ToString(),
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
                    Elevation = l.Elevation,
                    SharedElevation = l.Elevation
                })
                .OrderBy(l => l.Elevation)
                .ToList();
        }

        /// <inheritdoc />
        public IReadOnlyList<LevelItem> GetLinkedModelLevels(string linkedModelId)
        {
#if REVIT2022 || REVIT2023
            if (!int.TryParse(linkedModelId, out int idVal)) return new List<LevelItem>();
            var linkInstance = _doc.GetElement(new ElementId(idVal)) as RevitLinkInstance;
#else
            if (!long.TryParse(linkedModelId, out long idVal)) return new List<LevelItem>();
            var linkInstance = _doc.GetElement(new ElementId(idVal)) as RevitLinkInstance;
#endif

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
                    XYZ internalPt = new XYZ(0, 0, l.Elevation);
                    XYZ sharedPt = linkToSharedTransform.OfPoint(internalPt);
                    return new LevelItem
                    {
                        Id = l.Id.ToString(),
                        Name = l.Name,
                        Elevation = l.Elevation,
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
                            var newPos = CreateProjectPosition(
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
                            var newPos = CreateProjectPosition(
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
            var linkInstances = new FilteredElementCollector(_doc)
                .OfClass(typeof(RevitLinkInstance))
                .Cast<RevitLinkInstance>()
                .ToList();

            if (linkInstances.Count == 0)
            {
                return new List<LinkRepositionGroupItem>();
            }

            var hostActiveLoc = _doc.ActiveProjectLocation;
            Transform hostToSharedTransform = hostActiveLoc != null ? hostActiveLoc.GetTotalTransform() : Transform.Identity;

            var groupedInstances = linkInstances.GroupBy(inst => inst.GetTypeId());
            var result = new List<LinkRepositionGroupItem>();

            foreach (var group in groupedInstances)
            {
                var typeId = group.Key;
                var linkType = _doc.GetElement(typeId) as RevitLinkType;
                string typeName = linkType?.Name ?? $"Link Type {typeId}";

                int totalInstances = 0;
                int outOfSyncCount = 0;
                int unassignedCount = 0;
                var instanceItems = new List<LinkRepositionInstanceItem>();

                foreach (var instance in group)
                {
                    totalInstances++;

                    string? siteName = GetAssignedSiteName(instance);

                    bool isAssigned = !string.IsNullOrWhiteSpace(siteName) &&
                                     !string.Equals(siteName, "<Not Shared>", StringComparison.OrdinalIgnoreCase);

                    if (!isAssigned)
                    {
                        unassignedCount++;
                        instanceItems.Add(new LinkRepositionInstanceItem
                        {
                            InstanceId = instance.Id.ToString(),
                            InstanceName = instance.Name,
                            TypeId = typeId.ToString(),
                            TypeName = typeName,
                            AssignedSiteName = siteName ?? "<Not Shared>",
                            IsAssigned = false,
                            IsPinned = instance.Pinned,
                            IsOutOfSync = false
                        });
                        continue;
                    }

                    var linkDoc = instance.GetLinkDocument();
                    if (linkDoc == null)
                    {
                        unassignedCount++;
                        instanceItems.Add(new LinkRepositionInstanceItem
                        {
                            InstanceId = instance.Id.ToString(),
                            InstanceName = instance.Name,
                            TypeId = typeId.ToString(),
                            TypeName = typeName,
                            AssignedSiteName = siteName!,
                            IsAssigned = false,
                            IsPinned = instance.Pinned,
                            IsOutOfSync = false
                        });
                        continue;
                    }

                    var targetLoc = ResolveAssignedLocation(linkDoc, siteName!);
                    if (targetLoc == null)
                    {
                        unassignedCount++;
                        instanceItems.Add(new LinkRepositionInstanceItem
                        {
                            InstanceId = instance.Id.ToString(),
                            InstanceName = instance.Name,
                            TypeId = typeId.ToString(),
                            TypeName = typeName,
                            AssignedSiteName = siteName!,
                            IsAssigned = false,
                            IsPinned = instance.Pinned,
                            IsOutOfSync = false
                        });
                        continue;
                    }

                    Transform linkToSharedTransform = targetLoc.GetTotalTransform();
                    Transform currentTransform = instance.GetTransform();

                    var alignment = SharedCoordinatesMath.ComputeAlignment(
                        hostToSharedTransform,
                        linkToSharedTransform,
                        currentTransform);

                    if (alignment.IsOutOfSync)
                    {
                        outOfSyncCount++;
                    }

                    instanceItems.Add(new LinkRepositionInstanceItem
                    {
                        InstanceId = instance.Id.ToString(),
                        InstanceName = instance.Name,
                        TypeId = typeId.ToString(),
                        TypeName = typeName,
                        AssignedSiteName = siteName!,
                        IsAssigned = true,
                        IsPinned = instance.Pinned,
                        IsOutOfSync = alignment.IsOutOfSync,
                        DeltaAngle = alignment.DeltaAngle,
                        DeltaX = alignment.DeltaTranslation.X,
                        DeltaY = alignment.DeltaTranslation.Y,
                        DeltaZ = alignment.DeltaTranslation.Z,
                        DeltaTranslationLength = alignment.DeltaTranslationLength
                    });
                }

                result.Add(new LinkRepositionGroupItem
                {
                    TypeId = typeId.ToString(),
                    TypeName = typeName,
                    TotalInstanceCount = totalInstances,
                    OutOfSyncCount = outOfSyncCount,
                    UnassignedCount = unassignedCount,
                    IsChecked = true,
                    Instances = instanceItems
                });
            }

            return result.OrderBy(g => g.TypeName).ToList();
        }

        /// <inheritdoc />
        public RepositionLinksSummary RepositionLinks(RepositionLinksRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));

            var summary = new RepositionLinksSummary();
            var repositionedNames = new List<string>();
            var skippedNames = new List<string>();
            var errors = new List<string>();
            int alreadyAlignedCount = 0;

            if (request.SelectedTypeIds == null || request.SelectedTypeIds.Count == 0)
            {
                return summary;
            }

            var selectedTypeSet = new HashSet<string>(request.SelectedTypeIds, StringComparer.OrdinalIgnoreCase);

            using (var trans = new Transaction(_doc, "Move Links to Shared Sites"))
            {
                trans.Start();

                try
                {
                    var hostActiveLoc = _doc.ActiveProjectLocation;
                    Transform hostToSharedTransform = hostActiveLoc != null ? hostActiveLoc.GetTotalTransform() : Transform.Identity;

                    var linkInstances = new FilteredElementCollector(_doc)
                        .OfClass(typeof(RevitLinkInstance))
                        .Cast<RevitLinkInstance>()
                        .Where(inst => selectedTypeSet.Contains(inst.GetTypeId().ToString()))
                        .ToList();

                    foreach (var instance in linkInstances)
                    {
                        string? siteName = GetAssignedSiteName(instance);

                        bool isAssigned = !string.IsNullOrWhiteSpace(siteName) &&
                                         !string.Equals(siteName, "<Not Shared>", StringComparison.OrdinalIgnoreCase);

                        if (!isAssigned)
                        {
                            skippedNames.Add(instance.Name);
                            continue;
                        }

                        var linkDoc = instance.GetLinkDocument();
                        if (linkDoc == null)
                        {
                            skippedNames.Add(instance.Name);
                            continue;
                        }

                        var targetLoc = ResolveAssignedLocation(linkDoc, siteName!);
                        if (targetLoc == null)
                        {
                            skippedNames.Add(instance.Name);
                            continue;
                        }

                        Transform linkToSharedTransform = targetLoc.GetTotalTransform();
                        Transform currentTransform = instance.GetTransform();

                        var alignment = SharedCoordinatesMath.ComputeAlignment(
                            hostToSharedTransform,
                            linkToSharedTransform,
                            currentTransform);

                        if (!alignment.IsOutOfSync)
                        {
                            alreadyAlignedCount++;
                            continue;
                        }

                        bool wasPinned = instance.Pinned;
                        if (wasPinned)
                        {
                            instance.Pinned = false;
                        }

                        if (Math.Abs(alignment.DeltaAngle) > SharedCoordinatesMath.Tolerance)
                        {
                            XYZ center = currentTransform.Origin;
                            Line axis = Line.CreateBound(center, center + XYZ.BasisZ);
                            ElementTransformUtils.RotateElement(_doc, instance.Id, axis, alignment.DeltaAngle);
                        }

                        if (alignment.DeltaTranslationLength > SharedCoordinatesMath.Tolerance)
                        {
                            ElementTransformUtils.MoveElement(_doc, instance.Id, alignment.DeltaTranslation);
                        }

                        if (wasPinned)
                        {
                            instance.Pinned = true;
                        }

                        repositionedNames.Add(instance.Name);
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

            summary.RepositionedCount = repositionedNames.Count;
            summary.AlreadyAlignedCount = alreadyAlignedCount;
            summary.SkippedCount = skippedNames.Count;
            summary.RepositionedInstanceNames = repositionedNames;
            summary.SkippedInstanceNames = skippedNames;
            summary.ErrorMessages = errors;

            return summary;
        }

        private ProjectPosition CreateProjectPosition(double eastWest, double northSouth, double elevation, double angle)
        {
#if REVIT2022 || REVIT2023
            return _doc.Application.Create.NewProjectPosition(eastWest, northSouth, elevation, angle);
#else
            return new ProjectPosition(eastWest, northSouth, elevation, angle);
#endif
        }

        private static string? GetAssignedSiteName(RevitLinkInstance instance)
        {
            try
            {
                var geoParam = instance.get_Parameter(BuiltInParameter.GEO_LOCATION);
                string? siteName = geoParam?.AsValueString();
                if (string.IsNullOrWhiteSpace(siteName))
                {
                    siteName = geoParam?.AsString();
                }
                return siteName;
            }
            catch
            {
                return null;
            }
        }

        private static ProjectLocation? ResolveAssignedLocation(Document linkDoc, string siteName)
        {
            if (linkDoc == null || string.IsNullOrWhiteSpace(siteName))
            {
                return null;
            }

            return new FilteredElementCollector(linkDoc)
                .OfClass(typeof(ProjectLocation))
                .Cast<ProjectLocation>()
                .FirstOrDefault(loc => string.Equals(loc.Name, siteName, StringComparison.OrdinalIgnoreCase));
        }
    }
}
