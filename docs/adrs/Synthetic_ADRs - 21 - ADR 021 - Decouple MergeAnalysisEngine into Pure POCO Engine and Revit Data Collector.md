## ADR 021 - Decouple MergeAnalysisEngine into Pure POCO Engine and Revit Data Collector

To achieve 100% decoupling of backend duplicate analysis from the Autodesk Revit API, enable fast headless Tier 2 unit testing, and enforce clean architectural seams between data harvesting and cluster analysis, we decided to split duplicate matching and extraction responsibilities into two distinct components: `RevitMergeDataCollector` (Revit-connected document extraction) and `MergeAnalysisEngine` (pure POCO analysis engine).

### Status

Accepted

### Context & Problem Statement

- `MergeAnalysisEngine.cs` previously mixed Revit API database queries (`FilteredElementCollector`, `Document`, `Element`, `FamilySymbol`, `GroupType`, `AssemblyType`) with pure POCO duplicate clustering, identity comparison, and recommendation generation.
- Tightly coupling the analysis engine to `Autodesk.Revit.DB` prevented running duplicate analysis tests headlessly without an active Revit session.
- ViewModels and command handlers contained scattered `FilteredElementCollector` queries for element selection and straggler harvesting, reducing locality.
- Parameter group and parameter type definitions were passed as raw `object? paramGroup, object? paramType` data clumps across dynamic reflection and multi-version preprocessor branches.

### Decision Drivers

- Achieve 100% decoupling of `MergeAnalysisEngine` from `Autodesk.Revit.DB` and `Document`.
- Maximize depth, leverage, and locality for `Synthetic.Modules.MergeDuplicates`.
- Enable fast, isolated NUnit Tier 2 headless unit tests on POCO snapshots (`test_duplicate_families.json`, `test_duplicate_groups.json`).
- Eliminate data clumps and single-letter variable names across the merge engine.

### Considered Options

- **Option A (Accepted):** Split responsibilities into `RevitMergeDataCollector` (Revit-connected data harvesting) and `MergeAnalysisEngine` (pure POCO analysis). Introduce `ParameterDefinitionSpec` in `Synthetic.RevitDOM.Models` to resolve data clumps.
- **Option B:** Keep Revit database queries inline inside `MergeAnalysisEngine.cs` and mock `Document` / `Element`. Rejected because mocking Revit API interfaces is fragile across Revit version increments (2022–2026).

### Decision Outcome

1. **Extract `RevitMergeDataCollector`**: Move all `Document` queries (`RunFastScan`, `RunTargetedScan`, `CollectStragglers`, `ConvertToPoco`) into `RevitMergeDataCollector` in `Synthetic.Modules.MergeDuplicates.Services`.
2. **Pure POCO `MergeAnalysisEngine`**: Refactor `MergeAnalysisEngine` to take pure `ElementModel` POCO lists and return `DuplicateClusterModel` collections without importing `Autodesk.Revit.DB`.
3. **Encapsulate POCO Geometry**: Add safe vector helper methods (`GetSize()`, `GetCenter()`, `IsValid`) on `BoundingBoxXYZModel` and `XYZModel` to eliminate `.ToNative()!` calls during headless deep scans.
4. **Data Clump Resolution (`ParameterDefinitionSpec`)**: Bundle parameter group and spec metadata into a cohesive `ParameterDefinitionSpec` value object in `Synthetic.RevitDOM.Models`.
5. **Descriptive Variable Names**: Rename all single-letter variables in `MergeAnalysisEngine.cs` to explicit, self-documenting names.

### Consequences

- Tier 2 NUnit tests execute `MergeAnalysisEngine` headlessly with zero Revit API dependencies.
- `MergeDuplicatesViewModel` delegates all Revit document harvesting to `RevitMergeDataCollector`.
- `CONTEXT.md` glossary updated with `Revit Merge Data Collector` and `Parameter Definition Spec`.
