import os
import sys
import argparse
import re

def get_latest_journal(version="2026"):
    """
    Locates the most recently modified Revit journal file in the local AppData directory.
    """
    journal_dir = os.path.expandvars(f"%LOCALAPPDATA%\\Autodesk\\Revit\\Autodesk Revit {version}\\Journals")
    if not os.path.exists(journal_dir):
        print(f"[-] Directory does not exist: {journal_dir}")
        return None
    files = [os.path.join(journal_dir, f) for f in os.listdir(journal_dir) if f.endswith(".txt")]
    if not files:
        print(f"[-] No journal files (.txt) found in: {journal_dir}")
        return None
    latest = max(files, key=os.path.getmtime)
    return latest

def read_journal_lines(filepath):
    """
    Safely reads lines from a journal file, trying various common Revit journal encodings.
    """
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

def tail_journal(lines, num_lines):
    """
    Returns the last N lines of the journal.
    """
    start = max(0, len(lines) - num_lines)
    return lines[start:], start

def search_journal(lines, query, context_lines=15):
    """
    Searches the journal lines for a specific term and returns matches with context.
    """
    query_lower = query.lower()
    matches = []
    for idx, line in enumerate(lines):
        if query_lower in line.lower():
            matches.append(idx)
    return matches

def analyze_journal(lines, filepath):
    """
    Performs comprehensive diagnostic analysis on journal lines to locate crashes,
    exceptions, memory growth, and termination status.
    """
    analysis = []
    analysis.append("=== DIAGNOSTIC ANALYSIS ===")
    
    # 1. Look for Managed Exceptions / Stack Traces
    exceptions = []
    stack_trace_trigger = False
    current_exception = []
    
    # Exceptions often contain System.Exception, Exception, or stack trace patterns like "at System." or "at Synthetic."
    for idx, line in enumerate(lines):
        line_clean = line.strip()
        # Detect C# exception stack trace patterns or explicit Exception type throws
        if "exception" in line_clean.lower() or "at " in line_clean and re.search(r'\b[A-Za-z0-9_]+\.[A-Za-z0-9_.]+\(', line_clean):
            if not stack_trace_trigger:
                stack_trace_trigger = True
                current_exception = [f"Line {idx+1}: {line_clean}"]
            else:
                current_exception.append(line_clean)
        else:
            if stack_trace_trigger:
                exceptions.append("\n".join(current_exception))
                stack_trace_trigger = False
                current_exception = []
                
    if stack_trace_trigger and current_exception:
        exceptions.append("\n".join(current_exception))
        
    analysis.append(f"Managed Exceptions/Traces Found: {len(exceptions)}")
    for exc in exceptions[:10]: # Print first 10
        analysis.append("-" * 40)
        analysis.append(exc)
    if len(exceptions) > 10:
        analysis.append(f"... and {len(exceptions) - 10} more exceptions.")
        
    # 2. Look for DBG_WARN or DBG_INFO with errors
    dbg_warnings = []
    for idx, line in enumerate(lines):
        if "DBG_WARN" in line or "DBG_INFO" in line or "error" in line.lower() or "fail" in line.lower():
            # Exclude some very common harmless lines to avoid noise
            if not any(x in line for x in ["API_SUCCESS", "GetPreferences", "LoadLatestEpoch"]):
                dbg_warnings.append((idx+1, line.strip()))
                
    analysis.append(f"\nPotential Error/Warning Logs Found: {len(dbg_warnings)}")
    for line_num, warning in dbg_warnings[:15]:
        analysis.append(f"  Line {line_num}: {warning}")
    if len(dbg_warnings) > 15:
        analysis.append(f"... and {len(dbg_warnings) - 15} more warnings.")

    # 3. Look for Memory Stats and Growth
    # Revit prints RAM Statistics periodically: e.g. "RAM Statistics:    40348 /    65461      2739=InUse     2992=Peak"
    mem_stats = []
    for idx, line in enumerate(lines):
        if "RAM Statistics:" in line or "Delta VM:" in line:
            mem_stats.append((idx+1, line.strip()))
            
    analysis.append(f"\nMemory Logs Found: {len(mem_stats)}")
    # Print the first few and the last few memory logs to show growth
    if len(mem_stats) > 0:
        analysis.append("Initial Memory Logs:")
        for line_num, stat in mem_stats[:3]:
            analysis.append(f"  Line {line_num}: {stat}")
        if len(mem_stats) > 6:
            analysis.append("...")
            analysis.append("Latest Memory Logs:")
            for line_num, stat in mem_stats[-3:]:
                analysis.append(f"  Line {line_num}: {stat}")
        elif len(mem_stats) > 3:
            for line_num, stat in mem_stats[3:]:
                analysis.append(f"  Line {line_num}: {stat}")
                
    # 4. Determine Shutdown Cleanliness
    # The ricaun.RevitTest adapter closes Revit programmatically — no ribbon Quit is recorded.
    # Instead look for crash indicators:
    #   a) An abrupt end mid-transaction (no 'Destroy Display Manager' in last 200 lines)
    #   b) A matching .dmp file in the Journals directory
    last_200 = lines[-200:] if len(lines) >= 200 else lines
    
    destroy_display_found = any("Destroy Display Manager" in ln for ln in last_200)
    api_success_unregister = any("API_SUCCESS" in ln and "Unregistering" in ln for ln in last_200)
    
    # Check for a crash dump file matching this journal
    crash_detected = False
    journal_dir = os.path.dirname(filepath)
    journal_basename = os.path.splitext(os.path.basename(filepath))[0]  # e.g. "journal.0388"
    dmp_pattern = os.path.join(journal_dir, journal_basename + "*.dmp")
    import glob as _glob
    dmp_files = _glob.glob(dmp_pattern)
    if dmp_files:
        crash_detected = True
        analysis.append(f"\n[CRITICAL] Crash dump file(s) found for this journal session:")
        for d in dmp_files:
            analysis.append(f"  {d}")
    
    if destroy_display_found and not crash_detected:
        analysis.append(f"\n[OK] Shutdown appears CLEAN: 'Destroy Display Manager' found in last 200 lines.")
        if api_success_unregister:
            analysis.append("  Addins unregistered cleanly (API_SUCCESS Unregistering events found).")
    elif crash_detected:
        analysis.append(f"\n[CRASH] Shutdown was ABNORMAL: crash dump file detected.")
    else:
        analysis.append("\n[WARNING] Shutdown status UNCERTAIN: No 'Destroy Display Manager' and no crash dump.")
        analysis.append("  Revit may have been killed externally (e.g. taskkill) or the journal is still open.")
    
    return "\n".join(analysis)

def main():
    parser = argparse.ArgumentParser(description="Revit Journal Tailing and Search Utility")
    parser.add_argument("--version", default="2026", help="Revit version folder to target (default: 2026)")
    parser.add_argument("--tail", type=int, default=100, help="Number of lines to tail from the end of the journal")
    parser.add_argument("--search", help="Search query (case-insensitive) to run against the journal")
    parser.add_argument("--context", type=int, default=15, help="Number of lines of context around search matches")
    parser.add_argument("--analyze", action="store_true", help="Perform comprehensive diagnostics analysis on the latest journal")
    parser.add_argument("--output", help="Path to write the results (if omitted, writes to stdout)")

    args = parser.parse_args()

    latest_journal = get_latest_journal(args.version)
    if not latest_journal:
        sys.exit(1)

    lines, encoding = read_journal_lines(latest_journal)
    
    results = []
    results.append(f"Journal File: {latest_journal}")
    results.append(f"Encoding:     {encoding}")
    results.append(f"Total Lines:  {len(lines)}")
    results.append("=" * 60)

    if args.analyze:
        analysis_text = analyze_journal(lines, latest_journal)
        results.append(analysis_text)
    elif args.search:
        matches = search_journal(lines, args.search, args.context)
        results.append(f"Search Term:  '{args.search}'")
        results.append(f"Matches:      {len(matches)} found\n")
        
        for idx in matches:
            results.append(f"--- Match at Line {idx+1} ---")
            start = max(0, idx - args.context)
            end = min(len(lines), idx + args.context + 1)
            for i in range(start, end):
                prefix = ">>>" if i == idx else "   "
                results.append(f"{prefix} {i+1:5d}: {lines[i].rstrip()}")
            results.append("-" * 60)
    else:
        results.append(f"Tailing last {args.tail} lines:\n")
        tail_lines, start_idx = tail_journal(lines, args.tail)
        for idx, line in enumerate(tail_lines):
            results.append(f" {start_idx + idx + 1:5d}: {line.rstrip()}")

    output_text = "\n".join(results)
    
    if args.output:
        try:
            with open(args.output, "w", encoding="utf-8") as f:
                f.write(output_text)
            print(f"[+] Results written to: {args.output}")
        except Exception as e:
            print(f"[-] Failed to write output file: {e}")
            print(output_text)
    else:
        print(output_text)

if __name__ == "__main__":
    main()
