---
name: revit-debug-by-user
description: Launch an interactive Visual Studio and Revit debug session via start_debugging.ps1. Use when starting F5 debugging or inspecting a Synthetic build in Revit.
---

# Revit User Debugging Playbook (`revit-debug-by-user`)

Launch an interactive Visual Studio debugging session for a targeted **Revit Version** via [start_debugging.ps1](scripts/start_debugging.ps1). For build outputs, signing, and shadow-cache internals, see [revit-build-deploy](../revit-build-deploy/SKILL.md).

## 1. Launch from an Interactive Terminal

Agent tool shells run in non-interactive Windows Session 0, which hides Visual Studio and Revit windows. Have the user run [start_debugging.ps1](scripts/start_debugging.ps1) directly in their own interactive PowerShell terminal:

- **Latest supported version (`CURRENT_LATEST_VERSION` in [revit-config.md](../../../docs/agents/revit-config.md)):**
  ```powershell
  powershell -File .agents/skills/revit-debug-by-user/scripts/start_debugging.ps1
  ```
- **Specific Revit Version (e.g., `2024`):**
  ```powershell
  powershell -File .agents/skills/revit-debug-by-user/scripts/start_debugging.ps1 -Version 2024
  ```

## 2. Startup Flow & Interactive Verification

1. **Solution & Startup Project:** [start_debugging.ps1](scripts/start_debugging.ps1) attaches to or launches Visual Studio, opens [src/Synthetic.sln](../../../src/Synthetic.sln), sets `src/Synthetic<Year>/Synthetic<Year>.csproj` (for example, `Synthetic2026\Synthetic2026.csproj`) as the startup project, and starts debugging (`F5`).
2. **Build & Bootstrap Entry:** Compiling `Synthetic<Year>` builds `Synthetic.Core`, `Synthetic<Year>`, and `Synthetic.Bootstrap<Year>`, signs `Debug` binaries, and writes `%APPDATA%\Autodesk\Revit\Addins\<Year>\Synthetic<Year>.addin` (**Local Deployment**). When Revit starts, **Add-In Bootstrap** ([BootstrapApp](../../../src/Synthetic.Bootstrap/BootstrapApp.cs)) stages `output/Synthetic/Payload/<Year>/` into `%LOCALAPPDATA%\Synthetic\ShadowCache\<Year>\<PID>_<UtcStartTicks>\` (or loads `Payload/<Year>/Synthetic<Year>.dll` in place if `SYNTHETIC_DISABLE_SHADOW_COPY=1`) and invokes [Synthetic.Platform.Host.App](../../../src/Platform/Host/Addin/App.cs).
3. **Revit Launch Profile:** `Synthetic2025` and `Synthetic2026` include a `Synthetic<Year>Executable` launch profile targeting `Revit.exe` (`Properties/launchSettings.json`). For `2023–2024` (or if `F5` starts without launching Revit), launch Revit directly and use **Debug -> Attach to Process** (`Revit.exe`).
4. **Inspect in Revit:** Place breakpoints in `Core/` (`Synthetic.Core`) or `Addin/` (`Synthetic<Year>`) source files, open a target model in Revit, and invoke the command from the `"Synthetic"` ribbon tab.

**Completion criterion:** Visual Studio attaches to `Revit.exe`, the `"Synthetic"` ribbon tab initializes via `BootstrapApp` -> `Synthetic.Platform.Host.App`, and execution stops at your breakpoints.
