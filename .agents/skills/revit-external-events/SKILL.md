---
name: revit-external-events
description: Modeless context patterns for external events. Use when writing code that executes Revit API code from modeless windows (WPF) or background threads.
---

# Revit Modeless External Events Playbook (`revit-external-events`)

Safely execute Revit API modifications from modeless WPF windows and toast notifications under the current window-scoped single-instance `ExternalEvent` contract ([CODING_STANDARDS.md](../../../CODING_STANDARDS.md), [ADR 0027](../../../docs/adrs/0027-tiered-vertical-slice-architecture-and-seam-cqs.md); `ICoalescedRevitDispatcher<TAdapter>` replaces direct handler injection in Spec 5).

## 1. Window-Scoped Single-Instance `ExternalEvent` Contract

Enforced by `FitnessRule.ExternalEventAndDialogPurity` ([ADR 0029](../../../docs/adrs/0029-isomorphic-test-mirror-and-architecture-fitness-guardrails.md)): keep `ExternalEvent.Create(handler)` at the `IExternalCommand.Execute` (or [`App.OnDocumentOpen`](../../../src/Platform/Host/Addin/App.cs)) entry point—never inside `ViewModels/` and never as a `static ExternalEvent` field.

* **Command-Owned Session Lifecycle:** As exemplified in [`CmdProjectStandards.cs`](../../../src/Features/StandardsManagement/Addin/Commands/CmdProjectStandards.cs), `IExternalCommand.Execute` tracks one `_windowInstance` (`Focus()` if `IsLoaded`, cleared on `Closed`), instantiates one `IExternalEventHandler` + `ExternalEvent.Create(handler)` per session, injects both into the window/ViewModel, and disposes the `ExternalEvent` when the session ends.
* **Modal Alternative:** For modal workflows (`ShowDialog()`), run synchronously inside `IExternalCommand.Execute` with [`ProgressCoordinator`](../../../src/Platform/UI/Addin/ProgressCoordinator.cs) instead of creating an `ExternalEvent`.

## 2. Gating and UI Synchronization

1. **Mark Busy & Check `Raise()` Result:** Set UI busy state before calling `externalEvent.Raise()`, and clear busy state immediately if `Raise()` returns `ExternalEventRequest.Denied` or `ExternalEventRequest.TimedOut`.
2. **Execution Gating:** Guard `Execute(UIApplication app)` with an `IsRunning` flag to drop duplicate turns.
3. **Dispatcher Callback & Zero Handler Dialogs:** Marshal completion back via WPF `Dispatcher.Invoke` in `finally`, returning **Typed Outcome Objects** rather than calling `TaskDialog.Show` or `MessageBox.Show` inside handlers or ViewModels (`FitnessRule.ExternalEventAndDialogPurity`):

```csharp
public void Execute(UIApplication app)
{
    if (_isRunning) return;
    _isRunning = true;

    try
    {
        Document? doc = app.ActiveUIDocument?.Document;
        if (doc == null) return;

        doc.ExecuteTransaction(
            "Apply Modeless Update",
            FailurePipelines.ObserveOnly(),
            () => ApplyUpdate(doc));
    }
    finally
    {
        _isRunning = false;
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            _onCompleted();
        });
    }
}
```

## 3. Managed Transactions & Failure Handling

Revit does **not** wrap external event callbacks in automatic transactions. Route every database write inside `IExternalEventHandler.Execute` through [`DocumentTransactionExtensions`](../../../src/Platform/Revit/Addin/DocumentTransactionExtensions.cs) (`ExecuteTransaction`, `ExecuteCommittedBatchGroup`, `ExecuteRolledBackGroup`) with an explicit [`FailurePipelines`](../../../src/Platform/Revit/Addin/FailurePipelines.cs) preprocessor per [`revit-transactions`](../revit-transactions/SKILL.md) and [`revit-failure-handling`](../revit-failure-handling/SKILL.md).
