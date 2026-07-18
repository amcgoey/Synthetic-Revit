# Issue tracker: GitHub

Issues and PRDs for this repo live as GitHub issues. You can use either the `github` MCP server tools (preferred, as they execute directly without terminal commands) or the `gh` CLI.

## Repository Context
- **Owner**: `amcgoey`
- **Repo**: `Synthetic-Revit`

## Conventions

For all operations, prefer the `github` MCP server tools if available. Otherwise, fall back to the `gh` CLI.

### Create an issue
- **MCP Server**: Call `issue_write` with `method: "create"`, `owner`, `repo`, `title`, `body`.
- **`gh` CLI**: `gh issue create --title "..." --body "..."`. Use a heredoc for multi-line bodies.

### Read an issue
- **MCP Server**: Call `issue_read` with `method: "get"`, `owner`, `repo`, `issue_number`. Call `issue_read` with `method: "get_comments"` to retrieve comments.
- **`gh` CLI**: `gh issue view <number> --comments`, filtering comments by `jq` and also fetching labels.

### List issues
- **MCP Server**: Call `list_issues` or `search_issues` with `owner`, `repo`, and filters (e.g. `state: "open"`, `labels: ["..."]`).
- **`gh` CLI**: `gh issue list --state open --json number,title,body,labels,comments --jq '[.[] | {number, title, body, labels: [.labels[].name], comments: [.comments[].body]}]'` with appropriate `--label` and `--state` filters.

### Comment on an issue
- **MCP Server**: Call `add_issue_comment` with `owner`, `repo`, `issue_number`, `body`.
- **`gh` CLI**: `gh issue comment <number> --body "..."`

### Apply / remove labels
- **MCP Server**: Call `issue_write` with `method: "update"`, `owner`, `repo`, `issue_number`, and `labels: ["label1", "label2"]` containing the complete list of desired labels.
- **`gh` CLI**: `gh issue edit <number> --add-label "..."` / `--remove-label "..."`

### Close an issue
- **MCP Server**: Call `issue_write` with `method: "update"`, `owner`, `repo`, `issue_number`, `state: "closed"`, `state_reason: "completed"` (or `"not_planned"`). Add comment beforehand using `add_issue_comment` if a comment is needed.
- **`gh` CLI**: `gh issue close <number> --comment "..."`

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

Fetch issue details and comments using either `issue_read` (MCP) or `gh issue view <number> --comments`.

---

## Wayfinding operations

Used by `/wayfinder`. The **map** is a single issue with **child** issues as tickets.

- **Map**: a single issue labelled `wayfinder:map`, holding the Notes / Decisions-so-far / Fog body.
  - **MCP**: `issue_write` with `method: "create"`, `title`, `body`, `labels: ["wayfinder:map"]`.
  - **`gh` CLI**: `gh issue create --label wayfinder:map`.
- **Child ticket**: an issue linked to the map as a GitHub sub-issue.
  - **MCP**: Call `sub_issue_write` with `method: "add"`, `owner`, `repo`, `issue_number: <parent-number>`, `sub_issue_id: <child-database-id>`. (Obtain `<child-database-id>` from `.id` in the response of `issue_read` for the child).
  - **`gh` CLI**: `gh api` on the sub-issues endpoint.
  - **Fallback**: Where sub-issues aren't enabled, add the child to a task list in the map body and put `Part of #<map>` at the top of the child body. Labels: `wayfinder:<type>` (`research`/`prototype`/`grilling`/`task`). Once claimed, the ticket is assigned to the driving dev.
- **Blocking**: GitHub's **native issue dependencies** — the canonical, UI-visible representation.
  - **`gh` CLI**: Use the `gh issue edit` command with the appropriate dependency flags:
    - Add blocking edge: `gh issue edit <child-number> --add-blocked-by <blocker-number>`
    - Remove blocking edge: `gh issue edit <child-number> --remove-blocked-by <blocker-number>`
    - Overwrite/Set: `gh issue edit <child-number> --blocked-by <blocker-number>`
  - **Fallback**: Where dependencies aren't available, fall back to a `Blocked by: #<n>, #<n>` line at the top of the child body. A ticket is unblocked when every blocker is closed.
- **Frontier query**: list the map's open children.
  - **MCP**: Call `issue_read` with `method: "get_sub_issues"` (if sub-issues are enabled). Otherwise use `list_issues` or `search_issues` and parse bodies.
  - **`gh` CLI**: `gh issue list --state open`, scoped to the map's sub-issues / task list.
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
