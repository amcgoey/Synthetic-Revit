using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
using Synthetic.Platform.Host;
using Synthetic.Platform.UI;
using Synthetic.RevitDOM.Models;
using SyntheticTests.Fixtures;
using SyntheticTests.Helpers;
using SyntheticTests.Modules.StandardsManagement;

namespace SyntheticTests.Features.MergeDuplicates
{
    /// <summary>
    /// Live Revit STA smoke tests verifying ribbon command resolution, theme loading,
    /// cross-assembly <c>Synthetic.RevitDOM.Models;assembly=Synthetic.Core</c> BAML resolution,
    /// and zero WPF binding errors across <c>MergeDuplicates</c> and <c>StandardsManagement</c> views.
    /// </summary>
    [TestFixture]
    [RevitSmokeTest]
    public class MergeAndStandardsSmokeRevitTests
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
                    "MergeAndStandardsSmokeRevitTests leaked open Revit document(s).");
            }
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

            Document? doc = null;

            using (var harness = new WpfBindingSmokeHarness())
            {
                try
                {
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
                    WpfBindingSmokeHarness.ExerciseWindow(new MergeDuplicatesWindow(ownerHandle) { DataContext = mergeVm }, clearDataContextOnClose: true);

                    // 2. MergeDetailedReviewWindow
                    var detailVm = new MergeDetailedReviewViewModel(sampleCluster);
                    WpfBindingSmokeHarness.ExerciseWindow(new MergeDetailedReviewWindow(ownerHandle) { DataContext = detailVm }, clearDataContextOnClose: true);

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
                    WpfBindingSmokeHarness.ExerciseWindow(new StandardsReviewWindow(ownerHandle) { DataContext = reviewVm }, clearDataContextOnClose: true);

                    // 5. Additional StandardsManagement views
                    var importSummaryVm = new ImportSummaryViewModel(new ObservableCollection<Synthetic.Features.StandardsManagement.ImportLogItem>());
                    WpfBindingSmokeHarness.ExerciseWindow(new ImportSummaryWindow(ownerHandle, importSummaryVm), clearDataContextOnClose: true);

                    var selectDocVm = new SelectRevitDocumentViewModel(null, new List<Document> { doc });
                    var selectDocWindow = new SelectRevitDocumentWindow(selectDocVm);
                    RevitWindowHelper.SetOwner(selectDocWindow, ownerHandle);
                    WpfBindingSmokeHarness.ExerciseWindow(selectDocWindow, clearDataContextOnClose: true);

                    var nestedEditorVm = new NestedDataEditorViewModel(
                        new CompoundStructureModel(),
                        new ObservableCollection<ElementTypeWrapperVM>());
                    WpfBindingSmokeHarness.ExerciseWindow(new NestedDataEditorWindow(ownerHandle, nestedEditorVm), clearDataContextOnClose: true);

                    var catControl = new CategorySelectionControl
                    {
                        DataContext = selectDocVm.FilterHierarchy
                    };
                    var catHostWindow = new Window
                    {
                        Title = "CategorySelectionControl Smoke Host",
                        Content = catControl,
                        Width = 400,
                        Height = 400,
                        ShowInTaskbar = false
                    };
                    RevitWindowHelper.SetOwner(catHostWindow, ownerHandle);
                    WpfBindingSmokeHarness.ExerciseWindow(catHostWindow, clearDataContextOnClose: true);

                    harness.AssertNoBindingErrors("MergeDuplicates and StandardsManagement views");
                }
                finally
                {
                    if (doc != null && doc.IsValidObject)
                    {
                        doc.Close(false);
                    }
                }
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
