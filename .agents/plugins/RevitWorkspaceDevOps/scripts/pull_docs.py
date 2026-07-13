import os
import sys
import json
import re
import shutil
import argparse
from google.oauth2.credentials import Credentials
from google_auth_oauthlib.flow import InstalledAppFlow
from google.auth.transport.requests import Request
from googleapiclient.discovery import build
from googleapiclient.errors import HttpError

# Google API scopes required for listing and reading documents
SCOPES = [
    'https://www.googleapis.com/auth/drive.readonly',
    'https://www.googleapis.com/auth/documents.readonly'
]

# Mapping of Google Doc styles to Markdown prefixes
HEADING_MAP = {
    'HEADING_1': '# ',
    'HEADING_2': '## ',
    'HEADING_3': '### ',
    'HEADING_4': '#### ',
    'HEADING_5': '##### ',
    'HEADING_6': '###### ',
    'TITLE': '# ',
    'SUBTITLE': '## '
}


def find_workspace_root():
    """Walk up from this script's directory until a .git marker is found."""
    current = os.path.dirname(os.path.abspath(__file__))
    while True:
        if os.path.exists(os.path.join(current, '.git')):
            return current
        parent = os.path.dirname(current)
        if parent == current:
            return os.path.dirname(os.path.abspath(__file__))
        current = parent


def get_auth_paths():
    """Resolve workspace-relative auth, credentials, and config file paths."""
    workspace_dir = find_workspace_root()
    projects_dir = os.path.dirname(workspace_dir)

    auth_dir = os.path.join(projects_dir, "Revit API Synthetic v2 Support", "auth")
    client_secret_path = os.path.join(auth_dir, "client_secret.json")
    token_path = os.path.join(auth_dir, "token_pull.json")
    default_config_path = os.path.join(auth_dir, "pull_config.json")

    return auth_dir, client_secret_path, token_path, default_config_path, projects_dir


def authenticate(auth_dir, client_secret_path, token_path):
    """Authenticate with Google APIs and return credentials."""
    creds = None
    if os.path.exists(token_path):
        try:
            creds = Credentials.from_authorized_user_file(token_path, SCOPES)
        except Exception as e:
            print(f"Warning: Failed to load existing token_pull.json: {e}. Re-authenticating...", file=sys.stderr)
            creds = None

    if not creds or not creds.valid:
        if creds and creds.expired and creds.refresh_token:
            try:
                creds.refresh(Request())
            except Exception as e:
                print(f"Warning: Failed to refresh token: {e}. Re-authenticating...", file=sys.stderr)
                creds = None

        if not creds:
            if not os.path.exists(client_secret_path):
                print("\n" + "="*80, file=sys.stderr)
                print("ERROR: client_secret.json not found!", file=sys.stderr)
                print("Please place your Google OAuth Client secret JSON file at:", file=sys.stderr)
                print(f"  {client_secret_path}", file=sys.stderr)
                print("="*80 + "\n", file=sys.stderr)
                sys.exit(1)

            print("Launching browser for OAuth authentication (readonly scopes)...", flush=True)
            flow = InstalledAppFlow.from_client_secrets_file(client_secret_path, SCOPES)
            creds = flow.run_local_server(port=0)

        os.makedirs(auth_dir, exist_ok=True)
        with open(token_path, 'w') as token_file:
            token_file.write(creds.to_json())
            print(f"Saved authentication token to: {token_path}")

    return creds


def validate_safe_path(target_path, allowed_parent):
    """Ensure the target path resolves inside the allowed parent boundary to prevent accidental deletions."""
    target_abs = os.path.abspath(target_path)
    parent_abs = os.path.abspath(allowed_parent)
    if not target_abs.lower().startswith(parent_abs.lower()):
        raise ValueError(
            f"Path validation failed: '{target_abs}' is outside the allowed workspace boundary '{parent_abs}'"
        )


def clear_local_directory(local_dir):
    """Safely clear all files and folders inside the target directory."""
    if os.path.exists(local_dir):
        print(f"Clearing local folder: '{local_dir}'...")
        for item in os.listdir(local_dir):
            item_path = os.path.join(local_dir, item)
            try:
                if os.path.isdir(item_path):
                    shutil.rmtree(item_path)
                else:
                    os.remove(item_path)
            except Exception as e:
                print(f"Warning: Failed to delete '{item_path}': {e}", file=sys.stderr)
    else:
        print(f"Creating local folder: '{local_dir}'...")
        os.makedirs(local_dir, exist_ok=True)


def list_drive_docs(drive_service, folder_id, include_pat=None, exclude_pat=None):
    """List Google Docs directly inside the root of the specified folder."""
    query = f"'{folder_id}' in parents and mimeType='application/vnd.google-apps.document' and trashed=false"
    
    files = []
    page_token = None
    while True:
        response = drive_service.files().list(
            q=query,
            spaces='drive',
            fields='nextPageToken, files(id, name)',
            pageToken=page_token
        ).execute()
        files.extend(response.get('files', []))
        page_token = response.get('nextPageToken', None)
        if not page_token:
            break

    # Apply include/exclude regular expression filters if configured
    filtered = []
    for f in files:
        name = f['name']
        if include_pat:
            if not re.search(include_pat, name, re.IGNORECASE):
                continue
        if exclude_pat:
            if re.search(exclude_pat, name, re.IGNORECASE):
                continue
        filtered.append(f)
        
    return filtered


def get_doc_content(docs_service, doc_id):
    """Retrieve Google Doc content JSON using the Docs API."""
    return docs_service.documents().get(documentId=doc_id).execute()


def format_text_run(text, style):
    """Apply markdown formatting runs (bold, italic, strike, link) while keeping spaces and newlines correct."""
    if not text:
        return ""
    if not style:
        return text

    # Extract trailing newline so formatting markers stay outside it
    has_newline = text.endswith('\n')
    clean_text = text[:-1] if has_newline else text

    # Preserve leading/trailing spaces outside formatting markers
    stripped_text = clean_text.strip()
    if not stripped_text:
        return text

    leading_space = clean_text[:len(clean_text) - len(clean_text.lstrip())]
    trailing_space = clean_text[len(clean_text.rstrip()):]

    formatted = stripped_text

    # Inner-most formatting: Link
    if 'link' in style and 'url' in style['link']:
        url = style['link']['url']
        formatted = f"[{formatted}]({url})"

    # Strikethrough
    if style.get('strikethrough'):
        formatted = f"~~{formatted}~~"

    # Italic
    if style.get('italic'):
        formatted = f"*{formatted}*"

    # Bold
    if style.get('bold'):
        formatted = f"**{formatted}**"

    result = leading_space + formatted + trailing_space
    if has_newline:
        result += '\n'
    return result


def parse_table(table, lists, doc_content):
    """Parse a Google Doc table element into GFM markdown table."""
    rows = table.get('tableRows', [])
    if not rows:
        return ""

    markdown_rows = []
    max_cols = 0

    for row in rows:
        cells = row.get('tableCells', [])
        max_cols = max(max_cols, len(cells))
        cell_texts = []
        for cell in cells:
            cell_content = cell.get('content', [])
            cell_markdown = parse_cell_content(cell_content, lists, doc_content)
            # Table cell contents must be single line, replace newline with html break
            cell_clean = cell_markdown.strip().replace('\n', '<br>')
            cell_clean = cell_clean.replace('|', '\\|')
            cell_texts.append(cell_clean)
        markdown_rows.append(cell_texts)

    if max_cols == 0:
        return ""

    lines = []
    # Header row
    header_row = markdown_rows[0] if markdown_rows else []
    header_row += [""] * (max_cols - len(header_row))
    lines.append("| " + " | ".join(header_row) + " |")

    # Column delimiter
    delimiter = ["---"] * max_cols
    lines.append("| " + " | ".join(delimiter) + " |")

    # Data rows
    for row_cells in markdown_rows[1:]:
        row_cells += [""] * (max_cols - len(row_cells))
        lines.append("| " + " | ".join(row_cells) + " |")

    return "\n".join(lines)


def parse_cell_content(content, lists, doc_content):
    """Parse structural content blocks within table cells."""
    markdown_lines = []
    for element in content:
        if 'paragraph' in element:
            para = element['paragraph']
            para_style = para.get('paragraphStyle', {})
            style_type = para_style.get('namedStyleType', 'NORMAL_TEXT')
            text_runs = para.get('elements', [])
            para_text = ""
            for run_el in text_runs:
                if 'textRun' in run_el:
                    run = run_el['textRun']
                    text = run.get('content', '')
                    style = run.get('textStyle', {})
                    para_text += format_text_run(text, style)
                elif 'inlineObjectElement' in run_el:
                    obj_id = run_el['inlineObjectElement'].get('inlineObjectId')
                    if obj_id and 'inlineObjects' in doc_content and obj_id in doc_content['inlineObjects']:
                        obj = doc_content['inlineObjects'][obj_id]
                        emb = obj.get('inlineObjectProperties', {}).get('embeddedObject', {})
                        if 'imageProperties' in emb:
                            uri = emb['imageProperties'].get('contentUri', '')
                            title = emb.get('title', '')
                            desc = emb.get('description', '')
                            alt = title or desc or 'image'
                            para_text += f"![{alt}]({uri})"

            bullet = para.get('bullet')
            if bullet:
                list_id = bullet.get('listId')
                nesting_level = bullet.get('nestingLevel', 0)
                is_ordered = False
                if list_id in lists:
                    list_props = lists[list_id].get('listProperties', {})
                    nesting_levels = list_props.get('nestingLevels', [])
                    if nesting_level < len(nesting_levels):
                        glyph_type = nesting_levels[nesting_level].get('glyphType')
                        if glyph_type and glyph_type != 'GLYPH_TYPE_UNSPECIFIED':
                            is_ordered = True

                indent = "  " * nesting_level
                prefix = "1. " if is_ordered else "- "
                clean_text = para_text.rstrip('\n')
                markdown_lines.append(f"{indent}{prefix}{clean_text}\n")
            else:
                clean_text = para_text.strip('\n')
                if style_type.startswith('HEADING') or style_type == 'TITLE':
                    markdown_lines.append(f"**{clean_text}**\n")
                else:
                    markdown_lines.append(para_text)
        elif 'table' in element:
            # Avoid nesting tables inside markdown table cell
            pass
    return "".join(markdown_lines)


def get_split_level(split_by):
    """Resolve splitting header keyword into Google Doc namedStyle identifier."""
    if not split_by:
        return None
    val = split_by.strip().upper()
    mapping = {
        'H1': 'HEADING_1',
        'HEADING_1': 'HEADING_1',
        'H2': 'HEADING_2',
        'HEADING_2': 'HEADING_2',
        'H3': 'HEADING_3',
        'HEADING_3': 'HEADING_3',
        'H4': 'HEADING_4',
        'HEADING_4': 'HEADING_4',
        'H5': 'HEADING_5',
        'HEADING_5': 'HEADING_5',
        'H6': 'HEADING_6',
        'HEADING_6': 'HEADING_6'
    }
    return mapping.get(val)


def parse_and_split_doc(doc_content, split_level=None):
    """Parse Google Doc JSON into a list of (section_title, section_markdown)."""
    body = doc_content.get('body', {})
    content = body.get('content', [])
    lists = doc_content.get('lists', {})

    sections = []
    current_title = ""
    current_lines = []

    for element in content:
        if 'paragraph' in element:
            para = element['paragraph']
            para_style = para.get('paragraphStyle', {})
            style_type = para_style.get('namedStyleType', 'NORMAL_TEXT')
            text_runs = para.get('elements', [])
            para_text = ""
            for run_el in text_runs:
                if 'textRun' in run_el:
                    run = run_el['textRun']
                    text = run.get('content', '')
                    style = run.get('textStyle', {})
                    para_text += format_text_run(text, style)
                elif 'inlineObjectElement' in run_el:
                    obj_id = run_el['inlineObjectElement'].get('inlineObjectId')
                    if obj_id and 'inlineObjects' in doc_content and obj_id in doc_content['inlineObjects']:
                        obj = doc_content['inlineObjects'][obj_id]
                        emb = obj.get('inlineObjectProperties', {}).get('embeddedObject', {})
                        if 'imageProperties' in emb:
                            uri = emb['imageProperties'].get('contentUri', '')
                            title = emb.get('title', '')
                            desc = emb.get('description', '')
                            alt = title or desc or 'image'
                            para_text += f"![{alt}]({uri})"

            bullet = para.get('bullet')
            if bullet:
                list_id = bullet.get('listId')
                nesting_level = bullet.get('nestingLevel', 0)
                is_ordered = False
                if list_id in lists:
                    list_props = lists[list_id].get('listProperties', {})
                    nesting_levels = list_props.get('nestingLevels', [])
                    if nesting_level < len(nesting_levels):
                        glyph_type = nesting_levels[nesting_level].get('glyphType')
                        if glyph_type and glyph_type != 'GLYPH_TYPE_UNSPECIFIED':
                            is_ordered = True

                indent = "  " * nesting_level
                prefix = "1. " if is_ordered else "- "
                clean_text = para_text.rstrip('\n')
                current_lines.append(f"{indent}{prefix}{clean_text}\n")
            else:
                header_prefix = HEADING_MAP.get(style_type, "")
                if header_prefix:
                    clean_text = para_text.strip('\n')
                    if clean_text:
                        if split_level and style_type == split_level:
                            # Save previous section if it contains elements
                            sections.append((current_title, "".join(current_lines)))
                            # Start new section
                            current_title = clean_text
                            current_lines = [f"{header_prefix}{clean_text}\n\n"]
                        else:
                            current_lines.append(f"{header_prefix}{clean_text}\n\n")
                else:
                    if para_text == '\n':
                        current_lines.append('\n')
                    else:
                        current_lines.append(para_text)

        elif 'table' in element:
            table = element['table']
            table_markdown = parse_table(table, lists, doc_content)
            current_lines.append(table_markdown + "\n\n")

    # Save trailing section
    sections.append((current_title, "".join(current_lines)))
    return sections


def sanitize_filename(name):
    """Sanitize string to be filename safe on Windows."""
    name = re.sub(r'[\\/:*?"<>|]', "", name)
    return name.strip()


def save_sections(original_doc_name, sections, local_dir):
    """Save parsed document sections to the target local folder using safe naming schemes."""
    # Filter out empty sections
    sections = [(title, content) for title, content in sections if content.strip()]
    if not sections:
        print(f"Warning: Document '{original_doc_name}' has no content - skipping.")
        return

    # If document has only 1 section and it has no title, save directly as {original_doc_name}.md
    if len(sections) == 1 and not sections[0][0]:
        filename = f"{original_doc_name}.md"
        filepath = os.path.join(local_dir, sanitize_filename(filename))
        print(f"  -> Writing '{filename}'...")
        with open(filepath, 'w', encoding='utf-8') as f:
            f.write(sections[0][1])
        return

    # Determine if there is introductory content preceding the first split header
    has_intro = not sections[0][0]

    for idx, (title, content) in enumerate(sections):
        if has_intro:
            if idx == 0:
                filename = f"{original_doc_name} - 00 - Intro.md"
            else:
                filename = f"{original_doc_name} - {idx:02d} - {title}.md"
        else:
            filename = f"{original_doc_name} - {idx+1:02d} - {title}.md"

        filepath = os.path.join(local_dir, sanitize_filename(filename))
        print(f"  -> Writing '{filename}'...")
        with open(filepath, 'w', encoding='utf-8') as f:
            f.write(content)


def main():
    auth_dir, client_secret_path, token_path, default_config_path, projects_dir = get_auth_paths()

    parser = argparse.ArgumentParser(
        description="Pulls Google Docs from Google Drive and exports them as local Markdown files with section splitting."
    )
    parser.add_argument(
        "--config",
        dest="config_path",
        default=default_config_path,
        help=f"Path to pull_config.json. Defaults to: {default_config_path}"
    )
    parser.add_argument(
        "targets",
        nargs="*",
        help="Optional specific folder targets to pull (e.g. 'adrs', 'prds', 'issues', 'development_briefs'). If omitted, pulls all targets."
    )
    args = parser.parse_args()

    print("="*60)
    print("PHASE 1: Authentication...")
    print("="*60)
    creds = authenticate(auth_dir, client_secret_path, token_path)

    try:
        drive_service = build('drive', 'v3', credentials=creds)
        docs_service = build('docs', 'v1', credentials=creds)
    except Exception as e:
        print(f"Error initializing Google API clients: {e}", file=sys.stderr)
        sys.exit(1)

    print("\n" + "="*60)
    print("PHASE 2: Reading Configuration...")
    print("="*60)
    if not os.path.exists(args.config_path):
        print(f"Error: Configuration file '{args.config_path}' not found.", file=sys.stderr)
        sys.exit(1)

    try:
        with open(args.config_path, 'r', encoding='utf-8') as f:
            config = json.load(f)
    except Exception as e:
        print(f"Error parsing configuration JSON: {e}", file=sys.stderr)
        sys.exit(1)

    pull_targets = config.get("pull_targets", [])
    if not pull_targets:
        print("Warning: No pull_targets found in config.", file=sys.stderr)
        sys.exit(0)

    requested_targets = [t.lower() for t in args.targets]
    if requested_targets:
        filtered_targets = []
        for target in pull_targets:
            local_path_raw = target.get("local_path", "")
            folder_name = os.path.basename(local_path_raw).lower()
            if folder_name in requested_targets:
                filtered_targets.append(target)
        pull_targets = filtered_targets

    if not pull_targets:
        print(f"No matching pull targets found for: {args.targets}")
        sys.exit(0)

    print(f"Found {len(pull_targets)} pull targets.")

    for idx, target in enumerate(pull_targets):
        drive_folder_id = target.get("drive_folder_id")
        drive_file_id = target.get("drive_file_id")
        local_path_raw = target.get("local_path")
        split_by = target.get("split_by")
        include_pattern = target.get("include_pattern")
        exclude_pattern = target.get("exclude_pattern")

        if not (drive_folder_id or drive_file_id) or not local_path_raw:
            print(f"Warning: Pull target index {idx} missing drive_folder_id/drive_file_id or local_path - skipping.", file=sys.stderr)
            continue

        # Resolve local directory relative to projects folder
        local_target = os.path.abspath(os.path.join(projects_dir, local_path_raw))
        
        # Verify target folder path stays within workspace bounds for safety
        try:
            validate_safe_path(local_target, projects_dir)
        except ValueError as err:
            print(f"Error: {err}", file=sys.stderr)
            continue

        is_file_target = local_target.lower().endswith('.md')
        if is_file_target:
            local_dir = os.path.dirname(local_target)
        else:
            local_dir = local_target

        print("\n" + "-"*60)
        if drive_folder_id:
            print(f"Processing Target folder ID: '{drive_folder_id}'")
        else:
            print(f"Processing Target file ID: '{drive_file_id}'")
        print(f"Local output folder: '{local_dir}'")
        if split_by:
            print(f"Splitting files on: '{split_by}'")
        print("-"*60)

        # Clear target local directory before downloading to keep clean (only if it's a folder target)
        if not is_file_target:
            try:
                clear_local_directory(local_dir)
            except Exception as e:
                print(f"Error clearing local directory: {e}", file=sys.stderr)
                continue
        else:
            os.makedirs(local_dir, exist_ok=True)

        # List files matching filters
        try:
            if drive_folder_id:
                files_to_pull = list_drive_docs(drive_service, drive_folder_id, include_pattern, exclude_pattern)
            else:
                # Fetch single file metadata
                doc_info = drive_service.files().get(fileId=drive_file_id, fields='id, name').execute()
                files_to_pull = [doc_info]
        except HttpError as err:
            if drive_folder_id:
                print(f"Error listing drive contents: {err}", file=sys.stderr)
            else:
                print(f"Error getting file metadata: {err}", file=sys.stderr)
            continue

        if not files_to_pull:
            if drive_folder_id:
                print("No matching Google Docs found in this folder.")
            else:
                print("Google Doc not found on Drive.")
            continue

        print(f"Found {len(files_to_pull)} matching Google Docs. Fetching and converting...")
        split_level = get_split_level(split_by)

        for doc_info in files_to_pull:
            doc_id = doc_info['id']
            doc_name = doc_info['name']

            print(f"\n- Fetching doc: '{doc_name}' ({doc_id})...")
            try:
                doc_content = get_doc_content(docs_service, doc_id)
            except HttpError as err:
                print(f"Error fetching Google Doc '{doc_name}': {err}", file=sys.stderr)
                continue

            try:
                sections = parse_and_split_doc(doc_content, split_level)
                if is_file_target:
                    # Concatenate sections and save directly to file path
                    full_content = "\n\n".join([sec[1] for sec in sections if sec[1].strip()])
                    print(f"  -> Writing to file '{os.path.basename(local_target)}'...")
                    with open(local_target, 'w', encoding='utf-8') as f:
                        f.write(full_content)
                else:
                    save_sections(doc_name, sections, local_dir)
            except Exception as e:
                print(f"Error converting document '{doc_name}': {e}", file=sys.stderr)
                import traceback
                traceback.print_exc()

    print("\n" + "="*60)
    print("ALL PULL AND EXPORT OPERATIONS COMPLETED")
    print("="*60)


if __name__ == '__main__':
    main()
