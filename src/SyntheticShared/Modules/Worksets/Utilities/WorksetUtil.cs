using Synthetic.Modules.Worksets.Commands;
using Synthetic.Modules.Worksets.Models;
using Synthetic.Modules.Worksets.Utilities;

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Synthetic.Infrastructure.Serialization;
using Synthetic.Infrastructure.IO;

using Synthetic.Modules.RevitDOM;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.Header;


//Aliases for Revit Classes
using RevitDoc = Autodesk.Revit.DB.Document;
using revitWorkset = Autodesk.Revit.DB.Workset;
using revitWorksetId = Autodesk.Revit.DB.WorksetId;

using Synthetic.Settings;
using Synthetic.Modules.StandardsManagement.ViewModels;
namespace Synthetic.Modules.Worksets.Utilities
{
    /// <summary>
    /// Utilities for dealing with Worksets, WorksetModels and WorksetSettings.
    /// </summary>
    public class WorksetUtil
    {
        /// <summary>
        /// Dictionary of WorksetModels with Workset Name as Key.
        /// </summary>
        public Dictionary<string, WorksetModel> Worksets { get; set; }

        /// <summary>
        /// Constructor that creates an empty WorksetUtil object
        /// </summary>
        public WorksetUtil()
        {
            this.Worksets = new Dictionary<string, WorksetModel>();
        }

        /// <summary>
        /// Creates a list of WorksetModels from Excel cells.
        /// </summary>
        /// <param name="cells">List of List of Excel cells.</param>
        public WorksetUtil(List<List<object>> cells)
        {
            this.Worksets = new Dictionary<string, WorksetModel>();

            foreach (List<object> row in cells)
            {
                List<string> rowData = new List<string>();
                foreach (object item in row)
                {
                     rowData.Add(item?.ToString() ?? string.Empty);
                }

                string? name = null;
                string vis = "TRUE";
                string? alias = null;
                string description = string.Empty;
                
                int itemCount = rowData.Count;
                if (itemCount > 0) { name = rowData[0]; }
                if (itemCount > 1) { vis = rowData[1]; }
                if (itemCount > 2) { alias = rowData[2]; }
                if (itemCount > 3) { description = rowData[3]; }

                if (name != null && name != string.Empty)
                {
                    bool visibility = true;
                    if (vis == "FALSE" || vis == "false" || vis == "False")
                    {
                        visibility = false;
                    }
                    this.Worksets.Add(name, new WorksetModel(name, visibility, alias, description));
                }
            }
        }

        /// <summary>
        /// Adds a Workset to the WorksetUtil object.
        /// </summary>
        /// <param name="workset"></param>
        public WorksetUtil Add (WorksetModel workset)
        {
            Worksets.Add(workset.Name, workset);
            return this;
        }

        /// <summary>
        /// Gets a list of Workset names
        /// </summary>
        /// <returns>List of Workset names as strings</returns>
        public List<string> Names()
        {
            return Worksets.Keys.ToList();
        }

        /// <summary>
        /// Return a string Workset names with line breaks between
        /// </summary>
        /// <returns>Return a string Workset names with line breaks between</returns>
        public string WorksetNames()
        {
            return string.Join("\n", this.Worksets.Keys);
        }

        /// <summary>
        /// Returns a string of Workset visibilities with line breaks between
        /// </summary>
        /// <returns>Returns a string of Workset visibilities with line breaks between</returns>
        public string WorksetVisibilities()
        {
            string visibilities = String.Empty;
            foreach (WorksetModel worksetModel in Worksets.Values)
            {
                visibilities = visibilities + "\n" + worksetModel.Visibility;
            }
            return visibilities;
        }

        /// <summary>
        /// Returns a string of Workset descriptions with line breaks between
        /// </summary>
        /// <returns>Returns a string of Workset descriptions with line breaks between</returns>
        public string WorksetDescriptions()
        {
            string descriptions = String.Empty;
            foreach (WorksetModel worksetModel in Worksets.Values)
            {
                descriptions = descriptions + "\n" + worksetModel.Description;
            }
            return descriptions;
        }

        /// <summary>
        /// Given a list of WorksetModel names, updates the WorksetUtil object to only include those WorksetModels
        /// </summary>
        /// <param name="filters">List of WorksetModel names as strings</param>
        /// <returns>The modified WorksetUtil object.</returns>
        public WorksetUtil Filter(List<string> filters)
        {
            Dictionary<string, WorksetModel> filteredWorksets = new Dictionary<string, WorksetModel>();
            foreach (string name in filters)
            {
                if(this.Worksets.ContainsKey(name))
                {
                    filteredWorksets.Add(name, this.Worksets[name]);
                }
            }
            this.Worksets = filteredWorksets;

            return this;
        }

        /// <summary>
        /// Sets the "Workset Names", "Workset Visibility", and "Workset Descriptions" parameters on the ProjectInfo element.
        /// </summary>
        /// <param name="doc">Revit Document</param>
        /// <returns>Returns the ProjectInfo element</returns>
        public ProjectInfo SetProjectInfo (RevitDoc doc)
        {
            FilteredElementCollector collector = new FilteredElementCollector(doc);
            ProjectInfo projectInfo = (ProjectInfo)collector.OfClass(typeof(ProjectInfo)).FirstElement();
            if (projectInfo != null)
            {
                Parameter paramNames = projectInfo.LookupParameter("Workset Names");
                Parameter paramVisibility = projectInfo.LookupParameter("Workset Visibility");
                Parameter paramDescriptions = projectInfo.LookupParameter("Workset Descriptions");

                using (Autodesk.Revit.DB.Transaction trans = new Autodesk.Revit.DB.Transaction(doc))
                {
                    trans.Start("Set ProjectInfo Workset Descriptions");
                    if (paramNames != null) { paramNames.Set(this.WorksetNames()); }
                    if (paramVisibility != null) { paramVisibility.Set(this.WorksetVisibilities()); }
                    if (paramDescriptions != null) { paramDescriptions.Set(this.WorksetDescriptions()); }
                    trans.Commit();
                }
            }
            return projectInfo!;
        }

        /// <summary>
        /// Creates Worksets from a WorksetUtil object.  Worksets that already exist are modified.  Worksets are renamed if they have Aliases.
        /// </summary>
        /// <param name="doc">Revit Document to create Worksets in</param>
        /// <param name="worksets">WorksetUtil object that includes a collection of Worksets with names, visibility, aliases and descriptions.</param>
        /// <returns>Dictionary with Workset Names as keys and Revit Workset objects as values.</returns>
        public static Dictionary<string, revitWorkset>? Create (RevitDoc doc, WorksetUtil worksets)
        {
            return _create(doc, worksets);
        }

        /// <summary>
        /// Creates a Workset given a Revit Document and workset information.  Will modify an existing Workset.  Worksets named as the alias, will be renamed.
        /// </summary>
        /// <param name="doc">Revit Document to make the workset in.</param>
        /// <param name="name">Name of the Workset as a string</param>
        /// <param name="visible">Whether a visibility is set to true or false.</param>
        /// <param name="alias">Aliases to be renamed to this workset</param>
        /// <param name="description">Description of the workset.</param>
        /// <returns></returns>
        public static Dictionary<string, revitWorkset>? Create (RevitDoc doc, string name, bool visible = true, string alias = "", string description = "")
        {
            WorksetUtil worksets = new WorksetUtil();
            worksets.Add(new WorksetModel(name, visible, alias, description));

            return _create(doc, worksets);
        }

        private static Dictionary<string, revitWorkset>? _create (RevitDoc doc, WorksetUtil wksets)
        {
            Dictionary<string, revitWorkset> importedWorksets = new Dictionary<string, revitWorkset> ();

            using (Autodesk.Revit.DB.Transaction trans = new Autodesk.Revit.DB.Transaction(doc))
            {
                trans.Start("Create Worksets");
                foreach (WorksetModel wkset in wksets.Worksets.Values)
                {
                    //Only create workset if it's name isn't an empty string
                    if (wkset.Name != null && wkset.Name != "")
                    {
                        // Verify that each workset isn't already in the document
                        // If the workset is unique, either check if the alias exists or create a new workset.
                        if (WorksetTable.IsWorksetNameUnique(doc, wkset.Name))
                        {
                            //If the alias is already in the document, rename the alias
                            if (wkset.Alias != null && WorksetTable.IsWorksetNameUnique(doc, wkset.Alias) == false)
                            {
                                revitWorkset? workset = WorksetUtil.GetByName(wkset.Alias, doc);
                                if (workset != null)
                                {
                                    WorksetTable.RenameWorkset(doc, workset.Id, wkset.Name);
                                    importedWorksets.Add(wkset.Name, workset);
                                }
                            }
                            // Otherwise, create a new workset.
                            else
                            {
                                revitWorkset workset = revitWorkset.Create(doc, wkset.Name);

                                // Set the workset’s default visibility      
                                WorksetDefaultVisibilitySettings defaultVisibility = WorksetDefaultVisibilitySettings.GetWorksetDefaultVisibilitySettings(doc);
                                defaultVisibility.SetWorksetVisibility(workset.Id, wkset.Visibility);
                                importedWorksets.Add(wkset.Name, workset);
                            }
                        }
                    }
                    // If the workset is already in the document, retrieve the workset
                    else if (wkset.Name != null)
                    {
                        revitWorkset? workset = GetByName(wkset.Name, doc);
                        if (workset != null)
                        {
                            importedWorksets.Add(wkset.Name, workset);
                        }
                    }
                }
                trans.Commit();
            }

            if (importedWorksets.Count > 0)
            {
                return importedWorksets;
            }
            else 
            {
                return null;
            }
        }

        /// <summary>
        /// Retrieves the workset with the given name.
        /// </summary>
        /// <param name="name">A workset name</param>
        /// <param name="doc">The Revit document</param>
        /// <returns name="workset">Returns a workset.  Returns null if workset does not exist.</returns>
        public static revitWorkset? GetByName (string name, RevitDoc doc)
        {
            revitWorkset? foundWorkset = null;

            if (name != null)
            {
                FilteredWorksetCollector fwCollector = new FilteredWorksetCollector(doc);

                foreach (revitWorkset workset in fwCollector)
                {
                    if (workset.Name == name)
                    {
                        foundWorkset = workset;
                    }
                }
                fwCollector.Dispose();
            }
            return foundWorkset;
        }

        /// <summary>
        /// Retrieves the workset with the given WorksetId.
        /// </summary>
        /// <param name="worksetId">The workset ID</param>
        /// <param name="doc">A Revit document</param>
        /// <returns name="workset">Returns a workset.  Returns null if workset does not exist.</returns>
        public static revitWorkset GetByWorksetId (WorksetId worksetId, RevitDoc doc)
        {
            return doc.GetWorksetTable().GetWorkset(worksetId);
        }

        /// <summary>
        /// Retrieves the workset with the given Workset's UniqueId.
        /// </summary>
        /// <param name="worksetUniqueId">The GUID of the workset</param>
        /// <param name="doc">A Revit document</param>
        /// <returns name="workset">Returns a workset.  Returns null if workset does not exist.</returns>
        public static revitWorkset GetByWorksetUniqueId(System.Guid worksetUniqueId, RevitDoc doc)
        {
            return doc.GetWorksetTable().GetWorkset(worksetUniqueId);
        }

        /// <summary>
        /// Retrieves all elements belonging to a specific workset in the document.
        /// </summary>
        /// <param name="workset">The workset to query.</param>
        /// <param name="document">The Revit document.</param>
        /// <returns>A list of elements on the specified workset.</returns>
        public static IList<Element> GetElementsOnWorkset(Workset workset, RevitDoc document)
        {
            // filter all elements that belong to the given workset
            FilteredElementCollector elementCollector = new FilteredElementCollector(document);
            ElementWorksetFilter elementWorksetFilter = new ElementWorksetFilter(workset.Id, false);
            IList<Element> elements = elementCollector.WherePasses(elementWorksetFilter).ToElements();

            return elements;
        }

        /// <summary>
        /// Retrieves all the user worksets from a document.  Excludes view and family worksets.
        /// </summary>
        /// <returns name="worksets">Returns all user worksets in the document.</returns>
        public static List<revitWorkset> GetUserWorksets (RevitDoc doc)
        {
            FilteredWorksetCollector fwCollector = new FilteredWorksetCollector(doc);
            List<revitWorkset> worksets = new List<revitWorkset>();

            foreach (revitWorkset workset in fwCollector.OfKind(WorksetKind.UserWorkset))
            {
                worksets.Add(workset);
            }
            return worksets;
        }

        /// <summary>
        /// Renames a workset.
        /// </summary>
        /// <param name="workset">A workset</param>
        /// <param name="name">A workset name</param>
        /// <param name="doc">The Revit document</param>
        /// <returns name="workset">renamed workeset.</returns>
        /// <returns name="renamed">renamed workeset.</returns>
        public static revitWorkset? Rename (revitWorkset workset, string name, RevitDoc doc)
        {
            revitWorkset? renamedWorkset = null;

            if (name != null && workset != null)
            {
                //Verify that the existing workset is in the document.
                //If the workset is unique, create it
                if (WorksetTable.IsWorksetNameUnique(doc, workset.Name) == false)
                {
                    // Verify that the new name doesn't already exist
                    if (WorksetTable.IsWorksetNameUnique(doc, name) == true)
                    {
                        //Only rename workset if it's name isn't an empty string
                        if (name != "")
                        {
                            using (Autodesk.Revit.DB.Transaction trans = new Autodesk.Revit.DB.Transaction(doc))
                            {
                                trans.Start("Rename Worksets");

                                WorksetTable.RenameWorkset(doc, workset.Id, name);
                                renamedWorkset = workset;

                                trans.Commit();
                            }
                        }
                    }
                }
            }
            return renamedWorkset;
        }

        /// <summary>
        /// Sets the Default Visibility of a workset within a document.
        /// </summary>
        /// <param name="workset">The workset that you wish to set the visibility of.</param>
        /// <param name="visible">The visibility of the workset</param>
        /// <param name="doc">The Revit document</param>
        /// <returns name="workset">A Revit workset</returns>
        public static revitWorkset SetDefaultVisibility (revitWorkset workset, bool visible, RevitDoc doc)
        {
            using (Autodesk.Revit.DB.Transaction trans = new Autodesk.Revit.DB.Transaction(doc))
            {
                trans.Start("Set " + workset.Name + " Workset Default Visibility");

                // Set the workset’s default visibility      
                WorksetDefaultVisibilitySettings defaultVisibility = WorksetDefaultVisibilitySettings.GetWorksetDefaultVisibilitySettings(doc);
                defaultVisibility.SetWorksetVisibility(workset.Id, visible);

                trans.Commit();
                defaultVisibility.Dispose();
            }
            return workset;
        }
    }
}
