# ADR 024 - Unified Internal StandardTypeRegistry and Composite Class Routing

## Context

**ADR 010** established strongly-typed **RevitDOM** POCOs with explicit translator registration, **ADR 012** established deterministic bottom-up DAG topological sorting, and **ADR 017** consolidated parameter-driven `ElementType` subclasses onto base `ElementTypeModel` / `HostObjTypeModel` while routing them into 26 separate JSON dictionaries inside `ModelsToSerialize.cs`. In practice, leaving `ModelDispatcher` keyed solely by C# `Type` (`typeof(TModel)`) while scattering type rules across `ModelsToSerialize`, `MissingModels`, `ImportExecutionRunner`, `StandardsHierarchyUtility`, `StandardsSourceTreeViewModel`, `PocoToRevitDiffEngine`, and `RevitFamilyEnforcer` caused translator overwrites on shared POCOs, exact-type lookup misses on empty marker subclasses, silent JSON key collisions across families and categories, blind cross-family `.Duplicate()` calls, and divergent family-document filtering.

## Decision

We consolidate all standard element metadata into a single internal `StandardTypeRegistry` inside **RevitDOM** and retire both public `ModelsToSerialize` and the seven empty marker POCO subclasses (`GridTypeModel`, `LevelTypeModel`, `CurtainSystemTypeModel`, `MullionTypeModel`, `FasciaTypeModel`, `GutterTypeModel`, `ViewFamilyTypeModel`). Specifically:

1. **Internal Registry & Composite Dispatch**: Each registered Revit standard class defines its `ModelType`, `(ModelType, Class)` translator binding, deterministic DAG tier (Tiers 1–6), **Document Scope** (`Universal`, `ProjectOnly`, `FamilyOnly`), **Source Pane** `(GroupName, ClassName)` taxonomy, root collector rule, JSON bucket, deterministic composite key selector, and context-aware duplication/resolution strategy.
2. **Seam Placement**: `IStandardSerializationEngine` remains the narrow Revit `Document` port seam (`ByRevit`, `ExtractCategory`, `GatherRootElements`, `Analyze`, `ToRevit`), enforcing **Document Scope** uniformly inside `Analyze` and `ToRevit`. Pure headless operations (`StandardSerializationEngine.ToJson` / `FromJson`, `ImportExecutionRunner.Sort`, and `StandardsHierarchyUtility`) execute directly against the same internal `StandardTypeRegistry` without requiring Revit runtime or test-port indirection.
3. **Disambiguated Identity & Legacy JSON Compatibility**: `ElementTypeModel` captures `FamilyName` and optional `ViewFamily` so JSON buckets emit deterministic composite keys per bucket kind, `FromJson` hydrates entries polymorphically via `.Class` (transparently migrating legacy 26-bucket JSON files), and `ToRevit` / `Analyze` disambiguate existing elements and template duplication by `Category` + `FamilyName`, `ViewFamily`, `DimensionStyle`, and `Pattern.Target`.

## Status

Accepted (refines [ADR 010](Synthetic_ADRs%20-%2010%20-%20ADR%20010%20-%20Hybrid%20DispatcherTranslator%20Architecture.md) and [ADR 012](Synthetic_ADRs%20-%2012%20-%20ADR%20012%20-%20Explicit%20Translator%20Mapping%20for%20Topological%20Sorting.md); supersedes the `ModelsToSerialize` routing portion of [ADR 017](Synthetic_ADRs%20-%2017%20-%20ADR%20017%20-%20Generic%20POCO%20Consolidation%20for%20Parameter-Driven%20Types.md)).

## Consequences

- Adding or adjusting a Revit standard type is localized to a single internal registry entry rather than an 8-file shotgun edit.
- Shared POCOs (`ElementTypeModel`, `HostObjTypeModel`, and polymorphic `ViewModel` subclasses) route cleanly without overwriting translators or requiring empty C# marker subclasses.
- Same-name types across distinct families, categories, dimension styles, view families, or pattern targets no longer overwrite one another in JSON templates or live Revit documents, while existing JSON files continue to load seamlessly.
