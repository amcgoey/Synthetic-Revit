using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

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
        [JsonProperty("AstronomicalEvent")]
        [JsonConverter(typeof(StringEnumConverter))]
        public AstronomicalEventKind AstronomicalEvent { get; set; } = AstronomicalEventKind.None;

        /// <summary>
        /// Gets or sets the fixed calendar date when <see cref="AstronomicalEvent"/> is <see cref="AstronomicalEventKind.None"/>.
        /// </summary>
        [JsonProperty("CustomDate")]
        public DateTime? CustomDate { get; set; }

        /// <summary>
        /// Gets or sets an optional custom label associated with this preset date slot.
        /// </summary>
        [JsonProperty("CustomLabel")]
        public string CustomLabel { get; set; } = string.Empty;

        /// <summary>
        /// Creates a deep copy of this preset date specification.
        /// </summary>
        public ShadowStudyPresetDateSpec DeepClone()
        {
            return new ShadowStudyPresetDateSpec
            {
                AstronomicalEvent = AstronomicalEvent,
                CustomDate = CustomDate,
                CustomLabel = CustomLabel ?? string.Empty
            };
        }
    }
}
