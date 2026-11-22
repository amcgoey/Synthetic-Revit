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
