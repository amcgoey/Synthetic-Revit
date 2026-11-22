# Project Standards Source Model Nested Dependencies - Issue Tracker

Summary: Refactoring the Project Standards extraction and staging pipeline to reliably harvest and stage nested dependencies (like Materials required by Wall Types) for both live Revit models and offline JSON files. This removes the extraction logic from the "God ViewModel" by introducing a POCO-driven dependency graph utilizing new RevitDomDependencyScanner and GetElementsByElementIdModels seams in RevitDOM.
## Issue 015-1 - Create RevitDomDependencyScanner in RevitDOM

### What to build

Build a new highly generic reflection utility, PocoDependencyScanner, inside the Synthetic.Modules.RevitDOM namespace. This utility takes an ObjectModel (POCO) and recursively yields every property of type ElementIdModel. It must be capable of inspecting complex nested collections and objects (such as CompoundStructureModel or List<FilterRuleModel>). This creates a pure-C# seam to map references without touching the Revit API.
### Acceptance criteria

- [x] Implement RevitDomDependencyScanner utility in RevitDOM.
- [x] Ensure it accurately extracts all ElementIdModel instances from a given ObjectModel.
- [x] Ensure it recursively navigates nested lists and complex properties (like CompoundStructureModel).
- [x] Add headless NUnit tests passing complex POCOs to verify accurate reference extraction without a Revit context.
### Blocked by

None - can start immediately
## Issue 015-2 - Add Bulk Element Resolution to RevitIdentityService

### What to build

Extend the existing RevitIdentityService within Synthetic.Modules.RevitDOM to include a new bulk-resolution method: IEnumerable<Element> GetElementsByElementIdModels(Document doc, IEnumerable<ElementIdModel> identifiers). This method should efficiently resolve a batch of ElementIdModels into native Revit Element instances using the established 5-step fallback search (Id -> UniqueId -> Name/Class -> Aliases).
### Acceptance criteria

- [x] Add GetElementsByElementIdModels method to RevitIdentityService.
- [x] Ensure the method efficiently returns matching Elements using the established 5-step fallback rules.
- [x] Gracefully handle missing elements (skip or log) without throwing fatal exceptions.
- [x] Write a Tier 2 integration test against a live document to verify accurate bulk resolution.
### Blocked by

None - can start immediately
## Issue 015-3 - Build StandardsExtractionOrchestrator

### What to build

Create a new StandardsExtractionOrchestrator service in the Synthetic.Modules.StandardsManagement.Engine namespace. This service is responsible for the iterative extraction of a live Revit document.
It must execute a recursive Directed Acyclic Graph (DAG) loop:
- Extract primary native elements (using StandardSerializationEngine).
- Scan the resulting POCOs for nested references using PocoDependencyScanner.
- Check missing references against a HashSet<string> of visited identities (UniqueId or Class+Name).
- Fetch unvisited native dependencies using RevitIdentityService.GetElementsByElementIdModels.
- Repeat until the graph is fully resolved.The resulting POCOs must have their DependencyOrigin property explicitly populated if they were pulled in as a nested dependency.
### Acceptance criteria

- [x] Create StandardsExtractionOrchestrator to manage the recursive extraction loop.
- [x] Implement the HashSet<string> visited registry to prevent infinite recursion.
- [x] Ensure DependencyOrigin is accurately populated on harvested child POCOs.
- [x] Add a Tier 2 integration test validating the deep extraction of a complex host element (e.g., WallType) and asserting its materials are included in the final output.
### Blocked by

- Issue 015-1 - Create RevitDomDependencyScanner in RevitDOM
- Issue 015-2 - Add Bulk Element Resolution to RevitIdentityService
## Issue 015-4 - Refactor ViewModel Staging to use POCO Graph

### What to build

Refactor the Staging Queue logic within ProjectStandardsDashboardViewModel. Deprecate the legacy HarvestDependenciesInternal method that relied on native Revit elements.
The new staging logic must operate entirely on the Source Tab's POCO pool. When a user clicks "Add to Queue", the logic uses RevitDomDependencyScanner on the selected POCOs and automatically stages any matching POCOs already present in the Source Tab's SourceHierarchy pool, preserving the DependencyOrigin string.
### Acceptance criteria

- [x] Remove legacy native HarvestDependenciesInternal logic from the ViewModel.
- [x] Refactor queue staging logic to execute a fast, in-memory POCO scan matching against the Source Tab's pool.
- [x] Ensure nested dependencies map correctly to the Queue UI with the DependencyOrigin badge visible.
- [x] Ensure this logic works identically for both Revit Model sources and JSON File sources.
### Blocked by

- Issue 015-1 - Create RevitDomDependencyScanner in RevitDOM
## Issue 015-5 - Integrate Orchestrator into Source Extraction Workflow

### What to build

Refactor the "Add Revit Model" execution pipeline in ProjectStandardsDashboardViewModel (specifically the logic that executes after the SelectRevitDocumentWindow closes). Remove the raw ExtractRevitElements logic from the ViewModel. Inject the new StandardsExtractionOrchestrator via the constructor and use it to perform the initial model extraction. Pass the fully resolved list of POCOs into the StandardsHierarchyUtility.BuildHierarchy method to generate the Source Pane UI tree.
### Acceptance criteria

- [x] Remove ExtractRevitElements from ProjectStandardsDashboardViewModel.
- [x] Inject StandardsExtractionOrchestrator into the ViewModel via the constructor.
- [x] Update the AddRevitModelCommand execution to utilize the injected Orchestrator.
- [x] Ensure the resulting POCO list correctly renders in the standard top-level taxonomy folders within the Source Pane.
### Blocked by

- Issue 015-3 - Build StandardsExtractionOrchestrator
