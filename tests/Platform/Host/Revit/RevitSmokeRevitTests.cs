using System;
using Autodesk.Revit.UI;
using NUnit.Framework;
using Synthetic.Platform.Host;
using SyntheticTests.Fixtures;
using SyntheticTests.Helpers;

namespace SyntheticTests.Platform.Host
{
    [TestFixture]
    [RevitSmokeTest]
    public class RevitSmokeRevitTests
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
                    "RevitSmokeRevitTests leaked open Revit document(s).");
            }
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

            // Resolve types dynamically via reflection based on the live Revit VersionNumber
            string revitVersion = _uiapp!.Application.VersionNumber;
            
            var vmType = Type.GetType($"Synthetic.Features.StandardsManagement.ViewModels.ProjectStandardsDashboardViewModel, Synthetic{revitVersion}");
            Assert.IsNotNull(vmType, $"ProjectStandardsDashboardViewModel type could not be loaded for Revit version {revitVersion}.");

            var windowType = Type.GetType($"Synthetic.Features.StandardsManagement.Views.ProjectStandardsDashboardWindow, Synthetic{revitVersion}");
            Assert.IsNotNull(windowType, $"ProjectStandardsDashboardWindow type could not be loaded for Revit version {revitVersion}.");

            var fakeDialog = new FakeFileDialogService();

            var suffix = $", Synthetic{revitVersion}";
            var coreSuffix = ", Synthetic.Core";
            var guardrail = new SyntheticTests.Modules.StandardsManagement.FakeGuardrailPromptService();
            var userPromptService = new SyntheticTests.Modules.StandardsManagement.FakeUserPromptService();

            var exportServiceType = Type.GetType($"Synthetic.Features.StandardsManagement.StandardsExportService{suffix}");
            var exportService = Activator.CreateInstance(exportServiceType, guardrail, fakeDialog);

            var findReplaceType = Type.GetType($"Synthetic.Features.StandardsManagement.FindReplaceService{coreSuffix}");
            var findReplaceService = Activator.CreateInstance(findReplaceType);

            var serializationEngineType = Type.GetType($"Synthetic.RevitDOM.Operations.StandardSerializationEngine{suffix}");
            var serializationEngine = Activator.CreateInstance(serializationEngineType);

            var revitIdentityType = Type.GetType($"Synthetic.RevitDOM.Translation.RevitIdentityService{suffix}");
            var revitIdentity = Activator.CreateInstance(revitIdentityType);

            var orchestratorType = Type.GetType($"Synthetic.Features.StandardsManagement.StandardsExtractionOrchestrator{suffix}");
            var orchestrator = Activator.CreateInstance(orchestratorType, revitIdentity, serializationEngine);

            var pocoIdentityType = Type.GetType($"Synthetic.RevitDOM.Translation.PocoIdentityService{coreSuffix}");
            var pocoIdentityService = Activator.CreateInstance(pocoIdentityType);

            var diffEngineType = Type.GetType($"Synthetic.RevitDOM.Operations.Diffing.PocoToRevitDiffEngine{suffix}");
            var diffEngine = Activator.CreateInstance(diffEngineType, revitIdentity);

            var revitFamilyEnforcerType = Type.GetType($"Synthetic.Features.StandardsManagement.RevitFamilyEnforcer{suffix}");
            var revitFamilyEnforcer = Activator.CreateInstance(revitFamilyEnforcerType, serializationEngine);

            var pipelineType = Type.GetType($"Synthetic.Features.StandardsManagement.StandardsExecutionPipeline{suffix}");
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

            using (var harness = new WpfBindingSmokeHarness())
            {
                var window = Activator.CreateInstance(windowType, _uiapp!.MainWindowHandle);

                Assert.IsNotNull(vm, "ViewModel could not be instantiated.");
                Assert.IsNotNull(window, "Window could not be instantiated.");

                // Set DataContext
                var dataContextProp = windowType.GetProperty("DataContext");
                Assert.IsNotNull(dataContextProp, "DataContext property should exist on the window.");
                dataContextProp!.SetValue(window, vm);

                WpfBindingSmokeHarness.ExerciseWindow((System.Windows.Window)window!, clearDataContextOnClose: true);
                harness.AssertNoBindingErrors("ProjectStandardsDashboardWindow");
            }
        }

        private class FakeFileDialogService : Synthetic.Platform.UI.IFileDialogService
        {
            public string? OpenFileDialog(string filter, string title, string defaultName) => null;
            public string? SaveFileDialog(string filter, string title, string defaultName) => null;
        }
    }
}

