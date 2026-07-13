using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

using Synthetic.Shared.RevitAPI;
using Synthetic.Infrastructure.IO;

using Synthetic.Shared.UI;
using Synthetic.Core;
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
    /// Converts Drafting Views to Legends
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class ConvertDraftingToLegend : IExternalCommand
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

            string transactionGroupName = "Convert Drafting Views to Legends";

            List<View> draftingViews = (List<View>)new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .OfCategory(BuiltInCategory.OST_Views)
                .OfType<View>()
                .Where(v => v.ViewType == ViewType.DraftingView)
                .ToList();

            IList<View> views = SelectViews(draftingViews, uiapp.MainWindowHandle);

            if (views == null || views.Count == 0)
            {
                return Result.Cancelled;
            }

            IList<View> legends = new List<View>();
            bool warningTripped = false;
            bool isCanceled = false;

            // Initialize Progress Bar Window
            ProgressCoordinator.Initialize("Convert Drafting to Legend", "Starting conversion...", views.Count);

            using (TransactionGroup transGroup = new TransactionGroup(doc, transactionGroupName))
            {
                try
                {
                    transGroup.Start();

                    foreach (View view in views)
                    {
                        if (ProgressCoordinator.IsCancelled())
                        {
                            isCanceled = true;
                            break;
                        }

                        View? legend = LegendsUtil.ConvertFromDrafting(view, ref warningTripped);
                        if (legend == null)
                        {
                            if (ProgressCoordinator.IsCancelled())
                            {
                                isCanceled = true;
                            }
                            else
                            {
                                throw new InvalidOperationException($"Conversion failed for view: {view.Name}");
                            }
                            break;
                        }

                        legends.Add(legend);
                        ProgressCoordinator.UpdateProgress(view.Name);
                    }

                    if (isCanceled)
                    {
                        transGroup.RollBack();
                        ProgressCoordinator.Close();
                        Autodesk.Revit.UI.TaskDialog.Show("Cancelled", "The conversion process was cancelled. All modifications have been rolled back.");
                        return Result.Cancelled;
                    }

                    transGroup.Assimilate();
                }
                catch (Exception ex)
                {
                    if (transGroup.GetStatus() == TransactionStatus.Started)
                    {
                        transGroup.RollBack();
                    }
                    ProgressCoordinator.Close();
                    Autodesk.Revit.UI.TaskDialog.Show("Error", $"An error occurred during conversion:\n{ex.Message}\n\nAll changes have been rolled back.");
                    return Result.Failed;
                }
            }

            ProgressCoordinator.Close();

            string successMsg = $"Successfully converted {legends.Count} Drafting Views to Legends.";
            if (warningTripped)
            {
                successMsg += "\n\nNote: Dimension/Constraint deletion warnings were encountered and suppressed when grouping reference planes.";
            }

            Autodesk.Revit.UI.TaskDialog.Show("Success", successMsg);
            return Result.Succeeded;
        }

        /// <summary>
        /// Displays a checkbox selection dialog allowing the user to choose views for batch conversion.
        /// </summary>
        /// <param name="views">The list of available views for selection.</param>
        /// <param name="mainWindowHandle">The parent main window handle.</param>
        /// <returns>A list of user-selected views.</returns>
        internal IList<View> SelectViews(IList<View> views, IntPtr mainWindowHandle)
        {
            List<string> itemList = new List<string>();
            List<View> selectedViews = new List<View>();

            ListByCheckboxViewModel viewModel = new ListByCheckboxViewModel();
            viewModel.Title = "Select Drafting Views";
            viewModel.Instruction = "The selected Drafting Views will be converted to Legends";
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
