using System;
using System.Linq;
using NUnit.Framework;
using Synthetic.Modules.SharedCoordinates.ViewModels;

namespace SyntheticTests.Modules.SharedCoordinates
{
    [TestFixture]
    public class MoveLinksToSharedSitesViewModelTests
    {
        private FakeSharedCoordinatesAdapter _fakeAdapter = null!;

        [SetUp]
        public void SetUp()
        {
            _fakeAdapter = new FakeSharedCoordinatesAdapter();
        }

        [Test]
        public void Initialization_AllLoadedGroupsCheckedByDefault()
        {
            // Arrange
            _fakeAdapter.AddRepositionGroup("type-1", "Unit_A.rvt", total: 4, outOfSync: 2, unassigned: 1, isChecked: false);
            _fakeAdapter.AddRepositionGroup("type-2", "Unit_B.rvt", total: 6, outOfSync: 0, unassigned: 0, isChecked: false);

            // Act
            var vm = new MoveLinksToSharedSitesViewModel(_fakeAdapter);

            // Assert: All groups must be checked by default upon viewmodel loading
            Assert.AreEqual(2, vm.AllGroups.Count);
            Assert.IsTrue(vm.AllGroups[0].IsChecked);
            Assert.IsTrue(vm.AllGroups[1].IsChecked);
        }

        [Test]
        public void AggregateTallies_CalculatesTotalOutOfSyncAndUnassignedAccurately()
        {
            // Arrange
            _fakeAdapter.AddRepositionGroup("type-1", "Unit_A.rvt", total: 5, outOfSync: 3, unassigned: 1);
            _fakeAdapter.AddRepositionGroup("type-2", "Unit_B.rvt", total: 8, outOfSync: 2, unassigned: 2);

            // Act
            var vm = new MoveLinksToSharedSitesViewModel(_fakeAdapter);

            // Assert
            Assert.AreEqual(13, vm.TotalInstancesCount);
            Assert.AreEqual(5, vm.OutOfSyncInstancesCount);
            Assert.AreEqual(3, vm.UnassignedInstancesCount);
        }

        [Test]
        public void SearchText_FiltersGroupsCaseInsensitively()
        {
            // Arrange
            _fakeAdapter.AddRepositionGroup("type-1", "Tower_Unit_A.rvt", 2, 1, 0);
            _fakeAdapter.AddRepositionGroup("type-2", "Tower_Unit_B.rvt", 3, 2, 0);
            _fakeAdapter.AddRepositionGroup("type-3", "Podium_Core.rvt", 1, 0, 0);

            var vm = new MoveLinksToSharedSitesViewModel(_fakeAdapter);

            // Act 1: Filter by "unit"
            vm.SearchText = "unit";
            Assert.AreEqual(2, vm.FilteredGroups.Count);
            Assert.IsTrue(vm.FilteredGroups.All(g => g.TypeName.Contains("Unit")));

            // Act 2: Filter by "podium"
            vm.SearchText = "podium";
            Assert.AreEqual(1, vm.FilteredGroups.Count);
            Assert.AreEqual("Podium_Core.rvt", vm.FilteredGroups[0].TypeName);

            // Act 3: Clear filter
            vm.SearchText = string.Empty;
            Assert.AreEqual(3, vm.FilteredGroups.Count);
        }

        [Test]
        public void SelectAllAndSelectNoneCommands_ToggleCheckStateOfFilteredGroups()
        {
            // Arrange
            _fakeAdapter.AddRepositionGroup("type-1", "Unit_A.rvt", 2, 1, 0);
            _fakeAdapter.AddRepositionGroup("type-2", "Unit_B.rvt", 2, 1, 0);

            var vm = new MoveLinksToSharedSitesViewModel(_fakeAdapter);

            // Act 1: Deselect all
            vm.SelectNoneCommand.Execute(null);
            Assert.IsTrue(vm.AllGroups.All(g => !g.IsChecked));

            // Act 2: Select all
            vm.SelectAllCommand.Execute(null);
            Assert.IsTrue(vm.AllGroups.All(g => g.IsChecked));
        }

        [Test]
        public void RepositionCommand_DispatchesOnlySelectedLinkTypes()
        {
            // Arrange
            _fakeAdapter.AddRepositionInstance(
                typeId: "type-1",
                typeName: "Unit_A.rvt",
                instanceId: "inst-1",
                instanceName: "Unit_A - Floor 2",
                assignedSiteName: "Floor 2",
                isOutOfSync: true,
                isPinned: false,
                deltaZ: 10.0);

            _fakeAdapter.AddRepositionInstance(
                typeId: "type-2",
                typeName: "Unit_B.rvt",
                instanceId: "inst-2",
                instanceName: "Unit_B - Floor 3",
                assignedSiteName: "Floor 3",
                isOutOfSync: true,
                isPinned: false,
                deltaZ: 15.0);

            var vm = new MoveLinksToSharedSitesViewModel(_fakeAdapter);

            // Uncheck Unit_B
            var groupB = vm.AllGroups.First(g => g.TypeId == "type-2");
            groupB.IsChecked = false;

            bool? closeResult = null;
            vm.RequestClose += res => closeResult = res;

            // Act
            vm.RepositionCommand.Execute(null);

            // Assert
            Assert.IsTrue(closeResult);
            Assert.AreEqual(1, _fakeAdapter.RepositionCallCount);
            Assert.IsNotNull(_fakeAdapter.LastRepositionRequest);
            Assert.AreEqual(1, _fakeAdapter.LastRepositionRequest!.SelectedTypeIds.Count);
            Assert.Contains("type-1", (System.Collections.ICollection)_fakeAdapter.LastRepositionRequest.SelectedTypeIds);

            // Only Unit_A was repositioned
            Assert.AreEqual(1, vm.LastSummary!.RepositionedCount);
            Assert.Contains("Unit_A - Floor 2", (System.Collections.ICollection)vm.LastSummary.RepositionedInstanceNames);
        }

        [Test]
        public void RepositionCommand_PreservesPinnedStateLifecycle()
        {
            // Arrange: Pinned instance out of sync
            _fakeAdapter.AddRepositionInstance(
                typeId: "type-pinned",
                typeName: "Unit_Core.rvt",
                instanceId: "inst-pinned-1",
                instanceName: "Unit_Core - Floor 5",
                assignedSiteName: "Floor 5",
                isOutOfSync: true,
                isPinned: true, // Pinned!
                deltaZ: 20.0);

            var vm = new MoveLinksToSharedSitesViewModel(_fakeAdapter);

            // Act
            vm.RepositionCommand.Execute(null);

            // Assert: Pinned state lifecycle: unpinned (false) prior to transform, then restored (true)
            Assert.AreEqual(2, _fakeAdapter.PinStateHistory.Count);
            Assert.AreEqual(("inst-pinned-1", false), _fakeAdapter.PinStateHistory[0]);
            Assert.AreEqual(("inst-pinned-1", true), _fakeAdapter.PinStateHistory[1]);

            Assert.AreEqual(1, vm.LastSummary!.RepositionedCount);
        }

        [Test]
        public void RepositionCommand_SafelySkipsUnassignedInstances()
        {
            // Arrange: 1 out of sync instance, 1 <Not Shared> instance
            _fakeAdapter.AddRepositionInstance(
                typeId: "type-1",
                typeName: "Unit_Mixed.rvt",
                instanceId: "inst-assigned",
                instanceName: "Unit_Mixed - Floor 2",
                assignedSiteName: "Floor 2",
                isOutOfSync: true,
                isPinned: false,
                deltaZ: 10.0);

            _fakeAdapter.AddRepositionInstance(
                typeId: "type-1",
                typeName: "Unit_Mixed.rvt",
                instanceId: "inst-notshared",
                instanceName: "Unit_Mixed - <Not Shared>",
                assignedSiteName: "<Not Shared>",
                isOutOfSync: false,
                isPinned: false);

            var vm = new MoveLinksToSharedSitesViewModel(_fakeAdapter);

            // Act
            vm.RepositionCommand.Execute(null);

            // Assert
            Assert.AreEqual(1, vm.LastSummary!.RepositionedCount);
            Assert.AreEqual(1, vm.LastSummary.SkippedCount);
            Assert.Contains("Unit_Mixed - <Not Shared>", (System.Collections.ICollection)vm.LastSummary.SkippedInstanceNames);
        }
    }
}
