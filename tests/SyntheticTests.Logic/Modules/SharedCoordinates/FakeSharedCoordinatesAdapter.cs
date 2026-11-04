using System;
using System.Collections.Generic;
using System.Linq;
using Synthetic.Modules.SharedCoordinates.Adapters;
using Synthetic.Modules.SharedCoordinates.Models;

namespace SyntheticTests.Modules.SharedCoordinates
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
            return new List<LinkRepositionGroupItem>();
        }

        public RepositionLinksSummary RepositionLinks(RepositionLinksRequest request)
        {
            return new RepositionLinksSummary();
        }
    }
}
