using System;
using System.Collections.Generic;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Synthetic.Features.DetailItemFactory.Adapters;
using Synthetic.Features.DetailItemFactory.Models;
using Synthetic.Features.DetailItemFactory.ViewModels;
using Synthetic.Features.DetailItemFactory.Views;
using Synthetic.Platform.UI;
using TaskDialog = Autodesk.Revit.UI.TaskDialog;
using View = Autodesk.Revit.DB.View;

namespace Synthetic.Features.DetailItemFactory.Commands
{
    /// <summary>
    /// Synchronous external command to batch-process selected 3D model elements (or Legend Components)
    /// into 2D Detail Item family documents via temporary DWG projection and tracing.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class CmdDetailItemFactory : IExternalCommand
    {
        /// <summary>
        /// Executes the Detail Item Factory command synchronously on Revit's main thread.
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

                if (doc == null || uidoc == null)
                {
                    message = "No active document found.";
                    return Result.Failed;
                }

                if (doc.IsFamilyDocument)
                {
                    TaskDialog.Show(
                        "Detail Item Factory",
                        "Detail Item Factory cannot be run inside a family document. Please run this command from a project document.");
                    return Result.Failed;
                }

                View? activeView = doc.ActiveView;
                if (!RevitDetailItemFactoryAdapter.IsSupported2DView(activeView))
                {
                    TaskDialog.Show(
                        "Detail Item Factory",
                        "Please run this command from a non-template 2D view (Plan, Section, Elevation, Detail, or Legend).");
                    return Result.Failed;
                }

                ICollection<ElementId> selectedIds = uidoc.Selection.GetElementIds();
                if (selectedIds == null || selectedIds.Count == 0)
                {
                    TaskDialog.Show(
                        "Detail Item Factory",
                        "Please select at least one 3D model element (or Legend Component in a Legend view) to convert.");
                    return Result.Failed;
                }

                var adapter = new RevitDetailItemFactoryAdapter(doc, uidoc, uiapp);
                DetailItemSessionSnapshot snapshot = adapter.CaptureSessionSnapshot();
                if (snapshot.Elements.Count == 0)
                {
                    TaskDialog.Show(
                        "Detail Item Factory",
                        "None of the selected elements are convertible 3D model elements (or Legend Components in a Legend view).");
                    return Result.Failed;
                }

                var folderDialogService = new WindowsFolderDialogService(uiapp.MainWindowHandle);
                var vm = new DetailItemFactoryViewModel(snapshot, folderDialogService);
                var view = new DetailItemFactoryView(uiapp.MainWindowHandle) { DataContext = vm };

                if (view.ShowDialog() != true)
                {
                    return Result.Cancelled;
                }

                DetailItemBatchPlan plan = vm.BuildBatchPlan();

                ProgressCoordinator.Initialize(
                    "Detail Item Factory Progress",
                    "Starting batch processing...",
                    Math.Max(1, plan.Items.Count));

                DetailItemBatchOutcome outcome;
                try
                {
                    IProgress<ProgressState> progress = ProgressCoordinator.AsSynchronousProgressReporter();
                    outcome = adapter.ExecuteBatch(plan, progress, ProgressCoordinator.Token);
                }
                finally
                {
                    ProgressCoordinator.Close();
                }

                if (!outcome.Succeeded)
                {
                    TaskDialog.Show(
                        "Detail Item Factory",
                        outcome.ErrorMessage ?? "Detail Item Factory batch execution failed.");
                    return Result.Failed;
                }

                IUserPromptService? promptService = ProgressCoordinator.SuppressUI
                    ? null
                    : new WindowsUserPromptService();
                var resultsVm = new DetailItemFactoryResultsViewModel(
                    outcome.OutputFolder,
                    outcome.Results,
                    promptService);
                var resultsView = new DetailItemFactoryResultsView(uiapp.MainWindowHandle)
                {
                    DataContext = resultsVm
                };
                resultsView.ShowDialog();

                return outcome.WasCancelled && outcome.CommittedCount == 0
                    ? Result.Cancelled
                    : Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message + "\n" + ex.StackTrace;
                return Result.Failed;
            }
        }
    }
}
