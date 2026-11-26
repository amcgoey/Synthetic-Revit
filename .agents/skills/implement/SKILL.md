---
name: implement
description: "Implement a piece of work based on a spec or set of tickets."
disable-model-invocation: true
---

Implement the work described by the user in the spec or tickets on the current branch.

The issue tracker workflow is documented in [`docs/agents/issue-tracker.md`](../../../docs/agents/issue-tracker.md). Consult [`CODING_STANDARDS.md`](../../../CODING_STANDARDS.md), [`GLOSSARY.md`](../../../GLOSSARY.md), and [`docs/agents/domain.md`](../../../docs/agents/domain.md) for domain terminology, coding standards, and architecture.

## Workflow

1. **Ticket lookup**: If the user passes a ticket reference, fetch it from the issue tracker and state its title before starting. If the reference is ambiguous, ask.
2. **Context & Standards**: Review applicable `revit-*` skills for the subsystem being modified (`revit-multi-versions`, `revit-transactions`, `revit-wpf-mvvm`, `revit-data-retrieval`, `revit-extensible-storage`, `revit-external-events`, `revit-failure-handling`, `revit-ui-ribbon`, `revit-tests`).
3. **Direct File Editing**: Use direct, built-in file-editing tools for all code and documentation edits per [`.agents/AGENTS.md`](../../AGENTS.md).
4. **TDD Loop**: Drive [`tdd`](../tdd/SKILL.md) at pre-agreed seams and declare what to test via [`test_executor.py`](../revit-tests/scripts/test_executor.py) (see [`revit-tests`](../revit-tests/SKILL.md); multi-version parallelism, sharding, and detached **Worker Pool** execution run automatically):
   - Run `--version Logic` regularly while working on slices.
   - Run `--version <CURRENT_LATEST_VERSION>` ([`revit-config.md`](../../../docs/agents/revit-config.md)) when working across Revit API boundaries.
   - Run `python .agents/skills/revit-tests/scripts/test_executor.py` (omitting `--version` for the full parallel multi-version suite, or passing comma-separated targets such as `--version 2025,2026,2027`) once implementation is complete.
5. **Review**: Run `code-review` against the merge-base or start commit to verify Standards and Spec.
6. **Commit**: Commit verified work to the current branch.
