using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.Attributes;
using Synthetic.Modules.DetailItemFactory.Commands;
using Synthetic.Modules.DetailItemFactory.Handlers;
using Synthetic.Modules.DetailItemFactory.ViewModels;
using Synthetic.Modules.DetailItemFactory.Views;
using Synthetic.Modules.DetailItemFactory.Settings;

using System;
using System.Collections.Generic;
using System.IO;
using View = Autodesk.Revit.DB.View;
using TaskDialog = Autodesk.Revit.UI.TaskDialog;

using Synthetic.Shared.UI;

using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;

namespace Synthetic.Modules.DetailItemFactory.Commands
{
    /// <summary>
    /// Production external command to batch-process selected 3D model elements
    /// into 2D Detail Item family documents via temporary DWG projection and tracing.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class CmdDetailItemFactory : IExternalCommand
    {
        private static DetailItemFactoryEventHandler? _eventHandler;
        private static ExternalEvent? _externalEvent;

        /// <summary>
        /// Executes the Detail Item Factory command, showing the WPF UI options and running the batch conversion.
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
            Document doc = uidoc.Document;

            // Verify active view is 2D projection view
            View activeView = doc.ActiveView;
            if (activeView is View3D)
            {
                TaskDialog.Show("Detail Item Factory", "Please run this command from a 2D view (Plan, Section, or Elevation).");
                return Result.Failed;
            }

            // Retrieve selection
            ICollection<ElementId> selectedIds = uidoc.Selection.GetElementIds();
            if (selectedIds == null || selectedIds.Count == 0)
            {
                TaskDialog.Show("Detail Item Factory", "Please select at least one 3D model element to convert.");
                return Result.Failed;
            }

            // Show UI to gather user preferences
            DetailItemFactoryViewModel vm = new DetailItemFactoryViewModel(doc, activeView, selectedIds, uiapp.MainWindowHandle);
            DetailItemFactoryView view = new DetailItemFactoryView(uiapp.MainWindowHandle) { DataContext = vm };

            if (view.ShowDialog() != true)
            {
                return Result.Cancelled;
            }

            // Setup asynchronous modeless progress dialog and event handler
            Synthetic.Shared.UI.ProgressCoordinator.Initialize("Detail Item Factory Progress", "Starting batch processing...", vm.Elements.Count);

            _eventHandler = new DetailItemFactoryEventHandler
            {
                ConfiguredElements = new List<SelectedElementItemViewModel>(vm.Elements),
                OutputFolder = vm.OutputPath,
                TargetSubcategory = vm.SelectedSubcategory,
                OverwriteExisting = vm.OverwriteExisting
            };

            // Register and raise the external event (persisted in static reference to prevent garbage collection)
            _externalEvent = ExternalEvent.Create(_eventHandler);
            if (_externalEvent == null)
            {
                Synthetic.Shared.UI.ProgressCoordinator.Close();
                TaskDialog.Show("Detail Item Factory", "Failed to create the external event handler.");
                return Result.Failed;
            }

            // Raise the event to start processing on the main thread
            _externalEvent.Raise();

            return Result.Succeeded;
        }
    }
}
