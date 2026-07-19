using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

using Synthetic.Shared.UI;

using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;
using Synthetic.Modules.SettingsDashboard.ViewModels;
using Synthetic.Modules.SettingsDashboard.Views;

namespace Synthetic.Modules.SettingsDashboard.Commands
{
    /// <summary>
    /// Revit external command to display and edit the current project's configuration settings
    /// using the Unified Settings Dashboard.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class SettingsDashboardCommand : IExternalCommand
    {
        /// <summary>
        /// Executes the settings dashboard command.
        /// </summary>
        /// <param name="commandData">Revit external command data.</param>
        /// <param name="message">A message returning errors if any.</param>
        /// <param name="elements">Revit elements set.</param>
        /// <returns>Result code of the execution.</returns>
        public Result Execute(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
        {
            UIApplication uiapp = commandData.Application;
            UIDocument uidoc = uiapp.ActiveUIDocument;
            if (uidoc == null)
            {
                message = "No active document open.";
                return Result.Failed;
            }
            Document doc = uidoc.Document;

            try
            {
                // Robustness Directive: Fetch the Revit main window handle using Process
                IntPtr mainWindowHandle = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;

                var viewModel = new SettingsDashboardViewModel(doc, mainWindowHandle);
                var window = new SettingsDashboardWindow(mainWindowHandle)
                {
                    DataContext = viewModel
                };

                window.ShowDialog();
                window.DataContext = null;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }

            return Result.Succeeded;
        }
    }
}
