using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Input;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Synthetic.Features.AutoTagger.Commands;
using Synthetic.Features.AutoTagger.Models;
using Synthetic.Features.AutoTagger.ViewModels;
using Synthetic.Features.AutoTagger.Views;
using Synthetic.Platform.Host;
using Synthetic.Platform.UI;
using SyntheticTests.Fixtures;
using SyntheticTests.Helpers;
using SyntheticTests.Modules.StandardsManagement;

namespace SyntheticTests.Features.AutoTagger
{
    /// <summary>
    /// Live Revit STA smoke tests verifying ribbon command resolution and WPF binding/resource
    /// cleanliness for all <c>AutoTagger</c> views.
    /// </summary>
    [TestFixture]
    [RevitSmokeTest]
    public class AutoTaggerSmokeRevitTests
    {
        private UIApplication? _uiapp;
        private int _initialDocCount;

        private sealed class SmokeSetTemplateDataContext
        {
            public string TemplateName { get; set; } = "Smoke Template";
            public string CategoryName { get; set; } = "Doors";
            public string FamilyName { get; set; } = "Single-Flush";
            public string TypeName { get; set; } = "36\" x 84\"";
            public bool IsCategoryScope { get; set; }
            public bool IsFamilyScope { get; set; }
            public bool IsTypeScope { get; set; } = true;
            public bool AllowOrientationChange { get; set; } = true;
            public ICommand SaveCommand { get; } = new RelayCommand(_ => { });
            public ICommand CancelCommand { get; } = new RelayCommand(_ => { });
        }

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
                    "AutoTaggerSmokeRevitTests leaked open Revit document(s).");
            }
        }

        [Test]
        public void RibbonCommands_AutoTagger_ResolveInAddinAssemblyAndRibbonConfig()
        {
            Type batchTagType = typeof(CmdBatchTag);
            Type manageTemplatesType = typeof(CmdManageTemplates);
            Type setTemplateType = typeof(CmdSetTemplate);

            Assert.IsTrue(typeof(IExternalCommand).IsAssignableFrom(batchTagType));
            Assert.IsTrue(typeof(IExternalCommand).IsAssignableFrom(manageTemplatesType));
            Assert.IsTrue(typeof(IExternalCommand).IsAssignableFrom(setTemplateType));

            string configPath = Path.Combine(TestPathHelper.GetProjectRoot(), "src", "Assets", "ribbon_config.json");
            Assert.IsTrue(File.Exists(configPath), $"Expected ribbon_config.json at '{configPath}'.");

            JObject config = JObject.Parse(File.ReadAllText(configPath));
            var registeredClasses = new HashSet<string>(
                config.SelectTokens("$..class")
                    .Select(t => t.Value<string>() ?? string.Empty),
                StringComparer.Ordinal);

            Assert.IsTrue(registeredClasses.Contains(batchTagType.FullName!), $"Expected '{batchTagType.FullName}' in ribbon_config.json.");
            Assert.IsTrue(registeredClasses.Contains(manageTemplatesType.FullName!), $"Expected '{manageTemplatesType.FullName}' in ribbon_config.json.");
        }

        [Test]
        public void AutoTaggerViews_ConstructShowAndBind_WithoutIOExceptionOrBindingErrors()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            IntPtr ownerHandle = _uiapp!.MainWindowHandle;

            Document? doc = null;

            using (var harness = new WpfBindingSmokeHarness())
            {
                try
                {
                    doc = _uiapp.Application.NewProjectDocument(UnitSystem.Metric);

                    // 1. ManageTemplatesView
                    var manageVm = new ManageTemplatesViewModel(
                        doc,
                        new FakeFileDialogService(),
                        new FakeUserPromptService());
                    WpfBindingSmokeHarness.ExerciseWindow(
                        new ManageTemplatesView(ownerHandle) { DataContext = manageVm },
                        clearDataContextOnClose: true);

                    // 2. ResolveConflictsView
                    var conflictTemplateA = new TagTemplate { TemplateName = "Door Tag A" };
                    var conflictTemplateB = new TagTemplate { TemplateName = "Door Tag B" };
                    var conflictsVm = new ResolveConflictsViewModel(new List<ConflictItem>
                    {
                        new ConflictItem
                        {
                            DisplayName = "Doors : Single-Flush",
                            UniqueKey = "Doors|Single-Flush",
                            Candidates = new List<TagTemplate> { conflictTemplateA, conflictTemplateB },
                            SelectedCandidate = conflictTemplateA
                        }
                    });
                    WpfBindingSmokeHarness.ExerciseWindow(
                        new ResolveConflictsView(ownerHandle) { DataContext = conflictsVm },
                        clearDataContextOnClose: true);

                    // 3. SetTemplateView
                    WpfBindingSmokeHarness.ExerciseWindow(
                        new SetTemplateView(ownerHandle) { DataContext = new SmokeSetTemplateDataContext() },
                        clearDataContextOnClose: true);

                    harness.AssertNoBindingErrors("AutoTagger views");
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
