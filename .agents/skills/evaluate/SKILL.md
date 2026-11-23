---
name: evaluate
description: Critically evaluate a plan, proposal, issue, or code file against Synthetic's architecture and Revit standards.
disable-model-invocation: true
---

# Evaluate

Produce an independent, evidence-backed critique of any target—a plan, proposal, GitHub issue, code file, or inline chat text—in a fresh auditor context.

## 1. Root Agent: Resolve & Dispatch

The root agent resolves the subject, dispatches a fresh-context auditor, and presents the finished artifact link—delegating all analysis to the subagent.

1. **Resolve Subject**: Pin down the exact target before dispatching:
   - **File or artifact**: capture its exact path.
   - **GitHub issue or PR**: fetch title, body, and comments using [`docs/agents/issue-tracker.md`](../../../docs/agents/issue-tracker.md) and pass the retrieved content with its number.
   - **Inline proposal / chat text**: extract the exact text verbatim.
   If the target is ambiguous, ask the user to confirm the exact subject first.
2. **Derive Artifact Path**: Build `<artifactDir>/evaluate_<slug>.md` from the conversation's artifact directory and a short lowercase slug of the subject (e.g. `evaluate_standards_pipeline.md`, `evaluate_issue_184.md`).
3. **Dispatch Auditor**: Define a subagent via `define_subagent` (`Name: "objective-auditor"`, `EnableWriteTools: true`) whose `SystemPrompt` establishes the skeptical, evidence-backed Objective Auditor role and the Write Boundary below. Invoke it via `invoke_subagent` (`TypeName: "objective-auditor"`, `Model: "pro"`, `Role: "Objective Auditor"`), passing the resolved subject, the exact target artifact path, and the instruction to read `.agents/skills/evaluate/SKILL.md` and execute **Section 2: Auditor Protocol**.
4. **Present Result**: When the auditor returns, share a clickable link to the artifact and highlight only the highest-leverage open choices or trade-offs.

## 2. Subagent: Auditor Protocol

Act as a **skeptical, evidence-backed auditor**: cite concrete file paths, symbols, quotes from the subject, ADRs, or playbook rules for every claim, and surface flaws even in polished material.

### Step 1: Gather Ground Truth

1. Read the resolved subject in full.
2. Read the always-applicable baselines:
   - [`docs/agents/domain.md`](../../../docs/agents/domain.md) — follow it into [`GLOSSARY.md`](../../../GLOSSARY.md) and ADRs in [`docs/adrs/`](../../../docs/adrs/) touching the subject's area.
   - [`codebase-design`](../codebase-design/SKILL.md) — shared deep-module vocabulary (`module`, `interface`, `depth`, `seam`, `adapter`, `leverage`, `locality`).
   - [`.agents/AGENTS.md`](../../AGENTS.md) and `CODING_STANDARDS.md` (if present).
3. Inspect relevant code in `src/` and `tests/` to verify the subject's claims against actual types, call sites, and prior art.
4. Load only the playbooks whose branch matches the subject:
   - **WPF views, XAML, ViewModels, or styling** → [`revit-wpf-mvvm`](../revit-wpf-mvvm/SKILL.md), [`revit-external-events`](../revit-external-events/SKILL.md), [`docs/Synthetic_UI_Theme_Specification.md`](../../../docs/Synthetic_UI_Theme_Specification.md)
   - **Revit DB writes, transactions, or native dialogs** → [`revit-transactions`](../revit-transactions/SKILL.md), [`revit-failure-handling`](../revit-failure-handling/SKILL.md)
   - **Revit element queries** → [`revit-data-retrieval`](../revit-data-retrieval/SKILL.md)
   - **Cross-version C#, preprocessor branches, or `.csproj` targets** → [`revit-multi-versions`](../revit-multi-versions/SKILL.md)
   - **In-document persistence or settings schemas** → [`revit-extensible-storage`](../revit-extensible-storage/SKILL.md)
   - **Ribbon tabs, panels, commands, or icons** → [`revit-ui-ribbon`](../revit-ui-ribbon/SKILL.md)
   - **Test strategy or verification plans** → [`tdd`](../tdd/SKILL.md), [`revit-tests`](../revit-tests/SKILL.md)
   - **Build, deployment, or Revit journal diagnostics** → [`revit-build-deploy`](../revit-build-deploy/SKILL.md), [`revit-journal-telemetry`](../revit-journal-telemetry/SKILL.md)

### Step 2: Evaluate Across All Five Axes

Assess the subject across every axis below, separating hard standard/ADR violations from judgement calls:

- **Strategic Alignment**: Alignment with [`GLOSSARY.md`](../../../GLOSSARY.md) ubiquitous language (flagging `_Avoid_` drift), ADRs in [`docs/adrs/`](../../../docs/adrs/) (flagging contradictions explicitly per `docs/agents/domain.md`), and reuse of existing `RevitDOM` and `SyntheticShared` prior art.
- **Structural Integrity**: Module **depth**, **seam** placement, **adapters**, **leverage**, and **locality** ([`codebase-design`](../codebase-design/SKILL.md)); keeping live `Autodesk.Revit.DB`/`UI` types behind seams and out of pure logic or WPF ViewModels; conformance to loaded `revit-*` playbooks, [`.agents/AGENTS.md`](../../AGENTS.md), and `CODING_STANDARDS.md` (if present).
- **Resilience**: Concrete failure modes—edge cases, stale or invalid Revit `Element`/`ElementId` wrappers, transaction or failure-dialog hazards, cross-thread WPF/modeless API violations, cross-version divergence, unhandled states, and testability gaps across headless `Logic` vs. live-Revit seams.
- **Simplicity**: Occam's razor and the **deletion test**—shallow pass-throughs, hypothetical single-adapter seams, unnecessary state, or moving parts that can collapse while preserving leverage.
- **Industry Best Practices**: Idiomatic C# / .NET / WPF craft beyond repo rules—deterministic disposal of unmanaged/Revit resources, explicit invariants and contracts, failure visibility, and structural code smells where no repo standard overrides them.

### Step 3: Deliver Artifact

Write the evaluation to the assigned `<artifactDir>/evaluate_<slug>.md` using `write_file` with `ArtifactMetadata` (`UserFacing: true`, `RequestFeedback: false`), then return its path and a concise summary of top findings to the root agent.

### Completion Criteria

The evaluation is complete only when the artifact contains every section below, grounded in cited evidence:

1. **Verdict & Scope**: Exact subject evaluated, reference docs/playbooks actually consulted, and a concise summary of primary strengths and highest-risk flaws.
2. **Five-Axis Analysis**: All five axes explicitly covered with cited findings (or an explicit *no material flaws found* noting what was verified).
3. **Direct Modifications**: Numbered, actionable fixes for every flaw identified, each naming the exact target section/file and governing ADR, playbook, or principle.
4. **Viable Alternatives**: At least one fundamentally different approach that shifts a different axis (e.g. simplicity vs. extensibility, or alternative seam placement), with explicit trade-offs against the current proposal.

**Write Boundary**: Write only the new evaluation artifact at `<artifactDir>/evaluate_<slug>.md`. Leave all repository files, existing artifacts, and issues untouched.
