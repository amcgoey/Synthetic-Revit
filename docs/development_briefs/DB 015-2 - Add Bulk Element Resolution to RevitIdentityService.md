# Development Brief 015-2 - Add Bulk Element Resolution to RevitIdentityService

## **Metadata & Alignment**

- **Parent PRD:** PRD 015 - Project Standards Source Model Nested Dependencies
- **Vertical Slice Issue:** Issue 015-2
- **Summary:** Extend IIdentityService and RevitIdentityService with a bulk-fetch method that safely resolves dependencies and logs missing assets.
## Implementation Strategy

### Strategic Intent

After the RevitDomDependencyScanner (from Issue 015-1) extracts required ElementIdModel references, the extraction orchestrator must fetch the native Elements from the live Revit database to serialize them. This slice provides that bulk-fetch mechanism. By extending the existing RevitIdentityService, we leverage the bulletproof 5-step fallback resolution logic (UniqueId -> Id -> Type Guard -> Name -> Alias) rather than reinventing a brittle bulk-collector.
### What to Build

- **Update IIdentityService (Interface):**
  - Add the following method signature to the IIdentityService interface:IEnumerable<Element> GetElementsByElementIdModels(Document doc, IEnumerable<ElementIdModel> identifiers)
- **Implement in RevitIdentityService:**
  - Implement the new method in RevitIdentityService.cs.
  - Create a loop that iterates over each ElementIdModel in identifiers.
  - Inside the loop, call the existing ResolveElement(model, doc) method.
  - **Missing Dependency Handling (Crucial):** If ResolveElement returns null, the asset is missing from the active Revit model. You **must** log this using SerializationResultModel.LogWarning($"Dependency not found: {model.Name} ({model.Class})").
  - Filter out all null results.
  - Return the flat list/enumerable of valid, resolved native Revit Element instances.
### Data & State Flow

- **Input:** A collection of ElementIdModel POCOs representing required dependencies, and the active Document.
- **Process:** The service routes each ID through the existing 5-step fallback algorithm. It filters out unresolvable items while registering warnings to the centralized telemetry tracker.
- **Output:** An IEnumerable<Element> containing only successfully located native Revit objects, ready for serialization.
### Tactical Anti-Scope

- **DO NOT** attempt to write a complex, grouped FilteredElementCollector optimization. Stick strictly to looping the existing ResolveElement method to maintain the integrity of our fallback logic.
- **DO NOT** throw exceptions when an element is missing. Missing standards are an expected reality of BIM models; rely exclusively on SerializationResultModel.LogWarning.
- **DO NOT** serialize the resolved elements back into POCOs in this method. This service's only job is to return native Elements.
### Acceptance Criteria

- [ ] Add GetElementsByElementIdModels method to IIdentityService.
- [ ] Implement the method in RevitIdentityService by looping the existing ResolveElement logic.
- [ ] Gracefully handle missing elements by skipping them and logging a warning via SerializationResultModel.LogWarning.
- [ ] Write a Tier 2 integration test against a live document to verify accurate bulk resolution and warning generation.
## Workflow & Version Control

- **Branching:** Create a new branch scoped specifically to the prd (e.g., feature/015-source-model-nested-dependencies).
- **Commits:** Commit frequently. Create a commit after completing each discrete, logical step of your implementation plan rather than waiting until the end.
## Testing & Seams

- **Testing Seam:** The IIdentityService interface acts as the boundary. The execution logic relies heavily on the live Revit Database.
- **Tests to Write:** Add tests to the existing RevitIdentityServiceIntegrationTests.cs (Tier 2).
- **Scenarios to Cover:**
  - **Success Path:** Pass a list of ElementIdModels containing known, valid targets (e.g., a known level and a known view) and assert that exactly those elements are returned.
  - **Degradation Path:** Pass a list containing one valid ElementIdModel and one completely fictitious ElementIdModel. Assert that exactly one element is returned, no exceptions are thrown, and a warning is successfully captured by checking the SerializationResultModel.CurrentThreadWarnings collection.
