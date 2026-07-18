import os
import sys
import argparse
import re
import subprocess
import time
from datetime import datetime

# Default directories to ignore in path searches
DEFAULT_EXCLUDES = {'.git', 'bin', 'obj', 'packages'}

def find_workspace_root():
    """Walk up from this script's directory until a .git marker is found."""
    current = os.path.dirname(os.path.abspath(__file__))
    while True:
        if os.path.exists(os.path.join(current, '.git')):
            return current
        parent = os.path.dirname(current)
        if parent == current:
            # Fallback to current script directory if not found
            return os.path.dirname(os.path.abspath(__file__))
        current = parent

def get_current_latest_version():
    """Read the CURRENT_LATEST_VERSION value from docs/agents/revit-config.md."""
    try:
        workspace_dir = find_workspace_root()
        config_file = os.path.join(workspace_dir, "docs", "agents", "revit-config.md")
        if os.path.exists(config_file):
            with open(config_file, "r", encoding="utf-8") as f:
                content = f.read()
                # Matches patterns like CURRENT_LATEST_VERSION = 2026 or CURRENT_LATEST_VERSION = "2026"
                match = re.search(r'CURRENT_LATEST_VERSION\s*=\s*"?(\d+)"?', content)
                if match:
                    return match.group(1)
    except Exception:
        pass
    return "2026"  # Safe default fallback

def get_latest_journal(version):
    """Locates the most recently modified Revit journal file in the local AppData directory."""
    journal_dir = os.path.expandvars(f"%LOCALAPPDATA%\\Autodesk\\Revit\\Autodesk Revit {version}\\Journals")
    if not os.path.exists(journal_dir):
        print(f"[-] Directory does not exist: {journal_dir}", file=sys.stderr)
        return None
    files = [os.path.join(journal_dir, f) for f in os.listdir(journal_dir) if f.endswith(".txt") and f.startswith("journal")]
    if not files:
        print(f"[-] No journal files found in: {journal_dir}", file=sys.stderr)
        return None
    latest = max(files, key=os.path.getmtime)
    return latest

def read_journal_lines(filepath):
    """Safely reads lines from a journal file, trying various common Revit journal encodings."""
    encodings = ["utf-8-sig", "utf-16", "utf-8", "cp1252", "latin-1"]
    for enc in encodings:
        try:
            with open(filepath, "r", encoding=enc, errors="strict") as f:
                return f.readlines(), enc
        except (UnicodeDecodeError, LookupError):
            continue
    # Fallback with ignore errors
    with open(filepath, "r", encoding="utf-8", errors="ignore") as f:
        return f.readlines(), "utf-8 (fallback)"

def is_revit_running():
    """Checks if Revit.exe is currently active in the OS process table."""
    try:
        output = subprocess.check_output('tasklist /FI "IMAGENAME eq Revit.exe"', shell=True, text=True)
        return "Revit.exe" in output
    except Exception:
        return False

def extract_timestamp(line):
    """Attempts to parse a standard Revit journal timestamp from a line."""
    # Timestamps look like: ' 0:< 16-Jul-2026 12:00:00.123;
    match = re.search(r'(\d{2}-[A-Za-z]{3}-\d{4} \d{2}:\d{2}:\d{2}\.\d{3})', line)
    if match:
        try:
            return datetime.strptime(match.group(1), "%d-%b-%Y %H:%M:%S.%f")
        except ValueError:
            pass
    return None

def analyze_line_for_diagnostics(line, line_idx, state_dict):
    """Utility to analyze a single journal line, updating the state tracking dictionary."""
    line_clean = line.strip()
    
    # 1. Action Sequence Tracing
    if any(x in line for x in ["Jrn.Command", "Jrn.RibbonEvent", "Jrn.ComboBox", "Jrn.ActiveView", "Jrn.UIEvent"]):
        state_dict["actions"].append((line_idx + 1, line_clean))
        if len(state_dict["actions"]) > 10:
            state_dict["actions"].pop(0)

    # 2. Managed Exceptions Tracking
    if "exception" in line_clean.lower() or ("at " in line_clean and re.search(r'\b[A-Za-z0-9_]+\.[A-Za-z0-9_.]+\(', line_clean)):
        if not state_dict["in_stack_trace"]:
            state_dict["in_stack_trace"] = True
            state_dict["current_exception"] = [f"Line {line_idx+1}: {line_clean}"]
        else:
            state_dict["current_exception"].append(line_clean)
    else:
        if state_dict["in_stack_trace"]:
            state_dict["exceptions"].append("\n".join(state_dict["current_exception"]))
            state_dict["in_stack_trace"] = False
            state_dict["current_exception"] = []

    # 3. Warning/Error Log Scanning
    if "DBG_WARN" in line or "DBG_INFO" in line or "error" in line.lower() or "fail" in line.lower():
        if not any(x in line for x in ["API_SUCCESS", "GetPreferences", "LoadLatestEpoch"]):
            state_dict["warnings"].append((line_idx + 1, line_clean))

    # 4. Memory Statistics Scanning
    if "RAM Statistics:" in line or "Delta VM:" in line:
        state_dict["memory_logs"].append((line_idx + 1, line_clean))

    # 5. Transaction Boundary Auditing
    # Scans for common start/commit/rollback markers
    trans_start = re.search(r'(?:transaction\s*:\s*start|start\s*transaction)\s*:\s*([^;]+)', line_clean, re.I)
    trans_commit = re.search(r'(?:transaction\s*:\s*commit|commit\s*transaction)\s*:\s*([^;]+)', line_clean, re.I)
    trans_rollback = re.search(r'(?:transaction\s*:\s*rollback|rollback\s*transaction)\s*:\s*([^;]+)', line_clean, re.I)
    
    if trans_start:
        name = trans_start.group(1).strip()
        state_dict["active_transactions"][name] = line_idx + 1
    elif trans_commit:
        name = trans_commit.group(1).strip()
        state_dict["active_transactions"].pop(name, None)
    elif trans_rollback:
        name = trans_rollback.group(1).strip()
        state_dict["active_transactions"].pop(name, None)
        state_dict["rolled_back_transactions"].append((line_idx + 1, name))

    # 6. Startup Latency Benchmarking
    if "Synthetic" in line:
        ts = extract_timestamp(line)
        if ts:
            if not state_dict["synthetic_first_ts"]:
                state_dict["synthetic_first_ts"] = ts
            state_dict["synthetic_last_ts"] = ts

def analyze_journal(lines, filepath):
    """Performs full diagnostics on the journal lines."""
    state = {
        "exceptions": [],
        "in_stack_trace": False,
        "current_exception": [],
        "warnings": [],
        "memory_logs": [],
        "actions": [],
        "active_transactions": {},
        "rolled_back_transactions": [],
        "synthetic_first_ts": None,
        "synthetic_last_ts": None
    }

    for idx, line in enumerate(lines):
        analyze_line_for_diagnostics(line, idx, state)

    # Flush final exception block if still open
    if state["in_stack_trace"] and state["current_exception"]:
        state["exceptions"].append("\n".join(state["current_exception"]))

    analysis = []
    analysis.append("=== TELEMETRY DIAGNOSTIC ANALYSIS ===")
    
    # 1. Startup Latency Report
    if state["synthetic_first_ts"] and state["synthetic_last_ts"]:
        delta = state["synthetic_last_ts"] - state["synthetic_first_ts"]
        latency_ms = int(delta.total_seconds() * 1000)
        analysis.append(f"\n[STARTUP] Synthetic Add-In Load Time: {latency_ms} ms")
        analysis.append(f"  - Initiated: {state['synthetic_first_ts'].strftime('%H:%M:%S.%f')[:-3]}")
        analysis.append(f"  - Completed: {state['synthetic_last_ts'].strftime('%H:%M:%S.%f')[:-3]}")
    else:
        analysis.append("\n[STARTUP] Synthetic startup latency could not be calculated (missing lifecycle logs).")

    # 2. Managed Exceptions
    analysis.append(f"\n[EXCEPTIONS] Managed Exceptions Found: {len(state['exceptions'])}")
    for exc in state["exceptions"][:10]:
        analysis.append("-" * 40)
        analysis.append(exc)
    if len(state["exceptions"]) > 10:
        analysis.append(f"... and {len(state['exceptions']) - 10} more exceptions.")

    # 3. Warnings
    analysis.append(f"\n[WARNINGS] Potential Error/Warning Logs Found: {len(state['warnings'])}")
    for line_num, warning in state["warnings"][:15]:
        analysis.append(f"  Line {line_num}: {warning}")
    if len(state["warnings"]) > 15:
        analysis.append(f"... and {len(state['warnings']) - 15} more warnings.")

    # 4. Memory Profiling
    analysis.append(f"\n[MEMORY] Memory Logs Found: {len(state['memory_logs'])}")
    if len(state["memory_logs"]) > 0:
        analysis.append("Initial Memory Logs:")
        for line_num, stat in state["memory_logs"][:3]:
            analysis.append(f"  Line {line_num}: {stat}")
        if len(state["memory_logs"]) > 6:
            analysis.append("...")
            analysis.append("Latest Memory Logs:")
            for line_num, stat in state["memory_logs"][-3:]:
                analysis.append(f"  Line {line_num}: {stat}")

    # 5. Transactions Telemetry
    analysis.append(f"\n[TRANSACTIONS] Orphaned Database Locks: {len(state['active_transactions'])}")
    for name, line_num in state["active_transactions"].items():
        analysis.append(f"  [LOCKED] Transaction '{name}' opened at line {line_num} was never committed/rolled back!")
    if state["rolled_back_transactions"]:
        analysis.append(f"Rolled-Back Transactions: {len(state['rolled_back_transactions'])}")
        for line_num, name in state["rolled_back_transactions"][:5]:
            analysis.append(f"  Line {line_num}: Transaction '{name}' was Rolled Back")

    # 6. Shutdown Cleanliness & Process Check
    last_200 = lines[-200:] if len(lines) >= 200 else lines
    destroy_display_found = any("Destroy Display Manager" in ln for ln in last_200)
    
    # Check for crash dumps
    crash_detected = False
    journal_dir = os.path.dirname(filepath)
    journal_basename = os.path.splitext(os.path.basename(filepath))[0]
    dmp_pattern = os.path.join(journal_dir, journal_basename + "*.dmp")
    import glob as _glob
    dmp_files = _glob.glob(dmp_pattern)
    if dmp_files:
        crash_detected = True
        analysis.append(f"\n[CRITICAL] Crash dump file(s) found for this journal session:")
        for d in dmp_files:
            analysis.append(f"  {d}")

    revit_active = is_revit_running()
    
    analysis.append("\n[SHUTDOWN STATUS]")
    if destroy_display_found and not crash_detected:
        analysis.append("  [OK] Shutdown appears CLEAN: 'Destroy Display Manager' registered.")
    elif crash_detected:
        analysis.append("  [CRASH] Shutdown was ABNORMAL: Crash dump file (.dmp) detected.")
    elif revit_active:
        analysis.append("  [ACTIVE] Session is active (Revit.exe is currently running).")
    else:
        analysis.append("  [FORCE-KILLED] Shutdown was ABNORMAL: Process is dead, but exited without clean shutdown signature.")

    # 7. Action Sequence Context (if failure detected)
    if crash_detected or state["exceptions"] or (not destroy_display_found and not revit_active):
        analysis.append("\n[REPRODUCTION TRAIL] Last 10 User UI Actions:")
        if state["actions"]:
            for line_num, act in state["actions"]:
                analysis.append(f"  Line {line_num}: {act}")
        else:
            analysis.append("  No UI action logs captured in this session.")

    return "\n".join(analysis)

def watch_journal(filepath):
    """Continuously monitors the active journal, printing new telemetry logs in real-time."""
    print("="*80)
    print(f"WATCHING ACTIVE JOURNAL: {filepath}")
    print("Monitoring real-time exceptions, warnings, transactions, and memory. Press Ctrl+C to stop.")
    print("="*80)
    
    state = {
        "exceptions": [],
        "in_stack_trace": False,
        "current_exception": [],
        "warnings": [],
        "memory_logs": [],
        "actions": [],
        "active_transactions": {},
        "rolled_back_transactions": [],
        "synthetic_first_ts": None,
        "synthetic_last_ts": None
    }

    # Open and seek to end
    try:
        f = open(filepath, "r", encoding="utf-8-sig", errors="ignore")
        f.seek(0, 2)  # Go to end
    except Exception as e:
        print(f"[-] Failed to open watch stream: {e}", file=sys.stderr)
        sys.exit(1)

    line_idx = 0
    with f:
        try:
            while True:
                line = f.readline()
                if not line:
                    time.sleep(0.5)
                    continue
                
                # Analyze single line
                analyze_line_for_diagnostics(line, line_idx, state)
                line_clean = line.strip()

                # Print matching events immediately
                if "exception" in line_clean.lower() or "at " in line_clean:
                    print(f"\033[91m[EXCEPTION] Line {line_idx+1}: {line_clean}\033[0m")
                elif "DBG_WARN" in line or "error" in line_clean.lower():
                    if not any(x in line for x in ["API_SUCCESS", "GetPreferences"]):
                        print(f"\033[93m[WARNING] Line {line_idx+1}: {line_clean}\033[0m")
                elif "RAM Statistics:" in line:
                    print(f"\033[94m[MEMORY] Line {line_idx+1}: {line_clean}\033[0m")
                elif "transaction : start" in line_clean.lower() or "start transaction" in line_clean.lower():
                    print(f"\033[92m[TRANSACTION START] Line {line_idx+1}: {line_clean}\033[0m")
                elif "transaction : commit" in line_clean.lower() or "commit transaction" in line_clean.lower():
                    print(f"\033[92m[TRANSACTION COMMIT] Line {line_idx+1}: {line_clean}\033[0m")
                elif "transaction : rollback" in line_clean.lower() or "rollback transaction" in line_clean.lower():
                    print(f"\033[91m[TRANSACTION ROLLBACK] Line {line_idx+1}: {line_clean}\033[0m")

                line_idx += 1
        except KeyboardInterrupt:
            print("\nStopping watch stream. Telemetry closed.")

def main():
    default_ver = get_current_latest_version()
    
    parser = argparse.ArgumentParser(description="Revit Journal Telemetry Diagnostics Tool")
    parser.add_argument("--version", default=default_ver, help=f"Revit version target folder (default: {default_ver})")
    parser.add_argument("--tail", type=int, default=100, help="Number of lines to tail")
    parser.add_argument("--search", help="Query string to search for with context")
    parser.add_argument("--context", type=int, default=15, help="Lines of context around search matches")
    parser.add_argument("--analyze", action="store_true", help="Perform telemetry diagnostics analysis")
    parser.add_argument("--watch", action="store_true", help="Watch the active journal real-time (polling)")
    parser.add_argument("--output", help="Write results to path")

    args = parser.parse_args()

    latest_journal = get_latest_journal(args.version)
    if not latest_journal:
        sys.exit(1)

    if args.watch:
        watch_journal(latest_journal)
        sys.exit(0)

    lines, encoding = read_journal_lines(latest_journal)
    
    results = []
    results.append(f"Telemetry Target: {latest_journal}")
    results.append(f"Encoding:         {encoding}")
    results.append(f"Total Lines:      {len(lines)}")
    results.append("=" * 70)

    if args.analyze:
        analysis_text = analyze_journal(lines, latest_journal)
        results.append(analysis_text)
    elif args.search:
        query_lower = args.search.lower()
        matches = []
        for idx, line in enumerate(lines):
            if query_lower in line.lower():
                matches.append(idx)
        results.append(f"Search Query: '{args.search}'")
        results.append(f"Matches:      {len(matches)} found\n")
        
        for idx in matches:
            results.append(f"--- Match at Line {idx+1} ---")
            start = max(0, idx - args.context)
            end = min(len(lines), idx + args.context + 1)
            for i in range(start, end):
                prefix = ">>>" if i == idx else "   "
                results.append(f"{prefix} {i+1:5d}: {lines[i].rstrip()}")
            results.append("-" * 70)
    else:
        results.append(f"Tailing last {args.tail} lines:\n")
        start_idx = max(0, len(lines) - args.tail)
        for idx in range(start_idx, len(lines)):
            results.append(f" {idx + 1:5d}: {lines[idx].rstrip()}")

    output_text = "\n".join(results)
    
    if args.output:
        try:
            with open(args.output, "w", encoding="utf-8") as f:
                f.write(output_text)
            print(f"[+] Diagnostic report written to: {args.output}")
        except Exception as e:
            print(f"[-] Failed to write output file: {e}", file=sys.stderr)
            print(output_text)
    else:
        print(output_text)

if __name__ == "__main__":
    main()
