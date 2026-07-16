---
name: revit-multi-versions
description: Enforce inverted preprocessor directive hierarchy and manage Revit version increments. Use when writing code targeting multiple Revit versions.
---

# Revit Multi-Version Playbook (`revit-multi-versions`)

This playbook governs how to write and refactor C# code to support multiple Revit versions (2022–2027+) and their respective .NET frameworks (.NET Framework 4.8, .NET 8, .NET 10+).

## 1. Inverted Preprocessor Hierarchy

To make it easy to drop support for deprecated Revit versions, write all version-conditional code using an **inverted preprocessor hierarchy**:
* **The Default Baseline:** Write the newest supported Revit version (defined by `CURRENT_LATEST_VERSION` in `docs/agents/revit-config.md`) as the unconditioned `#else` or default code block.
* **The Legacy Condition:** Quarantine all older versions inside explicit `#if` or `#elif` blocks. When a version is dropped, simply delete its isolated block.

### Example:
```csharp
#if REVIT2022 || REVIT2023
    // Legacy: ParameterType is deprecated in 2022 and removed in 2024
    ParameterType paramType = definition.ParameterType;
#elif REVIT2024
    // Transitionary: GetDataType() returning ForgeTypeId (ParameterType removed)
    ForgeTypeId dataType = definition.GetDataType();
#else
    // DEFAULT baseline: Revit 2025+ (includes 2026, 2027, etc.)
    // Clean, modern, unconditioned code branch.
    ForgeTypeId dataType = definition.GetDataType();
#endif
```

## 2. Framework Dividers (.NET Framework vs. .NET Core/Modern)

Follow the same inverted preprocessor hierarchy when handling differences between .NET Framework 4.8, .NET 8, and future versions like .NET 10:
* Keep the newest framework version (.NET 10+) as the unconditioned `#else` baseline.
* Quarantine older framework code (e.g., .NET Framework 4.8 or .NET 8) inside explicit Revit version checks (e.g., `#if REVIT2022 || REVIT2023 || REVIT2024`).

## 3. Version Upgrade Protocol

Do not automatically refactor files when a new version target is introduced. Perform checks only at startup, `/finalize`, or during the `/check-version` command.

1. **Audit:** Scan all `.csproj` files in the repository. Compare the highest version suffix against `CURRENT_LATEST_VERSION` in `docs/agents/revit-config.md`.
2. **Flag:** If a higher-version project target is discovered (e.g., `Synthetic2027.csproj` when `CURRENT_LATEST_VERSION` is `2026`):
   * Halt execution and prompt the user: *"Detected new compilation target: Synthetic2027.csproj. Would you like to update CURRENT_LATEST_VERSION to 2027?"*
3. **Refactor Gate:** If the user approves, update the configuration file. Then ask explicit permission to refactor:
   * *"Updating baseline to 2027. Would you like me to refactor our current unconditioned code paths into an explicit #elif REVIT2026 branch to clear the default block for the new 2027 API?"*
