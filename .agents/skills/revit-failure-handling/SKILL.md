---
name: revit-failure-handling
description: Configure FailurePipelines presets, FailurePipelineBuilder, and pure FailurePipelinePolicy rules for Revit transaction failures. Use when handling Revit warnings/errors, implementing IFailuresPreprocessor, or using transactions.
---

# Revit Failure Handling Playbook (`revit-failure-handling`)

Use the dual-seam failure pipeline (`namespace Synthetic.Platform.Revit`) to evaluate warnings and errors deterministically, capture structured telemetry in `FailureProcessingReport`, and keep rule evaluation unit-testable without Revit. Attach configured preprocessors to transactions via [`revit-transactions`](../revit-transactions/SKILL.md) and [`DocumentTransactionExtensions`](../../../src/Platform/Revit/Addin/DocumentTransactionExtensions.cs).

## 1. Dual-Seam Architecture (`Core/` + `Addin/`)

Following [ADR 0028](../../../docs/adrs/0028-hybrid-synthetic-core-assembly-and-colocated-partitions.md), pure failure policy lives in `Synthetic.Core` (`Core/`) while Revit API adapters live in `Synthetic<Year>` (`Addin/`), keeping distinct type names across both seams inside `namespace Synthetic.Platform.Revit` to prevent `CS0433` collisions:

| Seam & Assembly | Files & Key Types | Responsibility |
| :--- | :--- | :--- |
| **`src/Platform/Revit/Core/`** (`Synthetic.Core`, `net48;net8.0-windows8.0`) | [`FailureRecord`](../../../src/Platform/Revit/Core/FailureRecord.cs), [`FailureSeverityLevel`](../../../src/Platform/Revit/Core/FailureSeverityLevel.cs), [`FailureHandlingAction`](../../../src/Platform/Revit/Core/FailureHandlingAction.cs), [`FailureProcessingReport`](../../../src/Platform/Revit/Core/FailureProcessingReport.cs), [`IFailurePolicyRule`](../../../src/Platform/Revit/Core/IFailurePolicyRule.cs), [`TargetIdFailurePolicyRule`](../../../src/Platform/Revit/Core/TargetIdFailurePolicyRule.cs), [`SeverityFailurePolicyRule`](../../../src/Platform/Revit/Core/SeverityFailurePolicyRule.cs), [`FailurePipelinePolicy`](../../../src/Platform/Revit/Core/FailurePipelinePolicy.cs), [`FailurePipelinePolicyBuilder`](../../../src/Platform/Revit/Core/FailurePipelinePolicyBuilder.cs), [`FailurePipelinePolicies`](../../../src/Platform/Revit/Core/FailurePipelinePolicies.cs) | Zero `Autodesk.Revit.*` dependencies. Evaluates primitive failure DTOs against ordered rules, enforces `AllowBlanketSuppression = false` by default, stamps `ContextTag`, and records `FailureRecord` telemetry (`WarningTripped` flips `true` when any record is `Deleted`). |
| **`src/Platform/Revit/Addin/`** (`Synthetic<Year>`) | [`CompositeFailuresPreprocessor`](../../../src/Platform/Revit/Addin/CompositeFailuresPreprocessor.cs), [`FailurePipelineBuilder`](../../../src/Platform/Revit/Addin/FailurePipelineBuilder.cs), [`FailurePipelines`](../../../src/Platform/Revit/Addin/FailurePipelines.cs), [`IFailureRule`](../../../src/Platform/Revit/Addin/IFailureRule.cs), [`TargetIdFailureRule`](../../../src/Platform/Revit/Addin/TargetIdFailureRule.cs), [`SeverityFailureRule`](../../../src/Platform/Revit/Addin/SeverityFailureRule.cs) | Thin `IFailuresPreprocessor` adapters translating Revit `FailureMessageAccessor` / `FailuresAccessor` calls to `FailurePipelinePolicy`, executing `DeleteWarning` / `ResolveFailure`, and logging `[FailureTelemetry]` to `Debug.WriteLine`. |

## 2. `FailurePipelines` Presets

Select a preset from [`FailurePipelines`](../../../src/Platform/Revit/Addin/FailurePipelines.cs) when passing the mandatory `IFailuresPreprocessor` to `CreateTransaction` or `ExecuteTransaction`:

| Preset | Overloads | Behavior & Use Case |
| :--- | :--- | :--- |
| **`FailurePipelines.ObserveOnly()`** | `()`, `(FailureProcessingReport)`, `(out FailureProcessingReport)` | Non-mutating diagnostic preset (`AllowBlanketSuppression = false`, zero rules). Records all failures into `Report` with `ActionTaken = PassedThrough` and `WarningTripped = false`, leaving native Revit warnings/errors untouched (`FailureProcessingResult.Continue`). Default choice when transactions need visibility without suppression. |
| **`FailurePipelines.Purge()`** | `()` | Targeted preset for element deletion and purge operations. Registers `TargetIdFailureRule` for `BuiltInFailures.EditingFailures.ElementsWillBeDeleted` and `BuiltInFailures.EditingFailures.ElementsDeleted`, deleting and recording only those warnings while passing unrelated failures through. |
| **`FailurePipelines.SuppressConstraints()`** | `()`, `(FailureProcessingReport)`, `(out FailureProcessingReport)` | Targeted preset for group/constraint modifications. Resolves via reflection across Revit 2023–2026 `BuiltInFailures.GroupFailures` (`GroupConstraintsFailed`, `CannotRemoveGroupMemberConstraintWarn`, `CannotRemoveGroupMemberConstraint`, `RemoveGroupSketchConstraintParent`) and `BuiltInFailures.ConstraintFailures` (`UndeletedConstraintsInGroup`), deleting matched warnings (`WarningTripped = true`) and passing unrelated failures through. |
| **`FailurePipelines.DeleteAllWarningsRecorded()`** | `()`, `(FailureProcessingReport)`, `(out FailureProcessingReport)` | Blanket warning-suppression preset (renamed from `DeleteWarnings()`, backed by `FailurePipelinePolicies.DeleteAllWarningsRecorded` with `AllowBlanketSuppression = true`). Deletes every unmatched `Warning` with full telemetry recording (`ActionTaken = Deleted`, `WarningTripped = true`) while passing `Error` and `DocumentCorruption` through. Subject to the **Blanket Suppression Gate** below. |

## 3. Rule Matching, Suppression Gate, & Telemetry

* **Targeted Matching Default & Blanket Suppression Gate:** Match specific `FailureDefinitionId`s (`FailurePipelineBuilder.OnFailure(...)`, `Purge()`, or `SuppressConstraints()`) or observe passively with `ObserveOnly()`. Both `FailurePipelinePolicyBuilder` and `FailurePipelineBuilder` default `AllowBlanketSuppression` to `false`. Blanket warning suppression (`FailurePipelines.DeleteAllWarningsRecorded()` or `.AllowBlanketSuppression(true)`) is **strictly prohibited** unless explicitly warranted and authorized by the user/developer or an approved spec/ADR; when authorized, always use `DeleteAllWarningsRecorded()` so every suppressed warning stays recorded in `Report` and `[FailureTelemetry]`.
* **Evaluation Order & Action Mapping:** `CompositeFailuresPreprocessor.PreprocessFailures` evaluates each `FailureMessageAccessor` against registered `IFailureRule`s in order and stops on the first rule where `Evaluates` and `Execute` succeed:
  * `TargetIdFailureRule` and `SeverityFailureRule` delegate to their `Core/` policy rules: `Warning` maps to `Deleted` (`failuresAccessor.DeleteWarning`), `Error` maps to `Resolved` (`failuresAccessor.ResolveFailure`), and other severities pass through.
  * `PreprocessFailures` returns `FailureProcessingResult.ProceedWithCommit` when at least one failure is handled, or `FailureProcessingResult.Continue` when all failures record `PassedThrough` so Revit surfaces unresolvable warnings and errors safely.
* **Custom Pipelines & Batch Context Tagging:** Build targeted rules with `FailurePipelineBuilder` (Revit) or `FailurePipelinePolicyBuilder` (pure `Core/` logic), and call `SetContext(...)` per loop item so `FailureRecord.ContextTag` and `[FailureTelemetry]` pinpoint failing items:

```csharp
CompositeFailuresPreprocessor preprocessor = new FailurePipelineBuilder()
    .OnFailure(BuiltInFailures.EditingFailures.PinnedObjectNotDeleted)
    .Build();

foreach (var item in items)
{
    preprocessor.SetContext($"Item:{item.Id}");
    doc.ExecuteTransaction($"Update {item.Name}", preprocessor, () => UpdateItem(doc, item));
}

FailureProcessingReport report = preprocessor.Report;
```
