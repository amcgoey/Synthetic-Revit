# Root-Cause Investigation: CTC BIM Project Suite Concurrency Hang Under Parallel Revit Test Workers

**Date:** 2026-11-24  
**Trigger Incident:** [conversation 711c5441-2aaf-446e-a915-d2ce66c01b46](conversation://711c5441-2aaf-446e-a915-d2ce66c01b46) (`RunId: 20261124_010902_8044_05423d47`, Ticket `#204`, Revit 2026)  
**Scope:** Revit 2023–2026 live test execution via [test_executor.py](../../.agents/skills/revit-tests/scripts/test_executor.py) and `ricaun.RevitTest` (`1.11.1`) alongside concurrent interactive Revit usage

---

## Executive Summary

When two or more `Revit.exe` processes of the **same Revit release year** enter `CTC BIM Project Suite` startup concurrently, one process can collide with the other while deleting and recreating shared per-tool startup log files in `C:\Users\Public\CTC Software\CTC BIM Project Suite\<Tool> <Year> LastOnStartupLog.txt`.

In the observed hang on 2026-11-24 (`journal.0184.txt`, Revit 2026 PID `6084`), `ModelDashboard2026.App.OnStartup` called `CTC.Util.LoggingTools.LoggingFunctions.AddToLog`, which executes an unguarded `File.SetAttributes(..., Normal)` + `File.Delete(...)` on `C:\Users\Public\CTC Software\CTC BIM Project Suite\Model Dashboard 2026 LastOnStartupLog.txt` while concurrent Revit 2026 PID `32336` (`journal.0185.txt`) was inside the exact same startup routine (`01:09:44.543`). Unlike `File.AppendAllText`, `LoggingFunctions.AddToLog` wraps `File.Delete` failures in a rethrown exception (`"Error deleting the existing log file ..."`), crashing `CTC.RET.SuiteExternalApp.App.OnStartup`. Revit then displays a blocking modal dialog (`TaskDialog_External_Tools_External_Tool_Failure`) during `DesktopMFCApp::doStartupWarnings` (`01:10:00.669`). Because `ricaun.RevitTest.Application` (`1.11.1`) only auto-dismisses two unrelated `TaskDialogId`s (`TaskDialog_License_Current_Status_Demo_Mode` and `TaskDialog_DNSMTemplate`) and its idle watchdog does not abort before `ApplicationInitialized`, the worker process deadlocks indefinitely until `test_executor.py`'s `660s` timeout or manual termination.

---

## 1. Observed Incident & Forensic Timeline

### 1.1 Failing Run Details
Extracted from [conversation 711c5441-2aaf-446e-a915-d2ce66c01b46](conversation://711c5441-2aaf-446e-a915-d2ce66c01b46), subagent `ff74108c` logs, and `%LOCALAPPDATA%\Autodesk\Revit\Autodesk Revit 2026\Journals\`:

| Field | Hung Worker (Ticket `#204`) | Colliding Concurrent Worker (Ticket `#205`) |
| :--- | :--- | :--- |
| **Command / Filter** | `python .agents/skills/revit-tests/scripts/test_executor.py --version 2026 --filter SharedCoordinates` | `BatchPrint` test run |
| **`RunId`** | `20261124_010902_8044_05423d47` | `20261124_010927_13204_c647ce1f` |
| **Process IDs** | `Revit.exe` PID `6084`; `ricaun.RevitTest.Console.exe` PID `8804`; `RevitWorker` PID `31836` | `Revit.exe` PID `32336` |
| **Journal File** | `%LOCALAPPDATA%\Autodesk\Revit\Autodesk Revit 2026\Journals\journal.0184.txt` (1,748 lines; `274,422` B) | `%LOCALAPPDATA%\Autodesk\Revit\Autodesk Revit 2026\Journals\journal.0185.txt` |
| **Revit Build** | Autodesk Revit `2026` (`Build: 20261015_1515(x64)`, `2026.5.1`) | Autodesk Revit `2026` (`Build: 20261015_1515(x64)`) |
| **Outcome** | Deadlocked on `TaskDialog_External_Tools_External_Tool_Failure`; `output_2026.log` stayed `0` bytes; manually cancelled and killed (`taskkill /F /T /PID 8804` / `6084`) after >5 min (`06:16:36Z–06:16:45Z`) | Cleared CTC startup in `12.977s` (`NoError`), ran 2 tests (`1,612.31 ms`), exited cleanly (`Execute Total: 00:00:04.6959955`) |

### 1.2 Why a 25-Second Launch Stagger Still Collided at the Same Millisecond
Comparing pre-CTC timestamps in `journal.0184.txt` and `journal.0185.txt` reveals why naive launch staggering failed:

| Timestamp (Local) | PID `6084` (`journal.0184.txt`) | PID `32336` (`journal.0185.txt`) |
| :--- | :--- | :--- |
| `01:09:02` | `test_executor.py` run `20261124_010902_8044_05423d47` starts | — |
| `01:09:12.309` | `L3`: `started recording journal file` (cold cache: `13.25s` before `manage involvement`, `2.30s` `manage licensing`, `4.36s` DB add-ins) | — |
| `01:09:27` | *(Still booting)* | `test_executor.py` run `20261124_010927_13204_c647ce1f` starts (~25s later) |
| `01:09:35.125` | *(Loading external applications)* | `L3`: `started recording journal file` (warm cache: reaches CTC in `9.4s` vs. `32.3s`) |
| **`01:09:44.543`** | `L1466` (`01:09:44.572`): **`CTC BIM Project Suite startup begins at 11/24/2026 01:09:44.543`** | `L1452` (`01:09:44.571`): **`CTC BIM Project Suite startup begins at 11/24/2026 01:09:44.543`** |
| `01:09:56.710` | `L1548–1551`: **Throws `Access to the path '...Model Dashboard 2026 LastOnStartupLog.txt' is denied.` (`12.168s`) -> `API_ERROR`** | *(Inside `LoadExternalApplications` at the same second)* |
| `01:09:57.520` | *(Continuing remaining add-ins)* | `L1543`: Completes CTC startup (`12.977s`) cleanly |
| `01:09:59` | `L1688–1700`: `ricaun.RevitTest.Application` `1.11.1` registers `DialogBoxShowing` & `ApplicationInitialized`; starts `PipeTestServer: True ricaun.Revit.6084` | `L1680–1691`: Same for PID `32336` |
| `01:10:00.040` | `L1711`: `Synthetic.Bootstrap2026` logs `SYNTHETIC_TEST_RUN:...`, returns `Succeeded` (`SYNTHETIC_TEST_WORKER=1`) | `L1702`: `Synthetic.Bootstrap2026` completes cleanly (`01:09:59.982`) |
| `01:10:00.229` | `L1713`: `->DesktopMFCApp::doStartupWarnings` | `L1704–1734`: Enters and exits `doStartupWarnings` (`0.51s`), runs tests |
| **`01:10:00.669`** | `L1744–1748`: `start up warnings for AddIns` displays **`TaskDialog_External_Tools_External_Tool_Failure`** (`Close`) — **hangs indefinitely** | *(Already running `BatchPrint` tests)* |

Additional concurrent Revit 2026 runs observed later that morning (`journal.0188.txt` PID `10300` at `01:13:22`, `journal.0189.txt` PID `35080` at `01:13:31`, `journal.0202.txt` PID `31632` at `01:35:25`, and `journal.0203.txt` PID `42288` at `01:35:31`) happened to clear CTC `OnStartup` ~1.4–8.3 seconds apart and succeeded. Across all archived Revit `2023`–`2026` journals in `%LOCALAPPDATA%\Autodesk\Revit\`, `journal.0184.txt` is the only recorded crash to date.

Exact failure lines from `%LOCALAPPDATA%\Autodesk\Revit\Autodesk Revit 2026\Journals\journal.0184.txt`:

```text
'C 24-Nov-2026 01:09:56.710;   0:< ERROR starting up CTC BIM Project Suite:  ERROR starting the product:  Error deleting the existing log file 'C:\Users\Public\CTC Software\CTC BIM Project Suite\Model Dashboard 2026 LastOnStartupLog.txt'
'   Access to the path 'C:\Users\Public\CTC Software\CTC BIM Project Suite\Model Dashboard 2026 LastOnStartupLog.txt' is denied. 
'C 24-Nov-2026 01:09:56.712;   0:< CTC BIM Project Suite total product startup time = 12.1682598 seconds 
' 0:< API_ERROR { Starting External Application: CTC BIM Project Suite, Class: CTC.RET.SuiteExternalApp.App, Vendor : CTC_(CTC Software   https://ctcsoftware.com), Assembly: CTC-BIM-Project-Suite.Bundle\Contents\CTC.RET.ProjectSuiteExternalApp2026.dll,  Assembly Version: 22.0.21054.0 }
...
' 0:< TaskDialog "Revit cannot run the external application "CTC BIM Project Suite". Contact the provider for assistance.  Information they provided to Revit about their identity: CTC Software   https://ctcsoftware.com."
'Id : TaskDialog_External_Tools_External_Tool_Failure
'CommonButtons : Close
'DefaultButton : Close
```

---

## 2. Verified Primary-Source Root-Cause Analysis

IL metadata and instruction disassembly via `System.Reflection.Metadata` (`dotnet fsi`) across CTC BIM Suites `v27.1` (`SuiteVersion.txt`), Revit `2023`–`2026` add-in bundles, `%ProgramData%\CTC Software\CommonRevit\`, and `ricaun.RevitTest` (`1.11.1`) pinpoint a three-link failure chain.

### 2.1 Link 1 — CTC Resolves a Machine-Global Public Folder via `SHGetKnownFolderPath`
In `%ProgramData%\Autodesk\Revit\Addins\2026\CTC-BIM-Project-Suite.bundle\Contents\CTC.RET.RevitExternalAppTools.dll`:
- `CTC.RET.RevitExternalAppTools.PathTools::GetSuiteLoggingFolder` calls `System.Environment.GetFolderPath((SpecialFolder)46)` (`Environment.SpecialFolder.CommonDocuments`), strips `\Documents` via `Path.GetDirectoryName`, and combines `"CTC Software"` + the product name (`"CTC BIM Project Suite"`).
- Per Microsoft `.NET` / Windows docs, `Environment.SpecialFolder.CommonDocuments` wraps `SHGetKnownFolderPath(FOLDERID_PublicDocuments)`, resolving to `C:\Users\Public\Documents` (parent `C:\Users\Public`) **independent of `%PUBLIC%` environment variables**.
- CTC's official *CTC BIM Suites Installation and Configuration Guide* (`%ProgramData%\CTC Software\CTC BIM Suites\CTC BIM Suites Installation and Configuration Guide.pdf`, p. 5) explicitly documents this machine-wide layout (`C:\Users\Public\CTC Software`) alongside per-user Cloud Shared licensing (`p. 9`, which documents that concurrent Revit instances on a single workstation are supported under one license).

### 2.2 Link 2 — Seven Sub-Tools Perform Unguarded `File.Delete` on Startup Logs Across Revit 2023–2026
Disassembly of `CTC.RET.ProductStartup2026.dll` (`StartTheSuite`, `LoadExternalApplications`, `LoadExternalApp`) and all bundled tool assemblies shows:
1. `CTC.RET.SuiteExternalApp.App.OnStartup` (`CTC.RET.ProjectSuiteExternalApp2023..2026.dll`) logs to `CTC BIM Project Suite <Year> ExternalAppLastStartupLog.txt` via `CTC.RET.RevitExternalAppTools.Logger`, whose `Initialize` (`IL_0013..002E`) and `AddToLog` (`IL_0000..013E`) wrap `File.Delete` and `File.AppendAllText` in silent `try/catch` blocks.
2. `CTC.RET.ProductStartup.Startup.StartTheSuite` (`ProductStartup2023..2026.dll`) logs `ProductLastStartupLog.txt` with `DeleteExistingLogFileOnFirstWrite = false` (`IL_0025: ldc.i4.0`), `OnPermissionsErrorWriteLogFileToUserTempFolder = true` (`IL_0033: ldc.i4.1`), and explicit `try/catch` fallbacks to `%TEMP%` around `File.Delete`.
3. `LoadExternalApplications` iterates visible commands from `Ribbon Button Visibility Settings.txt` and invokes `IExternalApplication.OnStartup` on each enabled tool DLL. **Seven sub-tools** construct `CTC.Util.LoggingTools.LoggingFunctions` with **`DeleteExistingLogFileOnFirstWrite = true`** (`ldc.i4.1`) and **`OnPermissionsErrorWriteLogFileToUserTempFolder = false`** (`0, 0, ...` in `LoggingFunctionsParameters`) pointing to `C:\Users\Public\CTC Software\CTC BIM Project Suite\<Tool> <Year> LastOnStartupLog.txt`:
   - `CTC.RET.ViewAligner<Year>.dll` -> `View Aligner <Year> LastOnStartupLog.txt`
   - `CTC.RET.InvisibilityAdvisor<Year>.dll` -> `Invisibility Advisor <Year> LastOnStartupLog.txt`
   - `CTC.RET.ModelCompare<Year>.dll` -> `Model Compare <Year> LastOnStartupLog.txt`
   - `CTC.RET.ModelDashboard<Year>.dll` -> `Model Dashboard <Year> LastOnStartupLog.txt` *(crash site in `journal.0184`)*
   - `CTC.RET.OccupantFlowAnalyzer<Year>.dll` -> `Occupant Flow Analyzer <Year> LastOnStartupLog.txt`
   - `CTC.RET.ScheduleXL<Year>.dll` -> `Schedule XL <Year> LastOnStartupLog.txt`
   - `CTC.RET.SheetAssistant<Year>.dll` -> `Sheet Assistant <Year> LastOnStartupLog.txt`
4. Inside `CTC.Util.LoggingTools.LoggingFunctions::AddToLog`—verified identical across **`CTC.Util.LoggingTools.dll`** (`net48`, used by Revit `2023`–`2024`), **`CTC.Util.LoggingTools2.dll`**, and **`CTC.Util.LoggingTools2.NET.dll`** (`net8.0`, used by Revit `2025`–`2026`):
   - **First-write delete (`IL_0056–IL_00CA` in `CTC.Util.LoggingTools2.NET.dll`):** If `File.Exists(LogFileName)` is true, it calls `File.SetAttributes(LogFileName, FileAttributes.Normal)` and `File.Delete(LogFileName)`. Its `catch` block (`IL_009C–IL_00CA`) **unconditionally rethrows** `new Exception("Error deleting the existing log file '{0}'\n{1}", ex)`—completely ignoring `OnPermissionsErrorWriteLogFileToUserTempFolder`.
   - **Append path (`IL_02F1–IL_0351` in `CTC.Util.LoggingTools2.NET.dll`):** `File.AppendAllText(LogFileName, ...)` opens the file with `FileShare.Read` (without `FileShare.Delete`) and *does* safely catch exceptions (falling back to `%TEMP%` if `OnPermissionsErrorWriteLogFileToUserTempFolder == true`, or silently returning if `false`).
   - Consequently, when Process A is executing `File.SetAttributes`/`File.AppendAllText` or holds a pending-delete handle (`STATUS_DELETE_PENDING`) while Process B calls `File.SetAttributes`/`File.Delete` on the same `<Tool> <Year> LastOnStartupLog.txt`, Windows raises `ERROR_ACCESS_DENIED` -> `UnauthorizedAccessException`, which `AddToLog` rethrows up to Revit, aborting CTC startup.

### 2.3 Full CTC Concurrency Audit (2023–2026)

| CTC Component / Path | Shared State / Operation | Multi-Process Race Risk | Evidence |
| :--- | :--- | :--- | :--- |
| **7 Sub-Tools `OnStartup`** (`C:\Users\Public\CTC Software\CTC BIM Project Suite\<Tool> <Year> LastOnStartupLog.txt`) | `File.SetAttributes` + `File.Delete` with `DeleteExistingLogFileOnFirstWrite = true` and rethrow `catch` | **CRITICAL (Confirmed Root Cause)** across same Revit year (2023, 2024, 2025, 2026) | `journal.0184.txt`; `LoggingFunctions::AddToLog` IL; `ModelDashboard2023..2026` IL |
| `SuiteExternalApp` (`ExternalAppLastStartupLog.txt`) & `ProductStartup` (`ProductLastStartupLog.txt`) | `File.Delete` / `File.AppendAllText` | **Low / Non-fatal** (`try/catch` swallows or falls back to `%TEMP%`) | `RevitExternalAppTools.Logger` & `ProductStartup2026.StartTheSuite` IL |
| `ModelCompare::ApplicationInitialized` (`LookForWorkToDo`) | Calls `File.Delete` (`"Clearing previous log files."`) | **Low / Non-fatal** (`IL_0EA7..0EF9` wraps both `File.Delete` calls in empty `catch` blocks) | `ModelCompare2026.dll` IL; `Model Compare 2026 LastApplicationInitialized.txt` |
| `ModelDashboard::DocumentOpened` (`%APPDATA%\CTC Software\Model Dashboard\Logs\YYYY.MM.DD Model Dashboard.log`) | Calls `FileLogger` (`DeleteExistingLogFileOnFirstLogEntry = false`, `OnPermissionsErrorWriteLogFileToUserTempFolder = true`) inside `try/catch` | **Low / Non-fatal** (`0` non-standard lines in `276` entries on `2026-11-24`) | `LoggingTools.dll` IL; `ModelDashboard2026.dll` IL; `2026.11.24 Model Dashboard.log` |
| Cloud Shared Licensing & `CTC Authenticator.exe` | Writes `%LOCALAPPDATA%\Temp\<guid>\CTCAuthenticationResults\<guid>.json`; `TokenLock` uses `System.Threading.Mutex` | **Low** (per-call GUID output paths + mutex; did not run during `OnStartup` in `journal.0184`/`0185`) | `AuthenticatorLog.txt`; `CTC.Forge.AuthenticationManager.dll` IL |
| `CTC.Revit.LinkUtils2023..2026.dll` | Uses `System.Threading.Mutex` (no `Pipe`/`SQLite`/`LiteDB` anywhere in CTC binaries) | **Low** (only triggered if user invokes LinkUtils tools) | Static IL reference scan across `CommonRevit` and 2026 bundle |

**Cross-version vs. same-version boundary:** Because the 7 startup log filenames include the Revit year (`2023`–`2026`), launching Revit `2025` concurrently with Revit `2026` does not hit this bug. Any two instances of the **same** Revit version—whether two parallel agent test workers or **one interactive user Revit session + one test worker**—race whenever their `OnStartup` windows overlap.

### 2.4 Link 3 — Why `ricaun.RevitTest` (`1.11.1`) Deadlocks Instead of Failing Fast
Disassembly of `ricaun.RevitTest.Application.dll` and `ricaun.RevitTest.Console.dll` (`1.11.1`, unpacked from local NuGet cache) confirms:
1. **`DialogBoxResolver` (`ricaun.RevitTest.Application.dll`):**
   - Subscribes to `UIControlledApplication.DialogBoxShowing` in `OnStartup` and disposes on `ApplicationInitialized`.
   - `TaskDialogIds` only contains two hardcoded IDs: `"TaskDialog_License_Current_Status_Demo_Mode"` and `"TaskDialog_DNSMTemplate"`.
   - `"TaskDialog_External_Tools_External_Tool_Failure"` does not match either ID, so `DialogBoxResolver.OnDialogBoxShowing` leaves the `TaskDialog` open, stalling Revit's UI message loop indefinitely inside `DesktopMFCApp::doStartupWarnings`.
2. **`TimeoutNotBusyMaxSeconds` (`ricaun.RevitTest.Console.dll`):**
   - `EnvironmentVariable.get_TimeoutNotBusyMaxSeconds` reads `RICAUN_REVITTEST_CONSOLE_TIMEOUT_NOT_BUSY_MAX_SECONDS` with a default of **`10` seconds** (`ldc.i4.s 10`).
   - However, `CreateRevitServer` only triggers `"RevitTest: Timeout not busy for too long ..."` when `TestResponse.IsBusy` (driven by `RevitBusyService` on `UIControlledApplication.Idling`) reports not busy. Because `DesktopMFCApp::doStartupWarnings` blocks *before* `ApplicationInitialized` and *before* Revit enters `Idling`, `Console` waits indefinitely until [test_executor.py](../../.agents/skills/revit-tests/scripts/test_executor.py) hits its `660s` timeout and executes `taskkill /F /T /PID`.
3. **Why Revit & `test_executor.py` Do Not Isolate CTC Today:**
   - Revit provides **no command-line switch** (only `/language` and `.rvt`/journal path) and **no `Revit.ini` flag** to disable `.addin` manifests per process.
   - `AddInsSettings.json` (introduced in Revit 2025.3+, absent in 2023–2024) lives globally per Windows user under `%APPDATA%\Autodesk\Revit\Autodesk Revit <Ver>\AddinsData\AddInsSettings.json`—disabling CTC there strips CTC tools from interactive Revit too.
   - `TempDisableThirdPartyAddins` in [test_executor.py](../../.agents/skills/revit-tests/scripts/test_executor.py) intentionally only restores stale `.bak` files and leaves `CTC-BIM-Project-Suite.addin` untouched so interactive Revit usage is preserved.

---

## 3. Concrete, Ranked Fix & Mitigation Options

### Evaluation Matrix

| Rank | Option | Root-Cause Coverage | Concurrent Interactive Revit Compatibility | Brittleness / Failure Modes | Revit 2023–2026 Support | Maintenance Burden |
| :---: | :--- | :--- | :--- | :--- | :--- | :--- |
| **1** | **Per-Version Revit Startup Serialization Gate (`test_executor.py` Mutex) + Fail-Fast Startup Modal Watchdog & Retry** | **High** (100% eliminates worker-vs-worker race; self-recovers from human-vs-worker race in seconds) | **Full** (zero modification to manifests, `AddInsSettings.json`, CTC, or interactive Revit) | **Low** (mutex timeout + journal/HWND polling; slight `~15–30s` boot serialization per simultaneous same-version worker) | **2023–2026** | **Low** (confined to `test_executor.py` + optional Bootstrap helper) |
| **2** | **Pre-Booted Idle Standby Worker Pool (Sequential Boot, Parallel Pipe Dispatch)** *([sandboxing doc Method 3](2026-11-20-revit-test-sandboxing-feasibility.md#L190-L205))* | **High** (zero concurrent `OnStartup` across test workers + eliminates `~30s` boot latency) | **Full** (same profile, PID-scoped pipes; pair with Option 4 watchdog if human launches Revit concurrently) | **Moderate** (needs pool health checks, OAuth/idle lifecycle, HWND virtual-desktop hiding) | **2023–2026** | **Moderate** (new Python/C# pool daemon ~150–300 LOC) |
| **3** | **Targeted NTFS ACL Write-Deny on `C:\Users\Public\CTC Software\CTC BIM Project Suite`** *(after deleting existing `* LastOnStartupLog.txt`)* | **High for `LastOnStartupLog.txt`** (blocks both worker-vs-worker & human-vs-worker races without changing Revit) | **Preserves all CTC UI tools**; disables CTC's `LastOnStartupLog.txt` diagnostics | **High / Fragile** (Admin required; elevated Revit/CTC installer recreates files -> **100% crash** on non-elevated launch; relies on undocumented `AddToLog` exception-swallowing) | **2023–2026** | **Moderate** (re-apply after every CTC suite update) |
| **4** | **Fail-Fast Startup Modal Recovery Only** (`Synthetic.Bootstrap` `DialogBoxShowing` / external Win32 `TaskDialog` dismiss + retry) | **Symptom only** (unblocks worker in ~1s instead of `660s` hang; CTC still fails to load for loser) | **Safe for workers**, but if interactive Revit loses the race, **human user still gets CTC failure dialog** | **Low–Moderate** (requires verifying if `DialogBoxShowing` fires for `doStartupWarnings` or using UIAutomation/Win32 `WM_CLOSE`) | **2023–2026** | **Low** |
| **5** | **Disable the 7 Racing Tools via CTC `Ribbon Button Visibility Settings.txt` (or Uninstall `BIM Project Suite`)** | **Complete for the 7 sub-tools** (`LoadExternalApplications` skips disabled commands) | **Breaks interactive Revit** (removes `Model Dashboard`, `Model Compare`, `Schedule XL`, `View Aligner`, etc. globally) | **Moderate** (installer upgrades overwrite `Ribbon Button Visibility Settings.txt` per CTC guide p. 31) | **2023–2026** | **Low** |
| **6** | **Report Upstream Bug to CTC Software (`support@ctcsoftware.com`)** | **Complete once patched upstream** | **Full** | **Zero short-term control** (already on latest Suite `27.1`) | **2023–2027** | **None once released** |
| **NR** | **Rejected Approaches** (`AddInsSettings.json` / `.addin` toggling, fixed stagger delay, `%PUBLIC%` env var, secondary user, Sandbox, Hyper-V, Containers, Detours) | Partial or **0%** (`%PUBLIC%` ignored; stagger failed at 25s) | **Breaks** interactive Revit or `AdskLicensingService` | **Severe** (race conditions, OAuth failure, licensing limit, OS/GPU blockers) | Partial / Broken | High |

---

### Detailed Option Evaluation & Trade-Offs

#### Option 1 (Rank 1 — Recommended Immediate Fix): Per-Version Startup Serialization Gate + Fail-Fast Watchdog & Retry
- **Mechanism:**
  1. **Startup Mutex in [test_executor.py](../../.agents/skills/revit-tests/scripts/test_executor.py):** Reusing the Win32 `acquire_mutex` pattern already present in `RicaunBundleLease`, acquire a per-version mutex `Local\Synthetic_RevitStartupGate_<Ver>` right before spawning `dotnet test` (since `RicaunBundleLease` already proves cross-process Win32 mutex synchronization works reliably across agents).
  2. **Early Gate Release (Bootstrap/Journal Signal):** Hold the startup mutex **only until `Revit.exe` completes `OnStartup` and `doStartupWarnings`** (e.g., poll the worker's newly created `journal.*.txt` for `<-DesktopMFCApp::doStartupWarnings` or `ricaun.RevitTest: Execute:` or process exit, with an upper hold cap of ~`90s`), rather than holding it for the entire test run. Workers still execute their 1–5 minute NUnit suites **100% concurrently in parallel** once booted; only the ~12–35s boot window is serialized per Revit year.
  3. **Human-Launch Safety Net (Fail-Fast + Auto-Retry):** Because an uninstrumented interactive `Revit.exe` launched manually by the developer does not acquire `Local\Synthetic_RevitStartupGate_<Ver>`, add a lightweight startup modal detector in `test_executor.py` (scanning top-level windows owned by the worker `Revit.exe` PID for `#32770` / Revit `TaskDialog` title `"External Tools - External Tool Failure"`, or detecting `TaskDialog_External_Tools_External_Tool_Failure` in the worker's live `journal.*.txt` before any test executes). On detection, immediately kill the wedged worker (`kill_process_tree`), release the gate, and retry once instead of blocking `660s`.
- **Prerequisites & Trade-offs:**
  - *Trade-off:* When 3 workers for the **same** Revit year launch at the exact same instant, Worker 2 waits ~15–30s and Worker 3 waits ~30–60s to start booting (workers targeting *different* Revit years like `2025` + `2026` do not block each other).
  - *Remaining gap:* If a human user manually starts Revit 2026 during the ~15–30s window while an agent worker is starting Revit 2026, it is still theoretically possible for the human's interactive Revit to lose the coin flip instead of the worker. (In practice, Option 2 shrinks that human exposure window to **zero** once the pool is warmed.)

#### Option 2 (Rank 2 — Phase 2 Evolution): Pre-Booted Idle Standby Worker Pool (`2026-11-20` Method 3)
- **Mechanism:** Implement the standby worker pool documented in [2026-11-20-revit-test-sandboxing-feasibility.md](2026-11-20-revit-test-sandboxing-feasibility.md#L190-L205): a lightweight pool manager boots 2–4 idle `Revit.exe /nosplash` workers **strictly sequentially** (zero concurrent `OnStartup`), keeping `SYNTHETIC_TEST_WORKER=1` so [BootstrapApp.cs](../../src/Synthetic.Bootstrap/BootstrapApp.cs) stays idle without loading `Synthetic202X.dll`. Agents dispatch NUnit payloads directly over `\\.\pipe\ricaun.Revit.<PID>` in 1–3 seconds and retire/replenish workers sequentially in the background.
- **Prerequisites & Trade-offs:**
  - Eliminates worker-vs-worker startup races completely **and** drops inner-loop test latency from ~30–45s to ~1–3s, while never waking CTC `OnStartup` during agent test bursts (making accidental overlap with interactive Revit launches near-zero).
  - Requires building and maintaining the pool manager (~150–300 LOC) + Virtual Desktop/NoActivate window management.

#### Option 3 (Rank 3 — Admin Machine Workaround, Fragile): NTFS ACL Write-Deny on `C:\Users\Public\CTC Software\CTC BIM Project Suite`
- **Mechanism:** Verified directly in `CTC.Util.LoggingTools2.NET.dll` (`LoggingFunctions::AddToLog` IL):
  - If all `* LastOnStartupLog.txt` files do **not** exist (`File.Exists == false`), the unguarded throwing `File.Delete` branch (`IL_0056–IL_00CA`) is skipped.
  - Then `File.AppendAllText` throws `UnauthorizedAccessException`, which `AddToLog` (`IL_0309–IL_0351`) **catches and silently ignores** because all 7 sub-tools pass `OnPermissionsErrorWriteLogFileToUserTempFolder = false`.
  - Meanwhile `ProductLastStartupLog.txt` cleanly falls back to `%TEMP%` and `ExternalAppLastStartupLog.txt` silently swallows the failure.
  - Thus, deleting all `* LastOnStartupLog.txt` files from `C:\Users\Public\CTC Software\CTC BIM Project Suite` and denying `WriteData`/`CreateFiles` on that directory (including preventing elevated processes from recreating those files) suppresses the race even between interactive Revit and test workers while keeping every CTC ribbon tool enabled.
- **CRITICAL Anti-Pattern Warning & Trade-offs:**
  - **NEVER mark `LastOnStartupLog.txt` Read-Only or ACL-deny Delete while the files still exist:** `AddToLog` explicitly calls `File.SetAttributes(LogFileName, Normal)` then `File.Delete(LogFileName)` and **rethrows** on failure—making existing files non-deletable causes CTC to crash **100% of the time, even in single-instance interactive Revit**.
  - **High fragility:** Requires Administrator privileges; any CTC suite upgrade/repair (`msiexec`) or elevated `Revit.exe` run (`As Administrator`) can reset ACLs or recreate `Model Dashboard <Year> LastOnStartupLog.txt`, immediately triggering 100% failures for non-elevated Revit sessions until cleaned up.
  - Relies on undocumented internal exception-swallowing behavior inside obfuscated CTC assemblies and suppresses CTC's `LastOnStartupLog.txt` troubleshooting logs.

#### Option 4 (Rank 4 — Defense-in-Depth Add-on): Auto-Dismiss `TaskDialog_External_Tools_External_Tool_Failure`
- **Mechanism:** Either:
  1. Subscribe to `UIControlledApplication.DialogBoxShowing` inside [BootstrapApp.cs](../../src/Synthetic.Bootstrap/BootstrapApp.cs) (only when `SYNTHETIC_TEST_WORKER == "1"` and unregister on `ApplicationInitialized`), calling `OverrideResult((int)TaskDialogResult.Close)` when `args.DialogId == "TaskDialog_External_Tools_External_Tool_Failure"`; **or** open an upstream issue/PR on `ricaun-io/ricaun.RevitTest` to add this ID to `DialogBoxResolver.TaskDialogIds`.
  2. Pair with a Win32 dialog watcher in `test_executor.py` (since `DialogBoxShowing` interception of `DesktopMFCApp::doStartupWarnings`'s `External_Tool_Failure` dialog has not been empirically runtime-tested in this repo—whereas `TaskDialog_DNSMTemplate` confirms `doStartupWarnings` TaskDialogs *do* fire `DialogBoxShowing`—a Win32 window monitor guarantees coverage even if a non-`TaskDialog` popup appears).
- **Trade-offs:**
  - *Important limitation:* In [Synthetic.Bootstrap](../../src/Synthetic.Bootstrap/BootstrapApp.cs), auto-dismissing CTC's failure dialog lets the test worker run cleanly (Synthetic tests do not require CTC), **but it does not fix CTC crashing** when the human's interactive Revit loses the race to a test worker. Therefore, this belongs **paired under Option 1** rather than as a standalone solution.

#### Option 5 (Rank 5): Disable the 7 Racing Sub-Tools via `Ribbon Button Visibility Settings.txt` (or Uninstall Suite)
- **Mechanism:** Comment out the 7 tools (`#View Aligner`, `#Invisibility Advisor`, `#Model Compare`, `#Model Dashboard`, `#Occupant Flow Analyzer`, `#Schedule XL`, `#Sheet Assistant`) in `%ProgramData%\CTC Software\CTC BIM Suites\CTC BIM Project Suite Common Files\Ribbon Button Visibility Settings.txt`. Because `ProductStartup.LoadExternalApplications` filters `OnStartup` invocation by visible commands, none of the 7 unsafe `DeleteExistingLogFileOnFirstWrite = true` loggers will execute.
- **Trade-offs:** Disables all 7 tools interactively as well (unsuitable if the developer relies on `Model Compare`, `Schedule XL`, `View Aligner`, or `Sheet Assistant`), and future CTC updates overwrite `Ribbon Button Visibility Settings.txt`.

#### Option 6 (Rank 6): Vendor Escalation (`support@ctcsoftware.com`)
- **Action:** File a bug report with CTC Software citing `CTC.Util.LoggingTools.LoggingFunctions.AddToLog` (`CTC.Util.LoggingTools.dll` / `CTC.Util.LoggingTools2.NET.dll`, Suite `v27.1`) throwing an unhandled exception on `File.Delete` of `%PUBLIC%\CTC Software\CTC BIM Project Suite\<Tool> <Year> LastOnStartupLog.txt` when two instances of the same Revit version start concurrently—contradicting CTC's own documented multi-Revit workstation support (*Installation and Configuration Guide*, p. 9). Recommended vendor fix: either per-PID log filenames, `try/catch` fallback to `%TEMP%` matching `ProductStartup`, or guarding deletion with a cross-process named `Mutex`.

#### Rejected Options (Verified Non-Viable or Inferior)
1. **Per-run `AddInsSettings.json` toggling or moving `CTC-BIM-Project-Suite.addin` (`.bak`):** Both locations are shared by interactive Revit (`%APPDATA%\...\AddinsData\AddInsSettings.json` is per-user global and absent in Revit 2023–2024; `%ProgramData%\Autodesk\Revit\Addins\<Ver>\` is machine-global). Temporarily disabling CTC breaks concurrent interactive Revit launches and races if the human opens Revit mid-test.
2. **Fixed N-second launch stagger (`sleep`):** Empirically disproven by `journal.0184.txt` vs. `journal.0185.txt`, where a ~25-second launch delay still converged to the exact same `01:09:44.543` timestamp due to cold vs. warm OS cache variance.
3. **Overriding `%PUBLIC%` in `build_worker_env`:** Verified ineffective in IL (`PathTools.GetSuiteLoggingFolder` calls `Environment.GetFolderPath(SpecialFolder.CommonDocuments)` / `SHGetKnownFolderPath(FOLDERID_PublicDocuments)`, bypassing `%PUBLIC%`).
4. **Revit CLI / `Revit.ini` flag:** No Revit command-line switch or `Revit.ini` directive exists to skip `.addin` manifests per process.
5. **Secondary Windows User, Windows Sandbox, Hyper-V VMs, Windows Containers:** Previously ruled out due to Session 0 / DWM / DirectX 11 / WebView2 failures and `AdskLicensingService` single-seat constraints ([2026-11-20-revit-test-sandboxing-feasibility.md](2026-11-20-revit-test-sandboxing-feasibility.md#L216-L254)).
6. **Win32 API Hooking / Detours (`SHGetKnownFolderPath` / `CreateFileW`):** Brittle across `.NET 4.8` (2023–2024) and `.NET 8.0` (2025–2026), risks triggering EDR/antivirus heuristics or breaking `AdskLicensingService` / Revit native hooks.

---

## Primary-Source References

- **Local Incident Evidence:**
  - [conversation 711c5441-2aaf-446e-a915-d2ce66c01b46](conversation://711c5441-2aaf-446e-a915-d2ce66c01b46)
  - `%LOCALAPPDATA%\Autodesk\Revit\Autodesk Revit 2026\Journals\journal.0184.txt`, `journal.0185.txt`, `journal.0188.txt`, `journal.0189.txt`, `journal.0202.txt`, `journal.0203.txt`
  - `%PUBLIC%\CTC Software\CTC BIM Project Suite\Model Dashboard 2026 LastOnStartupLog.txt`, `Model Compare 2026 LastApplicationInitialized.txt`, `CTC BIM Project Suite 2026 ExternalAppLastStartupLog.txt`, `CTC BIM Project Suite 2026 ProductLastStartupLog.txt`
  - `%APPDATA%\CTC Software\Model Dashboard\Logs\2026.11.24 Model Dashboard.log`
  - `%LOCALAPPDATA%\CTC Software\Authentication\Logs\AuthenticatorLog.txt`, `AuthenticationLog_20261124_028.log`
- **Local Repo Files & Binaries:**
  - [test_executor.py](../../.agents/skills/revit-tests/scripts/test_executor.py)
  - [BootstrapApp.cs](../../src/Synthetic.Bootstrap/BootstrapApp.cs)
  - [2026-11-20-revit-test-sandboxing-feasibility.md](2026-11-20-revit-test-sandboxing-feasibility.md)
  - `%ProgramData%\Autodesk\Revit\Addins\2023..2026\CTC-BIM-Project-Suite.addin` & `.bundle\Contents\`
  - `%ProgramData%\CTC Software\CommonRevit\CTC.Util.LoggingTools.dll`, `CTC.Util.LoggingTools2.dll`, `CTC.Util.LoggingTools2.NET.dll`, `CTC.Forge.AuthenticationManager.dll`
  - `%ProgramData%\CTC Software\CTC BIM Suites\CTC BIM Suites Installation and Configuration Guide.pdf` (v27.1, pp. 5–15, 29–35, 46–60)
  - `%USERPROFILE%\.nuget\packages\ricaun.revittest.testadapter\1.11.1\` (`ricaun.RevitTest.Console.dll`, `ricaun.RevitTest.Application.dll`)
- **External Primary Documentation:**
  - CTC Software Documentation & Licensing Portal: https://help.ctcsoftware.com/ , https://ctcsoftware.com/ctc-bim-suites-download/ , https://www.ctcsoftware.com
  - `ricaun.RevitTest` Source Repository: https://github.com/ricaun-io/ricaun.RevitTest
  - Microsoft .NET `UnauthorizedAccessException` / Windows File Semantics: https://learn.microsoft.com/en-us/dotnet/api/system.unauthorizedaccessexception
  - Autodesk Revit Help (`Add-Ins Manager`, `AddInsSettings.json`, `TaskDialog_External_Tools_External_Tool_Failure`, `DialogBoxShowing`): https://help.autodesk.com/ (`search_web` verified against `help.autodesk.com` & `revitapidocs.com`)
