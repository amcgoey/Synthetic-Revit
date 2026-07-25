import os
import sys
import subprocess
import glob
import xml.etree.ElementTree as ET
import re
import winreg
import json
import shutil
import guid_whitelister

class TempDisableThirdPartyAddins:
    def __init__(self, version):
        self.version = version
        appdata = os.environ.get("AppData", os.path.expanduser("~\\AppData\\Roaming"))
        self.json_path = os.path.join(appdata, "Autodesk", "Revit", f"Autodesk Revit {version}", "AddinsData", "AddInsSettings.json")
        self.backup_path = self.json_path + ".bak"

    def restore_addins(self):
        if os.path.exists(self.backup_path):
            try:
                if os.path.exists(self.json_path):
                    os.remove(self.json_path)
                shutil.move(self.backup_path, self.json_path)
                print(f"[TESTS] Restored native AddInsSettings.json for Revit {self.version}")
            except Exception as e:
                print(f"[WARN] Failed to restore AddInsSettings.json backup for Revit {self.version}: {e}")

    def disable_addins(self):
        if not os.path.exists(self.json_path):
            return
        
        try:
            shutil.copy2(self.json_path, self.backup_path)
            
            with open(self.json_path, "r", encoding="utf-8") as f:
                data = json.load(f)
                
            modified = False
            if "AddInItemSettings" in data:
                for item in data["AddInItemSettings"]:
                    name = item.get("Name", "")
                    vendor = item.get("Vendor", "")
                    disabled = item.get("Disabled", False)
                    
                    if not disabled:
                        if vendor == "ADSK" or vendor == "ricaun" or vendor == "net.amcgoey" or name == "Synthetic" or name == "ricaun.RevitTest.Application":
                            continue
                        
                        item["Disabled"] = True
                        modified = True
                        print(f"[TESTS] Temporarily disabled third-party add-in: {name} (Vendor: {vendor})")
                        
            if modified:
                with open(self.json_path, "w", encoding="utf-8") as f:
                    json.dump(data, f)
        except Exception as e:
            print(f"[WARN] Failed to modify AddInsSettings.json for Revit {self.version}: {e}")
            if os.path.exists(self.backup_path):
                try:
                    os.remove(self.backup_path)
                except Exception:
                    pass

    def __enter__(self):
        self.restore_addins()
        self.disable_addins()
        return self

    def __exit__(self, exc_type, exc_val, exc_tb):
        self.restore_addins()

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
    
    os.environ["SYNTHETIC_PROJECT_ROOT"] = workspace_path

    # Self-heal any previously disabled add-ins across all versions on start
    for v in ["2023", "2024", "2025", "2026"]:
        try:
            TempDisableThirdPartyAddins(v).restore_addins()
        except Exception:
            pass
    
    # 1. Restore solution NuGet packages
    print("[TESTS] Restoring solution NuGet packages...")
    try:
        subprocess.run(
            ["dotnet", "restore", solution_path],
            check=True
        )
    except subprocess.CalledProcessError as e:
        print(f"[ERROR] NuGet restore failed: {e}")
        sys.exit(1)

    msbuild_path = find_msbuild()

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
            print(f"[TESTS] Building logic test project with MSBuild: {msbuild_path}...")
        else:
            print(f"[TESTS] Building test project for Revit {ver} with MSBuild: {msbuild_path}...")
            
        try:
            subprocess.run(
                [msbuild_path, csproj_path, "/t:Build", "/p:Configuration=Debug", "/p:Platform=x64"],
                check=True
            )
            print(f"[TESTS] Build succeeded for {ver}.")
        except subprocess.CalledProcessError as e:
            print(f"[ERROR] Build failed for {ver}: {e}")
            sys.exit(1)

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
            with TempDisableThirdPartyAddins(ver) as isolator:
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

