using Synthetic.Modules.Worksets.Commands;
using Synthetic.Modules.Worksets.Models;
using Synthetic.Modules.Worksets.Utilities;

using System;
using System.Collections.Generic;
using System.Linq;
using Synthetic.Infrastructure.Serialization;
using Synthetic.Infrastructure.IO;

using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;
using Synthetic.Modules.StandardsManagement.ViewModels;
namespace Synthetic.Modules.Worksets.Models
{
    /// <summary>
    /// Represents a model representation of a Revit Workset, mapping its name, visibility, alias, and description.
    /// </summary>
    public class WorksetModel : ObjectModel
    {
        /// <summary>
        /// Gets or sets the name of the workset.
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets a value indicating whether the workset is visible.
        /// </summary>
        public bool Visibility { get; set; }

        /// <summary>
        /// Gets or sets the alias name of the workset.
        /// </summary>
        public string? Alias { get; set; }

        /// <summary>
        /// Gets or sets the description of the workset.
        /// </summary>
        public string? Description { get; set; }

        /// <summary>
        /// Initializes a new instance of the WorksetModel class.
        /// </summary>
        /// <param name="name">The name of the workset.</param>
        /// <param name="visibility">Visibility state, default is true.</param>
        /// <param name="alias">The alias name.</param>
        /// <param name="description">The description.</param>
        public WorksetModel(string name, bool visibility = true, string? alias = null, string? description = null)
        {
            this.Name = name;
            this.Visibility = visibility;
            this.Alias = alias;
            this.Description = description;
        }

        //public WorksetModel(object[] excelRow)
        //{
        //    this.name = excelRow[0].ToString();
        //    if (excelRow[1].ToString() == "True") { this.visibility = true; }
        //    else { this.visibility = false; }
        //    this.description = excelRow[2].ToString();
        //}
    }
}
