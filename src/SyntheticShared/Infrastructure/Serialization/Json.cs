using System;
using System.Collections.Generic;
using j = Newtonsoft.Json;

using Synthetic.Infrastructure.Serialization;
using Synthetic.Modules.RevitDOM;
using Synthetic.Modules.StandardsManagement.ViewModels;
namespace Synthetic.Infrastructure.Serialization{
    /// <summary>
    /// Convert Revit Elements to and from JSON.
    /// </summary>
    public class Json
    {
        internal Json() { }

        /// <summary>
        /// Serializes an element into JSON.
        /// </summary>
        /// <param name="object">An object to serialize.</param>
        /// <returns name="JSON">A string of JSON.</returns>
        public static string Encode(System.Object @object)
        {
            return j.JsonConvert.SerializeObject(@object, j.Formatting.Indented);
        }

        /// <summary>
        /// Serializes an object to JSON string with no indentation (minimal formatting).
        /// </summary>
        /// <param name="object">The object to serialize.</param>
        /// <returns>A JSON string representation of the object.</returns>
        public static string EncodeMinimal(System.Object @object)
        {
            return j.JsonConvert.SerializeObject(@object, j.Formatting.None);
        }

        /// <summary>
        /// Deserializes an element from JSON.
        /// </summary>
        /// <param name="json">A string of JSON</param>
        /// <returns>An object to deserialize.</returns>
        public static System.Object? Decode(string json)
        {
            return j.JsonConvert.DeserializeObject(json);
        }

        /// <summary>
        /// Converts a list of objects to an indented JSON string.
        /// </summary>
        /// <param name="ListJSON">The list of objects to serialize.</param>
        /// <returns>An indented JSON string.</returns>
        public static string ListToJSON(List<System.Object> ListJSON)
        {
            return j.JsonConvert.SerializeObject(ListJSON, j.Formatting.Indented);
        }

        /// <summary>
        /// Deserializes a JSON string into a general object list structure.
        /// </summary>
        /// <param name="JSON">The JSON string representing list data.</param>
        /// <returns>A deserialized object structure.</returns>
        public static object? JsonToList(string JSON)
        {
            return j.JsonConvert.DeserializeObject(JSON);
        }
    }
}
