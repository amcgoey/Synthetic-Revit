import importlib.util
import os
import subprocess
import sys
import tempfile
import time
import unittest

REPO_ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
SCRIPT_PATH = os.path.join(
    REPO_ROOT,
    ".agents",
    "skills",
    "revit-tests",
    "scripts",
    "test_executor.py",
)

spec = importlib.util.spec_from_file_location("test_executor", SCRIPT_PATH)
te = importlib.util.module_from_spec(spec)
spec.loader.exec_module(te)


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


if __name__ == "__main__":
    unittest.main()
