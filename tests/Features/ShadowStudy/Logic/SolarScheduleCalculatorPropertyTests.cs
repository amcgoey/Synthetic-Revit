using System;
using System.Collections.Generic;
using System.Linq;
using CsCheck;
using NUnit.Framework;
using Synthetic.Features.ShadowStudy.Models;
using Synthetic.Features.ShadowStudy.Services;

namespace SyntheticTests.Features.ShadowStudy
{
    [TestFixture]
    [UnitTest]
    public class SolarScheduleCalculatorPropertyTests
    {
        private static readonly DateTime MinStudyDate = new DateTime(1900, 1, 1);
        private static readonly DateTime MaxStudyDate = new DateTime(2100, 12, 31);

        private static readonly Gen<DateTime> GenStudyDate =
            Gen.Date[MinStudyDate, MaxStudyDate];

        private static readonly Gen<double> GenLatitude = Gen.OneOf(
            Gen.Double[-90.0, 90.0],
            Gen.OneOfConst(-90.0, -66.5, -33.8688, 0.0, 42.3601, 66.5, 90.0));

        private static readonly Gen<double> GenLongitude = Gen.OneOf(
            Gen.Double[-180.0, 180.0],
            Gen.OneOfConst(-180.0, -71.0589, 0.0, 72.8777, 151.2093, 180.0));

        private static readonly Gen<double> GenTimeZoneHours = Gen.OneOf(
            Gen.Double[-12.0, 14.0],
            Gen.OneOfConst(-12.0, -8.0, -5.0, 0.0, 1.0, 5.5, 10.0, 14.0));

        private static readonly Gen<ShadowStudySiteInfo> GenSiteInfo = Gen.Select(
            GenLatitude,
            GenLongitude,
            GenTimeZoneHours,
            Gen.Bool,
            (lat, lon, tz, dst) => new ShadowStudySiteInfo
            {
                PlaceName = "PropertyTestSite",
                LatitudeDegrees = lat,
                LongitudeDegrees = lon,
                TimeZoneHours = tz,
                UsesDst = dst
            });

        private static readonly Gen<int> GenStepMinutes = Gen.OneOf(
            Gen.OneOfConst(1, 5, 15, 30, 60, 120, 240, 1440),
            Gen.Int[1, 1440]);

        private static readonly Gen<double> GenMinAltitudeDegrees = Gen.OneOf(
            Gen.Double[-30.0, 0.0],
            Gen.Double[0.0, 89.0],
            Gen.OneOfConst(-10.0, 0.0, 5.0, 15.0, 45.0, 89.0));

        [Test]
        public void CalculateSolarPosition_ArbitraryDatesAndCoordinates_ReturnsFiniteBoundedAngles()
        {
            Gen.Select(
                    Gen.DateTime[new DateTime(1900, 1, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2100, 12, 31, 23, 59, 59, DateTimeKind.Utc)],
                    GenLatitude,
                    GenLongitude)
                .Sample((utcTime, latitude, longitude) =>
                {
                    DateTime utc = DateTime.SpecifyKind(utcTime, DateTimeKind.Utc);
                    SolarScheduleCalculator.SolarPosition pos =
                        SolarScheduleCalculator.CalculateSolarPosition(utc, latitude, longitude);

                    Assert.IsFalse(double.IsNaN(pos.AltitudeDegrees), "Solar altitude must not be NaN.");
                    Assert.IsFalse(double.IsInfinity(pos.AltitudeDegrees), "Solar altitude must be finite.");
                    Assert.That(pos.AltitudeDegrees, Is.InRange(-90.0 - 1e-9, 90.0 + 1e-9));

                    Assert.IsFalse(double.IsNaN(pos.AzimuthDegrees), "Solar azimuth must not be NaN.");
                    Assert.IsFalse(double.IsInfinity(pos.AzimuthDegrees), "Solar azimuth must be finite.");
                    Assert.GreaterOrEqual(pos.AzimuthDegrees, 0.0);
                    Assert.Less(pos.AzimuthDegrees, 360.0);
                }, iter: 400);
        }

        [Test]
        public void BuildSchedule_ArbitrarySitesDatesAndIntervals_PreservesChronologyThresholdsAndIntensityBounds()
        {
            Gen.Select(
                    GenStudyDate,
                    GenSiteInfo,
                    GenStepMinutes,
                    GenMinAltitudeDegrees,
                    Gen.Int[-50, 150])
                .Sample((date, site, stepMinutes, minAltitude, maxDarkness) =>
                {
                    var entry = new ShadowStudyDateEntry { Date = date };
                    DateSolarSchedule schedule = SolarScheduleCalculator.BuildSchedule(
                        entry,
                        site,
                        stepMinutes,
                        minAltitude,
                        maxDarkness);

                    int maxPossibleSlots = (1440 + stepMinutes - 1) / stepMinutes;
                    Assert.LessOrEqual(schedule.Frames.Count, maxPossibleSlots);
                    Assert.AreEqual(schedule.Frames.Count > 0, schedule.HasValidFrames);

                    if (schedule.Frames.Count == 0)
                    {
                        Assert.AreEqual(0, schedule.ShadowIntensity);
                    }
                    else
                    {
                        Assert.That(schedule.ShadowIntensity, Is.InRange(1, 100));
                    }

                    Assert.That(schedule.PeakSolarAltitudeDegrees, Is.InRange(-90.0 - 1e-9, 90.0 + 1e-9));

                    double expectedOffsetHours = site.EffectiveUtcOffsetHours;
                    DateTime? prevLocal = null;
                    DateTime? prevUtc = null;

                    foreach (SolarFrameSlot frame in schedule.Frames)
                    {
                        Assert.AreEqual(date.Date, frame.ProjectLocalTime.Date);
                        Assert.AreEqual(DateTimeKind.Unspecified, frame.ProjectLocalTime.Kind);
                        Assert.AreEqual(DateTimeKind.Utc, frame.UtcTime.Kind);

                        DateTime expectedUtc = DateTime.SpecifyKind(
                            frame.ProjectLocalTime.AddHours(-expectedOffsetHours),
                            DateTimeKind.Utc);
                        Assert.AreEqual(expectedUtc, frame.UtcTime);

                        int minuteOfDay = (int)Math.Round((frame.ProjectLocalTime - date.Date).TotalMinutes);
                        Assert.AreEqual(0, minuteOfDay % stepMinutes, "Frame timestamps must align to stepMinutes from 00:00.");

                        if (minAltitude <= 0.0)
                        {
                            Assert.Greater(
                                frame.SolarAltitudeDegrees,
                                0.0,
                                $"minAltitudeDegrees={minAltitude} must clamp to strictly > 0.0 degrees.");
                        }
                        else
                        {
                            Assert.GreaterOrEqual(
                                frame.SolarAltitudeDegrees,
                                minAltitude,
                                $"Frame altitude must meet minAltitudeDegrees={minAltitude}.");
                        }

                        if (prevLocal.HasValue && prevUtc.HasValue)
                        {
                            Assert.Greater(frame.ProjectLocalTime, prevLocal.Value, "ProjectLocalTime must be strictly increasing.");
                            Assert.Greater(frame.UtcTime, prevUtc.Value, "UtcTime must be strictly increasing.");
                        }

                        prevLocal = frame.ProjectLocalTime;
                        prevUtc = frame.UtcTime;
                    }

                    if (schedule.Frames.Count > 0)
                    {
                        double maxFrameAltitude = schedule.Frames.Max(f => f.SolarAltitudeDegrees);
                        Assert.GreaterOrEqual(schedule.PeakSolarAltitudeDegrees + 1e-12, maxFrameAltitude);
                    }
                }, iter: 250);
        }

        [Test]
        public void BuildSchedule_IncreasingMinAltitudeThreshold_MonotonicallyReducesFrameSet()
        {
            Gen.Select(
                    GenStudyDate,
                    GenSiteInfo,
                    Gen.OneOfConst(15, 30, 60, 120),
                    Gen.Double[-10.0, 45.0],
                    Gen.Double[0.0, 45.0])
                .Sample((date, site, stepMinutes, rawLowThreshold, delta) =>
                {
                    double lowThreshold = rawLowThreshold <= 0.0 ? 0.0 : rawLowThreshold;
                    double highThreshold = Math.Min(90.0, lowThreshold + delta);

                    var entry = new ShadowStudyDateEntry { Date = date };
                    DateSolarSchedule lowSchedule = SolarScheduleCalculator.BuildSchedule(entry, site, stepMinutes, lowThreshold, 80);
                    DateSolarSchedule highSchedule = SolarScheduleCalculator.BuildSchedule(entry, site, stepMinutes, highThreshold, 80);

                    Assert.LessOrEqual(highSchedule.Frames.Count, lowSchedule.Frames.Count);

                    var lowTimestamps = new HashSet<DateTime>(lowSchedule.Frames.Select(f => f.ProjectLocalTime));
                    foreach (SolarFrameSlot highFrame in highSchedule.Frames)
                    {
                        Assert.IsTrue(
                            lowTimestamps.Contains(highFrame.ProjectLocalTime),
                            "Higher altitude threshold schedule must be a subset of lower altitude threshold schedule.");
                    }
                }, iter: 250);
        }

        [Test]
        public void CalculateShadowIntensity_ArbitraryCountsAndDarkness_IsBoundedAndMonotonic()
        {
            Gen.Select(
                    Gen.Int[-10, 500],
                    Gen.Int[-50, 150])
                .Sample((frameCount, darkness) =>
                {
                    int intensity = SolarScheduleCalculator.CalculateShadowIntensity(frameCount, darkness);
                    if (frameCount <= 0)
                    {
                        Assert.AreEqual(0, intensity);
                    }
                    else
                    {
                        Assert.That(intensity, Is.InRange(1, 100));
                    }
                }, iter: 400);

            Gen.Select(
                    Gen.Int[1, 500],
                    Gen.Int[1, 500],
                    Gen.Int[-50, 150],
                    Gen.Int[-50, 150])
                .Sample((c1, c2, d1, d2) =>
                {
                    int lowCount = Math.Min(c1, c2);
                    int highCount = Math.Max(c1, c2);
                    int lowDarkness = Math.Min(d1, d2);
                    int highDarkness = Math.Max(d1, d2);

                    // More frames require lower or equal per-frame intensity to reach the same cumulative darkness.
                    Assert.GreaterOrEqual(
                        SolarScheduleCalculator.CalculateShadowIntensity(lowCount, lowDarkness),
                        SolarScheduleCalculator.CalculateShadowIntensity(highCount, lowDarkness));

                    // Higher darkness target requires greater or equal per-frame intensity for the same frame count.
                    Assert.LessOrEqual(
                        SolarScheduleCalculator.CalculateShadowIntensity(lowCount, lowDarkness),
                        SolarScheduleCalculator.CalculateShadowIntensity(lowCount, highDarkness));
                }, iter: 400);
        }
    }
}
