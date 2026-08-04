using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

using Synthetic.Modules.SheetIndex.Models;
using Synthetic.Modules.SheetIndex.Services;
using Synthetic.Modules.SheetIndex.ViewModels;
using Synthetic.Modules.SheetIndex.Views;

namespace Synthetic.Modules.SheetIndex.Commands
{
    /// <summary>
    /// Revit command to export a Dot Sheet Index matrix to Excel.
    /// Registered under the Publish panel on the Synthetic ribbon.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class CmdExportSheetIndex : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                UIApplication uiapp = commandData.Application;
                Document? doc = uiapp.ActiveUIDocument?.Document;
                if (doc == null)
                {
                    message = "No active document found.";
                    return Result.Failed;
                }

                // 1. Query sheets from Revit document
                var sheetElements = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewSheet))
                    .Cast<ViewSheet>()
                    .Where(s => !s.IsPlaceholder)
                    .ToList();

                if (sheetElements.Count == 0)
                {
                    Autodesk.Revit.UI.TaskDialog.Show("Export Dot Sheet Index", "No valid sheets found in the current Revit document.");
                    return Result.Succeeded;
                }

                var sheetModels = new List<SheetIndexSheetModel>();
                foreach (var sheet in sheetElements)
                {
                    sheetModels.Add(CreateSheetModel(sheet, doc, null));
                }

                // 2. Query revisions from Revit document
                var revisionElements = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_Revisions)
                    .OfClass(typeof(Revision))
                    .Cast<Revision>()
                    .ToList();

                var revisionModels = new List<SheetIndexRevisionModel>();
                foreach (var rev in revisionElements)
                {
                    string name = !string.IsNullOrWhiteSpace(rev.Description)
                        ? rev.Description
                        : $"Revision {rev.SequenceNumber}";

                    string date = rev.RevisionDate ?? string.Empty;

                    revisionModels.Add(new SheetIndexRevisionModel(
                        rev.UniqueId,
                        name,
                        date,
                        rev.SequenceNumber
                    ));
                }

                // 2b. Query ViewSheetSets (Print Sets) and ViewSchedules (Sheet Schedules)
                var printSetElements = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewSheetSet))
                    .WhereElementIsNotElementType()
                    .Cast<ViewSheetSet>()
                    .ToList();

                var printSetSheetMap = new Dictionary<string, List<string>>();
                foreach (var vss in printSetElements)
                {
                    if (!string.IsNullOrEmpty(vss.Name))
                    {
                        var ids = vss.Views.Cast<Autodesk.Revit.DB.View>().OfType<ViewSheet>().Select(s => s.UniqueId).ToList();
                        printSetSheetMap[vss.Name] = ids;
                    }
                }

                // 3. Query ViewSchedule sheet schedules from Revit document
                var scheduleElements = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewSchedule))
                    .WhereElementIsNotElementType()
                    .Cast<ViewSchedule>()
                    .Where(s => !s.IsTemplate && s.Definition != null && s.Definition.CategoryId == new ElementId(BuiltInCategory.OST_Sheets))
                    .ToList();

                var scheduleSheetMap = new Dictionary<string, List<string>>();
                var scheduleModels = new List<SheetIndexScheduleModel>();

                foreach (var schedule in scheduleElements)
                {
                    if (string.IsNullOrEmpty(schedule.Name)) continue;

                    var scheduledSheets = new FilteredElementCollector(doc, schedule.Id)
                        .OfClass(typeof(ViewSheet))
                        .Cast<ViewSheet>()
                        .Where(s => !s.IsPlaceholder)
                        .ToList();

                    var ids = scheduledSheets.Select(s => s.UniqueId).ToList();
                    scheduleSheetMap[schedule.Name] = ids;

                    if (scheduledSheets.Count == 0) continue;

                    // Inspect schedule definition for section header grouping fields and sort order
                    string? groupParamName = null;
                    var sortGroupFields = schedule.Definition.GetSortGroupFields();
                    foreach (var field in sortGroupFields)
                    {
                        if (field.ShowHeader && groupParamName == null)
                        {
                            var schedField = schedule.Definition.GetField(field.FieldId);
                            groupParamName = schedField?.GetName();
                        }
                    }

                    // Sort scheduled sheets according to all schedule sort/group fields if available
                    if (sortGroupFields.Count > 0)
                    {
                        var comparer = new AlphanumericComparer();
                        IOrderedEnumerable<ViewSheet>? orderedSheets = null;

                        foreach (var field in sortGroupFields)
                        {
                            var schedField = schedule.Definition.GetField(field.FieldId);
                            string? paramName = schedField?.GetName();
                            if (string.IsNullOrEmpty(paramName)) continue;

                            bool isAscending = field.SortOrder == ScheduleSortOrder.Ascending;

                            if (orderedSheets == null)
                            {
                                orderedSheets = isAscending
                                    ? scheduledSheets.OrderBy(s => s.LookupParameter(paramName)?.AsString() ?? string.Empty, comparer)
                                    : scheduledSheets.OrderByDescending(s => s.LookupParameter(paramName)?.AsString() ?? string.Empty, comparer);
                            }
                            else
                            {
                                orderedSheets = isAscending
                                    ? orderedSheets.ThenBy(s => s.LookupParameter(paramName)?.AsString() ?? string.Empty, comparer)
                                    : orderedSheets.ThenByDescending(s => s.LookupParameter(paramName)?.AsString() ?? string.Empty, comparer);
                            }
                        }

                        if (orderedSheets != null)
                        {
                            scheduledSheets = orderedSheets.ToList();
                        }
                    }

                    var schedSheetModels = new List<SheetIndexSheetModel>();
                    foreach (var sheet in scheduledSheets)
                    {
                        string? groupValue = null;
                        if (!string.IsNullOrEmpty(groupParamName))
                        {
                            var param = sheet.LookupParameter(groupParamName);
                            if (param != null && param.HasValue)
                            {
                                groupValue = param.AsString();
                            }
                        }

                        schedSheetModels.Add(CreateSheetModel(sheet, doc, groupValue));
                    }

                    scheduleModels.Add(new SheetIndexScheduleModel(schedule.UniqueId, schedule.Name, schedSheetModels));
                }

                // 4. Launch WPF Selection UI
                var viewModel = new ExportSheetIndexViewModel(
                    sheetModels,
                    revisionModels,
                    printSetSheetMap.Keys,
                    scheduleSheetMap.Keys,
                    printSetSheetMap,
                    scheduleSheetMap,
                    scheduleModels);
                var window = new ExportSheetIndexWindow(viewModel, uiapp.MainWindowHandle);

                bool? dialogResult = window.ShowDialog();
                if (dialogResult != true)
                {
                    return Result.Cancelled;
                }

                var selectedSheets = viewModel.GetSelectedSheets();
                var selectedRevisions = viewModel.GetSelectedRevisions();

                if (selectedSheets.Count == 0 || selectedRevisions.Count == 0)
                {
                    Autodesk.Revit.UI.TaskDialog.Show("Export Dot Sheet Index", "Please select at least one sheet and one revision to export.");
                    return Result.Succeeded;
                }

                // 5. Prompt for Save File Path using Revit FileSaveDialog
                string? savePath = null;
                using (var saveFileDialog = new FileSaveDialog("Excel Files (*.xlsx)|*.xlsx"))
                {
                    saveFileDialog.Title = "Save Dot Sheet Index Excel File";
                    saveFileDialog.InitialFileName = "Dot Sheet Index.xlsx";
                    ItemSelectionDialogResult resultSave = saveFileDialog.Show();
                    if (resultSave == ItemSelectionDialogResult.Confirmed)
                    {
                        ModelPath modelPath = saveFileDialog.GetSelectedModelPath();
                        savePath = ModelPathUtils.ConvertModelPathToUserVisiblePath(modelPath);
                    }
                }

                if (string.IsNullOrWhiteSpace(savePath))
                {
                    return Result.Cancelled;
                }

                // 6. Build 2D Sheet Index Matrix
                var matrixBuilder = new SheetIndexMatrixBuilder();
                var matrix = matrixBuilder.BuildMatrix(selectedSheets, selectedRevisions, preserveSheetOrder: viewModel.PreserveSheetOrder);

                // 7. Export to Excel
                var exporterService = new SheetIndexExporterService();
                exporterService.ExportToExcel(matrix, savePath);

                // 8. Show Post-Export Completion Dialog
                var completionWindow = new ExportCompletionWindow(savePath, uiapp.MainWindowHandle);
                completionWindow.ShowDialog();

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message + "\n" + ex.StackTrace;
                return Result.Failed;
            }
        }

        private static SheetIndexSheetModel CreateSheetModel(ViewSheet sheet, Document doc, string? sectionGroup)
        {
            var revIds = sheet.GetAllRevisionIds()
                .Select(id => doc.GetElement(id)?.UniqueId ?? id.ToString())
                .Where(id => !string.IsNullOrEmpty(id))
                .ToList();

            return new SheetIndexSheetModel(
                sheet.UniqueId,
                sheet.SheetNumber,
                sheet.Name,
                revIds
            ) { SectionGroup = sectionGroup };
        }
    }
}
