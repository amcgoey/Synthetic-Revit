using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Synthetic.Modules.ShadowStudy.Models;
using Synthetic.Modules.ShadowStudy.ViewModels;
using Synthetic.Shared.UI;

namespace SyntheticTests.Modules.ShadowStudy
{
    [TestFixture]
    public class ShadowStudyPrintSetTests
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
        public void PrintSets_PopulatesInSessionFirstFollowedByDocumentSets_AndSelectingChecksOnlyShadowViews()
        {
            var adapter = CreateAdapterWithViewsAndMixedSets();
            var vm = new ShadowStudyViewModel(
                adapter,
                new FakeFolderDialogService(),
                new DateTime(2026, 6, 21),
                textInputPromptService: new FakeTextInputPromptService());

            Assert.GreaterOrEqual(vm.PrintSets.Count, 3);
            Assert.IsTrue(vm.PrintSets[0].IsInSession);
            Assert.AreEqual(ShadowStudyPrintSetItem.InSessionName, vm.PrintSets[0].Name);
            Assert.AreSame(vm.PrintSets[0], vm.SelectedPrintSet);

            // Sorted case-insensitively after <In-Session>
            Assert.AreEqual("Alpha Set", vm.PrintSets[1].Name);
            Assert.AreEqual("Mixed Set", vm.PrintSets[2].Name);

            // Select Mixed Set: checks v1 and v3, ignores non-shadow sheet/schedule members
            vm.ExportRangeMode = ShadowStudyExportRangeMode.SelectedViews;
            vm.SelectedPrintSet = vm.PrintSets[2];

            Assert.AreEqual(2, vm.SelectedViewsCount);
            Assert.IsTrue(vm.AllViews.First(v => v.ViewId == "v1").IsSelected);
            Assert.IsFalse(vm.AllViews.First(v => v.ViewId == "v2").IsSelected);
            Assert.IsTrue(vm.AllViews.First(v => v.ViewId == "v3").IsSelected);
            Assert.IsTrue(vm.HasPrintSetStatus);
            StringAssert.Contains("2", vm.PrintSetStatusMessage);
            Assert.AreEqual(0, adapter.SavePrintSetCalls.Count);

            // Switching to Alpha Set unchecks v3 and checks v2
            vm.SelectedPrintSet = vm.PrintSets[1];
            Assert.AreEqual(1, vm.SelectedViewsCount);
            Assert.IsFalse(vm.AllViews.First(v => v.ViewId == "v1").IsSelected);
            Assert.IsTrue(vm.AllViews.First(v => v.ViewId == "v2").IsSelected);
            Assert.IsFalse(vm.AllViews.First(v => v.ViewId == "v3").IsSelected);
            Assert.AreEqual(0, adapter.SavePrintSetCalls.Count);
        }

        [Test]
        public void Save_AfterModifyingLoadedMixedPrintSet_UpdatesExistingSetAndPreservesNonShadowMembers()
        {
            var adapter = CreateAdapterWithViewsAndMixedSets();
            var promptService = new FakeTextInputPromptService();
            var vm = new ShadowStudyViewModel(
                adapter,
                new FakeFolderDialogService(),
                new DateTime(2026, 6, 21),
                textInputPromptService: promptService);

            vm.ExportRangeMode = ShadowStudyExportRangeMode.SelectedViews;
            vm.SelectedPrintSet = vm.PrintSets.First(p => p.Name == "Mixed Set");

            // Modify checked views: uncheck v1, check v2 (keep v3)
            vm.AllViews.First(v => v.ViewId == "v1").IsSelected = false;
            vm.AllViews.First(v => v.ViewId == "v2").IsSelected = true;

            Assert.IsTrue(vm.IsPrintSetModified);
            Assert.IsFalse(vm.SelectedPrintSet.IsInSession);
            Assert.AreEqual("ps-mixed", vm.SelectedPrintSet.Id);

            vm.SavePrintSetCommand.Execute(null);

            Assert.AreEqual(0, promptService.PromptCalls.Count, "Save on an existing saved Print Set must not prompt for a name.");
            Assert.AreEqual(1, adapter.SavePrintSetCalls.Count);

            var call = adapter.SavePrintSetCalls[0];
            Assert.AreEqual("Mixed Set", call.Name);
            Assert.IsFalse(call.SaveAsNew);
            Assert.AreEqual("ps-mixed", call.SourcePrintSetId);
            CollectionAssert.AreEquivalent(new[] { "v2", "v3" }, call.ShadowViewIds);

            // Saved set in adapter retains its non-shadow members
            ShadowStudyPrintSetItem updated = adapter.PrintSets.First(p => p.Id == "ps-mixed");
            CollectionAssert.AreEquivalent(new[] { "sheet-1", "sched-1" }, updated.NonShadowMemberIds);
            CollectionAssert.AreEquivalent(new[] { "v2", "v3" }, updated.ShadowViewIds);
            Assert.IsFalse(vm.IsPrintSetModified);
            Assert.IsFalse(vm.HasPrintSetError);
        }

        [Test]
        public void Save_WhenInSessionSelected_RoutesThroughSaveAsPromptAndCreatesNewSet()
        {
            var adapter = CreateAdapterWithViewsAndMixedSets();
            var promptService = new FakeTextInputPromptService { NextResult = "Summer Views" };
            var vm = new ShadowStudyViewModel(
                adapter,
                new FakeFolderDialogService(),
                new DateTime(2026, 6, 21),
                textInputPromptService: promptService);

            vm.ExportRangeMode = ShadowStudyExportRangeMode.SelectedViews;
            Assert.IsTrue(vm.SelectedPrintSet.IsInSession);

            vm.AllViews.First(v => v.ViewId == "v1").IsSelected = true;
            vm.AllViews.First(v => v.ViewId == "v3").IsSelected = true;

            vm.SavePrintSetCommand.Execute(null);

            Assert.AreEqual(1, promptService.PromptCalls.Count, "Save while <In-Session> is selected must route through Save As... prompt.");
            Assert.AreEqual(1, adapter.SavePrintSetCalls.Count);
            Assert.AreEqual("Summer Views", adapter.SavePrintSetCalls[0].Name);
            Assert.IsTrue(adapter.SavePrintSetCalls[0].SaveAsNew);
            Assert.IsNull(adapter.SavePrintSetCalls[0].SourcePrintSetId);
            CollectionAssert.AreEquivalent(new[] { "v1", "v3" }, adapter.SavePrintSetCalls[0].ShadowViewIds);

            Assert.AreEqual("Summer Views", vm.SelectedPrintSet.Name);
            Assert.IsFalse(vm.SelectedPrintSet.IsInSession);
        }

        [Test]
        public void SaveAs_FromLoadedMixedPrintSet_PreservesSourceNonShadowMembersInNewSet()
        {
            var adapter = CreateAdapterWithViewsAndMixedSets();
            var promptService = new FakeTextInputPromptService { NextResult = "Mixed Set Fork" };
            var vm = new ShadowStudyViewModel(
                adapter,
                new FakeFolderDialogService(),
                new DateTime(2026, 6, 21),
                textInputPromptService: promptService);

            vm.ExportRangeMode = ShadowStudyExportRangeMode.SelectedViews;
            vm.SelectedPrintSet = vm.PrintSets.First(p => p.Name == "Mixed Set");

            // Swap v3 out for v2
            vm.AllViews.First(v => v.ViewId == "v3").IsSelected = false;
            vm.AllViews.First(v => v.ViewId == "v2").IsSelected = true;

            vm.SaveAsPrintSetCommand.Execute(null);

            Assert.AreEqual(1, promptService.PromptCalls.Count);
            Assert.AreEqual(1, adapter.SavePrintSetCalls.Count);

            var call = adapter.SavePrintSetCalls[0];
            Assert.AreEqual("Mixed Set Fork", call.Name);
            Assert.IsTrue(call.SaveAsNew);
            Assert.AreEqual("ps-mixed", call.SourcePrintSetId);
            CollectionAssert.AreEquivalent(new[] { "v1", "v2" }, call.ShadowViewIds);

            ShadowStudyPrintSetItem forked = adapter.PrintSets.First(p => p.Name == "Mixed Set Fork");
            CollectionAssert.AreEquivalent(new[] { "sheet-1", "sched-1" }, forked.NonShadowMemberIds);
            Assert.AreEqual("Mixed Set Fork", vm.SelectedPrintSet.Name);
        }

        [Test]
        public void SaveAs_GuardsBlankInSessionAndDuplicateNames_AndCancelledPromptMakesNoChanges()
        {
            var adapter = CreateAdapterWithViewsAndMixedSets();
            var promptService = new FakeTextInputPromptService();
            var vm = new ShadowStudyViewModel(
                adapter,
                new FakeFolderDialogService(),
                new DateTime(2026, 6, 21),
                textInputPromptService: promptService);

            vm.ExportRangeMode = ShadowStudyExportRangeMode.SelectedViews;
            vm.AllViews.First(v => v.ViewId == "v1").IsSelected = true;

            // Cancelled prompt -> no adapter calls, no error
            promptService.NextResult = null;
            vm.SaveAsPrintSetCommand.Execute(null);
            Assert.AreEqual(0, adapter.SavePrintSetCalls.Count);
            Assert.IsFalse(vm.HasPrintSetError);

            // Validator passed to prompt rejects blank, <In-Session>, and case-insensitive duplicates
            var validator = promptService.LastValidator;
            Assert.IsNotNull(validator);
            Assert.IsNotNull(validator!(string.Empty));
            Assert.IsNotNull(validator("   "));
            Assert.IsNotNull(validator(ShadowStudyPrintSetItem.InSessionName));
            Assert.IsNotNull(validator("<in-session>"));
            Assert.IsNotNull(validator("alpha set"));
            Assert.IsNull(validator("Brand New Print Set"));

            // Defense in depth if prompt returns an invalid/duplicate string anyway
            promptService.NextResult = "  ALPHA SET  ";
            vm.SaveAsPrintSetCommand.Execute(null);
            Assert.AreEqual(0, adapter.SavePrintSetCalls.Count);
            Assert.IsTrue(vm.HasPrintSetError);
            StringAssert.Contains("already exists", vm.PrintSetErrorMessage);

            promptService.NextResult = "   ";
            vm.SaveAsPrintSetCommand.Execute(null);
            Assert.AreEqual(0, adapter.SavePrintSetCalls.Count);
            Assert.IsTrue(vm.HasPrintSetError);
        }

        [Test]
        public void Export_WithoutClickingSaveOrSaveAs_NeverCallsSavePrintSet()
        {
            var adapter = CreateAdapterWithViewsAndMixedSets();
            var vm = new ShadowStudyViewModel(
                adapter,
                new FakeFolderDialogService(),
                new DateTime(2026, 6, 21),
                textInputPromptService: new FakeTextInputPromptService())
            {
                OutputFolder = @"C:\Exports\ShadowStudy"
            };

            vm.ExportRangeMode = ShadowStudyExportRangeMode.SelectedViews;
            vm.SelectedPrintSet = vm.PrintSets.First(p => p.Name == "Mixed Set");
            vm.AllViews.First(v => v.ViewId == "v2").IsSelected = true;

            bool? closedWith = null;
            vm.RequestClose += r => closedWith = r;
            vm.ExportCommand.Execute(null);

            Assert.AreEqual(true, closedWith);
            Assert.AreEqual(0, adapter.SavePrintSetCalls.Count);
        }

        [Test]
        public void SaveAndSaveAs_WhenSelectionHasZeroTotalMembers_GuardsWithoutCallingAdapter()
        {
            var adapter = CreateAdapterWithViewsAndMixedSets();
            var promptService = new FakeTextInputPromptService { NextResult = "Empty Set" };
            var vm = new ShadowStudyViewModel(
                adapter,
                new FakeFolderDialogService(),
                new DateTime(2026, 6, 21),
                textInputPromptService: promptService);

            vm.ExportRangeMode = ShadowStudyExportRangeMode.SelectedViews;
            Assert.AreEqual(0, vm.SelectedViewsCount);
            Assert.IsFalse(vm.CanSavePrintSet);

            vm.SaveAsPrintSetCommand.Execute(null);
            Assert.AreEqual(0, adapter.SavePrintSetCalls.Count);
            Assert.IsTrue(vm.HasPrintSetError);
        }

        private static FakeShadowStudyAdapter CreateAdapterWithViewsAndMixedSets()
        {
            var adapter = new FakeShadowStudyAdapter();
            var v1 = new ShadowStudyViewItem { Id = "v1", Name = "Level 1", ViewType = "FloorPlan", IsCropped = true };
            var v2 = new ShadowStudyViewItem { Id = "v2", Name = "Level 2", ViewType = "FloorPlan", IsCropped = true };
            var v3 = new ShadowStudyViewItem { Id = "v3", Name = "Exterior 3D", ViewType = "ThreeD", IsCropped = true };
            adapter.ActiveView = v1;
            adapter.EligibleViews.AddRange(new[] { v1, v2, v3 });

            adapter.PrintSets.Add(new ShadowStudyPrintSetItem
            {
                Id = "ps-mixed",
                Name = "Mixed Set",
                ShadowViewIds = new[] { "v1", "v3" },
                NonShadowMemberIds = new[] { "sheet-1", "sched-1" }
            });
            adapter.PrintSets.Add(new ShadowStudyPrintSetItem
            {
                Id = "ps-alpha",
                Name = "Alpha Set",
                ShadowViewIds = new[] { "v2" },
                NonShadowMemberIds = Array.Empty<string>()
            });

            return adapter;
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
