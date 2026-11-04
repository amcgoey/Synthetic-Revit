# ADR 023 - Transform-Based Shared Site Link Repositioning and Single Adapter Seam

## Context

In multi-story residential tower workflows, unit floor-plate models define **Level-Based Shared Sites** (`ProjectLocation` elements) corresponding to floor levels in the host (main) model. When floor elevations change in the host model and the **Level-Based Shared Sites** inside a unit model are updated via **Shared Site Sync**, simply calling `RevitLinkType.Reload()` in the host model does not automatically reposition existing `RevitLinkInstance` placements to reflect the modified `ProjectLocation` coordinates. Furthermore, the Revit API does not expose a method on `RevitLinkInstance` to trigger the UI's `"Move instance to <Site Name>"` operation directly, and `BuiltInParameter.GEO_LOCATION` cannot be mutated to force a transform recalculation on an already-assigned site.

## Decision

We implement **Shared Site Link Repositioning** by computing the exact 3D coordinate transformation between the host document's `ActiveProjectLocation` and the linked document's assigned `ProjectLocation` (resolved via `BuiltInParameter.GEO_LOCATION` / instance site name), deriving the translation and rotation delta against the `RevitLinkInstance`'s current `GetTransform()`, and applying the delta inside a transaction (automatically unpinning prior to transformation and re-pinning afterward).

To maximize module depth and testability without proliferating abstractions, all Revit API database reads and writes for both **Shared Site Sync** and **Shared Site Link Repositioning** are isolated behind a single seam (`ISharedCoordinatesAdapter`), enabling end-to-end headless testing of both workflows and their 3D coordinate math in `SyntheticTests.Logic`.

## Status

Accepted.

## Consequences

- Host model `RevitLinkInstance` placements reliably align with updated **Level-Based Shared Sites** in full 3D (East/West, North/South, Elevation, and True North Angle) even when pinned.
- Unassigned (`<Not Shared>`) link instances are safely skipped and surfaced in the post-execution summary.
- Both commands (`Sync Shared Sites to Levels` and `Move Links to Shared Sites`) are testable through their top-level ViewModel interfaces against an in-memory `FakeSharedCoordinatesAdapter` without requiring a running Revit session.
