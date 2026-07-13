using System;
using Autodesk.Revit.UI;
using NUnit.Framework;

namespace SyntheticTests
{
    [TestFixture]
    public class Tier2_RevitSmokeTests
    {
        private UIApplication? _uiapp;

        [OneTimeSetUp]
        public void Setup(UIApplication uiapp)
        {
            _uiapp = uiapp;
        }

        [Test]
        public void RevitSmokeTest()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            Assert.IsNotNull(_uiapp!.Application, "Revit Application should not be null.");
            System.Console.WriteLine($"Revit API hydrated successfully: {_uiapp.Application.VersionName}");
        }

        [Test]
        public void VerifyCmdProjectStandards_Initialization()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");

            // Resolve types dynamically via reflection based on executing assembly's Revit version suffix
            string assemblyName = System.Reflection.Assembly.GetExecutingAssembly().GetName().Name ?? "";
            string revitVersion = assemblyName.Replace("SyntheticTests", "");
            
            var vmType = Type.GetType($"Synthetic.Modules.StandardsManagement.ViewModels.ProjectStandardsDashboardViewModel, Synthetic{revitVersion}");
            Assert.IsNotNull(vmType, $"ProjectStandardsDashboardViewModel type could not be loaded for Revit version {revitVersion}.");

            var windowType = Type.GetType($"Synthetic.Modules.StandardsManagement.Views.ProjectStandardsDashboardWindow, Synthetic{revitVersion}");
            Assert.IsNotNull(windowType, $"ProjectStandardsDashboardWindow type could not be loaded for Revit version {revitVersion}.");

            // Instantiate ViewModel and Window
            var fakeDialog = new FakeFileDialogService();
            var vm = Activator.CreateInstance(vmType, _uiapp!, fakeDialog, null);
            var window = Activator.CreateInstance(windowType, _uiapp!.MainWindowHandle);

            Assert.IsNotNull(vm, "ViewModel could not be instantiated.");
            Assert.IsNotNull(window, "Window could not be instantiated.");

            // Set DataContext
            var dataContextProp = windowType.GetProperty("DataContext");
            Assert.IsNotNull(dataContextProp, "DataContext property should exist on the window.");
            dataContextProp.SetValue(window, vm);

            try
            {
                // Show window modelessly to verify XAML parsing & theme loading
                var showMethod = windowType.GetMethod("Show");
                Assert.IsNotNull(showMethod, "Show method should exist on the window.");
                showMethod.Invoke(window, null);

                var isVisibleProp = windowType.GetProperty("IsVisible");
                Assert.IsNotNull(isVisibleProp, "IsVisible property should exist on the window.");
                bool isVisible = (bool)(isVisibleProp.GetValue(window) ?? false);
                Assert.IsTrue(isVisible, "Dashboard window should be visible.");
            }
            finally
            {
                // Close window to clean up
                var closeMethod = windowType.GetMethod("Close");
                Assert.IsNotNull(closeMethod, "Close method should exist on the window.");
                closeMethod.Invoke(window, null);
            }
        }

        private class FakeFileDialogService : Synthetic.Shared.UI.IFileDialogService
        {
            public string? OpenFileDialog(string filter, string title, string defaultName) => null;
            public string? SaveFileDialog(string filter, string title, string defaultName) => null;
        }
    }
}
