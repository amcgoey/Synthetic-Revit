using Autodesk.Revit.ApplicationServices;
using Application = Autodesk.Revit.ApplicationServices.Application;
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
using Synthetic.Infrastructure.Serialization;
using Synthetic.Infrastructure.IO;

using Synthetic.Modules.RevitDOM;
using Synthetic.Settings;
using Synthetic.Infrastructure.Persistence;

using Synthetic.Shared.UI;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.Shared.RevitAPI;

namespace Synthetic.Modules.Worksets.Commands
{
    /// <summary>
    /// Revit Command to import Worksets based on an Excel file.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class WorksetsImport : IExternalCommand
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
            Application app = uiapp.Application;
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
                    List<string>? selectedWorksets = SelectWorksets(worksetUtil.Names(), uiapp.MainWindowHandle);
                    if (selectedWorksets != null)
                    {
                        worksetUtil.Filter(selectedWorksets);
                        WorksetUtil.Create(doc, worksetUtil);
                    }
                }
                else { TaskDialogResult tResult = MissingFile(); }
            }
            return Result.Succeeded;
        }

        internal List<string>? SelectWorksets (List<string> worksetNames, IntPtr mainWindowHandle)
        {
            List<string>? selectedList = null;

            ListByCheckboxViewModel viewModel = new ListByCheckboxViewModel();
            viewModel.Title = "Select Worksets to Import";
            viewModel.Instruction = "The selected Worksets will be imported.";
            viewModel.IsSingleSelection = false;

            viewModel.SetItems(worksetNames, true);

            ListByCheckboxView dialog = new ListByCheckboxView(mainWindowHandle) { DataContext = viewModel };

            bool? dResult = dialog.ShowDialog();

            if (dResult == true)
            {
                selectedList = viewModel.CheckedItems;
            }

            return selectedList;
        }

        internal TaskDialogResult FileNotSet()
        {
            Autodesk.Revit.UI.TaskDialog taskDialog = new Autodesk.Revit.UI.TaskDialog("Synthetic Workset Settings");
            taskDialog.MainInstruction = "Path to Workset File not set.";
            taskDialog.MainContent = "Run the Set Workset File command";
            taskDialog.CommonButtons = TaskDialogCommonButtons.Close;
            taskDialog.DefaultButton = TaskDialogResult.Close;
            return taskDialog.Show();
        }

        internal TaskDialogResult ShowWorksets (string instructions, string content)
        {
            Autodesk.Revit.UI.TaskDialog resultDialog = new Autodesk.Revit.UI.TaskDialog("Worksets Created");
            resultDialog.MainInstruction = instructions;
            resultDialog.MainContent = content;
            resultDialog.CommonButtons = TaskDialogCommonButtons.Close;
            resultDialog.DefaultButton = TaskDialogResult.Close;
            
            return resultDialog.Show();
        }

        internal TaskDialogResult MissingFile ()
        {
            Autodesk.Revit.UI.TaskDialog taskDialog = new Autodesk.Revit.UI.TaskDialog("Synthetic Workset Settings");
            taskDialog.MainInstruction = "Workset File does not exist";
            taskDialog.MainContent = "Run the Set Workset File command to change the file.";
            taskDialog.CommonButtons = TaskDialogCommonButtons.Close;
            taskDialog.DefaultButton = TaskDialogResult.Close;

            return taskDialog.Show();
        }
    }
}
