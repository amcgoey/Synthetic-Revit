using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Synthetic.Features.MultiModelPdfExport.Commands;
using Synthetic.Features.MultiModelPdfExport.Utilities;
using Synthetic.Platform.Host;
using Synthetic.Platform.UI;
using SyntheticTests.Fixtures;
using SyntheticTests.Helpers;

namespace SyntheticTests.Features.MultiModelPdfExport
{
    /// <summary>
    /// Non-executing live Revit STA smoke tests verifying <see cref="CmdMultiModelPdfExport"/> command,
    /// ribbon entry, selection ViewModel/View resolution, and document-leak safety without invoking
    /// <see cref="CmdMultiModelPdfExport.Execute"/> or <see cref="BBPrinterSettingsUtils.SetRegistryKeys"/>.
    /// </summary>
    [TestFixture]
    [RevitSmokeTest]
    public class MultiModelPdfExportSmokeRevitTests
    {
        private UIApplication? _uiapp;
        private int _initialDocCount;

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

            // Verify BBPrinterSettingsUtils resolves and constructs in-memory without touching Windows Registry.
            var previewSettings = new BBPrinterSettingsUtils("0", "0", @"C:\Temp\SyntheticMultiModelPdfExportSmoke");
            Assert.AreEqual("0", previewSettings.OpenInViewer);
            Assert.AreEqual("0", previewSettings.PromptForFileName);

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
        public void MultiModelPdfExportSelectionViewModel_AndListByCheckboxView_ConstructShowAndBind_WithoutBindingOrResourceErrors()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            IntPtr ownerHandle = _uiapp!.MainWindowHandle;

            using (var harness = new WpfBindingSmokeHarness())
            {
                var docSelectionVm = new ListByCheckboxViewModel
                {
                    Title = "Select Documents",
                    Instruction = "Select the Documents to print sheets from",
                    IsSingleSelection = false
                };
                docSelectionVm.SetItems(new[] { "HostModel.rvt", "LinkedUnit.rvt" }, checkAll: false);
                Assert.AreEqual(2, docSelectionVm.Items.Count);
                Assert.IsFalse(docSelectionVm.OkCommand.CanExecute(null));
                docSelectionVm.Items[0].IsChecked = true;
                Assert.IsTrue(docSelectionVm.OkCommand.CanExecute(null));
                CollectionAssert.AreEqual(new[] { "HostModel.rvt" }, docSelectionVm.CheckedItems);

                WpfBindingSmokeHarness.ExerciseWindow(
                    new ListByCheckboxView(ownerHandle) { DataContext = docSelectionVm },
                    clearDataContextOnClose: true);

                var printSetSelectionVm = new ListByCheckboxViewModel
                {
                    Title = "Select a Print List",
                    Instruction = "The name of the selected print list will be used in each document",
                    IsSingleSelection = true
                };
                printSetSelectionVm.SetItems(new[] { "CD Set", "Permit Set" }, checkAll: false);
                printSetSelectionVm.Items[0].IsChecked = true;
                printSetSelectionVm.Items[1].IsChecked = true;
                CollectionAssert.AreEqual(new[] { "Permit Set" }, printSetSelectionVm.CheckedItems);

                WpfBindingSmokeHarness.ExerciseWindow(
                    new ListByCheckboxView(ownerHandle) { DataContext = printSetSelectionVm },
                    clearDataContextOnClose: true);

                harness.AssertNoBindingErrors("MultiModelPdfExport selection ViewModel and ListByCheckboxView");
            }

            Assert.AreEqual(_initialDocCount, _uiapp.Application.Documents.Size, "Non-executing MultiModelPdfExport smoke test must not open or leak Revit documents.");
        }
    }
}
