using System;
using NUnit.Framework;
using Synthetic.Modules.ShadowStudy.Models;
using Synthetic.Modules.ShadowStudy.ViewModels;

namespace SyntheticTests.Modules.ShadowStudy
{
    [TestFixture]
    public class ShadowStudyViewModelTests
    {
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

            vm.CancelCommand.Execute(null);
            Assert.AreEqual(false, closedWith);
        }
    }
}
