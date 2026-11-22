using Synthetic.Modules.Worksets.Commands;
using Synthetic.Modules.Worksets.Models;
using Synthetic.Modules.Worksets.Utilities;

using System;
using System.Collections.Generic;
using System.Text;

using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Visual;
using Newtonsoft.Json;

using Synthetic.Shared.RevitAPI;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;
using Synthetic.Modules.StandardsManagement.ViewModels;
namespace Synthetic.Modules.Worksets.Models
{
    /// <summary>
    /// Model that tracks elements associated with a specific workset.
    /// Used for exporting elements on a workset and recreating them back.
    /// </summary>
    public class ElementsOnWorkset

    {
        /// <summary>
        /// Gets or sets the name of the workset.
        /// </summary>
        public string WorksetName = string.Empty;

        /// <summary>
        /// Gets or sets the unique identifier of the workset.
        /// </summary>
        public System.Guid WorksetUniqueId;

        /// <summary>
        /// Gets or sets the list of unique element IDs.
        /// </summary>
        public List<string> ElementUniqueIds;
        internal List<string> Results;
        internal List<string> Errors;

        /// <summary>
        /// Initializes a new instance of the ElementsOnWorkset class.
        /// </summary>
        public ElementsOnWorkset()
        {
            this.ElementUniqueIds = new List<string>();
            this.Results = new List<string>();
            this.Errors = new List<string>();
        }

        /// <summary>
        /// Initializes a new instance of the ElementsOnWorkset class from a Revit workset and document.
        /// </summary>
        /// <param name="workset">The Revit workset.</param>
        /// <param name="document">The Revit document.</param>
        public ElementsOnWorkset(Workset workset, Document document)
        {
            this.ElementUniqueIds = new List<string>();
            this.Results = new List<string>();
            this.Errors = new List<string>();

            this.WorksetName = workset.Name;
            this.WorksetUniqueId = workset.UniqueId;

            IList<Element> elements = WorksetUtil.GetElementsOnWorkset(workset, document);
            
            if(elements != null && elements.Count > 0)
            {
                foreach(Element element in elements)
                {
                    this.ElementUniqueIds.Add(element.UniqueId);
                }
            }
        }

        /// <summary>
        /// Initializes a new instance of the ElementsOnWorkset class with specific properties.
        /// </summary>
        /// <param name="worksetName">Name of the workset.</param>
        /// <param name="worksetUniqueId">Unique ID of the workset.</param>
        /// <param name="elementUniqueIds">List of element unique IDs.</param>
        public ElementsOnWorkset(string worksetName, Guid worksetUniqueId, List<string> elementUniqueIds)
        {
            this.WorksetName = worksetName;
            this.WorksetUniqueId = worksetUniqueId;
            this.ElementUniqueIds = elementUniqueIds;
            this.Results = new List<string>();
            this.Errors = new List<string>();
        }

        /// <summary>
        /// Add a new element's UniqueId to the collection.
        /// </summary>
        /// <param name="UniqueId">A string representing the UniqueId of the element</param>
        /// <returns>This ElementsOnWorkset object for chaining commands.</returns>
        public ElementsOnWorkset Add (string UniqueId)
        {
            this.ElementUniqueIds.Add(UniqueId);
            return this;
        }

        /// <summary>
        /// Moves the elements with UniqueIds in the ElementsOnWorkset object to the Workset.  If the workset doesn't exist, it will be created.
        /// </summary>
        /// <param name="document">A Revit Document</param>
        /// <returns>The Revit Elements with UniqueIds that were moved.</returns>
        /// <exception cref="System.NotImplementedException"></exception>
        /// <exception cref="System.InvalidOperationException"></exception>
        public List<Element>? MoveToWorkset(Document document)
        {
            List<Element>? elements = null;
            if (document.IsWorkshared)
            {
                Workset? workset = document.GetWorksetTable().GetWorkset(this.WorksetUniqueId);
                if (workset == null && this.WorksetName != null && this.WorksetName != String.Empty)
                {
                    if (!WorksetTable.IsWorksetNameUnique(document, this.WorksetName))
                    {
                        workset = WorksetUtil.GetByName(this.WorksetName, document);
                    }
                    else
                    {
                        try
                        {
                            var createdWorksets = WorksetUtil.Create(document, WorksetName, true);
                            if (createdWorksets != null && createdWorksets.ContainsKey(WorksetName))
                            {
                                workset = createdWorksets[WorksetName];
                            }
                        }
                        catch { workset = null; }
                    }
                }

                if (workset != null && this.ElementUniqueIds != null && this.ElementUniqueIds.Count > 0)
                {
                    elements = new List<Element>();
                    foreach (string uniqueId in this.ElementUniqueIds)
                    {
                        Element element = document.GetElement(uniqueId);
                        if (element != null)
                        {
                            elements.Add(element);
                        }
                        else { this.Errors.Add("Element missing from project.  " + uniqueId); }
                    }

                    if (elements.Count > 0)
                    {
                        ElementUtil.SetWorkset(elements, workset, document);
                    }
                    else { elements = null; }
                }
                else
                {
                    throw new NotImplementedException("Workset does not exist in the document and can not be created");
                }
            }
            else
            {
                throw new InvalidOperationException("Document isn't Workshared!  You cann't move elements to a workset if worksets don't exist.");
            }

            return elements;
        }

        /// <summary>
        /// Serializes the object to JSON
        /// </summary>
        /// <returns>A JSON string</returns>
        public string ToJSON()
        {
            return Newtonsoft.Json.JsonConvert.SerializeObject(this, Formatting.Indented);
        }

        /// <summary>
        /// Deserializes the object from a JSON string.
        /// </summary>
        /// <param name="JSON">A string of JSON</param>
        /// <returns>The deserializedd object</returns>
        public static ElementsOnWorkset ByJSON(string JSON)
        {
            return JsonConvert.DeserializeObject<ElementsOnWorkset>(JSON)!;
        }

        /// <summary>
        /// Checks if there are any results to log
        /// </summary>
        /// <returns>True if there are errors or results to log.</returns>
        public bool IfLog()
        {
            if (this.Errors.Count > 0 || this.Results.Count > 0)
            {
                return true;
            }
            else { return false; }
        }

        /// <summary>
        /// Complies the log of results
        /// </summary>
        /// <returns>A json string of the Log</returns>
        public string GetLog()
        {
            Dictionary<string, List<string>> log = new Dictionary<string, List<string>>
            {
                {"Elements Moved", this.Results},
                {"Errors", this.Errors }
            };
            return Newtonsoft.Json.JsonConvert.SerializeObject(log, Formatting.Indented);
        }
    }
}
