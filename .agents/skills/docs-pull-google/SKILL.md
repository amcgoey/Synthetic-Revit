---
name: docs-pull-google
description: How the pull pipeline downloads, splits, and formats cloud docs to local files using pull_docs.py. Use when initiating a Google Drive document pull.
disable-model-invocation: true
---

# Google Drive Document Pull Playbook (`docs-pull-google`)

Use this playbook to safely pull, format, and split Google Drive cloud documentation into local Markdown files.

## 1. Folder Isolation Layout (Local vs. Cloud)

To prevent local planning documents from being overwritten or deleted by the pull pipeline:

* **Separation Suffix:** All target folders are split into `local/` (your local-only edits) and `cloud/` (cloud-pulled documents) subdirectories:
  * `docs/adrs/cloud/`
  * `docs/prds/cloud/`
  * `docs/issues/cloud/`
  * `docs/development_briefs/cloud/`
* **Deletion Safety:** The pull script clears the target `cloud/` subdirectories before downloading fresh copies. The `local/` subdirectories are **never** cleared or touched.

## 2. On-Demand Pull Command

To execute the pull pipeline:
```powershell
python .agents/skills/docs-pull-google/scripts/pull_docs.py
```

## 3. Pre-Pull Git Verification

Although the script is isolated to `cloud/` subdirectories, as an extra layer of protection:
1. Run `git status` before executing the pull command.
2. If there are uncommitted changes or untracked files inside the target documentation subdirectories, prompt the user to commit or stash them before running the pull.

## 4. OAuth Authentication Gating

Because the Google Doc client uses the interactive OAuth readonly flow:

* **Token Check:** Before running the pull command, check if a valid Google token exists at:
  `Revit API Synthetic v2/auth/token_pull.json`
* **OAuth Interactive Browser Fail-Safe:** If `token_pull.json` is missing or expired, do **not** run the script headlessly.
* **User Redirection Prompt:** Instruct the user to run the authentication flow manually in their terminal. Print this exact message:

> [!WARNING]
> Google Drive pull token is missing or expired. To re-authenticate, copy and run the following command directly in your local terminal:
> ```powershell
> python .agents/skills/docs-pull-google/scripts/pull_docs.py
> ```
