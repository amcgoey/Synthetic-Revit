import importlib.util
import io
import json
import os
import subprocess
import sys
import tempfile
import time
import unittest
import zipfile

REPO_ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
SCRIPT_PATH = os.path.join(
    REPO_ROOT,
    ".agents",
    "skills",
    "revit-tests",
    "scripts",
    "test_executor.py",
)
WHITELISTER_PATH = os.path.join(
    REPO_ROOT,
    ".agents",
    "skills",
    "revit-tests",
    "scripts",
    "guid_whitelister.py",
)

spec = importlib.util.spec_from_file_location("test_executor", SCRIPT_PATH)
te = importlib.util.module_from_spec(spec)
spec.loader.exec_module(te)

wl_spec = importlib.util.spec_from_file_location("guid_whitelister", WHITELISTER_PATH)
gw = importlib.util.module_from_spec(wl_spec)
wl_spec.loader.exec_module(gw)


class TestDotnetSdkDiscovery(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.root = self.tmp.name

    def tearDown(self):
        self.tmp.cleanup()

    def _make_sdk(self, dotnet_root_name, version):
        sdk_dir = os.path.join(self.root, dotnet_root_name, "sdk", version)
        os.makedirs(os.path.join(sdk_dir, "Sdks"), exist_ok=True)
        return os.path.join(self.root, dotnet_root_name, "sdk")

    def test_parse_sdk_version_semantic_and_prerelease_ordering(self):
        v8 = te.parse_sdk_version("8.0.427")
        v10_rc1 = te.parse_sdk_version("10.0.100-rc.1")
        v10_rc2 = te.parse_sdk_version("10.0.100-rc.2")
        v10_rc10 = te.parse_sdk_version("10.0.100-rc.10")
        v10_stable = te.parse_sdk_version("10.0.100")

        self.assertIsNotNone(v8)
        self.assertGreater(v10_rc1, v8)
        self.assertGreater(v10_rc10, v10_rc2)
        self.assertGreater(v10_stable, v10_rc10)
        self.assertIsNone(te.parse_sdk_version("NuGetFallbackFolder"))
        self.assertIsNone(te.parse_sdk_version("not-a-version"))

    def test_find_best_dotnet_sdk_skips_missing_roots_and_picks_highest_semantic(self):
        missing_root = os.path.join(self.root, "missing", "sdk")
        local_sdk_root = self._make_sdk("local_dotnet", "8.0.427")
        pf_sdk_root = self._make_sdk("pf_dotnet", "10.0.100-rc.1")
        os.makedirs(os.path.join(pf_sdk_root, "NuGetFallbackFolder"), exist_ok=True)

        best = te.find_best_dotnet_sdk([missing_root, local_sdk_root, pf_sdk_root])

        self.assertIsNotNone(best)
        self.assertEqual(best["version"], "10.0.100-rc.1")
        self.assertEqual(best["dotnet_root"], os.path.join(self.root, "pf_dotnet"))

    def test_configure_dotnet_environment_binds_winning_root_and_path(self):
        local_sdk_root = self._make_sdk("local_dotnet", "8.0.427")
        pf_sdk_root = self._make_sdk("pf_dotnet", "10.0.100")
        env = {"PATH": r"C:\Windows\System32"}

        best = te.configure_dotnet_environment(env=env, sdk_roots=[local_sdk_root, pf_sdk_root])

        expected_dotnet_root = os.path.join(self.root, "pf_dotnet")
        self.assertEqual(best["version"], "10.0.100")
        self.assertEqual(env["DOTNET_ROOT"], expected_dotnet_root)
        self.assertTrue(env["PATH"].startswith(expected_dotnet_root + os.pathsep))
        self.assertEqual(
            env["MSBuildSdksPath"],
            os.path.join(pf_sdk_root, "10.0.100", "Sdks"),
        )
        self.assertEqual(env["MSBuildEnableWorkloadResolver"], "false")


class TestCategoryAndFilterComposition(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.tests_dir = os.path.join(self.tmp.name, "tests")
        os.makedirs(os.path.join(self.tests_dir, "SyntheticTests.Shared", "Modules", "Smoke"), exist_ok=True)
        os.makedirs(os.path.join(self.tests_dir, "SyntheticTests.Shared", "Modules", "SheetIndex"), exist_ok=True)
        os.makedirs(os.path.join(self.tests_dir, "SyntheticTests2026"), exist_ok=True)

        with open(
            os.path.join(self.tests_dir, "SyntheticTests.Shared", "Modules", "Smoke", "ClassLevelTests.cs"),
            "w",
            encoding="utf-8",
        ) as f:
            f.write(
                """
using NUnit.Framework;

namespace SyntheticTests.Modules.Smoke
{
    [TestFixture]
    [Category("Smoke")]
    public class AlphaSmokeTests
    {
        [Test]
        public void RunsFirst() {}
    }
}
"""
            )

        with open(
            os.path.join(self.tests_dir, "SyntheticTests2026", "MethodLevelTests.cs"),
            "w",
            encoding="utf-8",
        ) as f:
            f.write(
                """
using NUnit.Framework;

namespace SyntheticTests
{
    public class BetaRevitTests
    {
        [Test, Category("Smoke")]
        public void FastCheck() {}

        [Test]
        [Category("Slow")]
        public void SlowCheck() {}
    }
}
"""
            )

        # Excluded from Revit builds per tests/Directory.Build.targets
        with open(
            os.path.join(self.tests_dir, "SyntheticTests.Shared", "Modules", "SheetIndex", "ExcludedTests.cs"),
            "w",
            encoding="utf-8",
        ) as f:
            f.write(
                """
[Category("Smoke")]
public class ExcludedSheetIndexTests {}
"""
            )

    def tearDown(self):
        self.tmp.cleanup()

    def test_logic_category_and_filter_composition(self):
        expr_cat_only, zero1 = te.build_test_filter(is_logic=True, category="Smoke")
        expr_both, zero2 = te.build_test_filter(is_logic=True, target_class="Ribbon", category="Smoke")
        expr_filter_only, zero3 = te.build_test_filter(is_logic=True, target_class="Ribbon")

        self.assertFalse(zero1)
        self.assertFalse(zero2)
        self.assertFalse(zero3)
        self.assertEqual(expr_cat_only, "TestCategory=Smoke")
        self.assertEqual(expr_both, "TestCategory=Smoke&(FullyQualifiedName~Ribbon)")
        self.assertEqual(expr_filter_only, "FullyQualifiedName~Ribbon")

    def test_revit_category_parenthesized_expression_and_filter_composition(self):
        expr_cat, zero1 = te.build_test_filter(
            is_logic=False,
            category="Smoke",
            tests_dir=self.tests_dir,
            version="2026",
        )
        expr_both, zero2 = te.build_test_filter(
            is_logic=False,
            target_class="Smoke",
            category="Smoke",
            tests_dir=self.tests_dir,
            version="2026",
        )

        self.assertFalse(zero1)
        self.assertFalse(zero2)
        self.assertEqual(
            expr_cat,
            "(FullyQualifiedName~AlphaSmokeTests|FullyQualifiedName~BetaRevitTests.FastCheck)",
        )
        self.assertEqual(
            expr_both,
            "(FullyQualifiedName~AlphaSmokeTests|FullyQualifiedName~BetaRevitTests.FastCheck)&(FullyQualifiedName~Smoke)",
        )

    def test_revit_zero_match_category_short_circuits_cleanly(self):
        expr, zero = te.build_test_filter(
            is_logic=False,
            category="DoesNotExist",
            tests_dir=self.tests_dir,
            version="2026",
        )
        self.assertTrue(zero)
        self.assertEqual(expr, "")

    def test_cli_zero_match_category_exits_zero_with_message(self):
        res = subprocess.run(
            [sys.executable, SCRIPT_PATH, "--version", "2026", "--category", "__NoSuchCategory__"],
            cwd=REPO_ROOT,
            capture_output=True,
            text=True,
        )
        self.assertEqual(res.returncode, 0, msg=res.stdout + res.stderr)
        self.assertIn("0 matching tests", res.stdout)


class TestPoolFallbackAndResultRotation(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.root = self.tmp.name

    def tearDown(self):
        self.tmp.cleanup()

    def test_no_pool_probe_and_fallback(self):
        mode, cmd, msg = te.resolve_worker_mode(no_pool=False, workspace_path=self.root, env={})
        self.assertEqual(mode, "cold")
        self.assertIsNone(cmd)
        self.assertIn("falling back", msg.lower())

        pool_mode, pool_cmd, _ = te.resolve_worker_mode(
            no_pool=False,
            workspace_path=self.root,
            env={"SYNTHETIC_WORKER_POOL_CMD": "custom-pool.exe"},
        )
        self.assertEqual(pool_mode, "pool")
        self.assertEqual(pool_cmd, "custom-pool.exe")

        forced_cold, forced_cmd, _ = te.resolve_worker_mode(
            no_pool=True,
            workspace_path=self.root,
            env={"SYNTHETIC_WORKER_POOL_CMD": "custom-pool.exe"},
        )
        self.assertEqual(forced_cold, "cold")
        self.assertIsNone(forced_cmd)

    def test_probe_worker_pool_detects_tools_revit_worker_pool(self):
        pool_dir = os.path.join(self.root, "tools", "RevitWorkerPool")
        os.makedirs(pool_dir, exist_ok=True)
        pool_exe = os.path.join(pool_dir, "RevitWorkerPool.exe")
        with open(pool_exe, "w", encoding="utf-8") as f:
            f.write("")

        detected = te.probe_worker_pool(self.root, env={})
        self.assertEqual(detected, pool_exe)

    def test_rotate_test_results_keeps_latest_10_and_skips_non_run_entries(self):
        results_root = os.path.join(self.root, "TestResults")
        os.makedirs(results_root, exist_ok=True)

        # Non-run file and non-run dir must never be deleted
        non_run_file = os.path.join(results_root, "results_Logic.trx")
        with open(non_run_file, "w", encoding="utf-8") as f:
            f.write("<TestRun />")
        non_run_dir = os.path.join(results_root, "manual_notes")
        os.makedirs(non_run_dir, exist_ok=True)

        run_names = []
        base_time = time.time() - 100
        for i in range(13):
            run_name = f"20261122_1200{i:02d}_100_{i:08x}"
            run_dir = os.path.join(results_root, run_name)
            os.makedirs(run_dir, exist_ok=True)
            ts = base_time + i
            os.utime(run_dir, (ts, ts))
            run_names.append(run_name)

        removed = te.rotate_test_results(results_root, keep=10)

        self.assertEqual(set(removed), set(run_names[:3]))
        for kept in run_names[3:]:
            self.assertTrue(os.path.isdir(os.path.join(results_root, kept)))
        for old in run_names[:3]:
            self.assertFalse(os.path.exists(os.path.join(results_root, old)))
        self.assertTrue(os.path.isfile(non_run_file))
        self.assertTrue(os.path.isdir(non_run_dir))


class _FakeWinReg:
    HKEY_CURRENT_USER = object()
    REG_DWORD = 4

    def __init__(self, initial=None):
        self.store = dict(initial or {})
        self.deleted_calls = []

    def CreateKey(self, hive, subkey):
        return (hive, subkey)

    def QueryValueEx(self, key, name):
        full = (key[1], name)
        if full not in self.store:
            raise FileNotFoundError(name)
        return self.store[full], self.REG_DWORD

    def SetValueEx(self, key, name, reserved, reg_type, value):
        self.store[(key[1], name)] = value

    def DeleteValue(self, key, name):
        self.deleted_calls.append((key[1], name))
        self.store.pop((key[1], name), None)

    def CloseKey(self, key):
        pass


class TestRecursiveWhitelistingAndAddinSafety(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.pf = os.path.join(self.tmp.name, "ProgramFiles")
        self.pd = os.path.join(self.tmp.name, "ProgramData")
        self.ad = os.path.join(self.tmp.name, "AppData")

    def tearDown(self):
        self.tmp.cleanup()

    def _write_text(self, path, content):
        os.makedirs(os.path.dirname(path), exist_ok=True)
        with open(path, "w", encoding="utf-8") as f:
            f.write(content)

    def test_recursive_whitelisting_scans_all_five_areas_and_persists_registry(self):
        g_builtin = "11111111-1111-1111-1111-111111111111"
        g_pf_shared = "22222222-2222-2222-2222-222222222222"
        g_pd_nested = "33333333-3333-3333-3333-333333333333"
        g_ad_addin = "44444444-4444-4444-4444-444444444444"
        g_ad_pkg = "55555555-5555-5555-5555-555555555555"
        g_pd_pkg_addin = "66666666-6666-6666-6666-666666666666"

        # 1. C:\Program Files\Autodesk\Revit 2026\AddIns\eTransmitForRevit\...
        self._write_text(
            os.path.join(self.pf, "Autodesk", "Revit 2026", "AddIns", "eTransmitForRevit", "eTransmit.addin"),
            f"<RevitAddIns><AddIn><ClientId>{g_builtin}</ClientId></AddIn></RevitAddIns>",
        )
        # 2. C:\Program Files\Autodesk\Revit\Addins\2026\...
        self._write_text(
            os.path.join(self.pf, "Autodesk", "Revit", "Addins", "2026", "Vendor", "Shared.addin"),
            f"<RevitAddIns><AddIn><AddInId>{g_pf_shared}</AddInId></AddIn></RevitAddIns>",
        )
        # 3. %ProgramData%\Autodesk\Revit\Addins\2026\nested\...
        self._write_text(
            os.path.join(self.pd, "Autodesk", "Revit", "Addins", "2026", "Nested", "Prod.addin"),
            f"<RevitAddIns><AddIn><ClientId>{g_pd_nested}</ClientId></AddIn></RevitAddIns>",
        )
        # 4. %AppData%\Autodesk\Revit\Addins\2026\...
        self._write_text(
            os.path.join(self.ad, "Autodesk", "Revit", "Addins", "2026", "Dev.addin"),
            f"<RevitAddIns><AddIn><ClientId>{g_ad_addin}</ClientId></AddIn></RevitAddIns>",
        )
        # 5a. %AppData%\Autodesk\ApplicationPlugins\...\PackageContents.xml
        self._write_text(
            os.path.join(self.ad, "Autodesk", "ApplicationPlugins", "Sample.bundle", "PackageContents.xml"),
            f'<ApplicationPackage ProductCode="{g_ad_pkg}" />',
        )
        # 5b. %ProgramData%\Autodesk\ApplicationPlugins\...\nested\Inner.addin
        self._write_text(
            os.path.join(self.pd, "Autodesk", "ApplicationPlugins", "Org.bundle", "Contents", "Inner.addin"),
            f"<RevitAddIns><AddIn><AddInId>{g_pd_pkg_addin}</AddInId></AddIn></RevitAddIns>",
        )

        fake_reg = _FakeWinReg()
        added = gw.whitelist_existing_addins(
            "2026",
            program_files=self.pf,
            program_data=self.pd,
            app_data=self.ad,
            reg_module=fake_reg,
        )

        key_path = r"Software\Autodesk\Revit\Autodesk Revit 2026\CodeSigning"
        for expected in (g_builtin, g_pf_shared, g_pd_nested, g_ad_addin, g_ad_pkg, g_pd_pkg_addin):
            self.assertEqual(fake_reg.store.get((key_path, expected.lower())), 1)
            self.assertEqual(fake_reg.store.get((key_path, expected.upper())), 1)
            self.assertIn(expected.lower(), added)
        self.assertEqual(fake_reg.deleted_calls, [])

    def test_addins_settings_never_mutated_and_startup_bak_self_heals(self):
        self.assertFalse(hasattr(te, "TempConfigureRevitAddins"))
        self.assertFalse(hasattr(te.TempDisableThirdPartyAddins, "disable_addins"))

        addins_data_dir = os.path.join(self.ad, "Autodesk", "Revit", "Autodesk Revit 2026", "AddinsData")
        os.makedirs(addins_data_dir, exist_ok=True)
        json_path = os.path.join(addins_data_dir, "AddInsSettings.json")
        bak_path = json_path + ".bak"

        with open(json_path, "w", encoding="utf-8") as f:
            f.write('{"AddInItemSettings":[{"Name":"ThirdParty","Disabled":true}]}')
        with open(bak_path, "w", encoding="utf-8") as f:
            f.write('{"AddInItemSettings":[{"Name":"ThirdParty","Disabled":false}]}')

        te.TempDisableThirdPartyAddins("2026", appdata=self.ad).restore_addins()

        self.assertFalse(os.path.exists(bak_path))
        with open(json_path, "r", encoding="utf-8") as f:
            data = json.load(f)
        self.assertFalse(data["AddInItemSettings"][0]["Disabled"])

        # Verify test_executor.py never invokes cleanup_whitelisted_registry_entries or taskkill /IM Revit.exe
        with open(SCRIPT_PATH, "r", encoding="utf-8") as f:
            src = f.read()
        self.assertNotIn("cleanup_whitelisted_registry_entries", src)
        self.assertNotIn('"/IM", "Revit.exe"', src)


class TestRicaunBundleLeaseAndWorkerHardening(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.root = self.tmp.name
        self.appdata = os.path.join(self.root, "AppData")
        self.programdata = os.path.join(self.root, "ProgramData")
        os.makedirs(self.appdata, exist_ok=True)
        os.makedirs(self.programdata, exist_ok=True)

    def tearDown(self):
        self.tmp.cleanup()

    def _create_mock_testadapter_dll(self):
        # Inner bundle ZIP inside ricaun.RevitTest.Console.dll
        inner_buf = io.BytesIO()
        with zipfile.ZipFile(inner_buf, "w", zipfile.ZIP_DEFLATED) as zf:
            zf.writestr(
                "ricaun.RevitTest.Application.bundle/PackageContents.xml",
                '<ApplicationPackage ProductCode="65f304b3-8efb-464d-b08a-12cfd61a1986" />',
            )
            zf.writestr(
                "ricaun.RevitTest.Application.bundle/Contents/ricaun.RevitTest.Application/1.11.1/2026/ricaun.RevitTest.Application.addin",
                "<RevitAddIns />",
            )
        fake_console_dll = b"MZ_FAKE_PE_HEADER" + inner_buf.getvalue()

        # Outer console ZIP inside ricaun.RevitTest.TestAdapter.dll
        outer_buf = io.BytesIO()
        with zipfile.ZipFile(outer_buf, "w", zipfile.ZIP_DEFLATED) as zf:
            zf.writestr("ricaun.RevitTest.Console.dll", fake_console_dll)
        fake_adapter_dll = b"MZ_FAKE_ADAPTER_HEADER" + outer_buf.getvalue()

        dll_path = os.path.join(self.root, "ricaun.RevitTest.TestAdapter.dll")
        with open(dll_path, "wb") as f:
            f.write(fake_adapter_dll)
        return dll_path

    def test_shared_read_bundle_lease_overlap_and_teardown_zero_programdata(self):
        adapter_dll = self._create_mock_testadapter_dll()
        mutex_name = f"Local\\Synthetic_TestLease_{os.getpid()}"
        pd_bundle = os.path.join(self.programdata, "Autodesk", "ApplicationPlugins", te.RICAUN_BUNDLE_FOLDER_NAME)
        ad_bundle = os.path.join(self.appdata, "Autodesk", "ApplicationPlugins", te.RICAUN_BUNDLE_FOLDER_NAME)
        pkg_xml = os.path.join(ad_bundle, "PackageContents.xml")

        lease1 = te.RicaunBundleLease(
            appdata=self.appdata,
            testadapter_dll=adapter_dll,
            mutex_name=mutex_name,
        )
        lease2 = te.RicaunBundleLease(
            appdata=self.appdata,
            testadapter_dll=adapter_dll,
            mutex_name=mutex_name,
        )

        lease1.__enter__()
        self.assertTrue(os.path.isfile(pkg_xml))
        self.assertFalse(os.path.exists(pd_bundle))

        # Second concurrent lease opens same seeded PackageContents.xml
        lease2.__enter__()
        self.assertTrue(os.path.isfile(pkg_xml))

        # Closing lease1 while lease2 is active must preserve PackageContents.xml and bundle dir
        lease1.__exit__(None, None, None)
        self.assertTrue(os.path.isfile(pkg_xml))
        self.assertTrue(os.path.isdir(ad_bundle))

        # Closing the final lease removes PackageContents.xml and deletes the bundle dir
        lease2.__exit__(None, None, None)
        self.assertFalse(os.path.exists(pkg_xml))
        self.assertFalse(os.path.exists(ad_bundle))
        self.assertFalse(os.path.exists(pd_bundle))

    def test_worker_env_timeout_tree_kill_and_temp_cleanup(self):
        env = te.build_worker_env(r"C:\repo", "run_123", base_env={"PATH": "X"})
        self.assertEqual(env["SYNTHETIC_PROJECT_ROOT"], r"C:\repo")
        self.assertEqual(env["SYNTHETIC_TEST_WORKER"], "1")
        self.assertEqual(env["SYNTHETIC_TEST_RUN_ID"], "run_123")
        self.assertEqual(env["RICAUN_REVITTEST_CONSOLE_PROCESS_ARGUMENTS"], "/nosplash")

        killed_cmds = []

        def fake_runner(cmd, **kwargs):
            killed_cmds.append(cmd)
            return subprocess.CompletedProcess(cmd, 0)

        te.kill_process_tree(4242, runner=fake_runner)
        self.assertEqual(killed_cmds, [["taskkill", "/F", "/T", "/PID", "4242"]])

        class _TimeoutProc:
            pid = 9876
            returncode = None

            def __init__(self):
                self.wait_calls = 0

            def wait(self, timeout=None):
                self.wait_calls += 1
                if self.wait_calls == 1:
                    raise subprocess.TimeoutExpired("dotnet", timeout)
                self.returncode = 1
                return 1

            def kill(self):
                self.returncode = -1

        killed_pids = []
        rc, timed_out = te.execute_revit_test_process(
            ["dotnet", "test"],
            subprocess.DEVNULL,
            env,
            timeout=1,
            popen_fn=lambda *a, **kw: _TimeoutProc(),
            tree_killer=lambda pid: killed_pids.append(pid),
        )
        self.assertTrue(timed_out)
        self.assertEqual(killed_pids, [9876])
        self.assertEqual(rc, 1)

        # Verify %TEMP%/RevitTest/*<run_id>* cleanup only targets matching run_id
        revit_temp = os.path.join(self.root, "RevitTest")
        os.makedirs(os.path.join(revit_temp, "SyntheticTests2026_run_123_abc"), exist_ok=True)
        zip_match = os.path.join(revit_temp, "SyntheticTests2026_run_123.zip")
        zip_other = os.path.join(revit_temp, "SyntheticTests2026_other_999.zip")
        with open(zip_match, "w", encoding="utf-8") as f:
            f.write("x")
        with open(zip_other, "w", encoding="utf-8") as f:
            f.write("y")

        removed = te.cleanup_revit_test_temp("run_123", temp_dir=self.root)
        self.assertEqual(len(removed), 2)
        self.assertFalse(os.path.exists(zip_match))
        self.assertFalse(os.path.exists(os.path.join(revit_temp, "SyntheticTests2026_run_123_abc")))
        self.assertTrue(os.path.isfile(zip_other))

    def test_snapshot_and_post_run_assert_clean_gate_invocation_and_failure_propagation(self):
        journals_dir = os.path.join(self.root, "Journals")
        os.makedirs(journals_dir, exist_ok=True)
        existing_j = os.path.join(journals_dir, "journal.0001.txt")
        with open(existing_j, "w", encoding="utf-8") as f:
            f.write("' pre-existing\n")

        snapshot_path = os.path.join(self.root, "results", "known_journals_2026.json")
        te.snapshot_known_journals("2026", snapshot_path, journals_dir=journals_dir)

        with open(snapshot_path, "r", encoding="utf-8") as f:
            known = json.load(f)
        self.assertEqual(known, [os.path.abspath(existing_j)])

        # Verify CLI arguments passed to revit_journal_tool.py
        captured_cmds = []

        def fake_gate_runner(cmd, **kwargs):
            captured_cmds.append(cmd)
            return subprocess.CompletedProcess(cmd, 1, stdout="DIRTY", stderr="")

        ok, out = te.run_journal_cleanliness_gate("2026", "run_xyz", snapshot_path, runner=fake_gate_runner)
        self.assertFalse(ok)
        self.assertIn("DIRTY", out)
        self.assertEqual(
            captured_cmds[0][1:],
            [
                te.get_journal_tool_path(),
                "--version",
                "2026",
                "--run-id",
                "run_xyz",
                "--known-journals-file",
                snapshot_path,
                "--assert-clean",
            ],
        )

        # Verify real revit_journal_tool.py invocation fails on orphaned transaction for run_id
        dirty_j = os.path.join(journals_dir, "journal.0002.txt")
        with open(dirty_j, "w", encoding="utf-8") as f:
            f.write("' 0:< SYNTHETIC_TEST_RUN:run_dirty\n'inTransaction APITransactionIdentity_1:-1 LeakedTx\n")

        def run_with_journals_dir(cmd, **kwargs):
            return subprocess.run([*cmd, "--journals-dir", journals_dir], **kwargs)

        ok_real, out_real = te.run_journal_cleanliness_gate(
            "2026", "run_dirty", snapshot_path, runner=run_with_journals_dir
        )
        self.assertFalse(ok_real)
        self.assertIn("Cleanliness Gate Result: DIRTY", out_real)


if __name__ == "__main__":
    unittest.main()
