using System;
using System.Windows.Threading;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using NUnit.Framework;
using Synthetic.Features.SettingsDashboard.Handlers;
using Synthetic.Features.SettingsDashboard.ViewModels;
using Synthetic.Features.SettingsDashboard.Views;
using Synthetic.Platform.Host;
using Synthetic.Platform.Settings;
using Synthetic.Platform.UI;
using SyntheticTests.Fixtures;
using SyntheticTests.Helpers;

namespace SyntheticTests.Features.SettingsDashboard
{
    /// <summary>
    /// Live Revit STA smoke tests verifying <see cref="HostSettingsRegistrar.EnsureRegistered"/>,
    /// dynamic <see cref="SettingsDashboardWindow"/> composition across all 9 tabs, and auxiliary
    /// Settings Dashboard windows without <see cref="System.IO.IOException"/> resource crashes or WPF
    /// <c>BindingExpression</c> errors.
    /// </summary>
    [TestFixture]
    [RevitSmokeTest]
    public class SettingsDashboardSmokeRevitTests
    {
        private UIApplication? _uiapp;
        private int _initialDocCount;
        private bool _previousSuppressUi;
        private bool _previousSuppressDiskFallback;

        [OneTimeSetUp]
        public void OneTimeSetup(UIApplication uiapp)
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
                    "SettingsDashboardSmokeRevitTests leaked open Revit document(s).");
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
        public void SettingsDashboardWindow_ComposesAndRendersAllNineTabs_WithoutIOExceptionOrBindingErrors()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context must not be null.");
            HostSettingsRegistrar.EnsureRegistered();

            Assert.AreEqual(8, SettingsModuleRegistry.SyncedModules.Count, "Expected 8 registered synced settings modules.");
            Assert.AreEqual(9, SettingsTabRegistry.Tabs.Count, "Expected 9 ordered Settings Dashboard tabs.");

            Document? doc = null;
            SettingsDashboardWindow? window = null;

            using (var harness = new WpfBindingSmokeHarness())
            {
                try
                {
                    doc = _uiapp!.Application.NewProjectDocument(UnitSystem.Imperial);
                    IntPtr ownerHandle = _uiapp.MainWindowHandle;

                    var viewModel = new SettingsDashboardViewModel(doc, ownerHandle);
                    Assert.AreEqual(9, viewModel.SettingModules.Count, "SettingsDashboardViewModel must materialize all 9 tabs.");

                    window = new SettingsDashboardWindow(ownerHandle)
                    {
                        DataContext = viewModel
                    };
                    window.Show();

                    foreach (ISettingModuleViewModel tabViewModel in viewModel.SettingModules)
                    {
                        viewModel.SelectedModule = tabViewModel;
                        window.UpdateLayout();
                        Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.Loaded, new Action(() => { }));
                        Assert.IsTrue(window.IsVisible, $"SettingsDashboardWindow should remain visible on tab '{tabViewModel.ModuleName}'.");
                    }

                    // Auxiliary SettingsDashboard windows
                    var netPathsVm = new NetworkPathsWizardViewModel(new FileUtilitySettings().Defaults(), ownerHandle);
                    WpfBindingSmokeHarness.ExerciseWindow(new NetworkPathsWizardWindow(ownerHandle) { DataContext = netPathsVm }, clearDataContextOnClose: true);

                    var syncWizardVm = new SyncWizardViewModel(new SyncSettings(), new SyntheticTests.Modules.StandardsManagement.FakeFileDialogService());
                    WpfBindingSmokeHarness.ExerciseWindow(new SyncWizardWindow(ownerHandle) { DataContext = syncWizardVm }, clearDataContextOnClose: true);

                    var syncHandler = new SyncExternalEventHandler();
                    using (ExternalEvent syncEvent = ExternalEvent.Create(syncHandler))
                    {
                        WpfBindingSmokeHarness.ExerciseWindow(new SyncResolutionWindow(ownerHandle, doc, @"C:\Temp\SyntheticSmokeSync.json", syncHandler, syncEvent));
                        WpfBindingSmokeHarness.ExerciseWindow(new SyncToastNotification(ownerHandle, doc, @"C:\Temp\SyntheticSmokeSync.json", syncHandler, syncEvent));
                    }

                    harness.AssertNoBindingErrors("all 9 Settings Dashboard tabs and auxiliary windows");
                }
                finally
                {
                    if (window != null)
                    {
                        window.DataContext = null;
                        window.Close();
                    }

                    if (doc != null && doc.IsValidObject)
                    {
                        doc.Close(false);
                    }
                }
            }
        }
    }
}
