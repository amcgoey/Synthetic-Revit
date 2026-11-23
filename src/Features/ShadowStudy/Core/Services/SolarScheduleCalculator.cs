using System;
using System.Collections.Generic;
using Synthetic.Features.ShadowStudy.Models;

namespace Synthetic.Features.ShadowStudy.Services
{
    /// <summary>
    /// Pure NOAA solar position calculator and daylight Shadow Frame schedule builder.
    /// Evaluates clock-aligned project-local timestamps across <c>[00:00..23:59]</c> and converts
    /// explicitly to UTC via <c>TimeZoneHours + (UsesDst ? 1.0 : 0.0)</c> without calling Revit sunrise/sunset APIs.
    /// </summary>
    public static class SolarScheduleCalculator
    {
        private const double DegToRad = Math.PI / 180.0;
        private const double RadToDeg = 180.0 / Math.PI;

        /// <summary>
        /// Represents a calculated solar position in horizontal coordinates.
        /// </summary>
        public readonly struct SolarPosition
        {
            /// <summary>
            /// Initializes a new instance of the <see cref="SolarPosition"/> struct.
            /// </summary>
            public SolarPosition(double altitudeDegrees, double azimuthDegrees)
            {
                AltitudeDegrees = altitudeDegrees;
                AzimuthDegrees = azimuthDegrees;
            }

            /// <summary>
            /// Gets the solar altitude angle above the horizon in decimal degrees.
            /// </summary>
            public double AltitudeDegrees { get; }

            /// <summary>
            /// Gets the solar azimuth clockwise from true North in decimal degrees (<c>0..360</c>).
            /// </summary>
            public double AzimuthDegrees { get; }
        }

        /// <summary>
        /// Evaluates solar altitude and azimuth using the NOAA solar position equations.
        /// </summary>
        /// <param name="utcTime">Timestamp in Coordinated Universal Time.</param>
        /// <param name="latitudeDegrees">Site latitude in decimal degrees (positive North, negative South).</param>
        /// <param name="longitudeDegrees">Site longitude in decimal degrees (positive East, negative West).</param>
        public static SolarPosition CalculateSolarPosition(
            DateTime utcTime,
            double latitudeDegrees,
            double longitudeDegrees)
        {
            DateTime utc = utcTime.Kind == DateTimeKind.Local
                ? utcTime.ToUniversalTime()
                : DateTime.SpecifyKind(utcTime, DateTimeKind.Utc);

            double utHours = utc.Hour + (utc.Minute / 60.0) + (utc.Second / 3600.0) + (utc.Millisecond / 3600000.0);
            double jd = ComputeJulianDay(utc.Year, utc.Month, utc.Day, utHours);
            double t = (jd - 2451545.0) / 36525.0;

            double geomMeanLongSun = NormalizeDegrees(280.46646 + t * (36000.76983 + t * 0.0003032));
            double geomMeanAnomSun = 357.52911 + t * (35999.05029 - 0.0001537 * t);
            double eccentEarthOrbit = 0.016708634 - t * (0.000042037 + 0.0000001267 * t);

            double anomRad = geomMeanAnomSun * DegToRad;
            double sunEqOfCenter =
                Math.Sin(anomRad) * (1.914602 - t * (0.004817 + 0.000014 * t)) +
                Math.Sin(2.0 * anomRad) * (0.019993 - 0.000101 * t) +
                Math.Sin(3.0 * anomRad) * 0.000289;

            double sunTrueLong = geomMeanLongSun + sunEqOfCenter;
            double omega = 125.04 - 1934.136 * t;
            double sunAppLong = sunTrueLong - 0.00569 - 0.00478 * Math.Sin(omega * DegToRad);

            double meanObliqSeconds = 21.448 - t * (46.8150 + t * (0.00059 - t * 0.001813));
            double meanObliqDeg = 23.0 + (26.0 + (meanObliqSeconds / 60.0)) / 60.0;
            double obliqCorrDeg = meanObliqDeg + 0.00256 * Math.Cos(omega * DegToRad);

            double sinDecl = Math.Sin(obliqCorrDeg * DegToRad) * Math.Sin(sunAppLong * DegToRad);
            double declRad = Math.Asin(ClampUnit(sinDecl));

            double varY = Math.Tan((obliqCorrDeg * 0.5) * DegToRad);
            varY *= varY;

            double l0Rad = geomMeanLongSun * DegToRad;
            double eqOfTimeMinutes = 4.0 * RadToDeg * (
                varY * Math.Sin(2.0 * l0Rad) -
                2.0 * eccentEarthOrbit * Math.Sin(anomRad) +
                4.0 * eccentEarthOrbit * varY * Math.Sin(anomRad) * Math.Cos(2.0 * l0Rad) -
                0.5 * varY * varY * Math.Sin(4.0 * l0Rad) -
                1.25 * eccentEarthOrbit * eccentEarthOrbit * Math.Sin(2.0 * anomRad));

            double trueSolarTimeMinutes = NormalizeMinutes(utHours * 60.0 + eqOfTimeMinutes + 4.0 * longitudeDegrees);
            double hourAngleDeg = (trueSolarTimeMinutes / 4.0) - 180.0;

            double latRad = latitudeDegrees * DegToRad;
            double haRad = hourAngleDeg * DegToRad;

            double cosZenith = ClampUnit(
                Math.Sin(latRad) * Math.Sin(declRad) +
                Math.Cos(latRad) * Math.Cos(declRad) * Math.Cos(haRad));

            double zenithRad = Math.Acos(cosZenith);
            double altitudeDeg = 90.0 - (zenithRad * RadToDeg);

            double denom = Math.Cos(latRad) * Math.Sin(zenithRad);
            double azimuthDeg;
            if (Math.Abs(denom) < 1e-10)
            {
                azimuthDeg = latitudeDegrees >= 0.0 ? 180.0 : 0.0;
            }
            else
            {
                double cosAz = ClampUnit((Math.Sin(latRad) * Math.Cos(zenithRad) - Math.Sin(declRad)) / denom);
                double azBase = Math.Acos(cosAz) * RadToDeg;
                azimuthDeg = hourAngleDeg > 0.0
                    ? NormalizeDegrees(azBase + 180.0)
                    : NormalizeDegrees(540.0 - azBase);
            }

            return new SolarPosition(altitudeDeg, azimuthDeg);
        }

        /// <summary>
        /// Generates a clock-aligned daylight schedule across <c>[00:00..23:59]</c> project-local time.
        /// </summary>
        public static DateSolarSchedule BuildSchedule(
            ShadowStudyDateEntry dateEntry,
            ShadowStudySiteInfo siteInfo,
            int stepMinutes = 60,
            double minAltitudeDegrees = 5.0,
            int maxCompositeDarknessPercent = 80)
        {
            if (dateEntry == null) throw new ArgumentNullException(nameof(dateEntry));
            if (siteInfo == null) throw new ArgumentNullException(nameof(siteInfo));
            if (stepMinutes <= 0 || stepMinutes > 1440)
            {
                throw new ArgumentOutOfRangeException(nameof(stepMinutes), "Step minutes must be between 1 and 1440.");
            }

            DateTime dateOnly = dateEntry.Date.Date;
            double effectiveOffsetHours = siteInfo.EffectiveUtcOffsetHours;
            var frames = new List<SolarFrameSlot>();
            double peakAltitude = double.NegativeInfinity;

            for (int minuteOfDay = 0; minuteOfDay < 1440; minuteOfDay += stepMinutes)
            {
                DateTime localTime = DateTime.SpecifyKind(dateOnly.AddMinutes(minuteOfDay), DateTimeKind.Unspecified);
                DateTime utcTime = DateTime.SpecifyKind(localTime.AddHours(-effectiveOffsetHours), DateTimeKind.Utc);

                SolarPosition pos = CalculateSolarPosition(utcTime, siteInfo.LatitudeDegrees, siteInfo.LongitudeDegrees);
                if (pos.AltitudeDegrees > peakAltitude)
                {
                    peakAltitude = pos.AltitudeDegrees;
                }

                bool meetsThreshold = minAltitudeDegrees <= 0.0
                    ? pos.AltitudeDegrees > 0.0
                    : pos.AltitudeDegrees >= minAltitudeDegrees;

                if (meetsThreshold)
                {
                    frames.Add(new SolarFrameSlot
                    {
                        ProjectLocalTime = localTime,
                        UtcTime = utcTime,
                        SolarAltitudeDegrees = pos.AltitudeDegrees,
                        SolarAzimuthDegrees = pos.AzimuthDegrees
                    });
                }
            }

            if (double.IsNegativeInfinity(peakAltitude))
            {
                peakAltitude = 0.0;
            }

            return new DateSolarSchedule
            {
                DateEntry = new ShadowStudyDateEntry
                {
                    Date = dateOnly,
                    AstronomicalEvent = dateEntry.AstronomicalEvent,
                    PresetName = dateEntry.PresetName
                },
                Frames = frames.AsReadOnly(),
                PeakSolarAltitudeDegrees = peakAltitude,
                ShadowIntensity = CalculateShadowIntensity(frames.Count, maxCompositeDarknessPercent)
            };
        }

        /// <summary>
        /// Computes calibrated Revit <c>ShadowIntensity</c> (<c>1..100</c>) for <paramref name="frameCount"/> frames
        /// targeting <paramref name="maxCompositeDarknessPercent"/> (<c>1..95</c>).
        /// </summary>
        public static int CalculateShadowIntensity(int frameCount, int maxCompositeDarknessPercent)
        {
            if (frameCount <= 0)
            {
                return 0;
            }

            int clampedDarkness = Math.Max(1, Math.Min(95, maxCompositeDarknessPercent));
            double targetMinBrightness = 1.0 - (clampedDarkness / 100.0);
            double rawIntensity = 100.0 * (1.0 - Math.Pow(targetMinBrightness, 1.0 / frameCount));
            int rounded = (int)Math.Round(rawIntensity);
            return Math.Max(1, Math.Min(100, rounded));
        }

        private static double ComputeJulianDay(int year, int month, int day, double utHours)
        {
            if (month <= 2)
            {
                year -= 1;
                month += 12;
            }

            double a = Math.Floor(year / 100.0);
            double b = 2.0 - a + Math.Floor(a / 4.0);

            return Math.Floor(365.25 * (year + 4716))
                 + Math.Floor(30.6001 * (month + 1))
                 + day
                 + (utHours / 24.0)
                 + b
                 - 1524.5;
        }

        private static double NormalizeDegrees(double angle)
        {
            double result = angle % 360.0;
            return result < 0.0 ? result + 360.0 : result;
        }

        private static double NormalizeMinutes(double minutes)
        {
            double result = minutes % 1440.0;
            return result < 0.0 ? result + 1440.0 : result;
        }

        private static double ClampUnit(double value)
        {
            return Math.Max(-1.0, Math.Min(1.0, value));
        }
    }
}
