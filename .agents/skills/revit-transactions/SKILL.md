---
name: revit-transactions
description: Strategy for Revit transactions and transaction groups. Use when writing code that makes changes to the Revit database or executes commands.
---

# Revit Transaction Management Playbook (`revit-transactions`)

Use these patterns to maintain memory stability, preserve performance, and provide a clean Undo stack for the user when modifying the Revit database.

## 1. The Assimilated Transaction Group Pattern

Wrap multi-phase database modifications in a single overarching `TransactionGroup`, collapse them on success, and isolate individual changes using standard `Transaction` blocks.

### Scope Elevation
Initialize the `TransactionGroup` at the highest logical tier of the call stack (e.g., the external command entry point or main view model handler) rather than inside deep sub-services or loops.

### Individual Transactions
Within the group, perform logical batches of work in separate `Transaction` blocks. This isolates errors and keeps each block focused:

```csharp
using (TransactionGroup tg = new TransactionGroup(doc, "Import Project Standards"))
{
    tg.Start();

    // Process elements in logical batches
    foreach (var batch in elementBatches)
    {
        using (Transaction t = new Transaction(doc, $"Import Batch {batch.Name}"))
        {
            t.Start();
            try
            {
                // Perform Revit database modifications here
                ProcessBatch(batch);
                t.Commit();
            }
            catch (Exception ex)
            {
                t.Rollback();
                // Capture element failures and proceed with other batches
                LogFailure(batch, ex);
            }
        }
    }

    // Collapse all successful transactions into a single entry in Revit's Undo stack
    tg.Assimilate();
}
```

## 2. Key Transaction Rules

1. **The Partial-Commit Rule:** If an intermediate transaction fails inside an execution loop, commit the valid transactions and record the specific element failures. Avoid nuclear rollbacks of the entire operation unless it is a fatal requirement of the feature.
2. **The Assimilation Rule:** Always call `group.Assimilate()` at the end of a successful transaction group to keep the user's Undo menu tidy. Use `group.RollBack()` only if the entire operation fails.
3. **Deep Seams Rule:** Keep sub-services and deep modules transactional-agnostic. Deep modules should accept a reference to an active document or context and perform operations without initiating their own internal transactions.
