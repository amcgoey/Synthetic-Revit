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
    /// Revit external command to manage tag templates.
    /// Provides a user interface to view, create, edit, or delete tag templates.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class CmdManageTemplates : IExternalCommand
    {
        /// <summary>
        /// Executes the template manager command.
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
            bool keepOpen = true;
            
            // State Machine Loop: Allows transitioning between WPF Dialog (Thread Blocked) 
            // and Revit API Canvas Interaction without losing command context.
            while (keepOpen)
            {
                var vm = new ManageTemplatesViewModel(doc);
                var view = new ManageTemplatesView(uiapp.MainWindowHandle) { DataContext = vm };
                vm.CloseAction = new Action(view.Close);
                
                view.ShowDialog();

                if (vm.RequestedAction == ManageTemplatesAction.NewTemplate)
                {
                    try
                    {
                        Reference hostRef = uidoc.Selection.PickObject(ObjectType.Element, new FamilyInstanceFilter(), 
                            "Select a Host Element (FamilyInstance) for the new template.");
                        FamilyInstance? host = doc.GetElement(hostRef) as FamilyInstance;
                        Reference tagRef = uidoc.Selection.PickObject(ObjectType.Element, new IndependentTagFilter(), 
                            "Select the associated Independent Tag.");
                        IndependentTag? tag = doc.GetElement(tagRef) as IndependentTag;
                        if (host != null && tag != null)
                        {
                            XYZ? localOffset = CoordinateUtility.GetLocalOffset(host, tag.TagHeadPosition);
                            if (localOffset != null)
                            {
                                SetTemplateViewModel setVm = new SetTemplateViewModel(doc, host, localOffset!, tag.TagOrientation);
                                SetTemplateView setView = new SetTemplateView(uiapp.MainWindowHandle) { DataContext = setVm };
                                setVm.CloseAction = new Action(setView.Close);
                                setView.ShowDialog();
                            }
                            else
                            {
                                Autodesk.Revit.UI.TaskDialog.Show("Transformation Error", "Could not calculate the coordinate transform.");
                            }
                        }
                    }
                    catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                    {
                        // User pressed ESC during PickObject. Swallow exception to allow the while loop to re-open the manager.
                    }
                    catch (Exception ex)
                    {
                        Autodesk.Revit.UI.TaskDialog.Show("Error", ex.Message);
                    }
                }
                else if (vm.RequestedAction == ManageTemplatesAction.EditTemplate && vm.TemplateToEdit != null)
                {
                    try
                    {
                        Reference hostRef = uidoc.Selection.PickObject(ObjectType.Element, new FamilyInstanceFilter(), 
                            $"Select new Host Element for template: {vm.TemplateToEdit.TemplateName}");
                        FamilyInstance? host = doc.GetElement(hostRef) as FamilyInstance;
                        Reference tagRef = uidoc.Selection.PickObject(ObjectType.Element, new IndependentTagFilter(), 
                            "Select the associated Independent Tag.");
                        IndependentTag? tag = doc.GetElement(tagRef) as IndependentTag;
                        if (host != null && tag != null)
                        {
                            XYZ? localOffset = CoordinateUtility.GetLocalOffset(host, tag.TagHeadPosition);
                            if (localOffset != null)
                            {
                                SetTemplateViewModel setVm = new SetTemplateViewModel(doc, host, localOffset!, tag.TagOrientation, vm.TemplateToEdit);
                                SetTemplateView setView = new SetTemplateView(uiapp.MainWindowHandle) { DataContext = setVm };
                                setVm.CloseAction = new Action(setView.Close);
                                setView.ShowDialog();
                            }
                            else
                            {
                                Autodesk.Revit.UI.TaskDialog.Show("Transformation Error", "Could not calculate the coordinate transform.");
                            }
                        }
                    }
                    catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                    {
                        // User pressed ESC during PickObject. Swallow exception to allow the while loop to re-open the manager.
                    }
                    catch (Exception ex)
                    {
                        Autodesk.Revit.UI.TaskDialog.Show("Error", ex.Message);
                    }
                }
                else
                {
                    // User closed the window or clicked the X. Terminate the state loop.
                    keepOpen = false;
                }
            }
            return Result.Succeeded;
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
