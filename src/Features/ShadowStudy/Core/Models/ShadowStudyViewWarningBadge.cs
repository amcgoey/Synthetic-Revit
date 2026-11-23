namespace Synthetic.Features.ShadowStudy.Models
{
    /// <summary>
    /// Non-blocking informational warning badge surfaced for an eligible Shadow Study view.
    /// </summary>
    public class ShadowStudyViewWarningBadge
    {
        /// <summary>
        /// Gets or sets the short warning code identifier.
        /// </summary>
        public string Code { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the short badge label shown in the UI.
        /// </summary>
        public string Label { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the full descriptive tooltip message.
        /// </summary>
        public string Description { get; set; } = string.Empty;
    }
}
