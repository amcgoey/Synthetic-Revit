import contextlib
import importlib.util
import io
import json
import os
import subprocess
import sys
import tempfile
import unittest

REPO_ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
UPGRADER_SCRIPT_PATH = os.path.join(
    REPO_ROOT, "tools", "RevitModelUpgrader", "revit_model_upgrader.py"
)
TEST_EXECUTOR_SCRIPT_PATH = os.path.join(
    REPO_ROOT, ".agents", "skills", "revit-tests", "scripts", "test_executor.py"
)

_upgrader_spec = importlib.util.spec_from_file_location(
    "revit_model_upgrader", UPGRADER_SCRIPT_PATH
)
rmu = importlib.util.module_from_spec(_upgrader_spec)
_upgrader_spec.loader.exec_module(rmu)

_te_spec = importlib.util.spec_from_file_location("test_executor", TEST_EXECUTOR_SCRIPT_PATH)
te = importlib.util.module_from_spec(_te_spec)
_te_spec.loader.exec_module(te)


def _write_fake_model(path, format_year):
    os.makedirs(os.path.dirname(os.path.abspath(path)), exist_ok=True)
    header = b"\xd0\xcf\x11\xe0\xa1\xb1\x1a\xe1"
    info_block = f"Worksharing: Not enabled\r\nFormat: {format_year}\r\n".encode("utf-16-le")
    with open(path, "wb") as f:
        f.write(header + b"\x00" * 32 + info_block)
    return path


class TestRevitModelUpgraderNamingAndDiscovery(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.root = self.tmp.name

    def tearDown(self):
        self.tmp.cleanup()

    def test_compute_output_path_supports_rvt_rte_rfa_with_and_without_year(self):
        cases = [
            (os.path.join(self.root, "TestTemplate2026.rvt"), 2027, None, os.path.join(self.root, "TestTemplate2027.rvt")),
            (os.path.join(self.root, "INC Standards - Assemblies & Details.rvt"), 2026, None, os.path.join(self.root, "INC Standards - Assemblies & Details2026.rvt")),
            (os.path.join(self.root, "OfficeTemplate2024.rte"), 2027, None, os.path.join(self.root, "OfficeTemplate2027.rte")),
            (os.path.join(self.root, "DoorPanel.rfa"), 2026, os.path.join(self.root, "out"), os.path.join(self.root, "out", "DoorPanel2026.rfa")),
        ]
        for src, ver, out_dir, expected in cases:
            with self.subTest(src=src, ver=ver):
                self.assertEqual(
                    rmu.compute_output_path(src, ver, output_dir=out_dir),
                    os.path.abspath(expected),
                )

    def test_inspect_model_format_year_reads_utf16_basic_file_info(self):
        rvt = _write_fake_model(os.path.join(self.root, "Sample.rvt"), 2024)
        rte = _write_fake_model(os.path.join(self.root, "Template.rte"), 2025)
        rfa = _write_fake_model(os.path.join(self.root, "Fam.rfa"), 2026)

        self.assertEqual(rmu.inspect_model_format_year(rvt), 2024)
        self.assertEqual(rmu.inspect_model_format_year(rte), 2025)
        self.assertEqual(rmu.inspect_model_format_year(rfa), 2026)
        self.assertIsNone(rmu.inspect_model_format_year(os.path.join(self.root, "missing.rvt")))

    def test_discover_installed_revit_versions_requires_revit_exe(self):
        pf = os.path.join(self.root, "Program Files")
        for year in ("2023", "2026", "2027"):
            revit_dir = os.path.join(pf, "Autodesk", f"Revit {year}")
            os.makedirs(revit_dir, exist_ok=True)
            with open(os.path.join(revit_dir, "Revit.exe"), "wb") as f:
                f.write(b"MZ")
        # Folder without Revit.exe must be ignored
        os.makedirs(os.path.join(pf, "Autodesk", "Revit 2025"), exist_ok=True)
        os.makedirs(os.path.join(pf, "Autodesk", "AutoCAD 2026"), exist_ok=True)

        discovered = rmu.discover_installed_revit_versions(program_files=pf)
        self.assertEqual(discovered, [2023, 2026, 2027])

    def test_collect_input_model_files_filters_backups_and_staging_from_directory(self):
        _write_fake_model(os.path.join(self.root, "A.rvt"), 2024)
        _write_fake_model(os.path.join(self.root, "B.rte"), 2024)
        _write_fake_model(os.path.join(self.root, "C.rfa"), 2024)
        _write_fake_model(os.path.join(self.root, "A.0001.rvt"), 2024)
        with open(os.path.join(self.root, "notes.txt"), "w", encoding="utf-8") as f:
            f.write("ignore")

        files = rmu.collect_input_model_files([self.root])
        self.assertEqual(
            [os.path.basename(p) for p in files],
            ["A.rvt", "B.rte", "C.rfa"],
        )


class TestRevitModelUpgraderJobPlanning(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.root = self.tmp.name

    def tearDown(self):
        self.tmp.cleanup()

    def test_all_targets_installed_versions_newer_than_source_format(self):
        src_rte = _write_fake_model(os.path.join(self.root, "Office2024.rte"), 2024)
        src_rfa = _write_fake_model(os.path.join(self.root, "Chair2026.rfa"), 2026)

        plan = rmu.plan_upgrade_jobs(
            inputs=[src_rte, src_rfa],
            upgrade_all=True,
            audit=True,
            installed_versions=[2023, 2024, 2025, 2026, 2027],
        )

        planned = [(os.path.basename(j.target_path), j.target_version, j.audit) for j in plan.jobs]
        self.assertEqual(
            planned,
            [
                ("Office2025.rte", 2025, True),
                ("Office2026.rte", 2026, True),
                ("Office2027.rte", 2027, True),
                ("Chair2027.rfa", 2027, True),
            ],
        )

    def test_skips_up_to_date_targets_unless_force_and_upgrades_stale_targets(self):
        src = _write_fake_model(os.path.join(self.root, "Model2024.rvt"), 2024)
        _write_fake_model(os.path.join(self.root, "Model2026.rvt"), 2026)
        _write_fake_model(os.path.join(self.root, "Model2027.rvt"), 2025)  # Stale format

        plan = rmu.plan_upgrade_jobs(
            inputs=[src],
            versions=["2026,2027"],
            force=False,
        )
        self.assertEqual(len(plan.skipped), 1)
        self.assertEqual(plan.skipped[0].target_version, 2026)
        self.assertEqual(len(plan.jobs), 1)
        self.assertEqual(plan.jobs[0].target_version, 2027)

        forced_plan = rmu.plan_upgrade_jobs(
            inputs=[src],
            versions=["2026", "2027"],
            force=True,
        )
        self.assertEqual(len(forced_plan.skipped), 0)
        self.assertEqual([j.target_version for j in forced_plan.jobs], [2026, 2027])

    def test_downgrade_raises_clear_error(self):
        src = _write_fake_model(os.path.join(self.root, "Modern2026.rvt"), 2026)
        with self.assertRaises(ValueError) as ctx:
            rmu.plan_upgrade_jobs(inputs=[src], versions=["2025"])
        self.assertIn("downgrade", str(ctx.exception).lower())

    def test_preset_test_models_prefers_previous_year_and_skips_absent_studio_models(self):
        models_dir = os.path.join(self.root, "tests", "Assets", "test_models")
        _write_fake_model(os.path.join(models_dir, "TestTemplate2025.rvt"), 2025)
        _write_fake_model(os.path.join(models_dir, "TestTemplate2026.rvt"), 2026)

        plan = rmu.plan_upgrade_jobs(
            preset="test-models",
            versions=["2027"],
            repo_root=self.root,
        )

        self.assertEqual(len(plan.jobs), 1)
        self.assertEqual(os.path.basename(plan.jobs[0].source_path), "TestTemplate2026.rvt")
        self.assertEqual(os.path.basename(plan.jobs[0].target_path), "TestTemplate2027.rvt")
        self.assertEqual(plan.jobs[0].min_loadable_families, 2)
        self.assertTrue(any("INC Standards - Assemblies & Details" in n for n in plan.notes))

    def test_preset_test_models_falls_back_to_newest_earlier_local_inc_standards(self):
        models_dir = os.path.join(self.root, "tests", "Assets", "test_models")
        _write_fake_model(os.path.join(models_dir, "TestTemplate2026.rvt"), 2026)
        _write_fake_model(os.path.join(models_dir, "INC Standards - Assemblies & Details.rvt"), 2023)
        _write_fake_model(os.path.join(models_dir, "INC Standards - Assemblies & Details2025.rvt"), 2025)

        plan = rmu.plan_upgrade_jobs(
            preset="test-models",
            versions=["2027"],
            repo_root=self.root,
        )

        self.assertEqual(len(plan.jobs), 2)
        inc_job = [j for j in plan.jobs if "INC Standards" in j.target_path][0]
        self.assertEqual(
            os.path.basename(inc_job.source_path),
            "INC Standards - Assemblies & Details2025.rvt",
        )
        self.assertEqual(
            os.path.basename(inc_job.target_path),
            "INC Standards - Assemblies & Details2027.rvt",
        )
        self.assertEqual(inc_job.min_loadable_families, 0)

    def test_preset_test_models_falls_back_to_unversioned_inc_standards_when_only_base_exists(self):
        models_dir = os.path.join(self.root, "tests", "Assets", "test_models")
        _write_fake_model(os.path.join(models_dir, "TestTemplate2026.rvt"), 2026)
        _write_fake_model(os.path.join(models_dir, "INC Standards - Assemblies & Details.rvt"), 2023)

        plan = rmu.plan_upgrade_jobs(
            preset="test-models",
            versions=["2027"],
            repo_root=self.root,
        )

        inc_job = [j for j in plan.jobs if "INC Standards" in j.target_path][0]
        self.assertEqual(
            os.path.basename(inc_job.source_path),
            "INC Standards - Assemblies & Details.rvt",
        )
        self.assertEqual(
            os.path.basename(inc_job.target_path),
            "INC Standards - Assemblies & Details2027.rvt",
        )

    def test_preset_test_models_all_skips_up_to_date_and_schedules_newer(self):
        models_dir = os.path.join(self.root, "tests", "Assets", "test_models")
        for yr in (2023, 2024, 2025, 2026):
            _write_fake_model(os.path.join(models_dir, f"TestTemplate{yr}.rvt"), yr)

        plan = rmu.plan_upgrade_jobs(
            preset="test-models",
            upgrade_all=True,
            repo_root=self.root,
            installed_versions=[2023, 2024, 2025, 2026, 2027],
        )

        self.assertEqual([s.target_version for s in plan.skipped], [2023, 2024, 2025, 2026])
        self.assertEqual([j.target_version for j in plan.jobs], [2027])


class TestRevitModelUpgraderStagingAndBatchExecution(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.root = self.tmp.name

    def tearDown(self):
        self.tmp.cleanup()

    def test_atomic_stage_file_creates_and_replaces_without_leftover_staging(self):
        staging_dir = os.path.join(self.root, "staging")
        os.makedirs(staging_dir, exist_ok=True)
        staged = os.path.join(staging_dir, "Model2027.rvt")
        target = os.path.join(self.root, "final", "Model2027.rvt")

        with open(staged, "wb") as f:
            f.write(b"new-version-payload")

        rmu.atomic_stage_file(staged, target)
        self.assertFalse(os.path.exists(staged))
        with open(target, "rb") as f:
            self.assertEqual(f.read(), b"new-version-payload")

        # Overwrite existing target atomically
        staged2 = os.path.join(staging_dir, "Model2027_v2.rvt")
        with open(staged2, "wb") as f:
            f.write(b"replaced-payload")
        rmu.atomic_stage_file(staged2, target)
        self.assertFalse(os.path.exists(staged2))
        with open(target, "rb") as f:
            self.assertEqual(f.read(), b"replaced-payload")

    def test_upgrader_is_isolated_from_test_executor_discovery(self):
        discovered = te.discover_test_projects(os.path.join(REPO_ROOT, "tests"))
        for key, paths in discovered.items():
            for p in paths:
                self.assertNotIn("RevitModelUpgrader", p, f"Unexpected discovery under {key}")

    def test_execute_version_batch_orchestrates_whitelist_lease_build_test_and_assert_clean(self):
        src = _write_fake_model(os.path.join(self.root, "Sample2025.rvt"), 2025)
        dst = os.path.join(self.root, "Sample2026.rvt")
        job = rmu.UpgradeJob(source_path=src, target_path=dst, target_version=2026, audit=True)

        calls = []

        class FakeLease:
            def __init__(self, testadapter_search_dirs=None):
                calls.append(("lease_init", list(testadapter_search_dirs or ())))

            def __enter__(self):
                calls.append(("lease_enter",))
                return self

            def __exit__(self, exc_type, exc_val, exc_tb):
                calls.append(("lease_exit",))

        class FakeRestore:
            def __init__(self, version):
                self.version = version

            def restore_addins(self):
                calls.append(("restore_addins", self.version))

        def fake_subprocess_run(cmd, check=True):
            calls.append(("build", cmd))
            return subprocess.CompletedProcess(cmd, 0)

        def fake_startup_exec(cmd, out_f, worker_env, version, run_id, snapshot_path, timeout):
            calls.append(("test_exec", cmd, worker_env["SYNTHETIC_UPGRADER_MANIFEST"], version, timeout))
            with open(worker_env["SYNTHETIC_UPGRADER_MANIFEST"], "r", encoding="utf-8") as mf:
                manifest = json.load(mf)
            report_payload = {
                "TargetVersion": "2026",
                "ExecutedAtUtc": "2026-11-25T00:00:00Z",
                "Jobs": [
                    {
                        "SourcePath": src,
                        "TargetPath": dst,
                        "Success": True,
                        "Audit": True,
                        "WasWorkshared": False,
                        "FileExtension": ".rvt",
                        "SourceFormat": "2025",
                        "VerifiedFormat": "2026",
                        "WarningCount": 1,
                        "ErrorCount": 0,
                        "SuppressedDialogs": [],
                        "Failures": [
                            {
                                "FailureDefinitionGuid": "00000000-0000-0000-0000-000000000001",
                                "Severity": "Warning",
                                "Description": "Sample upgrade warning",
                                "FailingElementIds": [42],
                                "ResolutionAction": "RecordedAndDeletedWarning",
                            }
                        ],
                        "ErrorMessage": "",
                    }
                ],
            }
            with open(manifest["ReportPath"], "w", encoding="utf-8") as rf:
                json.dump(report_payload, rf)
            return 0, False, run_id, False

        def fake_gate(ver, run_id, snapshot_path):
            calls.append(("assert_clean", ver, run_id))
            return True, "[CLEAN] Journal verified."

        batch_ok, report = rmu.execute_version_batch(
            target_version=2026,
            jobs=[job],
            repo_root=self.root,
            installed_versions=[2026],
            configure_dotnet_fn=lambda: {"version": "10.0.404", "dotnet_root": ""},
            resolve_dotnet_fn=lambda _: "dotnet",
            whitelist_fn=lambda ver: calls.append(("whitelist", ver)),
            restore_addins_cls=FakeRestore,
            legacy_manifest_fn=lambda ver: calls.append(("legacy_manifest", ver)),
            bundle_lease_cls=FakeLease,
            snapshot_journals_fn=lambda ver, path: calls.append(("snapshot", ver, path)),
            run_gate_fn=fake_gate,
            startup_gate_exec_fn=fake_startup_exec,
            subprocess_run_fn=fake_subprocess_run,
            cleanup_temp_fn=lambda rid: calls.append(("cleanup_temp", rid)),
            temp_root=os.path.join(self.root, "temp"),
        )

        self.assertTrue(batch_ok)
        self.assertTrue(report["JournalClean"])
        self.assertEqual(report["Jobs"][0]["WarningCount"], 1)
        call_kinds = [c[0] for c in calls]
        self.assertIn("build", call_kinds)
        self.assertIn("whitelist", call_kinds)
        self.assertIn("lease_enter", call_kinds)
        self.assertIn("test_exec", call_kinds)
        self.assertIn("lease_exit", call_kinds)
        self.assertIn("assert_clean", call_kinds)

        report_text = rmu.format_upgrade_report_text(rmu.UpgradePlan(jobs=[job]), [report])
        self.assertIn("Sample upgrade warning", report_text)
        self.assertIn("Warnings=1", report_text)

    def test_execute_version_batch_fails_when_assert_clean_fails(self):
        src = _write_fake_model(os.path.join(self.root, "Sample2025.rvt"), 2025)
        dst = os.path.join(self.root, "Sample2026.rvt")
        job = rmu.UpgradeJob(source_path=src, target_path=dst, target_version=2026)

        class FakeLease:
            def __init__(self, testadapter_search_dirs=None):
                pass

            def __enter__(self):
                return self

            def __exit__(self, exc_type, exc_val, exc_tb):
                pass

        class FakeRestore:
            def __init__(self, version):
                pass

            def restore_addins(self):
                pass

        def fake_startup_exec(cmd, out_f, worker_env, version, run_id, snapshot_path, timeout):
            with open(worker_env["SYNTHETIC_UPGRADER_MANIFEST"], "r", encoding="utf-8") as mf:
                manifest = json.load(mf)
            with open(manifest["ReportPath"], "w", encoding="utf-8") as rf:
                json.dump(
                    {
                        "TargetVersion": "2026",
                        "Jobs": [{"SourcePath": src, "TargetPath": dst, "Success": True}],
                    },
                    rf,
                )
            return 0, False, run_id, False

        batch_ok, report = rmu.execute_version_batch(
            target_version=2026,
            jobs=[job],
            repo_root=self.root,
            installed_versions=[2026],
            configure_dotnet_fn=lambda: {"version": "10.0.404", "dotnet_root": ""},
            resolve_dotnet_fn=lambda _: "dotnet",
            whitelist_fn=lambda _: None,
            restore_addins_cls=FakeRestore,
            legacy_manifest_fn=lambda _: None,
            bundle_lease_cls=FakeLease,
            snapshot_journals_fn=lambda *_: None,
            run_gate_fn=lambda *_: (False, "[DIRTY] Orphaned transaction"),
            startup_gate_exec_fn=fake_startup_exec,
            subprocess_run_fn=lambda cmd, check=True: subprocess.CompletedProcess(cmd, 0),
            cleanup_temp_fn=lambda *_: None,
            temp_root=os.path.join(self.root, "temp"),
        )

        self.assertFalse(batch_ok)
        self.assertFalse(report["JournalClean"])

    def test_parameterized_csproj_builds_net48_and_net8_and_evaluates_net10_2027(self):
        env = os.environ.copy()
        sdk_info = te.configure_dotnet_environment(env=env)
        dotnet = te.resolve_dotnet_exe(sdk_info)

        build_root = os.path.join(self.root, "build_out")
        for revit_ver, expected_tfm in (("2023", "net48"), ("2026", "net8.0-windows8.0")):
            with self.subTest(revit_ver=revit_ver):
                cmd = [
                    dotnet,
                    "build",
                    rmu.UPGRADER_CSPROJ,
                    "-c",
                    "Debug",
                    "-p:Platform=x64",
                    f"-p:RevitVersion={revit_ver}",
                    f"-p:UpgraderBuildRoot={build_root}",
                    "-v:minimal",
                ]
                res = subprocess.run(cmd, env=env, capture_output=True, text=True)
                self.assertEqual(res.returncode, 0, msg=res.stdout + res.stderr)
                self.assertTrue(
                    os.path.isfile(os.path.join(build_root, "bin", revit_ver, "RevitModelUpgrader.dll"))
                )

                eval_res = subprocess.run(
                    [
                        dotnet,
                        "msbuild",
                        rmu.UPGRADER_CSPROJ,
                        f"-p:RevitVersion={revit_ver}",
                        "-getProperty:TargetFramework",
                    ],
                    env=env,
                    capture_output=True,
                    text=True,
                )
                self.assertEqual(eval_res.returncode, 0, msg=eval_res.stdout + eval_res.stderr)
                self.assertEqual(eval_res.stdout.strip(), expected_tfm)

        eval_2027 = subprocess.run(
            [
                dotnet,
                "msbuild",
                rmu.UPGRADER_CSPROJ,
                "-p:RevitVersion=2027",
                "-getProperty:TargetFramework",
                "-getProperty:RevitApiPackageVersion",
            ],
            env=env,
            capture_output=True,
            text=True,
        )
        self.assertEqual(eval_2027.returncode, 0, msg=eval_2027.stdout + eval_2027.stderr)
        props_2027 = json.loads(eval_2027.stdout)["Properties"]
        self.assertEqual(props_2027["TargetFramework"], "net10.0-windows8.0")
        self.assertEqual(props_2027["RevitApiPackageVersion"], "2027.2.0")

    def test_dry_run_preset_on_repo_clean_clone_exits_zero(self):
        out_buf = io.StringIO()
        with contextlib.redirect_stdout(out_buf):
            code, summary = rmu.run_upgrader(
                preset="test-models",
                versions=["2027"],
                dry_run=True,
                repo_root=REPO_ROOT,
                installed_versions=[2026, 2027],
            )
        self.assertEqual(code, 0)
        self.assertTrue(summary["DryRun"])
        self.assertEqual(len(summary["PlannedJobs"]), 1)
        self.assertTrue(summary["PlannedJobs"][0]["TargetPath"].endswith("TestTemplate2027.rvt"))


if __name__ == "__main__":
    unittest.main()
