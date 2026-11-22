---
name: orchestrate-code-review
description: "Coordinate subagents performing code reviews of completed tickets."
disable-model-invocation: true
---

Coordinate subagents performing code reviews across a set of completed tickets. This skill manages subagent concurrency and resources, compiling a consolidated master report with a nested numbering scheme.

The issue tracker workflow is documented in [`docs/agents/issue-tracker.md`](../../../docs/agents/issue-tracker.md).

## Process

### 1. Scope & Ticket Discovery

Identify the set of completed issues to review from [`docs/agents/issue-tracker.md`](../../../docs/agents/issue-tracker.md):
- **Parent Issue Mode**: If a parent issue is specified (e.g. `#57`), fetch its details and all completed sub-issues.
- **Custom Group Mode**: If a specific list of issues is provided, fetch all of them.
- **Frontier Mode**: If no specific issues are provided, fetch open/closed issues marked with the `ready-for-review` label or recently completed tickets.

For each issue, retrieve its title, body, and comments.

### 2. Identify Branches, Commits & Specs

For each ticket to be reviewed:
- Determine the git branch (e.g. `ticket-<number>`), pull request, or commit range associated with the work.
- Locate the originating spec or ticket description to serve as the spec source.

### 3. Resource & Concurrency Protocol

- **Concurrency Cap**: Run at most **2–4 subagents** in parallel.
- **Isolated Concurrent Builds & Tests**: Reviewers may build and run [`test_executor.py`](../revit-tests/scripts/test_executor.py) concurrently across separate worktrees without colliding on `output/Synthetic/`, `AddInsSettings.json`, `HKCU\...\CodeSigning`, or open developer Revit sessions (see [`revit-tests`](../revit-tests/SKILL.md)). Give each subagent its own worktree when checking out branches.

### 4. Spawning Review Subagents

For each ticket under review:
- Spawn a review subagent using `invoke_subagent` with `TypeName: "research"` and `Model: "pro"`.
- Start each subagent with a fresh context window.
- Provide sparse context pointers: target branch/commit range, ticket reference, and spec link. Instruct the subagent to run `code-review`.
- Wait for the subagent to return its Standards and Spec review reports.

### 5. Report Compilation & Formatting

Once all subagents report back, compile findings into a master report using a nested numbering scheme:

```text
X. [Issue #Number] - [Ticket Title]
  X.1. Standards
    X.1.1. [File/Line/Hunk]: [Standard/Smell] - [Description]
  X.2. Spec
    X.2.1. [Requirement]: [Description]
```

Output the compiled master report. Post the individual subagent reports as comments on their respective GitHub issues when requested.
