using Synthetic.Modules.AutoTagger.Commands;
using Synthetic.Modules.AutoTagger.Models;
using Synthetic.Modules.AutoTagger.ViewModels;
using Synthetic.Modules.AutoTagger.Views;
using Synthetic.Modules.AutoTagger.Repositories;

using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

using Synthetic.Shared.RevitAPI;
using Synthetic.Infrastructure.IO;

using Synthetic.Shared.UI;

using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.Modules.RevitDOM;

namespace Synthetic.Modules.AutoTagger.Commands
{
    /// <summary>
    /// Revit external command to set a tag template.
    /// Prompts the user to select a host element and its associated tag, then registers the configuration.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class CmdSetTemplate : IExternalCommand
    {
        /// <summary>
        /// Executes the set template command.
        /// </summary>
        /// <param name="commandData">Revit external command data.</param>
        /// <param name="message">A message returning errors if any.</param>
        /// <param name="elements">Revit elements set.</param>
        /// <returns>Result code of the execution.</returns>
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiapp = commandData.Application;
            UIDocument uidoc = uiapp.ActiveUIDocument;
            Document doc = uidoc.Document;
            try
            {
                Reference hostRef = uidoc.Selection.PickObject(ObjectType.Element, new FamilyInstanceFilter(), "Select a Host Element (FamilyInstance).");
                FamilyInstance? host = doc.GetElement(hostRef) as FamilyInstance;
                Reference tagRef = uidoc.Selection.PickObject(ObjectType.Element, new IndependentTagFilter(), "Select the associated Independent Tag.");
                IndependentTag? tag = doc.GetElement(tagRef) as IndependentTag;
                if (host == null || tag == null) return Result.Cancelled;
                XYZ? localOffset = CoordinateUtility.GetLocalOffset(host, tag.TagHeadPosition);
                                
                if (localOffset == null)
                {
                    Autodesk.Revit.UI.TaskDialog.Show("Transformation Error", "Could not calculate the coordinate transform. The host element geometry may not be conformal.");
                    return Result.Failed;
                }
                SetTemplateViewModel vm = new SetTemplateViewModel(doc, host, localOffset!, tag.TagOrientation);
                SetTemplateView view = new SetTemplateView(uiapp.MainWindowHandle) { DataContext = vm };
                vm.CloseAction = new Action(view.Close);
                                
                view.ShowDialog();
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return Result.Cancelled;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }

        private class FamilyInstanceFilter : ISelectionFilter
        {
            public bool AllowElement(Element elem) => elem is FamilyInstance;
            public bool AllowReference(Reference reference, XYZ position) => false;
        }

        private class IndependentTagFilter : ISelectionFilter
        {
            public bool AllowElement(Element elem) => elem is IndependentTag;
            public bool AllowReference(Reference reference, XYZ position) => false;
        }
    }
}
