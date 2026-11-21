using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Synthetic.Modules.ShadowStudy.Models;
using Synthetic.Modules.ShadowStudy.Services;
using Synthetic.Modules.ShadowStudy.Settings;
using Synthetic.Modules.ShadowStudy.ViewModels;
using Synthetic.Shared.UI;

namespace SyntheticTests.Modules.ShadowStudy
{
    [TestFixture]
    public class ShadowStudyPresetsTests
    {
        private bool _previousSuppressUi;
        private string _tempDir = string.Empty;

        [SetUp]
        public void SetUp()
        {
            _previousSuppressUi = ProgressCoordinator.SuppressUI;
            ProgressCoordinator.SuppressUI = true;
            _tempDir = Path.Combine(Path.GetTempPath(), "ShadowPresetTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
        }

        [TearDown]
        public void TearDown()
        {
            ProgressCoordinator.SuppressUI = _previousSuppressUi;
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, true);
            }
        }

        [Test]
        public void BuiltInPresets_InheritStudioDefaultsFromShadowStudySettings_OnOpenAndSelection()
        {
            var adapter = new FakeShadowStudyAdapter
            {
                Settings = new ShadowStudySettings
                {
                    DefaultTimeIncrementMinutes = 30,
                    DefaultMinSunAltitudeDegrees = 10.0,
                    DefaultMaxCompositeDarknessPercent = 65,
                    DefaultPreserveVisualStyle = true,
                    UserPresets = new List<ShadowStudyPresetModel>
                    {
                        new ShadowStudyPresetModel
                        {
                            Name = "Custom Fast",
                            TimeIncrementMinutes = 120,
                            MinSunAltitudeDegrees = 15.0,
                            MaxCompositeDarknessPercent = 50,
                            PreserveVisualStyle = false,
                            Dates = new List<ShadowStudyPresetDateSpec>
                            {
                                new ShadowStudyPresetDateSpec
                                {
                                    AstronomicalEvent = AstronomicalEventKind.None,
                                    CustomDate = new DateTime(2026, 5, 1)
                                }
                            }
                        }
                    }
                }
            };

            var vm = new ShadowStudyViewModel(
                adapter,
                new FakeFolderDialogService(),
                textInputPromptService: new FakeTextInputPromptService());

            // Default built-in Seasonal Study inherits studio defaults
            Assert.IsNotNull(vm.SelectedPreset);
            Assert.IsTrue(vm.SelectedPreset!.IsBuiltIn);
            Assert.AreEqual("Seasonal Study", vm.SelectedPreset.Name);
            Assert.AreEqual(30, vm.TimeStepMinutes);
            Assert.AreEqual(10.0, vm.MinSunAltitudeDegrees);
            Assert.AreEqual(65, vm.MaxCompositeDarknessPercent);
            Assert.IsTrue(vm.PreserveVisualStyle);
            Assert.IsFalse(vm.ForceHiddenLine);

            // Switch to user-defined preset -> applies user preset's own parameters
            vm.SelectedPreset = vm.AvailablePresets.First(p => p.Name == "Custom Fast");
            Assert.IsFalse(vm.SelectedPreset.IsBuiltIn);
            Assert.AreEqual(120, vm.TimeStepMinutes);
            Assert.AreEqual(15.0, vm.MinSunAltitudeDegrees);
            Assert.AreEqual(50, vm.MaxCompositeDarknessPercent);
            Assert.IsFalse(vm.PreserveVisualStyle);

            // Switch back to built-in Winter Solstice -> re-inherits studio defaults
            vm.SelectedPreset = vm.AvailablePresets.First(p => p.Name == "Winter Solstice");
            Assert.IsTrue(vm.SelectedPreset.IsBuiltIn);
            Assert.AreEqual(30, vm.TimeStepMinutes);
            Assert.AreEqual(10.0, vm.MinSunAltitudeDegrees);
            Assert.AreEqual(65, vm.MaxCompositeDarknessPercent);
            Assert.IsTrue(vm.PreserveVisualStyle);
        }

        [Test]
        public void BuiltInPreset_RemainsReadOnlyWhenModified_AndForksViaSaveAs()
        {
            var adapter = new FakeShadowStudyAdapter();
            var promptService = new FakeTextInputPromptService { NextResult = "Studio Forked Preset" };
            var vm = new ShadowStudyViewModel(
                adapter,
                new FakeFolderDialogService(),
                textInputPromptService: promptService);

            Assert.IsTrue(vm.SelectedPreset!.IsBuiltIn);
            Assert.IsFalse(vm.CanRenamePreset);
            Assert.IsFalse(vm.CanDeletePreset);
            Assert.IsFalse(vm.RenamePresetCommand.CanExecute(null));
            Assert.IsFalse(vm.DeletePresetCommand.CanExecute(null));

            // Modify dates and parameters while built-in preset is selected
            vm.CustomDateToAdd = new DateTime(2026, 7, 4);
            vm.AddCustomDateCommand.Execute(null);
            vm.TimeStepMinutes = 30;
            vm.MinSunAltitudeDegrees = 12.0;
            vm.MaxCompositeDarknessPercent = 70;
            vm.PreserveVisualStyle = true;

            // Built-in preset remains non-deletable and non-renamable
            Assert.IsFalse(vm.CanRenamePreset);
            Assert.IsFalse(vm.CanDeletePreset);

            // Clicking Save on a built-in preset forks via Save As prompt
            vm.SavePresetCommand.Execute(null);

            Assert.AreEqual(1, promptService.PromptCalls.Count);
            Assert.AreEqual(1, adapter.SaveSettingsCalls.Count);
            Assert.IsNotNull(vm.SelectedPreset);
            Assert.AreEqual("Studio Forked Preset", vm.SelectedPreset!.Name);
            Assert.IsFalse(vm.SelectedPreset.IsBuiltIn);
            Assert.IsTrue(vm.CanRenamePreset);
            Assert.IsTrue(vm.CanDeletePreset);

            ShadowStudyPresetModel saved = adapter.Settings.UserPresets.Single(p => p.Name == "Studio Forked Preset");
            Assert.AreEqual(30, saved.TimeIncrementMinutes);
            Assert.AreEqual(12.0, saved.MinSunAltitudeDegrees);
            Assert.AreEqual(70, saved.MaxCompositeDarknessPercent);
            Assert.IsTrue(saved.PreserveVisualStyle);
            Assert.AreEqual(4, saved.Dates.Count);
            Assert.AreEqual(AstronomicalEventKind.SummerSolstice, saved.Dates[0].AstronomicalEvent);
            Assert.AreEqual(AstronomicalEventKind.SpringEquinox, saved.Dates[1].AstronomicalEvent);
            Assert.AreEqual(AstronomicalEventKind.WinterSolstice, saved.Dates[2].AstronomicalEvent);
            Assert.AreEqual(AstronomicalEventKind.None, saved.Dates[3].AstronomicalEvent);
            Assert.AreEqual(new DateTime(2026, 7, 4), saved.Dates[3].CustomDate);
        }

        [Test]
        public void UserPreset_SaveRenameDelete_AndPreservesAstronomicalEventTagsOnReload()
        {
            var adapter = new FakeShadowStudyAdapter
            {
                SiteInfo = new ShadowStudySiteInfo
                {
                    PlaceName = "Sydney, Australia",
                    LatitudeDegrees = -33.8688,
                    LongitudeDegrees = 151.2093,
                    TimeZoneHours = 10.0,
                    UsesDst = false
                }
            };
            var promptService = new FakeTextInputPromptService();
            var vm = new ShadowStudyViewModel(
                adapter,
                new FakeFolderDialogService(),
                textInputPromptService: promptService);

            // 1. Save As from Seasonal Study + custom date
            vm.CustomDateToAdd = new DateTime(2026, 7, 4);
            vm.AddCustomDateCommand.Execute(null);
            vm.TimeStepMinutes = 15;
            vm.PreserveVisualStyle = true;

            promptService.NextResult = "Sydney Mixed";
            vm.SavePresetAsCommand.Execute(null);

            Assert.AreEqual("Sydney Mixed", vm.SelectedPreset!.Name);
            Assert.AreEqual(1, adapter.SaveSettingsCalls.Count);

            // 2. Modify user preset and click Save -> updates in place without prompting
            vm.MaxCompositeDarknessPercent = 60;
            vm.SavePresetCommand.Execute(null);

            Assert.AreEqual(1, promptService.PromptCalls.Count, "Save on user-defined preset must not prompt.");
            Assert.AreEqual(2, adapter.SaveSettingsCalls.Count);
            Assert.AreEqual(60, adapter.Settings.UserPresets.Single(p => p.Name == "Sydney Mixed").MaxCompositeDarknessPercent);

            // 3. Reload in fresh VM with TargetYear = 2029 -> astronomical dates resolve dynamically for 2029 Southern Hemisphere!
            var reloadedVm = new ShadowStudyViewModel(
                adapter,
                new FakeFolderDialogService(),
                textInputPromptService: promptService);

            reloadedVm.SelectedPreset = reloadedVm.AvailablePresets.First(p => p.Name == "Sydney Mixed");
            reloadedVm.TargetYear = 2029;

            Assert.AreEqual(4, reloadedVm.StudyDates.Count);
            Assert.AreEqual(AstronomicalEventKind.SummerSolstice, reloadedVm.StudyDates[0].AstronomicalEvent);
            Assert.AreEqual(new DateTime(2029, 12, 21), reloadedVm.StudyDates[0].Date);
            Assert.AreEqual("291221 - Summer Solstice", reloadedVm.StudyDates[0].DateLabel);
            Assert.AreEqual(AstronomicalEventKind.None, reloadedVm.StudyDates[3].AstronomicalEvent);
            Assert.AreEqual(new DateTime(2026, 7, 4), reloadedVm.StudyDates[3].Date);
            Assert.AreEqual("260704", reloadedVm.StudyDates[3].DateLabel);
            Assert.AreEqual(15, reloadedVm.TimeStepMinutes);
            Assert.AreEqual(60, reloadedVm.MaxCompositeDarknessPercent);
            Assert.IsTrue(reloadedVm.PreserveVisualStyle);

            // 4. Rename user preset
            promptService.NextResult = "Sydney Renamed";
            reloadedVm.RenamePresetCommand.Execute(null);

            Assert.AreEqual("Sydney Renamed", reloadedVm.SelectedPreset!.Name);
            Assert.IsFalse(adapter.Settings.UserPresets.Any(p => p.Name == "Sydney Mixed"));
            Assert.IsTrue(adapter.Settings.UserPresets.Any(p => p.Name == "Sydney Renamed"));

            // 5. Delete user preset -> falls back cleanly to built-in Seasonal Study
            reloadedVm.DeletePresetCommand.Execute(null);

            Assert.IsFalse(adapter.Settings.UserPresets.Any(p => p.Name == "Sydney Renamed"));
            Assert.IsNotNull(reloadedVm.SelectedPreset);
            Assert.AreEqual(BuiltInShadowStudyPresets.SeasonalStudyId, reloadedVm.SelectedPreset!.Id);
            Assert.IsTrue(reloadedVm.SelectedPreset.IsBuiltIn);
            Assert.IsFalse(reloadedVm.CanDeletePreset);
            Assert.IsFalse(reloadedVm.CanRenamePreset);
        }

        [Test]
        public void PresetNameGuards_RejectBlankDuplicateAndBuiltInNames_AndCancelIsolatesState()
        {
            var adapter = new FakeShadowStudyAdapter
            {
                Settings = new ShadowStudySettings
                {
                    UserPresets = new List<ShadowStudyPresetModel>
                    {
                        new ShadowStudyPresetModel
                        {
                            Name = "Office Standard",
                            TimeIncrementMinutes = 30,
                            MinSunAltitudeDegrees = 8.0,
                            MaxCompositeDarknessPercent = 75,
                            PreserveVisualStyle = false,
                            Dates = new List<ShadowStudyPresetDateSpec>
                            {
                                new ShadowStudyPresetDateSpec { AstronomicalEvent = AstronomicalEventKind.SummerSolstice }
                            }
                        },
                        new ShadowStudyPresetModel
                        {
                            Name = "Winter Only",
                            TimeIncrementMinutes = 60,
                            MinSunAltitudeDegrees = 5.0,
                            MaxCompositeDarknessPercent = 80,
                            PreserveVisualStyle = false,
                            Dates = new List<ShadowStudyPresetDateSpec>
                            {
                                new ShadowStudyPresetDateSpec { AstronomicalEvent = AstronomicalEventKind.WinterSolstice }
                            }
                        }
                    }
                }
            };

            var promptService = new FakeTextInputPromptService();
            var vm = new ShadowStudyViewModel(
                adapter,
                new FakeFolderDialogService(),
                textInputPromptService: promptService);

            vm.SelectedPreset = vm.AvailablePresets.First(p => p.Name == "Office Standard");

            // Cancelled Save As prompt -> no save calls
            promptService.NextResult = null;
            vm.SavePresetAsCommand.Execute(null);
            Assert.AreEqual(0, adapter.SaveSettingsCalls.Count);

            // Prompt validator rejects blank, built-in names, and duplicate user names
            var validator = promptService.LastValidator;
            Assert.IsNotNull(validator);
            Assert.IsNotNull(validator!(""));
            Assert.IsNotNull(validator("   "));
            Assert.IsNotNull(validator("seasonal study"));
            Assert.IsNotNull(validator("Winter Solstice"));
            Assert.IsNotNull(validator("office standard"));
            Assert.IsNull(validator("Brand New Studio Preset"));

            // Defense in depth if invalid name returned anyway
            promptService.NextResult = "Seasonal Study";
            vm.SavePresetAsCommand.Execute(null);
            Assert.AreEqual(0, adapter.SaveSettingsCalls.Count);
            Assert.IsTrue(vm.HasPresetError);

            // Rename validator allows casing change of own name, rejects other existing preset name
            promptService.NextResult = null;
            vm.RenamePresetCommand.Execute(null);
            var renameValidator = promptService.LastValidator;
            Assert.IsNotNull(renameValidator);
            Assert.IsNull(renameValidator!("office standard"));
            Assert.IsNotNull(renameValidator("winter only"));
            Assert.IsNotNull(renameValidator("Spring Equinox"));

            // Modifying loaded preset and cancelling dialog must NEVER mutate adapter.Settings
            vm.TimeStepMinutes = 120;
            vm.PreserveVisualStyle = true;
            vm.CustomDateToAdd = new DateTime(2026, 8, 15);
            vm.AddCustomDateCommand.Execute(null);
            vm.CancelCommand.Execute(null);

            Assert.AreEqual(0, adapter.SaveSettingsCalls.Count);
            ShadowStudyPresetModel untouched = adapter.Settings.UserPresets.First(p => p.Name == "Office Standard");
            Assert.AreEqual(30, untouched.TimeIncrementMinutes);
            Assert.IsFalse(untouched.PreserveVisualStyle);
            Assert.AreEqual(1, untouched.Dates.Count);
        }

        [Test]
        public void Export_NeverSavesShadowStudySettings_WhileLocalPrefsRemainStrictlyLocal()
        {
            string prefsPath = Path.Combine(_tempDir, "ShadowStudyUserPrefs.json");
            var store = new ShadowStudyUserPrefsStore(prefsPath);
            var adapter = new FakeShadowStudyAdapter
            {
                Settings = new ShadowStudySettings
                {
                    DefaultPreserveVisualStyle = true
                }
            };

            var vm = new ShadowStudyViewModel(
                adapter,
                new FakeFolderDialogService(),
                userPrefsStore: store,
                textInputPromptService: new FakeTextInputPromptService())
            {
                OutputFolder = @"C:\Exports\ShadowStudy",
                PixelWidthText = "2400",
                ExportIndividualFrames = false
            };

            // Studio default PreserveVisualStyle = true came from ShadowStudySettings
            Assert.IsTrue(vm.PreserveVisualStyle);

            // Modify study parameters and export without saving a preset
            vm.TimeStepMinutes = 15;
            vm.PreserveVisualStyle = false;

            bool? closedWith = null;
            vm.RequestClose += r => closedWith = r;
            vm.ExportCommand.Execute(null);

            Assert.AreEqual(true, closedWith);
            Assert.AreEqual(0, adapter.SaveSettingsCalls.Count, "Export must never save ShadowStudySettings to document storage.");

            // Local prefs store only local UI settings, never PreserveVisualStyle
            string json = File.ReadAllText(prefsPath);
            StringAssert.DoesNotContain("PreserveVisualStyle", json);

            ShadowStudyUserPrefs savedPrefs = store.Load();
            Assert.AreEqual(@"C:\Exports\ShadowStudy", savedPrefs.LastOutputFolder);
            Assert.AreEqual(2400, savedPrefs.PixelWidth);
            Assert.IsFalse(savedPrefs.ExportIndividualFrames);

            // Reopening dialog still uses studio default DefaultPreserveVisualStyle = true (local prefs never override it)
            var reopenedVm = new ShadowStudyViewModel(
                adapter,
                new FakeFolderDialogService(),
                userPrefsStore: store,
                textInputPromptService: new FakeTextInputPromptService());
            Assert.IsTrue(reopenedVm.PreserveVisualStyle);
            Assert.AreEqual(2400, reopenedVm.PixelWidth);
            Assert.IsFalse(reopenedVm.ExportIndividualFrames);
        }

        [Test]
        public void UserPresetWithLegacyCustomDateLabel_FormatsCustomDateStrictlyAsYyMmDd()
        {
            var adapter = new FakeShadowStudyAdapter
            {
                Settings = new ShadowStudySettings
                {
                    UserPresets = new List<ShadowStudyPresetModel>
                    {
                        new ShadowStudyPresetModel
                        {
                            Name = "Legacy Custom Date Preset",
                            Dates = new List<ShadowStudyPresetDateSpec>
                            {
                                new ShadowStudyPresetDateSpec
                                {
                                    AstronomicalEvent = AstronomicalEventKind.None,
                                    CustomDate = new DateTime(2026, 7, 4),
                                    CustomLabel = "Legacy Label"
                                }
                            }
                        }
                    }
                }
            };

            var vm = new ShadowStudyViewModel(adapter, new FakeFolderDialogService());
            vm.SelectedPreset = vm.AvailablePresets.First(p => p.Name == "Legacy Custom Date Preset");

            Assert.AreEqual(1, vm.StudyDates.Count);
            Assert.AreEqual("260704", vm.StudyDates[0].DateLabel);
        }

        private sealed class FakeTextInputPromptService : ITextInputPromptService
        {
            public List<(string Title, string PromptMessage, string? InitialValue)> PromptCalls { get; } =
                new List<(string, string, string?)>();

            public Func<string, string?>? LastValidator { get; private set; }

            public string? NextResult { get; set; }

            public string? PromptText(
                string title,
                string promptMessage,
                string? initialValue = null,
                Func<string, string?>? validator = null)
            {
                PromptCalls.Add((title, promptMessage, initialValue));
                LastValidator = validator;
                return NextResult;
            }
        }
    }
}
