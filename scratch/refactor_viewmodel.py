import os
import re

filepath = r"src\SyntheticShared\ViewModels\ExportStylesViewModel.cs"
with open(filepath, "r", encoding="utf-8") as f:
    code = f.read()

# 1. Add using System.Reflection;
code = code.replace(
    "using Application = System.Windows.Application;",
    "using Application = System.Windows.Application;\r\nusing System.Reflection;"
)

# 2. Constructor changes: Add ClassSelection instantiation
code = code.replace(
    "CategorySelection = new CategorySelectionViewModel(_doc);",
    "CategorySelection = new CategorySelectionViewModel(_doc);\r\n            ClassSelection = new StandardsClassSelectionViewModel(_doc);"
)

# 3. Remove properties definition block
# Starts with _exportTextNoteTypes backing field and ends with ExportParameterElements property.
prop_start_idx = code.find("private bool _exportTextNoteTypes;")
prop_end_idx = code.find("public bool ExportParameterElements")
# find the closing brace of ExportParameterElements
prop_end_block_idx = code.find("}", prop_end_idx) + 1

if prop_start_idx != -1 and prop_end_idx != -1:
    code_before = code[:prop_start_idx]
    code_after = code[prop_end_block_idx:]
    code = code_before + "\r\n        public StandardsClassSelectionViewModel ClassSelection { get; }\r\n" + code_after
else:
    print("Error: Could not find boolean properties block boundaries.")

# 4. Remove SelectAllAnnotationsCommand through SelectNoneStandardsCommand declarations
cmd_start_idx = code.find("public ICommand SelectAllAnnotationsCommand { get; }")
cmd_end_idx = code.find("public ICommand SelectNoneStandardsCommand { get; }")
cmd_end_block_idx = code.find("\n", cmd_end_idx) + 1

if cmd_start_idx != -1 and cmd_end_idx != -1:
    code = code[:cmd_start_idx] + code[cmd_end_block_idx:]
else:
    print("Error: Could not find commands declarations block boundaries.")

# 5. Remove commands initializations in the constructor
cmd_init_block = """            SelectAllAnnotationsCommand = new RelayCommand(_ => ToggleGroupAnnotations(true));
            SelectNoneAnnotationsCommand = new RelayCommand(_ => ToggleGroupAnnotations(false));

            SelectAllMaterialsCommand = new RelayCommand(_ => ToggleGroupMaterials(true));
            SelectNoneMaterialsCommand = new RelayCommand(_ => ToggleGroupMaterials(false));

            SelectAllSystemTypesCommand = new RelayCommand(_ => ToggleGroupSystemTypes(true));
            SelectNoneSystemTypesCommand = new RelayCommand(_ => ToggleGroupSystemTypes(false));

            SelectAllViewsCommand = new RelayCommand(_ => ToggleGroupViews(true));
            SelectNoneViewsCommand = new RelayCommand(_ => ToggleGroupViews(false));

            SelectAllStandardsCommand = new RelayCommand(_ => ToggleGroupStandards(true));
            SelectNoneStandardsCommand = new RelayCommand(_ => ToggleGroupStandards(false));"""

# Remove with standard line endings normalization
code = code.replace(cmd_init_block, "")
code = code.replace(cmd_init_block.replace("\r\n", "\n"), "")

# 6. Remove ToggleGroupAnnotations through ToggleGroupStandards methods
# Starts around "private void ToggleGroupAnnotations" and ends before "#endregion"
toggle_start_idx = code.find("private void ToggleGroupAnnotations")
toggle_end_idx = code.find("#endregion")

if toggle_start_idx != -1 and toggle_end_idx != -1:
    code = code[:toggle_start_idx] + "\r\n        " + code[toggle_end_idx:]
else:
    print("Error: Could not find ToggleGroup methods boundaries.")

# 7. Define the list of 31 properties to replace in the logic
properties = [
    "ExportTextNoteTypes", "ExportLabelTypes", "ExportDimensionTypes", "ExportFilledRegionTypes",
    "ExportMaterials", "ExportWallTypes", "ExportFloorTypes", "ExportRoofTypes", "ExportCeilingTypes",
    "ExportRailingTypes", "ExportStairsTypes", "ExportStandardViews", "ExportViewTemplates",
    "ExportLineStyles", "ExportModelCategories", "ExportAnnotationCategories", "ExportAnalyticalCategories",
    "ExportImportCategories", "ExportGridTypes", "ExportLevelTypes", "ExportFillPatterns",
    "ExportLinePatterns", "ExportAppearanceAssets", "ExportCurtainSystemTypes", "ExportMullionTypes",
    "ExportFasciaTypes", "ExportGutterTypes", "ExportTitleBlockTypes", "ExportViewFamilyTypes",
    "ExportBrowserOrganizations", "ExportParameterElements"
]

# Replace boolean checks in loops/methods with ClassSelection.Export...
# We want to be careful to match only whole words of properties.
for prop in properties:
    code = re.sub(r"\b" + prop + r"\b", "ClassSelection." + prop, code)

# 8. Replace CategorySelection.GetCheckedCategoryIds() with ClassSelection.GetCheckedCategoryIds()
code = code.replace(
    "CategorySelection.GetCheckedCategoryIds()",
    "ClassSelection.GetCheckedCategoryIds()"
)

# 9. Update the telemetry block to use reflection
old_telemetry = """                // [AG2_TEST_START: ExportCategoriesCountQA]
                // REVERT_METHOD: To remove, safely delete this entire block.
                int checkedCount = ClassSelection.GetCheckedCategoryIds().Count;
                Console.WriteLine($"Jrn.Directive \\"SyntheticQA\\", \\"ExportCategoriesCount: [{checkedCount}]\\"");
                // [AG2_TEST_END: ExportCategoriesCountQA]"""

# Also match old telemetry with CategorySelection (before our regex replacement changed it to ClassSelection)
old_telemetry_cat = """                // [AG2_TEST_START: ExportCategoriesCountQA]
                // REVERT_METHOD: To remove, safely delete this entire block.
                int checkedCount = CategorySelection.GetCheckedCategoryIds().Count;
                Console.WriteLine($"Jrn.Directive \\"SyntheticQA\\", \\"ExportCategoriesCount: [{checkedCount}]\\"");
                // [AG2_TEST_END: ExportCategoriesCountQA]"""

new_telemetry = """                // [AG2_TEST_START: ExportCategoriesCountQA]
                // REVERT_METHOD: To remove, safely delete this entire block.
                int activeClassCount = ClassSelection.GetType()
                    .GetProperties()
                    .Where(p => p.PropertyType == typeof(bool) && p.Name.StartsWith("Export"))
                    .Count(p => (bool)p.GetValue(ClassSelection));
                Console.WriteLine($"Jrn.Directive \\"SyntheticQA\\", \\"ExportCategoriesCount: [{activeClassCount}]\\"\");
                // [AG2_TEST_END: ExportCategoriesCountQA]"""

code = code.replace(old_telemetry, new_telemetry)
code = code.replace(old_telemetry_cat, new_telemetry)
code = code.replace(old_telemetry.replace("\r\n", "\n"), new_telemetry)
code = code.replace(old_telemetry_cat.replace("\r\n", "\n"), new_telemetry)

with open(filepath, "w", encoding="utf-8") as f:
    f.write(code)

print("Success: Refactoring completed.")
