using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Synthetic.Modules.ShadowStudy.Models;
using Synthetic.Modules.ShadowStudy.Settings;
using Synthetic.Modules.ShadowStudy.ViewModels;
using Synthetic.Shared.UI;

namespace SyntheticTests.Modules.ShadowStudy
{
    [TestFixture]
    public class ShadowStudySettingsDashboardTests
    {
        private bool _previousSuppressUi;

        [SetUp]
        public void SetUp()
        {
            _previousSuppressUi = ProgressCoordinator.SuppressUI;
            ProgressCoordinator.SuppressUI = true;
        }

        [TearDown]
        public void TearDown()
        {
            ProgressCoordinator.SuppressUI = _previousSuppressUi;
        }

        [Test]
        public void StudioDefaultInheritance_SavedFromSettingsDashboardVm_GovernsShadowStudyViewModelInitialAndBuiltInDefaults()
        {
            var stored = new ShadowStudySettings();
            bool isOverridden = false;

            var settingsVm = new ShadowStudySettingsViewModel(
                () => stored,
                s =>
                {
                    stored = s.DeepClone();
                    isOverridden = true;
                },
                () =>
                {
                    stored = new ShadowStudySettings();
                    isOverridden = false;
                },
                () => isOverridden,
                new FakeTextInputPromptService());

            settingsVm.IsOverridden = true;
            settingsVm.DefaultTimeIncrementMinutes = 15;
            settingsVm.DefaultMinSunAltitudeDegrees = 12.5;
            settingsVm.DefaultMaxCompositeDarknessPercent = 65;
            settingsVm.DefaultPreserveVisualStyle = true;
            settingsVm.Save();

            var adapter = new FakeShadowStudyAdapter
            {
                Settings = stored.DeepClone()
            };
            var dialogVm = new ShadowStudyViewModel(
                adapter,
                new FakeFolderDialogService(),
                textInputPromptService: new FakeTextInputPromptService());

            // Initial default Seasonal Study (built-in) inherits studio defaults
            Assert.AreEqual(15, dialogVm.TimeStepMinutes);
            Assert.AreEqual(12.5, dialogVm.MinSunAltitudeDegrees);
            Assert.AreEqual(65, dialogVm.MaxCompositeDarknessPercent);
            Assert.IsTrue(dialogVm.PreserveVisualStyle);

            // Switching to another built-in astronomical preset also inherits studio defaults
            dialogVm.SelectedPreset = dialogVm.AvailablePresets.First(p => p.Name == "Summer Solstice");
            Assert.AreEqual(15, dialogVm.TimeStepMinutes);
            Assert.AreEqual(12.5, dialogVm.MinSunAltitudeDegrees);
            Assert.AreEqual(65, dialogVm.MaxCompositeDarknessPercent);
            Assert.IsTrue(dialogVm.PreserveVisualStyle);
        }

        [Test]
        public void StudioDefaults_EditingAndBoundsValidation_TracksIsDirtyAndIsValid()
        {
            var stored = new ShadowStudySettings();
            bool isOverridden = true;

            var vm = new ShadowStudySettingsViewModel(
                () => stored,
                s => stored = s.DeepClone(),
                () => { stored = new ShadowStudySettings(); isOverridden = false; },
                () => isOverridden,
                new FakeTextInputPromptService());

            Assert.AreEqual("Shadow Study", vm.ModuleName);
            Assert.AreEqual(ShadowStudySettings.Name, vm.ModuleKey);
            Assert.IsTrue(vm.IsOverridden);
            Assert.IsFalse(vm.IsDirty);
            Assert.IsTrue(vm.IsValid);

            // Invalid sun altitude > 90
            vm.DefaultMinSunAltitudeText = "91";
            Assert.IsTrue(vm.IsDirty);
            Assert.IsFalse(vm.IsValid);
            Assert.IsTrue(vm.HasValidationErrors);

            // Valid sun altitude boundary 0
            vm.DefaultMinSunAltitudeText = "0";
            Assert.IsTrue(vm.IsValid);
            Assert.AreEqual(0.0, vm.DefaultMinSunAltitudeDegrees);

            // Invalid darkness > 95 or < 1
            vm.DefaultMaxCompositeDarknessText = "96";
            Assert.IsFalse(vm.IsValid);
            vm.DefaultMaxCompositeDarknessText = "0";
            Assert.IsFalse(vm.IsValid);

            // Valid darkness & time increment
            vm.DefaultMaxCompositeDarknessText = "75";
            vm.DefaultTimeIncrementMinutes = 30;
            vm.DefaultPreserveVisualStyle = true;

            Assert.IsTrue(vm.IsValid);
            Assert.IsFalse(vm.HasValidationErrors);

            vm.Save();
            Assert.AreEqual(30, stored.DefaultTimeIncrementMinutes);
            Assert.AreEqual(0.0, stored.DefaultMinSunAltitudeDegrees);
            Assert.AreEqual(75, stored.DefaultMaxCompositeDarknessPercent);
            Assert.IsTrue(stored.DefaultPreserveVisualStyle);
        }

        [Test]
        public void Presets_AddEditDatesAndParameters_RenameAndDelete_WithNameGuards()
        {
            var stored = new ShadowStudySettings
            {
                UserPresets = new List<ShadowStudyPresetModel>
                {
                    new ShadowStudyPresetModel
                    {
                        Name = "Existing Firm Preset",
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
            bool isOverridden = true;
            var promptService = new FakeTextInputPromptService();

            var vm = new ShadowStudySettingsViewModel(
                () => stored,
                s => stored = s.DeepClone(),
                () => { stored = new ShadowStudySettings(); isOverridden = false; },
                () => isOverridden,
                promptService);

            Assert.AreEqual(1, vm.UserPresets.Count);
            Assert.IsNotNull(vm.SelectedPreset);
            Assert.AreEqual("Existing Firm Preset", vm.SelectedPreset!.Name);

            // 1. Add Preset via prompt service
            promptService.NextResult = "Studio Solar Audit";
            vm.AddPresetCommand.Execute(null);

            Assert.AreEqual(2, vm.UserPresets.Count);
            Assert.IsNotNull(vm.SelectedPreset);
            Assert.AreEqual("Studio Solar Audit", vm.SelectedPreset!.Name);
            Assert.IsTrue(vm.IsDirty);

            // Verify name collision validator rejects empty, duplicate, and built-in names
            var addValidator = promptService.LastValidator;
            Assert.IsNotNull(addValidator);
            Assert.IsNotNull(addValidator!(""));
            Assert.IsNotNull(addValidator("   "));
            Assert.IsNotNull(addValidator("existing firm preset"));
            Assert.IsNotNull(addValidator("Seasonal Study"));
            Assert.IsNotNull(addValidator("Winter Solstice"));
            Assert.IsNull(addValidator("Unique Preset"));

            // Defense in depth: rejected if prompt returns invalid name anyway
            promptService.NextResult = "Summer Solstice";
            vm.AddPresetCommand.Execute(null);
            Assert.AreEqual(2, vm.UserPresets.Count);
            Assert.IsTrue(vm.HasPresetError);

            // 2. Edit Selected Preset dates (astronomical + custom) & parameters
            vm.SelectedAstronomicalEventToAdd = AstronomicalEventKind.SummerSolstice;
            vm.AddAstronomicalDateCommand.Execute(null);
            vm.SelectedAstronomicalEventToAdd = AstronomicalEventKind.WinterSolstice;
            vm.AddAstronomicalDateCommand.Execute(null);
            vm.CustomDateToAdd = new DateTime(2026, 7, 4);
            vm.CustomDateLabelToAdd = "Site Open";
            vm.AddCustomDateCommand.Execute(null);

            Assert.AreEqual(3, vm.SelectedPresetDates.Count);
            Assert.AreEqual(AstronomicalEventKind.SummerSolstice, vm.SelectedPresetDates[0].AstronomicalEvent);
            Assert.AreEqual(AstronomicalEventKind.WinterSolstice, vm.SelectedPresetDates[1].AstronomicalEvent);
            Assert.AreEqual(AstronomicalEventKind.None, vm.SelectedPresetDates[2].AstronomicalEvent);
            Assert.AreEqual(new DateTime(2026, 7, 4), vm.SelectedPresetDates[2].CustomDate);
            Assert.AreEqual("Site Open", vm.SelectedPresetDates[2].CustomLabel);

            // Remove one date
            vm.SelectedPresetDates[1].RemoveCommand.Execute(null);
            Assert.AreEqual(2, vm.SelectedPresetDates.Count);

            // Edit preset parameters and validate bounds
            vm.SelectedPresetTimeIncrementMinutes = 15;
            vm.SelectedPresetMinSunAltitudeText = "95";
            Assert.IsFalse(vm.IsValid);
            vm.SelectedPresetMinSunAltitudeText = "12.5";
            vm.SelectedPresetMaxCompositeDarknessText = "60";
            vm.SelectedPresetPreserveVisualStyle = true;
            Assert.IsTrue(vm.IsValid);

            // 3. Rename Preset (guards built-in and other user preset names, allows casing change)
            promptService.NextResult = "Studio Solar Audit V2";
            vm.RenamePresetCommand.Execute(null);
            Assert.AreEqual("Studio Solar Audit V2", vm.SelectedPreset!.Name);

            promptService.NextResult = null;
            vm.RenamePresetCommand.Execute(null);
            var renameValidator = promptService.LastValidator;
            Assert.IsNotNull(renameValidator);
            Assert.IsNull(renameValidator!("studio solar audit v2"));
            Assert.IsNotNull(renameValidator("existing firm preset"));
            Assert.IsNotNull(renameValidator("Fall Equinox"));

            // Save and verify persisted state
            vm.Save();
            ShadowStudyPresetModel saved = stored.UserPresets.Single(p => p.Name == "Studio Solar Audit V2");
            Assert.AreEqual(15, saved.TimeIncrementMinutes);
            Assert.AreEqual(12.5, saved.MinSunAltitudeDegrees);
            Assert.AreEqual(60, saved.MaxCompositeDarknessPercent);
            Assert.IsTrue(saved.PreserveVisualStyle);
            Assert.AreEqual(2, saved.Dates.Count);
            Assert.AreEqual(AstronomicalEventKind.SummerSolstice, saved.Dates[0].AstronomicalEvent);
            Assert.AreEqual(new DateTime(2026, 7, 4), saved.Dates[1].CustomDate);

            // 4. Delete Preset
            vm.SelectedPreset = vm.UserPresets.First(p => p.Name == "Existing Firm Preset");
            vm.DeletePresetCommand.Execute(null);
            Assert.AreEqual(1, vm.UserPresets.Count);
            vm.Save();
            Assert.AreEqual(1, stored.UserPresets.Count);
            Assert.AreEqual("Studio Solar Audit V2", stored.UserPresets[0].Name);
        }

        [Test]
        public void CancelReload_DeepCloneIsolation_NeverMutatesSourceSettings_AndSaveWhenNotOverriddenDeletes()
        {
            var source = new ShadowStudySettings
            {
                DefaultTimeIncrementMinutes = 60,
                DefaultMinSunAltitudeDegrees = 5.0,
                DefaultMaxCompositeDarknessPercent = 80,
                DefaultPreserveVisualStyle = false,
                UserPresets = new List<ShadowStudyPresetModel>
                {
                    new ShadowStudyPresetModel
                    {
                        Name = "Untouched Preset",
                        TimeIncrementMinutes = 30,
                        MinSunAltitudeDegrees = 8.0,
                        MaxCompositeDarknessPercent = 70,
                        PreserveVisualStyle = false,
                        Dates = new List<ShadowStudyPresetDateSpec>
                        {
                            new ShadowStudyPresetDateSpec { AstronomicalEvent = AstronomicalEventKind.SummerSolstice }
                        }
                    }
                }
            };
            bool isOverridden = true;
            int deleteCalls = 0;

            var vm = new ShadowStudySettingsViewModel(
                () => source,
                s => source = s.DeepClone(),
                () => { deleteCalls++; isOverridden = false; },
                () => isOverridden,
                new FakeTextInputPromptService());

            // Mutate studio defaults, edit preset dates and parameters, rename preset without calling Save()
            vm.DefaultTimeIncrementMinutes = 15;
            vm.DefaultPreserveVisualStyle = true;
            vm.SelectedPresetTimeIncrementMinutes = 120;
            vm.CustomDateToAdd = new DateTime(2026, 10, 1);
            vm.AddCustomDateCommand.Execute(null);

            // Verify source instance was NEVER mutated
            Assert.AreEqual(60, source.DefaultTimeIncrementMinutes);
            Assert.IsFalse(source.DefaultPreserveVisualStyle);
            Assert.AreEqual(30, source.UserPresets[0].TimeIncrementMinutes);
            Assert.AreEqual(1, source.UserPresets[0].Dates.Count);

            // Reload discards all working changes
            vm.Reload();
            Assert.IsFalse(vm.IsDirty);
            Assert.AreEqual(60, vm.DefaultTimeIncrementMinutes);
            Assert.IsFalse(vm.DefaultPreserveVisualStyle);
            Assert.AreEqual(30, vm.SelectedPresetTimeIncrementMinutes);
            Assert.AreEqual(1, vm.SelectedPresetDates.Count);

            // ResetToDefaults resets studio defaults and clears user presets, marks dirty
            vm.ResetToDefaults();
            Assert.IsTrue(vm.IsDirty);
            Assert.AreEqual(60, vm.DefaultTimeIncrementMinutes);
            Assert.AreEqual(0, vm.UserPresets.Count);

            // Turning off IsOverridden and calling Save() invokes delete
            vm.IsOverridden = false;
            Assert.IsTrue(vm.IsValid);
            vm.Save();
            Assert.AreEqual(1, deleteCalls);
        }

        private sealed class FakeTextInputPromptService : ITextInputPromptService
        {
            public Func<string, string?>? LastValidator { get; private set; }
            public string? NextResult { get; set; }

            public string? PromptText(
                string title,
                string promptMessage,
                string? initialValue = null,
                Func<string, string?>? validator = null)
            {
                LastValidator = validator;
                return NextResult;
            }
        }
    }
}
