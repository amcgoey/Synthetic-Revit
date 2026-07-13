using System;
using System.Collections.Generic;

namespace Synthetic.Modules.RevitDOM
{
    /// <summary>
    /// Represents a model representation of a Revit ViewPlan, inheriting from ViewModel.
    /// Tracks plan-specific properties: ViewRange, UnderlayId, and UnderlayOrientation.
    /// </summary>
    public class ViewPlanModel : ViewModel
    {
        #region Public Properties

        /// <summary>
        /// Gets or sets the view range (only applies to plan views).
        /// </summary>
        public PlanViewRangeModel? ViewRange { get; set; }

        /// <summary>
        /// Gets or sets the underlay view ID.
        /// </summary>
        public ElementIdModel? UnderlayId { get; set; }

        /// <summary>
        /// Gets or sets the underlay orientation value.
        /// </summary>
        public int UnderlayOrientation { get; set; }

        #endregion

        #region Public Constructors

        /// <summary>
        /// Initializes a new instance of the ViewPlanModel class.
        /// </summary>
        public ViewPlanModel() { }

        #endregion
    }
}
