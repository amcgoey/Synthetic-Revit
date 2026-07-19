using System;
using System.Collections.Generic;

using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;

namespace Synthetic.RevitDOM.Models
{
    /// <summary>
    /// Represents a model representation of a Revit ViewSchedule, inheriting from ViewModel.
    /// Omit standard graphical properties during serialization when left null.
    /// </summary>
    public class ViewScheduleModel : ViewModel
    {
        #region Public Constructors

        /// <summary>
        /// Initializes a new instance of the ViewScheduleModel class.
        /// </summary>
        public ViewScheduleModel() { }

        #endregion
    }
}
