---
name: docs-push-google
description: How the on-demand push/sync pipeline authenticates and transfers markdown bundles using drive_sync.py and sync_all.py. Use when initiating a Google Drive document sync.
disable-model-invocation: true
---

# Google Drive Sync Playbook (`docs-push-google`)

Use this playbook to aggregate the codebase and sync local documentation bundles on-demand to Google Drive.

## 1. Sync Pipeline Workflow

The sync pipeline consists of two sequential phases executed via the `sync_all.py` script:

1. **Phase 1: Codebase Aggregation (`aggregate.py`):**
   * Scans configured directories in `sync_config.json` (inside `Revit API Synthetic v2 Support/auth/`).
   * Bundles source file headers, code syntax, and comments into specialized markdown file collections (e.g. `Synthetic_src_Foundation.md`, `Synthetic_tests.md`) in the `docs/source_code/` folder.
2. **Phase 2: Google Drive Upload (`drive_sync.py`):**
   * Reads targets defined in `sync_config.json` and syncs them to Google Drive (using `drive_folder_id`).
   * If a target has no `drive_id`, uploads it as a new Google Doc and updates the JSON config.
   * If a target has a `drive_id`, updates the existing Google Doc. Falls back to raw markdown upload if Google Doc conversion fails.

## 2. On-Demand Sync Command

To execute the sync pipeline from a valid local session:
```powershell
python .agents/skills/docs-push-google/scripts/sync_all.py
```

## 3. OAuth Authentication Gating

Because the Google Drive client uses the interactive OAuth 2.0 flow:

* **Token Check:** Before running the sync command, check if a valid Google token exists at:
  `Revit API Synthetic v2 Support/auth/token.json`
* **OAuth Interactive Browser Fail-Safe:** If `token.json` is missing or expired, do **not** run the script headlessly, as the browser redirection flow will hang.
* **User Redirection Prompt:** Instruct the user to run the authentication flow manually in their terminal. Print this exact message:

> [!WARNING]
> Google Drive token is missing or expired. To re-authenticate, copy and run the following command directly in your local terminal:
> ```powershell
> python .agents/skills/docs-push-google/scripts/sync_all.py
> ```
