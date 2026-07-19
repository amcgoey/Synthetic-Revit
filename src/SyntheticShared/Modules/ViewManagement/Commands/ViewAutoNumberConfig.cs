using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Synthetic.Settings;
using Synthetic.Infrastructure.Persistence;

using Synthetic.Shared.UI;

using Synthetic.Shared.RevitAPI;
using Synthetic.Infrastructure.IO;
using Synthetic.Modules.ViewManagement.Commands;
using Synthetic.Modules.ViewManagement.Models;
using Synthetic.Modules.ViewManagement.Utilities;

using System;
using System.Collections.Generic;
using System.Text;

using Synthetic.Core;
using Synthetic.Modules.MergeDuplicates.Handlers;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;
using Synthetic.Modules.StandardsManagement.ViewModels;
namespace Synthetic.Modules.ViewManagement.Commands
{
    /// <summary>
    /// Revit Command to configure the View Autonumber command.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class ViewAutoNumberConfig : IExternalCommand
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

            ViewAutoNumSettings settings = new ViewAutoNumSettings();

            List<string> itemList = new List<string>();
            Dictionary<string, FamilySymbol> choices = new Dictionary<string, FamilySymbol>();
            IList<Element> familyList = FamilySymbolUtil.GetFamiliesOfCategory(doc, BuiltInCategory.OST_GenericAnnotation);

            if (familyList != null)
            {
                foreach (FamilySymbol familySymbol in familyList)
                {
                    string itemName = familySymbol.Family.Name + " | " + familySymbol.Name;
                    choices.Add(itemName, familySymbol);
                    itemList.Add(itemName);
                }

                var r = SelectFamily(itemList, uiapp.MainWindowHandle);
                bool dResult = r.result;
                string? selection = r.selection;

                if (dResult && selection != null)
                {
                    FamilySymbol familySymbol = choices[selection];

                    if (familySymbol != null)
                    {
                        List<string> itemList2 = new List<string>();

                        ParameterSet xGridParameters = familySymbol.Parameters;
                        foreach (Parameter parameter in xGridParameters)
                        {
                            itemList2.Add(parameter.Definition.Name);
                        }

                        r = SelectGrid(itemList2, true, uiapp.MainWindowHandle);
                        bool dResult2 = r.result;
                        string? viewAutoNumXGridName = r.selection;

                        if (dResult2 && viewAutoNumXGridName != null)
                        {
                            List<string> itemList3 = new List<string>();

                            ParameterSet yGridParameters = familySymbol.Parameters;
                            foreach (Parameter parameter in yGridParameters)
                            {
                                itemList3.Add(parameter.Definition.Name);
                            }

                            r = SelectGrid(itemList2, false, uiapp.MainWindowHandle);
                            bool dResult3 = r.result;
                            string? viewAutoNumYGridName = r.selection;

                            if (dResult3 && viewAutoNumYGridName != null)
                            {
                                settings.ViewAutoNumFamily = familySymbol.Family.Name;
                                settings.ViewAutoNumFamilyType = familySymbol.Name;
                                settings.ViewAutoNumXGridName = viewAutoNumXGridName;
                                settings.ViewAutoNumYGridName = viewAutoNumYGridName;

                                config?.SetSettings(ViewAutoNumSettings.Name, settings);
                            }
                        }
                    }
                }
            }
            return Result.Succeeded;
        }

        /// <summary>
        /// Opens dialog to select the family to use for the origin and parameters for the view renumbering
        /// </summary>
        internal (bool result, string? selection) SelectFamily(List<string> itemList, IntPtr mainWindowHandle)
        {
            DropdownSelectionViewModel viewModel = new DropdownSelectionViewModel();
            viewModel.Title = "Autonumber View Configuration";
            viewModel.Instruction = "Select a Generic Annotation Family";
            viewModel.ItemLabel = "Family & Type";
            viewModel.Items = itemList;

            DropdownSelectionView dialog = new DropdownSelectionView(mainWindowHandle) { DataContext = viewModel };
            bool? dResult = dialog.ShowDialog();
            string? selection = viewModel.SelectedItem;

            return (dResult == true, selection);
        }

        /// <summary>
        /// Opens dialog box to select the Grid Parameters with options for X Grid or Y Grid
        /// </summary>
        internal (bool result, string? selection) SelectGrid(List<string> itemList, bool IsXGrid, IntPtr mainWindowHandle)
        {
            DropdownSelectionViewModel viewModel = new DropdownSelectionViewModel();
            viewModel.Title = "Autonumber View Configuration";
            viewModel.Instruction = "Select a parameter that determines the " + ((IsXGrid) ? "X" : "Y") + " Grid Spacing";
            viewModel.ItemLabel = "Parameter";
            viewModel.Items = itemList;
            viewModel.IsSorted = true;

            DropdownSelectionView dialog = new DropdownSelectionView(mainWindowHandle) { DataContext = viewModel };
            bool? dResult = dialog.ShowDialog();
            string? selection = viewModel.SelectedItem;

            return (dResult == true, selection);
        }
    }
}
