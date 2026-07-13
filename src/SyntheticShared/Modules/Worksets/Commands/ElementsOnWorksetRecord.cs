using Synthetic.Modules.Worksets.Commands;
using Synthetic.Modules.Worksets.Models;
using Synthetic.Modules.Worksets.Utilities;
using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using System.IO;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Application = Autodesk.Revit.ApplicationServices.Application;

using Synthetic.Modules.RevitDOM;
using Synthetic.Infrastructure.Serialization;

using Synthetic.Shared.UI;

using Synthetic.Shared.RevitAPI;
using Synthetic.Infrastructure.IO;
using Synthetic.Modules.StandardsManagement.ViewModels;

namespace Synthetic.Modules.Worksets.Commands
{
    /// <summary>
    /// Revit external command to record the elements associated with a user-selected workset to a JSON file.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class ElementsOnWorksetRecord : IExternalCommand
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
            Document document = uidoc.Document;

            Result commandResult = Result.Succeeded;

            FilteredWorksetCollector worksets = new FilteredWorksetCollector(document).OfKind(WorksetKind.UserWorkset);

            List<string> worksetNames = new List<string>();
            foreach (Workset w in worksets)
            {
                worksetNames.Add(w.Name);
            }

            var r = SelectWorkset(worksetNames, uiapp.MainWindowHandle);
            bool dResult = r.result;
            string? selection = r.selection;

            if (dResult && !string.IsNullOrEmpty(selection))
            {
                string? docPath = DocumentUtil.GetFilePath(document);
                string docName = !string.IsNullOrEmpty(docPath) ? Path.GetFileNameWithoutExtension(docPath) : "Document";
                string initialFileName = docName + " - " + selection + ".json";
                string? fullPath = CommandUtil.SaveAsJSON(initialFileName);

                Workset? workset = worksets.Where(w => w.Name == selection).FirstOrDefault();
                if (workset != null && !string.IsNullOrEmpty(fullPath))
                {
                    ElementsOnWorkset elementsOnWorkset = new ElementsOnWorkset(workset, document);
                    string json = elementsOnWorkset.ToJSON();

                    File.WriteAllText(fullPath, json);

                    if (elementsOnWorkset.IfLog())
                    {
                        string logFileName = Path.GetFileNameWithoutExtension(fullPath);
                        string logExtension = Path.GetExtension(fullPath) ?? ".json";
                        string logPath = Path.GetDirectoryName(fullPath) ?? string.Empty;

                        string logFullPath = Path.Combine(logPath, logFileName + " - " + DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss") + " Log Record" + logExtension);

                        File.WriteAllText(logFullPath, elementsOnWorkset.GetLog());
                    }
                }
                else { commandResult = Result.Failed; }
            }
            else { commandResult = Result.Failed; }

            return commandResult;
        }

        internal (bool result, string? selection) SelectWorkset(List<string> worksetNames, IntPtr mainWindowHandle)
        {
            DropdownSelectionViewModel viewModel = new DropdownSelectionViewModel();
            viewModel.Title = "Select a Workset";
            viewModel.Instruction = "Select a Workset to record all elements on";
            viewModel.ItemLabel = "Worksets";
            viewModel.Items = worksetNames;

            DropdownSelectionView dialog = new DropdownSelectionView(mainWindowHandle) { DataContext = viewModel };
            bool? dResult = dialog.ShowDialog();
            string? selection = viewModel.SelectedItem;

            return (dResult == true, selection);
        }
    }
}