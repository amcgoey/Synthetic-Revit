using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows.Threading;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using NUnit.Framework;
using Synthetic.Features.SettingsDashboard.ViewModels;
using Synthetic.Features.SettingsDashboard.Views;
using Synthetic.Platform.Host;
using Synthetic.Platform.Settings;
using Synthetic.Platform.UI;
using SyntheticTests.Helpers;

namespace SyntheticTests.Features.SettingsDashboard
{
    /// <summary>
    /// Live Revit STA smoke tests verifying <see cref="HostSettingsRegistrar.EnsureRegistered"/>
    /// and dynamic <see cref="SettingsDashboardWindow"/> composition across all 9 tabs
    /// without <see cref="System.IO.IOException"/> resource crashes or WPF <c>BindingExpression</c> errors.
    /// </summary>
    [TestFixture]
    [RevitSmokeTest]
    public class SettingsDashboardSmokeRevitTests
    {
        private UIApplication? _uiapp;
        private bool _previousSuppressUi;
        private bool _previousSuppressDiskFallback;

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
        public void OneTimeSetup(UIApplication uiapp)
        {
            _uiapp = uiapp;
            HostSettingsRegistrar.EnsureRegistered();
            TestWorkerJournalMarker.EnsureWritten(uiapp);
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
            var listener = new CapturingTraceListener();
            TraceSource dataBindingSource = PresentationTraceSources.DataBindingSource;
            SourceLevels previousLevel = dataBindingSource.Switch.Level;
            dataBindingSource.Switch.Level = SourceLevels.Error;
            dataBindingSource.Listeners.Add(listener);

            try
            {
                PresentationTraceSources.Refresh();
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

                Assert.IsEmpty(
                    listener.Messages,
                    "Expected zero WPF DataBinding errors across all 9 Settings Dashboard tabs:" + Environment.NewLine
                    + string.Join(Environment.NewLine, listener.Messages));
            }
            finally
            {
                dataBindingSource.Listeners.Remove(listener);
                dataBindingSource.Switch.Level = previousLevel;

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
