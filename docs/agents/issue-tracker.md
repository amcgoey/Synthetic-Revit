# Issue tracker: GitHub

Issues and PRDs for this repo live as GitHub issues. You can use either the `github` MCP server tools (preferred, as they execute directly without terminal commands) or the `gh` CLI.

## Repository Context
- **Owner**: `amcgoey`
- **Repo**: `Synthetic-Revit`

File issues only into the user's repo (`amcgoey/Synthetic-Revit`). Never file into `mattpocock/skills`. In a fork or clone, confirm target with `gh repo view --json nameWithOwner` before the first issue creation.

## Conventions

For all operations, prefer the `github` MCP server tools if available. Otherwise, fall back to the `gh` CLI.

### Create an issue
- **MCP Server**: Call `issue_write` with `method: "create"`, `owner: "amcgoey"`, `repo: "Synthetic-Revit"`, `title`, `body`.
- **`gh` CLI**: `gh issue create --title "..." --body "..."`. Use a heredoc for multi-line bodies.

### Read an issue
- **MCP Server**: Call `issue_read` with `method: "get"`, `owner: "amcgoey"`, `repo: "Synthetic-Revit"`, `issue_number`. Call `issue_read` with `method: "get_comments"` to retrieve comments.
- **`gh` CLI**: `gh issue view <number> --json number,title,body,labels,comments` (or `gh issue view <number> --comments`).

### List issues
- **MCP Server**: Call `list_issues` or `search_issues` with `owner: "amcgoey"`, `repo: "Synthetic-Revit"`, and filters (e.g. `state: "open"`, `labels: ["..."]`).
- **`gh` CLI**: `gh issue list --state open --json number,title,body,labels,comments --jq '[.[] | {number, title, body, labels: [.labels[].name], comments: [.comments[].body]}]'` with appropriate `--label` and `--state` filters.

### Make an issue a sub-issue of a parent
- **MCP Server**: Call `sub_issue_write` with `method: "add"`, `owner: "amcgoey"`, `repo: "Synthetic-Revit"`, `issue_number: <parent-number>`, `sub_issue_id: <child-database-id>`. (Obtain `<child-database-id>` from `.id` in the response of `issue_read` for the child).
- **`gh` CLI (`gh` 2.94+)**: `gh issue create --parent <parent> ...`, or `gh issue edit <parent> --add-sub-issue <child>` afterwards.
- **Older `gh` CLI**: `gh api --method POST repos/amcgoey/Synthetic-Revit/issues/<parent>/sub_issues -F sub_issue_id=<child-db-id>`.
- **Fallback**: Where sub-issues aren't enabled, put `Part of #<parent>` at the top of the child body.

### Blocking (Issue Dependencies)
GitHub's **native issue dependencies** are the canonical, UI-visible representation.
- **`gh` CLI (`gh` 2.94+)**:
  - Add blocking edge: `gh issue create --blocked-by <n>,<n> ...` or `gh issue edit <child> --add-blocked-by <blocker>`
  - Remove blocking edge: `gh issue edit <child> --remove-blocked-by <blocker>`
  - Overwrite/Set: `gh issue edit <child> --blocked-by <blocker>`
- **Older `gh` CLI**: `gh api --method POST repos/amcgoey/Synthetic-Revit/issues/<child>/dependencies/blocked_by -F issue_id=<blocker-db-id>`, where `<blocker-db-id>` is the blocker's numeric database id (`gh api repos/amcgoey/Synthetic-Revit/issues/<n> --jq .id`, not `#number` or `node_id`).
- **Fallback**: Where dependencies aren't available, fall back to a `Blocked by: #<n>, #<n>` line at the top of the child body. A ticket is unblocked when every blocker is closed.

### Count open blockers
- **`gh` API**: `gh api repos/amcgoey/Synthetic-Revit/issues/<n> --jq .issue_dependencies_summary.blocked_by` (open blockers only; 0 means startable).
- **`gh` CLI alternative**: `gh issue view <n> --json blockedBy --jq '[.blockedBy.nodes[] | select(.state == "OPEN")] | length'`.

### Comment on an issue
- **MCP Server**: Call `add_issue_comment` with `owner: "amcgoey"`, `repo: "Synthetic-Revit"`, `issue_number`, `body`.
- **`gh` CLI**: `gh issue comment <number> --body "..."`

### Apply / remove labels
- **MCP Server**: Call `issue_write` with `method: "update"`, `owner: "amcgoey"`, `repo: "Synthetic-Revit"`, `issue_number`, and `labels: ["label1", "label2"]` containing the complete list of desired labels.
- **`gh` CLI**: `gh issue edit <number> --add-label "..."` / `--remove-label "..."`

### Close an issue (Safe Close Convention)
Always post the explanation comment first, then close the issue. (On an already-closed issue, `gh issue close --comment` drops the comment and still exits 0).
- **MCP Server**:
  1. Comment: Call `add_issue_comment` with `owner: "amcgoey"`, `repo: "Synthetic-Revit"`, `issue_number`, `body`.
  2. Close: Call `issue_write` with `method: "update"`, `owner: "amcgoey"`, `repo: "Synthetic-Revit"`, `issue_number`, `state: "closed"`, `state_reason: "completed"` (or `"not_planned"`).
- **`gh` CLI**:
  1. Comment: `gh issue comment <number> --body "..."`
  2. Close: `gh issue close <number>`

---

## Pull requests as a triage surface

**PRs as a request surface: no.**

When set to `yes`, PRs run through the same labels and states as issues.
- **MCP Server**: Use `pull_request_read` and `pull_request_review_write` or equivalent tools.
- **`gh` CLI**: Use the `gh pr` equivalents.

GitHub shares one number space across issues and PRs, so a bare `#42` may be either — resolve with `gh pr view 42` / `pull_request_read` and fall back to `gh issue view 42` / `issue_read`.

---

## When a skill says "publish to the issue tracker"

Create a GitHub issue using either `issue_write` (MCP) or `gh issue create`.

## When a skill says "fetch the relevant ticket"

Fetch issue details and comments using either `issue_read` (MCP) or `gh issue view <number> --comments` / `--json number,title,body,labels,comments`.

---

## Wayfinding operations

Used by `/wayfinder`. The **map** is a single issue with **child** issues as tickets.

- **Map**: a single issue labelled `wayfinder:map`, holding the Notes / Decisions-so-far / Fog body.
  - **MCP**: `issue_write` with `method: "create"`, `title`, `body`, `labels: ["wayfinder:map"]`.
  - **`gh` CLI**: `gh issue create --label wayfinder:map`.
- **Child ticket**: an issue linked to the map as a GitHub sub-issue (see **Make an issue a sub-issue of a parent**).
  - **MCP**: Call `sub_issue_write` with `method: "add"`, `owner: "amcgoey"`, `repo: "Synthetic-Revit"`, `issue_number: <parent-number>`, `sub_issue_id: <child-database-id>`. (Obtain `<child-database-id>` from `.id` in the response of `issue_read` for the child).
  - **`gh` CLI**: `gh issue create --parent <parent-number>` or `gh issue edit <parent-number> --add-sub-issue <child-number>`.
  - **Fallback**: Where sub-issues aren't enabled, add the child to a task list in the map body and put `Part of #<map>` at the top of the child body. Labels: `wayfinder:<type>` (`research`/`prototype`/`grilling`/`task`). Once claimed, the ticket is assigned to the driving dev.
- **Blocking**: GitHub's **native issue dependencies** — the canonical, UI-visible representation (see **Blocking** under Conventions).
- **Frontier query**: list the map's open children (`gh issue list --state open`, scoped to the map's sub-issues / task list).
  - Drop any with an open blocker (`issue_dependencies_summary.blocked_by > 0`, or an open issue in the `Blocked by` line) or an assignee; first in map order wins.
- **Claim**: Assign to the driving dev.
  - **MCP**: Call `get_me` to find the current user's login, then call `issue_write` with `method: "update"`, `issue_number`, `assignees: ["<username>"]`.
  - **`gh` CLI**: `gh issue edit <n> --add-assignee @me` — the session's first write.
- **Resolve**:
  - **MCP**:
    1. Comment: `add_issue_comment` with the answer.
    2. Close: `issue_write` with `state: "closed"`, `state_reason: "completed"`.
    3. Update map: `issue_write` (`method: "update"`) to append a context pointer to the Decisions-so-far list in the map's body.
  - **`gh` CLI**: `gh issue comment <n> --body "<answer>"`, then `gh issue close <n>`, then append a context pointer (gist + link) to the map's Decisions-so-far.
