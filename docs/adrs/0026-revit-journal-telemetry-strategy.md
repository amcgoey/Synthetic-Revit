# ADR 0026 - Revit Journal Telemetry Strategy

Status: Accepted

## Context
In our multi-version Revit add-in testing and development environment, we need a reliable telemetry engine to diagnose crashes, unhandled C# exceptions, memory growth, and database transaction lockups without relying on fragile host-wide OS heuristics.

Autodesk Revit generates a **Revit Journal** (`journal.XXXX.txt`) for every run. Parsing these files across Revit `2023–2027` introduces several concrete challenges:
1. Multiple installed Revit versions and parallel automated test workers write **Revit Journals** concurrently, so global latest-file lookups or host-wide `tasklist` (`Revit.exe`) checks misattribute runs and report false positives.
2. Revit logs internal native C++ entries (`*.cpp:<digits>`) alongside managed add-in transactions, uses `<<EndOrAbortUndoTransaction()` for rollbacks, and omits `ExitNativeInstance` when automated runners terminate via `NUnit.Close`.
3. Benign Revit startup/add-in messages (such as assembly version conflict notices, `CallbackFilter`, `API_SUCCESS`, and test-run markers) and non-Synthetic exceptions create alert noise if exception scanning is unscoped.
4. Troubleshooting failures requires knowing the exact user UI commands preceding a failure, verifying that **Transaction seams** were committed or rolled back cleanly, and providing a strict machine-checkable post-run cleanliness gate.

## Decision
We implement the [revit-journal-telemetry](../../.agents/skills/revit-journal-telemetry/SKILL.md) skill and [revit_journal_tool.py](../../.agents/skills/revit-journal-telemetry/scripts/revit_journal_tool.py) using the following strategy:

1. **Version Table Defaults & Deterministic Worker Attribution:**
   - Default `--version` parses `CURRENT_LATEST_VERSION` from the markdown table in [revit-config.md](../agents/revit-config.md) (falling back to legacy `=` syntax or `"2027"`), targeting `%LOCALAPPDATA%\Autodesk\Revit\Autodesk Revit <Version>\Journals` unless overridden by `--journals-dir`.
   - Journal resolution follows a strict priority order:
     1. `--file <path>`: explicit **Revit Journal** path.
     2. `--run-id <RunId>`: scans `journal*.txt` newest-first for the exact token `SYNTHETIC_TEST_RUN:<RunId>`. If no journal contains the marker yet (such as a pre-startup crash) and `--known-journals-file` is provided, falls back to the newest unknown `journal*.txt` absent from the pre-launch snapshot and lacking any `SYNTHETIC_TEST_RUN:` marker.
     3. Latest `journal*.txt` in the resolved target directory.

2. **Shutdown Classification Without Global Process or Mid-Session Heuristics:**
   - Remove global `tasklist` (`is_revit_running`) checks and mid-session `"Destroy Display Manager"` heuristics, both of which produce false classifications under parallel workers and normal view teardown.
   - Classify shutdown cleanly from trailing lines (`--limit-shutdown-scan`, default `200`, plus full-file `NUnit.Close` scan) and sibling **Crash Dumps** (`<journal_basename>*.dmp`):
     - Matching `.dmp` **Crash Dump** -> `[CRASH]` abnormal termination.
     - `ExitNativeInstance` in trailing lines -> `[OK]` clean interactive Revit shutdown.
     - `NUnit.Close` or `ricaun.RevitTest: Execute Total:` -> `[OK]` clean automated test runner termination without `ExitNativeInstance`.
     - Otherwise `[DIRTY]` if unresolved diagnostics exist, or `[OK]` when no **Crash Dumps**, orphaned transactions, or `Synthetic.*` / `SyntheticTests.*` exceptions occurred.

3. **Verified 2023–2027 Transaction-Seam Auditing & Startup Latency:**
   - Track **Transaction seams** across Revit `2023–2027`:
     - Open: `'inTransaction <source> <name>`, excluding native C++ sources matching `*.cpp:<digits>`.
     - Commit: `"Transaction Successful", "<name>"` clears `<name>` from open transactions.
     - Rollback / Cancel: `<<EndOrAbortUndoTransaction()` (excluding `--end of transaction checks` comments) and `Transaction Rolled Back` / `Transaction Cancelled` entries close matching open transactions and record rollbacks.
   - Report any open API transactions remaining at EOF as orphaned database locks, and compute Synthetic add-in load latency from the first and last timestamps on lines referencing `Synthetic`.

4. **Scoped Exception Matching & Benign Noise Filtering:**
   - Ignore benign journal lines (`API_ERROR { : Assembly version conflict`, `CallbackFilter`, `API_SUCCESS`, `GetPreferences`, `LoadLatestEpoch`, `SYNTHETIC_TEST_RUN:`, `[MERGE_WARNING]`, `[MERGE_ERROR]`) and non-failure runner/diagnostic prefixes (`ricaun.RevitTest: FilterName:`, `ricaun.RevitTest: ... Passed / Execute Total:`, `DBG_WARN`, `DBG_INFO` headers) before forming exception blocks.
   - Flag multi-line managed exception blocks as `synthetic_exceptions` only when their header or stack frames reference `Synthetic.<Member>` or `SyntheticTests.<Member>` (excluding file extensions such as `.dll`, `.exe`, `.addin`, `.json`, `.xlsx`, `.rvt`, `.rfa`).

5. **Post-Run Cleanliness Gate (`--assert-clean`), Watch Mode, & UI Trail:**
   - `--assert-clean` exits non-zero (`1`, `DIRTY`) strictly when matching `.dmp` **Crash Dumps**, orphaned open API transactions at EOF, or unhandled `Synthetic.*` / `SyntheticTests.*` exceptions are detected; otherwise exits `0` (`CLEAN`).
   - `--watch` tails active **Revit Journals** in real time for exceptions, warnings, **Transaction seams**, and **RAM Statistics**.
   - Retain a rolling buffer of the last `--limit-actions` (default `10`) UI actions (`Jrn.Command`, `Jrn.RibbonEvent`, `Jrn.ComboBox`, `Jrn.ActiveView`, `Jrn.UIEvent`) printed when cleanliness fails or exceptions occur.

## Consequences
* **Pros:** Enables deterministic attribution across concurrent test workers, prevents false positives from benign Revit startup logs or `NUnit.Close` exits, isolates real **Transaction seam** leaks from native C++ entries, and gives automated test runners a strict binary cleanliness gate via `--assert-clean`.
* **Cons:** Requires automated test runners to emit `SYNTHETIC_TEST_RUN:<RunId>` / `SYNTHETIC_WORKER_ID:<WorkerId>` (or supply `--known-journals-file`) for parallel worker disambiguation, and relies on Revit `2023–2027` journal text formats that must be re-verified when onboarding future Revit releases.
