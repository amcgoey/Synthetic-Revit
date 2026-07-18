import os
import re

def update_staging_queue():
    path = r"src\SyntheticShared\Modules\StandardsManagement\ViewModels\StagingQueueViewModel.cs"
    with open(path, "r", encoding="utf-8-sig") as f:
        content = f.read()

    # Add using DiffEngine
    if "using Synthetic.Modules.DiffEngine;" not in content:
        content = content.replace("using Synthetic.Modules.StandardsManagement.Engine;", "using Synthetic.Modules.StandardsManagement.Engine;\nusing Synthetic.Modules.DiffEngine;\nusing Autodesk.Revit.DB;")

    # Add private field
    if "_diffEngine" not in content:
        content = content.replace("private readonly IPocoIdentityService _pocoIdentityService;", "private readonly IPocoIdentityService _pocoIdentityService;\n        private readonly IDiffEngine<IEnumerable<ObjectModel>, Document> _diffEngine;")
    
    # Update constructor
    content = content.replace(
        "public StagingQueueViewModel(ProjectStandardsDashboardViewModel parent, IPocoIdentityService pocoIdentityService)",
        "public StagingQueueViewModel(ProjectStandardsDashboardViewModel parent, IPocoIdentityService pocoIdentityService, IDiffEngine<IEnumerable<ObjectModel>, Document> diffEngine)"
    )
    
    if "_diffEngine =" not in content:
        content = content.replace(
            "_pocoIdentityService = pocoIdentityService ?? throw new ArgumentNullException(nameof(pocoIdentityService));",
            "_pocoIdentityService = pocoIdentityService ?? throw new ArgumentNullException(nameof(pocoIdentityService));\n            _diffEngine = diffEngine ?? throw new ArgumentNullException(nameof(diffEngine));"
        )
    
    # Update ExecuteDiff
    content = content.replace(
        "var clusters = StandardsDiffEngine.RunDeepScan(_parent.Document, elementPocos, _parent.SerializationEngine);",
        "var clusters = _diffEngine.Compare(elementPocos, _parent.Document);"
    )

    with open(path, "w", encoding="utf-8-sig") as f:
        f.write(content)

def update_execution_pipeline():
    path = r"src\SyntheticShared\Modules\StandardsManagement\ViewModels\StandardsExecutionPipelineViewModel.cs"
    with open(path, "r", encoding="utf-8-sig") as f:
        content = f.read()

    # Update constructor
    content = content.replace(
        "public StandardsExecutionPipelineViewModel(ProjectStandardsDashboardViewModel parent)",
        "public StandardsExecutionPipelineViewModel(ProjectStandardsDashboardViewModel parent, IStandardsExecutionPipeline pipeline)"
    )
    
    content = content.replace(
        "_pipeline = parent.Pipeline;",
        "_pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));"
    )
    
    with open(path, "w", encoding="utf-8-sig") as f:
        f.write(content)

def update_dashboard():
    path = r"src\SyntheticShared\Modules\StandardsManagement\ViewModels\ProjectStandardsDashboardViewModel.cs"
    with open(path, "r", encoding="utf-8-sig") as f:
        content = f.read()
    
    if "using Synthetic.Modules.DiffEngine;" not in content:
        content = content.replace("using Synthetic.Modules.StandardsManagement.Utilities;", "using Synthetic.Modules.StandardsManagement.Utilities;\nusing Synthetic.Modules.DiffEngine;")

    content = content.replace(
        "_stagingQueueViewModel = new StagingQueueViewModel(this, _pocoIdentityService);",
        "_stagingQueueViewModel = new StagingQueueViewModel(this, _pocoIdentityService, diffEngine);"
    )
    
    content = content.replace(
        "_standardsExecutionViewModel = new StandardsExecutionPipelineViewModel(this);",
        "_standardsExecutionViewModel = new StandardsExecutionPipelineViewModel(this, _pipeline);"
    )
    
    content = content.replace(
        "IPocoIdentityService pocoIdentityService,",
        "IPocoIdentityService pocoIdentityService,\n            IDiffEngine<IEnumerable<ObjectModel>, Document> diffEngine,"
    )

    with open(path, "w", encoding="utf-8-sig") as f:
        f.write(content)

def update_cmd():
    path = r"src\SyntheticShared\Modules\StandardsManagement\Commands\CmdProjectStandards.cs"
    with open(path, "r", encoding="utf-8-sig") as f:
        content = f.read()
    
    if "using Synthetic.Modules.DiffEngine;" not in content:
        content = content.replace("using Synthetic.Modules.StandardsManagement.Engine;", "using Synthetic.Modules.StandardsManagement.Engine;\nusing Synthetic.Modules.DiffEngine;")

    content = content.replace(
        "var pocoIdentityService = new PocoIdentityService();",
        "var pocoIdentityService = new PocoIdentityService();\n                var diffEngine = new PocoToRevitDiffEngine(pocoIdentityService);"
    )
    
    content = content.replace(
        "pocoIdentityService,",
        "pocoIdentityService,\n                    diffEngine,"
    )

    with open(path, "w", encoding="utf-8-sig") as f:
        f.write(content)

try:
    update_staging_queue()
    update_execution_pipeline()
    update_dashboard()
    update_cmd()
    print("Files updated!")
except Exception as e:
    print("Error:", str(e))
