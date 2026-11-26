using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using NUnit.Framework;
using Synthetic.Features.DetailItemFactory.Adapters;
using Synthetic.Features.DetailItemFactory.Commands;
using Synthetic.Features.DetailItemFactory.Models;
using Synthetic.Features.DetailItemFactory.Services;
using Synthetic.Features.DetailItemFactory.ViewModels;
using Synthetic.Features.DetailItemFactory.Views;
using Synthetic.Platform.Host;
using Synthetic.Platform.Settings;
using Synthetic.Platform.UI;
using SyntheticTests.Fixtures;
using SyntheticTests.Helpers;
using SyntheticTests.Modules.StandardsManagement;
using View = Autodesk.Revit.DB.View;

namespace SyntheticTests.Features.DetailItemFactory
{
    /// <summary>
    /// Live Revit STA smoke tests verifying ribbon command resolution, view/path guards,
    /// template resolution, and WPF binding/resource cleanliness for all <c>DetailItemFactory</c> views.
    /// </summary>
    [TestFixture]
    [RevitSmokeTest]
    public class DetailItemFactorySmokeRevitTests
    {
        private UIApplication? _uiapp;
        private int _initialDocCount;
        private bool _previousSuppressUi;
        private bool _previousSuppressDiskFallback;

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
                    "DetailItemFactorySmokeRevitTests leaked open Revit document(s).");
            }
        }

        [SetUp]
        public void SetUp()
        {
            _previousSuppressUi = ProgressCoordinator.SuppressUI;
            _previousSuppressDiskFallback = SettingsRuntimeOptions.SuppressFirmwideDiskFallback;
            ProgressCoordinator.SuppressUI = true;
            SettingsRuntimeOptions.SuppressFirmwideDiskFallback = true;
            SettingsManager.ClearCache();
        }

        [TearDown]
        public void TearDown()
        {
            SettingsManager.ClearCache();
            ProgressCoordinator.SuppressUI = _previousSuppressUi;
            SettingsRuntimeOptions.SuppressFirmwideDiskFallback = _previousSuppressDiskFallback;
        }

        [Test]
        public void RibbonCommand_CmdDetailItemFactory_ResolvesInAddinAssemblyAndRibbonConfig_WithNoStaticExternalEvent()
        {
            Type cmdType = typeof(CmdDetailItemFactory);
            Assert.IsTrue(typeof(IExternalCommand).IsAssignableFrom(cmdType));
            Assert.AreEqual("Synthetic.Features.DetailItemFactory.Commands.CmdDetailItemFactory", cmdType.FullName);

            FieldInfo[] staticEventFields = cmdType
                .GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(f => typeof(ExternalEvent).IsAssignableFrom(f.FieldType) ||
                            typeof(IExternalEventHandler).IsAssignableFrom(f.FieldType))
                .ToArray();
            Assert.IsEmpty(staticEventFields, "CmdDetailItemFactory must execute synchronously without static ExternalEvent fields.");

            string configPath = Path.Combine(TestPathHelper.GetProjectRoot(), "src", "Assets", "ribbon_config.json");
            Assert.IsTrue(File.Exists(configPath), $"Expected ribbon_config.json at '{configPath}'.");

            RibbonConfig? config = JsonConvert.DeserializeObject<RibbonConfig>(File.ReadAllText(configPath));
            Assert.IsNotNull(config?.Panels);

            RibbonItemConfig? commandItem = config!.Panels!
                .SelectMany(p => p.Items ?? Enumerable.Empty<RibbonItemConfig>())
                .FirstOrDefault(i => string.Equals(i.Class, cmdType.FullName, StringComparison.Ordinal));

            Assert.IsNotNull(commandItem, $"Expected '{cmdType.FullName}' in ribbon_config.json.");
            Assert.IsNull(commandItem!.MinVersion, "CmdDetailItemFactory should not restrict minimum Revit version.");

            int activeVersion = int.Parse(_uiapp!.Application.VersionNumber);
            Assert.IsTrue(
                RibbonItemFilter.ShouldIncludeItem(commandItem, isDebug: false, activeVersion: activeVersion),
                $"Expected CmdDetailItemFactory to be enabled for Revit {activeVersion}.");
        }

        [Test]
        public void DetailItemTemplate_ResolvesBundledAssetFirstWithFamilyTemplateFallback_AndOpensCleanly()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");

            string assemblyLocation = typeof(RevitDetailItemFactoryAdapter).Assembly.Location;
            string assemblyDir = Path.GetDirectoryName(assemblyLocation) ?? string.Empty;
            string expectedBundledPath = Path.Combine(assemblyDir, "Assets", "Templates", "Detail Item.rft");
            Assert.IsTrue(
                File.Exists(expectedBundledPath),
                $"Expected per-version packaged template at '{expectedBundledPath}'.");

            string tempRoot = Path.Combine(Path.GetTempPath(), "SyntheticDetailItemTemplateTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);
            Document? familyDoc = null;
            try
            {
                string fakeFallbackDir = Path.Combine(tempRoot, "FallbackTemplates");
                Directory.CreateDirectory(fakeFallbackDir);
                string fakeFallbackTemplate = Path.Combine(fakeFallbackDir, "Detail Item.rft");
                File.WriteAllText(fakeFallbackTemplate, "fallback");

                string? resolvedBundled = RevitDetailItemFactoryAdapter.ResolveTemplatePath(assemblyLocation, fakeFallbackDir);
                Assert.AreEqual(expectedBundledPath, resolvedBundled, "Bundled assembly template should take precedence over FamilyTemplatePath.");

                string emptyAssemblyDir = Path.Combine(tempRoot, "EmptyAddin");
                Directory.CreateDirectory(emptyAssemblyDir);
                string fakeAssemblyPath = Path.Combine(emptyAssemblyDir, "Synthetic.dll");

                string? resolvedFallback = RevitDetailItemFactoryAdapter.ResolveTemplatePath(fakeAssemblyPath, fakeFallbackDir);
                Assert.AreEqual(fakeFallbackTemplate, resolvedFallback, "Template resolution should fall back to Application.FamilyTemplatePath.");

                string emptyFallbackDir = Path.Combine(tempRoot, "EmptyFallback");
                Directory.CreateDirectory(emptyFallbackDir);
                Assert.IsNull(RevitDetailItemFactoryAdapter.ResolveTemplatePath(fakeAssemblyPath, emptyFallbackDir));

                familyDoc = _uiapp!.Application.NewFamilyDocument(resolvedBundled!);
                Assert.IsNotNull(familyDoc, "Expected NewFamilyDocument to succeed with packaged Detail Item.rft.");
                Assert.IsTrue(familyDoc.IsFamilyDocument, "Opened document should be a family document.");
            }
            finally
            {
                if (familyDoc != null)
                {
                    if (familyDoc.IsValidObject)
                    {
                        familyDoc.Close(false);
                    }
                    familyDoc.Dispose();
                }

                if (Directory.Exists(tempRoot))
                {
                    Directory.Delete(tempRoot, recursive: true);
                }
            }
        }

        [Test]
        public void DetailItemFactoryViews_ConstructShowAndBind_WithoutIOExceptionOrBindingErrors()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            IntPtr ownerHandle = _uiapp!.MainWindowHandle;

            Document? doc = null;

            using (var harness = new WpfBindingSmokeHarness())
            {
                try
                {
                    doc = _uiapp.Application.NewProjectDocument(UnitSystem.Metric);

                    View? activeView = new FilteredElementCollector(doc)
                        .OfClass(typeof(ViewPlan))
                        .Cast<ViewPlan>()
                        .FirstOrDefault(v => !v.IsTemplate);
                    Assert.IsNotNull(activeView, "Metric project template must contain at least one ViewPlan.");
                    Assert.IsTrue(RevitDetailItemFactoryAdapter.IsSupported2DView(activeView));

                    var adapter = new RevitDetailItemFactoryAdapter(doc, null, _uiapp, activeView, Array.Empty<ElementId>());
                    DetailItemSessionSnapshot snapshot = adapter.CaptureSessionSnapshot();
                    Assert.AreEqual(
                        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                        snapshot.DefaultOutputFolder,
                        "Unsaved document must fall back to My Documents for default output folder.");

                    // 1. DetailItemFactoryView (including CleanupTempFiles CheckBox two-way binding)
                    var mainVm = new DetailItemFactoryViewModel(snapshot, new FakeFolderDialogService());
                    var factoryView = new DetailItemFactoryView(ownerHandle) { DataContext = mainVm };
                    try
                    {
                        factoryView.Show();
                        factoryView.UpdateLayout();
                        Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.Loaded, new Action(() => { }));
                        Assert.IsTrue(factoryView.IsVisible, "DetailItemFactoryView should show cleanly without resource exceptions.");

                        var cleanupCheckBox = factoryView.FindName("CleanupTempFilesCheckBox") as CheckBox;
                        Assert.IsNotNull(cleanupCheckBox, "Expected CleanupTempFilesCheckBox on DetailItemFactoryView.");

                        BindingExpression? cleanupBinding = BindingOperations.GetBindingExpression(
                            cleanupCheckBox!,
                            System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty);
                        Assert.IsNotNull(cleanupBinding, "Expected IsChecked binding on CleanupTempFilesCheckBox.");
                        Assert.IsFalse(cleanupBinding!.HasError, "CleanupTempFilesCheckBox binding must not have errors.");
                        Assert.IsTrue(cleanupCheckBox.IsChecked == true, "CleanupTempFilesCheckBox should default to checked.");
                        Assert.IsTrue(mainVm.CleanupTempFiles);

                        cleanupCheckBox.IsChecked = false;
                        Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.DataBind, new Action(() => { }));
                        Assert.IsFalse(mainVm.CleanupTempFiles, "Unchecking CleanupTempFilesCheckBox should update ViewModel.CleanupTempFiles.");

                        mainVm.CleanupTempFiles = true;
                        Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.DataBind, new Action(() => { }));
                        Assert.IsTrue(cleanupCheckBox.IsChecked == true, "Setting ViewModel.CleanupTempFiles should update CleanupTempFilesCheckBox.");
                    }
                    finally
                    {
                        factoryView.DataContext = null;
                        factoryView.Close();
                    }

                    // 2. DetailItemFactoryResultsView
                    var resultsVm = new DetailItemFactoryResultsViewModel(
                        @"C:\Temp\SyntheticDetailItemsSmoke",
                        new[]
                        {
                            new DetailItemResultItem
                            {
                                ElementName = "Sample Wall",
                                ElementId = "1001",
                                Status = DetailItemResultStatus.Success,
                                Message = "Smoke test item"
                            }
                        },
                        new FakeUserPromptService());
                    WpfBindingSmokeHarness.ExerciseWindow(
                        new DetailItemFactoryResultsView(ownerHandle) { DataContext = resultsVm },
                        clearDataContextOnClose: true);

                    // 3. DetailItemFactorySettingsView
                    var settingsVm = RevitDetailItemFactoryAdapter.CreateSettingsViewModel(doc, ownerHandle);
                    var settingsView = new DetailItemFactorySettingsView { DataContext = settingsVm };
                    var hostWindow = new Window
                    {
                        Title = "DetailItemFactorySettingsView Smoke Host",
                        Content = settingsView,
                        Width = 540,
                        Height = 480,
                        ShowInTaskbar = false
                    };
                    RevitWindowHelper.SetOwner(hostWindow, ownerHandle);
                    WpfBindingSmokeHarness.ExerciseWindow(hostWindow, clearDataContextOnClose: true);

                    harness.AssertNoBindingErrors("DetailItemFactory views");
                }
                finally
                {
                    if (doc != null)
                    {
                        if (doc.IsValidObject)
                        {
                            doc.Close(false);
                        }
                        doc.Dispose();
                    }
                }
            }
        }
    }
}
