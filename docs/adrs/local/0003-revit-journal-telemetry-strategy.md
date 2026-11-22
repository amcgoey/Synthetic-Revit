# ADR 0003: Revit Journal Telemetry Strategy

## Context
In our multi-version Revit add-in testing and development environment, we need a reliable telemetry engine to diagnose crashes, unhandled C# exceptions, memory leaks, and database transaction lockups. 

Revit generates a session log (a **Revit Journal**) for every run. However, parsing these logs introduces several challenges:
1. Multiple installed Revit versions write to different folders, creating noise.
2. Silent crashes or force-killed sessions (e.g., via `taskkill`) leave no clean exit signature, making their status ambiguous.
3. Troubleshooting bugs requires knowing the exact user commands preceding the failure and checking that database transactions were committed cleanly.

## Decision
We will implement the `revit-journal-telemetry` skill using the following strategy:
1. **Strict Version Folder Isolation:** The diagnostic utility will target only the journal directory corresponding to the project's active `CURRENT_LATEST_VERSION` by default, rejecting logs from other versions unless an explicit override argument is passed.
2. **Process-Gated Shutdown Resolution:** To resolve "uncertain" shutdowns, the utility will query the active OS process list for `Revit.exe`. If the process is dead and no clean exit signature was recorded, it will be classified as an `Abnormal Termination (Force-Killed)` instead of `Uncertain`.
3. **Transaction boundary & Latency Profiling:** The parsing engine will track transaction starts/commits to locate orphaned database locks, and compute the initialization latency of the Synthetic ribbon.
4. **Interactive Monitoring:** Implement a `--watch` mode using file-seek polling to output warnings, memory metrics, and stack traces in real-time.
5. **Pre-Crash sequence tracing:** Maintain a rolling log of the last 10 user interface actions (`Jrn.Command`, etc.) and print them as a reproduction path upon detecting a failure.

## Consequences
* **Pros:** Enables robust and automated test execution reports, prevents cross-version false positives, and provides immediate visibility into memory growth and uncommitted transaction locks.
* **Cons:** Requires process querying permissions and relies on text parsing of Revit's proprietary journal formats, which may slightly evolve between versions.
