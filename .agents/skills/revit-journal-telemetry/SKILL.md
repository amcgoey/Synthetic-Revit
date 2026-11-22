---
name: revit-journal-telemetry
description: How to diagnose crashes, exceptions, memory growth, and shutdown cleanliness by tailing and searching Revit journal session logs. Use when troubleshooting Revit run sessions, test failures, or crashes.
---

# Revit Journal Telemetry Playbook (`revit-journal-telemetry`)

Use this playbook to analyze Autodesk Revit journal files and diagnose unhandled exceptions, memory leaks, database transaction boundary lockups, and startup latency.

## 1. Diagnostic CLI Commands

The diagnostic utility is located at `.agents/skills/revit-journal-telemetry/scripts/revit_journal_tool.py`. By default, it targets the journal folder corresponding to the project's configuration-defined `CURRENT_LATEST_VERSION`.

### Run Telemetry Diagnostics
Generate a full session analysis of the active or most recent Revit run:
```powershell
python .agents/skills/revit-journal-telemetry/scripts/revit_journal_tool.py --analyze
```

### Real-Time Live Watch Mode
Continuously tail and watch the active journal for exceptions, warnings, transactions, and RAM allocation logs during a live debug run:
```powershell
python .agents/skills/revit-journal-telemetry/scripts/revit_journal_tool.py --watch
```

### Search with Line Context
Locate a specific keyword or exception pattern in the log with surrounding context lines (defaults to 15 lines):
```powershell
python .agents/skills/revit-journal-telemetry/scripts/revit_journal_tool.py --search "InvalidObjectException"
```

### Version Folder Override
Target a different Revit version folder for analysis:
```powershell
python .agents/skills/revit-journal-telemetry/scripts/revit_journal_tool.py --version 2024 --analyze
```

### Adjusting Telemetry Output Limits
You can adjust the limits of exceptions, warnings, actions, and memory stats printed by using optional arguments:
* `--limit-exceptions <N>`: Adjust the max exceptions reported (default: 10).
* `--limit-warnings <N>`: Adjust the max warnings reported (default: 15).
* `--limit-actions <N>`: Adjust the sliding UI action sequence history size (default: 10).
* `--limit-memory <N>`: Adjust the count of memory logs displayed at startup and session end (default: 3).
* `--limit-shutdown-scan <N>`: Adjust the count of trailing lines scanned for clean exit signatures (default: 200).

Example:
```powershell
python .agents/skills/revit-journal-telemetry/scripts/revit_journal_tool.py --analyze --limit-exceptions 5 --limit-actions 15
```

---

## 2. Telemetry Output & Diagnostics Guide

The telemetry engine reads the journal file sequentially and parses the following information:

* **[STARTUP] Startup Latency:** Calculates the exact milliseconds elapsed between the start and completion of the Synthetic ribbon loading sequence. 
* **[EXCEPTIONS] Managed Exception Stack:** Extracts standard C# stack traces (matching `System.Exception`, `at Synthetic.`, etc.) and bundles them into single blocks to capture the full context of unhandled add-in code failures.
* **[WARNINGS] System Warnings:** Filters for lines containing `DBG_WARN`, `DBG_INFO`, or explicit `error`/`fail` messages, ignoring benign API success messages to reduce log noise.
* **[MEMORY] Memory Stats Profile:** Tracks RAM utilization logs over the session, printing the initial vs. latest statistics to highlight memory leak growth.
* **[TRANSACTIONS] Transaction Boundaries:** Scans for starting, committing, and rolling back of database transactions. Identifies any **orphaned locks** (transactions started but never committed or rolled back), which cause Revit's database to lock up.
* **[SHUTDOWN STATUS] Final Exit Classification:**
  * `[OK] Shutdown CLEAN`: The log terminated with the `Destroy Display Manager` exit signature, and no dump files were found.
  * `[ACTIVE] Session Active`: The Revit process is still alive and running on the OS process list.
  * `[CRASH] Abnormal (Dump File)`: A matching `.dmp` crash dump file was found in the Journals folder.
  * `[FORCE-KILLED] Abnormal (Process Dead, No Clean Signature)`: Revit is no longer running, but failed to log its clean exit signature (e.g., forced shut down or killed via Task Manager).
* **[REPRODUCTION TRAIL] Pre-Crash UI Actions:** Prints the last 10 UI commands (`Jrn.Command`, view changes, button clicks) preceding a diagnosed crash or exception. Use this trail to reconstruct a step-by-step reproduction path.
