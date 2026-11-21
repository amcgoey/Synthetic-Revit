using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using NUnit.Framework;
using Synthetic.Modules.ShadowStudy.Models;
using Synthetic.Modules.ShadowStudy.Settings;
using Synthetic.Shared.UI;

namespace SyntheticTests.Modules.ShadowStudy
{
    [TestFixture]
    public class ShadowStudySettingsTests
    {
        private bool _previousSuppressUi;

        [SetUp]
        public void SetUp()
        {
            _previousSuppressUi = ProgressCoordinator.SuppressUI;
        }

        [TearDown]
        public void TearDown()
        {
            ProgressCoordinator.SuppressUI = _previousSuppressUi;
        }

        [Test]
        public void ParameterlessConstructor_WhenSuppressUiIsTrue_InitializesBaselineDefaults()
        {
            ProgressCoordinator.SuppressUI = true;

            var settings = new ShadowStudySettings();

            Assert.AreEqual("ShadowStudy", ShadowStudySettings.Name);
            Assert.AreEqual("ShadowStudy", settings.ModuleKey);
            Assert.AreEqual(1, settings.SchemaVersion);
            Assert.AreEqual(60, settings.DefaultTimeIncrementMinutes);
            Assert.AreEqual(5.0, settings.DefaultMinSunAltitudeDegrees);
            Assert.AreEqual(80, settings.DefaultMaxCompositeDarknessPercent);
            Assert.IsFalse(settings.DefaultPreserveVisualStyle);
            Assert.IsNotNull(settings.UserPresets);
            Assert.AreEqual(0, settings.UserPresets.Count);
            Assert.IsTrue(settings.IsValid(null!));
        }

        [Test]
        public void Defaults_ResetsModifiedFieldsToBaselineDefaults()
        {
            var settings = new ShadowStudySettings
            {
                SchemaVersion = 3,
                DefaultTimeIncrementMinutes = 15,
                DefaultMinSunAltitudeDegrees = 12.5,
                DefaultMaxCompositeDarknessPercent = 65,
                DefaultPreserveVisualStyle = true,
                UserPresets = new List<ShadowStudyPresetModel>
                {
                    new ShadowStudyPresetModel { Name = "Custom" }
                }
            };

            ShadowStudySettings returned = settings.Defaults();

            Assert.AreSame(settings, returned);
            Assert.AreEqual(1, settings.SchemaVersion);
            Assert.AreEqual(60, settings.DefaultTimeIncrementMinutes);
            Assert.AreEqual(5.0, settings.DefaultMinSunAltitudeDegrees);
            Assert.AreEqual(80, settings.DefaultMaxCompositeDarknessPercent);
            Assert.IsFalse(settings.DefaultPreserveVisualStyle);
            Assert.IsNotNull(settings.UserPresets);
            Assert.AreEqual(0, settings.UserPresets.Count);
        }

        [Test]
        public void JsonRoundTrip_PreservesStudioDefaultsAndUserPresetsWithAstronomicalAndCustomDates()
        {
            var original = new ShadowStudySettings
            {
                SchemaVersion = 1,
                DefaultTimeIncrementMinutes = 30,
                DefaultMinSunAltitudeDegrees = 10.5,
                DefaultMaxCompositeDarknessPercent = 70,
                DefaultPreserveVisualStyle = true,
                UserPresets = new List<ShadowStudyPresetModel>
                {
                    new ShadowStudyPresetModel
                    {
                        Name = "Studio Equinox + Site Open",
                        TimeIncrementMinutes = 15,
                        MinSunAltitudeDegrees = 12.0,
                        MaxCompositeDarknessPercent = 65,
                        PreserveVisualStyle = true,
                        Dates = new List<ShadowStudyPresetDateSpec>
                        {
                            new ShadowStudyPresetDateSpec
                            {
                                AstronomicalEvent = AstronomicalEventKind.SummerSolstice
                            },
                            new ShadowStudyPresetDateSpec
                            {
                                AstronomicalEvent = AstronomicalEventKind.None,
                                CustomDate = new DateTime(2026, 7, 4),
                                CustomLabel = "Independence Day"
                            }
                        }
                    }
                }
            };

            string json = JsonConvert.SerializeObject(original, Formatting.Indented);
            ShadowStudySettings? roundTripped = JsonConvert.DeserializeObject<ShadowStudySettings>(json);

            Assert.IsNotNull(roundTripped);
            Assert.AreEqual(1, roundTripped!.SchemaVersion);
            Assert.AreEqual(30, roundTripped.DefaultTimeIncrementMinutes);
            Assert.AreEqual(10.5, roundTripped.DefaultMinSunAltitudeDegrees);
            Assert.AreEqual(70, roundTripped.DefaultMaxCompositeDarknessPercent);
            Assert.IsTrue(roundTripped.DefaultPreserveVisualStyle);
            Assert.AreEqual(1, roundTripped.UserPresets.Count);

            ShadowStudyPresetModel preset = roundTripped.UserPresets[0];
            Assert.AreEqual("Studio Equinox + Site Open", preset.Name);
            Assert.AreEqual(15, preset.TimeIncrementMinutes);
            Assert.AreEqual(12.0, preset.MinSunAltitudeDegrees);
            Assert.AreEqual(65, preset.MaxCompositeDarknessPercent);
            Assert.IsTrue(preset.PreserveVisualStyle);
            Assert.AreEqual(2, preset.Dates.Count);
            Assert.AreEqual(AstronomicalEventKind.SummerSolstice, preset.Dates[0].AstronomicalEvent);
            Assert.IsNull(preset.Dates[0].CustomDate);
            Assert.AreEqual(AstronomicalEventKind.None, preset.Dates[1].AstronomicalEvent);
            Assert.AreEqual(new DateTime(2026, 7, 4), preset.Dates[1].CustomDate);
            Assert.AreEqual("Independence Day", preset.Dates[1].CustomLabel);
        }

        [Test]
        public void DeepClone_IsolatesNestedPresetsAndDatesFromSource()
        {
            var original = new ShadowStudySettings
            {
                DefaultTimeIncrementMinutes = 60,
                UserPresets = new List<ShadowStudyPresetModel>
                {
                    new ShadowStudyPresetModel
                    {
                        Name = "Original Preset",
                        TimeIncrementMinutes = 60,
                        MinSunAltitudeDegrees = 5.0,
                        MaxCompositeDarknessPercent = 80,
                        PreserveVisualStyle = false,
                        Dates = new List<ShadowStudyPresetDateSpec>
                        {
                            new ShadowStudyPresetDateSpec { AstronomicalEvent = AstronomicalEventKind.SpringEquinox }
                        }
                    }
                }
            };

            ShadowStudySettings clone = original.DeepClone();
            clone.DefaultTimeIncrementMinutes = 15;
            clone.UserPresets[0].Name = "Mutated";
            clone.UserPresets[0].Dates[0].AstronomicalEvent = AstronomicalEventKind.WinterSolstice;
            clone.UserPresets[0].Dates.Add(new ShadowStudyPresetDateSpec
            {
                AstronomicalEvent = AstronomicalEventKind.None,
                CustomDate = new DateTime(2026, 8, 1)
            });

            Assert.AreEqual(60, original.DefaultTimeIncrementMinutes);
            Assert.AreEqual("Original Preset", original.UserPresets[0].Name);
            Assert.AreEqual(1, original.UserPresets[0].Dates.Count);
            Assert.AreEqual(AstronomicalEventKind.SpringEquinox, original.UserPresets[0].Dates[0].AstronomicalEvent);
        }

        [Test]
        public void IsValid_RejectsOutOfRangeValuesAndInvalidPresets()
        {
            var valid = new ShadowStudySettings();
            Assert.IsTrue(valid.IsValid(null!));

            var invalidStep = new ShadowStudySettings { DefaultTimeIncrementMinutes = 45 };
            Assert.IsFalse(invalidStep.IsValid(null!));

            var invalidAlt = new ShadowStudySettings { DefaultMinSunAltitudeDegrees = 95.0 };
            Assert.IsFalse(invalidAlt.IsValid(null!));

            var invalidDarkness = new ShadowStudySettings { DefaultMaxCompositeDarknessPercent = 99 };
            Assert.IsFalse(invalidDarkness.IsValid(null!));

            var blankPresetName = new ShadowStudySettings
            {
                UserPresets = new List<ShadowStudyPresetModel>
                {
                    new ShadowStudyPresetModel { Name = "   " }
                }
            };
            Assert.IsFalse(blankPresetName.IsValid(null!));
        }
    }
}
