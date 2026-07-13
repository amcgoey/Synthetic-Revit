using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Synthetic.Modules.Worksets.Commands;
using Synthetic.Modules.Worksets.Models;
using Synthetic.Modules.Worksets.Utilities;

using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using Synthetic.Infrastructure.Serialization;
using Synthetic.Infrastructure.IO;
using Synthetic.Settings;
using Synthetic.Infrastructure.Persistence;

using Synthetic.Shared.UI;
using Application = Autodesk.Revit.ApplicationServices.Application;

using Synthetic.Core;
using Synthetic.Modules.RevitDOM;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.Shared.RevitAPI;
namespace Synthetic.Modules.Worksets.Commands
{
    /// <summary>
    /// Sets the path and sheet to an Excel file with Worksets.  Stores the setting in an Extensible Storage.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class WorksetSetFile : IExternalCommand
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

            WorksetSettings worksetSettings = new WorksetSettings();

            Config? config = null;
            if (App.Configurations.ContainsProjectConfig(doc))
            {
                config = App.Configurations.GetProjectConfig(doc);
            }
            else
            {
                config = new Config();
                App.Configurations.AddProjectConfig(doc, config);
            }

            // Ask user to select from different methods of setting the Workset File
            TaskDialogResult taskDialogResult = WorksetFileOptions();

            using (Autodesk.Revit.DB.Transaction trans = new Autodesk.Revit.DB.Transaction(doc))
            {
                trans.Start("Set Workset File Path");

                // Determine which method of setting the Workset file
                // Use Default Settings
                if (taskDialogResult == TaskDialogResult.CommandLink1)
                {
                    config = App.Configurations.GetAppConfig();
                    worksetSettings = config.GetSettings<WorksetSettings>(WorksetSettings.Name);
                }

                // Set Workset File to be named same as the Project
                else if (taskDialogResult == TaskDialogResult.CommandLink2)
                {
                    string file = doc.Title + " Worksets.xlsx";
                    string? path = doc.PathName;

                    if (!string.IsNullOrEmpty(path))
                    {
                        BasicFileInfo basicFileInfo = BasicFileInfo.Extract(path);
                        if (basicFileInfo.IsCentral)
                        {
                            ModelPath modelPath = doc.GetWorksharingCentralModelPath();
                            string centralServerPath = ModelPathUtils.ConvertModelPathToUserVisiblePath(modelPath);
                            file = Path.GetFileName(centralServerPath.Replace(".rvt", " Worksets.xlsx"));
                            path = Path.GetDirectoryName(centralServerPath) + "\\";
                        }
                    }

                    if (file != null && path != null)
                    {
                        if (File.Exists(Path.Combine(path, file)))
                        {
                            worksetSettings = new WorksetSettings(file, path, null);

                            Excel excel = new Excel(Path.Combine(path, file));
                            List<string> itemList = excel.WorkSheetNames();

                            ListByCheckboxViewModel viewModel = new ListByCheckboxViewModel();
                            viewModel.Title = "Select a Workset Group";
                            viewModel.Instruction = "";
                            viewModel.IsSingleSelection = true;
                            viewModel.SetItems(itemList, false);

                            ListByCheckboxView dialog = new ListByCheckboxView(uiapp.MainWindowHandle) { DataContext = viewModel };

                            if (dialog.ShowDialog() == true && viewModel.CheckedItems.Count > 0)
                            {
                                worksetSettings.WorksetGroup = viewModel.CheckedItems[0];
                            }

                            config.SetSettings(WorksetSettings.Name, worksetSettings);
                        }
                    }
                    else
                    {
                        return Result.Failed;
                    }
                }

                // Chose a custom path for the Workset File
                else if (taskDialogResult == TaskDialogResult.CommandLink3)
                {
                    string? file = null;
                    string? path = null;

                    // Create an instance of the open file dialog box.
                    FileOpenDialog openFileDialog = new FileOpenDialog("Excel Files (*.xlsx,*.xls,*.csv)|*.xlsx;*.xls;*.csv");
                    openFileDialog.Title = "Select Excel File with Workset List";

                    // Call the ShowDialog method to show the dialog box.
                    ItemSelectionDialogResult result = openFileDialog.Show();
                    // Process input if the user clicked OK.
                    if (result == ItemSelectionDialogResult.Confirmed)
                    {
                        ModelPath modelPath = openFileDialog.GetSelectedModelPath();
                        string fullPath = ModelPathUtils.ConvertModelPathToUserVisiblePath(modelPath);
                        file = Path.GetFileName(fullPath);
                        path = Path.GetDirectoryName(fullPath) + "\\";
                        openFileDialog.Dispose();

                        if (file != null && path != null)
                        {
                            if (File.Exists(Path.Combine(path, file)))
                            {
                                worksetSettings = new WorksetSettings(file, path, null);

                                Excel excel = new Excel(Path.Combine(path, file));
                                List<string> itemList = excel.WorkSheetNames();

                                ListByCheckboxViewModel viewModel = new ListByCheckboxViewModel();
                                viewModel.Title = "Select a Workset Group";
                                viewModel.Instruction = "";
                                viewModel.IsSingleSelection = true;
                                viewModel.SetItems(itemList, false);

                                ListByCheckboxView dialog = new ListByCheckboxView(uiapp.MainWindowHandle) { DataContext = viewModel };

                                if (dialog.ShowDialog() == true && viewModel.CheckedItems.Count > 0)
                                {
                                    worksetSettings.WorksetGroup = viewModel.CheckedItems[0];
                                }

                                config.SetSettings(WorksetSettings.Name, worksetSettings);
                            }
                        }
                        else
                        {
                            return Result.Failed;
                        }
                    }
                }

                // Cancel
                else if (taskDialogResult == TaskDialogResult.Cancel)
                {
                    trans.Dispose();
                    return Result.Succeeded;
                }

                SettingsManager.Save(doc, worksetSettings);
                trans.Commit();
            }
            return Result.Succeeded;
        }

        internal TaskDialogResult WorksetFileOptions()
        {
            Autodesk.Revit.UI.TaskDialog taskDialog = new Autodesk.Revit.UI.TaskDialog("Set Workset File");
            taskDialog.MainInstruction = "Select an option below";
            taskDialog.MainContent = "Select an Excel file with Workset list";
            taskDialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Select App Standard Workset File");
            taskDialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Select Excel file at Project Path named \"Project Name\" + \"Workset.xlsx\"");
            taskDialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "Select a custom location for the excel file");

            taskDialog.CommonButtons = TaskDialogCommonButtons.Cancel;
            taskDialog.DefaultButton = TaskDialogResult.CommandLink1;

            TaskDialogResult result = taskDialog.Show();
            taskDialog.Dispose();
            return result;
        }
    }
}
