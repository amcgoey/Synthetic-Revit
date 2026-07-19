import os
import sys
import argparse
from google.oauth2.credentials import Credentials
from google_auth_oauthlib.flow import InstalledAppFlow
from google.auth.transport.requests import Request
from googleapiclient.discovery import build
from googleapiclient.http import MediaFileUpload
from googleapiclient.errors import HttpError

# Define Google Drive Scopes
SCOPES = ['https://www.googleapis.com/auth/drive.file']


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


def get_auth_paths():
    """Compute the absolute paths to client_secret.json and token.json in the Auth folder."""
    workspace_dir = find_workspace_root()
    projects_dir = os.path.dirname(workspace_dir)

    auth_dir = os.path.join(workspace_dir, "auth")
    client_secret_path = os.path.join(auth_dir, "client_secret.json")
    token_path = os.path.join(auth_dir, "token.json")

    return auth_dir, client_secret_path, token_path


def authenticate():
    """Handles Google OAuth 2.0 user credentials flow and returns valid credentials."""
    auth_dir, client_secret_path, token_path = get_auth_paths()
    creds = None

    # Try loading existing cached token
    if os.path.exists(token_path):
        try:
            creds = Credentials.from_authorized_user_file(token_path, SCOPES)
        except Exception as e:
            print(f"Warning: Failed to load existing token.json: {e}. Re-authenticating...", file=sys.stderr)
            creds = None

    # If credentials don't exist or are invalid/expired, run authorization flow
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
                print(f"Please place your Google OAuth Client secret JSON file at:", file=sys.stderr)
                print(f"  {client_secret_path}", file=sys.stderr)
                print("="*80 + "\n", file=sys.stderr)
                sys.exit(1)

            # Run the local server flow to authenticate
            print("Launching browser for OAuth authentication...", flush=True)
            flow = InstalledAppFlow.from_client_secrets_file(client_secret_path, SCOPES)
            creds = flow.run_local_server(port=0)

        # Save the credentials for the next run
        os.makedirs(auth_dir, exist_ok=True)
        with open(token_path, 'w') as token_file:
            token_file.write(creds.to_json())
            print(f"Saved authentication token to: {token_path}")

    return creds


def sync_to_drive(file_path, drive_file_id=None, parent_folder_id=None):
    """
    Uploads or updates the specified local Markdown file to Google Drive.
    Converts the file to a Google Doc native format during creation.
    Handles placeholders (e.g. starting with 'YOUR_') by performing a new upload.
    Falls back to raw file upload if Google Docs conversion fails (e.g. due to size limits).
    """
    if not os.path.exists(file_path):
        print(f"Error: Local file '{file_path}' does not exist.", file=sys.stderr)
        sys.exit(1)

    creds = authenticate()

    # Check if drive_file_id is a placeholder or empty
    is_placeholder = (
        not drive_file_id
        or drive_file_id.startswith("YOUR_")
        or drive_file_id.lower() in ("placeholder", "null", "none", "")
    )

    if is_placeholder:
        drive_file_id = None

    try:
        service = build('drive', 'v3', credentials=creds)

        if drive_file_id:
            # Update an existing document
            print(f"Updating Google Doc with File ID: {drive_file_id} ...")
            media = MediaFileUpload(file_path, mimetype='text/plain', resumable=True)
            try:
                file = service.files().update(
                    fileId=drive_file_id,
                    media_body=media,
                    fields='id'
                ).execute()
                updated_id = file.get('id')
                print(f"SUCCESS: Updated Google Doc.")
                print(f"Google Drive File ID: {updated_id}")
                return updated_id
            except HttpError as error:
                if error.resp.status == 400:
                    print("Warning: Update failed with text/plain. Retrying as raw Markdown...", file=sys.stderr)
                    media = MediaFileUpload(file_path, mimetype='text/markdown', resumable=True)
                    file = service.files().update(
                        fileId=drive_file_id,
                        media_body=media,
                        fields='id'
                    ).execute()
                    updated_id = file.get('id')
                    print(f"SUCCESS: Updated raw file.")
                    print(f"Google Drive File ID: {updated_id}")
                    return updated_id
                else:
                    raise
        else:
            # Create a new document
            file_name = os.path.splitext(os.path.basename(file_path))[0]
            print(f"Uploading '{file_path}' to Google Drive as '{file_name}'...")

            file_metadata = {
                'name': file_name,
                'mimeType': 'application/vnd.google-apps.document'  # Force native conversion
            }
            if parent_folder_id:
                file_metadata['parents'] = [parent_folder_id]

            media = MediaFileUpload(file_path, mimetype='text/plain', resumable=True)

            try:
                file = service.files().create(
                    body=file_metadata,
                    media_body=media,
                    fields='id'
                ).execute()
                new_id = file.get('id')
                print(f"SUCCESS: Created new Google Doc.")
                print(f"Google Drive File ID: {new_id}")
                return new_id
            except HttpError as error:
                if error.resp.status == 400:
                    print("\nWarning: Google Docs conversion failed (likely file size limit).", file=sys.stderr)
                    print("Falling back to uploading raw Markdown file...", file=sys.stderr)

                    raw_metadata = {
                        'name': os.path.basename(file_path),
                        'mimeType': 'text/markdown'
                    }
                    if parent_folder_id:
                        raw_metadata['parents'] = [parent_folder_id]

                    media = MediaFileUpload(file_path, mimetype='text/markdown', resumable=True)

                    file = service.files().create(
                        body=raw_metadata,
                        media_body=media,
                        fields='id'
                    ).execute()
                    new_id = file.get('id')
                    print(f"SUCCESS: Created new raw Markdown file.")
                    print(f"Google Drive File ID: {new_id}")
                    return new_id
                else:
                    raise

    except HttpError as error:
        print(f"An error occurred: {error}", file=sys.stderr)
        sys.exit(1)


if __name__ == '__main__':
    parser = argparse.ArgumentParser(
        description="Sync a local Markdown file to Google Drive as a Google Doc."
    )
    parser.add_argument(
        "file_path",
        help="Path to the local Markdown file to sync."
    )
    parser.add_argument(
        "--id",
        dest="drive_file_id",
        default=None,
        help="Optional Google Drive File ID to update an existing document. If omitted, a new file is created."
    )
    parser.add_argument(
        "--folder",
        dest="parent_folder_id",
        default=None,
        help="Optional Google Drive Folder ID to place new files inside on creation."
    )

    args = parser.parse_args()
    sync_to_drive(args.file_path, args.drive_file_id, args.parent_folder_id)
