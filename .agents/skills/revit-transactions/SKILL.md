---
name: revit-transactions
description: Manage Revit transactions and TransactionGroups via DocumentTransactionExtensions. Use when writing code that modifies the Revit database or executes commands.
---

# Revit Transaction Management Playbook (`revit-transactions`)

Use [`DocumentTransactionExtensions`](../../../src/Platform/Revit/Addin/DocumentTransactionExtensions.cs) (`namespace Synthetic.Platform.Revit`) to execute Revit database modifications with entry guards, clean Undo-stack lifetimes, and explicit failure handling ([`revit-failure-handling`](../revit-failure-handling/SKILL.md)).

## 1. Managed Transaction & Group APIs (`DocumentTransactionExtensions`)

### Entry Guards
`ExecuteTransaction`, `ExecuteCommittedBatchGroup`, and `ExecuteRolledBackGroup` validate `doc`, non-whitespace `name`, and non-null `action` on entry, then check `doc.IsReadOnly` and `doc.IsModifiable`—throwing `InvalidOperationException` if the document is read-only or already has an open transaction.

### Single Transactions & Mandatory Failure Policy
Every `CreateTransaction` and `ExecuteTransaction` overload requires a non-null `IFailuresPreprocessor` (`ArgumentNullException` when `null`; zero default-null overloads). Pass a preset from [`FailurePipelines`](../../../src/Platform/Revit/Addin/FailurePipelines.cs) (`ObserveOnly()`, `Purge()`, `SuppressConstraints()`, or — only when explicitly authorized — `DeleteAllWarningsRecorded()`) or a custom pipeline per [`revit-failure-handling`](../revit-failure-handling/SKILL.md):

* **`doc.ExecuteTransaction(name, preprocessor, action, clearAfterRollback = false)`** (`Action`, `Action<Document>`, or `Action<Transaction>`): Starts and commits the `Transaction`, rolls back if `tx.GetStatus() == TransactionStatus.Started` on exception before rethrowing, and returns [`FailureProcessingReport`](../../../src/Platform/Revit/Core/FailureProcessingReport.cs) (populated from `CompositeFailuresPreprocessor.Report`).
* **`doc.CreateTransaction(name, preprocessor, clearAfterRollback = false)`**: Returns an unstarted `Transaction` with `FailureHandlingOptions` (`SetFailuresPreprocessor` and optional `SetClearAfterRollback(true)`) configured for callers managing `Start()`/`Commit()`/`RollBack()` manually.

### Transaction Group Wrappers
* **`doc.ExecuteCommittedBatchGroup(name, action)`** (`Action` or `Action<Document>`): Wraps persistent multi-transaction batches inside a `TransactionGroup`. Calls `group.Assimilate()` on clean completion to collapse inner commits into a single Undo-stack entry. On exception, calls `group.RollBack()` via `TryRollBackGroupWithoutMasking`—if an inner `Transaction` remained open and Revit throws during group rollback, the rollback failure logs to `Debug.WriteLine` so the original inner exception propagates unmasked.
* **`doc.ExecuteRolledBackGroup(name, action)`** (`Action` or `Action<Document>`): Wraps ephemeral probes, non-mutating exports, or integration tests inside a `TransactionGroup` that calls `group.RollBack()` unconditionally in `finally`, leaving zero persistent model or Undo-stack changes. Calls `group.RollBack()` directly after clean completion and `TryRollBackGroupWithoutMasking` during exception unwinding so inner exceptions stay unmasked.

## 2. Batch Execution Pattern

```csharp
doc.ExecuteCommittedBatchGroup("Import Project Standards", () =>
{
    foreach (var batch in elementBatches)
    {
        try
        {
            FailureProcessingReport report = doc.ExecuteTransaction(
                $"Import Batch {batch.Name}",
                FailurePipelines.ObserveOnly(),
                () => ProcessBatch(doc, batch));

            RecordBatchTelemetry(batch, report);
        }
        catch (Exception ex)
        {
            // ExecuteTransaction rolled back the failing batch transaction; record and proceed.
            // Allow fatal batch-wide exceptions to escape so ExecuteCommittedBatchGroup rolls back the group.
            LogFailure(batch, ex);
        }
    }
});
```

## 3. Architectural Transaction Rules

1. **Scope Elevation:** Start `ExecuteCommittedBatchGroup` or `ExecuteRolledBackGroup` at the highest orchestration tier (external command, external event handler, or export entry point).
2. **Transaction-Agnostic Deep Seams:** Keep sub-services and domain helpers free of internal `Transaction` or `TransactionGroup` creation; accept `Document` (or a context wrapper) and execute within caller-managed scopes.
3. **Partial-Commit Isolation:** Inside batch loops, isolate each independent unit in its own `ExecuteTransaction` within `ExecuteCommittedBatchGroup`; catch non-fatal per-item failures inside the loop so valid commits assimilate together, and let fatal batch-wide failures escape to roll back the group.
