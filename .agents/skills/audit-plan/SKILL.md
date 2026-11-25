---
name: audit-plan
description: Audit a completed plan, artifact, or issue across traceability, logic, deep-module architecture, and Revit add-in integration using a team of subagents.
disable-model-invocation: true
---

When the user invokes `/audit-plan [target]`, execute this workflow to audit a completed plan, artifact, or issue using a multi-agent team.

## 1. Root Agent: Dispatch Orchestrator

The root agent dispatches the orchestrator subagent and conducts the resolution interview once the audit report is written.

1. **Resolve Target**: Identify the exact target (e.g., `implementation_plan.md`, a spec artifact, or a GitHub issue via [`docs/agents/issue-tracker.md`](../../../docs/agents/issue-tracker.md)).
2. **Locate Transcript**: Determine the absolute path to the current conversation's transcript directory (`<appDataDir>\brain\<conversation-id>\.system_generated\logs\transcript.jsonl`).
3. **Dispatch Orchestrator**: Invoke `invoke_subagent` (`TypeName: "architect-auditor"`, `Role: "Senior Auditor"`). Pass the target, transcript path, and artifact directory path, and instruct it to execute the **Senior Auditor Protocol** below.
4. **Handoff to [`grill-with-docs`](../grill-with-docs/SKILL.md)**: When the Senior Auditor reports completion, immediately start an interactive [`grilling`](../grilling/SKILL.md) + [`domain-modeling`](../domain-modeling/SKILL.md) session with the user:
   - Work through the **Open Decisions** and **Recommended Modifications** in `plan_audit_report.md` in **design-tree rounds**, asking the **frontier** questions with your recommended answers formatted so "yes" accepts the recommendation.
   - Capture resolved domain terms inline in [`GLOSSARY.md`](../../../GLOSSARY.md) and record qualifying architectural trade-offs as ADRs in `docs/adrs/` using [`domain-modeling`](../domain-modeling/SKILL.md).
   - Update the audited target with all confirmed modifications once the frontier is empty.

## 2. Senior Auditor Protocol

As the Senior Auditor, you orchestrate the four specialist auditors, reconcile their findings, organize them by architectural impact, formulate concrete plan modifications, and write `plan_audit_report.md`.

### Step A: Spawn the Audit Team

Spawn all 4 specialist subagents concurrently via `invoke_subagent` with fresh context windows, using each role's `TypeName` below. Pass the target material to each:

1. **Role: Traceability Auditor** (`TypeName: "codebase-researcher"`)
   - **Prompt**: Read the provided `transcript.jsonl` file (and `transcript_full.jsonl` for any relevant lines marked with `truncated_fields`). Compare the full conversation history against the target plan. Report every dropped requirement, missing detail, unrecorded decision, or ignored user instruction.
2. **Role: Stress-Test Auditor** (`TypeName: "architect-auditor"`)
   - **Prompt**: Review the target plan for logic holes, edge cases, Revit API lifecycle and threading hazards (mutations outside `DocumentTransactionExtensions`, cross-thread Revit API calls from modeless WPF windows without `ExternalEvent`, invalidated or null `ElementId`/Revit wrappers, unhandled Revit failure dialogs, multi-version API behavior differences across supported **Revit Versions**), and gaps in the 3-step test strategy ([`tdd`](../tdd/SKILL.md) / [`revit-tests`](../revit-tests/SKILL.md): headless **Logic Tests** vs. live **Revit Tests**). Report every resilience and testability flaw.
3. **Role: Architecture Auditor** (`TypeName: "architect-auditor"`)
   - **Prompt**: Read [`docs/agents/domain.md`](../../../docs/agents/domain.md), [`GLOSSARY.md`](../../../GLOSSARY.md), `docs/adrs/`, [`CODING_STANDARDS.md`](../../../CODING_STANDARDS.md), [`.agents/AGENTS.md`](../../AGENTS.md), [`codebase-design`](../codebase-design/SKILL.md) (including [`DEEPENING.md`](../codebase-design/DEEPENING.md)), and the `revit-*` skill playbooks touched by the plan (`revit-wpf-mvvm`, `revit-transactions`, `revit-multi-versions`, `revit-data-retrieval`, `revit-extensible-storage`, `revit-external-events`, `revit-failure-handling`, `revit-ui-ribbon`). Evaluate **module depth**, **seam** placement, **adapters**, **leverage**, and **locality**: check whether live `Autodesk.Revit.DB`/`UI` types leak past the seam into WPF ViewModels or pure `Core/` logic instead of using pure POCOs or an `I<Slice>Adapter` port, apply the **deletion test** to spot shallow pass-throughs or single-adapter indirection, and check adherence to `CODING_STANDARDS.md` and Occam's razor. Report every structural, vocabulary, or standards violation.
4. **Role: Integration Auditor** (`TypeName: "codebase-researcher"`)
   - **Prompt**: Scan the codebase to evaluate how the plan wires into existing subsystems (`src/Features/<Slice>/`, `src/RevitDOM/`, `src/Platform/{Host, IO, Settings, UI, Revit}`, `Synthetic.Core`, `Synthetic<Year>`, `ribbon_config.json`, `SettingsModuleRegistry`, `SyntheticSettings.template.json`). Identify existing modules or prior art that should be reused or deepened rather than duplicated, missing call-site integrations, breaking changes, and zombie code that must be deleted. Report every integration risk.

### Step B: Reconcile and Deduplicate

Wait for all 4 specialist subagents to return their reports, then reconcile every finding:

1. **Cross-Check**: Merge duplicate observations and surface conflicting assessments across the 4 reports.
2. **Autonomous Resolution**: Settle factual discrepancies and clear-cut standards questions directly against the codebase, [`GLOSSARY.md`](../../../GLOSSARY.md), `docs/adrs/`, [`CODING_STANDARDS.md`](../../../CODING_STANDARDS.md), and the `revit-*` skills. Record each autonomous resolution and its rationale.
3. **Escalate Ambiguities**: Route genuine architectural trade-offs, scope questions, and conflicting requirements that require human judgement to **Open Decisions**.

### Step C: Organize by Architectural Impact

Group all reconciled findings by how they affect the codebase architecture:

- **Domain Boundaries & Ubiquitous Language**: Alignment with [`GLOSSARY.md`](../../../GLOSSARY.md) terms, domain invariants, and existing ADRs in `docs/adrs/`.
- **Module Depth, Seams & Adapters**: Deep vs. shallow modules ([`codebase-design`](../codebase-design/SKILL.md)), seam placement between pure `Core/` / **RevitDOM** POCOs and the unmanaged Revit API, WPF MVVM decoupling ([`revit-wpf-mvvm`](../revit-wpf-mvvm/SKILL.md)), and eliminating single-adapter indirection.
- **Revit API Lifecycle, Transactions & Threading**: `DocumentTransactionExtensions` placement ([`revit-transactions`](../revit-transactions/SKILL.md)), `FailurePipelines` dialog resolution ([`revit-failure-handling`](../revit-failure-handling/SKILL.md)), modeless `ExternalEvent` lifecycle ([`revit-external-events`](../revit-external-events/SKILL.md)), `FilteredElementCollector` optimization ([`revit-data-retrieval`](../revit-data-retrieval/SKILL.md)), and single-field JSON Extensible Storage ([`revit-extensible-storage`](../revit-extensible-storage/SKILL.md)).
- **Multi-Version & Add-in Integration**: Inverted legacy `#if REVIT2023` / `#elif REVIT2024` preprocessor hierarchy across supported **Revit Versions** ([`GLOSSARY.md`](../../../GLOSSARY.md), with `CURRENT_LATEST_VERSION` in [`revit-config.md`](../../../docs/agents/revit-config.md) in unconditioned `#else` and zero `#if`/`#elif REVIT<CURRENT_LATEST_VERSION>` repo-wide per [`revit-multi-versions`](../revit-multi-versions/SKILL.md)), `ribbon_config.json` wiring ([`revit-ui-ribbon`](../revit-ui-ribbon/SKILL.md)), prior art reuse, and zombie code cleanup.
- **Resilience & Testing Gaps**: Edge cases, invalid/stale element handling, and test coverage across the 3-step [`tdd`](../tdd/SKILL.md) / [`revit-tests`](../revit-tests/SKILL.md) sequence (headless **Logic Tests** at confirmed seams vs. live **Revit Tests**).

### Step D: Formulate Recommended Modifications

For every finding:

1. Ground the fix in [`docs/agents/domain.md`](../../../docs/agents/domain.md), [`GLOSSARY.md`](../../../GLOSSARY.md), `docs/adrs/`, [`CODING_STANDARDS.md`](../../../CODING_STANDARDS.md), [`codebase-design`](../codebase-design/SKILL.md), and the applicable `revit-*` skills.
2. Draft concrete, numbered edits to the target plan that resolve the flaw and cite the governing standard, ADR, or skill.

### Step E: Compile Report Artifact

Write the complete audit report to `<artifactDir>/plan_audit_report.md` (with `ArtifactMetadata` set to `UserFacing: true`, `RequestFeedback: false`) using this structure:

1. **Executive Summary**: High-level outcome, primary risks, and strengths of the plan.
2. **Architectural Findings**: Reconciled findings organized under the five Step C headings.
3. **Recommended Modifications**: Numbered, actionable edits to the target plan with rationales tied to repo standards, ADRs, and skills.
4. **Open Decisions**:
   - **User Decisions Needed**: Frontier questions formatted for [`grilling`](../grilling/SKILL.md) (`❓ **Q1** - **<Title>**: <Context> ➡️ <Recommended Answer>`).
   - **Autonomous Resolutions**: Table of conflicts or duplicates settled in Step B and the rule or codebase fact that resolved each.

### Step F: Completion

Once `plan_audit_report.md` is written and verified, return to the root agent with:

> *"Audit complete. Report generated at `plan_audit_report.md` with [N] recommended modifications and [M] open decisions. Ready for /grill-with-docs session."*
