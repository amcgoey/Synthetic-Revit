using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

using Synthetic.Shared.UI;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.Modules.StandardsManagement.Views;
using Synthetic.Modules.StandardsManagement.Utilities;

namespace Synthetic.Modules.StandardsManagement.Commands
{
    /// <summary>
    /// Command to display the Modeless Project Standards Dashboard window.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class CmdProjectStandards : IExternalCommand
    {
        private static ProjectStandardsDashboardWindow? _windowInstance;

        /// <summary>
        /// Executes the command.
        /// </summary>
        public Result Execute(
          ExternalCommandData commandData,
          ref string message,
          ElementSet elements)
        {
            UIApplication uiapp = commandData.Application;

            try
            {
                // If the window is already open, bring it to front
                if (_windowInstance != null && _windowInstance.IsLoaded)
                {
                    _windowInstance.Focus();
                    return Result.Succeeded;
                }

                // Initialize ViewModel with uiapp context and injected services
                var fileDialog = new WindowsFileDialogService();
                var guardrail = new WindowsGuardrailPromptService();
                var exportService = new StandardsExportService(guardrail, fileDialog);
                var vm = new ProjectStandardsDashboardViewModel(uiapp, fileDialog, exportService);

                // Create external event for modeless execution
                var handler = new ProjectStandardsExternalEventHandler();
                var externalEvent = ExternalEvent.Create(handler);
                vm.SetExternalEvent(externalEvent, handler);

                // Create the modeless window instance
                _windowInstance = new ProjectStandardsDashboardWindow(uiapp.MainWindowHandle) { DataContext = vm };
                
                // Clear instance reference on close to allow reopening later
                _windowInstance.Closed += (s, e) => { _windowInstance = null; };

                _windowInstance.Show(); // Modeless execution!
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}
