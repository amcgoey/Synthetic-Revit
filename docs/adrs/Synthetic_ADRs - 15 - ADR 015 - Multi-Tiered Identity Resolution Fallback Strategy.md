## ADR 015 - Multi-Tiered Identity Resolution Fallback Strategy

Context: When serializing and transferring standards between different Revit documents, strict database identifiers (ElementId, UniqueId) often fail because they are document-specific. Conversely, relying purely on string names is susceptible to duplication, localization differences, and users renaming elements. Decision: We established a strict, prioritized fallback pipeline within the ReferenceResolver for binding all element relationships:
- Magic ID Intercept: (For native singletons).
- UniqueId / ElementId + Type Guard: Fast-path resolution. We explicitly check that the resolved element's class matches the expected type to prevent cross-document ID collisions (e.g., ID 100 is a Wall in Doc A, but a Line in Doc B).
- Name Matching: Cross-document portability fallback.
- Alias Matching: Handles scenarios where a standard was renamed in the target document but must still be merged. Why: This pipeline resolves the fundamental tension between strict database precision and flexible cross-document portability, ensuring referential integrity is maintained when JSON standards are injected into completely distinct Revit models.
