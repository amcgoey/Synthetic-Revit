---
name: revit-multi-versions
description: Enforce inverted preprocessor directive hierarchy and manage Revit version increments. Use when writing code targeting multiple Revit versions.
---

# Revit Multi-Version Playbook (`revit-multi-versions`)

This playbook governs how to write and refactor C# code to support multiple Revit versions (2023–2027+) and their respective .NET frameworks (.NET Framework 4.8, .NET 8, .NET 10+).

## 1. Inverted Preprocessor Hierarchy

Isolate legacy Revit API branches so dropping an older version deletes only its guarded block. Place the newest supported Revit version (defined by `CURRENT_LATEST_VERSION` in [`docs/agents/revit-config.md`](../../../docs/agents/revit-config.md)) in the unconditioned `#else` baseline, and quarantine older versions inside `#if` or `#elif` blocks.

### Example:
```csharp
#if REVIT2023
    // Legacy: ParameterType was removed in Revit 2024
    ParameterType paramType = definition.ParameterType;
#elif REVIT2024
    // Transitionary: GetDataType() replaces ParameterType on .NET Framework 4.8
    ForgeTypeId dataType = definition.GetDataType();
#else
    // Default baseline: Revit 2025+ (includes 2026, 2027+)
    ForgeTypeId dataType = definition.GetDataType();
#endif
```

## 2. Framework Dividers (.NET Framework vs. .NET Core/Modern)

Apply the same inverted preprocessor hierarchy when bridging .NET Framework 4.8, .NET 8, and .NET 10+:
* Keep the newest framework version (.NET 10+) as the unconditioned `#else` baseline.
* Quarantine older framework code (e.g., .NET Framework 4.8 or .NET 8) inside explicit Revit version checks (e.g., `#if REVIT2023 || REVIT2024`).

## 3. Version Upgrade Protocol

Audit version targets whenever a new `.csproj` compilation target is added to the repository or when requested by the user. Keep existing source files untouched until the user approves the baseline upgrade.

1. **Audit:** Scan all `.csproj` files in the repository and compare the highest version suffix against `CURRENT_LATEST_VERSION` in [`docs/agents/revit-config.md`](../../../docs/agents/revit-config.md).
2. **Flag:** If a higher-version project target exists (e.g., `Synthetic2027.csproj` when `CURRENT_LATEST_VERSION` is `2026`), halt execution and prompt the user:
   * *"Detected new compilation target: Synthetic2027.csproj. Would you like to update CURRENT_LATEST_VERSION to 2027?"*
3. **Refactor Gate:** Once approved, update `docs/agents/revit-config.md` and ask explicit permission before refactoring source files:
   * *"Updating baseline to 2027. Would you like me to refactor our current unconditioned code paths into an explicit #elif REVIT2026 branch to clear the default block for the new 2027 API?"*
