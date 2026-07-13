#region Namespaces
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using Synthetic.Modules.Worksets.Commands;
using Synthetic.Modules.Worksets.Models;
using Synthetic.Modules.Worksets.Utilities;

using System;
using System.IO;
using System.Collections.Generic;
using System.Diagnostics;
using Synthetic.Infrastructure.Serialization;
using Synthetic.Infrastructure.IO;

using Synthetic.Modules.RevitDOM;
using Synthetic.Settings;
using Synthetic.Infrastructure.Persistence;
using Synthetic.Modules.StandardsManagement.ViewModels;

#endregion

namespace Synthetic.Modules.Worksets.Commands
{
    /// <summary>
    /// Displays the current path, worksheet and list of Worksets based on the current settings.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class WorksetSettingsShow : IExternalCommand
    {
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
            Autodesk.Revit.ApplicationServices.Application app = uiapp.Application;
            Document doc = uidoc.Document;

            WorksetSettings settings = SettingsManager.Get<WorksetSettings>(doc);

            if (settings == null || !settings.IsValid(doc))
            {
                TaskDialogResult tResult = FileNotSet();
            }
            else
            {
                WorksetUtil? worksetUtil = settings.WorksetsByExcel();
                if (worksetUtil != null)
                {
                    string instructions = "WorksetPath: " + settings.FullPath();
                    if (!string.IsNullOrEmpty(settings.WorksetGroup))
                    {
                        instructions = instructions + "\n\n" + "WorksetGroup: " + settings.WorksetGroup;
                    }

                    TaskDialogResult rResult = ShowWorksets(instructions, worksetUtil.WorksetNames());
                }
                else { TaskDialogResult tResult = MissingFile(); }
            }
            return Result.Succeeded;
        }

        internal TaskDialogResult ShowWorksets(string instructions, string content)
        {
            Autodesk.Revit.UI.TaskDialog resultDialog = new Autodesk.Revit.UI.TaskDialog("Worksets Created");
            resultDialog.MainInstruction = instructions;
            resultDialog.MainContent = content;
            resultDialog.CommonButtons = TaskDialogCommonButtons.Close;
            resultDialog.DefaultButton = TaskDialogResult.Close;

            TaskDialogResult result = resultDialog.Show();
            resultDialog.Dispose();
            return result;
        }
        internal TaskDialogResult FileNotSet()
        {
            Autodesk.Revit.UI.TaskDialog taskDialog = new Autodesk.Revit.UI.TaskDialog("Synthetic Workset Settings");
            taskDialog.MainInstruction = "Path to Workset File not set.";
            taskDialog.MainContent = "Run the Set Workset File command";
            taskDialog.CommonButtons = TaskDialogCommonButtons.Close;
            taskDialog.DefaultButton = TaskDialogResult.Close;
            TaskDialogResult result = taskDialog.Show();
            taskDialog.Dispose();
            return result;
        }

        internal TaskDialogResult MissingFile()
        {
            Autodesk.Revit.UI.TaskDialog taskDialog = new Autodesk.Revit.UI.TaskDialog("Synthetic Workset Settings");
            taskDialog.MainInstruction = "Workset File does not exist";
            taskDialog.MainContent = "Run the Set Workset File command to change the file.";
            taskDialog.CommonButtons = TaskDialogCommonButtons.Close;
            taskDialog.DefaultButton = TaskDialogResult.Close;

            TaskDialogResult result = taskDialog.Show();
            taskDialog.Dispose();
            return result;
        }
    }
}
