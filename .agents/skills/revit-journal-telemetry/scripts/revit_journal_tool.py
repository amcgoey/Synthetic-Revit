import argparse
import glob
import json
import os
import re
import sys
import time
from datetime import datetime

# Default directories to ignore in path searches
DEFAULT_EXCLUDES = {'.git', 'bin', 'obj', 'packages'}

# Benign Revit journal lines that must never trigger exceptions or warnings
BENIGN_LOG_PATTERNS = (
    "API_ERROR { : Assembly version conflict",
    "CallbackFilter",
    "API_SUCCESS",
    "GetPreferences",
    "LoadLatestEpoch",
    "SYNTHETIC_TEST_RUN:",
    "SYNTHETIC_WORKER_ID:",
    "[MERGE_WARNING]",
    "[MERGE_ERROR]",
)

# Verified 2023-2026 journal transaction patterns
IN_TRANSACTION_RE = re.compile(r"'inTransaction\s+(\S+)\s+(.+)")
CPP_SOURCE_RE = re.compile(r"\.cpp:\d+$", re.IGNORECASE)
TRANSACTION_SUCCESS_RE = re.compile(r'"Transaction Successful"\s*,\s*"([^"]+)"')
END_OR_ABORT_UNDO_RE = re.compile(r"<<EndOrAbortUndoTransaction\(\)(?!--)")
EXPLICIT_ROLLBACK_QUOTED_RE = re.compile(
    r'"Transaction (?:Rolled Back|Cancelled)"\s*,\s*"([^"]+)"',
    re.IGNORECASE,
)
EXPLICIT_ROLLBACK_RE = re.compile(
    r"\bTransaction\s+(Rolled Back|Cancelled)(?:\s*[,:]\s*(.+))?",
    re.IGNORECASE,
)

# Managed exception & stack frame patterns
EXCEPTION_HEADER_RE = re.compile(
    r"\b[A-Za-z_][A-Za-z0-9_.]*Exception\b|Unhandled\s+Exception",
    re.IGNORECASE,
)
STACK_FRAME_RE = re.compile(
    r"^\s*'?\s*at\s+[A-Za-z_][A-Za-z0-9_.`<>]+\.[A-Za-z_][A-Za-z0-9_.`<>]*\("
)
SYNTHETIC_REF_RE = re.compile(
    r"\b(?:Synthetic|SyntheticTests)\.(?!dll\b|exe\b|addin\b|json\b|xlsx\b|rvt\b|rfa\b)[A-Za-z_][A-Za-z0-9_]*"
)
JOURNAL_ENTRY_PREFIX_RE = re.compile(r"^\s*'\s*\d+:<")


def find_workspace_root():
    """Walk up from this script's directory until a .git marker is found."""
    current = os.path.dirname(os.path.abspath(__file__))
    while True:
        if os.path.exists(os.path.join(current, ".git")):
            return current
        parent = os.path.dirname(current)
        if parent == current:
            return os.path.dirname(os.path.abspath(__file__))
        current = parent


def parse_current_latest_version(content):
    """Parse CURRENT_LATEST_VERSION from markdown table rows or legacy '=' syntax."""
    table_match = re.search(
        r'\|\s*`?CURRENT_LATEST_VERSION`?\s*\|\s*[`"]?(\d+)[`"]?\s*\|',
        content,
    )
    if table_match:
        return table_match.group(1)

    legacy_match = re.search(r'CURRENT_LATEST_VERSION\s*=\s*"?(\d+)"?', content)
    if legacy_match:
        return legacy_match.group(1)

    return None


def get_current_latest_version(config_path=None):
    """Read CURRENT_LATEST_VERSION from docs/agents/revit-config.md."""
    try:
        if config_path is None:
            workspace_dir = find_workspace_root()
            config_path = os.path.join(
                workspace_dir, "docs", "agents", "revit-config.md"
            )
        if os.path.exists(config_path):
            with open(config_path, "r", encoding="utf-8") as f:
                parsed = parse_current_latest_version(f.read())
                if parsed:
                    return parsed
    except Exception:
        pass
    return "2026"


def get_default_journals_dir(version):
    """Return the default Revit Journals directory for the given Revit version."""
    return os.path.expandvars(
        f"%LOCALAPPDATA%\\Autodesk\\Revit\\Autodesk Revit {version}\\Journals"
    )


def list_journal_files(journal_dir):
    """Return journal*.txt files in journal_dir sorted newest-first by mtime."""
    if not os.path.isdir(journal_dir):
        return []
    files = []
    for entry in os.listdir(journal_dir):
        if entry.startswith("journal") and entry.endswith(".txt"):
            full_path = os.path.join(journal_dir, entry)
            if os.path.isfile(full_path):
                files.append(full_path)
    files.sort(key=lambda p: (os.path.getmtime(p), p), reverse=True)
    return files


def read_journal_lines(filepath):
    """Safely read lines from a journal file across common Revit encodings."""
    encodings = ["utf-8-sig", "utf-16", "utf-8", "cp1252", "latin-1"]
    for enc in encodings:
        try:
            with open(filepath, "r", encoding=enc, errors="strict") as f:
                return f.readlines(), enc
        except (UnicodeError, LookupError):
            continue
    with open(filepath, "r", encoding="utf-8", errors="ignore") as f:
        return f.readlines(), "utf-8 (fallback)"


def _load_known_journals(known_journals_file, journal_dir):
    """Load pre-launch journal snapshot entries into normalized full paths."""
    with open(known_journals_file, "r", encoding="utf-8-sig") as f:
        raw = f.read().strip()

    if not raw:
        return set()

    if raw.startswith("["):
        entries = json.loads(raw)
        if not isinstance(entries, list):
            raise ValueError("Known journals JSON must be a list of paths or filenames.")
        items = [str(item).strip() for item in entries if str(item).strip()]
    else:
        items = [
            ln.strip()
            for ln in raw.splitlines()
            if ln.strip() and not ln.strip().startswith("#")
        ]

    known_paths = set()
    for item in items:
        if os.path.isabs(item) or os.sep in item or "/" in item:
            known_paths.add(os.path.normcase(os.path.abspath(item)))
        else:
            known_paths.add(os.path.normcase(os.path.abspath(os.path.join(journal_dir, item))))
    return known_paths


def _contains_marker_token(text, prefix, value):
    """Return True when text contains the exact <prefix>:<value> token."""
    pattern = re.compile(
        rf"{re.escape(prefix)}:{re.escape(value)}(?![A-Za-z0-9_.-])"
    )
    return bool(pattern.search(text))


def _contains_run_id(text, run_id):
    """Return True when text contains the exact SYNTHETIC_TEST_RUN:<RunId> token."""
    return _contains_marker_token(text, "SYNTHETIC_TEST_RUN", run_id)


def _contains_worker_id(text, worker_id):
    """Return True when text contains the exact SYNTHETIC_WORKER_ID:<WorkerId> token."""
    return _contains_marker_token(text, "SYNTHETIC_WORKER_ID", worker_id)


def get_latest_journal(version, journals_dir=None):
    """Locate the most recently modified Revit journal file in the target folder."""
    target_dir = journals_dir if journals_dir else get_default_journals_dir(version)
    if not os.path.isdir(target_dir):
        print(f"[-] Directory does not exist: {target_dir}", file=sys.stderr)
        return None
    files = list_journal_files(target_dir)
    if not files:
        print(f"[-] No journal files found in: {target_dir}", file=sys.stderr)
        return None
    return files[0]


def resolve_journal_path(
    version,
    file_path=None,
    run_id=None,
    known_journals_file=None,
    journals_dir=None,
    worker_id=None,
):
    """Resolve the target journal according to --file -> --worker-id -> --run-id -> latest."""
    if file_path:
        if not os.path.isfile(file_path):
            print(f"[-] Specified journal file does not exist: {file_path}", file=sys.stderr)
            return None
        return os.path.abspath(file_path)

    target_dir = journals_dir if journals_dir else get_default_journals_dir(version)
    if not os.path.isdir(target_dir):
        print(f"[-] Directory does not exist: {target_dir}", file=sys.stderr)
        return None

    candidates = list_journal_files(target_dir)
    if not candidates:
        print(f"[-] No journal files found in: {target_dir}", file=sys.stderr)
        return None

    if worker_id is not None:
        if not worker_id.strip():
            print(
                "[-] Worker ID must be non-empty when --worker-id is specified.",
                file=sys.stderr,
            )
            return None
        for candidate in candidates:
            lines, _ = read_journal_lines(candidate)
            text = "".join(lines)
            if _contains_worker_id(text, worker_id):
                return candidate
        print(
            f"[-] No journal matching worker ID '{worker_id}' found in: {target_dir}",
            file=sys.stderr,
        )
        return None

    if run_id:
        cached_texts = {}
        for candidate in candidates:
            lines, _ = read_journal_lines(candidate)
            text = "".join(lines)
            cached_texts[candidate] = text
            if _contains_run_id(text, run_id):
                return candidate

        if known_journals_file:
            try:
                known_paths = _load_known_journals(known_journals_file, target_dir)
            except Exception as exc:
                print(
                    f"[-] Failed to read known journals snapshot '{known_journals_file}': {exc}",
                    file=sys.stderr,
                )
                return None

            for candidate in candidates:
                norm_candidate = os.path.normcase(os.path.abspath(candidate))
                if norm_candidate in known_paths:
                    continue
                if "SYNTHETIC_TEST_RUN:" in cached_texts.get(candidate, ""):
                    continue
                return candidate

        print(
            f"[-] No journal matching run ID '{run_id}' found in: {target_dir}",
            file=sys.stderr,
        )
        return None

    return candidates[0]


def extract_timestamp(line):
    """Attempt to parse a standard Revit journal timestamp from a line."""
    match = re.search(r"(\d{2}-[A-Za-z]{3}-\d{4} \d{2}:\d{2}:\d{2}\.\d{3})", line)
    if match:
        try:
            return datetime.strptime(match.group(1), "%d-%b-%Y %H:%M:%S.%f")
        except ValueError:
            pass
    return None


def _is_benign_line(line):
    """Return True if the journal line matches known benign Revit/Synthetic noise."""
    return any(pattern in line for pattern in BENIGN_LOG_PATTERNS)


def _is_exception_block_synthetic(block_lines):
    """Return True if an exception block's type/header or stack frames reference Synthetic."""
    return any(SYNTHETIC_REF_RE.search(ln) for ln in block_lines)


def _flush_exception_block(state_dict):
    """Finalize any open exception block into exceptions and synthetic_exceptions."""
    if not state_dict["in_stack_trace"] or not state_dict["current_exception"]:
        state_dict["in_stack_trace"] = False
        state_dict["current_exception"] = []
        return

    block_lines = state_dict["current_exception"]
    block_text = "\n".join(block_lines)
    state_dict["exceptions"].append(block_text)
    if _is_exception_block_synthetic(block_lines):
        state_dict["synthetic_exceptions"].append(block_text)

    state_dict["in_stack_trace"] = False
    state_dict["current_exception"] = []


def _is_exception_header(line, line_clean):
    """Determine if a non-benign line starts a managed exception block."""
    if "ricaun.RevitTest: FilterName:" in line:
        return False
    if "ricaun.RevitTest:" in line and ("\t Passed" in line or "Execute Total:" in line):
        return False
    if "DBG_WARN" in line or "DBG_INFO" in line:
        return False
    return bool(EXCEPTION_HEADER_RE.search(line_clean))


def _is_stack_continuation(line_clean):
    """Determine if a line continues an active managed exception stack trace."""
    if STACK_FRAME_RE.match(line_clean):
        return True
    stripped_comment = line_clean.lstrip("'").strip()
    if stripped_comment.startswith("--->") or stripped_comment.startswith("--- End of stack trace"):
        return True
    if stripped_comment.startswith("Parameter name:"):
        return True
    return False


def create_diagnostics_state():
    """Create a fresh diagnostics state dictionary."""
    return {
        "exceptions": [],
        "synthetic_exceptions": [],
        "in_stack_trace": False,
        "current_exception": [],
        "warnings": [],
        "memory_logs": [],
        "actions": [],
        "active_transactions": {},
        "rolled_back_transactions": [],
        "synthetic_first_ts": None,
        "synthetic_last_ts": None,
    }


def analyze_line_for_diagnostics(line, line_idx, state_dict, limit_actions=10):
    """Analyze a single journal line, updating the state tracking dictionary."""
    line_clean = line.strip()

    # 1. Action Sequence Tracing
    if any(
        x in line
        for x in (
            "Jrn.Command",
            "Jrn.RibbonEvent",
            "Jrn.ComboBox",
            "Jrn.ActiveView",
            "Jrn.UIEvent",
        )
    ):
        state_dict["actions"].append((line_idx + 1, line_clean))
        if len(state_dict["actions"]) > limit_actions:
            state_dict["actions"].pop(0)

    # 2. Managed Exceptions Tracking & Warning/Error Scanning (with benign filtering)
    is_benign = _is_benign_line(line)
    if is_benign:
        _flush_exception_block(state_dict)
    else:
        is_header = _is_exception_header(line, line_clean)
        is_continuation = _is_stack_continuation(line_clean)

        if state_dict["in_stack_trace"]:
            if is_continuation:
                state_dict["current_exception"].append(line_clean)
            elif is_header and JOURNAL_ENTRY_PREFIX_RE.match(line):
                _flush_exception_block(state_dict)
                state_dict["in_stack_trace"] = True
                state_dict["current_exception"] = [f"Line {line_idx + 1}: {line_clean}"]
            elif is_header:
                state_dict["current_exception"].append(line_clean)
            else:
                _flush_exception_block(state_dict)
        elif is_header or STACK_FRAME_RE.match(line_clean):
            state_dict["in_stack_trace"] = True
            state_dict["current_exception"] = [f"Line {line_idx + 1}: {line_clean}"]

        if (
            "DBG_WARN" in line
            or "DBG_INFO" in line
            or "error" in line.lower()
            or "fail" in line.lower()
        ):
            state_dict["warnings"].append((line_idx + 1, line_clean))

    # 3. Memory Statistics Scanning
    if "RAM Statistics:" in line or "Delta VM:" in line:
        state_dict["memory_logs"].append((line_idx + 1, line_clean))

    # 4. Verified 2023-2026 Transaction-Seam Auditing
    in_tx = IN_TRANSACTION_RE.search(line_clean)
    if in_tx:
        source = in_tx.group(1).strip()
        name = in_tx.group(2).strip()
        if not CPP_SOURCE_RE.search(source):
            if name not in state_dict["active_transactions"]:
                state_dict["active_transactions"][name] = line_idx + 1
    else:
        tx_success = TRANSACTION_SUCCESS_RE.search(line_clean)
        if tx_success:
            name = tx_success.group(1).strip()
            state_dict["active_transactions"].pop(name, None)
        elif (
            END_OR_ABORT_UNDO_RE.search(line_clean)
            and "--end of transaction checks" not in line_clean
        ):
            if state_dict["active_transactions"]:
                for open_name in list(state_dict["active_transactions"].keys()):
                    state_dict["active_transactions"].pop(open_name, None)
                    state_dict["rolled_back_transactions"].append(
                        (line_idx + 1, open_name)
                    )
        else:
            quoted_rb = EXPLICIT_ROLLBACK_QUOTED_RE.search(line_clean)
            if quoted_rb:
                name = quoted_rb.group(1).strip()
                state_dict["active_transactions"].pop(name, None)
                state_dict["rolled_back_transactions"].append((line_idx + 1, name))
            else:
                explicit_rb = EXPLICIT_ROLLBACK_RE.search(line_clean)
                if explicit_rb:
                    status_label = explicit_rb.group(1).strip()
                    tail = (explicit_rb.group(2) or "").strip().strip('"').strip("'")
                    if tail:
                        state_dict["active_transactions"].pop(tail, None)
                        state_dict["rolled_back_transactions"].append(
                            (line_idx + 1, tail)
                        )
                    elif state_dict["active_transactions"]:
                        for open_name in list(state_dict["active_transactions"].keys()):
                            state_dict["active_transactions"].pop(open_name, None)
                            state_dict["rolled_back_transactions"].append(
                                (line_idx + 1, open_name)
                            )
                    else:
                        state_dict["rolled_back_transactions"].append(
                            (line_idx + 1, f"Transaction {status_label}")
                        )

    # 5. Startup Latency Benchmarking
    if "Synthetic" in line:
        ts = extract_timestamp(line)
        if ts:
            if not state_dict["synthetic_first_ts"]:
                state_dict["synthetic_first_ts"] = ts
            state_dict["synthetic_last_ts"] = ts


def find_crash_dumps(filepath):
    """Return matching <journal_basename>*.dmp crash dump files alongside filepath."""
    journal_dir = os.path.dirname(os.path.abspath(filepath))
    journal_basename = os.path.splitext(os.path.basename(filepath))[0]
    dmp_pattern = glob.escape(os.path.join(journal_dir, journal_basename)) + "*.dmp"
    return sorted(glob.glob(dmp_pattern))


def analyze_journal_state(
    lines,
    filepath,
    limit_exceptions=10,
    limit_warnings=15,
    limit_actions=10,
    limit_memory=3,
    limit_shutdown_scan=200,
):
    """Analyze journal lines and return structured telemetry state and formatted report."""
    state = create_diagnostics_state()

    for idx, line in enumerate(lines):
        analyze_line_for_diagnostics(line, idx, state, limit_actions=limit_actions)

    _flush_exception_block(state)

    last_scan = lines[-limit_shutdown_scan:] if len(lines) >= limit_shutdown_scan else lines
    exit_native_found = any("ExitNativeInstance" in ln for ln in last_scan)
    nunit_close_found = any(
        "NUnit.Close" in ln or "ricaun.RevitTest: Execute Total:" in ln
        for ln in last_scan
    ) or any("NUnit.Close" in ln for ln in lines)

    dmp_files = find_crash_dumps(filepath)
    crash_detected = bool(dmp_files)
    orphaned_transactions = dict(state["active_transactions"])
    synthetic_exceptions = list(state["synthetic_exceptions"])
    is_clean = not crash_detected and not orphaned_transactions and not synthetic_exceptions

    analysis = []
    analysis.append("=== TELEMETRY DIAGNOSTIC ANALYSIS ===")

    # 1. Startup Latency Report
    if state["synthetic_first_ts"] and state["synthetic_last_ts"]:
        delta = state["synthetic_last_ts"] - state["synthetic_first_ts"]
        latency_ms = int(delta.total_seconds() * 1000)
        analysis.append(f"\n[STARTUP] Synthetic Add-In Load Time: {latency_ms} ms")
        analysis.append(
            f"  - Initiated: {state['synthetic_first_ts'].strftime('%H:%M:%S.%f')[:-3]}"
        )
        analysis.append(
            f"  - Completed: {state['synthetic_last_ts'].strftime('%H:%M:%S.%f')[:-3]}"
        )
    else:
        analysis.append(
            "\n[STARTUP] Synthetic startup latency could not be calculated (missing lifecycle logs)."
        )

    # 2. Managed Exceptions
    analysis.append(
        f"\n[EXCEPTIONS] Synthetic Managed Exceptions: {len(synthetic_exceptions)} "
        f"(Total Managed Exceptions: {len(state['exceptions'])})"
    )
    displayed_exceptions = (
        synthetic_exceptions if synthetic_exceptions else state["exceptions"]
    )
    for exc in displayed_exceptions[:limit_exceptions]:
        analysis.append("-" * 40)
        analysis.append(exc)
    if len(displayed_exceptions) > limit_exceptions:
        analysis.append(
            f"... and {len(displayed_exceptions) - limit_exceptions} more exceptions."
        )

    # 3. Warnings
    analysis.append(
        f"\n[WARNINGS] Potential Error/Warning Logs Found: {len(state['warnings'])}"
    )
    for line_num, warning in state["warnings"][:limit_warnings]:
        analysis.append(f"  Line {line_num}: {warning}")
    if len(state["warnings"]) > limit_warnings:
        analysis.append(
            f"... and {len(state['warnings']) - limit_warnings} more warnings."
        )

    # 4. Memory Profiling
    analysis.append(f"\n[MEMORY] Memory Logs Found: {len(state['memory_logs'])}")
    if len(state["memory_logs"]) > 0:
        analysis.append("Initial Memory Logs:")
        for line_num, stat in state["memory_logs"][:limit_memory]:
            analysis.append(f"  Line {line_num}: {stat}")
        if len(state["memory_logs"]) > (limit_memory * 2):
            analysis.append("...")
            analysis.append("Latest Memory Logs:")
            for line_num, stat in state["memory_logs"][-limit_memory:]:
                analysis.append(f"  Line {line_num}: {stat}")
        elif len(state["memory_logs"]) > limit_memory:
            for line_num, stat in state["memory_logs"][limit_memory:]:
                analysis.append(f"  Line {line_num}: {stat}")

    # 5. Transactions Telemetry
    analysis.append(
        f"\n[TRANSACTIONS] Orphaned Database Locks: {len(orphaned_transactions)}"
    )
    for name, line_num in orphaned_transactions.items():
        analysis.append(
            f"  [LOCKED] Transaction '{name}' opened at line {line_num} was never committed/rolled back!"
        )
    if state["rolled_back_transactions"]:
        analysis.append(
            f"Rolled-Back Transactions: {len(state['rolled_back_transactions'])}"
        )
        for line_num, name in state["rolled_back_transactions"][:5]:
            analysis.append(f"  Line {line_num}: Transaction '{name}' was Rolled Back")

    # 6. Crash Dumps & Shutdown Cleanliness
    if crash_detected:
        analysis.append("\n[CRITICAL] Crash dump file(s) found for this journal session:")
        for d in dmp_files:
            analysis.append(f"  {d}")

    analysis.append("\n[SHUTDOWN STATUS]")
    if crash_detected:
        analysis.append(
            "  [CRASH] Shutdown was ABNORMAL: Crash dump file (.dmp) detected."
        )
    elif exit_native_found:
        analysis.append(
            f"  [OK] Shutdown appears CLEAN: 'ExitNativeInstance' registered in last {limit_shutdown_scan} lines."
        )
    elif nunit_close_found:
        analysis.append(
            "  [OK] Shutdown appears CLEAN: Automated test runner termination (NUnit.Close) without 'ExitNativeInstance'."
        )
    elif not is_clean:
        analysis.append(
            "  [DIRTY] Session ended without 'ExitNativeInstance' and has unresolved diagnostics."
        )
    else:
        analysis.append(
            "  [OK] No 'ExitNativeInstance' recorded (e.g., NUnit.Close worker exit or active session); no crash dumps, orphaned transactions, or Synthetic exceptions detected."
        )

    # 7. Action Sequence Context (if failure detected)
    if not is_clean or state["exceptions"]:
        analysis.append(f"\n[REPRODUCTION TRAIL] Last {limit_actions} User UI Actions:")
        if state["actions"]:
            for line_num, act in state["actions"]:
                analysis.append(f"  Line {line_num}: {act}")
        else:
            analysis.append("  No UI action logs captured in this session.")

    result = {
        "state": state,
        "dmp_files": dmp_files,
        "crash_detected": crash_detected,
        "orphaned_transactions": orphaned_transactions,
        "synthetic_exceptions": synthetic_exceptions,
        "exit_native_found": exit_native_found,
        "nunit_close_found": nunit_close_found,
        "is_clean": is_clean,
        "report": "\n".join(analysis),
    }
    return result


def analyze_journal(
    lines,
    filepath,
    limit_exceptions=10,
    limit_warnings=15,
    limit_actions=10,
    limit_memory=3,
    limit_shutdown_scan=200,
):
    """Perform full diagnostics on journal lines and return the formatted report string."""
    return analyze_journal_state(
        lines,
        filepath,
        limit_exceptions=limit_exceptions,
        limit_warnings=limit_warnings,
        limit_actions=limit_actions,
        limit_memory=limit_memory,
        limit_shutdown_scan=limit_shutdown_scan,
    )["report"]


def format_cleanliness_report(target_journal, analysis_result):
    """Format a concise cleanliness gate report for --assert-clean."""
    lines = [
        "=== POST-RUN CLEANLINESS GATE ===",
        f"Target Journal:           {target_journal}",
        f"Crash Dumps (.dmp):       {len(analysis_result['dmp_files'])}",
        f"Orphaned Transactions:    {len(analysis_result['orphaned_transactions'])}",
        f"Synthetic Exceptions:     {len(analysis_result['synthetic_exceptions'])}",
        f"Rolled-Back Transactions: {len(analysis_result['state']['rolled_back_transactions'])}",
    ]

    if analysis_result["dmp_files"]:
        lines.append("\n[FAIL] Matching crash dump(s):")
        for d in analysis_result["dmp_files"]:
            lines.append(f"  {d}")

    if analysis_result["orphaned_transactions"]:
        lines.append("\n[FAIL] Orphaned open transaction(s) at EOF:")
        for name, line_num in analysis_result["orphaned_transactions"].items():
            lines.append(f"  Line {line_num}: {name}")

    if analysis_result["synthetic_exceptions"]:
        lines.append("\n[FAIL] Unhandled Synthetic/SyntheticTests exception(s):")
        for exc in analysis_result["synthetic_exceptions"]:
            lines.append("-" * 40)
            lines.append(exc)

    status = "CLEAN" if analysis_result["is_clean"] else "DIRTY"
    lines.append(f"\nCleanliness Gate Result: {status}")
    return "\n".join(lines)


def watch_journal(filepath, limit_actions=10):
    """Continuously monitor an active journal, printing new telemetry logs in real time."""
    print("=" * 80)
    print(f"WATCHING ACTIVE JOURNAL: {filepath}")
    print(
        "Monitoring real-time exceptions, warnings, transactions, and memory. Press Ctrl+C to stop."
    )
    print("=" * 80)

    state = create_diagnostics_state()

    try:
        f = open(filepath, "r", encoding="utf-8-sig", errors="ignore")
        f.seek(0, 2)
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

                analyze_line_for_diagnostics(
                    line, line_idx, state, limit_actions=limit_actions
                )
                line_clean = line.strip()

                if not _is_benign_line(line):
                    if _is_exception_header(line, line_clean) or STACK_FRAME_RE.match(
                        line_clean
                    ):
                        print(f"\033[91m[EXCEPTION] Line {line_idx + 1}: {line_clean}\033[0m")
                    elif "DBG_WARN" in line or "error" in line_clean.lower():
                        print(f"\033[93m[WARNING] Line {line_idx + 1}: {line_clean}\033[0m")

                if "RAM Statistics:" in line:
                    print(f"\033[94m[MEMORY] Line {line_idx + 1}: {line_clean}\033[0m")
                elif IN_TRANSACTION_RE.search(line_clean):
                    m = IN_TRANSACTION_RE.search(line_clean)
                    if not CPP_SOURCE_RE.search(m.group(1)):
                        print(
                            f"\033[92m[TRANSACTION START] Line {line_idx + 1}: {line_clean}\033[0m"
                        )
                elif TRANSACTION_SUCCESS_RE.search(line_clean):
                    print(
                        f"\033[92m[TRANSACTION COMMIT] Line {line_idx + 1}: {line_clean}\033[0m"
                    )
                elif (
                    END_OR_ABORT_UNDO_RE.search(line_clean)
                    and "--end of transaction checks" not in line_clean
                ) or EXPLICIT_ROLLBACK_RE.search(line_clean):
                    print(
                        f"\033[91m[TRANSACTION ROLLBACK] Line {line_idx + 1}: {line_clean}\033[0m"
                    )

                line_idx += 1
        except KeyboardInterrupt:
            print("\nStopping watch stream. Telemetry closed.")


def main():
    default_ver = get_current_latest_version()

    parser = argparse.ArgumentParser(description="Revit Journal Telemetry Diagnostics Tool")
    parser.add_argument(
        "--version",
        default=default_ver,
        help=f"Revit version target folder (default: {default_ver})",
    )
    parser.add_argument("--file", help="Explicit Revit journal file path to analyze")
    parser.add_argument(
        "--worker-id",
        help="Deterministic worker ID to locate journal containing SYNTHETIC_WORKER_ID:<WorkerId>",
    )
    parser.add_argument(
        "--run-id",
        help="Deterministic run ID to locate journal containing SYNTHETIC_TEST_RUN:<RunId>",
    )
    parser.add_argument(
        "--known-journals-file",
        help="Pre-launch journal snapshot file for pre-startup crash attribution fallback",
    )
    parser.add_argument(
        "--journals-dir",
        help="Override default Revit Journals directory",
    )
    parser.add_argument(
        "--assert-clean",
        action="store_true",
        help="Exit non-zero if crash dumps, orphaned transactions, or Synthetic exceptions are detected",
    )
    parser.add_argument("--tail", type=int, default=100, help="Number of lines to tail")
    parser.add_argument("--search", help="Query string to search for with context")
    parser.add_argument(
        "--context", type=int, default=15, help="Lines of context around search matches"
    )
    parser.add_argument(
        "--analyze", action="store_true", help="Perform telemetry diagnostics analysis"
    )
    parser.add_argument(
        "--watch",
        action="store_true",
        help="Watch the active journal real-time (polling)",
    )
    parser.add_argument("--output", help="Write results to path")

    parser.add_argument(
        "--limit-exceptions",
        type=int,
        default=10,
        help="Max exceptions to display in analysis (default: 10)",
    )
    parser.add_argument(
        "--limit-warnings",
        type=int,
        default=15,
        help="Max warnings to display in analysis (default: 15)",
    )
    parser.add_argument(
        "--limit-actions",
        type=int,
        default=10,
        help="Max action sequence history size (default: 10)",
    )
    parser.add_argument(
        "--limit-memory",
        type=int,
        default=3,
        help="Max memory logs to display at start/end (default: 3)",
    )
    parser.add_argument(
        "--limit-shutdown-scan",
        type=int,
        default=200,
        help="Trailing lines to scan for clean shutdown (default: 200)",
    )

    args = parser.parse_args()

    target_journal = resolve_journal_path(
        args.version,
        file_path=args.file,
        run_id=args.run_id,
        known_journals_file=args.known_journals_file,
        journals_dir=args.journals_dir,
        worker_id=args.worker_id,
    )
    if not target_journal:
        sys.exit(1)

    if args.watch:
        watch_journal(target_journal, limit_actions=args.limit_actions)
        sys.exit(0)

    lines, encoding = read_journal_lines(target_journal)

    results = [
        f"Telemetry Target: {target_journal}",
        f"Encoding:         {encoding}",
        f"Total Lines:      {len(lines)}",
        "=" * 70,
    ]

    exit_code = 0

    if args.analyze or args.assert_clean:
        analysis_result = analyze_journal_state(
            lines,
            target_journal,
            limit_exceptions=args.limit_exceptions,
            limit_warnings=args.limit_warnings,
            limit_actions=args.limit_actions,
            limit_memory=args.limit_memory,
            limit_shutdown_scan=args.limit_shutdown_scan,
        )
        if args.analyze:
            results.append(analysis_result["report"])
        if args.assert_clean:
            results.append(format_cleanliness_report(target_journal, analysis_result))
            if not analysis_result["is_clean"]:
                exit_code = 1
    elif args.search:
        query_lower = args.search.lower()
        matches = [
            idx for idx, line in enumerate(lines) if query_lower in line.lower()
        ]
        results.append(f"Search Query: '{args.search}'")
        results.append(f"Matches:      {len(matches)} found\n")

        for idx in matches:
            results.append(f"--- Match at Line {idx + 1} ---")
            start = max(0, idx - args.context)
            end = min(len(lines), idx + args.context + 1)
            for i in range(start, end):
                prefix = ">>>" if i == idx else "   "
                results.append(f"{prefix} {i + 1:5d}: {lines[i].rstrip()}")
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

    sys.exit(exit_code)


if __name__ == "__main__":
    main()
