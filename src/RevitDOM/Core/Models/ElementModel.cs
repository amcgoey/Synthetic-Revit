using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace Synthetic.RevitDOM.Models
{
    /// <summary>
    /// Base model representing a Revit Element, providing properties and methods for serialization, parameter management, and modification.
    /// </summary>
    public class ElementModel : ObjectModel
    {
        #region Public Properties
       
        /// <summary>
        /// Gets or sets the class name of the element.
        /// </summary>
        virtual public string Class
        {
            get => this.ElementId.Class;
            set => this.ElementId.Class = value;
        }

        /// <summary>
        /// Gets or sets the category name of the element.
        /// </summary>
        virtual public string Category
        {
            get => this.ElementId.Category;
            set => this.ElementId.Category = value;
        }

        /// <summary>
        /// Gets or sets the name of the element.
        /// </summary>
        public string Name
        {
            get => this.ElementId.Name;
            set => this.ElementId.Name = value;
        }

        /// <summary>
        /// Gets or sets the list of name aliases.
        /// </summary>
        public List<string>? Aliases
        {
            get => this.ElementId.Aliases;
            set => this.ElementId.Aliases = value;
        }

        /// <summary>
        /// Gets or sets the integer ID value of the element.
        /// </summary>
        public long Id
        {
            get => this.ElementId.Id;
            set => this.ElementId.Id = value;
        }

        /// <summary>
        /// Gets or sets the unique string ID of the element.
        /// </summary>
        public string? UniqueId
        {
            get => this.ElementId.UniqueId;
            set => this.ElementId.UniqueId = value;
        }

        /// <summary>
        /// List of the Parameters that belong to the Element.
        /// </summary>
        public List<ParameterModel> Parameters { get; set; } = new List<ParameterModel>();

        /// <summary>
        /// Gets or sets the count of instances in the document.
        /// </summary>
        public int InstanceCount { get; set; }

        /// <summary>
        /// Gets or sets the location point representation.
        /// </summary>
        public XYZModel? Location { get; set; }

        /// <summary>
        /// Gets or sets the bounding box representation.
        /// </summary>
        public BoundingBoxXYZModel? BoundingBox { get; set; }

        /// <summary>
        /// Nested collection of types/symbols belonging to this element (e.g. for families).
        /// </summary>
        public List<ElementModel> NestedTypes { get; set; } = new List<ElementModel>();

        /// <summary>
        /// The Revit ElementId of the element linked to the SerialElement.
        /// </summary>
        [JsonIgnoreAttribute]
        public ElementIdModel ElementId { get; set; } = new ElementIdModel();

        /// <summary>
        /// The Revit Element that is linked to the SerialElement.
        /// </summary>
        [JsonIgnoreAttribute]
        virtual public object? Element { get; set; }

        /// <summary>
        /// The Revit Document the element belongs too.
        /// </summary>
        [JsonIgnoreAttribute]
        public object? Document { get; set; }

        /// <summary>
        /// If true, SerialElement is intended to be deserialized as a template for use as standards or transfer to another project.
        /// If false, SerialElement is intended to modify an element inside the project and will include ElementIds and UniqueIds.
        /// </summary>
        [JsonIgnoreAttribute]
        public bool IsTemplate { get; set; }

        /// <summary>
        /// Gets or sets the name of the parent element that triggered the harvesting of this dependency.
        /// </summary>
        [JsonIgnoreAttribute]
        public string? DependencyOrigin { get; set; }

        #endregion
        #region Conditional Serialization Methods for Properties

        /// <summary>
        /// If IsTemplate, don't serialize the Element Id
        /// </summary>
        /// <returns>True if not a template</returns>
        public bool ShouldSerializeId()
        {
            return !IsTemplate;
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
        /// Initializes a new instance of the ElementModel class.
        /// </summary>
        public ElementModel()
        {
            this.ElementId = new ElementIdModel();
            this.IsTemplate = false;
        }

        #endregion
        #region Public Methods

        /// <summary>
        /// Sets the SerialElement's aliases given a list of strings.
        /// </summary>
        /// <param name="serialElement">A SerialElement</param>
        /// <param name="aliases">A list of strings representing name aliases.</param>
        /// <returns name="serialElement">Returns the modified SerialElement</returns>
        public static ElementModel SetAliases (ElementModel serialElement, List<string> aliases)
        {
            if (aliases.Count()>0)
            {
                serialElement.Aliases = aliases;
            }
            return serialElement;
        }

        /// <summary>
        /// Returns a string representation of the ElementModel.
        /// </summary>
        /// <returns>A string detailing the model name and ID.</returns>
        public override string ToString()
        {
            return string.Format("{0}(Name=\"{1}\", ID={2})", this.GetType().Name, this.Name, this.Id);
        }

        /// <summary>
        /// Creates a shallow copy of the ElementModel.
        /// </summary>
        /// <returns>A shallow copy of the model.</returns>
        public object Clone()
        {
            return this.MemberwiseClone();
        }

        /// <summary>
        /// Deserializes a JSON string into an ElementModel instance.
        /// </summary>
        /// <param name="JSON">The JSON string.</param>
        /// <returns>An ElementModel instance.</returns>
        public static ElementModel ByJSON(string JSON)
        {
            return JsonConvert.DeserializeObject<ElementModel>(JSON)!;
        }

        /// <summary>
        /// Serializes an ElementModel instance to a JSON string.
        /// </summary>
        /// <param name="serialElement">The instance to serialize.</param>
        /// <returns>A JSON string.</returns>
        public static string ToJSON(ElementModel serialElement)
        {
            return Newtonsoft.Json.JsonConvert.SerializeObject(serialElement, Formatting.Indented);
        }

        #endregion
    }
}

