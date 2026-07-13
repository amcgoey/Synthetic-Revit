---
name: revit-architecture-guard-intelligence
description: Teaches the agent structural design patterns for high-performance transactions, explicit multi-schema JSON storage, and optimized element querying.
---

# RevitArchitectureGuard Architectural Playbook

You are strictly forbidden from writing rigid database boilerplate. You must apply the following architectural design patterns conceptually, adapting the structure to fit the unique context of the target file.

## 1. Schema Sovereignty, Algorithmic GUIDs, & Progressive Purge Migrations
You do not possess the authority to alter data persistence boundaries without direct alignment. You must strictly enforce the single-field JSON payload standard while executing algorithmic identification and progressive data purges.

### Decision, Generation, & Migration Boundaries:
* **The Structural Constant:** Every Extensible Storage schema must utilize exactly one `String` field named `JsonData` to house serialized configuration objects.
* **The Algorithmic GUID Generation Rule:** When creating a new schema or initiating a structural data layout revision, you must calculate a completely unique GUID. Generate this value by hashing the combination of the target **Class Name** and a **Current Universal Timestamp** (formatted exactly as `ClassName_YYYYMMDD_HHMMSS`). Convert this hash into a standard 128-bit Guid structure.
* **The Static Persistence Rule:** Once this GUID is calculated, you must write it into the C# source file as a hardcoded `static readonly Guid` or `const string` field. It must remain constant across active Revit user sessions so data can be reliably retrieved.
* **The Revision Mandate:** If a schema undergoes structural transformations or field evolutions requiring a fresh database partition, you must execute a brand-new generation loop using a fresh universal timestamp formatted exactly as `ClassName_YYYYMMDD_HHMMSS` to produce an entirely new, non-colliding GUID constant.
* **The Consultation Threshold:** If a feature requires storing data, you must analyze if an existing schema can safely accept the payload properties. If the choice between extending an existing schema or generating a new separate schema is ambiguous, you must stop and present the architectural pros and cons to the user for explicit direction.
* **The Progressive Migration Mandate:** You are strictly forbidden from implementing automatic multi-GUID fallback arrays during routine data retrieval. Instead, when a schema is revised and a new GUID is introduced, you must construct an explicit **Progressive Migration Runner**. 
* **The Step-by-Step Chain:** The runner must contain isolated transformation blocks mapping your evolving data schema sequentially (e.g., Schema_V1 -> Schema_V2 -> Schema_V3). It must open the old schema entity, extract the legacy JSON string, pass it through the progressive version modifiers to hydrate missing fields or map altered properties, and write the finalized payload to the newest active schema.
* **The Active Purge Protocol:** To prevent database bloat and eliminate abandoned data clutter, the migration runner must execute an explicit database cleanup immediately following a successful data transfer. You must programmatically invoke `Schema.EraseSchemaAndAllEntities()` (or target specific elements to delete the legacy `Entity` allocation) to completely erase the historical schema footprint from the active Revit project file.

## 2. The Assimilated Transaction Group Pattern (Performance & Quality)
Do not inject micro-transactions inside iterative loops. You must batch database alterations to preserve Revit's memory stability and graphic processing speed.

### Pattern Principles:
* **Elevation of Scope:** Position the overarching `TransactionGroup` allocation at the highest logical tier of the execution call stack possible (e.g., outside execution loops, commanding UI handlers, or batch processors).
* **The Aggregation Pattern:** Utilize a `TransactionGroup` to wrap complex, multi-phase operations. Use isolated standard `Transaction` blocks within the group to process logical element batches cleanly.
* **The Partial-Commit Rule:** If an intermediate action fails within a batched loop, do not execute a nuclear rollback of the entire session unless it is a fatal fallback requirement specified in the feature design. Instead, design the execution block to attempt to `.Commit()` the valid individual transactions, capture the specific element IDs that encountered errors, and pass a granular data packet containing the failures to the `RevitRuntimeWatchdog` triage stream.
* **The Assimilation Step:** Upon total or partial success of the batch operations, invoke `tg.Assimilate()` to collapse the completed transaction collection into a single, clean user-facing entry in Revit's native Undo stack.

## 3. Guarded Data Retrieval Matrix (`CollectorOptimizer`)
When querying the Revit database via a `FilteredElementCollector`, you must strictly minimize memory hydration by stacking Quick Filters ahead of Slow Filters.

### Query Ordering Protocol:
1. **Phase 1 (Native Memory Space):** Apply Class (`OfClass`) and Category (`OfCategory`) filters immediately upon collector initialization. These execute in Revit's internal C++ memory.
2. **Phase 2 (Database Iteration):** Apply parameter-driven filters (`ElementParameterFilter`) only after Phase 1 has drastically narrowed the element pool.
3. **Phase 3 (Managed Memory Hydration):** You are strictly forbidden from executing LINQ queries or `.Where()` clauses directly on an unoptimized collector stream. LINQ must only be executed at the very end of the method call chain after the element collection has been heavily restricted by native filters.