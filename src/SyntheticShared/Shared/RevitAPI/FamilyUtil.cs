using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Events;
using Application = Autodesk.Revit.ApplicationServices.Application;
using Document = Autodesk.Revit.DB.Document;
using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using Microsoft.Office.Interop.Excel;
using static System.Net.Mime.MediaTypeNames;
using System.Windows.Forms;
using Autodesk.Revit.DB.ExtensibleStorage;

using Synthetic.Modules.RevitDOM;
using Synthetic.Infrastructure.Serialization;

using Synthetic.Shared.UI;
using Synthetic.Core;

using Synthetic.Shared.RevitAPI;
using Synthetic.Modules.StandardsManagement.ViewModels;
namespace Synthetic.Shared.RevitAPI{
    /// <summary>
    /// Utility methods for auditing, loading, and modifying Revit Families.
    /// </summary>
    public class FamilyUtil
    {
        /// <summary>
        /// Opens all the Families in a project to test if any have been corrupted.
        /// </summary>
        /// <param name="document">A Revit Document</param>
        /// <param name="purge">If true, unused elements in the family are purged.</param>
        /// <param name="purgeSchema">If true, extensible storage schemas are purged from the family.</param>
        /// <param name="schemaExceptions">A list of schema names that should not be purged.</param>
        /// <returns name="results">Returns a text string of the results of opening each family.</returns>
        public static Dictionary<string, object> AuditProjectFamilies(Document document, bool purge = false, bool purgeSchema = false, List<string>? schemaExceptions = null)
        {
            List<List<string>> results = new List<List<string>>();
            List<List<string>> errors = new List<List<string>>();

            FilteredElementCollector families = new FilteredElementCollector(document);
            families.OfClass(typeof(Family)).ToElements();

            IFamilyLoadOptions opt = new ffrFamilyLoadOptions();
            Application app = document.Application;

            List<Tuple<Family, ElementId>> familiesToReinsert = new List<Tuple<Family, ElementId>>();

            foreach (Family family in families)
            {
                string name = family.Name;
                if (document.IsWorkshared == true)
                {
                    IList<WorksetId> worksetIds = new List<WorksetId>();
                    worksetIds.Add(family.WorksetId);
                    WorksharingUtils.CheckoutWorksets(document, worksetIds);
                }
                familiesToReinsert.Add(new Tuple<Family, ElementId>(family, family.Id));
            }

            ProgressCoordinator.Initialize("Audit & Purge Families", "Auditing and purging project families...", familiesToReinsert.Count);

            try
            {
                foreach (Tuple<Family, ElementId> familyTuple in familiesToReinsert)
                {
                    if (ProgressCoordinator.IsCancelled())
                    {
                        break;
                    }

                    Family family = familyTuple.Item1;
                    ElementId familyId = familyTuple.Item2;
                    Element searchElem = document.GetElement(familyId);

                    List<string> familyResult = new List<string>();
                    familyResult.Add(family.Name);
                    familyResult.Add(family.Id.ToString());
                    familyResult.Add(family.UniqueId);

                    if (searchElem != null && family.IsEditable)
                    {
                        {
                            try
                            {
                                Document familyDoc = document.EditFamily(family);
                                familyResult.Add("Opened successfully");

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
                                if (purge)
                                {
#if !REVIT2022
                                    bool purgeResult = DocumentUtil.Purge(app, familyDoc);
                                    familyResult.Add("Unused Purged");
#endif
                                }
                                if (purgeSchema && StorageUtil.DoesAnyStorageExist(familyDoc))
                                {
                                    List<Schema>? schemas = StorageUtil.GetDocumentSchemas(document);
                                    List<Schema> filteredSchemas = new List<Schema>();
                                    if (schemas != null)
                                    {
                                        if (schemaExceptions != null)
                                        {
                                            foreach (Schema schema in schemas)
                                            {
                                                if (schema != null && schema.SchemaName != null && !schemaExceptions.Any(schema.SchemaName.Contains))
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
                                    
                                    List<string>? schemaResults = StorageUtil.PurgeSchema(filteredSchemas, document);
                                    if(schemaResults != null)
                                    {
                                        if (schemaResults.Count > 0) { familyResult.Add(String.Join(", ", schemaResults.ToArray())); }
                                        else { familyResult.Add("No schemas were purged"); }
                                    }
                                    else { familyResult.Add("Error: Could not purge schemas"); }
                                }

                                if (App.AppControlled != null)
                                {
                                    App.AppControlled.ControlledApplication.FailuresProcessing +=
                                            new EventHandler<Autodesk.Revit.DB.Events.FailuresProcessingEventArgs>
                                            (ResolveWarnings);
                                }

                                familyDoc.LoadFamily(document, opt);

                                if (App.AppControlled != null)
                                {
                                    App.AppControlled.ControlledApplication.FailuresProcessing -=
                                        new EventHandler<Autodesk.Revit.DB.Events.FailuresProcessingEventArgs>
                                        (ResolveWarnings);
                                }

                                familyDoc.Close(false);
                                results.Add(familyResult);
                            }
                            catch (Exception ex)
                            {
                                familyResult.Add("Error: " + ex.Message);
                                errors.Add(familyResult);
                            }
                        }
                    }
                    else
                    {
                        familyResult.Add("Skipped: Family not found or not editable");
                        errors.Add(familyResult);
                    }

                    ProgressCoordinator.UpdateProgress(family.Name);
                }
            }
            finally
            {
                ProgressCoordinator.Close();
            }
            return new Dictionary<string, object>
            {
                {"Results", results},
                {"Errors", errors}
            };
        }

        /// <summary>
        /// Opens every Annotation family in the project and creates the standard element types.
        /// </summary>
        /// <param name="doc">The Revit Document</param>
        /// <param name="standards">A ModelsToSerialize object that contains the standard element types.</param>
        public static void LoadStandards(Document doc, IEnumerable<ElementModel> standards)
        {
            Categories categories = doc.Settings.Categories;
            List<ElementId> annotationCategories = new List<ElementId>();
            foreach (Category category in categories)
            {
                if (category.CategoryType == CategoryType.Annotation)
                {
                    annotationCategories.Add(category.Id);
                }
            }

            IList<Element> families = new FilteredElementCollector(doc)
                .OfClass(typeof(Family))
                .ToElements();

            IFamilyLoadOptions opt = new ffrFamilyLoadOptions();

            List<Tuple<Family, ElementId>> familiesToReinsert = new List<Tuple<Family, ElementId>>();

            foreach (Family family in families)
            {
                string name = family.Name;
                if (family.FamilyCategory.CategoryType == CategoryType.Annotation)
                {
                    if (doc.IsWorkshared == true)
                    {
                        IList<WorksetId> worksetIds = new List<WorksetId>();
                        worksetIds.Add(family.WorksetId);
                        WorksharingUtils.CheckoutWorksets(doc, worksetIds);
                    }
                    familiesToReinsert.Add(new Tuple<Family, ElementId>(family, family.Id));
                }
            }

            foreach (Tuple<Family, ElementId> familyTuple in familiesToReinsert)
            {
                Family family = familyTuple.Item1;
                ElementId familyId = familyTuple.Item2;
                Element searchElem = doc.GetElement(familyId);
                if (searchElem != null && family.IsEditable)
                {

                    Document familyDoc = doc.EditFamily(family);

                    if (App.AppControlled != null)
                    {
                        App.AppControlled.ControlledApplication.FailuresProcessing +=
                                new EventHandler<Autodesk.Revit.DB.Events.FailuresProcessingEventArgs>
                                (ResolveWarnings);
                    }

                    if (standards != null)
                    {
                        var engine = new StandardSerializationEngine();
                        engine.ToRevit(standards.OfType<ElementTypeModel>().Cast<ObjectModel>(), familyDoc);
                    }
                    familyDoc.LoadFamily(doc, opt);

                    if (App.AppControlled != null)
                    {
                        App.AppControlled.ControlledApplication.FailuresProcessing -=
                            new EventHandler<Autodesk.Revit.DB.Events.FailuresProcessingEventArgs>
                            (ResolveWarnings);
                    }
                    familyDoc.Close(false);
                }
                else
                {
                    //family.Name;
                }
            }
        }
        /// <summary>
        /// Forces all annotation families in the project to be reinserted/reloaded to ensure they are up to date.
        /// </summary>
        /// <param name="doc">The Revit Document.</param>
        public static void ForceReinsertAnnotation(Document doc)
        {
            Categories categories = doc.Settings.Categories;
            List<ElementId> annotationCategories = new List<ElementId>();
                foreach (Category category in categories)
                {
                    if (category.CategoryType == CategoryType.Annotation)
                    {
                        annotationCategories.Add(category.Id);
                    }
                }

            //ElementMulticategoryFilter catFilter = new ElementMulticategoryFilter(annotationCategories);

            IList<Element> families = new FilteredElementCollector(doc)
                .OfClass(typeof(Family))
                //.WherePasses(catFilter)
                .ToElements();

            IFamilyLoadOptions opt = new ffrFamilyLoadOptions();

            //IList<WorksetId> worksetIds = (IList < WorksetId > ) new FilteredWorksetCollector(doc)
            //    .OfKind(WorksetKind.FamilyWorkset)
            //    .ToWorksetIds();

            //WorksharingUtils.CheckoutWorksets(doc, worksetIds);

            List<Tuple<Family, ElementId>> familiesToReinsert = new List<Tuple<Family, ElementId>>();

            foreach (Family family in families)
            {
                string name = family.Name;
                if (family.FamilyCategory.CategoryType == CategoryType.Annotation)
                {
                    if (doc.IsWorkshared == true)
                    {
                        IList<WorksetId> worksetIds = new List<WorksetId>();
                        worksetIds.Add(family.WorksetId);
                        WorksharingUtils.CheckoutWorksets(doc, worksetIds);
                    }
                    familiesToReinsert.Add(new Tuple<Family, ElementId>(family, family.Id));
                }
            }

            foreach (Tuple<Family, ElementId> familyTuple in familiesToReinsert)
            {
                Family family = familyTuple.Item1;
                ElementId familyId = familyTuple.Item2;
                Element searchElem = doc.GetElement(familyId);
                if (searchElem != null && family.IsEditable)
                {

                    Document familyDoc = doc.EditFamily(family);
                    //FamilyUtil.ForceReinsert(familyDoc);

                    if (App.AppControlled != null)
                    {
                        App.AppControlled.ControlledApplication.FailuresProcessing +=
                                new EventHandler<Autodesk.Revit.DB.Events.FailuresProcessingEventArgs>
                                (ResolveWarnings);
                    }

                    SetIsChanged(doc, familyDoc);
                    familyDoc.LoadFamily(doc, opt);

                    if (App.AppControlled != null)
                    {
                        App.AppControlled.ControlledApplication.FailuresProcessing -=
                            new EventHandler<Autodesk.Revit.DB.Events.FailuresProcessingEventArgs>
                            (ResolveWarnings);
                    }
                    familyDoc.Close(false);
                }
                else
                {
                    //family.Name;
                }
            }
        }

        /// <summary>
        /// Marks a family document as changed/dirty by creating and deleting a temporary text note.
        /// </summary>
        /// <param name="doc">The active Revit document.</param>
        /// <param name="familyDoc">The family document to mark as changed.</param>
        public static void SetIsChanged (Document doc, Document familyDoc)
        {
            ElementId? viewId = null;

            viewId = new FilteredElementCollector(familyDoc)
            .OfClass(typeof(ViewPlan))
            .ToElementIds()
            .FirstOrDefault();

            if (viewId == null)
            {
                viewId = new FilteredElementCollector(familyDoc)
                    .OfClass(typeof(ViewSheet))
                    .ToElementIds()
                    .FirstOrDefault();
            }

            if (viewId == null)
            {
                viewId = new FilteredElementCollector(familyDoc)
                    .OfClass(typeof(Autodesk.Revit.DB.View))
                    .Cast<Autodesk.Revit.DB.View>()
                    .Where(v => !v.IsTemplate)
                    .Select(v => v.Id)
                    .FirstOrDefault();
            }

            if (viewId != null)
            {
                ElementId textTypeId = new FilteredElementCollector(familyDoc)
                .OfClass(typeof(TextNoteType))
                .ToElementIds()
                .FirstOrDefault() ?? ElementId.InvalidElementId;

                TextNote? note = null;
                ElementId? noteId = null;

                XYZ origin = XYZ.Zero;
                string text = "test";

                using (Autodesk.Revit.DB.Transaction trans = new Autodesk.Revit.DB.Transaction(familyDoc))
                {
                    trans.Start("Temp text note");
                    if (viewId != null)
                    {
                        try
                        {
                            note = TextNote.Create(familyDoc, viewId, origin, text, textTypeId);
                            noteId = note.Id;
                        }
                        catch { }
                    }
                    trans.Commit();
                }
                using (Autodesk.Revit.DB.Transaction trans = new Autodesk.Revit.DB.Transaction(familyDoc))
                {
                    trans.Start("Delete temp text note");
                    if (noteId != null)
                    {
                        //Element Searchelem = doc.GetElement(noteId);
                        //if (Searchelem != null)
                        //{
                            familyDoc.Delete(noteId);
                        //}
                    }
                    trans.Commit();
                }
            }
        }


        /// <summary>
        /// Catches warners and resolves them so they are not displayed to the user.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private static void ResolveWarnings(object? sender, FailuresProcessingEventArgs e)
        {
            FailuresAccessor fa = e.GetFailuresAccessor();
            IList<FailureMessageAccessor> failList = new List<FailureMessageAccessor>();
            failList = fa.GetFailureMessages(); // Inside event handler, get all warnings

            if (failList.Count == 0)
            {
                // FailureProcessingResult.Continue is to let 
                // the failure cycle continue next step.

                e.SetProcessingResult(FailureProcessingResult.Continue);

                return;
            }

            foreach (FailureMessageAccessor failure in failList)
            {
                // check FailureDefinitionIds against ones that you want to dismiss, 
                FailureDefinitionId failID = failure.GetFailureDefinitionId();
                // prevent Revit from showing Unenclosed room warnings
                if (failID == BuiltInFailures.RoomFailures.RoomNotEnclosed)
                {
                    fa.DeleteWarning(failure);
                }
            }
            e.SetProcessingResult(FailureProcessingResult.ProceedWithCommit);

            return;
        }
    }
    class ffrFamilyLoadOptions : IFamilyLoadOptions
    {
        public bool OnFamilyFound(
          bool familyInUse,
          out bool overwriteParameterValues)
        {
            overwriteParameterValues = true;
            return true;
        }

        public bool OnSharedFamilyFound(
          Family sharedFamily,
          bool familyInUse,
          out FamilySource source,
          out bool overwriteParameterValues)
        {
            source = FamilySource.Family;
            overwriteParameterValues = true;
            return true;
        }
    }
}
