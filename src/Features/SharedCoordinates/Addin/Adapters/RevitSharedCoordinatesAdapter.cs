using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Synthetic.Platform.Revit;
using Synthetic.Features.SharedCoordinates.Models;
using Synthetic.Features.SharedCoordinates.Services;

namespace Synthetic.Features.SharedCoordinates.Adapters
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
#if REVIT2023
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
            Transform linkTotalTransform = activeLoc != null ? activeLoc.GetTotalTransform() : Transform.Identity;

            return new FilteredElementCollector(linkDoc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .Select(l =>
                {
                    XYZ internalPt = new XYZ(0, 0, l.Elevation);
                    XYZ sharedPt = linkTotalTransform.Inverse.OfPoint(internalPt);
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

            var preprocessor = FailurePipelines.DeleteAllWarningsRecorded();

            try
            {
                _doc.ExecuteCommittedBatchGroup("Sync Shared Sites to Levels", () =>
                {
                    var existingLocations = new FilteredElementCollector(_doc)
                        .OfClass(typeof(ProjectLocation))
                        .Cast<ProjectLocation>()
                        .ToDictionary(loc => loc.Name, loc => loc, StringComparer.OrdinalIgnoreCase);

                    var activeLoc = _doc.ActiveProjectLocation;
                    if (activeLoc == null)
                    {
                        errors.Add("The active document does not have an ActiveProjectLocation.");
                        return false;
                    }

                    double refDatumInternalElevation = request.ReferenceDatumLevel.Elevation;
                    int committedCount = 0;

                    foreach (var target in request.TargetSites)
                    {
                        if (!target.IsChecked) continue;

                        preprocessor.SetContext($"SyncSite:{target.LevelName}");
                        int priorRecordCount = preprocessor.Report.Records.Count;

                        using (Transaction itemTrans = _doc.CreateTransaction($"Sync Shared Site: {target.LevelName}", preprocessor, clearAfterRollback: true))
                        {
                            bool isUpdate = existingLocations.ContainsKey(target.LevelName);
                            ProjectLocation? duplicatedLoc = null;

                            try
                            {
                                itemTrans.Start();

                                double targetElevation = target.SharedElevation - refDatumInternalElevation;
                                ProjectLocation targetLoc = isUpdate
                                    ? existingLocations[target.LevelName]
                                    : (duplicatedLoc = activeLoc.Duplicate(target.LevelName));

                                var curPos = targetLoc.GetProjectPosition(XYZ.Zero);
                                var newPos = CreateProjectPosition(
                                    curPos.EastWest,
                                    curPos.NorthSouth,
                                    targetElevation,
                                    curPos.Angle);
                                targetLoc.SetProjectPosition(XYZ.Zero, newPos);

                                TransactionStatus status = itemTrans.Commit();

                                AppendFailureRecords(preprocessor, priorRecordCount, errors);

                                if (status == TransactionStatus.Committed)
                                {
                                    committedCount++;
                                    if (isUpdate)
                                    {
                                        updatedNames.Add(target.LevelName);
                                    }
                                    else
                                    {
                                        if (duplicatedLoc != null)
                                        {
                                            existingLocations[target.LevelName] = duplicatedLoc;
                                        }
                                        createdNames.Add(target.LevelName);
                                    }
                                }
                                else
                                {
                                    if (preprocessor.Report.Records.Count == priorRecordCount)
                                    {
                                        errors.Add($"Transaction for site '{target.LevelName}' did not commit ({status}).");
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                if (itemTrans.HasStarted() && !itemTrans.HasEnded())
                                {
                                    itemTrans.RollBack();
                                }

                                AppendFailureRecords(preprocessor, priorRecordCount, errors);

                                errors.Add(ex.Message);
                            }
                        }
                    }

                    return committedCount > 0;
                });
            }
            catch (Exception ex)
            {
                createdNames.Clear();
                updatedNames.Clear();
                errors.Add(ex.Message);
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
            Transform hostTotalTransform = hostActiveLoc != null ? hostActiveLoc.GetTotalTransform() : Transform.Identity;

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

                    var linkDoc = instance.GetLinkDocument();
                    string? siteName = GetAssignedSiteName(instance, linkDoc);

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

                    Transform linkTotalTransform = targetLoc.GetTotalTransform();
                    Transform currentTransform = instance.GetTransform();

                    var alignment = SharedCoordinatesMath.ComputeAlignment(
                        ToRigidTransform3D(hostTotalTransform),
                        ToRigidTransform3D(linkTotalTransform),
                        ToRigidTransform3D(currentTransform));

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

            var preprocessor = FailurePipelines.DeleteAllWarningsRecorded();

            try
            {
                _doc.ExecuteCommittedBatchGroup("Move Links to Shared Sites", () =>
                {
                    var hostActiveLoc = _doc.ActiveProjectLocation;
                    Transform hostTotalTransform = hostActiveLoc != null ? hostActiveLoc.GetTotalTransform() : Transform.Identity;
                    int committedCount = 0;

                    var linkInstances = new FilteredElementCollector(_doc)
                        .OfClass(typeof(RevitLinkInstance))
                        .Cast<RevitLinkInstance>()
                        .Where(inst => selectedTypeSet.Contains(inst.GetTypeId().ToString()))
                        .ToList();

                    foreach (var instance in linkInstances)
                    {
                        var linkDoc = instance.GetLinkDocument();
                        string? siteName = GetAssignedSiteName(instance, linkDoc);

                        bool isAssigned = !string.IsNullOrWhiteSpace(siteName) &&
                                         !string.Equals(siteName, "<Not Shared>", StringComparison.OrdinalIgnoreCase);

                        if (!isAssigned)
                        {
                            skippedNames.Add(instance.Name);
                            continue;
                        }

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

                        Transform linkTotalTransform = targetLoc.GetTotalTransform();
                        Transform currentTransform = instance.GetTransform();

                        var alignment = SharedCoordinatesMath.ComputeAlignment(
                            ToRigidTransform3D(hostTotalTransform),
                            ToRigidTransform3D(linkTotalTransform),
                            ToRigidTransform3D(currentTransform));

                        if (!alignment.IsOutOfSync)
                        {
                            alreadyAlignedCount++;
                            continue;
                        }

                        preprocessor.SetContext($"RepositionLink:{instance.Name}");
                        int priorRecordCount = preprocessor.Report.Records.Count;

                        using (Transaction itemTrans = _doc.CreateTransaction($"Move Link: {instance.Name}", preprocessor, clearAfterRollback: true))
                        {
                            bool wasPinned = instance.Pinned;
                            try
                            {
                                itemTrans.Start();

                                if (wasPinned)
                                {
                                    instance.Pinned = false;
                                }

                                if (Math.Abs(alignment.DeltaAngle) > SharedCoordinatesMath.Tolerance)
                                {
                                    XYZ currentOrigin = instance.GetTransform().Origin;
                                    Line axis = Line.CreateBound(currentOrigin, currentOrigin + XYZ.BasisZ);
                                    ElementTransformUtils.RotateElement(_doc, instance.Id, axis, alignment.DeltaAngle);
                                }

                                Transform postRotateTransform = instance.GetTransform();
                                XYZ remainingTranslation = ToXyz(alignment.ExpectedTransform.Origin) - postRotateTransform.Origin;
                                double minMoveTolerance = Math.Max(SharedCoordinatesMath.Tolerance, _doc.Application?.ShortCurveTolerance ?? 1e-4);

                                if (remainingTranslation.GetLength() > minMoveTolerance)
                                {
                                    ElementTransformUtils.MoveElement(_doc, instance.Id, remainingTranslation);
                                }

                                if (wasPinned)
                                {
                                    instance.Pinned = true;
                                }

                                TransactionStatus status = itemTrans.Commit();

                                AppendFailureRecords(preprocessor, priorRecordCount, errors);

                                if (status == TransactionStatus.Committed)
                                {
                                    committedCount++;
                                    repositionedNames.Add(instance.Name);
                                }
                                else
                                {
                                    if (preprocessor.Report.Records.Count == priorRecordCount)
                                    {
                                        errors.Add($"Transaction for link '{instance.Name}' did not commit ({status}).");
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                if (itemTrans.HasStarted() && !itemTrans.HasEnded())
                                {
                                    itemTrans.RollBack();
                                }

                                AppendFailureRecords(preprocessor, priorRecordCount, errors);

                                errors.Add(ex.Message);
                            }
                        }
                    }

                    return committedCount > 0;
                });
            }
            catch (Exception ex)
            {
                repositionedNames.Clear();
                errors.Add(ex.Message);
            }

            summary.RepositionedCount = repositionedNames.Count;
            summary.AlreadyAlignedCount = alreadyAlignedCount;
            summary.SkippedCount = skippedNames.Count;
            summary.RepositionedInstanceNames = repositionedNames;
            summary.SkippedInstanceNames = skippedNames;
            summary.ErrorMessages = errors;

            return summary;
        }

        private static void AppendFailureRecords(
            CompositeFailuresPreprocessor preprocessor,
            int priorRecordCount,
            List<string> errors)
        {
            for (int i = priorRecordCount; i < preprocessor.Report.Records.Count; i++)
            {
                var rec = preprocessor.Report.Records[i];
                errors.Add(string.IsNullOrWhiteSpace(rec.ContextTag)
                    ? $"[{rec.Severity}] {rec.DescriptionText}"
                    : $"[{rec.Severity}] ({rec.ContextTag}) {rec.DescriptionText}");
            }
        }

        private ProjectPosition CreateProjectPosition(double eastWest, double northSouth, double elevation, double angle)
        {
#if REVIT2023
            return _doc.Application.Create.NewProjectPosition(eastWest, northSouth, elevation, angle);
#else
            return new ProjectPosition(eastWest, northSouth, elevation, angle);
#endif
        }

        internal string? GetAssignedSiteName(RevitLinkInstance instance, Document? linkDoc)
        {
            if (instance == null) return null;

            try
            {
                // 1. Check BuiltInParameter.GEO_LOCATION
                var geoParam = instance.get_Parameter(BuiltInParameter.GEO_LOCATION);
                if (geoParam != null)
                {
                    if (geoParam.StorageType == StorageType.ElementId)
                    {
                        ElementId siteElemId = geoParam.AsElementId();
                        if (siteElemId != null && siteElemId != ElementId.InvalidElementId)
                        {
                            Element? siteElem = linkDoc?.GetElement(siteElemId) ?? _doc.GetElement(siteElemId);
                            if (siteElem != null && !string.IsNullOrWhiteSpace(siteElem.Name))
                            {
                                string trimmedName = siteElem.Name.Trim();
                                if (!string.Equals(trimmedName, "<Not Shared>", StringComparison.OrdinalIgnoreCase))
                                {
                                    return trimmedName;
                                }
                            }
                        }
                    }

                    string? valString = geoParam.AsValueString();
                    if (!string.IsNullOrWhiteSpace(valString) &&
                        valString.IndexOf("<Not Shared>", StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        return valString.Trim();
                    }

                    string? strVal = geoParam.AsString();
                    if (!string.IsNullOrWhiteSpace(strVal) &&
                        strVal.IndexOf("<Not Shared>", StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        return strVal.Trim();
                    }
                }

                // 2. Fallback to pure instance.Name inspection via SharedSiteNameParser
                string instanceName = instance.Name;
                if (string.IsNullOrWhiteSpace(instanceName))
                {
                    return null;
                }

                IReadOnlyCollection<string> knownLinkSiteNames = linkDoc == null
                    ? (IReadOnlyCollection<string>)Array.Empty<string>()
                    : new FilteredElementCollector(linkDoc)
                        .OfClass(typeof(ProjectLocation))
                        .Cast<ProjectLocation>()
                        .Select(loc => loc.Name)
                        .ToList();

                return SharedSiteNameParser.ResolveFromInstanceName(instanceName, knownLinkSiteNames);
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

        private static Vector3D ToVector3D(XYZ xyz) => new Vector3D(xyz.X, xyz.Y, xyz.Z);

        private static XYZ ToXyz(Vector3D vector) => new XYZ(vector.X, vector.Y, vector.Z);

        private static RigidTransform3D ToRigidTransform3D(Transform transform)
        {
            return new RigidTransform3D(
                ToVector3D(transform.Origin),
                ToVector3D(transform.BasisX),
                ToVector3D(transform.BasisY),
                ToVector3D(transform.BasisZ));
        }
    }
}
