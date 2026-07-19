using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Synthetic.Modules.DiffEngine;
using Synthetic.Modules.MergeDuplicates.Models;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;
using Synthetic.Modules.StandardsManagement.Engine;
using Synthetic.Modules.StandardsManagement.Utilities;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.Shared.UI;
using Synthetic.Settings;
using Synthetic.Infrastructure.Persistence;
using SyntheticTests.Modules.RevitDOM;

namespace SyntheticTests.Modules.StandardsManagement
{
    public static class DashboardTestFactory
    {
        public static ProjectStandardsDashboardViewModel Create(
            Document doc,
            IFileDialogService dialogService = null,
            IStandardsExportService exportService = null,
            StandardsSettings settings = null,
            IUserPromptService userPromptService = null,
            IFindReplaceService findReplaceService = null,
            IStandardsExtractionOrchestrator orchestrator = null,
            IPocoIdentityService pocoIdentityService = null,
            IDiffEngine<IEnumerable<ObjectModel>, Document> diffEngine = null,
            IStandardSerializationEngine serializationEngine = null,
            IStandardsExecutionPipeline pipeline = null)
        {
            dialogService ??= new FakeFileDialogService();
            if (exportService == null) 
            {
                var guardrail = new FakeGuardrailPromptService();
                exportService = new StandardsExportService(guardrail, dialogService);
            }
            userPromptService ??= new FakeUserPromptService();
            findReplaceService ??= new FindReplaceService();
            serializationEngine ??= new StandardSerializationEngine();
            pocoIdentityService ??= new PocoIdentityService();
            diffEngine ??= new PocoToRevitDiffEngine(new FakeIdentityService());
            orchestrator ??= new StandardsExtractionOrchestrator(new FakeIdentityService(), serializationEngine);
            pipeline ??= new StandardsExecutionPipeline(serializationEngine, exportService, new FakeFamilyEnforcer());

            var vm = new ProjectStandardsDashboardViewModel(
                doc,
                dialogService,
                exportService,
                settings,
                userPromptService,
                findReplaceService,
                orchestrator,
                pocoIdentityService,
                diffEngine,
                serializationEngine,
                pipeline
            );

            // Match test setup
            vm.ShowDocumentSelectionDialog = dialogVM =>
            {
                foreach (var docItem in dialogVM.OpenDocuments)
                {
                    docItem.IsSelected = true;
                }
                return true;
            };

            vm.ShowMergeDialog = dialogVM =>
            {
                if (dialogVM.Items != null)
                {
                    var enumerator = dialogVM.Items.GetEnumerator();
                    if (enumerator.MoveNext())
                    {
                        dialogVM.SelectedItem = enumerator.Current;
                    }
                }
                return true;
            };

            return vm;
        }
    }
}
