using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using NUnit.Framework;
using Synthetic.Features.SheetIndex.Models;
using Synthetic.Features.SheetIndex.Services;
using Synthetic.Features.SheetIndex.ViewModels;
using SyntheticTests.Fixtures;

namespace SyntheticTests.Features.SheetIndex
{
    [TestFixture]
    [UnitTest]
    [Apartment(ApartmentState.STA)]
    public class ExportSheetIndexViewModelTests
    {
        private static FakeSheetIndexAdapter CreateSampleAdapter()
        {
            var sheets = new List<SheetIndexSheetModel>
            {
                new SheetIndexSheetModel("s-101", "A101", "Ground Floor Plan"),
                new SheetIndexSheetModel("s-102", "A102", "Second Floor Plan"),
                new SheetIndexSheetModel("s-201", "S201", "Structural Foundation Plan"),
                new SheetIndexSheetModel("s-301", "M301", "Mechanical HVAC Plan")
            };

            var revisions = new List<SheetIndexRevisionModel>
            {
                new SheetIndexRevisionModel("rev-1", "Permit Set", "2026-01-01", 1),
                new SheetIndexRevisionModel("rev-2", "Construction Set", "2026-02-01", 2)
            };

            return new FakeSheetIndexAdapter(sheets, revisions);
        }

        [Test]
        public void CollapsedConstructor_ExposesOnlySheetIndexSnapshotConstructor()
        {
            ConstructorInfo[] constructors = typeof(ExportSheetIndexViewModel).GetConstructors(BindingFlags.Public | BindingFlags.Instance);
            Assert.AreEqual(1, constructors.Length, "ExportSheetIndexViewModel must expose only the collapsed SheetIndexSnapshot constructor.");

            ParameterInfo[] parameters = constructors[0].GetParameters();
            Assert.AreEqual(2, parameters.Length);
            Assert.AreEqual(typeof(SheetIndexSnapshot), parameters[0].ParameterType);
            Assert.AreEqual(typeof(SheetIndexSessionState), parameters[1].ParameterType);
            Assert.IsTrue(parameters[1].IsOptional);
        }

        [Test]
        public void SheetIndexSnapshot_IsImmutableAndDerivesLookupMapsDeterministically()
        {
            var sheets = new List<SheetIndexSheetModel>
            {
                new SheetIndexSheetModel("s-101", "A101", "Ground Floor Plan")
            };
            var revisions = new List<SheetIndexRevisionModel>
            {
                new SheetIndexRevisionModel("rev-1", "Permit Set", "2026-01-01", 1)
            };
            var printSets = new List<SheetIndexPrintSetModel>
            {
                new SheetIndexPrintSetModel("ps-1", "Arch Set", new[] { "s-101" })
            };
            var schedules = new List<SheetIndexScheduleModel>
            {
                new SheetIndexScheduleModel("sch-1", "Drawing Index", new[] { sheets[0] })
            };

            var snapshot = new SheetIndexSnapshot(sheets, revisions, printSets, schedules);

            // Mutating input lists does not mutate snapshot
            sheets.Add(new SheetIndexSheetModel("s-999", "A999", "Added Later"));
            Assert.AreEqual(1, snapshot.Sheets.Count);

            Assert.IsTrue(snapshot.PrintSetSheetMap.TryGetValue("arch set", out var psIds));
            CollectionAssert.AreEqual(new[] { "s-101" }, psIds);
            Assert.IsTrue(snapshot.ScheduleSheetMap.TryGetValue("drawing index", out var schIds));
            CollectionAssert.AreEqual(new[] { "s-101" }, schIds);

            // Collection and dictionary wrappers reject mutation
            Assert.Throws<NotSupportedException>(() => ((IList<SheetIndexSheetModel>)snapshot.Sheets).Add(sheets[0]));
            Assert.Throws<NotSupportedException>(() => ((IDictionary<string, IReadOnlyList<string>>)snapshot.PrintSetSheetMap).Add("Other", new[] { "s-101" }));
            Assert.Throws<NotSupportedException>(() => ((IDictionary<string, IReadOnlyList<string>>)snapshot.ScheduleSheetMap).Add("Other", new[] { "s-101" }));
        }

        [Test]
        public void LiveSearchFilter_FiltersBySheetNumberAndSheetName_CaseInsensitive()
        {
            FakeSheetIndexAdapter adapter = CreateSampleAdapter();
            var vm = new ExportSheetIndexViewModel(adapter.LoadProjectSnapshot());

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
            FakeSheetIndexAdapter adapter = CreateSampleAdapter();
            var vm = new ExportSheetIndexViewModel(adapter.LoadProjectSnapshot());

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
            FakeSheetIndexAdapter adapter = CreateSampleAdapter();
            var sessionState = new SheetIndexSessionState();

            // Run 1: User selects only S201 and rev-2, sets source mode to ViewSheetSet
            var vm1 = new ExportSheetIndexViewModel(adapter.LoadProjectSnapshot(), sessionState);
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
            Assert.IsTrue(sessionState.HasSavedState);
            Assert.AreEqual(SheetSelectionSourceMode.ViewSheetSet, sessionState.SelectedSourceMode);
            Assert.AreEqual("Architectural Set", sessionState.SelectedPrintSetName);

            // Run 2: Instantiate new ViewModel with same session state, should restore selections
            var vm2 = new ExportSheetIndexViewModel(adapter.LoadProjectSnapshot(), sessionState);
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
        public void SessionPersistence_RestoresPrintSetAndScheduleModeViaApplySourceModeSelection()
        {
            FakeSheetIndexAdapter adapter = CreateSampleAdapter();
            adapter.PrintSets.Add(new SheetIndexPrintSetModel("ps-1", "Arch Set", new[] { "s-102", "s-101" }));
            adapter.PrintSets.Add(new SheetIndexPrintSetModel("ps-2", "MEP Set", new[] { "s-301" }));

            var schedSheet1 = new SheetIndexSheetModel("s-201", "S201", "Structural Foundation Plan") { SectionGroup = "STRUCTURAL" };
            var schedSheet2 = new SheetIndexSheetModel("s-101", "A101", "Ground Floor Plan") { SectionGroup = "ARCHITECTURAL" };
            adapter.Schedules.Add(new SheetIndexScheduleModel("sch-1", "Drawing Index", new[] { schedSheet1, schedSheet2 }));

            // Case 1: Restore PrintSet mode and unchecked sheet inside that PrintSet
            var printSetSession = new SheetIndexSessionState();
            var vmPrint1 = new ExportSheetIndexViewModel(adapter.LoadProjectSnapshot(), printSetSession);
            vmPrint1.SelectedSourceMode = SheetSelectionSourceMode.ViewSheetSet;
            vmPrint1.SelectedPrintSetName = "Arch Set";
            vmPrint1.Sheets.First(s => s.SheetNumber == "A101").IsSelected = false;
            vmPrint1.ExportCommand.Execute(null);

            var vmPrint2 = new ExportSheetIndexViewModel(adapter.LoadProjectSnapshot(), printSetSession);
            Assert.AreEqual(SheetSelectionSourceMode.ViewSheetSet, vmPrint2.SelectedSourceMode);
            Assert.AreEqual("Arch Set", vmPrint2.SelectedPrintSetName);
            Assert.IsNotNull(vmPrint2.SelectedPrintSet);
            Assert.AreEqual("ps-1", vmPrint2.SelectedPrintSet!.UniqueId);
            Assert.IsTrue(vmPrint2.PreserveSheetOrder);
            CollectionAssert.AreEqual(new[] { "A102", "A101" }, vmPrint2.Sheets.Select(s => s.SheetNumber).ToArray());
            CollectionAssert.AreEqual(new[] { "A102" }, vmPrint2.GetSelectedSheets().Select(s => s.SheetNumber).ToArray());

            // Case 2: Restore Schedule mode with schedule order and SectionGroup preserved
            var scheduleSession = new SheetIndexSessionState();
            var vmSched1 = new ExportSheetIndexViewModel(adapter.LoadProjectSnapshot(), scheduleSession);
            vmSched1.SelectedSourceMode = SheetSelectionSourceMode.ViewSchedule;
            vmSched1.SelectedScheduleName = "Drawing Index";
            vmSched1.ExportCommand.Execute(null);

            var vmSched2 = new ExportSheetIndexViewModel(adapter.LoadProjectSnapshot(), scheduleSession);
            Assert.AreEqual(SheetSelectionSourceMode.ViewSchedule, vmSched2.SelectedSourceMode);
            Assert.AreEqual("Drawing Index", vmSched2.SelectedScheduleName);
            Assert.IsNotNull(vmSched2.SelectedSchedule);
            Assert.AreEqual("sch-1", vmSched2.SelectedSchedule!.UniqueId);
            Assert.IsTrue(vmSched2.PreserveSheetOrder);
            CollectionAssert.AreEqual(new[] { "S201", "A101" }, vmSched2.Sheets.Select(s => s.SheetNumber).ToArray());
            Assert.AreEqual("STRUCTURAL", vmSched2.Sheets[0].Model.SectionGroup);
            Assert.AreEqual("ARCHITECTURAL", vmSched2.Sheets[1].Model.SectionGroup);

            // Case 3: Restore AllSheets mode preserving unchecked sheets
            var allSheetsSession = new SheetIndexSessionState();
            var vmAll1 = new ExportSheetIndexViewModel(adapter.LoadProjectSnapshot(), allSheetsSession);
            vmAll1.Sheets.First(s => s.SheetNumber == "A101").IsSelected = false;
            vmAll1.ExportCommand.Execute(null);

            var vmAll2 = new ExportSheetIndexViewModel(adapter.LoadProjectSnapshot(), allSheetsSession);
            Assert.AreEqual(SheetSelectionSourceMode.AllSheets, vmAll2.SelectedSourceMode);
            Assert.IsFalse(vmAll2.PreserveSheetOrder);
            Assert.IsFalse(vmAll2.Sheets.First(s => s.SheetNumber == "A101").IsSelected);
            Assert.IsTrue(vmAll2.Sheets.First(s => s.SheetNumber == "A102").IsSelected);
        }

        [Test]
        public void SelectedPrintSetName_Change_SyncsSelectedPrintSetAndUpdatesSheetsInPrintSetMode()
        {
            FakeSheetIndexAdapter adapter = CreateSampleAdapter();
            adapter.PrintSets.Add(new SheetIndexPrintSetModel("ps-1", "Arch Set", new[] { "s-102", "s-101" }));
            adapter.PrintSets.Add(new SheetIndexPrintSetModel("ps-2", "MEP Set", new[] { "s-301" }));

            var vm = new ExportSheetIndexViewModel(adapter.LoadProjectSnapshot());
            var changedProperties = new List<string>();
            vm.PropertyChanged += (_, args) =>
            {
                if (!string.IsNullOrEmpty(args.PropertyName))
                {
                    changedProperties.Add(args.PropertyName!);
                }
            };

            // Change SelectedPrintSetName while in AllSheets mode: syncs SelectedPrintSet without replacing AllSheets list
            vm.SelectedPrintSetName = "MEP Set";
            Assert.IsNotNull(vm.SelectedPrintSet);
            Assert.AreEqual("ps-2", vm.SelectedPrintSet!.UniqueId);
            CollectionAssert.Contains(changedProperties, nameof(ExportSheetIndexViewModel.SelectedPrintSet));
            Assert.AreEqual(4, vm.Sheets.Count);

            // Switch to ViewSheetSet mode -> displays MEP Set
            vm.SelectedSourceMode = SheetSelectionSourceMode.ViewSheetSet;
            CollectionAssert.AreEqual(new[] { "M301" }, vm.GetSelectedSheets().Select(s => s.SheetNumber).ToArray());

            // Change SelectedPrintSetName while active -> syncs SelectedPrintSet and switches sheets to Arch Set in print order
            vm.SelectedPrintSetName = "Arch Set";
            Assert.IsNotNull(vm.SelectedPrintSet);
            Assert.AreEqual("ps-1", vm.SelectedPrintSet!.UniqueId);
            CollectionAssert.AreEqual(new[] { "A102", "A101" }, vm.GetSelectedSheets().Select(s => s.SheetNumber).ToArray());

            // Unknown name clears SelectedPrintSet safely
            vm.SelectedPrintSetName = "NonExistent";
            Assert.IsNull(vm.SelectedPrintSet);
        }

        [Test]
        public void SelectedScheduleName_Change_SyncsSelectedScheduleAndUpdatesSheetsInScheduleMode()
        {
            FakeSheetIndexAdapter adapter = CreateSampleAdapter();
            var archSheets = new[]
            {
                new SheetIndexSheetModel("s-102", "A102", "Second Floor Plan") { SectionGroup = "ARCH" },
                new SheetIndexSheetModel("s-101", "A101", "Ground Floor Plan") { SectionGroup = "ARCH" }
            };
            var structSheets = new[]
            {
                new SheetIndexSheetModel("s-201", "S201", "Structural Foundation Plan") { SectionGroup = "STRUCT" }
            };
            adapter.Schedules.Add(new SheetIndexScheduleModel("sch-1", "Arch Index", archSheets));
            adapter.Schedules.Add(new SheetIndexScheduleModel("sch-2", "Struct Index", structSheets));

            var vm = new ExportSheetIndexViewModel(adapter.LoadProjectSnapshot());
            var changedProperties = new List<string>();
            vm.PropertyChanged += (_, args) =>
            {
                if (!string.IsNullOrEmpty(args.PropertyName))
                {
                    changedProperties.Add(args.PropertyName!);
                }
            };

            // Change SelectedScheduleName while in AllSheets mode: syncs SelectedSchedule without replacing AllSheets list
            vm.SelectedScheduleName = "Struct Index";
            Assert.IsNotNull(vm.SelectedSchedule);
            Assert.AreEqual("sch-2", vm.SelectedSchedule!.UniqueId);
            CollectionAssert.Contains(changedProperties, nameof(ExportSheetIndexViewModel.SelectedSchedule));
            Assert.AreEqual(4, vm.Sheets.Count);

            // Switch to ViewSchedule mode -> displays Struct Index
            vm.SelectedSourceMode = SheetSelectionSourceMode.ViewSchedule;
            CollectionAssert.AreEqual(new[] { "S201" }, vm.GetSelectedSheets().Select(s => s.SheetNumber).ToArray());

            // Switch SelectedScheduleName while active -> syncs SelectedSchedule and displays Arch Index in schedule order
            vm.SelectedScheduleName = "Arch Index";
            Assert.IsNotNull(vm.SelectedSchedule);
            Assert.AreEqual("sch-1", vm.SelectedSchedule!.UniqueId);
            CollectionAssert.AreEqual(new[] { "A102", "A101" }, vm.GetSelectedSheets().Select(s => s.SheetNumber).ToArray());
            Assert.IsTrue(vm.PreserveSheetOrder);

            // Null clears SelectedSchedule safely
            vm.SelectedScheduleName = null;
            Assert.IsNull(vm.SelectedSchedule);
        }

        [Test]
        public void SourceModeSelection_FiltersSheetSelection_WhenPrintSetOrScheduleSelected()
        {
            FakeSheetIndexAdapter adapter = CreateSampleAdapter();
            adapter.PrintSets.Add(new SheetIndexPrintSetModel("ps-1", "Arch Set", new[] { "s-101", "s-102" }));

            var vm = new ExportSheetIndexViewModel(adapter.LoadProjectSnapshot());

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
        public void EmptySchedules_AreExcludedFromSnapshotMapsAndAvailableSchedules()
        {
            FakeSheetIndexAdapter adapter = CreateSampleAdapter();
            adapter.Schedules.Add(new SheetIndexScheduleModel("sch-empty", "Empty Sheet List", Array.Empty<SheetIndexSheetModel>()));
            adapter.Schedules.Add(new SheetIndexScheduleModel("sch-valid", "Populated Sheet List", new[]
            {
                new SheetIndexSheetModel("s-101", "A101", "Ground Floor Plan")
            }));

            SheetIndexSnapshot snapshot = adapter.LoadProjectSnapshot();
            Assert.AreEqual(1, snapshot.Schedules.Count);
            Assert.AreEqual("Populated Sheet List", snapshot.Schedules[0].Name);
            Assert.IsFalse(snapshot.ScheduleSheetMap.ContainsKey("Empty Sheet List"));
            CollectionAssert.AreEqual(new[] { "Populated Sheet List" }, snapshot.AvailableSchedules.ToArray());

            var vm = new ExportSheetIndexViewModel(snapshot);
            CollectionAssert.AreEqual(new[] { "Populated Sheet List" }, vm.AvailableSchedules.ToArray());
            Assert.IsFalse(vm.ScheduleSheetMap.ContainsKey("Empty Sheet List"));
            Assert.IsFalse(vm.Schedules.Any(s => string.Equals(s.Name, "Empty Sheet List", StringComparison.OrdinalIgnoreCase)));
            Assert.AreEqual("Populated Sheet List", vm.SelectedScheduleName);
        }

        [Test]
        public void DuplicatePrintSetAndScheduleNames_AreGuardedSafely()
        {
            FakeSheetIndexAdapter adapter = CreateSampleAdapter();
            adapter.PrintSets.Add(new SheetIndexPrintSetModel("ps-1", "Permit Set", new[] { "s-101" }));
            adapter.PrintSets.Add(new SheetIndexPrintSetModel("ps-2", "permit set", new[] { "s-201" }));

            // Empty schedule with duplicate name placed before populated schedule should not block the populated one
            adapter.Schedules.Add(new SheetIndexScheduleModel("sch-empty-dup", "Drawing Index", Array.Empty<SheetIndexSheetModel>()));
            adapter.Schedules.Add(new SheetIndexScheduleModel("sch-1", "Drawing Index", new[]
            {
                new SheetIndexSheetModel("s-101", "A101", "Ground Floor Plan")
            }));
            adapter.Schedules.Add(new SheetIndexScheduleModel("sch-2", "drawing index", new[]
            {
                new SheetIndexSheetModel("s-301", "M301", "Mechanical HVAC Plan")
            }));

            SheetIndexSnapshot snapshot = adapter.LoadProjectSnapshot();
            var vm = new ExportSheetIndexViewModel(snapshot);

            Assert.AreEqual(1, snapshot.PrintSets.Count);
            Assert.AreEqual("ps-1", snapshot.PrintSets[0].UniqueId);
            Assert.AreEqual(1, vm.AvailablePrintSets.Count);
            Assert.AreEqual(1, vm.PrintSetSheetMap.Count);
            CollectionAssert.AreEqual(new[] { "s-101" }, vm.PrintSetSheetMap["Permit Set"]);

            Assert.AreEqual(1, snapshot.Schedules.Count);
            Assert.AreEqual("sch-1", snapshot.Schedules[0].UniqueId);
            Assert.AreEqual(1, vm.AvailableSchedules.Count);
            Assert.AreEqual(1, vm.ScheduleSheetMap.Count);
            CollectionAssert.AreEqual(new[] { "s-101" }, vm.ScheduleSheetMap["Drawing Index"]);
        }

        [Test]
        public void SessionPersistence_NewlyAddedSheetsDefaultToSelected()
        {
            FakeSheetIndexAdapter adapter = CreateSampleAdapter();
            var sessionState = new SheetIndexSessionState();

            var vm1 = new ExportSheetIndexViewModel(adapter.LoadProjectSnapshot(), sessionState);
            vm1.ExportCommand.Execute(null);

            // Add a new sheet created later in session
            adapter.Sheets.Add(new SheetIndexSheetModel("s-999", "A999", "New Future Sheet"));

            var vm2 = new ExportSheetIndexViewModel(adapter.LoadProjectSnapshot(), sessionState);
            var selected2 = vm2.GetSelectedSheets();

            Assert.IsTrue(selected2.Any(s => s.UniqueId == "s-999"));
        }

        [Test]
        public void FilterSheetsByPrintSet_FiltersAndAssignsPrintOrderSequence()
        {
            var revisions = new List<SheetIndexRevisionModel>
            {
                new SheetIndexRevisionModel("rev-1", "Issue 1", "2026-01-01", 1)
            };

            var allSheets = new List<SheetIndexSheetModel>
            {
                new SheetIndexSheetModel("s-1", "A101", "Floor Plan"),
                new SheetIndexSheetModel("s-2", "A102", "Reflected Ceiling Plan"),
                new SheetIndexSheetModel("s-3", "A103", "Roof Plan"),
                new SheetIndexSheetModel("s-4", "A104", "Building Sections")
            };

            // Print set containing s-3 (index 0) and s-1 (index 1)
            var printSets = new List<SheetIndexPrintSetModel>
            {
                new SheetIndexPrintSetModel("ps-1", "Permit Submittal", new[] { "s-3", "s-1" })
            };

            var snapshot = new SheetIndexSnapshot(allSheets, revisions, printSets);
            var viewModel = new ExportSheetIndexViewModel(snapshot);

            viewModel.SelectedSourceType = SheetSelectionSourceType.PrintSet;

            Assert.IsTrue(viewModel.IsPrintSetSourceSelected);
            Assert.AreEqual(2, viewModel.Sheets.Count);
            Assert.AreEqual("A103", viewModel.Sheets[0].SheetNumber);
            Assert.AreEqual(0, viewModel.Sheets[0].Model.PrintOrderIndex);
            Assert.AreEqual("A101", viewModel.Sheets[1].SheetNumber);
            Assert.AreEqual(1, viewModel.Sheets[1].Model.PrintOrderIndex);

            viewModel.SelectedSourceType = SheetSelectionSourceType.AllSheets;

            Assert.IsFalse(viewModel.IsPrintSetSourceSelected);
            Assert.AreEqual(4, viewModel.Sheets.Count);
            Assert.IsNull(viewModel.Sheets[0].Model.PrintOrderIndex);
        }
    }
}
