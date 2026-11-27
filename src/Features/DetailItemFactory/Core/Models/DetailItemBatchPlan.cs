using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Synthetic.Features.DetailItemFactory.Settings;

namespace Synthetic.Features.DetailItemFactory.Models
{
    /// <summary>
    /// Disposition assigned to each planned element by <see cref="Services.DetailItemBatchPlanner"/>.
    /// </summary>
    public enum DetailItemPlanDisposition
    {
        /// <summary>
        /// Execute full DWG export, family creation, tracing, save, and host load.
        /// </summary>
        Execute = 0,

        /// <summary>
        /// Skip because the target <c>.rfa</c> already exists on disk and overwrite is disabled.
        /// </summary>
        SkipExisting = 1,

        /// <summary>
        /// Skip because an earlier item in the batch already targets the same <c>.rfa</c> path (case-insensitively).
        /// </summary>
        SkipDuplicate = 2
    }

    /// <summary>
    /// Immutable user-configured element selection passed to <see cref="Services.DetailItemBatchPlanner"/>.
    /// </summary>
    public sealed class DetailItemElementSelection
    {
        public string ElementUniqueId { get; }
        public string ElementId { get; }
        public string DisplayName { get; }
        public string FamilyName { get; }
        public string TypeName { get; }
        public string SelectedOrientation { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="DetailItemElementSelection"/> class.
        /// </summary>
        public DetailItemElementSelection(
            string elementUniqueId,
            string elementId,
            string displayName,
            string familyName,
            string typeName,
            string selectedOrientation)
        {
            if (string.IsNullOrWhiteSpace(elementUniqueId))
            {
                throw new ArgumentException("Element UniqueId cannot be blank.", nameof(elementUniqueId));
            }

            ElementUniqueId = elementUniqueId;
            ElementId = string.IsNullOrWhiteSpace(elementId) ? elementUniqueId : elementId;
            DisplayName = displayName ?? string.Empty;
            FamilyName = familyName ?? string.Empty;
            TypeName = typeName ?? string.Empty;
            SelectedOrientation = string.IsNullOrWhiteSpace(selectedOrientation)
                ? DetailItemOrientations.Plan
                : selectedOrientation;
        }
    }

    /// <summary>
    /// Immutable planned work item produced by <see cref="Services.DetailItemBatchPlanner"/>.
    /// </summary>
    public sealed class DetailItemPlannedElement
    {
        public int SequenceIndex { get; }
        public string ElementUniqueId { get; }
        public string ElementId { get; }
        public string DisplayName { get; }
        public string CleanFamilyName { get; }
        public string CleanTypeName { get; }
        public string Orientation { get; }
        public string TargetFileName { get; }
        public string TargetPath { get; }
        public DetailItemPlanDisposition Disposition { get; }
        public string? SkipReason { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="DetailItemPlannedElement"/> class.
        /// </summary>
        public DetailItemPlannedElement(
            int sequenceIndex,
            string elementUniqueId,
            string elementId,
            string displayName,
            string cleanFamilyName,
            string cleanTypeName,
            string orientation,
            string targetFileName,
            string targetPath,
            DetailItemPlanDisposition disposition,
            string? skipReason = null)
        {
            SequenceIndex = sequenceIndex;
            ElementUniqueId = elementUniqueId ?? string.Empty;
            ElementId = elementId ?? string.Empty;
            DisplayName = displayName ?? string.Empty;
            CleanFamilyName = cleanFamilyName ?? string.Empty;
            CleanTypeName = cleanTypeName ?? string.Empty;
            Orientation = orientation ?? string.Empty;
            TargetFileName = targetFileName ?? string.Empty;
            TargetPath = targetPath ?? string.Empty;
            Disposition = disposition;
            SkipReason = skipReason;
        }
    }

    /// <summary>
    /// Immutable batch execution plan consumed by <see cref="Adapters.IDetailItemFactoryAdapter.ExecuteBatch"/>.
    /// </summary>
    public sealed class DetailItemBatchPlan
    {
        public string ActiveViewUniqueId { get; }
        public string OutputFolder { get; }
        public string TargetSubcategory { get; }
        public bool OverwriteExisting { get; }
        public bool CleanupTempFiles { get; }
        public IReadOnlyList<DetailItemPlannedElement> Items { get; }
        public int ExecutableCount { get; }
        public int SkippedCount { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="DetailItemBatchPlan"/> class.
        /// </summary>
        public DetailItemBatchPlan(
            string activeViewUniqueId,
            string outputFolder,
            string targetSubcategory,
            bool overwriteExisting,
            IEnumerable<DetailItemPlannedElement>? items,
            bool cleanupTempFiles = true)
        {
            ActiveViewUniqueId = activeViewUniqueId ?? string.Empty;
            OutputFolder = outputFolder ?? string.Empty;
            TargetSubcategory = string.IsNullOrWhiteSpace(targetSubcategory)
                ? DetailItemFactorySettings.DefaultTargetSubcategory
                : targetSubcategory.Trim();
            OverwriteExisting = overwriteExisting;
            CleanupTempFiles = cleanupTempFiles;

            var itemList = items != null
                ? items.Where(item => item != null).ToList()
                : new List<DetailItemPlannedElement>();
            Items = new ReadOnlyCollection<DetailItemPlannedElement>(itemList);
            ExecutableCount = itemList.Count(item => item.Disposition == DetailItemPlanDisposition.Execute);
            SkippedCount = itemList.Count - ExecutableCount;
        }
    }
}
