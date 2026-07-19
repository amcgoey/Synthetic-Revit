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

            var fakeDialog = new FakeFileDialogService();

            var suffix = $", Synthetic{revitVersion}";
            var guardrail = new SyntheticTests.Modules.StandardsManagement.FakeGuardrailPromptService();
            var userPromptService = new SyntheticTests.Modules.StandardsManagement.FakeUserPromptService();

            var exportServiceType = Type.GetType($"Synthetic.RevitDOM.Operations.Standards.StandardsExportService{suffix}");
            var exportService = Activator.CreateInstance(exportServiceType, guardrail, fakeDialog);

            var findReplaceType = Type.GetType($"Synthetic.RevitDOM.Operations.Standards.FindReplaceService{suffix}");
            var findReplaceService = Activator.CreateInstance(findReplaceType);

            var serializationEngineType = Type.GetType($"Synthetic.Modules.RevitDOM.StandardSerializationEngine{suffix}");
            var serializationEngine = Activator.CreateInstance(serializationEngineType);

            var revitIdentityType = Type.GetType($"Synthetic.Modules.RevitDOM.RevitIdentityService{suffix}");
            var revitIdentity = Activator.CreateInstance(revitIdentityType);

            var orchestratorType = Type.GetType($"Synthetic.RevitDOM.Operations.Standards.StandardsExtractionOrchestrator{suffix}");
            var orchestrator = Activator.CreateInstance(orchestratorType, revitIdentity, serializationEngine);

            var pocoIdentityType = Type.GetType($"Synthetic.Modules.RevitDOM.PocoIdentityService{suffix}");
            var pocoIdentityService = Activator.CreateInstance(pocoIdentityType);

            var diffEngineType = Type.GetType($"Synthetic.RevitDOM.Operations.Diffing.PocoToRevitDiffEngine{suffix}");
            var diffEngine = Activator.CreateInstance(diffEngineType, revitIdentity);

            var revitFamilyEnforcerType = Type.GetType($"Synthetic.RevitDOM.Operations.Standards.RevitFamilyEnforcer{suffix}");
            var revitFamilyEnforcer = Activator.CreateInstance(revitFamilyEnforcerType, serializationEngine);

            var pipelineType = Type.GetType($"Synthetic.RevitDOM.Operations.Standards.StandardsExecutionPipeline{suffix}");
            var pipeline = Activator.CreateInstance(pipelineType, serializationEngine, exportService, revitFamilyEnforcer);

            var vm = Activator.CreateInstance(vmType, 
                _uiapp!, 
                fakeDialog, 
                exportService, 
                null, 
                userPromptService, 
                findReplaceService, 
                orchestrator, 
                pocoIdentityService, 
                diffEngine, 
                serializationEngine, 
                pipeline);

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

