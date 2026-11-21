using System;

namespace Synthetic.Modules.ShadowStudy.Models
{
    /// <summary>
    /// Describes a single study date slot inside a <see cref="ShadowStudyPresetDefinition"/>,
    /// either as a hemisphere-resolved <see cref="AstronomicalEventKind"/> or a fixed custom calendar date.
    /// </summary>
    public class ShadowStudyPresetDateSpec
    {
        /// <summary>
        /// Gets or sets the astronomical event kind (<see cref="AstronomicalEventKind.None"/> for custom dates).
        /// </summary>
        public AstronomicalEventKind AstronomicalEvent { get; set; } = AstronomicalEventKind.None;

        /// <summary>
        /// Gets or sets the fixed calendar date when <see cref="AstronomicalEvent"/> is <see cref="AstronomicalEventKind.None"/>.
        /// </summary>
        public DateTime? CustomDate { get; set; }

        /// <summary>
        /// Gets or sets an optional custom label associated with this preset date slot.
        /// </summary>
        public string CustomLabel { get; set; } = string.Empty;
    }
}
