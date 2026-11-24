using System;
using System.Collections.Generic;
using Synthetic.Features.SheetIndex.Adapters;
using Synthetic.Features.SheetIndex.Models;

namespace SyntheticTests.Fixtures
{
    /// <summary>
    /// Headless in-memory test double implementing <see cref="ISheetIndexAdapter"/>
    /// without invoking Revit API binaries.
    /// </summary>
    public class FakeSheetIndexAdapter : ISheetIndexAdapter
    {
        public List<SheetIndexSheetModel> Sheets { get; } = new List<SheetIndexSheetModel>();
        public List<SheetIndexRevisionModel> Revisions { get; } = new List<SheetIndexRevisionModel>();
        public List<SheetIndexPrintSetModel> PrintSets { get; } = new List<SheetIndexPrintSetModel>();
        public List<SheetIndexScheduleModel> Schedules { get; } = new List<SheetIndexScheduleModel>();

        public int LoadCallCount { get; private set; }
        public Exception? ExceptionToThrow { get; set; }

        public FakeSheetIndexAdapter()
        {
        }

        public FakeSheetIndexAdapter(
            IEnumerable<SheetIndexSheetModel>? sheets = null,
            IEnumerable<SheetIndexRevisionModel>? revisions = null,
            IEnumerable<SheetIndexPrintSetModel>? printSets = null,
            IEnumerable<SheetIndexScheduleModel>? schedules = null)
        {
            if (sheets != null) Sheets.AddRange(sheets);
            if (revisions != null) Revisions.AddRange(revisions);
            if (printSets != null) PrintSets.AddRange(printSets);
            if (schedules != null) Schedules.AddRange(schedules);
        }

        public SheetIndexSnapshot LoadProjectSnapshot()
        {
            LoadCallCount++;
            if (ExceptionToThrow != null)
            {
                throw ExceptionToThrow;
            }

            return new SheetIndexSnapshot(Sheets, Revisions, PrintSets, Schedules);
        }
    }
}
