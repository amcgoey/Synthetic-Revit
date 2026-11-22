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
    /// External command launching the Move Links to Shared Sites dialog.
    /// Operates inside the active host model to reposition linked unit model instances
    /// to align with their assigned Level-Based Shared Sites in full 3D coordinates.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class CmdMoveLinksToSharedSites : IExternalCommand
    {
        /// <summary>
        /// Executes the Move Links to Shared Sites command.
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
                var groups = adapter.GetLinkRepositionGroups();

                if (groups.Count == 0)
                {
                    Autodesk.Revit.UI.TaskDialog.Show(
                        "Move Links to Shared Sites",
                        "No loaded linked models found in the active project.\nPlease link a model to reposition links to shared sites.");
                    return Result.Succeeded;
                }

                var vm = new MoveLinksToSharedSitesViewModel(adapter);
                var window = new MoveLinksToSharedSitesWindow(uiapp.MainWindowHandle)
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
