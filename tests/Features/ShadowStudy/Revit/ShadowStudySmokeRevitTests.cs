using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Synthetic.Features.SettingsDashboard.ViewModels;
using Synthetic.Features.SettingsDashboard.Views;
using Synthetic.Features.ShadowStudy.Commands;
using Synthetic.Features.ShadowStudy.Models;
using Synthetic.Features.ShadowStudy.Settings;
using Synthetic.Features.ShadowStudy.ViewModels;
using Synthetic.Features.ShadowStudy.Views;
using Synthetic.Platform.Host;
using Synthetic.Platform.Settings;
using Synthetic.Platform.UI;
using SyntheticTests.Fixtures;
using SyntheticTests.Helpers;

namespace SyntheticTests.Features.ShadowStudy
{
    /// <summary>
    /// Live Revit STA smoke tests verifying ribbon command resolution, Settings Dashboard tab composition,
    /// and WPF binding/resource cleanliness for the <c>ShadowStudy</c> feature slice.
    /// </summary>
    [TestFixture]
    [RevitSmokeTest]
    public class ShadowStudySmokeRevitTests
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
        public void OneTimeSetUp(UIApplication uiapp)
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
        public void RibbonCommand_CmdShadowStudy_ResolvesInAddinAssemblyAndRibbonConfig()
        {
            Type cmdType = typeof(CmdShadowStudy);
            Assert.IsTrue(typeof(IExternalCommand).IsAssignableFrom(cmdType));
            Assert.AreEqual("Synthetic.Features.ShadowStudy.Commands.CmdShadowStudy", cmdType.FullName);

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
        public void SettingsDashboardWindow_ShadowStudyTab_RendersWithoutIOExceptionOrBindingErrors()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            HostSettingsRegistrar.EnsureRegistered();

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

                var dashboardVm = new SettingsDashboardViewModel(doc, ownerHandle);
                ISettingModuleViewModel? shadowTab = dashboardVm.SettingModules
                    .FirstOrDefault(m => string.Equals(m.ModuleName, "Shadow Study", StringComparison.Ordinal));
                Assert.IsNotNull(shadowTab, "Expected 'Shadow Study' tab in SettingsDashboardViewModel.");
                Assert.IsInstanceOf<ShadowStudySettingsViewModel>(shadowTab);

                window = new SettingsDashboardWindow(ownerHandle)
                {
                    DataContext = dashboardVm
                };
                window.Show();

                dashboardVm.SelectedModule = shadowTab;
                window.UpdateLayout();
                Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.Loaded, new Action(() => { }));
                Assert.IsTrue(window.IsVisible, "SettingsDashboardWindow should remain visible on the Shadow Study tab.");

                Assert.IsEmpty(
                    listener.Messages,
                    "Expected zero WPF DataBinding errors on SettingsDashboardWindow Shadow Study tab:" + Environment.NewLine
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

        [Test]
        public void ShadowStudyViews_ConstructShowAndBindInSta_WithoutIOExceptionOrBindingErrors()
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

                // 1. ShadowStudyWindow
                var adapter = new FakeShadowStudyAdapter();
                adapter.EligibleViews.Add(new ShadowStudyViewItem
                {
                    Id = "view-1",
                    Name = "Level 1 - Floor Plan",
                    ViewType = "FloorPlan",
                    IsCropped = false,
                    Warnings =
                    {
                        new ShadowStudyViewWarningBadge
                        {
                            Code = "Uncropped",
                            Label = "Uncropped View",
                            Description = "Crop View is off."
                        }
                    }
                });
                var folderDialog = new FakeFolderDialogService();
                var studyVm = new ShadowStudyViewModel(adapter, folderDialog, new DateTime(2026, 6, 21));
                studyVm.ExportRangeMode = ShadowStudyExportRangeMode.SelectedViews;
                ExerciseWindow(new ShadowStudyWindow(ownerHandle) { DataContext = studyVm });

                // 2. ShadowStudySettingsView hosted inside an STA Window
                var stored = new ShadowStudySettings
                {
                    UserPresets = new List<ShadowStudyPresetModel>
                    {
                        new ShadowStudyPresetModel
                        {
                            Name = "Smoke Preset",
                            Dates = new List<ShadowStudyPresetDateSpec>
                            {
                                new ShadowStudyPresetDateSpec
                                {
                                    AstronomicalEvent = AstronomicalEventKind.SummerSolstice
                                }
                            }
                        }
                    }
                };
                var settingsVm = new ShadowStudySettingsViewModel(
                    () => stored,
                    s => stored = s.DeepClone(),
                    () => { },
                    () => true);
                var settingsView = new ShadowStudySettingsView { DataContext = settingsVm };
                var hostWindow = new Window
                {
                    Title = "ShadowStudySettingsView Smoke Host",
                    Content = settingsView,
                    Width = 540,
                    Height = 680,
                    ShowInTaskbar = false
                };
                RevitWindowHelper.SetOwner(hostWindow, ownerHandle);
                ExerciseWindow(hostWindow);

                Assert.IsEmpty(
                    listener.Messages,
                    "Expected zero WPF DataBinding errors across ShadowStudy views:" + Environment.NewLine
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
                Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.Loaded, new Action(() => { }));
                Assert.IsTrue(window.IsVisible, $"{window.GetType().Name} should show cleanly without resource exceptions.");
            }
            finally
            {
                window.DataContext = null;
                window.Close();
            }
        }
    }
}
