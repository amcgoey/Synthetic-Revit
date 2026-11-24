---
name: diagnosing-bugs
description: Diagnosis loop for hard bugs and performance regressions. Use when the user says "diagnose"/"debug this", or reports something broken/throwing/failing/slow.
---

# Diagnosing Bugs

A discipline for hard bugs. Skip phases only when explicitly justified.

When exploring the codebase, read [`GLOSSARY.md`](../../../GLOSSARY.md) to get a clear mental model of the relevant modules, and check ADRs in [`docs/adrs/`](../../../docs/adrs/) and rules in [`CODING_STANDARDS.md`](../../../CODING_STANDARDS.md).

## Redact

This skill has you show commands, outputs, and captured artifacts. **Redact every secret first**: write `<REDACTED>` in its place. Build loops against env vars, so credentials stay in the environment rather than in what you show. Captured artifacts carry local paths and auth tokens: quote only the lines that carry the signal.

If the redacted output is not enough to diagnose the bug, say so and ask the user.

## Phase 1: Build a feedback loop

**This is the skill.** Everything else is mechanical. If you have a **tight** pass/fail signal for the bug (one that goes red on _this_ bug), you will find the cause; bisection, hypothesis-testing, and instrumentation all just consume it. If you don't have one, no amount of staring at code will save you.

Spend disproportionate effort here. **Be aggressive. Be creative. Refuse to give up.**

### Ways to construct one in Revit Add-in development, in roughly this order

1. **Headless Logic Test (`Synthetic.LogicTests`)** via `python .agents/skills/revit-tests/scripts/test_executor.py --version Logic --filter "<TestName>"` for bugs in `Core/` modules, ViewModels, **RevitDOM** POCOs, or `ArchitectureFitnessTests`. Runs across `net48` and `net8.0-windows8.0` in seconds.
2. **Live Revit Test (`Synthetic.RevitTests.<Year>`)** via `python .agents/skills/revit-tests/scripts/test_executor.py --version 2026 --filter "<TestName>"` for bugs requiring a live Revit `Document` or active database transaction.
3. **Revit Journal telemetry loop** (`revit-journal-telemetry`) via `python .agents/skills/revit-journal-telemetry/scripts/revit_journal_tool.py --analyze` / `--search "<pattern>"` / `--watch` to inspect unhandled managed exceptions, `DBG_WARN`, orphaned transactions, **RAM Statistics**, and the `[REPRODUCTION TRAIL]` preceding a crash.
4. **Replay a captured template / `RevitDOM` payload.** Save a failing JSON template or element payload to disk; replay it through the translation or merge pipeline in isolation.
5. **Differential multi-version loop** (`test_executor.py` omitting `--version` or targeting `2023`–`2026`) when the bug is version-specific (`revit-multi-versions`).
6. **Interactive Visual Studio + Revit debugger (`revit-debug-by-user`)** via `powershell -File .agents/skills/revit-debug-by-user/scripts/start_debugging.ps1` or `scripts/hitl-loop.template.sh` as last resort when a human must click in the Revit/WPF UI.

Build the right feedback loop, and the bug is 90% fixed.

### Tighten the loop

Treat the loop as a product. Once you have _a_ loop, **tighten** it:

- Can I make it faster? (Run `--version Logic` instead of spawning full Revit; narrow `--filter` to a single method.)
- Can I make the signal sharper? (Assert on the specific exception or property, not generic non-null.)
- Can I make it more deterministic? (Pin test model fixtures, freeze inputs.)

A 30-second flaky loop is barely better than no loop; a 2-second deterministic one is tight, a debugging superpower.

### Non-deterministic bugs

The goal is not a clean repro but a **higher reproduction rate**. Loop the trigger 100×, parallelise, add stress, narrow timing windows, inject sleeps. A 50%-flake bug is debuggable; 1% is not, so keep raising the rate until it's debuggable.

### When you genuinely cannot build a loop

Stop and say so explicitly. List what you tried. Ask the user for: (a) access to whatever environment reproduces it, (b) a redacted captured artifact (**Revit Journal**, **Crash Dump**, test model `.rvt`, screen recording with timestamps), or (c) permission to add temporary telemetry. Do **not** proceed to hypothesise without a loop.

### Completion criterion: a tight loop that goes red

Phase 1 is done when the loop is **tight** and **red-capable**: you can name **one command** (a test invocation, a python script, a journal command) that you have **already run at least once** (show the invocation and its output, redacted), and that is:

- [ ] **Red-capable**: it drives the actual bug code path and asserts the **user's exact symptom**, so it can go red on this bug and green once fixed. Not "runs without erroring"; it must be able to _catch this specific bug_.
- [ ] **Deterministic**: same verdict every run (flaky bugs: a pinned, high reproduction rate, per above).
- [ ] **Fast**: seconds, not minutes.
- [ ] **Agent-runnable**: you can run it unattended; a human in the loop only via `scripts/hitl-loop.template.sh`.

If you catch yourself reading code to build a theory before this command exists, **stop: jumping straight to a hypothesis is the exact failure this skill prevents.** No red-capable command, no Phase 2.

## Phase 2: Reproduce + minimise

Run the loop. Watch it go red as the bug appears.

Confirm:

- [ ] The loop produces the failure mode the **user** described, not a different failure that happens to be nearby. Wrong bug = wrong fix.
- [ ] The failure is reproducible across multiple runs (or, for non-deterministic bugs, reproducible at a high enough rate to debug against).
- [ ] You have captured the exact symptom (error message, stack trace, wrong property) so later phases can verify the fix actually addresses it.

### Minimise

Once it's red, shrink the repro to the **smallest scenario that still goes red**. Cut elements, parameters, callers, config, and steps **one at a time**, re-running the loop after each cut, and keep only what's load-bearing for the failure.

Done when **every remaining element is load-bearing**: removing any one of them makes the loop go green.

Do not proceed until you have reproduced **and** minimised.

## Phase 3: Hypothesise

Generate **3–5 ranked hypotheses** before testing any of them. Single-hypothesis generation anchors on the first plausible idea.

Each hypothesis must be **falsifiable**: state the prediction it makes.

> Format: "If <X> is the cause, then <changing Y> will make the bug disappear / <changing Z> will make it worse."

If you cannot state the prediction, the hypothesis is a vibe: discard or sharpen it.

**Show the ranked list to the user before testing.** They often have domain knowledge that re-ranks instantly, or know hypotheses they've already ruled out. Don't block on it; proceed with your ranking if the user is AFK.

## Phase 4: Instrument

Each probe must map to a specific prediction from Phase 3. **Change one variable at a time.**

Tool preference:

1. **Revit Journal telemetry** via `revit_journal_tool.py --watch` or `--search` to inspect exceptions and transaction boundaries.
2. **Targeted logs / Debug statements** at the boundaries that distinguish hypotheses.
3. **Interactive Visual Studio debugger** (`revit-debug-by-user`) if stepping through unmanaged Revit API execution is required.
4. Never "log everything and grep".

**Tag every debug log** with a unique prefix, e.g. `[DEBUG-a4f2]`. Cleanup at the end becomes a single grep. Untagged logs survive; tagged logs die.

**Perf branch.** For memory growth or performance regressions, check RAM statistics in the journal (`revit_journal_tool.py --analyze`) and measure transaction durations. Measure first, fix second.

## Phase 5: Fix + regression test

Write the regression test **before the fix**, but only if there is a **correct seam** for it.

A correct seam is one where the test exercises the **real bug pattern** as it occurs at the call site. If the only available seam is too shallow, a regression test there gives false confidence.

**If no correct seam exists, that itself is the finding.** Note it. The codebase architecture is preventing the bug from being locked down. Flag this for the next phase.

If a correct seam exists:

1. Turn the minimised repro into a failing test at that seam (`Synthetic.LogicTests` or `Synthetic.RevitTests.<Year>`).
2. Watch it fail. If you forced the red by mutating code or a fixture, `diff` against a pristine copy to prove the mutation landed before you trust it.
3. Apply the fix.
4. Watch it pass.
5. Re-run the Phase 1 feedback loop against the original (un-minimised) scenario.

## Phase 6: Cleanup + post-mortem

Required before declaring done:

- [ ] Original repro no longer reproduces (re-run the Phase 1 loop)
- [ ] Regression test passes (or absence of seam is documented)
- [ ] All `[DEBUG-...]` instrumentation removed (`grep` the prefix)
- [ ] Throwaway test fixtures deleted (or moved to a clearly-marked test asset folder)
- [ ] The hypothesis that turned out correct is stated in the commit / PR message, so the next debugger learns

**Then ask: what would have prevented this bug?**
- If process, testing, or environment failed: run the `/retro` skill to extract lessons.
- If the answer involves architectural change (no good test seam, tangled callers, hidden coupling): hand off to the `/improve-codebase-architecture` skill with the specifics. Make the recommendation **after** the fix is in, not before — you have more information now than when you started.
