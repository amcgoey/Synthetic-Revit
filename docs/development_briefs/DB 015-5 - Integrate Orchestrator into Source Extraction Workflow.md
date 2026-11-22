# Development Brief 015-5 - Integrate Orchestrator into Source Extraction Workflow

## **Metadata & Alignment**

- **Parent PRD:** PRD 015 - Project Standards Source Model Nested Dependencies
- **Vertical Slice Issue:** Issue 015-5
- **Summary:** Wire the new StandardsExtractionOrchestrator into the ProjectStandardsDashboardViewModel's extraction pipeline, replacing the legacy recursive native scanning logic.
## Implementation Strategy

### Strategic Intent

We are replacing the legacy extraction logic with our new POCO-driven DAG loop. The ProjectStandardsDashboardViewModel will remain responsible for gathering the initial root elements using its existing FilteredElementCollector logic. However, instead of the ViewModel directly serializing these elements and manually executing native recursive family scans (ScanFamilyRecursively), it will pass the gathered root elements to the StandardsExtractionOrchestrator. The Orchestrator will handle serialization, finding nested dependencies, and returning the fully resolved flat list of POCOs.
### What to Build

- **Inject the Orchestrator:**
  - Update the constructors of ProjectStandardsDashboardViewModel to accept IStandardsExtractionOrchestrator? extractionOrchestrator = null.
  - In the initialization logic, if the orchestrator is null, instantiate the default: _orchestrator = new StandardsExtractionOrchestrator(new RevitIdentityService());
- **Refactor Initial Element Gathering (ExtractElementsFromDoc):**
  - Currently, ExtractElementsFromDoc (and related methods) use FilteredElementCollectors to find elements and immediately serializes them using StandardSerializationEngine.
  - Refactor this logic to simply return a List<Element> (the raw native Revit elements) based on the user's selectedGroupings.
- **Wire the Orchestrator (ExecuteAddRevitModel / ExtractRevitElements):**
  - In the extraction execution block, call your refactored gathering method to get the rootElements.
  - Pass these rootElements to the orchestrator: var extractedPocos = _orchestrator.Extract(doc, rootElements, progressReporter);
  - Remove the legacy ScanFamilyRecursively logic entirely. The orchestrator's DAG loop now natively handles finding and pulling in nested dependencies.
  - Pass the resulting extractedPocos to StandardsHierarchyUtility.BuildHierarchy to populate the Source Tab.
### Data & State Flow

- **Trigger:** User clicks "OK" in the Add Revit Model dialog.
- **Gather:** ViewModel uses FilteredElementCollectors to gather List<Element> rootElements.
- **Orchestrate:** _orchestrator.Extract(doc, rootElements) serializes the elements and recursively fetches all nested dependencies via the POCO DAG loop.
- **Display:** The returned List<ObjectModel> is passed to StandardsHierarchyUtility.BuildHierarchy to render the UI tree.
### Tactical Anti-Scope

- **DO NOT** move the FilteredElementCollector logic out of the ProjectStandardsDashboardViewModel in this issue. Refactoring the gathering logic into a separate Harvester submodule is out of scope for this slice.
- **DO NOT** attempt to alter the behavior of offline JSON source loading. This issue strictly targets the "Add Revit Model" (live document) workflow.
### Acceptance Criteria

- [ ] Inject IStandardsExtractionOrchestrator into the Dashboard ViewModel.
- [ ] Refactor element gathering to yield native Elements instead of serializing them directly.
- [ ] Replace the legacy native extraction and ScanFamilyRecursively logic with a call to the Orchestrator.
- [ ] Verify that adding a Revit Model successfully populates the Source Pane with both the root elements and their nested dependencies.
## Workflow & Version Control

- **Branching:** Create a new branch scoped specifically to the prd (e.g., feature/015-source-model-nested-dependencies).
- **Commits:** Commit frequently. Create a commit after completing each discrete, logical step of your implementation plan rather than waiting until the end.
## Testing & Seams

- **Testing Seam:** The Orchestrator injection is the primary seam.
- **Tests to Write:** Update or add tests in ProjectStandardsDashboardTests.cs (Tier 1) and Tier2_StandardsExtractionOrchestratorTests.cs (Tier 2).
- **Scenarios to Cover:**
  - **Headless Injection:** Verify the ViewModel can be instantiated with a mocked IStandardsExtractionOrchestrator to ensure testability without a Revit database.
  - **End-to-End Extraction:** Run a Tier 2 test simulating ExecuteAddRevitModel and assert that the resulting POCO list contains correctly tagged dependencies (validating the Orchestrator was successfully invoked).
