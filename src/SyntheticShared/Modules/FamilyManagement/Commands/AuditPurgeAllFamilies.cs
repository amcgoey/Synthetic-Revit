using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Application = Autodesk.Revit.ApplicationServices.Application;
using Synthetic.Modules.FamilyManagement.Commands;

using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using Autodesk.Revit.Attributes;
using System.Diagnostics;
using System.IO;
using Newtonsoft.Json;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;
using Synthetic.Shared.UI;
using Synthetic.Shared.RevitAPI;
using Synthetic.Modules.FamilyManagement.Handlers;

namespace Synthetic.Modules.FamilyManagement.Commands
{
    /// <summary>
    /// Revit external command to audit and purge all families loaded in the current project.
    /// Provides options to audit only, purge unused elements, and delete schema options.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class AuditPurgeAllFamilies : IExternalCommand
    {
        private static Handlers.AuditPurgeEventHandler? _eventHandler;
        private static ExternalEvent? _externalEvent;

        /// <summary>
        /// Execute a Revit Command
        /// </summary>
        /// <param name="commandData">commandData</param>
        /// <param name="message">message</param>
        /// <param name="elements">Currently selected elements</param>
        /// <returns>A Autodesk.Revit.UI.Result</returns>
        public Result Execute(
          ExternalCommandData commandData,
          ref string message,
          ElementSet elements)
        {
            UIApplication uiapp = commandData.Application;
            UIDocument uidoc = uiapp.ActiveUIDocument;
            Document doc = uidoc.Document;

            TaskDialogResult dialogResult = PurgeOptions();

            bool purge = false;
            bool purgeSchema = false;
            List<string>? schemaExceptions = null;

            switch (dialogResult)
            {
                case TaskDialogResult.Cancel:
                    return Result.Cancelled;
                case TaskDialogResult.CommandLink1:
                    purge = false;
                    purgeSchema = false;
                    break;
                case TaskDialogResult.CommandLink2:
                    purge = true;
                    purgeSchema = false;
                    break;
                case TaskDialogResult.CommandLink3:
                    purge = true;
                    purgeSchema = true;
                    schemaExceptions = new List<string>() { "Enscape", "Kinship", "CTC", "Synthetic" };
                    break;
                case TaskDialogResult.CommandLink4:
                    purge = true;
                    purgeSchema = true;
                    schemaExceptions = null;
                    break;
                default:
                    return Result.Failed;
            }

            // Calculate total families to be processed
            IList<Family> families = new FilteredElementCollector(doc)
                .OfClass(typeof(Family))
                .Cast<Family>()
                .Where(f => f.IsEditable && !f.IsInPlace)
                .ToList();

            int totalFamilies = families.Count;

            // Initialize Progress Coordinator (modeless progress window)
            Synthetic.Shared.UI.ProgressCoordinator.Initialize("Audit & Purge Families", "Purging loaded families...", totalFamilies);

            // Register and raise the external event
            _eventHandler = new Handlers.AuditPurgeEventHandler
            {
                Purge = purge,
                PurgeSchema = purgeSchema,
                SchemaExceptions = schemaExceptions
            };

            _externalEvent = ExternalEvent.Create(_eventHandler);
            if (_externalEvent == null)
            {
                Synthetic.Shared.UI.ProgressCoordinator.Close();
                Autodesk.Revit.UI.TaskDialog.Show("Audit & Purge Families", "Failed to create the external event handler.");
                return Result.Failed;
            }

            // Raise the event to start processing on the main thread
            _externalEvent.Raise();

            return Result.Succeeded;
        }

        internal TaskDialogResult PurgeOptions()
        {
            Autodesk.Revit.UI.TaskDialog taskDialog = new Autodesk.Revit.UI.TaskDialog("Audit & Purge Families");
            taskDialog.MainInstruction = "Select Options to Audit and Purge All the Families in the Project";
            taskDialog.MainContent = "Command will open each family in the project, audit it and based on the options below, purge any unused elements and delete all schema.  Note that purging unused elements could impact the options available for nested families.  In addition, deleting all scheme from each family, deletes it from all open documents including the current project.  This will delete Enscape, Kinship, CTC, and Synthetic settings.  To keep these schema, only delete Typical Schema ";
            taskDialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Audit All Families Only");
            taskDialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Audit and Purge Unused");
            taskDialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "Audit, Purge and Delete Schema Except Typical");
            taskDialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink4, "Audit, Purge and Delete All Schema");

            taskDialog.CommonButtons = TaskDialogCommonButtons.Cancel;
            taskDialog.DefaultButton = TaskDialogResult.CommandLink1;

            TaskDialogResult result = taskDialog.Show();
            taskDialog.Dispose();
            return result;
        }
    }
}
