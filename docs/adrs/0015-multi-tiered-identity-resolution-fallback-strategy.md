# ADR 0015 - Multi-Tiered Identity Resolution Fallback Strategy

Status: Accepted

When serializing and transferring standards between different Revit documents, strict database identifiers (`ElementId`, `UniqueId`) often fail because they are document-specific, while relying purely on string names is susceptible to duplication, localization differences, and users renaming elements. We establish a strict, prioritized 5-step fallback pipeline within the identity resolver (`Magic ID Enum Intercept -> UniqueId -> ElementId + Type Guard -> Name -> Alias`) to bind all element relationships. This resolves the fundamental tension between strict database precision and flexible cross-document portability, ensuring referential integrity is maintained when JSON standards are injected into distinct Revit models.

## Considered Options

- **Strict Database Identifiers (`ElementId` / `UniqueId` Only):** Rejected because it completely breaks cross-document transfer. ID `12345` in Document A might be a `WallType`, whereas in Document B it might be a `LinePatternElement` or absent.
- **String Names Only:** Rejected because it fails when Revit language localizations change (e.g., English `"Solid"` vs. French `"Plein"`) and breaks if a user renames a standard in the target document.

## Consequences

- **The Magic ID Intercept:** The pipeline intercepts enum strings (such as `BuiltInCategory.OST_Walls` or `BuiltInParameter.WALL_BASE_OFFSET`) at the start of resolution. This bypasses the Revit API behavior where `doc.GetElement()` returns `null` for built-in singletons, guaranteeing localization-proof resolution.
- **The Type Guard:** When falling back to `ElementId`, the resolver checks that the resolved element's class matches the expected C# type (e.g., ensuring ID `100` resolved to a `Material` rather than a `FloorType`) to prevent cross-document ID collisions.
- **Alias Fallback:** Standards support an array of string `Aliases` to catch legacy names or user-modified names in target documents when strict IDs fail.
