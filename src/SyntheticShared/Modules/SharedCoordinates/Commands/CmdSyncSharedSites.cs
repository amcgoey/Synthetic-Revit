using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Synthetic.Modules.SharedCoordinates.Adapters;
using Synthetic.Modules.SharedCoordinates.ViewModels;
using Synthetic.Modules.SharedCoordinates.Views;

namespace Synthetic.Modules.SharedCoordinates.Commands
{
    /// <summary>
    /// External command launching the Sync Shared Sites to Levels dialog.
    /// Operates inside the active unit model to synchronize Level-Based Shared Sites from a linked host model.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class CmdSyncSharedSites : IExternalCommand
    {
        /// <summary>
        /// Executes the Sync Shared Sites to Levels command.
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

                var adapter = new RevitSharedCoordinatesAdapter(doc, uidoc);
                var loadedLinks = adapter.GetLoadedLinkedModels();

                if (loadedLinks.Count == 0)
                {
                    TaskDialog.Show(
                        "Sync Shared Sites to Levels",
                        "No loaded linked models found in the active project.\nPlease link a host model to synchronize shared sites to levels.");
                    return Result.Succeeded;
                }

                var vm = new SyncSharedSitesViewModel(adapter);
                var window = new SyncSharedSitesWindow(uiapp.MainWindowHandle)
                {
                    DataContext = vm
                };

                bool? dialogResult = window.ShowDialog();
                if (dialogResult == true && vm.LastSummary != null)
                {
                    var summaryWindow = new SharedCoordinatesSummaryWindow(uiapp.MainWindowHandle, vm.LastSummary);
                    summaryWindow.ShowDialog();
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
