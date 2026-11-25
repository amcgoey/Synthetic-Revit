using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Autodesk.Revit.UI;
using WpfComboBox = System.Windows.Controls.ComboBox;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Synthetic.Features.MultiModelPdfExport.Commands;
using Synthetic.Features.MultiModelPdfExport.Models;
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
