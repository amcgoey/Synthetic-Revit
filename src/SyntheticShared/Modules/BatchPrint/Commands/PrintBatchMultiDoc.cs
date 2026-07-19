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
