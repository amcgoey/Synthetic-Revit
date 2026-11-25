---
name: revit-multi-versions
description: Enforce inverted preprocessor directives and manage Revit version and .NET TFM targets. Use when writing multi-version C# across Addin, Core, or Bootstrap, or auditing/adding a Revit version target.
---

# Revit Multi-Version Playbook (`revit-multi-versions`)

Govern cross-version C# preprocessor directives, `.NET` target frameworks (TFMs), and per-year project heads across **Revit Versions** (baseline defined by `CURRENT_LATEST_VERSION` in [`docs/agents/revit-config.md`](../../../docs/agents/revit-config.md)).

## 1. Inverted Preprocessor Hierarchy (`Addin/` & `Revit/` Tests)

Quarantine legacy Revit API branches inside `#if` / `#elif` blocks and keep the newest **Revit Version** (`CURRENT_LATEST_VERSION`) in the unconditioned `#else` baseline, so retiring an older release deletes only its guarded block.

```csharp
#if REVIT2023
    // Legacy: ParameterType was removed in Revit 2024
    ParameterType paramType = definition.ParameterType;
#elif REVIT2024
    // Transitionary: GetDataType() replaces ParameterType on .NET Framework 4.8
    ForgeTypeId dataType = definition.GetDataType();
#else
    // Default baseline: Revit 2025+
    ForgeTypeId dataType = definition.GetDataType();
#endif
```

## 2. Framework Dividers by Assembly Partition

Keep the newest `.NET` runtime (`net8.0+` / `net10.0+`) in the unconditioned `#else` baseline and select the preprocessor symbol by compilation boundary:

| Partition / Project | Framework Guard | Rationale & Reference |
| :--- | :--- | :--- |
| `Core/` & `Logic/` (`Synthetic.Core`, `Synthetic.LogicTests`) | `#if NETFRAMEWORK` / `#else` | Multi-targets `<TargetFrameworks>` with pure BCL/WPF and zero `REVIT202*` symbols (enforced by `FitnessRule.CorePurityAndZeroPreprocessor` in [`ArchitectureFitnessTests.cs`](../../../tests/Architecture/Logic/ArchitectureFitnessTests.cs)); see [`Config.cs`](../../../src/Platform/Settings/Core/Config.cs). |
| **Add-In Bootstrap** (`src/Synthetic.Bootstrap/`) | `#if NETFRAMEWORK` / `#else` (or `#if !NETFRAMEWORK` for modern-only `using`s) | Shared source compiled per year (`AppDomain` vs `AssemblyLoadContext`) with zero `#if REVIT<Year>` directives; resolves `VersionNumber` from Revit first and falls back fail-fast to compile-time `[AssemblyMetadata("RevitVersion", "<Year>")]`; see [`BootstrapApp.cs`](../../../src/Synthetic.Bootstrap/BootstrapApp.cs). |
| `Addin/` & `Revit/` (`Synthetic<Year>`, `Synthetic.RevitTests.<Year>`) | `#if REVIT2023 \|\| REVIT2024` / `#else` (or `#if NETFRAMEWORK` for pure BCL/loader APIs) | Quarantines legacy `net48` **Revit Versions** behind explicit `REVIT<Year>` checks while keeping the newest `.NET` and **Revit Version** in `#else`. Use [`RevitVersionMetadata`](../../../src/Platform/Revit/Addin/RevitVersionMetadata.cs) (reading `[AssemblyMetadata("RevitVersion", "<Year>")]` emitted by [`src/Directory.Build.targets`](../../../src/Directory.Build.targets)) for payload version fallbacks instead of hardcoded years or `#if REVIT<Year>` blocks (e.g., [`RibbonManager.cs`](../../../src/Platform/Host/Addin/RibbonManager.cs), [`ETransmitAssemblyResolver.cs`](../../../src/Platform/Revit/Addin/ETransmitAssemblyResolver.cs)). |

## 3. `Synthetic.Core` + `Synthetic.LogicTests` Multi-TFM Checklist

[`src/Synthetic.Core/Synthetic.Core.csproj`](../../../src/Synthetic.Core/Synthetic.Core.csproj) and [`tests/Projects/Synthetic.LogicTests/Synthetic.LogicTests.csproj`](../../../tests/Projects/Synthetic.LogicTests/Synthetic.LogicTests.csproj) multi-target the active `.NET` `<TargetFrameworks>` (`net48;net8.0-windows8.0;net10.0-windows8.0`—verify directly in those `.csproj` files):

* **Same `.NET` TFM:** Make **`0` `Core` edits** when a new **Revit Version** shares an existing `.NET` TFM—leave `Synthetic.Core.csproj`, `Core/` sources, and `Synthetic.LogicTests.csproj` `<TargetFrameworks>` untouched.
* **Upgraded `.NET` TFM (new major `.NET` release):** Append `;netX.0-windows8.0` (e.g., `;net11.0-windows8.0`) to `<TargetFrameworks>` in both [`src/Synthetic.Core/Synthetic.Core.csproj`](../../../src/Synthetic.Core/Synthetic.Core.csproj) and [`tests/Projects/Synthetic.LogicTests/Synthetic.LogicTests.csproj`](../../../tests/Projects/Synthetic.LogicTests/Synthetic.LogicTests.csproj), then run [`revit-tests`](../revit-tests/SKILL.md) (`--version Logic`) to verify every TFM compiles and passes.

## 4. Version Target Audit & Upgrade Protocol

Audit version targets whenever a new `.csproj` compilation target is added or when requested by the user. Keep existing source files untouched until the user approves the baseline upgrade.

1. **Audit Project Heads & Registration:** Scan `.csproj` targets across `src/` and `tests/` and compare the highest version suffix against `CURRENT_LATEST_VERSION` in [`docs/agents/revit-config.md`](../../../docs/agents/revit-config.md). Every supported **Revit Version** (`<Year>`) registers three project heads plus shared wiring:
   * **Payload:** `src/Synthetic<Year>/Synthetic<Year>.csproj` (`<IsRevitPayloadProject>true</IsRevitPayloadProject>`, `<RevitVersion><Year></RevitVersion>`, `REVIT<Year>`)
   * **Add-In Bootstrap:** `src/Synthetic.Bootstrap<Year>/Synthetic.Bootstrap<Year>.csproj` (`<IsBootstrapProject>true</IsBootstrapProject>`, `<RevitVersion><Year></RevitVersion>`, `REVIT<Year>`)
   * **Revit Test Head:** `tests/Projects/Synthetic.RevitTests.<Year>/Synthetic.RevitTests.<Year>.csproj` (`<IsRevitTestProject>true</IsRevitTestProject>`, `REVIT<Year>`, `<AssemblyMetadata Include="NUnit.Version" Value="<Year>" />`; [`tests/Directory.Build.targets`](../../../tests/Directory.Build.targets) auto-wires shared test globs, NUnit metadata, and Debug signing)
   * **Solution & Targets Wiring:** Register heads in [`src/Synthetic.sln`](../../../src/Synthetic.sln), add `<InternalsVisibleTo Include="Synthetic.RevitTests.<Year>" />` under `Condition="'$(IsCoreProject)' == 'true'"` in [`src/Directory.Build.targets`](../../../src/Directory.Build.targets) (payload `InternalsVisibleTo` derives `Synthetic.RevitTests.$(RevitVersion)` dynamically, and `src/Directory.Build.targets` emits `[AssemblyMetadata("RevitVersion", "$(RevitVersion)")]` automatically for payload and bootstrap projects), and verify `FitnessRule.CoLocatedPartitionLayout` (dynamic `20\d{2}` heads) and `FitnessRule.CorePurityAndZeroPreprocessor` (dynamic `CURRENT_LATEST_VERSION` enforcement) pass via [`ArchitectureFitnessTests.cs`](../../../tests/Architecture/Logic/ArchitectureFitnessTests.cs).
2. **Flag:** If a higher-version project target exists (e.g., `Synthetic2028.csproj` when `CURRENT_LATEST_VERSION` is `2027`), halt execution and prompt the user:
   * *"Detected new compilation target: Synthetic2028.csproj. Would you like to update CURRENT_LATEST_VERSION to 2028?"*
3. **Refactor Gate:** Once approved, update [`docs/agents/revit-config.md`](../../../docs/agents/revit-config.md) and ask explicit permission before refactoring source files:
   * *"Updating baseline to 2028. Would you like me to refactor our current unconditioned code paths into an explicit #elif REVIT2027 branch to clear the default block for the new 2028 API?"*
