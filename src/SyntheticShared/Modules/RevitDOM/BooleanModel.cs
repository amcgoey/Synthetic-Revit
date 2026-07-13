using System;
using System.Collections.Generic;
using System.Text;

using Synthetic.Modules.RevitDOM;
using Synthetic.Modules.StandardsManagement.ViewModels;
namespace Synthetic.Modules.RevitDOM
{
    /// <summary>
    /// Model for serializing Revit Objects
    /// </summary>
    public class BooleanModel : ObjectModel
    {
        /// <summary>
        /// Value of the boolean
        /// </summary>
        public bool boolean { get; set; }

        /// <summary>
        /// Constructor
        /// </summary>
        public BooleanModel() { }

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="boolean"></param>
        public BooleanModel(bool boolean)
        {
            this.boolean = boolean;
        }
    }
}
