using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using Newtonsoft.Json;

using Synthetic.Infrastructure.Serialization;
using Autodesk.Revit.DB;

using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;

namespace Synthetic.RevitDOM.Models
{
    /// <summary>
    /// Represents a model representation of a Revit Parameter, mapping name, value, and storage type.
    /// </summary>
    public class ParameterModel : ObjectModel
    {
        #region Public Properties

        /// <summary>
        /// Gets or sets the parameter name.
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the parameter value as a string.
        /// </summary>
        public string? Value { get; set; }

        /// <summary>
        /// Gets or sets the parameter value as an ElementId model, if the parameter storage type is ElementId.
        /// </summary>
        public ElementIdModel? ValueElemId { get; set; }

        /// <summary>
        /// Gets or sets the storage type of the parameter (e.g. Double, Integer, String, ElementId).
        /// </summary>
        public string StorageType { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the integer value of the parameter definition ID.
        /// </summary>
        public long Id { get; set; }

        /// <summary>
        /// Gets or sets the GUID of the parameter, if it is a shared parameter.
        /// </summary>
        public string? GUID { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether this is a shared parameter.
        /// </summary>
        public bool IsShared { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether this parameter is read-only.
        /// </summary>
        public bool IsReadOnly { get; set; }

        /// <summary>
        /// If true, SerialParameter is intended to be deserialized as a template for use as standards or transfer to another project.
        /// If false, SerialParameter is intended to modify an element inside the project and will include ElementIds and UniqueIds.
        /// </summary>
        [JsonIgnoreAttribute]
        public bool IsTemplate { get; set; }

        #endregion

        #region Public Constructors

        /// <summary>
        /// Initializes a new instance of the ParameterModel class with specified properties.
        /// </summary>
        /// <param name="Name">The parameter name.</param>
        /// <param name="Value">The parameter value string.</param>
        /// <param name="ValueElemId">The parameter value ElementId model.</param>
        /// <param name="StorageType">The storage type of the parameter.</param>
        /// <param name="Id">The parameter definition ID.</param>
        /// <param name="GUID">The shared parameter GUID.</param>
        /// <param name="IsShared">True if shared.</param>
        /// <param name="IsReadOnly">True if read-only.</param>
        /// <summary>
        /// Initializes a new instance of the ParameterModel class.
        /// </summary>
        public ParameterModel() { }

        [JsonConstructor]
        public ParameterModel(string Name, string? Value, ElementIdModel? ValueElemId, string StorageType, long Id, string? GUID, bool IsShared, bool IsReadOnly)
        {
            this.Name = Name;
            this.Value = Value;
            this.ValueElemId = ValueElemId;
            this.StorageType = StorageType;
            this.Id = Id;
            this.GUID = GUID;
            this.IsShared = IsShared;
            this.IsReadOnly = IsReadOnly;

            this.IsTemplate = false;
        }

        #endregion
        #region Public Methods

        /// <summary>
        /// Serializes a ParameterModel to a JSON string.
        /// </summary>
        /// <param name="parameter">The ParameterModel to serialize.</param>
        /// <returns>A JSON string representation.</returns>
        public static string ToJSON(ParameterModel parameter)
        {
            return Newtonsoft.Json.JsonConvert.SerializeObject(parameter, Formatting.Indented);
        }

        #endregion
    }

    public static class ParameterExtensions
    {
        public static ParameterModel ToModel(this Parameter parameter, Document doc, bool isTemplate = false)
        {
            if (parameter == null) return null;
            
            long idVal;
#if REVIT2022 || REVIT2023
            idVal = parameter.Id.IntegerValue;
#else
            idVal = parameter.Id.Value;
#endif

            var model = new ParameterModel
            {
                Name = parameter.Definition.Name,
                StorageType = parameter.StorageType.ToString(),
                IsReadOnly = parameter.IsReadOnly,
                Id = idVal,
                IsShared = parameter.IsShared,
                IsTemplate = isTemplate
            };

            if (parameter.IsShared && parameter.GUID != null)
            {
                model.GUID = parameter.GUID.ToString();
            }

            if (parameter.HasValue)
            {
                if (parameter.StorageType == StorageType.ElementId)
                {
                    model.ValueElemId = parameter.AsElementId().ToModel(doc, isTemplate);
                }
                else if (parameter.StorageType == StorageType.Integer)
                {
                    model.Value = parameter.AsInteger().ToString();
                }
                else if (parameter.StorageType == StorageType.Double)
                {
                    model.Value = parameter.AsDouble().ToString(System.Globalization.CultureInfo.InvariantCulture);
                }
                else if (parameter.StorageType == StorageType.String)
                {
                    model.Value = parameter.AsString();
                }
            }

            return model;
        }
    }
}

