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
        public void Constructor_WhenActiveViewIsEligible_SelectsCurrentViewModeAndEnablesCurrentViewOption()
        {
            var adapter = new FakeShadowStudyAdapter();
            adapter.EligibleViews.Add(adapter.ActiveView!);
            adapter.EligibleViews.Add(new ShadowStudyViewItem
            {
                Id = "view-2",
                Name = "Level 2 - Floor Plan",
                ViewType = "FloorPlan",
                IsCropped = false,
                Warnings = { new ShadowStudyViewWarningBadge { Code = "Uncropped", Label = "Uncropped View" } }
            });

            var vm = new ShadowStudyViewModel(adapter, new FakeFolderDialogService(), new DateTime(2026, 6, 21));

            Assert.AreEqual(ShadowStudyExportRangeMode.CurrentView, vm.ExportRangeMode);
            Assert.IsTrue(vm.IsCurrentViewEligible);
            Assert.IsTrue(vm.IsCurrentViewMode);
            Assert.IsFalse(vm.IsSelectedViewsMode);
            Assert.AreEqual(2, vm.FilteredViews.Count);
            Assert.AreEqual(0, vm.SelectedViewsCount);
        }

        [Test]
        public void Constructor_WhenActiveViewIsNull_AutoSelectsSelectedViewsAndDisablesCurrentView()
        {
            var adapter = new FakeShadowStudyAdapter
            {
                ActiveView = null,
                SiteInfo = new ShadowStudySiteInfo
                {
                    PlaceName = "Boston, MA",
                    LatitudeDegrees = 42.3601,
                    LongitudeDegrees = -71.0589,
                    TimeZoneHours = -5.0,
                    UsesDst = true
                }
            };
            adapter.EligibleViews.Add(new ShadowStudyViewItem
            {
                Id = "v-plan",
                Name = "Level 1",
                ViewType = "FloorPlan",
                IsCropped = true
            });
            adapter.EligibleViews.Add(new ShadowStudyViewItem
            {
                Id = "v-3d",
                Name = "Exterior 3D",
                ViewType = "ThreeD",
                IsCropped = false,
                Warnings =
                {
                    new ShadowStudyViewWarningBadge { Code = "Uncropped", Label = "Uncropped View" },
                    new ShadowStudyViewWarningBadge { Code = "VisualStyle", Label = "Hidden Line Export" }
                }
            });

            var vm = new ShadowStudyViewModel(
                adapter,
                new FakeFolderDialogService { NextResult = @"C:\Exports\Shadows" },
                new DateTime(2026, 6, 21));

            vm.BrowseOutputFolderCommand.Execute(null);

            Assert.IsFalse(vm.IsCurrentViewEligible);
            Assert.AreEqual(ShadowStudyExportRangeMode.SelectedViews, vm.ExportRangeMode);
            Assert.IsTrue(vm.IsSelectedViewsMode);
            Assert.IsFalse(vm.IsCurrentViewMode);
            StringAssert.Contains("Enabled", vm.DstStatusDisplay);

            // Attempting to switch to CurrentView when ineligible is ignored
            vm.ExportRangeMode = ShadowStudyExportRangeMode.CurrentView;
            Assert.AreEqual(ShadowStudyExportRangeMode.SelectedViews, vm.ExportRangeMode);

            // 0 checked views -> CanExport is false
            Assert.AreEqual(0, vm.SelectedViewsCount);
            Assert.IsFalse(vm.CanExport, "Export must be disabled when 0 views are checked in Selected Views mode.");

            // Checking 1 view enables Export and preserves multiple coexisting warning badges
            vm.FilteredViews[1].IsSelected = true;
            Assert.AreEqual(1, vm.SelectedViewsCount);
            Assert.IsTrue(vm.FilteredViews[1].HasWarnings);
            Assert.AreEqual(2, vm.FilteredViews[1].Warnings.Count);
            Assert.IsTrue(vm.CanExport);
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
        public void ViewSearchAndSelectAllSelectNone_FilterAndToggleVisibleViewsWhilePreservingHiddenSelections()
        {
            var adapter = new FakeShadowStudyAdapter { ActiveView = null };
            adapter.EligibleViews.AddRange(new[]
            {
                new ShadowStudyViewItem { Id = "v1", Name = "Level 1", ViewType = "FloorPlan" },
                new ShadowStudyViewItem { Id = "v2", Name = "Level 2", ViewType = "CeilingPlan" },
                new ShadowStudyViewItem { Id = "v3", Name = "South Section", ViewType = "Section" },
                new ShadowStudyViewItem { Id = "v4", Name = "Detail Callout", ViewType = "Detail" }
            });

            var vm = new ShadowStudyViewModel(adapter, new FakeFolderDialogService(), new DateTime(2026, 6, 21));
            vm.OutputFolder = @"C:\Exports\Shadows";

            // Filter by "Plan" matches FloorPlan and CeilingPlan via ViewType
            vm.ViewSearchText = "plan";
            Assert.AreEqual(2, vm.FilteredViews.Count);

            vm.SelectAllViewsCommand.Execute(null);
            Assert.AreEqual(2, vm.SelectedViewsCount);

            // Filter to Section and select it too
            vm.ViewSearchText = "south";
            Assert.AreEqual(1, vm.FilteredViews.Count);
            vm.SelectAllViewsCommand.Execute(null);
            Assert.AreEqual(3, vm.SelectedViewsCount);

            // Select None only unchecks currently visible views (South Section), leaving Level 1 & Level 2 checked
            vm.SelectNoneViewsCommand.Execute(null);
            Assert.AreEqual(2, vm.SelectedViewsCount);

            // Clearing filter shows all 4 with the 2 prior selections retained
            vm.ViewSearchText = string.Empty;
            Assert.AreEqual(4, vm.FilteredViews.Count);
            Assert.IsTrue(vm.FilteredViews[0].IsSelected);
            Assert.IsTrue(vm.FilteredViews[1].IsSelected);
            Assert.IsFalse(vm.FilteredViews[2].IsSelected);
            Assert.IsFalse(vm.FilteredViews[3].IsSelected);
        }

        [Test]
        public void BuildRunRequest_InSelectedViewsMode_AssignsCollisionOnlyViewTypeLabelsAndSwitchesModes()
        {
            var adapter = new FakeShadowStudyAdapter();
            var viewA = new ShadowStudyViewItem { Id = "v1", Name = "Level 1", ViewType = "FloorPlan" };
            var viewB = new ShadowStudyViewItem { Id = "v2", Name = "Level 1", ViewType = "CeilingPlan" };
            var viewC = new ShadowStudyViewItem { Id = "v3", Name = "Building Section", ViewType = "Section" };
            adapter.ActiveView = viewA;
            adapter.EligibleViews.AddRange(new[] { viewA, viewB, viewC });

            var vm = new ShadowStudyViewModel(adapter, new FakeFolderDialogService(), new DateTime(2026, 6, 21));
            vm.OutputFolder = @"C:\Exports\Shadows";

            // Switch to SelectedViews and select all three
            vm.ExportRangeMode = ShadowStudyExportRangeMode.SelectedViews;
            Assert.IsFalse(vm.CanExport);

            vm.SelectAllViewsCommand.Execute(null);
            Assert.IsTrue(vm.CanExport);
            StringAssert.Contains("Level 1 (FloorPlan) - 260621 - Composite.png", vm.CompositeFileNamePreview);

            ShadowStudyRunRequest batchRequest = vm.BuildRunRequest();
            Assert.AreEqual(3, batchRequest.Views.Count);
            Assert.AreEqual("Level 1 (FloorPlan)", batchRequest.Views[0].ViewLabel);
            Assert.AreEqual("Level 1 (CeilingPlan)", batchRequest.Views[1].ViewLabel);
            Assert.AreEqual("Building Section", batchRequest.Views[2].ViewLabel);

            // Uncheck CeilingPlan -> Level 1 no longer collides!
            vm.FilteredViews[1].IsSelected = false;
            ShadowStudyRunRequest nonCollidingRequest = vm.BuildRunRequest();
            Assert.AreEqual(2, nonCollidingRequest.Views.Count);
            Assert.AreEqual("Level 1", nonCollidingRequest.Views[0].ViewLabel);
            Assert.AreEqual("Building Section", nonCollidingRequest.Views[1].ViewLabel);

            // Switch back to CurrentView -> exports ActiveView cleanly
            vm.ExportRangeMode = ShadowStudyExportRangeMode.CurrentView;
            ShadowStudyRunRequest singleRequest = vm.BuildRunRequest();
            Assert.AreEqual(1, singleRequest.Views.Count);
            Assert.AreEqual("Level 1", singleRequest.Views[0].ViewLabel);
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
    }
}
