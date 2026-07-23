## ADR 020 - 5-Step Identity Fallback for ElementIdModel Value Equality

When working with `ElementIdModel` across POCO objects, JSON serialization, and WPF UI bindings (such as parameter diff reviews and standards management), reference-based equality fails to equate elements from different documents, different Revit sessions, or deserialized JSON objects. We decided to enforce the 5-step Identity Resolution Fallback Strategy directly on `ElementIdModel.Equals()`, `operator==`, and `GetHashCode()`, and overload `IPocoIdentityService` with centralized identity methods.

### Status

Accepted

### Context & Problem Statement

- WPF radio buttons in `ParameterDiffRowModel` failed to show as selected because `WinningValueElementId == Options[0].ElementId` performed reference equality rather than value equality.
- Looking up `ElementIdModel` keys in `Dictionary<ElementIdModel, T>` in `StandardsReviewViewModel` threw `KeyNotFoundException` when lookup keys were distinct object instances representing the same Revit element.
- Elements in different documents or different Revit sessions have unstable numeric `Id`s or distinct `UniqueId`s, but represent the exact same conceptual BIM element if their `Name` and `Class`/`Category` match.

### Decision Drivers

- Maintain consistent element identification across RevitDOM without relying on active Revit API connections.
- Ensure WPF bindings and dictionary lookups operate transparently regardless of object instance origins.
- Honor the existing 5-step identity resolution strategy defined in ADR 015.

### Considered Options

- Reference Equality (Existing): Rejected because it breaks WPF bindings and dictionary lookups across object instances.
- 2-Step Fallback (`UniqueId` -> `Id` only): Rejected because elements from different documents or sessions have different `Id`s / `UniqueId`s despite sharing the same `Name` and `Class`.
- Full 5-Step Identity Fallback (`UniqueId` -> `Id` -> `Class`/`Category` + `Name` -> `Aliases`): Accepted as the universal rule across RevitDOM.

### Decision Outcome

1. Implement `IEquatable<ElementIdModel>` on `ElementIdModel`.
2. Override `Equals(object)`, `Equals(ElementIdModel)`, `operator==`, and `operator!=` to execute the 5-step fallback identity strategy:
   - Step 1: `UniqueId` match (+ Class type guard)
   - Step 2: `Id` integer match (+ Class type guard, preserving built-in negative IDs)
   - Step 3: Type/Class guard verification
   - Step 4: `Name` + `Class`/`Category` match (case-insensitive)
   - Step 5: `Aliases` match / Name vs Aliases cross-match
3. Implement `GetHashCode()` using normalized `Name` and `Class`/`Category` when `Name` is populated (falling back to `UniqueId`/`Id` when `Name` is empty), ensuring equal objects yield identical hash codes.
4. Overload `IPocoIdentityService` with `AreSameIdentity()` for `ElementIdModel`, `ElementModel`, and mixed pairs.

### Consequences

- All WPF bindings and dictionary lookups for `ElementIdModel` transparently resolve equal element identities.
- `GetHashCode()` is aligned with the fallback equality contract.
