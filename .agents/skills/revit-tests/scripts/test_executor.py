import ctypes
from ctypes import wintypes
import glob
import io
import json
import os
import re
import shutil
import struct
import subprocess
import sys
import tempfile
import threading
import time
import uuid
import winreg
import xml.etree.ElementTree as ET
import zipfile

_SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
if _SCRIPT_DIR not in sys.path:
    sys.path.insert(0, _SCRIPT_DIR)
import guid_whitelister

BUNDLE_LEASE_MUTEX_NAME = r"Local\Synthetic_RicaunBundleLease"
RICAUN_BUNDLE_FOLDER_NAME = "ricaun.RevitTest.Application.bundle"
STARTUP_GATE_MUTEX_PREFIX = r"Local\Synthetic_RevitStartupGate_"
DEFAULT_STARTUP_GATE_HOLD_SECONDS = 90.0
DEFAULT_STARTUP_GATE_ACQUIRE_TIMEOUT_MS = 300000
DEFAULT_STARTUP_POLL_INTERVAL_SECONDS = 0.5
STARTUP_COMPLETE_JOURNAL_MARKERS = (
    "<-DesktopMFCApp::doStartupWarnings",
    "ricaun.RevitTest: Execute:",
)
STARTUP_FAILURE_DIALOG_ID = "TaskDialog_External_Tools_External_Tool_Failure"
STARTUP_FAILURE_WINDOW_TEXT_MARKERS = (
    "external tools - external tool failure",
    "taskdialog_external_tools_external_tool_failure",
    "revit cannot run the external application",
)
LEGACY_MANIFEST_EXPECTED_CLASS = "Synthetic.Bootstrap.BootstrapApp"


def check_legacy_programdata_manifest(version, program_data=None):
    """
    Read-only preflight check for legacy machine-wide %ProgramData% Synthetic*.addin manifests.
    Warns if any <FullClassName> entry differs from Synthetic.Bootstrap.BootstrapApp due to shadow-loading risk.
    """
    resolved_pd = (
        program_data
        if program_data is not None
        else (os.environ.get("ProgramData") or r"C:\ProgramData")
    )
    if not resolved_pd:
        return []

    addins_dir = os.path.join(resolved_pd, "Autodesk", "Revit", "Addins", str(version))
    if not os.path.isdir(addins_dir):
        return []

    flagged = []
    try:
        entries = sorted(os.listdir(addins_dir))
    except OSError:
        return []

    for entry in entries:
        lower = entry.lower()
        if not (lower.startswith("synthetic") and lower.endswith(".addin")):
            continue
        manifest_path = os.path.join(addins_dir, entry)
        if not os.path.isfile(manifest_path):
            continue
        try:
            tree = ET.parse(manifest_path)
            root = tree.getroot()
        except (ET.ParseError, OSError) as exc:
            print(f"[WARN] Could not parse ProgramData manifest '{manifest_path}': {exc}")
            continue

        full_classes = [
            (elem.text or "").strip()
            for elem in root.iter()
            if elem.tag.split("}")[-1] == "FullClassName"
        ]
        legacy_classes = [fc for fc in full_classes if fc != LEGACY_MANIFEST_EXPECTED_CLASS]
        if legacy_classes:
            flagged.append(manifest_path)
            classes_desc = ", ".join(legacy_classes)
            print(
                f"[WARN] Legacy machine-wide manifest detected for Revit {version} at '{manifest_path}' "
                f"(FullClassName={classes_desc}; expected {LEGACY_MANIFEST_EXPECTED_CLASS}). "
                "Risk of shadow-loading stale binaries alongside %APPDATA% Debug Manifest "
                "(read-only warning; file left untouched)."
            )

    return flagged


class TempDisableThirdPartyAddins:
    """Startup self-healing helper to restore any orphaned AddInsSettings.json.bak."""

    def __init__(self, version, appdata=None):
        self.version = version
        resolved_appdata = appdata or os.environ.get("AppData", os.path.expanduser("~\\AppData\\Roaming"))
        self.json_path = os.path.join(
            resolved_appdata, "Autodesk", "Revit", f"Autodesk Revit {version}", "AddinsData", "AddInsSettings.json"
        )
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


def _extract_embedded_zip(data):
    """Locate and open the trailing/embedded ZIP archive inside a .NET assembly buffer."""
    eocd = data.rfind(b"PK\x05\x06")
    while eocd != -1:
        if eocd + 22 <= len(data):
            cdir_size, cdir_rel_offset, comment_len = struct.unpack_from("<IIH", data, eocd + 12)
            end = eocd + 22 + comment_len
            start = (eocd - cdir_size) - cdir_rel_offset
            if 0 <= start < eocd and end <= len(data) and data[start:start + 4] == b"PK\x03\x04":
                try:
                    return zipfile.ZipFile(io.BytesIO(data[start:end]))
                except zipfile.BadZipFile:
                    pass
        eocd = data.rfind(b"PK\x05\x06", 0, eocd)
    return None


def find_ricaun_testadapter_dll(search_dirs=None, env=None):
    """Locate ricaun.RevitTest.TestAdapter.dll in isolated build outputs or the NuGet cache."""
    for directory in search_dirs or ():
        if not directory:
            continue
        candidate = os.path.join(directory, "ricaun.RevitTest.TestAdapter.dll")
        if os.path.isfile(candidate):
            return candidate

    env_map = os.environ if env is None else env
    nuget_root = env_map.get("NUGET_PACKAGES") or os.path.join(os.path.expanduser("~"), ".nuget", "packages")
    pkg_dir = os.path.join(nuget_root, "ricaun.revittest.testadapter")
    if not os.path.isdir(pkg_dir):
        return None

    tfm_priority = {"net8.0": 3, "net6.0": 2, "net45": 1}
    candidates = []
    for root, _, files in os.walk(pkg_dir):
        if "ricaun.RevitTest.TestAdapter.dll" in files:
            tfm = os.path.basename(root).lower()
            candidates.append((tfm_priority.get(tfm, 0), root, os.path.join(root, "ricaun.RevitTest.TestAdapter.dll")))
    if not candidates:
        return None
    candidates.sort(key=lambda item: (item[0], item[1]), reverse=True)
    return candidates[0][2]


def seed_ricaun_bundle_from_testadapter(testadapter_dll_path, bundle_dir):
    """Extract ricaun.RevitTest.Application.bundle into %APPDATA% ApplicationPlugins from TestAdapter.dll."""
    if not testadapter_dll_path or not os.path.isfile(testadapter_dll_path):
        raise FileNotFoundError(f"Could not locate ricaun.RevitTest.TestAdapter.dll: {testadapter_dll_path}")

    with open(testadapter_dll_path, "rb") as f:
        adapter_bytes = f.read()

    outer_zip = _extract_embedded_zip(adapter_bytes)
    if outer_zip is None:
        raise RuntimeError(f"Embedded ricaun.RevitTest.Console archive not found in {testadapter_dll_path}")

    bundle_zip = None
    outer_names = set(outer_zip.namelist())
    for console_entry in ("ricaun.RevitTest.Console.dll", "ricaun.RevitTest.Console.exe"):
        if console_entry in outer_names:
            candidate_zip = _extract_embedded_zip(outer_zip.read(console_entry))
            if candidate_zip is not None:
                bundle_zip = candidate_zip
                break

    if bundle_zip is None:
        raise RuntimeError(
            f"Embedded bundle archive not found in ricaun.RevitTest.Console(.dll/.exe) from {testadapter_dll_path}"
        )

    prefix = f"{RICAUN_BUNDLE_FOLDER_NAME}/"
    bundle_root_abs = os.path.abspath(bundle_dir)
    os.makedirs(bundle_root_abs, exist_ok=True)
    extracted = 0
    for member in bundle_zip.infolist():
        if member.is_dir():
            continue
        member_name = member.filename.replace("\\", "/")
        if not member_name.startswith(prefix):
            continue
        rel_path = member_name[len(prefix):].lstrip("/")
        if not rel_path:
            continue
        dest_path = os.path.abspath(os.path.join(bundle_root_abs, rel_path))
        if not dest_path.startswith(bundle_root_abs + os.sep):
            continue
        os.makedirs(os.path.dirname(dest_path), exist_ok=True)
        with open(dest_path, "wb") as out_f:
            out_f.write(bundle_zip.read(member.filename))
        extracted += 1

    pkg_xml = os.path.join(bundle_root_abs, "PackageContents.xml")
    if extracted == 0 or not os.path.isfile(pkg_xml):
        raise RuntimeError(f"Failed to seed {pkg_xml} from {testadapter_dll_path}")
    return pkg_xml


class _Win32KernelLease:
    """Win32 mutex and shared-read (no FILE_SHARE_DELETE) handle primitives."""

    GENERIC_READ = 0x80000000
    FILE_SHARE_READ = 0x00000001
    FILE_SHARE_WRITE = 0x00000002
    OPEN_EXISTING = 3
    FILE_ATTRIBUTE_NORMAL = 0x00000080
    INVALID_HANDLE_VALUE = ctypes.c_void_p(-1).value
    WAIT_OBJECT_0 = 0x00000000
    WAIT_ABANDONED = 0x00000080

    def __init__(self):
        self._k32 = ctypes.WinDLL("kernel32", use_last_error=True)
        self._k32.CreateMutexW.argtypes = [wintypes.LPVOID, wintypes.BOOL, wintypes.LPCWSTR]
        self._k32.CreateMutexW.restype = wintypes.HANDLE
        self._k32.WaitForSingleObject.argtypes = [wintypes.HANDLE, wintypes.DWORD]
        self._k32.WaitForSingleObject.restype = wintypes.DWORD
        self._k32.ReleaseMutex.argtypes = [wintypes.HANDLE]
        self._k32.ReleaseMutex.restype = wintypes.BOOL
        self._k32.CreateFileW.argtypes = [
            wintypes.LPCWSTR,
            wintypes.DWORD,
            wintypes.DWORD,
            wintypes.LPVOID,
            wintypes.DWORD,
            wintypes.DWORD,
            wintypes.HANDLE,
        ]
        self._k32.CreateFileW.restype = wintypes.HANDLE
        self._k32.CloseHandle.argtypes = [wintypes.HANDLE]
        self._k32.CloseHandle.restype = wintypes.BOOL

    def acquire_mutex(self, name, timeout_ms=30000):
        h_mutex = self._k32.CreateMutexW(None, False, name)
        if not h_mutex:
            raise ctypes.WinError(ctypes.get_last_error())
        wait_res = self._k32.WaitForSingleObject(h_mutex, timeout_ms)
        if wait_res not in (self.WAIT_OBJECT_0, self.WAIT_ABANDONED):
            err = ctypes.get_last_error()
            self._k32.CloseHandle(h_mutex)
            raise TimeoutError(f"Timed out waiting for mutex '{name}' (wait={wait_res}, err={err}).")
        return h_mutex

    def release_mutex(self, h_mutex):
        released = False
        err = 0
        try:
            released = bool(self._k32.ReleaseMutex(h_mutex))
            if not released:
                err = ctypes.get_last_error()
        finally:
            self._k32.CloseHandle(h_mutex)
        if not released:
            raise ctypes.WinError(err)

    def open_shared_read_no_delete(self, file_path):
        share_mode = self.FILE_SHARE_READ | self.FILE_SHARE_WRITE
        handle = self._k32.CreateFileW(
            file_path,
            self.GENERIC_READ,
            share_mode,
            None,
            self.OPEN_EXISTING,
            self.FILE_ATTRIBUTE_NORMAL,
            None,
        )
        if not handle or handle == self.INVALID_HANDLE_VALUE:
            raise ctypes.WinError(ctypes.get_last_error())
        return handle

    def close_handle(self, handle):
        self._k32.CloseHandle(handle)


class RicaunBundleLease:
    """
    Coordinates concurrent worker access to %APPDATA%/Autodesk/ApplicationPlugins/ricaun.RevitTest.Application.bundle
    via a Win32 mutex and a shared-read (no FILE_SHARE_DELETE) file handle on PackageContents.xml.
    """

    def __init__(
        self,
        appdata=None,
        testadapter_search_dirs=None,
        testadapter_dll=None,
        mutex_name=BUNDLE_LEASE_MUTEX_NAME,
        env=None,
    ):
        env_map = os.environ if env is None else env
        resolved_appdata = appdata or env_map.get("AppData") or os.path.expanduser("~\\AppData\\Roaming")
        self.bundle_dir = os.path.join(
            resolved_appdata, "Autodesk", "ApplicationPlugins", RICAUN_BUNDLE_FOLDER_NAME
        )
        self.package_contents_path = os.path.join(self.bundle_dir, "PackageContents.xml")
        self.testadapter_search_dirs = list(testadapter_search_dirs or ())
        self.testadapter_dll = testadapter_dll
        self.mutex_name = mutex_name
        self.env = env_map
        self._win32 = _Win32KernelLease()
        self._handle = None

    def __enter__(self):
        h_mutex = self._win32.acquire_mutex(self.mutex_name)
        try:
            if not os.path.isfile(self.package_contents_path):
                adapter_dll = self.testadapter_dll or find_ricaun_testadapter_dll(
                    search_dirs=self.testadapter_search_dirs, env=self.env
                )
                seed_ricaun_bundle_from_testadapter(adapter_dll, self.bundle_dir)
            self._handle = self._win32.open_shared_read_no_delete(self.package_contents_path)
            return self
        finally:
            self._win32.release_mutex(h_mutex)

    def __exit__(self, exc_type, exc_val, exc_tb):
        h_mutex = self._win32.acquire_mutex(self.mutex_name)
        try:
            if self._handle is not None:
                self._win32.close_handle(self._handle)
                self._handle = None

            if os.path.exists(self.package_contents_path):
                try:
                    os.remove(self.package_contents_path)
                except PermissionError:
                    # Another active worker still holds a shared-read lease without FILE_SHARE_DELETE
                    return
                except FileNotFoundError:
                    pass

            if os.path.isdir(self.bundle_dir):
                shutil.rmtree(self.bundle_dir, ignore_errors=True)
        finally:
            self._win32.release_mutex(h_mutex)


def get_startup_gate_mutex_name(version):
    """Return the per-Revit-version Win32 Startup Gate named mutex."""
    return f"{STARTUP_GATE_MUTEX_PREFIX}{version}"


class RevitStartupGate:
    """
    Coordinates per-Revit-version startup serialization via Local\\Synthetic_RevitStartupGate_<Year>.
    Enforces Win32 mutex thread affinity: release() must execute on the exact acquiring OS thread.
    """

    def __init__(
        self,
        version,
        mutex_name=None,
        acquire_timeout_ms=DEFAULT_STARTUP_GATE_ACQUIRE_TIMEOUT_MS,
        win32=None,
    ):
        self.version = str(version)
        self.mutex_name = mutex_name or get_startup_gate_mutex_name(self.version)
        self.acquire_timeout_ms = acquire_timeout_ms
        self._win32 = win32 or _Win32KernelLease()
        self._handle = None
        self._owner_thread_id = None

    @property
    def is_held(self):
        return self._handle is not None

    def acquire(self):
        if self._handle is not None:
            raise RuntimeError(f"Startup gate '{self.mutex_name}' is already held by this instance.")
        handle = self._win32.acquire_mutex(self.mutex_name, timeout_ms=self.acquire_timeout_ms)
        self._handle = handle
        self._owner_thread_id = threading.get_ident()
        return self

    def release(self):
        if self._handle is None:
            return False
        current_tid = threading.get_ident()
        if current_tid != self._owner_thread_id:
            raise RuntimeError(
                f"Win32 mutex '{self.mutex_name}' must be released on acquiring thread "
                f"{self._owner_thread_id}, not thread {current_tid}."
            )
        handle = self._handle
        try:
            self._win32.release_mutex(handle)
        finally:
            self._handle = None
            self._owner_thread_id = None
        return True

    def __enter__(self):
        return self.acquire()

    def __exit__(self, exc_type, exc_val, exc_tb):
        self.release()


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


TYPED_CATEGORY_ATTRIBUTES = {
    "UnitTest": "Unit",
    "ArchitectureTest": "Architecture",
    "RevitSmokeTest": "RevitSmoke",
    "RevitAdapterTest": "RevitAdapter",
    "RevitIsolatedDocTest": "RevitIsolatedDoc",
    "QuarantineTest": "Quarantine",
}


def _map_projects_dir_key(dir_name):
    if dir_name == "Synthetic.LogicTests":
        return "Logic"
    if dir_name == "Synthetic.LegacyMockTests":
        return "LegacyMock"
    if dir_name.startswith("Synthetic.RevitTests."):
        return dir_name[len("Synthetic.RevitTests."):]
    if dir_name.startswith("Synthetic.") and dir_name.endswith("Tests") and len(dir_name) > len("Synthetic.Tests"):
        return dir_name[len("Synthetic."):-len("Tests")]
    return dir_name


def discover_test_projects(test_dir):
    """Discover tests/Projects/* heads, mapping target key -> list of csproj paths."""
    test_projects = {}
    if not os.path.exists(test_dir):
        return test_projects

    projects_dir = os.path.join(test_dir, "Projects")
    if os.path.isdir(projects_dir):
        for item in sorted(os.listdir(projects_dir)):
            item_path = os.path.join(projects_dir, item)
            if os.path.isdir(item_path):
                csproj_files = sorted(glob.glob(os.path.join(item_path, "*.csproj")))
                if csproj_files:
                    ver = _map_projects_dir_key(item)
                    test_projects.setdefault(ver, []).append(csproj_files[0])

    return test_projects


def select_run_projects(test_projects, target_version=""):
    """Select target test projects for --version, bundling LegacyMock once under --version Logic."""
    if not target_version:
        ordered = {}
        for key in ("Logic", "LegacyMock"):
            if key in test_projects:
                ordered[key] = test_projects[key]
        for key in sorted(test_projects.keys()):
            if key not in ordered:
                ordered[key] = test_projects[key]
        return ordered

    matched_key = None
    if target_version in test_projects:
        matched_key = target_version
    else:
        for key in test_projects:
            if key.casefold() == target_version.casefold():
                matched_key = key
                break

    if not matched_key:
        return {}

    selected = {matched_key: test_projects[matched_key]}
    if matched_key.casefold() == "logic" and "LegacyMock" in test_projects:
        selected["LegacyMock"] = test_projects["LegacyMock"]
    return selected


def get_project_target_frameworks(csproj_path):
    """Parse <TargetFrameworks> or <TargetFramework> from a .csproj, returning (tfms, is_multi_tfm)."""
    try:
        tree = ET.parse(csproj_path)
        root = tree.getroot()
    except (ET.ParseError, OSError):
        return [""], False

    for elem in root.iter():
        tag = elem.tag.split("}")[-1]
        if tag == "TargetFrameworks" and elem.text:
            tfms = [t.strip() for t in elem.text.split(";") if t.strip()]
            if tfms:
                return tfms, True

    for elem in root.iter():
        tag = elem.tag.split("}")[-1]
        if tag == "TargetFramework" and elem.text and elem.text.strip():
            return [elem.text.strip()], False

    return [""], False


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


def _strip_string_and_char_literals(code_line):
    """Replace string and char literal contents with spaces to avoid false keyword/brace matches."""
    out = []
    in_string = False
    in_char = False
    i = 0
    n = len(code_line)
    while i < n:
        ch = code_line[i]
        if in_string:
            if ch == "\\" and i + 1 < n:
                out.append("  ")
                i += 2
                continue
            if ch == '"':
                in_string = False
                out.append('"')
            else:
                out.append(" ")
        elif in_char:
            if ch == "\\" and i + 1 < n:
                out.append("  ")
                i += 2
                continue
            if ch == "'":
                in_char = False
                out.append("'")
            else:
                out.append(" ")
        else:
            if ch == '"':
                in_string = True
            elif ch == "'":
                in_char = True
            out.append(ch)
        i += 1
    return "".join(out)


def _extract_categories_from_line(code_line):
    """Extract normalized category names from [Category(...)] and typed category attributes."""
    cats = set()
    for block in re.findall(r'\[([^\]]*)\]', code_line):
        for cat_match in re.findall(r'\bCategory\s*\(\s*"([^"]+)"\s*\)', block):
            cats.add(cat_match.strip().casefold())
        for typed_match in re.findall(
            r'\b(UnitTest|ArchitectureTest|RevitSmokeTest|RevitAdapterTest|RevitIsolatedDocTest|QuarantineTest)(?:Attribute)?\b',
            block,
        ):
            cats.add(TYPED_CATEGORY_ATTRIBUTES[typed_match].casefold())
    return cats


def _parse_base_types(header_text):
    """Extract base class/interface names from a class header segment up to '{'."""
    no_brace = header_text.split("{", 1)[0]
    if ":" not in no_brace:
        return []
    after_colon = no_brace.split(":", 1)[1]
    after_colon = re.split(r'\bwhere\b', after_colon)[0]
    # Strip generic type argument lists like Base<Foo, Bar>
    cleaned = []
    angle_depth = 0
    for ch in after_colon:
        if ch == "<":
            angle_depth += 1
        elif ch == ">":
            angle_depth = max(0, angle_depth - 1)
        elif angle_depth == 0:
            cleaned.append(ch)
    bases = []
    for part in "".join(cleaned).split(","):
        token = part.strip()
        if token:
            bases.append(token)
    return bases


def _collect_revit_test_cs_files(tests_dir, version=None):
    """Collect C# files belonging to Revit tests and shared/Revit fixtures under tests_dir."""
    source_dirs = []

    if version:
        proj_ver_dir = os.path.join(tests_dir, "Projects", f"Synthetic.RevitTests.{version}")
        if os.path.isdir(proj_ver_dir):
            source_dirs.append(proj_ver_dir)
    else:
        projects_dir = os.path.join(tests_dir, "Projects")
        if os.path.isdir(projects_dir):
            for entry in sorted(os.listdir(projects_dir)):
                if entry.startswith("Synthetic.RevitTests."):
                    full_entry = os.path.join(projects_dir, entry)
                    if os.path.isdir(full_entry):
                        source_dirs.append(full_entry)

    for rel_sub in ("Fixtures", "Platform", "Features", "RevitDOM"):
        sub_dir = os.path.join(tests_dir, rel_sub)
        if os.path.isdir(sub_dir):
            source_dirs.append(sub_dir)

    cs_files = []
    seen_paths = set()
    for src_dir in source_dirs:
        rel_root_name = os.path.basename(src_dir)
        is_partition_root = rel_root_name in ("Platform", "Features", "RevitDOM")
        for root, dirs, files in os.walk(src_dir):
            dirs[:] = [d for d in dirs if d.lower() not in ("bin", "obj")]
            if is_partition_root:
                rel_parts = os.path.relpath(root, src_dir).split(os.sep)
                if "Revit" not in rel_parts:
                    continue
            for fn in sorted(files):
                if fn.endswith(".cs"):
                    full_path = os.path.abspath(os.path.join(root, fn))
                    if full_path not in seen_paths:
                        seen_paths.add(full_path)
                        cs_files.append(full_path)
    return cs_files


def find_tests_by_category(tests_dir, category, version=None):
    """Scan C# test files under tests_dir for NUnit [Category] and typed category attributes."""
    if not category or not os.path.isdir(tests_dir):
        return []

    target_cat = category.strip().casefold()
    cs_files = _collect_revit_test_cs_files(tests_dir, version=version)

    ns_re = re.compile(r'^\s*namespace\s+([A-Za-z_][A-Za-z0-9_\.]*)\s*(;|\{)?')
    class_re = re.compile(r'\bclass\s+([A-Za-z_][A-Za-z0-9_]*)\b')
    method_re = re.compile(
        r'(?:public|internal|protected|private)\s+(?:static\s+|async\s+|virtual\s+|override\s+|new\s+|sealed\s+)*'
        r'[\w<>\[\],\.\?\s]+\s+([A-Za-z_][A-Za-z0-9_]*)\s*\('
    )

    parsed_classes = []

    for cs_path in cs_files:
        try:
            with open(cs_path, "r", encoding="utf-8-sig") as f:
                lines = f.readlines()
        except OSError:
            continue

        in_block_comment = False
        brace_depth = 0
        namespace_stack = []
        file_scoped_ns = None
        pending_ns = None
        class_stack = []
        pending_class_header = None
        pending_categories = set()

        for raw_line in lines:
            code, in_block_comment = _strip_comments_and_strings(raw_line, in_block_comment)
            stripped = code.strip()
            if not stripped:
                continue

            sanitized = _strip_string_and_char_literals(stripped)

            if pending_class_header is not None:
                pending_class_header["text"] += " " + sanitized
                if "{" in sanitized:
                    cls_info = pending_class_header
                    pending_class_header = None
                    bases = _parse_base_types(cls_info["text"])
                    cls_record = {
                        "fqn": cls_info["fqn"],
                        "simple_name": cls_info["simple_name"],
                        "is_abstract": cls_info["is_abstract"],
                        "own_categories": set(cls_info["own_categories"]),
                        "bases": bases,
                        "methods": [],
                    }
                    parsed_classes.append(cls_record)
                    opens, closes = _count_braces(sanitized)
                    class_stack.append((cls_record, brace_depth + 1))
                    brace_depth = max(0, brace_depth + opens - closes)
                    while class_stack and brace_depth < class_stack[-1][1]:
                        class_stack.pop()
                    while namespace_stack and brace_depth < namespace_stack[-1][1]:
                        namespace_stack.pop()
                continue

            pending_categories.update(_extract_categories_from_line(stripped))

            ns_match = ns_re.match(sanitized)
            if ns_match and not class_stack:
                ns_name = ns_match.group(1)
                delim = ns_match.group(2)
                if delim == ";":
                    file_scoped_ns = ns_name
                elif delim == "{":
                    opens, closes = _count_braces(sanitized)
                    namespace_stack.append((ns_name, brace_depth + 1))
                    brace_depth = max(0, brace_depth + opens - closes)
                    while namespace_stack and brace_depth < namespace_stack[-1][1]:
                        namespace_stack.pop()
                    continue
                else:
                    pending_ns = ns_name
                pending_categories.clear()
                continue

            if pending_ns is not None and "{" in sanitized:
                opens, closes = _count_braces(sanitized)
                namespace_stack.append((pending_ns, brace_depth + 1))
                pending_ns = None
                brace_depth = max(0, brace_depth + opens - closes)
                while namespace_stack and brace_depth < namespace_stack[-1][1]:
                    namespace_stack.pop()
                continue

            class_match = class_re.search(sanitized)
            if class_match:
                cls_name = class_match.group(1)
                prefix = sanitized[:class_match.start()]
                is_abstract = bool(re.search(r'\babstract\b', prefix))
                ns_parts = [n for n, _ in namespace_stack]
                if file_scoped_ns and not ns_parts:
                    ns_parts = [file_scoped_ns]
                ns_prefix = ".".join(ns_parts)
                if class_stack:
                    fqn = f"{class_stack[-1][0]['fqn']}+{cls_name}"
                elif ns_prefix:
                    fqn = f"{ns_prefix}.{cls_name}"
                else:
                    fqn = cls_name

                header_rest = sanitized[class_match.start():]
                own_cats = set(pending_categories)
                pending_categories.clear()

                if "{" in header_rest:
                    bases = _parse_base_types(header_rest)
                    cls_record = {
                        "fqn": fqn,
                        "simple_name": cls_name,
                        "is_abstract": is_abstract,
                        "own_categories": own_cats,
                        "bases": bases,
                        "methods": [],
                    }
                    parsed_classes.append(cls_record)
                    opens, closes = _count_braces(sanitized)
                    class_stack.append((cls_record, brace_depth + 1))
                    brace_depth = max(0, brace_depth + opens - closes)
                    while class_stack and brace_depth < class_stack[-1][1]:
                        class_stack.pop()
                    while namespace_stack and brace_depth < namespace_stack[-1][1]:
                        namespace_stack.pop()
                else:
                    pending_class_header = {
                        "fqn": fqn,
                        "simple_name": cls_name,
                        "is_abstract": is_abstract,
                        "own_categories": own_cats,
                        "text": header_rest,
                    }
                continue

            if class_stack and pending_categories and brace_depth == class_stack[-1][1]:
                method_match = method_re.search(sanitized)
                if method_match:
                    method_name = method_match.group(1)
                    class_stack[-1][0]["methods"].append((method_name, set(pending_categories)))
                    pending_categories.clear()
                elif ";" in sanitized and "[" not in sanitized:
                    pending_categories.clear()

            opens, closes = _count_braces(sanitized)
            brace_depth = max(0, brace_depth + opens - closes)
            while class_stack and brace_depth < class_stack[-1][1]:
                class_stack.pop()
            while namespace_stack and brace_depth < namespace_stack[-1][1]:
                namespace_stack.pop()

    by_fqn = {c["fqn"]: c for c in parsed_classes}
    by_simple = {}
    for c in parsed_classes:
        by_simple.setdefault(c["simple_name"], []).append(c)

    def resolve_effective_categories(cls_rec, visited=None):
        if visited is None:
            visited = set()
        fqn = cls_rec["fqn"]
        if fqn in visited:
            return set()
        visited.add(fqn)
        cats = set(cls_rec["own_categories"])
        for base_ref in cls_rec["bases"]:
            base_candidates = []
            if base_ref in by_fqn:
                base_candidates.append(by_fqn[base_ref])
            else:
                simple_base = base_ref.split(".")[-1]
                base_candidates.extend(by_simple.get(simple_base, ()))
            for base_cls in base_candidates:
                cats.update(resolve_effective_categories(base_cls, visited))
        return cats

    matches = set()
    for cls_rec in parsed_classes:
        if cls_rec["is_abstract"]:
            continue
        effective_cats = resolve_effective_categories(cls_rec)
        if target_cat in effective_cats:
            matches.add(cls_rec["fqn"])
            continue
        for method_name, method_cats in cls_rec["methods"]:
            if target_cat in method_cats:
                matches.add(f"{cls_rec['fqn']}.{method_name}")

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
    """Probe for optional RevitWorkerPool command via env var, tools/bin/RevitWorkerPool, or tools/RevitWorkerPool."""
    env = os.environ if env is None else env
    env_cmd = env.get("SYNTHETIC_WORKER_POOL_CMD", "").strip()
    if env_cmd:
        return env_cmd

    tools_dir = os.path.join(workspace_path, "tools")
    candidates = [
        os.path.join(tools_dir, "bin", "RevitWorkerPool", "RevitWorkerPool.exe"),
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


def build_worker_env(workspace_path, run_id, base_env=None):
    """Build isolated child environment variables for Revit test worker processes."""
    env = dict(os.environ if base_env is None else base_env)
    env["SYNTHETIC_PROJECT_ROOT"] = workspace_path
    env["SYNTHETIC_TEST_WORKER"] = "1"
    env["SYNTHETIC_TEST_RUN_ID"] = str(run_id)
    env["RICAUN_REVITTEST_CONSOLE_PROCESS_ARGUMENTS"] = "/nosplash"
    return env


def get_default_journals_dir(version, env=None):
    env_map = os.environ if env is None else env
    local_appdata = env_map.get("LOCALAPPDATA") or os.path.expanduser("~\\AppData\\Local")
    return os.path.join(local_appdata, "Autodesk", "Revit", f"Autodesk Revit {version}", "Journals")


def snapshot_known_journals(version, snapshot_path, journals_dir=None, env=None):
    """Snapshot pre-launch journal*.txt files into a JSON list before starting Revit."""
    target_dir = journals_dir or get_default_journals_dir(version, env=env)
    known = []
    if os.path.isdir(target_dir):
        for entry in sorted(os.listdir(target_dir)):
            if entry.startswith("journal") and entry.endswith(".txt"):
                full_path = os.path.join(target_dir, entry)
                if os.path.isfile(full_path):
                    known.append(os.path.abspath(full_path))

    os.makedirs(os.path.dirname(os.path.abspath(snapshot_path)), exist_ok=True)
    with open(snapshot_path, "w", encoding="utf-8") as f:
        json.dump(known, f, indent=2)
    return snapshot_path


def kill_process_tree(pid, runner=subprocess.run):
    """Terminate only the timed-out dotnet test / Revit child PID tree."""
    return runner(
        ["taskkill", "/F", "/T", "/PID", str(pid)],
        stdout=subprocess.DEVNULL,
        stderr=subprocess.DEVNULL,
    )


def get_journal_tool_path():
    return os.path.abspath(
        os.path.join(_SCRIPT_DIR, "..", "..", "revit-journal-telemetry", "scripts", "revit_journal_tool.py")
    )


def run_journal_cleanliness_gate(
    version,
    run_id,
    snapshot_path,
    python_exe=sys.executable,
    journal_tool_path=None,
    runner=subprocess.run,
):
    """Run revit_journal_tool.py --assert-clean for the completed worker run."""
    tool_path = journal_tool_path or get_journal_tool_path()
    cmd = [
        python_exe,
        tool_path,
        "--version",
        str(version),
        "--run-id",
        str(run_id),
        "--known-journals-file",
        snapshot_path,
        "--assert-clean",
    ]
    res = runner(cmd, capture_output=True, text=True)
    parts = [p.strip() for p in (res.stdout, res.stderr) if p and p.strip()]
    return res.returncode == 0, "\n".join(parts)


def run_worker_journal_cleanliness_gate(
    version,
    worker_id,
    python_exe=sys.executable,
    journal_tool_path=None,
    runner=subprocess.run,
):
    """Run revit_journal_tool.py --worker-id <worker_id> --assert-clean for a pooled worker shard."""
    if not worker_id or not str(worker_id).strip():
        return False, "[-] Worker ID must be non-empty for --worker-id journal cleanliness gating."
    tool_path = journal_tool_path or get_journal_tool_path()
    cmd = [
        python_exe,
        tool_path,
        "--version",
        str(version),
        "--worker-id",
        str(worker_id).strip(),
        "--assert-clean",
    ]
    res = runner(cmd, capture_output=True, text=True)
    parts = [p.strip() for p in (res.stdout, res.stderr) if p and p.strip()]
    return res.returncode == 0, "\n".join(parts)


def cleanup_revit_test_temp(run_id, temp_dir=None):
    """Remove %TEMP%/RevitTest/*<run_id>* archives and extracted folders for passing runs."""
    if not run_id:
        return []
    revit_test_dir = os.path.join(temp_dir or tempfile.gettempdir(), "RevitTest")
    if not os.path.isdir(revit_test_dir):
        return []
    removed = []
    for entry in os.listdir(revit_test_dir):
        if run_id not in entry:
            continue
        candidate = os.path.join(revit_test_dir, entry)
        try:
            if os.path.isdir(candidate):
                shutil.rmtree(candidate, ignore_errors=True)
            elif os.path.isfile(candidate):
                os.remove(candidate)
            removed.append(candidate)
        except OSError:
            pass
    return removed


def _list_journal_files(journals_dir):
    """Return journal*.txt paths in journals_dir sorted newest-first by mtime."""
    if not journals_dir or not os.path.isdir(journals_dir):
        return []
    files = []
    try:
        entries = os.listdir(journals_dir)
    except OSError:
        return []
    for entry in entries:
        if entry.startswith("journal") and entry.endswith(".txt"):
            full_path = os.path.join(journals_dir, entry)
            if os.path.isfile(full_path):
                try:
                    mtime = os.path.getmtime(full_path)
                except OSError:
                    mtime = 0.0
                files.append((mtime, full_path))
    files.sort(key=lambda item: (item[0], item[1]), reverse=True)
    return [item[1] for item in files]


def _read_journal_text_best_effort(filepath):
    """Read an active or completed Revit journal without failing on partial writes or encoding."""
    try:
        with open(filepath, "rb") as f:
            raw = f.read()
    except OSError:
        return ""
    if not raw:
        return ""
    if raw.startswith((b"\xff\xfe", b"\xfe\xff")):
        return raw.decode("utf-16", errors="ignore")
    if raw.startswith(b"\xef\xbb\xbf"):
        return raw.decode("utf-8-sig", errors="ignore")
    try:
        return raw.decode("utf-8")
    except UnicodeDecodeError:
        return raw.decode("cp1252", errors="ignore")


def _contains_run_id_marker(text, run_id):
    if not run_id or not text:
        return False
    pattern = rf"SYNTHETIC_TEST_RUN:{re.escape(str(run_id))}(?![A-Za-z0-9_.-])"
    return bool(re.search(pattern, text))


def find_live_worker_journal(journals_dir, run_id=None, known_journals=None):
    """
    Resolve the active worker journal in journals_dir:
      1) new journal (not in known_journals) containing SYNTHETIC_TEST_RUN:<run_id>
      2) newly created journal relative to known_journals not attributed to another run ID
    Returns (filepath, text) or (None, "").
    """
    known_set = {os.path.normcase(os.path.abspath(p)) for p in (known_journals or ())}
    candidates = _list_journal_files(journals_dir)
    new_candidates = [
        p for p in candidates if os.path.normcase(os.path.abspath(p)) not in known_set
    ]
    if not new_candidates:
        return None, ""

    cached = {}
    for candidate in new_candidates:
        text = _read_journal_text_best_effort(candidate)
        cached[candidate] = text
        if run_id and _contains_run_id_marker(text, run_id):
            return candidate, text

    for candidate in new_candidates:
        text = cached.get(candidate, "")
        if "SYNTHETIC_TEST_RUN:" in text:
            continue
        return candidate, text

    return None, ""


def _has_startup_completed(journal_text):
    return bool(journal_text) and any(
        marker in journal_text for marker in STARTUP_COMPLETE_JOURNAL_MARKERS
    )


def _poll_process(proc):
    poll_fn = getattr(proc, "poll", None)
    if callable(poll_fn):
        return poll_fn()
    return getattr(proc, "returncode", None)


def _terminate_process_tree_and_wait(proc, tree_killer):
    tree_killer(proc.pid)
    try:
        proc.wait(timeout=10)
    except subprocess.TimeoutExpired:
        proc.kill()


class _PROCESSENTRY32W(ctypes.Structure):
    _fields_ = [
        ("dwSize", wintypes.DWORD),
        ("cntUsage", wintypes.DWORD),
        ("th32ProcessID", wintypes.DWORD),
        ("th32DefaultHeapID", ctypes.c_size_t),
        ("th32ModuleID", wintypes.DWORD),
        ("cntThreads", wintypes.DWORD),
        ("th32ParentProcessID", wintypes.DWORD),
        ("pcPriClassBase", wintypes.LONG),
        ("dwFlags", wintypes.DWORD),
        ("szExeFile", wintypes.WCHAR * 260),
    ]


def iter_process_snapshot():
    """Return list of (pid, ppid, exe_name) tuples from Win32 CreateToolhelp32Snapshot."""
    th32cs_snapprocess = 0x00000002
    invalid_handle = ctypes.c_void_p(-1).value
    k32 = ctypes.WinDLL("kernel32", use_last_error=True)
    k32.CreateToolhelp32Snapshot.argtypes = [wintypes.DWORD, wintypes.DWORD]
    k32.CreateToolhelp32Snapshot.restype = wintypes.HANDLE
    k32.Process32FirstW.argtypes = [wintypes.HANDLE, ctypes.POINTER(_PROCESSENTRY32W)]
    k32.Process32FirstW.restype = wintypes.BOOL
    k32.Process32NextW.argtypes = [wintypes.HANDLE, ctypes.POINTER(_PROCESSENTRY32W)]
    k32.Process32NextW.restype = wintypes.BOOL
    k32.CloseHandle.argtypes = [wintypes.HANDLE]
    k32.CloseHandle.restype = wintypes.BOOL

    snap = k32.CreateToolhelp32Snapshot(th32cs_snapprocess, 0)
    if not snap or snap == invalid_handle:
        return []

    entries = []
    try:
        pe = _PROCESSENTRY32W()
        pe.dwSize = ctypes.sizeof(_PROCESSENTRY32W)
        ok = k32.Process32FirstW(snap, ctypes.byref(pe))
        while ok:
            entries.append((int(pe.th32ProcessID), int(pe.th32ParentProcessID), str(pe.szExeFile)))
            ok = k32.Process32NextW(snap, ctypes.byref(pe))
    finally:
        k32.CloseHandle(snap)
    return entries


def find_descendant_revit_pids(root_pid, process_snapshot=None):
    """Return sorted PIDs of Revit.exe processes within the descendant tree of root_pid."""
    if root_pid is None or root_pid <= 0:
        return []
    entries = process_snapshot if process_snapshot is not None else iter_process_snapshot()
    children_by_parent = {}
    exe_by_pid = {}
    for pid, ppid, exe_name in entries:
        if pid <= 0:
            continue
        exe_by_pid[pid] = exe_name or ""
        children_by_parent.setdefault(ppid, []).append(pid)

    visited = set()
    queue = [int(root_pid)]
    revit_pids = []
    while queue:
        current = queue.pop(0)
        if current in visited or current <= 0:
            continue
        visited.add(current)
        if exe_by_pid.get(current, "").casefold() == "revit.exe":
            revit_pids.append(current)
        for child_pid in children_by_parent.get(current, ()):
            if child_pid not in visited:
                queue.append(child_pid)
    return sorted(revit_pids)


def _enumerate_win32_windows_for_pids(target_pids):
    """Enumerate top-level Win32 #32770 windows (and child control text) owned by target_pids."""
    if not target_pids:
        return []
    u32 = ctypes.WinDLL("user32", use_last_error=True)
    wndenumproc = ctypes.WINFUNCTYPE(wintypes.BOOL, wintypes.HWND, wintypes.LPARAM)
    u32.EnumWindows.argtypes = [wndenumproc, wintypes.LPARAM]
    u32.EnumWindows.restype = wintypes.BOOL
    u32.EnumChildWindows.argtypes = [wintypes.HWND, wndenumproc, wintypes.LPARAM]
    u32.EnumChildWindows.restype = wintypes.BOOL
    u32.GetWindowThreadProcessId.argtypes = [wintypes.HWND, ctypes.POINTER(wintypes.DWORD)]
    u32.GetWindowThreadProcessId.restype = wintypes.DWORD
    u32.GetClassNameW.argtypes = [wintypes.HWND, wintypes.LPWSTR, ctypes.c_int]
    u32.GetClassNameW.restype = wintypes.INT if hasattr(wintypes, "INT") else ctypes.c_int
    u32.GetWindowTextLengthW.argtypes = [wintypes.HWND]
    u32.GetWindowTextLengthW.restype = ctypes.c_int
    u32.GetWindowTextW.argtypes = [wintypes.HWND, wintypes.LPWSTR, ctypes.c_int]
    u32.GetWindowTextW.restype = ctypes.c_int

    def _read_window_text(hwnd):
        length = u32.GetWindowTextLengthW(hwnd)
        buf_len = min(max(length + 1, 256), 4096)
        buf = ctypes.create_unicode_buffer(buf_len)
        u32.GetWindowTextW(hwnd, buf, buf_len)
        return buf.value

    def _read_class_name(hwnd):
        buf = ctypes.create_unicode_buffer(256)
        u32.GetClassNameW(hwnd, buf, 256)
        return buf.value

    target_set = set(target_pids)
    results = []

    def _on_top_window(hwnd, _lparam):
        try:
            pid_val = wintypes.DWORD()
            u32.GetWindowThreadProcessId(hwnd, ctypes.byref(pid_val))
            pid_int = int(pid_val.value)
            if pid_int not in target_set:
                return True
            class_name = _read_class_name(hwnd)
            if class_name != "#32770":
                return True
            title = _read_window_text(hwnd)
            child_texts = []

            def _on_child(child_hwnd, _child_lp):
                try:
                    txt = _read_window_text(child_hwnd)
                    if txt:
                        child_texts.append(txt)
                except Exception:
                    pass
                return True

            child_cb = wndenumproc(_on_child)
            u32.EnumChildWindows(hwnd, child_cb, 0)
            results.append((int(hwnd), pid_int, class_name, title, child_texts))
        except Exception:
            pass
        return True

    top_cb = wndenumproc(_on_top_window)
    u32.EnumWindows(top_cb, 0)
    return results


def detect_startup_modal_window(
    root_pid,
    process_snapshot_fn=iter_process_snapshot,
    enumerate_windows_fn=None,
):
    """
    Detect a #32770 External Tool Failure TaskDialog owned by any descendant Revit.exe of root_pid.
    Returns metadata dict if detected, otherwise None.
    """
    revit_pids = find_descendant_revit_pids(root_pid, process_snapshot=process_snapshot_fn())
    if not revit_pids:
        return None
    target_set = set(revit_pids)
    windows = (
        enumerate_windows_fn(target_set)
        if enumerate_windows_fn is not None
        else _enumerate_win32_windows_for_pids(target_set)
    )
    for hwnd, pid, class_name, title, child_texts in windows:
        if pid not in target_set or class_name != "#32770":
            continue
        texts = [title or ""] + list(child_texts or ())
        for txt in texts:
            lowered = txt.casefold()
            if any(marker in lowered for marker in STARTUP_FAILURE_WINDOW_TEXT_MARKERS):
                return {
                    "source": "window",
                    "pid": pid,
                    "hwnd": hwnd,
                    "class_name": class_name,
                    "title": title,
                    "matched_text": txt,
                }
    return None


def _build_effective_known_journals(journals_dir, known_journals, run_id):
    """Merge caller snapshot with any pre-spawn journals on disk not tagged for run_id."""
    effective_known = {os.path.normcase(os.path.abspath(p)) for p in (known_journals or ())}
    for existing_j in _list_journal_files(journals_dir):
        norm_j = os.path.normcase(os.path.abspath(existing_j))
        if norm_j in effective_known:
            continue
        existing_text = _read_journal_text_best_effort(existing_j)
        if run_id and _contains_run_id_marker(existing_text, run_id):
            continue
        effective_known.add(norm_j)
    return effective_known


def _detect_startup_modal_reason(proc_pid, journals_dir, run_id, known_journals, window_modal_detector):
    """Inspect live journal and descendant Revit.exe #32770 windows; return (startup_done, modal_reason)."""
    journal_path, journal_text = find_live_worker_journal(
        journals_dir,
        run_id=run_id,
        known_journals=known_journals,
    )
    startup_done = _has_startup_completed(journal_text)
    if startup_done:
        return True, None

    if journal_text and STARTUP_FAILURE_DIALOG_ID in journal_text:
        return False, f"journal '{journal_path}' ({STARTUP_FAILURE_DIALOG_ID})"

    modal_win = window_modal_detector(proc_pid)
    if modal_win:
        return (
            False,
            f"descendant Revit window (PID={modal_win.get('pid')}, title={modal_win.get('title')!r})",
        )
    return False, None


def _execute_revit_test_attempt(
    cmd,
    out_f,
    worker_env,
    version,
    run_id=None,
    journals_dir=None,
    known_journals=None,
    timeout=660,
    startup_hold_cap=DEFAULT_STARTUP_GATE_HOLD_SECONDS,
    poll_interval=DEFAULT_STARTUP_POLL_INTERVAL_SECONDS,
    acquire_timeout_ms=DEFAULT_STARTUP_GATE_ACQUIRE_TIMEOUT_MS,
    popen_fn=subprocess.Popen,
    tree_killer=kill_process_tree,
    gate_factory=None,
    window_modal_detector=None,
    monotonic_fn=time.monotonic,
    sleep_fn=time.sleep,
):
    """Run one cold Revit test worker attempt coordinated by the per-version Startup Gate."""
    resolved_journals_dir = journals_dir or get_default_journals_dir(version, env=worker_env)
    modal_win_detector = window_modal_detector or detect_startup_modal_window
    gate = (
        gate_factory(version)
        if gate_factory is not None
        else RevitStartupGate(version, acquire_timeout_ms=acquire_timeout_ms)
    )
    try:
        gate.acquire()
    except TimeoutError as exc:
        print(
            f"[WARN] Timed out acquiring Startup Gate '{gate.mutex_name}' ({exc}); "
            "proceeding with early-boot watchdog."
        )

    proc = None
    timed_out = False
    modal_aborted = False
    proc_started_at = monotonic_fn()
    gate_acquired_at = proc_started_at

    def _kill_on_timeout():
        print(
            f"[TESTS] Test execution timed out after {timeout} seconds. "
            f"Killing worker PID tree {proc.pid}..."
        )
        _terminate_process_tree_and_wait(proc, tree_killer)

    try:
        effective_known = _build_effective_known_journals(resolved_journals_dir, known_journals, run_id)
        proc = popen_fn(cmd, stdout=out_f, stderr=out_f, env=worker_env)
        proc_started_at = monotonic_fn()
        gate_acquired_at = proc_started_at

        while True:
            if _poll_process(proc) is not None:
                gate.release()
                break

            startup_done, modal_reason = _detect_startup_modal_reason(
                proc.pid,
                resolved_journals_dir,
                run_id,
                effective_known,
                modal_win_detector,
            )
            if modal_reason:
                modal_aborted = True
                print(
                    f"[WARN] Startup failure modal detected in {modal_reason} for Revit {version} "
                    f"(worker PID {proc.pid}); killing PID tree and releasing Startup Gate..."
                )
                _terminate_process_tree_and_wait(proc, tree_killer)
                gate.release()
                break

            if startup_done:
                gate.release()
                break

            now = monotonic_fn()
            if gate.is_held and (now - gate_acquired_at) >= startup_hold_cap:
                print(
                    f"[TESTS] Startup Gate hold cap ({startup_hold_cap:.1f}s) expired for Revit {version}; "
                    "releasing Startup Gate while continuing early-boot monitoring."
                )
                gate.release()

            elapsed = now - proc_started_at
            if elapsed >= timeout:
                timed_out = True
                _kill_on_timeout()
                gate.release()
                break

            if poll_interval > 0:
                sleep_fn(min(poll_interval, max(0.0, timeout - elapsed)))
    finally:
        gate.release()

    if proc is not None and not timed_out and not modal_aborted and _poll_process(proc) is None:
        remaining = max(0.001, timeout - (monotonic_fn() - proc_started_at))
        try:
            proc.wait(timeout=remaining)
        except subprocess.TimeoutExpired:
            timed_out = True
            _kill_on_timeout()

    rc = proc.returncode if (proc is not None and proc.returncode is not None) else 1
    if modal_aborted and rc == 0:
        rc = 1
    return rc, timed_out, modal_aborted


def _collect_aborted_attempt_journals(journals_dir, known_set, attempt_run_id):
    """Collect newly created journals belonging to (or unattributed from) an aborted attempt."""
    added = []
    for candidate in _list_journal_files(journals_dir):
        norm = os.path.normcase(os.path.abspath(candidate))
        if norm in known_set:
            continue
        text = _read_journal_text_best_effort(candidate)
        if "SYNTHETIC_TEST_RUN:" in text:
            if attempt_run_id and _contains_run_id_marker(text, attempt_run_id):
                added.append(os.path.abspath(candidate))
        else:
            added.append(os.path.abspath(candidate))
    return added


def _append_snapshot_entries(snapshot_path, new_paths):
    if not snapshot_path or not new_paths:
        return
    try:
        existing = []
        if os.path.isfile(snapshot_path):
            with open(snapshot_path, "r", encoding="utf-8") as f:
                loaded = json.load(f)
            if isinstance(loaded, list):
                existing = [str(p) for p in loaded]
        seen = {os.path.normcase(os.path.abspath(p)) for p in existing}
        for path in new_paths:
            abs_path = os.path.abspath(path)
            norm = os.path.normcase(abs_path)
            if norm not in seen:
                existing.append(abs_path)
                seen.add(norm)
        with open(snapshot_path, "w", encoding="utf-8") as f:
            json.dump(existing, f, indent=2)
    except (OSError, ValueError):
        pass


def execute_revit_test_with_startup_gate(
    cmd,
    out_f,
    worker_env,
    version,
    run_id=None,
    journals_dir=None,
    known_journals=None,
    snapshot_path=None,
    timeout=660,
    startup_hold_cap=DEFAULT_STARTUP_GATE_HOLD_SECONDS,
    poll_interval=DEFAULT_STARTUP_POLL_INTERVAL_SECONDS,
    acquire_timeout_ms=DEFAULT_STARTUP_GATE_ACQUIRE_TIMEOUT_MS,
    max_retries=1,
    popen_fn=subprocess.Popen,
    tree_killer=kill_process_tree,
    gate_factory=None,
    window_modal_detector=None,
    monotonic_fn=time.monotonic,
    sleep_fn=time.sleep,
):
    """
    Execute a cold Revit test run under the per-version Startup Gate with fail-fast
    startup modal detection and bounded single retry (max_retries=1).
    """
    resolved_journals_dir = journals_dir or get_default_journals_dir(version, env=worker_env)
    attempt_known = set()
    if known_journals is not None:
        attempt_known = {os.path.normcase(os.path.abspath(p)) for p in known_journals}
    elif snapshot_path and os.path.isfile(snapshot_path):
        try:
            with open(snapshot_path, "r", encoding="utf-8") as f:
                loaded = json.load(f)
            if isinstance(loaded, list):
                attempt_known = {os.path.normcase(os.path.abspath(str(p))) for p in loaded}
        except (OSError, ValueError):
            attempt_known = set()

    total_attempts = max(0, int(max_retries)) + 1
    rc = 1
    timed_out = False
    modal_aborted = False
    effective_run_id = run_id

    for attempt in range(total_attempts):
        attempt_run_id = run_id if attempt == 0 else (f"{run_id}_retry{attempt}" if run_id else None)
        effective_run_id = attempt_run_id
        attempt_env = dict(worker_env)
        if attempt_run_id:
            attempt_env["SYNTHETIC_TEST_RUN_ID"] = str(attempt_run_id)

        if attempt > 0:
            print(
                f"[TESTS] Retrying Revit {version} cold startup (attempt {attempt + 1}/{total_attempts}, "
                f"run ID: {attempt_run_id})..."
            )
            if hasattr(out_f, "write"):
                try:
                    out_f.write(
                        f"\n[TESTS] Retrying Revit {version} after startup modal abort "
                        f"(attempt {attempt + 1}/{total_attempts}, run ID: {attempt_run_id})...\n"
                    )
                    out_f.flush()
                except Exception:
                    pass

        rc, timed_out, modal_aborted = _execute_revit_test_attempt(
            cmd,
            out_f,
            attempt_env,
            version=version,
            run_id=attempt_run_id,
            journals_dir=resolved_journals_dir,
            known_journals=attempt_known,
            timeout=timeout,
            startup_hold_cap=startup_hold_cap,
            poll_interval=poll_interval,
            acquire_timeout_ms=acquire_timeout_ms,
            popen_fn=popen_fn,
            tree_killer=tree_killer,
            gate_factory=gate_factory,
            window_modal_detector=window_modal_detector,
            monotonic_fn=monotonic_fn,
            sleep_fn=sleep_fn,
        )

        if not modal_aborted or attempt >= total_attempts - 1:
            break

        aborted_journals = _collect_aborted_attempt_journals(
            resolved_journals_dir, attempt_known, attempt_run_id
        )
        for jpath in aborted_journals:
            attempt_known.add(os.path.normcase(os.path.abspath(jpath)))
        _append_snapshot_entries(snapshot_path, aborted_journals)

    return rc, timed_out, effective_run_id, modal_aborted


def execute_revit_test_process(
    cmd,
    out_f,
    worker_env,
    timeout=660,
    popen_fn=subprocess.Popen,
    tree_killer=kill_process_tree,
    version=None,
    run_id=None,
    journals_dir=None,
    known_journals=None,
    startup_hold_cap=DEFAULT_STARTUP_GATE_HOLD_SECONDS,
    poll_interval=DEFAULT_STARTUP_POLL_INTERVAL_SECONDS,
):
    """Launch dotnet test for a Revit worker and kill only its PID tree on timeout."""
    if version is not None:
        rc, timed_out, _ = _execute_revit_test_attempt(
            cmd,
            out_f,
            dict(worker_env),
            version=version,
            run_id=run_id,
            journals_dir=journals_dir,
            known_journals=known_journals,
            timeout=timeout,
            startup_hold_cap=startup_hold_cap,
            poll_interval=poll_interval,
            popen_fn=popen_fn,
            tree_killer=tree_killer,
        )
        return rc, timed_out

    proc = popen_fn(cmd, stdout=out_f, stderr=out_f, env=worker_env)
    timed_out = False
    try:
        proc.wait(timeout=timeout)
    except subprocess.TimeoutExpired:
        timed_out = True
        print(f"[TESTS] Test execution timed out after {timeout} seconds. Killing worker PID tree {proc.pid}...")
        _terminate_process_tree_and_wait(proc, tree_killer)
    return (proc.returncode if proc.returncode is not None else 1), timed_out


def build_trx_filename(version_key, tfm="", is_multi_tfm=False):
    if is_multi_tfm and tfm:
        return f"results_{version_key}_{tfm}.trx"
    return f"results_{version_key}.trx"


def build_log_filename(version_key, tfm="", is_multi_tfm=False):
    if is_multi_tfm and tfm:
        return f"output_{version_key}_{tfm}.log"
    return f"output_{version_key}.log"


def resolve_isolated_test_assembly_path(run_build_root, proj_name, run_id, tfm="", is_multi_tfm=False):
    """Resolve the compiled test DLL inside the isolated per-run MSBuild output root."""
    out_dir = os.path.join(run_build_root, "bin", f"{proj_name}_{run_id}")
    if is_multi_tfm and tfm:
        return os.path.join(out_dir, tfm, f"{proj_name}.dll")
    direct_path = os.path.join(out_dir, f"{proj_name}.dll")
    if tfm and not os.path.isfile(direct_path):
        tfm_path = os.path.join(out_dir, tfm, f"{proj_name}.dll")
        if os.path.isfile(tfm_path):
            return tfm_path
    return direct_path


def get_next_revit_version_map(run_projects, target_framework=""):
    """Map each Revit target key in run_projects to its subsequent Revit target key, or None."""
    target_tfm = (target_framework or "").strip()
    ordered_revit_keys = []
    for ver, csproj_list in run_projects.items():
        if ver.lower() in ("logic", "legacymock"):
            continue
        if target_tfm:
            if isinstance(csproj_list, str):
                csproj_list = [csproj_list]
            has_matching_tfm = False
            for csproj_path in csproj_list:
                proj_tfms, _ = get_project_target_frameworks(csproj_path)
                if any(t and t.casefold() == target_tfm.casefold() for t in proj_tfms):
                    has_matching_tfm = True
                    break
            if not has_matching_tfm:
                continue
        ordered_revit_keys.append(ver)

    next_map = {}
    for idx, ver in enumerate(ordered_revit_keys):
        next_map[ver] = ordered_revit_keys[idx + 1] if idx + 1 < len(ordered_revit_keys) else None
    return next_map


def build_worker_pool_run_command(
    pool_cmd,
    version,
    assembly_path,
    trx_path,
    project_root,
    target_class="",
    category="",
    shards=None,
    no_shim=False,
    next_version=None,
    python_exe=sys.executable,
):
    """Build the RevitWorkerPool run CLI command passing raw filter/category for ECMA-335 discovery."""
    if pool_cmd.casefold().endswith(".py"):
        cmd = [python_exe, pool_cmd]
    else:
        cmd = [pool_cmd]

    cmd.extend(
        [
            "run",
            "--version",
            str(version),
            "--assembly",
            assembly_path,
            "--trx",
            trx_path,
            "--project-root",
            project_root,
            "--json",
            "--diagnostics",
        ]
    )

    clean_filter = (target_class or "").strip()
    if clean_filter:
        cmd.extend(["--filter", clean_filter])

    clean_category = (category or "").strip()
    if clean_category:
        cmd.extend(["--category", clean_category])

    if shards is not None:
        cmd.extend(["--shards", str(shards)])

    if no_shim:
        cmd.append("--no-shim")

    if next_version is not None and str(next_version).strip():
        cmd.extend(["--next-version", str(next_version).strip()])

    return cmd


def parse_worker_pool_json_output(stdout_text):
    """Extract and parse the last non-empty JSON object line from RevitWorkerPool stdout."""
    if not stdout_text:
        return None
    lines = [ln.strip() for ln in stdout_text.splitlines() if ln.strip()]
    for line in reversed(lines):
        if line.startswith("{"):
            try:
                parsed = json.loads(line)
                if isinstance(parsed, dict):
                    return parsed
            except ValueError:
                continue
    return None


def _write_pool_log(out_file_path, pool_args, stderr_text, stdout_text, parsed_json):
    """Write pool stderr diagnostics and structured summary to output_<ver>.log."""
    with open(out_file_path, "w", encoding="utf-8") as out_f:
        out_f.write(f"[POOL] Command: {' '.join(pool_args)}\n")
        if stderr_text:
            out_f.write("\n=== Worker Pool Diagnostics (stderr) ===\n")
            out_f.write(stderr_text)
            if not stderr_text.endswith("\n"):
                out_f.write("\n")
        out_f.write("\n=== Worker Pool Result Summary ===\n")
        if parsed_json is not None:
            out_f.write(json.dumps(parsed_json, indent=2) + "\n")
        elif stdout_text:
            out_f.write(stdout_text)
            if not stdout_text.endswith("\n"):
                out_f.write("\n")


def execute_revit_tests_via_pool(
    pool_cmd,
    version,
    dll_path,
    trx_path,
    workspace_path,
    out_file_path,
    target_class="",
    category="",
    shards=None,
    no_shim=False,
    next_version=None,
    runner=None,
    journal_gate_fn=None,
    expect_shim=None,
):
    """
    Execute a Revit test target via RevitWorkerPool run, enforcing fail-closed shim
    semantics and per-shard --worker-id --assert-clean journal cleanliness gating.
    Returns (executed_target, had_error, pool_returncode).
    """
    run_fn = subprocess.run if runner is None else runner
    gate_fn = run_worker_journal_cleanliness_gate if journal_gate_fn is None else journal_gate_fn

    pool_args = build_worker_pool_run_command(
        pool_cmd=pool_cmd,
        version=version,
        assembly_path=dll_path,
        trx_path=trx_path,
        project_root=workspace_path,
        target_class=target_class,
        category=category,
        shards=shards,
        no_shim=no_shim,
        next_version=next_version,
    )

    try:
        res = run_fn(pool_args, capture_output=True, text=True)
    except OSError as exc:
        _write_pool_log(out_file_path, pool_args, str(exc), "", None)
        print(f"[ERROR] Failed to invoke RevitWorkerPool for Revit {version}: {exc}")
        return False, True, 2

    pool_returncode = res.returncode
    stderr_text = res.stderr or ""
    stdout_text = res.stdout or ""
    parsed_json = parse_worker_pool_json_output(stdout_text)
    _write_pool_log(out_file_path, pool_args, stderr_text, stdout_text, parsed_json)

    if parsed_json is None:
        detail = stderr_text.strip() or stdout_text.strip() or "empty stdout"
        print(
            f"[ERROR] RevitWorkerPool run for Revit {version} exited with code {pool_returncode} "
            f"without valid JSON output ({detail}). See {out_file_path}"
        )
        return False, True, pool_returncode if pool_returncode != 0 else 2

    json_exit_code = parsed_json.get("exitCode")
    json_success = parsed_json.get("success")
    json_error = (parsed_json.get("error") or "").strip()
    zero_matches = bool(parsed_json.get("zeroMatches"))
    total_tests = int(parsed_json.get("totalTests") or 0)
    raw_worker_ids = parsed_json.get("workerIds")
    worker_ids = (
        [str(w).strip() for w in raw_worker_ids if str(w).strip()]
        if isinstance(raw_worker_ids, list)
        else []
    )

    effective_rc = json_exit_code if json_exit_code is not None else pool_returncode
    is_fail_closed = (
        pool_returncode == 2
        or json_exit_code == 2
        or (effective_rc != 0 and total_tests == 0 and not zero_matches)
        or (json_success is False and total_tests == 0 and not zero_matches)
        or (zero_matches and (effective_rc != 0 or bool(json_error)))
    )

    if is_fail_closed:
        err_detail = json_error or stderr_text.strip() or f"exitCode={effective_rc}"
        print(
            f"[ERROR] RevitWorkerPool run for Revit {version} failed closed "
            f"(exitCode={effective_rc}): {err_detail}. See {out_file_path}"
        )
        if not zero_matches and worker_ids:
            for wid in worker_ids:
                is_clean, gate_output = gate_fn(version, wid)
                if gate_output:
                    print(gate_output)
                if not is_clean:
                    print(
                        f"[ERROR] Revit {version} worker '{wid}' failed post-run journal "
                        "cleanliness gate (--assert-clean)."
                    )
        return False, True, effective_rc

    if zero_matches:
        print(
            f"[TESTS] 0 matching tests for {version} "
            f"(filter: {target_class or '-'}, category: {category or '-'})."
        )
        return False, False, 0

    resolved_expect_shim = (
        not bool(no_shim or os.environ.get("SYNTHETIC_CLEAN_SHIM", "").strip() == "0")
        if expect_shim is None
        else bool(expect_shim)
    )
    if resolved_expect_shim and parsed_json.get("shimEnabled") is False:
        print(
            f"[ERROR] RevitWorkerPool reported shimEnabled=false for Revit {version} "
            "while Clean-Launch Shim was required."
        )
        if worker_ids:
            for wid in worker_ids:
                is_clean, gate_output = gate_fn(version, wid)
                if gate_output:
                    print(gate_output)
                if not is_clean:
                    print(
                        f"[ERROR] Revit {version} worker '{wid}' failed post-run journal "
                        "cleanliness gate (--assert-clean)."
                    )
        return False, True, 2

    had_error = False
    if not worker_ids:
        had_error = True
        print(
            f"[ERROR] Revit {version} worker pool run completed (totalTests={total_tests}) "
            "without returning any workerIds for journal cleanliness gating."
        )
    else:
        for wid in worker_ids:
            is_clean, gate_output = gate_fn(version, wid)
            if gate_output:
                print(gate_output)
            if not is_clean:
                had_error = True
                print(
                    f"[ERROR] Revit {version} worker '{wid}' failed post-run journal "
                    "cleanliness gate (--assert-clean)."
                )

    if pool_returncode != 0 and not os.path.exists(trx_path):
        had_error = True
        print(
            f"[ERROR] RevitWorkerPool exited with code {pool_returncode} without generating "
            f"{os.path.basename(trx_path)}. See {out_file_path}"
        )

    return True, had_error, pool_returncode


def run_test_suite(
    target_class="",
    target_version="",
    category="",
    no_pool=False,
    target_framework="",
    no_shim=False,
    shards=None,
):
    if shards is not None and shards < 1:
        print(f"[ERROR] --shards must be >= 1 (got {shards}).")
        sys.exit(2)

    workspace_path = os.getcwd()
    os.environ["SYNTHETIC_PROJECT_ROOT"] = workspace_path
    sdk_info = configure_dotnet_environment()
    msbuild_path = find_msbuild()

    # 1. Discover test projects dynamically
    test_dir = os.path.join(workspace_path, "tests")
    test_projects = discover_test_projects(test_dir)

    # Self-heal any leftover AddInsSettings.json.bak from older runs without mutating AddInsSettings.json
    for v in [ver for ver in test_projects if ver.isdigit()]:
        try:
            TempDisableThirdPartyAddins(v).restore_addins()
        except Exception:
            pass

    # Filter by version if specified (case-insensitive fallback)
    run_projects = select_run_projects(test_projects, target_version=target_version)
    if target_version and not run_projects:
        print(f"[ERROR] Target version '{target_version}' test project not found in {list(test_projects.keys())}")
        sys.exit(1)

    if not run_projects:
        print("[WARN] No test projects discovered to execute.")
        return

    # 2. Prepare per-run isolated build and results directories
    run_id = generate_run_id()
    run_build_root = os.path.join(tempfile.gettempdir(), "SyntheticBuilds", run_id)
    results_root = os.path.join(test_dir, "TestResults")
    run_results_dir = os.path.join(results_root, run_id)

    has_revit_targets = any(ver.lower() not in ("logic", "legacymock") for ver in run_projects)
    worker_mode = "cold"
    pool_cmd = None
    if has_revit_targets:
        worker_mode, pool_cmd, pool_msg = resolve_worker_mode(no_pool, workspace_path)
        print(pool_msg)
        if worker_mode == "cold" and shards is not None:
            print("[TESTS] Note: --shards is only used in RevitWorkerPool mode; running single cold worker.")

    dotnet_exe = "dotnet"
    if sdk_info:
        candidate_dotnet = os.path.join(sdk_info["dotnet_root"], "dotnet.exe")
        if os.path.exists(candidate_dotnet):
            dotnet_exe = candidate_dotnet

    executed_any = False
    had_execution_error = False
    had_nonzero_pool_rc = False
    target_framework = (target_framework or "").strip()
    next_revit_versions = get_next_revit_version_map(run_projects, target_framework=target_framework)

    for ver, csproj_list in run_projects.items():
        if isinstance(csproj_list, str):
            csproj_list = [csproj_list]
        is_headless = ver.lower() in ("logic", "legacymock")

        for csproj_path in csproj_list:
            proj_tfms, is_multi_tfm = get_project_target_frameworks(csproj_path)
            if target_framework:
                selected_tfms = [t for t in proj_tfms if t and t.casefold() == target_framework.casefold()]
                if not selected_tfms:
                    continue
            else:
                selected_tfms = proj_tfms

            if not is_headless and worker_mode == "pool":
                # RevitWorkerPool performs authoritative cross-TFM ECMA-335 metadata discovery (#231).
                filter_expr, matched_zero = "", False
            else:
                filter_expr, matched_zero = build_test_filter(
                    is_logic=is_headless,
                    target_class=target_class,
                    category=category,
                    tests_dir=test_dir,
                    version=None if is_headless else ver,
                )

            if matched_zero:
                print(f"[TESTS] 0 matching tests for {ver} (category: {category}).")
                continue

            if is_headless or worker_mode != "pool":
                executed_any = True

            os.makedirs(run_build_root, exist_ok=True)
            os.makedirs(run_results_dir, exist_ok=True)

            proj_name = os.path.splitext(os.path.basename(csproj_path))[0]
            if is_headless:
                print(f"[TESTS] Building {proj_name} ({ver}) with MSBuild ({run_id}): {msbuild_path}...")
            else:
                print(f"[TESTS] Building {proj_name} for Revit {ver} with MSBuild ({run_id}): {msbuild_path}...")

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
                print(f"[TESTS] Build succeeded for {proj_name}.")
            except subprocess.CalledProcessError as e:
                print(f"[ERROR] Build failed for {proj_name}: {e}")
                print(f"[TESTS] Preserving failed isolated build root for inspection: {run_build_root}")
                rotate_test_results(results_root)
                sys.exit(1)

            for tfm_idx, tfm in enumerate(selected_tfms):
                log_name = build_trx_filename(ver, tfm=tfm, is_multi_tfm=is_multi_tfm)
                out_file_path = os.path.join(run_results_dir, build_log_filename(ver, tfm=tfm, is_multi_tfm=is_multi_tfm))

                if not is_headless and worker_mode == "pool":
                    trx_path = os.path.join(run_results_dir, log_name)
                    dll_path = resolve_isolated_test_assembly_path(
                        run_build_root,
                        proj_name,
                        run_id,
                        tfm=tfm,
                        is_multi_tfm=is_multi_tfm,
                    )
                    is_unshimmed = bool(no_shim or os.environ.get("SYNTHETIC_CLEAN_SHIM", "").strip() == "0")
                    guid_whitelister.whitelist_existing_addins(ver)
                    if is_unshimmed:
                        check_legacy_programdata_manifest(ver)

                    next_ver = (
                        next_revit_versions.get(ver)
                        if tfm_idx == len(selected_tfms) - 1
                        else None
                    )
                    shard_label = f", shards={shards}" if shards is not None else ""
                    shim_label = " [no-shim]" if is_unshimmed else ""
                    next_label = f", next={next_ver}" if next_ver else ""
                    print(f"[TESTS] Executing Revit {ver} tests via RevitWorkerPool{shim_label}{shard_label}{next_label}...")

                    ran_pool, pool_err, pool_rc = execute_revit_tests_via_pool(
                        pool_cmd=pool_cmd,
                        version=ver,
                        dll_path=dll_path,
                        trx_path=trx_path,
                        workspace_path=workspace_path,
                        out_file_path=out_file_path,
                        target_class=target_class,
                        category=category,
                        shards=shards,
                        no_shim=no_shim,
                        next_version=next_ver,
                        expect_shim=not is_unshimmed,
                    )
                    if ran_pool:
                        executed_any = True
                        if pool_rc != 0:
                            had_nonzero_pool_rc = True
                    if pool_err:
                        had_execution_error = True
                    continue

                if is_headless:
                    tfm_label = f" [{tfm}]" if is_multi_tfm and tfm else ""
                    print(f"[TESTS] Executing headless NUnit tests for {proj_name}{tfm_label}...")
                else:
                    print(f"[TESTS] Executing dotnet test for Revit {ver}...")

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
                if is_multi_tfm and tfm:
                    cmd.extend(["--framework", tfm])
                if filter_expr:
                    cmd.extend(["--filter", filter_expr])

                proc_returncode = 0
                if is_headless:
                    with open(out_file_path, "w", encoding="utf-8") as out_f:
                        res = subprocess.run(cmd, stdout=out_f, stderr=out_f)
                        proc_returncode = res.returncode
                else:
                    guid_whitelister.whitelist_existing_addins(ver)
                    check_legacy_programdata_manifest(ver)
                    snapshot_path = os.path.join(run_results_dir, f"known_journals_{ver}.json")
                    snapshot_known_journals(ver, snapshot_path)
                    worker_env = build_worker_env(workspace_path, run_id)
                    isolated_test_outdir = os.path.join(
                        run_build_root, "bin", f"{proj_name}_{run_id}"
                    )
                    timeout = 660  # 11 min: matches 10m NUnit.Timeout + 60s Revit boot buffer

                    with open(out_file_path, "w", encoding="utf-8") as out_f:
                        with RicaunBundleLease(testadapter_search_dirs=[isolated_test_outdir]):
                            print(f"[TESTS] Waiting for Revit {ver} test execution to complete (timeout: {timeout}s)...")
                            proc_returncode, timed_out, effective_run_id, modal_aborted = execute_revit_test_with_startup_gate(
                                cmd,
                                out_f,
                                worker_env,
                                version=ver,
                                run_id=run_id,
                                snapshot_path=snapshot_path,
                                timeout=timeout,
                            )
                            if timed_out or modal_aborted:
                                had_execution_error = True

                    is_clean, gate_output = run_journal_cleanliness_gate(ver, effective_run_id, snapshot_path)
                    if gate_output:
                        print(gate_output)
                    if not is_clean:
                        had_execution_error = True
                        print(f"[ERROR] Revit {ver} failed post-run journal cleanliness gate (--assert-clean).")

                if proc_returncode != 0 and not os.path.exists(os.path.join(run_results_dir, log_name)):
                    had_execution_error = True
                    print(f"[ERROR] Test runner exited with code {proc_returncode} without generating {log_name}. See {out_file_path}")

    if not executed_any and not had_execution_error:
        shutil.rmtree(run_build_root, ignore_errors=True)
        cleanup_revit_test_temp(run_id)
        rotate_test_results(results_root)
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

    if had_nonzero_pool_rc and not failing_tests and total_failed == 0 and not had_execution_error:
        had_execution_error = True
        print("[ERROR] RevitWorkerPool reported test failures, but TRX recorded 0 failed tests.")

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
        cleanup_revit_test_temp(run_id)
        if total_run > 0:
            print(f"## All tests passed successfully! [PASS] (Total: {total_passed})")
        else:
            print("## No tests were executed. [WARN]")
        sys.exit(0)


def parse_cli_args(argv=None):
    """Parse CLI arguments for test_executor.py."""
    import argparse as _ap

    parser = _ap.ArgumentParser(description="Run Synthetic test suite.")
    parser.add_argument(
        "--filter",
        dest="target_class",
        default="",
        help="FullyQualifiedName filter (class or method name fragment)",
    )
    parser.add_argument(
        "--version",
        dest="target_version",
        default="",
        help="Revit version to target (e.g. 2026 or Logic). Omit to run all.",
    )
    parser.add_argument(
        "--category",
        dest="category",
        default="",
        help="NUnit [Category] filter",
    )
    parser.add_argument(
        "--framework",
        dest="target_framework",
        default="",
        help="Target framework to execute for multi-TFM test projects (e.g. net48 or net8.0-windows8.0)",
    )
    parser.add_argument(
        "--no-pool",
        dest="no_pool",
        action="store_true",
        default=False,
        help="Bypass optional RevitWorkerPool and use isolated cold Revit workers",
    )
    parser.add_argument(
        "--no-shim",
        dest="no_shim",
        action="store_true",
        default=False,
        help="Disable Clean-Launch Shim in RevitWorkerPool mode (uses StandardProcessLauncher + Startup Gate)",
    )
    parser.add_argument(
        "--shards",
        dest="shards",
        type=int,
        default=None,
        help="Override Test Shard worker count for RevitWorkerPool (>= 1)",
    )
    # Legacy positional support: test_executor.py [class] [version]
    parser.add_argument("pos_class", nargs="?", default="")
    parser.add_argument("pos_version", nargs="?", default="")

    args = parser.parse_args(argv)
    if args.shards is not None and args.shards < 1:
        parser.error("--shards must be >= 1.")

    args.target_class = (args.target_class or args.pos_class or "").strip()
    args.target_version = (args.target_version or args.pos_version or "").strip()
    args.category = (args.category or "").strip()
    args.target_framework = (args.target_framework or "").strip()
    if args.target_class == "DefaultAlignmentTests":
        args.target_class = ""
    return args


if __name__ == "__main__":
    _args = parse_cli_args()
    run_test_suite(
        _args.target_class,
        _args.target_version,
        category=_args.category,
        no_pool=_args.no_pool,
        target_framework=_args.target_framework,
        no_shim=_args.no_shim,
        shards=_args.shards,
    )

