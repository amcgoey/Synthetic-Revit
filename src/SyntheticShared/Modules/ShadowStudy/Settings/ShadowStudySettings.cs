using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using Autodesk.Revit.DB;
using Newtonsoft.Json;
using Synthetic.Modules.ShadowStudy.Models;
using Synthetic.Settings;

namespace Synthetic.Modules.ShadowStudy.Settings
{
    /// <summary>
    /// Versioned document-stored configuration for the Shadow Study module,
    /// storing studio defaults inherited by built-in astronomical presets and shared user-defined presets.
    /// </summary>
    public class ShadowStudySettings : ISettingModule
    {
        /// <summary>
        /// The unique settings module key name.
        /// </summary>
        [JsonIgnore]
        public const string Name = "ShadowStudy";

        /// <inheritdoc />
        [JsonIgnore]
        public string ModuleKey => Name;

        /// <summary>
        /// Gets or sets the schema version for progressive JSON migration.
        /// </summary>
        public int SchemaVersion { get; set; } = 1;

        /// <summary>
        /// Gets or sets the studio default clock-aligned time increment in minutes (<c>15</c>, <c>30</c>, <c>60</c>, or <c>120</c>).
        /// </summary>
        public int DefaultTimeIncrementMinutes { get; set; } = 60;

        /// <summary>
        /// Gets or sets the studio default minimum solar altitude threshold in degrees (<c>[0..90]</c>).
        /// </summary>
        public double DefaultMinSunAltitudeDegrees { get; set; } = 5.0;

        /// <summary>
        /// Gets or sets the studio default maximum composite darkness percentage (<c>[1..95]</c>).
        /// </summary>
        public int DefaultMaxCompositeDarknessPercent { get; set; } = 80;

        /// <summary>
        /// Gets or sets the studio default preference for preserving each view's existing visual style (<c>false</c> default).
        /// </summary>
        public bool DefaultPreserveVisualStyle { get; set; } = false;

        /// <summary>
        /// Gets or sets the collection of user-defined Shadow Study Presets saved in document storage.
        /// </summary>
        public List<ShadowStudyPresetModel> UserPresets { get; set; } = new List<ShadowStudyPresetModel>();

        /// <summary>
        /// Initializes a new instance of the <see cref="ShadowStudySettings"/> class with baseline studio defaults.
        /// </summary>
        public ShadowStudySettings()
        {
            SchemaVersion = 1;
            DefaultTimeIncrementMinutes = 60;
            DefaultMinSunAltitudeDegrees = 5.0;
            DefaultMaxCompositeDarknessPercent = 80;
            DefaultPreserveVisualStyle = false;
            UserPresets = new List<ShadowStudyPresetModel>();
        }

        [OnDeserialized]
        private void OnDeserialized(StreamingContext context)
        {
            if (SchemaVersion < 1)
            {
                SchemaVersion = 1;
            }

            if (UserPresets == null)
            {
                UserPresets = new List<ShadowStudyPresetModel>();
            }

            foreach (ShadowStudyPresetModel preset in UserPresets)
            {
                if (preset != null && preset.Dates == null)
                {
                    preset.Dates = new List<ShadowStudyPresetDateSpec>();
                }
            }
        }

        /// <summary>
        /// Resets all properties to baseline studio defaults and returns this instance.
        /// </summary>
        public ShadowStudySettings Defaults()
        {
            SchemaVersion = 1;
            DefaultTimeIncrementMinutes = 60;
            DefaultMinSunAltitudeDegrees = 5.0;
            DefaultMaxCompositeDarknessPercent = 80;
            DefaultPreserveVisualStyle = false;
            UserPresets = new List<ShadowStudyPresetModel>();
            return this;
        }

        /// <summary>
        /// Returns an isolated deep copy of this settings object, including all user presets and nested date specs.
        /// </summary>
        public ShadowStudySettings DeepClone()
        {
            var clone = new ShadowStudySettings
            {
                SchemaVersion = SchemaVersion,
                DefaultTimeIncrementMinutes = DefaultTimeIncrementMinutes,
                DefaultMinSunAltitudeDegrees = DefaultMinSunAltitudeDegrees,
                DefaultMaxCompositeDarknessPercent = DefaultMaxCompositeDarknessPercent,
                DefaultPreserveVisualStyle = DefaultPreserveVisualStyle,
                UserPresets = new List<ShadowStudyPresetModel>()
            };

            if (UserPresets != null)
            {
                foreach (ShadowStudyPresetModel preset in UserPresets)
                {
                    if (preset != null)
                    {
                        clone.UserPresets.Add(preset.DeepClone());
                    }
                }
            }

            return clone;
        }

        /// <inheritdoc />
        public bool IsValid(Document doc)
        {
            if (SchemaVersion < 1)
            {
                return false;
            }

            if (!IsValidTimeIncrement(DefaultTimeIncrementMinutes))
            {
                return false;
            }

            if (!IsValidSunAltitude(DefaultMinSunAltitudeDegrees))
            {
                return false;
            }

            if (!IsValidDarknessPercent(DefaultMaxCompositeDarknessPercent))
            {
                return false;
            }

            if (UserPresets != null)
            {
                foreach (ShadowStudyPresetModel preset in UserPresets)
                {
                    if (preset == null || !preset.IsValid())
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        internal static bool IsValidTimeIncrement(int minutes)
        {
            return minutes == 15 || minutes == 30 || minutes == 60 || minutes == 120;
        }

        internal static bool IsValidSunAltitude(double degrees)
        {
            return !double.IsNaN(degrees) &&
                   !double.IsInfinity(degrees) &&
                   degrees >= 0.0 &&
                   degrees <= 90.0;
        }

        internal static bool IsValidDarknessPercent(int percent)
        {
            return percent >= 1 && percent <= 95;
        }
    }

    /// <summary>
    /// Serializable user-defined Shadow Study Preset stored inside <see cref="ShadowStudySettings"/>.
    /// </summary>
    public class ShadowStudyPresetModel
    {
        /// <summary>
        /// Gets or sets the unique display name of this user-defined preset.
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the ordered date specifications (astronomical events and/or custom calendar dates).
        /// </summary>
        public List<ShadowStudyPresetDateSpec> Dates { get; set; } = new List<ShadowStudyPresetDateSpec>();

        /// <summary>
        /// Gets or sets the clock-aligned time increment in minutes (<c>15</c>, <c>30</c>, <c>60</c>, or <c>120</c>).
        /// </summary>
        public int TimeIncrementMinutes { get; set; } = 60;

        /// <summary>
        /// Gets or sets the minimum solar altitude threshold in degrees (<c>[0..90]</c>).
        /// </summary>
        public double MinSunAltitudeDegrees { get; set; } = 5.0;

        /// <summary>
        /// Gets or sets the maximum composite darkness percentage (<c>[1..95]</c>).
        /// </summary>
        public int MaxCompositeDarknessPercent { get; set; } = 80;

        /// <summary>
        /// Gets or sets a value indicating whether each view's existing visual style and background should be preserved.
        /// </summary>
        public bool PreserveVisualStyle { get; set; } = false;

        /// <summary>
        /// Returns an isolated deep copy of this preset model and its date specs.
        /// </summary>
        public ShadowStudyPresetModel DeepClone()
        {
            var clone = new ShadowStudyPresetModel
            {
                Name = Name ?? string.Empty,
                TimeIncrementMinutes = TimeIncrementMinutes,
                MinSunAltitudeDegrees = MinSunAltitudeDegrees,
                MaxCompositeDarknessPercent = MaxCompositeDarknessPercent,
                PreserveVisualStyle = PreserveVisualStyle,
                Dates = new List<ShadowStudyPresetDateSpec>()
            };

            if (Dates != null)
            {
                foreach (ShadowStudyPresetDateSpec spec in Dates)
                {
                    if (spec != null)
                    {
                        clone.Dates.Add(spec.DeepClone());
                    }
                }
            }

            return clone;
        }

        /// <summary>
        /// Validates this user-defined preset's name and solar/visual parameters.
        /// </summary>
        public bool IsValid()
        {
            if (string.IsNullOrWhiteSpace(Name))
            {
                return false;
            }

            if (!ShadowStudySettings.IsValidTimeIncrement(TimeIncrementMinutes))
            {
                return false;
            }

            if (!ShadowStudySettings.IsValidSunAltitude(MinSunAltitudeDegrees))
            {
                return false;
            }

            if (!ShadowStudySettings.IsValidDarknessPercent(MaxCompositeDarknessPercent))
            {
                return false;
            }

            return true;
        }
    }
}
