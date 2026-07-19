### File: .agents/AGENTS.md
```markdown
# Project Rules

## Documentation Links
- **Relative Links:** Always use relative links (relative to the repository root or the document's parent directory) for all markdown files (`.md`) inside the repository. Never use absolute `file:///` URLs or local user paths (such as `C:\Users\...`).

## Agent skills

### Issue tracker

GitHub Issues. See [issue-tracker.md](../docs/agents/issue-tracker.md).

### Domain docs

Single-context layout. See [domain.md](../docs/agents/domain.md).

```

### File: .agents/skills/ask-matt/SKILL.md
```markdown
---
name: ask-matt
description: Ask which skill or flow fits your situation. A router over the skills in this repo.
disable-model-invocation: true
---

# Ask Matt

You don't remember every skill, so ask.

A **flow** is a path through the skills. Most paths run along one **main flow**, and two **on-ramps** merge onto it. Everything else is standalone, or a vocabulary layer that runs underneath.

## The main flow: idea → ship

The route most work travels. You have an idea and want it built.

1. **`/grill-with-docs`** — sharpen the idea by interview. Start here when you **have a codebase**: it's stateful, retaining what it learns in `CONTEXT.md` and ADRs. (No codebase? Use `/grill-me` — see Standalone. Both run the same `/grilling` primitive; `grill-with-docs` is the one that leaves a paper trail.)
2. **Branch — can you settle every question in conversation?** If a question needs a runnable answer (state, business logic, a UI you have to see), detour through a prototype, bridged by **`/handoff`** in both directions (see Crossing sessions):
   - **`/handoff`** out, then open a fresh session against that file,
   - **`/prototype`** to answer the question with throwaway code,
   - **`/handoff`** back what you learned, and reference it from the original idea thread.
3. **Branch — is this a multi-session build?**
   - **Yes** → **`/to-spec`** (turn the thread into a spec), then **`/to-tickets`** to split it into tracer-bullet tickets, each declaring its **blocking edges**. On a local tracker that's one file per ticket under `.scratch/<feature>/issues/`, worked blockers-first by hand; on a real tracker the edges become native blocking links, so any ticket whose blockers are done can be grabbed — kick off **`/implement`** per ticket, **clearing context between each one**.
   - **No** → **`/implement`** right here, in the same context window.

   Either way, **`/implement`** builds each issue by driving **`/tdd`** internally — one red-green slice at a time — then closes out by running **`/code-review`**, a two-axis review (Standards + Spec) of the diff, before committing. Reach for **`/tdd`** on its own when you just want to build a concrete behaviour test-first without a full spec, and **`/code-review`** on its own whenever you want to review a branch or PR against a fixed point.

### Context hygiene

Keep steps 1–3 in **one unbroken context window** — don't compact or clear until after `/to-tickets` — so the grilling, spec, and tickets all build on the same thinking. Each `/implement` then starts fresh, working from the ticket.

The limit on this is the **[smart zone](https://www.aihero.dev/ai-coding-dictionary/smart-zone)**: the window (~120k tokens on state-of-the-art models) within which the model still reasons sharply. If a session approaches it before `/to-tickets`, don't push on degraded — `/handoff` and continue in a fresh thread.

## On-ramps

A starting situation that generates work, then merges onto the main flow.

- **Bugs and requests piling up** → **`/triage`**. It moves issues through triage roles and produces agent-ready issues, which **`/implement`** later picks up.

  Triage is only for issues **you didn't create** — bug reports, incoming feature requests, anything that arrives raw. Tickets that `/to-tickets` produced are already agent-ready, so **don't triage them**.

- **Something's broken** → **`/diagnosing-bugs`**. For the hard ones: the bug that resists a first glance, the intermittent flake, the regression that crept in between two known-good states. It refuses to theorise until it has a **tight feedback loop** — one command that already goes red on *this* bug — then fixes with a regression test. Its post-mortem hands off to **`/improve-codebase-architecture`** when the real finding is that there's no good seam to lock the bug down.

- **A huge, foggy effort — a greenfield project or a huge feature build, too big for one session** → **`/wayfinder`**, the most cognitively demanding flow here. When the way from here to the destination isn't visible yet, it charts a **shared map** of **decision tickets** on the issue tracker and resolves them one at a time — producing **decisions, not deliverables** — until the fog is pushed back and the way is clear. Where **`/grill-with-docs`** sharpens an idea you can hold in one session, wayfinder is for the idea you can't — and it's slower and denser, so save it for exactly that, never a well-scoped feature.

  When the map clears, **it hands off, it doesn't build**: merge onto the main flow at **`/to-spec`**, which collapses the map's linked decisions into a buildable plan, then `/to-tickets` and `/implement` as usual. Looping the map straight into `/implement` skips that collapse and throws the linked detail away — go straight to `/implement` only when the effort turned out genuinely small.

## Codebase health

Not feature work — upkeep.

- **`/improve-codebase-architecture`** — run whenever you have a spare moment to keep the codebase good for agents to operate in. It surfaces **deepening opportunities**; picking one _generates an idea_ you can take into the main flow at `/grill-with-docs`. It's the survey that finds the candidates; **`/codebase-design`** (below) is the bench you design the chosen one on.

## Vocabulary underneath

Two model-invoked references that run *beneath* the other skills — each the single source of truth for its vocabulary. Reach for them directly when the **words**, not the process, are the problem; or let the skills above pull them in.

- **`/domain-modeling`** — sharpen the project's *domain* language: challenge a fuzzy term, resolve an overloaded word ("account" doing three jobs), record a hard-to-reverse decision as an ADR. It's the active discipline `/grill-with-docs` drives to keep `CONTEXT.md` a clean glossary.
- **`/codebase-design`** — the deep-module vocabulary (module, interface, depth, seam, adapter, leverage, locality) for designing a module's *shape*: a lot of behaviour behind a small interface at a clean seam. `/tdd` and `/improve-codebase-architecture` both speak it.

## Crossing sessions

- **`/handoff`** — when a thread is full or you need to branch off (e.g. into a `/prototype` session), this compacts the conversation into a markdown file. You don't continue in place — you **open a new session and reference that file** to carry the context across. It's the bridge between context windows, in either direction. Use it when you want a **fresh session** but need the **current conversation preserved**.
- **`/compact`** (built-in) — stay in the **same conversation**, letting the earlier turns be summarized. Use it at **intentional breaks between phases**, when you don't mind losing the verbatim history. Don't compact mid-phase — the agent can lose its way. `/handoff` forks; `/compact` continues.

## Standalone

Off the main flow entirely.

- **`/grill-me`** — the same relentless interview as `/grill-with-docs`, but for when you have **no codebase**. Stateless: it saves nothing locally, builds no `CONTEXT.md`. Reach for it to sharpen any plan or design that doesn't live in a repo.
- **`/prototype`** — a small, throwaway program that answers one design question: does this state model feel right, or what should this UI look like. Throwaway from day one — keep the answer, delete the code. It's the detour in step 2 of the main flow, but reach for it any time a design question is hard to settle on paper.
- **`/research`** — delegate reading legwork to a **background agent**: it investigates a question against **primary sources**, then leaves a cited Markdown file in the repo. Keep working while it reads. The file it produces is something to take *into* the main flow at `/grill-with-docs` — research feeds the thinking, it doesn't replace it.
- **`/teach`** — learn a concept over multiple sessions, using the current directory as a stateful workspace.
- **`/writing-great-skills`** — reference for writing and editing skills well.

## Precondition

**`/setup-matt-pocock-skills`** — run before your first engineering flow to configure the issue tracker, triage labels, and doc layout the other skills assume. Custom issue trackers also work.
```

### File: .agents/skills/claude-handoff/SKILL.md
```markdown
---
name: claude-handoff
description: Hand the current conversation off to a fresh background agent that picks up the work immediately.
argument-hint: "What will the next session be used for?"
disable-model-invocation: true
---

Write a handoff summary of the current conversation so a fresh agent can continue the work. Instead of saving it, launch a background agent seeded with the summary as its prompt: `claude --bg --name "<descriptive name>" "<handoff summary>"`. It starts in the current working directory and returns immediately; the user manages it with `claude agents`.

Always pass `-n`/`--name` with a descriptive name (e.g. `--name "Fix login bug"`) — it sets the display name shown in the job list, session picker, and terminal title.

Include a "suggested skills" section in the summary, which suggests skills that the agent should invoke.

Do not duplicate content already captured in other artifacts (PRDs, plans, ADRs, issues, commits, diffs). Reference them by path or URL instead.

Redact any sensitive information, such as API keys, passwords, or personally identifiable information — the summary becomes the agent's prompt.

If the user passed arguments, treat them as a description of what the next session will focus on and tailor the summary accordingly.
```

### File: .agents/skills/code-review/SKILL.md
```markdown
---
name: code-review
description: Review the changes since a fixed point (commit, branch, tag, or merge-base) along two axes — Standards (does the code follow this repo's documented coding standards?) and Spec (does the code match what the originating issue/PRD asked for?). Runs both reviews in parallel sub-agents and reports them side by side. Use when the user wants to review a branch, a PR, work-in-progress changes, or asks to "review since X".
---

Two-axis review of the diff between `HEAD` and a fixed point the user supplies:

- **Standards** — does the code conform to this repo's documented coding standards?
- **Spec** — does the code faithfully implement the originating issue / PRD / spec?

Both axes run as **parallel sub-agents** so they don't pollute each other's context, then this skill aggregates their findings.

The issue tracker should have been provided to you — run `/setup-matt-pocock-skills` if `docs/agents/issue-tracker.md` is missing.

## Process

### 1. Pin the fixed point

Whatever the user said is the fixed point — a commit SHA, branch name, tag, `main`, `HEAD~5`, etc. If they didn't specify one, ask for it.

Capture the diff command once: `git diff <fixed-point>...HEAD` (three-dot, so the comparison is against the merge-base). Also note the list of commits via `git log <fixed-point>..HEAD --oneline`.

Before going further, confirm the fixed point resolves (`git rev-parse <fixed-point>`) and the diff is non-empty. A bad ref or empty diff should fail here — not inside two parallel sub-agents.

### 2. Identify the spec source

Look for the originating spec, in this order:

1. Issue references in the commit messages (`#123`, `Closes #45`, GitLab `!67`, etc.) — fetch via the workflow in `docs/agents/issue-tracker.md`.
2. A path the user passed as an argument.
3. A PRD/spec file under `docs/`, `specs/`, or `.scratch/` matching the branch name or feature.
4. If nothing is found, ask the user where the spec is. If they say there isn't one, the **Spec** sub-agent will skip and report "no spec available".

### 3. Identify the standards sources

Anything in the repo that documents how code should be written, such as `CODING_STANDARDS.md` or `CONTRIBUTING.md`.

On top of whatever the repo documents, the Standards axis always carries the **smell baseline** below — a fixed set of Fowler code smells (_Refactoring_, ch.3) that applies even when a repo documents nothing. Two rules bind it:

- **The repo overrides.** A documented repo standard always wins; where it endorses something the baseline would flag, suppress the smell.
- **Always a judgement call.** Each smell is a labelled heuristic ("possible Feature Envy"), never a hard violation — and, like any standard here, skip anything tooling already enforces.

Each smell reads *what it is* → *how to fix*; match it against the diff:

- **Mysterious Name** — a function, variable, or type whose name doesn't reveal what it does or holds. → rename it; if no honest name comes, the design's murky.
- **Duplicated Code** — the same logic shape appears in more than one hunk or file in the change. → extract the shared shape, call it from both.
- **Feature Envy** — a method that reaches into another object's data more than its own. → move the method onto the data it envies.
- **Data Clumps** — the same few fields or params keep travelling together (a type wanting to be born). → bundle them into one type, pass that.
- **Primitive Obsession** — a primitive or string standing in for a domain concept that deserves its own type. → give the concept its own small type.
- **Repeated Switches** — the same `switch`/`if`-cascade on the same type recurs across the change. → replace with polymorphism, or one map both sites share.
- **Shotgun Surgery** — one logical change forces scattered edits across many files in the diff. → gather what changes together into one module.
- **Divergent Change** — one file or module is edited for several unrelated reasons. → split so each module changes for one reason.
- **Speculative Generality** — abstraction, parameters, or hooks added for needs the spec doesn't have. → delete it; inline back until a real need shows.
- **Message Chains** — long `a.b().c().d()` navigation the caller shouldn't depend on. → hide the walk behind one method on the first object.
- **Middle Man** — a class or function that mostly just delegates onward. → cut it, call the real target direct.
- **Refused Bequest** — a subclass or implementer that ignores or overrides most of what it inherits. → drop the inheritance, use composition.

### 4. Spawn both sub-agents in parallel

Send a single message with two `Agent` tool calls. Use the `general-purpose` subagent for both.

**Standards sub-agent prompt** — include:

- The full diff command and commit list.
- The list of standards-source files you found in step 3, **plus the smell baseline from step 3** pasted in full — the sub-agent has no other access to it.
- The brief: "Report — per file/hunk where relevant — (a) every place the diff violates a documented standard: cite the standard (file + the rule); and (b) any baseline smell you spot: name it and quote the hunk. Distinguish hard violations from judgement calls — documented-standard breaches can be hard, but baseline smells are always judgement calls, and a documented repo standard overrides the baseline. Skip anything tooling enforces. Under 400 words."

**Spec sub-agent prompt** — include:

- The diff command and commit list.
- The path or fetched contents of the spec.
- The brief: "Report: (a) requirements the spec asked for that are missing or partial; (b) behaviour in the diff that wasn't asked for (scope creep); (c) requirements that look implemented but where the implementation looks wrong. Quote the spec line for each finding. Under 400 words."

If the spec is missing, skip the Spec sub-agent and note this in the final report.

### 5. Aggregate

Present the two reports under `## Standards` and `## Spec` headings, verbatim or lightly cleaned. Do **not** merge or rerank findings — the two axes are deliberately separate (see _Why two axes_).

End with a one-line summary: total findings per axis, and the worst issue _within each axis_ (if any). Don't pick a single winner across axes — that's the reranking the separation exists to prevent.

## Why two axes

A change can pass one axis and fail the other:

- Code that follows every standard but implements the wrong thing → **Standards pass, Spec fail.**
- Code that does exactly what the issue asked but breaks the project's conventions → **Spec pass, Standards fail.**

Reporting them separately stops one axis from masking the other.
```

### File: .agents/skills/codebase-design/DEEPENING.md
```markdown
# Deepening

How to deepen a cluster of shallow modules safely, given its dependencies. Assumes the vocabulary in [SKILL.md](SKILL.md) — **module**, **interface**, **seam**, **adapter**.

## Dependency categories

When assessing a candidate for deepening, classify its dependencies. The category determines how the deepened module is tested across its seam.

### 1. In-process

Pure computation, in-memory state, no I/O. Always deepenable — merge the modules and test through the new interface directly. No adapter needed.

### 2. Local-substitutable

Dependencies that have local test stand-ins (PGLite for Postgres, in-memory filesystem). Deepenable if the stand-in exists. The deepened module is tested with the stand-in running in the test suite. The seam is internal; no port at the module's external interface.

### 3. Remote but owned (Ports & Adapters)

Your own services across a network boundary (microservices, internal APIs). Define a **port** (interface) at the seam. The deep module owns the logic; the transport is injected as an **adapter**. Tests use an in-memory adapter. Production uses an HTTP/gRPC/queue adapter.

Recommendation shape: *"Define a port at the seam, implement an HTTP adapter for production and an in-memory adapter for testing, so the logic sits in one deep module even though it's deployed across a network."*

### 4. True external (Mock)

Third-party services (Stripe, Twilio, etc.) you don't control. The deepened module takes the external dependency as an injected port; tests provide a mock adapter.

## Seam discipline

- **One adapter means a hypothetical seam. Two adapters means a real one.** Don't introduce a port unless at least two adapters are justified (typically production + test). A single-adapter seam is just indirection.
- **Internal seams vs external seams.** A deep module can have internal seams (private to its implementation, used by its own tests) as well as the external seam at its interface. Don't expose internal seams through the interface just because tests use them.

## Testing strategy: replace, don't layer

- Old unit tests on shallow modules become waste once tests at the deepened module's interface exist — delete them.
- Write new tests at the deepened module's interface. The **interface is the test surface**.
- Tests assert on observable outcomes through the interface, not internal state.
- Tests should survive internal refactors — they describe behaviour, not implementation. If a test has to change when the implementation changes, it's testing past the interface.
```

### File: .agents/skills/codebase-design/DESIGN-IT-TWICE.md
```markdown
# Design It Twice

When the user wants to explore alternative interfaces for a chosen deepening candidate, use this parallel sub-agent pattern. Based on "Design It Twice" (Ousterhout) — your first idea is unlikely to be the best.

Uses the vocabulary in [SKILL.md](SKILL.md) — **module**, **interface**, **seam**, **adapter**, **leverage**.

## Process

### 1. Frame the problem space

Before spawning sub-agents, write a user-facing explanation of the problem space for the chosen candidate:

- The constraints any new interface would need to satisfy
- The dependencies it would rely on, and which category they fall into (see [DEEPENING.md](DEEPENING.md))
- A rough illustrative code sketch to ground the constraints — not a proposal, just a way to make the constraints concrete

Show this to the user, then immediately proceed to Step 2. The user reads and thinks while the sub-agents work in parallel.

### 2. Spawn sub-agents

Spawn 3+ sub-agents in parallel using the Agent tool. Each must produce a **radically different** interface for the deepened module.

Prompt each sub-agent with a separate technical brief (file paths, coupling details, dependency category from [DEEPENING.md](DEEPENING.md), what sits behind the seam). The brief is independent of the user-facing problem-space explanation in Step 1. Give each agent a different design constraint:

- Agent 1: "Minimize the interface — aim for 1–3 entry points max. Maximise leverage per entry point."
- Agent 2: "Maximise flexibility — support many use cases and extension."
- Agent 3: "Optimise for the most common caller — make the default case trivial."
- Agent 4 (if applicable): "Design around ports & adapters for cross-seam dependencies."

Include both [SKILL.md](SKILL.md) vocabulary and CONTEXT.md vocabulary in the brief so each sub-agent names things consistently with the architecture language and the project's domain language.

Each sub-agent outputs:

1. Interface (types, methods, params — plus invariants, ordering, error modes)
2. Usage example showing how callers use it
3. What the implementation hides behind the seam
4. Dependency strategy and adapters (see [DEEPENING.md](DEEPENING.md))
5. Trade-offs — where leverage is high, where it's thin

### 3. Present and compare

Present designs sequentially so the user can absorb each one, then compare them in prose. Contrast by **depth** (leverage at the interface), **locality** (where change concentrates), and **seam placement**.

After comparing, give your own recommendation: which design you think is strongest and why. If elements from different designs would combine well, propose a hybrid. Be opinionated — the user wants a strong read, not a menu.
```

### File: .agents/skills/codebase-design/SKILL.md
```markdown
---
name: codebase-design
description: Shared vocabulary for designing deep modules. Use when the user wants to design or improve a module's interface, find deepening opportunities, decide where a seam goes, make code more testable or AI-navigable, or when another skill needs the deep-module vocabulary.
---

# Codebase Design

Design **deep modules**: a lot of behaviour behind a small interface, placed at a clean seam, testable through that interface. Use this language and these principles wherever code is being designed or restructured. The aim is leverage for callers, locality for maintainers, and testability for everyone.

## Glossary

Use these terms exactly — don't substitute "component," "service," "API," or "boundary." Consistent language is the whole point.

**Module** — anything with an interface and an implementation. Deliberately scale-agnostic: a function, class, package, or tier-spanning slice. _Avoid_: unit, component, service.

**Interface** — everything a caller must know to use the module correctly: the type signature, but also invariants, ordering constraints, error modes, required configuration, and performance characteristics. _Avoid_: API, signature (too narrow — they refer only to the type-level surface).

**Implementation** — what's inside a module, its body of code. Distinct from **Adapter**: a thing can be a small adapter with a large implementation (a Postgres repo) or a large adapter with a small implementation (an in-memory fake). Reach for "adapter" when the seam is the topic; "implementation" otherwise.

**Depth** — leverage at the interface: the amount of behaviour a caller (or test) can exercise per unit of interface they have to learn. A module is **deep** when a large amount of behaviour sits behind a small interface, **shallow** when the interface is nearly as complex as the implementation.

**Seam** _(Michael Feathers)_ — a place where you can alter behaviour without editing in that place; the *location* at which a module's interface lives. Where to put the seam is its own design decision, distinct from what goes behind it. _Avoid_: boundary (overloaded with DDD's bounded context).

**Adapter** — a concrete thing that satisfies an interface at a seam. Describes *role* (what slot it fills), not substance (what's inside).

**Leverage** — what callers get from depth: more capability per unit of interface they learn. One implementation pays back across N call sites and M tests.

**Locality** — what maintainers get from depth: change, bugs, knowledge, and verification concentrate in one place rather than spreading across callers. Fix once, fixed everywhere.

## Deep vs shallow

**Deep module** = small interface + lots of implementation:

```
┌─────────────────────┐
│   Small Interface   │  ← Few methods, simple params
├─────────────────────┤
│                     │
│  Deep Implementation│  ← Complex logic hidden
│                     │
└─────────────────────┘
```

**Shallow module** = large interface + little implementation (avoid):

```
┌─────────────────────────────────┐
│       Large Interface           │  ← Many methods, complex params
├─────────────────────────────────┤
│  Thin Implementation            │  ← Just passes through
└─────────────────────────────────┘
```

When designing an interface, ask:

- Can I reduce the number of methods?
- Can I simplify the parameters?
- Can I hide more complexity inside?

## Principles

- **Depth is a property of the interface, not the implementation.** A deep module can be internally composed of small, mockable, swappable parts — they just aren't part of the interface. A module can have **internal seams** (private to its implementation, used by its own tests) as well as the **external seam** at its interface.
- **The deletion test.** Imagine deleting the module. If complexity vanishes, it was a pass-through. If complexity reappears across N callers, it was earning its keep.
- **The interface is the test surface.** Callers and tests cross the same seam. If you want to test *past* the interface, the module is probably the wrong shape.
- **One adapter means a hypothetical seam. Two adapters means a real one.** Don't introduce a seam unless something actually varies across it.

## Designing for testability

Good interfaces make testing natural:

1. **Accept dependencies, don't create them.**

   ```typescript
   // Testable
   function processOrder(order, paymentGateway) {}

   // Hard to test
   function processOrder(order) {
     const gateway = new StripeGateway();
   }
   ```

2. **Return results, don't produce side effects.**

   ```typescript
   // Testable
   function calculateDiscount(cart): Discount {}

   // Hard to test
   function applyDiscount(cart): void {
     cart.total -= discount;
   }
   ```

3. **Small surface area.** Fewer methods = fewer tests needed. Fewer params = simpler test setup.

## Relationships

- A **Module** has exactly one **Interface** (the surface it presents to callers and tests).
- **Depth** is a property of a **Module**, measured against its **Interface**.
- A **Seam** is where a **Module**'s **Interface** lives.
- An **Adapter** sits at a **Seam** and satisfies the **Interface**.
- **Depth** produces **Leverage** for callers and **Locality** for maintainers.

## Rejected framings

- **Depth as ratio of implementation-lines to interface-lines** (Ousterhout): rewards padding the implementation. We use depth-as-leverage instead.
- **"Interface" as the TypeScript `interface` keyword or a class's public methods**: too narrow — interface here includes every fact a caller must know.
- **"Boundary"**: overloaded with DDD's bounded context. Say **seam** or **interface**.

## Going deeper

- **Deepening a cluster given its dependencies** — see [DEEPENING.md](DEEPENING.md): dependency categories, seam discipline, and replace-don't-layer testing.
- **Exploring alternative interfaces** — see [DESIGN-IT-TWICE.md](DESIGN-IT-TWICE.md): spin up parallel sub-agents to design the interface several radically different ways, then compare on depth, locality, and seam placement.
```

### File: .agents/skills/diagnosing-bugs/SKILL.md
```markdown
---
name: diagnosing-bugs
description: Diagnosis loop for hard bugs and performance regressions. Use when the user says "diagnose"/"debug this", or reports something broken/throwing/failing/slow.
---

# Diagnosing Bugs

A discipline for hard bugs. Skip phases only when explicitly justified.

When exploring the codebase, read `CONTEXT.md` (if it exists) to get a clear mental model of the relevant modules, and check ADRs in the area you're touching.

## Phase 1 — Build a feedback loop

**This is the skill.** Everything else is mechanical. If you have a **tight** pass/fail signal for the bug — one that goes red on _this_ bug — you will find the cause; bisection, hypothesis-testing, and instrumentation all just consume it. If you don't have one, no amount of staring at code will save you.

Spend disproportionate effort here. **Be aggressive. Be creative. Refuse to give up.**

### Ways to construct one — try them in roughly this order

1. **Failing test** at whatever seam reaches the bug — unit, integration, e2e.
2. **Curl / HTTP script** against a running dev server.
3. **CLI invocation** with a fixture input, diffing stdout against a known-good snapshot.
4. **Headless browser script** (Playwright / Puppeteer) — drives the UI, asserts on DOM/console/network.
5. **Replay a captured trace.** Save a real network request / payload / event log to disk; replay it through the code path in isolation.
6. **Throwaway harness.** Spin up a minimal subset of the system (one service, mocked deps) that exercises the bug code path with a single function call.
7. **Property / fuzz loop.** If the bug is "sometimes wrong output", run 1000 random inputs and look for the failure mode.
8. **Bisection harness.** If the bug appeared between two known states (commit, dataset, version), automate "boot at state X, check, repeat" so you can `git bisect run` it.
9. **Differential loop.** Run the same input through old-version vs new-version (or two configs) and diff outputs.
10. **HITL bash script.** Last resort. If a human must click, drive _them_ with `scripts/hitl-loop.template.sh` so the loop is still structured. Captured output feeds back to you.

Build the right feedback loop, and the bug is 90% fixed.

### Tighten the loop

Treat the loop as a product. Once you have _a_ loop, **tighten** it:

- Can I make it faster? (Cache setup, skip unrelated init, narrow the test scope.)
- Can I make the signal sharper? (Assert on the specific symptom, not "didn't crash".)
- Can I make it more deterministic? (Pin time, seed RNG, isolate filesystem, freeze network.)

A 30-second flaky loop is barely better than no loop; a 2-second deterministic one is tight — a debugging superpower.

### Non-deterministic bugs

The goal is not a clean repro but a **higher reproduction rate**. Loop the trigger 100×, parallelise, add stress, narrow timing windows, inject sleeps. A 50%-flake bug is debuggable; 1% is not — keep raising the rate until it's debuggable.

### When you genuinely cannot build a loop

Stop and say so explicitly. List what you tried. Ask the user for: (a) access to whatever environment reproduces it, (b) a captured artifact (HAR file, log dump, core dump, screen recording with timestamps), or (c) permission to add temporary production instrumentation. Do **not** proceed to hypothesise without a loop.

### Completion criterion — a tight loop that goes red

Phase 1 is done when the loop is **tight** and **red-capable**: you can name **one command** — a script path, a test invocation, a curl — that you have **already run at least once** (paste the invocation and its output), and that is:

- [ ] **Red-capable** — it drives the actual bug code path and asserts the **user's exact symptom**, so it can go red on this bug and green once fixed. Not "runs without erroring" — it must be able to _catch this specific bug_.
- [ ] **Deterministic** — same verdict every run (flaky bugs: a pinned, high reproduction rate, per above).
- [ ] **Fast** — seconds, not minutes.
- [ ] **Agent-runnable** — you can run it unattended; a human in the loop only via `scripts/hitl-loop.template.sh`.

If you catch yourself reading code to build a theory before this command exists, **stop — jumping straight to a hypothesis is the exact failure this skill prevents.** No red-capable command, no Phase 2.

## Phase 2 — Reproduce + minimise

Run the loop. Watch it go red — the bug appears.

Confirm:

- [ ] The loop produces the failure mode the **user** described — not a different failure that happens to be nearby. Wrong bug = wrong fix.
- [ ] The failure is reproducible across multiple runs (or, for non-deterministic bugs, reproducible at a high enough rate to debug against).
- [ ] You have captured the exact symptom (error message, wrong output, slow timing) so later phases can verify the fix actually addresses it.

### Minimise

Once it's red, shrink the repro to the **smallest scenario that still goes red**. Cut inputs, callers, config, data, and steps **one at a time**, re-running the loop after each cut — keep only what's load-bearing for the failure.

Why bother: a minimal repro shrinks the hypothesis space in Phase 3 (fewer moving parts left to suspect) and becomes the clean regression test in Phase 5.

Done when **every remaining element is load-bearing** — removing any one of them makes the loop go green.

Do not proceed until you have reproduced **and** minimised.

## Phase 3 — Hypothesise

Generate **3–5 ranked hypotheses** before testing any of them. Single-hypothesis generation anchors on the first plausible idea.

Each hypothesis must be **falsifiable**: state the prediction it makes.

> Format: "If <X> is the cause, then <changing Y> will make the bug disappear / <changing Z> will make it worse."

If you cannot state the prediction, the hypothesis is a vibe — discard or sharpen it.

**Show the ranked list to the user before testing.** They often have domain knowledge that re-ranks instantly ("we just deployed a change to #3"), or know hypotheses they've already ruled out. Cheap checkpoint, big time saver. Don't block on it — proceed with your ranking if the user is AFK.

## Phase 4 — Instrument

Each probe must map to a specific prediction from Phase 3. **Change one variable at a time.**

Tool preference:

1. **Debugger / REPL inspection** if the env supports it. One breakpoint beats ten logs.
2. **Targeted logs** at the boundaries that distinguish hypotheses.
3. Never "log everything and grep".

**Tag every debug log** with a unique prefix, e.g. `[DEBUG-a4f2]`. Cleanup at the end becomes a single grep. Untagged logs survive; tagged logs die.

**Perf branch.** For performance regressions, logs are usually wrong. Instead: establish a baseline measurement (timing harness, `performance.now()`, profiler, query plan), then bisect. Measure first, fix second.

## Phase 5 — Fix + regression test

Write the regression test **before the fix** — but only if there is a **correct seam** for it.

A correct seam is one where the test exercises the **real bug pattern** as it occurs at the call site. If the only available seam is too shallow (single-caller test when the bug needs multiple callers, unit test that can't replicate the chain that triggered the bug), a regression test there gives false confidence.

**If no correct seam exists, that itself is the finding.** Note it. The codebase architecture is preventing the bug from being locked down. Flag this for the next phase.

If a correct seam exists:

1. Turn the minimised repro into a failing test at that seam.
2. Watch it fail.
3. Apply the fix.
4. Watch it pass.
5. Re-run the Phase 1 feedback loop against the original (un-minimised) scenario.

## Phase 6 — Cleanup + post-mortem

Required before declaring done:

- [ ] Original repro no longer reproduces (re-run the Phase 1 loop)
- [ ] Regression test passes (or absence of seam is documented)
- [ ] All `[DEBUG-...]` instrumentation removed (`grep` the prefix)
- [ ] Throwaway prototypes deleted (or moved to a clearly-marked debug location)
- [ ] The hypothesis that turned out correct is stated in the commit / PR message — so the next debugger learns

**Then ask: what would have prevented this bug?** If the answer involves architectural change (no good test seam, tangled callers, hidden coupling) hand off to the `/improve-codebase-architecture` skill with the specifics. Make the recommendation **after** the fix is in, not before — you have more information now than when you started.
```

### File: .agents/skills/docs-aggregate/SKILL.md
```markdown
---
name: docs-aggregate
description: How the codebase aggregation script (aggregate.py) resolves source files based on configuration. Use when bundling codebase directories into markdown files.
---

# Codebase Aggregation Playbook (`docs-aggregate`)

Use this playbook to aggregate codebase source directories into structured Markdown files for cloud documentation.

## 1. Stand-alone Aggregation Command

To run the codebase aggregation pipeline on its own:
```powershell
python .agents/skills/docs-aggregate/scripts/aggregate.py
```

## 2. Configuration Structure

The aggregation is driven by `aggregate_config.json` (located in `Revit API Synthetic v2/auth/`). It maps specific source directories to target markdown files:

```json
{
  "aggregations": [
    {
      "source_dir": "Revit API Synthetic v2\\.agents",
      "output_file": "Revit API Synthetic v2\\docs\\source_code\\Synthetic_agents.md",
      "extensions": [".md", ".json", ".py"]
    },
    {
      "source_dir": "Revit API Synthetic v2\\src\\SyntheticShared\\Core",
      "output_file": "Revit API Synthetic v2\\docs\\source_code\\Synthetic_src_Foundation.md"
    }
  ]
}
```

## 3. Aggregation Engine Execution Rules

When the script runs, it resolves files using the following rules:

* **Directory Exclusions:** Recursively walks the target `source_dir` but automatically prunes directories defined in `DEFAULT_EXCLUDES` (e.g. `.git`, `bin`, `obj`, `packages`, `scratch`, `build`) to avoid junk file clutter.
* **Extension Filtering:** Only processes files matching allowed extensions (defaults to `.cs`, `.xaml`, `.md` unless specified per-aggregation).
* **Bundling Append Mode:** If multiple source directories map to the same `output_file`, the script overwrites the file on the first match, and then switches to append mode (`'a'`) to bundle subsequent directories together.
* **Markdown Formatting:** Wraps each file's contents inside a Markdown code block with its relative path header and the appropriate language-specific tag (e.g. `csharp`, `xml`):
  
  ```markdown
  ### File: SyntheticShared/Core/App.cs
  ```csharp
  // Source contents...
  ```
  ```
```

### File: .agents/skills/docs-aggregate/scripts/aggregate.py
```python
import os
import argparse
import re
import sys
import json


# Default directories to ignore to prevent clutter and circular reference
DEFAULT_EXCLUDES = {
    '.git', '.vs', 'bin', 'obj', 'packages', 'output', 'scratch', 'build', 'node_modules', 'docs'
}


def find_workspace_root():
    """
    Walk up from this script's directory until a .git marker is found.
    This makes the script location-independent regardless of CWD.
    """
    current = os.path.dirname(os.path.abspath(__file__))
    while True:
        if os.path.exists(os.path.join(current, '.git')):
            return current
        parent = os.path.dirname(current)
        if parent == current:
            raise RuntimeError(
                "Could not locate workspace root. "
                "Ensure this script lives inside the project repository (.git not found)."
            )
        current = parent


def get_code_block_lang(ext):
    """Map file extensions to markdown code block language specifiers."""
    ext = ext.lower()
    if ext == '.cs':
        return 'csharp'
    elif ext in ('.xaml', '.xml', '.csproj', '.projitems', '.shproj', '.addin', '.config'):
        return 'xml'
    elif ext == '.md':
        return 'markdown'
    elif ext == '.py':
        return 'python'
    elif ext == '.json':
        return 'json'
    elif ext in ('.bat', '.cmd'):
        return 'batch'
    elif ext == '.ps1':
        return 'powershell'
    return ''


def read_file_safely(file_path):
    """Attempt to read file contents with common encodings."""
    encodings = ['utf-8', 'utf-8-sig', 'windows-1252', 'latin-1']
    for encoding in encodings:
        try:
            with open(file_path, 'r', encoding=encoding) as f:
                return f.read()
        except UnicodeDecodeError:
            continue
    # Fallback with ignore errors
    try:
        with open(file_path, 'r', encoding='utf-8', errors='ignore') as f:
            return f.read()
    except Exception as e:
        return f"[Error reading file: {str(e)}]"


def get_default_paths():
    """Locate the projects directory and default config path using the workspace root anchor."""
    workspace_dir = find_workspace_root()
    projects_dir = os.path.dirname(workspace_dir)
    config_path = os.path.join(
        workspace_dir, "auth", "aggregate_config.json"
    )
    return projects_dir, config_path


def resolve_config_path(path_str, projects_dir):
    """Resolve paths to absolute. If already absolute, return as-is. Otherwise resolve relative to projects_dir."""
    if os.path.isabs(path_str):
        return path_str
    return os.path.abspath(os.path.join(projects_dir, path_str))


cleared_files = set()

def aggregate_directory_to_file(source_dir, output_file, allowed_extensions):
    """
    Recursively scans source_dir for files matching allowed_extensions
    and writes their full contents (including comments) to output_file.
    """
    source_dir = os.path.abspath(source_dir)
    output_file = os.path.abspath(output_file)
    allowed_extensions = {ext.lower().strip() for ext in allowed_extensions}

    # Ensure output directory exists
    output_directory = os.path.dirname(output_file)
    if output_directory:
        os.makedirs(output_directory, exist_ok=True)

    print(f"Scanning target directory: {source_dir}")
    print(f"Allowed extensions: {', '.join(allowed_extensions)}")
    print(f"Output file: {output_file}")

    file_count = 0
    import time

    # Clear any previous aggregation output
    if output_file not in cleared_files:
        if os.path.exists(output_file):
            for i in range(5):
                try:
                    os.remove(output_file)
                    break
                except Exception:
                    time.sleep(0.5)
            print(f"Cleared existing output file: {os.path.basename(output_file)}")
        cleared_files.add(output_file)
        mode = 'w'
    else:
        mode = 'a'

    out_file = None
    for i in range(5):
        try:
            out_file = open(output_file, mode, encoding='utf-8')
            break
        except Exception as e:
            if i == 4:
                raise e
            time.sleep(0.5)

    with out_file:
        for root, dirs, files in os.walk(source_dir):
            # Prune excluded directories in-place
            dirs[:] = [d for d in dirs if d not in DEFAULT_EXCLUDES]

            for file in files:
                _, ext = os.path.splitext(file)
                if ext.lower() in allowed_extensions:
                    full_path = os.path.join(root, file)

                    # Avoid including the output file in itself
                    if os.path.abspath(full_path) == output_file:
                        continue

                    # Relative path with source directory name prepended
                    rel_path = os.path.relpath(full_path, source_dir)
                    base_dir_name = os.path.basename(source_dir)
                    rel_path_with_base = f"{base_dir_name}/{rel_path.replace(os.path.sep, '/')}"

                    print(f"  Aggregating: {rel_path_with_base}")
                    content = read_file_safely(full_path)

                    lang = get_code_block_lang(ext)
                    out_file.write(f"### File: {rel_path_with_base}\n")
                    out_file.write(f"```{lang}\n")
                    out_file.write(content)
                    if not content.endswith('\n'):
                        out_file.write('\n')
                    out_file.write("```\n\n")

                    file_count += 1

    print(f"Successfully aggregated {file_count} files into {output_file}.\n")
    return file_count


def process_config(config_path, allowed_extensions=None):
    """Parses config_path and runs aggregation for each item in the aggregations array."""
    cleared_files.clear()
    if allowed_extensions is None:
        allowed_extensions = {".cs", ".xaml", ".md"}

    projects_dir, _ = get_default_paths()

    if not os.path.exists(config_path):
        print(f"Error: Config file not found at: {config_path}", file=sys.stderr)
        sys.exit(1)

    try:
        with open(config_path, 'r', encoding='utf-8') as f:
            config = json.load(f)
    except Exception as e:
        print(f"Error parsing config file: {e}", file=sys.stderr)
        sys.exit(1)

    aggregations = config.get("aggregations", [])
    if not aggregations:
        print("Warning: No aggregations found in configuration.")
        return 0

    total_files = 0
    for idx, agg in enumerate(aggregations):
        source_dir_raw = agg.get("source_dir")
        output_file_raw = agg.get("output_file")
        custom_extensions = agg.get("extensions")

        if not source_dir_raw or not output_file_raw:
            print(f"Warning: Item {idx} is missing source_dir or output_file, skipping.")
            continue

        source_dir = resolve_config_path(source_dir_raw, projects_dir)
        output_file = resolve_config_path(output_file_raw, projects_dir)

        if not os.path.exists(source_dir):
            print(f"Error: Target directory '{source_dir}' does not exist, skipping.")
            continue

        exts = custom_extensions if custom_extensions is not None else allowed_extensions
        total_files += aggregate_directory_to_file(source_dir, output_file, exts)

    return total_files


if __name__ == '__main__':
    _, default_config = get_default_paths()

    parser = argparse.ArgumentParser(
        description="Aggregate codebase directories into markdown bundle files, driven by sync_config.json."
    )
    parser.add_argument(
        "--config",
        default=default_config,
        help=f"Path to the configuration JSON file. Defaults to: {default_config}"
    )
    parser.add_argument(
        "--extensions",
        nargs="+",
        default=[".cs", ".xaml", ".md"],
        help="List of allowed file extensions. Defaults to .cs .xaml .md"
    )

    args = parser.parse_args()
    process_config(args.config, args.extensions)
```

### File: .agents/skills/docs-pull-google/SKILL.md
```markdown
---
name: docs-pull-google
description: How the pull pipeline downloads, splits, and formats cloud docs to local files using pull_docs.py. Use when initiating a Google Drive document pull.
disable-model-invocation: true
---

# Google Drive Document Pull Playbook (`docs-pull-google`)

Use this playbook to safely pull, format, and split Google Drive cloud documentation into local Markdown files.

## 1. Folder Isolation Layout (Local vs. Cloud)

To prevent local planning documents from being overwritten or deleted by the pull pipeline:

* **Separation Suffix:** All target folders are split into `local/` (your local-only edits) and `cloud/` (cloud-pulled documents) subdirectories:
  * `docs/adrs/cloud/`
  * `docs/prds/cloud/`
  * `docs/issues/cloud/`
  * `docs/development_briefs/cloud/`
* **Deletion Safety:** The pull script clears the target `cloud/` subdirectories before downloading fresh copies. The `local/` subdirectories are **never** cleared or touched.

## 2. On-Demand Pull Command

To execute the pull pipeline:
```powershell
python .agents/skills/docs-pull-google/scripts/pull_docs.py
```

## 3. Pre-Pull Git Verification

Although the script is isolated to `cloud/` subdirectories, as an extra layer of protection:
1. Run `git status` before executing the pull command.
2. If there are uncommitted changes or untracked files inside the target documentation subdirectories, prompt the user to commit or stash them before running the pull.

## 4. OAuth Authentication Gating

Because the Google Doc client uses the interactive OAuth readonly flow:

* **Token Check:** Before running the pull command, check if a valid Google token exists at:
  `Revit API Synthetic v2/auth/token_pull.json`
* **OAuth Interactive Browser Fail-Safe:** If `token_pull.json` is missing or expired, do **not** run the script headlessly.
* **User Redirection Prompt:** Instruct the user to run the authentication flow manually in their terminal. Print this exact message:

> [!WARNING]
> Google Drive pull token is missing or expired. To re-authenticate, copy and run the following command directly in your local terminal:
> ```powershell
> python .agents/skills/docs-pull-google/scripts/pull_docs.py
> ```
```

### File: .agents/skills/docs-pull-google/scripts/pull_docs.py
```python
import os
import sys
import json
import re
import shutil
import argparse
from google.oauth2.credentials import Credentials
from google_auth_oauthlib.flow import InstalledAppFlow
from google.auth.transport.requests import Request
from googleapiclient.discovery import build
from googleapiclient.errors import HttpError

# Google API scopes required for listing and reading documents
SCOPES = [
    'https://www.googleapis.com/auth/drive.readonly',
    'https://www.googleapis.com/auth/documents.readonly'
]

# Mapping of Google Doc styles to Markdown prefixes
HEADING_MAP = {
    'HEADING_1': '# ',
    'HEADING_2': '## ',
    'HEADING_3': '### ',
    'HEADING_4': '#### ',
    'HEADING_5': '##### ',
    'HEADING_6': '###### ',
    'TITLE': '# ',
    'SUBTITLE': '## '
}


def find_workspace_root():
    """Walk up from this script's directory until a .git marker is found."""
    current = os.path.dirname(os.path.abspath(__file__))
    while True:
        if os.path.exists(os.path.join(current, '.git')):
            return current
        parent = os.path.dirname(current)
        if parent == current:
            return os.path.dirname(os.path.abspath(__file__))
        current = parent


def get_auth_paths():
    """Resolve workspace-relative auth, credentials, and config file paths."""
    workspace_dir = find_workspace_root()
    projects_dir = os.path.dirname(workspace_dir)

    auth_dir = os.path.join(workspace_dir, "auth")
    client_secret_path = os.path.join(auth_dir, "client_secret.json")
    token_path = os.path.join(auth_dir, "token_pull.json")
    default_config_path = os.path.join(auth_dir, "pull_config.json")

    return auth_dir, client_secret_path, token_path, default_config_path, projects_dir


def authenticate(auth_dir, client_secret_path, token_path):
    """Authenticate with Google APIs and return credentials."""
    creds = None
    if os.path.exists(token_path):
        try:
            creds = Credentials.from_authorized_user_file(token_path, SCOPES)
        except Exception as e:
            print(f"Warning: Failed to load existing token_pull.json: {e}. Re-authenticating...", file=sys.stderr)
            creds = None

    if not creds or not creds.valid:
        if creds and creds.expired and creds.refresh_token:
            try:
                creds.refresh(Request())
            except Exception as e:
                print(f"Warning: Failed to refresh token: {e}. Re-authenticating...", file=sys.stderr)
                creds = None

        if not creds:
            if not os.path.exists(client_secret_path):
                print("\n" + "="*80, file=sys.stderr)
                print("ERROR: client_secret.json not found!", file=sys.stderr)
                print("Please place your Google OAuth Client secret JSON file at:", file=sys.stderr)
                print(f"  {client_secret_path}", file=sys.stderr)
                print("="*80 + "\n", file=sys.stderr)
                sys.exit(1)

            print("Launching browser for OAuth authentication (readonly scopes)...", flush=True)
            flow = InstalledAppFlow.from_client_secrets_file(client_secret_path, SCOPES)
            creds = flow.run_local_server(port=0)

        os.makedirs(auth_dir, exist_ok=True)
        with open(token_path, 'w') as token_file:
            token_file.write(creds.to_json())
            print(f"Saved authentication token to: {token_path}")

    return creds


def check_git_status_for_targets(pull_targets, projects_dir, workspace_dir):
    """
    Check if there are any uncommitted changes or untracked files inside
    the target directories/files. If so, abort and prompt user.
    """
    import subprocess
    changed_targets = []
    
    for target in pull_targets:
        local_path_raw = target.get("local_path")
        if not local_path_raw:
            continue
        local_target = os.path.abspath(os.path.join(projects_dir, local_path_raw))
        
        # We only run git check if the path exists
        if not os.path.exists(local_target):
            continue
            
        try:
            # Run git status --porcelain on the target path relative to workspace_dir
            res = subprocess.run(
                ['git', 'status', '--porcelain', local_target],
                cwd=workspace_dir,
                capture_output=True,
                text=True,
                encoding='utf-8'
            )
            if res.returncode == 0 and res.stdout.strip():
                changed_targets.append((local_path_raw, res.stdout.strip()))
        except Exception as e:
            print(f"Warning: Failed to run git status on '{local_target}': {e}", file=sys.stderr)
            
    if changed_targets:
        print("\n" + "="*80, file=sys.stderr)
        print("CRITICAL: Uncommitted changes or untracked files detected in target directories!", file=sys.stderr)
        print("Please commit or stash them before running the pull pipeline to prevent data loss.\n", file=sys.stderr)
        for path, status in changed_targets:
            print(f"Target: {path}", file=sys.stderr)
            print(f"Status:\n{status}\n", file=sys.stderr)
        print("="*80 + "\n", file=sys.stderr)
        sys.exit(1)


def validate_safe_path(target_path, allowed_parent):
    """Ensure the target path resolves inside the allowed parent boundary to prevent accidental deletions."""
    target_abs = os.path.abspath(target_path)
    parent_abs = os.path.abspath(allowed_parent)
    if not target_abs.lower().startswith(parent_abs.lower()):
        raise ValueError(
            f"Path validation failed: '{target_abs}' is outside the allowed workspace boundary '{parent_abs}'"
        )


def clear_local_directory(local_dir):
    """Safely clear all files and folders inside the target directory."""
    if os.path.exists(local_dir):
        print(f"Clearing local folder: '{local_dir}'...")
        for item in os.listdir(local_dir):
            item_path = os.path.join(local_dir, item)
            try:
                if os.path.isdir(item_path):
                    shutil.rmtree(item_path)
                else:
                    os.remove(item_path)
            except Exception as e:
                print(f"Warning: Failed to delete '{item_path}': {e}", file=sys.stderr)
    else:
        print(f"Creating local folder: '{local_dir}'...")
        os.makedirs(local_dir, exist_ok=True)


def list_drive_docs(drive_service, folder_id, include_pat=None, exclude_pat=None):
    """List Google Docs directly inside the root of the specified folder."""
    query = f"'{folder_id}' in parents and mimeType='application/vnd.google-apps.document' and trashed=false"
    
    files = []
    page_token = None
    while True:
        response = drive_service.files().list(
            q=query,
            spaces='drive',
            fields='nextPageToken, files(id, name)',
            pageToken=page_token
        ).execute()
        files.extend(response.get('files', []))
        page_token = response.get('nextPageToken', None)
        if not page_token:
            break

    # Apply include/exclude regular expression filters if configured
    filtered = []
    for f in files:
        name = f['name']
        if include_pat:
            if not re.search(include_pat, name, re.IGNORECASE):
                continue
        if exclude_pat:
            if re.search(exclude_pat, name, re.IGNORECASE):
                continue
        filtered.append(f)
        
    return filtered


def get_doc_content(docs_service, doc_id):
    """Retrieve Google Doc content JSON using the Docs API."""
    return docs_service.documents().get(documentId=doc_id).execute()


def format_text_run(text, style):
    """Apply markdown formatting runs (bold, italic, strike, link) while keeping spaces and newlines correct."""
    if not text:
        return ""
    if not style:
        return text

    # Extract trailing newline so formatting markers stay outside it
    has_newline = text.endswith('\n')
    clean_text = text[:-1] if has_newline else text

    # Preserve leading/trailing spaces outside formatting markers
    stripped_text = clean_text.strip()
    if not stripped_text:
        return text

    leading_space = clean_text[:len(clean_text) - len(clean_text.lstrip())]
    trailing_space = clean_text[len(clean_text.rstrip()):]

    formatted = stripped_text

    # Inner-most formatting: Link
    if 'link' in style and 'url' in style['link']:
        url = style['link']['url']
        formatted = f"[{formatted}]({url})"

    # Strikethrough
    if style.get('strikethrough'):
        formatted = f"~~{formatted}~~"

    # Italic
    if style.get('italic'):
        formatted = f"*{formatted}*"

    # Bold
    if style.get('bold'):
        formatted = f"**{formatted}**"

    result = leading_space + formatted + trailing_space
    if has_newline:
        result += '\n'
    return result


def parse_table(table, lists, doc_content):
    """Parse a Google Doc table element into GFM markdown table."""
    rows = table.get('tableRows', [])
    if not rows:
        return ""

    markdown_rows = []
    max_cols = 0

    for row in rows:
        cells = row.get('tableCells', [])
        max_cols = max(max_cols, len(cells))
        cell_texts = []
        for cell in cells:
            cell_content = cell.get('content', [])
            cell_markdown = parse_cell_content(cell_content, lists, doc_content)
            # Table cell contents must be single line, replace newline with html break
            cell_clean = cell_markdown.strip().replace('\n', '<br>')
            cell_clean = cell_clean.replace('|', '\\|')
            cell_texts.append(cell_clean)
        markdown_rows.append(cell_texts)

    if max_cols == 0:
        return ""

    lines = []
    # Header row
    header_row = markdown_rows[0] if markdown_rows else []
    header_row += [""] * (max_cols - len(header_row))
    lines.append("| " + " | ".join(header_row) + " |")

    # Column delimiter
    delimiter = ["---"] * max_cols
    lines.append("| " + " | ".join(delimiter) + " |")

    # Data rows
    for row_cells in markdown_rows[1:]:
        row_cells += [""] * (max_cols - len(row_cells))
        lines.append("| " + " | ".join(row_cells) + " |")

    return "\n".join(lines)


def parse_cell_content(content, lists, doc_content):
    """Parse structural content blocks within table cells."""
    markdown_lines = []
    for element in content:
        if 'paragraph' in element:
            para = element['paragraph']
            para_style = para.get('paragraphStyle', {})
            style_type = para_style.get('namedStyleType', 'NORMAL_TEXT')
            text_runs = para.get('elements', [])
            para_text = ""
            for run_el in text_runs:
                if 'textRun' in run_el:
                    run = run_el['textRun']
                    text = run.get('content', '')
                    style = run.get('textStyle', {})
                    para_text += format_text_run(text, style)
                elif 'inlineObjectElement' in run_el:
                    obj_id = run_el['inlineObjectElement'].get('inlineObjectId')
                    if obj_id and 'inlineObjects' in doc_content and obj_id in doc_content['inlineObjects']:
                        obj = doc_content['inlineObjects'][obj_id]
                        emb = obj.get('inlineObjectProperties', {}).get('embeddedObject', {})
                        if 'imageProperties' in emb:
                            uri = emb['imageProperties'].get('contentUri', '')
                            title = emb.get('title', '')
                            desc = emb.get('description', '')
                            alt = title or desc or 'image'
                            para_text += f"![{alt}]({uri})"

            bullet = para.get('bullet')
            if bullet:
                list_id = bullet.get('listId')
                nesting_level = bullet.get('nestingLevel', 0)
                is_ordered = False
                if list_id in lists:
                    list_props = lists[list_id].get('listProperties', {})
                    nesting_levels = list_props.get('nestingLevels', [])
                    if nesting_level < len(nesting_levels):
                        glyph_type = nesting_levels[nesting_level].get('glyphType')
                        if glyph_type and glyph_type != 'GLYPH_TYPE_UNSPECIFIED':
                            is_ordered = True

                indent = "  " * nesting_level
                prefix = "1. " if is_ordered else "- "
                clean_text = para_text.rstrip('\n')
                markdown_lines.append(f"{indent}{prefix}{clean_text}\n")
            else:
                clean_text = para_text.strip('\n')
                if style_type.startswith('HEADING') or style_type == 'TITLE':
                    markdown_lines.append(f"**{clean_text}**\n")
                else:
                    markdown_lines.append(para_text)
        elif 'table' in element:
            # Avoid nesting tables inside markdown table cell
            pass
    return "".join(markdown_lines)


def get_split_level(split_by):
    """Resolve splitting header keyword into Google Doc namedStyle identifier."""
    if not split_by:
        return None
    val = split_by.strip().upper()
    mapping = {
        'H1': 'HEADING_1',
        'HEADING_1': 'HEADING_1',
        'H2': 'HEADING_2',
        'HEADING_2': 'HEADING_2',
        'H3': 'HEADING_3',
        'HEADING_3': 'HEADING_3',
        'H4': 'HEADING_4',
        'HEADING_4': 'HEADING_4',
        'H5': 'HEADING_5',
        'HEADING_5': 'HEADING_5',
        'H6': 'HEADING_6',
        'HEADING_6': 'HEADING_6'
    }
    return mapping.get(val)


def parse_and_split_doc(doc_content, split_level=None):
    """Parse Google Doc JSON into a list of (section_title, section_markdown)."""
    body = doc_content.get('body', {})
    content = body.get('content', [])
    lists = doc_content.get('lists', {})

    sections = []
    current_title = ""
    current_lines = []

    for element in content:
        if 'paragraph' in element:
            para = element['paragraph']
            para_style = para.get('paragraphStyle', {})
            style_type = para_style.get('namedStyleType', 'NORMAL_TEXT')
            text_runs = para.get('elements', [])
            para_text = ""
            for run_el in text_runs:
                if 'textRun' in run_el:
                    run = run_el['textRun']
                    text = run.get('content', '')
                    style = run.get('textStyle', {})
                    para_text += format_text_run(text, style)
                elif 'inlineObjectElement' in run_el:
                    obj_id = run_el['inlineObjectElement'].get('inlineObjectId')
                    if obj_id and 'inlineObjects' in doc_content and obj_id in doc_content['inlineObjects']:
                        obj = doc_content['inlineObjects'][obj_id]
                        emb = obj.get('inlineObjectProperties', {}).get('embeddedObject', {})
                        if 'imageProperties' in emb:
                            uri = emb['imageProperties'].get('contentUri', '')
                            title = emb.get('title', '')
                            desc = emb.get('description', '')
                            alt = title or desc or 'image'
                            para_text += f"![{alt}]({uri})"

            bullet = para.get('bullet')
            if bullet:
                list_id = bullet.get('listId')
                nesting_level = bullet.get('nestingLevel', 0)
                is_ordered = False
                if list_id in lists:
                    list_props = lists[list_id].get('listProperties', {})
                    nesting_levels = list_props.get('nestingLevels', [])
                    if nesting_level < len(nesting_levels):
                        glyph_type = nesting_levels[nesting_level].get('glyphType')
                        if glyph_type and glyph_type != 'GLYPH_TYPE_UNSPECIFIED':
                            is_ordered = True

                indent = "  " * nesting_level
                prefix = "1. " if is_ordered else "- "
                clean_text = para_text.rstrip('\n')
                current_lines.append(f"{indent}{prefix}{clean_text}\n")
            else:
                header_prefix = HEADING_MAP.get(style_type, "")
                if header_prefix:
                    clean_text = para_text.strip('\n')
                    if clean_text:
                        if split_level and style_type == split_level:
                            # Save previous section if it contains elements
                            sections.append((current_title, "".join(current_lines)))
                            # Start new section
                            current_title = clean_text
                            current_lines = [f"{header_prefix}{clean_text}\n\n"]
                        else:
                            current_lines.append(f"{header_prefix}{clean_text}\n\n")
                else:
                    if para_text == '\n':
                        current_lines.append('\n')
                    else:
                        current_lines.append(para_text)

        elif 'table' in element:
            table = element['table']
            table_markdown = parse_table(table, lists, doc_content)
            current_lines.append(table_markdown + "\n\n")

    # Save trailing section
    sections.append((current_title, "".join(current_lines)))
    return sections


def sanitize_filename(name):
    """Sanitize string to be filename safe on Windows."""
    name = re.sub(r'[\\/:*?"<>|]', "", name)
    return name.strip()


def save_sections(original_doc_name, sections, local_dir):
    """Save parsed document sections to the target local folder using safe naming schemes."""
    # Filter out empty sections
    sections = [(title, content) for title, content in sections if content.strip()]
    if not sections:
        print(f"Warning: Document '{original_doc_name}' has no content - skipping.")
        return

    # If document has only 1 section and it has no title, save directly as {original_doc_name}.md
    if len(sections) == 1 and not sections[0][0]:
        filename = f"{original_doc_name}.md"
        filepath = os.path.join(local_dir, sanitize_filename(filename))
        print(f"  -> Writing '{filename}'...")
        with open(filepath, 'w', encoding='utf-8') as f:
            f.write(sections[0][1])
        return

    # Determine if there is introductory content preceding the first split header
    has_intro = not sections[0][0]

    for idx, (title, content) in enumerate(sections):
        if has_intro:
            if idx == 0:
                filename = f"{original_doc_name} - 00 - Intro.md"
            else:
                filename = f"{original_doc_name} - {idx:02d} - {title}.md"
        else:
            filename = f"{original_doc_name} - {idx+1:02d} - {title}.md"

        filepath = os.path.join(local_dir, sanitize_filename(filename))
        print(f"  -> Writing '{filename}'...")
        with open(filepath, 'w', encoding='utf-8') as f:
            f.write(content)


def main():
    auth_dir, client_secret_path, token_path, default_config_path, projects_dir = get_auth_paths()

    parser = argparse.ArgumentParser(
        description="Pulls Google Docs from Google Drive and exports them as local Markdown files with section splitting."
    )
    parser.add_argument(
        "--config",
        dest="config_path",
        default=default_config_path,
        help=f"Path to pull_config.json. Defaults to: {default_config_path}"
    )
    parser.add_argument(
        "targets",
        nargs="*",
        help="Optional specific folder targets to pull (e.g. 'adrs', 'prds', 'issues', 'development_briefs'). If omitted, pulls all targets."
    )
    args = parser.parse_args()

    print("="*60)
    print("PHASE 1: Authentication...")
    print("="*60)
    creds = authenticate(auth_dir, client_secret_path, token_path)

    try:
        drive_service = build('drive', 'v3', credentials=creds)
        docs_service = build('docs', 'v1', credentials=creds)
    except Exception as e:
        print(f"Error initializing Google API clients: {e}", file=sys.stderr)
        sys.exit(1)

    print("\n" + "="*60)
    print("PHASE 2: Reading Configuration...")
    print("="*60)
    if not os.path.exists(args.config_path):
        print(f"Error: Configuration file '{args.config_path}' not found.", file=sys.stderr)
        sys.exit(1)

    try:
        with open(args.config_path, 'r', encoding='utf-8') as f:
            config = json.load(f)
    except Exception as e:
        print(f"Error parsing configuration JSON: {e}", file=sys.stderr)
        sys.exit(1)

    pull_targets = config.get("pull_targets", [])
    if not pull_targets:
        print("Warning: No pull_targets found in config.", file=sys.stderr)
        sys.exit(0)

    requested_targets = [t.lower() for t in args.targets]
    if requested_targets:
        filtered_targets = []
        for target in pull_targets:
            local_path_raw = target.get("local_path", "")
            folder_name = os.path.basename(local_path_raw).lower()
            if folder_name in requested_targets:
                filtered_targets.append(target)
        pull_targets = filtered_targets

    if not pull_targets:
        print(f"No matching pull targets found for: {args.targets}")
        sys.exit(0)

    print(f"Found {len(pull_targets)} pull targets.")

    workspace_dir = find_workspace_root()
    check_git_status_for_targets(pull_targets, projects_dir, workspace_dir)

    for idx, target in enumerate(pull_targets):
        drive_folder_id = target.get("drive_folder_id")
        drive_file_id = target.get("drive_file_id")
        local_path_raw = target.get("local_path")
        split_by = target.get("split_by")
        include_pattern = target.get("include_pattern")
        exclude_pattern = target.get("exclude_pattern")

        if not (drive_folder_id or drive_file_id) or not local_path_raw:
            print(f"Warning: Pull target index {idx} missing drive_folder_id/drive_file_id or local_path - skipping.", file=sys.stderr)
            continue

        # Resolve local directory relative to projects folder
        local_target = os.path.abspath(os.path.join(projects_dir, local_path_raw))
        
        # Verify target folder path stays within workspace bounds for safety
        try:
            validate_safe_path(local_target, projects_dir)
        except ValueError as err:
            print(f"Error: {err}", file=sys.stderr)
            continue

        is_file_target = local_target.lower().endswith('.md')
        if is_file_target:
            local_dir = os.path.dirname(local_target)
        else:
            local_dir = local_target

        print("\n" + "-"*60)
        if drive_folder_id:
            print(f"Processing Target folder ID: '{drive_folder_id}'")
        else:
            print(f"Processing Target file ID: '{drive_file_id}'")
        print(f"Local output folder: '{local_dir}'")
        if split_by:
            print(f"Splitting files on: '{split_by}'")
        print("-"*60)

        # Clear target local directory before downloading to keep clean (only if it's a folder target)
        if not is_file_target:
            try:
                clear_local_directory(local_dir)
            except Exception as e:
                print(f"Error clearing local directory: {e}", file=sys.stderr)
                continue
        else:
            os.makedirs(local_dir, exist_ok=True)

        # List files matching filters
        try:
            if drive_folder_id:
                files_to_pull = list_drive_docs(drive_service, drive_folder_id, include_pattern, exclude_pattern)
            else:
                # Fetch single file metadata
                doc_info = drive_service.files().get(fileId=drive_file_id, fields='id, name').execute()
                files_to_pull = [doc_info]
        except HttpError as err:
            if drive_folder_id:
                print(f"Error listing drive contents: {err}", file=sys.stderr)
            else:
                print(f"Error getting file metadata: {err}", file=sys.stderr)
            continue

        if not files_to_pull:
            if drive_folder_id:
                print("No matching Google Docs found in this folder.")
            else:
                print("Google Doc not found on Drive.")
            continue

        print(f"Found {len(files_to_pull)} matching Google Docs. Fetching and converting...")
        split_level = get_split_level(split_by)

        for doc_info in files_to_pull:
            doc_id = doc_info['id']
            doc_name = doc_info['name']

            print(f"\n- Fetching doc: '{doc_name}' ({doc_id})...")
            try:
                doc_content = get_doc_content(docs_service, doc_id)
            except HttpError as err:
                print(f"Error fetching Google Doc '{doc_name}': {err}", file=sys.stderr)
                continue

            try:
                sections = parse_and_split_doc(doc_content, split_level)
                if is_file_target:
                    # Concatenate sections and save directly to file path
                    full_content = "\n\n".join([sec[1] for sec in sections if sec[1].strip()])
                    print(f"  -> Writing to file '{os.path.basename(local_target)}'...")
                    with open(local_target, 'w', encoding='utf-8') as f:
                        f.write(full_content)
                else:
                    save_sections(doc_name, sections, local_dir)
            except Exception as e:
                print(f"Error converting document '{doc_name}': {e}", file=sys.stderr)
                import traceback
                traceback.print_exc()

    print("\n" + "="*60)
    print("ALL PULL AND EXPORT OPERATIONS COMPLETED")
    print("="*60)


if __name__ == '__main__':
    main()
```

### File: .agents/skills/docs-push-google/SKILL.md
```markdown
---
name: docs-push-google
description: How the on-demand push/sync pipeline authenticates and transfers markdown bundles using drive_sync.py and sync_all.py. Use when initiating a Google Drive document sync.
disable-model-invocation: true
---

# Google Drive Sync Playbook (`docs-push-google`)

Use this playbook to aggregate the codebase and sync local documentation bundles on-demand to Google Drive.

## 1. Sync Pipeline Workflow

The sync pipeline consists of two sequential phases executed via the `sync_all.py` script:

1. **Phase 1: Codebase Aggregation (`aggregate.py`):**
   * Scans configured directories in `sync_config.json` (inside `Revit API Synthetic v2/auth/`).
   * Bundles source file headers, code syntax, and comments into specialized markdown file collections (e.g. `Synthetic_src_Foundation.md`, `Synthetic_tests.md`) in the `docs/source_code/` folder.
2. **Phase 2: Google Drive Upload (`drive_sync.py`):**
   * Reads targets defined in `sync_config.json` and syncs them to Google Drive (using `drive_folder_id`).
   * If a target has no `drive_id`, uploads it as a new Google Doc and updates the JSON config.
   * If a target has a `drive_id`, updates the existing Google Doc. Falls back to raw markdown upload if Google Doc conversion fails.

## 2. On-Demand Sync Command

To execute the sync pipeline from a valid local session:
```powershell
python .agents/skills/docs-push-google/scripts/sync_all.py
```

## 3. OAuth Authentication Gating

Because the Google Drive client uses the interactive OAuth 2.0 flow:

* **Token Check:** Before running the sync command, check if a valid Google token exists at:
  `Revit API Synthetic v2/auth/token.json`
* **OAuth Interactive Browser Fail-Safe:** If `token.json` is missing or expired, do **not** run the script headlessly, as the browser redirection flow will hang.
* **User Redirection Prompt:** Instruct the user to run the authentication flow manually in their terminal. Print this exact message:

> [!WARNING]
> Google Drive token is missing or expired. To re-authenticate, copy and run the following command directly in your local terminal:
> ```powershell
> python .agents/skills/docs-push-google/scripts/sync_all.py
> ```
```

### File: .agents/skills/docs-push-google/scripts/drive_sync.py
```python
import os
import sys
import argparse
from google.oauth2.credentials import Credentials
from google_auth_oauthlib.flow import InstalledAppFlow
from google.auth.transport.requests import Request
from googleapiclient.discovery import build
from googleapiclient.http import MediaFileUpload
from googleapiclient.errors import HttpError

# Define Google Drive Scopes
SCOPES = ['https://www.googleapis.com/auth/drive.file']


def find_workspace_root():
    """
    Walk up from this script's directory until a .git marker is found.
    This makes the script location-independent regardless of CWD.
    """
    current = os.path.dirname(os.path.abspath(__file__))
    while True:
        if os.path.exists(os.path.join(current, '.git')):
            return current
        parent = os.path.dirname(current)
        if parent == current:
            raise RuntimeError(
                "Could not locate workspace root. "
                "Ensure this script lives inside the project repository (.git not found)."
            )
        current = parent


def get_auth_paths():
    """Compute the absolute paths to client_secret.json and token.json in the Auth folder."""
    workspace_dir = find_workspace_root()
    projects_dir = os.path.dirname(workspace_dir)

    auth_dir = os.path.join(workspace_dir, "auth")
    client_secret_path = os.path.join(auth_dir, "client_secret.json")
    token_path = os.path.join(auth_dir, "token.json")

    return auth_dir, client_secret_path, token_path


def authenticate():
    """Handles Google OAuth 2.0 user credentials flow and returns valid credentials."""
    auth_dir, client_secret_path, token_path = get_auth_paths()
    creds = None

    # Try loading existing cached token
    if os.path.exists(token_path):
        try:
            creds = Credentials.from_authorized_user_file(token_path, SCOPES)
        except Exception as e:
            print(f"Warning: Failed to load existing token.json: {e}. Re-authenticating...", file=sys.stderr)
            creds = None

    # If credentials don't exist or are invalid/expired, run authorization flow
    if not creds or not creds.valid:
        if creds and creds.expired and creds.refresh_token:
            try:
                creds.refresh(Request())
            except Exception as e:
                print(f"Warning: Failed to refresh token: {e}. Re-authenticating...", file=sys.stderr)
                creds = None

        if not creds:
            if not os.path.exists(client_secret_path):
                print("\n" + "="*80, file=sys.stderr)
                print("ERROR: client_secret.json not found!", file=sys.stderr)
                print(f"Please place your Google OAuth Client secret JSON file at:", file=sys.stderr)
                print(f"  {client_secret_path}", file=sys.stderr)
                print("="*80 + "\n", file=sys.stderr)
                sys.exit(1)

            # Run the local server flow to authenticate
            print("Launching browser for OAuth authentication...", flush=True)
            flow = InstalledAppFlow.from_client_secrets_file(client_secret_path, SCOPES)
            creds = flow.run_local_server(port=0)

        # Save the credentials for the next run
        os.makedirs(auth_dir, exist_ok=True)
        with open(token_path, 'w') as token_file:
            token_file.write(creds.to_json())
            print(f"Saved authentication token to: {token_path}")

    return creds


def sync_to_drive(file_path, drive_file_id=None, parent_folder_id=None):
    """
    Uploads or updates the specified local Markdown file to Google Drive.
    Converts the file to a Google Doc native format during creation.
    Handles placeholders (e.g. starting with 'YOUR_') by performing a new upload.
    Falls back to raw file upload if Google Docs conversion fails (e.g. due to size limits).
    """
    if not os.path.exists(file_path):
        print(f"Error: Local file '{file_path}' does not exist.", file=sys.stderr)
        sys.exit(1)

    creds = authenticate()

    # Check if drive_file_id is a placeholder or empty
    is_placeholder = (
        not drive_file_id
        or drive_file_id.startswith("YOUR_")
        or drive_file_id.lower() in ("placeholder", "null", "none", "")
    )

    if is_placeholder:
        drive_file_id = None

    try:
        service = build('drive', 'v3', credentials=creds)

        if drive_file_id:
            # Update an existing document
            print(f"Updating Google Doc with File ID: {drive_file_id} ...")
            media = MediaFileUpload(file_path, mimetype='text/plain', resumable=True)
            try:
                file = service.files().update(
                    fileId=drive_file_id,
                    media_body=media,
                    fields='id'
                ).execute()
                updated_id = file.get('id')
                print(f"SUCCESS: Updated Google Doc.")
                print(f"Google Drive File ID: {updated_id}")
                return updated_id
            except HttpError as error:
                if error.resp.status == 400:
                    print("Warning: Update failed with text/plain. Retrying as raw Markdown...", file=sys.stderr)
                    media = MediaFileUpload(file_path, mimetype='text/markdown', resumable=True)
                    file = service.files().update(
                        fileId=drive_file_id,
                        media_body=media,
                        fields='id'
                    ).execute()
                    updated_id = file.get('id')
                    print(f"SUCCESS: Updated raw file.")
                    print(f"Google Drive File ID: {updated_id}")
                    return updated_id
                else:
                    raise
        else:
            # Create a new document
            file_name = os.path.splitext(os.path.basename(file_path))[0]
            print(f"Uploading '{file_path}' to Google Drive as '{file_name}'...")

            file_metadata = {
                'name': file_name,
                'mimeType': 'application/vnd.google-apps.document'  # Force native conversion
            }
            if parent_folder_id:
                file_metadata['parents'] = [parent_folder_id]

            media = MediaFileUpload(file_path, mimetype='text/plain', resumable=True)

            try:
                file = service.files().create(
                    body=file_metadata,
                    media_body=media,
                    fields='id'
                ).execute()
                new_id = file.get('id')
                print(f"SUCCESS: Created new Google Doc.")
                print(f"Google Drive File ID: {new_id}")
                return new_id
            except HttpError as error:
                if error.resp.status == 400:
                    print("\nWarning: Google Docs conversion failed (likely file size limit).", file=sys.stderr)
                    print("Falling back to uploading raw Markdown file...", file=sys.stderr)

                    raw_metadata = {
                        'name': os.path.basename(file_path),
                        'mimeType': 'text/markdown'
                    }
                    if parent_folder_id:
                        raw_metadata['parents'] = [parent_folder_id]

                    media = MediaFileUpload(file_path, mimetype='text/markdown', resumable=True)

                    file = service.files().create(
                        body=raw_metadata,
                        media_body=media,
                        fields='id'
                    ).execute()
                    new_id = file.get('id')
                    print(f"SUCCESS: Created new raw Markdown file.")
                    print(f"Google Drive File ID: {new_id}")
                    return new_id
                else:
                    raise

    except HttpError as error:
        print(f"An error occurred: {error}", file=sys.stderr)
        sys.exit(1)


if __name__ == '__main__':
    parser = argparse.ArgumentParser(
        description="Sync a local Markdown file to Google Drive as a Google Doc."
    )
    parser.add_argument(
        "file_path",
        help="Path to the local Markdown file to sync."
    )
    parser.add_argument(
        "--id",
        dest="drive_file_id",
        default=None,
        help="Optional Google Drive File ID to update an existing document. If omitted, a new file is created."
    )
    parser.add_argument(
        "--folder",
        dest="parent_folder_id",
        default=None,
        help="Optional Google Drive Folder ID to place new files inside on creation."
    )

    args = parser.parse_args()
    sync_to_drive(args.file_path, args.drive_file_id, args.parent_folder_id)
```

### File: .agents/skills/docs-push-google/scripts/sync_all.py
```python
import os
import sys
import argparse
import json

script_dir = os.path.dirname(os.path.abspath(__file__))
# Resolve relative path to docs-aggregate scripts folder
docs_aggregate_dir = os.path.abspath(os.path.join(script_dir, "..", "..", "docs-aggregate", "scripts"))

# Ensure sibling scripts and docs-aggregate script are importable regardless of CWD
sys.path.insert(0, script_dir)
sys.path.insert(0, docs_aggregate_dir)

from aggregate import process_config, resolve_config_path, get_default_paths
from drive_sync import sync_to_drive


def main():
    projects_dir, default_agg_config = get_default_paths()
    default_sync_config = os.path.join(
        os.path.dirname(default_agg_config), "sync_config.json"
    )

    parser = argparse.ArgumentParser(
        description="Configuration-driven codebase aggregation and Google Drive sync runner."
    )
    parser.add_argument(
        "--config",
        dest="config_path",
        default=default_agg_config,
        help=f"Path to the aggregation config JSON file. Defaults to: {default_agg_config}"
    )
    parser.add_argument(
        "--sync-config",
        dest="sync_config_path",
        default=default_sync_config,
        help=f"Path to the sync config JSON file. Defaults to: {default_sync_config}"
    )
    parser.add_argument(
        "--extensions",
        dest="extensions",
        nargs="+",
        default=[".cs", ".xaml", ".md"],
        help="Allowed file extensions. Defaults to .cs .xaml .md"
    )

    args = parser.parse_args()

    print("="*60)
    print("PHASE 1: Aggregating configured source directories...")
    print("="*60)
    try:
        file_count = process_config(args.config_path, args.extensions)
        print(f"Aggregation complete. {file_count} files bundled.")
    except Exception as e:
        print(f"Error during aggregation phase: {e}", file=sys.stderr)
        sys.exit(1)

    print("\n" + "="*60)
    print("PHASE 2: Syncing targets to Google Drive...")
    print("="*60)

    try:
        with open(args.sync_config_path, 'r', encoding='utf-8') as f:
            config = json.load(f)
    except Exception as e:
        print(f"Error loading configuration file: {e}", file=sys.stderr)
        sys.exit(1)

    sync_targets = config.get("sync_targets", [])
    if not sync_targets:
        print("Warning: No sync_targets found in configuration.")
        sys.exit(0)

    parent_folder_id = config.get("drive_folder_id")
    if parent_folder_id:
        print(f"Target Google Drive Folder ID: {parent_folder_id}")
    else:
        print("No Drive Folder ID in config — files will upload to root Drive directory.")

    results = {}
    for idx, target in enumerate(sync_targets):
        local_path_raw = target.get("local_path")
        drive_id = target.get("drive_id")

        if not local_path_raw:
            print(f"Warning: Target {idx} is missing local_path, skipping.")
            continue

        local_path = resolve_config_path(local_path_raw, projects_dir)
        target_name = os.path.basename(local_path)

        print(f"\n--- Syncing '{target_name}' ---")
        if not os.path.exists(local_path):
            print(f"Warning: Local file '{local_path}' does not exist — skipping.", file=sys.stderr)
            continue

        try:
            drive_id = sync_to_drive(local_path, drive_id, parent_folder_id)
            results[target_name] = drive_id
        except Exception as e:
            print(f"Error syncing '{local_path}': {e}", file=sys.stderr)
            sys.exit(1)

    print("\n" + "="*60)
    print("ALL SYNC PROCESSES COMPLETE")
    for filename, drive_id in results.items():
        print(f"  - '{filename}' -> Google Drive File ID: {drive_id}")
    print("="*60)


if __name__ == '__main__':
    main()
```

### File: .agents/skills/domain-modeling/ADR-FORMAT.md
```markdown
# ADR Format

ADRs live in `docs/adr/` and use sequential numbering: `0001-slug.md`, `0002-slug.md`, etc.

Create the `docs/adr/` directory lazily — only when the first ADR is needed.

## Template

```md
# {Short title of the decision}

{1-3 sentences: what's the context, what did we decide, and why.}
```

That's it. An ADR can be a single paragraph. The value is in recording *that* a decision was made and *why* — not in filling out sections.

## Optional sections

Only include these when they add genuine value. Most ADRs won't need them.

- **Status** frontmatter (`proposed | accepted | deprecated | superseded by ADR-NNNN`) — useful when decisions are revisited
- **Considered Options** — only when the rejected alternatives are worth remembering
- **Consequences** — only when non-obvious downstream effects need to be called out

## Numbering

Scan `docs/adr/` for the highest existing number and increment by one.

## When to offer an ADR

All three of these must be true:

1. **Hard to reverse** — the cost of changing your mind later is meaningful
2. **Surprising without context** — a future reader will look at the code and wonder "why on earth did they do it this way?"
3. **The result of a real trade-off** — there were genuine alternatives and you picked one for specific reasons

If a decision is easy to reverse, skip it — you'll just reverse it. If it's not surprising, nobody will wonder why. If there was no real alternative, there's nothing to record beyond "we did the obvious thing."

### What qualifies

- **Architectural shape.** "We're using a monorepo." "The write model is event-sourced, the read model is projected into Postgres."
- **Integration patterns between contexts.** "Ordering and Billing communicate via domain events, not synchronous HTTP."
- **Technology choices that carry lock-in.** Database, message bus, auth provider, deployment target. Not every library — just the ones that would take a quarter to swap out.
- **Boundary and scope decisions.** "Customer data is owned by the Customer context; other contexts reference it by ID only." The explicit no-s are as valuable as the yes-s.
- **Deliberate deviations from the obvious path.** "We're using manual SQL instead of an ORM because X." Anything where a reasonable reader would assume the opposite. These stop the next engineer from "fixing" something that was deliberate.
- **Constraints not visible in the code.** "We can't use AWS because of compliance requirements." "Response times must be under 200ms because of the partner API contract."
- **Rejected alternatives when the rejection is non-obvious.** If you considered GraphQL and picked REST for subtle reasons, record it — otherwise someone will suggest GraphQL again in six months.
```

### File: .agents/skills/domain-modeling/CONTEXT-FORMAT.md
```markdown
# CONTEXT.md Format

## Structure

```md
# {Context Name}

{One or two sentence description of what this context is and why it exists.}

## Language

**Order**:
{A one or two sentence description of the term}
_Avoid_: Purchase, transaction

**Invoice**:
A request for payment sent to a customer after delivery.
_Avoid_: Bill, payment request

**Customer**:
A person or organization that places orders.
_Avoid_: Client, buyer, account
```

## Rules

- **Be opinionated.** When multiple words exist for the same concept, pick the best one and list the others under `_Avoid_`.
- **Keep definitions tight.** One or two sentences max. Define what it IS, not what it does.
- **Only include terms specific to this project's context.** General programming concepts (timeouts, error types, utility patterns) don't belong even if the project uses them extensively. Before adding a term, ask: is this a concept unique to this context, or a general programming concept? Only the former belongs.
- **Group terms under subheadings** when natural clusters emerge. If all terms belong to a single cohesive area, a flat list is fine.

## Single vs multi-context repos

**Single context (most repos):** One `CONTEXT.md` at the repo root.

**Multiple contexts:** A `CONTEXT-MAP.md` at the repo root lists the contexts, where they live, and how they relate to each other:

```md
# Context Map

## Contexts

- [Ordering](./src/ordering/CONTEXT.md) — receives and tracks customer orders
- [Billing](./src/billing/CONTEXT.md) — generates invoices and processes payments
- [Fulfillment](./src/fulfillment/CONTEXT.md) — manages warehouse picking and shipping

## Relationships

- **Ordering → Fulfillment**: Ordering emits `OrderPlaced` events; Fulfillment consumes them to start picking
- **Fulfillment → Billing**: Fulfillment emits `ShipmentDispatched` events; Billing consumes them to generate invoices
- **Ordering ↔ Billing**: Shared types for `CustomerId` and `Money`
```

The skill infers which structure applies:

- If `CONTEXT-MAP.md` exists, read it to find contexts
- If only a root `CONTEXT.md` exists, single context
- If neither exists, create a root `CONTEXT.md` lazily when the first term is resolved

When multiple contexts exist, infer which one the current topic relates to. If unclear, ask.
```

### File: .agents/skills/domain-modeling/SKILL.md
```markdown
---
name: domain-modeling
description: Build and sharpen a project's domain model. Use when the user wants to pin down domain terminology or a ubiquitous language, record an architectural decision, or when another skill needs to maintain the domain model.
---

# Domain Modeling

Actively build and sharpen the project's domain model as you design. This is the *active* discipline — challenging terms, inventing edge-case scenarios, and writing the glossary and decisions down the moment they crystallise. (Merely *reading* `CONTEXT.md` for vocabulary is not this skill — that's a one-line habit any skill can do. This skill is for when you're changing the model, not just consuming it.)

## File structure

Most repos have a single context:

```
/
├── CONTEXT.md
├── docs/
│   └── adr/
│       ├── 0001-event-sourced-orders.md
│       └── 0002-postgres-for-write-model.md
└── src/
```

If a `CONTEXT-MAP.md` exists at the root, the repo has multiple contexts. The map points to where each one lives:

```
/
├── CONTEXT-MAP.md
├── docs/
│   └── adr/                          ← system-wide decisions
├── src/
│   ├── ordering/
│   │   ├── CONTEXT.md
│   │   └── docs/adr/                 ← context-specific decisions
│   └── billing/
│       ├── CONTEXT.md
│       └── docs/adr/
```

Create files lazily — only when you have something to write. If no `CONTEXT.md` exists, create one when the first term is resolved. If no `docs/adr/` exists, create it when the first ADR is needed.

## During the session

### Challenge against the glossary

When the user uses a term that conflicts with the existing language in `CONTEXT.md`, call it out immediately. "Your glossary defines 'cancellation' as X, but you seem to mean Y — which is it?"

### Sharpen fuzzy language

When the user uses vague or overloaded terms, propose a precise canonical term. "You're saying 'account' — do you mean the Customer or the User? Those are different things."

### Discuss concrete scenarios

When domain relationships are being discussed, stress-test them with specific scenarios. Invent scenarios that probe edge cases and force the user to be precise about the boundaries between concepts.

### Cross-reference with code

When the user states how something works, check whether the code agrees. If you find a contradiction, surface it: "Your code cancels entire Orders, but you just said partial cancellation is possible — which is right?"

### Update CONTEXT.md inline

When a term is resolved, update `CONTEXT.md` right there. Don't batch these up — capture them as they happen. Use the format in [CONTEXT-FORMAT.md](./CONTEXT-FORMAT.md).

`CONTEXT.md` should be totally devoid of implementation details. Do not treat `CONTEXT.md` as a spec, a scratch pad, or a repository for implementation decisions. It is a glossary and nothing else.

### Offer ADRs sparingly

Only offer to create an ADR when all three are true:

1. **Hard to reverse** — the cost of changing your mind later is meaningful
2. **Surprising without context** — a future reader will wonder "why did they do it this way?"
3. **The result of a real trade-off** — there were genuine alternatives and you picked one for specific reasons

If any of the three is missing, skip the ADR. Use the format in [ADR-FORMAT.md](./ADR-FORMAT.md).
```

### File: .agents/skills/grill-with-docs/SKILL.md
```markdown
---
name: grill-with-docs
description: A relentless interview to sharpen a plan or design, which also creates docs (ADR's and glossary) as we go.
disable-model-invocation: true
---

Run a `/grilling` session, using the `/domain-modeling` skill.
```

### File: .agents/skills/grilling/SKILL.md
```markdown
---
name: grilling
description: Grill the user relentlessly about a plan, decision, or idea. Use when the user wants to stress-test their thinking, or uses any 'grill' trigger phrases.
---

Interview me relentlessly about every aspect of this until we reach a shared understanding. Walk down each branch of the decision tree, resolving dependencies between decisions one-by-one. For each question, provide your recommended answer.

Ask the questions one at a time, waiting for feedback on each question before continuing. Asking multiple questions at once is bewildering.

If a *fact* can be found by exploring the environment (filesystem, tools, etc.), look it up rather than asking me. The *decisions*, though, are mine — put each one to me and wait for my answer.

Do not act on it until I confirm we have reached a shared understanding.
```

### File: .agents/skills/handoff/SKILL.md
```markdown
---
name: handoff
description: Compact the current conversation into a handoff document for another agent to pick up.
argument-hint: "What will the next session be used for?"
disable-model-invocation: true
---

Write a handoff document summarising the current conversation so a fresh agent can continue the work. Save to the temporary directory of the user's OS - not the current workspace.

Include a "suggested skills" section in the document, which suggests skills that the agent should invoke.

Do not duplicate content already captured in other artifacts (specs, plans, ADRs, issues, commits, diffs). Reference them by path or URL instead.

Redact any sensitive information, such as API keys, passwords, or personally identifiable information.

If the user passed arguments, treat them as a description of what the next session will focus on and tailor the doc accordingly.
```

### File: .agents/skills/implement/SKILL.md
```markdown
---
name: implement
description: "Implement a piece of work based on a spec or set of tickets."
disable-model-invocation: true
---

Implement the work described by the user in the spec or tickets.

Use /tdd where possible, at pre-agreed seams.

Run typechecking regularly, single test files regularly, and the full test suite once at the end.

Once done, use /code-review to review the work.

Commit your work to the current branch.
```

### File: .agents/skills/improve-codebase-architecture/HTML-REPORT.md
```markdown
# HTML Report Format

The architectural review is rendered as a single self-contained HTML file in the OS temp directory. Tailwind and Mermaid both come from CDNs. Mermaid handles graph-shaped diagrams reliably; hand-built divs and inline SVG handle the more editorial visuals (mass diagrams, cross-sections). Mix the two — don't lean on Mermaid for everything, it'll start to look generic.

## Scaffold

```html
<!doctype html>
<html lang="en">
  <head>
    <meta charset="utf-8" />
    <title>Architecture review — {{repo name}}</title>
    <script src="https://cdn.tailwindcss.com"></script>
    <script type="module">
      import mermaid from "https://cdn.jsdelivr.net/npm/mermaid@11/dist/mermaid.esm.min.mjs";
      mermaid.initialize({ startOnLoad: true, theme: "neutral", securityLevel: "loose" });
    </script>
    <style>
      /* small custom layer for things Tailwind doesn't cover cleanly:
         dashed seam lines, hand-drawn-feeling arrow heads, etc. */
      .seam { stroke-dasharray: 4 4; }
      .leak { stroke: #dc2626; }
      .deep { background: linear-gradient(135deg, #0f172a, #1e293b); }
    </style>
  </head>
  <body class="bg-stone-50 text-slate-900 font-sans">
    <main class="max-w-5xl mx-auto px-6 py-12 space-y-12">
      <header>...</header>
      <section id="candidates" class="space-y-10">...</section>
      <section id="top-recommendation">...</section>
    </main>
  </body>
</html>
```

## Header

Repo name, date, and a compact legend: solid box = module, dashed line = seam, red arrow = leakage, thick dark box = deep module. No introduction paragraph — straight into the candidates.

## Candidate card

The diagrams carry the weight. Prose is sparse, plain, and uses the glossary terms (from the `/codebase-design` skill) without ceremony.

Each candidate is one `<article>`:

- **Title** — short, names the deepening (e.g. "Collapse the Order intake pipeline").
- **Badge row** — recommendation strength (`Strong` = emerald, `Worth exploring` = amber, `Speculative` = slate), plus a tag for the dependency category (`in-process`, `local-substitutable`, `ports & adapters`, `mock`).
- **Files** — monospaced list, `font-mono text-sm`.
- **Before / After diagram** — the centrepiece. Two columns, side by side. See patterns below.
- **Problem** — one sentence. What hurts.
- **Solution** — one sentence. What changes.
- **Wins** — bullets, ≤6 words each. e.g. "Tests hit one interface", "Pricing logic stops leaking", "Delete 4 shallow wrappers".
- **ADR callout** (if applicable) — one line in an amber-tinted box.

No paragraphs of explanation. If the diagram needs a paragraph to be understood, redraw the diagram.

## Diagram patterns

Pick the pattern that fits the candidate. Mix them. Don't make every diagram look the same — variety is part of the point.

### Mermaid graph (the workhorse for dependencies / call flow)

Use a Mermaid `flowchart` or `graph` when the point is "X calls Y calls Z, and look at the mess." Wrap it in a Tailwind-styled card so it doesn't feel parachuted in. Style with classDef to colour leakage edges red and the deep module dark. Sequence diagrams work well for "before: 6 round-trips; after: 1."

```html
<div class="rounded-lg border border-slate-200 bg-white p-4">
  <pre class="mermaid">
    flowchart LR
      A[OrderHandler] --> B[OrderValidator]
      B --> C[OrderRepo]
      C -.leak.-> D[PricingClient]
      classDef leak stroke:#dc2626,stroke-width:2px;
      class C,D leak
  </pre>
</div>
```

### Hand-built boxes-and-arrows (when Mermaid's layout fights you)

Modules as `<div>`s with borders and labels. Arrows as inline SVG `<line>` or `<path>` elements positioned absolutely over a relative container. Reach for this when you want the "after" diagram to feel like one thick-bordered deep module with greyed-out internals — Mermaid won't render that with the right weight.

### Cross-section (good for layered shallowness)

Stack horizontal bands (`h-12 border-l-4`) to show layers a call passes through. Before: 6 thin layers each doing nothing. After: 1 thick band labelled with the consolidated responsibility.

### Mass diagram (good for "interface as wide as implementation")

Two rectangles per module — one for interface surface area, one for implementation. Before: interface rectangle is nearly as tall as the implementation rectangle (shallow). After: interface rectangle is short, implementation rectangle is tall (deep).

### Call-graph collapse

Before: a tree of function calls rendered as nested boxes. After: the same tree collapsed into one box, with the now-internal calls shown faded inside it.

## Style guidance

- Lean editorial, not corporate-dashboard. Generous whitespace. Serif optional for headings (`font-serif` works well with stone/slate).
- Colour sparingly: one accent (emerald or indigo) plus red for leakage and amber for warnings.
- Keep diagrams ~320px tall so before/after sits comfortably side by side without scrolling.
- Use `text-xs uppercase tracking-wider` for module labels inside diagrams — they should read as schematic, not as UI.
- The only scripts are the Tailwind CDN and the Mermaid ESM import. The report is otherwise static — no app code, no interactivity beyond Mermaid's own rendering.

## Top recommendation section

One larger card. Candidate name, one sentence on why, anchor link to its card. That's it.

## Tone

Plain English, concise — but the architectural nouns and verbs come straight from the `/codebase-design` skill. Concision is not an excuse to drift.

**Use exactly:** module, interface, implementation, depth, deep, shallow, seam, adapter, leverage, locality.

**Never substitute:** component, service, unit (for module) · API, signature (for interface) · boundary (for seam) · layer, wrapper (for module, when you mean module).

**Phrasings that fit the style:**

- "Order intake module is shallow — interface nearly matches the implementation."
- "Pricing leaks across the seam."
- "Deepen: one interface, one place to test."
- "Two adapters justify the seam: HTTP in prod, in-memory in tests."

**Wins bullets** name the gain in glossary terms: *"locality: bugs concentrate in one module"*, *"leverage: one interface, N call sites"*, *"interface shrinks; implementation absorbs the wrappers"*. Don't write *"easier to maintain"* or *"cleaner code"* — those terms aren't in the glossary and don't earn their place.

No hedging, no throat-clearing, no "it's worth noting that…". If a sentence could be a bullet, make it a bullet. If a bullet could be cut, cut it. If a term isn't in the `/codebase-design` glossary, reach for one that is before inventing a new one.
```

### File: .agents/skills/improve-codebase-architecture/SKILL.md
```markdown
---
name: improve-codebase-architecture
description: Scan a codebase for deepening opportunities, present them as a visual HTML report, then grill through whichever one you pick.
disable-model-invocation: true
---

# Improve Codebase Architecture

Surface architectural friction and propose **deepening opportunities** — refactors that turn shallow modules into deep ones. The aim is testability and AI-navigability.

This command is _informed_ by the project's domain model and built on a shared design vocabulary:

- Run the `/codebase-design` skill for the architecture vocabulary (**module**, **interface**, **depth**, **seam**, **adapter**, **leverage**, **locality**) and its principles (the deletion test, "the interface is the test surface", "one adapter = hypothetical seam, two = real"). Use these terms exactly in every suggestion — don't drift into "component," "service," "API," or "boundary."
- The domain language in `CONTEXT.md` gives names to good seams; ADRs in `docs/adr/` record decisions this command should not re-litigate.

## Process

### 1. Explore

**Scope before you scan — YAGNI.** Deepening a module pays off by making future changes to it easier, so put extra weight on the parts of the codebase that have recently changed. Decide *where* to look before you look:

- If the user named a direction — a module, a subsystem, a pain point — take it, and skip the inference below.
- Otherwise, walk back a good stretch of the commit history (`git log --oneline`) to find the codebase's hot spots — the files and areas that keep coming up — and let those paths pull your attention first. If the changes are scattered with no clear hot spot, widen the net.

Read the project's domain glossary (`CONTEXT.md`) and any ADRs in the area you're touching first.

Then use the Agent tool with `subagent_type=Explore` to walk the codebase. Don't follow rigid heuristics — explore organically and note where you experience friction:

- Where does understanding one concept require bouncing between many small modules?
- Where are modules **shallow** — interface nearly as complex as the implementation?
- Where have pure functions been extracted just for testability, but the real bugs hide in how they're called (no **locality**)?
- Where do tightly-coupled modules leak across their seams?
- Which parts of the codebase are untested, or hard to test through their current interface?

Apply the **deletion test** to anything you suspect is shallow: would deleting it concentrate complexity, or just move it? A "yes, concentrates" is the signal you want.

### 2. Present candidates as an HTML report

Write a self-contained HTML file to the OS temp directory so nothing lands in the repo. Resolve the temp dir from `$TMPDIR`, falling back to `/tmp` (or `%TEMP%` on Windows), and write to `<tmpdir>/architecture-review-<timestamp>.html` so each run gets a fresh file. Open it for the user — `xdg-open <path>` on Linux, `open <path>` on macOS, `start <path>` on Windows — and tell them the absolute path.

The report uses **Tailwind via CDN** for layout and styling, and **Mermaid via CDN** for diagrams where a graph/flow/sequence reliably communicates the structure. Mix Mermaid with hand-crafted CSS/SVG visuals — use Mermaid when relationships are graph-shaped (call graphs, dependencies, sequences), and hand-built divs/SVG when you want something more editorial (mass diagrams, cross-sections, collapse animations). Each candidate gets a **before/after visualisation**. Be visual.

For each candidate, render a card with:

- **Files** — which files/modules are involved
- **Problem** — why the current architecture is causing friction
- **Solution** — plain English description of what would change
- **Benefits** — explained in terms of locality and leverage, and how tests would improve
- **Before / After diagram** — side-by-side, custom-drawn, illustrating the shallowness and the deepening
- **Recommendation strength** — one of `Strong`, `Worth exploring`, `Speculative`, rendered as a badge

End the report with a **Top recommendation** section: which candidate you'd tackle first and why.

**Use CONTEXT.md vocabulary for the domain, and the `/codebase-design` vocabulary for the architecture.** If `CONTEXT.md` defines "Order," talk about "the Order intake module" — not "the FooBarHandler," and not "the Order service."

**ADR conflicts**: if a candidate contradicts an existing ADR, only surface it when the friction is real enough to warrant revisiting the ADR. Mark it clearly in the card (e.g. a warning callout: _"contradicts ADR-0007 — but worth reopening because…"_). Don't list every theoretical refactor an ADR forbids.

See [HTML-REPORT.md](HTML-REPORT.md) for the full HTML scaffold, diagram patterns, and styling guidance.

Do NOT propose interfaces yet. After the file is written, ask the user: "Which of these would you like to explore?"

### 3. Grilling loop

Once the user picks a candidate, run the `/grilling` skill to walk the decision tree with them — constraints, dependencies, the shape of the deepened module, what sits behind the seam, what tests survive.

Side effects happen inline as decisions crystallize — run the `/domain-modeling` skill to keep the domain model current as you go:

- **Naming a deepened module after a concept not in `CONTEXT.md`?** Add the term to `CONTEXT.md`. Create the file lazily if it doesn't exist.
- **Sharpening a fuzzy term during the conversation?** Update `CONTEXT.md` right there.
- **User rejects the candidate with a load-bearing reason?** Offer an ADR, framed as: _"Want me to record this as an ADR so future architecture reviews don't re-suggest it?"_ Only offer when the reason would actually be needed by a future explorer to avoid re-suggesting the same thing — skip ephemeral reasons ("not worth it right now") and self-evident ones.
- **Want to explore alternative interfaces for the deepened module?** Run the `/codebase-design` skill and use its design-it-twice parallel sub-agent pattern.
```

### File: .agents/skills/interview-me/SKILL.md
```markdown
---
name: interview-me
description: A relentless interview to sharpen a plan or design.
disable-model-invocation: true
---

Run a `/grilling` session.
```

### File: .agents/skills/loop-me/SKILL.md
```markdown
---
name: loop-me
description: Grill me about specs for the workflows I want to build, within this workspace.
disable-model-invocation: true
argument-hint: "A workflow to design, or nothing to go find one"
---

Run a stateful `/grilling` session whose only output is **workflow** specs. Use the grilling discipline — relentless, one question at a time, a recommended answer attached to each — aimed at the vocabulary and goal below. Create, edit, and delete specs as the grilling resolves things.

## The loop lens

A **loop** is a recurring pattern in the user's life: their career, their week, their morning, a single repeated activity. Picturing a life as loops within loops reveals how predictable its activities really are — which is what makes them worth **delegating**. Use the lens to find loops worth specifying, and propose ones the user hasn't noticed.

A **workflow** is the spec of one loop, made real. You run a workflow on a loop — the loop is its running instantiation. Workflows live in `workflows/*.md` and are the source of truth.

## Vocabulary

A shared language, reached for only when a workflow calls for it — never a checklist. **Mandate nothing structural**: a workflow needs no AI, no checkpoint, and no schedule unless the grilling shows it does.

- **Trigger** — what fires each run: an **event** (a new email, a new issue) or a **schedule** (every morning). Event-triggering is usually the more efficient.
- **Checkpoint** — a human-in-the-loop point where the user is asked to verify or decide. Some workflows have none and run autonomously; some use no AI at all.
- **Push right** — defer the checkpoint as far as it will go. Do maximal work before involving the human, so they are asked once, late, with everything prepared.
- **Brief** — what a checkpoint presents: a tight, decision-ready summary — what was produced, why, and a link down to the asset itself — never the raw output. The user reads a brief, not a draft. Speed of review is imperative.

## Definition of done

A workflow spec is done when an implementer agent could build it without asking a single question. Grill until then; nothing is done while a question remains.

## The workspace

- `workflows/*.md` — one spec per workflow.
- `NOTES.md` — raw notes on the user's world: the tools they use, the channels they process, and their own terminology for both. When it is empty or thin, interview them about their world before specifying anything. Sharpen fuzzy terms into canonical ones as they surface, and record them here.
```

### File: .agents/skills/prototype/LOGIC.md
```markdown
# Logic Prototype

A tiny interactive terminal app that lets the user drive a state model by hand. Use this when the question is about **business logic, state transitions, or data shape** — the kind of thing that looks reasonable on paper but only feels wrong once you push it through real cases.

## When this is the right shape

- "I'm not sure if this state machine handles the edge case where X then Y."
- "Does this data model actually let me represent the case where..."
- "I want to feel out what the API should look like before writing it."
- Anything where the user wants to **press buttons and watch state change**.

If the question is "what should this look like" — wrong branch. Use [UI.md](UI.md).

## Process

### 1. State the question

Before writing code, write down what state model and what question you're prototyping. One paragraph, in the prototype's README or a comment at the top of the file. A logic prototype that answers the wrong question is pure waste — make the question explicit so it can be checked later, whether the user is watching now or returning to it AFK.

### 2. Pick the language

Use whatever the host project uses. If the project has no obvious runtime (e.g. a docs repo), ask.

Match the project's existing conventions for tooling — don't add a new package manager or runtime just for the prototype.

### 3. Isolate the logic in a portable module

Put the actual logic — the bit that's answering the question — behind a small, pure interface that could be lifted out and dropped into the real codebase later. The TUI around it is throwaway; the logic module shouldn't be.

The right shape depends on the question:

- **A pure reducer** — `(state, action) => state`. Good when actions are discrete events and state is a single value.
- **A state machine** — explicit states and transitions. Good when "which actions are even legal right now" is part of the question.
- **A small set of pure functions** over a plain data type. Good when there's no implicit current state — just transformations.
- **A class or module with a clear method surface** when the logic genuinely owns ongoing internal state.

Pick whichever shape best fits the question being asked, *not* whichever is easiest to wire to a TUI. Keep it pure: no I/O, no terminal code, no `console.log` for control flow. The TUI imports it and calls into it; nothing flows the other direction.

This is what makes the prototype useful past its own lifetime: when the question's been answered, the validated reducer / machine / function set can be lifted into the real module on its own.

### 4. Build the smallest TUI that exposes the state

Build it as a **lightweight TUI** — on every tick, clear the screen (`console.clear()` / `print("\033[2J\033[H")` / equivalent) and re-render the whole frame. The user should always see one stable view, not an ever-growing scrollback.

Each frame has two parts, in this order:

1. **Current state**, pretty-printed and diff-friendly (one field per line, or formatted JSON). Use **bold** for field names or section headers and **dim** for less important context (timestamps, IDs, derived values). Native ANSI escape codes are fine — `\x1b[1m` bold, `\x1b[2m` dim, `\x1b[0m` reset. No need to pull in a styling library unless one is already in the project.
2. **Keyboard shortcuts**, listed at the bottom: `[a] add user  [d] delete user  [t] tick clock  [q] quit`. Bold the key, dim the description, or vice-versa — whatever reads cleanly.

Behaviour:

1. **Initialise state** — a single in-memory object/struct. Render the first frame on start.
2. **Read one keystroke (or one line)** at a time, dispatch to a handler that mutates state.
3. **Re-render** the full frame after every action — don't append, replace.
4. **Loop until quit.**

The whole frame should fit on one screen.

### 5. Make it runnable in one command

Add a script to the project's existing task runner (`package.json` scripts, `Makefile`, `justfile`, `pyproject.toml`). The user should run `pnpm run <prototype-name>` or equivalent — never need to remember a path.

If the host project has no task runner, just put the command at the top of the prototype's README.

### 6. Hand it over

Give the user the run command. They'll drive it themselves; the interesting moments are when they say "wait, that shouldn't be possible" or "huh, I assumed X would be different" — those are the bugs in the _idea_, which is the whole point. If they want new actions added, add them. Prototypes evolve.

### 7. Capture the answer and the prototype

Once the prototype has answered its question, capture the answer, then capture the prototype the way the [SKILL](SKILL.md) describes. The logic-specific mapping: the validated reducer / machine / function set lifts into the real module (the decision, absorbed); the TUI shell rides along to the throwaway branch that keeps the prototype as a primary source.

## Anti-patterns

- **Don't add tests.** A prototype that needs tests is no longer a prototype.
- **Don't wire it to the real database.** Use an in-memory store unless the question is specifically about persistence.
- **Don't generalise.** No "what if we wanted to support X later." The prototype answers one question.
- **Don't blur the logic and the TUI together.** If the reducer / state machine references `console.log`, prompts, or terminal escape codes, it's no longer portable. Keep the TUI as a thin shell over a pure module.
- **Don't ship the TUI shell into production.** The shell is optimised for being driven by hand from a terminal. The logic module behind it is the bit worth keeping.
```

### File: .agents/skills/prototype/SKILL.md
```markdown
---
name: prototype
description: Build a throwaway prototype to answer a design question. Use when the user wants to sanity-check whether a state model or logic feels right, or explore what a UI should look like.
---

# Prototype

A prototype is **throwaway code that answers a question**. The question decides the shape.

## Pick a branch

Identify which question is being answered — from the user's prompt, the surrounding code, or by asking if the user is around:

- **"Does this logic / state model feel right?"** → [LOGIC.md](LOGIC.md). Build a tiny interactive terminal app that pushes the state machine through cases that are hard to reason about on paper.
- **"What should this look like?"** → [UI.md](UI.md). Generate several radically different UI variations on a single route, switchable via a URL search param and a floating bottom bar.

The two branches produce very different artifacts — getting this wrong wastes the whole prototype. If the question is genuinely ambiguous and the user isn't reachable, default to whichever branch better matches the surrounding code (a backend module → logic; a page or component → UI) and state the assumption at the top of the prototype.

## Rules that apply to both

1. **Throwaway from day one, and clearly marked as such.** Locate the prototype code close to where it will actually be used (next to the module or page it's prototyping for) so context is obvious — but name it so a casual reader can see it's a prototype, not production. For throwaway UI routes, obey whatever routing convention the project already uses; don't invent a new top-level structure.
2. **One command to run.** Whatever the project's existing task runner supports — `pnpm <name>`, `python <path>`, `bun <path>`, etc. The user must be able to start it without thinking.
3. **No persistence by default.** State lives in memory. Persistence is the thing the prototype is _checking_, not something it should depend on. If the question explicitly involves a database, hit a scratch DB or a local file with a clear "PROTOTYPE — wipe me" name.
4. **Skip the polish.** No tests, no error handling beyond what makes the prototype _runnable_, no abstractions. The point is to learn something fast.
5. **Surface the state.** After every action (logic) or on every variant switch (UI), print or render the full relevant state so the user can see what changed.
6. **Capture it when done.** Fold any validated decision into the real code, then capture the prototype itself as a **primary source**: commit it to a throwaway branch, out of main, and leave a context pointer to that branch on the implementation issue. Capture the answer too — the verdict and the question it settled — in the issue or a commit. The main branch keeps only the validated decision.
```

### File: .agents/skills/prototype/UI.md
```markdown
# UI Prototype

Generate **several radically different UI variations** on a single route, switchable from a floating bottom bar. The user flips between variants in the browser, picks one (or steals bits from each), then throws the rest away.

If the question is about logic/state rather than what something looks like — wrong branch. Use [LOGIC.md](LOGIC.md).

## When this is the right shape

- "What should this page look like?"
- "I want to see a few options for this dashboard before committing."
- "Try a different layout for the settings screen."
- Any time the user would otherwise spend a day picking between three vague mockups in their head.

## Two sub-shapes — strongly prefer sub-shape A

A UI prototype is much easier to judge when it's **butting up against the rest of the app** — real header, real sidebar, real data, real density. A throwaway route on its own is a vacuum: every variant looks fine in isolation. Default to sub-shape A whenever there's a plausible existing page to host the variants. Only reach for sub-shape B if the prototype genuinely has no nearby home.

### Sub-shape A — adjustment to an existing page (preferred)

The route already exists. Variants are rendered **on the same route**, gated by a `?variant=` URL search param. The existing data fetching, params, and auth all stay — only the rendering swaps. This is the default; pick it unless there's a specific reason not to.

If the prototype is for something that doesn't yet have a page but *would naturally live inside one* (a new section of the dashboard, a new card on the settings screen, a new step in an existing flow) — that's still sub-shape A. Mount the variants inside the host page.

### Sub-shape B — a new page (last resort)

Only use this when the thing being prototyped genuinely has no existing page to live inside — e.g. an entirely new top-level surface, or a flow that can't be embedded anywhere sensible.

Create a **throwaway route** following whatever routing convention the project already uses — don't invent a new top-level structure. Name it so it's obviously a prototype (e.g. include the word `prototype` in the path or filename). Same `?variant=` pattern.

Before committing to sub-shape B, sanity-check: is there really no existing page this could be embedded in? An empty route hides design problems that a populated one would expose.

In both sub-shapes the floating bottom bar is identical.

## Process

### 1. State the question and pick N

Default to **3 variants**. More than 5 stops being radically different and starts being noise — cap there.

Write down the plan in one line, in the prototype's location or a top-of-file comment:

> "Three variants of the settings page, switchable via `?variant=`, on the existing `/settings` route."

This works whether the user is here to push back or not.

### 2. Generate radically different variants

Draft each variant. Hold each one to:

- The page's purpose and the data it has access to.
- The project's component library / styling system (TailwindCSS, shadcn, MUI, plain CSS, whatever).
- A clear exported component name, e.g. `VariantA`, `VariantB`, `VariantC`.

Variants must be **structurally different** — different layout, different information hierarchy, different primary affordance, not just different colours. Three slightly-tweaked card grids isn't a UI prototype, it's wallpaper. If two drafts come out too similar, redo one with explicit "do not use a card grid" guidance.

### 3. Wire them together

Create a single switcher component on the route:

```tsx
// pseudo-code — adapt to the project's framework
const variant = searchParams.get('variant') ?? 'A';
return (
  <>
    {variant === 'A' && <VariantA {...data} />}
    {variant === 'B' && <VariantB {...data} />}
    {variant === 'C' && <VariantC {...data} />}
    <PrototypeSwitcher variants={['A','B','C']} current={variant} />
  </>
);
```

For sub-shape A (existing page): keep all the existing data fetching above the switcher; only the rendered subtree changes per variant.

For sub-shape B (new page): the throwaway route under `/prototype/<name>` mounts the same switcher.

### 4. Build the floating switcher

A small fixed-position bar at the bottom-centre of the screen with three pieces:

- **Left arrow** — cycles to the previous variant (wraps around).
- **Variant label** — shows the current variant key and, if the variant exports a name, that name too. e.g. `B — Sidebar layout`.
- **Right arrow** — cycles forward (wraps around).

Behaviour:

- Clicking an arrow updates the URL search param (use the framework's router — `router.replace` on Next, `navigate` on React Router, etc) so the variant is shareable and reload-stable.
- Keyboard: `←` and `→` arrow keys also cycle. Don't intercept arrow keys when an `<input>`, `<textarea>`, or `[contenteditable]` is focused.
- Visually distinct from the page (e.g. high-contrast pill, subtle shadow) so it's obviously not part of the design being evaluated.
- Hidden in production builds — gate on `process.env.NODE_ENV !== 'production'` or an equivalent check, so a stray prototype merge can't ship the bar to users.

Put the switcher in a single shared component so both sub-shapes can reuse it. Locate it wherever shared UI lives in the project.

### 5. Hand it over

Surface the URL (and the `?variant=` keys). The user will flip through whenever they get to it. The interesting feedback is usually **"I want the header from B with the sidebar from C"** — that's the actual design they want.

### 6. Capture the answer and clean up

Once a variant has won, capture the answer — which variant and why — then capture the prototype the way the [SKILL](SKILL.md) describes. Fold the winner into the real code and move the rest onto the throwaway branch, not into main:

- **Sub-shape A** — fold the winner into the existing page; drop the losing variants and the switcher from main.
- **Sub-shape B** — promote the winning variant to a real route; drop the throwaway route and the switcher from main.

The full set of variants is the primary source, so it lands on the throwaway branch, not the bin — variant components and the switcher left in the main branch rot fast and confuse the next reader.

## Anti-patterns

- **Variants that differ only in colour or copy.** That's a tweak, not a prototype. Real variants disagree about structure.
- **Sharing too much code between variants.** A shared `<Header>` is fine; a shared `<Layout>` defeats the point. Each variant should be free to throw out the layout.
- **Wiring variants to real mutations.** Read-only prototypes are fine. If a variant needs to mutate, point it at a stub — the question is "what should this look like", not "does the backend work".
- **Promoting the prototype directly to production.** The variant code was written under prototype constraints (no tests, minimal error handling). Rewrite it properly when you fold it in.
```

### File: .agents/skills/research/SKILL.md
```markdown
---
name: research
description: Investigate a question against high-trust primary sources and capture the findings as a Markdown file in the repo. Use when the user wants a topic researched, docs or API facts gathered, or reading legwork delegated to a background agent.
---

Spin up a **background agent** to do the research, so you keep working while it reads.

Its job:

1. Investigate the question against **primary sources** — official docs, source code, specs, first-party APIs — not a secondary write-up of them. Follow every claim back to the source that owns it.
2. Write the findings to a single Markdown file, citing each claim's source.
3. Save it where the repo already keeps such notes; match the existing convention, and if there is none, put it somewhere sensible and say where.
```

### File: .agents/skills/resolving-merge-conflicts/SKILL.md
```markdown
---
name: resolving-merge-conflicts
description: "Use when you need to resolve an in-progress git merge/rebase conflict."
---

1. **See the current state** of the merge/rebase. Check git history, and the conflicting files.

2. **Find the primary sources** for each conflict. Understand deeply why each change was made, and what the original intent was. Read the commit messages, check the PRs, check original issues/tickets.

3. **Resolve each hunk.** Preserve both intents where possible. Where incompatible, pick the one matching the merge's stated goal and note the trade-off. Do **not** invent new behaviour. Always resolve; never `--abort`.

4. Discover the project's **automated checks** and run them — typically typecheck, then tests, then format. Fix anything the merge broke.

5. **Finish the merge/rebase.** Stage everything and commit. If rebasing, continue the rebase process until all commits are rebased.
```

### File: .agents/skills/revit-build-deploy/SKILL.md
```markdown
---
name: revit-build-deploy
description: Configuration and procedures for Release and Debug builds. Use when configuring build profiles or deploying add-in builds.
---

# Revit Build & Deploy Playbook (`revit-build-deploy`)

Use this playbook to configure and execute Debug and Release builds across multiple target Revit versions.

## 1. Compilation Targets & Output Directory

All project `.csproj` compilations output to the common directory:
`output/Synthetic/`

## 2. Debug Build Profile

The Debug profile is optimized for local developer debugging and testing.

### Manifest Assembly Pathing
The build automatically executes the custom MSBuild task `ReplaceAddinPath` to update the `<Assembly>` tag in the local `.addin` manifest file to point to the active build output DLL (e.g., `output/Synthetic/Synthetic202X.dll`), enabling Revit to load the DLL directly from the workspace.

### Local Code Signing
To bypass Revit's publisher warnings, MSBuild automatically executes `signtool.exe` to sign the Debug DLL with a local self-signed certificate named `"Synthetic"`.

### Debug Manifest Deployment
To load the debug build in Revit:
* Copy the modified `.addin` manifest file from `output/Synthetic/` to the Revit Addins folder.
* *Note: A task is underway to automate copying the debug `.addin` to `%APPDATA%\Autodesk\Revit\Addins\<Year>\` to avoid ProgramData write permission issues.*

## 3. Release Build Profile

The Release profile compiles optimized assemblies for production distribution.

### Manifest Assembly Pathing
The build updates the `<Assembly>` tag in the `.addin` manifest to point to the production installation path:
`C:\ProgramData\Autodesk\Revit\Addins\Synthetic\Synthetic202X.dll`

### Code Signing
Release builds do **not** require any code signing.

### Production Deployment
To deploy release builds to the production UNC server:
1. Compile the solution in `Release` configuration:
   ```powershell
   & "C:\Program-Files-Path-to-MSBuild\MSBuild.exe" src/Synthetic.sln /t:Build /p:Configuration=Release /p:Platform="Any CPU"
   ```
2. Run the deployment batch file `Deploy to INC Server.bat` (based on `build/Deploy-To-INC-Server.template.bat`) to mirror the build output, manifests, and settings configurations to the server folder using `robocopy`.
```

### File: .agents/skills/revit-data-retrieval/SKILL.md
```markdown
---
name: revit-data-retrieval
description: Describes tactics for optimizing FilteredElementCollectors. Use when querying elements from the Revit database.
---

# Revit Data Retrieval Playbook (`revit-data-retrieval`)

Use these rules to optimize database queries and minimize memory hydration when retrieving elements using `FilteredElementCollector`.

## 1. The Three-Phase Query Protocol

Always stack filters from fastest (native C++) to slowest (managed C#) to prevent premature object hydration.

```csharp
// GOOD: Stacking Quick Filters -> Slow Filters -> Managed LINQ
var walls = new FilteredElementCollector(doc)
    .OfClass(typeof(Wall))                  // Phase 1: Quick Filter (C++)
    .WhereElementIsNotElementType()         // Phase 1: Quick Filter (C++)
    .WherePasses(parameterFilter)           // Phase 2: Slow Filter (C++)
    .Cast<Wall>()                           // Phase 3: Managed Boundary
    .Where(w => w.Name.StartsWith("EXT_")); // Phase 3: Managed LINQ (C#)
```

### Phase 1: Quick Filters (Native C++)
Apply quick filters immediately upon collector initialization. These execute in Revit's internal C++ memory and do not hydrate C# objects.
* Use `OfClass()` and `OfCategory()`.
* Use `WhereElementIsNotElementType()` or `WhereElementIsElementType()`.
* Apply other native quick filters like `WherePasses(ElementFilter)` where applicable.

### Phase 2: Slow Filters (Native C++)
Apply parameter-driven native filters (`ElementParameterFilter`) only after Phase 1 filters have already narrowed down the element pool. These execute in C++ but must open elements to read parameter data.

### Phase 3: Managed Queries (C# Memory)
Casting (`.Cast<T>()`, `.OfType<T>()`) and managed LINQ queries (`.Where()`, `.First()`, etc.) are **strictly prohibited** at the beginning of the collector chain. They must only run at the very end of the chain after the element pool has been heavily restricted by Phase 1 and Phase 2.

## 2. Parameter Queries Optimization

* **Large Pools:** Always use native `ElementParameterFilter` (Phase 2) when searching an unconstrained or large database pool for parameter values.
* **Small Pools:** You may use C# managed property evaluations (e.g. `el.LookupParameter("Name")?.AsString()`) in Phase 3 LINQ **only** if the element pool has already been filtered down to a small set (under ~100 elements) by Phase 1 filters.

## 3. Context-Aware Scoping

Match the collector's constructor scope to the business requirements of the command:
* **Active View / Visible Elements:** If the feature only affects visible elements or the current view, use the view-scoped constructor:
  ```csharp
  var collector = new FilteredElementCollector(doc, activeView.Id);
  ```
* **Document-Wide:** Use `new FilteredElementCollector(doc)` when the operation requires processing elements across the entire project (including invisible or unplaced elements).
```

### File: .agents/skills/revit-debug-by-user/SKILL.md
```markdown
---
name: revit-debug-by-user
description: Procedure for opening Visual Studio and running the debugger to review the build in Revit. Use when launching a debug session.
---

# Revit User Debugging Playbook (`revit-debug-by-user`)

Use this playbook to automatically initiate a Revit debugging session in Visual Studio.

## 1. Automated Debug Launch

> [!IMPORTANT]
> Because the background AI agent runs in a non-interactive Windows session (Session 0), running the launch script via agent tools will spawn Visual Studio and Revit headlessly in the background. To see the graphical user interface, **the user must run these commands directly in their own interactive terminal**.

To open Visual Studio, select the correct project, and start debugging automatically, run one of the following commands in your local terminal:

* **Debug the latest supported version (from `revit-config.md`):**
  ```powershell
  powershell -File .agents/skills/revit-debug-by-user/scripts/start_debugging.ps1
  ```
* **Debug a specific target version (e.g., Revit 2024):**
  ```powershell
  powershell -File .agents/skills/revit-debug-by-user/scripts/start_debugging.ps1 -Version 2024
  ```

## 2. Interactive Debugging Steps

Once the script initiates the session:

1. **Verify Visual Studio Setup:**
   * Visual Studio will open (or attach to an existing running instance) and load `Synthetic.sln`.
   * The startup project will automatically change to the selected version (e.g., `Synthetic2026`).
   * The debugger will start immediately (F5), building the project, signing the DLL, and launching Revit.
2. **Review in Revit:**
   * Place breakpoints inside the desired source files (e.g. command `Execute` methods).
   * Open the target test model in Revit.
   * Run the command from the custom "Synthetic" ribbon tab.
   * Visual Studio will catch the execution path at your breakpoints.
3. **Closing:**
   * Visual Studio and Revit will remain open for your inspection until you close them.
```

### File: .agents/skills/revit-extensible-storage/SKILL.md
```markdown
---
name: revit-extensible-storage
description: Requirements for single-field JSON payload extensible storage schemas and progressive migration. Use when writing database serialization or settings persistence code.
---

# Revit Extensible Storage Playbook (`revit-extensible-storage`)

Use these rules to persist settings or complex payloads inside the Revit document database using Extensible Storage.

## 1. Single-Field JSON Payload Standard

To keep database schemas stable and minimize database schema modifications, wrap all configuration data in JSON payloads:

* **Field Signature:** Every settings schema must define exactly one `string` field named `JsonData` to store the serialized JSON payload (along with an optional `ModuleKey` string if storing multiple settings under one schema).
* **JSON-Level Upgrades:** Prefer handling configuration additions, renames, or default value updates purely within C# (using `Newtonsoft.Json` settings, converters, or ignore-missing attributes) rather than altering the Revit schema structure.

## 2. Algorithmic GUID Generation

When a new Revit schema is required, generate its Guid using this deterministic algorithm:

1. Format a string combining the target schema class name and the current universal timestamp: `ClassName_YYYYMMDD_HHMMSS`.
2. Compute the **MD5 hash** of this string (encoded as UTF-8) to produce a 128-bit (16-byte) hash.
3. Pass the hash bytes to the `new Guid(byte[])` constructor.
4. Hardcode the calculated Guid into your C# source file as a static constant (e.g. `private static readonly Guid SchemaGuid = new Guid("...")`).

## 3. When Revit Schema Migration is Needed

Revit schema migrations (creating a new schema and running a migration runner) are rare and must **only** be performed when:
* The Revit schema structure itself changes (e.g. adding new fields to the `SchemaBuilder`, or changing read/write access permissions).
* The storage container architecture changes (e.g. moving from `DataStorage` elements to another element category).

## 4. Progressive Migration & Dual Cleanup

If a Revit schema migration is necessary:

1. **Sequential Runner:** Implement an explicit migration runner that maps data sequentially: V1 -> V2 -> V3. Read the V1 JSON string, deserialize/map it, and save the updated JSON payload to the V2 schema.
2. **Dual Cleanup Protocol:** To prevent database bloat, the migration runner must delete the legacy data immediately after a successful transfer:
   * Delete the legacy `Entity` from the host elements using `element.DeleteEntity(legacySchema)`.
   * If the host element is a dedicated `DataStorage` element containing no other active entities, delete the `DataStorage` element from the document.
   * Erase the legacy schema metadata definition from the document by invoking `Schema.EraseSchemaAndAllEntities(legacySchema)`.
```

### File: .agents/skills/revit-external-events/SKILL.md
```markdown
---
name: revit-external-events
description: Modeless context patterns for external events. Use when writing code that executes Revit API code from modeless windows (WPF) or background threads.
---

# Revit Modeless External Events Playbook (`revit-external-events`)

Use this playbook to safely execute Revit API modifications from a modeless context (such as WPF windows, async tasks, or background threads) without triggering cross-thread exceptions.

## 1. The Dedicated Handler Pattern

All modeless Revit interactions must use dedicated, feature-specific implementations of `IExternalEventHandler` and `ExternalEvent`:

* **Single-Instance Lifetime:** Instantiate the `ExternalEvent` exactly once (e.g. during the WPF View Model's initialization) using `ExternalEvent.Create(handler)`. Keep the event instance alive as a private class field, and dispose of it when the UI window closes.
* **Thread-Safe Data Handoff:** Transfer inputs from the UI thread to the handler by updating public properties on the handler class before calling `.Raise()`.

## 2. Gating and UI Synchronization

To prevent duplicate runs, race conditions, or Revit UI lockups from multiple rapid user clicks:

1. **Disable UI:** Immediately disable the triggering buttons/controls in the WPF UI before calling `externalEvent.Raise()`.
2. **Execution Gating:** The handler must check an internal `IsRunning` boolean flag at the start of `Execute()` to immediately discard duplicate event dispatches.
3. **Completion Dispatch Callback:** Ensure that a `finally` block in the handler dispatches a notification back to the UI thread (via the WPF Dispatcher) to clear the busy state and re-enable the UI controls:

```csharp
public void Execute(UIApplication app)
{
    if (IsRunning) return;
    IsRunning = true;

    try
    {
        // Revit API operations...
    }
    catch (Exception ex)
    {
        LogException(ex);
    }
    finally
    {
        IsRunning = false;
        // Dispatch UI update back to the main WPF thread
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            _viewModel.OnExecutionCompleted();
        });
    }
}
```

## 3. Explicit Transaction Handling

Revit does **not** wrap external event executions in automatic transactions. You must explicitly instantiate and manage transactions inside the handler's `Execute` method:

* Wrap database changes in a `Transaction` or `TransactionGroup` using standard `using` blocks.
* Follow the transaction playbooks for error logging, rollback safety, and transaction group assimilation.
```

### File: .agents/skills/revit-failure-handling/SKILL.md
```markdown
---
name: revit-failure-handling
description: How to implement IFailuresPreprocessor to suppress/resolve Revit native dialog alerts. Use when handling warnings/errors or using transactions.
---

# Revit Failure Handling Playbook (`revit-failure-handling`)

Use this playbook to programmatically suppress warnings, resolve expected errors, and log failure telemetry during database transactions.

## 1. The Dedicated Preprocessor Pattern

To ensure clean isolation of domain rules:
* Create dedicated implementations of `IFailuresPreprocessor` for specific subsystems or commands (e.g. `SuppressConstraintsPreprocessor`).
* Attach the preprocessor to the `Transaction` before calling `Start()`:
  ```csharp
  FailureHandlingOptions options = transaction.GetFailureHandlingOptions();
  options.SetFailuresPreprocessor(new MyDedicatedPreprocessor());
  transaction.SetFailureHandlingOptions(options);
  ```

## 2. Failure Filtering and Resolution Rules

* **Targeted ID Filtering:** By default, match against specific `FailureDefinitionId`s (from `BuiltInFailures`) to resolve or delete warnings:
  ```csharp
  if (failure.GetFailureDefinitionId() == BuiltInFailures.GroupFailures.GroupConstraintsFailed)
  {
      failuresAccessor.DeleteWarning(failure);
  }
  ```
* **Blanket Suppression Gate:** You are **strictly prohibited** from implementing blanket warning suppression (suppressing all warnings in a transaction without ID matching) unless you obtain explicit developer/user permission.
* **Telemetry and Continuation:** For unresolvable errors:
  1. Extract and log the failure details (Failure Definition ID, severity, and message description) to the application log/telemetry stream.
  2. Return `FailureProcessingResult.Continue` to let Revit bubble up the warning/error safely.
```

### File: .agents/skills/revit-journal-telemetry/SKILL.md
```markdown
---
name: revit-journal-telemetry
description: How to diagnose crashes, exceptions, memory growth, and shutdown cleanliness by tailing and searching Revit journal session logs. Use when troubleshooting Revit run sessions, test failures, or crashes.
---

# Revit Journal Telemetry Playbook (`revit-journal-telemetry`)

Use this playbook to analyze Autodesk Revit journal files and diagnose unhandled exceptions, memory leaks, database transaction boundary lockups, and startup latency.

## 1. Diagnostic CLI Commands

The diagnostic utility is located at `.agents/skills/revit-journal-telemetry/scripts/revit_journal_tool.py`. By default, it targets the journal folder corresponding to the project's configuration-defined `CURRENT_LATEST_VERSION`.

### Run Telemetry Diagnostics
Generate a full session analysis of the active or most recent Revit run:
```powershell
python .agents/skills/revit-journal-telemetry/scripts/revit_journal_tool.py --analyze
```

### Real-Time Live Watch Mode
Continuously tail and watch the active journal for exceptions, warnings, transactions, and RAM allocation logs during a live debug run:
```powershell
python .agents/skills/revit-journal-telemetry/scripts/revit_journal_tool.py --watch
```

### Search with Line Context
Locate a specific keyword or exception pattern in the log with surrounding context lines (defaults to 15 lines):
```powershell
python .agents/skills/revit-journal-telemetry/scripts/revit_journal_tool.py --search "InvalidObjectException"
```

### Version Folder Override
Target a different Revit version folder for analysis:
```powershell
python .agents/skills/revit-journal-telemetry/scripts/revit_journal_tool.py --version 2024 --analyze
```

### Adjusting Telemetry Output Limits
You can adjust the limits of exceptions, warnings, actions, and memory stats printed by using optional arguments:
* `--limit-exceptions <N>`: Adjust the max exceptions reported (default: 10).
* `--limit-warnings <N>`: Adjust the max warnings reported (default: 15).
* `--limit-actions <N>`: Adjust the sliding UI action sequence history size (default: 10).
* `--limit-memory <N>`: Adjust the count of memory logs displayed at startup and session end (default: 3).
* `--limit-shutdown-scan <N>`: Adjust the count of trailing lines scanned for clean exit signatures (default: 200).

Example:
```powershell
python .agents/skills/revit-journal-telemetry/scripts/revit_journal_tool.py --analyze --limit-exceptions 5 --limit-actions 15
```

---

## 2. Telemetry Output & Diagnostics Guide

The telemetry engine reads the journal file sequentially and parses the following information:

* **[STARTUP] Startup Latency:** Calculates the exact milliseconds elapsed between the start and completion of the Synthetic ribbon loading sequence. 
* **[EXCEPTIONS] Managed Exception Stack:** Extracts standard C# stack traces (matching `System.Exception`, `at Synthetic.`, etc.) and bundles them into single blocks to capture the full context of unhandled add-in code failures.
* **[WARNINGS] System Warnings:** Filters for lines containing `DBG_WARN`, `DBG_INFO`, or explicit `error`/`fail` messages, ignoring benign API success messages to reduce log noise.
* **[MEMORY] Memory Stats Profile:** Tracks RAM utilization logs over the session, printing the initial vs. latest statistics to highlight memory leak growth.
* **[TRANSACTIONS] Transaction Boundaries:** Scans for starting, committing, and rolling back of database transactions. Identifies any **orphaned locks** (transactions started but never committed or rolled back), which cause Revit's database to lock up.
* **[SHUTDOWN STATUS] Final Exit Classification:**
  * `[OK] Shutdown CLEAN`: The log terminated with the `Destroy Display Manager` exit signature, and no dump files were found.
  * `[ACTIVE] Session Active`: The Revit process is still alive and running on the OS process list.
  * `[CRASH] Abnormal (Dump File)`: A matching `.dmp` crash dump file was found in the Journals folder.
  * `[FORCE-KILLED] Abnormal (Process Dead, No Clean Signature)`: Revit is no longer running, but failed to log its clean exit signature (e.g., forced shut down or killed via Task Manager).
* **[REPRODUCTION TRAIL] Pre-Crash UI Actions:** Prints the last 10 UI commands (`Jrn.Command`, view changes, button clicks) preceding a diagnosed crash or exception. Use this trail to reconstruct a step-by-step reproduction path.
```

### File: .agents/skills/revit-journal-telemetry/scripts/revit_journal_tool.py
```python
import os
import sys
import argparse
import re
import subprocess
import time
from datetime import datetime

# Default directories to ignore in path searches
DEFAULT_EXCLUDES = {'.git', 'bin', 'obj', 'packages'}

def find_workspace_root():
    """Walk up from this script's directory until a .git marker is found."""
    current = os.path.dirname(os.path.abspath(__file__))
    while True:
        if os.path.exists(os.path.join(current, '.git')):
            return current
        parent = os.path.dirname(current)
        if parent == current:
            # Fallback to current script directory if not found
            return os.path.dirname(os.path.abspath(__file__))
        current = parent

def get_current_latest_version():
    """Read the CURRENT_LATEST_VERSION value from docs/agents/revit-config.md."""
    try:
        workspace_dir = find_workspace_root()
        config_file = os.path.join(workspace_dir, "docs", "agents", "revit-config.md")
        if os.path.exists(config_file):
            with open(config_file, "r", encoding="utf-8") as f:
                content = f.read()
                # Matches patterns like CURRENT_LATEST_VERSION = 2026 or CURRENT_LATEST_VERSION = "2026"
                match = re.search(r'CURRENT_LATEST_VERSION\s*=\s*"?(\d+)"?', content)
                if match:
                    return match.group(1)
    except Exception:
        pass
    return "2026"  # Safe default fallback

def get_latest_journal(version):
    """Locates the most recently modified Revit journal file in the local AppData directory."""
    journal_dir = os.path.expandvars(f"%LOCALAPPDATA%\\Autodesk\\Revit\\Autodesk Revit {version}\\Journals")
    if not os.path.exists(journal_dir):
        print(f"[-] Directory does not exist: {journal_dir}", file=sys.stderr)
        return None
    files = [os.path.join(journal_dir, f) for f in os.listdir(journal_dir) if f.endswith(".txt") and f.startswith("journal")]
    if not files:
        print(f"[-] No journal files found in: {journal_dir}", file=sys.stderr)
        return None
    latest = max(files, key=os.path.getmtime)
    return latest

def read_journal_lines(filepath):
    """Safely reads lines from a journal file, trying various common Revit journal encodings."""
    encodings = ["utf-8-sig", "utf-16", "utf-8", "cp1252", "latin-1"]
    for enc in encodings:
        try:
            with open(filepath, "r", encoding=enc, errors="strict") as f:
                return f.readlines(), enc
        except (UnicodeDecodeError, LookupError):
            continue
    # Fallback with ignore errors
    with open(filepath, "r", encoding="utf-8", errors="ignore") as f:
        return f.readlines(), "utf-8 (fallback)"

def is_revit_running():
    """Checks if Revit.exe is currently active in the OS process table."""
    try:
        output = subprocess.check_output('tasklist /FI "IMAGENAME eq Revit.exe"', shell=True, text=True)
        return "Revit.exe" in output
    except Exception:
        return False

def extract_timestamp(line):
    """Attempts to parse a standard Revit journal timestamp from a line."""
    # Timestamps look like: ' 0:< 16-Jul-2026 12:00:00.123;
    match = re.search(r'(\d{2}-[A-Za-z]{3}-\d{4} \d{2}:\d{2}:\d{2}\.\d{3})', line)
    if match:
        try:
            return datetime.strptime(match.group(1), "%d-%b-%Y %H:%M:%S.%f")
        except ValueError:
            pass
    return None

def analyze_line_for_diagnostics(line, line_idx, state_dict, limit_actions=10):
    """Utility to analyze a single journal line, updating the state tracking dictionary."""
    line_clean = line.strip()
    
    # 1. Action Sequence Tracing
    if any(x in line for x in ["Jrn.Command", "Jrn.RibbonEvent", "Jrn.ComboBox", "Jrn.ActiveView", "Jrn.UIEvent"]):
        state_dict["actions"].append((line_idx + 1, line_clean))
        if len(state_dict["actions"]) > limit_actions:
            state_dict["actions"].pop(0)

    # 2. Managed Exceptions Tracking
    if "exception" in line_clean.lower() or ("at " in line_clean and re.search(r'\b[A-Za-z0-9_]+\.[A-Za-z0-9_.]+\(', line_clean)):
        if not state_dict["in_stack_trace"]:
            state_dict["in_stack_trace"] = True
            state_dict["current_exception"] = [f"Line {line_idx+1}: {line_clean}"]
        else:
            state_dict["current_exception"].append(line_clean)
    else:
        if state_dict["in_stack_trace"]:
            state_dict["exceptions"].append("\n".join(state_dict["current_exception"]))
            state_dict["in_stack_trace"] = False
            state_dict["current_exception"] = []

    # 3. Warning/Error Log Scanning
    if "DBG_WARN" in line or "DBG_INFO" in line or "error" in line.lower() or "fail" in line.lower():
        if not any(x in line for x in ["API_SUCCESS", "GetPreferences", "LoadLatestEpoch"]):
            state_dict["warnings"].append((line_idx + 1, line_clean))

    # 4. Memory Statistics Scanning
    if "RAM Statistics:" in line or "Delta VM:" in line:
        state_dict["memory_logs"].append((line_idx + 1, line_clean))

    # 5. Transaction Boundary Auditing
    # Scans for common start/commit/rollback markers
    trans_start = re.search(r'(?:transaction\s*:\s*start|start\s*transaction)\s*:\s*([^;]+)', line_clean, re.I)
    trans_commit = re.search(r'(?:transaction\s*:\s*commit|commit\s*transaction)\s*:\s*([^;]+)', line_clean, re.I)
    trans_rollback = re.search(r'(?:transaction\s*:\s*rollback|rollback\s*transaction)\s*:\s*([^;]+)', line_clean, re.I)
    
    if trans_start:
        name = trans_start.group(1).strip()
        state_dict["active_transactions"][name] = line_idx + 1
    elif trans_commit:
        name = trans_commit.group(1).strip()
        state_dict["active_transactions"].pop(name, None)
    elif trans_rollback:
        name = trans_rollback.group(1).strip()
        state_dict["active_transactions"].pop(name, None)
        state_dict["rolled_back_transactions"].append((line_idx + 1, name))

    # 6. Startup Latency Benchmarking
    if "Synthetic" in line:
        ts = extract_timestamp(line)
        if ts:
            if not state_dict["synthetic_first_ts"]:
                state_dict["synthetic_first_ts"] = ts
            state_dict["synthetic_last_ts"] = ts

def analyze_journal(lines, filepath, limit_exceptions=10, limit_warnings=15, limit_actions=10, limit_memory=3, limit_shutdown_scan=200):
    """Performs full diagnostics on the journal lines."""
    state = {
        "exceptions": [],
        "in_stack_trace": False,
        "current_exception": [],
        "warnings": [],
        "memory_logs": [],
        "actions": [],
        "active_transactions": {},
        "rolled_back_transactions": [],
        "synthetic_first_ts": None,
        "synthetic_last_ts": None
    }

    for idx, line in enumerate(lines):
        analyze_line_for_diagnostics(line, idx, state, limit_actions=limit_actions)

    # Flush final exception block if still open
    if state["in_stack_trace"] and state["current_exception"]:
        state["exceptions"].append("\n".join(state["current_exception"]))

    analysis = []
    analysis.append("=== TELEMETRY DIAGNOSTIC ANALYSIS ===")
    
    # 1. Startup Latency Report
    if state["synthetic_first_ts"] and state["synthetic_last_ts"]:
        delta = state["synthetic_last_ts"] - state["synthetic_first_ts"]
        latency_ms = int(delta.total_seconds() * 1000)
        analysis.append(f"\n[STARTUP] Synthetic Add-In Load Time: {latency_ms} ms")
        analysis.append(f"  - Initiated: {state['synthetic_first_ts'].strftime('%H:%M:%S.%f')[:-3]}")
        analysis.append(f"  - Completed: {state['synthetic_last_ts'].strftime('%H:%M:%S.%f')[:-3]}")
    else:
        analysis.append("\n[STARTUP] Synthetic startup latency could not be calculated (missing lifecycle logs).")

    # 2. Managed Exceptions
    analysis.append(f"\n[EXCEPTIONS] Managed Exceptions Found: {len(state['exceptions'])}")
    for exc in state["exceptions"][:limit_exceptions]:
        analysis.append("-" * 40)
        analysis.append(exc)
    if len(state["exceptions"]) > limit_exceptions:
        analysis.append(f"... and {len(state['exceptions']) - limit_exceptions} more exceptions.")

    # 3. Warnings
    analysis.append(f"\n[WARNINGS] Potential Error/Warning Logs Found: {len(state['warnings'])}")
    for line_num, warning in state["warnings"][:limit_warnings]:
        analysis.append(f"  Line {line_num}: {warning}")
    if len(state["warnings"]) > limit_warnings:
        analysis.append(f"... and {len(state['warnings']) - limit_warnings} more warnings.")

    # 4. Memory Profiling
    analysis.append(f"\n[MEMORY] Memory Logs Found: {len(state['memory_logs'])}")
    if len(state["memory_logs"]) > 0:
        analysis.append("Initial Memory Logs:")
        for line_num, stat in state["memory_logs"][:limit_memory]:
            analysis.append(f"  Line {line_num}: {stat}")
        if len(state["memory_logs"]) > (limit_memory * 2):
            analysis.append("...")
            analysis.append("Latest Memory Logs:")
            for line_num, stat in state["memory_logs"][-limit_memory:]:
                analysis.append(f"  Line {line_num}: {stat}")
        elif len(state["memory_logs"]) > limit_memory:
            for line_num, stat in state["memory_logs"][limit_memory:]:
                analysis.append(f"  Line {line_num}: {stat}")

    # 5. Transactions Telemetry
    analysis.append(f"\n[TRANSACTIONS] Orphaned Database Locks: {len(state['active_transactions'])}")
    for name, line_num in state["active_transactions"].items():
        analysis.append(f"  [LOCKED] Transaction '{name}' opened at line {line_num} was never committed/rolled back!")
    if state["rolled_back_transactions"]:
        analysis.append(f"Rolled-Back Transactions: {len(state['rolled_back_transactions'])}")
        for line_num, name in state["rolled_back_transactions"][:5]:
            analysis.append(f"  Line {line_num}: Transaction '{name}' was Rolled Back")

    # 6. Shutdown Cleanliness & Process Check
    last_scan = lines[-limit_shutdown_scan:] if len(lines) >= limit_shutdown_scan else lines
    destroy_display_found = any("Destroy Display Manager" in ln for ln in last_scan)
    
    # Check for crash dumps
    crash_detected = False
    journal_dir = os.path.dirname(filepath)
    journal_basename = os.path.splitext(os.path.basename(filepath))[0]
    dmp_pattern = os.path.join(journal_dir, journal_basename + "*.dmp")
    import glob as _glob
    dmp_files = _glob.glob(dmp_pattern)
    if dmp_files:
        crash_detected = True
        analysis.append(f"\n[CRITICAL] Crash dump file(s) found for this journal session:")
        for d in dmp_files:
            analysis.append(f"  {d}")

    revit_active = is_revit_running()
    
    analysis.append("\n[SHUTDOWN STATUS]")
    if destroy_display_found and not crash_detected:
        analysis.append(f"  [OK] Shutdown appears CLEAN: 'Destroy Display Manager' registered in last {limit_shutdown_scan} lines.")
    elif crash_detected:
        analysis.append("  [CRASH] Shutdown was ABNORMAL: Crash dump file (.dmp) detected.")
    elif revit_active:
        analysis.append("  [ACTIVE] Session is active (Revit.exe is currently running).")
    else:
        analysis.append("  [FORCE-KILLED] Shutdown was ABNORMAL: Process is dead, but exited without clean shutdown signature.")

    # 7. Action Sequence Context (if failure detected)
    if crash_detected or state["exceptions"] or (not destroy_display_found and not revit_active):
        analysis.append(f"\n[REPRODUCTION TRAIL] Last {limit_actions} User UI Actions:")
        if state["actions"]:
            for line_num, act in state["actions"]:
                analysis.append(f"  Line {line_num}: {act}")
        else:
            analysis.append("  No UI action logs captured in this session.")

    return "\n".join(analysis)

def watch_journal(filepath, limit_actions=10):
    """Continuously monitors the active journal, printing new telemetry logs in real-time."""
    print("="*80)
    print(f"WATCHING ACTIVE JOURNAL: {filepath}")
    print("Monitoring real-time exceptions, warnings, transactions, and memory. Press Ctrl+C to stop.")
    print("="*80)
    
    state = {
        "exceptions": [],
        "in_stack_trace": False,
        "current_exception": [],
        "warnings": [],
        "memory_logs": [],
        "actions": [],
        "active_transactions": {},
        "rolled_back_transactions": [],
        "synthetic_first_ts": None,
        "synthetic_last_ts": None
    }

    # Open and seek to end
    try:
        f = open(filepath, "r", encoding="utf-8-sig", errors="ignore")
        f.seek(0, 2)  # Go to end
    except Exception as e:
        print(f"[-] Failed to open watch stream: {e}", file=sys.stderr)
        sys.exit(1)

    line_idx = 0
    with f:
        try:
            while True:
                line = f.readline()
                if not line:
                    time.sleep(0.5)
                    continue
                
                # Analyze single line
                analyze_line_for_diagnostics(line, line_idx, state, limit_actions=limit_actions)
                line_clean = line.strip()

                # Print matching events immediately
                if "exception" in line_clean.lower() or "at " in line_clean:
                    print(f"\033[91m[EXCEPTION] Line {line_idx+1}: {line_clean}\033[0m")
                elif "DBG_WARN" in line or "error" in line_clean.lower():
                    if not any(x in line for x in ["API_SUCCESS", "GetPreferences"]):
                        print(f"\033[93m[WARNING] Line {line_idx+1}: {line_clean}\033[0m")
                elif "RAM Statistics:" in line:
                    print(f"\033[94m[MEMORY] Line {line_idx+1}: {line_clean}\033[0m")
                elif "transaction : start" in line_clean.lower() or "start transaction" in line_clean.lower():
                    print(f"\033[92m[TRANSACTION START] Line {line_idx+1}: {line_clean}\033[0m")
                elif "transaction : commit" in line_clean.lower() or "commit transaction" in line_clean.lower():
                    print(f"\033[92m[TRANSACTION COMMIT] Line {line_idx+1}: {line_clean}\033[0m")
                elif "transaction : rollback" in line_clean.lower() or "rollback transaction" in line_clean.lower():
                    print(f"\033[91m[TRANSACTION ROLLBACK] Line {line_idx+1}: {line_clean}\033[0m")

                line_idx += 1
        except KeyboardInterrupt:
            print("\nStopping watch stream. Telemetry closed.")

def main():
    default_ver = get_current_latest_version()
    
    parser = argparse.ArgumentParser(description="Revit Journal Telemetry Diagnostics Tool")
    parser.add_argument("--version", default=default_ver, help=f"Revit version target folder (default: {default_ver})")
    parser.add_argument("--tail", type=int, default=100, help="Number of lines to tail")
    parser.add_argument("--search", help="Query string to search for with context")
    parser.add_argument("--context", type=int, default=15, help="Lines of context around search matches")
    parser.add_argument("--analyze", action="store_true", help="Perform telemetry diagnostics analysis")
    parser.add_argument("--watch", action="store_true", help="Watch the active journal real-time (polling)")
    parser.add_argument("--output", help="Write results to path")
    
    # Custom limits parameters
    parser.add_argument("--limit-exceptions", type=int, default=10, help="Max exceptions to display in analysis (default: 10)")
    parser.add_argument("--limit-warnings", type=int, default=15, help="Max warnings to display in analysis (default: 15)")
    parser.add_argument("--limit-actions", type=int, default=10, help="Max action sequence history size (default: 10)")
    parser.add_argument("--limit-memory", type=int, default=3, help="Max memory logs to display at start/end (default: 3)")
    parser.add_argument("--limit-shutdown-scan", type=int, default=200, help="Trailing lines to scan for clean shutdown (default: 200)")

    args = parser.parse_args()

    latest_journal = get_latest_journal(args.version)
    if not latest_journal:
        sys.exit(1)

    if args.watch:
        watch_journal(latest_journal, limit_actions=args.limit_actions)
        sys.exit(0)

    lines, encoding = read_journal_lines(latest_journal)
    
    results = []
    results.append(f"Telemetry Target: {latest_journal}")
    results.append(f"Encoding:         {encoding}")
    results.append(f"Total Lines:      {len(lines)}")
    results.append("=" * 70)

    if args.analyze:
        analysis_text = analyze_journal(
            lines, 
            latest_journal,
            limit_exceptions=args.limit_exceptions,
            limit_warnings=args.limit_warnings,
            limit_actions=args.limit_actions,
            limit_memory=args.limit_memory,
            limit_shutdown_scan=args.limit_shutdown_scan
        )
        results.append(analysis_text)
    elif args.search:
        query_lower = args.search.lower()
        matches = []
        for idx, line in enumerate(lines):
            if query_lower in line.lower():
                matches.append(idx)
        results.append(f"Search Query: '{args.search}'")
        results.append(f"Matches:      {len(matches)} found\n")
        
        for idx in matches:
            results.append(f"--- Match at Line {idx+1} ---")
            start = max(0, idx - args.context)
            end = min(len(lines), idx + args.context + 1)
            for i in range(start, end):
                prefix = ">>>" if i == idx else "   "
                results.append(f"{prefix} {i+1:5d}: {lines[i].rstrip()}")
            results.append("-" * 70)
    else:
        results.append(f"Tailing last {args.tail} lines:\n")
        start_idx = max(0, len(lines) - args.tail)
        for idx in range(start_idx, len(lines)):
            results.append(f" {idx + 1:5d}: {lines[idx].rstrip()}")

    output_text = "\n".join(results)
    
    if args.output:
        try:
            with open(args.output, "w", encoding="utf-8") as f:
                f.write(output_text)
            print(f"[+] Diagnostic report written to: {args.output}")
        except Exception as e:
            print(f"[-] Failed to write output file: {e}", file=sys.stderr)
            print(output_text)
    else:
        print(output_text)

if __name__ == "__main__":
    main()
```

### File: .agents/skills/revit-multi-versions/SKILL.md
```markdown
---
name: revit-multi-versions
description: Enforce inverted preprocessor directive hierarchy and manage Revit version increments. Use when writing code targeting multiple Revit versions.
---

# Revit Multi-Version Playbook (`revit-multi-versions`)

This playbook governs how to write and refactor C# code to support multiple Revit versions (2022–2027+) and their respective .NET frameworks (.NET Framework 4.8, .NET 8, .NET 10+).

## 1. Inverted Preprocessor Hierarchy

To make it easy to drop support for deprecated Revit versions, write all version-conditional code using an **inverted preprocessor hierarchy**:
* **The Default Baseline:** Write the newest supported Revit version (defined by `CURRENT_LATEST_VERSION` in `docs/agents/revit-config.md`) as the unconditioned `#else` or default code block.
* **The Legacy Condition:** Quarantine all older versions inside explicit `#if` or `#elif` blocks. When a version is dropped, simply delete its isolated block.

### Example:
```csharp
#if REVIT2022 || REVIT2023
    // Legacy: ParameterType is deprecated in 2022 and removed in 2024
    ParameterType paramType = definition.ParameterType;
#elif REVIT2024
    // Transitionary: GetDataType() returning ForgeTypeId (ParameterType removed)
    ForgeTypeId dataType = definition.GetDataType();
#else
    // DEFAULT baseline: Revit 2025+ (includes 2026, 2027, etc.)
    // Clean, modern, unconditioned code branch.
    ForgeTypeId dataType = definition.GetDataType();
#endif
```

## 2. Framework Dividers (.NET Framework vs. .NET Core/Modern)

Follow the same inverted preprocessor hierarchy when handling differences between .NET Framework 4.8, .NET 8, and future versions like .NET 10:
* Keep the newest framework version (.NET 10+) as the unconditioned `#else` baseline.
* Quarantine older framework code (e.g., .NET Framework 4.8 or .NET 8) inside explicit Revit version checks (e.g., `#if REVIT2022 || REVIT2023 || REVIT2024`).

## 3. Version Upgrade Protocol

Do not automatically refactor files when a new version target is introduced. Perform checks only at startup, `/finalize`, or during the `/check-version` command.

1. **Audit:** Scan all `.csproj` files in the repository. Compare the highest version suffix against `CURRENT_LATEST_VERSION` in `docs/agents/revit-config.md`.
2. **Flag:** If a higher-version project target is discovered (e.g., `Synthetic2027.csproj` when `CURRENT_LATEST_VERSION` is `2026`):
   * Halt execution and prompt the user: *"Detected new compilation target: Synthetic2027.csproj. Would you like to update CURRENT_LATEST_VERSION to 2027?"*
3. **Refactor Gate:** If the user approves, update the configuration file. Then ask explicit permission to refactor:
   * *"Updating baseline to 2027. Would you like me to refactor our current unconditioned code paths into an explicit #elif REVIT2026 branch to clear the default block for the new 2027 API?"*
```

### File: .agents/skills/revit-tests/SKILL.md
```markdown
---
name: revit-tests
description: Run NUnit unit and integration tests across multiple versions of Revit. Use when executing test commands or running TDD loops.
---

# Revit Testing Playbook (`revit-tests`)

Use this playbook to run tests efficiently and validate multi-version compatibility.

## 1. TDD Execution Sequence

When writing code under the `/tdd` loop, execute tests in the following order to maximize speed and feedback:

1. **Step 1 (Logic Loop):** Run headless Logic tests first. Iterate until they are Green.
   ```powershell
   python .agents/skills/revit-tests/scripts/test_executor.py --version Logic
   ```
2. **Step 2 (Latest Revit Loop):** Run integration tests for the version defined by `CURRENT_LATEST_VERSION` in `docs/agents/revit-config.md` (e.g. `2026`). Iterate until they are Green.
   ```powershell
   python .agents/skills/revit-tests/scripts/test_executor.py --version 2026
   ```
3. **Step 3 (Multi-Version Validation):** Once the feature is complete, run tests across all versions to ensure multi-version compatibility.
   ```powershell
   python .agents/skills/revit-tests/scripts/test_executor.py
   ```

Refer to the `revit-multi-versions` skill to resolve compilation or API discrepancies detected during Step 3.

## 2. Test Execution Commands

Use the unified Python test executor to build the solution and run tests:

* **Run logic tests:**
  ```powershell
  python .agents/skills/revit-tests/scripts/test_executor.py --version Logic
  ```
* **Run specific Revit version tests (e.g. latest supported):**
  ```powershell
  # Replace 2026 with the value of CURRENT_LATEST_VERSION from docs/agents/revit-config.md
  python .agents/skills/revit-tests/scripts/test_executor.py --version 2026
  ```
* **Filter to specific test classes or methods:**
  ```powershell
  python .agents/skills/revit-tests/scripts/test_executor.py --version 2026 --filter "ParameterEngineTests"
  ```

## 3. Publisher Verification Bypass

The executor script automatically runs `guid_whitelister.py` to bypass Autodesk's code-signing verification prompt. It:
1. Extracts ClientId/AddInId GUIDs from the local `.addin` files and test framework.
2. Writes them to the registry under `HKCU\Software\Autodesk\Revit\Autodesk Revit <Version>\CodeSigning` with a value of `1`.
3. Cleans up these temporary registry entries when the test run exits.
```

### File: .agents/skills/revit-tests/scripts/guid_whitelister.py
```python
import os
import re

# Since registry actions are Windows specific, import winreg
try:
    import winreg
except ImportError:
    winreg = None

# GUIDs for permanently installed test-framework addins that must always be whitelisted.
# ricaun.RevitTest.Application (ApplicationPlugins bundle, all versions)
KNOWN_FRAMEWORK_GUIDS = {
    "65f304b3-8efb-464d-b08a-12cfd61a1986",  # ricaun.RevitTest.Application v1.x
}

def extract_guids_from_file(full_path):
    guids = set()
    try:
        if not os.path.exists(full_path):
            return guids
        with open(full_path, "r", encoding="utf-8") as f:
            content = f.read()
        matches = re.findall(r'<(?:ClientId|AddInId)>(.*?)</', content, re.IGNORECASE)
        for m in matches:
            guid_cand = m.strip().strip("{}").strip()
            if len(guid_cand) >= 32:
                guids.add(guid_cand)
    except Exception:
        pass
    return guids

def extract_guids_from_package_contents(xml_path):
    """Extract ProductCode GUIDs from a bundle PackageContents.xml."""
    guids = set()
    try:
        if not os.path.exists(xml_path):
            return guids
        with open(xml_path, "r", encoding="utf-8") as f:
            content = f.read()
        # ProductCode attribute on ApplicationPackage element
        matches = re.findall(r'ProductCode=["\']([^"\'\']+)["\']', content, re.IGNORECASE)
        for m in matches:
            guid_cand = m.strip().strip("{}").strip()
            if len(guid_cand) >= 32:
                guids.add(guid_cand)
        # Also grab AddInId from any embedded .addin fragments inside the XML
        matches2 = re.findall(r'<(?:ClientId|AddInId)>(.*?)</', content, re.IGNORECASE)
        for m in matches2:
            guid_cand = m.strip().strip("{}").strip()
            if len(guid_cand) >= 32:
                guids.add(guid_cand)
    except Exception:
        pass
    return guids

def get_bundle_and_addin_paths(version):
    programdata = os.environ.get("ProgramData", r"C:\ProgramData")
    appdata = os.environ.get("AppData", os.path.expanduser("~\\AppData\\Roaming"))
    
    paths = [
        os.path.join(programdata, "Autodesk", "Revit", "Addins", version),
        os.path.join(appdata, "Autodesk", "Revit", "Addins", version)
    ]
    bundle_paths = [
        os.path.join(appdata, "Autodesk", "ApplicationPlugins"),
        os.path.join(programdata, "Autodesk", "ApplicationPlugins"),
    ]
    return paths, bundle_paths

def whitelist_existing_addins(version):
    """
    Scans add-in directories and whitelists code-signing GUIDs in registry.
    Returns the list of registry value names that were added.
    """
    if winreg is None:
        print("[WARN] winreg module not available. Registry whitelisting skipped.")
        return []

    guids_to_whitelist = set(KNOWN_FRAMEWORK_GUIDS)
    paths, bundle_paths = get_bundle_and_addin_paths(version)

    # Scan standard .addin files
    for path in paths:
        if not os.path.exists(path):
            continue
        try:
            for file in os.listdir(path):
                if file.endswith(".addin"):
                    full_path = os.path.join(path, file)
                    guids = extract_guids_from_file(full_path)
                    guids_to_whitelist.update(guids)
        except Exception:
            pass

    # Scan ApplicationPlugins bundles (PackageContents.xml)
    for bundle_root in bundle_paths:
        if not os.path.exists(bundle_root):
            continue
        try:
            for entry in os.listdir(bundle_root):
                if entry.endswith(".bundle"):
                    pkg_xml = os.path.join(bundle_root, entry, "PackageContents.xml")
                    guids_to_whitelist.update(extract_guids_from_package_contents(pkg_xml))
                    # Also walk .addin files inside the bundle
                    for root, _, files in os.walk(os.path.join(bundle_root, entry)):
                        for f in files:
                            if f.endswith(".addin"):
                                guids_to_whitelist.update(
                                    extract_guids_from_file(os.path.join(root, f))
                                )
        except Exception:
            pass

    added_guids = []
    if guids_to_whitelist:
        key_path = f"Software\\Autodesk\\Revit\\Autodesk Revit {version}\\CodeSigning"
        try:
            key = winreg.CreateKey(winreg.HKEY_CURRENT_USER, key_path)
            for g in guids_to_whitelist:
                for g_variant in (g.lower(), g.upper()):
                    try:
                        val, _ = winreg.QueryValueEx(key, g_variant)
                        if val == 1:
                            continue
                    except FileNotFoundError:
                        pass
                    winreg.SetValueEx(key, g_variant, 0, winreg.REG_DWORD, 1)
                    if g_variant not in added_guids:
                        added_guids.append(g_variant)
                        print(f"[TESTS] Dynamically whitelisted new GUID {g_variant} in registry for Revit {version}.")
            winreg.CloseKey(key)
        except Exception as e:
            print(f"[WARN] Failed to write whitelist to registry for Revit {version}: {e}")
            
    return added_guids

def cleanup_whitelisted_registry_entries(version, added_guids):
    """
    Cleans up whitelisted registry entries that were added during the run.
    """
    if winreg is None or not added_guids:
        return

    key_path = f"Software\\Autodesk\\Revit\\Autodesk Revit {version}\\CodeSigning"
    try:
        key = winreg.OpenKey(winreg.HKEY_CURRENT_USER, key_path, 0, winreg.KEY_SET_VALUE)
        for g in set(added_guids):
            try:
                winreg.DeleteValue(key, g)
            except Exception:
                pass
        winreg.CloseKey(key)
        print(f"[TESTS] Cleaned up whitelisted registry entries for Revit {version}.")
    except Exception as e:
        print(f"[WARN] Failed to clean up registry whitelist for Revit {version}: {e}")
```

### File: .agents/skills/revit-tests/scripts/test_executor.py
```python
import os
import sys
import subprocess
import glob
import xml.etree.ElementTree as ET
import re
import winreg

# Import helper scripts for isolating Revit environment configurations
import guid_whitelister

class TempConfigureRevitAddins:
    def __init__(self, version):
        self.version = version
        self.added_guids = []
        
    def whitelist_existing_addins(self):
        new_guids = guid_whitelister.whitelist_existing_addins(self.version)
        self.added_guids.extend(new_guids)
        
    def __enter__(self):
        self.whitelist_existing_addins()
        return self
        
    def __exit__(self, exc_type, exc_val, exc_tb):
        if self.added_guids:
            guid_whitelister.cleanup_whitelisted_registry_entries(self.version, self.added_guids)


def find_msbuild():
    paths = [
        r"C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe",
        r"C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe",
        r"C:\Program Files\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\MSBuild.exe",
        r"C:\Program Files\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe",
    ]
    for p in paths:
        if os.path.exists(p):
            return p
    return "msbuild"  # fallback to PATH

def run_test_suite(target_class, target_version):
    workspace_path = os.getcwd()
    solution_path = os.path.join(workspace_path, "src", "Synthetic.sln")
    
    # 1. Build the entire solution first to ensure compilation and restore
    print("[TESTS] Restoring solution NuGet packages...")
    try:
        # Run restore letting output print to console
        subprocess.run(
            ["dotnet", "restore", solution_path],
            check=True
        )
    except subprocess.CalledProcessError as e:
        print(f"[ERROR] NuGet restore failed: {e}")
        sys.exit(1)

    msbuild_path = find_msbuild()
    print(f"[TESTS] Building solution with MSBuild: {msbuild_path}...")
    try:
        # Run build letting output print to console
        subprocess.run(
            [msbuild_path, solution_path, "/t:Build", "/p:Configuration=Debug", "/p:Platform=Any CPU"],
            check=True
        )
        print("[TESTS] Solution build succeeded.")
    except subprocess.CalledProcessError as e:
        print(f"[ERROR] Solution build failed: {e}")
        sys.exit(1)

    # 2. Discover test projects dynamically
    test_dir = os.path.join(workspace_path, "tests")
    test_projects = {}
    if os.path.exists(test_dir):
        for item in os.listdir(test_dir):
            item_path = os.path.join(test_dir, item)
            if os.path.isdir(item_path) and item.startswith("SyntheticTests") and not item.endswith(".Shared"):
                ver = item.replace("SyntheticTests", "")
                if ver.startswith("."):
                    ver = ver[1:]
                csproj_files = glob.glob(os.path.join(item_path, "*.csproj"))
                if csproj_files:
                    test_projects[ver] = csproj_files[0]

    # Filter by version if specified
    run_projects = {}
    if target_version:
        if target_version in test_projects:
            run_projects[target_version] = test_projects[target_version]
        else:
            print(f"[ERROR] Target version '{target_version}' test project not found in {list(test_projects.keys())}")
            sys.exit(1)
    else:
        run_projects = test_projects

    if not run_projects:
        print("[WARN] No test projects discovered to execute.")
        return

    # 3. Execute dotnet test on each target project
    results_dir = os.path.join(test_dir, "TestResults")
    # Clean old results
    if os.path.exists(results_dir):
        for f in glob.glob(os.path.join(results_dir, "results_*.trx")):
            try:
                os.remove(f)
            except Exception:
                pass
    else:
        os.makedirs(results_dir, exist_ok=True)

    import time
    for ver, csproj_path in run_projects.items():
        is_logic = (ver.lower() == "logic")
        if is_logic:
            print(f"[TESTS] Executing headless NUnit tests for logic...")
        else:
            print(f"[TESTS] Executing dotnet test for Revit {ver}...")
        log_name = f"results_{ver}.trx"
        cmd = [
            "dotnet", "test", csproj_path,
            "--no-build",
            "--logger", f"trx;LogFileName={log_name}",
            "--results-directory", results_dir,
            "/p:Platform=x64"
        ]
        if target_class:
            cmd.extend(["--filter", f"FullyQualifiedName~{target_class}"])
        
        out_file_path = os.path.join(results_dir, f"output_{ver}.log")
        out_f = open(out_file_path, "w", encoding="utf-8")
        
        if is_logic:
            # Run headless tests synchronously in the foreground
            subprocess.run(cmd, stdout=out_f, stderr=out_f)
        else:
            with TempConfigureRevitAddins(ver) as configurer:
                # Start dotnet test in the background
                proc = subprocess.Popen(cmd, stdout=out_f, stderr=out_f)
                
                # Poll for new add-ins and wait for dotnet test to exit
                start_time = time.time()
                timeout = 660  # 11 min: matches 600s ricaun.RevitTest.Timeout + 60s Revit boot buffer
                print(f"[TESTS] Waiting for Revit {ver} test execution to complete (timeout: {timeout}s)...")
                
                while time.time() - start_time < timeout:
                    configurer.whitelist_existing_addins()
                    
                    # Check if dotnet test has finished
                    if proc.poll() is not None:
                        break
                    time.sleep(0.5)
                
                # If still running after timeout, kill Revit
                if proc.poll() is None:
                    print(f"[TESTS] Test execution timed out after {timeout} seconds. Killing Revit...")
                    subprocess.run(["taskkill", "/F", "/IM", "Revit.exe"], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
                    try:
                        proc.wait(timeout=10)
                    except subprocess.TimeoutExpired:
                        proc.kill()
        
        out_f.close()

    # 4. Ingest and parse TRX files
    failing_tests = []
    total_run = 0
    total_failed = 0
    total_passed = 0

    ns = {'ns': 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
    trx_files = glob.glob(os.path.join(results_dir, "results_*.trx"))

    for trx_path in trx_files:
        try:
            tree = ET.parse(trx_path)
            root = tree.getroot()
            
            # Extract basic counts
            counters = root.find('.//ns:ResultSummary/ns:Counters', ns)
            if counters is not None:
                total_run += int(counters.attrib.get('total', 0))
                total_failed += int(counters.attrib.get('failed', 0))
                total_passed += int(counters.attrib.get('passed', 0))

            for result in root.findall('.//ns:UnitTestResult', ns):
                outcome = result.attrib.get('outcome')
                if outcome == 'Failed':
                    test_name = result.attrib.get('testName')
                    error_message = ""
                    stack_trace = ""
                    
                    error_info = result.find('.//ns:ErrorInfo', ns)
                    if error_info is not None:
                        msg_el = error_info.find('ns:Message', ns)
                        if msg_el is not None:
                            error_message = msg_el.text or ""
                        stack_el = error_info.find('ns:StackTrace', ns)
                        if stack_el is not None:
                            stack_trace = stack_el.text or ""
                    
                    # Try to extract version from trx file name
                    base_name = os.path.basename(trx_path)
                    ver_str = base_name.replace("results_", "").replace(".trx", "")
                    
                    failing_tests.append({
                        'name': f"[{ver_str}] {test_name}",
                        'message': error_message.strip(),
                        'stack_trace': stack_trace.strip()
                    })
        except Exception as e:
            print(f"[ERROR] Failed to parse TRX file '{trx_path}': {e}")

    # 5. Output Markdown formatted summary
    print("\n# Test Execution Summary\n")
    if failing_tests:
        print(f"## [FAIL] Failing Tests ({len(failing_tests)} / {total_run})\n")
        for idx, t in enumerate(failing_tests, 1):
            print(f"### {idx}. Test Name: `{t['name']}`")
            print("**Error Message:**")
            print("```")
            print(t['message'])
            print("```\n")
            print("**Stack Trace:**")
            print("```")
            print(t['stack_trace'])
            print("```\n")
            print("---")
        sys.exit(1)
    else:
        if total_run > 0:
            print(f"## All tests passed successfully! [PASS] (Total: {total_passed})")
        else:
            print("## No tests were executed. [WARN]")
        sys.exit(0)

if __name__ == "__main__":
    import argparse as _ap
    _parser = _ap.ArgumentParser(description="Run Synthetic test suite.")
    _parser.add_argument("--filter", dest="target_class", default="",
                         help="FullyQualifiedName filter (class or method name fragment)")
    _parser.add_argument("--version", dest="target_version", default="",
                         help="Revit version to target (e.g. 2026). Omit to run all.")
    # Legacy positional support: test_executor.py [class] [version]
    _parser.add_argument("pos_class", nargs="?", default="")
    _parser.add_argument("pos_version", nargs="?", default="")
    _args = _parser.parse_args()

    target_class   = (_args.target_class   or _args.pos_class   or "").strip()
    target_version = (_args.target_version or _args.pos_version or "").strip()

    # Treat legacy sentinel value as blank
    if target_class == "DefaultAlignmentTests":
        target_class = ""

    run_test_suite(target_class, target_version)
```

### File: .agents/skills/revit-transactions/SKILL.md
```markdown
---
name: revit-transactions
description: Strategy for Revit transactions and transaction groups. Use when writing code that makes changes to the Revit database or executes commands.
---

# Revit Transaction Management Playbook (`revit-transactions`)

Use these patterns to maintain memory stability, preserve performance, and provide a clean Undo stack for the user when modifying the Revit database.

## 1. The Assimilated Transaction Group Pattern

Wrap multi-phase database modifications in a single overarching `TransactionGroup`, collapse them on success, and isolate individual changes using standard `Transaction` blocks.

### Scope Elevation
Initialize the `TransactionGroup` at the highest logical tier of the call stack (e.g., the external command entry point or main view model handler) rather than inside deep sub-services or loops.

### Individual Transactions
Within the group, perform logical batches of work in separate `Transaction` blocks. This isolates errors and keeps each block focused:

```csharp
using (TransactionGroup tg = new TransactionGroup(doc, "Import Project Standards"))
{
    tg.Start();

    // Process elements in logical batches
    foreach (var batch in elementBatches)
    {
        using (Transaction t = new Transaction(doc, $"Import Batch {batch.Name}"))
        {
            t.Start();
            try
            {
                // Perform Revit database modifications here
                ProcessBatch(batch);
                t.Commit();
            }
            catch (Exception ex)
            {
                t.Rollback();
                // Capture element failures and proceed with other batches
                LogFailure(batch, ex);
            }
        }
    }

    // Collapse all successful transactions into a single entry in Revit's Undo stack
    tg.Assimilate();
}
```

## 2. Key Transaction Rules

1. **The Partial-Commit Rule:** If an intermediate transaction fails inside an execution loop, commit the valid transactions and record the specific element failures. Avoid nuclear rollbacks of the entire operation unless it is a fatal requirement of the feature.
2. **The Assimilation Rule:** Always call `group.Assimilate()` at the end of a successful transaction group to keep the user's Undo menu tidy. Use `group.RollBack()` only if the entire operation fails.
3. **Deep Seams Rule:** Keep sub-services and deep modules transactional-agnostic. Deep modules should accept a reference to an active document or context and perform operations without initiating their own internal transactions.
```

### File: .agents/skills/revit-ui-ribbon/SKILL.md
```markdown
---
name: revit-ui-ribbon
description: Use when modifying or building the Revit ribbon interface.
---

# Revit UI Ribbon Playbook (`revit-ui-ribbon`)

Use these rules to build and maintain the Revit application ribbon programmatically from a single JSON configuration source.

## 1. Unified UI Ribbon Mapping

To keep the UI configuration stable and easily editable across all Revit target versions:
* **No Preprocessor Directives:** Preprocessor compilation directives (like `#if REVIT2025`) are **strictly prohibited** inside the ribbon assembly logic.
* **Configuration Location:** Define the ribbon layout in a loose JSON file named `ribbon_config.json` inside the `Assets/` directory next to the compiled assembly.

## 2. Structured JSON Schema

The `ribbon_config.json` file must follow a structured panel-and-item layout using type discriminators for pushbuttons and stacked groupings:

```json
{
  "tab_name": "Synthetic",
  "panels": [
    {
      "name": "Views & Tags",
      "items": [
        {
          "type": "PushButton",
          "name": "AutonumberViews",
          "text": " Autonumber\nViews ",
          "class": "Synthetic.Modules.ViewManagement.Commands.ViewsAutoNumber",
          "tooltip": "Autonumber Views on the Active Sheet",
          "large_image": "autonumber_32.png",
          "image": "autonumber_16.png"
        },
        {
          "type": "StackedGroup",
          "sub_items": [
            {
              "type": "PushButton",
              "name": "ManageTemplates",
              "text": "Manage Templates",
              "class": "Synthetic.Modules.AutoTagger.Commands.CmdManageTemplates",
              "tooltip": "Open control panel to view templates.",
              "large_image": "autotag_32.png",
              "image": "autotag_16.png"
            }
          ]
        }
      ]
    }
  ]
}
```

## 3. Placeholder Recovery Pattern

To ensure the add-in boots reliably even if configuration assets or assembly class structures are out of sync:

* **Missing Icon Recovery:** If an icon image file specified in the JSON cannot be found in the `Assets/` directory, load a default fallback placeholder image (`placeholder_16.png` or `placeholder_32.png`) instead of throwing an exception.
* **Missing Class Recovery:** If a command class cannot be resolved within the assembly at runtime:
  1. Add the button to the panel.
  2. Programmatically disable the button (`button.Enabled = false`).
  3. Override the tooltip to display a prominent warning: `[WARNING] Command class not found: <ClassPath>`.
```

### File: .agents/skills/revit-wpf-mvvm/SKILL.md
```markdown
---
name: revit-wpf-mvvm
description: How to separate WPF UI binding and C# Revit API thread execution. Use when writing user interface (WPF) or view model code.
---

# Revit WPF & MVVM Playbook (`revit-wpf-mvvm`)

Use this playbook to maintain clean architectural boundaries between the WPF presentation layer (MVVM) and the Revit database thread execution.

## 1. Decoupled Data Binding (RevitDOM DRY Rule)

WPF Views must never bind directly to live Revit API database elements (`Element`, `Parameter`, `Category`, etc.) to prevent `InvalidObjectException` crashes when elements become invalid or deleted in Revit:

* **Map to POCOs:** Always map Revit database elements to presentation-safe C# DTOs or custom sub-ViewModels.
* **Reuse RevitDOM:** To remain DRY, where appropriate reuse the existing POCO/wrapper classes and translators under the `Synthetic.Modules.RevitDOM` namespace (such as `ElementModel`, `ParameterModel`, `CategoryModel`, and `XYZModel`) rather than writing redundant wrapper classes.
* **Reference by ID:** Store the element's `ElementId` or `UniqueId` string inside the ViewModel to reference it, rather than holding references to active `Element` object instances.

## 2. UI Thread Dispatching

Modifying bound ViewModel properties or collections (like `ObservableCollection<T>`) from an external thread (like a background task or Revit's main thread running inside an external event) will throw a cross-thread `NotSupportedException`.

* **WPF Dispatcher Delegation:** You must explicitly delegate all collection modifications or property updates to the WPF UI thread using the dispatcher:
  ```csharp
  System.Windows.Application.Current.Dispatcher.Invoke(() =>
  {
      MyObservableCollection.Add(newModel);
      StatusMessage = "Updated successfully.";
  });
  ```

## 3. Window Presentation Contexts

Understand the thread context of the active WPF window to handle Revit API calls correctly:

### Modal Windows (`window.ShowDialog()`)
* **Context:** Modal windows run synchronously, blocking the Revit UI thread.
* **API Access:** You can call the Revit API and execute database modifications directly within transactions because Revit is suspended waiting for the dialog to close.

### Modeless Windows (`window.Show()`)
* **Context:** Modeless windows run alongside Revit asynchronously.
* **API Access:** You are **strictly forbidden** from calling the Revit API directly from modeless events. You must queue operations and trigger them using the `IExternalEventHandler` pattern (see the `revit-external-events` playbook).
```

### File: .agents/skills/setup-matt-pocock-skills/domain.md
```markdown
# Domain Docs

How the engineering skills should consume this repo's domain documentation when exploring the codebase.

## Before exploring, read these

- **`CONTEXT.md`** at the repo root, or
- **`CONTEXT-MAP.md`** at the repo root if it exists — it points at one `CONTEXT.md` per context. Read each one relevant to the topic.
- **`docs/adr/`** — read ADRs that touch the area you're about to work in. In multi-context repos, also check `src/<context>/docs/adr/` for context-scoped decisions.

If any of these files don't exist, **proceed silently**. Don't flag their absence; don't suggest creating them upfront. The `/domain-modeling` skill (reached via `/grill-with-docs` and `/improve-codebase-architecture`) creates them lazily when terms or decisions actually get resolved.

## File structure

Single-context repo (most repos):

```
/
├── CONTEXT.md
├── docs/adr/
│   ├── 0001-event-sourced-orders.md
│   └── 0002-postgres-for-write-model.md
└── src/
```

Multi-context repo (presence of `CONTEXT-MAP.md` at the root):

```
/
├── CONTEXT-MAP.md
├── docs/adr/                          ← system-wide decisions
└── src/
    ├── ordering/
    │   ├── CONTEXT.md
    │   └── docs/adr/                  ← context-specific decisions
    └── billing/
        ├── CONTEXT.md
        └── docs/adr/
```

## Use the glossary's vocabulary

When your output names a domain concept (in an issue title, a refactor proposal, a hypothesis, a test name), use the term as defined in `CONTEXT.md`. Don't drift to synonyms the glossary explicitly avoids.

If the concept you need isn't in the glossary yet, that's a signal — either you're inventing language the project doesn't use (reconsider) or there's a real gap (note it for `/domain-modeling`).

## Flag ADR conflicts

If your output contradicts an existing ADR, surface it explicitly rather than silently overriding:

> _Contradicts ADR-0007 (event-sourced orders) — but worth reopening because…_
```

### File: .agents/skills/setup-matt-pocock-skills/issue-tracker-github.md
```markdown
# Issue tracker: GitHub

Issues and PRDs for this repo live as GitHub issues. Use the `gh` CLI for all operations.

## Conventions

- **Create an issue**: `gh issue create --title "..." --body "..."`. Use a heredoc for multi-line bodies.
- **Read an issue**: `gh issue view <number> --comments`, filtering comments by `jq` and also fetching labels.
- **List issues**: `gh issue list --state open --json number,title,body,labels,comments --jq '[.[] | {number, title, body, labels: [.labels[].name], comments: [.comments[].body]}]'` with appropriate `--label` and `--state` filters.
- **Comment on an issue**: `gh issue comment <number> --body "..."`
- **Apply / remove labels**: `gh issue edit <number> --add-label "..."` / `--remove-label "..."`
- **Close**: `gh issue close <number> --comment "..."`

Infer the repo from `git remote -v` — `gh` does this automatically when run inside a clone.

## Pull requests as a triage surface

**PRs as a request surface: no.** _(Set to `yes` if this repo treats external PRs as feature requests; `/triage` reads this flag.)_

When set to `yes`, PRs run through the same labels and states as issues, using the `gh pr` equivalents:

- **Read a PR**: `gh pr view <number> --comments` and `gh pr diff <number>` for the diff.
- **List external PRs for triage**: `gh pr list --state open --json number,title,body,labels,author,authorAssociation,comments` then keep only `authorAssociation` of `CONTRIBUTOR`, `FIRST_TIME_CONTRIBUTOR`, or `NONE` (drop `OWNER`/`MEMBER`/`COLLABORATOR`).
- **Comment / label / close**: `gh pr comment`, `gh pr edit --add-label`/`--remove-label`, `gh pr close`.

GitHub shares one number space across issues and PRs, so a bare `#42` may be either — resolve with `gh pr view 42` and fall back to `gh issue view 42`.

## When a skill says "publish to the issue tracker"

Create a GitHub issue.

## When a skill says "fetch the relevant ticket"

Run `gh issue view <number> --comments`.

## Wayfinding operations

Used by `/wayfinder`. The **map** is a single issue with **child** issues as tickets.

- **Map**: a single issue labelled `wayfinder:map`, holding the Notes / Decisions-so-far / Fog body. `gh issue create --label wayfinder:map`.
- **Child ticket**: an issue linked to the map as a GitHub sub-issue (`gh api` on the sub-issues endpoint). Where sub-issues aren't enabled, add the child to a task list in the map body and put `Part of #<map>` at the top of the child body. Labels: `wayfinder:<type>` (`research`/`prototype`/`grilling`/`task`). Once claimed, the ticket is assigned to the driving dev.
- **Blocking**: GitHub's **native issue dependencies** — the canonical, UI-visible representation. Add an edge with `gh api --method POST repos/<owner>/<repo>/issues/<child>/dependencies/blocked_by -F issue_id=<blocker-db-id>`, where `<blocker-db-id>` is the blocker's numeric **database id** (`gh api repos/<owner>/<repo>/issues/<n> --jq .id`, _not_ the `#number` or `node_id`). GitHub reports `issue_dependencies_summary.blocked_by` (open blockers only — the live gate). Where dependencies aren't available, fall back to a `Blocked by: #<n>, #<n>` line at the top of the child body. A ticket is unblocked when every blocker is closed.
- **Frontier query**: list the map's open children (`gh issue list --state open`, scoped to the map's sub-issues / task list), drop any with an open blocker (`issue_dependencies_summary.blocked_by > 0`, or an open issue in the `Blocked by` line) or an assignee; first in map order wins.
- **Claim**: `gh issue edit <n> --add-assignee @me` — the session's first write.
- **Resolve**: `gh issue comment <n> --body "<answer>"`, then `gh issue close <n>`, then append a context pointer (gist + link) to the map's Decisions-so-far.
```

### File: .agents/skills/setup-matt-pocock-skills/issue-tracker-gitlab.md
```markdown
# Issue tracker: GitLab

Issues and PRDs for this repo live as GitLab issues. Use the [`glab`](https://gitlab.com/gitlab-org/cli) CLI for all operations.

## Conventions

- **Create an issue**: `glab issue create --title "..." --description "..."`. Use a heredoc for multi-line descriptions. Pass `--description -` to open an editor.
- **Read an issue**: `glab issue view <number> --comments`. Use `-F json` for machine-readable output.
- **List issues**: `glab issue list -F json` with appropriate `--label` filters.
- **Comment on an issue**: `glab issue note <number> --message "..."`. GitLab calls comments "notes".
- **Apply / remove labels**: `glab issue update <number> --label "..."` / `--unlabel "..."`. Multiple labels can be comma-separated or by repeating the flag.
- **Close**: `glab issue close <number>`. `glab issue close` does not accept a closing comment, so post the explanation first with `glab issue note <number> --message "..."`, then close.
- **Merge requests**: GitLab calls PRs "merge requests". Use `glab mr create`, `glab mr view`, `glab mr note`, etc. — the same shape as `gh pr ...` with `mr` in place of `pr` and `note`/`--message` in place of `comment`/`--body`.

Infer the repo from `git remote -v` — `glab` does this automatically when run inside a clone.

## Merge requests as a triage surface

**MRs as a request surface: no.** _(Set to `yes` if this repo treats external merge requests as feature requests; `/triage` reads this flag.)_

When set to `yes`, MRs run through the same labels and states as issues, using the `glab mr` equivalents:

- **Read an MR**: `glab mr view <number> --comments` and `glab mr diff <number>` for the diff.
- **List external MRs for triage**: `glab mr list -F json`, then keep only MRs whose author is not a project member/owner (a contributor's MR, not a maintainer's in-flight work).
- **Comment / label / close**: `glab mr note`, `glab mr update --label`/`--unlabel`, `glab mr close`.

Unlike GitHub, GitLab numbers issues and MRs separately, so `#42` is unambiguous once you know which surface the maintainer means.

## When a skill says "publish to the issue tracker"

Create a GitLab issue.

## When a skill says "fetch the relevant ticket"

Run `glab issue view <number> --comments`.

## Wayfinding operations

Used by `/wayfinder`. The **map** is a single issue with **child** issues as tickets.

- **Map**: a single issue labelled `wayfinder:map`, holding the Notes / Decisions-so-far / Fog body. `glab issue create --label wayfinder:map`. (On GitLab tiers with native epics, an epic may hold the map instead; a labelled issue works everywhere.)
- **Child ticket**: an issue carrying `Part of #<map>` at the top of its description and labels `wayfinder:<type>` (`research`/`prototype`/`grilling`/`task`). Once claimed, the ticket is assigned to the driving dev.
- **Blocking**: GitLab's **native blocking link** — the canonical, UI-visible representation. Add it with the `/blocked_by #<n>` quick action, posted as a note (`glab issue note <child> --message "/blocked_by #<blocker>"`). Native blocking links are a Premium/Ultimate feature; on the free tier (or where unavailable) fall back to a `Blocked by: #<n>, #<n>` line at the top of the description. A ticket is unblocked when every blocker is closed.
- **Frontier query**: `glab issue list -F json` scoped to the map's children, drop any with an open blocker — a native `blocked_by` link to an open issue (`glab api projects/:id/issues/:iid/links`), or an open issue in the `Blocked by` line — or an assignee; first in map order wins.
- **Claim**: `glab issue update <n> --assignee @me` — the session's first write.
- **Resolve**: `glab issue note <n> --message "<answer>"`, then `glab issue close <n>`, then append a context pointer (gist + link) to the map's Decisions-so-far.
```

### File: .agents/skills/setup-matt-pocock-skills/issue-tracker-local.md
```markdown
# Issue tracker: Local Markdown

Issues and specs (you may know a spec as a PRD) for this repo live as markdown files in `.scratch/`.

## Conventions

- One feature per directory: `.scratch/<feature-slug>/`
- The spec is `.scratch/<feature-slug>/spec.md`
- Implementation issues are one file per ticket at `.scratch/<feature-slug>/issues/<NN>-<slug>.md`, numbered from `01` — never a single combined tickets file
- Triage state is recorded as a `Status:` line near the top of each issue file (see `triage-labels.md` for the role strings)
- Comments and conversation history append to the bottom of the file under a `## Comments` heading

## When a skill says "publish to the issue tracker"

Create a new file under `.scratch/<feature-slug>/` (creating the directory if needed).

## When a skill says "fetch the relevant ticket"

Read the file at the referenced path. The user will normally pass the path or the issue number directly.

## Wayfinding operations

Used by `/wayfinder`. The **map** is a file with one **child** file per ticket.

- **Map**: `.scratch/<effort>/map.md` — the Notes / Decisions-so-far / Fog body.
- **Child ticket**: `.scratch/<effort>/issues/NN-<slug>.md`, numbered from `01`, with the question in the body. A `Type:` line records the ticket type (`research`/`prototype`/`grilling`/`task`); a `Status:` line records `claimed`/`resolved`.
- **Blocking**: a `Blocked by: NN, NN` line near the top. A ticket is unblocked when every file it lists is `resolved`.
- **Frontier**: scan `.scratch/<effort>/issues/` for files that are open, unblocked, and unclaimed; first by number wins.
- **Claim**: set `Status: claimed` and save before any work.
- **Resolve**: append the answer under an `## Answer` heading, set `Status: resolved`, then append a context pointer (gist + link) to the map's Decisions-so-far in `map.md`.
```

### File: .agents/skills/setup-matt-pocock-skills/SKILL.md
```markdown
---
name: setup-matt-pocock-skills
description: Configure this repo for the engineering skills — set up its issue tracker, triage label vocabulary, and domain doc layout. Run once before first use of the other engineering skills.
disable-model-invocation: true
---

# Setup Matt Pocock's Skills

Scaffold the per-repo configuration that the engineering skills assume:

- **Issue tracker** — where issues live (GitHub by default; local markdown is also supported out of the box)
- **Triage labels** — the strings used for the five canonical triage roles
- **Domain docs** — where `CONTEXT.md` and ADRs live, and the consumer rules for reading them

This is a prompt-driven skill, not a deterministic script. Explore, present what you found, confirm with the user, then write.

## Process

### 1. Explore

Look at the current repo to understand its starting state. Read whatever exists; don't assume:

- `git remote -v` and `.git/config` — is this a GitHub repo? Which one?
- `AGENTS.md` and `CLAUDE.md` at the repo root — does either exist? Is there already an `## Agent skills` section in either?
- `CONTEXT.md` and `CONTEXT-MAP.md` at the repo root
- `docs/adr/` and any `src/*/docs/adr/` directories
- `docs/agents/` — does this skill's prior output already exist?
- `.scratch/` — sign that a local-markdown issue tracker convention is already in use
- Is the `triage` skill installed? (a `triage` skill folder alongside this one, or `triage` in your available skills.) This decides whether Section B runs at all.
- Monorepo signals — a `pnpm-workspace.yaml`, a `workspaces` field in `package.json`, or a populated `packages/*` with its own `src/`. Present only in a genuinely large multi-package repo; their absence means single-context, which is almost every repo.

### 2. Present findings and ask

Summarise what's present and what's missing. Then take the sections in order — one section, one answer, then the next.

Lead each section with the recommended answer so the user can accept it in a word. Give a one-line explainer only when the choice genuinely branches; skip the section entirely when exploration already settled it (Section B when `triage` isn't installed, Section C when there's no monorepo).

**Section A — Issue tracker.**

> Explainer: The "issue tracker" is where issues live for this repo. Skills like `to-tickets`, `triage`, `to-spec`, and `qa` read from and write to it — they need to know whether to call `gh issue create`, write a markdown file under `.scratch/`, or follow some other workflow you describe. Pick the place you actually track work for this repo.

Default posture: these skills were designed for GitHub. If a `git remote` points at GitHub, propose that. If a `git remote` points at GitLab (`gitlab.com` or a self-hosted host), propose GitLab. Otherwise (or if the user prefers), offer:

- **GitHub** — issues live in the repo's GitHub Issues (uses the `gh` CLI)
- **GitLab** — issues live in the repo's GitLab Issues (uses the [`glab`](https://gitlab.com/gitlab-org/cli) CLI)
- **Local markdown** — issues live as files under `.scratch/<feature>/` in this repo (good for solo projects or repos without a remote)
- **Other** (Jira, Linear, etc.) — ask the user to describe the workflow in one paragraph; the skill will record it as freeform prose

Record the choice in `docs/agents/issue-tracker.md`. The GitHub and GitLab templates carry a "PRs as a request surface" flag, defaulted **off** — leave it off and don't raise it; a user who wants external PRs in the triage queue can flip the flag in the file later.

**Section B — Triage label vocabulary.** Skip this section entirely if the `triage` skill isn't installed (exploration told you) — an uninstalled skill needs no labels.

If it is installed, ask exactly one question:

> Do you want to keep the default triage labels? (recommended: **yes**)

The defaults are the five canonical roles, each label string equal to its name: `needs-triage`, `needs-info`, `ready-for-agent`, `ready-for-human`, `wontfix`. On **yes**, write them as-is. Only if the user says no — usually because their tracker already uses other names (e.g. `bug:triage` for `needs-triage`) — collect the overrides so `triage` applies existing labels instead of creating duplicates.

**Section C — Domain docs.** Default to **single-context** — one `CONTEXT.md` + `docs/adr/` at the repo root. This fits almost every repo; write it without asking.

Offer **multi-context** — a root `CONTEXT-MAP.md` pointing to per-context `CONTEXT.md` files — only when exploration found monorepo signals. Then confirm which layout they want.

### 3. Confirm and edit

Show the user a draft of:

- The `## Agent skills` block to add to whichever of `CLAUDE.md` / `AGENTS.md` is being edited (see step 4 for selection rules)
- The contents of `docs/agents/issue-tracker.md`, `docs/agents/domain.md`, and `docs/agents/triage-labels.md` (the last only when `triage` is installed)

Let them edit before writing.

### 4. Write

**Pick the file to edit:**

- If `CLAUDE.md` exists, edit it.
- Else if `AGENTS.md` exists, edit it.
- If neither exists, ask the user which one to create — don't pick for them.

Never create `AGENTS.md` when `CLAUDE.md` already exists (or vice versa) — always edit the one that's already there.

If an `## Agent skills` block already exists in the chosen file, update its contents in-place rather than appending a duplicate. Don't overwrite user edits to the surrounding sections.

The block:

```markdown
## Agent skills

### Issue tracker

[one-line summary of where issues are tracked]. See `docs/agents/issue-tracker.md`.

### Triage labels

[one-line summary of the label vocabulary]. See `docs/agents/triage-labels.md`.

### Domain docs

[one-line summary of layout — "single-context" or "multi-context"]. See `docs/agents/domain.md`.
```

Include the `### Triage labels` sub-block, and write `docs/agents/triage-labels.md`, only when `triage` is installed and Section B ran. When it isn't, both are omitted.

Then write the docs files using the seed templates in this skill folder as a starting point:

- [issue-tracker-github.md](./issue-tracker-github.md) — GitHub issue tracker
- [issue-tracker-gitlab.md](./issue-tracker-gitlab.md) — GitLab issue tracker
- [issue-tracker-local.md](./issue-tracker-local.md) — local-markdown issue tracker
- [triage-labels.md](./triage-labels.md) — label mapping (only if `triage` is installed)
- [domain.md](./domain.md) — domain doc consumer rules + layout

For "other" issue trackers, write `docs/agents/issue-tracker.md` from scratch using the user's description.

### 5. Done

Tell the user the setup is complete and which engineering skills will now read from these files. Mention they can edit `docs/agents/*.md` directly later — re-running this skill is only necessary if they want to switch issue trackers or restart from scratch.
```

### File: .agents/skills/setup-matt-pocock-skills/triage-labels.md
```markdown
# Triage Labels

The skills speak in terms of five canonical triage roles. This file maps those roles to the actual label strings used in this repo's issue tracker.

| Label in mattpocock/skills | Label in our tracker | Meaning                                  |
| -------------------------- | -------------------- | ---------------------------------------- |
| `needs-triage`             | `needs-triage`       | Maintainer needs to evaluate this issue  |
| `needs-info`               | `needs-info`         | Waiting on reporter for more information |
| `ready-for-agent`          | `ready-for-agent`    | Fully specified, ready for an AFK agent  |
| `ready-for-human`          | `ready-for-human`    | Requires human implementation            |
| `wontfix`                  | `wontfix`            | Will not be actioned                     |

When a skill mentions a role (e.g. "apply the AFK-ready triage label"), use the corresponding label string from this table.

Edit the right-hand column to match whatever vocabulary you actually use.
```

### File: .agents/skills/tdd/mocking.md
```markdown
# When to Mock

Mock at **system boundaries** only:

- External APIs (payment, email, etc.)
- Databases (sometimes - prefer test DB)
- Time/randomness
- File system (sometimes)

Don't mock:

- Your own classes/modules
- Internal collaborators
- Anything you control

## Designing for Mockability

At system boundaries, design interfaces that are easy to mock:

**1. Use dependency injection**

Pass external dependencies in rather than creating them internally:

```typescript
// Easy to mock
function processPayment(order, paymentClient) {
  return paymentClient.charge(order.total);
}

// Hard to mock
function processPayment(order) {
  const client = new StripeClient(process.env.STRIPE_KEY);
  return client.charge(order.total);
}
```

**2. Prefer SDK-style interfaces over generic fetchers**

Create specific functions for each external operation instead of one generic function with conditional logic:

```typescript
// GOOD: Each function is independently mockable
const api = {
  getUser: (id) => fetch(`/users/${id}`),
  getOrders: (userId) => fetch(`/users/${userId}/orders`),
  createOrder: (data) => fetch('/orders', { method: 'POST', body: data }),
};

// BAD: Mocking requires conditional logic inside the mock
const api = {
  fetch: (endpoint, options) => fetch(endpoint, options),
};
```

The SDK approach means:
- Each mock returns one specific shape
- No conditional logic in test setup
- Easier to see which endpoints a test exercises
- Type safety per endpoint
```

### File: .agents/skills/tdd/SKILL.md
```markdown
---
name: tdd
description: Test-driven development. Use when the user wants to build features or fix bugs test-first, mentions "red-green-refactor", or wants integration tests.
---

# Test-Driven Development

TDD is the red → green loop. This skill is the reference that makes that loop produce tests worth keeping: what a good test is, where tests go, the anti-patterns, and the rules of the loop. Every section applies on every cycle — consult them before and during the loop, not after.

When exploring the codebase, read `CONTEXT.md` (if it exists) so test names and interface vocabulary match the project's domain language, and respect ADRs in the area you're touching.

## What a good test is

Tests verify behavior through public interfaces, not implementation details. Code can change entirely; tests shouldn't. A good test reads like a specification — "user can checkout with valid cart" tells you exactly what capability exists — and survives refactors because it doesn't care about internal structure.

See [tests.md](tests.md) for examples and [mocking.md](mocking.md) for mocking guidelines.

## Seams — where tests go

A **seam** is the public boundary you test at: the interface where you observe behavior without reaching inside. Tests live at seams, never against internals.

**Test only at pre-agreed seams.** Before writing any test, write down the seams under test and confirm them with the user. No test is written at an unconfirmed seam. You can't test everything — agreeing the seams up front is how testing effort lands on the critical paths and complex logic instead of every edge case.

Ask: "What's the public interface, and which seams should we test?"

## Anti-patterns

- **Implementation-coupled** — mocks internal collaborators, tests private methods, or verifies through a side channel (querying the database instead of using the interface). The tell: the test breaks when you refactor but behavior hasn't changed.
- **Tautological** — the assertion recomputes the expected value the way the code does (`expect(add(a, b)).toBe(a + b)`, a snapshot derived by hand the same way, a constant asserted equal to itself), so it passes by construction and can never disagree with the code. Expected values must come from an independent source of truth — a known-good literal, a worked example, the spec.
- **Horizontal slicing** — writing all tests first, then all implementation. Bulk tests verify _imagined_ behavior: you test the _shape_ of things rather than user-facing behavior, the tests go insensitive to real changes, and you commit to test structure before understanding the implementation. Work in **vertical slices** instead — one test → one implementation → repeat, each test a **tracer bullet** that responds to what the last cycle taught you.

## Rules of the loop

- **Red before green.** Write the failing test first, then only enough code to pass it. Don't anticipate future tests or add speculative features.
- **One slice at a time.** One seam, one test, one minimal implementation per cycle.
- **Refactoring is not part of the loop.** It belongs to the review stage (see the `code-review` skill), not the red → green implementation cycle.
```

### File: .agents/skills/tdd/tests.md
```markdown
# Good and Bad Tests

## Good Tests

**Integration-style**: Test through real interfaces, not mocks of internal parts.

```typescript
// GOOD: Tests observable behavior
test("user can checkout with valid cart", async () => {
  const cart = createCart();
  cart.add(product);
  const result = await checkout(cart, paymentMethod);
  expect(result.status).toBe("confirmed");
});
```

Characteristics:

- Tests behavior users/callers care about
- Uses public API only
- Survives internal refactors
- Describes WHAT, not HOW
- One logical assertion per test

## Bad Tests

**Implementation-detail tests**: Coupled to internal structure.

```typescript
// BAD: Tests implementation details
test("checkout calls paymentService.process", async () => {
  const mockPayment = jest.mock(paymentService);
  await checkout(cart, payment);
  expect(mockPayment.process).toHaveBeenCalledWith(cart.total);
});
```

Red flags:

- Mocking internal collaborators
- Testing private methods
- Asserting on call counts/order
- Test breaks when refactoring without behavior change
- Test name describes HOW not WHAT
- Verifying through external means instead of interface

```typescript
// BAD: Bypasses interface to verify
test("createUser saves to database", async () => {
  await createUser({ name: "Alice" });
  const row = await db.query("SELECT * FROM users WHERE name = ?", ["Alice"]);
  expect(row).toBeDefined();
});

// GOOD: Verifies through interface
test("createUser makes user retrievable", async () => {
  const user = await createUser({ name: "Alice" });
  const retrieved = await getUser(user.id);
  expect(retrieved.name).toBe("Alice");
});
```

**Tautological tests**: Expected value restates the implementation, so the test passes by construction.

```typescript
// BAD: Expected value is recomputed the way the code computes it
test("calculateTotal sums line items", () => {
  const items = [{ price: 10 }, { price: 5 }];
  const expected = items.reduce((sum, i) => sum + i.price, 0);
  expect(calculateTotal(items)).toBe(expected);
});

// GOOD: Expected value is an independent, known literal
test("calculateTotal sums line items", () => {
  expect(calculateTotal([{ price: 10 }, { price: 5 }])).toBe(15);
});
```
```

### File: .agents/skills/to-spec/SKILL.md
```markdown
---
name: to-spec
description: Turn the current conversation into a spec and publish it to the project issue tracker — no interview, just synthesis of what you've already discussed.
disable-model-invocation: true
---

This skill takes the current conversation context and codebase understanding and produces a spec (you may know this document as a PRD). Do NOT interview the user — just synthesize what you already know.

The issue tracker and triage label vocabulary should have been provided to you — run `/setup-matt-pocock-skills` if not.

## Process

1. Explore the repo to understand the current state of the codebase, if you haven't already. Use the project's domain glossary vocabulary throughout the spec, and respect any ADRs in the area you're touching.

2. Sketch out the seams at which you're going to test the feature. Existing seams should be preferred to new ones. Use the highest seam possible. If new seams are needed, propose them at the highest point you can. The fewer seams across the codebase, the better - the ideal number is one.

Check with the user that these seams match their expectations.

3. Write the spec using the template below, then publish it to the project issue tracker. Apply the `ready-for-agent` triage label - no need for additional triage.

<spec-template>

## Problem Statement

The problem that the user is facing, from the user's perspective.

## Solution

The solution to the problem, from the user's perspective.

## User Stories

A LONG, numbered list of user stories. Each user story should be in the format of:

1. As an <actor>, I want a <feature>, so that <benefit>

<user-story-example>
1. As a mobile bank customer, I want to see balance on my accounts, so that I can make better informed decisions about my spending
</user-story-example>

This list of user stories should be extremely extensive and cover all aspects of the feature.

## Implementation Decisions

A list of implementation decisions that were made. This can include:

- The modules that will be built/modified
- The interfaces of those modules that will be modified
- Technical clarifications from the developer
- Architectural decisions
- Schema changes
- API contracts
- Specific interactions

Do NOT include specific file paths or code snippets. They may end up being outdated very quickly.

Exception: if a prototype produced a snippet that encodes a decision more precisely than prose can (state machine, reducer, schema, type shape), inline it within the relevant decision and note briefly that it came from a prototype. Trim to the decision-rich parts — not a working demo, just the important bits.

## Testing Decisions

A list of testing decisions that were made. Include:

- A description of what makes a good test (only test external behavior, not implementation details)
- Which modules will be tested
- Prior art for the tests (i.e. similar types of tests in the codebase)

## Out of Scope

A description of the things that are out of scope for this spec.

## Further Notes

Any further notes about the feature.

</spec-template>
```

### File: .agents/skills/to-tickets/SKILL.md
```markdown
---
name: to-tickets
description: Break a plan, spec, or the current conversation into a set of tracer-bullet tickets, each declaring its blocking edges, published to the configured tracker — edges as text in one file per ticket locally, or native blocking links on a real tracker.
disable-model-invocation: true
---

# To Tickets

Break a plan, spec, or conversation into a set of **tickets** — tracer-bullet vertical slices, each declaring the tickets that **block** it.

The issue tracker and triage label vocabulary should have been provided to you — run `/setup-matt-pocock-skills` if not.

## Process

### 1. Gather context

Work from whatever is already in the conversation context. If the user passes a reference (a spec path, an issue number or URL) as an argument, fetch it and read its full body and comments.

### 2. Explore the codebase (optional)

If you have not already explored the codebase, do so to understand the current state of the code. Ticket titles and descriptions should use the project's domain glossary vocabulary, and respect ADRs in the area you're touching.

Look for opportunities to prefactor the code to make the implementation easier. "Make the change easy, then make the easy change."

### 3. Draft vertical slices

Break the work into **tracer bullet** tickets.

<vertical-slice-rules>

- Each slice cuts a narrow but COMPLETE path through every layer (schema, API, UI, tests) — vertical, NOT a horizontal slice of one layer
- A completed slice is demoable or verifiable on its own
- Each slice is sized to fit in a single fresh context window
- Any prefactoring should be done first

</vertical-slice-rules>

Give each ticket its **blocking edges** — the other tickets that must complete before it can start. A ticket with no blockers can start immediately.

**Wide refactors are the exception to vertical slicing.** A **wide refactor** is one mechanical change — rename a column, retype a shared symbol — whose **blast radius** fans across the whole codebase, so a single edit breaks thousands of call sites at once and no vertical slice can land green. Don't force it into a tracer bullet; sequence it as **expand–contract**. First expand: add the new form beside the old so nothing breaks. Then migrate the call sites over in batches sized by blast radius (per package, per directory), each batch its own ticket blocked by the expand, keeping CI green batch to batch because the old form still exists. Finally contract: delete the old form once no caller remains, in a ticket blocked by every migrate batch. When even the batches can't stay green alone, keep the sequence but let them share an integration branch that all block a final integrate-and-verify ticket — green is promised only there.

### 4. Quiz the user

Present the proposed breakdown as a numbered list. For each ticket, show:

- **Title**: short descriptive name
- **Blocked by**: which other tickets (if any) must complete first
- **What it delivers**: the end-to-end behaviour this ticket makes work

Ask the user:

- Does the granularity feel right? (too coarse / too fine)
- Are the blocking edges correct — does each ticket only depend on tickets that genuinely gate it?
- Should any tickets be merged or split further?

Iterate until the user approves the breakdown.

### 5. Publish the tickets to the configured tracker

Publish the approved tickets. **How** depends on the tracker `/setup-matt-pocock-skills` configured — the tickets are the same either way, only the shape of the blocking edges changes:

- **Local files** → write one file per ticket under `.scratch/<feature-slug>/issues/<NN>-<slug>.md`, numbered from `01` in dependency order (blockers first). Each file's "Blocked by" lists the numbers/titles it depends on. Use the per-ticket file template below — one ticket per file, never a single combined file.
- **A real issue tracker (GitHub, Linear, …)** → publish one issue per ticket in dependency order (blockers first) so each ticket's blocking edges can reference real identifiers. Use the platform's native blocking / sub-issue relationship where it has one; otherwise set each ticket's "Blocked by" to the blocking issues. Apply the `ready-for-agent` triage label unless instructed otherwise — the tickets are agent-grabbable by construction.

Work the **frontier**: any ticket whose blockers are all done. For a purely linear chain that means top to bottom.

Do NOT close or modify any parent issue.

<local-ticket-template>

# <NN> — <Ticket title>

**What to build:** the end-to-end behaviour this ticket makes work, from the user's perspective — not a layer-by-layer implementation list.

**Blocked by:** the numbers/titles of the tickets that gate this one, or "None — can start immediately".

**Status:** ready-for-agent

- [ ] Acceptance criterion 1
- [ ] Acceptance criterion 2

</local-ticket-template>

<issue-template>

## Parent

A reference to the parent issue on the tracker (if the source was an existing issue, otherwise omit this section).

## What to build

The end-to-end behaviour this ticket makes work, from the user's perspective — not layer-by-layer implementation.

## Acceptance criteria

- [ ] Criterion 1
- [ ] Criterion 2

## Blocked by

- A reference to each blocking ticket, or "None — can start immediately".

</issue-template>

In either form, avoid specific file paths or code snippets — they go stale fast. Exception: if a prototype produced a snippet that encodes a decision more precisely than prose can (state machine, reducer, schema, type shape), inline it and note briefly that it came from a prototype. Trim to the decision-rich parts — not a working demo, just the important bits.

Work the frontier one ticket at a time with `/implement`, clearing context between tickets.
```

### File: .agents/skills/triage/AGENT-BRIEF.md
```markdown
# Writing Agent Briefs

An agent brief is a structured comment posted on a GitHub issue or PR when it moves to `ready-for-agent`. It is the authoritative specification that an AFK agent will work from. The original body and discussion are context — the agent brief is the contract.

The brief states **what the agent should do**, which stretches to both surfaces: for an issue, that's building the change from nothing; for a PR, it's what's left to do *to the existing diff* — finish it, close gaps, address review points. Same principles either way; the PR example below shows the difference.

## Principles

### Durability over precision

The issue may sit in `ready-for-agent` for days or weeks. The codebase will change in the meantime. Write the brief so it stays useful even as files are renamed, moved, or refactored.

- **Do** describe interfaces, types, and behavioral contracts
- **Do** name specific types, function signatures, or config shapes that the agent should look for or modify
- **Don't** reference file paths — they go stale
- **Don't** reference line numbers
- **Don't** assume the current implementation structure will remain the same

### Behavioral, not procedural

Describe **what** the system should do, not **how** to implement it. The agent will explore the codebase fresh and make its own implementation decisions.

- **Good:** "The `SkillConfig` type should accept an optional `schedule` field of type `CronExpression`"
- **Bad:** "Open src/types/skill.ts and add a schedule field on line 42"
- **Good:** "When a user runs `/triage` with no arguments, they should see a summary of issues needing attention"
- **Bad:** "Add a switch statement in the main handler function"

### Complete acceptance criteria

The agent needs to know when it's done. Every agent brief must have concrete, testable acceptance criteria. Each criterion should be independently verifiable.

- **Good:** "Running `gh issue list --label needs-triage` returns issues that have been through initial classification"
- **Bad:** "Triage should work correctly"

### Explicit scope boundaries

State what is out of scope. This prevents the agent from gold-plating or making assumptions about adjacent features.

## Template

```markdown
## Agent Brief

**Category:** bug / enhancement
**Summary:** one-line description of what needs to happen

**Current behavior:**
Describe what happens now. For bugs, this is the broken behavior.
For enhancements, this is the status quo the feature builds on.

**Desired behavior:**
Describe what should happen after the agent's work is complete.
Be specific about edge cases and error conditions.

**Key interfaces:**
- `TypeName` — what needs to change and why
- `functionName()` return type — what it currently returns vs what it should return
- Config shape — any new configuration options needed

**Acceptance criteria:**
- [ ] Specific, testable criterion 1
- [ ] Specific, testable criterion 2
- [ ] Specific, testable criterion 3

**Out of scope:**
- Thing that should NOT be changed or addressed in this issue
- Adjacent feature that might seem related but is separate
```

## Examples

### Good agent brief (bug)

```markdown
## Agent Brief

**Category:** bug
**Summary:** Skill description truncation drops mid-word, producing broken output

**Current behavior:**
When a skill description exceeds 1024 characters, it is truncated at exactly
1024 characters regardless of word boundaries. This produces descriptions
that end mid-word (e.g. "Use when the user wants to confi").

**Desired behavior:**
Truncation should break at the last word boundary before 1024 characters
and append "..." to indicate truncation.

**Key interfaces:**
- The `SkillMetadata` type's `description` field — no type change needed,
  but the validation/processing logic that populates it needs to respect
  word boundaries
- Any function that reads SKILL.md frontmatter and extracts the description

**Acceptance criteria:**
- [ ] Descriptions under 1024 chars are unchanged
- [ ] Descriptions over 1024 chars are truncated at the last word boundary
      before 1024 chars
- [ ] Truncated descriptions end with "..."
- [ ] The total length including "..." does not exceed 1024 chars

**Out of scope:**
- Changing the 1024 char limit itself
- Multi-line description support
```

### Good agent brief (enhancement)

```markdown
## Agent Brief

**Category:** enhancement
**Summary:** Add `.out-of-scope/` directory support for tracking rejected feature requests

**Current behavior:**
When a feature request is rejected, the issue is closed with a `wontfix` label
and a comment. There is no persistent record of the decision or reasoning.
Future similar requests require the maintainer to recall or search for the
prior discussion.

**Desired behavior:**
Rejected feature requests should be documented in `.out-of-scope/<concept>.md`
files that capture the decision, reasoning, and links to all issues that
requested the feature. When triaging new issues, these files should be
checked for matches.

**Key interfaces:**
- Markdown file format in `.out-of-scope/` — each file should have a
  `# Concept Name` heading, a `**Decision:**` line, a `**Reason:**` line,
  and a `**Prior requests:**` list with issue links
- The triage workflow should read all `.out-of-scope/*.md` files early
  and match incoming issues against them by concept similarity

**Acceptance criteria:**
- [ ] Closing a feature as wontfix creates/updates a file in `.out-of-scope/`
- [ ] The file includes the decision, reasoning, and link to the closed issue
- [ ] If a matching `.out-of-scope/` file already exists, the new issue is
      appended to its "Prior requests" list rather than creating a duplicate
- [ ] During triage, existing `.out-of-scope/` files are checked and surfaced
      when a new issue matches a prior rejection

**Out of scope:**
- Automated matching (human confirms the match)
- Reopening previously rejected features
- Bug reports (only enhancement rejections go to `.out-of-scope/`)
```

### Good agent brief (PR)

For a PR, "Current behavior" describes the state of the diff, and the brief asks the agent to finish or fix it rather than build from scratch.

```markdown
## Agent Brief

**Category:** enhancement
**Summary:** Finish the contributor's `--json` output flag for `triage list`

**Current behavior:**
The PR adds a `--json` flag that serializes the issue list to JSON. The happy
path works and the diff matches the project's command structure. Two gaps
remain: errors are still printed as human text (not JSON), and the new flag has
no test coverage.

**Desired behavior:**
With `--json`, all output — including errors — is well-formed JSON on stdout,
and the command's exit codes are unchanged. The existing human-readable output
is untouched when the flag is absent.

**Key interfaces:**
- The command's error path should emit `{ "error": string }` under `--json`
  instead of the plain-text error
- Reuse the existing serializer the PR already added; don't introduce a second

**Acceptance criteria:**
- [ ] `triage list --json` emits valid JSON for both success and error cases
- [ ] Exit codes match the non-JSON command
- [ ] A test covers the `--json` success output and one error case
- [ ] Default (non-JSON) output is byte-for-byte unchanged

**Out of scope:**
- Adding `--json` to any other command
- Changing the JSON shape of the success payload the PR already defined
```

### Bad agent brief

```markdown
## Agent Brief

**Summary:** Fix the triage bug

**What to do:**
The triage thing is broken. Look at the main file and fix it.
The function around line 150 has the issue.

**Files to change:**
- src/triage/handler.ts (line 150)
- src/types.ts (line 42)
```

This is bad because:
- No category
- Vague description ("the triage thing is broken")
- References file paths and line numbers that will go stale
- No acceptance criteria
- No scope boundaries
- No description of current vs desired behavior
```

### File: .agents/skills/triage/OUT-OF-SCOPE.md
```markdown
# Out-of-Scope Knowledge Base

The `.out-of-scope/` directory in a repo stores persistent records of rejected feature requests. It serves two purposes:

1. **Institutional memory** — why a feature was rejected, so the reasoning isn't lost when the issue is closed
2. **Deduplication** — when a new issue comes in that matches a prior rejection, the skill can surface the previous decision instead of re-litigating it

## Directory structure

```
.out-of-scope/
├── dark-mode.md
├── plugin-system.md
└── graphql-api.md
```

One file per **concept**, not per issue. Multiple issues requesting the same thing are grouped under one file.

## File format

The file should be written in a relaxed, readable style — more like a short design document than a database entry. Use paragraphs, code samples, and examples to make the reasoning clear and useful to someone encountering it for the first time.

```markdown
# Dark Mode

This project does not support dark mode or user-facing theming.

## Why this is out of scope

The rendering pipeline assumes a single color palette defined in
`ThemeConfig`. Supporting multiple themes would require:

- A theme context provider wrapping the entire component tree
- Per-component theme-aware style resolution
- A persistence layer for user theme preferences

This is a significant architectural change that doesn't align with the
project's focus on content authoring. Theming is a concern for downstream
consumers who embed or redistribute the output.

```ts
// The current ThemeConfig interface is not designed for runtime switching:
interface ThemeConfig {
  colors: ColorPalette; // single palette, resolved at build time
  fonts: FontStack;
}
```

## Prior requests

- #42 — "Add dark mode support"
- #87 — "Night theme for accessibility"
- #134 — "Dark theme option"
```

### Naming the file

Use a short, descriptive kebab-case name for the concept: `dark-mode.md`, `plugin-system.md`, `graphql-api.md`. The name should be recognizable enough that someone browsing the directory understands what was rejected without opening the file.

### Writing the reason

The reason should be substantive — not "we don't want this" but why. Good reasons reference:

- Project scope or philosophy ("This project focuses on X; theming is a downstream concern")
- Technical constraints ("Supporting this would require Y, which conflicts with our Z architecture")
- Strategic decisions ("We chose to use A instead of B because...")

The reason should be durable. Avoid referencing temporary circumstances ("we're too busy right now") — those aren't real rejections, they're deferrals.

## When to check `.out-of-scope/`

During triage (Step 1: Gather context), read all files in `.out-of-scope/`. When evaluating a new issue:

- Check if the request matches an existing out-of-scope concept
- Matching is by concept similarity, not keyword — "night theme" matches `dark-mode.md`
- If there's a match, surface it to the maintainer: "This is similar to `.out-of-scope/dark-mode.md` — we rejected this before because [reason]. Do you still feel the same way?"

The maintainer may:

- **Confirm** — the new issue gets added to the existing file's "Prior requests" list, then closed
- **Reconsider** — the out-of-scope file gets deleted or updated, and the issue proceeds through normal triage
- **Disagree** — the issues are related but distinct, proceed with normal triage

## When to write to `.out-of-scope/`

Only when an **enhancement** (not a bug) is *rejected* as `wontfix`. This applies to enhancement PRs exactly as it does to issues — a rejected PR is recorded here so the same request doesn't return as fresh code.

Do **not** write here when something is closed as `wontfix` because it's **already implemented**. That's a built feature, not a rejected one; recording it would poison the dedup checks with false rejections. Instead, the closing comment points to where the feature already lives.

The flow:

1. Maintainer decides a feature request is out of scope
2. Check if a matching `.out-of-scope/` file already exists
3. If yes: append the new issue to the "Prior requests" list
4. If no: create a new file with the concept name, decision, reason, and first prior request
5. Post a comment on the issue explaining the decision and mentioning the `.out-of-scope/` file
6. Close the issue with the `wontfix` label

## Updating or removing out-of-scope files

If the maintainer changes their mind about a previously rejected concept:

- Delete the `.out-of-scope/` file
- The skill does not need to reopen old issues — they're historical records
- The new issue that triggered the reconsideration proceeds through normal triage
```

### File: .agents/skills/triage/SKILL.md
```markdown
---
name: triage
description: Move issues and external PRs through a state machine of triage roles — categorise, verify, grill if needed, and write agent-ready briefs.
disable-model-invocation: true
---

# Triage

Move issues on the project issue tracker through a small state machine of triage roles.

If this repo treats external pull requests as a request surface (see the issue-tracker config), triage covers them too: **a PR is an issue with attached code** — same roles, same states, same machine, with a few deltas marked "for a PR" below. Resolve a bare `#42` to an issue or PR per the tracker config.

Every comment or issue posted to the issue tracker during triage **must** start with this disclaimer:

```
> *This was generated by AI during triage.*
```

## Reference docs

- [AGENT-BRIEF.md](AGENT-BRIEF.md) — how to write durable agent briefs
- [OUT-OF-SCOPE.md](OUT-OF-SCOPE.md) — how the `.out-of-scope/` knowledge base works

## Roles

Two **category** roles:

- `bug` — something is broken
- `enhancement` — new feature or improvement

Five **state** roles:

- `needs-triage` — maintainer needs to evaluate
- `needs-info` — waiting on reporter for more information
- `ready-for-agent` — fully specified, ready for an AFK agent
- `ready-for-human` — needs human implementation
- `wontfix` — will not be actioned

For a PR, the same states read against the attached code: `ready-for-agent` means a brief is attached and an agent should take the next step on the diff; `ready-for-human` means it's ready for a human to merge.

Every triaged issue should carry exactly one category role and one state role. If state roles conflict, flag it and ask the maintainer before doing anything else.

These are canonical role names — the actual label strings used in the issue tracker may differ. The mapping should have been provided to you - run `/setup-matt-pocock-skills` if not.

State transitions: an unlabeled issue normally goes to `needs-triage` first; from there it moves to `needs-info`, `ready-for-agent`, `ready-for-human`, or `wontfix`. `needs-info` returns to `needs-triage` once the reporter replies. The maintainer can override at any time — flag transitions that look unusual and ask before proceeding.

## Invocation

The maintainer invokes `/triage` and describes what they want in natural language. Interpret the request and act. Examples:

- "Show me anything that needs my attention"
- "Let's look at #42" (issue or PR)
- "Move #42 to ready-for-agent"
- "What's ready for agents to pick up?"

## Show what needs attention

Query the issue tracker and present three buckets, oldest first:

1. **Unlabeled** — never triaged.
2. **`needs-triage`** — evaluation in progress.
3. **`needs-info` with reporter activity since the last triage notes** — needs re-evaluation.

When PRs are in scope, include external PRs in these buckets and tag each line `[PR]` or `[issue]`. Discovery surfaces only *external* PRs (the tracker config defines who counts as external) — a collaborator's in-flight PR is not triage work. This filter is discovery-only; an explicitly named PR is always triaged regardless of author.

Show counts and a one-line summary per item. Let the maintainer pick.

## Triage a specific issue or PR

1. **Gather context.** Read the full issue or PR (body, comments, labels, author, dates; for a PR, the diff too). Parse any prior triage notes so you don't re-ask resolved questions. Explore the codebase using the project's domain glossary, respecting ADRs in the area. Run two checks against the codebase: (a) **redundancy** — search for an existing implementation of the requested behavior by domain concept (not just the request's wording), and report where you looked. If found, it's an already-implemented `wontfix` (step 5). (b) **prior rejection** — read `.out-of-scope/*.md` and surface any that resembles this request.

2. **Recommend.** Tell the maintainer your category and state recommendation with reasoning, plus a brief codebase summary relevant to the request — including whether it's already implemented. Wait for direction.

3. **Verify the claim.** Before any grilling, check that the claim holds up. For a bug, reproduce it from the reporter's steps. For a PR, confirm the diff does what it claims — check it out, run the relevant tests or commands. Report what happened: confirmed (with code path), failed, or insufficient detail (a strong `needs-info` signal). A confirmed verification makes a much stronger agent brief.

4. **Grill (if needed).** If the request needs fleshing out, run the `/grilling` and `/domain-modeling` skills together — grill it into shape one question at a time, sharpening domain terms and updating `CONTEXT.md`/ADRs inline as decisions land.

5. **Apply the outcome:**
   - `ready-for-agent` — post an agent brief comment ([AGENT-BRIEF.md](AGENT-BRIEF.md)).
   - `ready-for-human` — same structure as an agent brief, but note why it can't be delegated (judgment calls, external access, design decisions, manual testing).
   - `needs-info` — post triage notes (template below).
   - `wontfix` — close, with the comment depending on *why*:
     - **Already implemented** — the change already exists in the codebase. Point to where it lives; do **not** write to `.out-of-scope/` (that KB is for *rejected* requests, not built ones).
     - **Rejected (bug)** — polite explanation, then close.
     - **Rejected (enhancement)** — write to `.out-of-scope/`, link to it from a comment, then close ([OUT-OF-SCOPE.md](OUT-OF-SCOPE.md)).
   - `needs-triage` — apply the role. Optional comment if there's partial progress.

## Quick state override

If the maintainer says "move #42 to ready-for-agent", trust them and apply the role directly. Confirm what you're about to do (role changes, comment, close), then act. Skip grilling. If moving to `ready-for-agent` without a grilling session, ask whether they want to write an agent brief.

## Needs-info template

```markdown
## Triage Notes

**What we've established so far:**

- point 1
- point 2

**What we still need from you (@reporter):**

- question 1
- question 2
```

Capture everything resolved during grilling under "established so far" so the work isn't lost. Questions must be specific and actionable, not "please provide more info".

## Resuming a previous session

If prior triage notes exist on the issue or PR, read them, check whether the reporter has answered any outstanding questions, and present an updated picture before continuing. Don't re-ask resolved questions.
```

### File: .agents/skills/wayfinder/SKILL.md
```markdown
---
name: wayfinder
description: Plan a huge chunk of work — more than one agent session can hold — as a shared map of decision tickets on your issue tracker, and resolve them one at a time until the way to the destination is clear.
disable-model-invocation: true
---

A loose idea has arrived — too big for one agent session, and wrapped in fog: the way from here to the **destination** isn't visible yet. Wayfinding is about finding that way, not charging at the destination. This skill charts the way as a **shared map** on the repo's issue tracker, then works its **decision tickets** — questions whose resolution is a decision, not slices of a build to execute — one at a time until the route is clear.

The destination varies per effort, and naming it is the first act of charting — it shapes every ticket. It might be a spec to hand off and iterate on, a decision to lock before planning starts, or a change made in place like a data-structure migration. The map is domain-agnostic — engineering work, course content, whatever fits the shape.

## Plan, don't do

Wayfinder is **planning** by default: each ticket resolves a decision, and the map is done when the way is clear — nothing left to decide before someone goes and does the thing. The pull to just do the work is usually the signal you've reached the edge of the map and it's time to hand off. An effort can override this in its **Notes** — carrying execution into the map itself — but absent that, produce decisions, not deliverables.

## Refer by name

Every map and ticket is an issue, so it has a **name** — its title. In everything the human reads — narration, the map's Decisions-so-far — refer to it by that name, never by a bare id, number, or slug. A wall of `#42, #43, #44` is illegible; names read at a glance. The id and URL don't vanish — a name wraps its link — but they ride *inside* the name, never stand in for it.

## The Map

The map is a single issue on this repo's issue tracker, labelled `wayfinder:map` — the canonical artifact. Its tickets are child issues of the map.

The map is an **index**, not a store. It lists the decisions made and points at the tickets that hold their detail; a decision lives in exactly one place — its ticket — so the map never restates it, only gists it and links.

**Where the map, its child tickets, blocking, and frontier queries physically live is tracker-specific.** The issue tracker should have been provided to you — run `/setup-matt-pocock-skills` if not. Consult the tracker doc's "Wayfinding operations" section for how _this_ repo expresses them. If no tracker has been provided, default to the local-markdown tracker.

### The map body

The whole map at low resolution, loaded once per session. Open tickets are **not** listed — they are open child issues, found by query.

```markdown
## Destination

<what reaching the end of this map looks like — the spec, decision, or change this effort is finding its way to. One or two lines; every session orients to it before choosing a ticket.>

## Notes

<domain; skills every session should consult; standing preferences for this effort>

## Decisions so far

<!-- the index — one line per closed ticket: enough to judge relevance, then zoom the link for the detail the ticket holds -->

- [<closed ticket title>](link) — <one-line gist of the answer>

## Not yet specified

<!-- see "Fog of war": in-scope fog you can't ticket yet; graduates as the frontier advances -->

## Out of scope

<!-- see "Out of scope": work ruled beyond the destination; closed, never graduates -->
```

### Tickets

Each ticket is a **child issue** of the map; the tracker's issue id is its identity. Its body is the question, sized to one 100K token agent session:

```markdown
## Question

<the decision or investigation this ticket resolves>
```

Each ticket carries a `wayfinder:<type>` label — one of `research`, `prototype`, `grilling`, `task` (see [Ticket Types](#ticket-types)).

A session **claims** a ticket by assigning it to the dev driving the map, **first**, before any work, so concurrent sessions skip it. That assignee _is_ the claim: an open, unassigned ticket is unclaimed.

Blocking uses the tracker's **native** dependency relationship — essential because it renders the frontier _visually_ in the tracker's own UI, so the human sees what's takeable without opening the map. Only a tracker that lacks native blocking falls back to a body convention. A ticket is **unblocked** when every ticket blocking it is closed; the **frontier** is the open, unblocked, unclaimed children — the edge of the known.

The answer isn't part of the body — it's recorded on resolution (see [Work through the map](#work-through-the-map)). Assets created while resolving a ticket are linked from the issue, not pasted in.

## Ticket Types

Every ticket is either **HITL** — human in the loop, worked *with* a human who speaks for themselves — or **AFK**, driven by the agent alone. A HITL ticket only resolves through that live exchange; the agent never stands in for the human's side of it (a grilling agent that answers its own questions has broken this).

- **Research** (AFK): Reading documentation, third-party APIs, or local resources like knowledge bases to surface a fact a decision waits on. Resolved by a `/research` **subagent**. Use when knowledge outside the current working directory is required.
- **Prototype** (HITL): Raise the fidelity of the discussion by making a cheap, rough, concrete artifact to react to — an outline, a rough take, a stub, or UI/logic code via the /prototype skill. Links the prototype as an asset. Use when "how should it look" or "how should it behave" is the key question.
- **Grilling** (HITL): Conversation via the /grilling and /domain-modeling skills, one question at a time. The default case.
- **Task** (HITL or AFK): Manual work that must happen before a *decision* can be made — nothing to decide, prototype, or research, but the discussion is blocked until it's done. Signing up for a service so its API can be judged, provisioning access, moving data so its shape can be seen. This is the one type that *does* rather than decides — and it earns its place by unblocking a decision, not by delivering the destination. The agent drives it alone where it can (AFK); otherwise it hands the human a precise checklist (HITL). Resolved when the work is done; the answer records what was done and any resulting facts (credentials location, new URLs, row counts) later tickets depend on.

## Fog of war

The map is _deliberately_ incomplete: don't chart what you can't yet see. Beyond the live tickets lies the **fog of war** — the dim view of decisions and investigations you can tell are coming but can't yet pin down, because they hang on questions still open. Resolving a ticket clears the fog ahead of it, graduating whatever's now specifiable into fresh tickets — one at a time, until the way to the destination is clear and no tickets remain.

The map's **Not yet specified** section is where that dim view is written down: the suspected question, the area to revisit later. It's the undiscovered frontier _toward_ the destination — everything here is in scope, just not sharp enough to ticket. Write as loosely or as fully as the view allows; it doubles as a signpost for collaborators reading where the effort is headed.

**Fog or ticket?** The test is whether you can state the question precisely now — _not_ whether you can answer it now.

- **Ticket when** the question is already sharp — even if it's blocked and you can't act on it yet.
- **Not yet specified when** you can't yet phrase it that sharply. Don't pre-slice the fog into ticket-sized pieces: it's coarser than a ticket, and one patch may graduate into several tickets, or none, once the frontier reaches it.

**Not yet specified** excludes what's already decided (Decisions so far), what's already a live ticket, and what's out of scope (the next section).

## Out of scope

Fog only ever gathers _toward_ the destination. The destination fixes the scope, so work beyond it is **out of scope** — it isn't fog, and it doesn't belong in **Not yet specified**. It gets its own **Out of scope** section on the map: work you've consciously ruled out of _this_ effort. Scope, not sharpness, lands it here.

Out-of-scope work never graduates — the frontier stops at the destination — so it returns only if the destination is redrawn, and then as a fresh effort, not a resumption.

Ruling something out of scope is a scoping act, not a step on the route. When a ticket that already exists turns out to sit past the destination — mis-scoped in while charting, or exposed by a resolution — **close it** (a closed ticket is unambiguously off the frontier) and leave one line in the **Out of scope** section: the gist plus why it's out of scope, linking the closed ticket. It stays out of **Decisions so far**, which records the route actually walked — a scope boundary isn't a step on it.

## Invocation

Two modes. Either way, **never resolve more than one ticket per session** — with the exception of research tickets.

### Chart the map

User invokes with a loose idea.

1. **Name the destination.** Run a `/grilling` and `/domain-modeling` session to pin down what this map is finding its way to — the spec, decision, or change. The destination fixes the scope, so it's settled first.
2. **Map the frontier.** Grill again, **breadth-first** this time: fan out across the whole space rather than deep on any one thread, surfacing the open decisions and the first steps takeable now. **If this surfaces no fog** — the way to the destination is already clear, the whole journey small enough for one session — you don't need a map. Stop and ask the user how they'd like to proceed.
3. **Create the map** (label `wayfinder:map`): Destination and Notes filled in, Decisions-so-far empty, the fog sketched into **Not yet specified**.
4. **Create the tickets you can specify now** as child issues of the map — then wire blocking edges in a **second pass** (issues need ids before they can reference each other). Wiring sorts them into the frontier and the blocked; everything you can't yet specify stays in the fog — the **Not yet specified** section.
5. **Fire the research subagents.** For each `research` ticket you just created, spin up a `/research` subagent to resolve it in parallel, capturing its findings on a throwaway `research/<name>` branch with a context pointer from the ticket.
6. Stop — charting is one session's work; it hand-resolves nothing.

### Work through the map

User invokes with a map (URL or number). A ticket is **optional** — without one, you pick the next decision, not the user.

1. Load the **map** — the low-res view, not every ticket body.
2. Choose the ticket. If the user named one, use it. Otherwise take the first frontier ticket in order. **Claim it**: assign it to yourself before any work.
3. Resolve it — **zoom as needed**: fetch the full body of any related or closed ticket on demand; invoke the skills the `## Notes` block names. If in doubt, use `/grilling` and `/domain-modeling`.
4. Record the resolution: post the answer as a **resolution comment**, **close** the issue, and **append a context pointer** to the map's Decisions-so-far.
5. Add newly-surfaced tickets (create-then-wire); graduate any fog the answer has made specifiable, clearing each graduated patch from **Not yet specified** so it lives only as its new ticket. If the answer reveals a ticket — this one or another — sits beyond the destination, **rule it out of scope** rather than resolving it on the route. If the decision invalidates other parts of the map, update or delete those tickets.

The user may run unblocked tickets in parallel, so expect other sessions to be editing the tracker concurrently.
```

### File: .agents/skills/wizard/SKILL.md
```markdown
---
name: wizard
description: Generate an interactive bash wizard that walks a human through a manual procedure — third-party setup, a one-off migration, an A→B state transition — opening URLs, capturing values, confirming each step, and writing .env files and GitHub Actions secrets.
disable-model-invocation: true
---

# Wizard

A **wizard** is a bash script that walks a human, step by step, through a manual procedure that's tedious to do by hand and tedious to re-explain to an AI every time. It opens each URL, says exactly what to click and copy, captures the values, writes them where they belong (`.env`, GitHub secrets), confirms at every stage, and shows how much is left. It might configure third-party services, run a one-off migration, or move the project from one state to another.

The delightful UX is already solved by [template.sh](template.sh) — progress with time-remaining, confirmation gates, cross-platform URL opening (including WSL), hidden secret entry, idempotent `.env` upserts, `gh secret`/`gh variable` writes, and a closing summary. **Your job is only to scope the procedure and author its stages.** The library above the `STAGES` marker is identical in every wizard; that consistency is the point — never hand-edit it.

A wizard is ephemeral by default — built for one run, saved to a scratch or `scripts/` path, deleted when the job's done. Commit it only when the user wants a repeatable setup path that should live in the repo.

## Process

### 1. Scope the procedure

Work out every manual step the human must take and every value that gets captured along the way. Read the repo first — don't ask cold:

- For setup: `.env`, `.env.example`, `.env.*`, `README`, `docker-compose*`, framework config, and `.github/workflows/*` (every `secrets.*` / `vars.*` reference is a value the wizard must produce).
- For a migration or transition: the current state, the target state, and the irreversible actions between them.

Then show the user the ordered list of stages and the values each produces, and confirm — they may add, drop, or reorder.

**Done when:** every stage is named in order, and for each captured value you know (a) where the human gets it, (b) where it's written (`.env`, a GitHub secret, both, or nowhere — some stages are pure actions), and (c) whether it's secret (hidden entry) or public.

### 2. Map each stage's journey

For each stage, write the precise path a human follows: which URL to open, what to do there, where a value is shown, which variable it fills — e.g. "Dashboard → Developers → API keys → Reveal test key → copy". Where you don't actually know the current UI or the exact command, say so and ask the user or check the docs — never invent steps that may not exist.

**Done when:** every stage traces to concrete instructions a stranger could follow.

### 3. Author the wizard

Copy `template.sh` to the target path. Replace the example stage with one `stage` per step, in dependency order. Use the library helpers — `stage`, `say`/`step`, `open_url`, `ask`/`ask_secret`, `write_env`, `set_secret`/`set_var`, `pause`/`confirm` — and set `TOTAL_STAGES` and `TOTAL_MINUTES` to honest estimates (this drives the time-remaining display).

Hold the bar the template sets: open the URL before asking for its value, use `ask_secret` for anything secret, `write_env` every persisted value, `set_secret` only the values CI actually needs, and `confirm` before any irreversible action. Each `stage` clears the screen so only the current step is visible — keep a stage to one focused task so nothing the human needs scrolls away. Don't touch the library above the marker.

### 4. Verify and hand off

- `bash -n <script>`; run `shellcheck` if available.
- `chmod +x <script>`.
- Don't run it end-to-end yourself — it opens browsers and blocks on human input. Trace it statically instead: every value from step 1 is captured and lands where step 1 said, and every `set_secret` name exactly matches a `secrets.*` reference in CI.
- Tell the user how to run it. If it's a repeatable setup path, commit it and link it from the README so the next person runs the script instead of asking an AI.
```

### File: .agents/skills/writing-great-skills/GLOSSARY.md
```markdown
# Glossary — Building Great Skills

The domain model for what makes a skill great. A skill exists to wrangle determinism out of a stochastic system; the root virtue is **Predictability**, and every term below is a lever on it. This is the disclosed reference for [`writing-great-skills`](SKILL.md).

The terms are grouped by axis: **Invocation** (how a skill is reached), **Information Hierarchy** (how its content is arranged), **Steering** (how the agent's runtime behaviour is shaped), and **Pruning** (how it is kept lean). Each **failure mode** lives beside the lever that cures it, tagged _failure mode_.

**Bold terms** in any definition are themselves defined in this glossary; find them by their heading.

## Predictability

The degree to which a skill makes the agent behave the same _way_ on every run — the same process, not the same output (a brainstorming skill should _predictably_ diverge; its tokens vary, its behaviour doesn't). The root virtue every other term serves — cost and maintainability are symptoms of it, not rivals.

_Avoid_: consistency, reliability, robustness, output-determinism

## Invocation

How a skill is reached — and the two loads you pay for the choice.

### Model-Invoked

A skill that keeps its **description** field, so the agent can see it and fire it autonomously — and the human can still type its name, so model-invocation always _includes_ user reach. There is no model-only state: a description only ever _adds_ agent discovery, never removes the human's. Pays a permanent **context load** on every turn in exchange for that discoverability. Reachable by other skills, because the description that makes it agent-discoverable makes it invocable. A model-invoked skill whose content is all **reference** is also one home for shared reference: another skill can invoke it, so reference needed by several skills lives in one place. Pick model-invocation only when the agent must reach the skill on its own; if it never fires except by hand, drop the description and pay no context load.

_Avoid_: ability, tool, capability

### User-Invoked

A skill with its **description** stripped — invisible to the agent and reachable only by the human typing its name (user-_only_, where **model-invoked** is user-_and-agent_). Trades agent-discoverability for zero **context load**. Because it has no description, nothing but the human can reach it: no other skill can fire it.

_Avoid_: procedure, workflow, command

### Description

The skill's machine-readable trigger, and the one **context pointer** a **model-invoked** skill is forced to keep loaded at all times. Its mere presence _is_ the invocation axis: keep it and the skill is model-invoked (and reachable by other skills); delete it and the skill is **user-invoked**, reachable only by the human. The source of a model-invoked skill's **context load**.

_Avoid_: frontmatter, summary

### Context Pointer

A reference held in the agent's context that names some out-of-context material and encodes the condition for reaching it. The **description** is the top-level context pointer (context window → skill); pointers to disclosed files are the same object one level down. Its wording, not the target, decides _when_ the agent reaches — and _how reliably_. A must-have target behind a weakly worded pointer is a variance bug: fix the wording first, and inline the material only if sharpening fails.

_Avoid_: link, reference, import

### Context Load

The cost a **model-invoked** skill imposes on the agent's context window — its **description**, always loaded, spending both tokens and attention. What **user-invoked** skills escape by having no description, and the brake on splitting into more model-invoked skills.

_Avoid_: token cost, context bloat

### Cognitive Load

The cost a **user-invoked** skill imposes on the human — what they must hold in their head: which skills exist and when to reach for each (the human is the index). What **model-invocation** removes by being agent-discoverable, and the brake on splitting into more user-invoked skills. Not a cost to minimise: it is the price of human agency, the reason some skills stay user-invoked. Spend it where human judgement matters; remove it where it does not.

_Avoid_: human index, burden, overhead

### Router Skill

A **user-invoked** skill whose job is to point at your other user-invoked skills — naming each and when to reach for it — so the human has one skill to remember instead of many. It can only hint, never fire them: user-invoked skills have no **description**, so nothing but the human can reach them. The cure for **cognitive load** when user-invoked skills multiply.

_Avoid_: dispatcher, menu, registry, index, router procedure

### Granularity

How finely you divide skills. Finer division spends one of the two loads: more **model-invoked** skills spend **context load** (more descriptions crowding the window and competing for attention); more **user-invoked** skills spend **cognitive load** (more for the human to remember and reach for). Two cuts guide the division. By **invocation**, split off a model-invoked skill where you have a distinct **leading word** to trigger it — a trigger word you actually use in your prompts. By **sequence**, split a run of **steps** where a step's **post-completion steps** need hiding, since isolating it in its own context clears what follows. Beware the reverse: merging sequences exposes each step's post-completion steps to what follows, inviting premature completion.

_Avoid_: chunking, modularity

## Information Hierarchy

How a skill's content is arranged, and how far down the ladder each piece sits.

### Information Hierarchy

A skill's content ranked by how immediately the agent needs it — a single ladder, produced by two cuts: in-file or behind a pointer, and step or reference. The rungs:

- **Steps** — in-file, primary
- **Reference**, in-file — secondary
- **Reference**, disclosed — behind a **context pointer**

A skill with no **steps** uses just the bottom two rungs — often a legitimately flat peer-set (e.g. every rule of a review on one rung), which is a fine arrangement, not a smell. The hierarchy is independent of invocation: a skill can be model- or user-invoked whether it is all steps, all reference, or both. When a skill has steps, in-file reference that should be disclosed buries them and turns attending to them into a coin-flip — a variance lever, not just a legibility one. Keep the top of the ladder legible; push down it whatever you can.

_Avoid_: structure, organization, layout

### Steps

The ordered actions the agent performs — when a skill has them, the primary tier of its content, and the part that earns its place in SKILL.md. Not every skill has steps: a skill can be all steps (`tdd`), all **reference** (a review), or both, independent of invocation. Every step ends on a **completion criterion**, clear or vague.

_Avoid_: workflow, instructions, choreography

### Reference

Material the agent refers to on demand — definitions, facts, parameters, examples, conditional instructions. When a skill has **steps** it is secondary to them; when a skill has none it is the entire content; or it lives outside any skill entirely — see **External Reference**. Reached via **context pointers**, and the prime candidate for **progressive disclosure**.

_Avoid_: supporting material, docs, background

### External Reference

**Reference** that lives outside the skill system — a plain file, no **description**, no **steps**, not invocable — that any skill can point at. The home for shared reference that needn't fire on its own, and the only shared home two **user-invoked** skills can use, since neither has a description and so neither can fire the other.

_Avoid_: doc, resource, knowledge base

### Progressive Disclosure

Moving **reference** down the ladder — out of SKILL.md and behind a **context pointer** — so the top stays legible. Not primarily a token optimisation; it is how the **information hierarchy** is protected. Licensed by **branching**: disclose what only some branches need, inline what every path needs, and if a pointer fires unreliably on must-have material, sharpen its wording, and pull it back inline only if that fails.

_Avoid_: lazy loading, chunking

### Co-location

Keeping the material an agent needs at once in one place — a concept's definition, rules, and caveats under a single heading, not scattered across the file — so reading one part brings its neighbours with it. The within-file companion to the **Information Hierarchy**: the hierarchy ranks _how far down_ a piece sits; co-location decides _what sits beside it_ once there. There is no formula for the right format of a body of **reference**; the test is that a skill should read like documentation written for the agent, and grouped material reads that way where scattered material does not. Distinct from **Duplication**: that repeats one meaning in two places, where scattering fragments a single meaning across many.

_Avoid_: grouping, clustering, cohesion

### Sprawl

_Failure mode._ A skill that is simply too long — too many lines in SKILL.md — independent of whether they are stale or repeated. Even an all-live, all-unique skill can sprawl. It costs readability (the agent wades through more before it can act, and attention thins across the excess), maintainability (every extra line is one more to keep **relevant**), and tokens. The cure is the **information hierarchy**: push **reference** down behind **context pointers**, and split by **branch** or sequence so each path carries only what it needs. Distinct from **sediment** (length from stale accumulation) and **duplication** (length from repeated meaning) — sprawl is length itself, whatever its cause.

_Avoid_: bloat, length, size, verbosity

## Steering

The levers that shape the agent's runtime behaviour toward **Predictability**.

### Branch

A distinct way a skill can be invoked — a case the skill handles — so different runs take different paths through it. A skill with many steps may carry many branches; a linear one has none.

_Avoid_: path, case, fork

### Leading Word

A compact concept — also called a _Leitwort_ — already living in the model's pretraining, that the agent thinks with while running the skill. It encodes a behavioural principle in the fewest possible tokens by invoking priors the model already holds (e.g. _lesson_, _proximal zone of development_, _fog of war_, _tracer bullets_). Repeated as a token, never as a sentence, it accumulates a distributed definition across the skill and anchors a whole region of behaviour. Coining your own works if you define it clearly, but a made-up word recruits no priors — you pay in definition tokens what a pretrained word gives free. Reach for an existing word first.

A leading word serves **predictability** twice. In the body it anchors **execution** — the agent reaches for the same behaviour every time the concept appears, and inside flat reference it focuses attention on a class of thing to look for, recruiting the right checks each run. In the **description** it anchors **invocation** — and not only within the skill: when the same word lives in your prompts, your docs, and your codebase, the agent links that shared language to the skill and fires it more reliably. Word a description with the leading words you actually use when you want the skill.

_Avoid_: keyword, term, motif

### Completion Criterion

The condition that tells the agent a unit of work is done — the target it judges against. Two properties make it a lever, not just a quality. Its **clarity** (can the agent tell done from not-done?) resists **premature completion** — a vague bound ("understanding reached") lets the agent declare done and slip to the next step; this axis needs _steps_ to bite, since premature completion is a between-steps failure. Its **demand** (how much it requires) sets **legwork** — "every modified model accounted for" forces thorough work where "produce a change list" does not — and this axis is _not_ step-bound: it can bind a body of flat reference too, which is how a skill with no steps still carries an exhaustiveness bar ("every rule applied"). The strongest criteria are both checkable and exhaustive.

_Avoid_: done condition, exit condition, stopping rule

### Legwork

The work an agent does behind the scenes within a single step — reading files, exploring the codebase, making changes, digging up what it needs rather than offloading to the user. It lives below the step structure: never written as its own step, latent in the wording, controlled by the agent rather than the skill. The within-step counterpart to **post-completion steps**' across-step pull. Raised by a **leading word** (_comprehensive_, _thorough_) or a **completion criterion** that demands the work be exhaustive — including the demand axis applied to flat reference, which is what drives a skill of flat reference to cover all its rungs. Goes thin either when that demand is missing or when **premature completion** cuts the step short.

_Avoid_: scope, effort, diligence, coverage

### Post-Completion Steps

The **steps** that follow the current step. Visible, they pull the agent forward into **premature completion** — the more it sees, the stronger the tug; the defence is to hide them by splitting the sequence of steps into two.

_Avoid_: horizon, fog of war, lookahead

### Premature Completion

_Failure mode._ Ending the current step before it is genuinely done, because the agent's attention slips to being done rather than to the work. A between-steps failure: it needs **steps** to occur — a skill with no steps that quits early isn't premature completion but thin **legwork** under an unmet demand. A tug-of-war between two forces: visible **post-completion steps** (the pull forward) and the **completion criterion**'s clarity (the resistance — a sharp, checkable bar holds; a vague one gives way). Fuzziness is the necessary condition: a sharp bound resists the pull no matter how many later steps are visible, so a step that never rushes needs no defending. Two levers hold a step that does, but reach for them in order: **sharpen the bound first** — it is local and cheap. Only when the criterion is irreducibly fuzzy _and_ you actually observe the rush do you **hide the later steps** — and hiding only works across a real context boundary (a user-invoked hand-off or a subagent dispatch; an inline model-invoked call leaves the later steps in context and clears nothing). One cause of thin legwork, but distinct from it: legwork can be thin even when a step runs to full completion.

_Avoid_: premature closure, the rush, rushing, shortcutting

### Negation

_Failure mode._ Steering by prohibition — telling the agent what _not_ to do — which drags the forbidden behaviour into context and makes it _more_ available, not less. _Don't think of an elephant_, and the elephant is all there is; _never write verbose comments_, and verbosity is the pattern the agent has just read. The negation is a weak modifier the strongly-activated concept overruns, so the ban half-reads as an instruction to do the thing. Its **leading word** is the _elephant_: whatever a prohibition names into the frame. Cure: prompt the **positive** — describe the target behaviour ("write one-line comments") so the banned one is never spoken. A prohibition earns its place only as a hard guardrail on a behaviour you cannot phrase positively; even then, pair it with the positive target so attention lands on what to do.

_Avoid_: ironic rebound, don't-prompting, the pink elephant

## Pruning

Keeping a skill lean — each remedy paired with the failure it cures.

### Single Source of Truth

The desired state where each meaning lives in exactly one authoritative place, so a change to the skill's behaviour is a change in one place. **Duplication** is its violation.

_Avoid_: home, canonical location

### Duplication

_Failure mode._ The same meaning given more than one **single source of truth**. It costs maintenance (change one place, you must change the others), costs tokens, and inflates prominence — repeating a meaning weights it on the ladder past its real rank. The accidental inverse of a **leading word**, which raises attention on purpose by repeating a token, never the meaning.

_Avoid_: repetition, redundancy

### Relevance

Whether a line still bears on what the skill does — the lens for what to keep. A line loses relevance either by never bearing on the task (mere exposition, or a **branch** that should be disclosed) or by going stale: drifting out of date as the behaviour or world it describes changes. Shorter skills are easier to keep relevant, because each line is cheaper to check. Distinct from **no-op**: relevance asks whether a line bears on the task, not whether it changes behaviour.

_Avoid_: load-bearing, staleness, freshness

### Sediment

_Failure mode._ Layers of old content that settle in a skill and are never cleared, because adding feels safe and removing feels risky — so stale and irrelevant lines accumulate and you must core down through them to find what is still live. The default fate of any skill without a pruning discipline; the slow erosion of **relevance**, as opposed to **duplication**'s repeated meaning.

_Avoid_: accretion, bloat, cruft, rot

### No-Op

_Failure mode._ An instruction that changes nothing because the model already does it by default — you pay load to tell the agent what it would do anyway. The test: does a line change behaviour versus the default? A line can be perfectly **relevant** and still be a no-op. The same priors that make a **leading word** free make a no-op worthless.

A leading word is a _technique_; No-Op is a _verdict_ on a line — and they cross. A leading word too weak to beat the default is a no-op (_be thorough_ when the agent is already thorough-ish), and the fix is a stronger word that passes the verdict (_relentless_), not a different technique. So the No-Op test — does it change behaviour versus the default? — is also how you grade whether a leading word is earning its repetitions. This is model-relative, not reader-relative: two people disagreeing over whether a line is a no-op disagree about the default, and settle it by running the skill, not by debate.

_Avoid_: redundant instruction, restating the obvious, belaboring
```

### File: .agents/skills/writing-great-skills/SKILL.md
```markdown
---
name: writing-great-skills
description: Reference for writing and editing skills well — the vocabulary and principles that make a skill predictable.
disable-model-invocation: true
---

A skill exists to wrangle determinism out of a stochastic system. **Predictability** — the agent taking the same _process_ every run, not producing the same output — is the root virtue; every lever below serves it.

**Bold terms** are defined in [`GLOSSARY.md`](GLOSSARY.md); look them up there for the full meaning.

## Invocation

Two choices, trading different costs:

- A **model-invoked** skill keeps a **description**, so the agent can fire it autonomously _and_ other skills can reach it (you can still type its name too). It contributes to **context load** — the description sits in the window every turn. Mechanics: omit `disable-model-invocation`, and write a model-facing description with rich trigger phrasing ("Use when the user wants…, mentions…").
- A **user-invoked** skill strips the description from the agent's reach: only you, typing its name, can invoke it — and no other skill can. Zero context load, but it spends **cognitive load**: _you_ are the index that must remember it exists. Mechanics: set `disable-model-invocation: true`; the `description` becomes human-facing — a one-line summary, trigger lists stripped.

Pick model-invocation only when the agent must reach the skill on its own, or another skill must. If it only ever fires by hand, make it user-invoked and pay no context load.

When user-invoked skills multiply past what you can remember, that piled-up cognitive load is cured by a **router skill**: one user-invoked skill that names the others and when to reach for each.

## Writing the description

A model-invoked **description** does two jobs — state what the skill is, and list the **branches** that should trigger it. Every word increases **context load**, so a description earns even harder pruning than the body:

- **Front-load the skill's leading word** — the description is where it does its invocation work.
- **One trigger per branch.** Synonyms that rename a single branch are **duplication** — "build features using TDD … asks for test-first development" is one branch written twice. Collapse them; keep only genuinely distinct branches.
- **Cut identity that's already in the body.** Keep the description to triggers, plus any "when another skill needs…" reach clause.

## Information hierarchy

A skill is built from two content types — **steps** and **reference** — that mix freely: a skill can be all steps, all reference, or both. The core decision is which to use and where each sits on the **information hierarchy**, a ladder ranked by how immediately the agent needs the material:

1. **In-skill step** — an ordered action in `SKILL.md`, the primary tier: what the agent does, in order. Each step ends on a **completion criterion**, the condition that tells the agent the work is done. Make it _checkable_ (can the agent tell done from not-done?) and, where it matters, _exhaustive_ ("every modified model accounted for", not "produce a change list") — a vague criterion invites **premature completion**.
2. **In-skill reference** — a definition, rule, or fact in `SKILL.md`, consulted on demand. Often a legitimately flat peer-set (every rule of a review on one rung) — a fine arrangement, not a smell. _This skill is all reference._
3. **External reference** — reference pushed out of `SKILL.md` into a separate file, reached by a **context pointer**, loaded only when the pointer fires. (Spans _disclosed_ reference — a sibling file like `GLOSSARY.md`, still part of the skill — through fully **external reference** that lives outside the skill system and any skill can point at.)

A demanding completion criterion drives thorough **legwork** — the digging the agent does within the work — whether the skill has steps or not, since "every rule applied" binds flat reference just as "every step done" binds a sequence.

Push too little down and the top bloats; push too much and you hide material the agent actually needs. That tension is the whole decision.

**Progressive disclosure** is the move down the ladder — out of `SKILL.md` into a linked file — so the top stays legible. Mechanics: a linked `.md` file in the skill folder, named for what it holds (this skill discloses its full definitions to `GLOSSARY.md`). Some skills are used in more than one way, and each distinct way is a **branch** — different runs taking different paths through the skill. Branching is the cleanest disclosure test: inline what every branch needs, and push behind a pointer what only some branches reach. A **context pointer**'s _wording_, not its target, decides when and how reliably the agent reaches the material.

Where the ladder decides _how far down_ a piece sits, **co-location** decides _what sits beside it_ once there: keep a concept's definition, rules, and caveats under one heading rather than scattered, so reading one part brings its neighbours with it.

## When to split

**Granularity** is how finely you divide skills, and each cut spends one of the two loads, so split only when the cut earns it. Two cuts:

- **By invocation** — split off a **model-invoked** skill when you have a distinct **leading word** that should trigger it on its own, or another skill must reach it. You pay **context load** for the new always-loaded **description**, so that independent reach has to be worth it.
- **By sequence** — split a run of **steps** when the steps still ahead (a step's **post-completion steps**) tempt the agent to rush the one in front of it (**premature completion**). Keeping them out of view encourages the agent to do more **legwork** on the current task.

## Pruning

Keep each meaning in a **single source of truth**: one authoritative place, so changing the behaviour is a one-place edit.

Check every line for **relevance**: does it still bear on what the skill does?

Then hunt **no-ops** sentence by sentence, not just line by line: run the no-op test on each sentence in isolation, and when one fails, delete the whole sentence rather than trim words from it. Be aggressive — most prose that fails should go, not be rewritten.

## Leading words

A **leading word** is a compact concept already living in the model's pretraining that the agent thinks with while running the skill (e.g. _lesson_, _fog of war_, _tracer bullets_). Repeated throughout the text (though not necessarily - a strong leading word might only be needed once), it accumulates a distributed definition and anchors a whole region of behaviour in the fewest tokens, by recruiting priors the model already holds.

It serves predictability twice. In the body it anchors _execution_: the agent reaches for the same behaviour every time the word appears. In the description it anchors _invocation_: when the same word lives in your prompts, docs, and code, the agent links that shared language to the skill and fires it more reliably.

Hunt for opportunities to refactor skills to use leading words. A triad spelled out at three sites (**duplication**), a description spending a sentence to gesture at one idea — each is a passage begging to **collapse** into a single token. Examples include:

- "fast, deterministic, low-overhead" -> _tight_ — one quality restated across a phase — into a single pretrained word (a _tight_ loop).
- "a loop you believe in" -> _red_ — converts a fuzzy gate into a binary observable state (the loop goes _red_ on the bug, or it doesn't).

You win twice over: fewer tokens, _and_ a sharper hook for the agent to hang its thinking on. Assume every skill is carrying restatements that leading words retire — go find them.

## Failure modes

Use these to diagnose issues the user may be having with the skill.

- **Premature completion** — ending a step before it's genuinely done, attention slipping to _being done_. Defence, in order: sharpen the completion criterion first (cheap, local); only if it is irreducibly fuzzy _and_ you observe the rush, hide the post-completion steps by splitting (the sequence cut).
- **Duplication** — the same meaning in more than one place. Costs maintenance and tokens, and inflates a meaning's prominence on the ladder past its real rank.
- **Sediment** — stale layers that settle because adding feels safe and removing feels risky. The default fate of any skill without a pruning discipline.
- **Sprawl** — a skill simply too long, even when every line is live and unique. Hurts readability and maintainability and wastes tokens. The cure is the ladder: disclose **reference** behind pointers, and split by **branch** or sequence so each path carries only what it needs.
- **No-op** — a line the model already obeys by default, so you pay load to say nothing. The test: does it change behaviour versus the default? A weak leading word (_be thorough_ when the agent is already thorough-ish) is a no-op; the fix is a stronger word (_relentless_), not a different technique.
- **Negation** — steering by prohibition backfires: _don't think of an elephant_ names the elephant and makes it more available, not less. Prompt the **positive** — state the target behaviour so the banned one is never spoken; keep a prohibition only as a hard guardrail you can't phrase positively, and even then pair it with what to do instead.
```

