import os
import sys
import argparse
import json

# Ensure sibling scripts (aggregate.py, drive_sync.py) are importable regardless of CWD
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from aggregate import process_config, resolve_config_path, get_default_paths
from drive_sync import sync_to_drive


def main():
    _, default_config = get_default_paths()

    parser = argparse.ArgumentParser(
        description="Configuration-driven codebase aggregation and Google Drive sync runner."
    )
    parser.add_argument(
        "--config",
        dest="config_path",
        default=default_config,
        help=f"Path to the configuration JSON file. Defaults to: {default_config}"
    )
    parser.add_argument(
        "--extensions",
        dest="extensions",
        nargs="+",
        default=[".cs", ".xaml", ".md"],
        help="Allowed file extensions. Defaults to .cs .xaml .md"
    )

    args = parser.parse_args()

    # Resolve projects_dir from the workspace root anchor
    projects_dir, _ = get_default_paths()

    print("="*60)
    print("PHASE 1: Aggregating configured source directories...")
    print("="*60)
    try:
        file_count = process_config(args.config_path, args.extensions)
        print(f"Aggregation complete. {file_count} files bundled.")
    except Exception as e:
        print(f"Error during aggregation phase: {e}", file=sys.stderr)
        sys.exit(1)

    print("\n" + "="*60)
    print("PHASE 2: Syncing targets to Google Drive...")
    print("="*60)

    try:
        with open(args.config_path, 'r', encoding='utf-8') as f:
            config = json.load(f)
    except Exception as e:
        print(f"Error loading configuration file: {e}", file=sys.stderr)
        sys.exit(1)

    sync_targets = config.get("sync_targets", [])
    if not sync_targets:
        print("Warning: No sync_targets found in configuration.")
        sys.exit(0)

    parent_folder_id = config.get("drive_folder_id")
    if parent_folder_id:
        print(f"Target Google Drive Folder ID: {parent_folder_id}")
    else:
        print("No Drive Folder ID in config — files will upload to root Drive directory.")

    results = {}
    for idx, target in enumerate(sync_targets):
        local_path_raw = target.get("local_path")
        drive_id = target.get("drive_id")

        if not local_path_raw:
            print(f"Warning: Target {idx} is missing local_path, skipping.")
            continue

        local_path = resolve_config_path(local_path_raw, projects_dir)
        target_name = os.path.basename(local_path)

        print(f"\n--- Syncing '{target_name}' ---")
        if not os.path.exists(local_path):
            print(f"Warning: Local file '{local_path}' does not exist — skipping.", file=sys.stderr)
            continue

        try:
            drive_id = sync_to_drive(local_path, drive_id, parent_folder_id)
            results[target_name] = drive_id
        except Exception as e:
            print(f"Error syncing '{local_path}': {e}", file=sys.stderr)
            sys.exit(1)

    print("\n" + "="*60)
    print("ALL SYNC PROCESSES COMPLETE")
    for filename, drive_id in results.items():
        print(f"  - '{filename}' -> Google Drive File ID: {drive_id}")
    print("="*60)


if __name__ == '__main__':
    main()
