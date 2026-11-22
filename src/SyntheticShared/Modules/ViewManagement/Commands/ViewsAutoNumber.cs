using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Synthetic.Modules.ViewManagement.Commands;
using Synthetic.Modules.ViewManagement.Models;
using Synthetic.Modules.ViewManagement.Utilities;

using System;
using System.Collections.Generic;
using System.Text;
using Synthetic.Settings;
using Synthetic.Infrastructure.Persistence;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;

namespace Synthetic.Modules.ViewManagement.Commands
{
    /// <summary>
    /// Revit Command to automatically number views on the active sheet based on a family that determines the origin point and the grid spacing.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class ViewsAutoNumber : IExternalCommand
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

            string transactionName = "Autonumber views on sheet";

            // Access View Renumber Settings
            ViewAutoNumSettings settings = SettingsManager.Get<ViewAutoNumSettings>(doc);

            if (
                settings == null ||
                !settings.IsValid(doc)
                )
            {
                Autodesk.Revit.UI.TaskDialog taskDialog = new Autodesk.Revit.UI.TaskDialog("Synthetic View Autonumber Settings");
                taskDialog.MainInstruction = "View Autonumber Settins not set";
                taskDialog.MainContent = "Run the Set View Autonumber Settings command";
                taskDialog.CommonButtons = TaskDialogCommonButtons.Close;
                taskDialog.DefaultButton = TaskDialogResult.Close;
                TaskDialogResult tResult = taskDialog.Show();
                taskDialog.Dispose();
            }
            else
            {
                ViewAutoNumModel viewAutoNum = new ViewAutoNumModel(
                    doc,
                    settings.ViewAutoNumFamily,
                    settings.ViewAutoNumFamilyType,
                    settings.ViewAutoNumXGridName,
                    settings.ViewAutoNumYGridName
                    );

                

                if (viewAutoNum.Family != null)
                {
                    ViewSheet sheet = (ViewSheet)doc.ActiveView;
                    using (Autodesk.Revit.DB.Transaction trans = new Autodesk.Revit.DB.Transaction(doc))
                    {
                        trans.Start(transactionName);
                        viewAutoNum.AutoNumberOnSheet(sheet);
                        trans.Commit();
                    }
                }
                else
                {
                    Autodesk.Revit.UI.TaskDialog taskDialog = new Autodesk.Revit.UI.TaskDialog("Synthetic View Autonumber Settings");
                    taskDialog.MainInstruction = "View Autonumber Family cannot be found";
                    taskDialog.MainContent = "Please load the family and place on the sheet or set a different family.";
                    taskDialog.CommonButtons = TaskDialogCommonButtons.Close;
                    taskDialog.DefaultButton = TaskDialogResult.Close;
                    TaskDialogResult tResult = taskDialog.Show();
                    taskDialog.Dispose();
                }
            }

                return Result.Succeeded;
        }
    }
}
