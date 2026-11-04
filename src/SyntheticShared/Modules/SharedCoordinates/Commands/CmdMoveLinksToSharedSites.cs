using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace Synthetic.Modules.SharedCoordinates.Commands
{
    /// <summary>
    /// External command stub for Move Links to Shared Sites (Ticket #128).
    /// Provides assembly type resolution for RibbonManager and RibbonTests.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class CmdMoveLinksToSharedSites : IExternalCommand
    {
        /// <summary>
        /// Executes the Move Links to Shared Sites command stub.
        /// </summary>
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            TaskDialog.Show(
                "Move Links to Shared Sites",
                "Move Links to Shared Sites command is scheduled for Ticket #128.");
            return Result.Succeeded;
        }
    }
}
