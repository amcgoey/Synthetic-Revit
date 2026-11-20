using System;
using System.Linq;
using NUnit.Framework;
using Synthetic.Modules.ShadowStudy.Models;
using Synthetic.Modules.ShadowStudy.Services;

namespace SyntheticTests.Modules.ShadowStudy
{
    [TestFixture]
    public class SolarScheduleCalculatorTests
    {
        [Test]
        public void CalculateSolarPosition_BostonSummerSolsticeNoon_MatchesExpectedAltitudeAndAzimuth()
        {
            // June 21, 2026 at 12:00 EDT (UTC-4 => 16:00 UTC), Boston MA (42.3601° N, -71.0589° W)
            var utc = new DateTime(2026, 6, 21, 16, 45, 0, DateTimeKind.Utc);
            var pos = SolarScheduleCalculator.CalculateSolarPosition(utc, 42.3601, -71.0589);

            // Max solar altitude on June 21 at 42.36° N is ~90 - 42.36 + 23.44 = ~71.08°
            Assert.That(pos.AltitudeDegrees, Is.InRange(69.5, 72.0));
            Assert.That(pos.AzimuthDegrees, Is.InRange(170.0, 190.0));
        }

        [Test]
        public void BuildSchedule_BostonSummerVsWinterSolstice_ProducesMoreFramesInSummer()
        {
            var site = new ShadowStudySiteInfo
            {
                PlaceName = "Boston, MA",
                LatitudeDegrees = 42.3601,
                LongitudeDegrees = -71.0589,
                TimeZoneHours = -5.0,
                UsesDst = false
            };

            var summer = SolarScheduleCalculator.BuildSchedule(
                new ShadowStudyDateEntry { Date = new DateTime(2026, 6, 21) },
                site,
                stepMinutes: 60,
                minAltitudeDegrees: 5.0,
                maxCompositeDarknessPercent: 80);

            var winter = SolarScheduleCalculator.BuildSchedule(
                new ShadowStudyDateEntry { Date = new DateTime(2026, 12, 21) },
                site,
                stepMinutes: 60,
                minAltitudeDegrees: 5.0,
                maxCompositeDarknessPercent: 80);

            Assert.Greater(summer.Frames.Count, winter.Frames.Count);
            Assert.That(summer.Frames.All(f => f.SolarAltitudeDegrees >= 5.0), Is.True);
            Assert.That(winter.Frames.All(f => f.SolarAltitudeDegrees >= 5.0), Is.True);
            Assert.That(summer.Frames.All(f => f.UtcTime.Kind == DateTimeKind.Utc), Is.True);
            Assert.That(summer.Frames.All(f => f.ProjectLocalTime.Minute == 0), Is.True);
            Assert.Greater(summer.PeakSolarAltitudeDegrees, 68.0);
            Assert.That(winter.PeakSolarAltitudeDegrees, Is.InRange(23.0, 26.0));
        }

        [Test]
        public void BuildSchedule_DstAndFractionalTimeZone_ShiftsUtcOffsetAccurately()
        {
            // India Standard Time UTC+5:30, no DST
            var indiaSite = new ShadowStudySiteInfo
            {
                PlaceName = "Mumbai",
                LatitudeDegrees = 19.0760,
                LongitudeDegrees = 72.8777,
                TimeZoneHours = 5.5,
                UsesDst = false
            };

            var scheduleNoDst = SolarScheduleCalculator.BuildSchedule(
                new ShadowStudyDateEntry { Date = new DateTime(2026, 3, 20) },
                indiaSite,
                stepMinutes: 60,
                minAltitudeDegrees: 5.0,
                maxCompositeDarknessPercent: 80);

            Assert.IsTrue(scheduleNoDst.HasValidFrames);
            var noonSlot = scheduleNoDst.Frames.First(f => f.ProjectLocalTime.Hour == 12);
            Assert.AreEqual(new DateTime(2026, 3, 20, 6, 30, 0, DateTimeKind.Utc), noonSlot.UtcTime);
            Assert.Greater(noonSlot.SolarAltitudeDegrees, 65.0);

            // Boston with UsesDst = true => effective offset -4.0
            var bostonDst = new ShadowStudySiteInfo
            {
                PlaceName = "Boston, MA",
                LatitudeDegrees = 42.3601,
                LongitudeDegrees = -71.0589,
                TimeZoneHours = -5.0,
                UsesDst = true
            };

            var bostonSchedule = SolarScheduleCalculator.BuildSchedule(
                new ShadowStudyDateEntry { Date = new DateTime(2026, 6, 21) },
                bostonDst,
                stepMinutes: 60,
                minAltitudeDegrees: 5.0,
                maxCompositeDarknessPercent: 80);

            var bostonNoon = bostonSchedule.Frames.First(f => f.ProjectLocalTime.Hour == 12);
            Assert.AreEqual(new DateTime(2026, 6, 21, 16, 0, 0, DateTimeKind.Utc), bostonNoon.UtcTime);
        }

        [Test]
        public void BuildSchedule_UtcPlusMidnightCrossing_HandlesSydneyMorningWithoutDateWrapBug()
        {
            // Sydney UTC+10: 07:00 local on Dec 21 is 21:00 UTC on Dec 20!
            var sydney = new ShadowStudySiteInfo
            {
                PlaceName = "Sydney, Australia",
                LatitudeDegrees = -33.8688,
                LongitudeDegrees = 151.2093,
                TimeZoneHours = 10.0,
                UsesDst = false
            };

            var decSchedule = SolarScheduleCalculator.BuildSchedule(
                new ShadowStudyDateEntry { Date = new DateTime(2026, 12, 21) },
                sydney,
                stepMinutes: 60,
                minAltitudeDegrees: 5.0,
                maxCompositeDarknessPercent: 80);

            var morningSlot = decSchedule.Frames.First(f => f.ProjectLocalTime.Hour == 7);
            Assert.AreEqual(new DateTime(2026, 12, 20, 21, 0, 0, DateTimeKind.Utc), morningSlot.UtcTime);
            Assert.Greater(morningSlot.SolarAltitudeDegrees, 15.0);
        }

        [Test]
        public void BuildSchedule_ZeroDegreeThreshold_UsesStrictGreaterThanZero()
        {
            var site = new ShadowStudySiteInfo
            {
                LatitudeDegrees = 0.0,
                LongitudeDegrees = 0.0,
                TimeZoneHours = 0.0,
                UsesDst = false
            };

            var schedule = SolarScheduleCalculator.BuildSchedule(
                new ShadowStudyDateEntry { Date = new DateTime(2026, 3, 20) },
                site,
                stepMinutes: 60,
                minAltitudeDegrees: 0.0,
                maxCompositeDarknessPercent: 80);

            Assert.IsTrue(schedule.Frames.All(f => f.SolarAltitudeDegrees > 0.0));
        }

        [Test]
        public void BuildSchedule_PolarNight_ReturnsZeroFramesAndTracksNegativePeakAltitude()
        {
            // Tromso, Norway (69.6492° N) on Dec 21 -> sun stays below horizon all day
            var tromso = new ShadowStudySiteInfo
            {
                PlaceName = "Tromso",
                LatitudeDegrees = 69.6492,
                LongitudeDegrees = 18.9553,
                TimeZoneHours = 1.0,
                UsesDst = false
            };

            var winter = SolarScheduleCalculator.BuildSchedule(
                new ShadowStudyDateEntry { Date = new DateTime(2026, 12, 21) },
                tromso,
                stepMinutes: 60,
                minAltitudeDegrees: 5.0,
                maxCompositeDarknessPercent: 80);

            Assert.IsFalse(winter.HasValidFrames);
            Assert.AreEqual(0, winter.Frames.Count);
            Assert.AreEqual(0, winter.ShadowIntensity);
            Assert.Less(winter.PeakSolarAltitudeDegrees, 0.0);
        }

        [Test]
        public void CalculateShadowIntensity_ClampsAndGuardsZeroFrames()
        {
            Assert.AreEqual(0, SolarScheduleCalculator.CalculateShadowIntensity(0, 80));
            Assert.AreEqual(80, SolarScheduleCalculator.CalculateShadowIntensity(1, 80));
            Assert.AreEqual(15, SolarScheduleCalculator.CalculateShadowIntensity(10, 80));
            Assert.AreEqual(95, SolarScheduleCalculator.CalculateShadowIntensity(1, 120));
            Assert.AreEqual(1, SolarScheduleCalculator.CalculateShadowIntensity(1, -10));
            Assert.AreEqual(1, SolarScheduleCalculator.CalculateShadowIntensity(500, 80));
        }
    }
}
