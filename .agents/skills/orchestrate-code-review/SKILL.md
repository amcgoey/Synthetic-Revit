---
name: orchestrate-code-review
description: Coordinate subagents performing code reviews of completed tickets. Spawns subagents using the `/code-review` skill and compiles a consolidated review report with a nested numbering scheme.
disable-model-invocation: true
---

# Code Review Orchestrator

This skill coordinates subagents performing code reviews of completed tickets. The orchestrator organizes subagents, manages resources, and compiles a master report, but does **not** review the code directly.

## Process

### 1. Scope & Ticket Discovery

Identify the set of completed issues to review. Determine the mode based on user input:
- **Parent Issue Mode**: If a parent issue is specified (e.g., `#57`), fetch its details and all its completed sub-issues (children).
- **Custom Group Mode**: If a specific list of issues is provided, fetch all of them.
- **Frontier Mode**: If no specific issues are provided, fetch all open/closed issues in the repository marked with the `ready-for-review` label or recently completed tickets.

For each issue, retrieve its title, body, and comments.

### 2. Identify Branches, Commits & Specs

For each ticket to be reviewed:
- Determine the git branch (e.g., `ticket-<number>`), pull request, or commit range associated with the work.
- Find the originating spec/ticket description (to serve as the spec source for the review).

### 3. Resource & Queue Management

Because checking out branches, compiling, and running tests are resource-intensive and prone to lock conflicts, enforce the **Resource Lock Protocol**:
- **Concurrency Limit**: Allow a maximum of 1 or 2 concurrent subagents running active build/test/checkout commands.
- **Build / Test Lock**: Serialize compilation and testing. Ensure only one subagent is performing these operations on the local environment at any time.

### 4. Spawning Review Subagents

For each ticket under review:
- Spawn a subagent using the `invoke_subagent` tool.
- Instruct the subagent to run the `/code-review` skill.
- Provide the subagent with the target branch/commit range and the spec/ticket description.
- Wait for the subagent to return their review report (which contains Standards and Spec axes).

### 5. Report Compilation & Formatting

Once all subagents return their reviews, compile the findings into a master report.
- Use a **nested numbering scheme** to organize findings clearly:
  - `X. [Issue #Number] - [Ticket Title]`
    - `X.1. Standards` (violations of coding standards or baseline smells)
      - `X.1.1. [File/Line/Hunk]: [Standard/Smell] - [Description]`
    - `X.2. Spec` (missing/partial requirements or incorrect implementation)
      - `X.2.1. [Requirement]: [Description]`
- Output the compiled master report.
- Optionally post the individual subagent reports as comments on their respective GitHub issues.
