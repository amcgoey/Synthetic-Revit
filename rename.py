import os

files_to_update = [
    r"src\SyntheticShared\SyntheticShared.projitems",
    r"src\SyntheticShared\Modules\StandardsManagement\ViewModels\StagingQueueViewModel.cs",
    r"src\SyntheticShared\Modules\StandardsManagement\ViewModels\StandardsExecutionPipelineViewModel.cs",
    r"src\SyntheticShared\Modules\StandardsManagement\ViewModels\ParameterWrapperVM.cs",
    r"src\SyntheticShared\Modules\StandardsManagement\ViewModels\ProjectStandardsDashboardViewModel.cs",
    r"src\SyntheticShared\Modules\StandardsManagement\Views\ProjectStandardsDashboardWindow.xaml",
    r"tests\SyntheticTests.Logic\Modules\StandardsManagement\DashboardFindReplaceTests.cs",
    r"tests\SyntheticTests.Logic\Modules\StandardsManagement\DashboardParametersEditorTests.cs",
    r"tests\SyntheticTests.Logic\Modules\StandardsManagement\DashboardTabAndFamilyFilterTests.cs",
    r"tests\SyntheticTests.Logic\Modules\StandardsManagement\DependencyOriginTests.cs",
    r"tests\SyntheticTests.Logic\Modules\StandardsManagement\PhasedExecutionTests.cs",
    r"tests\SyntheticTests.Logic\Modules\StandardsManagement\ProjectStandardsDashboardTests.cs",
    r"tests\SyntheticTests.Logic\Modules\StandardsManagement\QueueMergeTests.cs",
    r"tests\SyntheticTests.Shared\Modules\StandardsManagement\Tier2_DashboardIntegrationTests.cs"
]

replacements = {
    "ActionQueueViewModel.cs": "StagingQueueViewModel.cs",
    "StandardsExecutionViewModel.cs": "StandardsExecutionPipelineViewModel.cs",
    "ActionQueueViewModel": "StagingQueueViewModel",
    "StandardsExecutionViewModel": "StandardsExecutionPipelineViewModel",
    "ActionQueue": "StagingQueue",
    "actionQueue": "stagingQueue"
}

for file_path in files_to_update:
    if not os.path.exists(file_path):
        print(f"Skipping {file_path}, does not exist.")
        continue
    
    with open(file_path, "r", encoding="utf-8-sig") as f:
        content = f.read()
    
    original_content = content
    for old, new in replacements.items():
        content = content.replace(old, new)
        
    if content != original_content:
        with open(file_path, "w", encoding="utf-8-sig") as f:
            f.write(content)
        print(f"Updated {file_path}")
