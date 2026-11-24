# Logic Prototype

A throwaway harness that lets anyone drive a state model and observe the outcomes. Use this when the question is about **business logic, state transitions, or data shape**: the kind of thing that looks reasonable on paper but only feels wrong once you push it through real cases.

In this repository, choose between two execution formats based on dependencies:
- **Shareable HTML Demo (Preferred for zero setup)**: A single self-contained HTML file that lets anyone (developer, PM, domain expert) push buttons and watch state change without compiling or installing tools.
- **Headless C# / NUnit Harness**: A lightweight `[UnitTest]` NUnit fixture under `tests/Features/<Slice>/Logic/` (compiled by [`Synthetic.LogicTests`](../../../tests/Projects/Synthetic.LogicTests/Synthetic.LogicTests.csproj)) when the prototype must call existing `Core/` or **RevitDOM** C# types directly.

## When this is the right shape

- "I'm not sure if this state machine handles the edge case where X then Y."
- "Does this data model actually let me represent the case where..."
- "I want to feel out what the API should look like before writing it."
- Anything where someone wants to **trigger transitions and watch state change**.

If the question is "what should this look like," this is the wrong branch. Use [UI.md](UI.md).

## Process: Shareable HTML Demo

### 1. State the question

Before writing code, write down what state model and what question you're prototyping. One paragraph, at the top of the demo (in a visible intro, not just a comment). A logic prototype that answers the wrong question is pure waste.

### 2. Isolate the logic in a portable module

Put the actual logic in a single `<script>` block written as a small, pure module that could be lifted out and translated to C# later. Keep it pure: no DOM, no `document`, no button handlers reaching inside it. The page calls into it; nothing flows the other direction.

The right shape depends on the question:
- **A pure reducer**: `(state, action) => state`.
- **A state machine**: explicit states and transitions.
- **A set of pure functions** over a plain data type.

### 3. Build the shareable HTML file

One file, plain HTML/CSS/JS: no framework, no bundler, no server, double-clickable from Windows Explorer:

1. **Title and one-line explanation** of what this demo lets you explore.
2. **Current state panel**: the full relevant state rendered as readable fields, re-rendered after every click.
3. **Free-play buttons**: one button per action, always available, so anyone can poke at the model in any order.
4. **Guided walkthroughs**: a set of **scenarios**, one per tab. Each tab holds a plain-language description of the scenario and the ordered buttons to press. Starting a walkthrough resets to a known initial state.

---

## Process: Headless C# Harness (`Synthetic.Core` / `RevitDOM`)

When the logic must directly exercise existing C# domain classes (`Synthetic.Core`, **RevitDOM** POCO models):

1. **Create a temporary `[UnitTest]` fixture**: Add a single `[TestFixture]` `[UnitTest]` class in `tests/Features/<Slice>/Logic/` (e.g. `Prototype_ParameterResolution_Tests.cs`) so partition rules stay green while experimenting, then lift or move it onto `prototype/<name>` when done.
2. **Isolate domain state**: Keep tests completely headless—interact only with `Core/` modules and **RevitDOM** POCOs with zero `Autodesk.Revit.DB` dependencies.
3. **Print state transitions**: Use `TestContext.WriteLine` to dump the state after each transition so the execution trace is visible.
4. **Run quickly**: Execute with `python .agents/skills/revit-tests/scripts/test_executor.py --version Logic --filter <FixtureName>`.

---

## Hand it over and capture

1. **Review outcomes**: Present the HTML demo or test run output to the user. Look for unexpected states or edge-case breakdowns.
2. **Capture**: Once the question is answered, lift the validated logic into production code. Move the prototype artifact onto a throwaway git branch (`prototype/<name>`) as a primary source, and note the verdict in the implementation issue or spec.

## Anti-patterns

- **Keep logic prototypes headless**: Exercise pure `Core/` models without Revit `Document` fixtures; live `Document` workflows belong in **Revit Tests**.
- **Skip production ceremony**: Omit exhaustive error hierarchies, logging frameworks, or speculative abstractions.
- **Separate logic from presentation**: Keep the HTML logic module distinct from UI buttons, and keep C# `Core/` logic distinct from `Addin/Views/` WPF XAML.
