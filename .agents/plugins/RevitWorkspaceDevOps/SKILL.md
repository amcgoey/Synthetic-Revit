---
name: revit-workspace-devops-intelligence
description: Governs inverted multi-version compilation conditioning, on-demand docstring compilation, and unified UI ribbon mapping.
---

# RevitWorkspaceDevOps Architectural Playbook

You must strictly follow these rules to maintain multi-version build stability and prevent unauthorized source refactoring.

## 1. Inverted Preprocessor Conditioning Hierarchy
You must treat the most modern Revit API structure as the unconditioned baseline. Legacy variations must be quarantined inside explicit, historical conditional blocks.

### Code Branching Principles:
* **The Default Baseline:** The latest version of Revit (defined by the `CURRENT_LATEST_VERSION` environment variable) must be written as the clean, standard, unconditioned code path (the fallback final `#else` or unconditioned block).
* **The Legacy Condition:** Prior supported releases must be wrapped inside explicitly tagged conditional compilation symbols (e.g., `#if REVIT2024` or `#elif REVIT2025`). This guarantees that dropping an old version requires only deleting its isolated block, leaving the main modern branch untouched.

```csharp
#if REVIT2022 || REVIT2023
    // Legacy Implementation: Parameter data type via deprecated ParameterType enum
    // (ParameterType was deprecated in 2022 and removed in 2024)
    ParameterType paramType = definition.ParameterType;
#elif REVIT2024
    // Transitionary Implementation: Use GetDataType() returning ForgeTypeId
    // (ParameterType fully removed; GetDataType() is now the only valid path)
    ForgeTypeId dataType = definition.GetDataType();
#else
    // DEFAULT baseline: Latest Revit API Implementation (2025+)
    // Clean, modern, unconditioned code branch.
    ForgeTypeId dataType = definition.GetDataType();
#endif
```
## 2. Discrete Version Detection & Gated Code Upgrading

You are strictly forbidden from continuously monitoring project files in the background. You must only sweep the workspace for `.csproj` changes at three specific trigger points: **Chat Initialization**, **Invocation of `/finalize`**, or **Explicit User Command (`/check-version`)**.

### Upgrade Protocol:

1. **The Audit Step:** Extract the version integer suffixes from all `.csproj` filenames within the solution. Compare the highest detected version against the active `CURRENT_LATEST_VERSION` constant.  
2. **The Prompt Block:** If a higher-version project target is discovered (e.g., `Plugin_2027.csproj` when the constant is `2026`), you must halt the active workflow and output an explicit confirmation request to the user:  
   *"Detected a new compilation target: `Plugin_2027.csproj`. Would you like to update the workspace environment constant `CURRENT_LATEST_VERSION` to 2027?"*  
3. **The Refactoring Permission Gate:** If the user approves the environment update, **do not automatically alter the C\# files**. You must explicitly request permission to refactor the preprocessor structures:  
   *"Updating baseline constant to 2027\. Would you like me to refactor our current unconditioned code paths into an explicit `#elif REVIT2026` branch to clear the default block for the new 2027 API developments?"*

## 3. Google Drive Sync Pipeline

The following scripts in `scripts/` form a two-phase documentation sync pipeline. They are invoked automatically by the `/finalize` and `/sync` workflow triggers.

### Scripts

| Script | Role |
|---|---|
| `aggregate.py` | Reads `sync_config.json` and bundles configured source directories into large markdown files in `docs/`. Preserves all comments. |
| `drive_sync.py` | Low-level Google Drive uploader. Authenticates via OAuth 2.0. Converts uploads to native Google Docs format; falls back to raw markdown on size limit errors. |
| `sync_all.py` | Orchestrator. Runs `aggregate.py` (Phase 1) then calls `drive_sync.py` for every `sync_targets` entry in config (Phase 2). |
| `pull_docs.py` | Google Drive downloader and Markdown exporter. Reads `pull_config.json`, clears local directories, downloads Google Docs, converts them to Markdown (retaining headings, formatting, lists, tables, and images), and splits them by specified headings. |

### Configuration

* **`sync_config.json`** lives at: `../Revit API Synthetic v2 Support/auth/sync_config.json`
  * `aggregations`: array of `{ source_dir, output_file }` pairs — what to bundle and where to write it
  * `sync_targets`: array of `{ local_path, drive_id }` pairs — what to push to Drive
  * `drive_folder_id`: the Google Drive folder that receives newly created files

* **`pull_config.json`** lives at: `../Revit API Synthetic v2 Support/auth/pull_config.json`
  * `pull_targets`: array of `{ drive_folder_id, local_path, split_by, include_pattern, exclude_pattern }` — what Google Drive folders and files to download, how to split, and where to save them.

### Authentication

Google OAuth credentials are stored in the sibling Support project at:
`../Revit API Synthetic v2 Support/auth/`
* `client_secret.json` — OAuth client credentials from Google Cloud Console (must be placed manually)
* `token.json` — cached OAuth token written on first authenticated run (auto-managed for uploads)
* `token_pull.json` — cached OAuth token for read-only doc pull operations (auto-managed for downloads)

### Dependencies

Python packages required. Install once:
```
pip install -r .agents/plugins/RevitWorkspaceDevOps/requirements.txt
```

### Running Manually

From the project root:
```
python .agents/plugins/RevitWorkspaceDevOps/scripts/sync_all.py
```
Or pull Google Docs down:
```
python .agents/plugins/RevitWorkspaceDevOps/scripts/pull_docs.py
```
Or aggregate only (no Drive push):
```
python .agents/plugins/RevitWorkspaceDevOps/scripts/aggregate.py
```
