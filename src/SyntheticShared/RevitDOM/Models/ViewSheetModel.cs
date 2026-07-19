using System;
using System.Collections.Generic;

using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;

namespace Synthetic.RevitDOM.Models
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
