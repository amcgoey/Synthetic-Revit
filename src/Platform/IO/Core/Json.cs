using System.Collections.Generic;
using Newtonsoft.Json;

namespace Synthetic.Platform.IO
{
    /// <summary>
    /// Convert objects to and from JSON.
    /// </summary>
    public class Json
    {
        internal Json() { }

        /// <summary>
        /// Serializes an object into indented JSON.
        /// </summary>
        /// <param name="object">An object to serialize.</param>
        /// <returns>A string of JSON.</returns>
        public static string Encode(object? @object)
        {
            return JsonConvert.SerializeObject(@object, Formatting.Indented);
        }

        /// <summary>
        /// Serializes an object to JSON string with no indentation (minimal formatting).
        /// </summary>
        /// <param name="object">The object to serialize.</param>
        /// <returns>A JSON string representation of the object.</returns>
        public static string EncodeMinimal(object? @object)
        {
            return JsonConvert.SerializeObject(@object, Formatting.None);
        }

        /// <summary>
        /// Deserializes an object from JSON.
        /// </summary>
        /// <param name="json">A string of JSON.</param>
        /// <returns>A deserialized object.</returns>
        public static object? Decode(string json)
        {
            return JsonConvert.DeserializeObject(json);
        }

        /// <summary>
        /// Converts a list of objects to an indented JSON string.
        /// </summary>
        /// <param name="ListJSON">The list of objects to serialize.</param>
        /// <returns>An indented JSON string.</returns>
        public static string ListToJSON(List<object> ListJSON)
        {
            return JsonConvert.SerializeObject(ListJSON, Formatting.Indented);
        }

        /// <summary>
        /// Deserializes a JSON string into a general object list structure.
        /// </summary>
        /// <param name="JSON">The JSON string representing list data.</param>
        /// <returns>A deserialized object structure.</returns>
        public static object? JsonToList(string JSON)
        {
            return JsonConvert.DeserializeObject(JSON);
        }
    }
}
