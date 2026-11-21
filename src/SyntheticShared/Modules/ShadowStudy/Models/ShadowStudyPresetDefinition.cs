using System;
using System.Collections.Generic;

namespace Synthetic.Modules.ShadowStudy.Models
{
    /// <summary>
    /// Reusable Shadow Study Preset descriptor supporting both built-in read-only astronomical presets
    /// and user-defined presets.
    /// </summary>
    public class ShadowStudyPresetDefinition
    {
        /// <summary>
        /// Gets or sets the stable unique identifier for this preset.
        /// </summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the display name of this preset.
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets a value indicating whether this preset is a read-only built-in preset.
        /// </summary>
        public bool IsBuiltIn { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether this preset is the default selection on dialog open.
        /// </summary>
        public bool IsDefault { get; set; }

        /// <summary>
        /// Gets or sets a concise human-readable description of the preset.
        /// </summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the ordered date specifications included in this preset.
        /// </summary>
        public IReadOnlyList<ShadowStudyPresetDateSpec> DateSpecs { get; set; } = Array.Empty<ShadowStudyPresetDateSpec>();

        /// <summary>
        /// Gets or sets an optional time increment override in minutes for user-defined presets.
        /// </summary>
        public int? TimeStepMinutes { get; set; }

        /// <summary>
        /// Gets or sets an optional minimum solar altitude threshold override in degrees.
        /// </summary>
        public double? MinSunAltitudeDegrees { get; set; }

        /// <summary>
        /// Gets or sets an optional maximum composite darkness percentage override.
        /// </summary>
        public int? MaxCompositeDarknessPercent { get; set; }

        /// <summary>
        /// Gets or sets an optional Hidden Line override preference.
        /// </summary>
        public bool? ForceHiddenLine { get; set; }

        /// <summary>
        /// Gets or sets an optional pure white background preference.
        /// </summary>
        public bool? ForceWhiteBackground { get; set; }

        /// <summary>
        /// Gets or sets an optional frame-retention preference.
        /// </summary>
        public bool? ExportIndividualFrames { get; set; }
    }
}
