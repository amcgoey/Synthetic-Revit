using System;
using System.Collections.Generic;

namespace Synthetic.Modules.RevitDOM
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
