using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Synthetic.Modules.ShadowStudy.Models;
using Synthetic.Modules.ShadowStudy.Services;
using Synthetic.Modules.ShadowStudy.ViewModels;
using Synthetic.Shared.UI;

namespace SyntheticTests.Modules.ShadowStudy
{
    [TestFixture]
    public class ShadowStudyViewModelTests
    {
        private bool _previousSuppressUi;
        private string _tempDir = string.Empty;

        [SetUp]
        public void SetUp()
        {
            _previousSuppressUi = ProgressCoordinator.SuppressUI;
            ProgressCoordinator.SuppressUI = true;
            _tempDir = Path.Combine(Path.GetTempPath(), "ShadowVmTests_" + Guid.NewGuid().ToString("N"));
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
        public void Constructor_PopulatesSiteMetadataViewWarningsAndDefaults()
        {
            var adapter = new FakeShadowStudyAdapter
            {
                SiteInfo = new ShadowStudySiteInfo
                {
                    PlaceName = "Sydney, Australia",
                    LatitudeDegrees = -33.8688,
                    LongitudeDegrees = 151.2093,
                    TimeZoneHours = 10.0,
                    UsesDst = true
                },
                ActiveView = new ShadowStudyViewItem
                {
                    Id = "v1",
                    Name = "Site/Plan:01",
                    ViewType = "FloorPlan",
                    ViewLabel = "Site_Plan_01",
                    IsCropped = false,
                    DisplayStyleName = "Wireframe",
                    Warnings =
                    {
                        new ShadowStudyViewWarningBadge { Code = "Uncropped", Label = "Uncropped" },
                        new ShadowStudyViewWarningBadge { Code = "VisualStyle", Label = "Hidden Line Override" }
                    }
                }
            };

            var folderDialog = new FakeFolderDialogService();
            var vm = new ShadowStudyViewModel(adapter, folderDialog, new DateTime(2026, 6, 21));

            Assert.AreEqual("Sydney, Australia", vm.SitePlaceName);
            StringAssert.Contains("33.8688", vm.LatitudeDisplay);
            StringAssert.Contains("S", vm.LatitudeDisplay);
            StringAssert.Contains("151.2093", vm.LongitudeDisplay);
            StringAssert.Contains("E", vm.LongitudeDisplay);
            Assert.AreEqual("Southern Hemisphere", vm.HemisphereDisplay);
            Assert.AreEqual("UTC+10:00", vm.TimeZoneDisplay);
            StringAssert.Contains("Enabled", vm.DstStatusDisplay);

            Assert.IsTrue(vm.HasViewWarnings);
            Assert.AreEqual(2, vm.ViewWarnings.Count);
            Assert.AreEqual("Site_Plan_01 - 260621 - Composite.png", vm.CompositeFileNamePreview);
            Assert.AreEqual(60, vm.TimeStepMinutes);
            Assert.AreEqual(5.0, vm.MinSunAltitudeDegrees);
            Assert.AreEqual(80, vm.MaxCompositeDarknessPercent);
            Assert.AreEqual(3000, vm.PixelWidth);
            Assert.IsTrue(vm.ForceHiddenLine);
            Assert.IsTrue(vm.ForceWhiteBackground);
            Assert.IsTrue(vm.ExportIndividualFrames, "Export individual frame PNGs must default to checked.");
            Assert.AreEqual(4, vm.TimeIncrementOptions.Count);
            CollectionAssert.AreEqual(new[] { 15, 30, 60, 120 }, vm.TimeIncrementOptions.Select(o => o.Minutes).ToArray());
            Assert.IsFalse(vm.CanExport, "Export must be disabled when OutputFolder is blank.");
        }

        [Test]
        public void CanExport_RequiresNonBlankOutputFolderAndPositiveValidFrameCount()
        {
            var adapter = new FakeShadowStudyAdapter
            {
                SiteInfo = new ShadowStudySiteInfo
                {
                    PlaceName = "Tromso",
                    LatitudeDegrees = 69.6492,
                    LongitudeDegrees = 18.9553,
                    TimeZoneHours = 1.0,
                    UsesDst = false
                }
            };

            var folderDialog = new FakeFolderDialogService { NextResult = @"C:\Temp\ShadowStudy" };
            var vm = new ShadowStudyViewModel(adapter, folderDialog, new DateTime(2026, 12, 21));

            // Polar night has 0 valid frames
            Assert.AreEqual(0, vm.ValidFrameCount);
            Assert.IsTrue(vm.HasZeroFrames);

            vm.BrowseOutputFolderCommand.Execute(null);
            Assert.AreEqual(@"C:\Temp\ShadowStudy", vm.OutputFolder);
            Assert.IsFalse(vm.CanExport, "Export must remain disabled when 0 valid frames exist.");

            // Switch to summer date => valid frames exist
            vm.SelectedDate = new DateTime(2026, 6, 21);
            Assert.Greater(vm.ValidFrameCount, 0);
            Assert.IsFalse(vm.HasZeroFrames);
            Assert.IsTrue(vm.CanExport);

            // Clearing folder disables export again
            vm.OutputFolder = "   ";
            Assert.IsFalse(vm.CanExport);
        }

        [Test]
        public void Commands_ExportAndCancel_RaiseExpectedRequestCloseAndBuildRunRequest()
        {
            var adapter = new FakeShadowStudyAdapter();
            var folderDialog = new FakeFolderDialogService { NextResult = @"C:\Exports\Shadows" };
            var vm = new ShadowStudyViewModel(adapter, folderDialog, new DateTime(2026, 6, 21));

            bool? closedWith = null;
            vm.RequestClose += r => closedWith = r;

            Assert.IsFalse(vm.ExportCommand.CanExecute(null));
            vm.BrowseOutputFolderCommand.Execute(null);
            Assert.IsTrue(vm.ExportCommand.CanExecute(null));

            vm.ExportCommand.Execute(null);
            Assert.AreEqual(true, closedWith);

            var request = vm.BuildRunRequest();
            Assert.AreEqual(1, request.Views.Count);
            Assert.AreEqual(1, request.DateSchedules.Count);
            Assert.AreEqual(@"C:\Exports\Shadows", request.OutputFolder);
            Assert.AreEqual(3000, request.PixelWidth);
            Assert.IsTrue(request.KeepIndividualFrames);

            vm.CancelCommand.Execute(null);
            Assert.AreEqual(false, closedWith);
        }

        [Test]
        public void SchedulePreview_UpdatesImmediatelyOnTimeStepAltitudeAndDarknessChanges_AndFlagsZeroFrames()
        {
            var adapter = new FakeShadowStudyAdapter
            {
                SiteInfo = new ShadowStudySiteInfo
                {
                    PlaceName = "Tromso",
                    LatitudeDegrees = 69.6492,
                    LongitudeDegrees = 18.9553,
                    TimeZoneHours = 1.0,
                    UsesDst = false
                }
            };
            var folderDialog = new FakeFolderDialogService();
            var vm = new ShadowStudyViewModel(adapter, folderDialog, new DateTime(2026, 6, 21))
            {
                OutputFolder = @"C:\Exports\Tromso"
            };

            int hourlyCount = vm.ValidFrameCount;
            string hourlyPreview = vm.SchedulePreviewText;

            vm.TimeStepMinutes = 30;
            Assert.Greater(vm.ValidFrameCount, hourlyCount);
            Assert.AreNotEqual(hourlyPreview, vm.SchedulePreviewText);

            string beforeDarkness = vm.SchedulePreviewText;
            vm.MaxCompositeDarknessPercent = 50;
            Assert.AreNotEqual(beforeDarkness, vm.SchedulePreviewText);

            // Raise altitude above peak summer Tromso sun (~43.8°) -> 0 frames
            vm.MinSunAltitudeDegrees = 60.0;
            Assert.AreEqual(0, vm.ValidFrameCount);
            Assert.IsTrue(vm.HasZeroFrames);
            Assert.IsFalse(vm.CanExport);
            StringAssert.Contains(vm.PeakSolarAltitudeDegrees.ToString("F1", System.Globalization.CultureInfo.InvariantCulture), vm.ZeroFramesWarningText);

            // Lower altitude back -> valid frames return
            vm.MinSunAltitudeDegrees = 5.0;
            Assert.Greater(vm.ValidFrameCount, 0);
            Assert.IsFalse(vm.HasZeroFrames);
            Assert.IsTrue(vm.CanExport);
        }

        [Test]
        public void Validation_BlocksOutOfRangeAndMalformedInputsForAltitudeDarknessAndPixelWidth()
        {
            var adapter = new FakeShadowStudyAdapter();
            var folderDialog = new FakeFolderDialogService();
            var vm = new ShadowStudyViewModel(adapter, folderDialog, new DateTime(2026, 6, 21))
            {
                OutputFolder = @"C:\Exports\Validation"
            };

            Assert.IsFalse(vm.HasValidationErrors);
            Assert.IsTrue(vm.CanExport);

            // Altitude out of [0..90]
            vm.MinSunAltitudeText = "-0.5";
            Assert.IsTrue(vm.HasValidationErrors);
            Assert.IsFalse(vm.CanExport);

            vm.MinSunAltitudeText = "90.1";
            Assert.IsTrue(vm.HasValidationErrors);
            Assert.IsFalse(vm.CanExport);

            vm.MinSunAltitudeText = "abc";
            Assert.IsTrue(vm.HasValidationErrors);
            Assert.IsFalse(vm.CanExport);

            vm.MinSunAltitudeText = "0";
            Assert.IsFalse(vm.HasValidationErrors);
            Assert.IsTrue(vm.CanExport);

            // Darkness out of [1..95]
            vm.MaxCompositeDarknessText = "0";
            Assert.IsTrue(vm.HasValidationErrors);
            Assert.IsFalse(vm.CanExport);

            vm.MaxCompositeDarknessText = "96";
            Assert.IsTrue(vm.HasValidationErrors);
            Assert.IsFalse(vm.CanExport);

            vm.MaxCompositeDarknessText = "75";
            Assert.IsFalse(vm.HasValidationErrors);
            Assert.IsTrue(vm.CanExport);

            // PixelWidth out of [64..15000]
            vm.PixelWidthText = "63";
            Assert.IsTrue(vm.HasValidationErrors);
            Assert.IsFalse(vm.CanExport);

            vm.PixelWidthText = "15001";
            Assert.IsTrue(vm.HasValidationErrors);
            Assert.IsFalse(vm.CanExport);

            vm.PixelWidthText = "1920";
            Assert.IsFalse(vm.HasValidationErrors);
            Assert.AreEqual(1920, vm.PixelWidth);
            Assert.IsTrue(vm.CanExport);
        }

        [Test]
        public void LocalPreferences_LoadOnConstructionAndSaveOnlyOnExportConfirmation()
        {
            string prefsPath = Path.Combine(_tempDir, "ShadowStudyUserPrefs.json");
            var store = new ShadowStudyUserPrefsStore(prefsPath);
            store.Save(new ShadowStudyUserPrefs
            {
                LastOutputFolder = @"D:\Studio\Shadows",
                PixelWidth = 2400,
                ExportIndividualFrames = false
            });

            var adapter = new FakeShadowStudyAdapter();
            var folderDialog = new FakeFolderDialogService();
            var vm = new ShadowStudyViewModel(adapter, folderDialog, new DateTime(2026, 6, 21), store);

            Assert.AreEqual(@"D:\Studio\Shadows", vm.OutputFolder);
            Assert.AreEqual(2400, vm.PixelWidth);
            Assert.AreEqual("2400", vm.PixelWidthText);
            Assert.IsFalse(vm.ExportIndividualFrames);

            // Cancelling must not overwrite stored prefs
            vm.OutputFolder = @"D:\Unsaved\Cancelled";
            vm.PixelWidthText = "1280";
            vm.ExportIndividualFrames = true;
            vm.CancelCommand.Execute(null);

            ShadowStudyUserPrefs afterCancel = store.Load();
            Assert.AreEqual(@"D:\Studio\Shadows", afterCancel.LastOutputFolder);
            Assert.AreEqual(2400, afterCancel.PixelWidth);
            Assert.IsFalse(afterCancel.ExportIndividualFrames);

            // Confirming Export saves updated prefs
            vm.OutputFolder = @"D:\Studio\Updated";
            vm.PixelWidthText = "4096";
            vm.ExportIndividualFrames = true;
            vm.ExportCommand.Execute(null);

            ShadowStudyUserPrefs afterExport = store.Load();
            Assert.AreEqual(@"D:\Studio\Updated", afterExport.LastOutputFolder);
            Assert.AreEqual(4096, afterExport.PixelWidth);
            Assert.IsTrue(afterExport.ExportIndividualFrames);
        }

        [Test]
        public void DefaultConstructor_SelectsSeasonalStudyForCurrentYearWithIndependentPerDateShadowIntensity()
        {
            var adapter = new FakeShadowStudyAdapter
            {
                SiteInfo = new ShadowStudySiteInfo
                {
                    PlaceName = "Boston, MA",
                    LatitudeDegrees = 42.3601,
                    LongitudeDegrees = -71.0589,
                    TimeZoneHours = -5.0,
                    UsesDst = false
                }
            };
            var folderDialog = new FakeFolderDialogService();

            var vm = new ShadowStudyViewModel(adapter, folderDialog);

            Assert.IsNotNull(vm.SelectedPreset);
            Assert.AreEqual("Seasonal Study", vm.SelectedPreset!.Name);
            Assert.AreEqual(DateTime.Today.Year, vm.TargetYear);
            Assert.AreEqual(3, vm.StudyDates.Count);
            Assert.AreEqual(AstronomicalEventKind.SummerSolstice, vm.StudyDates[0].AstronomicalEvent);
            Assert.AreEqual(AstronomicalEventKind.SpringEquinox, vm.StudyDates[1].AstronomicalEvent);
            Assert.AreEqual(AstronomicalEventKind.WinterSolstice, vm.StudyDates[2].AstronomicalEvent);

            // Summer solstice has more valid daylight frames than winter solstice,
            // and calibrates a lower per-frame ShadowIntensity independently.
            Assert.Greater(vm.StudyDates[0].ValidFrameCount, vm.StudyDates[2].ValidFrameCount);
            Assert.Less(vm.StudyDates[0].ShadowIntensity, vm.StudyDates[2].ShadowIntensity);
            Assert.AreEqual(vm.StudyDates.Sum(d => d.ValidFrameCount), vm.TotalValidFrameCount);

            var request = vm.BuildRunRequest();
            Assert.AreEqual(3, request.DateSchedules.Count);
        }

        [Test]
        public void TargetYearAndCustomDates_UpdateAstronomicalDatesOnlyAndPreventDuplicates()
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
            var folderDialog = new FakeFolderDialogService();
            var vm = new ShadowStudyViewModel(adapter, folderDialog);

            // Southern Hemisphere Seasonal Study starts with Dec 21, Sep 22, Jun 21
            int initialYear = vm.TargetYear;
            Assert.AreEqual(new DateTime(initialYear, 12, 21), vm.StudyDates[0].Date);
            Assert.AreEqual($"{initialYear % 100:D2}1221 - Summer Solstice", vm.StudyDates[0].DateLabel);

            // Add a custom calendar date
            var customDate = new DateTime(2026, 7, 4);
            vm.CustomDateToAdd = customDate;
            Assert.IsTrue(vm.AddCustomDateCommand.CanExecute(null));
            vm.AddCustomDateCommand.Execute(null);
            Assert.AreEqual(4, vm.StudyDates.Count);
            Assert.AreEqual("260704", vm.StudyDates[3].DateLabel);

            // Duplicate date cannot be added again
            vm.CustomDateToAdd = customDate;
            Assert.IsFalse(vm.AddCustomDateCommand.CanExecute(null));
            vm.AddCustomDateCommand.Execute(null);
            Assert.AreEqual(4, vm.StudyDates.Count);

            // Changing TargetYear updates astronomical dates while preserving the custom date
            vm.TargetYearText = "2029";
            Assert.AreEqual(2029, vm.TargetYear);
            Assert.AreEqual(new DateTime(2029, 12, 21), vm.StudyDates[0].Date);
            Assert.AreEqual("291221 - Summer Solstice", vm.StudyDates[0].DateLabel);
            Assert.AreEqual(new DateTime(2029, 9, 22), vm.StudyDates[1].Date);
            Assert.AreEqual(new DateTime(2029, 6, 21), vm.StudyDates[2].Date);
            Assert.AreEqual(customDate, vm.StudyDates[3].Date);

            // Remove Spring Equinox date
            vm.RemoveDateCommand.Execute(vm.StudyDates[1]);
            Assert.AreEqual(3, vm.StudyDates.Count);
        }

        [Test]
        public void MultiDateZeroFrameHandling_KeepsExportEnabledWhenAnyDateHasFramesAndDisablesWhenAllAreZero()
        {
            var adapter = new FakeShadowStudyAdapter
            {
                SiteInfo = new ShadowStudySiteInfo
                {
                    PlaceName = "Tromso",
                    LatitudeDegrees = 69.6492,
                    LongitudeDegrees = 18.9553,
                    TimeZoneHours = 1.0,
                    UsesDst = false
                }
            };
            var folderDialog = new FakeFolderDialogService();
            var vm = new ShadowStudyViewModel(adapter, folderDialog)
            {
                OutputFolder = @"C:\Exports\Tromso",
                TargetYear = 2026
            };

            // Seasonal Study at Tromso (69.6° N): Summer + Spring have frames, Winter (Dec 21) has 0 frames
            Assert.AreEqual(3, vm.StudyDates.Count);
            Assert.Greater(vm.StudyDates[0].ValidFrameCount, 0);
            Assert.Greater(vm.StudyDates[1].ValidFrameCount, 0);
            Assert.AreEqual(0, vm.StudyDates[2].ValidFrameCount);
            Assert.IsTrue(vm.StudyDates[2].HasZeroFrames);
            StringAssert.Contains(
                vm.StudyDates[2].PeakSolarAltitudeDegrees.ToString("F1", System.Globalization.CultureInfo.InvariantCulture),
                vm.StudyDates[2].ZeroFrameWarningText);

            Assert.IsTrue(vm.HasSkippedZeroFrameDates);
            Assert.IsFalse(vm.HasZeroFrames, "Total valid frames across all dates is positive.");
            Assert.IsTrue(vm.CanExport, "Export must remain enabled when at least one selected date has valid frames.");

            // Remove the 2 valid dates, leaving only 0-frame Winter Solstice -> Export disables
            vm.RemoveDateCommand.Execute(vm.StudyDates[0]);
            vm.RemoveDateCommand.Execute(vm.StudyDates[0]);
            Assert.AreEqual(1, vm.StudyDates.Count);
            Assert.AreEqual(0, vm.TotalValidFrameCount);
            Assert.IsTrue(vm.HasZeroFrames);
            Assert.IsFalse(vm.CanExport);

            // Removing final date also leaves CanExport disabled
            vm.RemoveDateCommand.Execute(vm.StudyDates[0]);
            Assert.AreEqual(0, vm.StudyDates.Count);
            Assert.IsFalse(vm.CanExport);
        }
    }
}
