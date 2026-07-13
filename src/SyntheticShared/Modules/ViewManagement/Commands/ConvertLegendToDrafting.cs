using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

using Synthetic.Shared.RevitAPI;
using Synthetic.Infrastructure.IO;

using Synthetic.Shared.UI;
using Synthetic.Modules.ViewManagement.Commands;
using Synthetic.Modules.ViewManagement.Models;
using Synthetic.Modules.ViewManagement.Utilities;

using System;
using System.Collections.Generic;
using System.Linq;
using View = Autodesk.Revit.DB.View;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.Modules.RevitDOM;

namespace Synthetic.Modules.ViewManagement.Commands
{
    /// <summary>
    /// Converts Legend Views to Drafting Views
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class ConvertLegendToDrafting : IExternalCommand
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
            Document doc = uidoc.Document;

            string transactionName = "Convert Legend Views to Drafting";

            List<View> legendViews = (List<View>)new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .OfCategory(BuiltInCategory.OST_Views)
                .OfType<View>()
                .Where(v => v.ViewType == ViewType.Legend)
                .ToList();

            IList<View> views = SelectViews(legendViews, uiapp.MainWindowHandle);

            IList<View> drafting = new List<View>();

            using (Autodesk.Revit.DB.Transaction trans = new Autodesk.Revit.DB.Transaction(doc))
            {
                trans.Start(transactionName);
                foreach (View view in views)
                {
                    View? draftingView = LegendsUtil.ConvertToDrafting(view);
                    if (draftingView != null)
                    {
                        drafting.Add(draftingView);
                    }
                }
                trans.Commit();
            }
            
            return Result.Succeeded;
        }

        internal IList<View> SelectViews(IList<View> views, IntPtr mainWindowHandle)
        {
            List<string> itemList = new List<string>();
            List<View> selectedViews = new List<View>();

            ListByCheckboxViewModel viewModel = new ListByCheckboxViewModel();
            viewModel.Title = "Select Legend Views";
            viewModel.Instruction = "The selected Legend Views will be converted to Drafting Views";
            viewModel.IsSingleSelection = false;

            foreach (View view in views)
            {
                itemList.Add(view.Name);
            }
            viewModel.SetItems(itemList, false);

            ListByCheckboxView viewWindow = new ListByCheckboxView(mainWindowHandle) { DataContext = viewModel };

            bool? dialogResult = viewWindow.ShowDialog();

            if (dialogResult == true)
            {
                List<string> selectedList = viewModel.CheckedItems;
                foreach (string selectedItem in selectedList)
                {
                    View v = views.First(s => s.Name == selectedItem);
                    selectedViews.Add(v);
                }
            }
            return selectedViews;
        }
    }
}
