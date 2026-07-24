### File: Worksets/Commands/ElementsOnWorksetRecord.cs
```csharp
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

using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;
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
```

### File: Worksets/Commands/ElementsOnWorksetReload.cs
```csharp
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

using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;
using Synthetic.Infrastructure.Serialization;

using Synthetic.Shared.RevitAPI;
using Synthetic.Infrastructure.IO;
using Newtonsoft.Json;
using Synthetic.Modules.StandardsManagement.ViewModels;

namespace Synthetic.Modules.Worksets.Commands
{
    /// <summary>
    /// Revit external command to reload recorded elements from a JSON file and move them back to their recorded workset.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class ElementsOnWorksetReload : IExternalCommand
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

            string? fullPath = CommandUtil.OpenJSON();
            if (fullPath != null && fullPath != String.Empty && File.Exists(fullPath))
            {
                string json = File.ReadAllText(fullPath);
                ElementsOnWorkset? elementsOnWorkset = JsonConvert.DeserializeObject<ElementsOnWorkset>(json);
                if (elementsOnWorkset != null)
                {
                    elementsOnWorkset.MoveToWorkset(document);
                    if (elementsOnWorkset.IfLog())
                    {
                        string logFileName = Path.GetFileNameWithoutExtension(fullPath);
                        string logExtension = Path.GetExtension(fullPath);
                        string? logPath = Path.GetDirectoryName(fullPath);

                        if (logPath != null)
                        {
                            string logFullPath = Path.Combine(logPath, logFileName + " - " + DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss") + " Log Reload" + logExtension);
                            File.WriteAllText(logFullPath, elementsOnWorkset.GetLog());
                        }
                    }
                }
            }
            else { commandResult = Result.Failed; }

            return commandResult;
        }
    }
}
```

### File: Worksets/Commands/ScopeBoxesMoveToWorkset.cs
```csharp
using Synthetic.Modules.Worksets.Commands;
using Synthetic.Modules.Worksets.Models;
using Synthetic.Modules.Worksets.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

using Synthetic.Modules.ViewManagement.Utilities;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;
using Synthetic.Infrastructure.Serialization;

using Synthetic.Shared.RevitAPI;
using Synthetic.Infrastructure.IO;

using Synthetic.Shared.UI;
using Synthetic.Modules.StandardsManagement.ViewModels;

namespace Synthetic.Modules.Worksets.Commands
{
    /// <summary>
    /// Revit external command to move all scope boxes in the project to a selected workset
    /// and optionally update their visibility in views.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class ScopeBoxesMoveToWorkset : IExternalCommand
    {
        /// <summary>
        /// Executes the command to move scope boxes.
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

            if (!doc.IsWorkshared)
            {
                Autodesk.Revit.UI.TaskDialog.Show("Error", "Document is not workshared.");
                return Result.Failed;
            }

            IList<Element> scopeBoxes = ScopeBoxUtil.GetAllScopeBoxes(doc);

            if (scopeBoxes.Count == 0)
            {
                Autodesk.Revit.UI.TaskDialog.Show("Scope Boxes", "No scope boxes found in the document.");
                return Result.Succeeded;
            }

            FilteredWorksetCollector worksets = new FilteredWorksetCollector(doc).OfKind(WorksetKind.UserWorkset);
            List<string> worksetNames = worksets.Select(w => w.Name).OrderBy(n => n).ToList();

            DropdownSelectionViewModel viewModel = new DropdownSelectionViewModel();
            viewModel.Title = "Move Scope Boxes";
            viewModel.Instruction = $"Select a workset to move all {scopeBoxes.Count} scope boxes to.";
            viewModel.ItemLabel = "Worksets";
            viewModel.Items = worksetNames;

            DropdownSelectionView dialog = new DropdownSelectionView(uiapp.MainWindowHandle) { DataContext = viewModel };

            if (dialog.ShowDialog() == true)
            {
                string? selection = viewModel.SelectedItem;
                Workset? destinationWorkset = worksets.FirstOrDefault(w => w.Name == selection);

                if (destinationWorkset != null)
                {
                    ScopeBoxUtil.MoveToWorkset(doc, scopeBoxes, destinationWorkset);

                    IList<Autodesk.Revit.DB.View> relevantViews = ScopeBoxUtil.GetScopeBoxViews(doc);
                    List<string> skippedViews = new List<string>();
                    int updatedCount = 0;

                    if (relevantViews.Count > 0)
                    {
                        using (Transaction trans = new Transaction(doc))
                        {
                            trans.Start("Update Scope Box View Visibility");
                            foreach (Autodesk.Revit.DB.View view in relevantViews)
                            {
                                if (ViewUtil.SetWorksetVisibilityInView(view, destinationWorkset))
                                {
                                    updatedCount++;
                                }
                                else
                                {
                                    skippedViews.Add(view.Name);
                                }
                            }
                            trans.Commit();
                        }
                    }

                    StringBuilder resultMessage = new StringBuilder();
                    resultMessage.AppendLine($"{scopeBoxes.Count} scope boxes moved to workset '{selection}'.");
                    
                    if (relevantViews.Count > 0)
                    {
                        resultMessage.AppendLine($"\nView Visibility Updated ({updatedCount}/{relevantViews.Count}):");
                        if (skippedViews.Count > 0)
                        {
                            resultMessage.AppendLine("\nThe following views were skipped because they have a view template:");
                            foreach (string viewName in skippedViews)
                            {
                                resultMessage.AppendLine($"- {viewName}");
                            }
                        }
                    }

                    Autodesk.Revit.UI.TaskDialog.Show("Success", resultMessage.ToString());
                }
            }

            return Result.Succeeded;
        }
    }
}
```

### File: Worksets/Commands/WorksetSetFile.cs
```csharp
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
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;
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
```

### File: Worksets/Commands/WorksetSettingsShow.cs
```csharp
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

using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;
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
```

### File: Worksets/Commands/WorksetsImport.cs
```csharp
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

using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;
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
```

### File: Worksets/Commands/WorksetStartView.cs
```csharp
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
```

### File: Worksets/Models/ElementsOnWorkset.cs
```csharp
using Synthetic.Modules.Worksets.Commands;
using Synthetic.Modules.Worksets.Models;
using Synthetic.Modules.Worksets.Utilities;

using System;
using System.Collections.Generic;
using System.Text;

using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Visual;
using Newtonsoft.Json;

using Synthetic.Shared.RevitAPI;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;
using Synthetic.Modules.StandardsManagement.ViewModels;
namespace Synthetic.Modules.Worksets.Models
{
    /// <summary>
    /// Model that tracks elements associated with a specific workset.
    /// Used for exporting elements on a workset and recreating them back.
    /// </summary>
    public class ElementsOnWorkset

    {
        /// <summary>
        /// Gets or sets the name of the workset.
        /// </summary>
        public string WorksetName = string.Empty;

        /// <summary>
        /// Gets or sets the unique identifier of the workset.
        /// </summary>
        public System.Guid WorksetUniqueId;

        /// <summary>
        /// Gets or sets the list of unique element IDs.
        /// </summary>
        public List<string> ElementUniqueIds;
        internal List<string> Results;
        internal List<string> Errors;

        /// <summary>
        /// Initializes a new instance of the ElementsOnWorkset class.
        /// </summary>
        public ElementsOnWorkset()
        {
            this.ElementUniqueIds = new List<string>();
            this.Results = new List<string>();
            this.Errors = new List<string>();
        }

        /// <summary>
        /// Initializes a new instance of the ElementsOnWorkset class from a Revit workset and document.
        /// </summary>
        /// <param name="workset">The Revit workset.</param>
        /// <param name="document">The Revit document.</param>
        public ElementsOnWorkset(Workset workset, Document document)
        {
            this.ElementUniqueIds = new List<string>();
            this.Results = new List<string>();
            this.Errors = new List<string>();

            this.WorksetName = workset.Name;
            this.WorksetUniqueId = workset.UniqueId;

            IList<Element> elements = WorksetUtil.GetElementsOnWorkset(workset, document);
            
            if(elements != null && elements.Count > 0)
            {
                foreach(Element element in elements)
                {
                    this.ElementUniqueIds.Add(element.UniqueId);
                }
            }
        }

        /// <summary>
        /// Initializes a new instance of the ElementsOnWorkset class with specific properties.
        /// </summary>
        /// <param name="worksetName">Name of the workset.</param>
        /// <param name="worksetUniqueId">Unique ID of the workset.</param>
        /// <param name="elementUniqueIds">List of element unique IDs.</param>
        public ElementsOnWorkset(string worksetName, Guid worksetUniqueId, List<string> elementUniqueIds)
        {
            this.WorksetName = worksetName;
            this.WorksetUniqueId = worksetUniqueId;
            this.ElementUniqueIds = elementUniqueIds;
            this.Results = new List<string>();
            this.Errors = new List<string>();
        }

        /// <summary>
        /// Add a new element's UniqueId to the collection.
        /// </summary>
        /// <param name="UniqueId">A string representing the UniqueId of the element</param>
        /// <returns>This ElementsOnWorkset object for chaining commands.</returns>
        public ElementsOnWorkset Add (string UniqueId)
        {
            this.ElementUniqueIds.Add(UniqueId);
            return this;
        }

        /// <summary>
        /// Moves the elements with UniqueIds in the ElementsOnWorkset object to the Workset.  If the workset doesn't exist, it will be created.
        /// </summary>
        /// <param name="document">A Revit Document</param>
        /// <returns>The Revit Elements with UniqueIds that were moved.</returns>
        /// <exception cref="System.NotImplementedException"></exception>
        /// <exception cref="System.InvalidOperationException"></exception>
        public List<Element>? MoveToWorkset(Document document)
        {
            List<Element>? elements = null;
            if (document.IsWorkshared)
            {
                Workset? workset = document.GetWorksetTable().GetWorkset(this.WorksetUniqueId);
                if (workset == null && this.WorksetName != null && this.WorksetName != String.Empty)
                {
                    if (!WorksetTable.IsWorksetNameUnique(document, this.WorksetName))
                    {
                        workset = WorksetUtil.GetByName(this.WorksetName, document);
                    }
                    else
                    {
                        try
                        {
                            var createdWorksets = WorksetUtil.Create(document, WorksetName, true);
                            if (createdWorksets != null && createdWorksets.ContainsKey(WorksetName))
                            {
                                workset = createdWorksets[WorksetName];
                            }
                        }
                        catch { workset = null; }
                    }
                }

                if (workset != null && this.ElementUniqueIds != null && this.ElementUniqueIds.Count > 0)
                {
                    elements = new List<Element>();
                    foreach (string uniqueId in this.ElementUniqueIds)
                    {
                        Element element = document.GetElement(uniqueId);
                        if (element != null)
                        {
                            elements.Add(element);
                        }
                        else { this.Errors.Add("Element missing from project.  " + uniqueId); }
                    }

                    if (elements.Count > 0)
                    {
                        ElementUtil.SetWorkset(elements, workset, document);
                    }
                    else { elements = null; }
                }
                else
                {
                    throw new NotImplementedException("Workset does not exist in the document and can not be created");
                }
            }
            else
            {
                throw new InvalidOperationException("Document isn't Workshared!  You cann't move elements to a workset if worksets don't exist.");
            }

            return elements;
        }

        /// <summary>
        /// Serializes the object to JSON
        /// </summary>
        /// <returns>A JSON string</returns>
        public string ToJSON()
        {
            return Newtonsoft.Json.JsonConvert.SerializeObject(this, Formatting.Indented);
        }

        /// <summary>
        /// Deserializes the object from a JSON string.
        /// </summary>
        /// <param name="JSON">A string of JSON</param>
        /// <returns>The deserializedd object</returns>
        public static ElementsOnWorkset ByJSON(string JSON)
        {
            return JsonConvert.DeserializeObject<ElementsOnWorkset>(JSON)!;
        }

        /// <summary>
        /// Checks if there are any results to log
        /// </summary>
        /// <returns>True if there are errors or results to log.</returns>
        public bool IfLog()
        {
            if (this.Errors.Count > 0 || this.Results.Count > 0)
            {
                return true;
            }
            else { return false; }
        }

        /// <summary>
        /// Complies the log of results
        /// </summary>
        /// <returns>A json string of the Log</returns>
        public string GetLog()
        {
            Dictionary<string, List<string>> log = new Dictionary<string, List<string>>
            {
                {"Elements Moved", this.Results},
                {"Errors", this.Errors }
            };
            return Newtonsoft.Json.JsonConvert.SerializeObject(log, Formatting.Indented);
        }
    }
}
```

### File: Worksets/Models/WorksetModel.cs
```csharp
using Synthetic.Modules.Worksets.Commands;
using Synthetic.Modules.Worksets.Models;
using Synthetic.Modules.Worksets.Utilities;

using System;
using System.Collections.Generic;
using System.Linq;
using Synthetic.Infrastructure.Serialization;
using Synthetic.Infrastructure.IO;

using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;
using Synthetic.Modules.StandardsManagement.ViewModels;
namespace Synthetic.Modules.Worksets.Models
{
    /// <summary>
    /// Represents a model representation of a Revit Workset, mapping its name, visibility, alias, and description.
    /// </summary>
    public class WorksetModel : ObjectModel
    {
        /// <summary>
        /// Gets or sets the name of the workset.
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets a value indicating whether the workset is visible.
        /// </summary>
        public bool Visibility { get; set; }

        /// <summary>
        /// Gets or sets the alias name of the workset.
        /// </summary>
        public string? Alias { get; set; }

        /// <summary>
        /// Gets or sets the description of the workset.
        /// </summary>
        public string? Description { get; set; }

        /// <summary>
        /// Initializes a new instance of the WorksetModel class.
        /// </summary>
        /// <param name="name">The name of the workset.</param>
        /// <param name="visibility">Visibility state, default is true.</param>
        /// <param name="alias">The alias name.</param>
        /// <param name="description">The description.</param>
        public WorksetModel(string name, bool visibility = true, string? alias = null, string? description = null)
        {
            this.Name = name;
            this.Visibility = visibility;
            this.Alias = alias;
            this.Description = description;
        }

        //public WorksetModel(object[] excelRow)
        //{
        //    this.name = excelRow[0].ToString();
        //    if (excelRow[1].ToString() == "True") { this.visibility = true; }
        //    else { this.visibility = false; }
        //    this.description = excelRow[2].ToString();
        //}
    }
}
```

### File: Worksets/Utilities/ScopeBoxUtil.cs
```csharp
using Synthetic.Modules.Worksets.Commands;
using Synthetic.Modules.Worksets.Models;
using Synthetic.Modules.Worksets.Utilities;

using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;

using RevitDB = Autodesk.Revit.DB;
using RevitDoc = Autodesk.Revit.DB.Document;
using RevitElem = Autodesk.Revit.DB.Element;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;
using Synthetic.Modules.StandardsManagement.ViewModels;

namespace Synthetic.Modules.Worksets.Utilities
{
    /// <summary>
    /// Utility methods for querying and modifying Revit Scope Box elements.
    /// </summary>
    public static class ScopeBoxUtil
    {
        /// <summary>
        /// Retrieves all Scope Boxes from the document.
        /// </summary>
        /// <param name="doc">Revit Document</param>
        /// <returns>List of Scope Box elements</returns>
        public static IList<RevitElem> GetAllScopeBoxes(RevitDoc doc)
        {
            RevitDB.FilteredElementCollector collector = new RevitDB.FilteredElementCollector(doc);
            return collector.OfCategory(RevitDB.BuiltInCategory.OST_VolumeOfInterest).ToElements();
        }

        /// <summary>
        /// Moves a list of Scope Boxes to a specific workset.
        /// </summary>
        /// <param name="doc">Revit Document</param>
        /// <param name="scopeBoxes">List of Scope Boxes</param>
        /// <param name="workset">Destination Workset</param>
        public static void MoveToWorkset(RevitDoc doc, IList<RevitElem> scopeBoxes, RevitDB.Workset workset)
        {
            if (workset == null) return;

            using (RevitDB.Transaction trans = new RevitDB.Transaction(doc))
            {
                trans.Start("Move Scope Boxes to Workset " + workset.Name);
                foreach (RevitElem elem in scopeBoxes)
                {
                    RevitDB.Parameter worksetParam = elem.get_Parameter(RevitDB.BuiltInParameter.ELEM_PARTITION_PARAM);
                    if (worksetParam != null && !worksetParam.IsReadOnly)
                    {
                        worksetParam.Set(workset.Id.IntegerValue);
                    }
                }
                trans.Commit();
            }
        }

        /// <summary>
        /// Finds all views with "Scope Boxes" in their name.
        /// </summary>
        /// <param name="doc">Revit Document</param>
        /// <returns>List of views</returns>
        public static IList<RevitDB.View> GetScopeBoxViews(RevitDoc doc)
        {
            RevitDB.FilteredElementCollector collector = new RevitDB.FilteredElementCollector(doc);
            return collector.OfClass(typeof(RevitDB.View))
                            .Cast<RevitDB.View>()
                            .Where(v => v.Name.IndexOf("Scope Boxes", StringComparison.OrdinalIgnoreCase) >= 0)
                            .ToList();
        }
    }
}
```

### File: Worksets/Utilities/WorksetUtil.cs
```csharp
using Synthetic.Modules.Worksets.Commands;
using Synthetic.Modules.Worksets.Models;
using Synthetic.Modules.Worksets.Utilities;

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Synthetic.Infrastructure.Serialization;
using Synthetic.Infrastructure.IO;

using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.Header;


//Aliases for Revit Classes
using RevitDoc = Autodesk.Revit.DB.Document;
using revitWorkset = Autodesk.Revit.DB.Workset;
using revitWorksetId = Autodesk.Revit.DB.WorksetId;

using Synthetic.Settings;
using Synthetic.Modules.StandardsManagement.ViewModels;
namespace Synthetic.Modules.Worksets.Utilities
{
    /// <summary>
    /// Utilities for dealing with Worksets, WorksetModels and WorksetSettings.
    /// </summary>
    public class WorksetUtil
    {
        /// <summary>
        /// Dictionary of WorksetModels with Workset Name as Key.
        /// </summary>
        public Dictionary<string, WorksetModel> Worksets { get; set; }

        /// <summary>
        /// Constructor that creates an empty WorksetUtil object
        /// </summary>
        public WorksetUtil()
        {
            this.Worksets = new Dictionary<string, WorksetModel>();
        }

        /// <summary>
        /// Creates a list of WorksetModels from Excel cells.
        /// </summary>
        /// <param name="cells">List of List of Excel cells.</param>
        public WorksetUtil(List<List<object>> cells)
        {
            this.Worksets = new Dictionary<string, WorksetModel>();

            foreach (List<object> row in cells)
            {
                List<string> rowData = new List<string>();
                foreach (object item in row)
                {
                     rowData.Add(item?.ToString() ?? string.Empty);
                }

                string? name = null;
                string vis = "TRUE";
                string? alias = null;
                string description = string.Empty;
                
                int itemCount = rowData.Count;
                if (itemCount > 0) { name = rowData[0]; }
                if (itemCount > 1) { vis = rowData[1]; }
                if (itemCount > 2) { alias = rowData[2]; }
                if (itemCount > 3) { description = rowData[3]; }

                if (name != null && name != string.Empty)
                {
                    bool visibility = true;
                    if (vis == "FALSE" || vis == "false" || vis == "False")
                    {
                        visibility = false;
                    }
                    this.Worksets.Add(name, new WorksetModel(name, visibility, alias, description));
                }
            }
        }

        /// <summary>
        /// Adds a Workset to the WorksetUtil object.
        /// </summary>
        /// <param name="workset"></param>
        public WorksetUtil Add (WorksetModel workset)
        {
            Worksets.Add(workset.Name, workset);
            return this;
        }

        /// <summary>
        /// Gets a list of Workset names
        /// </summary>
        /// <returns>List of Workset names as strings</returns>
        public List<string> Names()
        {
            return Worksets.Keys.ToList();
        }

        /// <summary>
        /// Return a string Workset names with line breaks between
        /// </summary>
        /// <returns>Return a string Workset names with line breaks between</returns>
        public string WorksetNames()
        {
            return string.Join("\n", this.Worksets.Keys);
        }

        /// <summary>
        /// Returns a string of Workset visibilities with line breaks between
        /// </summary>
        /// <returns>Returns a string of Workset visibilities with line breaks between</returns>
        public string WorksetVisibilities()
        {
            string visibilities = String.Empty;
            foreach (WorksetModel worksetModel in Worksets.Values)
            {
                visibilities = visibilities + "\n" + worksetModel.Visibility;
            }
            return visibilities;
        }

        /// <summary>
        /// Returns a string of Workset descriptions with line breaks between
        /// </summary>
        /// <returns>Returns a string of Workset descriptions with line breaks between</returns>
        public string WorksetDescriptions()
        {
            string descriptions = String.Empty;
            foreach (WorksetModel worksetModel in Worksets.Values)
            {
                descriptions = descriptions + "\n" + worksetModel.Description;
            }
            return descriptions;
        }

        /// <summary>
        /// Given a list of WorksetModel names, updates the WorksetUtil object to only include those WorksetModels
        /// </summary>
        /// <param name="filters">List of WorksetModel names as strings</param>
        /// <returns>The modified WorksetUtil object.</returns>
        public WorksetUtil Filter(List<string> filters)
        {
            Dictionary<string, WorksetModel> filteredWorksets = new Dictionary<string, WorksetModel>();
            foreach (string name in filters)
            {
                if(this.Worksets.ContainsKey(name))
                {
                    filteredWorksets.Add(name, this.Worksets[name]);
                }
            }
            this.Worksets = filteredWorksets;

            return this;
        }

        /// <summary>
        /// Sets the "Workset Names", "Workset Visibility", and "Workset Descriptions" parameters on the ProjectInfo element.
        /// </summary>
        /// <param name="doc">Revit Document</param>
        /// <returns>Returns the ProjectInfo element</returns>
        public ProjectInfo SetProjectInfo (RevitDoc doc)
        {
            FilteredElementCollector collector = new FilteredElementCollector(doc);
            ProjectInfo projectInfo = (ProjectInfo)collector.OfClass(typeof(ProjectInfo)).FirstElement();
            if (projectInfo != null)
            {
                Parameter paramNames = projectInfo.LookupParameter("Workset Names");
                Parameter paramVisibility = projectInfo.LookupParameter("Workset Visibility");
                Parameter paramDescriptions = projectInfo.LookupParameter("Workset Descriptions");

                using (Autodesk.Revit.DB.Transaction trans = new Autodesk.Revit.DB.Transaction(doc))
                {
                    trans.Start("Set ProjectInfo Workset Descriptions");
                    if (paramNames != null) { paramNames.Set(this.WorksetNames()); }
                    if (paramVisibility != null) { paramVisibility.Set(this.WorksetVisibilities()); }
                    if (paramDescriptions != null) { paramDescriptions.Set(this.WorksetDescriptions()); }
                    trans.Commit();
                }
            }
            return projectInfo!;
        }

        /// <summary>
        /// Creates Worksets from a WorksetUtil object.  Worksets that already exist are modified.  Worksets are renamed if they have Aliases.
        /// </summary>
        /// <param name="doc">Revit Document to create Worksets in</param>
        /// <param name="worksets">WorksetUtil object that includes a collection of Worksets with names, visibility, aliases and descriptions.</param>
        /// <returns>Dictionary with Workset Names as keys and Revit Workset objects as values.</returns>
        public static Dictionary<string, revitWorkset>? Create (RevitDoc doc, WorksetUtil worksets)
        {
            return _create(doc, worksets);
        }

        /// <summary>
        /// Creates a Workset given a Revit Document and workset information.  Will modify an existing Workset.  Worksets named as the alias, will be renamed.
        /// </summary>
        /// <param name="doc">Revit Document to make the workset in.</param>
        /// <param name="name">Name of the Workset as a string</param>
        /// <param name="visible">Whether a visibility is set to true or false.</param>
        /// <param name="alias">Aliases to be renamed to this workset</param>
        /// <param name="description">Description of the workset.</param>
        /// <returns></returns>
        public static Dictionary<string, revitWorkset>? Create (RevitDoc doc, string name, bool visible = true, string alias = "", string description = "")
        {
            WorksetUtil worksets = new WorksetUtil();
            worksets.Add(new WorksetModel(name, visible, alias, description));

            return _create(doc, worksets);
        }

        private static Dictionary<string, revitWorkset>? _create (RevitDoc doc, WorksetUtil wksets)
        {
            Dictionary<string, revitWorkset> importedWorksets = new Dictionary<string, revitWorkset> ();

            using (Autodesk.Revit.DB.Transaction trans = new Autodesk.Revit.DB.Transaction(doc))
            {
                trans.Start("Create Worksets");
                foreach (WorksetModel wkset in wksets.Worksets.Values)
                {
                    //Only create workset if it's name isn't an empty string
                    if (wkset.Name != null && wkset.Name != "")
                    {
                        // Verify that each workset isn't already in the document
                        // If the workset is unique, either check if the alias exists or create a new workset.
                        if (WorksetTable.IsWorksetNameUnique(doc, wkset.Name))
                        {
                            //If the alias is already in the document, rename the alias
                            if (wkset.Alias != null && WorksetTable.IsWorksetNameUnique(doc, wkset.Alias) == false)
                            {
                                revitWorkset? workset = WorksetUtil.GetByName(wkset.Alias, doc);
                                if (workset != null)
                                {
                                    WorksetTable.RenameWorkset(doc, workset.Id, wkset.Name);
                                    importedWorksets.Add(wkset.Name, workset);
                                }
                            }
                            // Otherwise, create a new workset.
                            else
                            {
                                revitWorkset workset = revitWorkset.Create(doc, wkset.Name);

                                // Set the workset’s default visibility      
                                WorksetDefaultVisibilitySettings defaultVisibility = WorksetDefaultVisibilitySettings.GetWorksetDefaultVisibilitySettings(doc);
                                defaultVisibility.SetWorksetVisibility(workset.Id, wkset.Visibility);
                                importedWorksets.Add(wkset.Name, workset);
                            }
                        }
                    }
                    // If the workset is already in the document, retrieve the workset
                    else if (wkset.Name != null)
                    {
                        revitWorkset? workset = GetByName(wkset.Name, doc);
                        if (workset != null)
                        {
                            importedWorksets.Add(wkset.Name, workset);
                        }
                    }
                }
                trans.Commit();
            }

            if (importedWorksets.Count > 0)
            {
                return importedWorksets;
            }
            else 
            {
                return null;
            }
        }

        /// <summary>
        /// Retrieves the workset with the given name.
        /// </summary>
        /// <param name="name">A workset name</param>
        /// <param name="doc">The Revit document</param>
        /// <returns name="workset">Returns a workset.  Returns null if workset does not exist.</returns>
        public static revitWorkset? GetByName (string name, RevitDoc doc)
        {
            revitWorkset? foundWorkset = null;

            if (name != null)
            {
                FilteredWorksetCollector fwCollector = new FilteredWorksetCollector(doc);

                foreach (revitWorkset workset in fwCollector)
                {
                    if (workset.Name == name)
                    {
                        foundWorkset = workset;
                    }
                }
                fwCollector.Dispose();
            }
            return foundWorkset;
        }

        /// <summary>
        /// Retrieves the workset with the given WorksetId.
        /// </summary>
        /// <param name="worksetId">The workset ID</param>
        /// <param name="doc">A Revit document</param>
        /// <returns name="workset">Returns a workset.  Returns null if workset does not exist.</returns>
        public static revitWorkset GetByWorksetId (WorksetId worksetId, RevitDoc doc)
        {
            return doc.GetWorksetTable().GetWorkset(worksetId);
        }

        /// <summary>
        /// Retrieves the workset with the given Workset's UniqueId.
        /// </summary>
        /// <param name="worksetUniqueId">The GUID of the workset</param>
        /// <param name="doc">A Revit document</param>
        /// <returns name="workset">Returns a workset.  Returns null if workset does not exist.</returns>
        public static revitWorkset GetByWorksetUniqueId(System.Guid worksetUniqueId, RevitDoc doc)
        {
            return doc.GetWorksetTable().GetWorkset(worksetUniqueId);
        }

        /// <summary>
        /// Retrieves all elements belonging to a specific workset in the document.
        /// </summary>
        /// <param name="workset">The workset to query.</param>
        /// <param name="document">The Revit document.</param>
        /// <returns>A list of elements on the specified workset.</returns>
        public static IList<Element> GetElementsOnWorkset(Workset workset, RevitDoc document)
        {
            // filter all elements that belong to the given workset
            FilteredElementCollector elementCollector = new FilteredElementCollector(document);
            ElementWorksetFilter elementWorksetFilter = new ElementWorksetFilter(workset.Id, false);
            IList<Element> elements = elementCollector.WherePasses(elementWorksetFilter).ToElements();

            return elements;
        }

        /// <summary>
        /// Retrieves all the user worksets from a document.  Excludes view and family worksets.
        /// </summary>
        /// <returns name="worksets">Returns all user worksets in the document.</returns>
        public static List<revitWorkset> GetUserWorksets (RevitDoc doc)
        {
            FilteredWorksetCollector fwCollector = new FilteredWorksetCollector(doc);
            List<revitWorkset> worksets = new List<revitWorkset>();

            foreach (revitWorkset workset in fwCollector.OfKind(WorksetKind.UserWorkset))
            {
                worksets.Add(workset);
            }
            return worksets;
        }

        /// <summary>
        /// Renames a workset.
        /// </summary>
        /// <param name="workset">A workset</param>
        /// <param name="name">A workset name</param>
        /// <param name="doc">The Revit document</param>
        /// <returns name="workset">renamed workeset.</returns>
        /// <returns name="renamed">renamed workeset.</returns>
        public static revitWorkset? Rename (revitWorkset workset, string name, RevitDoc doc)
        {
            revitWorkset? renamedWorkset = null;

            if (name != null && workset != null)
            {
                //Verify that the existing workset is in the document.
                //If the workset is unique, create it
                if (WorksetTable.IsWorksetNameUnique(doc, workset.Name) == false)
                {
                    // Verify that the new name doesn't already exist
                    if (WorksetTable.IsWorksetNameUnique(doc, name) == true)
                    {
                        //Only rename workset if it's name isn't an empty string
                        if (name != "")
                        {
                            using (Autodesk.Revit.DB.Transaction trans = new Autodesk.Revit.DB.Transaction(doc))
                            {
                                trans.Start("Rename Worksets");

                                WorksetTable.RenameWorkset(doc, workset.Id, name);
                                renamedWorkset = workset;

                                trans.Commit();
                            }
                        }
                    }
                }
            }
            return renamedWorkset;
        }

        /// <summary>
        /// Sets the Default Visibility of a workset within a document.
        /// </summary>
        /// <param name="workset">The workset that you wish to set the visibility of.</param>
        /// <param name="visible">The visibility of the workset</param>
        /// <param name="doc">The Revit document</param>
        /// <returns name="workset">A Revit workset</returns>
        public static revitWorkset SetDefaultVisibility (revitWorkset workset, bool visible, RevitDoc doc)
        {
            using (Autodesk.Revit.DB.Transaction trans = new Autodesk.Revit.DB.Transaction(doc))
            {
                trans.Start("Set " + workset.Name + " Workset Default Visibility");

                // Set the workset’s default visibility      
                WorksetDefaultVisibilitySettings defaultVisibility = WorksetDefaultVisibilitySettings.GetWorksetDefaultVisibilitySettings(doc);
                defaultVisibility.SetWorksetVisibility(workset.Id, visible);

                trans.Commit();
                defaultVisibility.Dispose();
            }
            return workset;
        }
    }
}
```

### File: BatchPrint/Commands/PrintBatchMultiDoc.cs
```csharp
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using View = Autodesk.Revit.DB.View;
using Autodesk.Revit.UI;
using Synthetic.Modules.BatchPrint.Commands;
using Synthetic.Modules.BatchPrint.Utilities;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using Autodesk.Revit.DB.Events;

using Synthetic.Shared.UI;
using Synthetic.Core;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;
using Synthetic.Shared.RevitAPI;

namespace Synthetic.Modules.BatchPrint.Commands
{
    /// <summary>
    /// Converts Drafting Views to Legends
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class PrintBatchMultiDoc : IExternalCommand
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

            ElementId elemIdActive = new FilteredElementCollector(doc, doc.ActiveView.Id).FirstElementId();

            DocumentSet documents = uiapp.Application.Documents;
            List<Document> selectedDocuments = SelectDocuments(documents, uiapp.MainWindowHandle);

            FilteredElementCollector coll = new FilteredElementCollector(doc).OfClass(typeof(ViewSheetSet));
            List<ViewSheetSet> viewSets = coll.Cast<ViewSheetSet>().ToList();

            ViewSheetSet? selectedViewSheetSet = SelectViewSheetSet(viewSets, uiapp.MainWindowHandle);
            string? viewSheetSetName = null;
            if(selectedViewSheetSet != null)
            {
                viewSheetSetName = selectedViewSheetSet.Name;
            }

            IList<ElementId> settingIds = (IList<ElementId>)doc.GetPrintSettingIds();
            List<PrintSetting> printSettings = new FilteredElementCollector(doc, settingIds)
                .OfClass(typeof(PrintSetting))
                .Cast<PrintSetting>()
                .ToList();
            PrintSetting? selectedPrintSetting = SelectPrintSetting(printSettings, uiapp.MainWindowHandle);
            string? printSettingSetName = null;
            if(selectedPrintSetting != null)
            {
                printSettingSetName = selectedPrintSetting.Name;
            }

            string? printPath = selectFolderPath(uiapp.MainWindowHandle);

            BBPrinterSettingsUtils defaultBB = new BBPrinterSettingsUtils();

            if (selectedDocuments != null && selectedDocuments.Count > 0 && selectedViewSheetSet != null && selectedPrintSetting != null && !string.IsNullOrEmpty(printPath))
            {
                BBPrinterSettingsUtils newBB = new BBPrinterSettingsUtils("0", "0", printPath!);
                // Set the Bluebeam Printer Settings before printing.
                newBB.SetRegistryKeys();

                foreach (Document document in selectedDocuments)
                {
                    string? docTitle = document.Title;
                    
                    ViewSheetSet? viewSheetSet = new FilteredElementCollector(document)
                        .OfClass(typeof(ViewSheetSet))
                        .OfType<ViewSheetSet>()
                        .FirstOrDefault(v => v.Name == viewSheetSetName);

                    IList<ElementId>? nextSettingIds = (IList<ElementId>)document.GetPrintSettingIds();
                    List<PrintSetting>? ps = new FilteredElementCollector(document, nextSettingIds)
                        .OfClass(typeof(PrintSetting))
                        .Cast<PrintSetting>()
                        .ToList();
                    PrintSetting? printSettting = ps.OfType<PrintSetting>().FirstOrDefault(p => p.Name == printSettingSetName);

                    Document? currentDoc = document;
                    if (document.IsLinked)
                    {
                        WorksetConfiguration configuration = new WorksetConfiguration(WorksetConfigurationOption.OpenAllWorksets);
                        OpenOptions openOptions = new OpenOptions();
                        openOptions.SetOpenWorksetsConfiguration(configuration);
                        openOptions.AllowOpeningLocalByWrongUser = true;

                        if (App.AppControlled != null)
                        {
                            App.AppControlled.ControlledApplication.FailuresProcessing += 
                                 new EventHandler<Autodesk.Revit.DB.Events.FailuresProcessingEventArgs>
                                 (ResolveWarnings);
                        }

                        if (document.IsModelInCloud)
                        {
                            OpenFromCloudCallback cloudCallback = new OpenFromCloudCallback();
                            currentDoc = uiapp.Application.OpenDocumentFile(document.GetCloudModelPath(), openOptions, cloudCallback);
                        }
                        else if (document.IsWorkshared)
                        {
                            openOptions.DetachFromCentralOption = DetachFromCentralOption.DetachAndPreserveWorksets;
                            currentDoc = uiapp.Application.OpenDocumentFile(document.GetWorksharingCentralModelPath(), openOptions);
                        }
                        else
                        {
                            currentDoc = uiapp.Application.OpenDocumentFile(document.PathName);
                        }

                        if (App.AppControlled != null)
                        {
                            App.AppControlled.ControlledApplication.FailuresProcessing -=
                                 new EventHandler<Autodesk.Revit.DB.Events.FailuresProcessingEventArgs>
                                 (ResolveWarnings);
                        }
                    }

                    if (viewSheetSet != null && printSettting != null)
                    {
                        PrintManager printManager = currentDoc.PrintManager;
                        printManager.SelectNewPrintDriver("Bluebeam PDF");
                        printManager.PrintSetup.CurrentPrintSetting = printSettting;
                        printManager.PrintRange = PrintRange.Select;
                        printManager.ViewSheetSetting.CurrentViewSheetSet = viewSheetSet;
                        printManager.CombinedFile = true;
                        printManager.PrintToFile = true;
                        string oldPrintToFileName = printManager.PrintToFileName;
                        string fileName = currentDoc.Title + ".pdf";
                        string printToFileName = Path.Combine(
                            printPath,
                            fileName);
                        printManager.PrintToFileName = printToFileName;
                        printManager.Apply();

                        if (currentDoc != null)
                        {
                            if (File.Exists(printToFileName))
                            {
                                File.Delete(printToFileName);
                            }
                            printManager.SubmitPrint();

                            if (currentDoc.Title != doc.Title)
                            {
                                currentDoc.Close(false);
                            }
                        }
                    }

                    // Reset
                    docTitle = null;
                    viewSheetSet = null;
                    nextSettingIds = null;
                    ps = null;
                    printSettting = null;
                    currentDoc = null;
                }
            }

            // Reset Bluebeam Printer settings back to what they were.
            defaultBB.SetRegistryKeys();

            return Result.Succeeded;
        }

        private void ResolveWarnings(object? sender, FailuresProcessingEventArgs e)
        {
            FailuresAccessor fa = e.GetFailuresAccessor();
            IList<FailureMessageAccessor> failList = new List<FailureMessageAccessor>();
            failList = fa.GetFailureMessages(); // Inside event handler, get all warnings

            if (failList.Count == 0)
            {
                e.SetProcessingResult(FailureProcessingResult.Continue);
                return;
            }

            foreach (FailureMessageAccessor failure in failList)
            {
                FailureDefinitionId failID = failure.GetFailureDefinitionId();
                if (failID == BuiltInFailures.RoomFailures.RoomNotEnclosed)
                {
                    fa.DeleteWarning(failure);
                }
            }
            e.SetProcessingResult(FailureProcessingResult.ProceedWithCommit);

            return;
        }

        internal string? selectFolderPath (IntPtr ownerHandle)
        {
            return FileDialogHelper.SelectFolder(ownerHandle, "Select Print Output Folder");
        }

        internal List<Document> SelectDocuments(DocumentSet docs, IntPtr mainWindowHandle)
        {
            List<string> itemList = new List<string>();
            List<Document> docList = new List<Document>();
            List<Document> selectedDocs = new List<Document>();

            ListByCheckboxViewModel viewModel = new ListByCheckboxViewModel();
            viewModel.Title = "Select Documents";
            viewModel.Instruction = "Select the Documents to print sheets from";
            viewModel.IsSingleSelection = false;

            if (!docs.IsEmpty)
            {
                foreach (Document doc in docs)
                {
                    itemList.Add(doc.Title);
                    docList.Add(doc);
                }
                viewModel.SetItems(itemList, false);

                ListByCheckboxView dialog = new ListByCheckboxView(mainWindowHandle) { DataContext = viewModel };
                bool? dResult = dialog.ShowDialog();

                if (dResult == true)
                {
                    List<string> selectedList = viewModel.CheckedItems;
                    foreach (string selectedItem in selectedList)
                    {
                        Document d = docList.First(s => s.Title == selectedItem);
                        selectedDocs.Add(d);
                    }
                }
            }
            return selectedDocs;
        }

        internal ViewSheetSet? SelectViewSheetSet(List<ViewSheetSet> viewSheetSet, IntPtr mainWindowHandle)
        {
            List<string> itemList = new List<string>();
            ViewSheetSet? selectedViewSheet = null;

            ListByCheckboxViewModel viewModel = new ListByCheckboxViewModel();
            viewModel.Title = "Select a Print List";
            viewModel.Instruction = "The name of the selected print list will be used in each document";
            viewModel.IsSingleSelection = true;

            foreach (ViewSheetSet viewSet in viewSheetSet)
            {
                itemList.Add(viewSet.Name);
            }
            viewModel.SetItems(itemList, false);

            ListByCheckboxView dialog = new ListByCheckboxView(mainWindowHandle) { DataContext = viewModel };
            bool? dResult = dialog.ShowDialog();

            if (dResult == true && viewModel.CheckedItems.Count > 0)
            {
                string selectedName = viewModel.CheckedItems[0];
                selectedViewSheet = viewSheetSet.FirstOrDefault(s => s.Name == selectedName);
            }
            return selectedViewSheet;
        }

        internal PrintSetting? SelectPrintSetting(List<PrintSetting> printSettings, IntPtr mainWindowHandle)
        {
            List<string> itemList = new List<string>();
            PrintSetting? selectedPrintSetting = null;

            ListByCheckboxViewModel viewModel = new ListByCheckboxViewModel();
            viewModel.Title = "Select a Print Setting";
            viewModel.Instruction = "Select a print setting to use for all prints.  This setting must be in every document.";
            viewModel.IsSingleSelection = true;

            foreach (PrintSetting setting in printSettings)
            {
                itemList.Add(setting.Name);
            }
            viewModel.SetItems(itemList, false);

            ListByCheckboxView dialog = new ListByCheckboxView(mainWindowHandle) { DataContext = viewModel };
            bool? dResult = dialog.ShowDialog();

            if (dResult == true && viewModel.CheckedItems.Count > 0)
            {
                string selectedName = viewModel.CheckedItems[0];
                selectedPrintSetting = printSettings.FirstOrDefault(s => s.Name == selectedName);
            }
            return selectedPrintSetting;
        }
    }

    class OpenFromCloudCallback : IOpenFromCloudCallback
    {
        public OpenConflictResult OnOpenConflict(OpenConflictScenario scenario)
        {
            return OpenConflictResult.DetachFromCentral;
        }
    }
}
```

### File: BatchPrint/Utilities/BBPrinterSettingsUtils.cs
```csharp
using Synthetic.Modules.BatchPrint.Commands;
using Synthetic.Modules.BatchPrint.Utilities;

using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Win32;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;
using Synthetic.Modules.StandardsManagement.ViewModels;

namespace Synthetic.Modules.BatchPrint.Utilities
{
    /// <summary>
    /// Utilities for setting BlueBeam Printer settings
    /// </summary>
    public class BBPrinterSettingsUtils
    {
        /// <summary>
        /// The BlueBeam Printer registry path.
        /// </summary>
        public string BBPrinterRegistryPath =
            "HKEY_CURRENT_USER\\SOFTWARE\\Bluebeam Software\\21\\Brewery\\V45\\Printer Driver";

        /// <summary>
        /// Gets or sets the value specifying whether to open in viewer.
        /// </summary>
        public string OpenInViewer;

        /// <summary>
        /// Gets or sets the value specifying whether to prompt for file name.
        /// </summary>
        public string PromptForFileName;

        /// <summary>
        /// Gets or sets the projects folder path.
        /// </summary>
        public string ProjectsFolder;

        /// <summary>
        /// Gets or sets the save as folder path.
        /// </summary>
        public string SaveAsFolder;

        /// <summary>
        /// Gets or sets the value specifying whether to use the last folder.
        /// </summary>
        public string UseLastFolder;

        /// <summary>
        /// Initializes a new instance of the <see cref="BBPrinterSettingsUtils"/> class by reading values from the registry.
        /// </summary>
        public BBPrinterSettingsUtils ()
        {
            this.OpenInViewer = Registry.GetValue(BBPrinterRegistryPath, "OpenInViewer", "1") as string ?? "1";
            this.PromptForFileName = Registry.GetValue(BBPrinterRegistryPath, "PromptForFileName", "1") as string ?? "1";
            this.ProjectsFolder = Registry.GetValue(BBPrinterRegistryPath, "ProjectsFolder", 
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)) as string ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            this.SaveAsFolder = Registry.GetValue(BBPrinterRegistryPath, "SaveAsFolder", 
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)) as string ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            this.UseLastFolder = Registry.GetValue(BBPrinterRegistryPath, "UseLastFolder", "0") as string ?? "0";
        }
        
        /// <summary>
        /// Initializes a new instance of the <see cref="BBPrinterSettingsUtils"/> class with custom settings.
        /// </summary>
        /// <param name="openInViewer">Specifies whether to open in viewer.</param>
        /// <param name="promptForFileName">Specifies whether to prompt for file name.</param>
        /// <param name="path">The folder path for projects and save locations.</param>
        public BBPrinterSettingsUtils (string openInViewer, string promptForFileName, string path)
        {
            this.OpenInViewer = openInViewer;
            this.PromptForFileName = promptForFileName;
            this.ProjectsFolder = path;
            this.SaveAsFolder = path;
            this.UseLastFolder = "2";
        }

        /// <summary>
        /// Sets registry keys according to the current property values.
        /// </summary>
        public void SetRegistryKeys ()
        {
            Registry.SetValue(BBPrinterRegistryPath, "OpenInViewer", this.OpenInViewer);
            Registry.SetValue(BBPrinterRegistryPath, "PromptForFileName", this.PromptForFileName);
            Registry.SetValue(BBPrinterRegistryPath, "ProjectsFolder", this.ProjectsFolder);
            Registry.SetValue(BBPrinterRegistryPath, "SaveAsFolder", this.SaveAsFolder);
            Registry.SetValue(BBPrinterRegistryPath, "UseLastFolder", this.UseLastFolder);
        }

        /// <summary>
        /// Sets registry keys to their default values.
        /// </summary>
        public void SetDefaultRegistryKeys()
        {
            Registry.SetValue(BBPrinterRegistryPath, "OpenInViewer", "1");
            Registry.SetValue(BBPrinterRegistryPath, "PromptForFileName", "1");
            Registry.SetValue(BBPrinterRegistryPath, "ProjectsFolder", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
            Registry.SetValue(BBPrinterRegistryPath, "SaveAsFolder", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
            Registry.SetValue(BBPrinterRegistryPath, "UseLastFolder", "0");
        }
    }
}
```

### File: SettingsDashboard/Commands/SettingsDashboardCommand.cs
```csharp
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
```

### File: SettingsDashboard/Handlers/SyncExternalEventHandler.cs
```csharp
using System;
using System.IO;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using Synthetic.Settings;
using Synthetic.Infrastructure.Persistence;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;
using Synthetic.Modules.SettingsDashboard.Handlers;

namespace Synthetic.Modules.SettingsDashboard.Handlers
{
    /// <summary>
    /// Request types supported by the SyncExternalEventHandler.
    /// </summary>
    public enum SyncRequestType
    {
        /// <summary>
        /// No request queued.
        /// </summary>
        None,
        
        /// <summary>
        /// Pull settings from file into document.
        /// </summary>
        Pull,
        
        /// <summary>
        /// Push settings from document into file.
        /// </summary>
        Push
    }

    /// <summary>
    /// External event handler to marshal settings pull/push operations from modeless UI to the Revit API thread.
    /// </summary>
    public class SyncExternalEventHandler : IExternalEventHandler
    {
        private readonly object _lock = new object();
        private SyncRequestType _requestType = SyncRequestType.None;
        private Document? _doc;
        private string? _filePath;

        /// <summary>
        /// Queues a sync request to be executed on the Revit API thread.
        /// </summary>
        public void QueueRequest(SyncRequestType requestType, Document doc, string filePath)
        {
            lock (_lock)
            {
                _requestType = requestType;
                _doc = doc;
                _filePath = filePath;
            }

        }

        /// <summary>
        /// Executes the queued sync request on the Revit API thread.
        /// </summary>
        public void Execute(UIApplication app)
        {
            SyncRequestType currentRequest;
            Document? currentDoc;
            string? currentFilePath;

            lock (_lock)
            {
                currentRequest = _requestType;
                currentDoc = _doc;
                currentFilePath = _filePath;

                // Reset state
                _requestType = SyncRequestType.None;
                _doc = null;
                _filePath = null;
            }

            if (currentDoc == null || string.IsNullOrEmpty(currentFilePath) || currentRequest == SyncRequestType.None)
            {
                return;
            }


            if (currentRequest == SyncRequestType.Pull)
            {
                try
                {
                    if (!File.Exists(currentFilePath))
                    {
                        Autodesk.Revit.UI.TaskDialog.Show("Pull Error", "The linked settings file no longer exists.");
                        return;
                    }

                    string jsonText = File.ReadAllText(currentFilePath);
                    var dict = JsonConvert.DeserializeObject<System.Collections.Generic.Dictionary<string, Newtonsoft.Json.Linq.JToken>>(jsonText);

                    if (dict == null)
                    {
                        Autodesk.Revit.UI.TaskDialog.Show("Pull Error", "Failed to deserialize settings from the linked file.");
                        return;
                    }

                    using (var trans = new Transaction(currentDoc, "Pull Settings from File"))
                    {
                        trans.Start();

                        if (dict.TryGetValue(WorksetSettings.Name, out var worksetToken))
                        {
                            var settings = worksetToken.ToObject<WorksetSettings>();
                            if (settings != null) SettingsManager.Save(currentDoc, settings);
                        }
                        if (dict.TryGetValue(ViewAutoNumSettings.Name, out var viewAutoNumToken))
                        {
                            var settings = viewAutoNumToken.ToObject<ViewAutoNumSettings>();
                            if (settings != null) SettingsManager.Save(currentDoc, settings);
                        }
                        if (dict.TryGetValue(MaterialLibrarySettings.Name, out var materialLibToken))
                        {
                            var settings = materialLibToken.ToObject<MaterialLibrarySettings>();
                            if (settings != null) SettingsManager.Save(currentDoc, settings);
                        }
                        if (dict.TryGetValue(ProjectMaterialSettings.Name, out var projectMatToken))
                        {
                            var settings = projectMatToken.ToObject<ProjectMaterialSettings>();
                            if (settings != null) SettingsManager.Save(currentDoc, settings);
                        }

                        trans.Commit();
                    }


                    Autodesk.Revit.UI.TaskDialog.Show("Settings Synced", "Successfully pulled configuration settings into the project.");
                }
                catch (Exception ex)
                {

                    Autodesk.Revit.UI.TaskDialog.Show("Pull Error", $"Failed to pull settings: {ex.Message}");
                }
            }
            else if (currentRequest == SyncRequestType.Push)
            {
                try
                {
                    var docSettings = new System.Collections.Generic.Dictionary<string, object>
                    {
                        { WorksetSettings.Name, SettingsManager.Get<WorksetSettings>(currentDoc) },
                        { ViewAutoNumSettings.Name, SettingsManager.Get<ViewAutoNumSettings>(currentDoc) },
                        { MaterialLibrarySettings.Name, SettingsManager.Get<MaterialLibrarySettings>(currentDoc) },
                        { ProjectMaterialSettings.Name, SettingsManager.Get<ProjectMaterialSettings>(currentDoc) }
                    };

                    // Maintain the linked file path in the file as well
                    var syncSettings = SettingsManager.Get<SyncSettings>(currentDoc);
                    docSettings.Add(SyncSettings.Name, syncSettings);

                    string jsonText = JsonConvert.SerializeObject(docSettings, Formatting.Indented);
                    File.WriteAllText(currentFilePath, jsonText);


                    Autodesk.Revit.UI.TaskDialog.Show("Settings Synced", "Successfully pushed project settings to the linked configuration file.");
                }
                catch (Exception ex)
                {

                    Autodesk.Revit.UI.TaskDialog.Show("Push Error", $"Failed to push settings: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Gets the name of this external event handler.
        /// </summary>
        public string GetName()
        {
            return "Synthetic Settings Synchronization External Event Handler";
        }
    }
}
```

### File: SettingsDashboard/ViewModels/FileUtilitySettingsViewModel.cs
```csharp
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Input;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using Synthetic.Settings;
using Synthetic.Infrastructure.Persistence;

using Synthetic.Shared.UI;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.Modules.SettingsDashboard.Views;
namespace Synthetic.Modules.SettingsDashboard.ViewModels
{
    /// <summary>
    /// ViewModel for managing File Utility and Network Path configuration settings in the Dashboard.
    /// </summary>
    public class FileUtilitySettingsViewModel : ViewModelBase, ISettingModuleViewModel
    {
        private readonly Document _doc;
        private readonly IntPtr _mainWindowHandle;
        private FileUtilitySettings _settings;
        private bool _isOverridden;
        private bool _isDirty;

        /// <inheritdoc/>
        public string ModuleName => "Firm Network Paths";

        /// <inheritdoc/>
        public bool IsDirty
        {
            get => _isDirty;
            set
            {
                if (SetProperty(ref _isDirty, value))
                {
                    System.Windows.Input.CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        /// <inheritdoc/>
        public bool IsOverridden
        {
            get => _isOverridden;
            set
            {
                if (SetProperty(ref _isOverridden, value))
                {
                    IsDirty = true;
                    OnPropertyChanged(nameof(SummaryText));
                    OnPropertyChanged(nameof(IsValid));
                }
            }
        }

        /// <inheritdoc/>
        public bool IsValid
        {
            get
            {
                if (!IsOverridden) return true;
                return _settings != null && _settings.IsValid(_doc);
            }
        }

        /// <inheritdoc/>
        public string SummaryText
        {
            get
            {
                FileUtilitySettings displaySettings;
                string prefix;

                if (!IsOverridden)
                {
                    prefix = "Using Firmwide Defaults:\n\n";
                    // Attempt to load firm settings from SyntheticSettings.json or fallback defaults
                    displaySettings = new FileUtilitySettings().Defaults();
                    try
                    {
                        string defaultPath = Path.Combine(Config.addinPath, "SyntheticSettings.json");
                        if (File.Exists(defaultPath))
                        {
                            Config? defaultConfig = Config.ReadFromFile(defaultPath);
                            if (defaultConfig != null && defaultConfig.Contains(FileUtilitySettings.Name))
                            {
                                displaySettings = defaultConfig.GetSettings<FileUtilitySettings>(FileUtilitySettings.Name);
                            }
                        }
                    }
                    catch { }
                }
                else
                {
                    prefix = "Overridden for Project:\n\n";
                    displaySettings = _settings;
                }

                if (displaySettings == null)
                {
                    return prefix + "(No settings configured)";
                }

                var sb = new StringBuilder(prefix);
                sb.AppendLine("Archive Directories:");
                if (displaySettings.ArchiveDirectories != null && displaySettings.ArchiveDirectories.Count > 0)
                {
                    foreach (var dir in displaySettings.ArchiveDirectories)
                    {
                        sb.AppendLine($"  - {dir}");
                    }
                }
                else
                {
                    sb.AppendLine("  (None)");
                }

                sb.AppendLine("\nAlternate Paths:");
                if (displaySettings.AlternatePaths != null && displaySettings.AlternatePaths.Count > 0)
                {
                    foreach (var kvp in displaySettings.AlternatePaths)
                    {
                        string mapped = kvp.Value != null && kvp.Value.Count > 0 ? kvp.Value[0] : string.Empty;
                        sb.AppendLine($"  - {kvp.Key} -> {mapped}");
                    }
                }
                else
                {
                    sb.AppendLine("  (None)");
                }

                return sb.ToString();
            }
        }

        /// <inheritdoc/>
        public ICommand ConfigureCommand { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="FileUtilitySettingsViewModel"/> class.
        /// </summary>
        /// <param name="doc">The Revit Document context.</param>
        /// <param name="mainWindowHandle">The parent main window handle.</param>
        public FileUtilitySettingsViewModel(Document doc, IntPtr mainWindowHandle)
        {
            _doc = doc ?? throw new ArgumentNullException(nameof(doc));
            _mainWindowHandle = mainWindowHandle;

            // Load settings using SettingsManager
            _settings = SettingsManager.Get<FileUtilitySettings>(_doc);

            var dataStorages = new FilteredElementCollector(_doc)
                .OfClass(typeof(DataStorage))
                .Cast<DataStorage>()
                .ToList();
            string storageName = "Synthetic_" + FileUtilitySettings.Name;
            _isOverridden = dataStorages.Any(ds => ds.Name.Equals(storageName, StringComparison.OrdinalIgnoreCase));

            ConfigureCommand = new RelayCommand(OnConfigure, _ => IsOverridden);
        }

        private void OnConfigure(object parameter)
        {
            // Draft vs Commit pattern: Clone settings before opening wizard
            var draftSettings = new FileUtilitySettings
            {
                ArchiveDirectories = _settings.ArchiveDirectories != null ? _settings.ArchiveDirectories.ToList() : new List<string>(),
                AlternatePaths = _settings.AlternatePaths != null 
                    ? _settings.AlternatePaths.ToDictionary(kvp => kvp.Key, kvp => kvp.Value != null ? kvp.Value.ToList() : new List<string>())
                    : new Dictionary<string, List<string>>()
            };

            var wizardVM = new NetworkPathsWizardViewModel(draftSettings, _mainWindowHandle);
            var wizardWindow = new NetworkPathsWizardWindow(_mainWindowHandle)
            {
                DataContext = wizardVM
            };

            var dialogResult = wizardWindow.ShowDialog();

            if (dialogResult == true)
            {
                // Commit settings from wizard
                _settings.ArchiveDirectories = wizardVM.ArchiveDirectories.ToList();
                _settings.AlternatePaths = wizardVM.AlternateMappings.ToDictionary(
                    m => m.OriginalServerPath,
                    m => new List<string> { m.LocalMappedPath }
                );

                IsDirty = true;
                OnPropertyChanged(nameof(SummaryText));
                OnPropertyChanged(nameof(IsValid));
            }
        }

        /// <inheritdoc/>
        public void Save()
        {
            if (IsOverridden)
            {
                SettingsManager.Save(_doc, _settings);
            }
            else
            {
                SettingsManager.Delete(_doc, FileUtilitySettings.Name);
            }
        }

        /// <inheritdoc/>
        public void Reload()
        {
            _settings = SettingsManager.Get<FileUtilitySettings>(_doc);

            var dataStorages = new FilteredElementCollector(_doc)
                .OfClass(typeof(DataStorage))
                .Cast<DataStorage>()
                .ToList();
            string storageName = "Synthetic_" + FileUtilitySettings.Name;
            _isOverridden = dataStorages.Any(ds => ds.Name.Equals(storageName, StringComparison.OrdinalIgnoreCase));
            _isDirty = false;

            OnPropertyChanged(nameof(IsOverridden));
            OnPropertyChanged(nameof(SummaryText));
            OnPropertyChanged(nameof(IsValid));
        }
    }
}
```

### File: SettingsDashboard/ViewModels/ISettingModuleViewModel.cs
```csharp
using System;
using System.ComponentModel;
using System.Windows.Input;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;

namespace Synthetic.Modules.SettingsDashboard.ViewModels
{
    /// <summary>
    /// Contract defining the properties and behaviors for settings module view models.
    /// </summary>
    public interface ISettingModuleViewModel : INotifyPropertyChanged
    {
        /// <summary>
        /// Gets the name of the settings module for display in the sidebar.
        /// </summary>
        string ModuleName { get; }

        /// <summary>
        /// Gets or sets a value indicating whether firm settings are overridden for the project.
        /// </summary>
        bool IsOverridden { get; set; }

        /// <summary>
        /// Gets a value indicating whether the settings configured for this module are valid.
        /// </summary>
        bool IsValid { get; }

        /// <summary>
        /// Gets a read-only text summary of the current settings.
        /// </summary>
        string SummaryText { get; }

        /// <summary>
        /// Gets the command that launches the module's configuration wizard window.
        /// </summary>
        ICommand? ConfigureCommand { get; }

        /// <summary>
        /// Saves or updates the settings in the project document or deletes them if overrides are disabled.
        /// </summary>
        void Save();

        /// <summary>
        /// Reloads the settings state from the Revit document database.
        /// </summary>
        void Reload();

        /// <summary>
        /// Gets or sets a value indicating whether the settings module has unsaved changes.
        /// </summary>
        bool IsDirty { get; set; }
    }
}
```

### File: SettingsDashboard/ViewModels/MaterialLibraryViewModel.cs
```csharp
using System;
using System.IO;
using System.Linq;
using System.Windows.Input;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using Synthetic.Settings;
using Synthetic.Infrastructure.Persistence;

using Synthetic.Shared.UI;
using Synthetic.Core;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.Shared.RevitAPI;

namespace Synthetic.Modules.SettingsDashboard.ViewModels
{
    /// <summary>
    /// ViewModel for managing Material Library configuration settings.
    /// </summary>
    public class MaterialLibraryViewModel : ViewModelBase, ISettingModuleViewModel
    {
        private readonly Document _doc;
        private readonly IntPtr _mainWindowHandle;
        private MaterialLibrarySettings _settings;
        private bool _isOverridden;
        private bool _isDirty;

        /// <inheritdoc/>
        public string ModuleName => "Material Library";

        /// <inheritdoc/>
        public bool IsDirty
        {
            get => _isDirty;
            set
            {
                if (SetProperty(ref _isDirty, value))
                {
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        /// <inheritdoc/>
        public bool IsOverridden
        {
            get => _isOverridden;
            set
            {
                if (SetProperty(ref _isOverridden, value))
                {
                    IsDirty = true;
                    OnPropertyChanged(nameof(SummaryText));
                    OnPropertyChanged(nameof(IsValid));
                }
            }
        }

        /// <inheritdoc/>
        public bool IsValid
        {
            get
            {
                if (!IsOverridden) return true;
                return _settings != null && _settings.IsValid(_doc);
            }
        }

        /// <inheritdoc/>
        public string SummaryText
        {
            get
            {
                if (!IsOverridden)
                {
                    var defaultSettings = new MaterialLibrarySettings().Defaults();
                    try
                     {
                        string defaultPath = Path.Combine(Config.addinPath, "SyntheticSettings.json");
                        if (File.Exists(defaultPath))
                        {
                            Config? defaultConfig = Config.ReadFromFile(defaultPath);
                            if (defaultConfig != null && defaultConfig.Contains(MaterialLibrarySettings.Name))
                            {
                                defaultSettings = defaultConfig.GetSettings<MaterialLibrarySettings>(MaterialLibrarySettings.Name);
                            }
                        }
                    }
                    catch { }
                    return $"Using Firmwide Defaults:\nLibrary Directory: {defaultSettings.LibraryFolderPath}";
                }

                return $"Overridden for Project:\nLibrary Directory: {_settings.LibraryFolderPath}";
            }
        }

        /// <inheritdoc/>
        public ICommand ConfigureCommand { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="MaterialLibraryViewModel"/> class.
        /// </summary>
        /// <param name="doc">The Revit Document.</param>
        /// <param name="mainWindowHandle">The parent main window handle.</param>
        public MaterialLibraryViewModel(Document doc, IntPtr mainWindowHandle)
        {
            _doc = doc ?? throw new ArgumentNullException(nameof(doc));
            _mainWindowHandle = mainWindowHandle;

            _settings = SettingsManager.Get<MaterialLibrarySettings>(_doc);

            var dataStorages = new FilteredElementCollector(_doc)
                .OfClass(typeof(DataStorage))
                .Cast<DataStorage>()
                .ToList();
            string storageName = "Synthetic_" + MaterialLibrarySettings.Name;
            _isOverridden = dataStorages.Any(ds => ds.Name.Equals(storageName, StringComparison.OrdinalIgnoreCase));

            ConfigureCommand = new RelayCommand(OnConfigure, _ => IsOverridden);
        }

        private void OnConfigure(object parameter)
        {

            string initialPath = _settings.LibraryFolderPath;
            if (string.IsNullOrEmpty(initialPath) || !Directory.Exists(initialPath))
            {
                initialPath = "C:\\Materials\\";
            }

            string selectedPath = FileDialogHelper.SelectFolder(_mainWindowHandle, "Select Material Library Folder", initialPath);
            if (selectedPath != null)
            {

                _settings.LibraryFolderPath = selectedPath;
                IsDirty = true;

                OnPropertyChanged(nameof(SummaryText));
                OnPropertyChanged(nameof(IsValid));
            }
        }

        /// <inheritdoc/>
        public void Save()
        {
            if (IsOverridden)
            {
                SettingsManager.Save(_doc, _settings);
            }
            else
            {
                SettingsManager.Delete(_doc, MaterialLibrarySettings.Name);
            }
        }

        /// <inheritdoc/>
        public void Reload()
        {
            _settings = SettingsManager.Get<MaterialLibrarySettings>(_doc);

            var dataStorages = new FilteredElementCollector(_doc)
                .OfClass(typeof(DataStorage))
                .Cast<DataStorage>()
                .ToList();
            string storageName = "Synthetic_" + MaterialLibrarySettings.Name;
            _isOverridden = dataStorages.Any(ds => ds.Name.Equals(storageName, StringComparison.OrdinalIgnoreCase));
            _isDirty = false;

            OnPropertyChanged(nameof(IsOverridden));
            OnPropertyChanged(nameof(SummaryText));
            OnPropertyChanged(nameof(IsValid));
        }
    }
}
```

### File: SettingsDashboard/ViewModels/NetworkPathsWizardViewModel.cs
```csharp
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Synthetic.Settings;
using Synthetic.Infrastructure.Persistence;

using Synthetic.Shared.UI;
using Synthetic.Core;

using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.Shared.RevitAPI;
namespace Synthetic.Modules.SettingsDashboard.ViewModels
{
    /// <summary>
    /// ViewModel for the Network Paths and File Utility Configuration Wizard.
    /// </summary>
    public class NetworkPathsWizardViewModel : ViewModelBase
    {
        private readonly IntPtr _mainWindowHandle;
        private string? _selectedArchiveDirectory;
        private PathMappingViewModel? _selectedMapping;
        private bool _isValidating;

        /// <summary>
        /// Gets the collection of archive directories.
        /// </summary>
        public ObservableCollection<string> ArchiveDirectories { get; }

        /// <summary>
        /// Gets the collection of alternate paths mapping views.
        /// </summary>
        public ObservableCollection<PathMappingViewModel> AlternateMappings { get; }

        /// <summary>
        /// Gets or sets the selected archive directory path.
        /// </summary>
        public string? SelectedArchiveDirectory
        {
            get => _selectedArchiveDirectory;
            set => SetProperty(ref _selectedArchiveDirectory, value);
        }

        /// <summary>
        /// Gets or sets the selected path mapping item.
        /// </summary>
        public PathMappingViewModel? SelectedMapping
        {
            get => _selectedMapping;
            set => SetProperty(ref _selectedMapping, value);
        }

        /// <summary>
        /// Gets or sets a value indicating whether paths are currently being validated in the background.
        /// </summary>
        public bool IsValidating
        {
            get => _isValidating;
            set => SetProperty(ref _isValidating, value);
        }

        /// <summary>
        /// Gets the command to add a new archive directory.
        /// </summary>
        public ICommand AddArchiveCommand { get; }

        /// <summary>
        /// Gets the command to remove the selected archive directory.
        /// </summary>
        public ICommand RemoveArchiveCommand { get; }

        /// <summary>
        /// Gets the command to browse and select an archive directory.
        /// </summary>
        public ICommand BrowseArchiveCommand { get; }

        /// <summary>
        /// Gets the command to add a new alternate path mapping row.
        /// </summary>
        public ICommand AddMappingCommand { get; }

        /// <summary>
        /// Gets the command to remove the selected alternate path mapping row.
        /// </summary>
        public ICommand RemoveMappingCommand { get; }

        /// <summary>
        /// Gets the command to browse and select a local mapped path.
        /// </summary>
        public ICommand BrowseMappingCommand { get; }

        /// <summary>
        /// Gets the command to validate paths and close the wizard on success.
        /// </summary>
        public ICommand OkCommand { get; }

        /// <summary>
        /// Gets the command to cancel editing and close the wizard.
        /// </summary>
        public ICommand CancelCommand { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="NetworkPathsWizardViewModel"/> class.
        /// </summary>
        /// <param name="settings">The initial settings copy to edit.</param>
        /// <param name="mainWindowHandle">The parent main window handle.</param>
        public NetworkPathsWizardViewModel(FileUtilitySettings settings, IntPtr mainWindowHandle)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            _mainWindowHandle = mainWindowHandle;

            ArchiveDirectories = new ObservableCollection<string>(settings.ArchiveDirectories ?? new List<string>());
            AlternateMappings = new ObservableCollection<PathMappingViewModel>();

            if (settings.AlternatePaths != null)
            {
                foreach (var kvp in settings.AlternatePaths)
                {
                    string mapped = kvp.Value != null && kvp.Value.Count > 0 ? kvp.Value[0] : string.Empty;
                    AlternateMappings.Add(new PathMappingViewModel(kvp.Key, mapped));
                }
            }

            // Bind Commands
            AddArchiveCommand = new RelayCommand(OnAddArchive);
            RemoveArchiveCommand = new RelayCommand(OnRemoveArchive, _ => !string.IsNullOrEmpty(SelectedArchiveDirectory));
            BrowseArchiveCommand = new RelayCommand(OnBrowseArchive, _ => !string.IsNullOrEmpty(SelectedArchiveDirectory));

            AddMappingCommand = new RelayCommand(OnAddMapping);
            RemoveMappingCommand = new RelayCommand(OnRemoveMapping, _ => SelectedMapping != null);
            BrowseMappingCommand = new RelayCommand(OnBrowseMapping, _ => SelectedMapping != null);

            OkCommand = new RelayCommand(OnOk, _ => !IsValidating);
            CancelCommand = new RelayCommand(OnCancel);
        }

        private void OnAddArchive(object parameter)
        {
            string selected = FileDialogHelper.SelectFolder(_mainWindowHandle, "Select Archive Directory");
            if (!string.IsNullOrEmpty(selected))
            {
                ArchiveDirectories.Add(selected);
                SelectedArchiveDirectory = selected;
            }
        }

        private void OnRemoveArchive(object parameter)
        {
            if (!string.IsNullOrEmpty(SelectedArchiveDirectory))
            {
                ArchiveDirectories.Remove(SelectedArchiveDirectory!);
                SelectedArchiveDirectory = ArchiveDirectories.FirstOrDefault();
            }
        }

        private void OnBrowseArchive(object parameter)
        {
            if (string.IsNullOrEmpty(SelectedArchiveDirectory)) return;

            string selected = FileDialogHelper.SelectFolder(_mainWindowHandle, "Browse Archive Directory", SelectedArchiveDirectory);
            if (!string.IsNullOrEmpty(selected))
            {
                int index = ArchiveDirectories.IndexOf(SelectedArchiveDirectory!);
                if (index >= 0)
                {
                    ArchiveDirectories[index] = selected;
                    SelectedArchiveDirectory = selected;
                }
            }
        }

        private void OnAddMapping(object parameter)
        {
            var newMapping = new PathMappingViewModel();
            AlternateMappings.Add(newMapping);
            SelectedMapping = newMapping;
        }

        private void OnRemoveMapping(object parameter)
        {
            if (SelectedMapping != null)
            {
                AlternateMappings.Remove(SelectedMapping);
                SelectedMapping = AlternateMappings.FirstOrDefault();
            }
        }

        private void OnBrowseMapping(object parameter)
        {
            if (SelectedMapping == null) return;

            string initialPath = SelectedMapping.LocalMappedPath;
            string selected = FileDialogHelper.SelectFolder(_mainWindowHandle, "Select Local Mapped Path", initialPath);
            if (!string.IsNullOrEmpty(selected))
            {
                SelectedMapping.LocalMappedPath = selected;
            }
        }

        private void OnOk(object parameter)
        {
            IsValidating = true;
            Task.Run(() =>
            {
                var pathsToCheck = new List<string>();

                // Gather archive paths
                foreach (var dir in ArchiveDirectories)
                {
                    if (!string.IsNullOrEmpty(dir))
                    {
                        pathsToCheck.Add(dir);
                    }
                }

                // Gather alternate mappings paths (we validate local mapped paths or original server paths if they are UNC/existing)
                foreach (var mapping in AlternateMappings)
                {
                    if (!string.IsNullOrEmpty(mapping.LocalMappedPath))
                    {
                        pathsToCheck.Add(mapping.LocalMappedPath);
                    }
                }

                bool anyOffline = false;
                foreach (var path in pathsToCheck.Distinct())
                {
                    try
                    {
                        if (!Directory.Exists(path))
                        {
                            anyOffline = true;
                            break;
                        }
                    }
                    catch
                    {
                        anyOffline = true;
                        break;
                    }
                }

                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    IsValidating = false;

                    if (anyOffline)
                    {
                        var dialog = new Autodesk.Revit.UI.TaskDialog("Network Path Validation")
                        {
                            MainInstruction = "One or more network paths cannot be found.",
                            MainContent = "They may be offline or typed incorrectly.\n\nDo you want to save them anyway?",
                            CommonButtons = Autodesk.Revit.UI.TaskDialogCommonButtons.Yes | Autodesk.Revit.UI.TaskDialogCommonButtons.No,
                            DefaultButton = Autodesk.Revit.UI.TaskDialogResult.No
                        };

                        var result = dialog.Show();
                        if (result == Autodesk.Revit.UI.TaskDialogResult.No)
                        {
                            return; // Keep window open so user can adjust paths
                        }
                    }

                    // Success or override save: close window
                    if (parameter is Window window)
                    {
                        window.DialogResult = true;
                        window.Close();
                    }
                });
            });
        }

        private void OnCancel(object parameter)
        {
            if (parameter is Window window)
            {
                window.DialogResult = false;
                window.Close();
            }
        }
    }
}
```

### File: SettingsDashboard/ViewModels/PathMappingViewModel.cs
```csharp
using System;

using Synthetic.Shared.UI;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;
using Synthetic.Modules.StandardsManagement.ViewModels;
namespace Synthetic.Modules.SettingsDashboard.ViewModels
{
    /// <summary>
    /// ViewModel representing a single path mapping entry (Original -> LocalMapped).
    /// </summary>
    public class PathMappingViewModel : ViewModelBase
    {
        private string _originalServerPath;
        private string _localMappedPath;

        /// <summary>
        /// Gets or sets the original server network path.
        /// </summary>
        public string OriginalServerPath
        {
            get => _originalServerPath;
            set => SetProperty(ref _originalServerPath, value);
        }

        /// <summary>
        /// Gets or sets the local mapped network path.
        /// </summary>
        public string LocalMappedPath
        {
            get => _localMappedPath;
            set => SetProperty(ref _localMappedPath, value);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="PathMappingViewModel"/> class.
        /// </summary>
        public PathMappingViewModel()
        {
            _originalServerPath = string.Empty;
            _localMappedPath = string.Empty;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="PathMappingViewModel"/> class with initial paths.
        /// </summary>
        public PathMappingViewModel(string originalServerPath, string localMappedPath)
        {
            _originalServerPath = originalServerPath ?? string.Empty;
            _localMappedPath = localMappedPath ?? string.Empty;
        }
    }
}
```

### File: SettingsDashboard/ViewModels/ProjectMaterialsViewModel.cs
```csharp
using System;
using System.IO;
using System.Linq;
using System.Windows.Input;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using Synthetic.Settings;
using Synthetic.Infrastructure.Persistence;

using Synthetic.Shared.UI;
using Synthetic.Core;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.Shared.RevitAPI;

namespace Synthetic.Modules.SettingsDashboard.ViewModels
{
    /// <summary>
    /// ViewModel for managing Project-specific Material paths.
    /// </summary>
    public class ProjectMaterialsViewModel : ViewModelBase, ISettingModuleViewModel
    {
        private readonly Document _doc;
        private readonly IntPtr _mainWindowHandle;
        private ProjectMaterialSettings _settings;
        private bool _isOverridden;
        private bool _isDirty;

        /// <inheritdoc/>
        public string ModuleName => "Project Materials";

        /// <inheritdoc/>
        public bool IsDirty
        {
            get => _isDirty;
            set
            {
                if (SetProperty(ref _isDirty, value))
                {
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        /// <inheritdoc/>
        public bool IsOverridden
        {
            get => _isOverridden;
            set
            {
                if (SetProperty(ref _isOverridden, value))
                {
                    IsDirty = true;
                    OnPropertyChanged(nameof(SummaryText));
                    OnPropertyChanged(nameof(IsValid));
                }
            }
        }

        /// <inheritdoc/>
        public bool IsValid
        {
            get
            {
                if (_doc == null || !_doc.IsValidObject)
                {
                    return false;
                }
                if (!IsOverridden)
                {
                    if (_doc.IsModelInCloud)
                    {
                        return false;
                    }
                    return true;
                }
                return _settings != null && _settings.IsValid(_doc);
            }
        }

        /// <inheritdoc/>
        public string SummaryText
        {
            get
            {
                if (_doc == null || !_doc.IsValidObject)
                {
                    return "Unresolved (Document is closed)";
                }
                if (!IsOverridden && _doc.IsModelInCloud)
                {
                    return "⚠️ Cloud model detected.\nYou must override and set an absolute project-specific path for materials.";
                }

                string? resolved = _settings.GetResolvedPath(_doc);
                string? resolvedText = string.IsNullOrEmpty(resolved) ? "Unresolved (Document is unsaved)" : resolved;

                if (!IsOverridden)
                {
                    var defaultSettings = new ProjectMaterialSettings().Defaults();
                    try
                    {
                        string defaultPath = Path.Combine(Config.addinPath, "SyntheticSettings.json");
                        if (File.Exists(defaultPath))
                        {
                            Config? defaultConfig = Config.ReadFromFile(defaultPath);
                            if (defaultConfig != null && defaultConfig.Contains(ProjectMaterialSettings.Name))
                            {
                                defaultSettings = defaultConfig.GetSettings<ProjectMaterialSettings>(ProjectMaterialSettings.Name);
                            }
                        }
                    }
                    catch { }
                    return $"Using Firmwide Defaults:\nDefault Relative Path: {defaultSettings.DefaultRelativePath}\nResolved Path: {resolvedText}";
                }

                return $"Overridden for Project:\nDefault Relative Path: {_settings.DefaultRelativePath}\nOverride Folder Path: {string.Join("", _settings.OverrideFolderPath)}\nResolved Path: {resolvedText}";
            }
        }

        /// <inheritdoc/>
        public ICommand ConfigureCommand { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="ProjectMaterialsViewModel"/> class.
        /// </summary>
        /// <param name="doc">The Revit Document.</param>
        /// <param name="mainWindowHandle">The parent main window handle.</param>
        public ProjectMaterialsViewModel(Document doc, IntPtr mainWindowHandle)
        {
            _doc = doc ?? throw new ArgumentNullException(nameof(doc));
            _mainWindowHandle = mainWindowHandle;

            _settings = SettingsManager.Get<ProjectMaterialSettings>(_doc) ?? new ProjectMaterialSettings().Defaults();

            var dataStorages = new FilteredElementCollector(_doc)
                .OfClass(typeof(DataStorage))
                .Cast<DataStorage>()
                .ToList();
            string storageName = "Synthetic_" + ProjectMaterialSettings.Name;
            _isOverridden = dataStorages.Any(ds => ds.Name.Equals(storageName, StringComparison.OrdinalIgnoreCase));

            ConfigureCommand = new RelayCommand(OnConfigure, _ => IsOverridden);
        }

        private void OnConfigure(object parameter)
        {

            string? initialPath = _settings.OverrideFolderPath;
            if (string.IsNullOrEmpty(initialPath) || !Directory.Exists(initialPath))
            {
                initialPath = _settings.GetResolvedPath(_doc);
            }

            string selectedPath = FileDialogHelper.SelectFolder(_mainWindowHandle, "Select Project Materials Folder", initialPath);
            if (selectedPath != null)
            {

                _settings.OverrideFolderPath = selectedPath;
                IsDirty = true;

                OnPropertyChanged(nameof(SummaryText));
                OnPropertyChanged(nameof(IsValid));
            }
        }

        /// <inheritdoc/>
        public void Save()
        {
            if (IsOverridden)
            {
                SettingsManager.Save(_doc, _settings);
            }
            else
            {
                SettingsManager.Delete(_doc, ProjectMaterialSettings.Name);
            }
        }

        /// <inheritdoc/>
        public void Reload()
        {
            _settings = SettingsManager.Get<ProjectMaterialSettings>(_doc) ?? new ProjectMaterialSettings().Defaults();

            var dataStorages = new FilteredElementCollector(_doc)
                .OfClass(typeof(DataStorage))
                .Cast<DataStorage>()
                .ToList();
            string storageName = "Synthetic_" + ProjectMaterialSettings.Name;
            _isOverridden = dataStorages.Any(ds => ds.Name.Equals(storageName, StringComparison.OrdinalIgnoreCase));
            _isDirty = false;

            OnPropertyChanged(nameof(IsOverridden));
            OnPropertyChanged(nameof(SummaryText));
            OnPropertyChanged(nameof(IsValid));
        }
    }
}
```

### File: SettingsDashboard/ViewModels/SettingsDashboardViewModel.cs
```csharp
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

using Synthetic.Infrastructure.Persistence;
using Synthetic.Shared.UI;

using Synthetic.Modules.DetailItemFactory.ViewModels;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;
using Synthetic.Modules.StandardsManagement.ViewModels;
namespace Synthetic.Modules.SettingsDashboard.ViewModels
{
    /// <summary>
    /// Main ViewModel for the Unified Settings Dashboard.
    /// </summary>
    public class SettingsDashboardViewModel : ViewModelBase
    {
        private readonly Document _doc;
        private readonly IntPtr _mainWindowHandle;
        private ISettingModuleViewModel? _selectedModule;

        /// <summary>
        /// Gets the list of settings modules.
        /// </summary>
        public ObservableCollection<ISettingModuleViewModel> SettingModules { get; }

        /// <summary>
        /// Gets or sets the selected settings module.
        /// </summary>
        public ISettingModuleViewModel? SelectedModule
        {
            get => _selectedModule;
            set => SetProperty(ref _selectedModule, value);
        }

        /// <summary>
        /// Gets the save command.
        /// </summary>
        public ICommand SaveCommand { get; }

        /// <summary>
        /// Gets the cancel command.
        /// </summary>
        public ICommand CancelCommand { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="SettingsDashboardViewModel"/> class.
        /// </summary>
        /// <param name="doc">The Revit Document context.</param>
        /// <param name="mainWindowHandle">The main window handle.</param>
        public SettingsDashboardViewModel(Document doc, IntPtr mainWindowHandle)
        {
            _doc = doc ?? throw new ArgumentNullException(nameof(doc));
            _mainWindowHandle = mainWindowHandle;

            SettingModules = new ObservableCollection<ISettingModuleViewModel>
            {
                new WorksetSettingsViewModel(_doc, _mainWindowHandle),
                new ViewAutoNumSettingsViewModel(_doc, _mainWindowHandle),
                new MaterialLibraryViewModel(_doc, _mainWindowHandle),
                new ProjectMaterialsViewModel(_doc, _mainWindowHandle),
                new FileUtilitySettingsViewModel(_doc, _mainWindowHandle),
                new StandardsSettingsViewModel(_doc, _mainWindowHandle),
                new DetailItemFactorySettingsViewModel(_doc, _mainWindowHandle),
                new SyncSettingsViewModel(_doc, _mainWindowHandle)
            };

            foreach (var module in SettingModules)
            {
                module.PropertyChanged += Module_PropertyChanged;
            }

            SelectedModule = SettingModules.FirstOrDefault();

            SaveCommand = new RelayCommand(OnSave, _ => CanSave());
            CancelCommand = new RelayCommand(OnCancel);
        }

        private void Module_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ISettingModuleViewModel.IsDirty) || e.PropertyName == nameof(ISettingModuleViewModel.IsValid))
            {


                // Safely update the SaveCommand CanExecute state on the UI thread after activation is completed
                System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() =>
                {
                    System.Windows.Input.CommandManager.InvalidateRequerySuggested();
                }));
            }
        }

        private bool CanSave()
        {
            if (_doc == null || !_doc.IsValidObject)
            {
                return false;
            }

            bool anyDirty = SettingModules.Any(m => m.IsDirty);
            bool allDirtyValid = SettingModules.Where(m => m.IsDirty).All(m => m.IsValid);
            


            return anyDirty && allDirtyValid;
        }

        private void OnSave(object parameter)
        {
            try
            {
                // Revit operations require manual transactions if we make changes.
                // SettingsManager handles transactions internally when needed, so we just run Save() on each module.
                foreach (var module in SettingModules)
                {
                    if (module.IsDirty)
                    {
                        module.Save();
                        module.IsDirty = false;
                    }
                }

                System.Windows.Input.CommandManager.InvalidateRequerySuggested();

                if (parameter is Window window)
                {
                    window.DialogResult = true;
                    window.Close();
                }
            }
            catch (Exception ex)
            {
                Autodesk.Revit.UI.TaskDialog.Show("Settings Error", $"Failed to save configurations: {ex.Message}");
            }
        }

        private void OnCancel(object parameter)
        {
            if (parameter is Window window)
            {
                window.DialogResult = false;
                window.Close();
            }
        }
    }
}
```

### File: SettingsDashboard/ViewModels/StandardsSettingsViewModel.cs
```csharp
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Input;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using Synthetic.Settings;
using Synthetic.Infrastructure.Persistence;

using Synthetic.Shared.UI;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.Shared.RevitAPI;
namespace Synthetic.Modules.SettingsDashboard.ViewModels
{
    /// <summary>
    /// ViewModel for managing Standards configuration settings in the Dashboard.
    /// </summary>
    public class StandardsSettingsViewModel : ViewModelBase, ISettingModuleViewModel
    {
        private readonly Document _doc;
        private readonly IntPtr _mainWindowHandle;
        private StandardsSettings _settings;
        private bool _isOverridden;
        private bool _isDirty;
        private string _standardsFilePath;
        private readonly IFileDialogService _fileDialogService;

        /// <inheritdoc/>
        public string ModuleName => "Standards";

        /// <inheritdoc/>
        public bool IsDirty
        {
            get => _isDirty;
            set
            {
                if (SetProperty(ref _isDirty, value))
                {
                    System.Windows.Input.CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        /// <inheritdoc/>
        public bool IsOverridden
        {
            get => _isOverridden;
            set
            {
                if (SetProperty(ref _isOverridden, value))
                {
                    IsDirty = true;
                    OnPropertyChanged(nameof(SummaryText));
                    OnPropertyChanged(nameof(IsValid));
                }
            }
        }

        /// <inheritdoc/>
        public bool IsValid
        {
            get
            {
                if (!IsOverridden) return true;
                return !string.IsNullOrEmpty(StandardsFilePath) && 
                       StandardsFilePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
            }
        }

        /// <summary>
        /// Gets or sets the path to the standards JSON file.
        /// </summary>
        public string StandardsFilePath
        {
            get => _standardsFilePath;
            set
            {
                if (SetProperty(ref _standardsFilePath, value))
                {
                    _settings.StandardsFilePath = value;
                    IsDirty = true;
                    OnPropertyChanged(nameof(SummaryText));
                    OnPropertyChanged(nameof(IsValid));
                }
            }
        }

        /// <inheritdoc/>
        public string SummaryText
        {
            get
            {
                string prefix;
                string filePath = string.Empty;

                if (!IsOverridden)
                {
                    prefix = "Using Firmwide Defaults:\n\n";
                    var displaySettings = new StandardsSettings();
                    try
                    {
                        string defaultPath = Path.Combine(Config.addinPath, "SyntheticSettings.json");
                        if (File.Exists(defaultPath))
                        {
                            Config? defaultConfig = Config.ReadFromFile(defaultPath);
                            if (defaultConfig != null && defaultConfig.Contains(StandardsSettings.Name))
                            {
                                displaySettings = defaultConfig.GetSettings<StandardsSettings>(StandardsSettings.Name);
                            }
                        }
                    }
                    catch { }
                    filePath = displaySettings?.StandardsFilePath ?? string.Empty;
                }
                else
                {
                    prefix = "Overridden for Project:\n\n";
                    filePath = StandardsFilePath;
                }

                if (string.IsNullOrEmpty(filePath))
                {
                    return prefix + "(No standards file path configured)";
                }

                return prefix + $"Standards File Path:\n{filePath}";
            }
        }

        /// <inheritdoc/>
        public ICommand? ConfigureCommand => null;

        /// <summary>
        /// Gets the command that allows browsing for a standards JSON file.
        /// </summary>
        public ICommand BrowseCommand { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="StandardsSettingsViewModel"/> class.
        /// </summary>
        /// <param name="doc">The Revit Document context.</param>
        /// <param name="mainWindowHandle">The parent main window handle.</param>
        public StandardsSettingsViewModel(Document doc, IntPtr mainWindowHandle, IFileDialogService? fileDialogService = null)
        {
            _doc = doc ?? throw new ArgumentNullException(nameof(doc));
            _mainWindowHandle = mainWindowHandle;
            _fileDialogService = fileDialogService ?? new WindowsFileDialogService();

            _settings = SettingsManager.Get<StandardsSettings>(_doc);
            _standardsFilePath = _settings.StandardsFilePath;

            var dataStorages = new FilteredElementCollector(_doc)
                .OfClass(typeof(DataStorage))
                .Cast<DataStorage>()
                .ToList();
            string storageName = "Synthetic_" + StandardsSettings.Name;
            _isOverridden = dataStorages.Any(ds => ds.Name.Equals(storageName, StringComparison.OrdinalIgnoreCase));

            BrowseCommand = new RelayCommand(OnBrowse, _ => IsOverridden);
        }

        private void OnBrowse(object parameter)
        {
            var filePath = _fileDialogService.OpenFileDialog("JSON files (*.json)|*.json", "Select Standards File", "");
            if (!string.IsNullOrEmpty(filePath))
            {
                StandardsFilePath = filePath;
            }
        }

        /// <inheritdoc/>
        public void Save()
        {
            if (IsOverridden)
            {
                SettingsManager.Save(_doc, _settings);
            }
            else
            {
                SettingsManager.Delete(_doc, StandardsSettings.Name);
            }

            // [AG2_TEST_START: StandardsSettingsQA]
            // REVERT_METHOD: To remove, safely delete this entire block.
            Console.WriteLine("Jrn.Directive \"SyntheticQA\", \"SettingsSaved: StandardsFilePath\"");
            // [AG2_TEST_END: StandardsSettingsQA]
        }

        /// <inheritdoc/>
        public void Reload()
        {
            _settings = SettingsManager.Get<StandardsSettings>(_doc);
            _standardsFilePath = _settings.StandardsFilePath;

            var dataStorages = new FilteredElementCollector(_doc)
                .OfClass(typeof(DataStorage))
                .Cast<DataStorage>()
                .ToList();
            string storageName = "Synthetic_" + StandardsSettings.Name;
            _isOverridden = dataStorages.Any(ds => ds.Name.Equals(storageName, StringComparison.OrdinalIgnoreCase));
            _isDirty = false;

            OnPropertyChanged(nameof(IsOverridden));
            OnPropertyChanged(nameof(StandardsFilePath));
            OnPropertyChanged(nameof(SummaryText));
            OnPropertyChanged(nameof(IsValid));
        }
    }
}
```

### File: SettingsDashboard/ViewModels/SyncSettingsViewModel.cs
```csharp
using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using Synthetic.Settings;
using Synthetic.Infrastructure.Persistence;

using Synthetic.Shared.UI;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.Shared.RevitAPI;
namespace Synthetic.Modules.SettingsDashboard.ViewModels
{
    /// <summary>
    /// ViewModel for managing Sync Configuration and Link settings.
    /// </summary>
    public class SyncSettingsViewModel : ViewModelBase, ISettingModuleViewModel
    {
        private readonly Document _doc;
        private readonly IntPtr _mainWindowHandle;
        private SyncSettings _settings;
        private bool _isOverridden;
        private readonly IFileDialogService _fileDialogService;
        private readonly IUserPromptService _userPromptService;

        /// <inheritdoc/>
        public string ModuleName => "Sync & Link";

        private bool _isDirty;

        /// <inheritdoc/>
        public bool IsDirty
        {
            get => _isDirty;
            set
            {
                if (SetProperty(ref _isDirty, value))
                {
                    System.Windows.Input.CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        /// <inheritdoc/>
        public bool IsOverridden
        {
            get => _isOverridden;
            set
            {
                if (SetProperty(ref _isOverridden, value))
                {
                    IsDirty = true;
                    OnPropertyChanged(nameof(SummaryText));
                    OnPropertyChanged(nameof(IsValid));
                }
            }
        }

        /// <inheritdoc/>
        public bool IsValid
        {
            get
            {
                if (!IsOverridden) return true;
                return _settings != null && _settings.IsValid(_doc);
            }
        }

        /// <inheritdoc/>
        public string SummaryText
        {
            get
            {
                if (!IsOverridden)
                {
                    return "Sync Settings are disabled by default. Enable overrides to link this project to an external configuration file.";
                }

                if (string.IsNullOrEmpty(_settings.LinkedFilePath))
                {
                    return "Overridden for Project:\nLinked File: (Not configured yet)";
                }

                return $"Overridden for Project:\nLinked File: {_settings.LinkedFilePath}";
            }
        }

        /// <summary>
        /// Gets or sets the path to the linked settings configuration JSON file.
        /// </summary>
        public string LinkedFilePath
        {
            get => _settings.LinkedFilePath;
            set
            {
                if (_settings != null && _settings.LinkedFilePath != value)
                {
                    _settings.LinkedFilePath = value;
                    OnPropertyChanged(nameof(LinkedFilePath));
                    OnPropertyChanged(nameof(SummaryText));
                    OnPropertyChanged(nameof(IsValid));
                }
            }
        }

        /// <inheritdoc/>
        public ICommand? ConfigureCommand => null;

        /// <summary>
        /// Gets the link file command.
        /// </summary>
        public ICommand LinkFileCommand { get; }

        /// <summary>
        /// Gets the export settings command.
        /// </summary>
        public ICommand ExportCommand { get; }

        /// <summary>
        /// Gets the import settings command.
        /// </summary>
        public ICommand ImportCommand { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="SyncSettingsViewModel"/> class.
        /// </summary>
        /// <param name="doc">The Revit Document.</param>
        /// <param name="mainWindowHandle">The parent main window handle.</param>
        public SyncSettingsViewModel(Document doc, IntPtr mainWindowHandle, IFileDialogService? fileDialogService = null, IUserPromptService? userPromptService = null)
        {
            _doc = doc ?? throw new ArgumentNullException(nameof(doc));
            _mainWindowHandle = mainWindowHandle;
            _fileDialogService = fileDialogService ?? new WindowsFileDialogService();
            _userPromptService = userPromptService ?? new WindowsUserPromptService();

            // Load settings using SettingsManager
            _settings = SettingsManager.Get<SyncSettings>(_doc) ?? new SyncSettings();

            // Determine if overridden in document extensible storage
            var dataStorages = new FilteredElementCollector(_doc)
                .OfClass(typeof(DataStorage))
                .Cast<DataStorage>()
                .ToList();
            string storageName = "Synthetic_" + SyncSettings.Name;
            _isOverridden = dataStorages.Any(ds => ds.Name.Equals(storageName, StringComparison.OrdinalIgnoreCase));

            LinkFileCommand = new RelayCommand(OnLinkFile, _ => IsOverridden);
            ExportCommand = new RelayCommand(OnExport);
            ImportCommand = new RelayCommand(OnImport);
        }

        private void OnLinkFile(object parameter)
        {
            var filePath = _fileDialogService.OpenFileDialog("JSON Files (*.json)|*.json|All Files (*.*)|*.*", "Select Linked Settings JSON File", "");
            if (!string.IsNullOrEmpty(filePath))
            {
                LinkedFilePath = filePath;
                IsDirty = true;
            }
        }

        private void OnExport(object parameter)
        {
            var filePath = _fileDialogService.SaveFileDialog("JSON Files (*.json)|*.json|All Files (*.*)|*.*", "Export Settings to JSON File", "");
            if (!string.IsNullOrEmpty(filePath))
            {
                try
                {
                    SettingsManager.ExportAllToFile(_doc, filePath);
                    _userPromptService.ShowMessage("Successfully exported active project settings.", "Export Succeeded");
                }
                catch (Exception ex)
                {
                    _userPromptService.ShowMessage($"Failed to export settings: {ex.Message}", "Export Failed");
                }
            }
        }

        private void OnImport(object parameter)
        {
            var filePath = _fileDialogService.OpenFileDialog("JSON Files (*.json)|*.json|All Files (*.*)|*.*", "Import Settings from JSON File", "");
            if (!string.IsNullOrEmpty(filePath))
            {
                try
                {
                    SettingsManager.ImportAllFromFile(_doc, filePath);

                    // Refresh all ViewModels in the Dashboard
                    if (parameter is Window dashboardWindow)
                    {
                        if (dashboardWindow.DataContext is SettingsDashboardViewModel dashboardVM)
                        {
                            foreach (var module in dashboardVM.SettingModules)
                            {
                                module.Reload();
                                module.IsDirty = false;
                            }
                        }
                    }
                    else
                    {
                        Reload();
                        IsDirty = false;
                    }

                    _userPromptService.ShowMessage("Successfully imported project settings. Dashboard UI has been updated.", "Import Succeeded");
                }
                catch (Exception ex)
                {
                    _userPromptService.ShowMessage($"Failed to import settings: {ex.Message}", "Import Failed");
                }
            }
        }

        /// <inheritdoc/>
        public void Save()
        {
            if (IsOverridden)
            {
                SettingsManager.Save(_doc, _settings);
            }
            else
            {
                SettingsManager.Delete(_doc, SyncSettings.Name);
            }
        }

        /// <inheritdoc/>
        public void Reload()
        {
            _settings = SettingsManager.Get<SyncSettings>(_doc) ?? new SyncSettings();

            var dataStorages = new FilteredElementCollector(_doc)
                .OfClass(typeof(DataStorage))
                .Cast<DataStorage>()
                .ToList();
            string storageName = "Synthetic_" + SyncSettings.Name;
            _isOverridden = dataStorages.Any(ds => ds.Name.Equals(storageName, StringComparison.OrdinalIgnoreCase));
            _isDirty = false; // Reset to clean state on reload

            OnPropertyChanged(nameof(IsOverridden));
            OnPropertyChanged(nameof(LinkedFilePath));
            OnPropertyChanged(nameof(SummaryText));
            OnPropertyChanged(nameof(IsValid));
        }
    }
}
```

### File: SettingsDashboard/ViewModels/SyncWizardViewModel.cs
```csharp
using System;
using System.IO;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using Synthetic.Settings;
using Synthetic.Infrastructure.Persistence;

using Synthetic.Shared.UI;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.Shared.RevitAPI;
namespace Synthetic.Modules.SettingsDashboard.ViewModels
{
    /// <summary>
    /// ViewModel for the Sync Settings Configuration Wizard.
    /// </summary>
    public class SyncWizardViewModel : ViewModelBase
    {
        private string _linkedFilePath;
        private readonly IFileDialogService _fileDialogService;

        /// <summary>
        /// Gets or sets the path to the linked settings configuration JSON file.
        /// </summary>
        public string LinkedFilePath
        {
            get => _linkedFilePath;
            set
            {
                if (SetProperty(ref _linkedFilePath, value))
                {
                    OnPropertyChanged(nameof(IsPathValid));
                }
            }
        }

        /// <summary>
        /// Gets a value indicating whether the selected linked path is valid.
        /// </summary>
        public bool IsPathValid => string.IsNullOrEmpty(LinkedFilePath) || File.Exists(LinkedFilePath);

        /// <summary>
        /// Gets the browse command.
        /// </summary>
        public ICommand BrowseCommand { get; }

        /// <summary>
        /// Gets the OK command.
        /// </summary>
        public ICommand OkCommand { get; }

        /// <summary>
        /// Gets the Cancel command.
        /// </summary>
        public ICommand CancelCommand { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="SyncWizardViewModel"/> class.
        /// </summary>
        /// <param name="settings">The sync settings to edit.</param>
        public SyncWizardViewModel(SyncSettings settings, IFileDialogService? fileDialogService = null)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            _linkedFilePath = settings.LinkedFilePath;
            _fileDialogService = fileDialogService ?? new WindowsFileDialogService();

            BrowseCommand = new RelayCommand(OnBrowse);
            OkCommand = new RelayCommand(OnOk, _ => CanOk());
            CancelCommand = new RelayCommand(OnCancel);
        }

        private void OnBrowse(object parameter)
        {
            var filePath = _fileDialogService.OpenFileDialog("JSON Files (*.json)|*.json|All Files (*.*)|*.*", "Select Linked Settings JSON File", "");
            if (!string.IsNullOrEmpty(filePath))
            {
                LinkedFilePath = filePath;
            }
        }

        private bool CanOk()
        {
            return !string.IsNullOrEmpty(LinkedFilePath) && File.Exists(LinkedFilePath);
        }

        private void OnOk(object parameter)
        {
            if (parameter is Window window)
            {
                window.DialogResult = true;
                window.Close();
            }
        }

        private void OnCancel(object parameter)
        {
            if (parameter is Window window)
            {
                window.DialogResult = false;
                window.Close();
            }
        }
    }
}
```

### File: SettingsDashboard/ViewModels/ViewAutoNumSettingsViewModel.cs
```csharp
using System;
using System.IO;
using System.Linq;
using System.Windows.Input;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using Synthetic.Settings;
using Synthetic.Infrastructure.Persistence;

using Synthetic.Shared.UI;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.Modules.SettingsDashboard.Views;
namespace Synthetic.Modules.SettingsDashboard.ViewModels
{
    /// <summary>
    /// ViewModel for managing View Auto-Numbering configuration settings.
    /// </summary>
    public class ViewAutoNumSettingsViewModel : ViewModelBase, ISettingModuleViewModel
    {
        private readonly Document _doc;
        private readonly IntPtr _mainWindowHandle;
        private ViewAutoNumSettings _settings;
        private bool _isOverridden;

        /// <inheritdoc/>
        public string ModuleName => "View Auto-Numbering";

        private bool _isDirty;

        /// <inheritdoc/>
        public bool IsDirty
        {
            get => _isDirty;
            set
            {
                if (SetProperty(ref _isDirty, value))
                {
                    System.Windows.Input.CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        /// <inheritdoc/>
        public bool IsOverridden
        {
            get => _isOverridden;
            set
            {
                if (SetProperty(ref _isOverridden, value))
                {
                    IsDirty = true;
                    OnPropertyChanged(nameof(SummaryText));
                    OnPropertyChanged(nameof(IsValid));
                }
            }
        }

        /// <inheritdoc/>
        public bool IsValid
        {
            get
            {
                if (!IsOverridden) return true;
                return _settings != null && _settings.IsValid(_doc);
            }
        }

        /// <inheritdoc/>
        public string SummaryText
        {
            get
            {
                if (!IsOverridden)
                {
                    // Fetch default/firm settings from disk to display in summary
                    var defaultSettings = new ViewAutoNumSettings().Defaults();
                    try
                    {
                        string defaultPath = Path.Combine(Config.addinPath, "SyntheticSettings.json");
                        if (File.Exists(defaultPath))
                        {
                            Config? defaultConfig = Config.ReadFromFile(defaultPath);
                            if (defaultConfig != null && defaultConfig.Contains(ViewAutoNumSettings.Name))
                            {
                                defaultSettings = defaultConfig.GetSettings<ViewAutoNumSettings>(ViewAutoNumSettings.Name);
                            }
                        }
                    }
                    catch { }
                    return $"Using Firmwide Defaults:\nFamily: {defaultSettings.ViewAutoNumFamily}\nType: {defaultSettings.ViewAutoNumFamilyType}\nGrid X Param: {defaultSettings.ViewAutoNumXGridName}\nGrid Y Param: {defaultSettings.ViewAutoNumYGridName}";
                }

                return $"Overridden for Project:\nFamily: {_settings.ViewAutoNumFamily}\nType: {_settings.ViewAutoNumFamilyType}\nGrid X Param: {_settings.ViewAutoNumXGridName}\nGrid Y Param: {_settings.ViewAutoNumYGridName}";
            }
        }

        /// <inheritdoc/>
        public ICommand ConfigureCommand { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="ViewAutoNumSettingsViewModel"/> class.
        /// </summary>
        /// <param name="doc">The Revit document.</param>
        /// <param name="mainWindowHandle">The Revit main window handle.</param>
        public ViewAutoNumSettingsViewModel(Document doc, IntPtr mainWindowHandle)
        {
            _doc = doc ?? throw new ArgumentNullException(nameof(doc));
            _mainWindowHandle = mainWindowHandle;

            // Load settings using SettingsManager
            _settings = SettingsManager.Get<ViewAutoNumSettings>(_doc);

            var dataStorages = new FilteredElementCollector(_doc)
                .OfClass(typeof(DataStorage))
                .Cast<DataStorage>()
                .ToList();
            string storageName = "Synthetic_" + ViewAutoNumSettings.Name;
            _isOverridden = dataStorages.Any(ds => ds.Name.Equals(storageName, StringComparison.OrdinalIgnoreCase));

            ConfigureCommand = new RelayCommand(OnConfigure, _ => IsOverridden);
        }

        private void OnConfigure(object parameter)
        {

            // Draft vs Commit pattern: Clone settings before opening wizard
            var draftSettings = new ViewAutoNumSettings(
                _settings.ViewAutoNumFamily, 
                _settings.ViewAutoNumFamilyType, 
                _settings.ViewAutoNumXGridName, 
                _settings.ViewAutoNumYGridName
            );
            var wizardVM = new ViewAutoNumWizardViewModel(_doc, draftSettings);
            var wizardWindow = new ViewAutoNumWizardWindow(_mainWindowHandle)
            {
                DataContext = wizardVM
            };

            var dialogResult = wizardWindow.ShowDialog();



            if (dialogResult == true)
            {


                // Commit settings from wizard
                _settings.ViewAutoNumFamily = wizardVM.SelectedFamily;
                _settings.ViewAutoNumFamilyType = wizardVM.SelectedType;
                _settings.ViewAutoNumXGridName = wizardVM.XGridName;
                _settings.ViewAutoNumYGridName = wizardVM.YGridName;

                IsDirty = true;
                


                OnPropertyChanged(nameof(SummaryText));
                OnPropertyChanged(nameof(IsValid));
            }
        }

        /// <inheritdoc/>
        public void Save()
        {
            if (IsOverridden)
            {
                SettingsManager.Save(_doc, _settings);
            }
            else
            {
                SettingsManager.Delete(_doc, ViewAutoNumSettings.Name);
            }
        }

        /// <inheritdoc/>
        public void Reload()
        {
            _settings = SettingsManager.Get<ViewAutoNumSettings>(_doc);

            var dataStorages = new FilteredElementCollector(_doc)
                .OfClass(typeof(DataStorage))
                .Cast<DataStorage>()
                .ToList();
            string storageName = "Synthetic_" + ViewAutoNumSettings.Name;
            _isOverridden = dataStorages.Any(ds => ds.Name.Equals(storageName, StringComparison.OrdinalIgnoreCase));
            _isDirty = false; // Reset to clean state on reload

            OnPropertyChanged(nameof(IsOverridden));
            OnPropertyChanged(nameof(SummaryText));
            OnPropertyChanged(nameof(IsValid));
        }
    }
}
```

### File: SettingsDashboard/ViewModels/ViewAutoNumWizardViewModel.cs
```csharp
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using Autodesk.Revit.DB;
using Synthetic.Settings;
using Synthetic.Infrastructure.Persistence;

using Synthetic.Shared.UI;

using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.Shared.RevitAPI;
namespace Synthetic.Modules.SettingsDashboard.ViewModels
{
    /// <summary>
    /// ViewModel for the View Autonumbering Configuration Wizard.
    /// </summary>
    public class ViewAutoNumWizardViewModel : ViewModelBase
    {
        private readonly Document _doc;
        private string _selectedFamily = string.Empty;
        private string _selectedType = string.Empty;
        private string _xGridName;
        private string _yGridName;

        /// <summary>
        /// Gets the collection of available annotation families.
        /// </summary>
        public ObservableCollection<string> AvailableFamilies { get; } = new ObservableCollection<string>();

        /// <summary>
        /// Gets the collection of available types for the selected family.
        /// </summary>
        public ObservableCollection<string> AvailableTypes { get; } = new ObservableCollection<string>();

        /// <summary>
        /// Gets or sets the selected annotation family name.
        /// </summary>
        public string SelectedFamily
        {
            get => _selectedFamily;
            set
            {
                if (SetProperty(ref _selectedFamily, value))
                {
                    LoadAvailableTypes();
                }
            }
        }

        /// <summary>
        /// Gets or sets the selected family type name.
        /// </summary>
        public string SelectedType
        {
            get => _selectedType;
            set => SetProperty(ref _selectedType, value);
        }

        /// <summary>
        /// Gets or sets the X Grid parameter name.
        /// </summary>
        public string XGridName
        {
            get => _xGridName;
            set => SetProperty(ref _xGridName, value);
        }

        /// <summary>
        /// Gets or sets the Y Grid parameter name.
        /// </summary>
        public string YGridName
        {
            get => _yGridName;
            set => SetProperty(ref _yGridName, value);
        }

        /// <summary>
        /// Gets the OK command.
        /// </summary>
        public ICommand OkCommand { get; }

        /// <summary>
        /// Gets the Cancel command.
        /// </summary>
        public ICommand CancelCommand { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="ViewAutoNumWizardViewModel"/> class.
        /// </summary>
        /// <param name="doc">The Revit Document context.</param>
        /// <param name="settings">The ViewAutoNum settings to edit.</param>
        public ViewAutoNumWizardViewModel(Document doc, ViewAutoNumSettings settings)
        {
            _doc = doc ?? throw new ArgumentNullException(nameof(doc));
            if (settings == null) throw new ArgumentNullException(nameof(settings));

            _xGridName = settings.ViewAutoNumXGridName;
            _yGridName = settings.ViewAutoNumYGridName;

            OkCommand = new RelayCommand(OnOk, _ => CanOk());
            CancelCommand = new RelayCommand(OnCancel);

            LoadAvailableFamilies(settings.ViewAutoNumFamily, settings.ViewAutoNumFamilyType);
        }

        private void LoadAvailableFamilies(string? targetFamily, string? targetType)
        {
            try
            {
                var families = new FilteredElementCollector(_doc)
                    .OfClass(typeof(FamilySymbol))
                    .Cast<FamilySymbol>()
                    .Where(fs => fs.Category != null && fs.Category.CategoryType == CategoryType.Annotation)
                    .Select(fs => fs.Family?.Name)
                    .Where(name => !string.IsNullOrEmpty(name))
                    .Cast<string>()
                    .Distinct()
                    .OrderBy(name => name)
                    .ToList();

                AvailableFamilies.Clear();
                foreach (var family in families)
                {
                    AvailableFamilies.Add(family);
                }

                if (targetFamily != null && AvailableFamilies.Contains(targetFamily))
                {
                    SelectedFamily = targetFamily;
                    
                    // Trigger loading types for selected family, then select the target type if it exists
                    LoadAvailableTypes();
                    if (targetType != null && AvailableTypes.Contains(targetType))
                    {
                        SelectedType = targetType;
                    }
                }
                else if (AvailableFamilies.Count > 0)
                {
                    SelectedFamily = AvailableFamilies[0];
                }
            }
            catch (Exception)
            {
                // Gracefully handle any Revit API collection querying exceptions
            }
        }

        private void LoadAvailableTypes()
        {
            AvailableTypes.Clear();
            if (string.IsNullOrEmpty(SelectedFamily)) return;

            try
            {
                var types = new FilteredElementCollector(_doc)
                    .OfClass(typeof(FamilySymbol))
                    .Cast<FamilySymbol>()
                    .Where(fs => fs.Family != null && fs.Family.Name.Equals(SelectedFamily, StringComparison.OrdinalIgnoreCase))
                    .Select(fs => fs.Name)
                    .Distinct()
                    .OrderBy(name => name)
                    .ToList();

                foreach (var type in types)
                {
                    AvailableTypes.Add(type);
                }

                if (AvailableTypes.Count > 0 && string.IsNullOrEmpty(SelectedType))
                {
                    SelectedType = AvailableTypes[0];
                }
            }
            catch (Exception)
            {
                // Gracefully handle Revit API exceptions
            }
        }

        private bool CanOk()
        {
            return !string.IsNullOrEmpty(SelectedFamily) &&
                   !string.IsNullOrEmpty(SelectedType) &&
                   !string.IsNullOrEmpty(XGridName) &&
                   !string.IsNullOrEmpty(YGridName);
        }

        private void OnOk(object parameter)
        {
            if (parameter is Window window)
            {
                window.DialogResult = true;
                window.Close();
            }
        }

        private void OnCancel(object parameter)
        {
            if (parameter is Window window)
            {
                window.DialogResult = false;
                window.Close();
            }
        }
    }
}
```

### File: SettingsDashboard/ViewModels/WorksetSettingsViewModel.cs
```csharp
using System;
using System.IO;
using System.Linq;
using System.Windows.Input;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using Synthetic.Settings;
using Synthetic.Infrastructure.Persistence;

using Synthetic.Shared.UI;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.Modules.SettingsDashboard.Views;
namespace Synthetic.Modules.SettingsDashboard.ViewModels
{
    /// <summary>
    /// ViewModel for managing Workset configuration settings.
    /// </summary>
    public class WorksetSettingsViewModel : ViewModelBase, ISettingModuleViewModel
    {
        private readonly Document _doc;
        private readonly IntPtr _mainWindowHandle;
        private WorksetSettings _settings;
        private bool _isOverridden;

        /// <inheritdoc/>
        public string ModuleName => "Worksets";

        private bool _isDirty;

        /// <inheritdoc/>
        public bool IsDirty
        {
            get => _isDirty;
            set
            {
                if (SetProperty(ref _isDirty, value))
                {
                    System.Windows.Input.CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        /// <inheritdoc/>
        public bool IsOverridden
        {
            get => _isOverridden;
            set
            {
                if (SetProperty(ref _isOverridden, value))
                {
                    IsDirty = true;
                    OnPropertyChanged(nameof(SummaryText));
                    OnPropertyChanged(nameof(IsValid));
                }
            }
        }

        /// <inheritdoc/>
        public bool IsValid
        {
            get
            {
                if (!IsOverridden) return true;
                return _settings != null && _settings.IsValid(_doc);
            }
        }

        /// <inheritdoc/>
        public string SummaryText
        {
            get
            {
                if (!IsOverridden)
                {
                    // Fetch default/firm settings from disk to display in summary
                    var defaultSettings = new WorksetSettings().Defaults();
                    try
                    {
                        string defaultPath = Path.Combine(Config.addinPath, "SyntheticSettings.json");
                        if (File.Exists(defaultPath))
                        {
                            Config? defaultConfig = Config.ReadFromFile(defaultPath);
                            if (defaultConfig != null && defaultConfig.Contains(WorksetSettings.Name))
                            {
                                defaultSettings = defaultConfig.GetSettings<WorksetSettings>(WorksetSettings.Name);
                            }
                        }
                    }
                    catch { }
                    return $"Using Firmwide Defaults:\nFile: {defaultSettings.WorksetFile}\nGroup: {defaultSettings.WorksetGroup}";
                }

                return $"Overridden for Project:\nFile: {_settings.WorksetFile}\nGroup: {_settings.WorksetGroup}\nPath: {_settings.PathOrDefault()}\nFull Path: {_settings.FullPath()}";
            }
        }

        /// <inheritdoc/>
        public ICommand ConfigureCommand { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="WorksetSettingsViewModel"/> class.
        /// </summary>
        /// <param name="doc">The Revit document.</param>
        /// <param name="mainWindowHandle">The Revit main window handle.</param>
        public WorksetSettingsViewModel(Document doc, IntPtr mainWindowHandle)
        {
            _doc = doc ?? throw new ArgumentNullException(nameof(doc));
            _mainWindowHandle = mainWindowHandle;

            // Load settings using SettingsManager
            _settings = SettingsManager.Get<WorksetSettings>(_doc);

            var dataStorages = new FilteredElementCollector(_doc)
                .OfClass(typeof(DataStorage))
                .Cast<DataStorage>()
                .ToList();
            string storageName = "Synthetic_" + WorksetSettings.Name;
            _isOverridden = dataStorages.Any(ds => ds.Name.Equals(storageName, StringComparison.OrdinalIgnoreCase));

            ConfigureCommand = new RelayCommand(OnConfigure, _ => IsOverridden);
        }

        private void OnConfigure(object parameter)
        {

            // Draft vs Commit pattern: Clone settings before opening wizard
            var draftSettings = new WorksetSettings(_settings.WorksetFile, _settings.WorksetPath, _settings.WorksetGroup);
            var wizardVM = new WorksetWizardViewModel(draftSettings);
            var wizardWindow = new WorksetWizardWindow(_mainWindowHandle)
            {
                DataContext = wizardVM
            };

            var dialogResult = wizardWindow.ShowDialog();



            if (dialogResult == true)
            {


                // Commit settings from wizard
                _settings.WorksetFile = wizardVM.WorksetFile;
                _settings.WorksetPath = wizardVM.WorksetPath;
                _settings.WorksetGroup = wizardVM.SelectedGroup;

                IsDirty = true;
                


                OnPropertyChanged(nameof(SummaryText));
                OnPropertyChanged(nameof(IsValid));
            }
        }

        /// <inheritdoc/>
        public void Save()
        {
            if (IsOverridden)
            {
                SettingsManager.Save(_doc, _settings);
            }
            else
            {
                SettingsManager.Delete(_doc, WorksetSettings.Name);
            }
        }

        /// <inheritdoc/>
        public void Reload()
        {
            _settings = SettingsManager.Get<WorksetSettings>(_doc);

            var dataStorages = new FilteredElementCollector(_doc)
                .OfClass(typeof(DataStorage))
                .Cast<DataStorage>()
                .ToList();
            string storageName = "Synthetic_" + WorksetSettings.Name;
            _isOverridden = dataStorages.Any(ds => ds.Name.Equals(storageName, StringComparison.OrdinalIgnoreCase));
            _isDirty = false; // Reset to clean state on reload

            OnPropertyChanged(nameof(IsOverridden));
            OnPropertyChanged(nameof(SummaryText));
            OnPropertyChanged(nameof(IsValid));
        }
    }
}
```

### File: SettingsDashboard/ViewModels/WorksetWizardViewModel.cs
```csharp
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using Synthetic.Infrastructure.Serialization;
using Synthetic.Infrastructure.IO;
using Synthetic.Settings;
using Synthetic.Infrastructure.Persistence;

using Synthetic.Shared.UI;

using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.Shared.RevitAPI;
namespace Synthetic.Modules.SettingsDashboard.ViewModels
{
    /// <summary>
    /// ViewModel for the Workset Configuration Wizard.
    /// </summary>
    public class WorksetWizardViewModel : ViewModelBase
    {
        private string? _worksetFile;
        private string? _worksetPath;
        private string? _selectedGroup;
        private bool _isLoading;
        private readonly IFileDialogService _fileDialogService;

        /// <summary>
        /// Gets or sets the workset configuration Excel filename.
        /// </summary>
        public string? WorksetFile
        {
            get => _worksetFile;
            set
            {
                if (SetProperty(ref _worksetFile, value))
                {
                    OnPropertyChanged(nameof(FullPath));
                    TriggerLoadAvailableGroups();
                }
            }
        }

        /// <summary>
        /// Gets or sets the path to the workset Excel file directory.
        /// </summary>
        public string? WorksetPath
        {
            get => _worksetPath;
            set
            {
                if (SetProperty(ref _worksetPath, value))
                {
                    OnPropertyChanged(nameof(FullPath));
                    TriggerLoadAvailableGroups();
                }
            }
        }

        /// <summary>
        /// Gets the full path to the workset configuration Excel file.
        /// </summary>
        public string FullPath
        {
            get
            {
                if (string.IsNullOrEmpty(WorksetFile)) return string.Empty;
                string dir = !string.IsNullOrEmpty(WorksetPath) ? WorksetPath! : Config.addinPath;
                return Path.Combine(dir, WorksetFile);
            }
        }

        /// <summary>
        /// Gets or sets the selected workset group.
        /// </summary>
        public string? SelectedGroup
        {
            get => _selectedGroup;
            set => SetProperty(ref _selectedGroup, value);
        }

        /// <summary>
        /// Gets or sets a value indicating whether background worksheet loading is in progress.
        /// </summary>
        public bool IsLoading
        {
            get => _isLoading;
            set => SetProperty(ref _isLoading, value);
        }

        /// <summary>
        /// Gets the collection of available workset groups (worksheet names).
        /// </summary>
        public ObservableCollection<string> AvailableGroups { get; } = new ObservableCollection<string>();

        /// <summary>
        /// Gets the browse command.
        /// </summary>
        public ICommand BrowseCommand { get; }

        /// <summary>
        /// Gets the OK command.
        /// </summary>
        public ICommand OkCommand { get; }

        /// <summary>
        /// Gets the Cancel command.
        /// </summary>
        public ICommand CancelCommand { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="WorksetWizardViewModel"/> class.
        /// </summary>
        /// <param name="settings">The workset settings to edit.</param>
        public WorksetWizardViewModel(WorksetSettings settings, IFileDialogService? fileDialogService = null)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            _worksetFile = settings.WorksetFile;
            _worksetPath = settings.WorksetPath;
            _selectedGroup = settings.WorksetGroup;
            _fileDialogService = fileDialogService ?? new WindowsFileDialogService();

            BrowseCommand = new RelayCommand(OnBrowse);
            OkCommand = new RelayCommand(OnOk, _ => CanOk());
            CancelCommand = new RelayCommand(OnCancel);

            TriggerLoadAvailableGroups();
        }

        private void OnBrowse(object parameter)
        {
            var filePath = _fileDialogService.OpenFileDialog("Excel Files (*.xlsx)|*.xlsx", "Select Workset Configuration Excel File", "");
            if (!string.IsNullOrEmpty(filePath))
            {
                WorksetPath = Path.GetDirectoryName(filePath);
                WorksetFile = Path.GetFileName(filePath);
            }
        }

        private bool CanOk()
        {
            return !string.IsNullOrEmpty(WorksetFile) && File.Exists(FullPath) && !string.IsNullOrEmpty(SelectedGroup);
        }

        private void OnOk(object parameter)
        {
            if (parameter is Window window)
            {
                window.DialogResult = true;
                window.Close();
            }
        }

        private void OnCancel(object parameter)
        {
            if (parameter is Window window)
            {
                window.DialogResult = false;
                window.Close();
            }
        }

        private void TriggerLoadAvailableGroups()
        {
            string path = FullPath;
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                AvailableGroups.Clear();
                SelectedGroup = null;
                return;
            }

            IsLoading = true;
            Task.Run(() =>
            {
                List<string>? sheetNames = null;
                try
                {
                    var excel = new Excel(path);
                    sheetNames = excel.WorkSheetNames();
                }
                catch (Exception)
                {
                    // Catch Excel Interop launch failures or file-lock issues gracefully
                }

                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    AvailableGroups.Clear();
                    if (sheetNames != null)
                    {
                        foreach (var name in sheetNames)
                        {
                            AvailableGroups.Add(name);
                        }
                    }

                    if (_selectedGroup != null && AvailableGroups.Contains(_selectedGroup))
                    {
                        SelectedGroup = _selectedGroup;
                    }
                    else if (AvailableGroups.Count > 0)
                    {
                        SelectedGroup = AvailableGroups[0];
                    }
                    else
                    {
                        SelectedGroup = null;
                    }
                    IsLoading = false;
                });
            });
        }
    }
}
```

### File: SettingsDashboard/Views/NetworkPathsWizardWindow.xaml
```xml
<Window x:Class="Synthetic.Modules.SettingsDashboard.Views.NetworkPathsWizardWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:local="clr-namespace:Synthetic.Shared.UI"
        Title="Configure Network Paths Settings" 
        Height="500" Width="650" 
        WindowStartupLocation="CenterOwner" 
        ResizeMode="CanResize" 
        ShowInTaskbar="False"
        Style="{DynamicResource SyntheticWindowStyle}"
        local:WindowChromeBehavior.EnableWindowCommands="True">

    <Window.Resources>
        <ResourceDictionary>
            <ResourceDictionary.MergedDictionaries>
                <ResourceDictionary Source="../../../Shared/UI/SyntheticTheme.xaml"/>
            </ResourceDictionary.MergedDictionaries>
            
            <Style x:Key="ValidatingTextBlockStyle" TargetType="TextBlock">
                <Setter Property="Visibility" Value="Collapsed"/>
                <Setter Property="Foreground" Value="{DynamicResource Synthetic.Brushes.AccentActive}"/>
                <Setter Property="FontStyle" Value="Italic"/>
                <Setter Property="VerticalAlignment" Value="Center"/>
                <Style.Triggers>
                    <DataTrigger Binding="{Binding IsValidating}" Value="True">
                        <Setter Property="Visibility" Value="Visible"/>
                    </DataTrigger>
                </Style.Triggers>
            </Style>
        </ResourceDictionary>
    </Window.Resources>

    <Grid Margin="15">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/> <!-- Title / Desc -->
            <RowDefinition Height="2*"/>   <!-- Archive Paths UI -->
            <RowDefinition Height="3*"/>   <!-- Alternate Paths UI -->
            <RowDefinition Height="Auto"/> <!-- Footer Buttons -->
        </Grid.RowDefinitions>

        <!-- Title -->
        <TextBlock Grid.Row="0" 
                   Text="Network Paths &amp; File Redirections" 
                   FontSize="14" 
                   FontWeight="Bold" 
                   Foreground="{DynamicResource Synthetic.Brushes.TextPrimary}" 
                   Margin="0,0,0,12"/>

        <!-- Archive Paths Group -->
        <GroupBox Grid.Row="1" 
                  Header="Archive Directories" 
                  Padding="10" 
                  Margin="0,0,0,15">
            <Grid>
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="*"/>
                    <ColumnDefinition Width="Auto"/>
                </Grid.ColumnDefinitions>

                <ListBox Grid.Column="0" 
                          ItemsSource="{Binding ArchiveDirectories}" 
                          SelectedItem="{Binding SelectedArchiveDirectory, Mode=TwoWay}"
                          Margin="0,0,10,0"/>

                <StackPanel Grid.Column="1" Width="100">
                    <Button Content="Add Path" 
                            Command="{Binding AddArchiveCommand}" 
                            Margin="0,0,0,8"/>
                    <Button Content="Browse..." 
                            Command="{Binding BrowseArchiveCommand}" 
                            Margin="0,0,0,8"/>
                    <Button Content="Remove" 
                            Command="{Binding RemoveArchiveCommand}"/>
                </StackPanel>
            </Grid>
        </GroupBox>

        <!-- Alternate Paths Group -->
        <GroupBox Grid.Row="2" 
                  Header="Alternate Path Mappings" 
                  Padding="10" 
                  Margin="0,0,0,15">
            <Grid>
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="*"/>
                    <ColumnDefinition Width="Auto"/>
                </Grid.ColumnDefinitions>

                <DataGrid Grid.Column="0" 
                          ItemsSource="{Binding AlternateMappings}" 
                          SelectedItem="{Binding SelectedMapping, Mode=TwoWay}"
                          AutoGenerateColumns="False" 
                          CanUserAddRows="False" 
                          CanUserDeleteRows="False"
                          SelectionMode="Single" 
                          Margin="0,0,10,0">
                    <DataGrid.Columns>
                        <DataGridTextColumn Header="Original Server Path" 
                                             Binding="{Binding OriginalServerPath, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}" 
                                             Width="*"/>
                        <DataGridTextColumn Header="Local Mapped Path" 
                                             Binding="{Binding LocalMappedPath, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}" 
                                             Width="*"/>
                    </DataGrid.Columns>
                </DataGrid>

                <StackPanel Grid.Column="1" Width="100">
                    <Button Content="Add Row" 
                            Command="{Binding AddMappingCommand}" 
                            Margin="0,0,0,8"/>
                    <Button Content="Browse Map" 
                            Command="{Binding BrowseMappingCommand}" 
                            Margin="0,0,0,8"/>
                    <Button Content="Remove Row" 
                            Command="{Binding RemoveMappingCommand}"/>
                </StackPanel>
            </Grid>
        </GroupBox>

        <!-- Footer -->
        <Grid Grid.Row="3">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="*"/>
                <ColumnDefinition Width="Auto"/>
            </Grid.ColumnDefinitions>

            <!-- Spinner / Status text -->
            <StackPanel Grid.Column="0" Orientation="Horizontal" VerticalAlignment="Center">
                <TextBlock Text="Validating network directories..." 
                           Style="{StaticResource ValidatingTextBlockStyle}"/>
            </StackPanel>

            <StackPanel Grid.Column="1" Orientation="Horizontal" HorizontalAlignment="Right">
                <Button Content="OK" 
                        Command="{Binding OkCommand}" 
                        CommandParameter="{Binding RelativeSource={RelativeSource AncestorType=Window}}"
                        Style="{DynamicResource Synthetic.Styles.PrimaryButton}"
                        Margin="0,0,10,0"/>
                <Button Content="Cancel" 
                        Command="{Binding CancelCommand}" 
                        CommandParameter="{Binding RelativeSource={RelativeSource AncestorType=Window}}"
                        Style="{DynamicResource Synthetic.Styles.SecondaryButton.Right}"/>
            </StackPanel>
        </Grid>
    </Grid>
</Window>
```

### File: SettingsDashboard/Views/NetworkPathsWizardWindow.xaml.cs
```csharp
using System;
using System.Windows;

using Synthetic.Shared.UI;
using Synthetic.Core;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;

namespace Synthetic.Modules.SettingsDashboard.Views
{
    /// <summary>
    /// Interaction logic for NetworkPathsWizardWindow.xaml.
    /// </summary>
    public partial class NetworkPathsWizardWindow : Window
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="NetworkPathsWizardWindow"/> class.
        /// </summary>
        /// <param name="mainWindowHandle">The parent Revit main window handle.</param>
        public NetworkPathsWizardWindow(IntPtr mainWindowHandle)
        {
            InitializeComponent();
            RevitWindowHelper.SetOwner(this, mainWindowHandle);
        }
    }
}
```

### File: SettingsDashboard/Views/SettingsDashboardWindow.xaml
```xml
<Window x:Class="Synthetic.Modules.SettingsDashboard.Views.SettingsDashboardWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:local="clr-namespace:Synthetic.Shared.UI"
        xmlns:vm="clr-namespace:Synthetic.Modules.SettingsDashboard.ViewModels"
        xmlns:views="clr-namespace:Synthetic.Modules.SettingsDashboard.Views"
        xmlns:difvm="clr-namespace:Synthetic.Modules.DetailItemFactory.ViewModels"
        xmlns:difviews="clr-namespace:Synthetic.Modules.DetailItemFactory.Views"
        Title="Synthetic - Settings Configuration Panel"
        Height="450" Width="700"
        MinHeight="400" MinWidth="600"
        WindowStartupLocation="CenterOwner"
        ResizeMode="CanResize"
        ShowInTaskbar="False"
        Style="{DynamicResource SyntheticWindowStyle}"
        local:WindowChromeBehavior.EnableWindowCommands="True">

    <Window.Resources>
        <ResourceDictionary>
            <ResourceDictionary.MergedDictionaries>
                <ResourceDictionary Source="../../../Shared/UI/SyntheticTheme.xaml"/>
            </ResourceDictionary.MergedDictionaries>

            <!-- Shared Generic Settings Module Layout — layout only, inherits theme styles -->
            <DataTemplate x:Key="GenericModuleTemplate">
                <Grid>
                    <Grid.RowDefinitions>
                        <RowDefinition Height="Auto"/> <!-- Title -->
                        <RowDefinition Height="Auto"/> <!-- Checkbox Override -->
                        <RowDefinition Height="*"/>    <!-- Summary Box -->
                        <RowDefinition Height="Auto"/> <!-- Action / Configure -->
                    </Grid.RowDefinitions>

                    <TextBlock Grid.Row="0"
                               Text="{Binding ModuleName}"
                               FontSize="18" FontWeight="Bold"
                               Foreground="{DynamicResource Synthetic.Brushes.TextPrimary}"
                               Margin="0,0,0,15"/>

                    <CheckBox Grid.Row="1"
                              Content="Override Firm Settings for this Project"
                              IsChecked="{Binding IsOverridden, Mode=TwoWay}"
                              FontSize="13"
                              Margin="0,0,0,15"/>

                    <!-- Config Summary Card -->
                    <GroupBox Grid.Row="2"
                              Header="Configuration Summary"
                              BorderBrush="{DynamicResource Synthetic.Brushes.BorderNormal}"
                              BorderThickness="1"
                              Foreground="{DynamicResource Synthetic.Brushes.TextSecondary}"
                              Margin="0,0,0,15"
                              Padding="10">
                        <ScrollViewer VerticalScrollBarVisibility="Auto" HorizontalScrollBarVisibility="Auto">
                            <TextBlock Text="{Binding SummaryText}"
                                       Foreground="{DynamicResource Synthetic.Brushes.TextPrimary}"
                                       FontSize="12"
                                       FontFamily="Consolas"
                                       LineHeight="18"
                                       TextWrapping="Wrap"/>
                        </ScrollViewer>
                    </GroupBox>

                    <!-- Configure Action -->
                    <Button Grid.Row="3"
                            Content="Configure..."
                            Command="{Binding ConfigureCommand}"
                            CommandParameter="{Binding RelativeSource={RelativeSource AncestorType=Window}}"
                            HorizontalAlignment="Left"/>
                </Grid>
            </DataTemplate>
        </ResourceDictionary>
    </Window.Resources>

    <Grid>
        <Grid.RowDefinitions>
            <RowDefinition Height="*"/>
            <RowDefinition Height="Auto"/>
        </Grid.RowDefinitions>

        <!-- Main Body -->
        <Grid Grid.Row="0">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="200"/>
                <ColumnDefinition Width="*"/>
            </Grid.ColumnDefinitions>

            <!-- Sidebar Navigation -->
            <ListBox Grid.Column="0"
                     ItemsSource="{Binding SettingModules}"
                     SelectedItem="{Binding SelectedModule, Mode=TwoWay}"
                     Background="{DynamicResource Synthetic.Brushes.ControlSurface}"
                     BorderBrush="{DynamicResource Synthetic.Brushes.BorderNormal}"
                     BorderThickness="0,0,1,0"
                     ScrollViewer.HorizontalScrollBarVisibility="Disabled">
                <ListBox.ItemContainerStyle>
                    <Style TargetType="ListBoxItem">
                        <Setter Property="Background"      Value="Transparent"/>
                        <Setter Property="BorderThickness" Value="0"/>
                        <Setter Property="Padding"         Value="15,12"/>
                        <Setter Property="Foreground"      Value="{DynamicResource Synthetic.Brushes.TextPrimary}"/>
                        <Setter Property="Cursor"          Value="Hand"/>
                        <Setter Property="Template">
                            <Setter.Value>
                                <ControlTemplate TargetType="ListBoxItem">
                                    <Border x:Name="ItemBd"
                                            Background="{TemplateBinding Background}"
                                            Padding="{TemplateBinding Padding}"
                                            SnapsToDevicePixels="True">
                                        <ContentPresenter VerticalAlignment="Center"/>
                                    </Border>
                                    <ControlTemplate.Triggers>
                                        <Trigger Property="IsSelected" Value="True">
                                            <Setter TargetName="ItemBd" Property="Background"
                                                    Value="{DynamicResource Synthetic.Brushes.BorderNormal}"/>
                                            <Setter Property="Foreground"
                                                    Value="{DynamicResource Synthetic.Brushes.AccentActive}"/>
                                        </Trigger>
                                        <Trigger Property="IsMouseOver" Value="True">
                                            <Setter TargetName="ItemBd" Property="Background" Value="#332E2D2C"/>
                                        </Trigger>
                                    </ControlTemplate.Triggers>
                                </ControlTemplate>
                            </Setter.Value>
                        </Setter>
                    </Style>
                </ListBox.ItemContainerStyle>
                <ListBox.ItemTemplate>
                    <DataTemplate>
                        <TextBlock Text="{Binding ModuleName}" FontSize="13"/>
                    </DataTemplate>
                </ListBox.ItemTemplate>
            </ListBox>

            <!-- Detail Display Area -->
            <ContentControl Grid.Column="1" Content="{Binding SelectedModule}" Margin="20">
                <ContentControl.Resources>
                    <!-- Implicit DataTemplate for SyncSettingsViewModel -->
                    <DataTemplate DataType="{x:Type vm:SyncSettingsViewModel}">
                        <views:SyncSettingsView />
                    </DataTemplate>

                    <!-- Implicit DataTemplate for WorksetSettingsViewModel -->
                    <DataTemplate DataType="{x:Type vm:WorksetSettingsViewModel}">
                        <ContentPresenter Content="{Binding}" ContentTemplate="{StaticResource GenericModuleTemplate}" />
                    </DataTemplate>

                    <!-- Implicit DataTemplate for ViewAutoNumSettingsViewModel -->
                    <DataTemplate DataType="{x:Type vm:ViewAutoNumSettingsViewModel}">
                        <ContentPresenter Content="{Binding}" ContentTemplate="{StaticResource GenericModuleTemplate}" />
                    </DataTemplate>

                    <!-- Implicit DataTemplate for MaterialLibraryViewModel -->
                    <DataTemplate DataType="{x:Type vm:MaterialLibraryViewModel}">
                        <ContentPresenter Content="{Binding}" ContentTemplate="{StaticResource GenericModuleTemplate}" />
                    </DataTemplate>

                    <!-- Implicit DataTemplate for ProjectMaterialsViewModel -->
                    <DataTemplate DataType="{x:Type vm:ProjectMaterialsViewModel}">
                        <ContentPresenter Content="{Binding}" ContentTemplate="{StaticResource GenericModuleTemplate}" />
                    </DataTemplate>

                    <!-- Implicit DataTemplate for FileUtilitySettingsViewModel -->
                    <DataTemplate DataType="{x:Type vm:FileUtilitySettingsViewModel}">
                        <ContentPresenter Content="{Binding}" ContentTemplate="{StaticResource GenericModuleTemplate}" />
                    </DataTemplate>

                    <!-- Implicit DataTemplate for StandardsSettingsViewModel -->
                    <DataTemplate DataType="{x:Type vm:StandardsSettingsViewModel}">
                        <views:StandardsSettingsView />
                    </DataTemplate>

                    <!-- Implicit DataTemplate for DetailItemFactorySettingsViewModel -->
                    <DataTemplate DataType="{x:Type difvm:DetailItemFactorySettingsViewModel}">
                        <difviews:DetailItemFactorySettingsView />
                    </DataTemplate>
                </ContentControl.Resources>
            </ContentControl>
        </Grid>

        <!-- Footer Control Buttons -->
        <Border Grid.Row="1"
                Background="{DynamicResource Synthetic.Brushes.ControlSurface}"
                BorderBrush="{DynamicResource Synthetic.Brushes.BorderNormal}"
                BorderThickness="0,1,0,0"
                Padding="15,10">
            <StackPanel Orientation="Horizontal" HorizontalAlignment="Right">
                <!-- Primary Action: Save -->
                <Button Content="Save"
                        Command="{Binding SaveCommand}"
                        CommandParameter="{Binding RelativeSource={RelativeSource AncestorType=Window}}"
                        Style="{DynamicResource Synthetic.Styles.PrimaryButton}"
                        Margin="0,0,10,0"/>
                <!-- Secondary: Cancel -->
                <Button Content="Cancel"
                        Command="{Binding CancelCommand}"
                        CommandParameter="{Binding RelativeSource={RelativeSource AncestorType=Window}}"
                        Style="{DynamicResource Synthetic.Styles.SecondaryButton.Right}"/>
            </StackPanel>
        </Border>
    </Grid>
</Window>
```

### File: SettingsDashboard/Views/SettingsDashboardWindow.xaml.cs
```csharp
using System;
using System.Windows;

using Synthetic.Shared.UI;
using Synthetic.Core;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;

namespace Synthetic.Modules.SettingsDashboard.Views
{
    /// <summary>
    /// Interaction logic for SettingsDashboardWindow.xaml.
    /// </summary>
    public partial class SettingsDashboardWindow : Window
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SettingsDashboardWindow"/> class.
        /// </summary>
        /// <param name="mainWindowHandle">The parent Revit main window handle.</param>
        public SettingsDashboardWindow(IntPtr mainWindowHandle)
        {
            InitializeComponent();
            RevitWindowHelper.SetOwner(this, mainWindowHandle);
        }
    }
}
```

### File: SettingsDashboard/Views/StandardsSettingsView.xaml
```xml
<UserControl x:Class="Synthetic.Modules.SettingsDashboard.Views.StandardsSettingsView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             Background="Transparent" Foreground="{DynamicResource Synthetic.Brushes.TextPrimary}">
    <UserControl.Resources>
        <ResourceDictionary>
            <ResourceDictionary.MergedDictionaries>
                <ResourceDictionary Source="../../../Shared/UI/SyntheticTheme.xaml"/>
            </ResourceDictionary.MergedDictionaries>
        </ResourceDictionary>
    </UserControl.Resources>

    <Grid>
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/> <!-- Title -->
            <RowDefinition Height="Auto"/> <!-- Override Checkbox -->
            <RowDefinition Height="*"/>    <!-- Settings Section -->
        </Grid.RowDefinitions>

        <!-- Title -->
        <TextBlock Grid.Row="0"
                   Text="Standards Configuration"
                   FontSize="18" FontWeight="Bold"
                   Foreground="{DynamicResource Synthetic.Brushes.TextPrimary}"
                   Margin="0,0,0,15"/>

        <!-- Override Checkbox -->
        <CheckBox Grid.Row="1"
                  Content="Override Firm Settings for this Project"
                  IsChecked="{Binding IsOverridden, Mode=TwoWay}"
                  FontSize="13"
                  Margin="0,0,0,15"/>

        <!-- Settings Group -->
        <GroupBox Grid.Row="2"
                  Header="Configuration"
                  BorderBrush="{DynamicResource Synthetic.Brushes.BorderNormal}"
                  BorderThickness="1"
                  Foreground="{DynamicResource Synthetic.Brushes.TextSecondary}"
                  Padding="12"
                  IsEnabled="{Binding IsOverridden}">
            <Grid>
                <Grid.RowDefinitions>
                    <RowDefinition Height="Auto"/>
                    <RowDefinition Height="Auto"/>
                </Grid.RowDefinitions>

                <TextBlock Grid.Row="0"
                           Text="Standards File Path (.json):"
                           Foreground="{DynamicResource Synthetic.Brushes.TextSecondary}"
                           FontSize="11"
                           Margin="0,0,0,5"/>

                <Grid Grid.Row="1">
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="*"/>
                        <ColumnDefinition Width="Auto"/>
                    </Grid.ColumnDefinitions>

                    <!-- TextBox for File Path — inherits theme TextBox style -->
                    <TextBox Grid.Column="0"
                             Text="{Binding StandardsFilePath, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}"
                             FontFamily="Consolas"
                             VerticalAlignment="Center"
                             Margin="0,0,10,0"/>

                    <Button Grid.Column="1"
                            Content="Browse..."
                            Command="{Binding BrowseCommand}"/>
                </Grid>
            </Grid>
        </GroupBox>
    </Grid>
</UserControl>
```

### File: SettingsDashboard/Views/StandardsSettingsView.xaml.cs
```csharp
using System.Windows.Controls;
using Synthetic.Modules.StandardsManagement.ViewModels;

namespace Synthetic.Modules.SettingsDashboard.Views
{
    /// <summary>
    /// Interaction logic for StandardsSettingsView.xaml.
    /// </summary>
    public partial class StandardsSettingsView : System.Windows.Controls.UserControl
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="StandardsSettingsView"/> class.
        /// </summary>
        public StandardsSettingsView()
        {
            InitializeComponent();
        }
    }
}
```

### File: SettingsDashboard/Views/SyncResolutionWindow.xaml
```xml
<Window x:Class="Synthetic.Modules.SettingsDashboard.Views.SyncResolutionWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:local="clr-namespace:Synthetic.Shared.UI"
        Title="Resolve Configuration Drift" 
        SizeToContent="WidthAndHeight" MinHeight="200" MinWidth="500" 
        WindowStartupLocation="CenterOwner" 
        ResizeMode="NoResize" 
        ShowInTaskbar="False"
        Style="{DynamicResource SyntheticWindowStyle}"
        local:WindowChromeBehavior.EnableWindowCommands="True">

    <Window.Resources>
        <ResourceDictionary>
            <ResourceDictionary.MergedDictionaries>
                <ResourceDictionary Source="../../../Shared/UI/SyntheticTheme.xaml"/>
            </ResourceDictionary.MergedDictionaries>
        </ResourceDictionary>
    </Window.Resources>

    <Grid Margin="15">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/> <!-- Header -->
            <RowDefinition Height="*"/>    <!-- Text Detail -->
            <RowDefinition Height="Auto"/> <!-- Buttons -->
        </Grid.RowDefinitions>

        <TextBlock Grid.Row="0" 
                   Text="Linked File Conflict Detected" 
                   FontSize="14" 
                   FontWeight="Bold" 
                   Foreground="{DynamicResource Synthetic.Brushes.TextPrimary}" 
                   Margin="0,0,0,10"/>

        <TextBlock Grid.Row="1" 
                   Name="txtDetail" 
                   Foreground="{DynamicResource Synthetic.Brushes.TextSecondary}" 
                   FontSize="12" 
                   TextWrapping="Wrap" 
                   LineHeight="16"
                   Margin="0,0,0,15"/>

        <Grid Grid.Row="2">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="*"/>
                <ColumnDefinition Width="Auto"/>
                <ColumnDefinition Width="Auto"/>
                <ColumnDefinition Width="Auto"/>
            </Grid.ColumnDefinitions>

            <!-- Action Buttons -->
            <Button Grid.Column="1" 
                    Content="Pull to Project" 
                    Click="OnPullClick" 
                    Style="{DynamicResource Synthetic.Styles.PrimaryButton}"
                    Margin="0,0,10,0"/>

            <Button Grid.Column="2" 
                    Content="Push to File" 
                    Click="OnPushClick" 
                    Margin="0,0,10,0"/>

            <Button Grid.Column="3" 
                    Content="Ignore" 
                    Click="OnIgnoreClick"/>
        </Grid>
    </Grid>
</Window>
```

### File: SettingsDashboard/Views/SyncResolutionWindow.xaml.cs
```csharp
using System;
using System.IO;
using System.Windows;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using Synthetic.Settings;
using Synthetic.Infrastructure.Persistence;

using Synthetic.Shared.UI;
using Synthetic.Core;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.Modules.SettingsDashboard.Handlers;

namespace Synthetic.Modules.SettingsDashboard.Views
{
    /// <summary>
    /// Interaction logic for SyncResolutionWindow.xaml.
    /// </summary>
    public partial class SyncResolutionWindow : Window
    {
        private readonly Document _doc;
        private readonly string _filePath;
        private readonly Synthetic.Modules.SettingsDashboard.Handlers.SyncExternalEventHandler _handler;
        private readonly ExternalEvent _externalEvent;

        /// <summary>
        /// Initializes a new instance of the <see cref="SyncResolutionWindow"/> class.
        /// </summary>
        /// <param name="mainWindowHandle">The parent Revit main window handle.</param>
        /// <param name="doc">The active Revit Document.</param>
        /// <param name="filePath">The path to the linked settings configuration JSON file.</param>
        /// <param name="handler">The external event handler for settings sync.</param>
        /// <param name="externalEvent">The external event associated with the handler.</param>
        public SyncResolutionWindow(IntPtr mainWindowHandle, Document doc, string filePath, Synthetic.Modules.SettingsDashboard.Handlers.SyncExternalEventHandler handler, ExternalEvent externalEvent)
        {
            InitializeComponent();
            _doc = doc ?? throw new ArgumentNullException(nameof(doc));
            _filePath = filePath;
            _handler = handler ?? throw new ArgumentNullException(nameof(handler));
            _externalEvent = externalEvent ?? throw new ArgumentNullException(nameof(externalEvent));

            txtDetail.Text = $"The active project configuration settings differ from the linked external settings file:\n{_filePath}\n\nChoose how to resolve this conflict:";

            // Set parent owner handle if available
            RevitWindowHelper.SetOwner(this, mainWindowHandle);
        }

        private void OnPullClick(object sender, RoutedEventArgs e)
        {
            try
            {
                _handler.QueueRequest(Handlers.SyncRequestType.Pull, _doc, _filePath);
                _externalEvent.Raise();



                this.DialogResult = true;
                this.Close();
            }
            catch (Exception ex)
            {
                Autodesk.Revit.UI.TaskDialog.Show("Pull Request Error", $"Failed to raise Pull request: {ex.Message}");
            }
        }

        private void OnPushClick(object sender, RoutedEventArgs e)
        {
            try
            {
                _handler.QueueRequest(Handlers.SyncRequestType.Push, _doc, _filePath);
                _externalEvent.Raise();



                this.DialogResult = true;
                this.Close();
            }
            catch (Exception ex)
            {
                Autodesk.Revit.UI.TaskDialog.Show("Push Request Error", $"Failed to raise Push request: {ex.Message}");
            }
        }

        private void OnIgnoreClick(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }
    }
}
```

### File: SettingsDashboard/Views/SyncSettingsView.xaml
```xml
<UserControl x:Class="Synthetic.Modules.SettingsDashboard.Views.SyncSettingsView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             Background="Transparent" Foreground="{DynamicResource Synthetic.Brushes.TextPrimary}">
    <UserControl.Resources>
        <ResourceDictionary>
            <ResourceDictionary.MergedDictionaries>
                <ResourceDictionary Source="../../../Shared/UI/SyntheticTheme.xaml"/>
            </ResourceDictionary.MergedDictionaries>
        </ResourceDictionary>
    </UserControl.Resources>

    <Grid>
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/> <!-- Title -->
            <RowDefinition Height="Auto"/> <!-- Override Checkbox -->
            <RowDefinition Height="Auto"/> <!-- Active Link Section -->
            <RowDefinition Height="*"/>    <!-- Manual Transfer Section -->
        </Grid.RowDefinitions>

        <!-- Title -->
        <TextBlock Grid.Row="0"
                   Text="Sync &amp; Link"
                   FontSize="18" FontWeight="Bold"
                   Foreground="{DynamicResource Synthetic.Brushes.TextPrimary}"
                   Margin="0,0,0,15"/>

        <!-- Override Checkbox -->
        <CheckBox Grid.Row="1"
                  Content="Override Firm Settings for this Project"
                  IsChecked="{Binding IsOverridden, Mode=TwoWay}"
                  FontSize="13"
                  Margin="0,0,0,15"/>

        <!-- Active Link Section -->
        <GroupBox Grid.Row="2"
                  Header="Active Link"
                  BorderBrush="{DynamicResource Synthetic.Brushes.BorderNormal}"
                  BorderThickness="1"
                  Foreground="{DynamicResource Synthetic.Brushes.TextSecondary}"
                  Margin="0,0,0,15"
                  Padding="12"
                  IsEnabled="{Binding IsOverridden}">
            <Grid>
                <Grid.RowDefinitions>
                    <RowDefinition Height="Auto"/>
                    <RowDefinition Height="Auto"/>
                </Grid.RowDefinitions>

                <TextBlock Grid.Row="0"
                           Text="Linked File Path:"
                           Foreground="{DynamicResource Synthetic.Brushes.TextSecondary}"
                           FontSize="11"
                           Margin="0,0,0,5"/>

                <Grid Grid.Row="1">
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="*"/>
                        <ColumnDefinition Width="Auto"/>
                    </Grid.ColumnDefinitions>

                    <!-- Read-only path display styled as a TextBox -->
                    <TextBox Grid.Column="0"
                             Text="{Binding LinkedFilePath, TargetNullValue='(No linked file path configured)'}"
                             IsReadOnly="True"
                             FontFamily="Consolas"
                             Margin="0,0,10,0"/>

                    <Button Grid.Column="1"
                            Content="Set Linked Settings File"
                            Command="{Binding LinkFileCommand}"/>
                </Grid>
            </Grid>
        </GroupBox>

        <!-- Manual Transfer Section -->
        <GroupBox Grid.Row="3"
                  Header="Manual Transfer"
                  BorderBrush="{DynamicResource Synthetic.Brushes.BorderNormal}"
                  BorderThickness="1"
                  Foreground="{DynamicResource Synthetic.Brushes.TextSecondary}"
                  Padding="12">
            <Grid>
                <Grid.RowDefinitions>
                    <RowDefinition Height="Auto"/>
                    <RowDefinition Height="Auto"/>
                    <RowDefinition Height="*"/>
                </Grid.RowDefinitions>

                <!-- Action Buttons -->
                <StackPanel Grid.Row="0" Orientation="Horizontal" Margin="0,0,0,12">
                    <Button Content="Export Project Settings to JSON"
                            Command="{Binding ExportCommand}"
                            Margin="0,0,15,0"/>

                    <!-- Import is a Primary Action -->
                    <Button Content="Import Settings from JSON"
                            Command="{Binding ImportCommand}"
                            CommandParameter="{Binding RelativeSource={RelativeSource AncestorType=Window}}"
                            Style="{DynamicResource Synthetic.Styles.PrimaryButton}"/>
                </StackPanel>

                <!-- Warning Alert Box — uses Warning semantic color -->
                <Border Grid.Row="1"
                        Background="#1AC2A26A"
                        BorderBrush="{DynamicResource Synthetic.Brushes.Warning}"
                        BorderThickness="1"
                        CornerRadius="4"
                        Padding="10">
                    <Grid>
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="Auto"/>
                            <ColumnDefinition Width="*"/>
                        </Grid.ColumnDefinitions>

                        <!-- Warning Icon (triangle vector path) -->
                        <Path Grid.Column="0"
                              Data="{StaticResource Synthetic.Geometries.Warning}"
                              Fill="{DynamicResource Synthetic.Brushes.Warning}"
                              Width="18" Height="18"
                              Stretch="Uniform"
                              VerticalAlignment="Center"
                              Margin="0,0,10,0"/>

                        <TextBlock Grid.Column="1"
                                   Text="Warning: Importing will immediately overwrite all current project settings in Extensible Storage."
                                   Foreground="{DynamicResource Synthetic.Brushes.TextPrimary}"
                                   FontSize="11.5"
                                   FontWeight="SemiBold"
                                   VerticalAlignment="Center"
                                   TextWrapping="Wrap"/>
                    </Grid>
                </Border>
            </Grid>
        </GroupBox>
    </Grid>
</UserControl>
```

### File: SettingsDashboard/Views/SyncSettingsView.xaml.cs
```csharp
using System.Windows.Controls;
using Synthetic.Modules.StandardsManagement.ViewModels;

namespace Synthetic.Modules.SettingsDashboard.Views
{
    /// <summary>
    /// Interaction logic for SyncSettingsView.xaml.
    /// </summary>
    public partial class SyncSettingsView : System.Windows.Controls.UserControl
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SyncSettingsView"/> class.
        /// </summary>
        public SyncSettingsView()
        {
            InitializeComponent();
        }
    }
}
```

### File: SettingsDashboard/Views/SyncToastNotification.xaml
```xml
<Window x:Class="Synthetic.Modules.SettingsDashboard.Views.SyncToastNotification"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="Project Settings Out of Sync"
        Height="80" Width="380"
        WindowStyle="None"
        AllowsTransparency="True"
        Topmost="True"
        ShowInTaskbar="False"
        Background="Transparent">

    <Window.Resources>
        <ResourceDictionary>
            <ResourceDictionary.MergedDictionaries>
                <ResourceDictionary Source="../../../Shared/UI/SyntheticTheme.xaml"/>
            </ResourceDictionary.MergedDictionaries>
        </ResourceDictionary>
    </Window.Resources>

    <Border Background="{DynamicResource Synthetic.Brushes.ControlSurface}"
            BorderBrush="{DynamicResource Synthetic.Brushes.Warning}"
            BorderThickness="2"
            CornerRadius="4"
            Padding="10">
        <Grid>
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="Auto"/> <!-- Warning Icon -->
                <ColumnDefinition Width="*"/>    <!-- Warning Msg -->
                <ColumnDefinition Width="Auto"/> <!-- Action Button -->
                <ColumnDefinition Width="Auto"/> <!-- Close Button -->
            </Grid.ColumnDefinitions>

            <!-- Warning Icon -->
            <Path Grid.Column="0"
                  Data="{StaticResource Synthetic.Geometries.Warning}"
                  Fill="{DynamicResource Synthetic.Brushes.Warning}"
                  Width="24" Height="24"
                  Stretch="Uniform"
                  VerticalAlignment="Center"
                  Margin="0,0,10,0"/>

            <!-- Message -->
            <TextBlock Grid.Column="1"
                       Text="Project settings are out of sync with linked file."
                       Foreground="{DynamicResource Synthetic.Brushes.TextPrimary}"
                       FontSize="11.5"
                       FontWeight="SemiBold"
                       TextWrapping="Wrap"
                       VerticalAlignment="Center"
                       Margin="0,0,10,0"/>

            <!-- Resolve Button -->
            <Button Grid.Column="2"
                    Content="Resolve"
                    Click="OnResolveClick"
                    Style="{DynamicResource Synthetic.Styles.PrimaryButton}"
                    Width="65"
                    Height="24"
                    VerticalAlignment="Center"
                    Margin="0,0,5,0"
                    FontSize="11"/>

            <!-- Close Button -->
            <Button Grid.Column="3"
                    Content="✕"
                    Click="OnCloseClick"
                    Style="{DynamicResource Synthetic.Styles.NeutralButton}"
                    Width="20"
                    Height="20"
                    VerticalAlignment="Center"
                    FontSize="12"/>
        </Grid>
    </Border>
</Window>
```

### File: SettingsDashboard/Views/SyncToastNotification.xaml.cs
```csharp
using System;
using System.Windows;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

using Synthetic.Shared.UI;
using Synthetic.Core;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.Modules.SettingsDashboard.Handlers;

namespace Synthetic.Modules.SettingsDashboard.Views
{
    /// <summary>
    /// Interaction logic for SyncToastNotification.xaml.
    /// </summary>
    public partial class SyncToastNotification : Window
    {
        private readonly IntPtr _mainWindowHandle;
        private readonly Document _doc;
        private readonly string _filePath;
        private readonly Synthetic.Modules.SettingsDashboard.Handlers.SyncExternalEventHandler _handler;
        private readonly ExternalEvent _externalEvent;

        /// <summary>
        /// Initializes a new instance of the <see cref="SyncToastNotification"/> class.
        /// </summary>
        /// <param name="mainWindowHandle">The parent Revit main window handle.</param>
        /// <param name="doc">The active Revit Document.</param>
        /// <param name="filePath">The path to the linked settings configuration JSON file.</param>
        /// <param name="handler">The external event handler for settings sync.</param>
        /// <param name="externalEvent">The external event associated with the handler.</param>
        public SyncToastNotification(IntPtr mainWindowHandle, Document doc, string filePath, Synthetic.Modules.SettingsDashboard.Handlers.SyncExternalEventHandler handler, ExternalEvent externalEvent)
        {
            InitializeComponent();
            _mainWindowHandle = mainWindowHandle;
            _doc = doc;
            _filePath = filePath;
            _handler = handler ?? throw new ArgumentNullException(nameof(handler));
            _externalEvent = externalEvent ?? throw new ArgumentNullException(nameof(externalEvent));

            // Set parent owner handle if available
            RevitWindowHelper.SetOwner(this, _mainWindowHandle);
        }

        /// <inheritdoc/>
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            try
            {
                // Position the toast in the bottom-right corner of the working area
                var workArea = SystemParameters.WorkArea;
                this.Left = workArea.Right - this.Width - 10;
                this.Top = workArea.Bottom - this.Height - 10;
            }
            catch
            {
                // Safe positioning fallback
                this.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }
        }

        private void OnResolveClick(object sender, RoutedEventArgs e)
        {
            try
            {
                var resolutionWindow = new SyncResolutionWindow(_mainWindowHandle, _doc, _filePath, _handler, _externalEvent);
                resolutionWindow.ShowDialog();
            }
            catch (Exception ex)
            {
                Autodesk.Revit.UI.TaskDialog.Show("Resolution Error", $"Failed to open resolution window: {ex.Message}");
            }
            finally
            {
                this.Close();
            }
        }

        private void OnCloseClick(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}
```

### File: SettingsDashboard/Views/SyncWizardWindow.xaml
```xml
<Window x:Class="Synthetic.Modules.SettingsDashboard.Views.SyncWizardWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:local="clr-namespace:Synthetic.Shared.UI"
        Title="Configure Passive Sync Settings" 
        Height="180" Width="550" 
        WindowStartupLocation="CenterOwner" 
        ResizeMode="NoResize" 
        ShowInTaskbar="False"
        Style="{DynamicResource SyntheticWindowStyle}"
        local:WindowChromeBehavior.EnableWindowCommands="True">

    <Window.Resources>
        <ResourceDictionary>
            <ResourceDictionary.MergedDictionaries>
                <ResourceDictionary Source="../../../Shared/UI/SyntheticTheme.xaml"/>
            </ResourceDictionary.MergedDictionaries>
        </ResourceDictionary>
    </Window.Resources>

    <Grid Margin="15">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/> <!-- Title / Desc -->
            <RowDefinition Height="Auto"/> <!-- File Path Selection -->
            <RowDefinition Height="*"/>    <!-- Spacer -->
            <RowDefinition Height="Auto"/> <!-- Footer Buttons -->
        </Grid.RowDefinitions>

        <!-- Title -->
        <TextBlock Grid.Row="0" 
                   Text="Passive Sync Settings File Link" 
                   FontSize="14" 
                   FontWeight="Bold" 
                   Foreground="{DynamicResource Synthetic.Brushes.TextPrimary}" 
                   Margin="0,0,0,15"/>

        <!-- Linked File Path Selection Row -->
        <Grid Grid.Row="1" Margin="0,0,0,15">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="110"/>
                <ColumnDefinition Width="*"/>
                <ColumnDefinition Width="80"/>
            </Grid.ColumnDefinitions>

            <TextBlock Grid.Column="0" 
                       Text="Linked File JSON:" 
                       VerticalAlignment="Center" 
                       Foreground="{DynamicResource Synthetic.Brushes.TextSecondary}" 
                       FontSize="12"/>

            <TextBox Grid.Column="1" 
                     Text="{Binding LinkedFilePath, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}" 
                     Margin="0,0,10,0"/>

            <Button Grid.Column="2" 
                    Content="Browse..." 
                    Command="{Binding BrowseCommand}"/>
        </Grid>

        <!-- Footer -->
        <StackPanel Grid.Row="3" 
                     Orientation="Horizontal" 
                     HorizontalAlignment="Right">
            <Button Content="OK" 
                    Command="{Binding OkCommand}" 
                    CommandParameter="{Binding RelativeSource={RelativeSource AncestorType=Window}}"
                    Style="{DynamicResource Synthetic.Styles.PrimaryButton}"
                    Margin="0,0,10,0"/>
            <Button Content="Cancel" 
                    Command="{Binding CancelCommand}" 
                    CommandParameter="{Binding RelativeSource={RelativeSource AncestorType=Window}}"
                    Style="{DynamicResource Synthetic.Styles.SecondaryButton.Right}"/>
        </StackPanel>
    </Grid>
</Window>
```

### File: SettingsDashboard/Views/SyncWizardWindow.xaml.cs
```csharp
using System;
using System.Windows;

using Synthetic.Shared.UI;
using Synthetic.Core;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;

namespace Synthetic.Modules.SettingsDashboard.Views
{
    /// <summary>
    /// Interaction logic for SyncWizardWindow.xaml.
    /// </summary>
    public partial class SyncWizardWindow : Window
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SyncWizardWindow"/> class.
        /// </summary>
        /// <param name="mainWindowHandle">The parent Revit main window handle.</param>
        public SyncWizardWindow(IntPtr mainWindowHandle)
        {
            InitializeComponent();
            RevitWindowHelper.SetOwner(this, mainWindowHandle);
        }
    }
}
```

### File: SettingsDashboard/Views/ViewAutoNumWizardWindow.xaml
```xml
<Window x:Class="Synthetic.Modules.SettingsDashboard.Views.ViewAutoNumWizardWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:local="clr-namespace:Synthetic.Shared.UI"
        Title="Configure View Autonumbering Settings" 
        Height="280" Width="450" 
        WindowStartupLocation="CenterOwner" 
        ResizeMode="NoResize" 
        ShowInTaskbar="False"
        Style="{DynamicResource SyntheticWindowStyle}"
        local:WindowChromeBehavior.EnableWindowCommands="True">

    <Window.Resources>
        <ResourceDictionary>
            <ResourceDictionary.MergedDictionaries>
                <ResourceDictionary Source="../../../Shared/UI/SyntheticTheme.xaml"/>
            </ResourceDictionary.MergedDictionaries>
        </ResourceDictionary>
    </Window.Resources>

    <Grid Margin="15">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/> <!-- Title -->
            <RowDefinition Height="Auto"/> <!-- Family Name -->
            <RowDefinition Height="Auto"/> <!-- Type Name -->
            <RowDefinition Height="Auto"/> <!-- X Param -->
            <RowDefinition Height="Auto"/> <!-- Y Param -->
            <RowDefinition Height="*"/>    <!-- Spacer -->
            <RowDefinition Height="Auto"/> <!-- Buttons -->
        </Grid.RowDefinitions>

        <!-- Title -->
        <TextBlock Grid.Row="0" 
                   Text="Autonumber Location Marker Configuration" 
                   FontSize="14" 
                   FontWeight="Bold" 
                   Foreground="{DynamicResource Synthetic.Brushes.TextPrimary}" 
                   Margin="0,0,0,15"/>

        <!-- Family Name -->
        <Grid Grid.Row="1" Margin="0,0,0,10">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="130"/>
                <ColumnDefinition Width="*"/>
            </Grid.ColumnDefinitions>

            <TextBlock Grid.Column="0" 
                       Text="Family Name:" 
                       VerticalAlignment="Center" 
                       Foreground="{DynamicResource Synthetic.Brushes.TextSecondary}" 
                       FontSize="12"/>

            <ComboBox Grid.Column="1" 
                      ItemsSource="{Binding AvailableFamilies}" 
                      SelectedItem="{Binding SelectedFamily, Mode=TwoWay}"/>
        </Grid>

        <!-- Type Name -->
        <Grid Grid.Row="2" Margin="0,0,0,10">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="130"/>
                <ColumnDefinition Width="*"/>
            </Grid.ColumnDefinitions>

            <TextBlock Grid.Column="0" 
                       Text="Type Name:" 
                       VerticalAlignment="Center" 
                       Foreground="{DynamicResource Synthetic.Brushes.TextSecondary}" 
                       FontSize="12"/>

            <ComboBox Grid.Column="1" 
                      ItemsSource="{Binding AvailableTypes}" 
                      SelectedItem="{Binding SelectedType, Mode=TwoWay}"/>
        </Grid>

        <!-- X Grid Spacing Parameter -->
        <Grid Grid.Row="3" Margin="0,0,0,10">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="130"/>
                <ColumnDefinition Width="*"/>
            </Grid.ColumnDefinitions>

            <TextBlock Grid.Column="0" 
                       Text="Grid Size X Parameter:" 
                       VerticalAlignment="Center" 
                       Foreground="{DynamicResource Synthetic.Brushes.TextSecondary}" 
                       FontSize="12"/>

            <TextBox Grid.Column="1" 
                     Text="{Binding XGridName, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}"/>
        </Grid>

        <!-- Y Grid Spacing Parameter -->
        <Grid Grid.Row="4" Margin="0,0,0,10">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="130"/>
                <ColumnDefinition Width="*"/>
            </Grid.ColumnDefinitions>

            <TextBlock Grid.Column="0" 
                       Text="Grid Size Y Parameter:" 
                       VerticalAlignment="Center" 
                       Foreground="{DynamicResource Synthetic.Brushes.TextSecondary}" 
                       FontSize="12"/>

            <TextBox Grid.Column="1" 
                     Text="{Binding YGridName, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}"/>
        </Grid>

        <!-- Footer -->
        <StackPanel Grid.Row="6" 
                     Orientation="Horizontal" 
                     HorizontalAlignment="Right">
            <Button Content="OK" 
                    Command="{Binding OkCommand}" 
                    CommandParameter="{Binding RelativeSource={RelativeSource AncestorType=Window}}"
                    Style="{DynamicResource Synthetic.Styles.PrimaryButton}"
                    Margin="0,0,10,0"/>
            <Button Content="Cancel" 
                    Command="{Binding CancelCommand}" 
                    CommandParameter="{Binding RelativeSource={RelativeSource AncestorType=Window}}"
                    Style="{DynamicResource Synthetic.Styles.SecondaryButton.Right}"/>
        </StackPanel>
    </Grid>
</Window>
```

### File: SettingsDashboard/Views/ViewAutoNumWizardWindow.xaml.cs
```csharp
using System;
using System.Windows;

using Synthetic.Shared.UI;
using Synthetic.Core;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;

namespace Synthetic.Modules.SettingsDashboard.Views
{
    /// <summary>
    /// Interaction logic for ViewAutoNumWizardWindow.xaml.
    /// </summary>
    public partial class ViewAutoNumWizardWindow : Window
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ViewAutoNumWizardWindow"/> class.
        /// </summary>
        /// <param name="mainWindowHandle">The parent Revit main window handle.</param>
        public ViewAutoNumWizardWindow(IntPtr mainWindowHandle)
        {
            InitializeComponent();
            RevitWindowHelper.SetOwner(this, mainWindowHandle);
        }
    }
}
```

### File: SettingsDashboard/Views/WorksetWizardWindow.xaml
```xml
<Window x:Class="Synthetic.Modules.SettingsDashboard.Views.WorksetWizardWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:local="clr-namespace:Synthetic.Shared.UI"
        Title="Configure Worksets Settings" 
        Height="250" Width="550" 
        WindowStartupLocation="CenterOwner" 
        ResizeMode="NoResize" 
        ShowInTaskbar="False"
        Style="{DynamicResource SyntheticWindowStyle}"
        local:WindowChromeBehavior.EnableWindowCommands="True">

    <Window.Resources>
        <ResourceDictionary>
            <ResourceDictionary.MergedDictionaries>
                <ResourceDictionary Source="../../../Shared/UI/SyntheticTheme.xaml"/>
            </ResourceDictionary.MergedDictionaries>
        </ResourceDictionary>
    </Window.Resources>

    <Grid Margin="15">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/> <!-- Title / Desc -->
            <RowDefinition Height="Auto"/> <!-- Excel Path Selection -->
            <RowDefinition Height="Auto"/> <!-- Workset Group Selection -->
            <RowDefinition Height="*"/>    <!-- Spacer -->
            <RowDefinition Height="Auto"/> <!-- Footer Buttons -->
        </Grid.RowDefinitions>

        <!-- Title -->
        <TextBlock Grid.Row="0" 
                   Text="Excel Mapping &amp; Configuration" 
                   FontSize="14" 
                   FontWeight="Bold" 
                   Foreground="{DynamicResource Synthetic.Brushes.TextPrimary}" 
                   Margin="0,0,0,15"/>

        <!-- Excel File Row -->
        <Grid Grid.Row="1" Margin="0,0,0,15">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="100"/>
                <ColumnDefinition Width="*"/>
                <ColumnDefinition Width="80"/>
            </Grid.ColumnDefinitions>

            <TextBlock Grid.Column="0" 
                       Text="Excel File:" 
                       VerticalAlignment="Center" 
                       Foreground="{DynamicResource Synthetic.Brushes.TextSecondary}" 
                       FontSize="12"/>

            <TextBox Grid.Column="1" 
                     Text="{Binding FullPath, Mode=OneWay}" 
                     IsReadOnly="True" 
                     Margin="0,0,10,0"/>

            <Button Grid.Column="2" 
                    Content="Browse..." 
                    Command="{Binding BrowseCommand}"
                    CommandParameter="{Binding RelativeSource={RelativeSource AncestorType=Window}}"/>
        </Grid>

        <!-- Workset Group Row -->
        <Grid Grid.Row="2">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="100"/>
                <ColumnDefinition Width="*"/>
            </Grid.ColumnDefinitions>

            <TextBlock Grid.Column="0" 
                       Text="Workset Group:" 
                       VerticalAlignment="Center" 
                       Foreground="{DynamicResource Synthetic.Brushes.TextSecondary}" 
                       FontSize="12"/>

            <Grid Grid.Column="1">
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="*"/>
                    <ColumnDefinition Width="Auto"/>
                </Grid.ColumnDefinitions>

                <ComboBox Grid.Column="0" 
                          ItemsSource="{Binding AvailableGroups}" 
                          SelectedItem="{Binding SelectedGroup, Mode=TwoWay}">
                    <ComboBox.Style>
                        <Style TargetType="ComboBox" BasedOn="{StaticResource {x:Type ComboBox}}">
                            <Style.Triggers>
                                <DataTrigger Binding="{Binding IsLoading}" Value="True">
                                    <Setter Property="IsEnabled" Value="False"/>
                                </DataTrigger>
                            </Style.Triggers>
                        </Style>
                    </ComboBox.Style>
                </ComboBox>

                <!-- Spinner / Status text -->
                <TextBlock Grid.Column="1" 
                           Text="Loading sheets..." 
                           Foreground="{DynamicResource Synthetic.Brushes.Accent}" 
                           FontStyle="Italic" 
                           VerticalAlignment="Center" 
                           Margin="10,0,0,0">
                    <TextBlock.Style>
                        <Style TargetType="TextBlock" BasedOn="{StaticResource {x:Type TextBlock}}">
                            <Setter Property="Visibility" Value="Collapsed"/>
                            <Style.Triggers>
                                <DataTrigger Binding="{Binding IsLoading}" Value="True">
                                    <Setter Property="Visibility" Value="Visible"/>
                                </DataTrigger>
                            </Style.Triggers>
                        </Style>
                    </TextBlock.Style>
                </TextBlock>
            </Grid>
        </Grid>

        <!-- Footer -->
        <StackPanel Grid.Row="4" 
                     Orientation="Horizontal" 
                     HorizontalAlignment="Right">
            <Button Content="OK" 
                    Command="{Binding OkCommand}" 
                    CommandParameter="{Binding RelativeSource={RelativeSource AncestorType=Window}}"
                    Style="{DynamicResource Synthetic.Styles.PrimaryButton}"
                    Margin="0,0,10,0"/>
            <Button Content="Cancel" 
                    Command="{Binding CancelCommand}" 
                    CommandParameter="{Binding RelativeSource={RelativeSource AncestorType=Window}}"
                    Style="{DynamicResource Synthetic.Styles.SecondaryButton.Right}"/>
        </StackPanel>
    </Grid>
</Window>
```

### File: SettingsDashboard/Views/WorksetWizardWindow.xaml.cs
```csharp
using System;
using System.Windows;

using Synthetic.Shared.UI;
using Synthetic.Core;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;

namespace Synthetic.Modules.SettingsDashboard.Views
{
    /// <summary>
    /// Interaction logic for WorksetWizardWindow.xaml.
    /// </summary>
    public partial class WorksetWizardWindow : Window
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="WorksetWizardWindow"/> class.
        /// </summary>
        /// <param name="mainWindowHandle">The parent Revit main window handle.</param>
        public WorksetWizardWindow(IntPtr mainWindowHandle)
        {
            InitializeComponent();
            RevitWindowHelper.SetOwner(this, mainWindowHandle);
        }
    }
}
```

