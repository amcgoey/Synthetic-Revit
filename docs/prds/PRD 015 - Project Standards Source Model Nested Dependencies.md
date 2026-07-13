# PRD 015 - Project Standards Source Model Nested Dependencies - Product Requirement Document

## Problem Statement

When users add an open Revit model as a source to the Project Standards Dashboard, the selected class filters correctly extract the primary target elements but fail to extract their nested dependencies (e.g., Wall Types are extracted, but their required Materials and Fill Patterns are not). This creates an inconsistency with the Staging Queue (which *does* harvest dependencies) and risks incomplete data transfers.
Furthermore, the existing extraction and harvesting logic is tightly coupled directly inside the ProjectStandardsDashboardViewModel. This prevents the staging logic from working universally across both live Revit Models and offline JSON files, and heavily contributes to the "God ViewModel" anti-pattern identified in the roadmap.
## Solution

Implement a highly-leveraged, POCO-driven dependency harvesting pipeline orchestrated by a newly decoupled StandardsExtractionOrchestrator.
When a source is loaded, the orchestrator will execute a recursive loop: it extracts primary native elements, translates them to pure C# POCOs (ObjectModel), and scans those POCOs for nested references using IdentifyRelationships. It then fetches any missing native dependencies using RevitIdentityService and repeats the process. To prevent infinite loops and redundant processing, the orchestrator utilizes a strict Directed Acyclic Graph (DAG) "Visited Identities" registry.
Once extracted, these dependencies will be placed seamlessly into their standard top-level taxonomy folders in the Source Pane (e.g., a harvested material goes into "Materials & Assets"), keeping the tree clean and deduplicated. To ensure UI transparency, they will be tagged with a DependencyOrigin badge indicating exactly which parent element pulled them in. Ultimately, by shifting dependency detection to the POCO layer, both Revit Model sources and JSON file sources will utilize the exact same staging logic.
## User Stories

- As a user adding a live Revit Model as a Source, I want the extraction process to automatically trace and pull in all required nested dependencies (like Materials required by Wall Types) before displaying them in the tree, so that I don't have to manually check every required sub-category in the filter dialog.
- As a user, I want nested dependencies to automatically appear in the Source Pane when I filter for their parent elements, so that I have a complete picture of the required data being imported.
- As a user, I want harvested dependencies to be grouped in their standard top-level folders (e.g., all materials under "Materials & Assets"), so that the UI remains clean, predictable, and avoids duplicate nested nodes.
- As a user, I want to see a "DependencyOrigin" badge on automatically harvested elements in the Source Pane, so that I understand why an element was extracted even if I didn't explicitly check its category filter.
- As a user staging items from a JSON file source, I want the system to automatically stage required dependencies just like it does for live Revit models, so that my workflow remains consistent regardless of the source type.
- As a maintainer, I want the heavy extraction and dependency resolution logic removed from the Dashboard ViewModel, so that the UI code remains clean, highly testable, and adheres to the Single Responsibility Principle.
## Implementation Decisions

- **StandardsExtractionOrchestrator:** Create a new orchestrator service in the Synthetic.Modules.StandardsManagement.Engine namespace to manage the initial Revit model extraction.
  - It will execute an iterative "Extract -> Scan POCOs -> Fetch Native -> Extract" loop.
  - It takes a raw list of target classes, translates them to POCOs via StandardSerializationEngine, uses the RevitDomDependencyScanner (below) to find missing ElementIds, fetches those missing native elements using RevitIdentityService, and repeats until the graph is fully resolved.
  - This completely removes extraction logic from the ProjectStandardsDashboardViewModel.
- **DependencyHarvester Service:** Extract the shared dependency traversal rules into a dedicated, injectable IRevitDependencyHarvester service to guarantee DRY principles.
- **POCO-Driven Staging:** Staging logic will rely entirely on the extracted POCO models (not native Revit elements) as the single source of truth for the dependency graph. By treating the POCO as the source of truth, the staging operation becomes a lightning-fast, purely in-memory relational lookup that works universally for both JSON file sources and Live Revit Model sources. When a user clicks "Add to Queue", the logic scans the selected POCOs for dependencies and automatically stages any matching POCOs already present in the Source Tab's pool.
- **New RevitDOM Seam - IdentifyRelationships:** A new generic reflection utility (e.g., PocoDependencyScanner) will be introduced inside Synthetic.Modules.RevitDOM. It takes an ObjectModel and recursively yields every property of type ElementIdModel (including inspecting complex nested objects like CompoundStructureModel). This provides a pure-C# seam to map references without touching the Revit API.
- **New RevitDOM Seam - GetElementsByElementIdModels:** A new bulk-resolution method will be added to the existing RevitIdentityService within Synthetic.Modules.RevitDOM. It will take an IEnumerable<ElementIdModel> and efficiently resolve them to native Revit Element instances using the established 5-step fallback search (Id -> UniqueId -> Name/Class -> Aliases). This acts as the fetch mechanism when the orchestrator determines a required dependency has not yet been extracted.
- **Visited Identities Registry:** The orchestrator will maintain a HashSet<string> of visited identities (using UniqueId or Class + Name) during its recursive loop. This Directed Acyclic Graph (DAG) traversal pattern prevents infinite recursion and redundant API calls.
- **Constructor Injection:** The new extraction services will be injected into the ProjectStandardsDashboardViewModel via the constructor, allowing for easy headless unit testing.
- **Taxonomy Standardization:** Dependencies will be piped through the existing StandardsHierarchyUtility.BuildHierarchy to ensure they land in root-level taxonomy folders rather than being visually nested under parents.
## Testing Decisions

- **Tier 1 (Logic Tests):** Write fast, headless NUnit tests for the ProjectStandardsDashboardViewModel by injecting a mocked IRevitDependencyHarvester / StandardsExtractionOrchestrator. Verify that the ViewModel correctly updates the AvailableSources tabs, formats the SourceHierarchy, and maps DependencyOrigin strings without invoking the Revit API. Test the new RevitDOM POCO scanner by verifying it correctly extracts ElementIdModels from complex nested POCO structures without Revit Context.
- **Tier 2 (Integration Tests):** Write headless integration tests executing the StandardsExtractionOrchestrator against a live, test .rvt document. Extract a host element (e.g., a specific WallType) and assert that the resulting POCO list automatically includes its required dependencies (e.g., the correct Material) and that the DependencyOrigin properties are accurately populated. Validate that GetElementsByElementIdModels successfully retrieves target native elements from the live document.
## Out of Scope

- Modifying the serialization schema or structure of the actual .json files.
- Visual redesign of the Dashboard Window's XAML beyond surfacing the existing DependencyOrigin badge in the Source Pane.
## Further Notes

This architecture elegantly resolves the JSON vs. Live Model staging discrepancy. By forcing all dependency logic to operate on the extracted POCOs rather than native Revit objects, the staging UI no longer cares where the data came from. This represents a massive step forward in decoupling our presentation layer from the Revit database.
