using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Synthetic.Features.MergeDuplicates.Commands;
using Synthetic.Features.MergeDuplicates.Services;
using Synthetic.Features.MergeDuplicates.ViewModels;
using Synthetic.Features.MergeDuplicates.Views;
using Synthetic.Features.StandardsManagement.Commands;
using Synthetic.Features.StandardsManagement.Settings;
using Synthetic.Features.StandardsManagement.ViewModels;
using Synthetic.Features.StandardsManagement.Views;
using Synthetic.RevitDOM.Models;
using SyntheticTests.Helpers;
using SyntheticTests.Modules.StandardsManagement;

namespace SyntheticTests.Features.MergeDuplicates
{
    /// <summary>
    /// Live Revit STA smoke tests verifying ribbon command resolution, theme loading,
    /// cross-assembly <c>Synthetic.RevitDOM.Models;assembly=Synthetic.Core</c> BAML resolution,
    /// and zero WPF binding errors for all 4 Merge-POCO views across
    /// <c>MergeDuplicates</c> and <c>StandardsManagement</c>.
    /// </summary>
    [TestFixture]
    [RevitSmokeTest]
    public class MergeAndStandardsSmokeRevitTests
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
        public void RibbonCommands_MergeAndStandards_ResolveInAddinAssemblyAndRibbonConfig()
        {
            Type mergeCmdType = typeof(CmdMergeDuplicates);
            Type standardsCmdType = typeof(CmdProjectStandards);

            Assert.IsTrue(typeof(IExternalCommand).IsAssignableFrom(mergeCmdType));
            Assert.IsTrue(typeof(IExternalCommand).IsAssignableFrom(standardsCmdType));
            Assert.AreEqual("Synthetic.Features.MergeDuplicates.Commands.CmdMergeDuplicates", mergeCmdType.FullName);
            Assert.AreEqual("Synthetic.Features.StandardsManagement.Commands.CmdProjectStandards", standardsCmdType.FullName);

            string configPath = Path.Combine(TestPathHelper.GetProjectRoot(), "src", "Assets", "ribbon_config.json");
            Assert.IsTrue(File.Exists(configPath), $"Expected ribbon_config.json at '{configPath}'.");

            JObject config = JObject.Parse(File.ReadAllText(configPath));
            var registeredClasses = new HashSet<string>(
                config.SelectTokens("$..class")
                    .Select(t => t.Value<string>() ?? string.Empty),
                StringComparer.Ordinal);

            Assert.IsTrue(registeredClasses.Contains(mergeCmdType.FullName!), $"Expected '{mergeCmdType.FullName}' in ribbon_config.json.");
            Assert.IsTrue(registeredClasses.Contains(standardsCmdType.FullName!), $"Expected '{standardsCmdType.FullName}' in ribbon_config.json.");
        }

        [Test]
        public void MergePocoViews_ConstructShowAndBind_WithoutIOExceptionOrBindingErrors()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            IntPtr ownerHandle = _uiapp!.MainWindowHandle;

            var listener = new CapturingTraceListener();
            TraceSource dataBindingSource = PresentationTraceSources.DataBindingSource;
            SourceLevels previousLevel = dataBindingSource.Switch.Level;
            dataBindingSource.Switch.Level = SourceLevels.Error;
            dataBindingSource.Listeners.Add(listener);

            Document? doc = null;

            try
            {
                PresentationTraceSources.Refresh();
                doc = _uiapp.Application.NewProjectDocument(UnitSystem.Metric);

                DuplicateClusterModel sampleCluster = BuildSampleCluster();

                // 1. MergeDuplicatesWindow
                var mergeVm = new MergeDuplicatesViewModel
                {
                    MainWindowHandle = ownerHandle,
                    Document = doc,
                    ScannedClusters = new ObservableCollection<DuplicateClusterModel> { sampleCluster }
                };
                mergeVm.QueueVM.QueuedClusters.Add(sampleCluster);
                ExerciseWindow(new MergeDuplicatesWindow(ownerHandle) { DataContext = mergeVm });

                // 2. MergeDetailedReviewWindow
                var detailVm = new MergeDetailedReviewViewModel(sampleCluster);
                ExerciseWindow(new MergeDetailedReviewWindow(ownerHandle) { DataContext = detailVm });

                // 3. ProjectStandardsDashboardWindow (Idle, Edit, and Diff sub-workspaces)
                var dashboardVm = DashboardTestFactory.Create(doc, settings: new StandardsSettings());
                dashboardVm.ActiveDiffClusters.Add(sampleCluster);
                var dashboardWindow = new ProjectStandardsDashboardWindow(ownerHandle)
                {
                    DataContext = dashboardVm
                };
                try
                {
                    dashboardWindow.Show();
                    foreach (WorkspaceMode mode in new[] { WorkspaceMode.Idle, WorkspaceMode.Edit, WorkspaceMode.Diff })
                    {
                        dashboardVm.ActiveWorkspace = mode;
                        dashboardWindow.UpdateLayout();
                        Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.Loaded, new Action(() => { }));
                        Assert.IsTrue(dashboardWindow.IsVisible, $"ProjectStandardsDashboardWindow should remain visible in '{mode}' mode.");
                    }
                }
                finally
                {
                    dashboardWindow.DataContext = null;
                    dashboardWindow.Close();
                }

                // 4. StandardsReviewWindow
                var reviewVm = new StandardsReviewViewModel(doc, new ObservableCollection<DuplicateClusterModel> { sampleCluster });
                ExerciseWindow(new StandardsReviewWindow(ownerHandle) { DataContext = reviewVm });

                Assert.IsEmpty(
                    listener.Messages,
                    "Expected zero WPF DataBinding errors across all 4 Merge-POCO views:" + Environment.NewLine
                    + string.Join(Environment.NewLine, listener.Messages));
            }
            finally
            {
                dataBindingSource.Listeners.Remove(listener);
                dataBindingSource.Switch.Level = previousLevel;

                if (doc != null && doc.IsValidObject)
                {
                    doc.Close(false);
                }
            }
        }

        private static void ExerciseWindow(Window window)
        {
            try
            {
                window.Show();
                window.UpdateLayout();
                Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.Loaded, new Action(() => { }));
                Assert.IsTrue(window.IsVisible, $"{window.GetType().Name} should show cleanly without resource exceptions.");
            }
            finally
            {
                window.DataContext = null;
                window.Close();
            }
        }

        private static DuplicateClusterModel BuildSampleCluster()
        {
            var primaryType = new DuplicateTypeModel
            {
                RevitTypeId = new ElementIdModel { Id = 101, UniqueId = "uid-type-1" },
                Name = "Type A",
                Parameters = new Dictionary<string, string> { { "Comments", "String:Primary" } }
            };
            var sourceType = new DuplicateTypeModel
            {
                RevitTypeId = new ElementIdModel { Id = 102, UniqueId = "uid-type-2" },
                Name = "Type A",
                Parameters = new Dictionary<string, string> { { "Comments", "String:Secondary" } }
            };

            var primaryItem = new DuplicateItemModel
            {
                RevitElementId = new ElementIdModel { Id = 1001, UniqueId = "uid-1001" },
                ItemName = "Sample Family",
                CategoryName = "Detail Items",
                IsPrimary = true,
                IsIncludedForMerge = true,
                IsLoadableFamily = true,
                Types = new ObservableCollection<DuplicateTypeModel> { primaryType }
            };
            var secondaryItem = new DuplicateItemModel
            {
                RevitElementId = new ElementIdModel { Id = 1002, UniqueId = "uid-1002" },
                ItemName = "Sample Family 1",
                CategoryName = "Detail Items",
                IsPrimary = false,
                IsIncludedForMerge = true,
                IsLoadableFamily = true,
                Types = new ObservableCollection<DuplicateTypeModel> { sourceType }
            };

            var cluster = new DuplicateClusterModel
            {
                ClusterName = "Detail Items: Sample Family Duplicates",
                Items = new ObservableCollection<DuplicateItemModel> { primaryItem, secondaryItem }
            };
            MergeAnalysisEngine.GenerateRecommendations(cluster);
            return cluster;
        }
    }
}
