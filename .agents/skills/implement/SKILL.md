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
3. **Direct File Editing**: Follow [`.agents/AGENTS.md`](../../AGENTS.md) strictly. Use direct file-editing tools (`replace_file_content`, `multi_replace_file_content`, `write_to_file`). Do not run scripts or command-line one-liners to edit code files.
4. **TDD Loop**: Drive the `tdd` skill at pre-agreed seams. Run tests via `revit-tests` (`python .agents/skills/revit-tests/scripts/test_executor.py`):
   - Run `--version Logic` regularly while working on slices.
   - Run `--version <CURRENT_LATEST_VERSION>` (e.g. `2026`) when working across Revit API boundaries.
   - Run the full multi-version test suite (`python .agents/skills/revit-tests/scripts/test_executor.py` with no `--version` flag) once implementation is complete.
5. **Review**: Run `code-review` against the merge-base or start commit to verify Standards and Spec.
6. **Commit**: Commit verified work to the current branch.
