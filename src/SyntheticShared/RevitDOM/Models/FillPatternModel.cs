using System;
using System.Collections.Generic;
using System.Text;

using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.Shared.RevitAPI;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;

namespace Synthetic.RevitDOM.Models
{
    
    /// <summary>
    /// Represents a model for a Revit FillPattern.
    /// </summary>
    public class FillPatternModel : ObjectModel
    {
        /// <summary>
        /// Name of the Fill Pattern
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Enum reprsenting Drafting or Model patterns
        /// </summary>
        public string Target { get; set; } = string.Empty;

        /// <summary>
        /// Enum representing the orientation of the pattern relative the host or world.
        /// </summary>
        public string HostOrientation { get; set; } = string.Empty;
        
        /// <summary>
        /// List of Fillgrid objects
        /// </summary>
        public List<FillGridModel> FillGrids {  get; set; } = new List<FillGridModel>();               

        /// <summary>
        /// Constructor for an empty object
        /// </summary>
        public FillPatternModel() { }

        /// <summary>
        /// Constructor that takes the Name, Target, Orientation and Fillgrids
        /// </summary>
        /// <param name="name">Name of the Filled Region</param>
        /// <param name="target">Model or Drafting type of Filled Region</param>
        /// <param name="orientation">Orientation of the Filled Region</param>
        /// <param name="fillgrids">List of FillgridModels</param>
        public FillPatternModel (string name, string target, string orientation, List<FillGridModel> fillgrids)
        {
            this.Name = name;
            this.Target = target;
            this.HostOrientation = orientation;
            this.FillGrids = fillgrids;
        }

        /// <summary>
        /// Create a new FillPatternModel by deserializing a JSON string
        /// </summary>
        /// <param name="JSON">A JSON string representing the FillPatternModel</param>
        /// <returns>A FillPatternModel</returns>
        public static FillPatternModel ByJSON (string JSON)
        {
            return JsonConvert.DeserializeObject<FillPatternModel>(JSON)!;
        }

        /// <summary>
        /// Serializes a FillPatternModel to a JSON string.
        /// </summary>
        /// <param name="fillPatternModel">A FillPatternModel</param>
        /// <returns>A JSON string</returns>
        public static string ToJSON (FillPatternModel fillPatternModel)
        {
            return Newtonsoft.Json.JsonConvert.SerializeObject(fillPatternModel, Formatting.Indented);
        }
    }
}

