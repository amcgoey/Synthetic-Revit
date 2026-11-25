using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Synthetic.Features.MultiModelPdfExport.Adapters;
using Synthetic.Features.MultiModelPdfExport.Models;
using Synthetic.Features.MultiModelPdfExport.Services;
using Synthetic.Features.MultiModelPdfExport.ViewModels;
using Synthetic.Features.MultiModelPdfExport.Views;
using Synthetic.Platform.UI;

namespace Synthetic.Features.MultiModelPdfExport.Commands
{
    /// <summary>
    /// External command launching the native Multi-Model PDF Export workflow
    /// from the Publish ribbon panel.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class CmdMultiModelPdfExport : IExternalCommand
    {
        /// <summary>
        /// Executes the Multi-Model PDF Export command.
        /// </summary>
        public Result Execute(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
        {
            try
            {
                UIApplication uiapp = commandData.Application;
                UIDocument? uidoc = uiapp.ActiveUIDocument;
                Document? doc = uidoc?.Document;

                if (doc == null)
                {
                    message = "No active document found.";
                    return Result.Failed;
                }

                if (doc.IsFamilyDocument)
                {
                    Autodesk.Revit.UI.TaskDialog.Show(
                        "Multi-Model PDF Export",
                        "Multi-Model PDF Export supports Revit project documents (.rvt) only. Family documents (.rfa) cannot be exported.");
                    return Result.Cancelled;
                }

                var adapter = new RevitMultiModelPdfExportAdapter(doc, uiapp);
                var folderService = new WindowsFolderDialogService(uiapp.MainWindowHandle);
                var vm = new MultiModelPdfExportViewModel(adapter, folderService);
                var window = new MultiModelPdfExportWindow(uiapp.MainWindowHandle)
                {
                    DataContext = vm
                };

                bool? dialogResult = window.ShowDialog();
                if (dialogResult != true)
                {
                    return Result.Cancelled;
                }

                MultiModelPdfRunRequest request = vm.BuildRunRequest();
                ProgressCoordinator.Initialize(
                    "Multi-Model PDF Export",
                    "Exporting PDFs...",
                    Math.Max(1, request.Jobs.Count));

                MultiModelPdfRunSummary? summary = null;
                bool cancelledWithZeroCompleted = false;

                try
                {
                    IProgress<ProgressState> progress = ProgressCoordinator.AsSynchronousProgressReporter();
                    var pipeline = new MultiModelPdfExportPipeline(adapter);
                    summary = pipeline.Execute(request, progress, ProgressCoordinator.Token);
                    bool wasCancelled = summary.WasCancelled || ProgressCoordinator.IsCancelled();
                    cancelledWithZeroCompleted = wasCancelled && summary.CompletedDocumentCount == 0 && summary.GeneratedCount == 0;
                }
                finally
                {
                    ProgressCoordinator.Close();
                }

                if (cancelledWithZeroCompleted)
                {
                    return Result.Cancelled;
                }

                if (summary != null)
                {
                    var completionVm = ExportCompletionViewModel.ForBatch(
                        outputFolder: summary.OutputFolder,
                        generatedCount: summary.GeneratedCount,
                        primaryFilePath: summary.PrimaryFilePath,
                        outputNounPlural: "PDFs",
                        primaryActionLabel: "Open First PDF",
                        warningNotices: summary.CompletionNotices,
                        windowTitle: "Multi-Model PDF Export Complete",
                        headerTitle: summary.GeneratedCount > 0 ? "Multi-Model PDF Export Complete" : "Export Completed with Warnings");

                    var completionWindow = new ExportCompletionWindow(completionVm, uiapp.MainWindowHandle);
                    completionWindow.ShowDialog();
                }

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
