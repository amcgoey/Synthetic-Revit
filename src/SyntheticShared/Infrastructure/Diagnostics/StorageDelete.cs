using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using TaskDialog = Autodesk.Revit.UI.TaskDialog;
using Autodesk.Revit.DB.ExtensibleStorage;

using Synthetic.Shared.UI;

using Synthetic.Shared.RevitAPI;
using Synthetic.Infrastructure.IO;

using Synthetic.Infrastructure.Diagnostics;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.Modules.RevitDOM;
namespace Synthetic.Infrastructure.Diagnostics{
    /// <summary>
    /// Deletes all extensible storage created by any application all active documents.
    /// This command will also report if there is no storage in the active document to delete.
    /// The document must be saved after the storage is deleted to commit the deletion.
    /// </summary>
    [Autodesk.Revit.Attributes.Transaction(Autodesk.Revit.Attributes.TransactionMode.Manual)]
    [Autodesk.Revit.Attributes.Regeneration(Autodesk.Revit.Attributes.RegenerationOption.Manual)]
    public class StorageDelete : IExternalCommand
    {
        /// <summary>
        /// Executes the extensible storage deletion command.
        /// </summary>
        /// <param name="commandData">Revit external command data.</param>
        /// <param name="message">A message returning errors if any.</param>
        /// <param name="elements">Revit elements set.</param>
        /// <returns>Result code of the execution.</returns>
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiapp = commandData.Application;
            Document document = uiapp.ActiveUIDocument.Document;

            if (!(StorageUtil.DoesAnyStorageExist(document)))
                message = "No storage in this document to delete.";
            else
            {
                IList<Schema> schemas = Schema.ListSchemas();
                List<string> itemList = new List<string>();

                foreach (Schema schema in schemas)
                {
                    itemList.Add(schema.SchemaName + " | " + schema.GUID);
                }

                ListByCheckboxViewModel viewModel = new ListByCheckboxViewModel();
                viewModel.Title = "Select Extensible Storage Schemas to Delete";
                viewModel.Instruction = "Choose which Extensible Storage Schemas to delete from the active document.";
                viewModel.IsSingleSelection = false;
                viewModel.SetItems(itemList, false);

                ListByCheckboxView dialog = new ListByCheckboxView(uiapp.MainWindowHandle) { DataContext = viewModel };

                bool? dResult = dialog.ShowDialog();

                if (dResult == true)
                {
                    List<Schema> filteredSchemas = new List<Schema>();
                    foreach (Schema schema in schemas)
                    {
                        if (viewModel.CheckedItems.Contains(schema.SchemaName + " | " + schema.GUID))
                        {
                            filteredSchemas.Add(schema);
                        }
                    }
                    if (filteredSchemas.Count > 0)
                    {
                        List<string>? purgedSchema = StorageUtil.PurgeSchema(filteredSchemas, document);
                        if (purgedSchema != null && purgedSchema.Count > 0)
                        {
                            message = "Extensible storage was deleted.\n";
                            message = message + String.Join("\n", purgedSchema.ToArray());
                        }
                        else { message = "Error: There was a problem deleting the extensible storage."; }
                    }
                    else
                    {
                        message = "No Schema selected.  No action taken.";
                    }
                }
            }

            TaskDialog.Show("ExtensibleStorageUtility", message);
            return Result.Succeeded;
        }
    }
}
