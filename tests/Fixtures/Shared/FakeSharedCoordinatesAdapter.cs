using System;
using System.Collections.Generic;
using System.Linq;
using Synthetic.Features.SharedCoordinates.Adapters;
using Synthetic.Features.SharedCoordinates.Models;

namespace SyntheticTests.Fixtures
{
    /// <summary>
    /// Headless in-memory test double implementing <see cref="ISharedCoordinatesAdapter"/>.
    /// Records all queries and mutations without invoking Revit API binaries.
    /// </summary>
    public class FakeSharedCoordinatesAdapter : ISharedCoordinatesAdapter
    {
        public List<LinkedModelItem> LinkedModels { get; } = new List<LinkedModelItem>();
        public List<LevelItem> ActiveLevels { get; } = new List<LevelItem>();
        public Dictionary<string, List<LevelItem>> LinkedLevelsByModelId { get; } = new Dictionary<string, List<LevelItem>>();
        public Dictionary<string, (double EastWest, double NorthSouth, double Elevation, double Angle)> ExistingSites { get; } =
            new Dictionary<string, (double, double, double, double)>(StringComparer.OrdinalIgnoreCase);

        public List<string> CreatedSites { get; } = new List<string>();
        public List<string> UpdatedSites { get; } = new List<string>();
        public int SyncCallCount { get; private set; }
        public SyncSharedSitesRequest? LastRequest { get; private set; }

        public List<LinkRepositionGroupItem> RepositionGroups { get; } = new List<LinkRepositionGroupItem>();
        public List<string> RepositionedInstances { get; } = new List<string>();
        public List<(string InstanceId, bool WasPinned, double DeltaAngle, double DeltaX, double DeltaY, double DeltaZ)> RepositionedDetails { get; } =
            new List<(string, bool, double, double, double, double)>();
        public List<(string InstanceId, bool Pinned)> PinStateHistory { get; } = new List<(string, bool)>();
        public int RepositionCallCount { get; private set; }
        public RepositionLinksRequest? LastRepositionRequest { get; private set; }

        public void AddLinkedModel(string id, string name, bool isSelected = false)
        {
            LinkedModels.Add(new LinkedModelItem
            {
                Id = id,
                Name = name,
                IsSelected = isSelected
            });
        }

        public void AddActiveLevel(string name, double elevation)
        {
            ActiveLevels.Add(new LevelItem
            {
                Id = Guid.NewGuid().ToString(),
                Name = name,
                Elevation = elevation,
                SharedElevation = elevation
            });
        }

        public void AddLinkedLevel(string linkedModelId, string name, double elevation, double sharedElevation)
        {
            if (!LinkedLevelsByModelId.TryGetValue(linkedModelId, out var list))
            {
                list = new List<LevelItem>();
                LinkedLevelsByModelId[linkedModelId] = list;
            }

            list.Add(new LevelItem
            {
                Id = Guid.NewGuid().ToString(),
                Name = name,
                Elevation = elevation,
                SharedElevation = sharedElevation
            });
        }

        public void AddExistingSite(string name, double ew = 100.0, double ns = 200.0, double elev = 0.0, double angle = 0.5)
        {
            ExistingSites[name] = (ew, ns, elev, angle);
        }

        public (double EastWest, double NorthSouth, double Elevation, double Angle)? GetSitePosition(string name)
        {
            return ExistingSites.TryGetValue(name, out var pos) ? pos : null;
        }

        public void AddRepositionGroup(string typeId, string typeName, int total, int outOfSync, int unassigned, bool isChecked = true)
        {
            RepositionGroups.Add(new LinkRepositionGroupItem
            {
                TypeId = typeId,
                TypeName = typeName,
                TotalInstanceCount = total,
                OutOfSyncCount = outOfSync,
                UnassignedCount = unassigned,
                IsChecked = isChecked
            });
        }

        public void AddRepositionInstance(
            string typeId,
            string typeName,
            string instanceId,
            string instanceName,
            string assignedSiteName,
            bool isOutOfSync,
            bool isPinned,
            double deltaAngle = 0.0,
            double deltaX = 0.0,
            double deltaY = 0.0,
            double deltaZ = 0.0)
        {
            var group = RepositionGroups.FirstOrDefault(g => g.TypeId == typeId);
            if (group == null)
            {
                group = new LinkRepositionGroupItem
                {
                    TypeId = typeId,
                    TypeName = typeName,
                    IsChecked = true
                };
                RepositionGroups.Add(group);
            }

            bool isAssigned = !string.IsNullOrWhiteSpace(assignedSiteName) &&
                              !string.Equals(assignedSiteName, "<Not Shared>", StringComparison.OrdinalIgnoreCase);

            group.TotalInstanceCount++;
            if (!isAssigned)
            {
                group.UnassignedCount++;
            }
            else if (isOutOfSync)
            {
                group.OutOfSyncCount++;
            }

            double len = Math.Sqrt(deltaX * deltaX + deltaY * deltaY + deltaZ * deltaZ);

            group.Instances.Add(new LinkRepositionInstanceItem
            {
                InstanceId = instanceId,
                InstanceName = instanceName,
                TypeId = typeId,
                TypeName = typeName,
                AssignedSiteName = assignedSiteName,
                IsAssigned = isAssigned,
                IsPinned = isPinned,
                IsOutOfSync = isOutOfSync,
                DeltaAngle = deltaAngle,
                DeltaX = deltaX,
                DeltaY = deltaY,
                DeltaZ = deltaZ,
                DeltaTranslationLength = len
            });
        }

        public IReadOnlyList<LinkedModelItem> GetLoadedLinkedModels() => LinkedModels.ToList();

        public IReadOnlyList<LevelItem> GetActiveModelLevels() => ActiveLevels.ToList();

        public IReadOnlyList<LevelItem> GetLinkedModelLevels(string linkedModelId)
        {
            return LinkedLevelsByModelId.TryGetValue(linkedModelId, out var list)
                ? list.ToList()
                : new List<LevelItem>();
        }

        public IReadOnlyList<string> GetExistingSharedSiteNames() => ExistingSites.Keys.ToList();

        public SyncSharedSitesSummary SyncSharedSites(SyncSharedSitesRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (request.ReferenceDatumLevel == null) throw new ArgumentException("ReferenceDatumLevel required.", nameof(request));

            SyncCallCount++;
            LastRequest = request;

            var createdNames = new List<string>();
            var updatedNames = new List<string>();

            double refDatumElevation = request.ReferenceDatumLevel.Elevation;

            foreach (var target in request.TargetSites)
            {
                if (!target.IsChecked) continue;

                double targetElevation = target.SharedElevation - refDatumElevation;

                if (ExistingSites.TryGetValue(target.LevelName, out var existingPos))
                {
                    // Existing site: update only Elevation, preserving EastWest, NorthSouth, Angle
                    ExistingSites[target.LevelName] = (existingPos.EastWest, existingPos.NorthSouth, targetElevation, existingPos.Angle);
                    updatedNames.Add(target.LevelName);
                    UpdatedSites.Add(target.LevelName);
                }
                else
                {
                    // New site: duplicate active site coordinates, setting calculated elevation
                    ExistingSites[target.LevelName] = (100.0, 200.0, targetElevation, 0.5);
                    createdNames.Add(target.LevelName);
                    CreatedSites.Add(target.LevelName);
                }
            }

            return new SyncSharedSitesSummary
            {
                CreatedCount = createdNames.Count,
                UpdatedCount = updatedNames.Count,
                CreatedSiteNames = createdNames,
                UpdatedSiteNames = updatedNames
            };
        }

        public IReadOnlyList<LinkRepositionGroupItem> GetLinkRepositionGroups()
        {
            return RepositionGroups.ToList();
        }

        public RepositionLinksSummary RepositionLinks(RepositionLinksRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));

            RepositionCallCount++;
            LastRepositionRequest = request;

            var repositionedNames = new List<string>();
            var skippedNames = new List<string>();
            int alreadyAlignedCount = 0;

            var selectedTypeSet = new HashSet<string>(request.SelectedTypeIds, StringComparer.OrdinalIgnoreCase);

            foreach (var group in RepositionGroups.Where(g => selectedTypeSet.Contains(g.TypeId)))
            {
                foreach (var inst in group.Instances)
                {
                    if (!inst.IsAssigned)
                    {
                        skippedNames.Add(inst.InstanceName);
                        continue;
                    }

                    if (!inst.IsOutOfSync)
                    {
                        alreadyAlignedCount++;
                        continue;
                    }

                    if (inst.IsPinned)
                    {
                        PinStateHistory.Add((inst.InstanceId, false));
                    }

                    RepositionedInstances.Add(inst.InstanceName);
                    RepositionedDetails.Add((inst.InstanceId, inst.IsPinned, inst.DeltaAngle, inst.DeltaX, inst.DeltaY, inst.DeltaZ));

                    if (inst.IsPinned)
                    {
                        PinStateHistory.Add((inst.InstanceId, true));
                    }

                    inst.IsOutOfSync = false;
                    group.OutOfSyncCount = Math.Max(0, group.OutOfSyncCount - 1);
                }
            }

            return new RepositionLinksSummary
            {
                RepositionedCount = repositionedNames.Count > 0 ? repositionedNames.Count : RepositionedInstances.Count,
                RepositionedInstanceNames = RepositionedInstances.ToList(),
                AlreadyAlignedCount = alreadyAlignedCount,
                SkippedCount = skippedNames.Count,
                SkippedInstanceNames = skippedNames
            };
        }
    }
}
