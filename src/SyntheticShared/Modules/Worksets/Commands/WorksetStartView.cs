using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Synthetic.Modules.Worksets.Commands;
using Synthetic.Modules.Worksets.Models;
using Synthetic.Modules.Worksets.Utilities;

using System;
using System.Collections.Generic;
using System.Text;
using Synthetic.Settings;
using Synthetic.Infrastructure.Persistence;

using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;
using Synthetic.Infrastructure.Serialization;

using Synthetic.Infrastructure.IO;
using Synthetic.Modules.StandardsManagement.ViewModels;
namespace Synthetic.Modules.Worksets.Commands
{
    /// <summary>
    /// Revit external command to set project info (such as starting view) based on workset Excel configuration settings.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class WorksetStartView : IExternalCommand
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

            if (settings == null || settings.IsSettingsEmpty())
            {
                TaskDialogResult tResult = FileNotSet();
            }
            else
            {
                WorksetUtil? worksetUtil = settings.WorksetsByExcel();
                if (worksetUtil != null)
                {
                    worksetUtil.SetProjectInfo(doc);
                }
                else { TaskDialogResult tResult = MissingFile(); }
            }

            return Result.Succeeded;
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
