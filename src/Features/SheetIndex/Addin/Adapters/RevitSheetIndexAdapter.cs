using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using Synthetic.Features.SheetIndex.Models;
using Synthetic.Features.SheetIndex.Services;

namespace Synthetic.Features.SheetIndex.Adapters
{
    /// <summary>
    /// Revit database adapter implementing <see cref="ISheetIndexAdapter"/> to harvest sheets,
    /// revisions, Print Sets, and sheet schedules into an immutable <see cref="SheetIndexSnapshot"/>.
    /// </summary>
    public class RevitSheetIndexAdapter : ISheetIndexAdapter
    {
        private static readonly AlphanumericComparer SheetComparer = new AlphanumericComparer();

        private readonly Document _doc;

        /// <summary>
        /// Initializes a new instance of the <see cref="RevitSheetIndexAdapter"/> class.
        /// </summary>
        /// <param name="doc">The active Revit document to query.</param>
        public RevitSheetIndexAdapter(Document doc)
        {
            _doc = doc ?? throw new ArgumentNullException(nameof(doc));
        }

        /// <inheritdoc />
        public SheetIndexSnapshot LoadProjectSnapshot()
        {
            List<SheetIndexSheetModel> sheetModels = HarvestSheets();
            List<SheetIndexRevisionModel> revisionModels = HarvestRevisions();
            List<SheetIndexPrintSetModel> printSetModels = HarvestPrintSets();
            List<SheetIndexScheduleModel> scheduleModels = HarvestSchedules();

            return new SheetIndexSnapshot(sheetModels, revisionModels, printSetModels, scheduleModels);
        }

        private List<SheetIndexSheetModel> HarvestSheets()
        {
            List<ViewSheet> sheetElements = new FilteredElementCollector(_doc)
                .OfClass(typeof(ViewSheet))
                .WhereElementIsNotElementType()
                .Cast<ViewSheet>()
                .Where(s => !s.IsPlaceholder)
                .OrderBy(s => s.SheetNumber ?? string.Empty, SheetComparer)
                .ThenBy(s => s.UniqueId, StringComparer.Ordinal)
                .ToList();

            var sheetModels = new List<SheetIndexSheetModel>(sheetElements.Count);
            foreach (ViewSheet sheet in sheetElements)
            {
                sheetModels.Add(CreateSheetModel(sheet, _doc, null));
            }

            return sheetModels;
        }

        private List<SheetIndexRevisionModel> HarvestRevisions()
        {
            List<Revision> revisionElements = new FilteredElementCollector(_doc)
                .OfCategory(BuiltInCategory.OST_Revisions)
                .OfClass(typeof(Revision))
                .WhereElementIsNotElementType()
                .Cast<Revision>()
                .OrderBy(r => r.SequenceNumber)
                .ThenBy(r => r.UniqueId, StringComparer.Ordinal)
                .ToList();

            var revisionModels = new List<SheetIndexRevisionModel>(revisionElements.Count);
            foreach (Revision rev in revisionElements)
            {
                string name = !string.IsNullOrWhiteSpace(rev.Description)
                    ? rev.Description
                    : $"Revision {rev.SequenceNumber}";

                string date = rev.RevisionDate ?? string.Empty;

                revisionModels.Add(new SheetIndexRevisionModel(
                    rev.UniqueId,
                    name,
                    date,
                    rev.SequenceNumber));
            }

            return revisionModels;
        }

        private List<SheetIndexPrintSetModel> HarvestPrintSets()
        {
            List<ViewSheetSet> printSetElements = new FilteredElementCollector(_doc)
                .OfClass(typeof(ViewSheetSet))
                .WhereElementIsNotElementType()
                .Cast<ViewSheetSet>()
                .OrderBy(vs => vs.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ThenBy(vs => vs.UniqueId, StringComparer.Ordinal)
                .ToList();

            var printSetModels = new List<SheetIndexPrintSetModel>();
            var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (ViewSheetSet printSet in printSetElements)
            {
                if (string.IsNullOrWhiteSpace(printSet.Name) || !seenNames.Add(printSet.Name))
                {
                    continue;
                }

                var sheetsInSet = new List<ViewSheet>();
                foreach (Autodesk.Revit.DB.View v in printSet.Views)
                {
                    if (v is ViewSheet sheet && !sheet.IsPlaceholder)
                    {
                        sheetsInSet.Add(sheet);
                    }
                }

                List<string> sheetIdsInSet = sheetsInSet
                    .OrderBy(s => s.SheetNumber ?? string.Empty, SheetComparer)
                    .ThenBy(s => s.UniqueId, StringComparer.Ordinal)
                    .Select(s => s.UniqueId)
                    .ToList();

                printSetModels.Add(new SheetIndexPrintSetModel(
                    printSet.UniqueId,
                    printSet.Name,
                    sheetIdsInSet));
            }

            return printSetModels;
        }

        private List<SheetIndexScheduleModel> HarvestSchedules()
        {
            ElementId sheetCategoryId = new ElementId(BuiltInCategory.OST_Sheets);
            List<ViewSchedule> scheduleElements = new FilteredElementCollector(_doc)
                .OfClass(typeof(ViewSchedule))
                .WhereElementIsNotElementType()
                .Cast<ViewSchedule>()
                .Where(s => !s.IsTemplate && s.Definition != null && s.Definition.CategoryId == sheetCategoryId)
                .OrderBy(s => s.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ThenBy(s => s.UniqueId, StringComparer.Ordinal)
                .ToList();

            var scheduleModels = new List<SheetIndexScheduleModel>();
            var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (ViewSchedule schedule in scheduleElements)
            {
                if (string.IsNullOrWhiteSpace(schedule.Name))
                {
                    continue;
                }

                List<ViewSheet> scheduledSheets = new FilteredElementCollector(_doc, schedule.Id)
                    .OfClass(typeof(ViewSheet))
                    .WhereElementIsNotElementType()
                    .Cast<ViewSheet>()
                    .Where(s => !s.IsPlaceholder)
                    .ToList();

                if (scheduledSheets.Count == 0)
                {
                    continue;
                }

                if (!seenNames.Add(schedule.Name))
                {
                    continue;
                }

                IList<ScheduleSortGroupField> sortGroupFields = schedule.Definition.GetSortGroupFields();
                ScheduleField? groupField = null;
                foreach (ScheduleSortGroupField field in sortGroupFields)
                {
                    if (field.ShowHeader && groupField == null)
                    {
                        groupField = schedule.Definition.GetField(field.FieldId);
                    }
                }

                if (sortGroupFields.Count > 0)
                {
                    IOrderedEnumerable<ViewSheet>? orderedSheets = null;

                    foreach (ScheduleSortGroupField field in sortGroupFields)
                    {
                        ScheduleField? schedField = schedule.Definition.GetField(field.FieldId);
                        if (schedField == null)
                        {
                            continue;
                        }

                        ScheduleField capturedField = schedField;
                        bool isAscending = field.SortOrder == ScheduleSortOrder.Ascending;

                        if (orderedSheets == null)
                        {
                            orderedSheets = isAscending
                                ? scheduledSheets.OrderBy(s => GetScheduleFieldValue(s, capturedField, _doc), SheetComparer)
                                : scheduledSheets.OrderByDescending(s => GetScheduleFieldValue(s, capturedField, _doc), SheetComparer);
                        }
                        else
                        {
                            orderedSheets = isAscending
                                ? orderedSheets.ThenBy(s => GetScheduleFieldValue(s, capturedField, _doc), SheetComparer)
                                : orderedSheets.ThenByDescending(s => GetScheduleFieldValue(s, capturedField, _doc), SheetComparer);
                        }
                    }

                    if (orderedSheets != null)
                    {
                        scheduledSheets = orderedSheets
                            .ThenBy(s => s.SheetNumber ?? string.Empty, SheetComparer)
                            .ThenBy(s => s.UniqueId, StringComparer.Ordinal)
                            .ToList();
                    }
                }

                var schedSheetModels = new List<SheetIndexSheetModel>(scheduledSheets.Count);
                foreach (ViewSheet sheet in scheduledSheets)
                {
                    string? groupValue = null;
                    if (groupField != null)
                    {
                        string rawGroup = GetScheduleFieldValue(sheet, groupField, _doc);
                        if (!string.IsNullOrWhiteSpace(rawGroup))
                        {
                            groupValue = rawGroup;
                        }
                    }

                    schedSheetModels.Add(CreateSheetModel(sheet, _doc, groupValue));
                }

                scheduleModels.Add(new SheetIndexScheduleModel(schedule.UniqueId, schedule.Name, schedSheetModels));
            }

            return scheduleModels;
        }

        private static string GetScheduleFieldValue(ViewSheet sheet, ScheduleField schedField, Document doc)
        {
            Parameter? param = ResolveSheetParameter(sheet, schedField, doc);
            return GetParameterDisplayValue(param, doc);
        }

        private static Parameter? ResolveSheetParameter(ViewSheet sheet, ScheduleField schedField, Document doc)
        {
            ElementId paramId = schedField.ParameterId;
            if (paramId != null && paramId != ElementId.InvalidElementId)
            {
#if REVIT2023
                long rawId = paramId.IntegerValue;
#else
                long rawId = paramId.Value;
#endif
                if (rawId < 0)
                {
                    BuiltInParameter bip = (BuiltInParameter)(int)rawId;
                    Parameter? bipParam = sheet.get_Parameter(bip);
                    if (bipParam != null)
                    {
                        return bipParam;
                    }
                }
                else
                {
                    Element? paramElement = doc.GetElement(paramId);
                    if (paramElement is ParameterElement pe)
                    {
                        Definition? def = pe.GetDefinition();
                        if (def != null)
                        {
                            Parameter? defParam = sheet.get_Parameter(def);
                            if (defParam != null)
                            {
                                return defParam;
                            }
                        }
                    }
                }
            }

            string? fieldName = schedField.GetName();
            if (!string.IsNullOrEmpty(fieldName))
            {
                return sheet.LookupParameter(fieldName);
            }

            return null;
        }

        private static string GetParameterDisplayValue(Parameter? param, Document doc)
        {
            if (param == null || !param.HasValue)
            {
                return string.Empty;
            }

            switch (param.StorageType)
            {
                case StorageType.String:
                    return param.AsString() ?? string.Empty;

                case StorageType.Integer:
                {
                    string? valueString = param.AsValueString();
                    return !string.IsNullOrEmpty(valueString)
                        ? valueString!
                        : param.AsInteger().ToString(CultureInfo.InvariantCulture);
                }

                case StorageType.Double:
                {
                    string? valueString = param.AsValueString();
                    return !string.IsNullOrEmpty(valueString)
                        ? valueString!
                        : param.AsDouble().ToString(CultureInfo.InvariantCulture);
                }

                case StorageType.ElementId:
                {
                    ElementId elemId = param.AsElementId();
                    if (elemId == null || elemId == ElementId.InvalidElementId)
                    {
                        return string.Empty;
                    }

                    string? valueString = param.AsValueString();
                    if (!string.IsNullOrEmpty(valueString))
                    {
                        return valueString!;
                    }

                    Element? target = doc.GetElement(elemId);
                    return target != null && !string.IsNullOrEmpty(target.Name)
                        ? target.Name
                        : string.Empty;
                }

                default:
                    return param.AsValueString() ?? param.AsString() ?? string.Empty;
            }
        }

        private static SheetIndexSheetModel CreateSheetModel(ViewSheet sheet, Document doc, string? sectionGroup)
        {
            List<string> revIds = sheet.GetAllRevisionIds()
                .Select(id => doc.GetElement(id)?.UniqueId ?? id.ToString())
                .Where(id => !string.IsNullOrEmpty(id))
                .ToList();

            return new SheetIndexSheetModel(
                sheet.UniqueId,
                sheet.SheetNumber ?? string.Empty,
                sheet.Name ?? string.Empty,
                revIds)
            {
                SectionGroup = sectionGroup
            };
        }
    }
}
