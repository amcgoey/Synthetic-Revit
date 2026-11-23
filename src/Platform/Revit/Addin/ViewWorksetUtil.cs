using Autodesk.Revit.DB;
using RevitView = Autodesk.Revit.DB.View;

namespace Synthetic.Platform.Revit
{
    /// <summary>
    /// Shared view and workset visibility helpers across feature slices.
    /// </summary>
    public static class ViewWorksetUtil
    {
        /// <summary>
        /// Sets a workset to visible in a specific view when the view is not controlled by a view template.
        /// </summary>
        /// <param name="view">The Revit view.</param>
        /// <param name="workset">The workset to make visible.</param>
        /// <returns><c>true</c> if successful; <c>false</c> if skipped due to an applied view template.</returns>
        public static bool SetWorksetVisibilityInView(RevitView view, Workset workset)
        {
            if (view.ViewTemplateId != ElementId.InvalidElementId)
            {
                return false;
            }

            view.SetWorksetVisibility(workset.Id, WorksetVisibility.Visible);
            return true;
        }
    }
}
