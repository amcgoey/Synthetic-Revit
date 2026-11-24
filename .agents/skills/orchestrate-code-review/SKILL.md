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

### 3. Baseline Verification & Concurrency Protocol

- **Concurrency Cap**: Run at most **2–4 review subagents** in parallel.
- **Orchestrator-Owned Baselines**: Before spawning a review subagent for a ticket, the orchestrator checks out the ticket branch in an isolated worktree when needed and runs `python .agents/skills/revit-tests/scripts/test_executor.py --version Logic` to confirm a clean compilation and headless logic baseline (multiple worktrees can build and run [`test_executor.py`](../revit-tests/scripts/test_executor.py) concurrently without colliding on `output/Synthetic/`, `AddInsSettings.json`, `HKCU\...\CodeSigning`, or open Revit sessions; see [`revit-tests`](../revit-tests/SKILL.md)). This keeps `code-reviewer` strictly within read-only inspection.

### 4. Spawning Review Subagents

For each ticket whose baseline passes:
- Spawn a review subagent via `invoke_subagent` (`TypeName: "code-reviewer"`) with a fresh context window.
- Provide sparse context pointers: target branch/commit range, ticket reference, spec link, and confirmation that the `Logic` baseline already passed. Instruct it to run the Standards and Spec review axes from `code-review` (skipping the already-verified baseline build/test step).
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
