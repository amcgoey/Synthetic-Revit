import os
import sys
import subprocess
import glob
import xml.etree.ElementTree as ET
import re
import winreg
import json
import shutil
import tempfile
import time
import uuid

_SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
if _SCRIPT_DIR not in sys.path:
    sys.path.insert(0, _SCRIPT_DIR)
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


RUN_DIR_PATTERN = re.compile(r"^\d{8}_\d{6}_\d+_[0-9a-fA-F]+$")
MAX_TEST_RESULT_RUNS = 10


def find_msbuild():
    paths = [
        r"C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe",
        r"C:\Program Files\Microsoft Visual Studio\18\BuildTools\MSBuild\Current\Bin\amd64\MSBuild.exe",
        r"C:\Program Files (x86)\Microsoft Visual Studio\18\BuildTools\MSBuild\Current\Bin\amd64\MSBuild.exe",
        r"C:\Program Files (x86)\Microsoft Visual Studio\18\BuildTools\MSBuild\Current\Bin\MSBuild.exe",
        r"C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe",
        r"C:\Program Files\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\MSBuild.exe",
        r"C:\Program Files\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe",
    ]
    for p in paths:
        if os.path.exists(p):
            return p
    return "msbuild"  # fallback to PATH


def parse_sdk_version(version_str):
    """Parse a .NET SDK folder name into a semantically comparable tuple, or None if invalid."""
    if not version_str:
        return None
    cleaned = version_str.strip()
    base_part, sep, prerelease = cleaned.partition("-")
    parts = base_part.split(".")
    if len(parts) < 3 or any(not p.isdigit() for p in parts):
        return None
    numeric_tuple = tuple(int(p) for p in parts)
    is_stable = 0 if sep else 1
    pre_tokens = []
    if prerelease:
        for seg in prerelease.split("."):
            if seg.isdigit():
                pre_tokens.append((0, int(seg)))
            else:
                pre_tokens.append((1, seg.lower()))
    return (numeric_tuple, is_stable, tuple(pre_tokens))


def get_default_dotnet_sdk_roots(env=None):
    env = os.environ if env is None else env
    program_files = env.get("ProgramW6432") or env.get("ProgramFiles") or r"C:\Program Files"
    local_appdata = env.get("LOCALAPPDATA", os.path.expanduser("~\\AppData\\Local"))
    return [
        os.path.join(program_files, "dotnet", "sdk"),
        os.path.join(local_appdata, "Microsoft", "dotnet", "sdk"),
    ]


def find_best_dotnet_sdk(sdk_roots=None):
    """Return metadata for the highest semantic .NET SDK across candidate sdk roots."""
    roots = get_default_dotnet_sdk_roots() if sdk_roots is None else sdk_roots
    best = None
    best_key = None

    for sdk_root in roots:
        if not sdk_root or not os.path.isdir(sdk_root):
            continue
        try:
            entries = os.listdir(sdk_root)
        except OSError:
            continue

        for entry in entries:
            sdk_path = os.path.join(sdk_root, entry)
            if not os.path.isdir(sdk_path):
                continue
            parsed = parse_sdk_version(entry)
            if parsed is None:
                continue
            if best_key is None or parsed > best_key:
                best_key = parsed
                best = {
                    "version": entry,
                    "sdk_path": sdk_path,
                    "dotnet_root": os.path.dirname(sdk_root),
                }

    return best


def configure_dotnet_environment(env=None, sdk_roots=None):
    env = os.environ if env is None else env
    roots = get_default_dotnet_sdk_roots(env) if sdk_roots is None else sdk_roots
    best = find_best_dotnet_sdk(sdk_roots=roots)
    if best:
        dotnet_root = best["dotnet_root"]
        env["DOTNET_ROOT"] = dotnet_root
        current_path = env.get("PATH", "")
        path_parts = current_path.split(os.pathsep) if current_path else []
        if not path_parts or os.path.normcase(path_parts[0]) != os.path.normcase(dotnet_root):
            env["PATH"] = dotnet_root + (os.pathsep + current_path if current_path else "")

        sdks_path = os.path.join(best["sdk_path"], "Sdks")
        if os.path.isdir(sdks_path):
            env["MSBuildSdksPath"] = sdks_path
            env["MSBuildEnableWorkloadResolver"] = "false"

    system_net48 = r"C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8"
    if not os.path.exists(system_net48):
        nuget_net48_root = os.path.join(
            os.path.expanduser("~"),
            ".nuget",
            "packages",
            "microsoft.netframework.referenceassemblies.net48",
        )
        if os.path.exists(nuget_net48_root):
            versions = sorted(
                [d for d in os.listdir(nuget_net48_root) if os.path.isdir(os.path.join(nuget_net48_root, d))]
            )
            if versions:
                build_dir = os.path.join(nuget_net48_root, versions[-1], "build") + os.sep
                if os.path.exists(build_dir):
                    env["TargetFrameworkRootPath"] = build_dir

    return best


def discover_test_projects(test_dir):
    """Discover SyntheticTests* project directories dynamically, mapping version key -> csproj path."""
    test_projects = {}
    if not os.path.exists(test_dir):
        return test_projects

    for item in sorted(os.listdir(test_dir)):
        item_path = os.path.join(test_dir, item)
        if os.path.isdir(item_path) and item.startswith("SyntheticTests") and not item.endswith(".Shared"):
            ver = item.replace("SyntheticTests", "")
            if ver.startswith("."):
                ver = ver[1:]
            csproj_files = sorted(glob.glob(os.path.join(item_path, "*.csproj")))
            if csproj_files:
                test_projects[ver] = csproj_files[0]

    return test_projects


def _strip_comments_and_strings(line, in_block_comment):
    """Strip C# comments from a line, returning (code_without_comments, new_in_block_comment)."""
    out = []
    i = 0
    n = len(line)
    in_string = False
    while i < n:
        ch = line[i]
        nxt = line[i + 1] if i + 1 < n else ""
        if in_block_comment:
            if ch == "*" and nxt == "/":
                in_block_comment = False
                i += 2
            else:
                i += 1
            continue
        if in_string:
            out.append(ch)
            if ch == "\\" and i + 1 < n:
                out.append(nxt)
                i += 2
                continue
            if ch == '"':
                in_string = False
            i += 1
            continue
        if ch == "/" and nxt == "/":
            break
        if ch == "/" and nxt == "*":
            in_block_comment = True
            i += 2
            continue
        if ch == '"':
            in_string = True
        out.append(ch)
        i += 1
    return "".join(out), in_block_comment


def _count_braces(code_line):
    """Count opening and closing braces outside string/char literals."""
    opens = 0
    closes = 0
    in_string = False
    in_char = False
    i = 0
    n = len(code_line)
    while i < n:
        ch = code_line[i]
        if in_string:
            if ch == "\\":
                i += 2
                continue
            if ch == '"':
                in_string = False
        elif in_char:
            if ch == "\\":
                i += 2
                continue
            if ch == "'":
                in_char = False
        else:
            if ch == '"':
                in_string = True
            elif ch == "'":
                in_char = True
            elif ch == "{":
                opens += 1
            elif ch == "}":
                closes += 1
        i += 1
    return opens, closes


def find_tests_by_category(tests_dir, category, version=None):
    """Scan C# test files under tests_dir for NUnit [Category] attributes at class or method level."""
    if not category or not os.path.isdir(tests_dir):
        return []

    target_cat = category.strip().casefold()
    source_dirs = []
    shared_dir = os.path.join(tests_dir, "SyntheticTests.Shared")
    if os.path.isdir(shared_dir):
        source_dirs.append(shared_dir)

    if version:
        ver_dir = os.path.join(tests_dir, f"SyntheticTests{version}")
        if os.path.isdir(ver_dir):
            source_dirs.append(ver_dir)
    else:
        for entry in sorted(os.listdir(tests_dir)):
            if entry.startswith("SyntheticTests") and entry not in ("SyntheticTests.Shared", "SyntheticTests.Logic"):
                full_entry = os.path.join(tests_dir, entry)
                if os.path.isdir(full_entry):
                    source_dirs.append(full_entry)

    cs_files = []
    excluded_sheet_index = os.path.normcase(os.path.join(shared_dir, "Modules", "SheetIndex"))
    for src_dir in source_dirs:
        for root, dirs, files in os.walk(src_dir):
            dirs[:] = [d for d in dirs if d.lower() not in ("bin", "obj")]
            if os.path.normcase(root).startswith(excluded_sheet_index):
                continue
            for fn in sorted(files):
                if fn.endswith(".cs"):
                    cs_files.append(os.path.join(root, fn))

    matches = set()
    cat_attr_re = re.compile(r'\bCategory\s*\(\s*"([^"]+)"\s*\)')
    class_re = re.compile(r'\bclass\s+([A-Za-z_][A-Za-z0-9_]*)\b')
    method_re = re.compile(
        r'(?:public|internal|protected|private)\s+(?:static\s+|async\s+|virtual\s+|override\s+|new\s+|sealed\s+)*'
        r'[\w<>\[\],\.\?\s]+\s+([A-Za-z_][A-Za-z0-9_]*)\s*\('
    )

    for cs_path in cs_files:
        try:
            with open(cs_path, "r", encoding="utf-8-sig") as f:
                lines = f.readlines()
        except OSError:
            continue

        in_block_comment = False
        brace_depth = 0
        class_stack = []
        pending_class = None
        pending_categories = set()

        for raw_line in lines:
            code, in_block_comment = _strip_comments_and_strings(raw_line, in_block_comment)
            stripped = code.strip()
            if not stripped:
                continue

            for cat_match in cat_attr_re.findall(stripped):
                pending_categories.add(cat_match.strip().casefold())

            class_match = class_re.search(stripped)
            if class_match:
                cls_name = class_match.group(1)
                if target_cat in pending_categories:
                    matches.add(cls_name)
                pending_categories.clear()
                pending_class = cls_name
            elif class_stack and pending_categories and brace_depth == class_stack[-1][1]:
                method_match = method_re.search(stripped)
                if method_match:
                    method_name = method_match.group(1)
                    if target_cat in pending_categories:
                        matches.add(f"{class_stack[-1][0]}.{method_name}")
                    pending_categories.clear()
                elif ";" in stripped and "[" not in stripped:
                    pending_categories.clear()

            opens, closes = _count_braces(stripped)
            if pending_class is not None and opens > 0:
                class_stack.append((pending_class, brace_depth + 1))
                pending_class = None

            brace_depth = max(0, brace_depth + opens - closes)
            while class_stack and brace_depth < class_stack[-1][1]:
                class_stack.pop()

    return sorted(matches)


def build_test_filter(is_logic, target_class="", category="", tests_dir="", version=None):
    """Build VSTest --filter expression and indicate whether category yielded zero matches."""
    target_class = (target_class or "").strip()
    category = (category or "").strip()

    if is_logic:
        if category and target_class:
            return f"TestCategory={category}&(FullyQualifiedName~{target_class})", False
        if category:
            return f"TestCategory={category}", False
        if target_class:
            return f"FullyQualifiedName~{target_class}", False
        return "", False

    if category:
        matched_names = find_tests_by_category(tests_dir, category, version=version)
        if not matched_names:
            return "", True
        cat_expr = "(" + "|".join(f"FullyQualifiedName~{name}" for name in matched_names) + ")"
        if target_class:
            return f"{cat_expr}&(FullyQualifiedName~{target_class})", False
        return cat_expr, False

    if target_class:
        return f"FullyQualifiedName~{target_class}", False

    return "", False


def probe_worker_pool(workspace_path, env=None):
    """Probe for optional RevitWorkerPool command via env var or tools/RevitWorkerPool."""
    env = os.environ if env is None else env
    env_cmd = env.get("SYNTHETIC_WORKER_POOL_CMD", "").strip()
    if env_cmd:
        return env_cmd

    tools_dir = os.path.join(workspace_path, "tools")
    candidates = [
        os.path.join(tools_dir, "RevitWorkerPool", "RevitWorkerPool.exe"),
        os.path.join(tools_dir, "RevitWorkerPool", "RevitWorkerPool.py"),
        os.path.join(tools_dir, "RevitWorkerPool.exe"),
        os.path.join(tools_dir, "RevitWorkerPool.py"),
        os.path.join(tools_dir, "RevitWorkerPool.bat"),
        os.path.join(tools_dir, "RevitWorkerPool.cmd"),
        os.path.join(tools_dir, "RevitWorkerPool"),
    ]
    for candidate in candidates:
        if os.path.isfile(candidate):
            return candidate
    return None


def resolve_worker_mode(no_pool, workspace_path, env=None):
    """Decide worker execution mode ('pool' vs 'cold') with clear diagnostic message."""
    if no_pool:
        return "cold", None, "[TESTS] --no-pool specified; using isolated cold Revit workers."
    pool_cmd = probe_worker_pool(workspace_path, env=env)
    if pool_cmd:
        return "pool", pool_cmd, f"[TESTS] Detected RevitWorkerPool at: {pool_cmd}"
    return (
        "cold",
        None,
        "[TESTS] No RevitWorkerPool found (SYNTHETIC_WORKER_POOL_CMD / tools/RevitWorkerPool); "
        "falling back to isolated cold Revit workers.",
    )


def generate_run_id():
    return f"{time.strftime('%Y%m%d_%H%M%S')}_{os.getpid()}_{uuid.uuid4().hex[:8]}"


def rotate_test_results(results_root, keep=MAX_TEST_RESULT_RUNS):
    """Keep only the latest `keep` TestResults/<RunId>/ directories, skipping non-run entries."""
    if not os.path.isdir(results_root):
        return []

    run_dirs = []
    for entry in os.listdir(results_root):
        full_path = os.path.join(results_root, entry)
        if not os.path.isdir(full_path):
            continue
        if not RUN_DIR_PATTERN.match(entry):
            continue
        try:
            mtime = os.path.getmtime(full_path)
        except OSError:
            mtime = 0.0
        run_dirs.append((mtime, entry, full_path))

    run_dirs.sort(key=lambda item: (item[0], item[1]), reverse=True)
    removed = []
    for _, entry, full_path in run_dirs[keep:]:
        shutil.rmtree(full_path, ignore_errors=True)
        removed.append(entry)
    return removed


def run_test_suite(target_class="", target_version="", category="", no_pool=False):
    workspace_path = os.getcwd()
    os.environ["SYNTHETIC_PROJECT_ROOT"] = workspace_path
    sdk_info = configure_dotnet_environment()
    msbuild_path = find_msbuild()

    # 1. Discover test projects dynamically
    test_dir = os.path.join(workspace_path, "tests")
    test_projects = discover_test_projects(test_dir)

    # Self-heal any previously disabled add-ins across discovered Revit versions on start
    for v in [ver for ver in test_projects if ver.isdigit()]:
        try:
            TempDisableThirdPartyAddins(v).restore_addins()
        except Exception:
            pass

    # Filter by version if specified (case-insensitive fallback)
    run_projects = {}
    if target_version:
        matched_key = None
        if target_version in test_projects:
            matched_key = target_version
        else:
            for key in test_projects:
                if key.casefold() == target_version.casefold():
                    matched_key = key
                    break
        if matched_key:
            run_projects[matched_key] = test_projects[matched_key]
        else:
            print(f"[ERROR] Target version '{target_version}' test project not found in {list(test_projects.keys())}")
            sys.exit(1)
    else:
        run_projects = test_projects

    if not run_projects:
        print("[WARN] No test projects discovered to execute.")
        return

    # 2. Prepare per-run isolated build and results directories
    run_id = generate_run_id()
    run_build_root = os.path.join(tempfile.gettempdir(), "SyntheticBuilds", run_id)
    results_root = os.path.join(test_dir, "TestResults")
    run_results_dir = os.path.join(results_root, run_id)

    has_revit_targets = any(ver.lower() != "logic" for ver in run_projects)
    if has_revit_targets:
        _, _, pool_msg = resolve_worker_mode(no_pool, workspace_path)
        print(pool_msg)

    dotnet_exe = "dotnet"
    if sdk_info:
        candidate_dotnet = os.path.join(sdk_info["dotnet_root"], "dotnet.exe")
        if os.path.exists(candidate_dotnet):
            dotnet_exe = candidate_dotnet

    executed_any = False
    had_execution_error = False

    for ver, csproj_path in run_projects.items():
        is_logic = ver.lower() == "logic"
        filter_expr, matched_zero = build_test_filter(
            is_logic=is_logic,
            target_class=target_class,
            category=category,
            tests_dir=test_dir,
            version=None if is_logic else ver,
        )

        if matched_zero:
            print(f"[TESTS] 0 matching tests for {ver} (category: {category}).")
            continue

        executed_any = True
        os.makedirs(run_build_root, exist_ok=True)
        os.makedirs(run_results_dir, exist_ok=True)

        if is_logic:
            print(f"[TESTS] Building logic test project with MSBuild ({run_id}): {msbuild_path}...")
        else:
            print(f"[TESTS] Building test project for Revit {ver} with MSBuild ({run_id}): {msbuild_path}...")

        build_cmd = [
            msbuild_path,
            csproj_path,
            "/t:Build",
            "/restore",
            "/p:Configuration=Debug",
            "/p:Platform=x64",
            f"/p:SyntheticIsolatedBuildRoot={run_build_root}",
            f"/p:SyntheticRunId={run_id}",
            "/v:minimal",
            "/clp:ErrorsOnly;Summary",
        ]
        try:
            subprocess.run(build_cmd, check=True)
            print(f"[TESTS] Build succeeded for {ver}.")
        except subprocess.CalledProcessError as e:
            print(f"[ERROR] Build failed for {ver}: {e}")
            print(f"[TESTS] Preserving failed isolated build root for inspection: {run_build_root}")
            rotate_test_results(results_root)
            sys.exit(1)

        if is_logic:
            print("[TESTS] Executing headless NUnit tests for logic...")
        else:
            print(f"[TESTS] Executing dotnet test for Revit {ver}...")

        log_name = f"results_{ver}.trx"
        cmd = [
            dotnet_exe, "test", csproj_path,
            "--no-build",
            "--logger", f"trx;LogFileName={log_name}",
            "--results-directory", run_results_dir,
            "-p:Configuration=Debug",
            "-p:Platform=x64",
            f"-p:SyntheticIsolatedBuildRoot={run_build_root}",
            f"-p:SyntheticRunId={run_id}",
        ]
        if filter_expr:
            cmd.extend(["--filter", filter_expr])

        out_file_path = os.path.join(run_results_dir, f"output_{ver}.log")
        proc_returncode = 0
        with open(out_file_path, "w", encoding="utf-8") as out_f:
            if is_logic:
                res = subprocess.run(cmd, stdout=out_f, stderr=out_f)
                proc_returncode = res.returncode
            else:
                with TempDisableThirdPartyAddins(ver):
                    with TempConfigureRevitAddins(ver) as configurer:
                        proc = subprocess.Popen(cmd, stdout=out_f, stderr=out_f)
                        start_time = time.time()
                        timeout = 660  # 11 min: matches 600s ricaun.RevitTest.Timeout + 60s Revit boot buffer
                        print(f"[TESTS] Waiting for Revit {ver} test execution to complete (timeout: {timeout}s)...")

                        while time.time() - start_time < timeout:
                            configurer.whitelist_existing_addins()
                            if proc.poll() is not None:
                                break
                            time.sleep(0.5)

                    if proc.poll() is None:
                        print(f"[TESTS] Test execution timed out after {timeout} seconds. Killing Revit...")
                        subprocess.run(["taskkill", "/F", "/IM", "Revit.exe"], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
                        try:
                            proc.wait(timeout=10)
                        except subprocess.TimeoutExpired:
                            proc.kill()

                    proc_returncode = proc.returncode if proc.returncode is not None else 1

        if proc_returncode != 0 and not os.path.exists(os.path.join(run_results_dir, log_name)):
            had_execution_error = True
            print(f"[ERROR] Test runner exited with code {proc_returncode} without generating {log_name}. See {out_file_path}")

    if not executed_any:
        print("0 matching tests")
        sys.exit(0)

    # 3. Ingest and parse TRX files for this run
    failing_tests = []
    total_run = 0
    total_failed = 0
    total_passed = 0

    ns = {'ns': 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
    trx_files = glob.glob(os.path.join(run_results_dir, "results_*.trx"))

    for trx_path in trx_files:
        try:
            tree = ET.parse(trx_path)
            root = tree.getroot()

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

                    base_name = os.path.basename(trx_path)
                    ver_str = base_name.replace("results_", "").replace(".trx", "")

                    failing_tests.append({
                        'name': f"[{ver_str}] {test_name}",
                        'message': error_message.strip(),
                        'stack_trace': stack_trace.strip()
                    })
        except Exception as e:
            had_execution_error = True
            print(f"[ERROR] Failed to parse TRX file '{trx_path}': {e}")

    rotate_test_results(results_root)

    # 4. Output Markdown formatted summary
    print("\n# Test Execution Summary\n")
    if failing_tests or total_failed > 0 or had_execution_error:
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
        print(f"[TESTS] Preserving failed isolated build root for inspection: {run_build_root}")
        sys.exit(1)
    else:
        shutil.rmtree(run_build_root, ignore_errors=True)
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
                         help="Revit version to target (e.g. 2026 or Logic). Omit to run all.")
    _parser.add_argument("--category", dest="category", default="",
                         help="NUnit [Category] filter")
    _parser.add_argument("--no-pool", dest="no_pool", action="store_true", default=False,
                         help="Bypass optional RevitWorkerPool and use isolated cold Revit workers")
    # Legacy positional support: test_executor.py [class] [version]
    _parser.add_argument("pos_class", nargs="?", default="")
    _parser.add_argument("pos_version", nargs="?", default="")
    _args = _parser.parse_args()

    target_class   = (_args.target_class   or _args.pos_class   or "").strip()
    target_version = (_args.target_version or _args.pos_version or "").strip()
    category       = (_args.category or "").strip()

    # Treat legacy sentinel value as blank
    if target_class == "DefaultAlignmentTests":
        target_class = ""

    run_test_suite(target_class, target_version, category=category, no_pool=_args.no_pool)

