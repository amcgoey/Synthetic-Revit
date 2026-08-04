using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Synthetic.Modules.SheetIndex.Models;
using Synthetic.Modules.SheetIndex.Services;
using Synthetic.Modules.SheetIndex.ViewModels;

namespace SyntheticTests.Shared.Modules.SheetIndex
{
    [TestFixture]
    public class ExportSheetIndexViewModelTests
    {
        [SetUp]
        public void SetUp()
        {
            SheetIndexSessionState.Reset();
        }

        [TearDown]
        public void TearDown()
        {
            SheetIndexSessionState.Reset();
        }

        private List<SheetIndexSheetModel> CreateSampleSheets()
        {
            return new List<SheetIndexSheetModel>
            {
                new SheetIndexSheetModel("s-101", "A101", "Ground Floor Plan"),
                new SheetIndexSheetModel("s-102", "A102", "Second Floor Plan"),
                new SheetIndexSheetModel("s-201", "S201", "Structural Foundation Plan"),
                new SheetIndexSheetModel("s-301", "M301", "Mechanical HVAC Plan")
            };
        }

        private List<SheetIndexRevisionModel> CreateSampleRevisions()
        {
            return new List<SheetIndexRevisionModel>
            {
                new SheetIndexRevisionModel("rev-1", "Permit Set", "2026-01-01", 1),
                new SheetIndexRevisionModel("rev-2", "Construction Set", "2026-02-01", 2)
            };
        }

        [Test]
        public void LiveSearchFilter_FiltersBySheetNumberAndSheetName_CaseInsensitive()
        {
            var sheets = CreateSampleSheets();
            var revisions = CreateSampleRevisions();
            var vm = new ExportSheetIndexViewModel(sheets, revisions);

            // Filter by number "a10"
            vm.SearchText = "a10";
            var visible1 = vm.VisibleSheets.Cast<SheetItemViewModel>().ToList();
            Assert.AreEqual(2, visible1.Count);
            Assert.IsTrue(visible1.All(s => s.SheetNumber.StartsWith("A10")));

            // Filter by name "HVAC"
            vm.SearchText = "hvac";
            var visible2 = vm.VisibleSheets.Cast<SheetItemViewModel>().ToList();
            Assert.AreEqual(1, visible2.Count);
            Assert.AreEqual("M301", visible2[0].SheetNumber);

            // Clear filter
            vm.SearchText = string.Empty;
            var visibleAll = vm.VisibleSheets.Cast<SheetItemViewModel>().ToList();
            Assert.AreEqual(4, visibleAll.Count);
        }

        [Test]
        public void SelectAllAndDeselectAll_WithActiveFilter_AppliesToVisibleSheetsOnly()
        {
            var sheets = CreateSampleSheets();
            var revisions = CreateSampleRevisions();
            var vm = new ExportSheetIndexViewModel(sheets, revisions);

            // Filter to A101 & A102
            vm.SearchText = "Floor";
            vm.DeselectAllSheetsCommand.Execute(null);

            // Only visible sheets ("Floor") should be deselected
            Assert.IsFalse(vm.Sheets.First(s => s.SheetNumber == "A101").IsSelected);
            Assert.IsFalse(vm.Sheets.First(s => s.SheetNumber == "A102").IsSelected);
            Assert.IsTrue(vm.Sheets.First(s => s.SheetNumber == "S201").IsSelected);
            Assert.IsTrue(vm.Sheets.First(s => s.SheetNumber == "M301").IsSelected);

            // Select all visible sheets
            vm.SelectAllSheetsCommand.Execute(null);
            Assert.IsTrue(vm.Sheets.First(s => s.SheetNumber == "A101").IsSelected);
            Assert.IsTrue(vm.Sheets.First(s => s.SheetNumber == "A102").IsSelected);
        }

        [Test]
        public void SessionPersistence_SavesAndRestoresSelectionState()
        {
            var sheets = CreateSampleSheets();
            var revisions = CreateSampleRevisions();

            // Run 1: User selects only S201 and rev-2, sets source mode to ViewSheetSet
            var vm1 = new ExportSheetIndexViewModel(sheets, revisions);
            vm1.SelectedSourceMode = SheetSelectionSourceMode.ViewSheetSet;
            vm1.SelectedPrintSetName = "Architectural Set";

            // Deselect A101, A102, M301, and rev-1
            foreach (var item in vm1.Sheets)
            {
                item.IsSelected = (item.SheetNumber == "S201");
            }
            foreach (var item in vm1.Revisions)
            {
                item.IsSelected = (item.Model.UniqueId == "rev-2");
            }

            // Execute export saves state
            vm1.ExportCommand.Execute(null);
            Assert.IsTrue(SheetIndexSessionState.HasSavedState);
            Assert.AreEqual(SheetSelectionSourceMode.ViewSheetSet, SheetIndexSessionState.SelectedSourceMode);
            Assert.AreEqual("Architectural Set", SheetIndexSessionState.SelectedPrintSetName);

            // Run 2: Instantiate new ViewModel, should restore selections from session state
            var vm2 = new ExportSheetIndexViewModel(sheets, revisions);
            Assert.AreEqual(SheetSelectionSourceMode.ViewSheetSet, vm2.SelectedSourceMode);
            Assert.AreEqual("Architectural Set", vm2.SelectedPrintSetName);

            var selectedSheets2 = vm2.GetSelectedSheets();
            Assert.AreEqual(1, selectedSheets2.Count);
            Assert.AreEqual("S201", selectedSheets2[0].SheetNumber);

            var selectedRevisions2 = vm2.GetSelectedRevisions();
            Assert.AreEqual(1, selectedRevisions2.Count);
            Assert.AreEqual("rev-2", selectedRevisions2[0].UniqueId);
        }

        [Test]
        public void SourceModeSelection_FiltersSheetSelection_WhenPrintSetOrScheduleSelected()
        {
            var sheets = CreateSampleSheets();
            var revisions = CreateSampleRevisions();
            var printSets = new[] { "Arch Set" };
            var printSetMap = new Dictionary<string, List<string>>
            {
                { "Arch Set", new List<string> { "s-101", "s-102" } }
            };

            var vm = new ExportSheetIndexViewModel(sheets, revisions, printSets, null, printSetMap, null);

            // Change source mode to Print Set
            vm.SelectedSourceMode = SheetSelectionSourceMode.ViewSheetSet;
            vm.SelectedPrintSetName = "Arch Set";

            var selected = vm.GetSelectedSheets();
            Assert.AreEqual(2, selected.Count);
            Assert.IsTrue(selected.All(s => s.UniqueId == "s-101" || s.UniqueId == "s-102"));

            // Re-select All Sheets
            vm.SelectedSourceMode = SheetSelectionSourceMode.AllSheets;
            Assert.AreEqual(4, vm.GetSelectedSheets().Count);
        }

        [Test]
        public void SessionPersistence_NewlyAddedSheetsDefaultToSelected()
        {
            var sheets = CreateSampleSheets();
            var revisions = CreateSampleRevisions();

            var vm1 = new ExportSheetIndexViewModel(sheets, revisions);
            vm1.ExportCommand.Execute(null);

            // Add a new sheet created later in session
            sheets.Add(new SheetIndexSheetModel("s-999", "A999", "New Future Sheet"));

            var vm2 = new ExportSheetIndexViewModel(sheets, revisions);
            var selected2 = vm2.GetSelectedSheets();

            Assert.IsTrue(selected2.Any(s => s.UniqueId == "s-999"));
        }
    }
}

