using System;
using Autodesk.Revit.UI;
using NUnit.Framework;
using Synthetic.Features.Worksets.Settings;
using Synthetic.Platform.Host;
using SyntheticTests.Fixtures;
using SyntheticTests.Helpers;
using SyntheticTests.Modules.StandardsManagement;

namespace SyntheticTests.Features.Worksets
{
    /// <summary>
    /// Live Revit STA smoke test verifying WPF theme and binding cleanliness for <see cref="WorksetWizardWindow"/>.
    /// </summary>
    [TestFixture]
    [RevitSmokeTest]
    public class WorksetsSmokeRevitTests
    {
        private UIApplication? _uiapp;

        [OneTimeSetUp]
        public void Setup(UIApplication uiapp)
        {
            _uiapp = uiapp;
            HostSettingsRegistrar.EnsureRegistered();
            TestWorkerJournalMarker.EnsureWritten(uiapp);
        }

        [Test]
        public void WorksetWizardWindow_ConstructsShowsAndBinds_WithoutIOExceptionOrBindingErrors()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            IntPtr ownerHandle = _uiapp!.MainWindowHandle;

            using (var harness = new WpfBindingSmokeHarness())
            {
                var vm = new WorksetWizardViewModel(
                    new WorksetSettings(string.Empty, string.Empty, string.Empty),
                    new FakeFileDialogService());
                WpfBindingSmokeHarness.ExerciseWindow(
                    new WorksetWizardWindow(ownerHandle) { DataContext = vm },
                    clearDataContextOnClose: true);
                harness.AssertNoBindingErrors("WorksetWizardWindow");
            }
        }
    }
}
