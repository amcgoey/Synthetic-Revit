# Development Brief 015-4 - Refactor ViewModel Staging to use POCO Graph

## **Metadata & Alignment**

- **Parent PRD:** PRD 015 - Project Standards Source Model Nested Dependencies
- **Vertical Slice Issue:** Issue 015-4
- **Summary:** Completely replace the native Revit dependency harvesting in the staging queue with a fast, in-memory POCO graph lookup that works universally for both JSON and Live Revit sources.
## Implementation Strategy

### Strategic Intent

Currently, when a user clicks "Add to Queue" (ExecutePushToQueue in ProjectStandardsDashboardViewModel), the code checks if it's a live Revit source and then calls ExportStylesViewModel.HarvestDependencies, passing native Elements. This breaks the JSON file staging workflow (which has no live elements) and couples the UI to the database.
We will replace this entirely. The staging logic will now treat the SelectedSource tab's POCO hierarchy as the single source of truth. By scanning the selected POCOs for dependencies and doing an O(1) dictionary lookup against the Source Tab's pool, we can instantly stage dependencies regardless of where the data originated.
### What to Build

- **Remove Native Harvesting Logic:**
  - In ProjectStandardsDashboardViewModel.cs, locate the ExecutePushToQueue method.
  - Delete the block of code that instantiates native Elements and calls ExportStylesViewModel.HarvestDependencies(elementsToProcess, doc).
  - Also remove the complex re-extraction logic (StandardSerializationEngine...) that was happening inside this block just to serialize the harvested dependencies.
- **Implement POCO-Driven Staging Algorithm:**
  - Inside ExecutePushToQueue (or a private helper), build a temporary lookup pool of all elements currently available in the SelectedSource tab:
    - var sourcePool = new Dictionary<string, ObjectModel>(StringComparer.OrdinalIgnoreCase);
    - Iterate through SelectedSource.SourceHierarchy (you can reuse GetElementModelsFromHierarchy) and populate the dictionary.
    - **Key Strategy:** Use model.UniqueId if available, otherwise fallback to model.Class + "|" + model.Name.
  - Initialize a Queue<QueueItemModel> or standard list to process the user's checkedItems.
  - **The Staging Loop:**
    - For each staged item:
      - Run RevitDomDependencyScanner.Scan(item.Model).
      - For each yielded ElementIdModel dependency:
        - Construct the lookup key (idModel.UniqueId or idModel.Class + "|" + idModel.Name).
        - If sourcePool.TryGetValue(key, out var dependencyPoco):
          - Check if dependencyPoco is already in the ActionQueue (to prevent duplicates).
          - If not, create a new QueueItemModel for it.
          - **CRITICAL:** Set dependencyPoco.DependencyOrigin = item.Model.Name so the UI badge appears.
          - Add the new QueueItemModel to the ActionQueue.
          - *Optional:* Add this new dependency to your processing queue so you can recursively stage *its* dependencies too.
### Data & State Flow

- **Trigger:** User checks items in the UI and clicks "Add to Queue".
- **Pool Creation:** Active Source Tab hierarchy -> flattened Dictionary.
- **Scan & Match:** Checked POCOs -> Scanner -> ElementIdModels -> Dictionary Lookup.
- **Stage:** Matches are staged into the ActionQueue with the DependencyOrigin flag set.
### Tactical Anti-Scope

- **DO NOT** use the Document object or IIdentityService in this method. Staging is now a 100% disconnected, offline operation.
- **DO NOT** modify ExportStylesViewModel.HarvestDependencies (we are just stopping the Dashboard from calling it; it may still be needed by the legacy export button for now).
### Acceptance Criteria

- [ ] Delete native HarvestDependencies calls inside ProjectStandardsDashboardViewModel.ExecutePushToQueue.
- [ ] Implement the O(1) source pool dictionary lookup for dependency matching.
- [ ] Integrate RevitDomDependencyScanner.Scan to trigger the lookups.
- [ ] Ensure recursively found dependencies are staged with DependencyOrigin correctly populated.
## Workflow & Version Control

- **Branching:** Create a new branch scoped specifically to the prd (e.g., feature/015-source-model-nested-dependencies).
- **Commits:** Commit frequently. Create a commit after completing each discrete, logical step of your implementation plan rather than waiting until the end.
## Testing & Seams

- **Testing Seam:** This logic exists within the ProjectStandardsDashboardViewModel, which is heavily covered by Tier 1 tests.
- **Tests to Write:** Add a test to ProjectStandardsDashboardTests.cs.
- **Scenarios to Cover:**
  - **JSON Dependency Staging:** Mock a JSON-based ProjectStandardsSourceViewModel containing a WallType POCO and a Material POCO (where the WallType references the Material). Select *only* the WallType, execute PushToQueue, and assert that *both* the WallType and the Material end up in the ActionQueue, and that the Material has its DependencyOrigin populated.
