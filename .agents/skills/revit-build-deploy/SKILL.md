---
name: revit-build-deploy
description: Build, sign, isolate, and deploy Synthetic across Revit versions. Use when compiling Debug or Release x64 builds, isolating headless test builds, attaching in-place debuggers, staging UNC studio releases, or configuring PDQ workstation installs.
---

# Revit Build & Deploy (`revit-build-deploy`)

Architectural contract and rationale live in [ADR 0018](../../../docs/adrs/0018-universal-add-in-bootstrap-manifest-routing-and-studio-deployment.md). Authoritative MSBuild and deployment definitions live in [Directory.Build.props](../../../src/Directory.Build.props), [Directory.Build.targets](../../../src/Directory.Build.targets), [Deploy-To-INC-Server.template.bat](../../../build/Deploy-To-INC-Server.template.bat), and [Install-Synthetic-Workstation.template.bat](../../../build/Install-Synthetic-Workstation.template.bat).

## 1. Platform & Output Layout

Target `x64` (`src/Synthetic.sln` or `-p:Platform=x64`) across supported **Revit Versions** (`2023–2027`; see `CURRENT_LATEST_VERSION` in [revit-config.md](../../../docs/agents/revit-config.md)). Requires a `.NET 10.0` SDK (`10.0.100+`) via `dotnet build` (legacy `MSBuild.exe` cannot resolve `net10.0-windows8.0` SDK targets). Standard builds write to:

- `output/Synthetic/Bootstrap/v1/`: `Synthetic.Bootstrap<Year>.dll` (**Add-In Bootstrap**)
- `output/Synthetic/Payload/<Year>/`: `Synthetic<Year>.dll`, dependencies, assets, and settings templates
- `output/Manifest for Deploy/`: `Release` `.addin` manifests (`Synthetic<Year>.addin`)

`GenerateRevitAddinManifest` in [Directory.Build.targets](../../../src/Directory.Build.targets) generates `Synthetic<Year>.addin` with `FullClassName` = `Synthetic.Bootstrap.BootstrapApp`, `ClientId` = `4fd5b26e-1325-460f-97b1-878804070868`, and `<ManifestSettings><UseRevitContext>True</UseRevitContext></ManifestSettings>` for `2026+`.

## 2. Build Isolation & Test Workers

Pass `/p:SyntheticIsolatedBuildRoot=<dir>` and `/p:SyntheticRunId=<id>` when running headless or parallel builds. [Directory.Build.props](../../../src/Directory.Build.props) automatically sets `SkipDeployAddin=true` and isolates `obj/` (`<root>\obj\<Project>\`) and `bin/` (`<root>\bin\<Project>_<RunId>\`), leaving `%APPDATA%` **Local Deployment** and `output/` untouched.

Automated test workers set `SYNTHETIC_TEST_WORKER=1` (and `SYNTHETIC_TEST_RUN_ID=<RunId>`) so `BootstrapApp` skips payload staging and logs `SYNTHETIC_TEST_RUN:<RunId>` to the **Revit Journal**.

## 3. Debug Profile & Local Deployment

- **Manifest & Local Deployment:** `Debug` manifests target `output\Synthetic\Bootstrap\v1\Synthetic.Bootstrap<Year>.dll` and copy `Synthetic<Year>.addin` to `%APPDATA%\Autodesk\Revit\Addins\<Year>\` (**Local Deployment**).
- **Signing:** `SignDebugBuild` signs intermediate assemblies via Windows SDK `signtool.exe` (`SHA256`) using the local self-signed `"Synthetic"` certificate.
- **Shadow Cache vs. In-Place Attach:** By default, **Add-In Bootstrap** stages `Payload/<Year>/` into `%LOCALAPPDATA%\Synthetic\ShadowCache\<Year>\<PID>_<UtcStartTicks>\` and sets `SYNTHETIC_ADDIN_ROOT` to the source `Payload/<Year>/` directory. Set `SYNTHETIC_DISABLE_SHADOW_COPY=1` in the debug session environment to load `Payload/<Year>/Synthetic<Year>.dll` directly in place for interactive debugger attach.

## 4. Release Profile & UNC Staging

`Release` assemblies are intentionally unsigned locally. `Release` manifests in `output/Manifest for Deploy/` route `<Assembly>` to `C:\ProgramData\Autodesk\Revit\Addins\Synthetic\Bootstrap\v1\Synthetic.Bootstrap<Year>.dll` for `2023–2026` (`< 2027`) and `C:\Program Files\Autodesk\Revit\Addins\Synthetic\Bootstrap\v1\Synthetic.Bootstrap<Year>.dll` for `2027+` (`>= 2027`).

1. Compile `Release|x64` via the `.NET 10` CLI:
   ```powershell
   dotnet build src/Synthetic.sln -c Release -p:Platform=x64
   ```
2. Stage to the studio UNC share using [Deploy-To-INC-Server.template.bat](../../../build/Deploy-To-INC-Server.template.bat) (set `LOCAL_OUTPUT_PATH`, `UNC_SERVER_PATH`, and `NO_PAUSE=1` for unattended runs). The script mirrors `Bootstrap/v1/` and `Payload/<Year>/` (`/MIR /XF *.addin`), copies `Release` `.addin` manifests to `Manifest for Deploy\`, and overlays `%LOCAL_OUTPUT_PATH%\INC Settings` into every 4-digit `Payload/<Year>/` folder (`/E`, preserving per-year binaries).

## 5. Elevated Workstation Installation (PDQ)

Deploy [Install-Synthetic-Workstation.template.bat](../../../build/Install-Synthetic-Workstation.template.bat) in an elevated workstation context (set `UNC_SERVER_PATH`, optional overrides `PROGRAMDATA_ROOT`, `PROGRAMFILES_ROOT`, `REVIT_INSTALL_ROOT`, and `NO_PAUSE=1` for unattended runs):

- Purges retired `2022` manifests (`Synthetic2022.addin`) and legacy flat `ProgramData\...\Synthetic\` root artifacts while preserving `Bootstrap\` and `Payload\`.
- Routes each installed **Revit Version** (`< 2027` to `PROGRAMDATA_ROOT`, `>= 2027` to `PROGRAMFILES_ROOT`), copies `Synthetic<Year>.addin`, seeds `Bootstrap/v1/` with `/XC /XN /XO` (skip-existing so locked live session binaries stay intact), and mirrors `Payload/<Year>/` (`/MIR`) for `Stage-While-Open, Activate-on-Next-Restart` daytime updates.
- Enforces bounded `robocopy /R:3 /W:2 /NP` (`0..7` = success; `>= 8` exits immediately with a non-zero code).

## 6. Verification

Confirm completion against the active branch:
- **Debug:** `output/Synthetic/Bootstrap/v1/Synthetic.Bootstrap<Year>.dll`, `output/Synthetic/Payload/<Year>/Synthetic<Year>.dll`, and `%APPDATA%\Autodesk\Revit\Addins\<Year>\Synthetic<Year>.addin` exist, with `<Assembly>` pointing to `output\Synthetic\Bootstrap\v1\`.
- **Isolated:** Outputs exist under `$(SyntheticIsolatedBuildRoot)\bin\` and `%APPDATA%\Autodesk\Revit\Addins\<Year>\` is unmodified.
- **Release & Studio Deploy:** `output/Manifest for Deploy/Synthetic<Year>.addin` points to `ProgramData` (`2023–2026`) or `Program Files` (`2027+`), UNC staging overlays `INC Settings` per `Payload/<Year>/`, and workstation install exits `0`.
