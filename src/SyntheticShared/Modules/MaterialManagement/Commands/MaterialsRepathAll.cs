using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

using Synthetic.Shared.RevitAPI;
using Synthetic.Infrastructure.IO;

using Synthetic.Shared.UI;
using Synthetic.Modules.MaterialManagement.Commands;
using Synthetic.Modules.MaterialManagement.Utilities;

using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using Application = Autodesk.Revit.ApplicationServices.Application;
using Synthetic.Settings;
using Synthetic.Infrastructure.Persistence;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.Modules.RevitDOM;

namespace Synthetic.Modules.MaterialManagement.Commands
{
    /// <summary>
    /// Revit external command to repath material bitmap images based on search paths.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class MaterialsRepathAll : IExternalCommand
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

            Result commandResult = Result.Succeeded;

            MaterialLibrarySettings libSettings = SettingsManager.Get<MaterialLibrarySettings>(doc);
            ProjectMaterialSettings projSettings = SettingsManager.Get<ProjectMaterialSettings>(doc);

            string? LibraryPath = libSettings.LibraryFolderPath;
            string? ProjectPath = projSettings.GetResolvedPath(doc);
            List<string> defaultPaths = new List<string>();
            if (ProjectPath != null) defaultPaths.Add(ProjectPath);
            if (LibraryPath != null) defaultPaths.Add(LibraryPath);
            List<string>? pathList = SelectSearchPaths(defaultPaths, uiapp.MainWindowHandle);

            Dictionary<string, object>? resutls = null;

            if (pathList != null && pathList.Count > 0)
            {
                List<Material> allMaterials = MaterialUtil.GetAllMaterials(doc).Cast<Material>().ToList();
                List<Material> selectedMaterials = SelectMaterials(allMaterials, uiapp.MainWindowHandle);

                SearchPaths searchPaths = new SearchPaths(pathList);

                if (selectedMaterials != null && selectedMaterials.Count > 0)
                {
                    string transactionName = "Replace Bitmap Paths on Materials";
                    using (Autodesk.Revit.DB.Transaction trans = new Autodesk.Revit.DB.Transaction(doc))
                    {
                        trans.Start(transactionName);
                        resutls = MaterialUtil.ReplaceBitmapPaths(selectedMaterials, searchPaths, true, false);
                        trans.Commit();
                    }
                }
                else { commandResult = Result.Failed; }
            }
            else { commandResult = Result.Failed; }
            
            if (resutls != null)
            {
                CommandUtil.SaveResults(resutls, doc, "Material Repath Results");
            }
            
            return commandResult;
        }

        /// <summary>
        /// Allows the user to add paths and select from a list of default paths for constructing a SearchPath
        /// </summary>
        internal List<string> SelectSearchPaths(List<string> defaultPaths, IntPtr mainWindowHandle)
        {
            SelectSearchPathsViewModel viewModel = new SelectSearchPathsViewModel();
            viewModel.Title = "List of Paths to Search";
            viewModel.Instruction = "Add and select the folder paths that you want to search for files.  If there are duplicate file names, only the first file will be used.  This means the order of the paths is important.  User Selected paths will be searched in order before Default Paths.";
            
            foreach (var p in defaultPaths)
            {
                viewModel.DefaultPaths.Add(new CheckableItem(p, false));
            }
            viewModel.CheckAllDefaults();

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
