import argparse
from dataclasses import dataclass, field
import json
import os
import re
import shutil
import subprocess
import sys
import tempfile

UPGRADER_DIR = os.path.dirname(os.path.abspath(__file__))
UPGRADER_CSPROJ = os.path.join(UPGRADER_DIR, "RevitModelUpgrader.csproj")
REPO_ROOT = os.path.abspath(os.path.join(UPGRADER_DIR, "..", ".."))
TEST_EXECUTOR_SCRIPTS_DIR = os.path.join(
    REPO_ROOT, ".agents", "skills", "revit-tests", "scripts"
)

if TEST_EXECUTOR_SCRIPTS_DIR not in sys.path:
    sys.path.insert(0, TEST_EXECUTOR_SCRIPTS_DIR)

import guid_whitelister
import test_executor as te

SUPPORTED_EXTENSIONS = (".rvt", ".rte", ".rfa")
BACKUP_FILE_RE = re.compile(r"\.\d{4}\.(?:rvt|rte|rfa)$", re.IGNORECASE)
TRAILING_YEAR_RE = re.compile(r"(20\d{2})$")
TEST_TEMPLATE_FILE_RE = re.compile(r"^TestTemplate(20\d{2})?\.rvt$", re.IGNORECASE)
INC_STANDARDS_FILE_RE = re.compile(
    r"^INC Standards - Assemblies & Details(20\d{2})?\.rvt$", re.IGNORECASE
)
BASIC_FILE_INFO_FORMAT_MARKER = "Format:".encode("utf-16-le")
BASIC_FILE_INFO_FORMAT_RE = re.compile(r"Format:\s*(20\d{2})")

DEFAULT_BATCH_TIMEOUT_SECONDS = 1800
TEST_TEMPLATE_MIN_LOADABLE_FAMILIES = 2


@dataclass(frozen=True)
class UpgradeJob:
    source_path: str
    target_path: str
    target_version: int
    audit: bool = False
    min_loadable_families: int = 0
    preset_group: str = ""


@dataclass(frozen=True)
class SkippedTarget:
    source_path: str
    target_path: str
    target_version: int
    reason: str
    preset_group: str = ""


@dataclass
class UpgradePlan:
    jobs: list = field(default_factory=list)
    skipped: list = field(default_factory=list)
    notes: list = field(default_factory=list)


def strip_trailing_year(stem):
    """Strip a trailing 4-digit Revit year (20xx) from a file stem."""
    return TRAILING_YEAR_RE.sub("", stem)


def extract_trailing_year(stem):
    """Extract a trailing 4-digit Revit year (20xx) from a file stem, or None."""
    match = TRAILING_YEAR_RE.search(stem)
    return int(match.group(1)) if match else None


def compute_output_path(source_path, target_version, output_dir=None):
    """Compute <stem-without-year><Year><ext> output path."""
    abs_source = os.path.abspath(source_path)
    base_name = os.path.basename(abs_source)
    stem, ext = os.path.splitext(base_name)
    stem_no_year = strip_trailing_year(stem)
    out_filename = f"{stem_no_year}{int(target_version)}{ext}"
    dest_dir = os.path.abspath(output_dir) if output_dir else os.path.dirname(abs_source)
    return os.path.join(dest_dir, out_filename)


def inspect_model_format_year(model_path):
    """
    Read the BasicFileInfo UTF-16LE 'Format: <Year>' marker from a .rvt/.rte/.rfa file
    without launching Revit. Returns an integer year or None if unreadable/missing.
    """
    if not model_path or not os.path.isfile(model_path):
        return None

    chunk_size = 1024 * 1024
    overlap_size = 64
    overlap = b""

    try:
        with open(model_path, "rb") as f:
            while True:
                chunk = f.read(chunk_size)
                if not chunk:
                    break
                buf = overlap + chunk
                idx = buf.find(BASIC_FILE_INFO_FORMAT_MARKER)
                while idx != -1:
                    tail_bytes = buf[idx : idx + 128]
                    # Ensure even byte count for UTF-16LE decoding
                    if len(tail_bytes) % 2 == 1:
                        tail_bytes = tail_bytes[:-1]
                    tail_text = tail_bytes.decode("utf-16-le", errors="ignore")
                    match = BASIC_FILE_INFO_FORMAT_RE.search(tail_text)
                    if match:
                        return int(match.group(1))
                    idx = buf.find(BASIC_FILE_INFO_FORMAT_MARKER, idx + 2)
                overlap = buf[-overlap_size:] if len(buf) >= overlap_size else buf
    except OSError:
        return None

    return None


def discover_installed_revit_versions(program_files=None, env=None):
    """Discover installed Autodesk Revit versions containing Revit.exe, sorted ascending."""
    env_map = os.environ if env is None else env
    pf = (
        program_files
        or env_map.get("ProgramW6432")
        or env_map.get("ProgramFiles")
        or r"C:\Program Files"
    )
    autodesk_dir = os.path.join(pf, "Autodesk")
    if not os.path.isdir(autodesk_dir):
        return []

    versions = []
    try:
        entries = os.listdir(autodesk_dir)
    except OSError:
        return []

    for entry in entries:
        match = re.fullmatch(r"Revit (20\d{2})", entry)
        if not match:
            continue
        revit_exe = os.path.join(autodesk_dir, entry, "Revit.exe")
        if os.path.isfile(revit_exe):
            versions.append(int(match.group(1)))

    return sorted(set(versions))


def parse_versions_arg(raw_versions):
    """Parse --versions tokens (space- or comma-separated) into sorted unique integer years."""
    if not raw_versions:
        return []
    parsed = []
    for token in raw_versions:
        for part in str(token).split(","):
            cleaned = part.strip()
            if not cleaned:
                continue
            if not re.fullmatch(r"20\d{2}", cleaned):
                raise ValueError(
                    f"Invalid Revit version '{cleaned}'. Expected a 4-digit year such as 2026 or 2027."
                )
            year = int(cleaned)
            if year not in parsed:
                parsed.append(year)
    return sorted(parsed)


def collect_input_model_files(inputs):
    """Collect standalone .rvt/.rte/.rfa files from explicit file or directory inputs."""
    collected = []
    seen = set()

    for raw_path in inputs or ():
        abs_path = os.path.abspath(raw_path)
        if os.path.isfile(abs_path):
            ext = os.path.splitext(abs_path)[1].lower()
            if ext not in SUPPORTED_EXTENSIONS:
                raise ValueError(
                    f"Unsupported file extension '{ext}' for '{abs_path}'. Expected .rvt, .rte, or .rfa."
                )
            norm = os.path.normcase(abs_path)
            if norm not in seen:
                seen.add(norm)
                collected.append(abs_path)
        elif os.path.isdir(abs_path):
            for entry in sorted(os.listdir(abs_path)):
                if entry.startswith(".synthetic_upgrade_staging_"):
                    continue
                if BACKUP_FILE_RE.search(entry):
                    continue
                full_entry = os.path.join(abs_path, entry)
                if not os.path.isfile(full_entry):
                    continue
                ext = os.path.splitext(entry)[1].lower()
                if ext not in SUPPORTED_EXTENSIONS:
                    continue
                norm = os.path.normcase(os.path.abspath(full_entry))
                if norm not in seen:
                    seen.add(norm)
                    collected.append(os.path.abspath(full_entry))
        else:
            raise FileNotFoundError(f"Input path does not exist: '{abs_path}'.")

    return collected


def _select_preset_source(candidates, target_version, force, inspect_fn):
    """
    Pick the best source model for a preset family targeting `target_version`:
      1. Prefer `<Family><target_version - 1>.rvt` when its format < target_version.
      2. Fall back to the newest earlier local copy (format < target_version).
      3. When `force` is True and no earlier copy exists, allow format == target_version.
    Returns (source_path, source_format) or (None, None).
    """
    scored = []
    for path in candidates:
        stem = os.path.splitext(os.path.basename(path))[0]
        name_year = extract_trailing_year(stem)
        file_format = inspect_fn(path)
        effective_year = file_format if file_format is not None else name_year
        if effective_year is None:
            continue
        is_prev_year = name_year == (target_version - 1)
        has_explicit_year = name_year is not None
        scored.append((path, effective_year, is_prev_year, has_explicit_year))

    earlier = [item for item in scored if item[1] < target_version]
    if earlier:
        earlier.sort(
            key=lambda item: (
                1 if item[2] else 0,
                item[1],
                1 if item[3] else 0,
                item[0],
            ),
            reverse=True,
        )
        return earlier[0][0], earlier[0][1]

    if force:
        same_year = [item for item in scored if item[1] == target_version]
        if same_year:
            same_year.sort(key=lambda item: (1 if item[3] else 0, item[0]), reverse=True)
            return same_year[0][0], same_year[0][1]

    return None, None


def _plan_preset_family(
    family_label,
    candidates,
    target_versions,
    default_output_dir,
    output_dir,
    force,
    audit,
    min_loadable_families,
    using_all_versions,
    inspect_fn,
    plan,
    required=True,
):
    if not candidates:
        if required:
            raise FileNotFoundError(
                f"Required preset model family '{family_label}' not found under '{default_output_dir}'."
            )
        plan.notes.append(
            f"[INFO] Local studio model '{family_label}' not found in '{default_output_dir}'; "
            "skipping absent local studio model."
        )
        return

    dest_dir = os.path.abspath(output_dir) if output_dir else default_output_dir

    for target_ver in target_versions:
        target_path = os.path.join(dest_dir, f"{family_label}{target_ver}.rvt")
        if not force and os.path.isfile(target_path):
            existing_format = inspect_fn(target_path)
            if existing_format == target_ver:
                plan.skipped.append(
                    SkippedTarget(
                        source_path=target_path,
                        target_path=target_path,
                        target_version=target_ver,
                        reason=f"already up-to-date (Format {existing_format})",
                        preset_group=family_label,
                    )
                )
                continue

        source_path, _ = _select_preset_source(candidates, target_ver, force, inspect_fn)
        if source_path is None:
            if using_all_versions:
                continue
            raise ValueError(
                f"No eligible source model for '{family_label}' to upgrade to Revit {target_ver} "
                f"in '{default_output_dir}'."
            )

        plan.jobs.append(
            UpgradeJob(
                source_path=source_path,
                target_path=target_path,
                target_version=target_ver,
                audit=audit,
                min_loadable_families=min_loadable_families,
                preset_group=family_label,
            )
        )


def plan_upgrade_jobs(
    inputs=None,
    versions=None,
    upgrade_all=False,
    force=False,
    audit=False,
    preset=None,
    output_dir=None,
    repo_root=REPO_ROOT,
    installed_versions=None,
    inspect_fn=inspect_model_format_year,
):
    """Build an UpgradePlan containing scheduled UpgradeJobs, SkippedTargets, and informational notes."""
    if not inputs and not preset:
        raise ValueError("Provide at least one input path (.rvt/.rte/.rfa) or --preset test-models.")
    if upgrade_all and versions:
        raise ValueError("--versions and --all cannot be combined.")
    if not upgrade_all and not versions:
        raise ValueError("Specify target Revit versions with --versions or --all.")

    if upgrade_all:
        available_versions = (
            sorted(set(int(v) for v in installed_versions))
            if installed_versions is not None
            else discover_installed_revit_versions()
        )
        if not available_versions:
            raise RuntimeError("No installed Autodesk Revit versions discovered for --all.")
        requested_versions = available_versions
    else:
        requested_versions = parse_versions_arg(versions)

    plan = UpgradePlan()

    if inputs:
        model_files = collect_input_model_files(inputs)
        if not model_files:
            raise FileNotFoundError("No .rvt, .rte, or .rfa files found in the provided input paths.")

        for source_path in model_files:
            source_format = inspect_fn(source_path)
            if upgrade_all:
                if source_format is None:
                    raise ValueError(
                        f"Could not determine BasicFileInfo Format for '{source_path}' required by --all."
                    )
                target_vers = [v for v in requested_versions if v > source_format]
                if not target_vers:
                    plan.skipped.append(
                        SkippedTarget(
                            source_path=source_path,
                            target_path=source_path,
                            target_version=source_format,
                            reason=f"no installed Revit version newer than source Format {source_format}",
                        )
                    )
                    continue
            else:
                target_vers = requested_versions

            stem_no_year = strip_trailing_year(os.path.splitext(os.path.basename(source_path))[0])
            min_families = (
                TEST_TEMPLATE_MIN_LOADABLE_FAMILIES
                if stem_no_year.casefold() == "testtemplate"
                else 0
            )

            for target_ver in target_vers:
                if source_format is not None and source_format > target_ver:
                    raise ValueError(
                        f"Cannot downgrade '{source_path}' (Format {source_format}) to Revit {target_ver}."
                    )

                target_path = compute_output_path(source_path, target_ver, output_dir=output_dir)
                if not force and os.path.isfile(target_path):
                    existing_format = inspect_fn(target_path)
                    if existing_format == target_ver:
                        plan.skipped.append(
                            SkippedTarget(
                                source_path=source_path,
                                target_path=target_path,
                                target_version=target_ver,
                                reason=f"already up-to-date (Format {existing_format})",
                            )
                        )
                        continue

                plan.jobs.append(
                    UpgradeJob(
                        source_path=source_path,
                        target_path=target_path,
                        target_version=target_ver,
                        audit=audit,
                        min_loadable_families=min_families,
                    )
                )

    if preset:
        if preset != "test-models":
            raise ValueError(f"Unsupported preset '{preset}'.")

        test_models_dir = os.path.join(os.path.abspath(repo_root), "tests", "Assets", "test_models")
        if not os.path.isdir(test_models_dir):
            raise FileNotFoundError(f"Test models directory not found: '{test_models_dir}'.")

        test_template_candidates = []
        inc_standards_candidates = []
        for entry in sorted(os.listdir(test_models_dir)):
            full_entry = os.path.join(test_models_dir, entry)
            if not os.path.isfile(full_entry):
                continue
            if TEST_TEMPLATE_FILE_RE.match(entry):
                test_template_candidates.append(full_entry)
            elif INC_STANDARDS_FILE_RE.match(entry):
                inc_standards_candidates.append(full_entry)

        _plan_preset_family(
            family_label="TestTemplate",
            candidates=test_template_candidates,
            target_versions=requested_versions,
            default_output_dir=test_models_dir,
            output_dir=output_dir,
            force=force,
            audit=audit,
            min_loadable_families=TEST_TEMPLATE_MIN_LOADABLE_FAMILIES,
            using_all_versions=upgrade_all,
            inspect_fn=inspect_fn,
            plan=plan,
            required=True,
        )
        _plan_preset_family(
            family_label="INC Standards - Assemblies & Details",
            candidates=inc_standards_candidates,
            target_versions=requested_versions,
            default_output_dir=test_models_dir,
            output_dir=output_dir,
            force=force,
            audit=audit,
            min_loadable_families=0,
            using_all_versions=upgrade_all,
            inspect_fn=inspect_fn,
            plan=plan,
            required=False,
        )

    return plan


def atomic_stage_file(staging_path, target_path):
    """
    Atomically move or replace `target_path` using verified `staging_path` on disk.
    Falls back to copying into a sibling temp file when staging resides on a different volume.
    """
    abs_staging = os.path.abspath(staging_path)
    abs_target = os.path.abspath(target_path)
    if not os.path.isfile(abs_staging):
        raise FileNotFoundError(f"Verified staging file not found: '{abs_staging}'.")

    target_dir = os.path.dirname(abs_target)
    os.makedirs(target_dir, exist_ok=True)

    staging_drive = os.path.splitdrive(abs_staging)[0].casefold()
    target_drive = os.path.splitdrive(abs_target)[0].casefold()
    if staging_drive != target_drive:
        sibling_temp = os.path.join(
            target_dir, f".{os.path.basename(abs_target)}.staging_{os.getpid()}"
        )
        try:
            shutil.copy2(abs_staging, sibling_temp)
            os.replace(sibling_temp, abs_target)
        finally:
            if os.path.exists(sibling_temp):
                try:
                    os.remove(sibling_temp)
                except OSError:
                    pass
        return abs_target

    os.replace(abs_staging, abs_target)
    return abs_target


def format_upgrade_report_text(plan, batch_reports):
    """Render a concise human-readable upgrade report including warnings, errors, and skips."""
    lines = ["# RevitModelUpgrader Summary", ""]

    for note in plan.notes:
        lines.append(note)
    if plan.notes:
        lines.append("")

    if plan.skipped:
        lines.append(f"## Skipped ({len(plan.skipped)})")
        for s in plan.skipped:
            lines.append(
                f"- [SKIP] Revit {s.target_version}: `{s.target_path}` ({s.reason})"
            )
        lines.append("")

    for batch in batch_reports:
        ver = batch.get("TargetVersion", "")
        journal_clean = batch.get("JournalClean", False)
        jobs = batch.get("Jobs", [])
        lines.append(
            f"## Revit {ver} Batch (Jobs: {len(jobs)}, JournalClean: {journal_clean})"
        )
        for job in jobs:
            status = "PASS" if job.get("Success") else "FAIL"
            src = job.get("SourcePath", "")
            dst = job.get("TargetPath", "")
            src_fmt = job.get("SourceFormat", "")
            ver_fmt = job.get("VerifiedFormat", "")
            fam_count = job.get("LoadableFamilyCount")
            fam_part = f", LoadableFamilies={fam_count}" if fam_count is not None else ""
            lines.append(
                f"- [{status}] `{src}` -> `{dst}` "
                f"(Format {src_fmt} -> {ver_fmt}, Workshared={job.get('WasWorkshared')}, "
                f"Warnings={job.get('WarningCount', 0)}, Errors={job.get('ErrorCount', 0)}, "
                f"Dialogs={len(job.get('SuppressedDialogs', []))}{fam_part})"
            )
            if job.get("ErrorMessage"):
                lines.append(f"  Error: {job['ErrorMessage']}")
            for dlg in job.get("SuppressedDialogs", []):
                lines.append(
                    f"  [Dialog] {dlg.get('DialogId', '')} (override={dlg.get('OverrideResultCode')}, "
                    f"accepted={dlg.get('OverrideAccepted')}): {dlg.get('Message', '')}"
                )
            for failure in job.get("Failures", []):
                lines.append(
                    f"  [{failure.get('Severity', 'Failure')}] ({failure.get('ResolutionAction', '')}) "
                    f"{failure.get('Description', '')}"
                )
        lines.append("")

    return "\n".join(lines).rstrip() + "\n"


def execute_version_batch(
    target_version,
    jobs,
    repo_root=REPO_ROOT,
    upgrader_csproj=UPGRADER_CSPROJ,
    timeout=DEFAULT_BATCH_TIMEOUT_SECONDS,
    installed_versions=None,
    configure_dotnet_fn=te.configure_dotnet_environment,
    resolve_dotnet_fn=te.resolve_dotnet_exe,
    whitelist_fn=guid_whitelister.whitelist_existing_addins,
    restore_addins_cls=te.TempDisableThirdPartyAddins,
    legacy_manifest_fn=te.check_legacy_programdata_manifest,
    bundle_lease_cls=te.RicaunBundleLease,
    snapshot_journals_fn=te.snapshot_known_journals,
    run_gate_fn=te.run_journal_cleanliness_gate,
    startup_gate_exec_fn=te.execute_revit_test_with_startup_gate,
    subprocess_run_fn=subprocess.run,
    cleanup_temp_fn=te.cleanup_revit_test_temp,
    temp_root=None,
):
    """Build and execute one Revit version's upgrade batch with bundle lease and --assert-clean."""
    ver_str = str(target_version)
    available_versions = (
        [int(v) for v in installed_versions]
        if installed_versions is not None
        else discover_installed_revit_versions()
    )
    if int(target_version) not in available_versions:
        raise RuntimeError(
            f"Revit {ver_str} is not installed (discovered: {available_versions})."
        )

    sdk_info = configure_dotnet_fn()
    dotnet_exe = resolve_dotnet_fn(sdk_info)

    run_id = te.generate_run_id()
    base_temp = temp_root or tempfile.gettempdir()
    run_work_dir = os.path.join(base_temp, "SyntheticModelUpgraderRuns", run_id)
    run_build_root = os.path.join(base_temp, "SyntheticModelUpgraderBuilds", run_id)
    os.makedirs(run_work_dir, exist_ok=True)
    os.makedirs(run_build_root, exist_ok=True)

    manifest_path = os.path.join(run_work_dir, f"job_manifest_{ver_str}.json")
    report_path = os.path.join(run_work_dir, f"report_{ver_str}.json")
    snapshot_path = os.path.join(run_work_dir, f"known_journals_{ver_str}.json")
    out_log_path = os.path.join(run_work_dir, f"output_{ver_str}.log")

    manifest_payload = {
        "TargetVersion": ver_str,
        "ReportPath": report_path,
        "Jobs": [
            {
                "SourcePath": job.source_path,
                "TargetPath": job.target_path,
                "Audit": bool(job.audit),
                "MinLoadableFamilies": int(job.min_loadable_families),
            }
            for job in jobs
        ],
    }
    with open(manifest_path, "w", encoding="utf-8") as f:
        json.dump(manifest_payload, f, indent=2)

    print(
        f"[UPGRADER] Building RevitModelUpgrader for Revit {ver_str} ({run_id}): {dotnet_exe}..."
    )
    build_cmd = [
        dotnet_exe,
        "build",
        upgrader_csproj,
        "-c",
        "Debug",
        "-p:Platform=x64",
        f"-p:RevitVersion={ver_str}",
        f"-p:UpgraderBuildRoot={run_build_root}",
        "-v:minimal",
        "-clp:ErrorsOnly;Summary",
    ]
    subprocess_run_fn(build_cmd, check=True)

    try:
        restore_addins_cls(ver_str).restore_addins()
    except OSError as exc:
        print(f"[WARN] Failed restoring legacy AddInsSettings backup for Revit {ver_str}: {exc}")

    whitelist_fn(ver_str)
    legacy_manifest_fn(ver_str)
    snapshot_journals_fn(ver_str, snapshot_path)

    worker_env = te.build_worker_env(os.path.abspath(repo_root), run_id)
    worker_env["SYNTHETIC_UPGRADER_MANIFEST"] = manifest_path
    isolated_out_dir = os.path.join(run_build_root, "bin", ver_str)

    test_cmd = [
        dotnet_exe,
        "test",
        upgrader_csproj,
        "--no-build",
        "--logger",
        f"trx;LogFileName=results_{ver_str}.trx",
        "--results-directory",
        run_work_dir,
        "-p:Configuration=Debug",
        "-p:Platform=x64",
        f"-p:RevitVersion={ver_str}",
        f"-p:UpgraderBuildRoot={run_build_root}",
    ]

    print(
        f"[UPGRADER] Running {len(jobs)} upgrade job(s) in Revit {ver_str} (timeout: {timeout}s)..."
    )
    with open(out_log_path, "w", encoding="utf-8") as out_f:
        with bundle_lease_cls(testadapter_search_dirs=[isolated_out_dir]):
            proc_rc, timed_out, effective_run_id, modal_aborted = startup_gate_exec_fn(
                test_cmd,
                out_f,
                worker_env,
                version=ver_str,
                run_id=run_id,
                snapshot_path=snapshot_path,
                timeout=timeout,
            )

    journal_clean, gate_output = run_gate_fn(ver_str, effective_run_id, snapshot_path)
    if gate_output:
        print(gate_output)

    report_data = {
        "TargetVersion": ver_str,
        "ExecutedAtUtc": "",
        "Jobs": [],
    }
    if os.path.isfile(report_path):
        with open(report_path, "r", encoding="utf-8") as f:
            report_data = json.load(f)

    report_data["JournalClean"] = bool(journal_clean)
    report_data["JournalGateOutput"] = gate_output or ""
    report_data["ProcessReturnCode"] = int(proc_rc)
    report_data["TimedOut"] = bool(timed_out)
    report_data["ModalAborted"] = bool(modal_aborted)
    report_data["RunId"] = effective_run_id
    report_data["LogPath"] = out_log_path

    all_jobs_succeeded = (
        bool(report_data.get("Jobs"))
        and len(report_data["Jobs"]) == len(jobs)
        and all(bool(j.get("Success")) for j in report_data["Jobs"])
    )
    batch_ok = (
        proc_rc == 0
        and not timed_out
        and not modal_aborted
        and journal_clean
        and all_jobs_succeeded
    )

    if batch_ok:
        shutil.rmtree(run_build_root, ignore_errors=True)
        cleanup_temp_fn(run_id)
    else:
        print(
            f"[UPGRADER] Preserving failed build/work directories for inspection: "
            f"{run_build_root}, {run_work_dir}"
        )

    return batch_ok, report_data


def run_upgrader(
    inputs=None,
    versions=None,
    upgrade_all=False,
    force=False,
    audit=False,
    preset=None,
    output_dir=None,
    report_path=None,
    timeout=DEFAULT_BATCH_TIMEOUT_SECONDS,
    dry_run=False,
    repo_root=REPO_ROOT,
    installed_versions=None,
    inspect_fn=inspect_model_format_year,
    batch_executor=execute_version_batch,
):
    """Plan and execute multi-version model upgrades, returning (exit_code, summary_dict)."""
    resolved_installed = (
        sorted(set(int(v) for v in installed_versions))
        if installed_versions is not None
        else discover_installed_revit_versions()
    )
    plan = plan_upgrade_jobs(
        inputs=inputs,
        versions=versions,
        upgrade_all=upgrade_all,
        force=force,
        audit=audit,
        preset=preset,
        output_dir=output_dir,
        repo_root=repo_root,
        installed_versions=resolved_installed,
        inspect_fn=inspect_fn,
    )

    if dry_run or not plan.jobs:
        summary = {
            "DryRun": bool(dry_run),
            "PlannedJobs": [
                {
                    "SourcePath": j.source_path,
                    "TargetPath": j.target_path,
                    "TargetVersion": j.target_version,
                    "Audit": j.audit,
                    "MinLoadableFamilies": j.min_loadable_families,
                }
                for j in plan.jobs
            ],
            "Skipped": [
                {
                    "SourcePath": s.source_path,
                    "TargetPath": s.target_path,
                    "TargetVersion": s.target_version,
                    "Reason": s.reason,
                }
                for s in plan.skipped
            ],
            "Notes": list(plan.notes),
            "Batches": [],
        }
        print(format_upgrade_report_text(plan, []))
        if dry_run and plan.jobs:
            print(f"[DRY-RUN] Planned {len(plan.jobs)} upgrade job(s):")
            for j in plan.jobs:
                print(
                    f"  - Revit {j.target_version}: {j.source_path} -> {j.target_path} "
                    f"(audit={j.audit}, min_families={j.min_loadable_families})"
                )
        if report_path:
            os.makedirs(os.path.dirname(os.path.abspath(report_path)), exist_ok=True)
            with open(report_path, "w", encoding="utf-8") as f:
                json.dump(summary, f, indent=2)
        return 0, summary

    jobs_by_version = {}
    for job in plan.jobs:
        jobs_by_version.setdefault(job.target_version, []).append(job)

    batch_reports = []
    all_ok = True
    for target_ver in sorted(jobs_by_version.keys()):
        batch_ok, batch_data = batch_executor(
            target_ver,
            jobs_by_version[target_ver],
            repo_root=repo_root,
            timeout=timeout,
            installed_versions=resolved_installed,
        )
        batch_reports.append(batch_data)
        if not batch_ok:
            all_ok = False

    print("")
    print(format_upgrade_report_text(plan, batch_reports))

    summary = {
        "DryRun": False,
        "Skipped": [
            {
                "SourcePath": s.source_path,
                "TargetPath": s.target_path,
                "TargetVersion": s.target_version,
                "Reason": s.reason,
            }
            for s in plan.skipped
        ],
        "Notes": list(plan.notes),
        "Batches": batch_reports,
    }
    if report_path:
        os.makedirs(os.path.dirname(os.path.abspath(report_path)), exist_ok=True)
        with open(report_path, "w", encoding="utf-8") as f:
            json.dump(summary, f, indent=2)

    return (0 if all_ok else 1), summary


def parse_cli_args(argv=None):
    """Parse CLI arguments for revit_model_upgrader.py."""
    parser = argparse.ArgumentParser(
        description="Standalone multi-version Autodesk Revit model/template/family batch upgrader."
    )
    parser.add_argument(
        "inputs",
        nargs="*",
        help="Input .rvt, .rte, or .rfa files or directories.",
    )
    parser.add_argument(
        "--versions",
        nargs="+",
        default=None,
        help="Target Revit version(s), e.g. --versions 2026 2027 or --versions 2026,2027.",
    )
    parser.add_argument(
        "--all",
        dest="upgrade_all",
        action="store_true",
        default=False,
        help="Upgrade to all installed Revit versions newer than each source file's format.",
    )
    parser.add_argument(
        "--force",
        action="store_true",
        default=False,
        help="Re-upgrade and overwrite targets that are already up to date.",
    )
    parser.add_argument(
        "--audit",
        action="store_true",
        default=False,
        help="Enable OpenOptions.Audit when opening source files.",
    )
    parser.add_argument(
        "--preset",
        choices=("test-models",),
        default=None,
        help="Upgrade tracked TestTemplate models and local INC Standards models when present.",
    )
    parser.add_argument(
        "--output-dir",
        default=None,
        help="Optional output directory for upgraded files (defaults to each source file's directory).",
    )
    parser.add_argument(
        "--report",
        default=None,
        help="Optional path to write the full JSON upgrade & warning report.",
    )
    parser.add_argument(
        "--timeout",
        type=int,
        default=DEFAULT_BATCH_TIMEOUT_SECONDS,
        help=f"Per-version Revit batch execution timeout in seconds (default: {DEFAULT_BATCH_TIMEOUT_SECONDS}).",
    )
    parser.add_argument(
        "--dry-run",
        action="store_true",
        default=False,
        help="Print planned upgrade jobs and skips without launching Revit.",
    )

    args = parser.parse_args(argv)
    if not args.inputs and not args.preset:
        parser.error("Provide at least one input path or --preset test-models.")
    if args.upgrade_all and args.versions:
        parser.error("--versions and --all are mutually exclusive.")
    if not args.upgrade_all and not args.versions:
        parser.error("Specify target version(s) via --versions or --all.")
    if args.timeout < 60:
        parser.error("--timeout must be >= 60 seconds.")
    return args


def main(argv=None):
    args = parse_cli_args(argv)
    try:
        exit_code, _ = run_upgrader(
            inputs=args.inputs,
            versions=args.versions,
            upgrade_all=args.upgrade_all,
            force=args.force,
            audit=args.audit,
            preset=args.preset,
            output_dir=args.output_dir,
            report_path=args.report,
            timeout=args.timeout,
            dry_run=args.dry_run,
        )
    except Exception as exc:
        print(f"[ERROR] {exc}", file=sys.stderr)
        return 1
    return exit_code


if __name__ == "__main__":
    sys.exit(main())
