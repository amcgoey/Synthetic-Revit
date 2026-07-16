import os
import sys
import subprocess
import glob
import xml.etree.ElementTree as ET
import re
import winreg

# Import helper scripts for isolating Revit environment configurations
import guid_whitelister

class TempConfigureRevitAddins:
    def __init__(self, version):
        self.version = version
        self.added_guids = []
        
    def whitelist_existing_addins(self):
        new_guids = guid_whitelister.whitelist_existing_addins(self.version)
        self.added_guids.extend(new_guids)
        
    def __enter__(self):
        self.whitelist_existing_addins()
        return self
        
    def __exit__(self, exc_type, exc_val, exc_tb):
        if self.added_guids:
            guid_whitelister.cleanup_whitelisted_registry_entries(self.version, self.added_guids)


def find_msbuild():
    paths = [
        r"C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe",
        r"C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe",
        r"C:\Program Files\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\MSBuild.exe",
        r"C:\Program Files\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe",
    ]
    for p in paths:
        if os.path.exists(p):
            return p
    return "msbuild"  # fallback to PATH

def run_test_suite(target_class, target_version):
    workspace_path = os.getcwd()
    solution_path = os.path.join(workspace_path, "src", "Synthetic.sln")
    
    # 1. Build the entire solution first to ensure compilation and restore
    print("[TESTS] Restoring solution NuGet packages...")
    try:
        # Run restore letting output print to console
        subprocess.run(
            ["dotnet", "restore", solution_path],
            check=True
        )
    except subprocess.CalledProcessError as e:
        print(f"[ERROR] NuGet restore failed: {e}")
        sys.exit(1)

    msbuild_path = find_msbuild()
    print(f"[TESTS] Building solution with MSBuild: {msbuild_path}...")
    try:
        # Run build letting output print to console
        subprocess.run(
            [msbuild_path, solution_path, "/t:Build", "/p:Configuration=Debug", "/p:Platform=Any CPU"],
            check=True
        )
        print("[TESTS] Solution build succeeded.")
    except subprocess.CalledProcessError as e:
        print(f"[ERROR] Solution build failed: {e}")
        sys.exit(1)

    # 2. Discover test projects dynamically
    test_dir = os.path.join(workspace_path, "tests")
    test_projects = {}
    if os.path.exists(test_dir):
        for item in os.listdir(test_dir):
            item_path = os.path.join(test_dir, item)
            if os.path.isdir(item_path) and item.startswith("SyntheticTests") and not item.endswith(".Shared"):
                ver = item.replace("SyntheticTests", "")
                if ver.startswith("."):
                    ver = ver[1:]
                csproj_files = glob.glob(os.path.join(item_path, "*.csproj"))
                if csproj_files:
                    test_projects[ver] = csproj_files[0]

    # Filter by version if specified
    run_projects = {}
    if target_version:
        if target_version in test_projects:
            run_projects[target_version] = test_projects[target_version]
        else:
            print(f"[ERROR] Target version '{target_version}' test project not found in {list(test_projects.keys())}")
            sys.exit(1)
    else:
        run_projects = test_projects

    if not run_projects:
        print("[WARN] No test projects discovered to execute.")
        return

    # 3. Execute dotnet test on each target project
    results_dir = os.path.join(test_dir, "TestResults")
    # Clean old results
    if os.path.exists(results_dir):
        for f in glob.glob(os.path.join(results_dir, "results_*.trx")):
            try:
                os.remove(f)
            except Exception:
                pass
    else:
        os.makedirs(results_dir, exist_ok=True)

    import time
    for ver, csproj_path in run_projects.items():
        is_logic = (ver.lower() == "logic")
        if is_logic:
            print(f"[TESTS] Executing headless NUnit tests for logic...")
        else:
            print(f"[TESTS] Executing dotnet test for Revit {ver}...")
        log_name = f"results_{ver}.trx"
        cmd = [
            "dotnet", "test", csproj_path,
            "--no-build",
            "--logger", f"trx;LogFileName={log_name}",
            "--results-directory", results_dir,
            "/p:Platform=x64"
        ]
        if target_class:
            cmd.extend(["--filter", f"FullyQualifiedName~{target_class}"])
        
        out_file_path = os.path.join(results_dir, f"output_{ver}.log")
        out_f = open(out_file_path, "w", encoding="utf-8")
        
        if is_logic:
            # Run headless tests synchronously in the foreground
            subprocess.run(cmd, stdout=out_f, stderr=out_f)
        else:
            with TempConfigureRevitAddins(ver) as configurer:
                # Start dotnet test in the background
                proc = subprocess.Popen(cmd, stdout=out_f, stderr=out_f)
                
                # Poll for new add-ins and wait for dotnet test to exit
                start_time = time.time()
                timeout = 660  # 11 min: matches 600s ricaun.RevitTest.Timeout + 60s Revit boot buffer
                print(f"[TESTS] Waiting for Revit {ver} test execution to complete (timeout: {timeout}s)...")
                
                while time.time() - start_time < timeout:
                    configurer.whitelist_existing_addins()
                    
                    # Check if dotnet test has finished
                    if proc.poll() is not None:
                        break
                    time.sleep(0.5)
                
                # If still running after timeout, kill Revit
                if proc.poll() is None:
                    print(f"[TESTS] Test execution timed out after {timeout} seconds. Killing Revit...")
                    subprocess.run(["taskkill", "/F", "/IM", "Revit.exe"], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
                    try:
                        proc.wait(timeout=10)
                    except subprocess.TimeoutExpired:
                        proc.kill()
        
        out_f.close()

    # 4. Ingest and parse TRX files
    failing_tests = []
    total_run = 0
    total_failed = 0
    total_passed = 0

    ns = {'ns': 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
    trx_files = glob.glob(os.path.join(results_dir, "results_*.trx"))

    for trx_path in trx_files:
        try:
            tree = ET.parse(trx_path)
            root = tree.getroot()
            
            # Extract basic counts
            counters = root.find('.//ns:ResultSummary/ns:Counters', ns)
            if counters is not None:
                total_run += int(counters.attrib.get('total', 0))
                total_failed += int(counters.attrib.get('failed', 0))
                total_passed += int(counters.attrib.get('passed', 0))

            for result in root.findall('.//ns:UnitTestResult', ns):
                outcome = result.attrib.get('outcome')
                if outcome == 'Failed':
                    test_name = result.attrib.get('testName')
                    error_message = ""
                    stack_trace = ""
                    
                    error_info = result.find('.//ns:ErrorInfo', ns)
                    if error_info is not None:
                        msg_el = error_info.find('ns:Message', ns)
                        if msg_el is not None:
                            error_message = msg_el.text or ""
                        stack_el = error_info.find('ns:StackTrace', ns)
                        if stack_el is not None:
                            stack_trace = stack_el.text or ""
                    
                    # Try to extract version from trx file name
                    base_name = os.path.basename(trx_path)
                    ver_str = base_name.replace("results_", "").replace(".trx", "")
                    
                    failing_tests.append({
                        'name': f"[{ver_str}] {test_name}",
                        'message': error_message.strip(),
                        'stack_trace': stack_trace.strip()
                    })
        except Exception as e:
            print(f"[ERROR] Failed to parse TRX file '{trx_path}': {e}")

    # 5. Output Markdown formatted summary
    print("\n# Test Execution Summary\n")
    if failing_tests:
        print(f"## [FAIL] Failing Tests ({len(failing_tests)} / {total_run})\n")
        for idx, t in enumerate(failing_tests, 1):
            print(f"### {idx}. Test Name: `{t['name']}`")
            print("**Error Message:**")
            print("```")
            print(t['message'])
            print("```\n")
            print("**Stack Trace:**")
            print("```")
            print(t['stack_trace'])
            print("```\n")
            print("---")
        sys.exit(1)
    else:
        if total_run > 0:
            print(f"## All tests passed successfully! [PASS] (Total: {total_passed})")
        else:
            print("## No tests were executed. [WARN]")
        sys.exit(0)

if __name__ == "__main__":
    import argparse as _ap
    _parser = _ap.ArgumentParser(description="Run Synthetic test suite.")
    _parser.add_argument("--filter", dest="target_class", default="",
                         help="FullyQualifiedName filter (class or method name fragment)")
    _parser.add_argument("--version", dest="target_version", default="",
                         help="Revit version to target (e.g. 2026). Omit to run all.")
    # Legacy positional support: test_executor.py [class] [version]
    _parser.add_argument("pos_class", nargs="?", default="")
    _parser.add_argument("pos_version", nargs="?", default="")
    _args = _parser.parse_args()

    target_class   = (_args.target_class   or _args.pos_class   or "").strip()
    target_version = (_args.target_version or _args.pos_version or "").strip()

    # Treat legacy sentinel value as blank
    if target_class == "DefaultAlignmentTests":
        target_class = ""

    run_test_suite(target_class, target_version)