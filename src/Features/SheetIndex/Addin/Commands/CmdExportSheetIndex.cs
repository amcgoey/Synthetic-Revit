using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

using Synthetic.Features.SheetIndex.Adapters;
using Synthetic.Features.SheetIndex.Models;
using Synthetic.Features.SheetIndex.Services;
using Synthetic.Features.SheetIndex.ViewModels;
using Synthetic.Features.SheetIndex.Views;
using Synthetic.Platform.UI;

namespace Synthetic.Features.SheetIndex.Commands
{
    /// <summary>
    /// Revit command to export a Dot Sheet Index matrix to Excel.
    /// Registered under the Publish panel on the Synthetic ribbon.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class CmdExportSheetIndex : IExternalCommand
    {
        private static readonly SheetIndexSessionState _sessionState = new SheetIndexSessionState();

        /// <summary>
        /// Executes the Dot Sheet Index export command.
        /// </summary>
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

                // 1. Harvest project snapshot via single-query Revit adapter
                ISheetIndexAdapter adapter = new RevitSheetIndexAdapter(doc);
                SheetIndexSnapshot snapshot = adapter.LoadProjectSnapshot();

                if (snapshot.Sheets.Count == 0)
                {
                    Autodesk.Revit.UI.TaskDialog.Show("Export Dot Sheet Index", "No valid sheets found in the current Revit document.");
                    return Result.Succeeded;
                }

                // 2. Launch WPF Selection UI
                var viewModel = new ExportSheetIndexViewModel(snapshot, _sessionState);
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

                // 3. Prompt for Save File Path using Revit FileSaveDialog
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

                // 4. Build 2D Sheet Index Matrix
                var matrixBuilder = new SheetIndexMatrixBuilder();
                var matrix = matrixBuilder.BuildMatrix(selectedSheets, selectedRevisions, preserveSheetOrder: viewModel.PreserveSheetOrder);

                // 5. Export to Excel
                var exporterService = new SheetIndexExporterService();
                exporterService.ExportToExcel(matrix, savePath!);

                // 6. Show Post-Export Completion Dialog
                var completionViewModel = ExportCompletionViewModel.ForSingleFile(
                    savePath!,
                    windowTitle: "Export Complete",
                    headerTitle: "Sheet Index Export Complete");
                var completionWindow = new ExportCompletionWindow(completionViewModel, uiapp.MainWindowHandle);
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
