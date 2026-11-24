# ADR 0017 - Generic POCO Consolidation for Parameter-Driven Types

Status: Accepted (POCO consolidation retained; `ModelsToSerialize.cs` dictionary routing superseded by [ADR 0024](0024-unified-internal-standardtyperegistry-and-composite-class-routing.md))

## Context
We frequently encounter Revit system families (such as `TextNoteType`, `ModelTextType`, and `SpotDimensionType`) that must be instantiated via the `.Duplicate()` creation API, whose properties are managed by the generic `ParameterEngine`. Creating empty, strongly-typed POCOs (e.g., `TextNoteTypeModel : ElementTypeModel`) solely to route them into separate JSON buckets produced unnecessary boilerplate.

## Decision
Abandon empty boilerplate POCOs. For elements without explicit C# property elevation, route them universally to the base `ElementTypeModel` (or `ElementModel` / `HostObjTypeModel`). JSON bucket routing and composite disambiguation now live in [`StandardTypeMetadataRegistry`](../../src/RevitDOM/Core/Operations/StandardTypeMetadataRegistry.cs) and [`StandardTypeRegistry`](../../src/RevitDOM/Addin/Operations/StandardTypeRegistry.cs) (per [ADR 0024](0024-unified-internal-standardtyperegistry-and-composite-class-routing.md)), while `TemplateDuplicatingTranslator` uses the model's `Class` string to cast the duplicated element dynamically.

## Consequences
* **Pros:** Eliminates redundant boilerplate POCOs and translators while maximizing leverage of `ParameterEngine` and `TemplateDuplicatingTranslator`.
* **Cons:** Bucket routing and deserialization hydration must evaluate `.Class` metadata via `StandardTypeMetadataRegistry` to dispatch generic models accurately.

