using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Autodesk.Revit.UI;
using WpfButton = System.Windows.Controls.Button;
using WpfCheckBox = System.Windows.Controls.CheckBox;
using WpfComboBox = System.Windows.Controls.ComboBox;
using WpfTextBox = System.Windows.Controls.TextBox;
using WpfToggleButton = System.Windows.Controls.Primitives.ToggleButton;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Synthetic.Features.MultiModelPdfExport.Commands;
using Synthetic.Features.MultiModelPdfExport.Models;
using Synthetic.Features.MultiModelPdfExport.Services;
using Synthetic.Features.MultiModelPdfExport.ViewModels;
using Synthetic.Features.MultiModelPdfExport.Views;
using Synthetic.Platform.Host;
using Synthetic.Platform.UI;
using SyntheticTests.Fixtures;
using SyntheticTests.Helpers;

namespace SyntheticTests.Features.MultiModelPdfExport
{
    /// <summary>
    /// Non-executing live Revit STA smoke tests verifying <see cref="CmdMultiModelPdfExport"/> command,
    /// ribbon entry, Variant A WPF view/ViewModel bindings, and document-leak safety without mutating
    /// Revit documents or opening production modal loops.
    /// </summary>
    [TestFixture]
    [RevitSmokeTest]
    public class MultiModelPdfExportSmokeRevitTests
    {
        private UIApplication? _uiapp;
        private int _initialDocCount;
        private bool _previousSuppressUi;

        [OneTimeSetUp]
        public void Setup(UIApplication uiapp)
        {
            _uiapp = uiapp;
            HostSettingsRegistrar.EnsureRegistered();
            TestWorkerJournalMarker.EnsureWritten(uiapp);
            _initialDocCount = uiapp.Application.Documents.Size;
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            if (_uiapp?.Application != null)
            {
                Assert.AreEqual(
                    _initialDocCount,
                    _uiapp.Application.Documents.Size,
                    "MultiModelPdfExportSmokeRevitTests leaked open Revit document(s).");
            }
        }

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
        public void RibbonCommand_CmdMultiModelPdfExport_ResolvesInAddinAssemblyAndRibbonConfig()
        {
            Type cmdType = typeof(CmdMultiModelPdfExport);
            Assert.IsTrue(typeof(IExternalCommand).IsAssignableFrom(cmdType));
            Assert.IsFalse(cmdType.IsAbstract);
            Assert.AreEqual("Synthetic.Features.MultiModelPdfExport.Commands.CmdMultiModelPdfExport", cmdType.FullName);

            Type? resolvedFromAddin = typeof(App).Assembly.GetType(cmdType.FullName!, throwOnError: false);
            Assert.AreSame(cmdType, resolvedFromAddin, "Expected CmdMultiModelPdfExport to resolve from the active Addin payload assembly.");

            object? instance = Activator.CreateInstance(cmdType);
            Assert.IsInstanceOf<CmdMultiModelPdfExport>(instance);

            string configPath = Path.Combine(TestPathHelper.GetProjectRoot(), "src", "Assets", "ribbon_config.json");
            Assert.IsTrue(File.Exists(configPath), $"Expected ribbon_config.json at '{configPath}'.");

            JObject config = JObject.Parse(File.ReadAllText(configPath));
            var registeredClasses = new HashSet<string>(
                config.SelectTokens("$..class")
                    .Select(t => t.Value<string>() ?? string.Empty),
                StringComparer.Ordinal);

            Assert.IsTrue(registeredClasses.Contains(cmdType.FullName!), $"Expected '{cmdType.FullName}' in ribbon_config.json.");
        }

        [Test]
        public void CmdMultiModelPdfExport_SuppressUi_BypassesUserPrefsStoreAndDiskIo()
        {
            string defaultPath = MultiModelPdfExportUserPrefsStore.DefaultFilePath;
            bool existedBefore = File.Exists(defaultPath);
            DateTime writeTimeBefore = existedBefore ? File.GetLastWriteTimeUtc(defaultPath) : DateTime.MinValue;
            byte[]? bytesBefore = existedBefore ? File.ReadAllBytes(defaultPath) : null;

            bool previousSuppressUi = ProgressCoordinator.SuppressUI;
            try
            {
                ProgressCoordinator.SuppressUI = true;
                MultiModelPdfExportUserPrefsStore? suppressedStore = CmdMultiModelPdfExport.ResolveUserPrefsStore();
                Assert.IsNull(suppressedStore, "CmdMultiModelPdfExport must bypass Local User Preferences when ProgressCoordinator.SuppressUI is true.");

                var adapter = new FakeMultiModelPdfExportAdapter();
                var vm = new MultiModelPdfExportViewModel(
                    adapter,
                    new FakeFolderDialogService(),
                    userPrefsStore: suppressedStore);
                vm.OutputFolder = @"C:\PDFExports\SuppressUiSmoke";
                vm.Rows[0].SelectedPrintSetName = "CD Set";
                Assert.IsTrue(vm.CanExport);

                bool? closedWith = null;
                vm.RequestClose += result => closedWith = result;
                vm.ExportCommand.Execute(null);
                Assert.AreEqual(true, closedWith);

                Assert.AreEqual(existedBefore, File.Exists(defaultPath), "SuppressUI null store must never create default prefs file.");
                if (existedBefore)
                {
                    Assert.AreEqual(writeTimeBefore, File.GetLastWriteTimeUtc(defaultPath));
                    CollectionAssert.AreEqual(bytesBefore!, File.ReadAllBytes(defaultPath));
                }

                ProgressCoordinator.SuppressUI = false;
                MultiModelPdfExportUserPrefsStore? activeStore = CmdMultiModelPdfExport.ResolveUserPrefsStore();
                Assert.IsNotNull(activeStore, "CmdMultiModelPdfExport must construct MultiModelPdfExportUserPrefsStore when SuppressUI is false.");
                Assert.AreEqual(defaultPath, activeStore!.FilePath);
            }
            finally
            {
                ProgressCoordinator.SuppressUI = previousSuppressUi;
            }
        }

        [Test]
        public void MultiModelPdfExportWindow_ConstructShowAndBind_WithoutBindingOrResourceErrors()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            IntPtr ownerHandle = _uiapp!.MainWindowHandle;

            using (var harness = new WpfBindingSmokeHarness())
            {
                // 1. Valid populated state (active model + Print Sets + 10 skipped items -> "+ 2 more" tooltip + mode toggle)
                var tenSkipped = new List<MultiModelPdfSkippedViewInfo>
                {
                    new MultiModelPdfSkippedViewInfo("A900", "UNPLACED SIGNAGE", "Placeholder sheet"),
                    new MultiModelPdfSkippedViewInfo("FloorPlan", "Working Template", "View template"),
                    new MultiModelPdfSkippedViewInfo("Schedule", "Door Schedule", "Schedule — not printable")
                };
                for (int i = 4; i <= 10; i++)
                {
                    tenSkipped.Add(new MultiModelPdfSkippedViewInfo($"A90{i}", $"PLACEHOLDER {i}", "Placeholder sheet"));
                }

                var populatedSnapshot = new MultiModelPdfSessionSnapshot(
                    "HostModel.rvt",
                    new[]
                    {
                        new MultiModelPdfDocumentSnapshot(
                            "active-host",
                            "HostModel.rvt",
                            @"C:\BIM\HostModel.rvt",
                            MultiModelPdfDocumentOrigin.Active,
                            MultiModelPdfStorageKind.Workshared,
                            printSets: new[]
                            {
                                new MultiModelPdfSetSnapshot("CD Set", 3),
                                new MultiModelPdfSetSnapshot("Permit Set", 2, tenSkipped)
                            })
                    },
                    new[] { MultiModelPdfSetupOption.CreateDefault() });

                var populatedAdapter = new FakeMultiModelPdfExportAdapter(populatedSnapshot);
                var populatedVm = new MultiModelPdfExportViewModel(
                    populatedAdapter,
                    new FakeFolderDialogService(),
                    @"C:\PDFExports\Demo");
                MultiModelPdfExportRowViewModel activeRow = populatedVm.Rows[0];
                activeRow.SelectedPrintSetName = "Permit Set";
                Assert.IsTrue(populatedVm.CanExport);
                Assert.AreEqual("Export 1 PDF", populatedVm.ExportButtonText);

                var populatedWindow = new MultiModelPdfExportWindow(ownerHandle) { DataContext = populatedVm };
                try
                {
                    populatedWindow.Show();
                    populatedWindow.UpdateLayout();
                    Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.Loaded, new Action(() => { }));
                    Assert.IsTrue(populatedWindow.IsVisible, "MultiModelPdfExportWindow should show cleanly.");

                    // Verify Combined vs Separate RadioButtons
                    List<RadioButton> radios = FindVisualChildren<RadioButton>(populatedWindow).ToList();
                    RadioButton? combinedRadio = radios.FirstOrDefault(r => Equals(r.Content, "Combined PDF per document"));
                    RadioButton? separateRadio = radios.FirstOrDefault(r => Equals(r.Content, "Separate PDFs per view/sheet"));
                    Assert.IsNotNull(combinedRadio, "Expected 'Combined PDF per document' RadioButton in Variant A UI.");
                    Assert.IsNotNull(separateRadio, "Expected 'Separate PDFs per view/sheet' RadioButton in Variant A UI.");
                    Assert.IsTrue(combinedRadio!.IsChecked);
                    Assert.IsFalse(separateRadio!.IsChecked);

                    // Verify inline Print Set ComboBox dropdown labels
                    List<WpfComboBox> comboBoxes = FindVisualChildren<WpfComboBox>(populatedWindow).ToList();
                    WpfComboBox? rowSetCombo = comboBoxes.FirstOrDefault(cb => cb.ItemsSource is IEnumerable<MultiModelPdfPrintSetChoice>);
                    Assert.IsNotNull(rowSetCombo, "Expected inline row Print Set ComboBox.");
                    var dropdownLabels = rowSetCombo!.Items.Cast<MultiModelPdfPrintSetChoice>().Select(c => c.DisplayName).ToList();
                    CollectionAssert.Contains(dropdownLabels, "Permit Set (2 views · 10 skip)");

                    // Verify Sand Gold "10 skip" badge and 8-item "+ 2 more" hover tooltip
                    TextBlock? skipBadgeText = FindVisualChildren<TextBlock>(populatedWindow)
                        .FirstOrDefault(tb => string.Equals(tb.Text, "10 skip", StringComparison.Ordinal));
                    Assert.IsNotNull(skipBadgeText, "Expected '10 skip' badge TextBlock in Views column.");
                    Assert.AreEqual(Visibility.Visible, skipBadgeText!.Visibility);

                    var skipContainer = VisualTreeHelper.GetParent(skipBadgeText) as FrameworkElement;
                    Assert.IsNotNull(skipContainer);
                    Assert.AreEqual(Visibility.Visible, skipContainer!.Visibility);
                    string? tooltip = skipContainer.ToolTip as string;
                    Assert.IsFalse(string.IsNullOrWhiteSpace(tooltip), "Expected hover tooltip on skip badge container.");
                    StringAssert.Contains("• A900 UNPLACED SIGNAGE (Placeholder sheet)", tooltip!);
                    StringAssert.Contains("• Schedule Door Schedule (Schedule — not printable)", tooltip!);
                    StringAssert.Contains("+ 2 more", tooltip!);

                    // Toggle Separate mode through UI RadioButton two-way binding
                    separateRadio.IsChecked = true;
                    populatedWindow.UpdateLayout();
                    Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.Loaded, new Action(() => { }));
                    Assert.AreEqual(MultiModelPdfFileMode.SeparatePerViewSheet, populatedVm.FileMode);
                    Assert.IsFalse(combinedRadio.IsChecked);
                    Assert.AreEqual("Export 2 PDFs", populatedVm.ExportButtonText);
                    StringAssert.Contains(@"<NamingRule>.pdf (2 PDFs in 1 folder)", populatedVm.OutputPreviewText);
                }
                finally
                {
                    populatedWindow.DataContext = null;
                    populatedWindow.Close();
                }

                // 2. Multi-tab federated origin badges (ACTIVE + LINKED + Unloaded LINKED + OPEN) and muted collision cue/tooltip
                var multiTabSnapshot = new MultiModelPdfSessionSnapshot(
                    "Tower.rvt",
                    new[]
                    {
                        new MultiModelPdfDocumentSnapshot(
                            "active-tower",
                            "Tower.rvt",
                            @"C:\BIM\Active\Tower.rvt",
                            MultiModelPdfDocumentOrigin.Active,
                            MultiModelPdfStorageKind.Workshared,
                            printSets: new[] { new MultiModelPdfSetSnapshot("CD Set", 2) }),
                        new MultiModelPdfDocumentSnapshot(
                            "linked-tower",
                            "Tower.rvt",
                            @"C:\BIM\Links\Tower.rvt",
                            MultiModelPdfDocumentOrigin.Linked,
                            MultiModelPdfStorageKind.Workshared,
                            isUnloadedLink: false,
                            printSets: new[] { new MultiModelPdfSetSnapshot("CD Set", 4) }),
                        new MultiModelPdfDocumentSnapshot(
                            "unloaded-link",
                            "SiteUnloaded.rvt",
                            @"C:\BIM\Links\SiteUnloaded.rvt",
                            MultiModelPdfDocumentOrigin.Linked,
                            MultiModelPdfStorageKind.Local,
                            isUnloadedLink: true,
                            printSets: Array.Empty<MultiModelPdfSetSnapshot>()),
                        new MultiModelPdfDocumentSnapshot(
                            "open-tower",
                            "Tower.rvt",
                            @"C:\BIM\Open\Tower.rvt",
                            MultiModelPdfDocumentOrigin.OpenStandalone,
                            MultiModelPdfStorageKind.Local,
                            printSets: new[] { new MultiModelPdfSetSnapshot("CD Set", 3) })
                    },
                    new[] { MultiModelPdfSetupOption.CreateDefault() });

                var multiTabVm = new MultiModelPdfExportViewModel(
                    new FakeMultiModelPdfExportAdapter(multiTabSnapshot),
                    new FakeFolderDialogService(),
                    @"C:\PDFExports\Demo");
                multiTabVm.Rows[0].SelectedPrintSetName = "CD Set";
                multiTabVm.Rows[1].SelectedPrintSetName = "CD Set";
                multiTabVm.Rows[3].SelectedPrintSetName = "CD Set";
                Assert.IsTrue(multiTabVm.Rows[1].IsChecked, "Eligible loaded Linked row must start checked.");
                Assert.IsFalse(multiTabVm.Rows[2].IsChecked, "Unloaded Linked row must start unchecked.");
                Assert.IsFalse(multiTabVm.Rows[2].IsEligible, "Unloaded Linked row must be ineligible.");
                Assert.IsFalse(multiTabVm.Rows[3].IsChecked, "OpenStandalone row must start unchecked.");

                var multiTabWindow = new MultiModelPdfExportWindow(ownerHandle) { DataContext = multiTabVm };
                try
                {
                    multiTabWindow.Show();
                    multiTabWindow.UpdateLayout();
                    Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.Loaded, new Action(() => { }));

                    List<TextBlock> textBlocks = FindVisualChildren<TextBlock>(multiTabWindow).ToList();
                    Assert.IsTrue(textBlocks.Any(tb => string.Equals(tb.Text, "ACTIVE", StringComparison.Ordinal)), "Expected ACTIVE origin badge.");
                    Assert.IsTrue(textBlocks.Any(tb => string.Equals(tb.Text, "LINKED", StringComparison.Ordinal)), "Expected LINKED origin badge.");
                    Assert.IsTrue(textBlocks.Any(tb => string.Equals(tb.Text, "OPEN", StringComparison.Ordinal)), "Expected OPEN origin badge.");

                    TextBlock? unloadedStatus = textBlocks.FirstOrDefault(tb =>
                        string.Equals(tb.Text, "Unloaded link — load in host to inspect", StringComparison.Ordinal));
                    Assert.IsNotNull(unloadedStatus, "Expected unloaded link status TextBlock.");
                    Assert.AreEqual(Visibility.Visible, unloadedStatus!.Visibility);

                    Border? unloadedRowBorder = FindVisualChildren<Border>(multiTabWindow)
                        .FirstOrDefault(b => ReferenceEquals(b.DataContext, multiTabVm.Rows[2]));
                    Assert.IsNotNull(unloadedRowBorder, "Expected Border container for unloaded link row.");
                    Assert.AreEqual(0.25, unloadedRowBorder!.Opacity, 1e-6, "Unloaded link row must render at 25% opacity.");

                    CheckBox? unloadedCheckBox = FindVisualChildren<CheckBox>(unloadedRowBorder).FirstOrDefault();
                    Assert.IsNotNull(unloadedCheckBox);
                    Assert.IsFalse(unloadedCheckBox!.IsEnabled, "Unloaded link row CheckBox must be disabled.");

                    TextBlock? linkCue = textBlocks.FirstOrDefault(tb => string.Equals(tb.Text, "→ Tower (Link).pdf", StringComparison.Ordinal));
                    Assert.IsNotNull(linkCue, "Expected '→ Tower (Link).pdf' collision cue for colliding Linked row.");
                    Assert.AreEqual(Visibility.Visible, linkCue!.Visibility);

                    TextBlock? openCueBefore = textBlocks.FirstOrDefault(tb => string.Equals(tb.Text, "→ Tower_2.pdf", StringComparison.Ordinal));
                    Assert.IsTrue(openCueBefore == null || openCueBefore.Visibility != Visibility.Visible, "Unchecked OpenStandalone row must not show collision cue.");

                    // Check colliding OpenStandalone row -> muted "→ Tower_2.pdf" cue + tooltip become Visible
                    multiTabVm.Rows[3].IsChecked = true;
                    multiTabWindow.UpdateLayout();
                    Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.Loaded, new Action(() => { }));

                    TextBlock? combinedCue = FindVisualChildren<TextBlock>(multiTabWindow)
                        .FirstOrDefault(tb => string.Equals(tb.Text, "→ Tower_2.pdf", StringComparison.Ordinal));
                    Assert.IsNotNull(combinedCue, "Expected '→ Tower_2.pdf' collision cue TextBlock.");
                    Assert.AreEqual(Visibility.Visible, combinedCue!.Visibility);
                    StringAssert.Contains("Tower_2.pdf", combinedCue.ToolTip as string ?? string.Empty);

                    // Switch to Separate mode -> cue updates to "→ Tower_2\"
                    multiTabVm.FileMode = MultiModelPdfFileMode.SeparatePerViewSheet;
                    multiTabWindow.UpdateLayout();
                    Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.Loaded, new Action(() => { }));

                    TextBlock? separateCue = FindVisualChildren<TextBlock>(multiTabWindow)
                        .FirstOrDefault(tb => string.Equals(tb.Text, @"→ Tower_2\", StringComparison.Ordinal));
                    Assert.IsNotNull(separateCue, "Expected '→ Tower_2\\' collision cue in Separate mode.");
                    Assert.AreEqual(Visibility.Visible, separateCue!.Visibility);
                }
                finally
                {
                    multiTabWindow.DataContext = null;
                    multiTabWindow.Close();
                }

                // 3. Disabled "No saved Print Sets" row state
                var noSetsAdapter = new FakeMultiModelPdfExportAdapter(
                    FakeMultiModelPdfExportAdapter.CreateDefaultActiveSnapshot(includePrintSets: false));
                var noSetsVm = new MultiModelPdfExportViewModel(
                    noSetsAdapter,
                    new FakeFolderDialogService());
                Assert.IsFalse(noSetsVm.Rows[0].IsEligible);
                Assert.IsFalse(noSetsVm.CanExport);

                WpfBindingSmokeHarness.ExerciseWindow(
                    new MultiModelPdfExportWindow(ownerHandle) { DataContext = noSetsVm },
                    clearDataContextOnClose: true);

                // 3. Coral Red error banner state (invalid output folder characters)
                var errorAdapter = new FakeMultiModelPdfExportAdapter(
                    FakeMultiModelPdfExportAdapter.CreateDefaultActiveSnapshot());
                var errorVm = new MultiModelPdfExportViewModel(
                    errorAdapter,
                    new FakeFolderDialogService(),
                    @"C:\PDFExports\Invalid|Folder");
                errorVm.Rows[0].SelectedPrintSetName = "CD Set";
                Assert.IsTrue(errorVm.HasErrorBanner);

                WpfBindingSmokeHarness.ExerciseWindow(
                    new MultiModelPdfExportWindow(ownerHandle) { DataContext = errorVm },
                    clearDataContextOnClose: true);

                harness.AssertNoBindingErrors("MultiModelPdfExportWindow Variant A states");
            }

            Assert.AreEqual(_initialDocCount, _uiapp.Application.Documents.Size, "Non-executing MultiModelPdfExport smoke test must not open or leak Revit documents.");
        }

        [Test]
        public void MultiModelPdfExportWindow_BulkFilterAndTriStateHeader_BindAndRenderCleanly()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            IntPtr ownerHandle = _uiapp!.MainWindowHandle;

            var snapshot = new MultiModelPdfSessionSnapshot(
                "CoreShell.rvt",
                new[]
                {
                    new MultiModelPdfDocumentSnapshot(
                        "active-core",
                        "CoreShell.rvt",
                        @"C:\BIM\CoreShell.rvt",
                        MultiModelPdfDocumentOrigin.Active,
                        MultiModelPdfStorageKind.Workshared,
                        printSets: new[]
                        {
                            new MultiModelPdfSetSnapshot("CD Set", 3),
                            new MultiModelPdfSetSnapshot("Permit Set", 2)
                        }),
                    new MultiModelPdfDocumentSnapshot(
                        "link-unit1",
                        "Unit1.rvt",
                        @"C:\BIM\Links\Unit1.rvt",
                        MultiModelPdfDocumentOrigin.Linked,
                        MultiModelPdfStorageKind.CloudAcc,
                        printSets: new[] { new MultiModelPdfSetSnapshot("CD Set", 4) }),
                    new MultiModelPdfDocumentSnapshot(
                        "link-retail",
                        "Retail.rvt",
                        @"C:\BIM\Links\Retail.rvt",
                        MultiModelPdfDocumentOrigin.Linked,
                        MultiModelPdfStorageKind.Local,
                        printSets: new[] { new MultiModelPdfSetSnapshot("Permit Set", 2) }),
                    new MultiModelPdfDocumentSnapshot(
                        "link-unloaded",
                        "SiteUnloaded.rvt",
                        @"C:\BIM\Links\SiteUnloaded.rvt",
                        MultiModelPdfDocumentOrigin.Linked,
                        MultiModelPdfStorageKind.Local,
                        isUnloadedLink: true,
                        printSets: Array.Empty<MultiModelPdfSetSnapshot>()),
                    new MultiModelPdfDocumentSnapshot(
                        "open-annex",
                        "Annex.rvt",
                        @"C:\BIM\Annex.rvt",
                        MultiModelPdfDocumentOrigin.OpenStandalone,
                        MultiModelPdfStorageKind.Local,
                        printSets: new[] { new MultiModelPdfSetSnapshot("CD Set", 5) })
                },
                new[] { MultiModelPdfSetupOption.CreateDefault() });

            var vm = new MultiModelPdfExportViewModel(
                new FakeMultiModelPdfExportAdapter(snapshot),
                new FakeFolderDialogService(),
                @"C:\PDFExports\Batch");

            using (var harness = new WpfBindingSmokeHarness())
            {
                var window = new MultiModelPdfExportWindow(ownerHandle) { DataContext = vm };
                try
                {
                    window.Show();
                    FlushLayout(window);
                    Assert.IsTrue(window.IsVisible);

                    // 1. Bulk Print Set ComboBox formatting & sort order
                    var bulkCombo = window.FindName("BulkPrintSetComboBox") as WpfComboBox;
                    var applyBulkBtn = window.FindName("ApplyBulkPrintSetButton") as WpfButton;
                    var dismissBulkBtn = window.FindName("DismissBulkFeedbackButton") as WpfButton;
                    Assert.IsNotNull(bulkCombo, "Expected BulkPrintSetComboBox.");
                    Assert.IsNotNull(applyBulkBtn, "Expected ApplyBulkPrintSetButton.");
                    Assert.IsNotNull(dismissBulkBtn, "Expected DismissBulkFeedbackButton.");
                    var bulkLabels = bulkCombo!.Items.Cast<MultiModelPdfBulkPrintSetOption>().Select(o => o.DisplayName).ToList();
                    CollectionAssert.AreEqual(
                        new[] { "CD Set (3 of 5 models)", "Permit Set (2 of 5 models)" },
                        bulkLabels);

                    // 2. Tri-state master header CheckBox: initial mixed state -> indeterminate (null)
                    var masterCheck = window.FindName("MasterHeaderCheckBox") as WpfCheckBox;
                    Assert.IsNotNull(masterCheck, "Expected MasterHeaderCheckBox.");
                    Assert.IsTrue(masterCheck!.IsEnabled);
                    Assert.IsTrue(
                        ReferenceEquals(vm.ToggleAllVisibleCommand, masterCheck.Command),
                        "MasterHeaderCheckBox.Command must bind to ToggleAllVisibleCommand.");
                    Binding? masterCheckedBinding = BindingOperations.GetBinding(masterCheck, WpfToggleButton.IsCheckedProperty);
                    Assert.IsNotNull(masterCheckedBinding, "Expected IsChecked binding on MasterHeaderCheckBox.");
                    Assert.AreEqual(
                        BindingMode.OneWay,
                        masterCheckedBinding!.Mode,
                        "MasterHeaderCheckBox.IsChecked must use Mode=OneWay so ToggleButton.OnToggle() cannot push null->false before Command executes.");
                    Assert.IsNull(masterCheck.IsChecked, "Active+Links checked with Open unchecked must yield indeterminate header state.");

                    var checkMark = masterCheck.Template.FindName("CheckMark", masterCheck) as FrameworkElement;
                    var indetMark = masterCheck.Template.FindName("IndeterminateMark", masterCheck) as FrameworkElement;
                    Assert.IsNotNull(checkMark);
                    Assert.IsNotNull(indetMark);
                    Assert.AreEqual(Visibility.Visible, indetMark!.Visibility);
                    Assert.AreEqual(Visibility.Collapsed, checkMark!.Visibility);

                    // Seed a bulk mismatch accent before toggling from indeterminate -> true to prove
                    // ToggleButton.OnToggle()'s internal null->false does not fire AllVisibleChecked=false transiently.
                    MultiModelPdfExportRowViewModel retailRow = vm.Rows.Single(r => r.DocumentKey == "link-retail");
                    retailRow.SetBulkMismatchAccent(true);
                    Assert.IsTrue(retailRow.HasBulkMismatchAccent);

                    // Real user toggle cycle via Space key (invokes ToggleButton.OnClick -> OnToggle() + Command):
                    // 1) null (indeterminate) -> true
                    SimulateCheckBoxUserToggle(masterCheck);
                    FlushLayout(window);
                    Assert.AreEqual(true, masterCheck.IsChecked);
                    Assert.AreEqual(true, vm.AllVisibleChecked);
                    Assert.IsTrue(vm.Rows.Single(r => r.DocumentKey == "open-annex").IsChecked);
                    Assert.IsFalse(vm.Rows.Single(r => r.DocumentKey == "link-unloaded").IsChecked);
                    Assert.IsTrue(
                        retailRow.HasBulkMismatchAccent,
                        "Indeterminate -> Checked click must preserve existing mismatch accent (no transient null->false).");
                    Assert.AreEqual(Visibility.Visible, checkMark.Visibility);
                    Assert.AreEqual(Visibility.Collapsed, indetMark.Visibility);

                    // 2) true -> false
                    SimulateCheckBoxUserToggle(masterCheck);
                    FlushLayout(window);
                    Assert.AreEqual(false, masterCheck.IsChecked);
                    Assert.AreEqual(false, vm.AllVisibleChecked);
                    Assert.AreEqual(0, vm.CheckedCount);
                    Assert.IsFalse(retailRow.HasBulkMismatchAccent);
                    Assert.AreEqual(Visibility.Collapsed, checkMark.Visibility);
                    Assert.AreEqual(Visibility.Collapsed, indetMark.Visibility);

                    // 3) false -> true
                    SimulateCheckBoxUserToggle(masterCheck);
                    FlushLayout(window);
                    Assert.AreEqual(true, masterCheck.IsChecked);
                    Assert.AreEqual(true, vm.AllVisibleChecked);
                    Assert.AreEqual(Visibility.Visible, checkMark.Visibility);
                    Assert.AreEqual(Visibility.Collapsed, indetMark.Visibility);

                    // 4) Restore Active + Links -> indeterminate (null)
                    vm.SelectActiveAndLinksVisibleCommand.Execute(null);
                    FlushLayout(window);
                    Assert.IsNull(masterCheck.IsChecked);
                    Assert.AreEqual(Visibility.Visible, indetMark.Visibility);
                    Assert.AreEqual(Visibility.Collapsed, checkMark.Visibility);

                    // 3. Partial match bulk apply -> Warning (#C2A26A) banner + Uncheck unmatched + row mismatch stripe
                    var bulkBanner = window.FindName("BulkFeedbackBanner") as Border;
                    var uncheckBtn = window.FindName("UncheckUnmatchedButton") as WpfButton;
                    Assert.IsNotNull(bulkBanner);
                    Assert.IsNotNull(uncheckBtn);
                    Assert.AreEqual(Visibility.Collapsed, bulkBanner!.Visibility);

                    applyBulkBtn!.Command.Execute(null);
                    FlushLayout(window);

                    Assert.AreEqual(Visibility.Visible, bulkBanner.Visibility);
                    Assert.AreEqual(Visibility.Visible, uncheckBtn!.Visibility);
                    Assert.AreEqual(Color.FromRgb(0xC2, 0xA2, 0x6A), ((SolidColorBrush)bulkBanner.BorderBrush).Color);

                    var retailStripe = FindVisualChildren<Border>(window)
                        .FirstOrDefault(b => b.Name == "BulkMismatchAccentStripe" &&
                                             ReferenceEquals(b.DataContext, vm.Rows.Single(r => r.DocumentKey == "link-retail")));
                    var unit1Stripe = FindVisualChildren<Border>(window)
                        .FirstOrDefault(b => b.Name == "BulkMismatchAccentStripe" &&
                                             ReferenceEquals(b.DataContext, vm.Rows.Single(r => r.DocumentKey == "link-unit1")));
                    Assert.IsNotNull(retailStripe);
                    Assert.IsNotNull(unit1Stripe);
                    Assert.AreEqual(Visibility.Visible, retailStripe!.Visibility);
                    Assert.AreEqual(Visibility.Collapsed, unit1Stripe!.Visibility);

                    // Dismiss clears banner & stripe; re-apply + Uncheck unmatched clears unmatched row and banner
                    dismissBulkBtn!.Command.Execute(null);
                    FlushLayout(window);
                    Assert.AreEqual(Visibility.Collapsed, bulkBanner.Visibility);
                    Assert.AreEqual(Visibility.Collapsed, retailStripe.Visibility);

                    applyBulkBtn.Command.Execute(null);
                    FlushLayout(window);
                    Assert.AreEqual(Visibility.Visible, retailStripe.Visibility);

                    uncheckBtn.Command.Execute(null);
                    FlushLayout(window);
                    Assert.AreEqual(Visibility.Collapsed, bulkBanner.Visibility);
                    Assert.AreEqual(Visibility.Collapsed, retailStripe.Visibility);

                    // Full match bulk apply -> Success (#7A8F75) banner without Uncheck unmatched button
                    applyBulkBtn.Command.Execute(null);
                    FlushLayout(window);
                    Assert.AreEqual(Visibility.Visible, bulkBanner.Visibility);
                    Assert.AreEqual(Visibility.Collapsed, uncheckBtn.Visibility);
                    Assert.AreEqual(Color.FromRgb(0x7A, 0x8F, 0x75), ((SolidColorBrush)bulkBanner.BorderBrush).Color);

                    // 4. Filter TextBox updates summary, empty state, and preserves row ComboBox selection across hide/restore
                    var filterBox = window.FindName("ProjectFilterTextBox") as WpfTextBox;
                    var summaryBlock = window.FindName("SelectionSummaryTextBlock") as TextBlock;
                    var emptyText = window.FindName("NoMatchingProjectsText") as TextBlock;
                    var allBtn = window.FindName("SelectAllVisibleButton") as WpfButton;
                    var activeLinksBtn = window.FindName("SelectActiveAndLinksVisibleButton") as WpfButton;
                    var noneBtn = window.FindName("SelectNoneVisibleButton") as WpfButton;
                    Assert.IsNotNull(filterBox);
                    Assert.IsNotNull(summaryBlock);
                    Assert.IsNotNull(emptyText);
                    Assert.IsNotNull(allBtn);
                    Assert.IsNotNull(activeLinksBtn);
                    Assert.IsNotNull(noneBtn);

                    filterBox!.Text = "Unit1";
                    FlushLayout(window);
                    Assert.AreEqual("2 selected (1 hidden)", summaryBlock!.Text);
                    Assert.AreEqual(true, masterCheck.IsChecked);
                    Assert.AreEqual(Visibility.Collapsed, emptyText!.Visibility);

                    // Visible-only None unchecks Unit1 while leaving hidden CoreShell checked
                    noneBtn!.Command.Execute(null);
                    FlushLayout(window);
                    Assert.AreEqual("1 selected (1 hidden)", summaryBlock.Text);
                    Assert.AreEqual(false, masterCheck.IsChecked);

                    // Visible-only All re-checks Unit1
                    allBtn!.Command.Execute(null);
                    FlushLayout(window);
                    Assert.AreEqual("2 selected (1 hidden)", summaryBlock.Text);
                    Assert.AreEqual(true, masterCheck.IsChecked);

                    filterBox.Text = "DoesNotMatchAnyProject";
                    FlushLayout(window);
                    Assert.AreEqual(Visibility.Visible, emptyText.Visibility);
                    Assert.IsFalse(masterCheck.IsEnabled);

                    filterBox.Text = string.Empty;
                    FlushLayout(window);
                    Assert.AreEqual(Visibility.Collapsed, emptyText.Visibility);
                    Assert.AreEqual("2 of 4 eligible", summaryBlock.Text);
                    Assert.IsNull(masterCheck.IsChecked);

                    // Active + Links button keeps CoreShell & Unit1 & Retail checked, Annex unchecked
                    activeLinksBtn!.Command.Execute(null);
                    FlushLayout(window);
                    Assert.AreEqual("3 of 4 eligible", summaryBlock.Text);
                    Assert.IsNull(masterCheck.IsChecked);

                    MultiModelPdfExportRowViewModel unit1Row = vm.Rows.Single(r => r.DocumentKey == "link-unit1");
                    Assert.AreEqual("CD Set", unit1Row.SelectedPrintSetName, "Row Print Set selection must survive hide/show filtering.");
                    WpfComboBox? unit1Combo = FindVisualChildren<WpfComboBox>(window)
                        .FirstOrDefault(cb => ReferenceEquals(cb.DataContext, unit1Row));
                    Assert.IsNotNull(unit1Combo);
                    Assert.AreSame(unit1Row.SelectedPrintSetOption, unit1Combo!.SelectedItem);

                    harness.AssertNoBindingErrors("MultiModelPdfExportWindow bulk/filter/tri-state header");
                }
                finally
                {
                    window.DataContext = null;
                    window.Close();
                }
            }

            Assert.AreEqual(_initialDocCount, _uiapp.Application.Documents.Size, "Non-executing MultiModelPdfExport smoke test must not open or leak Revit documents.");
        }

        private static void SimulateCheckBoxUserToggle(WpfCheckBox checkBox)
        {
            // Raise Space KeyDown/KeyUp so WPF executes ToggleButton.OnClick() -> OnToggle() + Command
            // (ToggleButtonAutomationPeer.Toggle() calls OnToggle() alone without invoking Command).
            PresentationSource? source = PresentationSource.FromVisual(checkBox);
            Assert.IsNotNull(source, "Expected rendered PresentationSource for CheckBox toggle simulation.");

            var keyDown = new KeyEventArgs(Keyboard.PrimaryDevice, source!, 0, Key.Space)
            {
                RoutedEvent = UIElement.KeyDownEvent
            };
            checkBox.RaiseEvent(keyDown);

            var keyUp = new KeyEventArgs(Keyboard.PrimaryDevice, source!, 0, Key.Space)
            {
                RoutedEvent = UIElement.KeyUpEvent
            };
            checkBox.RaiseEvent(keyUp);
        }

        private static void FlushLayout(Window window)
        {
            window.UpdateLayout();
            Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.Loaded, new Action(() => { }));
        }

        private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root) where T : DependencyObject
        {
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(root, i);
                if (child is T typed)
                {
                    yield return typed;
                }

                foreach (T descendant in FindVisualChildren<T>(child))
                {
                    yield return descendant;
                }
            }
        }
    }
}
