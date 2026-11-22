---
name: implement-spec
description: "Implement the result of /to-spec and /to-tickets in code."
disable-model-invocation: true
---

You have been provided a spec. This spec should have tickets associated with it, describing how to implement the spec.

The issue tracker should have been provided to you in [`docs/agents/issue-tracker.md`](../../../docs/agents/issue-tracker.md). If not, tell the user to run `/setup-matt-pocock-skills`.

The goal is the entire spec implemented on a single **integration branch**, with every ticket verified across supported Revit versions and resolved the way the issue tracker closes work.

The tickets are not a list of steps. They are a **task graph** with blocking relationships between them (`issue_dependencies_summary.blocked_by` or `Blocked by: #XX`). This means there is always a **frontier** of tickets which are ready to be grabbed.

Start every subagent with a fresh context window.

Communication to and from subagents should be sparse. Communicate primarily through **context pointers**: to the spec, tickets, [`GLOSSARY.md`](../../../GLOSSARY.md), [`docs/agents/domain.md`](../../../docs/agents/domain.md), relevant `revit-*` skills, research notes, and previous commits. Don't duplicate information already available via pointers.

- **Exploration subagents** use the `pro` model.
- **Implementer subagents** use the `flash` model and run in the background where possible for maximum concurrency.
- **Merger subagents** use the `flash` model.
- **Review planner agents** use the `pro` model.
- **Code review agents** use the `pro` model.

## Revit Resource & Tool Guardrails

Git worktrees (`Workspace: "share"`) isolate source files, and [`test_executor.py`](../revit-tests/scripts/test_executor.py) isolates each worker's build and execution environment (`SyntheticIsolatedBuildRoot`, `SYNTHETIC_TEST_WORKER=1`, persistent `HKCU\...\CodeSigning` entries, shared-read bundle lease, and automatic post-run `--assert-clean` journal gating). See [`revit-tests`](../revit-tests/SKILL.md) for worker flags (`--version`, `--filter`, `--category`, `--no-pool`) and runtime guarantees.

- **Concurrency Cap**: Run at most **2–4 implementer subagents** in parallel.
- **Concurrent Isolated Builds & Tests**: Implementer subagents build and run tests concurrently in their own worktrees via [`test_executor.py`](../revit-tests/scripts/test_executor.py) without colliding on `output/Synthetic/`, `AddInsSettings.json`, `CodeSigning`, or open developer Revit sessions.
- **Direct File Editing**: Subagents must use built-in file-editing tools (`replace_file_content`, `multi_replace_file_content`, `write_to_file`) and never execute Python or PowerShell scripts to edit code files.

## Steps

1. Read the spec and tickets to understand the task graph.

2. (optional) Use an **exploration subagent** (`pro` model) to conduct any exploration required by the tickets: relevant codebase files, `RevitDOM` seams, or Revit API documentation. Ensure the exploration subagent can save files: it should save its markdown notes in a scratch directory outside the repo, accessible by all future subagents. This lets **implementer subagents** focus on implementation rather than exploration.

3. Create the integration branch. If the issue tracker closes work through PRs, or the user asks for one, open a draft PR after the first merge in step 5 (a branch with no commits ahead of main can't open one), marked as closing the spec and tickets.

4. Use **implementer subagents** (`flash` model) to implement each ready frontier ticket, each in its own worktree (`Workspace: "share"`) on its own branch (`ticket-<number>`). Claim each ticket on the tracker (`gh issue edit <n> --add-assignee @me`) before starting. Each implementer subagent:
   - confirms its worktree is based on the integration branch before starting, and resets onto it if not;
   - consults [`GLOSSARY.md`](../../../GLOSSARY.md) and applicable `revit-*` skills (`revit-multi-versions`, `revit-transactions`, `revit-wpf-mvvm`, `revit-data-retrieval`, `revit-extensible-storage`, `revit-failure-handling`);
   - runs the [`tdd`](../tdd/SKILL.md) skill paired with [`revit-tests`](../revit-tests/SKILL.md) (Step 1 `--version Logic` → Step 2 `--version <CURRENT_LATEST_VERSION>` → Step 3 multi-version validation via [`test_executor.py`](../revit-tests/scripts/test_executor.py)) until all targeted tests pass and post-run `--assert-clean` journal verification succeeds;
   - posts a brief walkthrough comment to its ticket (`gh issue comment <n> --body "..."`, redacting any secrets or local paths);
   - merges the integration branch tip into its own branch before reporting done.

5. Once an **implementer subagent** completes, merge its work to the integration branch with a **merger subagent** (`flash` model).

6. If this changes the **frontier** of available tickets, kick off more **implementer subagents** (`flash` model) to work on the new tickets. This allows for maximum concurrency within the concurrency cap.

7. **Review & Convergence Loop** (maximum 8 iterations):
   a. Run the `code-review` skill (`pro` model subagents) on the integration branch.
   b. Use a **review planner agent** (`pro` model) to evaluate the review findings against the spec and tickets:
      - Classify findings into **Fix** (hard violations, missing requirements) vs **Keep** (low-leverage smells, diminishing returns).
      - Check for convergence: if an inconsistent spec makes a defect unfixable, stop the loop and record the conflict.
      - Write the fix plan and post a summary comment to the spec.
   c. If no **Fix** items remain or iteration limit is reached, proceed to step 8.
   d. Otherwise, dispatch an **implementer subagent** (`flash` model) to resolve the fix plan (verifying with [`test_executor.py`](../revit-tests/scripts/test_executor.py)), merge to the integration branch, and repeat from 7a (filtering out previously kept items during triage).

8. If a draft PR exists, mark it ready for review. Otherwise, resolve each ticket the way the issue tracker closes work, and report the integration branch. Present any kept or unresolved defects to the user for final sign-off.

9. Clean up all **implementer subagent** worktrees.
