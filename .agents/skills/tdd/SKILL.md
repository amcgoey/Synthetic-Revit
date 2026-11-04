---
name: tdd
description: Test-driven development. Use when the user wants to build features or fix bugs test-first, mentions "red-green-refactor", or wants integration tests.
---

# Test-Driven Development

TDD is the red → green loop. This skill is the reference that makes that loop produce tests worth keeping: what a good test is, where tests go, the anti-patterns, and the rules of the loop. Every section applies on every cycle: consult them before and during the loop, not after.

When exploring the codebase, read [`GLOSSARY.md`](../../../GLOSSARY.md) (if it exists) so test names and interface vocabulary match the project's domain language, and respect ADRs in `docs/adrs/` in the area you're touching.

## What a good test is

Tests verify behavior through public interfaces, not implementation details. Code can change entirely; tests shouldn't. A good test reads like a specification: "user can merge duplicate materials and retain aliases" tells you exactly what capability exists, and it survives refactors because it doesn't care about internal structure.

See [tests.md](tests.md) for examples and [mocking.md](mocking.md) for mocking guidelines.

## Seams: where tests go

A **seam** is the public boundary you test at: the interface where you observe behavior without reaching inside. Tests live at seams, never against internals.

**Test only at pre-agreed seams.** Before writing any test, write down the seams under test and confirm them with the user. No test is written at an unconfirmed seam. You can't test everything, so agreeing the seams up front is how testing effort lands on the critical paths and complex logic instead of every edge case.

Ask: "What's the public interface, and which seams should we test?" Give each proposed seam a one-line note on what it catches and what it misses.

When the shape of that interface is itself in question (how deep the module is, where the seam belongs, what the interface should expose), consult the [`codebase-design`](../codebase-design/SKILL.md) skill for the vocabulary. It is the shared source of the module, interface, depth, seam, adapter, leverage, and locality terms, and it is a reference to consult, not a session to run.

## Revit Test Execution Sequence

In this Revit Add-in, execute tests in the 3-step sequence defined in [`revit-tests`](../revit-tests/SKILL.md):

1. **Step 1 (Headless Logic Loop):** Fast red-green loop running NUnit tests headlessly against `RevitDOM` POCOs without launching Revit. Iterate until green.
   ```powershell
   python .agents/skills/revit-tests/scripts/test_executor.py --version Logic
   ```
2. **Step 2 (Latest Revit Loop):** Verify Revit database operations and translators against real `Document` fixtures in the active latest version (`CURRENT_LATEST_VERSION` in `docs/agents/revit-config.md`, e.g. 2026). Iterate until green.
   ```powershell
   python .agents/skills/revit-tests/scripts/test_executor.py --version 2026
   ```
3. **Step 3 (Multi-Version Validation):** Once the feature or fix is complete, run tests across all supported Revit versions (2022–2026) to catch cross-version API discrepancies (`revit-multi-versions`).
   ```powershell
   python .agents/skills/revit-tests/scripts/test_executor.py
   ```

## Anti-patterns

- **Implementation-coupled**: mocks internal collaborators, tests private methods, or verifies through a side channel (querying private state instead of using the interface). The tell: the test breaks when you refactor but behavior hasn't changed.
- **Tautological**: the assertion recomputes the expected value the way the code does (`Assert.That(IdentityUtility.ComputeKey(m), Is.EqualTo($"{m.Class}::{m.Name}"))`), so it passes by construction and can never disagree with the code. Expected values must come from an independent source of truth: a known-good literal, a worked example, the spec.
- **Horizontal slicing**: writing all tests first, then all implementation. Bulk tests verify _imagined_ behavior: you test the _shape_ of things rather than user-facing behavior, the tests go insensitive to real changes, and you commit to test structure before understanding the implementation. Work in **vertical slices** instead: one test → one implementation → repeat, each test a **tracer bullet** that responds to what the last cycle taught you.

## Rules of the loop

- **Red before green.** Write the failing test first, then only enough code to pass it. Don't anticipate future tests or add speculative features.
- **One slice at a time.** One seam, one test, one minimal implementation per cycle.
- **Refactoring is not part of the loop.** It belongs to the review stage (see the [`code-review`](../code-review/SKILL.md) skill), not the red → green implementation cycle.
