---
name: revit-tests
description: Run NUnit unit and integration tests across Logic and Revit versions using isolated concurrent workers. Use when executing test commands, filtering by class or category, or running TDD loops.
---

# Revit Testing Playbook (`revit-tests`)

Run headless logic tests and live Revit integration tests via [test_executor.py](scripts/test_executor.py). Concurrent agents and test runs stay isolated from each other and from interactive developer Revit sessions through per-run MSBuild outputs, Win32 bundle leases, persistent code-signing trust, and automatic post-run journal gating ([revit-journal-telemetry](../revit-journal-telemetry/SKILL.md)).

## 1. TDD Execution Sequence

Under [tdd](../tdd/SKILL.md), execute tests in order of feedback speed:

1. **Step 1 (Logic Loop):** Run headless Logic tests first until green:
   ```powershell
   python .agents/skills/revit-tests/scripts/test_executor.py --version Logic
   ```
2. **Step 2 (Latest Revit Loop):** Run integration tests for `CURRENT_LATEST_VERSION` in [revit-config.md](../../../docs/agents/revit-config.md) (e.g. `2026`) until green:
   ```powershell
   python .agents/skills/revit-tests/scripts/test_executor.py --version 2026
   ```
3. **Step 3 (Multi-Version Validation):** Omit `--version` to validate all discovered `SyntheticTests*` targets (`Logic` and every supported Revit version):
   ```powershell
   python .agents/skills/revit-tests/scripts/test_executor.py
   ```
   Use [revit-multi-versions](../revit-multi-versions/SKILL.md) to resolve compilation or API discrepancies across Revit versions.

**Completion criterion:** A run passes only when `test_executor.py` exits `0`—confirming all targeted NUnit assertions passed, TRX results parsed cleanly, and every Revit target cleared the automatic post-run `--assert-clean` journal gate.

## 2. Test Execution Commands & CLI Flags

Run commands from the repository root:

```powershell
# Headless logic unit tests
python .agents/skills/revit-tests/scripts/test_executor.py --version Logic

# Specific Revit version (replace 2026 with CURRENT_LATEST_VERSION)
python .agents/skills/revit-tests/scripts/test_executor.py --version 2026

# Filter by class or method name fragment
python .agents/skills/revit-tests/scripts/test_executor.py --version 2026 --filter "ParameterEngineTests"

# Filter by NUnit [Category("...")] attribute
python .agents/skills/revit-tests/scripts/test_executor.py --version 2026 --category "Smoke"

# Bypass optional RevitWorkerPool and launch isolated cold Revit workers
python .agents/skills/revit-tests/scripts/test_executor.py --version 2026 --no-pool
```

| Flag | Default | Behavior |
| :--- | :--- | :--- |
| `--version <Target>` | All discovered targets | Targets `Logic` or a specific Revit year (e.g. `2026`). |
| `--filter <Fragment>` | None | Applies `FullyQualifiedName~<Fragment>` (combines with `--category` using `&`). |
| `--category <Name>` | None | Filters by NUnit `[Category("<Name>")]` (`TestCategory=<Name>` for `Logic`; scans `SyntheticTests.Shared` and target version sources for Revit projects, exiting `0` with `0 matching tests` if none match). |
| `--no-pool` | `False` | Skips optional `RevitWorkerPool` probing (`SYNTHETIC_WORKER_POOL_CMD` or `tools/RevitWorkerPool`) and forces isolated cold Revit workers. |

## 3. Isolated Concurrent Worker Architecture

### Per-Run MSBuild & Temp Isolation
- Each invocation generates `<RunId>` (`YYYYMMDD_HHMMSS_<pid>_<hex>`) and builds with `/p:SyntheticIsolatedBuildRoot=%TEMP%\SyntheticBuilds\<RunId>` and `/p:SyntheticRunId=<RunId>` ([tests/Directory.Build.props](../../../tests/Directory.Build.props), [src/Directory.Build.props](../../../src/Directory.Build.props)).
- Setting `SyntheticIsolatedBuildRoot` sets `SkipDeployAddin=true` and redirects `BaseIntermediateOutputPath` and `OutputPath` (`<SyntheticIsolatedBuildRoot>\bin\<Project>_<RunId>\`), leaving `output/Synthetic/` and `%APPDATA%\Autodesk\Revit\Addins\` untouched.
- Logs and `.trx` reports write to `tests/TestResults/<RunId>/` (rotated to keep the latest 10 runs).
- Passing runs remove `<SyntheticIsolatedBuildRoot>` and `%TEMP%\RevitTest\*<RunId>*`; failing runs preserve `<SyntheticIsolatedBuildRoot>` for inspection.

### Worker Process Controls & Timeout Cleanup
- [tests/Directory.Build.targets](../../../tests/Directory.Build.targets) injects `NUnit.Open=true`, `NUnit.Close=true`, `NUnit.Timeout=10` (minutes), and `ricaun.RevitTest.Application.Tasks.Timeout=5`.
- `test_executor.py` launches Revit workers with `SYNTHETIC_PROJECT_ROOT=<workspace>`, `SYNTHETIC_TEST_WORKER=1` (causes `BootstrapApp` to skip payload staging/loading at Revit startup so `ricaun.NUnit` loads the isolated test build), `SYNTHETIC_TEST_RUN_ID=<RunId>`, and `RICAUN_REVITTEST_CONSOLE_PROCESS_ARGUMENTS=/nosplash` (`/nosplash`).
- If `dotnet test` exceeds `660` s (`NUnit.Timeout` + 60 s boot buffer), `execute_revit_test_process` terminates only that worker's child PID tree with `taskkill /F /T /PID`.

### Shared Bundle Lease (`RicaunBundleLease`)
- Concurrent workers coordinate `%APPDATA%\Autodesk\ApplicationPlugins\ricaun.RevitTest.Application.bundle\PackageContents.xml` via the Win32 mutex `Local\Synthetic_RicaunBundleLease` and a shared-read handle without `FILE_SHARE_DELETE`, with zero `ProgramData` bundle seeding.
- The first active worker extracts the bundle from `ricaun.RevitTest.TestAdapter.dll` into `%APPDATA%`; only the final exiting worker deletes the bundle once all shared-read handles close.

## 4. Code-Signing Whitelist & Add-In Safety

- **Persistent `CodeSigning` Registry Trust:** [guid_whitelister.py](scripts/guid_whitelister.py) recursively scans `.addin` (`ClientId`/`AddInId`) and `PackageContents.xml` (`ProductCode`/`ClientId`/`AddInId`) manifests across all Revit add-in roots (`%ProgramFiles%\Autodesk\Revit <ver>\AddIns`, `%ProgramFiles%\Autodesk\Revit\Addins\<ver>`, `%ProgramData%\Autodesk\Revit\Addins\<ver>`, `%AppData%\Autodesk\Revit\Addins\<ver>`) and `ApplicationPlugins` bundle roots (`%AppData%\Autodesk\ApplicationPlugins`, `%ProgramData%\Autodesk\ApplicationPlugins`), plus `KNOWN_FRAMEWORK_GUIDS`. It persists each GUID (lowercase and uppercase `REG_DWORD=1`) in `HKCU\Software\Autodesk\Revit\Autodesk Revit <ver>\CodeSigning` with no teardown deletion, eliminating registry races between concurrent workers.
- **Zero `AddInsSettings.json` Mutation:** Test runs leave `AddInsSettings.json` unmodified; on startup, `TempDisableThirdPartyAddins.restore_addins()` only self-heals any orphaned `AddInsSettings.json.bak` left behind by legacy runs.

## 5. Post-Run Journal Cleanliness Gate

- Before starting each Revit worker, `test_executor.py` snapshots existing `journal*.txt` files to `tests/TestResults/<RunId>/known_journals_<ver>.json`.
- Inside Revit, `BootstrapApp` and [TestWorkerJournalMarker.cs](../../../tests/SyntheticTests.Shared/Helpers/TestWorkerJournalMarker.cs) (`TestWorkerJournalMarker.EnsureWritten(uiapp)`) emit `SYNTHETIC_TEST_RUN:<RunId>` once per worker process into the active Revit Journal.
- Immediately after each Revit run finishes, `test_executor.py` invokes [revit_journal_tool.py](../revit-journal-telemetry/scripts/revit_journal_tool.py):
  ```powershell
  python .agents/skills/revit-journal-telemetry/scripts/revit_journal_tool.py --version <ver> --run-id <run_id> --known-journals-file <snapshot_path> --assert-clean
  ```
- Any matching `.dmp` crash dump, orphaned open transaction, or unhandled `Synthetic.*` / `SyntheticTests.*` exception fails the run even when NUnit reports passing tests. Use [revit-journal-telemetry](../revit-journal-telemetry/SKILL.md) to inspect dirty runs.
