---
name: revit-journal-telemetry
description: Diagnose crashes, managed exceptions, RAM Statistics growth, and orphaned transactions in Revit Journals. Use when gating worker or run cleanliness (--assert-clean, --worker-id, --run-id), tailing or searching a Revit Journal, or watching a live Revit run.
---

# Revit Journal Telemetry (`revit-journal-telemetry`)

Analyze **Revit Journals** (`journal*.txt`) via [revit_journal_tool.py](scripts/revit_journal_tool.py) to gate automated test workers, isolate managed `Synthetic` exceptions, audit **Transaction seams**, and inspect **RAM Statistics** and **Crash Dumps**. Core diagnostics architecture lives in [ADR 0026](../../../docs/adrs/0026-revit-journal-telemetry-strategy.md), and **Standby Worker** / sharded attribution lives in [ADR 0030](../../../docs/adrs/0030-clean-standby-worker-pool-and-clean-launch-shim.md).

## 1. Journal Resolution Priority & Worker Attribution

`revit_journal_tool.py` resolves the target **Revit Journal** in strict priority order:
1. `--file <path>`: Analyze the explicit **Revit Journal** file directly.
2. `--worker-id <WorkerId>`: Scan `journal*.txt` newest-first for the exact token `SYNTHETIC_WORKER_ID:<WorkerId>` (exits `1` if empty or unmatched, without `--known-journals-file` fallback).
3. `--run-id <RunId>`: Scan `journal*.txt` newest-first for the exact token `SYNTHETIC_TEST_RUN:<RunId>`.
4. `--known-journals-file <path>` (when `--run-id` finds no marker, such as a pre-startup crash): Select the newest unknown `journal*.txt` absent from that pre-launch snapshot and lacking any `SYNTHETIC_TEST_RUN:` marker.
5. **Latest by mtime**: Select the most recently modified `journal*.txt` in `--journals-dir <dir>` or `%LOCALAPPDATA%\Autodesk\Revit\Autodesk Revit <Version>\Journals` (`--version` defaults to `CURRENT_LATEST_VERSION` in [revit-config.md](../../../docs/agents/revit-config.md)).

When no matching **Revit Journal** resolves, the tool exits `1`.

### Standby Worker Attribution & Multi-Shard Gating
- **Worker Pool emission:** Each **Standby Worker** boots with `SYNTHETIC_WORKER_ID=<WorkerId>`. **Add-In Bootstrap** ([`BootstrapApp.cs`](../../../src/Synthetic.Bootstrap/BootstrapApp.cs)) logs `SYNTHETIC_WORKER_ID:<WorkerId>` into the **Revit Journal** during `OnStartup` (with [`TestWorkerJournalMarker.EnsureWritten(uiapp)`](../../../tests/Fixtures/Revit/TestWorkerJournalMarker.cs) as idempotent fixture fallback), attributing pre-test crashes as well as executed runs.
- **Multi-shard per-worker gating:** Sharded **Worker Pool** runs execute each **Test Shard** on its own single-use **Standby Worker** (`1` **Revit Journal** per `WorkerId`). [`test_executor.py`](../revit-tests/scripts/test_executor.py) gates every returned `WorkerId` individually via `--worker-id <WorkerId> --assert-clean`, failing the run if any shard journal is dirty. Cold `--no-pool` runs gate via `--run-id <RunId> --known-journals-file <SnapshotPath> --assert-clean` (see [`revit-tests`](../revit-tests/SKILL.md)).

## 2. Diagnostic Commands & CLI Flags

Run commands from the repository root with `.agents/skills/revit-journal-telemetry/scripts/revit_journal_tool.py`:

```powershell
# Post-run cleanliness gate for a pooled Standby Worker / Test Shard
python .agents/skills/revit-journal-telemetry/scripts/revit_journal_tool.py --version 2027 --worker-id <WorkerId> --assert-clean

# Post-run cleanliness gate for a cold --no-pool worker run
python .agents/skills/revit-journal-telemetry/scripts/revit_journal_tool.py --run-id <RunId> --known-journals-file <SnapshotPath> --assert-clean

# Full telemetry diagnostics on an explicit Revit Journal or version folder
python .agents/skills/revit-journal-telemetry/scripts/revit_journal_tool.py --file <JournalPath> --analyze
python .agents/skills/revit-journal-telemetry/scripts/revit_journal_tool.py --version 2025 --analyze --output telemetry.txt

# Search with context, tail trailing lines, or watch an active Revit Journal
python .agents/skills/revit-journal-telemetry/scripts/revit_journal_tool.py --search "InvalidObjectException" --context 15
python .agents/skills/revit-journal-telemetry/scripts/revit_journal_tool.py --tail 100
python .agents/skills/revit-journal-telemetry/scripts/revit_journal_tool.py --watch
```

### Target & Mode Flags
| Flag | Default | Purpose |
| :--- | :--- | :--- |
| `--file <path>` | None | Explicit **Revit Journal** path (priority 1). |
| `--worker-id <WorkerId>` | None | Match `SYNTHETIC_WORKER_ID:<WorkerId>` for pooled **Standby Worker** / **Test Shard** attribution (priority 2). |
| `--run-id <RunId>` | None | Match `SYNTHETIC_TEST_RUN:<RunId>` for cold worker attribution (priority 3). |
| `--known-journals-file <path>` | None | Pre-launch snapshot (JSON list or line-delimited paths) for `--run-id` pre-startup fallback (priority 4). |
| `--journals-dir <dir>` | `%LOCALAPPDATA%\...\Journals` | Override the Revit Journals directory for lookup. |
| `--version <Year>` | `CURRENT_LATEST_VERSION` | Target **Revit Version** folder when `--file` and `--journals-dir` are omitted. |
| `--assert-clean` | Off | Run the post-run cleanliness gate and exit non-zero on failure. |
| `--analyze` | Off | Print full telemetry report (startup latency, exceptions, warnings, **RAM Statistics**, transactions, shutdown, UI trail). |
| `--watch` | Off | Poll an active **Revit Journal** in real time (`0.5 s` interval). |
| `--search <query>` | None | Case-insensitive match with `--context <N>` surrounding lines (default: `15`). |
| `--tail <N>` | `100` | Print trailing `<N>` lines when `--analyze`, `--assert-clean`, `--search`, and `--watch` are omitted. |
| `--output <path>` | stdout | Write command output to a UTF-8 file. |

### Output Limit Flags
| Flag | Default | Scope |
| :--- | :--- | :--- |
| `--limit-exceptions <N>` | `10` | Maximum exception blocks displayed in `--analyze`. |
| `--limit-warnings <N>` | `15` | Maximum warning/error lines displayed in `--analyze`. |
| `--limit-actions <N>` | `10` | Rolling UI reproduction trail size (`Jrn.Command`, `Jrn.RibbonEvent`, `Jrn.ComboBox`, `Jrn.ActiveView`, `Jrn.UIEvent`). |
| `--limit-memory <N>` | `3` | Initial and latest **RAM Statistics** entries displayed in `--analyze`. |
| `--limit-shutdown-scan <N>` | `200` | Trailing lines scanned for `ExitNativeInstance` and runner termination markers. |

## 3. Verified 2023–2027 Journal Semantics

### Transaction Seams
- **Open:** `'inTransaction <source> <name>` opens `<name>` unless `<source>` matches native C++ `*.cpp:<digits>`.
- **Commit:** `"Transaction Successful", "<name>"` closes `<name>` cleanly.
- **Rollback / Cancel:** `<<EndOrAbortUndoTransaction()` (excluding `--end of transaction checks` comments) clears open active transactions as rolled back; explicit `Transaction Rolled Back` or `Transaction Cancelled` entries close matching active transactions and record rollbacks.
- **Orphaned Lock:** Any open API transaction still active at EOF is flagged as locked.

### Scoped Exceptions & Benign Noise Filters
- **Benign lines skipped before exception/warning extraction:** `API_ERROR { : Assembly version conflict`, `CallbackFilter`, `API_SUCCESS`, `GetPreferences`, `LoadLatestEpoch`, `SYNTHETIC_TEST_RUN:`, `SYNTHETIC_WORKER_ID:`, `[MERGE_WARNING]`, and `[MERGE_ERROR]`.
- **Excluded exception headers:** `ricaun.RevitTest: FilterName:`, `ricaun.RevitTest:` lines containing `Passed` or `Execute Total:`, `DBG_WARN`, and `DBG_INFO` (the latter two are still recorded under `[WARNINGS]`).
- **Scoped failure attribution:** Managed exception blocks (header plus `at ...(...)`, `--->`, `--- End of stack trace`, `Parameter name:` frames) are counted in `synthetic_exceptions` when any line references `Synthetic.<Member>` or `SyntheticTests.<Member>` (excluding `.dll`, `.exe`, `.addin`, `.json`, `.xlsx`, `.rvt`, `.rfa` filenames).

### Crash Dumps, Shutdown Status, & `--assert-clean` Contract
- **Crash Dumps:** Any `<journal_basename>*.dmp` file beside the resolved **Revit Journal** triggers `[CRASH]`.
- **Shutdown classification:** Interactive sessions record `ExitNativeInstance`; automated `ricaun.RevitTest` runs terminate cleanly via `NUnit.Close` or `ricaun.RevitTest: Execute Total:` without `ExitNativeInstance`.
- **`--assert-clean` exit codes:**
  - `0` (`CLEAN`): Zero matching `.dmp` **Crash Dumps**, zero orphaned open API transactions at EOF, and zero unhandled `Synthetic.*` / `SyntheticTests.*` exceptions.
  - `1` (`DIRTY`): One or more matching `.dmp` **Crash Dumps**, orphaned open API transactions at EOF, or unhandled `Synthetic.*` / `SyntheticTests.*` exceptions (or target journal resolution failure).
