using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Synthetic.Features.SharedCoordinates.Commands;
using Synthetic.Features.SharedCoordinates.Models;
using Synthetic.Features.SharedCoordinates.ViewModels;
using Synthetic.Features.SharedCoordinates.Views;
using SyntheticTests.Fixtures;
using SyntheticTests.Helpers;

namespace SyntheticTests.Features.SharedCoordinates
{
    /// <summary>
    /// Live Revit STA smoke tests verifying ribbon command resolution, theme loading, and
    /// WPF binding cleanliness for all <c>SharedCoordinates</c> windows.
    /// </summary>
    [TestFixture]
    [RevitSmokeTest]
    public class SharedCoordinatesSmokeRevitTests
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
        public void RibbonCommands_SharedCoordinates_ResolveInAddinAssemblyAndRibbonConfig()
        {
            Type syncCmdType = typeof(CmdSyncSharedSites);
            Type moveCmdType = typeof(CmdMoveLinksToSharedSites);

            Assert.IsTrue(typeof(IExternalCommand).IsAssignableFrom(syncCmdType));
            Assert.IsTrue(typeof(IExternalCommand).IsAssignableFrom(moveCmdType));
            Assert.AreEqual("Synthetic.Features.SharedCoordinates.Commands.CmdSyncSharedSites", syncCmdType.FullName);
            Assert.AreEqual("Synthetic.Features.SharedCoordinates.Commands.CmdMoveLinksToSharedSites", moveCmdType.FullName);

            string configPath = Path.Combine(TestPathHelper.GetProjectRoot(), "src", "Assets", "ribbon_config.json");
            Assert.IsTrue(File.Exists(configPath), $"Expected ribbon_config.json at '{configPath}'.");

            JObject config = JObject.Parse(File.ReadAllText(configPath));
            var registeredClasses = new HashSet<string>(
                config.SelectTokens("$..class")
                    .Select(t => t.Value<string>() ?? string.Empty),
                StringComparer.Ordinal);

            Assert.IsTrue(registeredClasses.Contains(syncCmdType.FullName!), $"Expected '{syncCmdType.FullName}' in ribbon_config.json.");
            Assert.IsTrue(registeredClasses.Contains(moveCmdType.FullName!), $"Expected '{moveCmdType.FullName}' in ribbon_config.json.");
        }

        [Test]
        public void SharedCoordinatesViews_ConstructShowAndBind_WithoutThemeOrBindingErrors()
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

                var fakeAdapter = new FakeSharedCoordinatesAdapter();
                fakeAdapter.AddLinkedModel("link-1", "Tower_Host.rvt", isSelected: true);
                fakeAdapter.AddActiveLevel("Level 1", 0.0);
                fakeAdapter.AddExistingSite("Floor 1");
                fakeAdapter.AddLinkedLevel("link-1", "Floor 1", 0.0, 10.0);
                fakeAdapter.AddLinkedLevel("link-1", "Floor 2", 12.0, 22.0);
                fakeAdapter.AddRepositionInstance(
                    typeId: "type-1",
                    typeName: "Unit_A.rvt",
                    instanceId: "inst-1",
                    instanceName: "Unit_A : 1",
                    assignedSiteName: "Floor 1",
                    isOutOfSync: true,
                    isPinned: true,
                    deltaZ: 10.0);

                // 1. MoveLinksToSharedSitesWindow
                var moveVm = new MoveLinksToSharedSitesViewModel(fakeAdapter);
                ExerciseWindow(new MoveLinksToSharedSitesWindow(ownerHandle) { DataContext = moveVm });

                // 2. SyncSharedSitesWindow
                var syncVm = new SyncSharedSitesViewModel(fakeAdapter);
                ExerciseWindow(new SyncSharedSitesWindow(ownerHandle) { DataContext = syncVm });

                // 3. SharedCoordinatesSummaryWindow (Sync mode)
                var syncSummary = new SyncSharedSitesSummary
                {
                    CreatedCount = 1,
                    UpdatedCount = 1,
                    CreatedSiteNames = new[] { "Floor 2" },
                    UpdatedSiteNames = new[] { "Floor 1" }
                };
                ExerciseWindow(new SharedCoordinatesSummaryWindow(ownerHandle, syncSummary));

                // 4. SharedCoordinatesSummaryWindow (Reposition mode)
                var repoSummary = new RepositionLinksSummary
                {
                    RepositionedCount = 1,
                    AlreadyAlignedCount = 1,
                    SkippedCount = 1,
                    RepositionedInstanceNames = new[] { "Unit_A : 1" },
                    SkippedInstanceNames = new[] { "Unit_B : <Not Shared>" }
                };
                ExerciseWindow(new SharedCoordinatesSummaryWindow(ownerHandle, repoSummary));

                Assert.IsEmpty(
                    listener.Messages,
                    "Expected zero WPF DataBinding errors across SharedCoordinates views:" + Environment.NewLine
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
