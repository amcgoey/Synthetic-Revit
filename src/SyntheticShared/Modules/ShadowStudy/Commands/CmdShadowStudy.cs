using System;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Synthetic.Modules.ShadowStudy.Adapters;
using Synthetic.Modules.ShadowStudy.Models;
using Synthetic.Modules.ShadowStudy.Services;
using Synthetic.Modules.ShadowStudy.ViewModels;
using Synthetic.Modules.ShadowStudy.Views;
using Synthetic.Shared.UI;

namespace Synthetic.Modules.ShadowStudy.Commands
{
    /// <summary>
    /// External command launching the Shadow Study composite exporter from the Publish ribbon panel.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class CmdShadowStudy : IExternalCommand
    {
        /// <summary>
        /// Executes the Shadow Study command.
        /// </summary>
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
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

                var adapter = new RevitShadowStudyAdapter(doc, uidoc, uiapp);
                ShadowStudyViewItem? activeView = adapter.GetActiveViewInfo();
                if (activeView == null && adapter.GetEligibleViews().Count == 0)
                {
                    Autodesk.Revit.UI.TaskDialog.Show(
                        "Shadow Study",
                        "No shadow-capable graphical views were found in this project.\n\nCreate a graphical view (Floor Plan, Ceiling Plan, Area Plan, Structural Plan, 3D View, Section, Elevation, or Detail View) and try again.");
                    return Result.Succeeded;
                }

                var folderService = new WindowsFolderDialogService(uiapp.MainWindowHandle);
                ShadowStudyUserPrefsStore? userPrefsStore = ProgressCoordinator.SuppressUI
                    ? null
                    : new ShadowStudyUserPrefsStore();
                ITextInputPromptService? textInputService = ProgressCoordinator.SuppressUI
                    ? null
                    : new WindowsTextInputPromptService(uiapp.MainWindowHandle);
                var vm = new ShadowStudyViewModel(
                    adapter,
                    folderService,
                    userPrefsStore: userPrefsStore,
                    textInputPromptService: textInputService);
                var window = new ShadowStudyWindow(uiapp.MainWindowHandle)
                {
                    DataContext = vm
                };

                bool? dialogResult = window.ShowDialog();
                if (dialogResult != true)
                {
                    return Result.Cancelled;
                }

                ShadowStudyRunRequest request = vm.BuildRunRequest();
                int totalRawFrames = request.DateSchedules.Sum(s => s.Frames.Count) * request.Views.Count;
                int totalComposites = request.DateSchedules.Count(s => s.Frames.Count > 0) * request.Views.Count;
                int totalSteps = Math.Max(1, totalRawFrames + totalComposites);

                ProgressCoordinator.Initialize("Shadow Study", "Exporting Shadow Frames...", totalSteps);

                ShadowStudyRunSummary? summary = null;
                bool wasCancelled = false;

                try
                {
                    IProgress<ProgressState> progress = ProgressCoordinator.AsSynchronousProgressReporter();
                    var pipeline = new ShadowStudyPipeline(adapter);
                    summary = pipeline.Execute(request, progress, ProgressCoordinator.Token);
                    wasCancelled = summary.WasCancelled || ProgressCoordinator.IsCancelled();
                }
                finally
                {
                    if (!wasCancelled)
                    {
                        wasCancelled = ProgressCoordinator.IsCancelled();
                    }

                    ProgressCoordinator.Close();
                }

                if (wasCancelled)
                {
                    return Result.Cancelled;
                }

                if (summary != null)
                {
                    var completionVm = ExportCompletionViewModel.ForBatch(
                        outputFolder: summary.OutputFolder,
                        generatedCount: summary.GeneratedCount,
                        primaryFilePath: summary.PrimaryCompositePath,
                        outputNounPlural: "composites",
                        primaryActionLabel: "Open Composite",
                        warningNotices: summary.Warnings,
                        windowTitle: "Shadow Study Export Complete",
                        headerTitle: summary.GeneratedCount > 0 ? "Shadow Study Export Complete" : "Export Completed with Warnings");

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
