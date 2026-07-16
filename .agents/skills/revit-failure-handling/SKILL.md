---
name: revit-failure-handling
description: How to implement IFailuresPreprocessor to suppress/resolve Revit native dialog alerts. Use when handling warnings/errors or using transactions.
---

# Revit Failure Handling Playbook (`revit-failure-handling`)

Use this playbook to programmatically suppress warnings, resolve expected errors, and log failure telemetry during database transactions.

## 1. The Dedicated Preprocessor Pattern

To ensure clean isolation of domain rules:
* Create dedicated implementations of `IFailuresPreprocessor` for specific subsystems or commands (e.g. `SuppressConstraintsPreprocessor`).
* Attach the preprocessor to the `Transaction` before calling `Start()`:
  ```csharp
  FailureHandlingOptions options = transaction.GetFailureHandlingOptions();
  options.SetFailuresPreprocessor(new MyDedicatedPreprocessor());
  transaction.SetFailureHandlingOptions(options);
  ```

## 2. Failure Filtering and Resolution Rules

* **Targeted ID Filtering:** By default, match against specific `FailureDefinitionId`s (from `BuiltInFailures`) to resolve or delete warnings:
  ```csharp
  if (failure.GetFailureDefinitionId() == BuiltInFailures.GroupFailures.GroupConstraintsFailed)
  {
      failuresAccessor.DeleteWarning(failure);
  }
  ```
* **Blanket Suppression Gate:** You are **strictly prohibited** from implementing blanket warning suppression (suppressing all warnings in a transaction without ID matching) unless you obtain explicit developer/user permission.
* **Telemetry and Continuation:** For unresolvable errors:
  1. Extract and log the failure details (Failure Definition ID, severity, and message description) to the application log/telemetry stream.
  2. Return `FailureProcessingResult.Continue` to let Revit bubble up the warning/error safely.
