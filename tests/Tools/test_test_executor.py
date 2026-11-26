import importlib.util
import io
import json
import os
import subprocess
import sys
import tempfile
import threading
import time
import unittest
import uuid
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

    def test_configure_dotnet_environment_binds_winning_root_and_clears_msbuild_sdks_path(self):
        local_sdk_root = self._make_sdk("local_dotnet", "8.0.427")
        pf_sdk_root = self._make_sdk("pf_dotnet", "10.0.100")
        env = {
            "PATH": r"C:\Windows\System32",
            "MSBUILDSDKSPATH": os.path.join(local_sdk_root, "8.0.427", "Sdks"),
        }

        best = te.configure_dotnet_environment(env=env, sdk_roots=[local_sdk_root, pf_sdk_root])

        expected_dotnet_root = os.path.join(self.root, "pf_dotnet")
        self.assertEqual(best["version"], "10.0.100")
        self.assertEqual(env["DOTNET_ROOT"], expected_dotnet_root)
        self.assertTrue(env["PATH"].startswith(expected_dotnet_root + os.pathsep))
        self.assertFalse(any(k.casefold() == "msbuildsdkspath" for k in env))
        self.assertNotIn("MSBuildEnableWorkloadResolver", env)

    def test_dotnet_build_migration_removes_msbuild_downgrade_workaround(self):
        local_sdk_root = self._make_sdk("local_dotnet", "8.0.427")
        pf_sdk_root = self._make_sdk("pf_dotnet", "10.0.404")

        self.assertFalse(hasattr(te, "find_msbuild"))
        self.assertFalse(hasattr(te, "is_sdk_compatible_with_msbuild"))
        self.assertFalse(hasattr(te, "_msbuild_has_broken_dotnet_releases_redirect"))

        best = te.find_best_dotnet_sdk([local_sdk_root, pf_sdk_root])
        self.assertIsNotNone(best)
        self.assertEqual(best["version"], "10.0.404")

    def test_net10_sdk_preflight_fails_fast_with_remediation_when_absent_or_below_10_0_100(self):
        import contextlib

        missing_pf = os.path.join(self.root, "missing_pf", "dotnet", "sdk")
        only_net8 = self._make_sdk("local_dotnet", "8.0.427")
        prerelease_10 = self._make_sdk("pre_dotnet", "10.0.100-rc.1")

        for label, roots in (
            ("missing", [missing_pf]),
            ("net8-only", [missing_pf, only_net8]),
            ("net10-rc1-only", [missing_pf, prerelease_10]),
        ):
            with self.subTest(case=label):
                out_buf = io.StringIO()
                with contextlib.redirect_stdout(out_buf):
                    with self.assertRaises(SystemExit) as exit_ctx:
                        te.require_net10_sdk(sdk_roots=roots)
                self.assertEqual(exit_ctx.exception.code, 1)
                msg = out_buf.getvalue()
                self.assertIn("10.0.100", msg)
                self.assertIn("winget", msg)
                self.assertIn("DOTNET_ROOT", msg)
                self.assertIn("dotnet-install", msg)

        # Accepts 10.0.100+ in user LocalAppData root as well as machine Program Files root
        user_net10 = self._make_sdk("user_local_dotnet", "10.0.100")
        passed = te.require_net10_sdk(sdk_roots=[missing_pf, user_net10])
        self.assertEqual(passed["version"], "10.0.100")

    def test_resolve_dotnet_exe_uses_discovered_root_or_fallback(self):
        dotnet_root = os.path.join(self.root, "dotnet_bin")
        os.makedirs(dotnet_root, exist_ok=True)
        dotnet_exe = os.path.join(dotnet_root, "dotnet.exe")
        with open(dotnet_exe, "w", encoding="utf-8") as f:
            f.write("")

        self.assertEqual(te.resolve_dotnet_exe({"dotnet_root": dotnet_root}), dotnet_exe)
        self.assertEqual(te.resolve_dotnet_exe({"dotnet_root": os.path.join(self.root, "absent")}), "dotnet")
        self.assertEqual(te.resolve_dotnet_exe(None), "dotnet")


class TestCategoryAndFilterComposition(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.tests_dir = os.path.join(self.tmp.name, "tests")
        os.makedirs(os.path.join(self.tests_dir, "Platform", "Host", "Revit"), exist_ok=True)
        os.makedirs(os.path.join(self.tests_dir, "Features", "SheetIndex", "Logic"), exist_ok=True)
        os.makedirs(os.path.join(self.tests_dir, "Projects", "Synthetic.RevitTests.2026"), exist_ok=True)

        with open(
            os.path.join(self.tests_dir, "Platform", "Host", "Revit", "ClassLevelTests.cs"),
            "w",
            encoding="utf-8",
        ) as f:
            f.write(
                """
using NUnit.Framework;

namespace SyntheticTests.Platform.Host
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
            os.path.join(self.tests_dir, "Projects", "Synthetic.RevitTests.2026", "MethodLevelTests.cs"),
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

        # Excluded from Revit builds because it lives under Logic/
        with open(
            os.path.join(self.tests_dir, "Features", "SheetIndex", "Logic", "ExcludedTests.cs"),
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
            "(FullyQualifiedName~SyntheticTests.BetaRevitTests.FastCheck|FullyQualifiedName~SyntheticTests.Platform.Host.AlphaSmokeTests)",
        )
        self.assertEqual(
            expr_both,
            "(FullyQualifiedName~SyntheticTests.BetaRevitTests.FastCheck|FullyQualifiedName~SyntheticTests.Platform.Host.AlphaSmokeTests)&(FullyQualifiedName~Smoke)",
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

    def test_discover_test_projects_discovers_projects_dir_only_and_selects_targets_without_double_run(self):
        with tempfile.TemporaryDirectory() as tmp:
            tdir = os.path.join(tmp, "tests")
            for rel, csproj_name in (
                (os.path.join("Projects", "Synthetic.LogicTests"), "Synthetic.LogicTests.csproj"),
                (os.path.join("Projects", "Synthetic.LegacyMockTests"), "Synthetic.LegacyMockTests.csproj"),
                (os.path.join("Projects", "Synthetic.RevitTests.2026"), "Synthetic.RevitTests.2026.csproj"),
                ("SyntheticTests.Logic", "SyntheticTests.Logic.csproj"),
                ("SyntheticTests2026", "SyntheticTests2026.csproj"),
                ("SyntheticTests.Shared", "SyntheticTests.Shared.csproj"),
            ):
                pdir = os.path.join(tdir, rel)
                os.makedirs(pdir, exist_ok=True)
                with open(os.path.join(pdir, csproj_name), "w", encoding="utf-8") as f:
                    f.write("<Project Sdk=\"Microsoft.NET.Sdk\" />")

            discovered = te.discover_test_projects(tdir)

            self.assertEqual(
                discovered["Logic"],
                [os.path.join(tdir, "Projects", "Synthetic.LogicTests", "Synthetic.LogicTests.csproj")],
            )
            self.assertEqual(
                discovered["LegacyMock"],
                [os.path.join(tdir, "Projects", "Synthetic.LegacyMockTests", "Synthetic.LegacyMockTests.csproj")],
            )
            self.assertEqual(
                discovered["2026"],
                [os.path.join(tdir, "Projects", "Synthetic.RevitTests.2026", "Synthetic.RevitTests.2026.csproj")],
            )
            self.assertNotIn("Shared", discovered)

            # --version Logic bundles LegacyMock once
            logic_selected = te.select_run_projects(discovered, target_version="Logic")
            self.assertEqual(list(logic_selected.keys()), ["Logic", "LegacyMock"])

            # --version LegacyMock selects only LegacyMock
            mock_selected = te.select_run_projects(discovered, target_version="LegacyMock")
            self.assertEqual(list(mock_selected.keys()), ["LegacyMock"])

            # Omitting --version selects every project once (never double-runs LegacyMock)
            all_selected = te.select_run_projects(discovered, target_version="")
            self.assertEqual(list(all_selected.keys()), ["Logic", "LegacyMock", "2026"])
            self.assertEqual(len(all_selected["LegacyMock"]), 1)

            # --framework net48 skips single-TFM net8.0-windows8.0 LegacyMockTests
            with open(discovered["LegacyMock"][0], "w", encoding="utf-8") as f:
                f.write("<Project><PropertyGroup><TargetFramework>net8.0-windows8.0</TargetFramework></PropertyGroup></Project>")
            mock_tfms, _ = te.get_project_target_frameworks(discovered["LegacyMock"][0])
            self.assertEqual([t for t in mock_tfms if t.casefold() == "net48"], [])

    def test_select_run_projects_comma_separated_deduplication_ordering_and_validation(self):
        discovered = {
            "Logic": ["Logic.csproj"],
            "LegacyMock": ["LegacyMock.csproj"],
            "2025": ["2025.csproj"],
            "2026": ["2026.csproj"],
            "2027": ["2027.csproj"],
        }

        # Trims, deduplicates repeated tokens, preserves deterministic first-seen order
        selected_revit = te.select_run_projects(discovered, target_version=" 2027 , 2025 , 2027 , 2026 ")
        self.assertEqual(list(selected_revit.keys()), ["2027", "2025", "2026"])

        # Overlapping Logic,LegacyMock deduplicates LegacyMock (case-insensitive)
        selected_overlap1 = te.select_run_projects(discovered, target_version="logic, LEGACYMOCK, 2026")
        self.assertEqual(list(selected_overlap1.keys()), ["Logic", "LegacyMock", "2026"])

        selected_overlap2 = te.select_run_projects(discovered, target_version="LegacyMock, Logic")
        self.assertEqual(list(selected_overlap2.keys()), ["LegacyMock", "Logic"])

        # Fails fast on empty tokens or unknown tokens
        for bad_spec in ("2026,,2027", ",2026", "2026,", " , ", "2026,2099", "unknown"):
            with self.subTest(bad_spec=bad_spec):
                with self.assertRaises(ValueError):
                    te.select_run_projects(discovered, target_version=bad_spec)

        # CLI fails fast on empty --version "" and empty comma token, and parses --fail-fast
        parsed_ff = te.parse_cli_args(["--version", "2026,2027", "--fail-fast"])
        self.assertEqual(parsed_ff.target_version, "2026,2027")
        self.assertTrue(parsed_ff.fail_fast)
        self.assertFalse(te.parse_cli_args([]).fail_fast)

        for cli_bad in ("", " , ", "2026,,2027", "2026,2099"):
            res = subprocess.run(
                [sys.executable, SCRIPT_PATH, "--version", cli_bad],
                cwd=REPO_ROOT,
                capture_output=True,
                text=True,
            )
            self.assertNotEqual(res.returncode, 0, msg=f"Expected fail-fast for --version {cli_bad!r}")

    def test_multi_tfm_target_framework_parsing_and_trx_isolation(self):
        with tempfile.TemporaryDirectory() as tmp:
            multi_csproj = os.path.join(tmp, "Multi.csproj")
            single_csproj = os.path.join(tmp, "Single.csproj")
            with open(multi_csproj, "w", encoding="utf-8") as f:
                f.write("<Project><PropertyGroup><TargetFrameworks>net48;net8.0-windows8.0;net10.0-windows8.0</TargetFrameworks></PropertyGroup></Project>")
            with open(single_csproj, "w", encoding="utf-8") as f:
                f.write("<Project><PropertyGroup><TargetFramework>net8.0-windows8.0</TargetFramework></PropertyGroup></Project>")

            multi_tfms, is_multi = te.get_project_target_frameworks(multi_csproj)
            single_tfms, is_single_multi = te.get_project_target_frameworks(single_csproj)

            self.assertTrue(is_multi)
            self.assertEqual(multi_tfms, ["net48", "net8.0-windows8.0", "net10.0-windows8.0"])
            self.assertFalse(is_single_multi)
            self.assertEqual(single_tfms, ["net8.0-windows8.0"])

            trx_net48 = te.build_trx_filename("Logic", tfm="net48", is_multi_tfm=True)
            trx_net8 = te.build_trx_filename("Logic", tfm="net8.0-windows8.0", is_multi_tfm=True)
            trx_net10 = te.build_trx_filename("Logic", tfm="net10.0-windows8.0", is_multi_tfm=True)
            trx_legacy = te.build_trx_filename("Logic", tfm="net8.0-windows8.0", is_multi_tfm=False)

            self.assertEqual(trx_net48, "results_Logic_net48.trx")
            self.assertEqual(trx_net8, "results_Logic_net8.0-windows8.0.trx")
            self.assertEqual(trx_net10, "results_Logic_net10.0-windows8.0.trx")
            self.assertEqual(trx_legacy, "results_Logic.trx")
            self.assertEqual(len({trx_net48, trx_net8, trx_net10, trx_legacy}), 4)

    def test_single_framework_filtering_runs_only_matching_projects_across_three_tfms(self):
        import contextlib
        from unittest import mock

        with tempfile.TemporaryDirectory() as tmp:
            tdir = os.path.join(tmp, "tests")
            logic_dir = os.path.join(tdir, "Projects", "Synthetic.LogicTests")
            mock_dir = os.path.join(tdir, "Projects", "Synthetic.LegacyMockTests")
            os.makedirs(logic_dir, exist_ok=True)
            os.makedirs(mock_dir, exist_ok=True)
            with open(os.path.join(logic_dir, "Synthetic.LogicTests.csproj"), "w", encoding="utf-8") as f:
                f.write("<Project><PropertyGroup><TargetFrameworks>net48;net8.0-windows8.0;net10.0-windows8.0</TargetFrameworks></PropertyGroup></Project>")
            with open(os.path.join(mock_dir, "Synthetic.LegacyMockTests.csproj"), "w", encoding="utf-8") as f:
                f.write("<Project><PropertyGroup><TargetFramework>net8.0-windows8.0</TargetFramework></PropertyGroup></Project>")

            temp_build_root = os.path.join(tmp, "temp_builds")
            os.makedirs(temp_build_root, exist_ok=True)

            cases = [
                ("net48", [("Synthetic.LogicTests.csproj", "net48")]),
                ("net8.0-windows8.0", [("Synthetic.LogicTests.csproj", "net8.0-windows8.0"), ("Synthetic.LegacyMockTests.csproj", None)]),
                ("net10.0-windows8.0", [("Synthetic.LogicTests.csproj", "net10.0-windows8.0")]),
            ]
            for fw_filter, expected_runs in cases:
                with self.subTest(framework=fw_filter):
                    build_projs = []
                    test_runs = []

                    def fake_run(cmd, **kwargs):
                        if len(cmd) > 1 and cmd[1] == "build":
                            build_projs.append(os.path.basename(cmd[2]))
                            return subprocess.CompletedProcess(cmd, 0)
                        if len(cmd) > 1 and cmd[1] == "test":
                            proj = os.path.basename(cmd[2])
                            fw = cmd[cmd.index("--framework") + 1] if "--framework" in cmd else None
                            test_runs.append((proj, fw))
                            res_dir = cmd[cmd.index("--results-directory") + 1]
                            os.makedirs(res_dir, exist_ok=True)
                            trx_name = cmd[cmd.index("--logger") + 1].split("LogFileName=", 1)[1]
                            with open(os.path.join(res_dir, trx_name), "w", encoding="utf-8") as tf:
                                tf.write(
                                    '<?xml version="1.0" encoding="utf-8"?>'
                                    '<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">'
                                    '<ResultSummary><Counters total="1" executed="1" passed="1" failed="0" /></ResultSummary>'
                                    '</TestRun>'
                                )
                            return subprocess.CompletedProcess(cmd, 0)
                        raise AssertionError(f"Unexpected command: {cmd}")

                    out_buf = io.StringIO()
                    with mock.patch("os.getcwd", return_value=tmp), \
                         mock.patch.object(te.tempfile, "gettempdir", return_value=temp_build_root), \
                         mock.patch.object(te, "configure_dotnet_environment", return_value=None), \
                         mock.patch.object(te.subprocess, "run", side_effect=fake_run), \
                         contextlib.redirect_stdout(out_buf):
                        with self.assertRaises(SystemExit) as exit_ctx:
                            te.run_test_suite(target_version="Logic", target_framework=fw_filter)

                    self.assertEqual(exit_ctx.exception.code, 0, msg=out_buf.getvalue())
                    self.assertEqual(test_runs, expected_runs)
                    self.assertEqual(build_projs, [p for p, _ in expected_runs])

    def test_multiline_class_headers_typed_attributes_inherited_categories_and_fqn(self):
        fixtures_dir = os.path.join(self.tests_dir, "Fixtures", "Revit")
        revit_dir = os.path.join(self.tests_dir, "Features", "Sample", "Revit")
        os.makedirs(fixtures_dir, exist_ok=True)
        os.makedirs(revit_dir, exist_ok=True)

        with open(os.path.join(fixtures_dir, "RevitTemplateRollbackFixture.cs"), "w", encoding="utf-8") as f:
            f.write(
                """
using System;
namespace SyntheticTests.Fixtures
{
    [RevitAdapterTest]
    public abstract class RevitTemplateRollbackFixture
    {
    }
}
"""
            )

        with open(os.path.join(revit_dir, "SampleAdapterRevitTests.cs"), "w", encoding="utf-8") as f:
            f.write(
                """
using System;
using NUnit.Framework;
using SyntheticTests.Fixtures;

namespace SyntheticTests.Features.Sample
{
    [TestFixture]
    public sealed class MultilineInheritedAdapterTests
        : RevitTemplateRollbackFixture,
          IDisposable
    {
        public void Dispose() {}

        [Test]
        public void InheritedAdapterRuns() {}
    }

    [TestFixture]
    public class MethodTypedSmokeTests
    {
        [Test, RevitSmokeTest]
        public void SmokeMethod() {}
    }
}
"""
            )

        adapter_matches = te.find_tests_by_category(self.tests_dir, "RevitAdapter", version="2026")
        self.assertEqual(adapter_matches, ["SyntheticTests.Features.Sample.MultilineInheritedAdapterTests"])
        self.assertNotIn("SyntheticTests.Fixtures.RevitTemplateRollbackFixture", adapter_matches)

        smoke_matches = te.find_tests_by_category(self.tests_dir, "RevitSmoke", version="2026")
        self.assertIn("SyntheticTests.Features.Sample.MethodTypedSmokeTests.SmokeMethod", smoke_matches)


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

    def test_probe_worker_pool_prioritizes_tools_bin_over_fallback_and_respects_env_override(self):
        fallback_dir = os.path.join(self.root, "tools", "RevitWorkerPool")
        bin_dir = os.path.join(self.root, "tools", "bin", "RevitWorkerPool")
        os.makedirs(fallback_dir, exist_ok=True)
        os.makedirs(bin_dir, exist_ok=True)
        fallback_exe = os.path.join(fallback_dir, "RevitWorkerPool.exe")
        bin_exe = os.path.join(bin_dir, "RevitWorkerPool.exe")
        with open(fallback_exe, "w", encoding="utf-8") as f:
            f.write("")
        with open(bin_exe, "w", encoding="utf-8") as f:
            f.write("")

        detected = te.probe_worker_pool(self.root, env={})
        self.assertEqual(detected, bin_exe)

        env_detected = te.probe_worker_pool(
            self.root,
            env={"SYNTHETIC_WORKER_POOL_CMD": "override-pool.exe"},
        )
        self.assertEqual(env_detected, "override-pool.exe")

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

    def test_whitelisting_scans_program_files_application_plugins_and_prefers_programw6432(self):
        pf32 = os.path.join(self.tmp.name, "ProgramFilesX86")
        pf64 = os.path.join(self.tmp.name, "ProgramFiles64")
        g_pf_pkg = "77777777-7777-7777-7777-777777777777"
        g_pf_pkg_addin = "88888888-8888-8888-8888-888888888888"
        g_pf32_decoy = "99999999-9999-9999-9999-999999999999"

        self._write_text(
            os.path.join(pf64, "Autodesk", "ApplicationPlugins", "Machine2027.bundle", "PackageContents.xml"),
            f'<ApplicationPackage ProductCode="{{{g_pf_pkg}}}" />',
        )
        self._write_text(
            os.path.join(pf64, "Autodesk", "ApplicationPlugins", "Machine2027.bundle", "Contents", "Plugin2027.addin"),
            f"<RevitAddIns><AddIn><ClientId>{g_pf_pkg_addin}</ClientId></AddIn></RevitAddIns>",
        )
        self._write_text(
            os.path.join(pf32, "Autodesk", "ApplicationPlugins", "Decoy.bundle", "PackageContents.xml"),
            f'<ApplicationPackage ProductCode="{g_pf32_decoy}" />',
        )

        env = {
            "ProgramW6432": pf64,
            "ProgramFiles": pf32,
            "ProgramData": self.pd,
            "AppData": self.ad,
        }
        _, bundle_paths = gw.get_bundle_and_addin_paths("2027", env=env)
        self.assertIn(os.path.join(pf64, "Autodesk", "ApplicationPlugins"), bundle_paths)

        fake_reg = _FakeWinReg()
        added = gw.whitelist_existing_addins("2027", env=env, reg_module=fake_reg)

        key_path = r"Software\Autodesk\Revit\Autodesk Revit 2027\CodeSigning"
        for expected in (g_pf_pkg, g_pf_pkg_addin):
            self.assertEqual(fake_reg.store.get((key_path, expected.lower())), 1)
            self.assertEqual(fake_reg.store.get((key_path, expected.upper())), 1)
            self.assertIn(expected.lower(), added)
        self.assertIsNone(fake_reg.store.get((key_path, g_pf32_decoy.lower())))

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

    def test_bundle_lease_exit_closes_handle_even_if_acquire_mutex_times_out(self):
        adapter_dll = self._create_mock_testadapter_dll()
        mutex_name = f"Local\\Synthetic_TestLeaseTimeout_{os.getpid()}"
        lease = te.RicaunBundleLease(
            appdata=self.appdata,
            testadapter_dll=adapter_dll,
            mutex_name=mutex_name,
        )
        lease.__enter__()
        self.assertIsNotNone(lease._handle)

        closed_handles = []
        original_close = lease._win32.close_handle

        def tracking_close(handle):
            closed_handles.append(handle)
            return original_close(handle)

        released_mutexes = []
        lease._win32.close_handle = tracking_close
        lease._win32.acquire_mutex = lambda name, timeout_ms=30000: (_ for _ in ()).throw(
            TimeoutError(f"Simulated timeout on {name}")
        )
        lease._win32.release_mutex = lambda h: released_mutexes.append(h)

        # Must not raise TimeoutError and must close _handle without calling release_mutex.
        lease.__exit__(None, None, None)
        self.assertEqual(len(closed_handles), 1)
        self.assertIsNone(lease._handle)
        self.assertEqual(released_mutexes, [])
        # PackageContents.xml is now unlocked and deletable.
        os.remove(lease.package_contents_path)
        self.assertFalse(os.path.exists(lease.package_contents_path))

    def test_find_ricaun_testadapter_dll_prioritizes_net10_above_net8_net6_net45(self):
        nuget_root = os.path.join(self.root, "nuget_pkgs")
        pkg_lib = os.path.join(nuget_root, "ricaun.revittest.testadapter", "1.12.0", "lib")
        dll_by_tfm = {}
        for tfm in ("net45", "net6.0", "net8.0", "net10.0"):
            tfm_dir = os.path.join(pkg_lib, tfm)
            os.makedirs(tfm_dir, exist_ok=True)
            dll_path = os.path.join(tfm_dir, "ricaun.RevitTest.TestAdapter.dll")
            with open(dll_path, "wb") as f:
                f.write(tfm.encode("ascii"))
            dll_by_tfm[tfm] = dll_path

        env = {"NUGET_PACKAGES": nuget_root}
        self.assertEqual(te.find_ricaun_testadapter_dll(env=env), dll_by_tfm["net10.0"])

        os.remove(dll_by_tfm["net10.0"])
        self.assertEqual(te.find_ricaun_testadapter_dll(env=env), dll_by_tfm["net8.0"])

        os.remove(dll_by_tfm["net8.0"])
        self.assertEqual(te.find_ricaun_testadapter_dll(env=env), dll_by_tfm["net6.0"])

        os.remove(dll_by_tfm["net6.0"])
        self.assertEqual(te.find_ricaun_testadapter_dll(env=env), dll_by_tfm["net45"])

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
        os.makedirs(os.path.join(revit_temp, "Synthetic.RevitTests.2026_run_123_abc"), exist_ok=True)
        zip_match = os.path.join(revit_temp, "Synthetic.RevitTests.2026_run_123.zip")
        zip_other = os.path.join(revit_temp, "Synthetic.RevitTests.2026_other_999.zip")
        with open(zip_match, "w", encoding="utf-8") as f:
            f.write("x")
        with open(zip_other, "w", encoding="utf-8") as f:
            f.write("y")

        removed = te.cleanup_revit_test_temp("run_123", temp_dir=self.root)
        self.assertEqual(len(removed), 2)
        self.assertFalse(os.path.exists(zip_match))
        self.assertFalse(os.path.exists(os.path.join(revit_temp, "Synthetic.RevitTests.2026_run_123_abc")))
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


class TestBuildOutputsAndPayloadStaging(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.env = os.environ.copy()
        sdk_info = te.configure_dotnet_environment(env=cls.env)
        cls.dotnet = te.resolve_dotnet_exe(sdk_info)

    def test_core_and_payload_outputs_zero_excel_pia_and_flat_payload_staging(self):
        with tempfile.TemporaryDirectory() as iso_root:
            core_csproj = os.path.join(REPO_ROOT, "src", "Synthetic.Core", "Synthetic.Core.csproj")
            payload_2026_csproj = os.path.join(REPO_ROOT, "src", "Synthetic2026", "Synthetic2026.csproj")
            payload_2027_csproj = os.path.join(REPO_ROOT, "src", "Synthetic2027", "Synthetic2027.csproj")
            run_id = "tooltest"

            for csproj in (core_csproj, payload_2026_csproj, payload_2027_csproj):
                cmd = [
                    self.dotnet,
                    "build",
                    csproj,
                    "-c",
                    "Debug",
                    "-p:Platform=x64",
                    f"-p:SyntheticIsolatedBuildRoot={iso_root}",
                    f"-p:SyntheticRunId={run_id}",
                    "-v:minimal",
                    "-clp:ErrorsOnly;Summary",
                ]
                res = subprocess.run(cmd, env=self.env, capture_output=True, text=True)
                self.assertEqual(res.returncode, 0, msg=res.stdout + res.stderr)

            core_bin = os.path.join(iso_root, "bin", f"Synthetic.Core_{run_id}")
            for tfm in ("net48", "net8.0-windows8.0", "net10.0-windows8.0"):
                tfm_dir = os.path.join(core_bin, tfm)
                self.assertTrue(os.path.isfile(os.path.join(tfm_dir, "Synthetic.Core.dll")), f"Missing {tfm} Synthetic.Core.dll")
                self.assertFalse(os.path.exists(os.path.join(tfm_dir, "Microsoft.Office.Interop.Excel.dll")))
                self.assertFalse(os.path.exists(os.path.join(tfm_dir, "Assets")))
                self.assertFalse(os.path.exists(os.path.join(tfm_dir, "SyntheticSettings.template.json")))
                self.assertFalse(os.path.exists(os.path.join(tfm_dir, "SyntheticWorksets.xlsx")))

            for year in ("2026", "2027"):
                payload_bin = os.path.join(iso_root, "bin", f"Synthetic{year}_{run_id}")
                self.assertTrue(os.path.isfile(os.path.join(payload_bin, f"Synthetic{year}.dll")))
                self.assertTrue(os.path.isfile(os.path.join(payload_bin, "Synthetic.Core.dll")))
                self.assertTrue(os.path.isfile(os.path.join(payload_bin, "Newtonsoft.Json.dll")))
                self.assertTrue(os.path.isfile(os.path.join(payload_bin, "Assets", "ribbon_config.json")))
                self.assertTrue(os.path.isfile(os.path.join(payload_bin, "SyntheticSettings.template.json")))
                self.assertTrue(os.path.isfile(os.path.join(payload_bin, "SyntheticWorksets.xlsx")))
                self.assertFalse(os.path.exists(os.path.join(payload_bin, "Microsoft.Office.Interop.Excel.dll")))


class TestLegacyProgramDataManifestCheck(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.programdata = os.path.join(self.tmp.name, "ProgramData")

    def tearDown(self):
        self.tmp.cleanup()

    def _write_addin(self, version, filename, full_class_name):
        addins_dir = os.path.join(self.programdata, "Autodesk", "Revit", "Addins", str(version))
        os.makedirs(addins_dir, exist_ok=True)
        path = os.path.join(addins_dir, filename)
        content = (
            '<?xml version="1.0" encoding="utf-8"?>\n'
            "<RevitAddIns>\n"
            '  <AddIn Type="Application">\n'
            "    <Name>Synthetic</Name>\n"
            f"    <FullClassName>{full_class_name}</FullClassName>\n"
            "  </AddIn>\n"
            "</RevitAddIns>\n"
        )
        with open(path, "w", encoding="utf-8") as f:
            f.write(content)
        return path, content

    def test_warns_on_legacy_full_class_and_silent_on_bootstrap_or_missing_without_mutation(self):
        import contextlib

        # 1. Missing directory -> silent, empty
        buf_missing = io.StringIO()
        with contextlib.redirect_stdout(buf_missing):
            flagged_missing = te.check_legacy_programdata_manifest("2026", program_data=self.programdata)
        self.assertEqual(flagged_missing, [])
        self.assertEqual(buf_missing.getvalue(), "")

        # 2. Unrelated vendor manifest + valid BootstrapApp manifest -> silent, empty
        self._write_addin("2026", "CTC-BIM-Project-Suite.addin", "CTC.RET.SuiteExternalApp.App")
        bootstrap_path, bootstrap_content = self._write_addin(
            "2026", "Synthetic2026.addin", "Synthetic.Bootstrap.BootstrapApp"
        )
        buf_clean = io.StringIO()
        with contextlib.redirect_stdout(buf_clean):
            flagged_clean = te.check_legacy_programdata_manifest("2026", program_data=self.programdata)
        self.assertEqual(flagged_clean, [])
        self.assertEqual(buf_clean.getvalue(), "")

        # 3. Legacy Synthetic.Core.App manifest -> warns clearly and leaves file untouched
        with open(bootstrap_path, "w", encoding="utf-8") as f:
            legacy_content = bootstrap_content.replace(
                "Synthetic.Bootstrap.BootstrapApp", "Synthetic.Core.App"
            )
            f.write(legacy_content)

        buf_legacy = io.StringIO()
        with contextlib.redirect_stdout(buf_legacy):
            flagged_legacy = te.check_legacy_programdata_manifest("2026", program_data=self.programdata)

        self.assertEqual(flagged_legacy, [bootstrap_path])
        out = buf_legacy.getvalue()
        self.assertIn("[WARN]", out)
        self.assertIn(bootstrap_path, out)
        self.assertIn("Synthetic.Core.App", out)
        self.assertIn("shadow", out.lower())

        # Read-only guarantee: file still exists with exact original content
        self.assertTrue(os.path.isfile(bootstrap_path))
        with open(bootstrap_path, "r", encoding="utf-8") as f:
            self.assertEqual(f.read(), legacy_content)

    def test_2027_preflight_checks_program_files_primary_and_programdata_secondary_read_only(self):
        import contextlib

        programfiles = os.path.join(self.tmp.name, "ProgramFiles")

        def write_addin_at(root_dir, version, filename, full_class_name):
            addins_dir = os.path.join(root_dir, "Autodesk", "Revit", "Addins", str(version))
            os.makedirs(addins_dir, exist_ok=True)
            path = os.path.join(addins_dir, filename)
            content = (
                '<?xml version="1.0" encoding="utf-8"?>\n'
                "<RevitAddIns>\n"
                '  <AddIn Type="Application">\n'
                "    <Name>Synthetic</Name>\n"
                f"    <FullClassName>{full_class_name}</FullClassName>\n"
                "  </AddIn>\n"
                "</RevitAddIns>\n"
            )
            with open(path, "w", encoding="utf-8") as f:
                f.write(content)
            return path, content

        pf_2027, pf_2027_content = write_addin_at(programfiles, "2027", "Synthetic2027.addin", "Synthetic.Core.App")
        pd_2027, pd_2027_content = write_addin_at(self.programdata, "2027", "Synthetic2027.addin", "Synthetic.Legacy.App")
        pf_2026, _ = write_addin_at(programfiles, "2026", "Synthetic2026.addin", "Synthetic.Core.App")

        # 2027+: Program Files is checked as primary, ProgramData as secondary; neither is mutated
        buf_2027 = io.StringIO()
        with contextlib.redirect_stdout(buf_2027):
            flagged_2027 = te.check_legacy_programdata_manifest(
                "2027",
                program_data=self.programdata,
                program_files=programfiles,
            )
        self.assertEqual(flagged_2027, [pf_2027, pd_2027])
        self.assertIn(pf_2027, buf_2027.getvalue())
        self.assertIn(pd_2027, buf_2027.getvalue())
        with open(pf_2027, "r", encoding="utf-8") as f:
            self.assertEqual(f.read(), pf_2027_content)
        with open(pd_2027, "r", encoding="utf-8") as f:
            self.assertEqual(f.read(), pd_2027_content)

        # Clean primary + legacy secondary -> only secondary flagged
        write_addin_at(programfiles, "2027", "Synthetic2027.addin", "Synthetic.Bootstrap.BootstrapApp")
        flagged_secondary_only = te.check_legacy_programdata_manifest(
            "2027",
            program_data=self.programdata,
            program_files=programfiles,
        )
        self.assertEqual(flagged_secondary_only, [pd_2027])

        # 2023-2026 retains ProgramData-only behavior (ignores Program Files)
        flagged_2026 = te.check_legacy_programdata_manifest(
            "2026",
            program_data=self.programdata,
            program_files=programfiles,
        )
        self.assertEqual(flagged_2026, [])
        self.assertTrue(os.path.isfile(pf_2026))

    def test_2027_preflight_prefers_programw6432_before_programfiles(self):
        import contextlib

        pf64 = os.path.join(self.tmp.name, "ProgramW6432")
        pf32 = os.path.join(self.tmp.name, "ProgramFiles32")
        pd = os.path.join(self.tmp.name, "ProgramDataEnv")

        for root_dir, cls_name in ((pf64, "Synthetic.Legacy64"), (pf32, "Synthetic.Legacy32")):
            addins_dir = os.path.join(root_dir, "Autodesk", "Revit", "Addins", "2027")
            os.makedirs(addins_dir, exist_ok=True)
            with open(os.path.join(addins_dir, "Synthetic2027.addin"), "w", encoding="utf-8") as f:
                f.write(f"<RevitAddIns><AddIn><FullClassName>{cls_name}</FullClassName></AddIn></RevitAddIns>")

        expected_pf64 = os.path.join(pf64, "Autodesk", "Revit", "Addins", "2027", "Synthetic2027.addin")
        expected_pf32 = os.path.join(pf32, "Autodesk", "Revit", "Addins", "2027", "Synthetic2027.addin")

        buf_64 = io.StringIO()
        with contextlib.redirect_stdout(buf_64):
            flagged_64 = te.check_legacy_programdata_manifest(
                "2027",
                env={"ProgramW6432": pf64, "ProgramFiles": pf32, "ProgramData": pd},
            )
        self.assertEqual(flagged_64, [expected_pf64])

        # Fallback to ProgramFiles when ProgramW6432 is absent
        buf_32 = io.StringIO()
        with contextlib.redirect_stdout(buf_32):
            flagged_32 = te.check_legacy_programdata_manifest(
                "2027",
                env={"ProgramFiles": pf32, "ProgramData": pd},
            )
        self.assertEqual(flagged_32, [expected_pf32])


class TestRevitStartupGate(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.suffix = f"{os.getpid()}_{uuid.uuid4().hex[:6]}"

    def tearDown(self):
        self.tmp.cleanup()

    def test_startup_gate_mutex_naming_and_cross_process_serialization_and_cross_version_nonblocking(self):
        self.assertEqual(te.get_startup_gate_mutex_name("2026"), r"Local\Synthetic_RevitStartupGate_2026")
        self.assertEqual(te.get_startup_gate_mutex_name("2025"), r"Local\Synthetic_RevitStartupGate_2025")

        ver_2026 = f"2026_test_{self.suffix}"
        ver_2025 = f"2025_test_{self.suffix}"
        marker_same_ver = os.path.join(self.tmp.name, "acquired_2026.txt")
        marker_diff_ver = os.path.join(self.tmp.name, "acquired_2025.txt")

        child_code = (
            "import importlib.util, sys\n"
            f"spec = importlib.util.spec_from_file_location('te', {SCRIPT_PATH!r})\n"
            "te = importlib.util.module_from_spec(spec)\n"
            "spec.loader.exec_module(te)\n"
            "ver, out_file = sys.argv[1], sys.argv[2]\n"
            "with te.RevitStartupGate(ver, acquire_timeout_ms=10000):\n"
            "    with open(out_file, 'w', encoding='utf-8') as f:\n"
            "        f.write('ACQUIRED')\n"
        )

        gate_2026 = te.RevitStartupGate(ver_2026, acquire_timeout_ms=10000)
        gate_2026.acquire()
        child_same = None
        child_diff = None
        try:
            # Child targeting SAME version must block while parent holds gate
            child_same = subprocess.Popen(
                [sys.executable, "-c", child_code, ver_2026, marker_same_ver],
                stdout=subprocess.PIPE,
                stderr=subprocess.PIPE,
            )
            # Child targeting DIFFERENT version must acquire and exit immediately without blocking
            child_diff = subprocess.Popen(
                [sys.executable, "-c", child_code, ver_2025, marker_diff_ver],
                stdout=subprocess.PIPE,
                stderr=subprocess.PIPE,
            )
            rc_diff = child_diff.wait(timeout=10)
            self.assertEqual(rc_diff, 0, msg=child_diff.stderr.read().decode())
            self.assertTrue(os.path.isfile(marker_diff_ver))

            # Give same-version child plenty of time to try acquiring and confirm it is blocked
            time.sleep(0.35)
            self.assertIsNone(child_same.poll(), "Same-version child should still be blocked on Startup Gate")
            self.assertFalse(os.path.exists(marker_same_ver))
        finally:
            gate_2026.release()

        rc_same = child_same.wait(timeout=10)
        self.assertEqual(rc_same, 0, msg=child_same.stderr.read().decode())
        self.assertTrue(os.path.isfile(marker_same_ver))

    def test_startup_gate_enforces_same_os_thread_release_affinity(self):
        ver = f"2026_affinity_{self.suffix}"
        gate = te.RevitStartupGate(ver, acquire_timeout_ms=5000)
        gate.acquire()
        wrong_thread_error = []
        try:
            def try_wrong_thread_release():
                try:
                    gate.release()
                except Exception as exc:
                    wrong_thread_error.append(exc)

            t = threading.Thread(target=try_wrong_thread_release)
            t.start()
            t.join(timeout=5)
            self.assertEqual(len(wrong_thread_error), 1)
            self.assertIsInstance(wrong_thread_error[0], RuntimeError)
            self.assertTrue(gate.is_held)
        finally:
            released = gate.release()
            self.assertTrue(released)
            self.assertFalse(gate.is_held)

        # Idempotent second release returns False cleanly
        self.assertFalse(gate.release())

        # Now another thread can acquire and release cleanly
        acquired_by_other = []

        def acquire_and_release():
            with te.RevitStartupGate(ver, acquire_timeout_ms=2000):
                acquired_by_other.append(True)

        t2 = threading.Thread(target=acquire_and_release)
        t2.start()
        t2.join(timeout=5)
        self.assertEqual(acquired_by_other, [True])

    def test_early_gate_release_on_journal_readiness_allows_concurrent_test_execution(self):
        journals_dir = os.path.join(self.tmp.name, "Journals")
        os.makedirs(journals_dir, exist_ok=True)
        pre_existing = os.path.join(journals_dir, "journal.0000.txt")
        with open(pre_existing, "w", encoding="utf-8") as f:
            f.write("' <-DesktopMFCApp::doStartupWarnings\n")
        # Another run's journal not in snapshot must be ignored
        other_run_j = os.path.join(journals_dir, "journal.0003.txt")
        with open(other_run_j, "w", encoding="utf-8") as f:
            f.write("' 0:< SYNTHETIC_TEST_RUN:other_agent\n' <-DesktopMFCApp::doStartupWarnings\n")

        ver = f"2026_early_{self.suffix}"
        events = {}
        events_lock = threading.Lock()

        class _TimedFakeProc:
            _next_pid = 10000

            def __init__(self, label, run_id, journal_name, ready_delay, total_duration, marker):
                _TimedFakeProc._next_pid += 1
                self.pid = _TimedFakeProc._next_pid
                self.returncode = None
                self._start = time.monotonic()
                self._total = total_duration
                jpath = os.path.join(journals_dir, journal_name)
                with open(jpath, "w", encoding="utf-8") as f:
                    f.write(f"' 0:< SYNTHETIC_TEST_RUN:{run_id}\n' ->DesktopMFCApp::doStartupWarnings\n")

                def write_ready():
                    time.sleep(ready_delay)
                    with open(jpath, "a", encoding="utf-8") as f:
                        f.write(f"{marker}\n")
                    with events_lock:
                        events[f"{label}_ready_written"] = time.monotonic()

                threading.Thread(target=write_ready, daemon=True).start()

            def poll(self):
                if self.returncode is not None:
                    return self.returncode
                if time.monotonic() - self._start >= self._total:
                    self.returncode = 0
                return self.returncode

            def wait(self, timeout=None):
                deadline = None if timeout is None else time.monotonic() + timeout
                while self.poll() is None:
                    if deadline is not None and time.monotonic() >= deadline:
                        raise subprocess.TimeoutExpired("dotnet", timeout)
                    time.sleep(0.01)
                return self.returncode

            def kill(self):
                self.returncode = -1

        def make_popen(label, run_id, journal_name, ready_delay, total_duration, marker):
            def _popen(cmd, stdout=None, stderr=None, env=None):
                with events_lock:
                    events[f"{label}_started"] = time.monotonic()
                return _TimedFakeProc(label, run_id, journal_name, ready_delay, total_duration, marker)
            return _popen

        results = {}

        def run_worker(label, run_id, journal_name, ready_delay, total_duration, marker, delay_before_start=0.0):
            if delay_before_start:
                time.sleep(delay_before_start)
            res = te.execute_revit_test_with_startup_gate(
                ["dotnet", "test"],
                subprocess.DEVNULL,
                {"SYNTHETIC_TEST_RUN_ID": run_id},
                version=ver,
                run_id=run_id,
                journals_dir=journals_dir,
                known_journals=[pre_existing],
                startup_hold_cap=10.0,
                poll_interval=0.02,
                timeout=10.0,
                popen_fn=make_popen(label, run_id, journal_name, ready_delay, total_duration, marker),
            )
            with events_lock:
                events[f"{label}_finished"] = time.monotonic()
            results[label] = res

        t1 = threading.Thread(
            target=run_worker,
            args=("w1", "run_w1", "journal.0001.txt", 0.15, 0.55, "' <-DesktopMFCApp::doStartupWarnings", 0.0),
        )
        t2 = threading.Thread(
            target=run_worker,
            args=("w2", "run_w2", "journal.0002.txt", 0.05, 0.15, "' ricaun.RevitTest: Execute: Tests.dll", 0.04),
        )
        t1.start()
        t2.start()
        t1.join(timeout=10)
        t2.join(timeout=10)

        self.assertEqual(results["w1"], (0, False, "run_w1", False))
        self.assertEqual(results["w2"], (0, False, "run_w2", False))
        self.assertGreaterEqual(events["w2_started"], events["w1_ready_written"])
        self.assertLess(events["w2_started"], events["w1_finished"])

    def test_startup_gate_releases_on_process_exit_and_on_hold_cap_expiry(self):
        journals_dir = os.path.join(self.tmp.name, "JournalsCap")
        os.makedirs(journals_dir, exist_ok=True)
        ver = f"2026_cap_{self.suffix}"

        # Case A: Fast process exit without journal releases gate quickly (does not stall for 10s hold cap)
        class _FastExitProc:
            pid = 20001
            returncode = None

            def __init__(self):
                self._start = time.monotonic()

            def poll(self):
                if time.monotonic() - self._start >= 0.04:
                    self.returncode = 0
                return self.returncode

            def wait(self, timeout=None):
                while self.poll() is None:
                    time.sleep(0.01)
                return self.returncode

            def kill(self):
                self.returncode = -1

        t_start = time.monotonic()
        res_fast = te.execute_revit_test_with_startup_gate(
            ["dotnet", "test"],
            subprocess.DEVNULL,
            {"SYNTHETIC_TEST_RUN_ID": "fast_exit"},
            version=ver,
            run_id="fast_exit",
            journals_dir=journals_dir,
            known_journals=[],
            startup_hold_cap=10.0,
            poll_interval=0.02,
            timeout=10.0,
            popen_fn=lambda *a, **kw: _FastExitProc(),
        )
        elapsed_fast = time.monotonic() - t_start
        self.assertEqual(res_fast, (0, False, "fast_exit", False))
        self.assertLess(elapsed_fast, 2.0)

        # Case B: Hold cap expiry releases gate early while slow boot finishes cleanly afterwards
        class _SlowNoReadyJournalProc:
            pid = 20002
            returncode = None

            def __init__(self):
                self._start = time.monotonic()
                with open(os.path.join(journals_dir, "journal.0010.txt"), "w", encoding="utf-8") as f:
                    f.write("' 0:< SYNTHETIC_TEST_RUN:slow_cap\n' ->DesktopMFCApp::doStartupWarnings\n")

            def poll(self):
                if self.returncode is not None:
                    return self.returncode
                if time.monotonic() - self._start >= 0.45:
                    self.returncode = 0
                return self.returncode

            def wait(self, timeout=None):
                while self.poll() is None:
                    time.sleep(0.01)
                return self.returncode

            def kill(self):
                self.returncode = -1

        gate_acquired_while_running = []
        proc_finished_at = []

        def probe_gate():
            time.sleep(0.05)
            with te.RevitStartupGate(ver, acquire_timeout_ms=3000):
                gate_acquired_while_running.append(time.monotonic())

        probe_thread = threading.Thread(target=probe_gate)
        probe_thread.start()
        res_cap = te.execute_revit_test_with_startup_gate(
            ["dotnet", "test"],
            subprocess.DEVNULL,
            {"SYNTHETIC_TEST_RUN_ID": "slow_cap"},
            version=ver,
            run_id="slow_cap",
            journals_dir=journals_dir,
            known_journals=[],
            startup_hold_cap=0.15,
            poll_interval=0.02,
            timeout=5.0,
            popen_fn=lambda *a, **kw: _SlowNoReadyJournalProc(),
        )
        proc_finished_at.append(time.monotonic())
        probe_thread.join(timeout=5)
        self.assertEqual(res_cap, (0, False, "slow_cap", False))
        self.assertEqual(len(gate_acquired_while_running), 1)
        self.assertLess(gate_acquired_while_running[0], proc_finished_at[0])


class TestStartupModalWatchdogAndRetry(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.journals_dir = os.path.join(self.tmp.name, "Journals")
        os.makedirs(self.journals_dir, exist_ok=True)
        self.suffix = f"{os.getpid()}_{uuid.uuid4().hex[:6]}"

    def tearDown(self):
        self.tmp.cleanup()

    def test_descendant_revit_pid_tree_and_real_win32_dialog_detection(self):
        snapshot = [
            (100, 1, "dotnet.exe"),
            (101, 100, "testhost.exe"),
            (102, 101, "ricaun.RevitTest.Console.exe"),
            (103, 102, "Revit.exe"),
            (104, 103, "RevitWorker.exe"),
            (105, 103, "msedgewebview2.exe"),
            (999, 1, "Revit.exe"),  # Unrelated interactive Revit session
        ]
        self.assertEqual(te.find_descendant_revit_pids(100, process_snapshot=snapshot), [103])
        self.assertEqual(te.find_descendant_revit_pids(999, process_snapshot=snapshot), [999])

        # Mocked window list: ignores unrelated Revit 999, detects descendant Revit 103 via title or child text
        unrelated_only = [
            (1, 999, "#32770", "External Tools - External Tool Failure", []),
            (2, 103, "#32770", "Normal Revit Dialog", ["OK"]),
        ]
        self.assertIsNone(
            te.detect_startup_modal_window(
                100,
                process_snapshot_fn=lambda: snapshot,
                enumerate_windows_fn=lambda target_pids: unrelated_only,
            )
        )

        child_match = [
            (1, 999, "#32770", "External Tools - External Tool Failure", []),
            (3, 103, "#32770", "Warning", ["Revit cannot run the external application CTC BIM Project Suite"]),
        ]
        found_mock = te.detect_startup_modal_window(
            100,
            process_snapshot_fn=lambda: snapshot,
            enumerate_windows_fn=lambda target_pids: child_match,
        )
        self.assertIsNotNone(found_mock)
        self.assertEqual(found_mock["pid"], 103)

        # Real Win32 CreateWindowExW + EnumWindows + GetClassNameW + GetWindowTextW + EnumChildWindows
        import ctypes
        from ctypes import wintypes

        u32 = ctypes.WinDLL("user32", use_last_error=True)
        u32.CreateWindowExW.argtypes = [
            wintypes.DWORD, wintypes.LPCWSTR, wintypes.LPCWSTR, wintypes.DWORD,
            ctypes.c_int, ctypes.c_int, ctypes.c_int, ctypes.c_int,
            wintypes.HWND, wintypes.HMENU, wintypes.HINSTANCE, wintypes.LPVOID,
        ]
        u32.CreateWindowExW.restype = wintypes.HWND
        u32.DestroyWindow.argtypes = [wintypes.HWND]
        u32.DestroyWindow.restype = wintypes.BOOL

        hwnd_parent = u32.CreateWindowExW(
            0, "#32770", "External Tools - External Tool Failure",
            0, 0, 0, 120, 120, None, None, None, None
        )
        self.assertTrue(hwnd_parent)
        hwnd_child = u32.CreateWindowExW(
            0, "STATIC", "Revit cannot run the external application",
            0x40000000, 0, 0, 50, 50, hwnd_parent, None, None, None
        )
        try:
            self_pid = os.getpid()
            real_found = te.detect_startup_modal_window(
                self_pid,
                process_snapshot_fn=lambda: [(self_pid, os.getppid(), "Revit.exe")],
            )
            self.assertIsNotNone(real_found)
            self.assertEqual(real_found["pid"], self_pid)
            self.assertEqual(real_found["class_name"], "#32770")
            self.assertIn("External Tools - External Tool Failure", real_found["title"])
        finally:
            if hwnd_child:
                u32.DestroyWindow(hwnd_child)
            if hwnd_parent:
                u32.DestroyWindow(hwnd_parent)

    def test_journal_modal_watchdog_kills_only_wedged_pid_releases_gate_and_retries_once(self):
        pre_existing = os.path.join(self.journals_dir, "journal.0000.txt")
        with open(pre_existing, "w", encoding="utf-8") as f:
            f.write("' baseline\n")
        snapshot_path = os.path.join(self.tmp.name, "known_journals.json")
        te.snapshot_known_journals("2026", snapshot_path, journals_dir=self.journals_dir)

        # Unrelated run journal that already has a modal must NEVER trigger our watchdog
        other_j = os.path.join(self.journals_dir, "journal.0001.txt")
        with open(other_j, "w", encoding="utf-8") as f:
            f.write("' 0:< SYNTHETIC_TEST_RUN:someone_else\n'Id : TaskDialog_External_Tools_External_Tool_Failure\n")

        ver = f"2026_modal_j_{self.suffix}"
        gate_events = []
        killed_pids = []
        spawned_envs = []

        class _SpyGate(te.RevitStartupGate):
            def acquire(self):
                gate_events.append(("acquire", len(killed_pids)))
                return super().acquire()

            def release(self):
                was_held = self.is_held
                res = super().release()
                if was_held:
                    gate_events.append(("release", len(killed_pids)))
                return res

        class _FakeAttemptProc:
            def __init__(self, pid, should_hang, journal_path, run_id_val, unattributed=False):
                self.pid = pid
                self.returncode = None
                self._should_hang = should_hang
                self._start = time.monotonic()
                if should_hang:
                    # Test fallback 2(a): newly created journal without SYNTHETIC_TEST_RUN yet,
                    # OR matching SYNTHETIC_TEST_RUN:<run_id>
                    content = (
                        "' 0:< start up warnings for AddIns\n"
                        "'Id : TaskDialog_External_Tools_External_Tool_Failure\n"
                    )
                    if not unattributed:
                        content = f"' 0:< SYNTHETIC_TEST_RUN:{run_id_val}\n" + content
                    with open(journal_path, "w", encoding="utf-8") as f:
                        f.write(content)
                else:
                    with open(journal_path, "w", encoding="utf-8") as f:
                        f.write(
                            f"' 0:< SYNTHETIC_TEST_RUN:{run_id_val}\n"
                            "' <-DesktopMFCApp::doStartupWarnings\n"
                        )

            def poll(self):
                if self.returncode is not None:
                    return self.returncode
                if not self._should_hang and time.monotonic() - self._start >= 0.05:
                    self.returncode = 0
                return self.returncode

            def wait(self, timeout=None):
                deadline = None if timeout is None else time.monotonic() + timeout
                while self.poll() is None:
                    if deadline is not None and time.monotonic() >= deadline:
                        raise subprocess.TimeoutExpired("dotnet", timeout)
                    time.sleep(0.01)
                return self.returncode

            def kill(self):
                self.returncode = -1

        procs = {}

        def fake_popen(cmd, stdout=None, stderr=None, env=None):
            attempt_idx = len(spawned_envs)
            spawned_envs.append(dict(env))
            pid = 51000 + attempt_idx
            jname = f"journal.001{attempt_idx}.txt"
            p = _FakeAttemptProc(
                pid=pid,
                should_hang=(attempt_idx == 0),
                journal_path=os.path.join(self.journals_dir, jname),
                run_id_val=env.get("SYNTHETIC_TEST_RUN_ID"),
                unattributed=(attempt_idx == 0),
            )
            procs[pid] = p
            return p

        def fake_tree_killer(pid):
            killed_pids.append(pid)
            procs[pid].returncode = 1

        rc, timed_out, effective_run_id, modal_aborted = te.execute_revit_test_with_startup_gate(
            ["dotnet", "test"],
            subprocess.DEVNULL,
            {"SYNTHETIC_TEST_RUN_ID": "run_modal_j"},
            version=ver,
            run_id="run_modal_j",
            journals_dir=self.journals_dir,
            snapshot_path=snapshot_path,
            timeout=10.0,
            startup_hold_cap=5.0,
            poll_interval=0.02,
            max_retries=1,
            popen_fn=fake_popen,
            tree_killer=fake_tree_killer,
            gate_factory=lambda v: _SpyGate(v, acquire_timeout_ms=5000),
            window_modal_detector=lambda pid: None,
        )

        self.assertEqual(rc, 0)
        self.assertFalse(timed_out)
        self.assertFalse(modal_aborted)
        self.assertEqual(effective_run_id, "run_modal_j_retry1")
        self.assertEqual(killed_pids, [51000])
        self.assertEqual(len(spawned_envs), 2)
        self.assertEqual(spawned_envs[0]["SYNTHETIC_TEST_RUN_ID"], "run_modal_j")
        self.assertEqual(spawned_envs[1]["SYNTHETIC_TEST_RUN_ID"], "run_modal_j_retry1")
        # Gate must be acquired, released immediately after kill, then acquired again for retry, then released
        self.assertEqual(
            gate_events,
            [("acquire", 0), ("release", 1), ("acquire", 1), ("release", 1)],
        )
        # Snapshot updated so Attempt 0's aborted journal is excluded for post-run fallback
        with open(snapshot_path, "r", encoding="utf-8") as f:
            updated_known = json.load(f)
        self.assertIn(os.path.abspath(os.path.join(self.journals_dir, "journal.0010.txt")), updated_known)

    def test_hwnd_modal_watchdog_catches_post_hold_cap_hang_and_exhausts_single_retry(self):
        ver = f"2026_modal_w_{self.suffix}"
        killed_pids = []
        attempts = []

        class _Proc:
            def __init__(self, pid, succeeds=False):
                self.pid = pid
                self.returncode = None
                self._succeeds = succeeds
                self._start = time.monotonic()

            def poll(self):
                if self.returncode is not None:
                    return self.returncode
                if self._succeeds and time.monotonic() - self._start >= 0.04:
                    self.returncode = 0
                return self.returncode

            def wait(self, timeout=None):
                deadline = None if timeout is None else time.monotonic() + timeout
                while self.poll() is None:
                    if deadline is not None and time.monotonic() >= deadline:
                        raise subprocess.TimeoutExpired("dotnet", timeout)
                    time.sleep(0.01)
                return self.returncode

            def kill(self):
                self.returncode = -1

        # Part A: modal appears via HWND after startup_hold_cap expires -> still caught before tests, retry succeeds
        procs_a = {}
        attempt0_start = []

        def popen_a(cmd, stdout=None, stderr=None, env=None):
            idx = len(attempts)
            attempts.append(idx)
            pid = 61000 + idx
            if idx == 0:
                attempt0_start.append(time.monotonic())
            else:
                jpath = os.path.join(self.journals_dir, "journal.0021.txt")
                with open(jpath, "w", encoding="utf-8") as f:
                    f.write(f"' 0:< SYNTHETIC_TEST_RUN:{env['SYNTHETIC_TEST_RUN_ID']}\n' <-DesktopMFCApp::doStartupWarnings\n")
            p = _Proc(pid, succeeds=(idx == 1))
            procs_a[pid] = p
            return p

        def window_detector_a(pid):
            # Trigger on pid 61000 AFTER startup_hold_cap (0.06s) has already expired
            if pid == 61000 and attempt0_start and (time.monotonic() - attempt0_start[0]) >= 0.10:
                return {"pid": 61999, "hwnd": 12345, "class_name": "#32770", "title": "External Tools - External Tool Failure"}
            return None

        def killer_a(pid):
            killed_pids.append(pid)
            procs_a[pid].returncode = 1

        rc_a, timed_out_a, eff_id_a, modal_aborted_a = te.execute_revit_test_with_startup_gate(
            ["dotnet", "test"],
            subprocess.DEVNULL,
            {"SYNTHETIC_TEST_RUN_ID": "run_hwnd_a"},
            version=ver,
            run_id="run_hwnd_a",
            journals_dir=self.journals_dir,
            known_journals=[],
            timeout=10.0,
            startup_hold_cap=0.06,
            poll_interval=0.02,
            max_retries=1,
            popen_fn=popen_a,
            tree_killer=killer_a,
            window_modal_detector=window_detector_a,
        )
        self.assertEqual((rc_a, timed_out_a, eff_id_a, modal_aborted_a), (0, False, "run_hwnd_a_retry1", False))
        self.assertEqual(killed_pids, [61000])

        # Part B: both initial attempt and retry hit modal -> capped at max_retries=1 (2 total attempts)
        killed_b = []
        procs_b = {}

        def popen_b(cmd, stdout=None, stderr=None, env=None):
            pid = 62000 + len(procs_b)
            p = _Proc(pid, succeeds=False)
            procs_b[pid] = p
            return p

        def killer_b(pid):
            killed_b.append(pid)
            procs_b[pid].returncode = 1

        rc_b, timed_out_b, eff_id_b, modal_aborted_b = te.execute_revit_test_with_startup_gate(
            ["dotnet", "test"],
            subprocess.DEVNULL,
            {"SYNTHETIC_TEST_RUN_ID": "run_exhaust"},
            version=f"2026_modal_b_{self.suffix}",
            run_id="run_exhaust",
            journals_dir=self.journals_dir,
            known_journals=[],
            timeout=10.0,
            startup_hold_cap=1.0,
            poll_interval=0.02,
            max_retries=1,
            popen_fn=popen_b,
            tree_killer=killer_b,
            window_modal_detector=lambda pid: {"pid": pid + 1, "hwnd": 1, "class_name": "#32770", "title": "External Tools - External Tool Failure"},
        )
        self.assertNotEqual(rc_b, 0)
        self.assertFalse(timed_out_b)
        self.assertTrue(modal_aborted_b)
        self.assertEqual(eff_id_b, "run_exhaust_retry1")
        self.assertEqual(killed_b, [62000, 62001])

    def test_prespawn_unattributed_crashed_journal_from_prior_worker_is_ignored(self):
        # Simulate an earlier worker's unattributed journal left on disk before we acquired the gate
        stale_modal_j = os.path.join(self.journals_dir, "journal.0099.txt")
        with open(stale_modal_j, "w", encoding="utf-8") as f:
            f.write("'Id : TaskDialog_External_Tools_External_Tool_Failure\n")

        class _CleanProc:
            pid = 70001
            returncode = None

            def __init__(self, jdir):
                self._start = time.monotonic()
                with open(os.path.join(jdir, "journal.0100.txt"), "w", encoding="utf-8") as f:
                    f.write("' 0:< SYNTHETIC_TEST_RUN:run_clean_after_stale\n' <-DesktopMFCApp::doStartupWarnings\n")

            def poll(self):
                if time.monotonic() - self._start >= 0.04:
                    self.returncode = 0
                return self.returncode

            def wait(self, timeout=None):
                while self.poll() is None:
                    time.sleep(0.01)
                return self.returncode

            def kill(self):
                self.returncode = -1

        killed = []
        rc, timed_out, eff_id, modal_aborted = te.execute_revit_test_with_startup_gate(
            ["dotnet", "test"],
            subprocess.DEVNULL,
            {"SYNTHETIC_TEST_RUN_ID": "run_clean_after_stale"},
            version=f"2026_stale_{self.suffix}",
            run_id="run_clean_after_stale",
            journals_dir=self.journals_dir,
            known_journals=[],
            timeout=5.0,
            startup_hold_cap=2.0,
            poll_interval=0.02,
            popen_fn=lambda *a, **kw: _CleanProc(self.journals_dir),
            tree_killer=lambda pid: killed.append(pid),
            window_modal_detector=lambda pid: None,
        )
        self.assertEqual((rc, timed_out, eff_id, modal_aborted), (0, False, "run_clean_after_stale", False))
        self.assertEqual(killed, [])


class TestWorkerPoolRoutingAndShardedJournalGating(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.root = self.tmp.name
        self.tests_dir = os.path.join(self.root, "tests")
        self.journals_dir = os.path.join(self.root, "Journals")
        os.makedirs(self.journals_dir, exist_ok=True)

        # Create minimal projects for Logic, 2024 (net48), 2026 (net8.0-windows8.0)
        for rel_dir, proj_name, tfm in (
            ("Synthetic.LogicTests", "Synthetic.LogicTests", "net8.0-windows8.0"),
            ("Synthetic.RevitTests.2024", "Synthetic.RevitTests.2024", "net48"),
            ("Synthetic.RevitTests.2026", "Synthetic.RevitTests.2026", "net8.0-windows8.0"),
        ):
            pdir = os.path.join(self.tests_dir, "Projects", rel_dir)
            os.makedirs(pdir, exist_ok=True)
            with open(os.path.join(pdir, f"{proj_name}.csproj"), "w", encoding="utf-8") as f:
                f.write(
                    f"<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup>"
                    f"<TargetFramework>{tfm}</TargetFramework></PropertyGroup></Project>"
                )

        # Create one RevitSmoke test class so category pre-check finds a match
        smoke_dir = os.path.join(self.tests_dir, "Platform", "Host", "Revit")
        os.makedirs(smoke_dir, exist_ok=True)
        with open(os.path.join(smoke_dir, "SmokeRevitTests.cs"), "w", encoding="utf-8") as f:
            f.write(
                """
using NUnit.Framework;
namespace SyntheticTests.Platform.Host
{
    [TestFixture]
    [RevitSmokeTest]
    public class SmokeRevitTests
    {
        [Test]
        public void Smoke() {}
    }
}
"""
            )

        # Built RevitWorkerPool.exe at standard tools/bin/RevitWorkerPool/RevitWorkerPool.exe
        pool_bin_dir = os.path.join(self.root, "tools", "bin", "RevitWorkerPool")
        os.makedirs(pool_bin_dir, exist_ok=True)
        self.pool_exe = os.path.join(pool_bin_dir, "RevitWorkerPool.exe")
        with open(self.pool_exe, "w", encoding="utf-8") as f:
            f.write("")

    def tearDown(self):
        self.tmp.cleanup()

    @staticmethod
    def _write_trx(trx_path, passed_names=(), failed_tests=()):
        os.makedirs(os.path.dirname(trx_path), exist_ok=True)
        total = len(passed_names) + len(failed_tests)
        passed = len(passed_names)
        failed = len(failed_tests)
        results_xml = []
        for idx, name in enumerate(passed_names):
            results_xml.append(
                f'<UnitTestResult executionId="00000000-0000-0000-0000-0000000000{idx:02d}" '
                f'testId="10000000-0000-0000-0000-0000000000{idx:02d}" '
                f'testName="{name}" outcome="Passed" />'
            )
        for idx, (name, msg, stack) in enumerate(failed_tests):
            results_xml.append(
                f'<UnitTestResult executionId="20000000-0000-0000-0000-0000000000{idx:02d}" '
                f'testId="30000000-0000-0000-0000-0000000000{idx:02d}" '
                f'testName="{name}" outcome="Failed">'
                f"<Output><ErrorInfo><Message>{msg}</Message><StackTrace>{stack}</StackTrace></ErrorInfo></Output>"
                f"</UnitTestResult>"
            )

        xml = (
            '<?xml version="1.0" encoding="utf-8"?>\n'
            '<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">\n'
            f"  <Results>{''.join(results_xml)}</Results>\n"
            "  <ResultSummary>\n"
            f'    <Counters total="{total}" executed="{total}" passed="{passed}" failed="{failed}" />\n'
            "  </ResultSummary>\n"
            "</TestRun>\n"
        )
        with open(trx_path, "w", encoding="utf-8") as f:
            f.write(xml)

    def test_cli_parses_no_pool_no_shim_shards_and_rejects_invalid_shards(self):
        parsed_default = te.parse_cli_args([])
        self.assertFalse(parsed_default.no_pool)
        self.assertFalse(parsed_default.no_shim)
        self.assertIsNone(parsed_default.shards)

        parsed_flags = te.parse_cli_args(
            ["--version", "2026", "--filter", "Smoke", "--category", "RevitSmoke", "--no-pool", "--no-shim", "--shards", "3"]
        )
        self.assertEqual(parsed_flags.target_version, "2026")
        self.assertEqual(parsed_flags.target_class, "Smoke")
        self.assertEqual(parsed_flags.category, "RevitSmoke")
        self.assertTrue(parsed_flags.no_pool)
        self.assertTrue(parsed_flags.no_shim)
        self.assertEqual(parsed_flags.shards, 3)

        for bad_shards in ("0", "-2"):
            res = subprocess.run(
                [sys.executable, SCRIPT_PATH, "--shards", bad_shards],
                cwd=REPO_ROOT,
                capture_output=True,
                text=True,
            )
            self.assertNotEqual(res.returncode, 0, msg=f"Expected failure for --shards {bad_shards}")
            self.assertIn("--shards", res.stderr + res.stdout)

    def test_cli_help_cites_2027_and_net10_windows8(self):
        res = subprocess.run(
            [sys.executable, SCRIPT_PATH, "--help"],
            cwd=REPO_ROOT,
            capture_output=True,
            text=True,
        )
        self.assertEqual(res.returncode, 0, msg=res.stderr)
        help_text = res.stdout
        self.assertIn("2027", help_text)
        self.assertIn("net10.0-windows8.0", help_text)

    def test_build_worker_pool_run_command_for_exe_and_py(self):
        self.assertFalse(hasattr(te, "get_next_revit_version_map"))
        exe_cmd = te.build_worker_pool_run_command(
            pool_cmd=r"C:\repo\tools\bin\RevitWorkerPool\RevitWorkerPool.exe",
            version="2024",
            assembly_path=r"C:\tmp\Synthetic.RevitTests.2024.dll",
            trx_path=r"C:\tmp\results_2024.trx",
            project_root=r"C:\repo",
            target_class="SmokeRevitTests",
            category="RevitSmoke",
            shards=4,
            no_shim=True,
        )
        self.assertEqual(
            exe_cmd,
            [
                r"C:\repo\tools\bin\RevitWorkerPool\RevitWorkerPool.exe",
                "run",
                "--version", "2024",
                "--assembly", r"C:\tmp\Synthetic.RevitTests.2024.dll",
                "--trx", r"C:\tmp\results_2024.trx",
                "--project-root", r"C:\repo",
                "--json",
                "--diagnostics",
                "--filter", "SmokeRevitTests",
                "--category", "RevitSmoke",
                "--shards", "4",
                "--no-shim",
            ],
        )
        self.assertNotIn("--next-version", exe_cmd)

        py_cmd = te.build_worker_pool_run_command(
            pool_cmd=r"C:\repo\tools\RevitWorkerPool.py",
            version="2026",
            assembly_path=r"C:\tmp\Synthetic.RevitTests.2026.dll",
            trx_path=r"C:\tmp\results_2026.trx",
            project_root=r"C:\repo",
        )
        self.assertEqual(py_cmd[0], sys.executable)
        self.assertEqual(py_cmd[1], r"C:\repo\tools\RevitWorkerPool.py")
        self.assertNotIn("--no-shim", py_cmd)
        self.assertNotIn("--shards", py_cmd)
        self.assertNotIn("--next-version", py_cmd)
        self.assertNotIn("--filter", py_cmd)
        self.assertNotIn("--category", py_cmd)

    def test_worker_journal_cleanliness_gate_invocation_and_real_worker_id_gating(self):
        captured_cmds = []

        def fake_runner(cmd, **kwargs):
            captured_cmds.append(cmd)
            return subprocess.CompletedProcess(cmd, 0, stdout="Cleanliness Gate Result: CLEAN", stderr="")

        ok, out = te.run_worker_journal_cleanliness_gate("2026", "w_shard_1", runner=fake_runner)
        self.assertTrue(ok)
        self.assertIn("CLEAN", out)
        self.assertEqual(
            captured_cmds[0][1:],
            [
                te.get_journal_tool_path(),
                "--version",
                "2026",
                "--worker-id",
                "w_shard_1",
                "--assert-clean",
            ],
        )

        clean_j = os.path.join(self.journals_dir, "journal.0001.txt")
        with open(clean_j, "w", encoding="utf-8") as f:
            f.write("' 0:< SYNTHETIC_WORKER_ID:w_ok\n' 0:< ricaun.RevitTest: Execute Total: 00:00:01.0\n")

        dirty_j = os.path.join(self.journals_dir, "journal.0002.txt")
        with open(dirty_j, "w", encoding="utf-8") as f:
            f.write("' 0:< SYNTHETIC_WORKER_ID:w_leaked\n'inTransaction APITransactionIdentity_1:-1 LeakedTx\n")

        def run_with_dir(cmd, **kwargs):
            return subprocess.run([*cmd, "--journals-dir", self.journals_dir], **kwargs)

        ok_clean, out_clean = te.run_worker_journal_cleanliness_gate("2026", "w_ok", runner=run_with_dir)
        self.assertTrue(ok_clean)
        self.assertIn("Cleanliness Gate Result: CLEAN", out_clean)

        ok_dirty, out_dirty = te.run_worker_journal_cleanliness_gate("2026", "w_leaked", runner=run_with_dir)
        self.assertFalse(ok_dirty)
        self.assertIn("Cleanliness Gate Result: DIRTY", out_dirty)

    def test_pool_mode_routes_parallel_sweep_without_next_version_shards_raw_filters_and_runs_logic_first(self):
        import contextlib
        from unittest import mock

        pool_calls = []
        dotnet_calls = []
        build_calls = []
        gated_workers = []
        whitelisted = []
        legacy_checks = []
        call_lock = threading.Lock()
        temp_build_root = os.path.join(self.root, "temp_root")
        os.makedirs(temp_build_root, exist_ok=True)

        def fake_subproc_run(cmd, **kwargs):
            exe_name = os.path.basename(cmd[0]).lower()
            if cmd[0] == self.pool_exe:
                with call_lock:
                    pool_calls.append(list(cmd))
                ver = cmd[cmd.index("--version") + 1]
                trx_path = cmd[cmd.index("--trx") + 1]
                self._write_trx(trx_path, passed_names=[f"Smoke_{ver}"])
                payload = {
                    "success": True,
                    "exitCode": 0,
                    "version": int(ver),
                    "trxPath": trx_path,
                    "workerIds": [f"w_{ver}_1", f"w_{ver}_2"],
                    "shardCount": 2,
                    "totalTests": 1,
                    "passedTests": 1,
                    "failedTests": 0,
                    "skippedTests": 0,
                    "zeroMatches": False,
                    "shimEnabled": True,
                    "error": None,
                }
                return subprocess.CompletedProcess(
                    cmd,
                    0,
                    stdout=f"banner line\n{json.dumps(payload)}\n",
                    stderr=f"[diag] ran Revit {ver}\n",
                )
            if "dotnet" in exe_name and len(cmd) > 1 and cmd[1] == "build":
                with call_lock:
                    build_calls.append(list(cmd))
                return subprocess.CompletedProcess(cmd, 0, stdout="", stderr="")
            if "dotnet" in exe_name and len(cmd) > 1 and cmd[1] == "test":
                with call_lock:
                    dotnet_calls.append(list(cmd))
                res_dir = cmd[cmd.index("--results-directory") + 1]
                logger_arg = cmd[cmd.index("--logger") + 1]
                trx_name = logger_arg.split("LogFileName=", 1)[1]
                self._write_trx(os.path.join(res_dir, trx_name), passed_names=["LogicTest"])
                return subprocess.CompletedProcess(cmd, 0)
            raise AssertionError(f"Unexpected subprocess call: {cmd}")

        def fake_gate(version, worker_id, **kwargs):
            with call_lock:
                gated_workers.append((str(version), worker_id))
            return True, f"[GATE] {version} {worker_id}: CLEAN"

        stdout_buf = io.StringIO()
        with mock.patch.dict(os.environ, {"SYNTHETIC_PROJECT_ROOT": self.root}, clear=False), \
             mock.patch("os.getcwd", return_value=self.root), \
             mock.patch.object(te.tempfile, "gettempdir", return_value=temp_build_root), \
             mock.patch.object(te, "configure_dotnet_environment", return_value=None), \
             mock.patch.object(te.guid_whitelister, "whitelist_existing_addins", side_effect=lambda v: whitelisted.append(v)), \
             mock.patch.object(te, "check_legacy_programdata_manifest", side_effect=lambda v: legacy_checks.append(v)), \
             mock.patch.object(te, "run_worker_journal_cleanliness_gate", side_effect=fake_gate), \
             mock.patch.object(te.subprocess, "run", side_effect=fake_subproc_run), \
             contextlib.redirect_stdout(stdout_buf):
            with self.assertRaises(SystemExit) as exit_ctx:
                te.run_test_suite(
                    target_class="SmokeRevitTests",
                    target_version="",
                    category="RevitSmoke",
                    no_pool=False,
                    no_shim=False,
                    shards=3,
                )

        self.assertEqual(exit_ctx.exception.code, 0, msg=stdout_buf.getvalue())
        out_text = stdout_buf.getvalue()
        self.assertIn("Total: 3", out_text)
        self.assertIn("[PASS]", out_text)
        self.assertRegex(out_text, r"\[TESTS\] Revit 2024: PASS \(1 passed, 0 failed, clean journal, [0-9.]+s\)")
        self.assertRegex(out_text, r"\[TESTS\] Revit 2026: PASS \(1 passed, 0 failed, clean journal, [0-9.]+s\)")

        # Logic ran via dotnet test first; every target used isolated <RunBuildRoot>\<TargetKey>
        self.assertEqual(len(build_calls), 3)
        self.assertTrue(all(len(c) > 1 and c[1] == "build" for c in build_calls))
        for bcall in build_calls:
            iso_arg = next(a for a in bcall if a.startswith("-p:SyntheticIsolatedBuildRoot="))
            proj_base = os.path.basename(bcall[2])
            expected_key = "Logic" if "Logic" in proj_base else ("2024" if "2024" in proj_base else "2026")
            self.assertEqual(os.path.basename(iso_arg.split("=", 1)[1]), expected_key)
        self.assertEqual(len(dotnet_calls), 1)
        self.assertIn("Synthetic.LogicTests.csproj", dotnet_calls[0][2])

        # Pool ran for 2024 and 2026 without --next-version
        self.assertEqual(len(pool_calls), 2)
        calls_by_ver = {c[c.index("--version") + 1]: c for c in pool_calls}
        self.assertEqual(set(calls_by_ver.keys()), {"2024", "2026"})

        for ver, call in calls_by_ver.items():
            self.assertNotIn("--next-version", call)
            self.assertEqual(call[call.index("--filter") + 1], "SmokeRevitTests")
            self.assertEqual(call[call.index("--category") + 1], "RevitSmoke")
            self.assertEqual(call[call.index("--shards") + 1], "3")
            self.assertNotIn("--no-shim", call)
            self.assertNotIn("FullyQualifiedName~", " ".join(call))
            asm = call[call.index("--assembly") + 1]
            self.assertTrue(asm.endswith(f"Synthetic.RevitTests.{ver}.dll"), msg=asm)
            self.assertIn(os.path.sep + ver + os.path.sep, asm)

        # Whitelist called for both; legacy ProgramData check NOT called when shimmed
        self.assertEqual(set(whitelisted), {"2024", "2026"})
        self.assertEqual(legacy_checks, [])

        # Per-shard --worker-id gates ran for every consumed worker ID
        self.assertEqual(
            set(gated_workers),
            {("2024", "w_2024_1"), ("2024", "w_2024_2"), ("2026", "w_2026_1"), ("2026", "w_2026_2")},
        )

        # Isolated build root cleaned on pass; diagnostics written to output_<ver>.log
        builds_dir = os.path.join(temp_build_root, "SyntheticBuilds")
        self.assertFalse(os.path.exists(builds_dir) and os.listdir(builds_dir))
        results_dirs = [
            d for d in os.listdir(os.path.join(self.tests_dir, "TestResults"))
            if te.RUN_DIR_PATTERN.match(d)
        ]
        self.assertEqual(len(results_dirs), 1)
        log_2024 = os.path.join(self.tests_dir, "TestResults", results_dirs[0], "output_2024.log")
        with open(log_2024, "r", encoding="utf-8") as f:
            log_content = f.read()
        self.assertIn("[diag] ran Revit 2024", log_content)

    def test_pool_mode_zero_matches_and_fail_closed_shim_error_never_falls_back(self):
        import contextlib
        from unittest import mock

        temp_build_root = os.path.join(self.root, "temp_root")
        os.makedirs(temp_build_root, exist_ok=True)

        # Part A: zeroMatches == True on single Revit target exits 0 with "0 matching tests"
        gated_a = []

        def run_zero(cmd, **kwargs):
            if cmd[0] == self.pool_exe:
                trx_path = cmd[cmd.index("--trx") + 1]
                self._write_trx(trx_path, passed_names=())
                payload = {
                    "success": True,
                    "exitCode": 0,
                    "version": 2026,
                    "trxPath": trx_path,
                    "workerIds": [],
                    "shardCount": 0,
                    "totalTests": 0,
                    "passedTests": 0,
                    "failedTests": 0,
                    "skippedTests": 0,
                    "zeroMatches": True,
                    "shimEnabled": False,
                    "error": None,
                }
                return subprocess.CompletedProcess(cmd, 0, stdout=json.dumps(payload), stderr="")
            return subprocess.CompletedProcess(cmd, 0)

        out_zero = io.StringIO()
        with mock.patch.dict(os.environ, {"SYNTHETIC_PROJECT_ROOT": self.root}, clear=False), \
             mock.patch("os.getcwd", return_value=self.root), \
             mock.patch.object(te.tempfile, "gettempdir", return_value=temp_build_root), \
             mock.patch.object(te, "configure_dotnet_environment", return_value=None), \
             mock.patch.object(te.guid_whitelister, "whitelist_existing_addins"), \
             mock.patch.object(te, "run_worker_journal_cleanliness_gate", side_effect=lambda v, w: gated_a.append(w)), \
             mock.patch.object(te.subprocess, "run", side_effect=run_zero), \
             contextlib.redirect_stdout(out_zero):
            with self.assertRaises(SystemExit) as ctx_zero:
                te.run_test_suite(target_class="NoSuchTest", target_version="2026")

        self.assertEqual(ctx_zero.exception.code, 0)
        self.assertIn("0 matching tests", out_zero.getvalue())
        self.assertEqual(gated_a, [])

        # Part B: fail-closed shim error (exitCode == 2) fails immediately with zero retry/downgrade
        pool_invocations = []
        cold_invocations = []

        def run_fail_closed(cmd, **kwargs):
            if cmd[0] == self.pool_exe:
                pool_invocations.append(list(cmd))
                trx_path = cmd[cmd.index("--trx") + 1]
                payload = {
                    "success": False,
                    "exitCode": 2,
                    "version": 2026,
                    "trxPath": trx_path,
                    "workerIds": [],
                    "shardCount": 1,
                    "totalTests": 0,
                    "passedTests": 0,
                    "failedTests": 0,
                    "skippedTests": 0,
                    "zeroMatches": False,
                    "shimEnabled": True,
                    "error": "CleanShimLaunchException: RevitCleanShim.dll signature invalid",
                }
                return subprocess.CompletedProcess(cmd, 2, stdout=json.dumps(payload), stderr="[shim] fatal\n")
            if os.path.basename(cmd[0]).lower().startswith("dotnet") and len(cmd) > 1 and cmd[1] == "test":
                cold_invocations.append(list(cmd))
            return subprocess.CompletedProcess(cmd, 0)

        out_fail = io.StringIO()
        with mock.patch.dict(os.environ, {"SYNTHETIC_PROJECT_ROOT": self.root}, clear=False), \
             mock.patch("os.getcwd", return_value=self.root), \
             mock.patch.object(te.tempfile, "gettempdir", return_value=temp_build_root), \
             mock.patch.object(te, "configure_dotnet_environment", return_value=None), \
             mock.patch.object(te.guid_whitelister, "whitelist_existing_addins"), \
             mock.patch.object(te, "execute_revit_test_with_startup_gate", side_effect=AssertionError("Must not downgrade to cold worker!")), \
             mock.patch.object(te.subprocess, "run", side_effect=run_fail_closed), \
             contextlib.redirect_stdout(out_fail):
            with self.assertRaises(SystemExit) as ctx_fail:
                te.run_test_suite(target_version="2026")

        self.assertEqual(ctx_fail.exception.code, 1)
        self.assertEqual(len(pool_invocations), 1)
        self.assertNotIn("--no-shim", pool_invocations[0])
        self.assertEqual(cold_invocations, [])
        self.assertIn("[ERROR]", out_fail.getvalue())
        self.assertIn("RevitCleanShim.dll signature invalid", out_fail.getvalue())

    def test_pool_mode_multi_shard_journal_gating_dirty_shard_and_missing_worker_ids(self):
        import contextlib
        from unittest import mock

        temp_build_root = os.path.join(self.root, "temp_root")
        os.makedirs(temp_build_root, exist_ok=True)

        def make_pool_runner(worker_ids, total_tests=3):
            def _run(cmd, **kwargs):
                if cmd[0] == self.pool_exe:
                    trx_path = cmd[cmd.index("--trx") + 1]
                    self._write_trx(trx_path, passed_names=[f"T{i}" for i in range(total_tests)])
                    payload = {
                        "success": True,
                        "exitCode": 0,
                        "version": 2026,
                        "trxPath": trx_path,
                        "workerIds": worker_ids,
                        "shardCount": len(worker_ids),
                        "totalTests": total_tests,
                        "passedTests": total_tests,
                        "failedTests": 0,
                        "skippedTests": 0,
                        "zeroMatches": False,
                        "shimEnabled": True,
                        "error": None,
                    }
                    return subprocess.CompletedProcess(cmd, 0, stdout=json.dumps(payload), stderr="")
                return subprocess.CompletedProcess(cmd, 0)
            return _run

        # 1. Multi-shard all-clean pass across ["w_1", "w_2", "w_3"]
        checked_clean = []

        def all_clean_gate(ver, wid, **kwargs):
            checked_clean.append(wid)
            return True, f"Worker {wid}: CLEAN"

        out_clean = io.StringIO()
        with mock.patch.dict(os.environ, {"SYNTHETIC_PROJECT_ROOT": self.root}, clear=False), \
             mock.patch("os.getcwd", return_value=self.root), \
             mock.patch.object(te.tempfile, "gettempdir", return_value=temp_build_root), \
             mock.patch.object(te, "configure_dotnet_environment", return_value=None), \
             mock.patch.object(te.guid_whitelister, "whitelist_existing_addins"), \
             mock.patch.object(te, "run_worker_journal_cleanliness_gate", side_effect=all_clean_gate), \
             mock.patch.object(te.subprocess, "run", side_effect=make_pool_runner(["w_1", "w_2", "w_3"])), \
             contextlib.redirect_stdout(out_clean):
            with self.assertRaises(SystemExit) as ctx_clean:
                te.run_test_suite(target_version="2026", shards=3)

        self.assertEqual(ctx_clean.exception.code, 0)
        self.assertEqual(checked_clean, ["w_1", "w_2", "w_3"])

        # 2. Multi-shard 1-dirty-shard failure across ["w_1", "w_2", "w_3"] (must still check all 3 and fail)
        checked_dirty = []

        def one_dirty_gate(ver, wid, **kwargs):
            checked_dirty.append(wid)
            if wid == "w_2":
                return False, "Worker w_2: Cleanliness Gate Result: DIRTY"
            return True, f"Worker {wid}: CLEAN"

        out_dirty = io.StringIO()
        with mock.patch.dict(os.environ, {"SYNTHETIC_PROJECT_ROOT": self.root}, clear=False), \
             mock.patch("os.getcwd", return_value=self.root), \
             mock.patch.object(te.tempfile, "gettempdir", return_value=temp_build_root), \
             mock.patch.object(te, "configure_dotnet_environment", return_value=None), \
             mock.patch.object(te.guid_whitelister, "whitelist_existing_addins"), \
             mock.patch.object(te, "run_worker_journal_cleanliness_gate", side_effect=one_dirty_gate), \
             mock.patch.object(te.subprocess, "run", side_effect=make_pool_runner(["w_1", "w_2", "w_3"])), \
             contextlib.redirect_stdout(out_dirty):
            with self.assertRaises(SystemExit) as ctx_dirty:
                te.run_test_suite(target_version="2026", shards=3)

        self.assertEqual(ctx_dirty.exception.code, 1)
        self.assertEqual(checked_dirty, ["w_1", "w_2", "w_3"])
        self.assertIn("DIRTY", out_dirty.getvalue())
        self.assertIn("w_2", out_dirty.getvalue())

        # 3. Missing workerIds when tests ran -> fails closed
        out_missing = io.StringIO()
        with mock.patch.dict(os.environ, {"SYNTHETIC_PROJECT_ROOT": self.root}, clear=False), \
             mock.patch("os.getcwd", return_value=self.root), \
             mock.patch.object(te.tempfile, "gettempdir", return_value=temp_build_root), \
             mock.patch.object(te, "configure_dotnet_environment", return_value=None), \
             mock.patch.object(te.guid_whitelister, "whitelist_existing_addins"), \
             mock.patch.object(te.subprocess, "run", side_effect=make_pool_runner([], total_tests=2)), \
             contextlib.redirect_stdout(out_missing):
            with self.assertRaises(SystemExit) as ctx_missing:
                te.run_test_suite(target_version="2026")

        self.assertEqual(ctx_missing.exception.code, 1)
        self.assertIn("[ERROR]", out_missing.getvalue())
        self.assertIn("workerIds", out_missing.getvalue())

    def test_pool_mode_no_shim_invokes_check_legacy_programdata_manifest(self):
        import contextlib
        from unittest import mock

        temp_build_root = os.path.join(self.root, "temp_root")
        os.makedirs(temp_build_root, exist_ok=True)
        pool_cmds = []
        legacy_checked = []

        def run_no_shim(cmd, **kwargs):
            if cmd[0] == self.pool_exe:
                pool_cmds.append(list(cmd))
                trx_path = cmd[cmd.index("--trx") + 1]
                self._write_trx(trx_path, passed_names=["Smoke"])
                payload = {
                    "success": True,
                    "exitCode": 0,
                    "version": 2026,
                    "trxPath": trx_path,
                    "workerIds": ["w_unshimmed_1"],
                    "shardCount": 1,
                    "totalTests": 1,
                    "passedTests": 1,
                    "failedTests": 0,
                    "skippedTests": 0,
                    "zeroMatches": False,
                    "shimEnabled": False,
                    "error": None,
                }
                return subprocess.CompletedProcess(cmd, 0, stdout=json.dumps(payload), stderr="")
            return subprocess.CompletedProcess(cmd, 0)

        out_buf = io.StringIO()
        with mock.patch.dict(os.environ, {"SYNTHETIC_PROJECT_ROOT": self.root}, clear=False), \
             mock.patch("os.getcwd", return_value=self.root), \
             mock.patch.object(te.tempfile, "gettempdir", return_value=temp_build_root), \
             mock.patch.object(te, "configure_dotnet_environment", return_value=None), \
             mock.patch.object(te.guid_whitelister, "whitelist_existing_addins"), \
             mock.patch.object(te, "check_legacy_programdata_manifest", side_effect=lambda v: legacy_checked.append(v)), \
             mock.patch.object(te, "run_worker_journal_cleanliness_gate", return_value=(True, "CLEAN")), \
             mock.patch.object(te.subprocess, "run", side_effect=run_no_shim), \
             contextlib.redirect_stdout(out_buf):
            with self.assertRaises(SystemExit) as exit_ctx:
                te.run_test_suite(target_version="2026", no_shim=True)

        self.assertEqual(exit_ctx.exception.code, 0)
        self.assertEqual(legacy_checked, ["2026"])
        self.assertIn("--no-shim", pool_cmds[0])

    def test_pool_mode_delegates_custom_category_without_regex_preskip(self):
        import contextlib
        from unittest import mock

        temp_build_root = os.path.join(self.root, "temp_root")
        os.makedirs(temp_build_root, exist_ok=True)
        pool_cmds = []

        def run_custom_cat(cmd, **kwargs):
            if cmd[0] == self.pool_exe:
                pool_cmds.append(list(cmd))
                trx_path = cmd[cmd.index("--trx") + 1]
                self._write_trx(trx_path, passed_names=())
                payload = {
                    "success": True,
                    "exitCode": 0,
                    "version": 2026,
                    "trxPath": trx_path,
                    "workerIds": [],
                    "shardCount": 0,
                    "totalTests": 0,
                    "passedTests": 0,
                    "failedTests": 0,
                    "skippedTests": 0,
                    "zeroMatches": True,
                    "shimEnabled": False,
                    "error": None,
                }
                return subprocess.CompletedProcess(cmd, 0, stdout=json.dumps(payload), stderr="")
            return subprocess.CompletedProcess(cmd, 0)

        out_buf = io.StringIO()
        with mock.patch.dict(os.environ, {"SYNTHETIC_PROJECT_ROOT": self.root}, clear=False), \
             mock.patch("os.getcwd", return_value=self.root), \
             mock.patch.object(te.tempfile, "gettempdir", return_value=temp_build_root), \
             mock.patch.object(te, "configure_dotnet_environment", return_value=None), \
             mock.patch.object(te, "build_test_filter", side_effect=AssertionError("Pool mode must not regex pre-skip categories!")), \
             mock.patch.object(te.guid_whitelister, "whitelist_existing_addins"), \
             mock.patch.object(te.subprocess, "run", side_effect=run_custom_cat), \
             contextlib.redirect_stdout(out_buf):
            with self.assertRaises(SystemExit) as exit_ctx:
                te.run_test_suite(target_version="2026", category="CustomMetadataOnlyCategory")

        self.assertEqual(exit_ctx.exception.code, 0)
        self.assertEqual(len(pool_cmds), 1)
        self.assertEqual(pool_cmds[0][pool_cmds[0].index("--category") + 1], "CustomMetadataOnlyCategory")
        self.assertIn("0 matching tests", out_buf.getvalue())

    def test_pool_mode_env_clean_shim_zero_triggers_programdata_warning(self):
        import contextlib
        from unittest import mock

        temp_build_root = os.path.join(self.root, "temp_root")
        os.makedirs(temp_build_root, exist_ok=True)
        pool_cmds = []
        legacy_checked = []

        def run_env_no_shim(cmd, **kwargs):
            if cmd[0] == self.pool_exe:
                pool_cmds.append(list(cmd))
                trx_path = cmd[cmd.index("--trx") + 1]
                self._write_trx(trx_path, passed_names=["Smoke"])
                payload = {
                    "success": True,
                    "exitCode": 0,
                    "version": 2026,
                    "trxPath": trx_path,
                    "workerIds": ["w_env_unshimmed"],
                    "shardCount": 1,
                    "totalTests": 1,
                    "passedTests": 1,
                    "failedTests": 0,
                    "skippedTests": 0,
                    "zeroMatches": False,
                    "shimEnabled": False,
                    "error": None,
                }
                return subprocess.CompletedProcess(cmd, 0, stdout=json.dumps(payload), stderr="")
            return subprocess.CompletedProcess(cmd, 0)

        out_buf = io.StringIO()
        with mock.patch.dict(os.environ, {"SYNTHETIC_PROJECT_ROOT": self.root, "SYNTHETIC_CLEAN_SHIM": "0"}, clear=False), \
             mock.patch("os.getcwd", return_value=self.root), \
             mock.patch.object(te.tempfile, "gettempdir", return_value=temp_build_root), \
             mock.patch.object(te, "configure_dotnet_environment", return_value=None), \
             mock.patch.object(te.guid_whitelister, "whitelist_existing_addins"), \
             mock.patch.object(te, "check_legacy_programdata_manifest", side_effect=lambda v: legacy_checked.append(v)), \
             mock.patch.object(te, "run_worker_journal_cleanliness_gate", return_value=(True, "CLEAN")), \
             mock.patch.object(te.subprocess, "run", side_effect=run_env_no_shim), \
             contextlib.redirect_stdout(out_buf):
            with self.assertRaises(SystemExit) as exit_ctx:
                te.run_test_suite(target_version="2026", no_shim=False)

        self.assertEqual(exit_ctx.exception.code, 0)
        self.assertEqual(legacy_checked, ["2026"])
        self.assertEqual(len(pool_cmds), 1)
        self.assertIn("[no-shim]", out_buf.getvalue())

    def test_pool_mode_fails_closed_when_shim_disabled_unexpectedly(self):
        import contextlib
        from unittest import mock

        temp_build_root = os.path.join(self.root, "temp_root")
        os.makedirs(temp_build_root, exist_ok=True)
        cold_calls = []

        def run_unexpected_unshimmed(cmd, **kwargs):
            if cmd[0] == self.pool_exe:
                trx_path = cmd[cmd.index("--trx") + 1]
                self._write_trx(trx_path, passed_names=["Smoke"])
                payload = {
                    "success": True,
                    "exitCode": 0,
                    "version": 2026,
                    "trxPath": trx_path,
                    "workerIds": ["w_unshimmed_leak"],
                    "shardCount": 1,
                    "totalTests": 1,
                    "passedTests": 1,
                    "failedTests": 0,
                    "skippedTests": 0,
                    "zeroMatches": False,
                    "shimEnabled": False,
                    "error": None,
                }
                return subprocess.CompletedProcess(cmd, 0, stdout=json.dumps(payload), stderr="")
            if os.path.basename(cmd[0]).lower().startswith("dotnet") and len(cmd) > 1 and cmd[1] == "test":
                cold_calls.append(list(cmd))
            return subprocess.CompletedProcess(cmd, 0)

        out_buf = io.StringIO()
        with mock.patch.dict(os.environ, {"SYNTHETIC_PROJECT_ROOT": self.root}, clear=False), \
             mock.patch("os.getcwd", return_value=self.root), \
             mock.patch.object(te.tempfile, "gettempdir", return_value=temp_build_root), \
             mock.patch.object(te, "configure_dotnet_environment", return_value=None), \
             mock.patch.object(te.guid_whitelister, "whitelist_existing_addins"), \
             mock.patch.object(te, "run_worker_journal_cleanliness_gate", return_value=(True, "CLEAN")), \
             mock.patch.object(te.subprocess, "run", side_effect=run_unexpected_unshimmed), \
             contextlib.redirect_stdout(out_buf):
            with self.assertRaises(SystemExit) as exit_ctx:
                te.run_test_suite(target_version="2026", no_shim=False)

        self.assertEqual(exit_ctx.exception.code, 1)
        self.assertEqual(cold_calls, [])
        self.assertIn("shimEnabled=false", out_buf.getvalue())

    def test_pool_mode_zero_matches_with_worker_ids_enforces_journal_gate_and_shim_check(self):
        import contextlib
        from unittest import mock

        temp_build_root = os.path.join(self.root, "temp_root")
        os.makedirs(temp_build_root, exist_ok=True)

        def make_zero_match_runner(worker_ids, shim_enabled=True):
            def _run(cmd, **kwargs):
                if cmd[0] == self.pool_exe:
                    trx_path = cmd[cmd.index("--trx") + 1]
                    self._write_trx(trx_path, passed_names=())
                    payload = {
                        "success": True,
                        "exitCode": 0,
                        "version": 2026,
                        "trxPath": trx_path,
                        "workerIds": worker_ids,
                        "shardCount": len(worker_ids),
                        "totalTests": 0,
                        "passedTests": 0,
                        "failedTests": 0,
                        "skippedTests": 0,
                        "zeroMatches": True,
                        "shimEnabled": shim_enabled,
                        "error": None,
                    }
                    return subprocess.CompletedProcess(cmd, 0, stdout=json.dumps(payload), stderr="")
                return subprocess.CompletedProcess(cmd, 0)

            return _run

        # 1. zeroMatches=True with non-empty workerIds + dirty journal -> fails (SystemExit(1)).
        out_dirty = io.StringIO()
        checked_dirty = []

        def dirty_gate(ver, wid, **kwargs):
            checked_dirty.append(wid)
            return False, f"Worker {wid}: Cleanliness Gate Result: DIRTY"

        with mock.patch.dict(os.environ, {"SYNTHETIC_PROJECT_ROOT": self.root}, clear=False), \
             mock.patch("os.getcwd", return_value=self.root), \
             mock.patch.object(te.tempfile, "gettempdir", return_value=temp_build_root), \
             mock.patch.object(te, "configure_dotnet_environment", return_value=None), \
             mock.patch.object(te.guid_whitelister, "whitelist_existing_addins"), \
             mock.patch.object(te, "run_worker_journal_cleanliness_gate", side_effect=dirty_gate), \
             mock.patch.object(te.subprocess, "run", side_effect=make_zero_match_runner(["w_zero_dirty"])), \
             contextlib.redirect_stdout(out_dirty):
            with self.assertRaises(SystemExit) as ctx_dirty:
                te.run_test_suite(target_version="2026")

        self.assertEqual(ctx_dirty.exception.code, 1)
        self.assertEqual(checked_dirty, ["w_zero_dirty"])
        self.assertIn("DIRTY", out_dirty.getvalue())

        # 2. zeroMatches=True with non-empty workerIds + shimEnabled=False when shim expected -> fails (SystemExit(1)).
        out_shim = io.StringIO()
        checked_shim = []

        def clean_gate_shim(ver, wid, **kwargs):
            checked_shim.append(wid)
            return True, f"Worker {wid}: CLEAN"

        with mock.patch.dict(os.environ, {"SYNTHETIC_PROJECT_ROOT": self.root}, clear=False), \
             mock.patch("os.getcwd", return_value=self.root), \
             mock.patch.object(te.tempfile, "gettempdir", return_value=temp_build_root), \
             mock.patch.object(te, "configure_dotnet_environment", return_value=None), \
             mock.patch.object(te.guid_whitelister, "whitelist_existing_addins"), \
             mock.patch.object(te, "run_worker_journal_cleanliness_gate", side_effect=clean_gate_shim), \
             mock.patch.object(te.subprocess, "run", side_effect=make_zero_match_runner(["w_zero_noshim"], shim_enabled=False)), \
             contextlib.redirect_stdout(out_shim):
            with self.assertRaises(SystemExit) as ctx_shim:
                te.run_test_suite(target_version="2026", no_shim=False)

        self.assertEqual(ctx_shim.exception.code, 1)
        self.assertEqual(checked_shim, ["w_zero_noshim"])
        self.assertIn("shimEnabled=false", out_shim.getvalue())

        # 3. zeroMatches=True with non-empty workerIds + clean journal + shimEnabled=True -> succeeds (SystemExit(0)).
        out_ok = io.StringIO()
        checked_ok = []

        def clean_gate_ok(ver, wid, **kwargs):
            checked_ok.append(wid)
            return True, f"Worker {wid}: CLEAN"

        with mock.patch.dict(os.environ, {"SYNTHETIC_PROJECT_ROOT": self.root}, clear=False), \
             mock.patch("os.getcwd", return_value=self.root), \
             mock.patch.object(te.tempfile, "gettempdir", return_value=temp_build_root), \
             mock.patch.object(te, "configure_dotnet_environment", return_value=None), \
             mock.patch.object(te.guid_whitelister, "whitelist_existing_addins"), \
             mock.patch.object(te, "run_worker_journal_cleanliness_gate", side_effect=clean_gate_ok), \
             mock.patch.object(te.subprocess, "run", side_effect=make_zero_match_runner(["w_zero_clean"], shim_enabled=True)), \
             contextlib.redirect_stdout(out_ok):
            with self.assertRaises(SystemExit) as ctx_ok:
                te.run_test_suite(target_version="2026", no_shim=False)

        self.assertEqual(ctx_ok.exception.code, 0)
        self.assertEqual(checked_ok, ["w_zero_clean"])
        self.assertIn("0 matching tests", out_ok.getvalue())


class TestMultiVersionParallelExecution(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory(ignore_cleanup_errors=True)
        self.root = self.tmp.name
        self.tests_dir = os.path.join(self.root, "tests")
        for rel_dir, proj_name, tfm in (
            ("Synthetic.LogicTests", "Synthetic.LogicTests", "net10.0-windows8.0"),
            ("Synthetic.LegacyMockTests", "Synthetic.LegacyMockTests", "net10.0-windows8.0"),
            ("Synthetic.RevitTests.2023", "Synthetic.RevitTests.2023", "net48"),
            ("Synthetic.RevitTests.2024", "Synthetic.RevitTests.2024", "net48"),
            ("Synthetic.RevitTests.2025", "Synthetic.RevitTests.2025", "net8.0-windows8.0"),
            ("Synthetic.RevitTests.2026", "Synthetic.RevitTests.2026", "net8.0-windows8.0"),
            ("Synthetic.RevitTests.2027", "Synthetic.RevitTests.2027", "net10.0-windows8.0"),
        ):
            pdir = os.path.join(self.tests_dir, "Projects", rel_dir)
            os.makedirs(pdir, exist_ok=True)
            with open(os.path.join(pdir, f"{proj_name}.csproj"), "w", encoding="utf-8") as f:
                f.write(
                    f'<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup>'
                    f"<TargetFramework>{tfm}</TargetFramework></PropertyGroup></Project>"
                )

        smoke_dir = os.path.join(self.tests_dir, "Platform", "Host", "Revit")
        os.makedirs(smoke_dir, exist_ok=True)
        with open(os.path.join(smoke_dir, "SmokeRevitTests.cs"), "w", encoding="utf-8") as f:
            f.write(
                """
using NUnit.Framework;
namespace SyntheticTests.Platform.Host
{
    [TestFixture]
    [RevitSmokeTest]
    public class SmokeRevitTests
    {
        [Test]
        public void Smoke() {}
    }
}
"""
            )

        pool_bin_dir = os.path.join(self.root, "tools", "bin", "RevitWorkerPool")
        os.makedirs(pool_bin_dir, exist_ok=True)
        self.pool_exe = os.path.join(pool_bin_dir, "RevitWorkerPool.exe")
        with open(self.pool_exe, "w", encoding="utf-8") as f:
            f.write("")

        self.temp_build_root = os.path.join(self.root, "temp_root")
        os.makedirs(self.temp_build_root, exist_ok=True)

    def tearDown(self):
        self.tmp.cleanup()

    @staticmethod
    def _write_trx(trx_path, passed_names=(), failed_tests=()):
        TestWorkerPoolRoutingAndShardedJournalGating._write_trx(
            trx_path, passed_names=passed_names, failed_tests=failed_tests
        )

    @staticmethod
    def _build_pool_payload(version, trx_path, worker_ids, passed=1, failed=0, exit_code=0, error=None):
        return {
            "success": exit_code == 0 and failed == 0,
            "exitCode": exit_code,
            "version": int(version),
            "trxPath": trx_path,
            "workerIds": worker_ids,
            "shardCount": len(worker_ids),
            "totalTests": passed + failed,
            "passedTests": passed,
            "failedTests": failed,
            "skippedTests": 0,
            "zeroMatches": (passed + failed) == 0 and exit_code == 0,
            "shimEnabled": True,
            "error": error,
        }

    def test_logic_and_legacymock_run_first_in_isolated_subfolders_and_fail_fast_before_revit(self):
        import contextlib
        from unittest import mock

        events = []

        def fake_subproc_run(cmd, **kwargs):
            if len(cmd) > 1 and cmd[1] == "build":
                proj = os.path.basename(cmd[2])
                iso_root = next(a.split("=", 1)[1] for a in cmd if a.startswith("-p:SyntheticIsolatedBuildRoot="))
                events.append(("build", proj, os.path.basename(iso_root)))
                return subprocess.CompletedProcess(cmd, 0, stdout="", stderr="")
            if len(cmd) > 1 and cmd[1] == "test":
                proj = os.path.basename(cmd[2])
                iso_root = next(a.split("=", 1)[1] for a in cmd if a.startswith("-p:SyntheticIsolatedBuildRoot="))
                events.append(("test", proj, os.path.basename(iso_root)))
                res_dir = cmd[cmd.index("--results-directory") + 1]
                trx_name = cmd[cmd.index("--logger") + 1].split("LogFileName=", 1)[1]
                if "LogicTests" in proj:
                    self._write_trx(
                        os.path.join(res_dir, trx_name),
                        failed_tests=[("FailingLogicTest", "Assert failed", "at LogicTest.cs:10")],
                    )
                    return subprocess.CompletedProcess(cmd, 1)
                self._write_trx(os.path.join(res_dir, trx_name), passed_names=["MockPass"])
                return subprocess.CompletedProcess(cmd, 0)
            if cmd[0] == self.pool_exe:
                raise AssertionError("RevitWorkerPool must NOT be called when headless LogicTests fail!")
            raise AssertionError(f"Unexpected command: {cmd}")

        out_buf = io.StringIO()
        with mock.patch.dict(os.environ, {"SYNTHETIC_PROJECT_ROOT": self.root}, clear=False), \
             mock.patch("os.getcwd", return_value=self.root), \
             mock.patch.object(te.tempfile, "gettempdir", return_value=self.temp_build_root), \
             mock.patch.object(te, "configure_dotnet_environment", return_value=None), \
             mock.patch.object(te, "ensure_test_template_ready", side_effect=AssertionError("Must not run Revit template preflight after Logic failure!")), \
             mock.patch.object(te.subprocess, "run", side_effect=fake_subproc_run), \
             contextlib.redirect_stdout(out_buf):
            with self.assertRaises(SystemExit) as exit_ctx:
                te.run_test_suite(target_version="2027,Logic,LegacyMock,2026")

        self.assertEqual(exit_ctx.exception.code, 1)
        out_text = out_buf.getvalue()
        self.assertIn("FailingLogicTest", out_text)
        self.assertIn("Preserving failed isolated build root", out_text)
        # Headless targets built/tested in <RunBuildRoot>\Logic and <RunBuildRoot>\LegacyMock; no Revit builds occurred
        self.assertTrue(all("RevitTests" not in ev[1] for ev in events), msg=events)
        self.assertIn(("build", "Synthetic.LogicTests.csproj", "Logic"), events)
        self.assertIn(("test", "Synthetic.LogicTests.csproj", "Logic"), events)

    def test_template_preflight_runs_sequentially_in_ascending_version_order_before_parallel_builds(self):
        import contextlib
        from unittest import mock

        timeline = []
        active_preflight = 0
        max_preflight = 0
        tl_lock = threading.Lock()

        def fake_preflight(ver, **kwargs):
            nonlocal active_preflight, max_preflight
            with tl_lock:
                active_preflight += 1
                max_preflight = max(max_preflight, active_preflight)
                timeline.append(("preflight_start", str(ver)))
            time.sleep(0.01)
            with tl_lock:
                active_preflight -= 1
                timeline.append(("preflight_end", str(ver)))
            return False

        def fake_subproc_run(cmd, **kwargs):
            if len(cmd) > 1 and cmd[1] == "build":
                proj = os.path.basename(cmd[2])
                with tl_lock:
                    timeline.append(("build", proj))
                return subprocess.CompletedProcess(cmd, 0, stdout="", stderr="")
            if cmd[0] == self.pool_exe:
                ver = cmd[cmd.index("--version") + 1]
                trx_path = cmd[cmd.index("--trx") + 1]
                self._write_trx(trx_path, passed_names=[f"Pass_{ver}"])
                payload = self._build_pool_payload(ver, trx_path, [f"w_{ver}"])
                return subprocess.CompletedProcess(cmd, 0, stdout=json.dumps(payload), stderr="")
            raise AssertionError(f"Unexpected command: {cmd}")

        out_buf = io.StringIO()
        with mock.patch.dict(os.environ, {"SYNTHETIC_PROJECT_ROOT": self.root}, clear=False), \
             mock.patch("os.getcwd", return_value=self.root), \
             mock.patch.object(te.tempfile, "gettempdir", return_value=self.temp_build_root), \
             mock.patch.object(te, "configure_dotnet_environment", return_value=None), \
             mock.patch.object(te, "ensure_test_template_ready", side_effect=fake_preflight), \
             mock.patch.object(te.guid_whitelister, "whitelist_existing_addins"), \
             mock.patch.object(te, "run_worker_journal_cleanliness_gate", return_value=(True, "CLEAN")), \
             mock.patch.object(te.subprocess, "run", side_effect=fake_subproc_run), \
             contextlib.redirect_stdout(out_buf):
            with self.assertRaises(SystemExit) as exit_ctx:
                te.run_test_suite(target_version="2027,2024,2026,2025")

        self.assertEqual(exit_ctx.exception.code, 0, msg=out_buf.getvalue())
        self.assertEqual(max_preflight, 1)
        preflight_starts = [ver for kind, ver in timeline if kind == "preflight_start"]
        self.assertEqual(preflight_starts, ["2024", "2025", "2026", "2027"])
        last_preflight_idx = max(i for i, (kind, _) in enumerate(timeline) if kind == "preflight_end")
        first_build_idx = min(i for i, (kind, _) in enumerate(timeline) if kind == "build")
        self.assertLess(last_preflight_idx, first_build_idx)

    def test_pool_parallel_builds_capped_at_2_and_dispatch_immediately_upon_build_completion(self):
        import contextlib
        from unittest import mock

        state_lock = threading.Lock()
        active_builds = 0
        max_active_builds = 0
        build_2025_started = threading.Event()
        release_2025_build = threading.Event()
        dispatched_before_2025_released = []
        build_roots = {}

        def fake_subproc_run(cmd, **kwargs):
            nonlocal active_builds, max_active_builds
            if len(cmd) > 1 and cmd[1] == "build":
                proj = os.path.basename(cmd[2])
                ver = proj.rsplit(".", 2)[1]
                iso_root = next(a.split("=", 1)[1] for a in cmd if a.startswith("-p:SyntheticIsolatedBuildRoot="))
                with state_lock:
                    build_roots[ver] = iso_root
                    active_builds += 1
                    max_active_builds = max(max_active_builds, active_builds)
                try:
                    if ver == "2025":
                        build_2025_started.set()
                        self.assertTrue(release_2025_build.wait(timeout=5.0), "2025 build timed out waiting for gate")
                    else:
                        time.sleep(0.02)
                    return subprocess.CompletedProcess(cmd, 0, stdout="", stderr="")
                finally:
                    with state_lock:
                        active_builds -= 1

            if cmd[0] == self.pool_exe:
                ver = cmd[cmd.index("--version") + 1]
                trx_path = cmd[cmd.index("--trx") + 1]
                with state_lock:
                    if not release_2025_build.is_set():
                        dispatched_before_2025_released.append(ver)
                if ver == "2024":
                    # Once 2024 dispatches while 2025 build is still blocked, release 2025 build
                    release_2025_build.set()
                self._write_trx(trx_path, passed_names=[f"Pass_{ver}"])
                payload = self._build_pool_payload(ver, trx_path, [f"w_{ver}"])
                return subprocess.CompletedProcess(cmd, 0, stdout=json.dumps(payload), stderr="")
            raise AssertionError(f"Unexpected command: {cmd}")

        # Safety timer so test never hangs if immediate pipelined dispatch fails
        safety = threading.Timer(4.0, release_2025_build.set)
        safety.start()
        out_buf = io.StringIO()
        try:
            with mock.patch.dict(os.environ, {"SYNTHETIC_PROJECT_ROOT": self.root}, clear=False), \
                 mock.patch("os.getcwd", return_value=self.root), \
                 mock.patch.object(te.tempfile, "gettempdir", return_value=self.temp_build_root), \
                 mock.patch.object(te, "configure_dotnet_environment", return_value=None), \
                 mock.patch.object(te, "ensure_test_template_ready", return_value=False), \
                 mock.patch.object(te.guid_whitelister, "whitelist_existing_addins"), \
                 mock.patch.object(te, "run_worker_journal_cleanliness_gate", return_value=(True, "CLEAN")), \
                 mock.patch.object(te.subprocess, "run", side_effect=fake_subproc_run), \
                 contextlib.redirect_stdout(out_buf):
                with self.assertRaises(SystemExit) as exit_ctx:
                    te.run_test_suite(target_version="2024,2025,2026,2027")
        finally:
            safety.cancel()

        self.assertEqual(exit_ctx.exception.code, 0, msg=out_buf.getvalue())
        self.assertLessEqual(max_active_builds, 2)
        self.assertEqual(max_active_builds, 2)
        self.assertTrue(dispatched_before_2025_released, "Other versions must dispatch immediately without waiting for slow 2025 build")
        self.assertEqual(len(set(build_roots.values())), 4)
        for ver, root_path in build_roots.items():
            self.assertEqual(os.path.basename(root_path), ver)

    def test_run_to_completion_combined_summary_reports_test_build_pool_and_journal_failures_and_preserves_build_root(self):
        import contextlib
        from unittest import mock

        ran_versions = []
        lock = threading.Lock()

        def fake_subproc_run(cmd, **kwargs):
            if len(cmd) > 1 and cmd[1] == "build":
                proj = os.path.basename(cmd[2])
                ver = proj.rsplit(".", 2)[1]
                iso_root = next(a.split("=", 1)[1] for a in cmd if a.startswith("-p:SyntheticIsolatedBuildRoot="))
                os.makedirs(iso_root, exist_ok=True)
                if ver == "2024":
                    return subprocess.CompletedProcess(cmd, 1, stdout="error CS0103: MissingSymbol\n", stderr="")
                return subprocess.CompletedProcess(cmd, 0, stdout="", stderr="")

            if cmd[0] == self.pool_exe:
                ver = cmd[cmd.index("--version") + 1]
                trx_path = cmd[cmd.index("--trx") + 1]
                with lock:
                    ran_versions.append(ver)
                if ver == "2025":
                    payload = self._build_pool_payload(
                        ver, trx_path, [], passed=0, failed=0, exit_code=2, error="CleanShimLaunchException: signature invalid"
                    )
                    return subprocess.CompletedProcess(cmd, 2, stdout=json.dumps(payload), stderr="[shim] fatal\n")
                if ver == "2026":
                    self._write_trx(
                        trx_path,
                        passed_names=["Ok2026"],
                        failed_tests=[("Broken2026", "Expected true", "at Foo.cs:42")],
                    )
                    payload = self._build_pool_payload(ver, trx_path, ["w_2026"], passed=1, failed=1, exit_code=1)
                    return subprocess.CompletedProcess(cmd, 1, stdout=json.dumps(payload), stderr="")
                if ver == "2027":
                    self._write_trx(trx_path, passed_names=["Ok2027"])
                    payload = self._build_pool_payload(ver, trx_path, ["w_2027_dirty"], passed=1, failed=0, exit_code=0)
                    return subprocess.CompletedProcess(cmd, 0, stdout=json.dumps(payload), stderr="")
            raise AssertionError(f"Unexpected command: {cmd}")

        def fake_gate(ver, wid, **kwargs):
            if str(ver) == "2027":
                return False, f"Worker {wid}: Cleanliness Gate Result: DIRTY (orphaned transaction)"
            return True, f"Worker {wid}: CLEAN"

        out_buf = io.StringIO()
        with mock.patch.dict(os.environ, {"SYNTHETIC_PROJECT_ROOT": self.root}, clear=False), \
             mock.patch("os.getcwd", return_value=self.root), \
             mock.patch.object(te.tempfile, "gettempdir", return_value=self.temp_build_root), \
             mock.patch.object(te, "configure_dotnet_environment", return_value=None), \
             mock.patch.object(te, "ensure_test_template_ready", return_value=False), \
             mock.patch.object(te.guid_whitelister, "whitelist_existing_addins"), \
             mock.patch.object(te, "run_worker_journal_cleanliness_gate", side_effect=fake_gate), \
             mock.patch.object(te.subprocess, "run", side_effect=fake_subproc_run), \
             contextlib.redirect_stdout(out_buf):
            with self.assertRaises(SystemExit) as exit_ctx:
                te.run_test_suite(target_version="2024,2025,2026,2027", fail_fast=False)

        self.assertEqual(exit_ctx.exception.code, 1)
        out_text = out_buf.getvalue()
        # All remaining versions ran to completion despite 2024 build failure and 2025 pool exitCode==2
        self.assertEqual(set(ran_versions), {"2025", "2026", "2027"})
        # Thread-safe completion lines for every version
        self.assertRegex(out_text, r"\[TESTS\] Revit 2024: FAIL \(0 passed, 0 failed, clean journal, [0-9.]+s\)")
        self.assertRegex(out_text, r"\[TESTS\] Revit 2025: FAIL \(0 passed, 0 failed, clean journal, [0-9.]+s\)")
        self.assertRegex(out_text, r"\[TESTS\] Revit 2026: FAIL \(1 passed, 1 failed, clean journal, [0-9.]+s\)")
        self.assertRegex(out_text, r"\[TESTS\] Revit 2027: FAIL \(1 passed, 0 failed, dirty journal, [0-9.]+s\)")
        # Combined Markdown summary covers test, build, pool (exitCode == 2), and journal failures
        self.assertIn("# Test Execution Summary", out_text)
        self.assertIn("Broken2026", out_text)
        self.assertIn("MissingSymbol", out_text)
        self.assertIn("CleanShimLaunchException", out_text)
        self.assertIn("DIRTY", out_text)
        # Failed build root preserved on disk
        builds_parent = os.path.join(self.temp_build_root, "SyntheticBuilds")
        preserved = os.listdir(builds_parent)
        self.assertEqual(len(preserved), 1)
        self.assertIn(os.path.join(builds_parent, preserved[0]), out_text)

    def test_fail_fast_cancels_only_unstarted_targets_while_inflight_workers_finish_and_gate_clean(self):
        import contextlib
        from unittest import mock

        pool_2025_entered = threading.Event()
        release_2025_pool = threading.Event()
        build_2026_entered = threading.Event()
        build_2023_entered = threading.Event()
        release_inflight_builds = threading.Event()
        gated = []
        started_builds = []
        started_pools = []
        lock = threading.Lock()

        class _FailWatchStdout(io.StringIO):
            def write(self, s):
                n = super().write(s)
                if "[TESTS] Revit 2024: FAIL" in s:
                    release_inflight_builds.set()
                    release_2025_pool.set()
                return n

        def fake_subproc_run(cmd, **kwargs):
            if len(cmd) > 1 and cmd[1] == "build":
                proj = os.path.basename(cmd[2])
                ver = proj.rsplit(".", 2)[1]
                with lock:
                    started_builds.append(ver)
                if ver == "2027":
                    raise AssertionError("2027 unstarted build should have been cancelled under --fail-fast!")
                if ver == "2026":
                    build_2026_entered.set()
                    self.assertTrue(release_inflight_builds.wait(timeout=5.0))
                elif ver == "2023":
                    build_2023_entered.set()
                    self.assertTrue(release_inflight_builds.wait(timeout=5.0))
                return subprocess.CompletedProcess(cmd, 0, stdout="", stderr="")

            if cmd[0] == self.pool_exe:
                ver = cmd[cmd.index("--version") + 1]
                trx_path = cmd[cmd.index("--trx") + 1]
                with lock:
                    started_pools.append(ver)
                if ver == "2025":
                    pool_2025_entered.set()
                    self.assertTrue(release_2025_pool.wait(timeout=5.0))
                    self._write_trx(trx_path, passed_names=["Pass2025"])
                    payload = self._build_pool_payload(ver, trx_path, ["w_2025_inflight"], passed=1, failed=0, exit_code=0)
                    return subprocess.CompletedProcess(cmd, 0, stdout=json.dumps(payload), stderr="")
                if ver == "2024":
                    self.assertTrue(pool_2025_entered.wait(timeout=5.0))
                    self.assertTrue(build_2026_entered.wait(timeout=5.0))
                    self.assertTrue(build_2023_entered.wait(timeout=5.0))
                    payload = self._build_pool_payload(ver, trx_path, ["w_2024"], passed=0, failed=1, exit_code=1)
                    self._write_trx(trx_path, failed_tests=[("Fail2024", "boom", "trace")])
                    return subprocess.CompletedProcess(cmd, 1, stdout=json.dumps(payload), stderr="")
                raise AssertionError(f"Unstarted pool dispatch for {ver} must be cancelled!")
            raise AssertionError(f"Unexpected command: {cmd}")

        def fake_gate(ver, wid, **kwargs):
            with lock:
                gated.append((str(ver), wid))
            return True, f"Worker {wid}: CLEAN"

        # 5 targets (2024, 2025, 2026, 2023, 2027): 2024 & 2025 build fast and enter pool; 2026 & 2023 occupy
        # both build slots so 2027 cannot start building. 2024 fails while 2025 is in-flight in pool and
        # 2026/2023 are building -> 2027 build is cancelled, 2026/2023 pool dispatches are cancelled, and
        # in-flight 2025 finishes and passes --assert-clean!
        safety = threading.Timer(5.0, lambda: (release_inflight_builds.set(), release_2025_pool.set()))
        safety.start()
        out_buf = _FailWatchStdout()
        try:
            with mock.patch.dict(os.environ, {"SYNTHETIC_PROJECT_ROOT": self.root}, clear=False), \
                 mock.patch("os.getcwd", return_value=self.root), \
                 mock.patch.object(te.tempfile, "gettempdir", return_value=self.temp_build_root), \
                 mock.patch.object(te, "configure_dotnet_environment", return_value=None), \
                 mock.patch.object(te, "ensure_test_template_ready", return_value=False), \
                 mock.patch.object(te.guid_whitelister, "whitelist_existing_addins"), \
                 mock.patch.object(te, "run_worker_journal_cleanliness_gate", side_effect=fake_gate), \
                 mock.patch.object(te.subprocess, "run", side_effect=fake_subproc_run), \
                 contextlib.redirect_stdout(out_buf):
                with self.assertRaises(SystemExit) as exit_ctx:
                    te.run_test_suite(target_version="2024,2025,2026,2023,2027", fail_fast=True)
        finally:
            safety.cancel()

        self.assertEqual(exit_ctx.exception.code, 1, msg=out_buf.getvalue())
        self.assertIn(("2025", "w_2025_inflight"), gated)
        self.assertIn(("2024", "w_2024"), gated)
        self.assertNotIn("2026", started_pools)
        self.assertNotIn("2023", started_pools)
        self.assertNotIn("2027", started_pools)
        self.assertNotIn("2027", started_builds)

    def test_cold_no_pool_remains_sequential_across_revit_versions(self):
        import contextlib
        from unittest import mock

        events = []
        active_cold = 0
        max_cold = 0

        def fake_subproc_run(cmd, **kwargs):
            if len(cmd) > 1 and cmd[1] == "build":
                proj = os.path.basename(cmd[2])
                ver = proj.rsplit(".", 2)[1]
                iso_root = next(a.split("=", 1)[1] for a in cmd if a.startswith("-p:SyntheticIsolatedBuildRoot="))
                self.assertEqual(os.path.basename(iso_root), ver)
                events.append(("build", ver))
                return subprocess.CompletedProcess(cmd, 0, stdout="", stderr="")
            if cmd[0] == self.pool_exe:
                raise AssertionError("--no-pool must not invoke RevitWorkerPool!")
            raise AssertionError(f"Unexpected command: {cmd}")

        def fake_cold_gate_exec(cmd, out_f, worker_env, version, run_id, snapshot_path, timeout):
            nonlocal active_cold, max_cold
            active_cold += 1
            max_cold = max(max_cold, active_cold)
            events.append(("cold_start", version))
            time.sleep(0.01)
            trx_name = cmd[cmd.index("--logger") + 1].split("LogFileName=", 1)[1]
            results_dir = cmd[cmd.index("--results-directory") + 1]
            self._write_trx(os.path.join(results_dir, trx_name), passed_names=[f"Cold_{version}"])
            active_cold -= 1
            events.append(("cold_end", version))
            return 0, False, run_id, False

        class FakeLease:
            def __init__(self, *args, **kwargs):
                pass

            def __enter__(self):
                return self

            def __exit__(self, *args):
                pass

        out_buf = io.StringIO()
        with mock.patch.dict(os.environ, {"SYNTHETIC_PROJECT_ROOT": self.root}, clear=False), \
             mock.patch("os.getcwd", return_value=self.root), \
             mock.patch.object(te.tempfile, "gettempdir", return_value=self.temp_build_root), \
             mock.patch.object(te, "configure_dotnet_environment", return_value=None), \
             mock.patch.object(te, "ensure_test_template_ready", return_value=False), \
             mock.patch.object(te.guid_whitelister, "whitelist_existing_addins"), \
             mock.patch.object(te, "check_legacy_programdata_manifest"), \
             mock.patch.object(te, "snapshot_known_journals"), \
             mock.patch.object(te, "RicaunBundleLease", FakeLease), \
             mock.patch.object(te, "execute_revit_test_with_startup_gate", side_effect=fake_cold_gate_exec), \
             mock.patch.object(te, "run_journal_cleanliness_gate", return_value=(True, "CLEAN")), \
             mock.patch.object(te.subprocess, "run", side_effect=fake_subproc_run), \
             contextlib.redirect_stdout(out_buf):
            with self.assertRaises(SystemExit) as exit_ctx:
                te.run_test_suite(target_version="2024,2026,2027", no_pool=True)

        self.assertEqual(exit_ctx.exception.code, 0, msg=out_buf.getvalue())
        self.assertEqual(max_cold, 1)
        self.assertEqual(
            events,
            [
                ("build", "2024"),
                ("cold_start", "2024"),
                ("cold_end", "2024"),
                ("build", "2026"),
                ("cold_start", "2026"),
                ("cold_end", "2026"),
                ("build", "2027"),
                ("cold_start", "2027"),
                ("cold_end", "2027"),
            ],
        )

    def test_real_concurrent_two_process_dotnet_build_2026_2027_and_legacymock_2027(self):
        import concurrent.futures

        # Verify real concurrent 2-process dotnet build isolation against the actual repo projects:
        # 1) 2026 + 2027 (shares Synthetic.Core)
        # 2) LegacyMock + 2027 (shares Synthetic.Core and Synthetic2027)
        sdk_info = te.configure_dotnet_environment(env=os.environ.copy())
        dotnet_exe = te.resolve_dotnet_exe(sdk_info)
        build_env = os.environ.copy()
        te.configure_dotnet_environment(env=build_env)

        real_projects = te.discover_test_projects(os.path.join(REPO_ROOT, "tests"))
        for pair in (("2026", "2027"), ("LegacyMock", "2027")):
            with self.subTest(pair=pair):
                run_id = te.generate_run_id()
                run_root = os.path.join(self.temp_build_root, f"real_concurrent_{'_'.join(pair)}_{run_id}")
                os.makedirs(run_root, exist_ok=True)
                barrier = threading.Barrier(2)

                def _real_build(target_key):
                    csproj = real_projects[target_key][0]
                    target_root = os.path.join(run_root, target_key)
                    os.makedirs(target_root, exist_ok=True)
                    barrier.wait(timeout=15.0)
                    cmd = [
                        dotnet_exe,
                        "build",
                        csproj,
                        "-c",
                        "Debug",
                        "-p:Platform=x64",
                        f"-p:SyntheticIsolatedBuildRoot={target_root}",
                        f"-p:SyntheticRunId={run_id}",
                        "-v:minimal",
                        "-clp:ErrorsOnly;Summary",
                    ]
                    res = subprocess.run(cmd, cwd=REPO_ROOT, env=build_env, capture_output=True, text=True)
                    return target_key, csproj, target_root, res

                with concurrent.futures.ThreadPoolExecutor(max_workers=2) as ex:
                    results = list(ex.map(_real_build, pair))

                for target_key, csproj, target_root, res in results:
                    self.assertEqual(
                        res.returncode,
                        0,
                        msg=f"Concurrent build failed for {target_key}:\nSTDOUT:\n{res.stdout}\nSTDERR:\n{res.stderr}",
                    )
                    proj_name = os.path.splitext(os.path.basename(csproj))[0]
                    tfms, is_multi = te.get_project_target_frameworks(csproj)
                    dll_path = te.resolve_isolated_test_assembly_path(
                        target_root, proj_name, run_id, tfm=tfms[0], is_multi_tfm=is_multi
                    )
                    self.assertTrue(os.path.isfile(dll_path), msg=f"Missing isolated output DLL: {dll_path}")



class TestPreRunTestTemplateTrigger(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.root = self.tmp.name
        self.tests_dir = os.path.join(self.root, "tests")
        self.models_dir = os.path.join(self.tests_dir, "Assets", "test_models")
        os.makedirs(self.models_dir, exist_ok=True)

        # Create Logic, 2026, and 2027 test projects
        for rel_dir, proj_name, tfm in (
            ("Synthetic.LogicTests", "Synthetic.LogicTests", "net10.0-windows8.0"),
            ("Synthetic.RevitTests.2026", "Synthetic.RevitTests.2026", "net8.0-windows8.0"),
            ("Synthetic.RevitTests.2027", "Synthetic.RevitTests.2027", "net10.0-windows8.0"),
        ):
            pdir = os.path.join(self.tests_dir, "Projects", rel_dir)
            os.makedirs(pdir, exist_ok=True)
            with open(os.path.join(pdir, f"{proj_name}.csproj"), "w", encoding="utf-8") as f:
                f.write(
                    f'<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup>'
                    f"<TargetFramework>{tfm}</TargetFramework></PropertyGroup></Project>"
                )

        smoke_dir = os.path.join(self.tests_dir, "Platform", "Host", "Revit")
        os.makedirs(smoke_dir, exist_ok=True)
        with open(os.path.join(smoke_dir, "SmokeRevitTests.cs"), "w", encoding="utf-8") as f:
            f.write(
                """
using NUnit.Framework;
namespace SyntheticTests.Platform.Host
{
    [TestFixture]
    [RevitSmokeTest]
    public class SmokeRevitTests
    {
        [Test]
        public void Smoke() {}
    }
}
"""
            )

        pool_bin_dir = os.path.join(self.root, "tools", "bin", "RevitWorkerPool")
        os.makedirs(pool_bin_dir, exist_ok=True)
        self.pool_exe = os.path.join(pool_bin_dir, "RevitWorkerPool.exe")
        with open(self.pool_exe, "w", encoding="utf-8") as f:
            f.write("")

    def tearDown(self):
        self.tmp.cleanup()

    @staticmethod
    def _write_fake_model(path, format_year):
        os.makedirs(os.path.dirname(os.path.abspath(path)), exist_ok=True)
        header = b"\xd0\xcf\x11\xe0\xa1\xb1\x1a\xe1"
        info_block = f"Worksharing: Not enabled\r\nFormat: {format_year}\r\n".encode("utf-16-le")
        with open(path, "wb") as f:
            f.write(header + b"\x00" * 32 + info_block)
        return path

    @staticmethod
    def _write_trx(trx_path, passed_names=("Smoke",)):
        TestWorkerPoolRoutingAndShardedJournalGating._write_trx(
            trx_path, passed_names=passed_names
        )

    def test_cli_parses_no_auto_upgrade_and_help_documents_it(self):
        parsed_default = te.parse_cli_args([])
        self.assertFalse(parsed_default.no_auto_upgrade)

        parsed_flag = te.parse_cli_args(["--version", "2027", "--no-auto-upgrade"])
        self.assertEqual(parsed_flag.target_version, "2027")
        self.assertTrue(parsed_flag.no_auto_upgrade)

        help_res = subprocess.run(
            [sys.executable, SCRIPT_PATH, "--help"],
            cwd=REPO_ROOT,
            capture_output=True,
            text=True,
        )
        self.assertEqual(help_res.returncode, 0, msg=help_res.stderr)
        self.assertIn("--no-auto-upgrade", help_res.stdout)

    def test_existing_test_template_takes_noop_fast_path_without_lock_or_upgrader(self):
        import contextlib
        from unittest import mock

        self._write_fake_model(os.path.join(self.models_dir, "TestTemplate2026.rvt"), 2026)

        def fail_if_locked(*args, **kwargs):
            raise AssertionError("Lock should not be acquired when TestTemplate exists.")

        def fail_if_upgraded(*args, **kwargs):
            raise AssertionError("RevitModelUpgrader should not run when TestTemplate exists.")

        t0 = time.perf_counter()
        upgraded = te.ensure_test_template_ready(
            "2026",
            workspace_path=self.root,
            no_auto_upgrade=False,
            lock_factory=fail_if_locked,
            upgrader_runner=fail_if_upgraded,
        )
        elapsed_ms = (time.perf_counter() - t0) * 1000.0
        self.assertFalse(upgraded)
        self.assertLess(elapsed_ms, 50.0)

        # Also a no-op with --no-auto-upgrade when template exists
        self.assertFalse(
            te.ensure_test_template_ready(
                "2026",
                workspace_path=self.root,
                no_auto_upgrade=True,
                lock_factory=fail_if_locked,
                upgrader_runner=fail_if_upgraded,
            )
        )

        # End-to-end run_test_suite dispatches directly without invoking upgrader
        temp_build_root = os.path.join(self.root, "temp_root")
        os.makedirs(temp_build_root, exist_ok=True)
        seen_cmds = []

        def fake_subproc_run(cmd, **kwargs):
            seen_cmds.append(list(cmd))
            if cmd[0] == self.pool_exe:
                trx_path = cmd[cmd.index("--trx") + 1]
                self._write_trx(trx_path, passed_names=["Smoke"])
                payload = {
                    "success": True,
                    "exitCode": 0,
                    "version": 2026,
                    "trxPath": trx_path,
                    "workerIds": ["w_fast_2026"],
                    "shardCount": 1,
                    "totalTests": 1,
                    "passedTests": 1,
                    "failedTests": 0,
                    "skippedTests": 0,
                    "zeroMatches": False,
                    "shimEnabled": True,
                    "error": None,
                }
                return subprocess.CompletedProcess(cmd, 0, stdout=json.dumps(payload), stderr="")
            return subprocess.CompletedProcess(cmd, 0)

        out_buf = io.StringIO()
        with mock.patch.dict(os.environ, {"SYNTHETIC_PROJECT_ROOT": self.root}, clear=False), \
             mock.patch("os.getcwd", return_value=self.root), \
             mock.patch.object(te.tempfile, "gettempdir", return_value=temp_build_root), \
             mock.patch.object(te, "configure_dotnet_environment", return_value=None), \
             mock.patch.object(te.guid_whitelister, "whitelist_existing_addins"), \
             mock.patch.object(te, "run_worker_journal_cleanliness_gate", return_value=(True, "CLEAN")), \
             mock.patch.object(te.subprocess, "run", side_effect=fake_subproc_run), \
             contextlib.redirect_stdout(out_buf):
            with self.assertRaises(SystemExit) as ctx:
                te.run_test_suite(target_version="2026")

        self.assertEqual(ctx.exception.code, 0)
        self.assertFalse(any("revit_model_upgrader.py" in " ".join(c) for c in seen_cmds))
        self.assertTrue(any(c[0] == self.pool_exe for c in seen_cmds))

    def test_missing_template_acquires_lock_logs_loudly_stages_atomically_and_never_upgrades_inc_standards(self):
        import contextlib
        from unittest import mock

        src_2025 = self._write_fake_model(os.path.join(self.models_dir, "TestTemplate2025.rvt"), 2025)
        src_2026 = self._write_fake_model(os.path.join(self.models_dir, "TestTemplate2026.rvt"), 2026)
        self._write_fake_model(
            os.path.join(self.models_dir, "INC Standards - Assemblies & Details2026.rvt"),
            2026,
        )
        target_2027 = os.path.join(self.models_dir, "TestTemplate2027.rvt")
        inc_2027 = os.path.join(self.models_dir, "INC Standards - Assemblies & Details2027.rvt")
        self.assertFalse(os.path.exists(target_2027))

        temp_build_root = os.path.join(self.root, "temp_root")
        os.makedirs(temp_build_root, exist_ok=True)
        events = []

        def fake_subproc_run(cmd, **kwargs):
            if any("revit_model_upgrader.py" in str(part) for part in cmd):
                events.append(("upgrader", list(cmd)))
                self.assertNotIn("--preset", cmd)
                self.assertIn(os.path.abspath(src_2026), [os.path.abspath(p) for p in cmd])
                self.assertNotIn(os.path.abspath(src_2025), [os.path.abspath(p) for p in cmd])
                self.assertEqual(cmd[cmd.index("--versions") + 1], "2027")
                staging_dir = cmd[cmd.index("--output-dir") + 1]
                report_path = cmd[cmd.index("--report") + 1]
                # Target must not exist yet before atomic promotion
                self.assertFalse(os.path.exists(target_2027))
                staged_file = os.path.join(staging_dir, "TestTemplate2027.rvt")
                self._write_fake_model(staged_file, 2027)
                with open(report_path, "w", encoding="utf-8") as rf:
                    json.dump(
                        {
                            "DryRun": False,
                            "Batches": [
                                {
                                    "TargetVersion": "2027",
                                    "JournalClean": True,
                                    "Jobs": [
                                        {
                                            "SourcePath": src_2026,
                                            "TargetPath": staged_file,
                                            "Success": True,
                                            "VerifiedFormat": "2027",
                                            "LoadableFamilyCount": 3,
                                        }
                                    ],
                                }
                            ],
                        },
                        rf,
                    )
                return subprocess.CompletedProcess(cmd, 0, stdout="", stderr="")
            if cmd[0] == self.pool_exe:
                # Template must already exist and be verified before worker dispatch
                self.assertTrue(os.path.isfile(target_2027))
                events.append(("pool", list(cmd)))
                trx_path = cmd[cmd.index("--trx") + 1]
                self._write_trx(trx_path, passed_names=["Smoke2027"])
                payload = {
                    "success": True,
                    "exitCode": 0,
                    "version": 2027,
                    "trxPath": trx_path,
                    "workerIds": ["w_2027_1"],
                    "shardCount": 1,
                    "totalTests": 1,
                    "passedTests": 1,
                    "failedTests": 0,
                    "skippedTests": 0,
                    "zeroMatches": False,
                    "shimEnabled": True,
                    "error": None,
                }
                return subprocess.CompletedProcess(cmd, 0, stdout=json.dumps(payload), stderr="")
            events.append(("build", list(cmd)))
            return subprocess.CompletedProcess(cmd, 0)

        out_buf = io.StringIO()
        with mock.patch.dict(os.environ, {"SYNTHETIC_PROJECT_ROOT": self.root}, clear=False), \
             mock.patch("os.getcwd", return_value=self.root), \
             mock.patch.object(te.tempfile, "gettempdir", return_value=temp_build_root), \
             mock.patch.object(te, "configure_dotnet_environment", return_value=None), \
             mock.patch.object(te.guid_whitelister, "whitelist_existing_addins"), \
             mock.patch.object(te, "run_worker_journal_cleanliness_gate", return_value=(True, "CLEAN")), \
             mock.patch.object(te.subprocess, "run", side_effect=fake_subproc_run), \
             contextlib.redirect_stdout(out_buf):
            with self.assertRaises(SystemExit) as ctx:
                te.run_test_suite(target_version="2027")

        self.assertEqual(ctx.exception.code, 0)
        self.assertTrue(os.path.isfile(target_2027))
        self.assertEqual(te.inspect_model_format_year(target_2027), 2027)
        self.assertFalse(os.path.exists(inc_2027))
        self.assertEqual(
            [e for e in os.listdir(self.models_dir) if ".staging_" in e],
            [],
        )
        event_kinds = [e[0] for e in events]
        self.assertEqual(event_kinds, ["upgrader", "build", "pool"])
        stdout_text = out_buf.getvalue()
        self.assertIn("[PRE-RUN UPGRADE]", stdout_text)
        self.assertIn("TestTemplate2027.rvt", stdout_text)
        self.assertIn("RevitModelUpgrader", stdout_text)

    def test_missing_template_double_checked_locking_and_lock_release_on_failure(self):
        import contextlib

        self._write_fake_model(os.path.join(self.models_dir, "TestTemplate2026.rvt"), 2026)
        target_2027 = os.path.join(self.models_dir, "TestTemplate2027.rvt")
        lock_events = []

        class RaceSimLock:
            def __init__(self, create_on_enter=False):
                self.create_on_enter = create_on_enter

            def __enter__(self):
                lock_events.append("enter")
                if self.create_on_enter:
                    TestPreRunTestTemplateTrigger._write_fake_model(target_2027, 2027)
                return self

            def __exit__(self, exc_type, exc_val, exc_tb):
                lock_events.append("exit")

        def fail_if_called(cmd):
            raise AssertionError("Upgrader must not run if peer created template while waiting on lock.")

        out_race = io.StringIO()
        with contextlib.redirect_stdout(out_race):
            upgraded = te.ensure_test_template_ready(
                "2027",
                workspace_path=self.root,
                lock_factory=lambda *_: RaceSimLock(create_on_enter=True),
                upgrader_runner=fail_if_called,
            )
        self.assertFalse(upgraded)
        self.assertEqual(lock_events, ["enter", "exit"])

        # Lock must still release if upgrader fails
        os.remove(target_2027)
        lock_events.clear()

        def failing_upgrader(cmd):
            return subprocess.CompletedProcess(cmd, 1, stdout="", stderr="boom")

        with contextlib.redirect_stdout(io.StringIO()):
            with self.assertRaises(RuntimeError):
                te.ensure_test_template_ready(
                    "2027",
                    workspace_path=self.root,
                    lock_factory=lambda *_: RaceSimLock(create_on_enter=False),
                    upgrader_runner=failing_upgrader,
                )
        self.assertEqual(lock_events, ["enter", "exit"])
        self.assertFalse(os.path.exists(target_2027))

    def test_missing_template_upgrade_verification_failures_fail_cleanly_before_worker_dispatch(self):
        import contextlib
        from unittest import mock

        src_2026 = self._write_fake_model(os.path.join(self.models_dir, "TestTemplate2026.rvt"), 2026)
        target_2027 = os.path.join(self.models_dir, "TestTemplate2027.rvt")
        temp_build_root = os.path.join(self.root, "temp_root")
        os.makedirs(temp_build_root, exist_ok=True)

        failure_modes = ("nonzero_exit", "missing_staged", "wrong_format", "dirty_report")
        for mode in failure_modes:
            with self.subTest(mode=mode):
                if os.path.exists(target_2027):
                    os.remove(target_2027)
                worker_dispatched = []

                def run_with_mode(cmd, **kwargs):
                    if any("revit_model_upgrader.py" in str(part) for part in cmd):
                        staging_dir = cmd[cmd.index("--output-dir") + 1]
                        report_path = cmd[cmd.index("--report") + 1]
                        staged_file = os.path.join(staging_dir, "TestTemplate2027.rvt")
                        if mode == "nonzero_exit":
                            return subprocess.CompletedProcess(cmd, 1, stdout="", stderr="upgrader failed")
                        if mode == "wrong_format":
                            self._write_fake_model(staged_file, 2026)
                        elif mode == "dirty_report":
                            self._write_fake_model(staged_file, 2027)
                            with open(report_path, "w", encoding="utf-8") as rf:
                                json.dump(
                                    {
                                        "DryRun": False,
                                        "Batches": [
                                            {
                                                "TargetVersion": "2027",
                                                "JournalClean": False,
                                                "Jobs": [
                                                    {
                                                        "SourcePath": src_2026,
                                                        "TargetPath": staged_file,
                                                        "Success": True,
                                                    }
                                                ],
                                            }
                                        ],
                                    },
                                    rf,
                                )
                        return subprocess.CompletedProcess(cmd, 0, stdout="", stderr="")
                    worker_dispatched.append(list(cmd))
                    return subprocess.CompletedProcess(cmd, 0)

                out_buf = io.StringIO()
                with mock.patch.dict(os.environ, {"SYNTHETIC_PROJECT_ROOT": self.root}, clear=False), \
                     mock.patch("os.getcwd", return_value=self.root), \
                     mock.patch.object(te.tempfile, "gettempdir", return_value=temp_build_root), \
                     mock.patch.object(te, "configure_dotnet_environment", return_value=None), \
                     mock.patch.object(te.subprocess, "run", side_effect=run_with_mode), \
                     contextlib.redirect_stdout(out_buf):
                    with self.assertRaises(SystemExit) as ctx:
                        te.run_test_suite(target_version="2027")

                self.assertEqual(ctx.exception.code, 1)
                self.assertEqual(worker_dispatched, [])
                self.assertFalse(os.path.exists(target_2027))
                self.assertIn("[ERROR]", out_buf.getvalue())

    def test_no_auto_upgrade_fails_fast_with_remediation_without_invoking_upgrader(self):
        import contextlib
        from unittest import mock

        self._write_fake_model(os.path.join(self.models_dir, "TestTemplate2026.rvt"), 2026)
        target_2027 = os.path.join(self.models_dir, "TestTemplate2027.rvt")
        temp_build_root = os.path.join(self.root, "temp_root")
        os.makedirs(temp_build_root, exist_ok=True)
        subproc_calls = []

        out_buf = io.StringIO()
        with mock.patch.dict(os.environ, {"SYNTHETIC_PROJECT_ROOT": self.root}, clear=False), \
             mock.patch("os.getcwd", return_value=self.root), \
             mock.patch.object(te.tempfile, "gettempdir", return_value=temp_build_root), \
             mock.patch.object(te, "configure_dotnet_environment", return_value=None), \
             mock.patch.object(te.subprocess, "run", side_effect=lambda cmd, **kw: subproc_calls.append(cmd)), \
             contextlib.redirect_stdout(out_buf):
            with self.assertRaises(SystemExit) as ctx:
                te.run_test_suite(target_version="2027", no_auto_upgrade=True)

        self.assertEqual(ctx.exception.code, 1)
        self.assertEqual(subproc_calls, [])
        self.assertFalse(os.path.exists(target_2027))
        output = out_buf.getvalue()
        self.assertIn("[ERROR]", output)
        self.assertIn("TestTemplate2027.rvt", output)
        self.assertIn("--no-auto-upgrade", output)
        self.assertIn("revit_model_upgrader.py", output)

        # Headless Logic run must succeed even with no_auto_upgrade=True and missing templates
        logic_out = io.StringIO()
        logic_trx = os.path.join(temp_build_root, "logic.trx")
        with mock.patch.dict(os.environ, {"SYNTHETIC_PROJECT_ROOT": self.root}, clear=False), \
             mock.patch("os.getcwd", return_value=self.root), \
             mock.patch.object(te.tempfile, "gettempdir", return_value=temp_build_root), \
             mock.patch.object(te, "configure_dotnet_environment", return_value=None), \
             mock.patch.object(te.subprocess, "run", return_value=subprocess.CompletedProcess(["dotnet"], 0)), \
             contextlib.redirect_stdout(logic_out):
            with self.assertRaises(SystemExit) as logic_ctx:
                te.run_test_suite(target_version="Logic", no_auto_upgrade=True)
        self.assertEqual(logic_ctx.exception.code, 0)

    def test_cold_nopool_dispatch_triggers_pre_run_upgrade_and_real_win32_mutex_serializes_concurrent_runners(self):
        import contextlib
        from unittest import mock

        src_2026 = self._write_fake_model(os.path.join(self.models_dir, "TestTemplate2026.rvt"), 2026)
        target_2027 = os.path.join(self.models_dir, "TestTemplate2027.rvt")
        temp_build_root = os.path.join(self.root, "temp_root")
        os.makedirs(temp_build_root, exist_ok=True)

        # 1. Real Win32 mutex serialization + double-checked locking across threads
        worker_entered = threading.Event()
        allow_release = threading.Event()
        upgrader_invocations = []

        def slow_upgrader(cmd):
            upgrader_invocations.append(list(cmd))
            worker_entered.set()
            self.assertTrue(allow_release.wait(timeout=10.0))
            staging_dir = cmd[cmd.index("--output-dir") + 1]
            self._write_fake_model(os.path.join(staging_dir, "TestTemplate2027.rvt"), 2027)
            return subprocess.CompletedProcess(cmd, 0, stdout="", stderr="")

        results = {}

        def run_first():
            with contextlib.redirect_stdout(io.StringIO()):
                results["first"] = te.ensure_test_template_ready(
                    "2027", workspace_path=self.root, upgrader_runner=slow_upgrader
                )

        def run_second():
            self.assertTrue(worker_entered.wait(timeout=10.0))
            with contextlib.redirect_stdout(io.StringIO()):
                results["second"] = te.ensure_test_template_ready(
                    "2027", workspace_path=self.root, upgrader_runner=slow_upgrader
                )

        t1 = threading.Thread(target=run_first)
        t2 = threading.Thread(target=run_second)
        t1.start()
        t2.start()
        self.assertTrue(worker_entered.wait(timeout=10.0))
        time.sleep(0.1)
        allow_release.set()
        t1.join(timeout=10.0)
        t2.join(timeout=10.0)

        self.assertEqual(len(upgrader_invocations), 1)
        self.assertTrue(results["first"])
        self.assertFalse(results["second"])
        self.assertTrue(os.path.isfile(target_2027))

        # 2. Cold (--no-pool) run_test_suite triggers pre-run upgrade prior to execute_revit_test_with_startup_gate
        os.remove(target_2027)
        cold_events = []

        def fake_cold_subproc(cmd, **kwargs):
            if any("revit_model_upgrader.py" in str(part) for part in cmd):
                cold_events.append("upgrader")
                staging_dir = cmd[cmd.index("--output-dir") + 1]
                self._write_fake_model(os.path.join(staging_dir, "TestTemplate2027.rvt"), 2027)
                return subprocess.CompletedProcess(cmd, 0, stdout="", stderr="")
            cold_events.append("build")
            return subprocess.CompletedProcess(cmd, 0)

        def fake_cold_gate_exec(cmd, out_f, worker_env, version, run_id, snapshot_path, timeout):
            self.assertTrue(os.path.isfile(target_2027))
            cold_events.append("cold_worker")
            trx_name = cmd[cmd.index("--logger") + 1].split("LogFileName=", 1)[1]
            results_dir = cmd[cmd.index("--results-directory") + 1]
            self._write_trx(os.path.join(results_dir, trx_name), passed_names=["SmokeCold2027"])
            return 0, False, run_id, False

        class FakeLease:
            def __init__(self, *args, **kwargs):
                pass

            def __enter__(self):
                return self

            def __exit__(self, *args):
                pass

        out_cold = io.StringIO()
        with mock.patch.dict(os.environ, {"SYNTHETIC_PROJECT_ROOT": self.root}, clear=False), \
             mock.patch("os.getcwd", return_value=self.root), \
             mock.patch.object(te.tempfile, "gettempdir", return_value=temp_build_root), \
             mock.patch.object(te, "configure_dotnet_environment", return_value=None), \
             mock.patch.object(te.guid_whitelister, "whitelist_existing_addins"), \
             mock.patch.object(te, "check_legacy_programdata_manifest"), \
             mock.patch.object(te, "snapshot_known_journals"), \
             mock.patch.object(te, "RicaunBundleLease", FakeLease), \
             mock.patch.object(te, "execute_revit_test_with_startup_gate", side_effect=fake_cold_gate_exec), \
             mock.patch.object(te, "run_journal_cleanliness_gate", return_value=(True, "CLEAN")), \
             mock.patch.object(te.subprocess, "run", side_effect=fake_cold_subproc), \
             contextlib.redirect_stdout(out_cold):
            with self.assertRaises(SystemExit) as cold_ctx:
                te.run_test_suite(target_version="2027", no_pool=True)

        self.assertEqual(cold_ctx.exception.code, 0)
        self.assertEqual(cold_events, ["upgrader", "build", "cold_worker"])
        self.assertEqual(te.inspect_model_format_year(target_2027), 2027)
        self.assertIn(os.path.basename(src_2026), out_cold.getvalue())

    def test_upgrader_cli_args_exclude_inc_standards_and_nested_worker_guard_blocks_launch(self):
        import contextlib
        from unittest import mock

        src_2026 = self._write_fake_model(os.path.join(self.models_dir, "TestTemplate2026.rvt"), 2026)
        self._write_fake_model(
            os.path.join(self.models_dir, "INC Standards - Assemblies & Details2026.rvt"),
            2026,
        )
        staging_dir = os.path.join(self.root, "stage_dry")
        os.makedirs(staging_dir, exist_ok=True)
        report_path = os.path.join(staging_dir, "dry_report.json")

        # Verify real revit_model_upgrader.py CLI parses the exact flags used by ensure_test_template_ready
        # and schedules ONLY TestTemplate2027.rvt (never INC Standards).
        res = subprocess.run(
            [
                sys.executable,
                te.get_model_upgrader_path(),
                src_2026,
                "--versions",
                "2027",
                "--output-dir",
                staging_dir,
                "--report",
                report_path,
                "--dry-run",
            ],
            cwd=REPO_ROOT,
            capture_output=True,
            text=True,
        )
        self.assertEqual(res.returncode, 0, msg=res.stdout + res.stderr)
        with open(report_path, "r", encoding="utf-8") as rf:
            report = json.load(rf)
        self.assertEqual(len(report["PlannedJobs"]), 1)
        self.assertEqual(
            os.path.abspath(report["PlannedJobs"][0]["TargetPath"]),
            os.path.abspath(os.path.join(staging_dir, "TestTemplate2027.rvt")),
        )
        self.assertEqual(report["PlannedJobs"][0]["MinLoadableFamilies"], 2)

        # Nested Revit worker env must fail fast instead of launching RevitModelUpgrader
        with mock.patch.dict(os.environ, {"SYNTHETIC_TEST_WORKER": "1"}, clear=False):
            with contextlib.redirect_stdout(io.StringIO()):
                with self.assertRaises(RuntimeError) as ctx:
                    te.ensure_test_template_ready("2027", workspace_path=self.root)
        self.assertIn("nested", str(ctx.exception).lower())



if __name__ == "__main__":
    unittest.main()
