using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Synthetic.Features.SheetIndex.Commands;
using Synthetic.Features.SheetIndex.Models;
using Synthetic.Features.SheetIndex.Services;
using Synthetic.Features.SheetIndex.ViewModels;
using Synthetic.Features.SheetIndex.Views;
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

        private sealed class CapturingTraceListener : TraceListener
        {
            public List<string> Messages { get; } = new List<string>();

            public override void Write(string? message)
            {
                if (!string.IsNullOrEmpty(message))
                {
                    Messages.Add(message!);
                }
            }

            public override void WriteLine(string? message)
            {
                if (!string.IsNullOrEmpty(message))
                {
                    Messages.Add(message!);
                }
            }
        }

        [OneTimeSetUp]
        public void Setup(UIApplication uiapp)
        {
            _uiapp = uiapp;
            TestWorkerJournalMarker.EnsureWritten(uiapp);
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

            var listener = new CapturingTraceListener();
            TraceSource dataBindingSource = PresentationTraceSources.DataBindingSource;
            SourceLevels previousLevel = dataBindingSource.Switch.Level;
            dataBindingSource.Switch.Level = SourceLevels.Error;
            dataBindingSource.Listeners.Add(listener);

            try
            {
                PresentationTraceSources.Refresh();

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
                ExerciseWindow(window);

                Assert.IsEmpty(
                    listener.Messages,
                    "Expected zero WPF DataBinding errors in ExportSheetIndexWindow:" + Environment.NewLine
                    + string.Join(Environment.NewLine, listener.Messages));
            }
            finally
            {
                dataBindingSource.Listeners.Remove(listener);
                dataBindingSource.Switch.Level = previousLevel;
            }
        }

        private static void ExerciseWindow(Window window)
        {
            try
            {
                window.Show();
                window.UpdateLayout();
                Assert.IsTrue(window.IsVisible, $"{window.GetType().Name} should show cleanly without resource exceptions.");
            }
            finally
            {
                window.Close();
            }
        }
    }
}
