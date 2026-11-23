using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Synthetic.Features.DetailItemFactory.Commands;
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

            JObject config = JObject.Parse(File.ReadAllText(configPath));
            var registeredClasses = new HashSet<string>(
                config.SelectTokens("$..class")
                    .Select(t => t.Value<string>() ?? string.Empty),
                StringComparer.Ordinal);

            Assert.IsTrue(registeredClasses.Contains(cmdType.FullName!), $"Expected '{cmdType.FullName}' in ribbon_config.json.");
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
                    if (doc != null && doc.IsValidObject)
                    {
                        doc.Close(false);
                    }
                }
            }
        }
    }
}
