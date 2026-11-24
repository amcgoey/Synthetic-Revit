---
name: retro
description: "Conduct a retrospective on a coding session."
disable-model-invocation: true
---

The user has asked for a **retrospective**. You are suggesting improvements to the coding agent's **environment** to improve future runs.

## Steps

1. Call the Skill tool with `writing-for-agents` for the writing style guide.

2. Read the primary sources for the session the user specifies. This may mean searching through session logs on this machine. If the user doesn't specify a session, default to the current one.

3. Look for candidates for improvement in these categories:

- **Navigation**: how easy was it for the agent to find the right files? Are there hidden dependencies between files? Would a **navigation pointer** in [`.agents/AGENTS.md`](../../AGENTS.md) or [`docs/agents/`](../../../docs/agents/) make it easier? _Use when_ the session took a long time to find a piece of information.
- **Automated checks**: are there automated checks that could catch errors the agent made? Compilation (`MSBuild.exe` / `dotnet build`), headless **Logic Tests** (`test_executor.py --version Logic`), multi-version **Revit Tests** (`test_executor.py --version 2026`), or journal telemetry (`revit_journal_tool.py`)? Read the repo's own check commands first so a check that already exists but sits unwired or silently broken is the finding, not a reinvention. A repo with no **guardrail** is itself a finding. _Use when_ the agent made a mistake an automated check could have caught, or the repo has no guardrail at all.
- **Coding standards**: should the **reviewer agent** be given a new rule to enforce? Should an existing rule be removed or clarified? Classify the violation first: a **mechanical** one (a fixed syntactic pattern, a banned Revit API, a file-location rule) gets a deterministic `FitnessRule` check in [`ArchitectureFitnessTests.cs`](../../../tests/Architecture/Logic/ArchitectureFitnessTests.cs), full stop. Default to building the check over writing the rule. Reserve [`CODING_STANDARDS.md`](../../../CODING_STANDARDS.md) or the applicable `revit-*` playbooks for genuine **judgement calls** (cross-version preprocessor strategy, WPF MVVM separation, failure handling). _Use when_ the reviewer agent failed to catch a mistake.
- **Project AGENTS.md**: are there any steering instructions that should be moved to coding standards (or automated checks) instead? Check [`.agents/AGENTS.md`](../../AGENTS.md): keep it lean, reserving it for universal rules like direct file editing and relative links. _Use when_ steering files are particularly large or noisy.
- **Tool economy**: did the agent make expensive tool calls that could be streamlined? Did it violate [`.agents/AGENTS.md`](../../AGENTS.md) by attempting to run Python/PowerShell scripts to edit files instead of built-in tools? Did it encounter lock contention on `MSBuild.exe` or `output/Synthetic/`? _Use when_ the agent made an expensive or forbidden tool call.
- **No-ops**: look for instructions in steering files that don't modify the agent's behavior. _Use when_ steering files are large and unwieldy.
- **Information access**: look for opportunities to increase the agent's access to information (e.g. tailing Revit journal session logs, inspecting `RevitDOM` definitions, reading primary API docs). _Use when_ a crucial piece of information was not available to the agent.

4. Present these candidates to the user, in order of severity.

## Reference

### Implementation vs Review

Remember that all work goes through two stages: implementation and review. The implementation agent has the most **context pressure**. They are responsible for exploration, writing code, and debugging failures.

The review agent has the least context pressure: it receives a diff, so no exploration is needed. It does not need to write code or debug.

This means that the review agent should be responsible for imposing coding standards, not the implementation agent.

### Files

You have access to several files in the repo:

- [`.agents/AGENTS.md`](../../AGENTS.md): pushed to the context window of any agent working in this repo. Use sparingly, only for navigation pointers and universal tool constraints.
- [`CODING_STANDARDS.md`](../../../CODING_STANDARDS.md): read during review and design; keep concise and disclose details into `docs/agents/` and `revit-*` playbooks.
- `docs/agents/`: contains persistent reference files (e.g. [`domain.md`](../../../docs/agents/domain.md), [`GLOSSARY.md`](../../../GLOSSARY.md), [`issue-tracker.md`](../../../docs/agents/issue-tracker.md), [`revit-config.md`](../../../docs/agents/revit-config.md)). Point to existing docs before writing new ones.
- Skills (`.agents/skills/`): specialized workflows and domain playbooks (`revit-*`). Follow the advice in the `writing-for-agents` skill.
