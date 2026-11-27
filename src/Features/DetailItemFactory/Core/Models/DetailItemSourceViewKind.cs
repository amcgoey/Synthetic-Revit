namespace Synthetic.Features.DetailItemFactory.Models
{
    /// <summary>
    /// Classifies the active Revit 2D source view kind for pure orientation default resolution.
    /// </summary>
    public enum DetailItemSourceViewKind
    {
        /// <summary>
        /// Any other supported 2D view (such as a Detail view).
        /// </summary>
        Other = 0,

        /// <summary>
        /// Floor, ceiling, engineering, or area plan view.
        /// </summary>
        Plan = 1,

        /// <summary>
        /// Elevation or section view.
        /// </summary>
        ElevationOrSection = 2,

        /// <summary>
        /// Legend view (supports Legend Components).
        /// </summary>
        Legend = 3
    }
}
