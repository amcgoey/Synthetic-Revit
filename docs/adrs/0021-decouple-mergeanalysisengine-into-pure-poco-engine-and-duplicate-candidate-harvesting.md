# ADR 0021 - Decouple MergeAnalysisEngine into Pure POCO Engine and Duplicate Candidate Harvesting

Status: Accepted

To decouple duplicate analysis from the Autodesk Revit API, enable fast headless **Logic Tests**, and enforce clean architectural seams between data harvesting and cluster analysis, we split duplicate matching and extraction responsibilities into two distinct modules: [`RevitMergeDataCollector`](../../src/Features/MergeDuplicates/Addin/Services/RevitMergeDataCollector.cs) (**Duplicate Candidate Harvesting**, Revit-connected adapter) and [`MergeAnalysisEngine`](../../src/Features/MergeDuplicates/Core/Services/MergeAnalysisEngine.cs) (pure POCO analysis engine in `Core/`).

## Context & Problem Statement

- `MergeAnalysisEngine.cs` previously mixed Revit API database queries (`FilteredElementCollector`, `Document`, `Element`, `FamilySymbol`, `GroupType`, `AssemblyType`) with pure POCO duplicate clustering, **Identity Comparison**, and recommendation generation.
- Coupling the analysis engine to `Autodesk.Revit.DB` prevented running duplicate analysis tests headlessly without an active Revit session.
- ViewModels and command handlers contained scattered `FilteredElementCollector` queries for element selection and straggler harvesting, reducing **locality**.
- Parameter group and parameter type definitions were passed as raw `object? paramGroup, object? paramType` data clumps across dynamic reflection and multi-version preprocessor branches.

## Decision Drivers

- Achieve 100% decoupling of `MergeAnalysisEngine` from `Autodesk.Revit.DB` and `Document`.
- Maximize **depth**, **leverage**, and **locality** for `Features/MergeDuplicates` (`Synthetic.Features.MergeDuplicates`).
- Enable fast, isolated NUnit **Logic Tests** on POCO snapshots ([`test_duplicate_families.json`](../../tests/Assets/test_duplicate_families.json), [`test_duplicate_groups.json`](../../tests/Assets/test_duplicate_groups.json)).
- Eliminate data clumps and single-letter variable names across the merge engine.

## Considered Options

- **Option A (Accepted):** Split responsibilities into `RevitMergeDataCollector` (**Duplicate Candidate Harvesting** in `src/Features/MergeDuplicates/Addin/Services/`) and pure `MergeAnalysisEngine` (`src/Features/MergeDuplicates/Core/Services/`). Introduce [`ParameterDefinitionSpec`](../../src/RevitDOM/Core/Models/ParameterDefinitionSpec.cs) in `Synthetic.RevitDOM.Models` to resolve data clumps.
- **Option B:** Keep Revit database queries inline inside `MergeAnalysisEngine.cs` and mock `Document` / `Element`. Rejected because mocking Revit API types is fragile across Revit version increments (`2023+`).

## Decision Outcome

1. **Extract `RevitMergeDataCollector`:** Move all `Document` queries (`RunFastScan`, `RunTargetedScan`, `CollectStragglers`, `ConvertToPoco`) into `RevitMergeDataCollector` in `src/Features/MergeDuplicates/Addin/Services/`.
2. **Pure POCO `MergeAnalysisEngine`:** Place `MergeAnalysisEngine` in `src/Features/MergeDuplicates/Core/Services/` (`Synthetic.Core`), accepting pure `ElementModel` POCO lists and returning `DuplicateClusterModel` collections without importing `Autodesk.Revit.DB`.
3. **Encapsulate POCO Geometry:** Add safe vector helpers (`GetSize()`, `GetCenter()`) on `BoundingBoxXYZModel` and `XYZModel` to eliminate `.ToNative()!` calls during headless deep scans.
4. **Data Clump Resolution (`ParameterDefinitionSpec`):** Bundle parameter group and spec metadata into a cohesive `ParameterDefinitionSpec` value object in `Synthetic.RevitDOM.Models`.
5. **Descriptive Variable Names:** Rename single-letter variables in `MergeAnalysisEngine.cs` to explicit, self-documenting names.

## Consequences

- **Logic Tests** (`Synthetic.LogicTests`, [`tests/Features/MergeDuplicates/Logic/`](../../tests/Features/MergeDuplicates/Logic/)) execute `MergeAnalysisEngine` headlessly with zero Revit API dependencies.
- `MergeDuplicatesViewModel` delegates all Revit document harvesting to `RevitMergeDataCollector`.
- [`GLOSSARY.md`](../../GLOSSARY.md) defines **Duplicate Candidate Harvesting** for this domain operation.
