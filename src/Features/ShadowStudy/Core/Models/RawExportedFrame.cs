namespace Synthetic.Features.ShadowStudy.Models
{
    /// <summary>
    /// Represents a single exported raw Shadow Frame PNG and its optional per-frame reference PNG.
    /// </summary>
    public class RawExportedFrame
    {
        /// <summary>
        /// Gets or sets the solar slot associated with this frame.
        /// </summary>
        public SolarFrameSlot Slot { get; set; } = new SolarFrameSlot();

        /// <summary>
        /// Gets or sets the file path of the exported shadow frame PNG.
        /// </summary>
        public string FramePngPath { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the optional per-frame <c>ShadowIntensity = 0</c> reference PNG path.
        /// </summary>
        public string? PerFrameReferencePngPath { get; set; }
    }
}
