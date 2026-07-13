using System;
using System.Collections.Generic;

namespace Synthetic.Modules.RevitDOM
{
    /// <summary>
    /// Represents a model representation of a Revit ViewSheet, inheriting from ViewModel.
    /// Omit standard graphical properties during serialization when left null.
    /// </summary>
    public class ViewSheetModel : ViewModel
    {
        #region Public Constructors

        /// <summary>
        /// Initializes a new instance of the ViewSheetModel class.
        /// </summary>
        public ViewSheetModel() { }

        #endregion
    }
}
