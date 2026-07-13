using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using View = Autodesk.Revit.DB.View;
using Autodesk.Revit.UI;
using Synthetic.Modules.MaterialManagement.Commands;
using Synthetic.Modules.MaterialManagement.Utilities;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Synthetic.Shared.UI;

using Synthetic.Shared.RevitAPI;
using Synthetic.Infrastructure.IO;
using Synthetic.Modules.RevitDOM;
using Synthetic.Modules.StandardsManagement.ViewModels;

namespace Synthetic.Modules.MaterialManagement.Commands
{
    /// <summary>
    /// Paints all faces of selected elements with a material
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class PaintElements : IExternalCommand
    {
        /// <summary>
        /// Execute a Revit Command
        /// </summary>
        /// <param name="commandData">commandData</param>
        /// <param name="message">message</param>
        /// <param name="elementSet">Currently selected elements</param>
        /// <returns>A Autodesk.Revit.UI.Result</returns>
        public Result Execute(
          ExternalCommandData commandData,
          ref string message,
          ElementSet elementSet)
        {
            UIApplication uiapp = commandData.Application;
            UIDocument uidoc = uiapp.ActiveUIDocument;
            Document document = uidoc.Document;

            ICollection<ElementId> elemIds = uidoc.Selection.GetElementIds();

            if (elemIds.Count > 0)
            {
                FilteredElementCollector collector = new FilteredElementCollector(document)
                    .OfClass(typeof(Material));

                List<string> names = collector.Select(x => x.Name).ToList();
                string? selectedMaterial = SelectMaterial(names, uiapp.MainWindowHandle);
                
                if (string.IsNullOrEmpty(selectedMaterial))
                {
                    return Result.Cancelled;
                }

                Material? material = MaterialUtil.GetByNameDocument(selectedMaterial!, document);
                if (material == null)
                {
                    return Result.Failed;
                }
                ElementId materialId = material.Id;

                string transactionName = "Paint Elements with material " + selectedMaterial;
                using (Autodesk.Revit.DB.Transaction trans = new Autodesk.Revit.DB.Transaction(document))
                {
                    trans.Start(transactionName);
                    try
                    {
                        foreach (ElementId id in elemIds)
                        {
                            Element element = document.GetElement(id);
                            ElementUtil.PaintElement(element, materialId);
                        }
                        trans.Commit();
                    }
                    catch { trans.RollBack(); }
                }
            }

            return Result.Succeeded;
        }

        internal string? SelectMaterial(List<string> materialNames, IntPtr mainWindowHandle)
        {
            DropdownSelectionViewModel viewModel = new DropdownSelectionViewModel();
            viewModel.Title = "Select a single material";
            viewModel.Instruction = "Elements will be painted with selected material";
            viewModel.ItemLabel = "Materials";
            viewModel.Items = materialNames;
            viewModel.IsSorted = true;

            DropdownSelectionView dialog = new DropdownSelectionView(mainWindowHandle) { DataContext = viewModel };
            bool? dResult = dialog.ShowDialog();

            return dResult == true ? viewModel.SelectedItem : null;
        }
    }
}
