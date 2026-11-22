# Development Brief 015-1 - Create RevitDomDependencyScanner in RevitDOM

## **Metadata & Alignment**

- **Parent PRD:** PRD 015 - Project Standards Source Model Nested Dependencies
- **Vertical Slice Issue:** Issue 015-1
- **Summary:** Build a pure-C# reflection utility in RevitDOM to recursively harvest ElementIdModel dependencies from POCOs.
## Implementation Strategy

### Strategic Intent

To accurately stage standards (like a WallType and its required Materials) universally for both Revit models and JSON files, we must treat the serialized POCOs as the single source of truth for the dependency graph. This slice builds the foundational generic scanner that traverses a POCO and yields all nested ElementIdModel references. This completely decouples dependency discovery from the Revit API.
### What to Build

- **Extract and Refactor Existing ScanObject Logic:**
  - Locate the existing ScanObject method. (Note: It may be found in ExportStylesViewModel.cs or EnforceStandardsViewModel.cs depending on the current state of the local branch).
  - Extract this logic out of the ViewModel and move it into a new static class RevitDomDependencyScanner in the Synthetic.Modules.RevitDOM.Utilities namespace (create the folder/namespace if it doesn't exist, or place it adjacent to StandardSerializationEngine if there's no Utilities folder).
  - Refactor the extracted logic to expose a primary public method: public static IEnumerable<ElementIdModel> Scan(ObjectModel rootModel).
- **Adapt the Recursive Reflection Logic:**
  - Adapt the extracted ScanObject code to yield instances of ElementIdModel rather than mutating external HashSets for UI preselection.
  - Retain the logic that recursively traverses into generic collections (e.g., IList, IEnumerable) to inspect their items.
  - Retain the logic that uses reflection to traverse complex properties.
  - Make sure to clean up the legacy UI-specific tracking collections (seenClasses, seenCategories).
- **Preserve Existing Safety Guardrails:**
  - **Visited Tracker:** Keep the existing HashSet<object> seenObjects (rename to visited) logic that is passed down into the private recursive helper method to prevent StackOverflowExceptions from circular references.
  - **Namespace Filtering:** Keep the existing namespace filter (ns.StartsWith("Synthetic", StringComparison.OrdinalIgnoreCase)) to prevent severe performance degradation when reflecting into core .NET types.
### Data & State Flow

- **Input:** A pure C# ObjectModel (e.g., a fully hydrated HostObjTypeModel representing a WallType).
- **Process:** The static method reflects through the object graph, guided by the "Synthetic" namespace rule, logging visited objects to prevent loops.
- **Output:** An IEnumerable<ElementIdModel> representing every single reference ID stored inside that POCO tree (e.g., the IDs of the Materials in its Compound Structure).
### Tactical Anti-Scope

- **DO NOT** add any Revit API dependencies (Autodesk.Revit.DB). This class must remain pure C#.
- **DO NOT** attempt to resolve the ElementIdModels into real Revit Elements or fetch them. That is the job of the RevitIdentityService in the next slice. This slice *only* finds the models.
### Acceptance Criteria

- [ ] Implement RevitDomDependencyScanner utility in RevitDOM.
- [ ] Ensure it accurately extracts all ElementIdModel instances from a given ObjectModel.
- [ ] Ensure it recursively navigates nested lists and complex properties (like CompoundStructureModel).
- [ ] Add headless NUnit tests passing complex POCOs to verify accurate reference extraction without a Revit context.
## Workflow & Version Control

- **Branching:** Create a new branch scoped specifically to the prd (e.g., feature/015-source-model-nested-dependencies).
- **Commits:** Commit frequently. Create a commit after completing each discrete, logical step of your implementation plan rather than waiting until the end.
## Testing & Seams

- **Testing Seam:** This is a pure C# utility, making it highly testable.
- **Tests to Write:** Create a new test fixture (e.g., RevitDomDependencyScannerTests) in the SyntheticTests.Logic project (likely under a Modules/RevitDOM folder).
- **Scenarios to Cover:**
  - A simple ObjectModel with a direct ElementIdModel property.
  - A complex nested model (e.g., mock a HostObjTypeModel containing a CompoundStructureModel containing a List<SerialCompoundStructureLayer>, where the layer holds a MaterialId). Verify the material ID is extracted.
  - A simulated circular reference (Object A points to Object B, which points back to Object A) to explicitly verify the HashSet<object> prevents a StackOverflow.
## Verification Steps

- Run the newly created NUnit tests. They must pass without requiring a Revit context.
- Review the Scan method to explicitly ensure the "Synthetic" namespace check and the visited hashset are correctly implemented to guarantee performance and safety.
