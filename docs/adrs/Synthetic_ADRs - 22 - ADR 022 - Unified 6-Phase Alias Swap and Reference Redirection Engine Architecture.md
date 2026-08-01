# ADR 022 - Unified 6-Phase Alias Swap and Reference Redirection Engine Architecture

## Context

Duplicate element merging and alias swapping in Revit require updating element instance types, writeable `ElementId` parameters, category default materials and line styles, compound structure layers (walls/floors/roofs/ceilings), view category graphic overrides, and view filter/element-level graphic overrides. Previously, instance remapping was handled inside `ProcessMergeEventHandler`, while property/style references were handled inside `AliasSwapEngine`.

## Decision

We unify all database reference redirection into a single 6-phase pipeline inside `Synthetic.RevitDOM.Operations.AliasSwapEngine`:
1. **Phase 1 (Instance & Type Remapping):** Universal `elem.ChangeTypeId(newId)` with `elem.IsValidTypeId(newId)` checks for families, system types, annotation types, groups, and assemblies.
2. **Phase 2 (Parameters):** Writeable `ElementId` parameter reference swapping across non-element-type and element-type elements.
3. **Phase 3 (Category Default Styles):** Category materials and cut/projection line pattern swapping.
4. **Phase 4 (Compound Structures):** Layer materials and deck profile swapping for host types.
5. **Phase 5 (View Category Overrides):** Category projection/cut line patterns and fill pattern overrides.
6. **Phase 6 (View Filter & Element Overrides):** View filter overrides (`view.GetFilterOverrides`) and element-level overrides (`view.GetElementOverrides`).

Transaction management uses a **Dual-Mode Transaction Strategy**:
- Discrete internal `Transaction` blocks per phase by default (running inside caller `TransactionGroup` contexts to prevent nested transaction crashes).
- Optional supplied `Transaction` parameter for single-transaction atomic callers.

Telemetry return is refactored into a `RedirectionResultModel` maintaining existing count metrics and error logs for Step 6 merge execution summary reporting.

## Status

Accepted.

## Consequences

- `ProcessMergeEventHandler` becomes a clean UI orchestration handler.
- Both modeless duplicate merge UI and headless serialization pipelines share identical reference redirection mechanics.
- System families, annotation types, view filters, and element-level overrides are fully supported during alias swaps.
