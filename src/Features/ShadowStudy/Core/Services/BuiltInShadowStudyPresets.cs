using System;
using System.Collections.Generic;
using Synthetic.Features.ShadowStudy.Models;

namespace Synthetic.Features.ShadowStudy.Services
{
    /// <summary>
    /// Provides read-only built-in Shadow Study Presets and hemisphere-aware solstice/equinox calendar date mapping.
    /// </summary>
    public static class BuiltInShadowStudyPresets
    {
        /// <summary>Stable preset ID for the default 3-date Seasonal Study.</summary>
        public const string SeasonalStudyId = "built-in:seasonal-study";

        /// <summary>Stable preset ID for Spring Equinox.</summary>
        public const string SpringEquinoxId = "built-in:spring-equinox";

        /// <summary>Stable preset ID for Summer Solstice.</summary>
        public const string SummerSolsticeId = "built-in:summer-solstice";

        /// <summary>Stable preset ID for Fall Equinox.</summary>
        public const string FallEquinoxId = "built-in:fall-equinox";

        /// <summary>Stable preset ID for Winter Solstice.</summary>
        public const string WinterSolsticeId = "built-in:winter-solstice";

        /// <summary>Gets the default 3-date Seasonal Study preset (Summer Solstice, Spring Equinox, Winter Solstice).</summary>
        public static ShadowStudyPresetDefinition SeasonalStudy { get; } = new ShadowStudyPresetDefinition
        {
            Id = SeasonalStudyId,
            Name = "Seasonal Study",
            IsBuiltIn = true,
            IsDefault = true,
            Description = "Summer Solstice, Spring Equinox, and Winter Solstice",
            DateSpecs = new[]
            {
                new ShadowStudyPresetDateSpec { AstronomicalEvent = AstronomicalEventKind.SummerSolstice },
                new ShadowStudyPresetDateSpec { AstronomicalEvent = AstronomicalEventKind.SpringEquinox },
                new ShadowStudyPresetDateSpec { AstronomicalEvent = AstronomicalEventKind.WinterSolstice }
            }
        };

        /// <summary>Gets the single-date Spring Equinox preset.</summary>
        public static ShadowStudyPresetDefinition SpringEquinox { get; } = CreateSingleEventPreset(
            SpringEquinoxId,
            AstronomicalEventKind.SpringEquinox);

        /// <summary>Gets the single-date Summer Solstice preset.</summary>
        public static ShadowStudyPresetDefinition SummerSolstice { get; } = CreateSingleEventPreset(
            SummerSolsticeId,
            AstronomicalEventKind.SummerSolstice);

        /// <summary>Gets the single-date Fall Equinox preset.</summary>
        public static ShadowStudyPresetDefinition FallEquinox { get; } = CreateSingleEventPreset(
            FallEquinoxId,
            AstronomicalEventKind.FallEquinox);

        /// <summary>Gets the single-date Winter Solstice preset.</summary>
        public static ShadowStudyPresetDefinition WinterSolstice { get; } = CreateSingleEventPreset(
            WinterSolsticeId,
            AstronomicalEventKind.WinterSolstice);

        /// <summary>Gets all built-in read-only Shadow Study Presets in display order.</summary>
        public static IReadOnlyList<ShadowStudyPresetDefinition> All { get; } = new[]
        {
            SeasonalStudy,
            SpringEquinox,
            SummerSolstice,
            FallEquinox,
            WinterSolstice
        };

        /// <summary>
        /// Returns the canonical human-readable label for an <see cref="AstronomicalEventKind"/>.
        /// </summary>
        public static string GetEventLabel(AstronomicalEventKind eventKind)
        {
            switch (eventKind)
            {
                case AstronomicalEventKind.SpringEquinox:
                    return "Spring Equinox";
                case AstronomicalEventKind.SummerSolstice:
                    return "Summer Solstice";
                case AstronomicalEventKind.FallEquinox:
                    return "Fall Equinox";
                case AstronomicalEventKind.WinterSolstice:
                    return "Winter Solstice";
                default:
                    return string.Empty;
            }
        }

        /// <summary>
        /// Resolves the calendar date for an <see cref="AstronomicalEventKind"/> in <paramref name="targetYear"/>
        /// using the site's hemisphere (<see cref="ShadowStudySiteInfo.IsSouthernHemisphere"/>).
        /// </summary>
        public static DateTime ResolveDate(
            AstronomicalEventKind eventKind,
            int targetYear,
            ShadowStudySiteInfo? siteInfo)
        {
            bool isSouthern = siteInfo != null && siteInfo.IsSouthernHemisphere;
            return ResolveDate(eventKind, targetYear, isSouthern);
        }

        /// <summary>
        /// Resolves the calendar date for an <see cref="AstronomicalEventKind"/> in <paramref name="targetYear"/>
        /// for Northern (<c>isSouthernHemisphere == false</c>) or Southern (<c>isSouthernHemisphere == true</c>) sites.
        /// </summary>
        public static DateTime ResolveDate(
            AstronomicalEventKind eventKind,
            int targetYear,
            bool isSouthernHemisphere)
        {
            if (targetYear < 1 || targetYear > 9999)
            {
                throw new ArgumentOutOfRangeException(nameof(targetYear), "Target year must be between 1 and 9999.");
            }

            switch (eventKind)
            {
                case AstronomicalEventKind.SpringEquinox:
                    return isSouthernHemisphere
                        ? new DateTime(targetYear, 9, 22)
                        : new DateTime(targetYear, 3, 20);
                case AstronomicalEventKind.SummerSolstice:
                    return isSouthernHemisphere
                        ? new DateTime(targetYear, 12, 21)
                        : new DateTime(targetYear, 6, 21);
                case AstronomicalEventKind.FallEquinox:
                    return isSouthernHemisphere
                        ? new DateTime(targetYear, 3, 20)
                        : new DateTime(targetYear, 9, 22);
                case AstronomicalEventKind.WinterSolstice:
                    return isSouthernHemisphere
                        ? new DateTime(targetYear, 6, 21)
                        : new DateTime(targetYear, 12, 21);
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(eventKind),
                        "AstronomicalEventKind.None does not map to an astronomical calendar date.");
            }
        }

        /// <summary>
        /// Expands a <see cref="ShadowStudyPresetDefinition"/> into resolved <see cref="ShadowStudyDateEntry"/> items
        /// for the given <paramref name="targetYear"/> and <paramref name="siteInfo"/>.
        /// </summary>
        public static IReadOnlyList<ShadowStudyDateEntry> ExpandPreset(
            ShadowStudyPresetDefinition preset,
            int targetYear,
            ShadowStudySiteInfo? siteInfo)
        {
            bool isSouthern = siteInfo != null && siteInfo.IsSouthernHemisphere;
            return ExpandPreset(preset, targetYear, isSouthern);
        }

        /// <summary>
        /// Expands a <see cref="ShadowStudyPresetDefinition"/> into resolved <see cref="ShadowStudyDateEntry"/> items
        /// for the given <paramref name="targetYear"/> and hemisphere.
        /// </summary>
        public static IReadOnlyList<ShadowStudyDateEntry> ExpandPreset(
            ShadowStudyPresetDefinition preset,
            int targetYear,
            bool isSouthernHemisphere)
        {
            if (preset == null) throw new ArgumentNullException(nameof(preset));

            var result = new List<ShadowStudyDateEntry>();
            if (preset.DateSpecs == null)
            {
                return result.AsReadOnly();
            }

            foreach (var spec in preset.DateSpecs)
            {
                if (spec == null) continue;

                if (spec.AstronomicalEvent != AstronomicalEventKind.None)
                {
                    DateTime resolvedDate = ResolveDate(spec.AstronomicalEvent, targetYear, isSouthernHemisphere);
                    string label = string.IsNullOrWhiteSpace(spec.CustomLabel)
                        ? GetEventLabel(spec.AstronomicalEvent)
                        : spec.CustomLabel.Trim();

                    result.Add(new ShadowStudyDateEntry
                    {
                        Date = resolvedDate,
                        AstronomicalEvent = spec.AstronomicalEvent,
                        PresetName = label
                    });
                }
                else if (spec.CustomDate.HasValue)
                {
                    result.Add(new ShadowStudyDateEntry
                    {
                        Date = spec.CustomDate.Value.Date,
                        AstronomicalEvent = AstronomicalEventKind.None,
                        PresetName = string.Empty
                    });
                }
            }

            return result.AsReadOnly();
        }

        private static ShadowStudyPresetDefinition CreateSingleEventPreset(string id, AstronomicalEventKind eventKind)
        {
            string label = GetEventLabel(eventKind);
            return new ShadowStudyPresetDefinition
            {
                Id = id,
                Name = label,
                IsBuiltIn = true,
                IsDefault = false,
                Description = label,
                DateSpecs = new[]
                {
                    new ShadowStudyPresetDateSpec { AstronomicalEvent = eventKind }
                }
            };
        }
    }
}
