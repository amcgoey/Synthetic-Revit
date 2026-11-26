import importlib.util
import json
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
    "revit-journal-telemetry",
    "scripts",
    "revit_journal_tool.py",
)

spec = importlib.util.spec_from_file_location("revit_journal_tool", SCRIPT_PATH)
rjt = importlib.util.module_from_spec(spec)
spec.loader.exec_module(rjt)


class TestConfigParsing(unittest.TestCase):
    def test_parses_repo_revit_config_markdown_table(self):
        config_path = os.path.join(REPO_ROOT, "docs", "agents", "revit-config.md")
        self.assertTrue(os.path.exists(config_path))
        self.assertEqual(rjt.get_current_latest_version(config_path), "2027")

    def test_parses_markdown_table_arbitrary_version(self):
        content = (
            "| Constant | Value | Description |\n"
            "| :--- | :--- | :--- |\n"
            "| `CURRENT_LATEST_VERSION` | `2027` | Latest |\n"
        )
        self.assertEqual(rjt.parse_current_latest_version(content), "2027")

    def test_parses_legacy_equals_syntax(self):
        self.assertEqual(
            rjt.parse_current_latest_version("CURRENT_LATEST_VERSION = 2025\n"), "2025"
        )
        self.assertEqual(
            rjt.parse_current_latest_version('CURRENT_LATEST_VERSION="2024"'), "2024"
        )

    def test_missing_config_falls_back_to_2027(self):
        self.assertEqual(
            rjt.get_current_latest_version(os.path.join(REPO_ROOT, "nonexistent.md")),
            "2027",
        )


class TestJournalResolution(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.journals_dir = self.tmp.name

    def tearDown(self):
        self.tmp.cleanup()

    def _write_journal(self, name, content, mtime_offset=0.0, encoding="utf-8"):
        path = os.path.join(self.journals_dir, name)
        with open(path, "w", encoding=encoding) as f:
            f.write(content)
        ts = time.time() + mtime_offset
        os.utime(path, (ts, ts))
        return path

    def test_file_takes_priority_over_run_id_and_journals_dir(self):
        self._write_journal("journal.0001.txt", "' SYNTHETIC_TEST_RUN:run-a\n", mtime_offset=10)
        explicit = self._write_journal("custom.txt", "' explicit\n", mtime_offset=-10)
        resolved = rjt.resolve_journal_path(
            "2026",
            file_path=explicit,
            run_id="run-a",
            journals_dir=self.journals_dir,
        )
        self.assertEqual(os.path.abspath(resolved), os.path.abspath(explicit))

    def test_run_id_selects_exact_worker_journal_over_newer_concurrent_session(self):
        worker = self._write_journal(
            "journal.0101.txt",
            "' 0:< SYNTHETIC_TEST_RUN:worker-42\n'C 20-Nov-2026 10:00:00.000;  <-desktop ExitNativeInstance\n",
            mtime_offset=-10,
        )
        # Newer concurrent developer session without marker
        self._write_journal(
            "journal.0102.txt",
            "' 0:< Interactive developer session\n",
            mtime_offset=10,
        )
        resolved = rjt.resolve_journal_path(
            "2026", run_id="worker-42", journals_dir=self.journals_dir
        )
        self.assertEqual(os.path.abspath(resolved), os.path.abspath(worker))

    def test_run_id_does_not_match_prefix_of_longer_run_id(self):
        self._write_journal(
            "journal.0101.txt", "' 0:< SYNTHETIC_TEST_RUN:run-10\n", mtime_offset=0
        )
        resolved = rjt.resolve_journal_path(
            "2026", run_id="run-1", journals_dir=self.journals_dir
        )
        self.assertIsNone(resolved)

    def test_run_id_pre_startup_crash_fallback_with_known_journals_snapshot(self):
        known1 = self._write_journal("journal.0201.txt", "' existing\n", mtime_offset=-20)
        self._write_journal("journal.0202.txt", "' existing2\n", mtime_offset=-15)
        # New concurrent worker journal with a DIFFERENT run id should be skipped
        self._write_journal(
            "journal.0203.txt",
            "' 0:< SYNTHETIC_TEST_RUN:other-run\n",
            mtime_offset=5,
        )
        # Newly created pre-startup crashed journal (no SYNTHETIC_TEST_RUN yet)
        crashed = self._write_journal(
            "journal.0204.txt",
            "' 0:< Revit crashed during early startup\n",
            mtime_offset=10,
        )
        snapshot_path = os.path.join(self.journals_dir, "known.txt")
        with open(snapshot_path, "w", encoding="utf-8") as f:
            f.write(f"{known1}\njournal.0202.txt\n")

        resolved = rjt.resolve_journal_path(
            "2026",
            run_id="crashed-run",
            known_journals_file=snapshot_path,
            journals_dir=self.journals_dir,
        )
        self.assertEqual(os.path.abspath(resolved), os.path.abspath(crashed))

    def test_run_id_json_known_journals_no_new_file_returns_none(self):
        existing = self._write_journal("journal.0301.txt", "' interactive dev\n")
        snapshot_path = os.path.join(self.journals_dir, "known.json")
        with open(snapshot_path, "w", encoding="utf-8") as f:
            json.dump([existing], f)

        resolved = rjt.resolve_journal_path(
            "2026",
            run_id="missing-run",
            known_journals_file=snapshot_path,
            journals_dir=self.journals_dir,
        )
        self.assertIsNone(resolved)

    def test_default_resolves_latest_journal_in_journals_dir(self):
        self._write_journal("journal.0401.txt", "' older\n", mtime_offset=-5)
        newest = self._write_journal("journal.0402.txt", "' newer\n", mtime_offset=5)
        resolved = rjt.resolve_journal_path("2026", journals_dir=self.journals_dir)
        self.assertEqual(os.path.abspath(resolved), os.path.abspath(newest))

    def test_worker_id_selects_exact_worker_journal_over_newer_concurrent_session(self):
        worker = self._write_journal(
            "journal.0601.txt",
            "' 0:< SYNTHETIC_WORKER_ID:w-42\n' 0:< ricaun.RevitTest: Execute Total: 00:00:01.0\n",
            mtime_offset=-10,
        )
        self._write_journal(
            "journal.0602.txt",
            "' 0:< SYNTHETIC_WORKER_ID:w-99\n",
            mtime_offset=10,
        )
        resolved = rjt.resolve_journal_path(
            "2026", worker_id="w-42", journals_dir=self.journals_dir
        )
        self.assertEqual(os.path.abspath(resolved), os.path.abspath(worker))

    def test_worker_id_takes_priority_over_run_id_and_file_wins_over_both(self):
        self._write_journal(
            "journal.0611.txt",
            "' 0:< SYNTHETIC_TEST_RUN:run-a\n",
            mtime_offset=10,
        )
        worker = self._write_journal(
            "journal.0612.txt",
            "' 0:< SYNTHETIC_WORKER_ID:w-b\n",
            mtime_offset=-5,
        )
        explicit = self._write_journal("explicit.txt", "' explicit\n", mtime_offset=-20)

        resolved_worker = rjt.resolve_journal_path(
            "2026",
            run_id="run-a",
            worker_id="w-b",
            journals_dir=self.journals_dir,
        )
        self.assertEqual(os.path.abspath(resolved_worker), os.path.abspath(worker))

        resolved_file = rjt.resolve_journal_path(
            "2026",
            file_path=explicit,
            run_id="run-a",
            worker_id="w-b",
            journals_dir=self.journals_dir,
        )
        self.assertEqual(os.path.abspath(resolved_file), os.path.abspath(explicit))

    def test_worker_id_does_not_match_prefix_of_longer_worker_id(self):
        self._write_journal(
            "journal.0621.txt",
            "' 0:< SYNTHETIC_WORKER_ID:worker-10\n",
            mtime_offset=0,
        )
        self._write_journal(
            "journal.0622.txt",
            "' 0:< SYNTHETIC_WORKER_ID:worker-1_extra\n",
            mtime_offset=5,
        )
        self._write_journal(
            "journal.0623.txt",
            "' 0:< SYNTHETIC_WORKER_ID:worker-1.2\n",
            mtime_offset=10,
        )
        self._write_journal(
            "journal.0624.txt",
            "' 0:< SYNTHETIC_WORKER_ID:worker-1-a\n",
            mtime_offset=15,
        )
        self.assertFalse(rjt._contains_worker_id("' 0:< SYNTHETIC_WORKER_ID:worker-10\n", "worker-1"))
        self.assertTrue(rjt._contains_worker_id("' 0:< SYNTHETIC_WORKER_ID:worker-1\n", "worker-1"))
        resolved = rjt.resolve_journal_path(
            "2026", worker_id="worker-1", journals_dir=self.journals_dir
        )
        self.assertIsNone(resolved)

    def test_unmatched_worker_id_returns_none_without_fallback_to_run_id_or_latest(self):
        existing = self._write_journal(
            "journal.0631.txt",
            "' 0:< SYNTHETIC_TEST_RUN:run-a\n",
            mtime_offset=-5,
        )
        self._write_journal(
            "journal.0632.txt",
            "' 0:< Unknown new journal\n",
            mtime_offset=10,
        )
        snapshot_path = os.path.join(self.journals_dir, "known_worker.txt")
        with open(snapshot_path, "w", encoding="utf-8") as f:
            f.write(f"{existing}\n")

        resolved = rjt.resolve_journal_path(
            "2026",
            worker_id="missing-worker",
            run_id="run-a",
            known_journals_file=snapshot_path,
            journals_dir=self.journals_dir,
        )
        self.assertIsNone(resolved)

        resolved_empty = rjt.resolve_journal_path(
            "2026",
            worker_id="   ",
            run_id="run-a",
            journals_dir=self.journals_dir,
        )
        self.assertIsNone(resolved_empty)

    def test_reads_utf16_and_utf8_bom_journals(self):
        p16 = self._write_journal(
            "journal.0501.txt", "' 0:< SYNTHETIC_TEST_RUN:utf16-run\n", encoding="utf-16"
        )
        lines, enc = rjt.read_journal_lines(p16)
        self.assertEqual(enc, "utf-16")
        self.assertIn("SYNTHETIC_TEST_RUN:utf16-run", "".join(lines))

        p8 = self._write_journal(
            "journal.0502.txt", "' 0:< SYNTHETIC_TEST_RUN:utf8-run\n", encoding="utf-8-sig"
        )
        lines8, enc8 = rjt.read_journal_lines(p8)
        self.assertEqual(enc8, "utf-8-sig")
        self.assertIn("SYNTHETIC_TEST_RUN:utf8-run", "".join(lines8))


class TestTransactionSeamAndExceptions(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()

    def tearDown(self):
        self.tmp.cleanup()

    def _analyze(self, lines, filename="journal.0900.txt", extra_files=()):
        jpath = os.path.join(self.tmp.name, filename)
        with open(jpath, "w", encoding="utf-8") as f:
            f.writelines(ln if ln.endswith("\n") else ln + "\n" for ln in lines)
        for extra in extra_files:
            with open(os.path.join(self.tmp.name, extra), "w", encoding="utf-8") as ef:
                ef.write("dump")
        read_lines, _ = rjt.read_journal_lines(jpath)
        return rjt.analyze_journal_state(read_lines, jpath)

    def test_committed_and_repeated_antecedent_transactions_have_no_orphans(self):
        res = self._analyze(
            [
                "' 4:< ANTECEDENT:",
                "'inTransaction APITransactionIdentity_1:-1 Create Group with Annotation Member",
                "' 4:< ANTECEDENT:",
                "'inTransaction APITransactionIdentity_1:-1 Create Group with Annotation Member",
                'Jrn.Data  _',
                '        "Transaction Successful"  , "Create Group with Annotation Member"',
                "' 0:< Before : Destroy Display Manager",
                "' 0:< After : Destroy Display Manager",
            ]
        )
        self.assertEqual(res["orphaned_transactions"], {})
        self.assertTrue(res["is_clean"])

    def test_cpp_internal_transactions_ignored(self):
        res = self._analyze(
            [
                "'inTransaction PostLoadDocument.cpp:230 Update project to latest changes.",
                "'inTransaction FamilyBaseUI.cpp:548 Edit Family",
                "'C 20-Nov-2026 22:00:42.402;  <-desktop ExitNativeInstance",
            ]
        )
        self.assertEqual(res["orphaned_transactions"], {})
        self.assertTrue(res["is_clean"])

    def test_end_or_abort_undo_moves_open_tx_to_rolled_back_excluding_checks(self):
        res = self._analyze(
            [
                "'inTransaction APITransactionIdentity_1:-1 Expected Rollback Tx",
                "'  0.136657    2:<<EndOrAbortUndoTransaction()--end of transaction checks;DOPT;",
                "'  0.154810   1:<<EndOrAbortUndoTransaction();DOPT;/ALL_GAPS   0.018153",
            ]
        )
        self.assertEqual(res["orphaned_transactions"], {})
        self.assertEqual(len(res["state"]["rolled_back_transactions"]), 1)
        self.assertEqual(
            res["state"]["rolled_back_transactions"][0][1], "Expected Rollback Tx"
        )
        self.assertTrue(res["is_clean"])

    def test_end_of_transaction_checks_does_not_mask_uncommitted_orphan(self):
        res = self._analyze(
            [
                "'inTransaction APITransactionIdentity_1:-1 Leaked Tx",
                "'  0.136657    2:<<EndOrAbortUndoTransaction()--end of transaction checks;DOPT;",
            ]
        )
        self.assertIn("Leaked Tx", res["orphaned_transactions"])
        self.assertFalse(res["is_clean"])

    def test_explicit_transaction_rolled_back_and_cancelled(self):
        res = self._analyze(
            [
                "'inTransaction APITransactionIdentity_1:-1 Tx One",
                '"Transaction Rolled Back"  , "Tx One"',
                "'inTransaction APITransactionIdentity_1:-1 Tx Two",
                "Transaction Cancelled",
            ]
        )
        self.assertEqual(res["orphaned_transactions"], {})
        rolled = [name for _, name in res["state"]["rolled_back_transactions"]]
        self.assertEqual(rolled, ["Tx One", "Tx Two"])
        self.assertTrue(res["is_clean"])

    def test_benign_noise_and_third_party_exceptions_do_not_fail_cleanliness(self):
        res = self._analyze(
            [
                "' 0:< SYNTHETIC_TEST_RUN:run-99",
                "' 0:< SYNTHETIC_WORKER_ID:worker-99-error-fail-Exception",
                "' 0:< API_ERROR { : Assembly version conflict in some references in Synthetic2026.dll assembly",
                "' 0:< false=isProcessUserModeExceptionPolicyDisabledForCallbackFilter",
                "' 0:< API_SUCCESS { Registering DocumentOpened event by application Synthetic. }",
                "' 0:< GetPreferences failed fallback",
                "' 0:< LoadLatestEpoch error ignored",
                "' 0:< [MERGE_WARNING] Synthetic.Modules.MergeDuplicates warning",
                "' 0:< [MERGE_ERROR] Synthetic.Modules.MergeDuplicates error",
                "' 0:< ricaun.RevitTest: FilterName: SyntheticTests.Modules.FooTests.HandlesException",
                "' 0:< ricaun.RevitTest: \t 1.2\t HandlesException\t Passed\t ",
                "' 4:< API_ERROR {  System.ArgumentNullException exception(Value cannot be null.",
                "'Parameter name: key) was thrown by application Enscape 4.22.0. }",
            ]
        )
        self.assertFalse(
            any("SYNTHETIC_WORKER_ID:" in msg for _, msg in res["state"]["warnings"])
        )
        self.assertEqual(res["synthetic_exceptions"], [])
        self.assertEqual(len(res["state"]["exceptions"]), 1)
        self.assertTrue(res["is_clean"])

    def test_worker_id_marker_filtered_as_benign_in_analyze_and_assert_clean(self):
        res = self._analyze(
            [
                "' 0:< SYNTHETIC_WORKER_ID:pool-error-fail-SystemException",
                "' 0:< ricaun.RevitTest: Execute Total: 00:00:01.0",
            ]
        )
        self.assertEqual(res["state"]["warnings"], [])
        self.assertEqual(res["state"]["exceptions"], [])
        self.assertEqual(res["synthetic_exceptions"], [])
        self.assertTrue(res["is_clean"])
        self.assertIn("Potential Error/Warning Logs Found: 0", res["report"])
        self.assertIn("Total Managed Exceptions: 0", res["report"])

    def test_captures_synthetic_and_synthetictests_exception_blocks(self):
        res = self._analyze(
            [
                "' 0:< System.InvalidOperationException: Boom in app",
                "'    at Synthetic.Modules.ShadowStudy.Exporter.Run()",
                "' 0:< ::100:: Delta VM: Avail 100 MB",
                "' 0:< SyntheticTests.Helpers.FixtureException: Fixture setup failed",
                "'    at Autodesk.Revit.UI.ExternalCommand()",
            ]
        )
        self.assertEqual(len(res["synthetic_exceptions"]), 2)
        self.assertFalse(res["is_clean"])

    def test_matching_dmp_fails_cleanliness_while_unrelated_dmp_is_ignored(self):
        clean_res = self._analyze(
            ["' 0:< ricaun.RevitTest: Execute Total: 00:00:01.0"],
            filename="journal.0910.txt",
            extra_files=("journal.0911.0001.dmp",),
        )
        self.assertTrue(clean_res["is_clean"])

        dirty_res = self._analyze(
            ["' 0:< ricaun.RevitTest: Execute Total: 00:00:01.0"],
            filename="journal.0910.txt",
            extra_files=("journal.0910.0001.dmp",),
        )
        self.assertFalse(dirty_res["is_clean"])
        self.assertEqual(len(dirty_res["dmp_files"]), 1)

    def test_nunit_close_without_exit_native_instance_is_clean(self):
        res = self._analyze(
            [
                "' 0:< SYNTHETIC_TEST_RUN:run-nunit",
                "' 0:< ricaun.RevitTest: Execute Total: 00:00:05.0",
            ]
        )
        self.assertTrue(res["nunit_close_found"])
        self.assertFalse(res["exit_native_found"])
        self.assertTrue(res["is_clean"])
        self.assertIn("NUnit.Close", res["report"])

    def test_no_tasklist_or_destroy_display_manager_heuristic_in_module(self):
        self.assertFalse(hasattr(rjt, "is_revit_running"))
        res = self._analyze(
            [
                "' 0:< Before : Destroy Display Manager",
                "' 0:< After : Destroy Display Manager",
                "'inTransaction APITransactionIdentity_1:-1 OpenAfterMidSessionDisplayDestroy",
            ]
        )
        # Must NOT classify mid-session Destroy Display Manager as clean
        self.assertFalse(res["is_clean"])

    def test_script_has_no_malformed_ansi_escapes(self):
        with open(SCRIPT_PATH, "r", encoding="utf-8") as f:
            source = f.read()
        self.assertNotIn(r"\03[", source)
        self.assertIn(r"\033[91m", source)


class TestCliInvocation(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()

    def tearDown(self):
        self.tmp.cleanup()

    def _run_cli(self, *args):
        return subprocess.run(
            [sys.executable, SCRIPT_PATH, *args],
            capture_output=True,
            text=True,
            check=False,
        )

    def test_cli_assert_clean_exit_codes_and_modes(self):
        clean_j = os.path.join(self.tmp.name, "journal.0001.txt")
        with open(clean_j, "w", encoding="utf-8") as f:
            f.write(
                "' 0:< SYNTHETIC_TEST_RUN:cli-clean\n"
                "'inTransaction APITransactionIdentity_1:-1 GoodTx\n"
                '        "Transaction Successful"  , "GoodTx"\n'
                "' 0:< ricaun.RevitTest: Execute Total: 00:00:02.0\n"
            )

        proc_clean = self._run_cli(
            "--journals-dir", self.tmp.name, "--run-id", "cli-clean", "--assert-clean"
        )
        self.assertEqual(proc_clean.returncode, 0, proc_clean.stdout + proc_clean.stderr)
        self.assertIn("Cleanliness Gate Result: CLEAN", proc_clean.stdout)

        # --analyze + --output
        out_path = os.path.join(self.tmp.name, "report.txt")
        proc_analyze = self._run_cli("--file", clean_j, "--analyze", "--output", out_path)
        self.assertEqual(proc_analyze.returncode, 0)
        self.assertTrue(os.path.exists(out_path))
        with open(out_path, "r", encoding="utf-8") as rf:
            self.assertIn("TELEMETRY DIAGNOSTIC ANALYSIS", rf.read())

        # --search and --tail
        proc_search = self._run_cli("--file", clean_j, "--search", "GoodTx", "--context", "1")
        self.assertEqual(proc_search.returncode, 0)
        self.assertIn("Matches:      2 found", proc_search.stdout)

        proc_tail = self._run_cli("--file", clean_j, "--tail", "2")
        self.assertEqual(proc_tail.returncode, 0)
        self.assertIn("Execute Total", proc_tail.stdout)

    def test_cli_assert_clean_fails_on_orphan_and_unmatched_run_id(self):
        dirty_j = os.path.join(self.tmp.name, "journal.0002.txt")
        with open(dirty_j, "w", encoding="utf-8") as f:
            f.write("' 0:< SYNTHETIC_TEST_RUN:cli-dirty\n'inTransaction APITransactionIdentity_1:-1 UnclosedTx\n")

        proc_dirty = self._run_cli("--file", dirty_j, "--assert-clean")
        self.assertEqual(proc_dirty.returncode, 1)
        self.assertIn("Cleanliness Gate Result: DIRTY", proc_dirty.stdout)

        proc_missing = self._run_cli(
            "--journals-dir", self.tmp.name, "--run-id", "nonexistent-id", "--assert-clean"
        )
        self.assertEqual(proc_missing.returncode, 1)
        self.assertIn("No journal matching run ID", proc_missing.stderr)

    def test_cli_worker_id_resolution_analyze_and_assert_clean(self):
        clean_worker_j = os.path.join(self.tmp.name, "journal.0010.txt")
        with open(clean_worker_j, "w", encoding="utf-8") as f:
            f.write(
                "' 0:< SYNTHETIC_WORKER_ID:cli-worker-1\n"
                "' 0:< SYNTHETIC_WORKER_ID:cli-worker-1-error-Exception\n"
                "'inTransaction APITransactionIdentity_1:-1 GoodWorkerTx\n"
                '        "Transaction Successful"  , "GoodWorkerTx"\n'
                "' 0:< ricaun.RevitTest: Execute Total: 00:00:02.0\n"
            )

        dirty_worker_j = os.path.join(self.tmp.name, "journal.0011.txt")
        with open(dirty_worker_j, "w", encoding="utf-8") as f:
            f.write(
                "' 0:< SYNTHETIC_WORKER_ID:cli-worker-10\n"
                "'inTransaction APITransactionIdentity_1:-1 LeakedWorkerTx\n"
            )

        run_fallback_j = os.path.join(self.tmp.name, "journal.0012.txt")
        with open(run_fallback_j, "w", encoding="utf-8") as f:
            f.write("' 0:< SYNTHETIC_TEST_RUN:run-fallback\n")

        proc_clean = self._run_cli(
            "--journals-dir", self.tmp.name, "--worker-id", "cli-worker-1", "--assert-clean"
        )
        self.assertEqual(proc_clean.returncode, 0, proc_clean.stdout + proc_clean.stderr)
        self.assertIn("Cleanliness Gate Result: CLEAN", proc_clean.stdout)
        self.assertIn("journal.0010.txt", proc_clean.stdout)

        proc_analyze = self._run_cli(
            "--journals-dir", self.tmp.name, "--worker-id", "cli-worker-1", "--analyze"
        )
        self.assertEqual(proc_analyze.returncode, 0, proc_analyze.stdout + proc_analyze.stderr)
        self.assertIn("Potential Error/Warning Logs Found: 0", proc_analyze.stdout)
        self.assertIn("Total Managed Exceptions: 0", proc_analyze.stdout)

        proc_dirty = self._run_cli(
            "--journals-dir", self.tmp.name, "--worker-id", "cli-worker-10", "--assert-clean"
        )
        self.assertEqual(proc_dirty.returncode, 1)
        self.assertIn("Cleanliness Gate Result: DIRTY", proc_dirty.stdout)

        proc_unmatched = self._run_cli(
            "--journals-dir",
            self.tmp.name,
            "--worker-id",
            "cli-worker",
            "--run-id",
            "run-fallback",
            "--assert-clean",
        )
        self.assertNotEqual(proc_unmatched.returncode, 0)
        self.assertIn("No journal matching worker ID", proc_unmatched.stderr)


if __name__ == "__main__":
    unittest.main()
