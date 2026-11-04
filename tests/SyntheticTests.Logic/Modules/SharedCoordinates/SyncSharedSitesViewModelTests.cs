using System;
using System.Linq;
using NUnit.Framework;
using Synthetic.Modules.SharedCoordinates.ViewModels;

namespace SyntheticTests.Modules.SharedCoordinates
{
    [TestFixture]
    public class SyncSharedSitesViewModelTests
    {
        private FakeSharedCoordinatesAdapter _fakeAdapter = null!;

        [SetUp]
        public void SetUp()
        {
            _fakeAdapter = new FakeSharedCoordinatesAdapter();
        }

        [Test]
        public void AutoSelection_SingleLoadedLink_AutoSelectedOnInitialization()
        {
            // Arrange
            _fakeAdapter.AddLinkedModel("link-1", "Tower_Host.rvt", isSelected: false);

            // Act
            var vm = new SyncSharedSitesViewModel(_fakeAdapter);

            // Assert
            Assert.IsNotNull(vm.SelectedLinkedModel);
            Assert.AreEqual("link-1", vm.SelectedLinkedModel!.Id);
            Assert.AreEqual("Tower_Host.rvt", vm.SelectedLinkedModel.Name);
        }

        [Test]
        public void AutoSelection_PreselectedViewportLink_TakesPrecedenceOverSingleRule()
        {
            // Arrange: 2 links, second is pre-selected in viewport
            _fakeAdapter.AddLinkedModel("link-1", "Tower_Host_A.rvt", isSelected: false);
            _fakeAdapter.AddLinkedModel("link-2", "Tower_Host_B.rvt", isSelected: true);

            // Act
            var vm = new SyncSharedSitesViewModel(_fakeAdapter);

            // Assert
            Assert.IsNotNull(vm.SelectedLinkedModel);
            Assert.AreEqual("link-2", vm.SelectedLinkedModel!.Id);
            Assert.AreEqual("Tower_Host_B.rvt", vm.SelectedLinkedModel.Name);
        }

        [Test]
        public void AutoSelection_MultipleUnselectedLinks_NoneSelected()
        {
            // Arrange: 2 links, neither pre-selected
            _fakeAdapter.AddLinkedModel("link-1", "Tower_Host_A.rvt", isSelected: false);
            _fakeAdapter.AddLinkedModel("link-2", "Tower_Host_B.rvt", isSelected: false);

            // Act
            var vm = new SyncSharedSitesViewModel(_fakeAdapter);

            // Assert
            Assert.IsNull(vm.SelectedLinkedModel);
            Assert.AreEqual(0, vm.AllTargetSites.Count);
        }

        [Test]
        public void ReferenceDatumLevel_DefaultsToLevelClosestToZeroElevation()
        {
            // Arrange: Levels at -10, 0, +15
            _fakeAdapter.AddActiveLevel("Basement", -10.0);
            _fakeAdapter.AddActiveLevel("Level 1", 0.0);
            _fakeAdapter.AddActiveLevel("Level 2", 15.0);

            // Act
            var vm = new SyncSharedSitesViewModel(_fakeAdapter);

            // Assert
            Assert.IsNotNull(vm.SelectedReferenceDatumLevel);
            Assert.AreEqual("Level 1", vm.SelectedReferenceDatumLevel!.Name);
            Assert.AreEqual(0.0, vm.SelectedReferenceDatumLevel.Elevation);
        }

        [Test]
        public void ReferenceDatumLevel_AllNonZeroLevels_SelectsSmallestAbsoluteElevation()
        {
            // Arrange: Non-zero levels at +0.5 ft and -2.0 ft -> +0.5 is closer to zero
            _fakeAdapter.AddActiveLevel("Top of Slab", 0.5);
            _fakeAdapter.AddActiveLevel("SOG Grade", -2.0);
            _fakeAdapter.AddActiveLevel("Roof", 100.0);

            // Act
            var vm = new SyncSharedSitesViewModel(_fakeAdapter);

            // Assert
            Assert.IsNotNull(vm.SelectedReferenceDatumLevel);
            Assert.AreEqual("Top of Slab", vm.SelectedReferenceDatumLevel!.Name);
            Assert.AreEqual(0.5, vm.SelectedReferenceDatumLevel.Elevation);
        }

        [Test]
        public void LinkedModelSelection_PopulatesLevelsSortedAscendingByElevation()
        {
            // Arrange
            _fakeAdapter.AddLinkedModel("link-host", "Host.rvt");
            _fakeAdapter.AddActiveLevel("Level 1", 0.0);

            // Add levels in disordered order
            _fakeAdapter.AddLinkedLevel("link-host", "Floor 10", 120.0, 150.0);
            _fakeAdapter.AddLinkedLevel("link-host", "Floor 2", 12.0, 42.0);
            _fakeAdapter.AddLinkedLevel("link-host", "Floor 5", 48.0, 78.0);

            // Act
            var vm = new SyncSharedSitesViewModel(_fakeAdapter);

            // Assert
            Assert.AreEqual(3, vm.FilteredTargetSites.Count);
            Assert.AreEqual("Floor 2", vm.FilteredTargetSites[0].LevelName);
            Assert.AreEqual(42.0, vm.FilteredTargetSites[0].SharedElevation);
            Assert.AreEqual("Floor 5", vm.FilteredTargetSites[1].LevelName);
            Assert.AreEqual(78.0, vm.FilteredTargetSites[1].SharedElevation);
            Assert.AreEqual("Floor 10", vm.FilteredTargetSites[2].LevelName);
            Assert.AreEqual(150.0, vm.FilteredTargetSites[2].SharedElevation);
        }

        [Test]
        public void LinkedLevelsMatchingExistingSites_ArePreCheckedWithUpdateAction()
        {
            // Arrange
            _fakeAdapter.AddLinkedModel("link-host", "Host.rvt");
            _fakeAdapter.AddActiveLevel("Level 1", 0.0);

            // Existing sites in unit model: Floor 2 and Floor 3
            _fakeAdapter.AddExistingSite("Floor 2");
            _fakeAdapter.AddExistingSite("Floor 3");

            // Linked host levels: Floor 2, Floor 3, Floor 4
            _fakeAdapter.AddLinkedLevel("link-host", "Floor 2", 12.0, 42.0);
            _fakeAdapter.AddLinkedLevel("link-host", "Floor 3", 24.0, 54.0);
            _fakeAdapter.AddLinkedLevel("link-host", "Floor 4", 36.0, 66.0);

            // Act
            var vm = new SyncSharedSitesViewModel(_fakeAdapter);

            // Assert
            var site2 = vm.FilteredTargetSites.First(s => s.LevelName == "Floor 2");
            Assert.IsTrue(site2.IsChecked, "Matching site Floor 2 should be pre-checked.");
            Assert.IsTrue(site2.SiteExists);
            Assert.AreEqual("Update", site2.ActionText);

            var site3 = vm.FilteredTargetSites.First(s => s.LevelName == "Floor 3");
            Assert.IsTrue(site3.IsChecked, "Matching site Floor 3 should be pre-checked.");
            Assert.IsTrue(site3.SiteExists);
            Assert.AreEqual("Update", site3.ActionText);

            var site4 = vm.FilteredTargetSites.First(s => s.LevelName == "Floor 4");
            Assert.IsFalse(site4.IsChecked, "Unmatched site Floor 4 should NOT be pre-checked.");
            Assert.IsFalse(site4.SiteExists);
            Assert.AreEqual("Create", site4.ActionText);
        }

        [Test]
        public void SearchTextFiltering_FiltersLevelsByNameCaseInsensitive()
        {
            // Arrange
            _fakeAdapter.AddLinkedModel("link-host", "Host.rvt");
            _fakeAdapter.AddActiveLevel("Level 1", 0.0);
            _fakeAdapter.AddLinkedLevel("link-host", "Floor 3", 30.0, 50.0);
            _fakeAdapter.AddLinkedLevel("link-host", "Floor 4", 40.0, 60.0);
            _fakeAdapter.AddLinkedLevel("link-host", "Penthouse A", 120.0, 140.0);

            var vm = new SyncSharedSitesViewModel(_fakeAdapter);
            Assert.AreEqual(3, vm.FilteredTargetSites.Count);

            // Act
            vm.SearchText = "floor";

            // Assert
            Assert.AreEqual(2, vm.FilteredTargetSites.Count);
            Assert.IsTrue(vm.FilteredTargetSites.All(s => s.LevelName.StartsWith("Floor")));

            // Act: clear filter
            vm.SearchText = string.Empty;
            Assert.AreEqual(3, vm.FilteredTargetSites.Count);
        }

        [Test]
        public void SelectAllAndSelectNone_ControlsCheckedState()
        {
            // Arrange
            _fakeAdapter.AddLinkedModel("link-host", "Host.rvt");
            _fakeAdapter.AddActiveLevel("Level 1", 0.0);
            _fakeAdapter.AddLinkedLevel("link-host", "Floor 1", 10.0, 30.0);
            _fakeAdapter.AddLinkedLevel("link-host", "Floor 2", 20.0, 40.0);

            var vm = new SyncSharedSitesViewModel(_fakeAdapter);
            Assert.IsFalse(vm.AllTargetSites.Any(s => s.IsChecked));

            // Act: Select All
            vm.SelectAllCommand.Execute(null);
            Assert.IsTrue(vm.AllTargetSites.All(s => s.IsChecked));
            Assert.IsTrue(vm.CanExecuteSync);

            // Act: Select None
            vm.SelectNoneCommand.Execute(null);
            Assert.IsTrue(vm.AllTargetSites.All(s => !s.IsChecked));
            Assert.IsFalse(vm.CanExecuteSync);
        }

        [Test]
        public void SyncCommand_CanExecute_RequiresValidModelDatumLevelAndCheckedTarget()
        {
            // Arrange: No levels checked initially
            _fakeAdapter.AddLinkedModel("link-host", "Host.rvt");
            _fakeAdapter.AddActiveLevel("Level 1", 0.0);
            _fakeAdapter.AddLinkedLevel("link-host", "Floor 1", 10.0, 30.0);

            var vm = new SyncSharedSitesViewModel(_fakeAdapter);

            // Assert: Cannot execute when 0 targets are checked
            Assert.IsFalse(vm.CanExecuteSync);

            // Check one target
            vm.FilteredTargetSites[0].IsChecked = true;
            Assert.IsTrue(vm.CanExecuteSync);

            // Clear selected linked model
            vm.SelectedLinkedModel = null;
            Assert.IsFalse(vm.CanExecuteSync);
        }

        [Test]
        public void SyncExecution_ElevationMath_ZeroReferenceDatumLevel()
        {
            // Formula: TargetPosition.Elevation = TargetLevelSharedElevation - ReferenceDatumInternalElevation
            // Arrange: Unit model Level 1 at 0.0 ft.
            // Linked host model Floor 5 at shared elevation 75.0 ft.
            _fakeAdapter.AddLinkedModel("link-host", "Host.rvt");
            _fakeAdapter.AddActiveLevel("Level 1", 0.0);
            _fakeAdapter.AddLinkedLevel("link-host", "Floor 5", 48.0, 75.0);

            var vm = new SyncSharedSitesViewModel(_fakeAdapter);
            vm.FilteredTargetSites[0].IsChecked = true;

            // Act
            vm.SyncCommand.Execute(null);

            // Assert
            Assert.AreEqual(1, _fakeAdapter.SyncCallCount);
            Assert.AreEqual(1, vm.LastSummary!.CreatedCount);
            Assert.AreEqual("Floor 5", vm.LastSummary.CreatedSiteNames[0]);

            var sitePos = _fakeAdapter.GetSitePosition("Floor 5");
            Assert.IsNotNull(sitePos);
            // 75.0 - 0.0 = 75.0
            Assert.AreEqual(75.0, sitePos!.Value.Elevation, 1e-6);
        }

        [Test]
        public void SyncExecution_ElevationMath_NonZeroReferenceDatumLevel()
        {
            // Formula: TargetPosition.Elevation = TargetLevelSharedElevation - ReferenceDatumInternalElevation
            // Arrange: Unit model Reference Datum Level at +1.5 ft.
            // Linked host model Floor 7 at shared elevation 100.0 ft.
            _fakeAdapter.AddLinkedModel("link-host", "Host.rvt");
            _fakeAdapter.AddActiveLevel("Reference Level", 1.5);
            _fakeAdapter.AddLinkedLevel("link-host", "Floor 7", 72.0, 100.0);

            var vm = new SyncSharedSitesViewModel(_fakeAdapter);
            vm.FilteredTargetSites[0].IsChecked = true;

            // Act
            vm.SyncCommand.Execute(null);

            // Assert
            Assert.AreEqual(1, _fakeAdapter.SyncCallCount);
            var sitePos = _fakeAdapter.GetSitePosition("Floor 7");
            Assert.IsNotNull(sitePos);
            // Expected: 100.0 - 1.5 = 98.5
            Assert.AreEqual(98.5, sitePos!.Value.Elevation, 1e-6);
        }

        [Test]
        public void SyncExecution_PreservesExistingHorizontalAndAngleCoordinates()
        {
            // Arrange: Site "Floor 3" already exists with specific East/West, North/South, Angle
            const double originalEW = 450.25;
            const double originalNS = 890.75;
            const double originalAngle = 0.785398; // ~45 deg
            const double originalElev = 20.0;

            _fakeAdapter.AddLinkedModel("link-host", "Host.rvt");
            _fakeAdapter.AddActiveLevel("Level 1", 0.0);
            _fakeAdapter.AddExistingSite("Floor 3", originalEW, originalNS, originalElev, originalAngle);
            _fakeAdapter.AddLinkedLevel("link-host", "Floor 3", 24.0, 52.0); // Updated shared elevation = 52.0

            var vm = new SyncSharedSitesViewModel(_fakeAdapter);
            // Pre-checked because it already exists
            Assert.IsTrue(vm.FilteredTargetSites[0].IsChecked);

            // Act
            vm.SyncCommand.Execute(null);

            // Assert
            Assert.AreEqual(1, vm.LastSummary!.UpdatedCount);
            Assert.AreEqual(0, vm.LastSummary.CreatedCount);
            Assert.AreEqual("Floor 3", vm.LastSummary.UpdatedSiteNames[0]);

            var sitePos = _fakeAdapter.GetSitePosition("Floor 3");
            Assert.IsNotNull(sitePos);
            // Elevation updated: 52.0 - 0.0 = 52.0
            Assert.AreEqual(52.0, sitePos!.Value.Elevation, 1e-6);
            // Horizontal and angle coordinates strictly preserved
            Assert.AreEqual(originalEW, sitePos.Value.EastWest, 1e-6);
            Assert.AreEqual(originalNS, sitePos.Value.NorthSouth, 1e-6);
            Assert.AreEqual(originalAngle, sitePos.Value.Angle, 1e-6);
        }

        [Test]
        public void SyncExecution_LeavesUnselectedExistingSitesUntouched()
        {
            // Arrange: Existing sites: "Floor 2" and "Floor 3"
            _fakeAdapter.AddLinkedModel("link-host", "Host.rvt");
            _fakeAdapter.AddActiveLevel("Level 1", 0.0);
            _fakeAdapter.AddExistingSite("Floor 2", 100.0, 200.0, 30.0, 0.0);
            _fakeAdapter.AddExistingSite("Floor 3", 100.0, 200.0, 42.0, 0.0);

            _fakeAdapter.AddLinkedLevel("link-host", "Floor 2", 12.0, 35.0);
            _fakeAdapter.AddLinkedLevel("link-host", "Floor 3", 24.0, 50.0);

            var vm = new SyncSharedSitesViewModel(_fakeAdapter);

            // Uncheck "Floor 3" so only "Floor 2" is synchronized
            var target3 = vm.FilteredTargetSites.First(s => s.LevelName == "Floor 3");
            target3.IsChecked = false;

            // Act
            vm.SyncCommand.Execute(null);

            // Assert
            Assert.AreEqual(1, vm.LastSummary!.UpdatedCount);
            Assert.AreEqual("Floor 2", vm.LastSummary.UpdatedSiteNames[0]);

            // Unselected "Floor 3" must remain exactly untouched at 42.0
            var pos3 = _fakeAdapter.GetSitePosition("Floor 3");
            Assert.AreEqual(42.0, pos3!.Value.Elevation, 1e-6);

            // Selected "Floor 2" updated to 35.0
            var pos2 = _fakeAdapter.GetSitePosition("Floor 2");
            Assert.AreEqual(35.0, pos2!.Value.Elevation, 1e-6);
        }
    }
}
