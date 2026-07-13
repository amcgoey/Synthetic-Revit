## ADR 015 - Multi-Tiered Identity Resolution Fallback Strategy

When serializing and transferring standards between different Revit documents, strict database identifiers (ElementId, UniqueId) often fail because they are document-specific, while relying purely on string names is susceptible to duplication, localization differences, and users renaming elements. We decided to establish a strict, prioritized 5-step fallback pipeline within the ReferenceResolver (Magic ID Enum Intercept -> UniqueId -> ElementId + Type Guard -> Name -> Alias) to bind all element relationships. This resolves the fundamental tension between strict database precision and flexible cross-document portability, ensuring referential integrity is maintained when JSON standards are injected into completely distinct Revit models.
### Status

Accepted
### Considered Options

- Strict Database Identifiers (ElementId / UniqueId Only): Rejected because it completely breaks cross-document transfer. ID 12345 in Document A might be a WallType, but in Document B it might be a LinePattern or simply not exist.
- String Names Only: Rejected because it fails when Revit language localizations change (e.g., English "Solid" vs French "Plein") and breaks if a user renames a standard in the target document.
### Consequences

- The Magic ID Intercept: The pipeline must intercept Enum strings (like BuiltInCategory.OST_Walls or BuiltInParameter.WALL_BASE_OFFSET) at the very beginning of the pipeline. This bypasses the Revit API trap where doc.GetElement() returns null for built-in singletons, guaranteeing localization-proof resolution.
- The Type Guard: When falling back to ElementId, the resolver must explicitly check that the resolved element's class matches the expected C# type (e.g., ensuring ID 100 actually resolved to a Material and not a FloorType) to prevent catastrophic cross-document ID collisions.
- Alias Fallback: Standards must support an array of string Aliases to catch legacy names or user-modified names in target documents when strict IDs fail.
