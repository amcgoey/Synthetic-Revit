using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Autodesk.Revit.DB;

namespace Synthetic.Modules.RevitDOM
{
    /// <summary>
    /// Model that acts as a wrapper for Revit ElementIds to facilitate JSON serialization.
    /// Supports resolving element instances by UniqueId, Id, Name, or Aliases.
    /// </summary>
    public class ElementIdModel : ObjectModel
    {
        #region Public Properties

        /// <summary>
        /// Gets or sets the class name of the referenced Revit element.
        /// </summary>
        public string Class { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the category name of the referenced Revit element.
        /// </summary>
        public string Category { get; set; } = string.Empty;

        /// <summary>
        /// Name of the element the Id belongs too.
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the list of name aliases to search if the primary Name is not found.
        /// </summary>
        public List<string>? Aliases { get; set; }

        /// <summary>
        /// Value of the Element Id as an int
        /// </summary>
        public long Id { get; set; }

        /// <summary>
        /// Gets or sets the unique string identifier of the element.
        /// </summary>
        public string? UniqueId { get; set; }

        /// <summary>
        /// If true, SerialElement is intended to be deserialized as a template for use as standards or transfer to another project.
        /// If false, SerialElement is intended to modify an element inside the project and will include ElementIds and UniqueIds.
        /// </summary>
        [JsonIgnoreAttribute]
        public bool IsTemplate { get; set; }

        #endregion
        #region Conditional Serialization Methods for Properties

        /// <summary>
        /// Conditional serialization method for the Element Id.
        /// If IsTemplate is true, excludes the ID unless it is a built-in Revit element ID (represented by negative values).
        /// This ensures built-in/system IDs (e.g. built-in categories or solid line patterns) are preserved across document transfers.
        /// </summary>
        /// <returns>True if the Id should be serialized; otherwise, false.</returns>
        public bool ShouldSerializeId()
        {
            return !IsTemplate || Id < 0;
        }

        /// <summary>
        /// If IsTemplate, don't serialize the Unqiue Id
        /// </summary>
        /// <returns>True if not a template</returns>
        public bool ShouldSerializeUniqueId()
        {
            return !IsTemplate;
        }

        #endregion
        #region Public Constructors

        /// <summary>
        /// Initializes a new instance of the ElementIdModel class.
        /// </summary>
        public ElementIdModel ()
        {
            this.IsTemplate = false;
        }

        /// <summary>
        /// Initializes a new instance of the ElementIdModel class.
        /// </summary>
        /// <param name="IsTemplate">Flag indicating if this model is a template.</param>
        public ElementIdModel(bool IsTemplate)
        {
            this.IsTemplate = IsTemplate;
        }

        /// <summary>
        /// Initializes a new instance of the ElementIdModel class.
        /// </summary>
        /// <param name="Name">Name of the element.</param>
        /// <param name="ElementId">Revit element ID integer.</param>
        /// <param name="Class">Class name of the element.</param>
        /// <param name="Category">Category name of the element.</param>
        public ElementIdModel (string Name, int ElementId, string Class, string Category)
        {
            this.Id = ElementId;
            this.Name = Name;
            this.Class = Class;
            this.Category = Category;
            this.IsTemplate = false;
        }

        #endregion
        #region Public Methods

        /// <summary>
        /// Deserializes a JSON string into an ElementIdModel instance.
        /// </summary>
        /// <param name="JSON">The JSON string.</param>
        /// <returns>An ElementIdModel instance.</returns>
        public static ElementIdModel? ByJSON (string JSON)
        {
            return JsonConvert.DeserializeObject<ElementIdModel>(JSON);
        }

        /// <summary>
        /// Serializes an ElementIdModel instance to a JSON string.
        /// </summary>
        /// <param name="IdJSON">The instance to serialize.</param>
        /// <returns>A JSON string.</returns>
        public static string ToJSON (ElementIdModel IdJSON)
        {
            return JsonConvert.SerializeObject(IdJSON, Formatting.Indented);
        }

        #endregion
    }

    public static class ElementIdExtensions
    {
        public static ElementIdModel ToModel(this ElementId id, Document doc, bool isTemplate = false)
        {
            if (id == null) return null;

            long idVal;
#if REVIT2022 || REVIT2023
            idVal = id.IntegerValue;
#else
            idVal = id.Value;
#endif

            if (id == LinePatternElement.GetSolidPatternId())
            {
                return new ElementIdModel
                {
                    Name = "Solid",
                    Class = "Autodesk.Revit.DB.LinePatternElement",
                    UniqueId = "",
                    Category = "",
                    Id = idVal,
                    IsTemplate = isTemplate
                };
            }

            var model = new ElementIdModel
            {
                Id = idVal,
                IsTemplate = isTemplate
            };

            if (doc != null)
            {
                Element elem = doc.GetElement(id);
                if (elem != null)
                {
                    model.Name = elem.Name;
                    model.Class = elem.GetType().FullName ?? string.Empty;
                    model.UniqueId = elem.UniqueId;

                    Category cat = elem.Category;
                    if (cat != null)
                    {
                        model.Category = cat.Name;
                    }
                }
            }

            return model;
        }

        public static ElementId ToElementId(this ElementIdModel model)
        {
#if REVIT2022 || REVIT2023
            return new ElementId((int)model.Id);
#else
            return new ElementId(model.Id);
#endif
        }
    }
}
