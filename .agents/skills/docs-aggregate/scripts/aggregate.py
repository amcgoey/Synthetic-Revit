import os
import argparse
import re
import sys
import json


# Default directories to ignore to prevent clutter and circular reference
DEFAULT_EXCLUDES = {
    '.git', '.vs', 'bin', 'obj', 'packages', 'output', 'scratch', 'build', 'node_modules', 'docs'
}


def find_workspace_root():
    """
    Walk up from this script's directory until a .git marker is found.
    This makes the script location-independent regardless of CWD.
    """
    current = os.path.dirname(os.path.abspath(__file__))
    while True:
        if os.path.exists(os.path.join(current, '.git')):
            return current
        parent = os.path.dirname(current)
        if parent == current:
            raise RuntimeError(
                "Could not locate workspace root. "
                "Ensure this script lives inside the project repository (.git not found)."
            )
        current = parent


def get_code_block_lang(ext):
    """Map file extensions to markdown code block language specifiers."""
    ext = ext.lower()
    if ext == '.cs':
        return 'csharp'
    elif ext in ('.xaml', '.xml', '.csproj', '.projitems', '.shproj', '.addin', '.config'):
        return 'xml'
    elif ext == '.md':
        return 'markdown'
    elif ext == '.py':
        return 'python'
    elif ext == '.json':
        return 'json'
    elif ext in ('.bat', '.cmd'):
        return 'batch'
    elif ext == '.ps1':
        return 'powershell'
    return ''


def read_file_safely(file_path):
    """Attempt to read file contents with common encodings."""
    encodings = ['utf-8', 'utf-8-sig', 'windows-1252', 'latin-1']
    for encoding in encodings:
        try:
            with open(file_path, 'r', encoding=encoding) as f:
                return f.read()
        except UnicodeDecodeError:
            continue
    # Fallback with ignore errors
    try:
        with open(file_path, 'r', encoding='utf-8', errors='ignore') as f:
            return f.read()
    except Exception as e:
        return f"[Error reading file: {str(e)}]"


def get_default_paths():
    """Locate the projects directory and default config path using the workspace root anchor."""
    workspace_dir = find_workspace_root()
    projects_dir = os.path.dirname(workspace_dir)
    config_path = os.path.join(
        projects_dir, "Revit API Synthetic v2 Support", "auth", "aggregate_config.json"
    )
    return projects_dir, config_path


def resolve_config_path(path_str, projects_dir):
    """Resolve paths to absolute. If already absolute, return as-is. Otherwise resolve relative to projects_dir."""
    if os.path.isabs(path_str):
        return path_str
    return os.path.abspath(os.path.join(projects_dir, path_str))


cleared_files = set()

def aggregate_directory_to_file(source_dir, output_file, allowed_extensions):
    """
    Recursively scans source_dir for files matching allowed_extensions
    and writes their full contents (including comments) to output_file.
    """
    source_dir = os.path.abspath(source_dir)
    output_file = os.path.abspath(output_file)
    allowed_extensions = {ext.lower().strip() for ext in allowed_extensions}

    # Ensure output directory exists
    output_directory = os.path.dirname(output_file)
    if output_directory:
        os.makedirs(output_directory, exist_ok=True)

    print(f"Scanning target directory: {source_dir}")
    print(f"Allowed extensions: {', '.join(allowed_extensions)}")
    print(f"Output file: {output_file}")

    file_count = 0
    import time

    # Clear any previous aggregation output
    if output_file not in cleared_files:
        if os.path.exists(output_file):
            for i in range(5):
                try:
                    os.remove(output_file)
                    break
                except Exception:
                    time.sleep(0.5)
            print(f"Cleared existing output file: {os.path.basename(output_file)}")
        cleared_files.add(output_file)
        mode = 'w'
    else:
        mode = 'a'

    out_file = None
    for i in range(5):
        try:
            out_file = open(output_file, mode, encoding='utf-8')
            break
        except Exception as e:
            if i == 4:
                raise e
            time.sleep(0.5)

    with out_file:
        for root, dirs, files in os.walk(source_dir):
            # Prune excluded directories in-place
            dirs[:] = [d for d in dirs if d not in DEFAULT_EXCLUDES]

            for file in files:
                _, ext = os.path.splitext(file)
                if ext.lower() in allowed_extensions:
                    full_path = os.path.join(root, file)

                    # Avoid including the output file in itself
                    if os.path.abspath(full_path) == output_file:
                        continue

                    # Relative path with source directory name prepended
                    rel_path = os.path.relpath(full_path, source_dir)
                    base_dir_name = os.path.basename(source_dir)
                    rel_path_with_base = f"{base_dir_name}/{rel_path.replace(os.path.sep, '/')}"

                    print(f"  Aggregating: {rel_path_with_base}")
                    content = read_file_safely(full_path)

                    lang = get_code_block_lang(ext)
                    out_file.write(f"### File: {rel_path_with_base}\n")
                    out_file.write(f"```{lang}\n")
                    out_file.write(content)
                    if not content.endswith('\n'):
                        out_file.write('\n')
                    out_file.write("```\n\n")

                    file_count += 1

    print(f"Successfully aggregated {file_count} files into {output_file}.\n")
    return file_count


def process_config(config_path, allowed_extensions=None):
    """Parses config_path and runs aggregation for each item in the aggregations array."""
    cleared_files.clear()
    if allowed_extensions is None:
        allowed_extensions = {".cs", ".xaml", ".md"}

    projects_dir, _ = get_default_paths()

    if not os.path.exists(config_path):
        print(f"Error: Config file not found at: {config_path}", file=sys.stderr)
        sys.exit(1)

    try:
        with open(config_path, 'r', encoding='utf-8') as f:
            config = json.load(f)
    except Exception as e:
        print(f"Error parsing config file: {e}", file=sys.stderr)
        sys.exit(1)

    aggregations = config.get("aggregations", [])
    if not aggregations:
        print("Warning: No aggregations found in configuration.")
        return 0

    total_files = 0
    for idx, agg in enumerate(aggregations):
        source_dir_raw = agg.get("source_dir")
        output_file_raw = agg.get("output_file")
        custom_extensions = agg.get("extensions")

        if not source_dir_raw or not output_file_raw:
            print(f"Warning: Item {idx} is missing source_dir or output_file, skipping.")
            continue

        source_dir = resolve_config_path(source_dir_raw, projects_dir)
        output_file = resolve_config_path(output_file_raw, projects_dir)

        if not os.path.exists(source_dir):
            print(f"Error: Target directory '{source_dir}' does not exist, skipping.")
            continue

        exts = custom_extensions if custom_extensions is not None else allowed_extensions
        total_files += aggregate_directory_to_file(source_dir, output_file, exts)

    return total_files


if __name__ == '__main__':
    _, default_config = get_default_paths()

    parser = argparse.ArgumentParser(
        description="Aggregate codebase directories into markdown bundle files, driven by sync_config.json."
    )
    parser.add_argument(
        "--config",
        default=default_config,
        help=f"Path to the configuration JSON file. Defaults to: {default_config}"
    )
    parser.add_argument(
        "--extensions",
        nargs="+",
        default=[".cs", ".xaml", ".md"],
        help="List of allowed file extensions. Defaults to .cs .xaml .md"
    )

    args = parser.parse_args()
    process_config(args.config, args.extensions)
