using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.UI;
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
                // 1. Valid populated state (active model + Print Sets + skipped view footer warning)
                var populatedAdapter = new FakeMultiModelPdfExportAdapter(
                    FakeMultiModelPdfExportAdapter.CreateDefaultActiveSnapshot());
                var populatedVm = new MultiModelPdfExportViewModel(
                    populatedAdapter,
                    new FakeFolderDialogService(),
                    @"C:\PDFExports\Demo");
                MultiModelPdfExportRowViewModel activeRow = populatedVm.Rows[0];
                activeRow.SelectedPrintSetName = "Permit Set";
                Assert.IsTrue(populatedVm.CanExport);
                Assert.AreEqual("Export 1 PDF", populatedVm.ExportButtonText);

                WpfBindingSmokeHarness.ExerciseWindow(
                    new MultiModelPdfExportWindow(ownerHandle) { DataContext = populatedVm },
                    clearDataContextOnClose: true);

                // 2. Disabled "No saved Print Sets" row state
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
    }
}
