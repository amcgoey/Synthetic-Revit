using System;
using System.Collections.Generic;
using System.IO;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Synthetic.Features.SheetIndex.Commands;
using Synthetic.Features.SheetIndex.Models;
using Synthetic.Features.SheetIndex.Services;
using Synthetic.Features.SheetIndex.ViewModels;
using Synthetic.Features.SheetIndex.Views;
using Synthetic.Platform.Host;
using SyntheticTests.Fixtures;
using SyntheticTests.Helpers;

namespace SyntheticTests.Features.SheetIndex
{
    /// <summary>
    /// Live Revit STA smoke tests verifying ribbon command resolution and WPF binding/resource
    /// cleanliness for the <c>SheetIndex</c> feature slice.
    /// </summary>
    [TestFixture]
    [RevitSmokeTest]
    public class SheetIndexSmokeRevitTests
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
                    "SheetIndexSmokeRevitTests leaked open Revit document(s).");
            }
        }

        [Test]
        public void RibbonCommand_CmdExportSheetIndex_ResolvesInAddinAssemblyAndRibbonConfig()
        {
            Type cmdType = typeof(CmdExportSheetIndex);
            Assert.IsTrue(typeof(IExternalCommand).IsAssignableFrom(cmdType));
            Assert.AreEqual("Synthetic.Features.SheetIndex.Commands.CmdExportSheetIndex", cmdType.FullName);

            string configPath = Path.Combine(TestPathHelper.GetProjectRoot(), "src", "Assets", "ribbon_config.json");
            Assert.IsTrue(File.Exists(configPath), $"Expected ribbon_config.json at '{configPath}'.");

            JObject config = JObject.Parse(File.ReadAllText(configPath));
            bool found = false;
            foreach (JToken token in config.SelectTokens("$..class"))
            {
                if (string.Equals(token.Value<string>(), cmdType.FullName, StringComparison.Ordinal))
                {
                    found = true;
                    break;
                }
            }

            Assert.IsTrue(found, $"Expected '{cmdType.FullName}' in ribbon_config.json.");
        }

        [Test]
        public void ExportSheetIndexWindow_ConstructsShowsAndBinds_WithoutBindingOrResourceErrors()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            IntPtr ownerHandle = _uiapp!.MainWindowHandle;

            using (var harness = new WpfBindingSmokeHarness())
            {
                var sheets = new List<SheetIndexSheetModel>
                {
                    new SheetIndexSheetModel("s-101", "A101", "Floor Plan", new[] { "rev-1" })
                };
                var revisions = new List<SheetIndexRevisionModel>
                {
                    new SheetIndexRevisionModel("rev-1", "Permit Set", "2026-01-01", 1)
                };
                var printSets = new List<SheetIndexPrintSetModel>
                {
                    new SheetIndexPrintSetModel("ps-1", "Permit Submittal", new[] { "s-101" })
                };
                var schedules = new List<SheetIndexScheduleModel>
                {
                    new SheetIndexScheduleModel("sch-1", "Drawing Index", sheets)
                };

                var vm = new ExportSheetIndexViewModel(
                    sheets,
                    revisions,
                    new[] { "Permit Submittal" },
                    new[] { "Drawing Index" },
                    new Dictionary<string, List<string>> { { "Permit Submittal", new List<string> { "s-101" } } },
                    new Dictionary<string, List<string>> { { "Drawing Index", new List<string> { "s-101" } } },
                    schedules,
                    printSets,
                    new SheetIndexSessionState());

                var window = new ExportSheetIndexWindow(vm, ownerHandle);
                WpfBindingSmokeHarness.ExerciseWindow(window);

                harness.AssertNoBindingErrors("ExportSheetIndexWindow");
            }
        }
    }
}
