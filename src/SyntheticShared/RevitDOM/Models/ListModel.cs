using System;
using System.Collections.Generic;
using System.Linq;

using Newtonsoft.Json;

using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;

namespace Synthetic.RevitDOM.Models
{
    /// <summary>
    /// Represents a serialized list of ElementModel objects.
    /// </summary>
    public class ListModel : ObjectModel
    {
        /// <summary>
        /// Gets or sets the list of element models.
        /// </summary>
        public List<ElementModel> Elements { get; set; }

        /// <summary>
        /// Initializes a new instance of the ListModel class.
        /// </summary>
        public ListModel()
        {
            this.Elements = new List<ElementModel>();
        }

        /// <summary>
        /// Initializes a new instance of the ListModel class with the specified list of element models.
        /// </summary>
        /// <param name="ElementJSONs">The list of element models.</param>
        public ListModel(List<ElementModel> ElementJSONs)
        {
            this.Elements = ElementJSONs;
        }

        /// <summary>
        /// Deserializes a ListModel from a JSON string.
        /// </summary>
        /// <param name="JSON">The JSON string representation.</param>
        /// <returns>A ListModel instance.</returns>
        public static ListModel ByJSON(string JSON)
        {
            return JsonConvert.DeserializeObject<ListModel>(JSON) ?? new ListModel();
        }

        /// <summary>
        /// Serializes a ListModel to a JSON string.
        /// </summary>
        /// <param name="ListJSON">The ListModel to serialize.</param>
        /// <returns>A JSON string representation.</returns>
        public static string ToJSON(ListModel ListJSON)
        {
            return Newtonsoft.Json.JsonConvert.SerializeObject(ListJSON, Formatting.Indented);
        }

        /// <summary>
        /// Returns a string representation of the ListModel.
        /// </summary>
        /// <returns>A string displaying class name and element count.</returns>
        public override string ToString()
        {
            return string.Format("{0}(Count=\"{1}\")", this.GetType().Name, this.Elements.Count());
        }
    }

    /// <summary>
    /// Represents a serialized list of MaterialModel objects, inheriting from ListModel.
    /// </summary>
    public class ListMaterialModel : ListModel
    {
        /// <summary>
        /// Gets or sets the list of material models. Hides the base list of element models.
        /// </summary>
        new public List<MaterialModel> Elements { get; set; }

        /// <summary>
        /// Initializes a new instance of the ListMaterialModel class with a list of material models.
        /// </summary>
        /// <param name="MaterialJSONs">The list of material models.</param>
        [JsonConstructor]
        public ListMaterialModel(List<MaterialModel> MaterialJSONs) : base()
        {
            this.Elements = MaterialJSONs;
        }

        /// <summary>
        /// Initializes a new instance of the ListMaterialModel class.
        /// </summary>
        public ListMaterialModel()
        {
            this.Elements = new List<MaterialModel>();
        }

        /// <summary>
        /// Deserializes a ListMaterialModel from a JSON string.
        /// </summary>
        /// <param name="JSON">The JSON representation.</param>
        /// <returns>A ListMaterialModel instance.</returns>
        public new static ListMaterialModel ByJSON(string JSON)
        {
            ListMaterialModel? materials = JsonConvert.DeserializeObject<ListMaterialModel>(JSON);
            return materials ?? new ListMaterialModel();
        }

        /// <summary>
        /// Serializes a ListMaterialModel to a JSON string.
        /// </summary>
        /// <param name="ListJSON">The ListMaterialModel to serialize.</param>
        /// <returns>A JSON representation.</returns>
        public static string ToJSON(ListMaterialModel ListJSON)
        {
            return Newtonsoft.Json.JsonConvert.SerializeObject(ListJSON, Formatting.Indented);
        }

        /// <summary>
        /// Returns a string representation of the ListMaterialModel.
        /// </summary>
        /// <returns>A string displaying class name and material count.</returns>
        public override string ToString()
        {
            return string.Format("{0}(Count=\"{1}\")", this.GetType().Name, this.Elements.Count());
        }
    }

    /// <summary>
    /// Represents a serialized list of string values.
    /// </summary>
    public class ListStringModel
    {
        /// <summary>
        /// Gets or sets the list of strings.
        /// </summary>
        public List<string> Elements { get; set; } = new List<string>();

        /// <summary>
        /// Initializes a new instance of the ListStringModel class.
        /// </summary>
        public ListStringModel () {}
    }
}

