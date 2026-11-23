using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Synthetic.Features.ShadowStudy.Models;
using Synthetic.Features.ShadowStudy.Services;

namespace SyntheticTests.Features.ShadowStudy
{
    [TestFixture]
    [UnitTest]
    public class BuiltInShadowStudyPresetsTests
    {
        [Test]
        public void All_ContainsFiveReadOnlyPresetsWithSeasonalStudyAsDefault()
        {
            IReadOnlyList<ShadowStudyPresetDefinition> all = BuiltInShadowStudyPresets.All;

            Assert.AreEqual(5, all.Count);
            CollectionAssert.AreEqual(
                new[]
                {
                    "Seasonal Study",
                    "Spring Equinox",
                    "Summer Solstice",
                    "Fall Equinox",
                    "Winter Solstice"
                },
                all.Select(p => p.Name).ToArray());

            Assert.That(all.All(p => p.IsBuiltIn), Is.True);
            Assert.AreEqual(1, all.Count(p => p.IsDefault));
            Assert.AreEqual("Seasonal Study", all.Single(p => p.IsDefault).Name);
        }

        [Test]
        public void ExpandPreset_NorthernHemisphere_SeasonalStudyResolvesThreeDatesWithEventLabels()
        {
            var northSite = new ShadowStudySiteInfo
            {
                PlaceName = "Boston, MA",
                LatitudeDegrees = 42.3601,
                LongitudeDegrees = -71.0589
            };

            IReadOnlyList<ShadowStudyDateEntry> entries = BuiltInShadowStudyPresets.ExpandPreset(
                BuiltInShadowStudyPresets.SeasonalStudy,
                2026,
                northSite);

            Assert.AreEqual(3, entries.Count);

            Assert.AreEqual(new DateTime(2026, 6, 21), entries[0].Date);
            Assert.AreEqual(AstronomicalEventKind.SummerSolstice, entries[0].AstronomicalEvent);
            Assert.AreEqual("Summer Solstice", entries[0].PresetName);
            Assert.AreEqual("260621 - Summer Solstice", entries[0].DateLabel);

            Assert.AreEqual(new DateTime(2026, 3, 20), entries[1].Date);
            Assert.AreEqual(AstronomicalEventKind.SpringEquinox, entries[1].AstronomicalEvent);
            Assert.AreEqual("Spring Equinox", entries[1].PresetName);
            Assert.AreEqual("260320 - Spring Equinox", entries[1].DateLabel);

            Assert.AreEqual(new DateTime(2026, 12, 21), entries[2].Date);
            Assert.AreEqual(AstronomicalEventKind.WinterSolstice, entries[2].AstronomicalEvent);
            Assert.AreEqual("Winter Solstice", entries[2].PresetName);
            Assert.AreEqual("261221 - Winter Solstice", entries[2].DateLabel);
        }

        [Test]
        public void ExpandPreset_SouthernHemisphere_FlipsSummerWinterAndSpringFallCalendarDates()
        {
            var southSite = new ShadowStudySiteInfo
            {
                PlaceName = "Sydney, Australia",
                LatitudeDegrees = -33.8688,
                LongitudeDegrees = 151.2093
            };

            IReadOnlyList<ShadowStudyDateEntry> seasonal = BuiltInShadowStudyPresets.ExpandPreset(
                BuiltInShadowStudyPresets.SeasonalStudy,
                2026,
                southSite);

            Assert.AreEqual(3, seasonal.Count);
            Assert.AreEqual(new DateTime(2026, 12, 21), seasonal[0].Date);
            Assert.AreEqual("261221 - Summer Solstice", seasonal[0].DateLabel);

            Assert.AreEqual(new DateTime(2026, 9, 22), seasonal[1].Date);
            Assert.AreEqual("260922 - Spring Equinox", seasonal[1].DateLabel);

            Assert.AreEqual(new DateTime(2026, 6, 21), seasonal[2].Date);
            Assert.AreEqual("260621 - Winter Solstice", seasonal[2].DateLabel);

            ShadowStudyDateEntry fall = BuiltInShadowStudyPresets.ExpandPreset(
                BuiltInShadowStudyPresets.FallEquinox,
                2026,
                southSite).Single();

            Assert.AreEqual(new DateTime(2026, 3, 20), fall.Date);
            Assert.AreEqual(AstronomicalEventKind.FallEquinox, fall.AstronomicalEvent);
            Assert.AreEqual("260320 - Fall Equinox", fall.DateLabel);
        }

        [Test]
        public void ResolveDate_EquatorZeroLatitude_TreatsAsNorthernHemisphere()
        {
            var equatorSite = new ShadowStudySiteInfo
            {
                PlaceName = "Quito",
                LatitudeDegrees = 0.0,
                LongitudeDegrees = -78.4678
            };

            Assert.IsFalse(equatorSite.IsSouthernHemisphere);
            Assert.AreEqual(
                new DateTime(2028, 6, 21),
                BuiltInShadowStudyPresets.ResolveDate(AstronomicalEventKind.SummerSolstice, 2028, equatorSite));
            Assert.AreEqual(
                new DateTime(2028, 9, 22),
                BuiltInShadowStudyPresets.ResolveDate(AstronomicalEventKind.FallEquinox, 2028, equatorSite));
        }

        [Test]
        public void DateEntry_SanitizesUnsafePresetNameInDateLabel()
        {
            var entry = new ShadowStudyDateEntry
            {
                Date = new DateTime(2026, 6, 21),
                AstronomicalEvent = AstronomicalEventKind.SummerSolstice,
                PresetName = "Summer/Solstice:Test"
            };

            Assert.AreEqual("260621 - Summer_Solstice_Test", entry.DateLabel);

            var customEntry = new ShadowStudyDateEntry
            {
                Date = new DateTime(2026, 7, 4),
                AstronomicalEvent = AstronomicalEventKind.None,
                PresetName = "Legacy Custom Label"
            };
            Assert.AreEqual("260704", customEntry.DateLabel);

            var presetWithLegacyCustomLabel = new ShadowStudyPresetDefinition
            {
                Id = "user:custom",
                Name = "Custom Only",
                IsBuiltIn = false,
                DateSpecs = new[]
                {
                    new ShadowStudyPresetDateSpec
                    {
                        AstronomicalEvent = AstronomicalEventKind.None,
                        CustomDate = new DateTime(2026, 7, 4),
                        CustomLabel = "Site Open"
                    }
                }
            };

            ShadowStudyDateEntry expandedCustom = BuiltInShadowStudyPresets.ExpandPreset(
                presetWithLegacyCustomLabel,
                2026,
                isSouthernHemisphere: false).Single();

            Assert.AreEqual(string.Empty, expandedCustom.PresetName);
            Assert.AreEqual("260704", expandedCustom.DateLabel);
        }
    }
}
