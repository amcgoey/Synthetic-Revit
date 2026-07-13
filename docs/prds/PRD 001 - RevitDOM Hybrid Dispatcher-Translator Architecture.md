# RevitDOM Hybrid Dispatcher/Translator Architecture - Product Requirement Document

## Problem Statement

The previous architecture for metadata-driven serialization (RevitSerialization Modules 1-5) successfully decoupled data from execution and solved critical dependency ordering issues, but introduced severe ergonomic failures. By utilizing a generic GenericElementModel and a fluent PropertyRegistry, the system lost domain expressiveness (making payloads difficult to read or debug) and fell into "configuration hell"—requiring thousands of lines of explicit lambda mappings for every property across all Revit classes. This resulted in a shallow module where callers and maintainers bore the brunt of the complexity. Conversely, the legacy RevitDOM architecture featured highly readable, inheritance-rich POCOs that efficiently modeled the Revit domain, but suffered from a tightly coupled "God Object" (DomTransactionEngine) that violated the Open/Closed Principle and crashed during complex batch imports due to a lack of topological sorting.
## Solution

Create a deep module that maximizes leverage at the public seam and locality for maintainers by implementing a **Hybrid Dispatcher/Translator Architecture**. This solution salvages the highly expressive, inheritance-based POCOs from the legacy RevitDOM, purges them of all Revit API dependencies to ensure absolute data purity, and orchestrates them via a decoupled internal Dispatcher. The internal implementation hides all Revit complexities—including topological sorting, multi-tiered identity resolution, and transaction assimilation—behind a simple, three-method public interface. A pipeline approach will separate generic parameter handling from class-specific translation, explicitly managing constructor dependencies and aggressively filtering read-only parameters to ensure lean, portable JSON standards.
## User Stories

- As a developer integrating UI commands, I want a single, deep public seam (StandardSerializationEngine) so that I can extract, analyze, and inject Revit elements without managing transactions or dependency order.
- As a developer extracting standards, I want to call ByRevit(elements, doc, isTemplate=true) so that the engine automatically strips document-specific ElementId and UniqueId values, yielding a sterilized payload safe for cross-project transfer.
- As a user reviewing changes, I want the system to expose an Analyze() method that compares my standard POCOs against the live Revit database, so that I can review a DuplicateClusterModel diff report before committing destructive changes.
- As a UI developer, I want to call ToRevit(models, doc) to write data and receive a detailed result report, so that the engine handles partial commits, gracefully catches failures without stopping the batch, and provides a list of successes and errors to display to the user.
- As a maintainer extending the addon, I want to author specific IModelTranslator adapters (e.g., HostObjTypeTranslator) that only handle unique properties (like CompoundStructure), so that I don't have to write boilerplate parameter-iteration logic.
- As a maintainer mapping inherited Revit types, I want the internal Dispatcher to support "One-to-Many" mapping, so that I can route WallType, FloorType, and RoofType to a single HostObjTypeTranslator, preserving the leverage of the POCO inheritance model.
- As a maintainer ensuring fast Revit startup, I want to explicitly register IModelTranslator adapters in a centralized registry, so that I avoid the performance penalties and unpredictability of runtime assembly scanning/reflection.
- As a developer debugging serialized payloads, I want the payloads to use strongly-typed POCOs (e.g., MaterialModel, ViewTemplateModel) rather than flattened dictionaries, so that the domain intent is immediately readable.
- As a developer managing cross-document element references, I want ElementIdModel to act purely as an Identity Model (holding Id, UniqueId, Name, and Aliases), so that the internal ReferenceResolver can execute a safe 5-step fallback search to bind dependencies.
- As a user exporting templates, I want the generic parameter engine to use a "Filter on Extract" strategy, so that any IsReadOnly == true or empty parameters are dropped, keeping my JSON standards files extremely lean and devoid of noise.
- As a maintainer handling elements with specific creation methods (e.g., LinePatternElement), I want constructor dependencies to be explicitly modeled in the POCOs and handled by the specific IModelTranslator, so that I don't rely on generic parameters that might miss constructor-only data.
- As the execution engine sorting batched imports, I want the ImportExecutionRunner to use a hardcoded, explicit bottom-up execution hierarchy (DAG), so that I don't rely on fragile reflection scavenging to prevent circular dependencies.
- As a UI developer executing long-running transactions, I want the engine methods to accept standard .NET CancellationToken and IProgress parameters, so that my UI can display modeless progress bars and cancel operations safely without embedding UI logic into the deep module.
## Implementation Decisions

### 1. The Deep Public Seam (StandardSerializationEngine)

- The module will expose a highly leveraged public interface representing the strict boundary between pure C# data and the Revit API.
- To remain entirely UI-agnostic while still supporting modeless progress bars and cancellation, the methods will accept IProgress<T> and CancellationToken:
  - IEnumerable<ObjectModel> ByRevit(IEnumerable<Element> elements, Document doc, bool isTemplate, IProgress<string> progress = null, CancellationToken cancellationToken = default)
  - IEnumerable<DuplicateClusterModel> Analyze(IEnumerable<ObjectModel> models, Document doc, IProgress<string> progress = null, CancellationToken cancellationToken = default)
  - IEnumerable<SerializationResultModel> ToRevit(IEnumerable<ObjectModel> models, Document doc, IProgress<string> progress = null, CancellationToken cancellationToken = default)
- **Graceful Degradation & Results:** ToRevit returns an IEnumerable<SerializationResultModel> containing the success/failure status and exception messages for each processed model. If a single element fails during import, its inner transaction is rolled back, the failure is recorded in the results, and the engine continues processing the rest of the batch.
- **Global Cancellation:** If a cancellationToken is triggered, the engine will gracefully roll back the entire TransactionGroup (undoing even the successes) and throw an OperationCanceledException.
### 2. Absolute POCO Purity & Namespace Alignment

- All POCO models in the RevitDOM namespace (e.g., ElementModel, HostObjTypeModel) will be stripped of Autodesk.Revit.DB dependencies.
- ElementIdModel and UVModel will be relocated from Infrastructure.Serialization to the RevitDOM namespace.
- ElementIdModel will function exclusively as the pure Identity Model. Methods like GetElem(doc) will be deleted; resolution is strictly the responsibility of the internal ReferenceResolver adapter.
- **Enum-First Built-In Identity:** Built-in parameters and categories will be serialized using their immutable Enum string names (e.g., "OST_Walls", "WALL_BASE_OFFSET") rather than volatile localized names or raw negative integers, ensuring complete localization-proof data transfer.
### 3. Internalized Dispatcher & The Pipeline Approach

- The DomTransactionEngine monolith will be replaced by an internal Dispatcher.
- **One-to-Many Mapping:** The Dispatcher will map specific IModelTranslator<TRev, TModel> adapters to multiple Revit native types during reads (e.g., typeof(WallType) and typeof(FloorType) both map to HostObjTypeTranslator).
- **The Pipeline Approach:** Translators will not use class inheritance to share behavior. Instead, the Dispatcher will use composition. For a WallType, the Dispatcher first routes the element through the generic ParameterEngine to populate base parameters, then passes the same POCO to the HostObjTypeTranslator to populate specific data (like CompoundStructure). This grants absolute locality to the translators.
### 4. Parameter Engine & Constructor Dependencies

- **Filter on Extract:** When isTemplate == true, the generic Parameter Engine will aggressively skip parameters where IsReadOnly == true or where the value is null/empty.
- **Constructor Exclusivity:** The generic Parameter Engine will not attempt to whitelist read-only parameters required for element instantiation. Constructor dependencies (e.g., segments for Line Patterns) must be explicitly defined on the POCO and extracted/injected exclusively by the specific IModelTranslator.
### 5. Explicit Topological Sorting

- The ImportExecutionRunner will not use reflection to scavenge for dependencies.
- It will sort batches using a hardcoded, explicit bottom-up Directed Acyclic Graph (DAG) derived from the legacy ModelsToSerialize hierarchy:
  - **Primitives:** Line Patterns, Fill Patterns.
  - **Foundations:** Materials (which inherently process their nested Material Assets).
  - **Simple Annotations & Datums:** Text Notes, Dimensions, Grids, Levels, Shared Parameters.
  - **Categorization & Filtering:** Categories, View Filters.
  - **System Families / Host Types:** Wall/Floor/Roof/Ceiling Types, Mullions, Curtain Systems.
  - **Views & UI:** View Templates, Views, Browser Organizations.
## Testing Decisions

- **Test at the Interface:** Integration tests must target the StandardSerializationEngine public seam, passing in raw Revit elements or pure ObjectModel lists to verify the end-to-end extraction, diffing, and injection pipelines.
- **Adapter Locality Testing:** Because of the Pipeline Approach, specific adapters (like HostObjTypeTranslator) can be unit-tested in isolation by passing them pre-hydrated mock HostObjTypeModel instances without booting the entire dispatcher.
- **Prior Art:** Leverage the structure of Tier2_SerializationIntegrationTests.cs (via ricaun.RevitTest) to ensure all write operations are wrapped in a rolling-back TransactionGroup (The Zero-Leak Rule).
## Workflow & Constraints

- **Eradicate RevitSerialization:** To prevent AI namespace confusion and context bleed during the development phase, the entire Synthetic.Modules.RevitSerialization namespace, directory, and its associated test files **must** be explicitly deleted from the new development branch.
## Out of Scope

- **JSON File I/O:** Methods like ByJSON() and ToJSON() are strictly out of scope for this module. The serialization of pure ObjectModel data to/from JSON strings must reside across the File/Serialization seam in the Infrastructure.Serialization module to maintain separation of concerns.
- **Physical Instance Serialization:** Per ADR 009, this engine will not serialize physical instances (e.g., individual Walls, Floors) or canvas elements. It remains strictly scoped to Base Classes, Element Types, and Standards.
- **UI Refactoring:** Other than updating the calling UI commands to pass IProgress and CancellationToken into the new StandardSerializationEngine methods, overhauling WPF views is out of scope.
## Further Notes

- This architecture formally deprecates ADRs 002, 004, 005, 006, 007, and 008 from the RevitSerialization exploration phase.
- It establishes ADR 010 (Hybrid Dispatcher/Translator Architecture), ADR 011 ("Filter on Extract" Parameter Strategy & Explicit Constructor Dependencies), and ADR 012 (Explicit Translator Mapping for Topological Sorting).
- **ADR 015:** Multi-Tiered Identity Resolution Fallback Strategy (UniqueId -> Id -> Name -> Alias).
- **ADR 016:** Enum-First Identity Strategy for Built-In Types (Bypassing GetElement() traps and localization failures).

