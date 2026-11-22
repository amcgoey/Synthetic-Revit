import os
import sys
import re

# Add the QA plugin scripts directory to the path so we can import its modules
workspace_path = os.getcwd()
qa_scripts_dir = os.path.join(workspace_path, ".agents", "plugins", "RevitQualityAssurance", "scripts")
sys.path.append(qa_scripts_dir)

import test_executor
import revit_journal_tool

def run_test(revit_version="2025"):
    print(f"[TEST RUNNER] Starting integration test for Audit & Purge on Revit {revit_version}...")
    
    # 1. Run the test suite via the test_executor from the QA plugin
    test_executor.run_test_suite("", revit_version)
    
    # 2. Locate the latest journal using the revit_journal_tool from the QA plugin
    latest_journal = revit_journal_tool.get_latest_journal(revit_version)
    if not latest_journal:
        print("[-] Error: Could not locate the latest Revit journal file.")
        sys.exit(1)
        
    print(f"[TEST RUNNER] Reading results from journal: {latest_journal}")
    lines, encoding = revit_journal_tool.read_journal_lines(latest_journal)
    
    # 3. Parse the journal data from the APIStringStringMapJournalData block
    metrics = {}
    in_block = False
    block_lines = []
    
    for line in lines:
        clean = line.strip()
        if 'APIStringStringMapJournalData' in clean:
            in_block = True
            block_lines.append(clean)
            continue
        if in_block:
            block_lines.append(clean)
            # If the line does not end with line continuation character '_', the block has ended.
            if not clean.endswith('_'):
                in_block = False
                
    # Now parse all accumulated block lines
    # Combine lines by stripping trailing underscores
    combined = ""
    for bl in block_lines:
        c_line = bl
        if c_line.endswith('_'):
            c_line = c_line[:-1].strip()
        combined += " " + c_line
        
    # Find all matches of double-quoted strings
    tokens = re.findall(r'"([^"]*)"', combined)
    if len(tokens) > 1:
        for i in range(1, len(tokens), 2):
            if i + 1 < len(tokens):
                key = tokens[i]
                val = tokens[i+1]
                metrics[key] = val

    print(f"[TEST RUNNER] Extracted metrics: {metrics}")
    
    # 4. Assertions
    size_before_str = metrics.get("Test_Family1_SizeBefore")
    size_after_str = metrics.get("Test_Family1_SizeAfter")
    param_value = metrics.get("Test_Family1_ParamValue")
    
    if not size_before_str or not size_after_str or not param_value:
        print("[-] FAIL: Missing required verification keys in the journal file.")
        sys.exit(1)
        
    size_before = int(size_before_str)
    size_after = int(size_after_str)
    
    print(f"[TEST RUNNER] Size Before: {size_before} bytes")
    print(f"[TEST RUNNER] Size After:  {size_after} bytes")
    print(f"[TEST RUNNER] Param Value: {param_value}")
    
    passed = True
    if size_after >= size_before:
        print("[-] FAIL: Family size did not decrease after purge.")
        passed = False
    else:
        print("[+] PASS: Family size successfully decreased after purge.")
        
    if param_value != "PRESERVE_TEST":
        print(f"[-] FAIL: Comments parameter was overwritten (Value: {param_value}, Expected: PRESERVE_TEST).")
        passed = False
    else:
        print("[+] PASS: Comments parameter override was preserved.")
        
    if passed:
        print("[+] INTEGRATION TEST PASSED SUCCESSFULLY!")
        sys.exit(0)
    else:
        print("[-] INTEGRATION TEST FAILED.")
        sys.exit(1)

if __name__ == "__main__":
    version = sys.argv[1] if len(sys.argv) > 1 else "2025"
    run_test(version)
