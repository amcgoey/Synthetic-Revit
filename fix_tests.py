import os
import re

def fix_tests():
    test_dir = r"tests\SyntheticTests.Logic\Modules\StandardsManagement"
    test_files = [f for f in os.listdir(test_dir) if f.endswith(".cs")]

    for test_file in test_files:
        path = os.path.join(test_dir, test_file)
        with open(path, "r", encoding="utf-8-sig") as f:
            content = f.read()

        # We want to replace new ProjectStandardsDashboardViewModel(...)
        # with DashboardTestFactory.Create(...)
        
        # Simple string replacements for common patterns
        content = content.replace("new ProjectStandardsDashboardViewModel(_doc, _fakeDialogService, new FakeGuardrailPromptService(), null)", "DashboardTestFactory.Create(_doc, _fakeDialogService, null, null)")
        content = content.replace("new ProjectStandardsDashboardViewModel(_doc, _fakeDialogService, (IStandardsExportService)null, settings)", "DashboardTestFactory.Create(_doc, _fakeDialogService, null, settings)")
        content = content.replace("new ProjectStandardsDashboardViewModel(_doc, _fakeDialogService)", "DashboardTestFactory.Create(_doc, _fakeDialogService)")
        
        if "DashboardTestFactory.Create" in content and "using SyntheticTests.Modules.StandardsManagement;" not in content:
            # We don't need using if it's in the same namespace
            pass

        with open(path, "w", encoding="utf-8-sig") as f:
            f.write(content)

def fix_shared_tests():
    test_dir = r"tests\SyntheticTests.Shared\Modules\StandardsManagement"
    test_files = [f for f in os.listdir(test_dir) if f.endswith(".cs")]

    for test_file in test_files:
        path = os.path.join(test_dir, test_file)
        with open(path, "r", encoding="utf-8-sig") as f:
            content = f.read()

        content = content.replace("new ProjectStandardsDashboardViewModel(_doc, _fakeDialogService, new FakeGuardrailPromptService(), null)", "DashboardTestFactory.Create(_doc, _fakeDialogService, null, null)")
        content = content.replace("new ProjectStandardsDashboardViewModel(_doc, _fakeDialogService)", "DashboardTestFactory.Create(_doc, _fakeDialogService)")

        with open(path, "w", encoding="utf-8-sig") as f:
            f.write(content)

fix_tests()
fix_shared_tests()
