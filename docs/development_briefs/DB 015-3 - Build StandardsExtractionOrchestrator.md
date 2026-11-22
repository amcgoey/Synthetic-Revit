# Development Brief 015-3 - Build StandardsExtractionOrchestrator

## **Metadata & Alignment**

- **Parent PRD:** PRD 015 - Project Standards Source Model Nested Dependencies
- **Vertical Slice Issue:** Issue 015-3
- **Summary:** Create a dedicated orchestrator service to manage the recursive extraction of Revit standards, automatically harvesting and tagging nested dependencies using a POCO-driven DAG loop.
## Implementation Strategy

### Strategic Intent

To prevent the "God ViewModel" anti-pattern, the ProjectStandardsDashboardViewModel must no longer handle the heavy logic of tracing Revit dependencies. We are building the StandardsExtractionOrchestrator as a dedicated service in the Synthetic.Modules.StandardsManagement.Engine namespace. It will combine the StandardSerializationEngine, the RevitDomDependencyScanner (from 015-1), and the IIdentityService bulk-fetcher (from 015-2) into a safe, recursive extraction loop that guarantees all required nested dependencies are pulled in and properly tagged for the UI.
### What to Build

- **Create the Interface and Service:**
  - Create IStandardsExtractionOrchestrator and StandardsExtractionOrchestrator in Synthetic.Modules.StandardsManagement.Engine.
  - **Constructor Injection:** Inject IIdentityService identityService.
- **Implement the Extraction Loop:**
  - Method Signature: public List<ObjectModel> Extract(Document doc, IEnumerable<Element> rootElements, IProgress<string>? progress = null)
  - **State Tracking:** * Initialize a HashSet<string> visitedIdentities (store UniqueIds or Class+Name keys) to prevent infinite loops.
    - Initialize a Dictionary<string, string> dependencyOrigins (mapping an extracted Identity Key to the Parent POCO's Name).
    - Initialize List<ObjectModel> finalPocos.
  - **The Recursive Batch Loop:**
    - Maintain a List<Element> currentBatch (starting with rootElements).
    - while (currentBatch.Count > 0):
      - **Extract:** Pass currentBatch to new StandardSerializationEngine(_identityService).ByRevit(...) to get a list of POCOs.
      - **Process POCOs:** For each extracted POCO:
        - Determine its unique identity key (e.g., poco.UniqueId or fallback to poco.Class + poco.Name).
        - Add the key to visitedIdentities.
        - **Tag Origin:** If this key exists in dependencyOrigins, set poco.DependencyOrigin = dependencyOrigins[key].
        - Add the POCO to finalPocos.
      - **Scan for Next Level:** For each extracted POCO, call RevitDomDependencyScanner.Scan(poco).
      - **Filter & Map:** Filter the yielded ElementIdModels. If their identity key is *not* in visitedIdentities, add them to a missingDependencies list, and add their key to dependencyOrigins mapping it to the current poco.Name.
      - **Fetch Native:** Pass the missingDependencies list to _identityService.GetElementsByElementIdModels(doc, missingDependencies).
      - **Set Next Batch:** Assign the returned native elements to currentBatch and repeat.
### Architectural Guardrails

- **No UI Dependencies:** This service must not reference WPF, MessageBox, or UI contexts. Use the IProgress<string> delegate to bubble status updates up to the ViewModel.
- **Strict DAG Enforcement:** The visitedIdentities set is absolutely critical. You must check it before fetching and before scanning to guarantee the loop terminates, even if the Revit model contains circular references.
- **Service Passthrough:** When instantiating the StandardSerializationEngine internally, you *must* pass the injected IIdentityService into its constructor.
### Data & State Flow

- **Input:** Native Revit Element instances (the user's root selection).
- **Extract:** Native Elements -> StandardSerializationEngine -> ObjectModel POCOs.
- **Scan:** ObjectModel -> RevitDomDependencyScanner -> Required ElementIdModels.
- **Fetch:** ElementIdModels -> IIdentityService.GetElementsByElementIdModels -> Next batch of Native Elements.
- **Output:** A flattened List<ObjectModel> containing the root elements and all nested dependencies, with DependencyOrigin populated on the children.
### Acceptance Criteria

- [ ] Implement IStandardsExtractionOrchestrator and StandardsExtractionOrchestrator in the Engine namespace.
- [ ] Implement the recursive DAG loop utilizing visitedIdentities.
- [ ] Implement the dependencyOrigins dictionary to accurately populate the DependencyOrigin property on child POCOs.
- [ ] Inject IIdentityService via constructor.
## Workflow & Version Control

- **Branching:** Create a new branch scoped specifically to the prd (e.g., feature/015-source-model-nested-dependencies).
- **Commits:** Commit frequently. Create a commit after completing each discrete, logical step of your implementation plan rather than waiting until the end.
## Testing & Seams

- **Testing Seam:** The Orchestrator boundary relies on IIdentityService. By injecting it, the Orchestrator's internal logic can be unit-tested, though the most valuable test is an integration test to verify the loop actually calls Revit correctly.
- **Tests to Write:** Create Tier2_StandardsExtractionOrchestratorTests.cs (or append to existing scanner tests).
- **Scenarios to Cover:**
  - **Deep Dependency Retrieval:** Target a known WallType in the test document that has a specific Material. Pass *only* the WallType to Extract().
  - **Assertions:** 1. Assert the returned list contains both the WallType POCO and the Material POCO.2. Assert that the Material POCO's DependencyOrigin property equals the name of the WallType.
