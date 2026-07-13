using System;
using System.Collections.Generic;
using System.Linq;

using Newtonsoft.Json;

using Synthetic.Modules.StandardsManagement.ViewModels;
namespace Synthetic.Modules.RevitDOM
{
    /// <summary>
    /// Model that acts as a wrapper for Revit Category ElementIds to facilitate JSON serialization.
    /// </summary>
    public class CategoryIdModel : ObjectModel
    {
        /// <summary>
        /// Gets or sets the name of the category.
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the integer ID of the category.
        /// </summary>
        public long Id { get; set; }

        /// <summary>
        /// Gets or sets the associated Revit Category object. Ignored in JSON.
        /// </summary>
        [JsonIgnoreAttribute]
        public object? Category { get; set; }

        /// <summary>
        /// Gets or sets the associated Revit Document. Ignored in JSON.
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
        /// If IsTemplate, don't serialize the Element Id
        /// </summary>
        /// <returns>True if not a template</returns>
        public bool ShouldSerializeId()
        {
            return !IsTemplate;
        }

        /// <summary>
        /// Initializes a new instance of the CategoryIdModel class.
        /// </summary>
        public CategoryIdModel ()
        {
            this.IsTemplate = false;        
        }
    }

}

