---
name: orchestrate-implement
description: Coordinate subagents implementing tickets (a parent issue and its children, a specific list of issues, or all ready-for-agent issues). Coordinates resources (git branches, building, testing), runs plan reviews, and executes an automated post-implementation code review loop with defect classification.
disable-model-invocation: true
---

# Code Implementation Orchestrator

This skill coordinates the development flow of subagents handling issue tickets. The orchestrator coordinates activities, manages resources, and routes defects, but does **not** write code. The subagents perform the actual planning and implementation.

## Process

### 1. Scope & Ticket Discovery

Identify the set of issues to orchestrate. Determine the mode based on user input:
- **Parent Issue Mode**: If a parent issue is specified (e.g., `#57`), fetch its details and all its sub-issues (children).
- **Custom Group Mode**: If a specific list of issues is provided, fetch all of them.
- **Frontier Mode**: If no specific issues are provided, fetch all open issues in the repository marked with the `ready-for-agent` label.

For each issue, retrieve its title, body, existing comments, and labels.

### 2. Map Dependencies & Identify Frontier

Build a Directed Acyclic Graph (DAG) of the target issues to determine their execution order:
- **Blocking Edges**: Read native GitHub issue dependencies. As a fallback, check for a `Blocked by: #XX` list in the issue body.
- **Frontier**: Identify the set of open issues that have no open blockers. These are the only issues that can be worked on immediately.

### 3. Resource & Queue Management

Because local environment resources are limited and concurrent tasks can cause file/build locks, you must coordinate access. Enforce the **Resource Lock Protocol**:
- **Concurrency Limit**: Limit active implementation to a manageable number (default: 1 or 2 concurrent subagents running active build/test steps).
- **Workspace Sharing (Git Worktrees)**: When spawning subagents via `invoke_subagent`, specify `Workspace: "share"`. This instructs the system to create isolated workspaces using Git worktrees, allowing subagents to check out independent branches without colliding or affecting the parent's working directory.
- **Branch Management**: Direct each subagent to create and work on its dedicated branch (e.g., `ticket-<number>`) within its shared worktree workspace.
- **Build / Compilation Lock**: Serialize build commands. Only one subagent is allowed to compile the code at a time to prevent MSBuild write conflicts.
- **Test Lock**: Serialize integration/unit test execution to prevent database, Revit session, or file lock conflicts.
- Keep a FIFO queue of subagent requests for build and test locks, granting them sequentially.

### 4. Spawning Subagents & Plan Review Loop

For each issue on the frontier, spawn a subagent using the `invoke_subagent` tool with the `implement` skill. 
Direct the subagent to:
1. Review the parent spec (if applicable) and their assigned ticket + comments.
2. Formulate an implementation plan.
3. Submit the plan back to the orchestrator for review and wait for approval.
4. Once approved, proceed with implementation using appropriate skills (such as `/tdd`).

When a subagent submits an implementation plan, review it thoroughly:
- **Standards & Spec Check**: Verify the plan against the Parent Spec/PRD, the subissue description, repository coding standards, and target test seams.
- **Feedback**: If the plan is incomplete or does not address the issue fully, send feedback to the subagent to revise it. Repeat until satisfied.
- **Approval**: Once the plan is approved, reply to the subagent to release them for development, and **attach the plan as a comment to the subissue on GitHub**.

### 5. Automated Post-Implementation Code Review

When a subagent reports completion of their implementation, do **not** merge the branch or close the ticket immediately. Instead, run a code review:
1. Spawn a new, separate subagent to run the `/code-review` skill on the ticket's branch (comparing it against the base branch).
2. When the review subagent returns the report, split the findings into two categories:
   - **Direct Defects**: Clear errors, missing features, bugs, or omissions that must be implemented directly.
   - **Judgement Calls / Human Review**: Design smells, subjective decisions, or potential issues requiring developer clarification or a grilling session.

### 6. Defect Routing & DAG Updating

Based on the code review findings, perform the following routing steps:
1. **Direct Defects**:
   - Group the findings logically into clean, vertical slices of work.
   - Present the drafted list of defect tickets to the user for approval.
   - Once approved, publish them as subissues to the original issue on GitHub with the `ready-for-agent` label.
2. **Judgement Calls**:
   - Create subissues linked to the original issue and apply a label for future human review (e.g., `grilling-required`). These tickets are held for later interactive grilling sessions and are not worked on by agents yet.
3. **DAG Update**:
   - Update the active dependency graph to block the original ticket's completion by all newly created defect and grilling subissues.
   - The original ticket's branch is NOT merged, and the ticket remains open.
   - Downstream tickets in the DAG that depend on the original ticket remain blocked.
   - Independent tickets on the frontier (unaffected by this branch) continue implementation in parallel.

### 7. Defect Resolution & Closure

For the created direct defect tickets:
1. Direct the assigned subagent to branch off the original ticket's branch (or work directly on it) to build on top of the existing implementation.
2. Run each defect ticket through the standard subagent plan review and implementation flow (Step 4).
3. Once all blocking subissues (both direct defects and grilling tickets) are closed and resolved:
   - Merge the original ticket's branch into the base integration/development branch.
   - Post the final walkthrough as a comment on the original issue.
   - Close the original issue (`state: "closed"`, `state_reason: "completed"`).
   - Recalculate the dependency frontier and spawn subagents for any newly unblocked issues.
