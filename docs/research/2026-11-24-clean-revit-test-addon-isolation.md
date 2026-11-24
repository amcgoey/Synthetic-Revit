# Research: Non-Brittle, Concurrent-Safe Clean-Process Add-In Isolation for Automated Revit Tests (2023–2027+)

**Date:** 2026-11-24  
**Scope:** Running automated `ricaun.RevitTest` integration tests across Revit 2023–2026 (and forward-looking 2027+) in a clean `Revit.exe` process with third-party add-ins unloaded—retaining only `Synthetic` (`Synthetic.Bootstrap` / test payload), `ricaun.RevitTest.Application`, and required Autodesk built-ins—while the developer concurrently works in interactive Revit with all normal third-party add-ins enabled.

---

## Executive Summary

**Core Finding:** Across Revit 2023, 2024, 2025, 2026, and 2027, **Autodesk provides no native per-process command-line switch, environment variable, or `AddInsSettings.json` / `Revit.ini` profile selector to suppress third-party add-ins per launch while selectively keeping test/dev add-ins enabled.**

Every built-in Autodesk mechanism (`Revit.ini` `[API] DisableUserAddinsAndMacros=1`, `AddInsSettings.json` `DisableAllAddIns: true`, `AdminSettings.json`) is **global per user or machine**, absent in Revit 2023–2024 (`AddInsManager` did not ship until Revit 2025.3), raced at both startup read and post-load write (`DesktopMFC.dll` -> `APIInterop!updateAddInsSettings`), and—when global disable flags are set—**also drops `ricaun.RevitTest.Application` (`ApplicationPlugins`) and `Synthetic.Bootstrap`** because Revit discards all `current user`, `all user`, `custom`, and `bundle` manifest lists before loading. Undocumented internal mechanisms (`/localbasepath`, journal playback `startedWithAutomation()`, `RevitDebugModes` `EnableSlimStartupAddinFiltering`) fail cleanly due to `.NET` `Environment.GetFolderPath` bypassing native `SpecialFolders`, Revit 2027 path regressions, or admin-protected global `addinsLoadInfo.ini` state in `%ProgramFiles%`.

Because brittle host-file mutations (renaming `.addin` files or swapping `AddInsSettings.json.bak`) and in-process `IExternalApplication` / manifest-wrapper gating are ruled out on crash-safety, concurrency, Authenticode/ALC, and installer-fragility grounds, **two genuinely non-brittle, concurrent-safe paths exist**:

1. **Zero-Hook, Multi-Seat Gold Standard (Cleanest Overall, Non-`$0`):** Dedicated CI workstation or persistent test VM assigned its own Autodesk seat/Flex licensing with **only** Revit 2023–2027, `eTransmitForRevitDB`, `ricaun.RevitTest.Application`, and `Synthetic` test payloads installed (zero third-party add-ins installed at all).
2. **Single-Workstation `$0` Clean-Process Isolation (Best Same-Host Technical Solution):** A **path-allowlist user-mode Detours shim** (`DetourCreateProcessWithDllsW` + signed C++ hook DLL) injected solely into test `Revit.exe` child PIDs spawned by a custom worker launcher / `RevitWorkerPool` driving `\\.\pipe\ricaun.Revit.<PID>`. By filtering directory enumeration (`FindFirstFile*` / `NtQueryDirectoryFile`) and file opens (`GetFileAttributesExW` / `CreateFileW` / `NtOpenFile` / `NtCreateFile`) **exclusively** on Revit's `.addin` folders, `ApplicationPlugins/*.bundle` folders, and `AddInsSettings.json`/`AdminSettings.json`, test processes see only `Synthetic` + `ricaun.RevitTest.Application` across Revit 2023–2027+ with zero host file mutation, full crash safety, and zero interference with interactive Revit—at the engineering cost of maintaining a signed native hook DLL (with EDR review) and bypassing `ricaun.RevitTest.Console.exe`'s own `Process.Start`.

Where custom native Win32/NT hooking is not justified, the **Pragmatic `$0` Fallback** is to retain the repository's current zero-hook isolation architecture ([BootstrapApp.cs](../../src/Synthetic.Bootstrap/BootstrapApp.cs) `SYNTHETIC_TEST_WORKER=1`, `NUnit.Open=true`, [test_executor.py](../../.agents/skills/revit-tests/scripts/test_executor.py) `SyntheticIsolatedBuildRoot` + `RicaunBundleLease`) paired with a pre-booted `RevitWorkerPool` standby worker to hide third-party load latency (`~1–3s` test execution), while cleaning up the stale `%ProgramData%\Autodesk\Revit\Addins\<Year>\Synthetic202*.addin` shadow hazard.

Additionally, binary inspection of `eTransmitForRevit.dll`, `eTransmitForRevitDB.dll`, and [ETransmitAssemblyResolver.cs](../../src/Platform/Revit/Addin/ETransmitAssemblyResolver.cs) confirms that `BootstrapWorkerIsolationRevitTests.TestWorker_DocumentUtilPurge_ResolvesETransmitWithoutHostStartup` **does not require `Autodesk.eTransmitApplication.addin` to load at startup**—it requires only `eTransmitForRevitDB.dll` on disk.

---

## 1. Revit Native Mechanisms & Add-In Discovery Lifecycle (2023–2027+)

Primary-source IL and x64 disassembly of Revit 2023, 2024, 2025, 2026, and 2027 (`Revit.exe`, `DesktopMFC.dll`, `Utility.dll`, `APIInterop.dll`, `RevitAddInUtility.dll`, `PackageContentsParser.dll`, and `Autodesk.AddInsManagerDB.dll`) establishes the exact discovery pipeline and why native switches cannot provide concurrent-safe per-process isolation.

### 1.1 Startup Add-In Discovery Paths & Load Order (2023–2026 vs. 2027+)

In `APIInterop.dll` (`AddInManagerUtils::loadAddins`), Revit resolves add-in sources differently between 2023–2026 and 2027+:

| Discovery Category | Revit 2023–2026 Resolution | Revit 2027 Resolution | API Mechanism Observed |
| :--- | :--- | :--- | :--- |
| **Internal Built-Ins** | `%ProgramFiles%\Autodesk\Revit <ver>\AddIns\` | `%ProgramFiles%\Autodesk\Revit 2027\AddIns\` (now also includes `BatchPrint`, `eTransmitForRevit`, `TotalCarbonAnalysis`, `WorksharingMonitor`, `FormItConverter`) | `AutoName.getInternalAddinsDedicatedPath` -> `AddInManifestUtility.GetRevitInternalAddInManifests` |
| **Current User `.addin`** | `%APPDATA%\Autodesk\Revit\Addins\<ver>\` | `%APPDATA%\Autodesk\Revit\Addins\2027\` | `AutoName.getCurrentUserAddinsDedicatedPath` -> `DirectoryInfo.GetFiles("*.addin")` |
| **All Users `.addin`** | `%ProgramData%\Autodesk\Revit\Addins\<ver>\` | `%ProgramFiles%\Autodesk\Revit\Addins\2027\` (primary); `%ProgramData%\Autodesk\Revit\Addins\2027\` **ignored unless** `AdminSettings.json` sets `"AdditionalOption": "Load Add-ins from ProgramData"` | `AutoName.getAllUsersAddinsDedicatedPath` (+ 2027 `getAllUsersAddinsDedicatedProgramDataPath` gated by `AdminSettingsUtility.IsAdditionalOptionEnabled()`) |
| **User Bundles** | `%APPDATA%\Autodesk\ApplicationPlugins\*.bundle` | `%APPDATA%\Autodesk\ApplicationPlugins\*.bundle` | `PackageContentsParser.GetDedicatedCurrUserFolder` (`SpecialFolder(26)` / `CSIDL_APPDATA`) |
| **All-User Bundles** | `%ProgramData%\Autodesk\ApplicationPlugins\*.bundle` | `%ProgramFiles%\Autodesk\ApplicationPlugins\*.bundle` **and** `%ProgramData%\Autodesk\ApplicationPlugins\*.bundle` | `PackageContentsParser.GetDedicatedAllUserFolder` (`35` in 2023–2026; `38` + `35` `GetDedicatedProgramDataAllUserFolder` in 2027) |
| **Custom Folders** | `ManageCustomAddinFolders` set | `ManageCustomAddinFolders` set | `ManageCustomAddinFolders.getCustomAddinFolders` |
| **`AddInsSettings.json`** | **Absent in 2023–2024**; `%APPDATA%\Autodesk\Revit\Autodesk Revit <ver>\AddinsData\AddInsSettings.json` in **2025.3–2026** | `%APPDATA%\Autodesk\Revit\Autodesk Revit 2027\AddinsData\AddInsSettings.json` | `AddInsManagerUtility.GetAddInsSettingsFilePath` (`SpecialFolder(26)`) |
| **`AdminSettings.json`** | `%ProgramData%\Autodesk\RVT <ver>\Admin\AdminSettings.json` (**2025.4–2026**) | `%ProgramFiles%\Autodesk\Revit\Admin\2027\AdminSettings.json` | `AdminSettingsUtility.GetAdminSettingsFilePath` (`SpecialFolder(35)` in 2025–2026; `SpecialFolder(38)` in 2027) |

**Exact Load Order & Duplicate Precedence (`AddInManagerUtils::loadAddins` & `updateNameListFromManifests`):**
1. **Internal promotion (`moveInternalAddins`):** Before applying disable flags, `APIInterop` scans `currentUser`, `allUser`, `custom`, and `bundle` manifests and moves any manifest containing one of 10 hardcoded GUIDs (`AddInsManagerUtility.InternalAddInsIds`: Batch Print, Collaborate/DB, Skyscraper, eTransmit, Insights, Worksharing Monitor, FormIt) into the `internal` list. Because `internal` manifests pass `null` for `AddInsManagerSettings`, those built-ins ignore `AddInsSettings.json` disable state.
2. **Duplicate `.addin` filename precedence:** `updateNameListFromManifests` inserts lists in fixed order: `internal` (into `internalNames`), then `"current user"` (`%APPDATA%`), `"all user program files"` (2027), `"all user"` (`%ProgramData%` / `%ProgramFiles%`), `"custom"`, and `"bundle"` (into `nonInternalNames`). Key lookup is by `RevitAddInManifest.Name` (`.addin` filename). If `"current user"` and `"all user"` share the same filename (for example `Synthetic2026.addin`), **first-in (`%APPDATA%`) wins** and `all user` is skipped with a duplicate warning (`"Duplicate addins: ..."`).
3. **Alphabetical initialization (`Sort`):** Both `internalNames` and `nonInternalNames` are sorted alphabetically (`List<string>.Sort()`), then concatenated (`internalNames.AddRange(nonInternalNames)`). Within `nonInternalNames`, Autodesk loads manifests strictly alphabetically by filename (e.g., `0_Enscape.addin` before `bluebeamAddin21.addin`, `CTC-BIM-Project-Suite.addin`, `Kinship.addin`, `RDES.KMPlus.addin`, `ricaun.RevitTest.Application.bundle`, `Synthetic2026.addin`).

### 1.2 Documented & Binary-Verified CLI Switches (`Revit.exe` / `DesktopMFC.dll`)

Scanning `DesktopMFC.dll` (`0x1801D2BE0` CLI switch table and UTF-16 string tables across Revit 2023, 2026, and 2027) confirms the exact CLI switch inventory ([Revit Command Line Options](https://help.autodesk.com/view/RVT/2024/ENU/?guid=GUID-BD09C1B4-5520-475D-BE7E-773642EEBD6C)):

- **Parsed by `DesktopMFC.dll`:** `/runhidden`, `/runmaximized`, `/invisible`, `/noninteractive`, `/translate`, `/exclude`, `/username`, `/debugmode`, `/allstrings`, `/nomacro`, `/nosplash`, `/viewer`, `/language` (plus trailing `.rvt`/`.rfa`/`.txt` file arguments).
- **Parsed by `Utility.dll` (`FileUtil::evaluateCommandLinePaths`):** `/hosted`, `/localbasepath <dir>`.
- **Confirmed absent across Revit 2023–2027:** `/noaddins`, `/safemode`, `/ini`, or any flag selecting an alternate `AddInsSettings.json` or add-in profile.

### 1.3 Native Disable Switches (`Revit.ini`, `AddInsSettings.json`, `AdminSettings.json`)

| Native Mechanism | Version Support | Scope | Fatal Blockers for Clean Concurrent Test Execution |
| :--- | :--- | :--- | :--- |
| **`Revit.ini` `[API] DisableUserAddinsAndMacros=1`** ([Autodesk Help](https://help.autodesk.com/cloudhelp/2025/CHS/Revit-Customize/files/GUID-B6ED7657-9D81-4BFF-B72D-84B5FDA5F611.htm)) | 2023–2027+ | Global per user (`%APPDATA%\Autodesk\Revit\Autodesk Revit <ver>\Revit.ini`) | 1. **Blocks test runner & SUT:** In `AddInManagerUtils::initialize` (`IL_00DC..00F4`), setting `DisableUserAddinsAndMacros=1` sets `bDisableUserAddinsAndMacros=true`, which nulls `currentUser`, `custom`, and `bundle` in `loadAddins` (`IL_0410..0421`)—**preventing `ricaun.RevitTest.Application` and `Synthetic.Bootstrap` from loading**.<br>2. **No `/ini` CLI flag:** Cannot be scoped per process.<br>3. Breaks concurrent interactive Revit (cannot disable just for tests without mutating shared `Revit.ini`). |
| **`AddInsSettings.json`** (`%APPDATA%\...\AddinsData\`) ([Autodesk Support](https://www.autodesk.com/support/technical/article/caas/sfdcarticles/sfdcarticles/Failed-to-save-Add-Ins-Manager-settings-when-disabling-add-ins-using-Add-in-Manager-in-Revit.html)) | **2025.3–2027+ only** (absent in 2023–2024) | Global per user | 1. **Zero 2023–2024 coverage.**<br>2. **Post-load overwrite race:** Immediately after add-in initialization in `DesktopMFCApp::InitInstance` (`0x1801B1A56`), Revit calls `APIInterop!updateAddInsSettings`, which executes `File.WriteAllText` (`AddInsManagerUtility::SaveAddInsSettings`) back to `%APPDATA%\...\AddInsSettings.json` with `LoadTime` telemetry. Swapping files per launch races both test-worker startup and interactive Revit startup/teardown.<br>3. `DisableAllAddIns: true` triggers the exact same `bDisableUserAddinsAndMacros=true` code path, dropping `ricaun.RevitTest.Application` and `Synthetic.Bootstrap`. |
| **`AdminSettings.json`** (`(Admin) Add-Ins Manager`) ([Autodesk Support](https://www.autodesk.com/support/technical/article/caas/sfdcarticles/sfdcarticles/How-to-manage-addins-on-a-multi-user-virtual-environment-for-Revit.html)) | **2025.4–2027+ only** | Global per machine (`%ProgramData%` in 2025–2026; `%ProgramFiles%` in 2027) | 1. Machine-wide scope breaks concurrent interactive third-party add-ins.<br>2. Missing in 2023–2024; requires admin rights (`%ProgramData%`/`%ProgramFiles%`).<br>3. Even when `getAdminForcedAddIns()` runs under `DisableAllAddIns=true`, it still forces all-or-nothing machine policy and skips `ApplicationPlugins` (`bundle` is nulled!). |

### 1.4 Undocumented Internal Mechanisms Verified in Revit Binaries

Three undocumented internal paths discovered in `Utility.dll` and `APIInterop.dll` were inspected to verify whether any offers a hidden clean-launch path:

1. **`/localbasepath <dir>` (`Utility.dll` `SpecialFolders::obtainOverridePathIfAppropriate`):**
   - Present in Revit 2023–2027 `Utility.dll`. Overrides native C++ `SpecialFolders` lookups **only** for `CSIDL_APPDATA` (`0x1A` -> `<dir>\LocalAppData`), `CSIDL_LOCAL_APPDATA` (`0x1C` -> `<dir>\LocalNonRoamingAppData`), and `CSIDL_COMMON_APPDATA` (`0x23` -> `<dir>\LocalAllUsersAppData`), and only when `flags & 1 == 0`.
   - **Why it fails:**
     - Both `RevitAddInUtility.dll` (`AddInsManagerUtility.GetAddInsSettingsFilePath`, `AdminSettingsUtility.GetAdminSettingsFilePath`) and `PackageContentsParser.dll` (`GetDedicated*Folder`) call `.NET`'s `System.Environment.GetFolderPath(...)` directly (`SHGetFolderPath` on .NET 4.8 / `SHGetKnownFolderPath` on .NET 8+), **completely bypassing `Utility.dll` `SpecialFolders`**. Consequently, `ApplicationPlugins/*.bundle` and `AddInsSettings.json` still hit real `%APPDATA%`/`%ProgramData%`.
     - In Revit 2027 `findAllUsersAddinsDedicatedPath` (`0x18015560F`), Autodesk uses `CSIDL_PROGRAM_FILES` (`0x26`) with `flags = 5` (`flags & 1 != 0`), explicitly suppressing `/localbasepath` override.
     - Redirecting `0x1A`/`0x1C`/`0x23` in native C++ also breaks/relocates `Revit.ini`, `Journals`, and `UserDataCache` unintendedly.

2. **Journal Playback Mode (`ManageJournals::startedWithAutomation()`):**
   - In `APIInterop.dll` (`2026`), when Revit is launched with a journal `.txt` script (`startedWithAutomation() == true`), `AddInManagerUtils::initialize` redirects `AddInsSettings.json` and `AdminSettings.json` to `AutoName.journalDirectory(false)`, and `loadAddins` skips `allUsers`, `currentUser`, and `bundle` manifests—loading only `*.addin` files placed inside `journalDirectory` (+ `internal` + `ManageCustomAddinFolders`).
   - **Why it fails:**
     - **Revit 2027 regression (`APIInterop.dll` `2027`, `IL_02AA..0391`):** Revit 2027 `loadAddins` changed control flow so `startedWithAutomation()` merely *appends* `journalDirectory` manifests to `currentUser` and still loads `%APPDATA%`, `%ProgramFiles%`, and `ApplicationPlugins` bundles.
     - Launching under active journal playback (`DesktopMFC` `ManageWorkstationInput::playbackOneIfAvailable`) couples process lifetime and idle dispatch to journal playback completion rather than passive Named Pipe `ExternalEvent` RPC (`\\.\pipe\ricaun.Revit.<PID>`).

3. **`RevitDebugModes` `EnableSlimStartupAddinFiltering` (`addinsLoadInfo.ini`):**
   - Verified active in Ship builds via `Utility.dll` `ManageDebugModeInitialization::loadDebugModesFromEnvironment` (`0x1803811F0`), which reads env vars `RevitDMQuietList` and `RevitDebugModes`.
   - When `EnableSlimStartupAddinFiltering` is enabled (`APIInterop.dll` `loadAddins`), Revit checks `FileUtil.determineInstallationFolder() + "\addinsLoadInfo.ini"`.
   - **Why it fails:** The INI path is fixed inside `%ProgramFiles%\Autodesk\Revit <ver>\addinsLoadInfo.ini`—**global across all concurrent `Revit.exe` processes of that year** and inside an admin-ACL directory—and executes *after* `verifySignedAddIns` (`IL_05A8`).

### 1.5 Local Workstation Baseline & `eTransmitForRevitDB` Verification

Audit of the workstation (`AddInsSettings.json` for Revit 2026 and manifest directories across 2023–2027) surfaced two concrete repository findings:

1. **Current third-party add-in footprint (Revit 2026 `AddInsSettings.json`, 16 recorded entries):**
   - **Autodesk built-ins (7):** Batch Print (`41 ms`), eTransmit for Revit (`19 ms`), Insights (`40 ms`), Worksharing Monitor (`48 ms` + Launch App `6 ms`), FormIt Conversion (`23 ms`), Import AXM (`6 ms`).
   - **Third-party add-ins (6 items across 5 vendors):** `CTC BIM Project Suite` (`1755 ms`), `Keynote Manager +` (`1189 ms`), `KM+ Keynote DB Server` (`1189 ms`), `Kinship` (`929 ms`), `BluebeamAddin` (`108 ms`), `Enscape 4.17.0` (`77 ms`), plus `BIM 42 Align.bundle` (`2018–2024`) and `AutoCAD-MCP-Server-2027.bundle` (`%ProgramFiles%\Autodesk\ApplicationPlugins`).
   - **Test / SUT entries:** `ricaun.RevitTest.Application` (`65f304b3-8efb-464d-b08a-12cfd61a1986`, `146 ms`), Debug Bootstrap `Synthetic` (`4fd5b26e-1325-460f-97b1-878804070868`, `38 ms`), and a second `Synthetic` entry (`4fd5b26e-1325-460f-97b1-878804070869`, `4 ms`).
   - **Stale `ProgramData` shadow hazard observed:** `%ProgramData%\Autodesk\Revit\Addins\2023..2026\Synthetic202*.addin` contains legacy direct Release manifests (`FullClassName=Synthetic.Core.App` pointing to `%ProgramData%\Autodesk\Revit\Addins\Synthetic\Synthetic2026.dll`). Only the `"current user"` precedence rule (`%APPDATA%\...\Synthetic2026.addin` matching filename `Synthetic2026.addin`) currently prevents Revit 2026 from loading that legacy Release DLL alongside test runs. In a test environment that hides `%APPDATA%`, stale `%ProgramData%\...\Synthetic202*.addin` would load `Synthetic.Core.App` directly unless also filtered or updated to `Synthetic.Bootstrap`.
2. **`eTransmitForRevitDB` requirement (`BootstrapWorkerIsolationRevitTests.cs`):**
   - IL verification of `C:\Program Files\Autodesk\eTransmit for Revit 2026\eTransmitForRevit.dll` (`eTransmitApplication::OnStartup`) and `eTransmitForRevitDB.dll` (`eTransmitUpgradeOMatic::purgeUnused`), together with [DocumentUtil.cs](../../src/Platform/Revit/Addin/DocumentUtil.cs#L97-L109) and [ETransmitAssemblyResolver.cs](../../src/Platform/Revit/Addin/ETransmitAssemblyResolver.cs#L32-L113), proves that:
     - `eTransmitApplication.OnStartup` only registers Ribbon UI buttons (`CreateRibbonPanel`, `PushButtonData`).
     - `Autodesk.eTransmitApplication.addin` runs with `<UseRevitContext>False</UseRevitContext>` (its own isolated ALC) and is **not** needed for `DocumentUtil.Purge`.
     - `eTransmitUpgradeOMatic.purgeUnused(doc)` references only `RevitAPI.dll` and .NET BCL assemblies (`Document.GetUnusedElements`, `Transaction`, `Document.Delete`).
     - `ETransmitAssemblyResolver` locates `eTransmitForRevitDB.dll` directly on disk (`%ProgramFiles%\Autodesk\eTransmit for Revit <Year>\eTransmitForRevitDB.dll` for 2023–2026, or `%ProgramFiles%\Autodesk\Revit 2027\AddIns\eTransmitForRevit\eTransmitForRevitDB.dll` for 2027+, verified present). Therefore **only `eTransmitForRevitDB.dll` on disk is required**.

---

## 2. Per-Process OS, Filesystem, & Path Virtualization Without Host Mutation

### 2.1 Surgical User-Mode Win32/NT API Hooking (`Microsoft Detours`) — Viable `$0` Same-Host Mechanism

Read-only PE header inspection (`dumpbin /HEADERS`) confirms that `Revit.exe` and `RevitWorker.exe` across **all five versions (2023–2027)** share `DLL characteristics = 0x8160` (`High Entropy Virtual Addresses`, `Dynamic base`, `NX compatible`, `Terminal Server Aware`) with **no Control Flow Guard (`GUARD_CF`) and no `Force Integrity`**. Standard user-mode IAT/inline hooking (`DetourCreateProcessWithDllsW` from [Microsoft Detours](https://github.com/microsoft/detours)) is therefore unobstructed by `Revit.exe` PE mitigations.

#### Why Naive `SHGetKnownFolderPath` / `SHGetFolderPathW` Redirection Fails

Intercepting `SHGetKnownFolderPath` / `SHGetFolderPathW` globally inside `Revit.exe` to spoof `%APPDATA%` and `%ProgramData%` is a **trap** across 2023–2027:
- **Breaks unrelated Revit & Autodesk subsystems:** `Revit.ini` (`CSIDL_APPDATA`), `Journals` / `UserDataCache` (`CSIDL_LOCAL_APPDATA`), `Colorbooks`, and `AdskLicensing` / `AdskIdentityManager` token caches resolve from the same Win32 Shell / KnownFolder APIs.
- **Misses Revit 2027 `%ProgramFiles%` add-ins:** Spoofing `FOLDERID_ProgramFiles` (`CSIDL_PROGRAM_FILES`) would break Revit's own `Program Files` installation path probing, DLL side-by-side loading, and `eTransmitForRevitDB.dll` resolution.

#### How a Surgical Path-Allowlist Detours Shim Works Safely

Instead of redirecting whole Known Folders, a surgical hook DLL injected into the test `Revit.exe` process intercepts file discovery and settings reads **strictly by normalized path prefix**:

1. **Hooking both .NET Framework 4.8 (Revit 2023–2024) and .NET 8/10 (Revit 2025–2027+) enumeration primitives:**
   - IL inspection of `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\mscorlib.dll` confirms `.NET Framework 4.8` `DirectoryInfo.GetFiles("*.addin")` and `GetDirectories("*.bundle")` call **`kernel32.dll!FindFirstFile`** (`FindFirstFileW` / `FindNextFileW` / `FindFirstFileExW`).
   - IL inspection of `System.Private.CoreLib.dll` (`8.0.34`, `FileSystemEnumerator<T>.GetData`) confirms `.NET 8+` bypasses `FindFirstFile` and calls **`ntdll.dll!NtQueryDirectoryFile`** directly on an open directory handle (`FILE_FULL_DIR_INFORMATION`, class `2`) alongside `NtQueryDirectoryFileEx`!
   - Therefore, a version-universal (`2023–2027+`) hook **must filter `ntdll!NtQueryDirectoryFile` / `NtQueryDirectoryFileEx`** (or both `NtQueryDirectoryFile*` and `FindFirstFile*`, mapping the directory handle back via `GetFinalPathNameByHandleW` / handle table cache) plus **`CreateFileW` / `NtCreateFile` / `NtOpenFile` / `GetFileAttributesExW` / `GetFileAttributesW`**.
2. **Scoped filtering rules (zero host mutation, PID-scoped):**
   - **Add-in manifest directories** (`%ProgramData%\Autodesk\Revit\Addins\<ver>`, `%ProgramFiles%\Autodesk\Revit\Addins\<ver>`, `%APPDATA%\Autodesk\Revit\Addins\<ver>`):
     - In directory enumeration buffers (`FileBothDirectoryInformation` / `FileFullDirectoryInformation` / `FileIdBothDirectoryInformation` / `WIN32_FIND_DATAW`), unlink/skip any `*.addin` entry **except** `Synthetic<ver>.addin` in `%APPDATA%` (when Bootstrap mode is active; skip stale `%ProgramData%\...\Synthetic<ver>.addin` and all third-party `.addin`s like `0_Enscape.addin`, `bluebeamAddin21.addin`, `CTC-BIM-Project-Suite.addin`, `RDES.KMPlus.addin`, `Kinship.addin`). Return `STATUS_OBJECT_NAME_NOT_FOUND` / `ERROR_FILE_NOT_FOUND` on direct opens to filtered paths.
   - **Bundle directories** (`%APPDATA%\Autodesk\ApplicationPlugins`, `%ProgramData%\Autodesk\ApplicationPlugins`, `%ProgramFiles%\Autodesk\ApplicationPlugins`):
     - Pass through **only** `ricaun.RevitTest.Application.bundle`; skip all other `*.bundle` directories (`Bluebeam Software`, `BIM 42 Align.bundle`, `CTC-BIM-Project-Suite.bundle`, etc.).
   - **Settings JSON virtualization (`2025.3–2027+`):**
     - Redirect reads/writes targeting `%APPDATA%\Autodesk\Revit\Autodesk Revit <ver>\AddinsData\AddInsSettings.json` (and optionally `%ProgramData%\...\AdminSettings.json` / `%ProgramFiles%\...\AdminSettings.json`) to an ephemeral per-PID temp file `%TEMP%\SyntheticCleanProfile\<PID>\AddInsSettings.json` (containing `{"DisableAllAddIns":false,"AddInItemSettings":[]}`). That guarantees interactive Revit's `AddInsSettings.json` is never read and `updateAddInsSettings` writes startup `LoadTime` telemetry to a disposable per-worker file.

#### Engineering Trade-Offs & Real-World Risks of Detours Injection

1. **Launcher integration (`ricaun.RevitTest.Console.exe` bypass):**
   - IL inspection of `ricaun.RevitTest.Console.dll` (`1.11.1`, `RevitTestUtils.CreateRevitServer`) confirms `ricaun.RevitTest.Console` spawns `Revit.exe` via `RevitInstallationExtension.Start` (`Process.Start`), NOT `DetourCreateProcessWithDllsW`.
   - Furthermore, with `NUnit.Open=false`, `TryGetProcess()` attaches to whichever `Revit.exe` PID it finds first—unsafe if interactive Revit is running; with `NUnit.Open=true`, `Console` spawns an unhooked `Revit.exe`.
   - **Implication:** Using Detours cleanly requires spawning the hooked `Revit.exe` from our own worker orchestrator (`RevitWorkerPool` or a thin custom launcher CLI using `ricaun.RevitTest.Shared.PipeTestClient` directly against `\\.\pipe\ricaun.Revit.<WorkerPID>`), rather than relying on `ricaun.RevitTest.Console.exe` to spawn `Revit.exe`.
2. **Code-Signing & AV/EDR Heuristic Risk:**
   - `DetourCreateProcessWithDllsW` modifies the suspended child's PE import table (`detoured.dll` + hook DLL) at creation time. Unsigned DLL injection into a signed Autodesk binary (`Revit.exe`) routinely triggers Defender/EDR behavioral alerts or blocks in managed environments unless the hook DLL and launcher are signed with a trusted code-signing certificate (such as the workstation's `"Synthetic"` cert or an enterprise CA) and, on corporate endpoints, excluded/allowlisted by security policy.
3. **Child Process / Chromium `WebView2` Isolation:**
   - `Revit.exe` spawns `RevitWorker.exe`, `AdskIdentityManager`, CefSharp, and `msedgewebview2.exe`. Chromium's renderer sandbox enforces strict DLL signing / Code Integrity Guard (`CIG`) / AppContainer boundaries that crash or reject unsigned/Detours-injected DLLs. The hook must **not** propagate into `msedgewebview2.exe` / `AdskIdentityManager` child processes.
4. **Native Struct & OS Maintenance Burden:**
   - Filtering `NtQueryDirectoryFile` requires safely splicing variable-length `NextEntryOffset` chains across `FileDirectoryInformation`, `FileFullDirectoryInformation`, `FileBothDirectoryInformation`, `FileNamesInformation`, `FileIdBothDirectoryInformation`, and `FileIdFullDirectoryInformation`, preserving `STATUS_NO_MORE_FILES` vs `STATUS_NO_SUCH_FILE` semantics if the first/only matching buffer is emptied, and verifying compatibility across future Windows 11 builds and `.NET 10` (`Revit 2027+`).

### 2.2 Evaluation of Other OS & Virtualization Approaches

| Technology | Primary Technical / Operational Blocker | Verdict |
| :--- | :--- | :--- |
| **Windows Projected File System (`ProjFS`)** ([PrjStartVirtualizing](https://learn.microsoft.com/en-us/windows/win32/api/projectedfslib/nf-projectedfslib-prjstartvirtualizing)) | `ProjFS` virtualization roots mark a **physical directory system-wide** (`IO_REPARSE_TAG_PROJFS`), affecting **all** processes on the host (including the developer's active interactive Revit and Explorer), and require admin elevation + reboot to enable `Client-ProjFS`. Unsuitable for per-process filtering. | **Infeasible** |
| **Custom Kernel FS Minifilter Driver** ([Microsoft Driver Signing](https://learn.microsoft.com/en-us/windows-hardware/drivers/dashboard/code-signing-attestation)) | Can technically inspect caller PID (`FltGetRequestorProcessId`), **but** requires kernel-mode C development, an **EV Code Signing Certificate**, Microsoft Partner Center Attestation/WHQL signing to load under Secure Boot on Windows 11 (Server requires full HLK), and introduces BSOD stability risk. | **Reject (Extreme Overkill)** |
| **MSIX + PSF (`FileRedirectionFixup`) / App-V** ([PSF FRF README](https://github.com/microsoft/MSIX-PackageSupportFramework/blob/master/fixups/FileRedirectionFixup/readme.md)) | Requires repackaging ~15+ GB per Revit version (2023–2027), breaks ODIS/MSI patching and `MsiEnumClients`/`Uninstall` registry queries used by `ricaun.Revit.Installation` and `AdskLicensingService`. Microsoft PSF `FileRedirectionFixup` relies on Detours itself, lacks full path canonicalization and `SetCurrentDirectory` tracking, and App-V requires Enterprise licensing / nears end of support. | **Reject** |
| **Sandboxie-Plus (`SbieDrv.sys`)** ([OpenPipePath Docs](https://sandboxie-plus.com/sandboxie/openpipepath/)) | Supports per-box `ClosedFilePath` rules without mutating host files, **but** requires installing a kernel driver (`SbieDrv.sys`), conflicts with `AdskLicensingService`, `AdskIdentityManager` OAuth token persistence, and `WebView2` AppContainers (`SBIE2112` named-pipe/IPC blocks requiring ongoing `OpenPipePath`/`OpenIpcPath` tuning per Revit update). | **Fragile / Not Recommended** |

---

## 3. Add-In / Loader-Layer Approaches & Installer Fragility

### 3.1 Can an `IExternalApplication` Disable Other Add-Ins at Runtime?

**No.** Verified in `APIInterop.dll` (`AddInManagerUtils::loadAddins` & `createAddInContexts`):
- Revit discovers, parses, deduplicates, filters against `AddInsSettings.json`, verifies Authenticode signatures (`verifySignedAddIns`), and creates isolated `.NET 8+` `AssemblyLoadContext` instances (`AddInLoader.AddInLoadContext` whenever `<UseRevitContext>False</UseRevitContext>`—confirmed in `CTC-BIM-Project-Suite.addin`, `RDES.KMPlus.addin`, `Kinship.addin`, and `Autodesk.eTransmitApplication.addin`) **before** any `IExternalApplication.OnStartup` executes.
- Furthermore, `nonInternalNames` loads alphabetically (`0_Enscape.addin`, `bluebeamAddin21.addin`, `CTC-BIM-Project-Suite.addin`, `Kinship.addin`, `RDES.KMPlus.addin`), so third-party add-ins have **already initialized** before `Synthetic2026.addin` or `ricaun.RevitTest.Application` runs, and Revit exposes no API to unload a third-party ALC or suppress sibling `OnStartup` calls.

### 3.2 Wrapping/Gating Third-Party `.addin` Manifests via a Shim Loader

Modifying third-party `.addin` files to route through a custom environment-variable-gating shim fails on four primary-source grounds:
1. **Installer / Update Fragility:** Every MSI, ODIS, or PDQ Deploy update for Enscape, Bluebeam, CTC BIM Project Suite, Keynote Manager+, Kinship, or Autodesk extensions overwrites its `.addin` manifest in `%ProgramData%\Autodesk\Revit\Addins\<ver>` or `%ProgramFiles%\Autodesk\Revit\Addins\2027`, silently re-enabling third-party add-ins or leaving stale wrappers behind.
2. **Breaks Authenticode & `VerifySignedAddIns`:** Modifying a vendor `.addin` file's `<Assembly>`/`<FullClassName>` invalidates vendor trust/GUID expectations and triggers Revit's unsigned add-in checks.
3. **Breaks `.NET 8+` Dependency Resolution (`AssemblyDependencyResolver`):** Wrapping an add-in's DLL from a different loader directory breaks its `.deps.json`, side-by-side native DLL probing, `Assembly.Location` / `ExecutingAssembly` path assumptions, and `<UseRevitContext>False</UseRevitContext>` custom ALC isolation (`CTCSoftware`, `RDES.KM`, `ANDK`).
4. **Privilege & Bundle Gaps:** Requires elevated write access to `%ProgramData%` and `%ProgramFiles%`, and cannot cleanly wrap `ApplicationPlugins/*.bundle` (`PackageContents.xml`) entries or multi-item manifests (`RDES.KMPlus.addin`).

**Verdict:** Reject wrapper/gating shims over third-party manifests.

---

## 4. Environment, Host, & Licensing Alternatives

Separating `$0` single-seat same-host options from multi-seat hardware/cloud options clarifies the real architectural trade-off boundary established alongside [2026-11-20-revit-test-sandboxing-feasibility.md](2026-11-20-revit-test-sandboxing-feasibility.md):

### 4.1 `$0` Single-Seat Same-Host Options

- **Blocked Same-Host Virtualization/Account Paths (Recap):** Secondary Windows accounts break `AdskIdentityManager` / `AdskLicensingService` session binding and Session 0 / Win32 desktop composition; Windows Sandbox (`.wsb`) lacks `HKLM` MSI state and `AdskLicensingService`; Windows Containers lack `WinSta0` / DirectX 11 / `WebView2`; and concurrent local Hyper-V VMs under a **single** Named-User license violate Autodesk's single-concurrent-device rule ([Autodesk Subscription Terms](https://damassets.autodesk.net/content/dam/autodesk/www/terms-of-use/TOULibrary_SubscriptionTypes_English-en.pdf), [Autodesk Virtualization Policy](https://www.autodesk.com/support/account/admin/manage/virtualization)).
- **Option 1 (`$0`, Cleanest Process Isolation): Surgical Detours Shim + `RevitWorkerPool` Launcher** — Hooks only `.addin` / `*.bundle` enumeration and virtualizes `AddInsSettings.json` per test PID; runs under the same Windows user session and single Named-User seat concurrently alongside interactive Revit with zero host file changes.
- **Option 2 (`$0`, Zero-Hook Pragmatic Baseline): Pre-Booted `RevitWorkerPool` + `SYNTHETIC_TEST_WORKER=1`** — Retains current repo isolation ([BootstrapApp.cs](../../src/Synthetic.Bootstrap/BootstrapApp.cs#L34-L46), `NUnit.Open=true`, [test_executor.py](../../.agents/skills/revit-tests/scripts/test_executor.py#L216-L275)) and pre-warms idle `Revit.exe` workers on a separate Virtual Desktop. Zero hooks and instant test runs (`~1–3s` after build), **with the explicit limitation that third-party add-ins still load inside the worker** (safe for `Synthetic` isolation, not a zero-third-party environment).

### 4.2 Multi-Seat / Hardware Options (Non-`$0`)

- **Option 3 (Cleanest Overall, Zero Hooks): Dedicated CI Workstation or Persistent Test VM with Dedicated Autodesk Seat:**
  - Provisions a dedicated Windows 11 host or VM running under a **separate Autodesk Named-User seat** (or Autodesk Flex) where **only** Revit 2023–2027, `eTransmitForRevitDB`, `ricaun.RevitTest.Application`, and `Synthetic` payloads exist.
  - Zero third-party add-ins installed, zero hook maintenance, zero EDR friction, zero conflict with interactive developer work, and 100% compliant with Autodesk licensing Terms of Use.
- **Option 4 (Unsuitable for Full Suite): Autodesk Platform Services (APS) Design Automation for Revit (`revitcoreconsole.exe`):**
  - Strictly headless (`RevitDBAPI`-only; [APS Design Automation Guide](https://aps.autodesk.com/en/docs/design-automation/v3/tutorials/revit/step1-convert-addin)). Prohibits `RevitAPIUI`, `UIApplication`, `ExternalEvent`, Ribbon APIs, and **WPF**—meaning UI/MVVM smoke tests such as [BootstrapWorkerIsolationRevitTests.cs](../../tests/Platform/Host/Revit/BootstrapWorkerIsolationRevitTests.cs#L76-L87) (`ProjectStandardsDashboardWindow`) cannot execute, plus incurs metered Flex token cost and 30–90s cloud round-trips.

---

## 5. Primary-Source Comparison Matrix & Recommendation

| Approach | Revit Coverage (`2023–2026` / `2027+`) | Concurrent Interactive Safe? | Strictly Zero 3rd-Party Add-Ins? | Crash Safe (Zero Cleanup State)? | Licensing / Cost | Maintenance & Risk |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| **Dedicated Test VM / CI Runner (Separate Seat)** | **2023–2027+** | **Yes** (Separate OS) | **Yes** (Never installed) | **Yes** | Extra Revit seat + Win license | **Lowest** (100% native Autodesk) |
| **Surgical Detours Shim + `RevitWorkerPool` (`NtQueryDirectoryFile` + `FindFirstFile*` allowlist)** | **2023–2027+** (handles `.NET 4.8` + `.NET 8/10`) | **Yes** (PID-scoped) | **Yes** (`Synthetic` + `ricaun` only) | **Yes** (In-memory; temp JSON per PID) | **$0** (Same user/host) | **High initial / Moderate ongoing** (Signed C++ DLL, EDR review, custom pipe launcher) |
| **Zero-Hook `RevitWorkerPool` + `SYNTHETIC_TEST_WORKER=1`** | **2023–2027+** | **Yes** (`NUnit.Open=true` / `PipeTestClient`) | **No** (3rd-party add-ins still load; only `Synthetic` bypassed) | **Yes** | **$0** (Same user/host) | **Low** (Pure C#/Python, zero OS hooks) |
| **Sandboxie-Plus (`SbieDrv.sys` + `ClosedFilePath`)** | Partial (`2023–2027+` fragile) | Mostly | **Yes** (via rules) | **Yes** | **$0** (OSS) / Kernel driver | **High** (`AdskLicensingService`/`WebView2` `SBIE2112` breakage) |
| **`AddInsSettings.json` / `AdminSettings.json`** | **2025.3+ only** (**No 2023–2024**) | **No** (Global + write race) | **No** (Blocks `ricaun` + `Synthetic`) | **No** (Mutates global JSON) | **$0** | **Blocked** |
| **`Revit.ini` `DisableUserAddinsAndMacros=1`** | 2023–2027+ | **No** (Global `Revit.ini`) | **No** (Also kills `ricaun` + `Synthetic`) | **No** | **$0** | **Blocked** |
| **Native `/localbasepath` CLI Flag** | 2023–2027+ (partial) | Partially | **No** (`.NET` ignores it; `2027` PF blocked) | **Yes** | **$0** | **Blocked** |
| **Journal Automation (`startedWithAutomation`)** | 2023–2026 only (**Breaks in 2027**) | Yes | **No** (Playback lifecycle conflicts; broken in 2027) | Fragile | **$0** | **Blocked** |
| **In-Process / Manifest Wrapper Shims** | 2023–2027+ | **No** | **No** (ALC/signing breakage) | **No** (Admin files) | **$0** | **Blocked** (Overwritten by PDQ/MSI) |
| **Custom Kernel Minifilter / ProjFS / MSIX / App-V** | N/A | ProjFS: **No**; others: poor | Technical only (Minifilter/MSIX) | N/A | EV Cert / Enterprise | **Rejected** (EV signing, ODIS/MSI breakage) |
| **APS Design Automation (`revitcoreconsole.exe`)** | Cloud only | Yes | Yes | Yes | **Paid Flex tokens** | **Blocked** (No `RevitAPIUI` / WPF) |

### Ranked Recommendations

1. **If multi-seat CI hardware/VM budget is available — Ranked #1 Overall:** Stand up a **dedicated clean Windows test runner / VM** under its own Autodesk license with zero third-party add-ins installed. It eliminates every hook, EDR, `ProgramData`, `ApplicationPlugins`, and `AddInsSettings.json` conflict permanently using 100% supported Autodesk behavior.
2. **If strict `$0` single-workstation clean isolation is required — Ranked #1 Single-Host:** Implement a **path-allowlist Microsoft Detours shim** (`DetourCreateProcessWithDllsW`) inside a custom **`RevitWorkerPool` launcher** driving `PipeTestClient` (`\\.\pipe\ricaun.Revit.<PID>`):
   - Keep [BootstrapApp.cs](../../src/Synthetic.Bootstrap/BootstrapApp.cs) (`SYNTHETIC_TEST_WORKER=1`) and `eTransmitForRevitDB.dll` untouched on disk (no `Autodesk.eTransmitApplication.addin` needed).
   - Filter `.addin` enumeration (`FindFirstFile*` + `NtQueryDirectoryFile*`) across `%APPDATA%`, `%ProgramData%`, and `%ProgramFiles%` (2027+) to expose only `Synthetic<ver>.addin` in `%APPDATA%` (blocking stale `%ProgramData%\...\Synthetic202*.addin` and all third-party `.addin`s).
   - Filter `*.bundle` enumeration across all three `ApplicationPlugins` roots to expose only `ricaun.RevitTest.Application.bundle`.
   - Virtualize `AddInsSettings.json` and `AdminSettings.json` paths per worker PID into `%TEMP%` so interactive Revit's settings never race `APIInterop!updateAddInsSettings`.
   - Code-sign the hook DLL and inject **only** into `Revit.exe` (never `msedgewebview2.exe` / `AdskIdentityManager`), and persist `CodeSigning` GUIDs via [guid_whitelister.py](../../.agents/skills/revit-tests/scripts/guid_whitelister.py).
3. **Pragmatic Immediate Hygiene & Zero-Hook Baseline (Regardless of Path Chosen):**
   - Clean up the legacy `%ProgramData%\Autodesk\Revit\Addins\2023..2026\Synthetic202*.addin` direct Release manifests (or align them with `Synthetic.Bootstrap`) so stale `Synthetic.Core.App` assemblies can never shadow isolated test payloads if `%APPDATA%` is absent.
   - If the engineering cost/EDR review of a Detours DLL outweighs third-party load contamination on local dev runs, pair `SYNTHETIC_TEST_WORKER=1` + `NUnit.Open=true` + `RicaunBundleLease` with a pre-booted `RevitWorkerPool` standby worker (zero OS hooks, `~1–3s` test feedback, while saving true clean-environment verification for a dedicated CI runner).

---

## Primary Sources Verified

- **Repository files inspected:** [BootstrapApp.cs](../../src/Synthetic.Bootstrap/BootstrapApp.cs), [ETransmitAssemblyResolver.cs](../../src/Platform/Revit/Addin/ETransmitAssemblyResolver.cs), [DocumentUtil.cs](../../src/Platform/Revit/Addin/DocumentUtil.cs), [Directory.Build.props](../../src/Directory.Build.props), [Directory.Build.targets](../../src/Directory.Build.targets), [BootstrapWorkerIsolationRevitTests.cs](../../tests/Platform/Host/Revit/BootstrapWorkerIsolationRevitTests.cs), [tests/Directory.Build.props](../../tests/Directory.Build.props), [tests/Directory.Build.targets](../../tests/Directory.Build.targets), [Synthetic.RevitTests.2026.csproj](../../tests/Projects/Synthetic.RevitTests.2026/Synthetic.RevitTests.2026.csproj), [test_executor.py](../../.agents/skills/revit-tests/scripts/test_executor.py), [guid_whitelister.py](../../.agents/skills/revit-tests/scripts/guid_whitelister.py), [ADR 018](../adrs/Synthetic_ADRs%20-%2018%20-%20ADR%20018%20-%20AppData%20Local%20Deployment%20for%20Debug%20Builds.md), [2026-11-20-revit-test-sandboxing-feasibility.md](2026-11-20-revit-test-sandboxing-feasibility.md).
- **Local Autodesk Revit binaries & configuration audited (read-only):**
  - Revit 2023 (`23.1.90.15`), 2024 (`24.3.60.12`), 2025 (`25.5.10.17`), 2026 (`26.5.10.38`), and 2027 (`27.3.0.28`): PE headers (`dumpbin /HEADERS`), native disassembly (`Utility.dll`, `DesktopMFC.dll`), managed/C++-CLI IL inspection (`APIInterop.dll`, `RevitAddInUtility.dll`, `PackageContentsParser.dll`, `Autodesk.AddInsManagerDB.dll`, `eTransmitForRevit.dll`, `eTransmitForRevitDB.dll`).
  - Workstation runtime manifests & settings: `%ProgramData%\Autodesk\Revit\Addins\2023..2026\`, `%ProgramFiles%\Autodesk\Revit\Addins\2027\`, `%APPDATA%\Autodesk\Revit\Addins\2023..2027\`, `%APPDATA%\Autodesk\ApplicationPlugins\`, `%ProgramData%\Autodesk\ApplicationPlugins\`, `%ProgramFiles%\Autodesk\ApplicationPlugins\`, `%APPDATA%\Autodesk\Revit\Autodesk Revit 2026\AddinsData\AddInsSettings.json`, `Revit.ini`.
- **.NET Runtime & Test Runner binaries inspected (IL):**
  - `mscorlib.dll` (`.NET Framework 4.8`), `System.Private.CoreLib.dll` (`.NET 8.0.34`).
  - `ricaun.RevitTest.TestAdapter` `1.11.1` (embedded `ricaun.RevitTest.Command.dll`, `ricaun.RevitTest.Console.dll`, `ApplicationPluginsDisposable`, `RevitTestUtils.CreateRevitServer`).
- **Official External Documentation:**
  - Autodesk Help — Revit Command-Line Switches: https://help.autodesk.com/view/RVT/2024/ENU/?guid=GUID-BD09C1B4-5520-475D-BE7E-773642EEBD6C
  - Autodesk Help — `Revit.ini` `[API]` (`DisableUserAddinsAndMacros`): https://help.autodesk.com/cloudhelp/2025/CHS/Revit-Customize/files/GUID-B6ED7657-9D81-4BFF-B72D-84B5FDA5F611.htm
  - Autodesk Support — `AddInsSettings.json` / Add-Ins Manager: https://www.autodesk.com/support/technical/article/caas/sfdcarticles/sfdcarticles/Failed-to-save-Add-Ins-Manager-settings-when-disabling-add-ins-using-Add-in-Manager-in-Revit.html
  - Autodesk Support — `(Admin) Add-Ins Manager` & `AdminSettings.json`: https://www.autodesk.com/support/technical/article/caas/sfdcarticles/sfdcarticles/How-to-manage-addins-on-a-multi-user-virtual-environment-for-Revit.html
  - Autodesk Subscription Terms & Virtualization Policy: https://damassets.autodesk.net/content/dam/autodesk/www/terms-of-use/TOULibrary_SubscriptionTypes_English-en.pdf and https://www.autodesk.com/support/account/admin/manage/virtualization
  - Autodesk Platform Services — Design Automation for Revit: https://aps.autodesk.com/en/docs/design-automation/v3/tutorials/revit/step1-convert-addin
  - Microsoft Detours: https://github.com/microsoft/detours
  - Microsoft Learn — `PrjStartVirtualizing` (ProjFS): https://learn.microsoft.com/en-us/windows/win32/api/projectedfslib/nf-projectedfslib-prjstartvirtualizing
  - Microsoft Learn — Windows 11 Driver Attestation & EV Signing: https://learn.microsoft.com/en-us/windows-hardware/drivers/dashboard/code-signing-attestation
  - Microsoft MSIX Package Support Framework (`FileRedirectionFixup`): https://github.com/microsoft/MSIX-PackageSupportFramework/blob/master/fixups/FileRedirectionFixup/readme.md
  - Sandboxie-Plus `OpenPipePath` Documentation: https://sandboxie-plus.com/sandboxie/openpipepath/
