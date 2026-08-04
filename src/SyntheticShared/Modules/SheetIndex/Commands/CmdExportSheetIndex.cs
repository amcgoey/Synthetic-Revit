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
                    var revIds = sheet.GetAllRevisionIds()
                        .Select(id => doc.GetElement(id)?.UniqueId ?? id.ToString())
                        .Where(id => !string.IsNullOrEmpty(id))
                        .ToList();

                    sheetModels.Add(new SheetIndexSheetModel(
                        sheet.UniqueId,
                        sheet.SheetNumber,
                        sheet.Name,
                        revIds
                    ));
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
                var printSets = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewSheetSet))
                    .Cast<ViewSheetSet>()
                    .Select(vss => vss.Name)
                    .Where(n => !string.IsNullOrEmpty(n))
                    .ToList();

                var sheetSchedules = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewSchedule))
                    .Cast<ViewSchedule>()
                    .Where(vs => !vs.IsTemplate && vs.Definition.CategoryId == new ElementId(BuiltInCategory.OST_Sheets))
                    .Select(vs => vs.Name)
                    .Where(n => !string.IsNullOrEmpty(n))
                    .ToList();

                // 3. Launch WPF Selection UI
                var viewModel = new ExportSheetIndexViewModel(sheetModels, revisionModels, printSets, sheetSchedules);
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

                // 4. Prompt for Save File Path using Revit FileSaveDialog
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

                // 5. Build 2D Sheet Index Matrix
                var matrixBuilder = new SheetIndexMatrixBuilder();
                var matrix = matrixBuilder.BuildMatrix(selectedSheets, selectedRevisions);

                // 6. Export to Excel
                var exporterService = new SheetIndexExporterService();
                exporterService.ExportToExcel(matrix, savePath);

                // 7. Show Post-Export Completion Dialog
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
    }
}
