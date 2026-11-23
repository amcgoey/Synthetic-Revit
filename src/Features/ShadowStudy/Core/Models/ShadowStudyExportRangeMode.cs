namespace Synthetic.Features.ShadowStudy.Models
{
    /// <summary>
    /// Defines whether Shadow Study exports the single active view or a user-selected batch of eligible views.
    /// </summary>
    public enum ShadowStudyExportRangeMode
    {
        /// <summary>
        /// Export only the currently active eligible graphical view.
        /// </summary>
        CurrentView = 0,

        /// <summary>
        /// Export all views checked in the Selected Views checklist.
        /// </summary>
        SelectedViews = 1
    }
}
