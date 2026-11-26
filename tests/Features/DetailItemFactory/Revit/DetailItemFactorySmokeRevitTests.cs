using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using NUnit.Framework;
using Synthetic.Features.DetailItemFactory.Commands;
using Synthetic.Features.DetailItemFactory.Handlers;
using Synthetic.Features.DetailItemFactory.Models;
using Synthetic.Features.DetailItemFactory.Settings;
using Synthetic.Features.DetailItemFactory.ViewModels;
using Synthetic.Features.DetailItemFactory.Views;
using Synthetic.Platform.Host;
using Synthetic.Platform.Settings;
using Synthetic.Platform.UI;
using SyntheticTests.Fixtures;
using SyntheticTests.Helpers;
using View = Autodesk.Revit.DB.View;

namespace SyntheticTests.Features.DetailItemFactory
{
    /// <summary>
    /// Live Revit STA smoke tests verifying ribbon command resolution and WPF binding/resource
    /// cleanliness for all <c>DetailItemFactory</c> views.
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
        public void RibbonCommand_CmdDetailItemFactory_ResolvesInAddinAssemblyAndRibbonConfig()
        {
            Type cmdType = typeof(CmdDetailItemFactory);
            Assert.IsTrue(typeof(IExternalCommand).IsAssignableFrom(cmdType));
            Assert.AreEqual("Synthetic.Features.DetailItemFactory.Commands.CmdDetailItemFactory", cmdType.FullName);

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

            string assemblyLocation = typeof(DetailItemFactoryEventHandler).Assembly.Location;
            string assemblyDir = Path.GetDirectoryName(assemblyLocation) ?? string.Empty;
            string expectedBundledPath = Path.Combine(assemblyDir, "Assets", "Templates", "Detail Item.rft");
            Assert.IsTrue(
                File.Exists(expectedBundledPath),
                $"Expected per-version packaged template at '{expectedBundledPath}'.");

            // 1. Assembly-directory template takes precedence over fallback FamilyTemplatePath
            string tempRoot = Path.Combine(Path.GetTempPath(), "SyntheticDetailItemTemplateTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);
            Document? familyDoc = null;
            try
            {
                string fakeFallbackDir = Path.Combine(tempRoot, "FallbackTemplates");
                Directory.CreateDirectory(fakeFallbackDir);
                string fakeFallbackTemplate = Path.Combine(fakeFallbackDir, "Detail Item.rft");
                File.WriteAllText(fakeFallbackTemplate, "fallback");

                string? resolvedBundled = DetailItemFactoryEventHandler.ResolveTemplatePath(assemblyLocation, fakeFallbackDir);
                Assert.AreEqual(expectedBundledPath, resolvedBundled, "Bundled assembly template should take precedence over FamilyTemplatePath.");

                // 2. Fallback to FamilyTemplatePath when assembly directory has no template
                string emptyAssemblyDir = Path.Combine(tempRoot, "EmptyAddin");
                Directory.CreateDirectory(emptyAssemblyDir);
                string fakeAssemblyPath = Path.Combine(emptyAssemblyDir, "Synthetic.dll");

                string? resolvedFallback = DetailItemFactoryEventHandler.ResolveTemplatePath(fakeAssemblyPath, fakeFallbackDir);
                Assert.AreEqual(fakeFallbackTemplate, resolvedFallback, "Template resolution should fall back to Application.FamilyTemplatePath.");

                // 3. Returns null when neither directory contains Detail Item.rft
                string emptyFallbackDir = Path.Combine(tempRoot, "EmptyFallback");
                Directory.CreateDirectory(emptyFallbackDir);
                Assert.IsNull(DetailItemFactoryEventHandler.ResolveTemplatePath(fakeAssemblyPath, emptyFallbackDir));

                // 4. Bundled template opens cleanly in the running Revit version without leaking documents
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

                    // 1. DetailItemFactoryView
                    var mainVm = new DetailItemFactoryViewModel(doc, activeView!, Array.Empty<ElementId>(), ownerHandle);
                    WpfBindingSmokeHarness.ExerciseWindow(
                        new DetailItemFactoryView(ownerHandle) { DataContext = mainVm },
                        clearDataContextOnClose: true);

                    // 2. DetailItemFactoryResultsView
                    var resultsVm = new DetailItemFactoryResultsViewModel(
                        @"C:\Temp\SyntheticDetailItemsSmoke",
                        new[]
                        {
                            new DetailItemResultItem
                            {
                                ElementName = "Sample Wall",
                                ElementId = "1001",
                                Status = "Success",
                                Message = "Smoke test item"
                            }
                        });
                    WpfBindingSmokeHarness.ExerciseWindow(
                        new DetailItemFactoryResultsView(ownerHandle) { DataContext = resultsVm },
                        clearDataContextOnClose: true);

                    // 3. DetailItemFactorySettingsView
                    var settingsVm = new DetailItemFactorySettingsViewModel(doc, ownerHandle);
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
