using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using NUnit.Framework;
using Synthetic.Features.ViewManagement.Settings;
using Synthetic.Platform.Host;
using SyntheticTests.Fixtures;
using SyntheticTests.Helpers;

namespace SyntheticTests.Features.ViewManagement
{
    /// <summary>
    /// Live Revit STA smoke test verifying WPF theme and binding cleanliness for <see cref="ViewAutoNumWizardWindow"/>.
    /// </summary>
    [TestFixture]
    [RevitSmokeTest]
    public class ViewManagementSmokeRevitTests
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
                    "ViewManagementSmokeRevitTests leaked open Revit document(s).");
            }
        }

        [Test]
        public void ViewAutoNumWizardWindow_ConstructsShowsAndBinds_WithoutIOExceptionOrBindingErrors()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            IntPtr ownerHandle = _uiapp!.MainWindowHandle;

            Document? doc = null;
            using (var harness = new WpfBindingSmokeHarness())
            {
                try
                {
                    doc = _uiapp.Application.NewProjectDocument(UnitSystem.Metric);
                    var vm = new ViewAutoNumWizardViewModel(doc, new ViewAutoNumSettings().Defaults());
                    WpfBindingSmokeHarness.ExerciseWindow(
                        new ViewAutoNumWizardWindow(ownerHandle) { DataContext = vm },
                        clearDataContextOnClose: true);
                    harness.AssertNoBindingErrors("ViewAutoNumWizardWindow");
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
