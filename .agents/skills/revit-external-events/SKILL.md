---
name: revit-external-events
description: Modeless context patterns for external events. Use when writing code that executes Revit API code from modeless windows (WPF) or background threads.
---

# Revit Modeless External Events Playbook (`revit-external-events`)

Use this playbook to safely execute Revit API modifications from a modeless context (such as WPF windows, async tasks, or background threads) without triggering cross-thread exceptions.

## 1. The Dedicated Handler Pattern

All modeless Revit interactions must use dedicated, feature-specific implementations of `IExternalEventHandler` and `ExternalEvent`:

* **Single-Instance Lifetime:** Instantiate the `ExternalEvent` exactly once (e.g. during the WPF View Model's initialization) using `ExternalEvent.Create(handler)`. Keep the event instance alive as a private class field, and dispose of it when the UI window closes.
* **Thread-Safe Data Handoff:** Transfer inputs from the UI thread to the handler by updating public properties on the handler class before calling `.Raise()`.

## 2. Gating and UI Synchronization

To prevent duplicate runs, race conditions, or Revit UI lockups from multiple rapid user clicks:

1. **Disable UI:** Immediately disable the triggering buttons/controls in the WPF UI before calling `externalEvent.Raise()`.
2. **Execution Gating:** The handler must check an internal `IsRunning` boolean flag at the start of `Execute()` to immediately discard duplicate event dispatches.
3. **Completion Dispatch Callback:** Ensure that a `finally` block in the handler dispatches a notification back to the UI thread (via the WPF Dispatcher) to clear the busy state and re-enable the UI controls:

```csharp
public void Execute(UIApplication app)
{
    if (IsRunning) return;
    IsRunning = true;

    try
    {
        // Revit API operations...
    }
    catch (Exception ex)
    {
        LogException(ex);
    }
    finally
    {
        IsRunning = false;
        // Dispatch UI update back to the main WPF thread
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            _viewModel.OnExecutionCompleted();
        });
    }
}
```

## 3. Explicit Transaction Handling

Revit does **not** wrap external event executions in automatic transactions. You must explicitly instantiate and manage transactions inside the handler's `Execute` method:

* Wrap database changes in a `Transaction` or `TransactionGroup` using standard `using` blocks.
* Follow the transaction playbooks for error logging, rollback safety, and transaction group assimilation.
