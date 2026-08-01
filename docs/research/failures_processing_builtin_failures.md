# Research Report: Built-In Failures & Standard Resolution Patterns

**Ticket:** Wayfinder #91  
**Module:** FailuresProcessing / Standard Operations  
**Target:** Preset Rule Library Specification for Revit Failure Handling  

---

## Executive Summary

Revit database operations — including standard model manipulations, family loading, document purges, and group edits — routinely trip native Revit failure conditions. In headless, batch, or automated add-in workflows, unresolved failure dialogs block execution threads and degrade add-in reliability.

This research report documents commonly encountered `BuiltInFailures` definitions across key add-in operation categories, analyzes default severities and resolution options, and provides the architectural specification for a **Failure Rule Preset Library** to power the `FailuresProcessing` module.

---

## Revit Failure Handling Mechanics

Revit exposes programmatic failure processing via two main API mechanisms:

1. **`IFailuresPreprocessor` Interface**: Attached directly to transaction options (`Transaction.GetFailureHandlingOptions().SetFailuresPreprocessor(...)`). Ideal for operation-scoped isolation.
2. **`ControlledApplication.FailuresProcessing` Event**: Application-wide event raised during transaction commits. Ideal for global failure handling or fallback error resolution.

### Core Processing Objects & Enums

* **`FailuresAccessor`**: Interface for inspecting and resolving active failures in a transaction lifecycle.
* **`FailureMessageAccessor`**: Represents an individual failure instance; provides `GetFailureDefinitionId()`, `GetSeverity()`, `GetDescriptionText()`, and resolution options.
* **`FailureSeverity`**:
  * `Warning`: Non-fatal message. Can be suppressed using `failuresAccessor.DeleteWarning(failure)`.
  * `Error`: Fatal condition preventing commit unless resolved via `failuresAccessor.ResolveFailure(failure)` or unconstrained.
  * `DocumentReset`: System failure requiring document rollback or reload.
* **`FailureProcessingResult`**:
  * `ProceedWithCommit`: Signals that active failures have been handled/resolved and transaction commit may proceed.
  * `ProceedWithRollBack`: Signals that failures cannot be safely resolved and forces transaction rollback.
  * `Continue`: Hands control back to Revit's default UI dialog processing loop.
  * `WaitForInput`: Pauses processing in modal UI contexts.

---

## Analysis of Common Built-In Failures by Operational Subsystem

### 1. Model Operations

Model operations involve placing, moving, joining, or modifying model geometry and annotations.

* **`BuiltInFailures.RoomFailures.RoomNotEnclosed`**
  * *Severity:* `Warning`
  * *Description:* Placed room or space is not bounded by enclosed bounding elements.
  * *Resolution Options:* `DeleteWarning`, `ProceedWithCommit`.
  * *Handling Pattern:* Safe to delete warning when processing room models or updating room properties in batch operations.
* **`BuiltInFailures.OverlapFailures.WallOverlap` / `DuplicateInstances`**
  * *Severity:* `Warning`
  * *Description:* Highlighted walls/instances overlap or occupy identical coordinates.
  * *Resolution Options:* `DeleteWarning`, `ProceedWithCommit`.
  * *Handling Pattern:* Delete warning when automated element generation places overlapping geometry intended for joining or splitting.
* **`BuiltInFailures.JoinFailures.CannotJoinElements`**
  * *Severity:* `Warning` / `Error`
  * *Description:* Automatic geometry join between structural/architectural elements failed.
  * *Resolution Options:* `DeleteWarning` (if Warning), `ResolveFailure` (unjoin elements), `ProceedWithRollBack`.
  * *Handling Pattern:* Attempt default resolution to unjoin elements; if warning only, delete warning.
* **`BuiltInFailures.DimensionFailures.DimensionInvalid`**
  * *Severity:* `Warning`
  * *Description:* Dimension reference point was deleted or moved, invalidating dimension segment.
  * *Resolution Options:* `DeleteWarning`, `ResolveFailure`.
  * *Handling Pattern:* Delete warning during batch geometric changes or purge dimension references.

---

### 2. Family Loading & Management

Family loading and updating operate on family symbols, parameters, and types across document boundaries.

* **`BuiltInFailures.FamilyFailures.FamilyNameInUse`**
  * *Severity:* `Warning`
  * *Description:* Loaded family name exists with parameter definition conflicts.
  * *Resolution Options:* Handled via `IFamilyLoadOptions` (`overwriteParameterValues = true`) combined with `DeleteWarning`.
  * *Handling Pattern:* Delete warning when reloading updated family definitions programmatically.
* **`BuiltInFailures.FamilyFailures.FamilySymbolNotFound`**
  * *Severity:* `Error`
  * *Description:* Required family symbol/type missing during instantiation or parameter reassignment.
  * *Resolution Options:* `ResolveFailure` (substitute default symbol) or `ProceedWithRollBack`.
  * *Handling Pattern:* Log failure telemetry and attempt default symbol substitution or rollback transaction.
* **`BuiltInFailures.FamilyFailures.FamilyParamConflict` / `SharedParameterConflict`**
  * *Severity:* `Warning`
  * *Description:* Parameter definition in loaded family conflicts with existing shared parameter file bindings.
  * *Resolution Options:* `DeleteWarning`, `ProceedWithCommit`.
  * *Handling Pattern:* Delete warning when enforcing project standards or applying standardized parameter definitions.

---

### 3. Purge & Deletion Operations

Purge operations delete unused element types, materials, styles, and families.

* **`BuiltInFailures.PurgeFailures.PurgeUnusedElementsFailed`**
  * *Severity:* `Warning` / `Error`
  * *Description:* Attempted to purge elements that have hidden or nested dependencies.
  * *Resolution Options:* `DeleteWarning`, `ResolveFailure`, `ProceedWithCommit`.
  * *Handling Pattern:* Suppress warning in `PurgeFailuresPreprocessor` to allow purge of independent elements to commit cleanly.
* **`BuiltInFailures.DeletionFailures.CannotDeleteElement`**
  * *Severity:* `Error`
  * *Description:* Element is locked by system or workset checkout.
  * *Resolution Options:* `ProceedWithRollBack`.
  * *Handling Pattern:* Log element ID telemetry and issue transaction rollback.
* **`BuiltInFailures.DeletionFailures.ElementsWillBeDeleted` / `DependencyDeletion`**
  * *Severity:* `Warning`
  * *Description:* Deleting target parent element will delete hosted children or dependent sub-elements.
  * *Resolution Options:* `DeleteWarning`, `ProceedWithCommit`.
  * *Handling Pattern:* Delete warning when bulk-purging parent categories or host elements.

---

### 4. Group Manipulations

Group manipulations include placing, editing, updating, or exploding model and detail groups.

* **`BuiltInFailures.GroupFailures.GroupConstraintsFailed`**
  * *Severity:* `Warning` / `Error`
  * *Description:* Constraints within group instance cannot be satisfied during move/edit operations.
  * *Resolution Options:* `DeleteWarning` (if warning), `ResolveFailure` (unconstrain/ungroup), `ProceedWithCommit`.
  * *Handling Pattern:* Delete warning or resolve failure by removing constraint, preventing modal popup.
* **`BuiltInFailures.GroupFailures.GroupMembersChanged`**
  * *Severity:* `Warning`
  * *Description:* Elements inside a group instance were modified independently of group definition.
  * *Resolution Options:* `DeleteWarning`, `ProceedWithCommit`.
  * *Handling Pattern:* Delete warning during batch modification of grouped elements.
* **`BuiltInFailures.GroupFailures.UngroupOrCancel`**
  * *Severity:* `Error`
  * *Description:* Group instance geometry cannot be maintained; group must be ungrouped or operation cancelled.
  * *Resolution Options:* `ResolveFailure` (ungroup instance) or `ProceedWithRollBack`.
  * *Handling Pattern:* Call `ResolveFailure` to automatically ungroup conflicting instances.

---

## Preset Rule Library Specification for `FailuresProcessing`

To ensure robust, targeted failure handling without risking forbidden blanket warning suppression (per repository standards in `revit-failure-handling` skill), the `FailuresProcessing` module preset library defines the following mapping matrix:

| Failure Definition ID | Category | Default Severity | Resolution Action | Target Result |
| :--- | :--- | :--- | :--- | :--- |
| `RoomFailures.RoomNotEnclosed` | Model Ops | Warning | `DeleteWarning` | `ProceedWithCommit` |
| `OverlapFailures.WallOverlap` | Model Ops | Warning | `DeleteWarning` | `ProceedWithCommit` |
| `OverlapFailures.DuplicateInstances` | Model Ops | Warning | `DeleteWarning` | `ProceedWithCommit` |
| `JoinFailures.CannotJoinElements` | Model Ops | Warning / Error | `DeleteWarning` / `ResolveFailure` | `ProceedWithCommit` |
| `DimensionFailures.DimensionInvalid` | Model Ops | Warning | `DeleteWarning` | `ProceedWithCommit` |
| `FamilyFailures.FamilyNameInUse` | Family Load | Warning | `DeleteWarning` | `ProceedWithCommit` |
| `FamilyFailures.FamilyParamConflict` | Family Load | Warning | `DeleteWarning` | `ProceedWithCommit` |
| `FamilyFailures.FamilySymbolNotFound` | Family Load | Error | `ResolveFailure` / Telemetry | `ProceedWithRollBack` (if unresolvable) |
| `PurgeFailures.PurgeUnusedElementsFailed` | Purge | Warning / Error | `DeleteWarning` / `ResolveFailure` | `ProceedWithCommit` |
| `DeletionFailures.ElementsWillBeDeleted` | Purge | Warning | `DeleteWarning` | `ProceedWithCommit` |
| `GroupFailures.GroupConstraintsFailed` | Group Ops | Warning / Error | `DeleteWarning` / `ResolveFailure` | `ProceedWithCommit` |
| `GroupFailures.GroupMembersChanged` | Group Ops | Warning | `DeleteWarning` | `ProceedWithCommit` |
| `GroupFailures.UngroupOrCancel` | Group Ops | Error | `ResolveFailure` (Ungroup) | `ProceedWithCommit` |

---

## Failure Processing Implementation Pattern

Below is the recommended standard preprocessor pattern for preset-driven failure handling:

```csharp
using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace Synthetic.Modules.FailuresProcessing.Utilities
{
    /// <summary>
    /// Targeted failure preprocessor utilizing the FailuresProcessing preset rule library.
    /// Strictly avoids blanket warning suppression by matching specific FailureDefinitionIds.
    /// </summary>
    public class PresetFailuresPreprocessor : IFailuresPreprocessor
    {
        private readonly HashSet<FailureDefinitionId> _targetWarningIds;

        public PresetFailuresPreprocessor(IEnumerable<FailureDefinitionId> targetWarningIds)
        {
            _targetWarningIds = new HashSet<FailureDefinitionId>(targetWarningIds);
        }

        public FailureProcessingResult PreprocessFailures(FailuresAccessor failuresAccessor)
        {
            IList<FailureMessageAccessor> failures = failuresAccessor.GetFailureMessages();

            foreach (FailureMessageAccessor failure in failures)
            {
                FailureDefinitionId failId = failure.GetFailureDefinitionId();
                FailureSeverity severity = failure.GetSeverity();

                if (severity == FailureSeverity.Warning && _targetWarningIds.Contains(failId))
                {
                    failuresAccessor.DeleteWarning(failure);
                }
                else if (severity == FailureSeverity.Error && failure.HasResolution())
                {
                    failuresAccessor.ResolveFailure(failure);
                }
            }

            return FailureProcessingResult.ProceedWithCommit;
        }
    }
}
```

---

## Conclusion & Next Steps

1. Integrate this failure catalog into the `FailuresProcessing` preset library module.
2. Standardize module transactions to supply target failure presets via `SetFailuresPreprocessor(...)`.
3. Monitor failure resolution telemetry to refine failure preset rules over time.
