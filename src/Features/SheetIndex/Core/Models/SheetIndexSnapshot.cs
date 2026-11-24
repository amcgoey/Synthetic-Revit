using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Synthetic.Features.SheetIndex.Models
{
    /// <summary>
    /// Immutable Core snapshot of harvested project sheets, revisions, Print Sets, and non-empty sheet schedules
    /// with deterministically derived lookup maps.
    /// </summary>
    public sealed class SheetIndexSnapshot
    {
        /// <summary>
        /// Gets all harvested non-placeholder project sheets.
        /// </summary>
        public IReadOnlyList<SheetIndexSheetModel> Sheets { get; }

        /// <summary>
        /// Gets all harvested project revisions.
        /// </summary>
        public IReadOnlyList<SheetIndexRevisionModel> Revisions { get; }

        /// <summary>
        /// Gets all harvested Print Sets deduplicated safely by name.
        /// </summary>
        public IReadOnlyList<SheetIndexPrintSetModel> PrintSets { get; }

        /// <summary>
        /// Gets all harvested non-empty sheet schedules deduplicated safely by name.
        /// </summary>
        public IReadOnlyList<SheetIndexScheduleModel> Schedules { get; }

        /// <summary>
        /// Gets the deterministic ordered list of available Print Set names.
        /// </summary>
        public IReadOnlyList<string> AvailablePrintSets { get; }

        /// <summary>
        /// Gets the deterministic ordered list of available non-empty sheet schedule names.
        /// </summary>
        public IReadOnlyList<string> AvailableSchedules { get; }

        /// <summary>
        /// Gets a case-insensitive lookup mapping Print Set names to their contained sheet UniqueIds.
        /// </summary>
        public IReadOnlyDictionary<string, IReadOnlyList<string>> PrintSetSheetMap { get; }

        /// <summary>
        /// Gets a case-insensitive lookup mapping sheet schedule names to their contained sheet UniqueIds.
        /// </summary>
        public IReadOnlyDictionary<string, IReadOnlyList<string>> ScheduleSheetMap { get; }

        /// <summary>
        /// Initializes an immutable <see cref="SheetIndexSnapshot"/> and derives lookup maps deterministically.
        /// Empty schedules and duplicate Print Set or schedule names are guarded safely.
        /// </summary>
        public SheetIndexSnapshot(
            IEnumerable<SheetIndexSheetModel>? sheets = null,
            IEnumerable<SheetIndexRevisionModel>? revisions = null,
            IEnumerable<SheetIndexPrintSetModel>? printSets = null,
            IEnumerable<SheetIndexScheduleModel>? schedules = null)
        {
            var sheetList = sheets != null
                ? sheets.Where(s => s != null).ToList()
                : new List<SheetIndexSheetModel>();
            Sheets = new ReadOnlyCollection<SheetIndexSheetModel>(sheetList);

            var revisionList = revisions != null
                ? revisions.Where(r => r != null).ToList()
                : new List<SheetIndexRevisionModel>();
            Revisions = new ReadOnlyCollection<SheetIndexRevisionModel>(revisionList);

            var normalizedPrintSets = new List<SheetIndexPrintSetModel>();
            var availablePrintSetNames = new List<string>();
            var printSetMap = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);

            if (printSets != null)
            {
                foreach (SheetIndexPrintSetModel? printSet in printSets)
                {
                    if (printSet == null || string.IsNullOrWhiteSpace(printSet.Name))
                    {
                        continue;
                    }

                    if (printSetMap.ContainsKey(printSet.Name))
                    {
                        continue;
                    }

                    var sheetIds = printSet.SheetIds != null
                        ? printSet.SheetIds.Where(id => !string.IsNullOrWhiteSpace(id)).ToList()
                        : new List<string>();

                    normalizedPrintSets.Add(printSet);
                    availablePrintSetNames.Add(printSet.Name);
                    printSetMap[printSet.Name] = new ReadOnlyCollection<string>(sheetIds);
                }
            }

            PrintSets = new ReadOnlyCollection<SheetIndexPrintSetModel>(normalizedPrintSets);
            AvailablePrintSets = new ReadOnlyCollection<string>(availablePrintSetNames);
            PrintSetSheetMap = new ReadOnlyDictionary<string, IReadOnlyList<string>>(printSetMap);

            var normalizedSchedules = new List<SheetIndexScheduleModel>();
            var availableScheduleNames = new List<string>();
            var scheduleMap = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);

            if (schedules != null)
            {
                foreach (SheetIndexScheduleModel? schedule in schedules)
                {
                    if (schedule == null ||
                        string.IsNullOrWhiteSpace(schedule.Name) ||
                        schedule.Sheets == null ||
                        schedule.Sheets.Count == 0)
                    {
                        continue;
                    }

                    if (scheduleMap.ContainsKey(schedule.Name))
                    {
                        continue;
                    }

                    var sheetIds = schedule.Sheets
                        .Where(s => s != null && !string.IsNullOrWhiteSpace(s.UniqueId))
                        .Select(s => s.UniqueId)
                        .ToList();

                    normalizedSchedules.Add(schedule);
                    availableScheduleNames.Add(schedule.Name);
                    scheduleMap[schedule.Name] = new ReadOnlyCollection<string>(sheetIds);
                }
            }

            Schedules = new ReadOnlyCollection<SheetIndexScheduleModel>(normalizedSchedules);
            AvailableSchedules = new ReadOnlyCollection<string>(availableScheduleNames);
            ScheduleSheetMap = new ReadOnlyDictionary<string, IReadOnlyList<string>>(scheduleMap);
        }
    }
}
