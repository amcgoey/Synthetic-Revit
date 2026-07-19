using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

using Synthetic.Shared.RevitAPI;
using Synthetic.Infrastructure.IO;
using SFileUtil = Synthetic.Infrastructure.IO.FileUtil;

using Synthetic.Shared.UI;
using Synthetic.Core;
using Synthetic.Modules.MaterialManagement.Commands;
using Synthetic.Modules.MaterialManagement.Utilities;

using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using System.IO;
using Application = Autodesk.Revit.ApplicationServices.Application;
using Synthetic.Settings;
using Synthetic.Infrastructure.Persistence;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;

namespace Synthetic.Modules.MaterialManagement.Commands
{
    /// <summary>
    /// Revit external command to package and transmit all material bitmap images to a new directory.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class MaterialImagesPackage : IExternalCommand
    {
        /// <summary>
        /// The button label text for this command in the Revit UI.
        /// </summary>
        public const string CommandButton = " Transmit \nMaterial \nImages ";

        /// <summary>
        /// The tooltip message for this command in the Revit UI.
        /// </summary>
        public const string CommandTooltip = "Copy all material bitmap images to a new folder location.";

        /// <summary>
        /// The full class path/name of this command.
        /// </summary>
        public static string? CommandPath = typeof(MaterialImagesPackage).FullName;

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

            Result commandResult = Result.Succeeded;

            MaterialLibrarySettings libSettings = SettingsManager.Get<MaterialLibrarySettings>(doc);
            ProjectMaterialSettings projSettings = SettingsManager.Get<ProjectMaterialSettings>(doc);

            string? LibraryPath = libSettings.LibraryFolderPath;
            string? ProjectPath = projSettings.GetResolvedPath(doc);
            List<string> defaultPaths = new List<string>();
            if (ProjectPath != null) defaultPaths.Add(ProjectPath);
            if (LibraryPath != null) defaultPaths.Add(LibraryPath);
            string? path = SelectPath(uiapp.MainWindowHandle);

            Dictionary<string, Dictionary<string, object>>? results = null;

            if (path != null && path != string.Empty)
            {
                List<Material> allMaterials = MaterialUtil.GetAllMaterials(doc).Cast<Material>().ToList();
                List<Material> selectedMaterials = SelectMaterials(allMaterials, uiapp.MainWindowHandle);

                List<string> pathList = new List<string>();
                pathList.Add(path);

                if (selectedMaterials != null && selectedMaterials.Count > 0)
                {
                    List<string> filePaths = new List<string>();
                    foreach (Material material in selectedMaterials)
                    {
                        List<string>? tempPaths = MaterialUtil.GetMaterialBitmapPaths(material);
                        if (tempPaths != null && tempPaths.Count > 0)
                        {
                            filePaths.AddRange(tempPaths);
                        }
                    }
                    List<string> rootNames = new List<string>() { "Materials","INC Material Maps", "Maps", "_Maps", "Substance" };
                    results = SFileUtil.CopyFiles(filePaths, path, rootNames);
                }
                else { commandResult = Result.Failed; }
            }
            else { commandResult = Result.Failed; }
            
            if (results != null)
            {
                CommandUtil.SaveResults(results, doc, "Transmit Material Images", path);
            }

            return commandResult;
        }

        /// <summary>
        /// Allows the user to browse for a folder location
        /// </summary>
        internal string? SelectPath(IntPtr ownerHandle)
        {
            return FileDialogHelper.SelectFolder(ownerHandle, "Select Folder to Transmit Images To");
        }

        /// <summary>
        /// Allows the user to add paths and select from a list of default paths for constructing a SearchPath
        /// </summary>
        internal List<string> SelectSearchPaths(List<string> defaultPaths, IntPtr mainWindowHandle)
        {
            SelectSearchPathsViewModel viewModel = new SelectSearchPathsViewModel();
            viewModel.Title = "List of Paths to Exclude";
            viewModel.Instruction = "Image files located in the checked User Paths and Default Paths will be excluded from being copied.  This is to allow";
            
            foreach (var p in defaultPaths)
            {
                viewModel.DefaultPaths.Add(new CheckableItem(p, false));
            }

            SelectSearchPathsView dialog = new SelectSearchPathsView(mainWindowHandle) { DataContext = viewModel };

            bool? dResult = dialog.ShowDialog();

            List<string> pathList = new List<string>();
            if (dResult == true)
            {
                if (viewModel.CheckedItems != null)
                {
                    pathList = viewModel.CheckedItems;
                }
            }
            return pathList;
        }

        /// <summary>
        /// Selects materials via checklist dialog
        /// </summary>
        internal List<Material> SelectMaterials(List<Material> materials, IntPtr mainWindowHandle)
        {
            List<string> itemList = new List<string>();
            List<Material> selectedMaterials = new List<Material>();

            ListByCheckboxViewModel viewModel = new ListByCheckboxViewModel();
            viewModel.Title = "Select Materials";
            viewModel.Instruction = "The selected Materials will have their images repathed based on the Search Paths previously selected.";
            viewModel.IsSingleSelection = false;

            foreach (Material material in materials)
            {
                itemList.Add(material.Name);
            }
            viewModel.SetItems(itemList, true);

            ListByCheckboxView dialog = new ListByCheckboxView(mainWindowHandle) { DataContext = viewModel };

            bool? dResult = dialog.ShowDialog();

            if (dResult == true)
            {
                List<string> selectedList = viewModel.CheckedItems;
                foreach (string selectedItem in selectedList)
                {
                    Material m = materials.First(s => s.Name == selectedItem);
                    selectedMaterials.Add(m);
                }
            }
            return selectedMaterials;
        }
    }
}
