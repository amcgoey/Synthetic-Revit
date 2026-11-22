import ctypes
from ctypes import wintypes
import os
import subprocess
import tempfile
import time
import unittest

REPO_ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
DEPLOY_SERVER_BAT = os.path.join(REPO_ROOT, "build", "Deploy-To-INC-Server.template.bat")
INSTALL_WORKSTATION_BAT = os.path.join(REPO_ROOT, "build", "Install-Synthetic-Workstation.template.bat")


def _write_bytes(path, data):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "wb") as f:
        f.write(data)


def _read_bytes(path):
    with open(path, "rb") as f:
        return f.read()


class _Win32ExclusiveLock:
    """Context manager holding a real Win32 CreateFileW exclusive lock (dwShareMode=0)."""

    GENERIC_READ = 0x80000000
    GENERIC_WRITE = 0x40000000
    OPEN_EXISTING = 3
    FILE_ATTRIBUTE_NORMAL = 0x80
    INVALID_HANDLE_VALUE = ctypes.c_void_p(-1).value

    def __init__(self, path):
        self.path = path
        self.handle = None

        kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
        kernel32.CreateFileW.argtypes = [
            wintypes.LPCWSTR,
            wintypes.DWORD,
            wintypes.DWORD,
            ctypes.c_void_p,
            wintypes.DWORD,
            wintypes.DWORD,
            wintypes.HANDLE,
        ]
        kernel32.CreateFileW.restype = wintypes.HANDLE
        kernel32.CloseHandle.argtypes = [wintypes.HANDLE]
        kernel32.CloseHandle.restype = wintypes.BOOL
        self._kernel32 = kernel32

    def __enter__(self):
        handle = self._kernel32.CreateFileW(
            self.path,
            self.GENERIC_READ | self.GENERIC_WRITE,
            0,  # dwShareMode = 0 (exclusive lock)
            None,
            self.OPEN_EXISTING,
            self.FILE_ATTRIBUTE_NORMAL,
            None,
        )
        if handle == self.INVALID_HANDLE_VALUE or not handle:
            err = ctypes.get_last_error()
            raise OSError(err, f"CreateFileW failed for '{self.path}' (Win32 error {err})")
        self.handle = handle
        return self

    def __exit__(self, exc_type, exc_val, exc_tb):
        if self.handle is not None:
            self._kernel32.CloseHandle(self.handle)
            self.handle = None


class TestDeployScripts(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.root = self.tmp.name

    def tearDown(self):
        self.tmp.cleanup()

    def _run_bat(self, bat_path, env_overrides):
        env = os.environ.copy()
        env.update(env_overrides)
        return subprocess.run(
            ["cmd.exe", "/c", bat_path],
            cwd=self.root,
            env=env,
            capture_output=True,
            text=True,
        )

    def test_deploy_to_inc_server_mirrors_payloads_manifests_and_overlays_inc_settings(self):
        local_out = os.path.join(self.root, "local_output")
        unc_root = os.path.join(self.root, "unc_share")

        # Bootstrap/v1
        for year in ("2023", "2026", "2027"):
            _write_bytes(
                os.path.join(local_out, "Synthetic", "Bootstrap", "v1", f"Synthetic.Bootstrap{year}.dll"),
                f"BOOTSTRAP_{year}".encode("ascii"),
            )

        # Payload/<Year> + default SyntheticWorksets.xlsx + stray .addin that must be excluded
        for year in ("2023", "2026", "2027"):
            _write_bytes(
                os.path.join(local_out, "Synthetic", "Payload", year, f"Synthetic{year}.dll"),
                f"PAYLOAD_{year}".encode("ascii"),
            )
            _write_bytes(
                os.path.join(local_out, "Synthetic", "Payload", year, "SyntheticWorksets.xlsx"),
                f"DEFAULT_WORKSETS_{year}".encode("ascii"),
            )
        _write_bytes(
            os.path.join(local_out, "Synthetic", "Payload", "2026", "stray.addin"),
            b"<RevitAddIns />",
        )

        # Release manifests
        for year in ("2023", "2026", "2027"):
            _write_bytes(
                os.path.join(local_out, "Manifest for Deploy", f"Synthetic{year}.addin"),
                f"<Addin>{year}</Addin>".encode("ascii"),
            )

        # Studio INC Settings
        _write_bytes(
            os.path.join(local_out, "INC Settings", "INC Worksets.xlsx"),
            b"STUDIO_WORKSETS",
        )
        _write_bytes(
            os.path.join(local_out, "INC Settings", "SyntheticSettings.json"),
            b'{"Studio":"INC"}',
        )

        # Pre-seed stale binary on UNC to confirm /MIR removes it
        _write_bytes(
            os.path.join(unc_root, "Synthetic", "Payload", "2026", "stale_old.dll"),
            b"STALE",
        )

        res = self._run_bat(
            DEPLOY_SERVER_BAT,
            {
                "LOCAL_OUTPUT_PATH": local_out,
                "UNC_SERVER_PATH": unc_root,
                "NO_PAUSE": "1",
            },
        )
        self.assertEqual(res.returncode, 0, msg=res.stdout + res.stderr)

        # Verify stale file purged and stray .addin excluded
        self.assertFalse(os.path.exists(os.path.join(unc_root, "Synthetic", "Payload", "2026", "stale_old.dll")))
        self.assertFalse(os.path.exists(os.path.join(unc_root, "Synthetic", "Payload", "2026", "stray.addin")))

        for year in ("2023", "2026", "2027"):
            self.assertEqual(
                _read_bytes(os.path.join(unc_root, "Synthetic", "Bootstrap", "v1", f"Synthetic.Bootstrap{year}.dll")),
                f"BOOTSTRAP_{year}".encode("ascii"),
            )
            self.assertEqual(
                _read_bytes(os.path.join(unc_root, "Manifest for Deploy", f"Synthetic{year}.addin")),
                f"<Addin>{year}</Addin>".encode("ascii"),
            )
            payload_year_dir = os.path.join(unc_root, "Synthetic", "Payload", year)
            self.assertEqual(
                _read_bytes(os.path.join(payload_year_dir, f"Synthetic{year}.dll")),
                f"PAYLOAD_{year}".encode("ascii"),
            )
            self.assertEqual(
                _read_bytes(os.path.join(payload_year_dir, "SyntheticWorksets.xlsx")),
                f"DEFAULT_WORKSETS_{year}".encode("ascii"),
            )
            self.assertEqual(
                _read_bytes(os.path.join(payload_year_dir, "INC Worksets.xlsx")),
                b"STUDIO_WORKSETS",
            )
            self.assertEqual(
                _read_bytes(os.path.join(payload_year_dir, "SyntheticSettings.json")),
                b'{"Studio":"INC"}',
            )

    def test_install_workstation_purges_legacy_routes_versions_and_skips_uninstalled(self):
        unc_root = os.path.join(self.root, "unc_share")
        prog_data = os.path.join(self.root, "ProgramData")
        prog_files = os.path.join(self.root, "Program Files")
        revit_root = os.path.join(self.root, "Autodesk")

        # Staged UNC years: 2023, 2025, 2026, 2027
        for year in ("2023", "2025", "2026", "2027"):
            _write_bytes(
                os.path.join(unc_root, "Synthetic", "Bootstrap", "v1", f"Synthetic.Bootstrap{year}.dll"),
                f"BOOT_{year}".encode("ascii"),
            )
            _write_bytes(
                os.path.join(unc_root, "Synthetic", "Payload", year, f"Synthetic{year}.dll"),
                f"PAY_{year}".encode("ascii"),
            )
            _write_bytes(
                os.path.join(unc_root, "Manifest for Deploy", f"Synthetic{year}.addin"),
                f"<Addin>{year}</Addin>".encode("ascii"),
            )

        # Installed Revit versions on workstation: 2023, 2026, 2027 (2025 intentionally absent)
        for installed in ("2023", "2026", "2027"):
            os.makedirs(os.path.join(revit_root, f"Revit {installed}"), exist_ok=True)

        # Seed retired 2022 manifest and legacy flat root artifacts in ProgramData
        _write_bytes(os.path.join(prog_data, "Autodesk", "Revit", "Addins", "2022", "Synthetic2022.addin"), b"OLD2022")
        legacy_root = os.path.join(prog_data, "Autodesk", "Revit", "Addins", "Synthetic")
        for legacy_file in (
            "Synthetic2024.dll",
            "Synthetic2026.pdb",
            "Newtonsoft.Json.dll",
            "SyntheticSettings.json",
            "SyntheticWorksets.xlsx",
            "INC Worksets.xlsx",
            "MergeDuplicates_Step6_ExecutionResult.json",
        ):
            _write_bytes(os.path.join(legacy_root, legacy_file), b"LEGACY")
        _write_bytes(os.path.join(legacy_root, "Assets", "icon.png"), b"ICON")

        # Pre-seed Bootstrap/v1 and Payload markers that legacy cleanup must preserve
        _write_bytes(os.path.join(legacy_root, "Bootstrap", "v1", "keep_bootstrap.txt"), b"KEEP_BOOT")
        _write_bytes(os.path.join(legacy_root, "Payload", "2024", "keep_payload.txt"), b"KEEP_PAY")

        res = self._run_bat(
            INSTALL_WORKSTATION_BAT,
            {
                "UNC_SERVER_PATH": unc_root,
                "PROGRAMDATA_ROOT": prog_data,
                "PROGRAMFILES_ROOT": prog_files,
                "REVIT_INSTALL_ROOT": revit_root,
                "NO_PAUSE": "1",
            },
        )
        self.assertEqual(res.returncode, 0, msg=res.stdout + res.stderr)

        # Legacy 2022 and flat artifacts purged
        self.assertFalse(os.path.exists(os.path.join(prog_data, "Autodesk", "Revit", "Addins", "2022", "Synthetic2022.addin")))
        for legacy_file in (
            "Synthetic2024.dll",
            "Synthetic2026.pdb",
            "Newtonsoft.Json.dll",
            "SyntheticSettings.json",
            "SyntheticWorksets.xlsx",
            "INC Worksets.xlsx",
            "MergeDuplicates_Step6_ExecutionResult.json",
            "Assets",
        ):
            self.assertFalse(os.path.exists(os.path.join(legacy_root, legacy_file)), f"Expected {legacy_file} purged")

        # Bootstrap/ and Payload/ preserved across legacy cleanup
        self.assertEqual(_read_bytes(os.path.join(legacy_root, "Bootstrap", "v1", "keep_bootstrap.txt")), b"KEEP_BOOT")
        self.assertEqual(_read_bytes(os.path.join(legacy_root, "Payload", "2024", "keep_payload.txt")), b"KEEP_PAY")

        # 2023 & 2026 routed to ProgramData
        for year in ("2023", "2026"):
            pd_addins = os.path.join(prog_data, "Autodesk", "Revit", "Addins")
            self.assertEqual(_read_bytes(os.path.join(pd_addins, year, f"Synthetic{year}.addin")), f"<Addin>{year}</Addin>".encode("ascii"))
            self.assertEqual(_read_bytes(os.path.join(pd_addins, "Synthetic", "Bootstrap", "v1", f"Synthetic.Bootstrap{year}.dll")), f"BOOT_{year}".encode("ascii"))
            self.assertEqual(_read_bytes(os.path.join(pd_addins, "Synthetic", "Payload", year, f"Synthetic{year}.dll")), f"PAY_{year}".encode("ascii"))
            self.assertFalse(os.path.exists(os.path.join(prog_files, "Autodesk", "Revit", "Addins", year)))

        # 2027 routed to Program Files
        pf_addins = os.path.join(prog_files, "Autodesk", "Revit", "Addins")
        self.assertEqual(_read_bytes(os.path.join(pf_addins, "2027", "Synthetic2027.addin")), b"<Addin>2027</Addin>")
        self.assertEqual(_read_bytes(os.path.join(pf_addins, "Synthetic", "Bootstrap", "v1", "Synthetic.Bootstrap2027.dll")), b"BOOT_2027")
        self.assertEqual(_read_bytes(os.path.join(pf_addins, "Synthetic", "Payload", "2027", "Synthetic2027.dll")), b"PAY_2027")
        self.assertFalse(os.path.exists(os.path.join(prog_data, "Autodesk", "Revit", "Addins", "2027")))

        # Uninstalled 2025 skipped
        self.assertFalse(os.path.exists(os.path.join(prog_data, "Autodesk", "Revit", "Addins", "2025")))
        self.assertFalse(os.path.exists(os.path.join(prog_files, "Autodesk", "Revit", "Addins", "2025")))

    def test_stage_while_open_updates_payload_while_bootstrap_holds_exclusive_win32_lock(self):
        unc_root = os.path.join(self.root, "unc_share")
        prog_data = os.path.join(self.root, "ProgramData")
        prog_files = os.path.join(self.root, "Program Files")
        revit_root = os.path.join(self.root, "Autodesk")
        os.makedirs(os.path.join(revit_root, "Revit 2026"), exist_ok=True)

        # Existing workstation Bootstrap/v1 and Payload/2026
        locked_bootstrap = os.path.join(
            prog_data, "Autodesk", "Revit", "Addins", "Synthetic", "Bootstrap", "v1", "Synthetic.Bootstrap2026.dll"
        )
        local_payload = os.path.join(
            prog_data, "Autodesk", "Revit", "Addins", "Synthetic", "Payload", "2026", "Synthetic2026.dll"
        )
        _write_bytes(locked_bootstrap, b"OLD_LOCKED_BOOTSTRAP_V1")
        _write_bytes(local_payload, b"OLD_PAYLOAD")

        # Newer UNC Bootstrap/v1 and Payload/2026
        unc_bootstrap = os.path.join(unc_root, "Synthetic", "Bootstrap", "v1", "Synthetic.Bootstrap2026.dll")
        _write_bytes(unc_bootstrap, b"NEW_BOOTSTRAP_SHOULD_BE_SKIPPED")
        future_ts = time.time() + 120
        os.utime(unc_bootstrap, (future_ts, future_ts))

        _write_bytes(os.path.join(unc_root, "Synthetic", "Payload", "2026", "Synthetic2026.dll"), b"NEW_PAYLOAD_UPDATED")
        _write_bytes(os.path.join(unc_root, "Manifest for Deploy", "Synthetic2026.addin"), b"<Addin>2026</Addin>")

        with _Win32ExclusiveLock(locked_bootstrap):
            res = self._run_bat(
                INSTALL_WORKSTATION_BAT,
                {
                    "UNC_SERVER_PATH": unc_root,
                    "PROGRAMDATA_ROOT": prog_data,
                    "PROGRAMFILES_ROOT": prog_files,
                    "REVIT_INSTALL_ROOT": revit_root,
                    "NO_PAUSE": "1",
                },
            )

        self.assertEqual(res.returncode, 0, msg=res.stdout + res.stderr)
        self.assertEqual(_read_bytes(local_payload), b"NEW_PAYLOAD_UPDATED")
        self.assertEqual(_read_bytes(locked_bootstrap), b"OLD_LOCKED_BOOTSTRAP_V1")

    def test_robocopy_failure_propagates_nonzero_exit_codes(self):
        # 1. Deploy-To-INC-Server with missing LOCAL_OUTPUT_PATH
        res_missing_local = self._run_bat(
            DEPLOY_SERVER_BAT,
            {
                "LOCAL_OUTPUT_PATH": os.path.join(self.root, "does_not_exist"),
                "UNC_SERVER_PATH": os.path.join(self.root, "unc_target"),
                "NO_PAUSE": "1",
            },
        )
        self.assertGreaterEqual(res_missing_local.returncode, 8, msg=res_missing_local.stdout + res_missing_local.stderr)

        # 2. Deploy-To-INC-Server with missing INC Settings inside for /d overlay loop
        local_out = os.path.join(self.root, "partial_local")
        unc_target = os.path.join(self.root, "partial_unc")
        _write_bytes(os.path.join(local_out, "Synthetic", "Payload", "2026", "Synthetic2026.dll"), b"PAY")
        _write_bytes(os.path.join(local_out, "Manifest for Deploy", "Synthetic2026.addin"), b"<Addin />")
        res_missing_inc = self._run_bat(
            DEPLOY_SERVER_BAT,
            {
                "LOCAL_OUTPUT_PATH": local_out,
                "UNC_SERVER_PATH": unc_target,
                "NO_PAUSE": "1",
            },
        )
        self.assertGreaterEqual(res_missing_inc.returncode, 8, msg=res_missing_inc.stdout + res_missing_inc.stderr)

        # 3. Install-Synthetic-Workstation with missing UNC_SERVER_PATH
        res_missing_unc = self._run_bat(
            INSTALL_WORKSTATION_BAT,
            {
                "UNC_SERVER_PATH": os.path.join(self.root, "missing_unc"),
                "PROGRAMDATA_ROOT": os.path.join(self.root, "pd"),
                "PROGRAMFILES_ROOT": os.path.join(self.root, "pf"),
                "REVIT_INSTALL_ROOT": os.path.join(self.root, "revit"),
                "NO_PAUSE": "1",
            },
        )
        self.assertNotEqual(res_missing_unc.returncode, 0, msg=res_missing_unc.stdout + res_missing_unc.stderr)

        # 4. Install-Synthetic-Workstation when Bootstrap/v1 is missing inside for /d subroutine
        broken_unc = os.path.join(self.root, "broken_unc")
        revit_root = os.path.join(self.root, "broken_revit")
        os.makedirs(os.path.join(revit_root, "Revit 2026"), exist_ok=True)
        _write_bytes(os.path.join(broken_unc, "Synthetic", "Payload", "2026", "Synthetic2026.dll"), b"PAY")
        _write_bytes(os.path.join(broken_unc, "Manifest for Deploy", "Synthetic2026.addin"), b"<Addin />")
        res_missing_boot = self._run_bat(
            INSTALL_WORKSTATION_BAT,
            {
                "UNC_SERVER_PATH": broken_unc,
                "PROGRAMDATA_ROOT": os.path.join(self.root, "pd2"),
                "PROGRAMFILES_ROOT": os.path.join(self.root, "pf2"),
                "REVIT_INSTALL_ROOT": revit_root,
                "NO_PAUSE": "1",
            },
        )
        self.assertGreaterEqual(res_missing_boot.returncode, 8, msg=res_missing_boot.stdout + res_missing_boot.stderr)


if __name__ == "__main__":
    unittest.main()
