using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.DB.ExtensibleStorage;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Synthetic.Shared.UI;
using Synthetic.Core;
using Synthetic;

using Synthetic.Shared.RevitAPI;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;
using Synthetic.Modules.FamilyManagement.Handlers;
using Synthetic.Modules.FamilyManagement.Utilities;
namespace Synthetic.Modules.FamilyManagement.Handlers
{
    /// <summary>
    /// External event handler to process Audit and Purge on all editable families in the active document.
    /// </summary>
    public class AuditPurgeEventHandler : IExternalEventHandler
    {
        /// <summary>
        /// Gets or sets whether to purge unused elements.
        /// </summary>
        public bool Purge { get; set; } = false;

        /// <summary>
        /// Gets or sets whether to purge extensible storage schemas.
        /// </summary>
        public bool PurgeSchema { get; set; } = false;

        /// <summary>
        /// Gets or sets the list of schema names to exclude from purging.
        /// </summary>
        public List<string>? SchemaExceptions { get; set; }

        /// <summary>
        /// Executes the audit and purge loop on the Revit API thread.
        /// </summary>
        /// <param name="app">The Revit UIApplication context.</param>
        public void Execute(UIApplication app)
        {
            if (app == null) return;
            UIDocument uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;
            Document doc = uidoc.Document;

            List<List<string>> results = new List<List<string>>();
            List<List<string>> errors = new List<List<string>>();

            // Collect editable, non-inplace families
            IList<Family> families = new FilteredElementCollector(doc)
                .OfClass(typeof(Family))
                .Cast<Family>()
                .Where(f => f.IsEditable && !f.IsInPlace)
                .ToList();

            // Checkout worksets if workshared
            if (doc.IsWorkshared && families.Count > 0)
            {
                try
                {
                    IList<WorksetId> worksetIds = families.Select(f => f.WorksetId).Distinct().ToList();
                    WorksharingUtils.CheckoutWorksets(doc, worksetIds);
                }
                catch (Exception ex)
                {
                    List<string> checkoutErr = new List<string> { "Worksets Checkout", "", "", $"Error checking out worksets: {ex.Message}" };
                    errors.Add(checkoutErr);
                }
            }

            try
            {
                foreach (Family family in families)
                {
                    if (ProgressCoordinator.IsCancelled())
                    {
                        break;
                    }

                    string familyName = family.Name;
                    List<string> familyResult = new List<string>
                    {
                        family.Name,
                        family.Id.ToString(),
                        family.UniqueId
                    };

                    Document? familyDoc = null;
                    try
                    {
                        familyDoc = doc.EditFamily(family);
                        familyResult.Add("Opened successfully");

                        // Get warnings
                        IList<FailureMessage> warnings = familyDoc.GetWarnings();
                        if (warnings.Count > 0)
                        {
                            StringBuilder warningString = new StringBuilder();
                            foreach (FailureMessage warning in warnings)
                            {
                                warningString.AppendLine(warning.GetDescriptionText());
                            }
                            familyResult.Add(warningString.ToString());
                        }

                        // Purge unused
                        if (Purge)
                        {
#if !REVIT2022
                            DocumentUtil.Purge(doc.Application, familyDoc);
                            familyResult.Add("Unused Purged");
#endif
                        }

                        // Purge Extensible Storage schemas
                        if (PurgeSchema && StorageUtil.DoesAnyStorageExist(familyDoc))
                        {
                            List<Schema>? schemas = StorageUtil.GetDocumentSchemas(doc);
                            List<Schema> filteredSchemas = new List<Schema>();
                            if (schemas != null)
                            {
                                if (SchemaExceptions != null)
                                {
                                    foreach (Schema schema in schemas)
                                    {
                                        if (schema != null && schema.SchemaName != null && !SchemaExceptions.Any(schema.SchemaName.Contains))
                                        {
                                            filteredSchemas.Add(schema);
                                        }
                                    }
                                }
                                else
                                {
                                    filteredSchemas.AddRange(schemas);
                                }
                            }

                            List<string>? schemaResults = StorageUtil.PurgeSchema(filteredSchemas, doc);
                            if (schemaResults != null)
                            {
                                if (schemaResults.Count > 0)
                                {
                                    familyResult.Add(string.Join(", ", schemaResults));
                                }
                                else
                                {
                                    familyResult.Add("No schemas were purged");
                                }
                            }
                            else
                            {
                                familyResult.Add("No matching schemas found to purge");
                            }
                        }

                        // Reload family using SafeFamilyLoadOptions inside a Transaction utilizing PurgeFailuresPreprocessor
                        using (Transaction trans = new Transaction(doc, $"Reload Family: {familyName}"))
                        {
                            FailureHandlingOptions options = trans.GetFailureHandlingOptions();
                            options.SetFailuresPreprocessor(new PurgeFailuresPreprocessor());
                            trans.SetFailureHandlingOptions(options);

                            trans.Start();
                            familyDoc.LoadFamily(doc, new SafeFamilyLoadOptions());
                            trans.Commit();
                        }

                        results.Add(familyResult);
                    }
                    catch (Exception ex)
                    {
                        familyResult.Add("Error: " + ex.Message);
                        errors.Add(familyResult);
                    }
                    finally
                    {
                        if (familyDoc != null)
                        {
                            familyDoc.Close(false);
                            familyDoc.Dispose();
                        }
                    }

                    ProgressCoordinator.UpdateProgress(familyName);
                }
            }
            finally
            {
                // Explicitly collect memory and close coordinator
                GC.Collect();
                GC.WaitForPendingFinalizers();

                ProgressCoordinator.Close();

                if (results.Count > 0 || errors.Count > 0)
                {
                    var finalResults = new Dictionary<string, object>
                    {
                        { "Results", results },
                        { "Errors", errors }
                    };
                    CommandUtil.SaveResults(finalResults, doc, "Audit and Purge Family Results");
                }
            }
        }

        /// <summary>
        /// Gets the name of the external event handler.
        /// </summary>
        public string GetName()
        {
            return "Audit & Purge Families Async Handler";
        }
    }
}
