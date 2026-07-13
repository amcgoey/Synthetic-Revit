### File: .agents/agent-settings.json
```json
{
  "agent_profile": {
    "name": "Revit Agentic Coder",
    "role": "Senior C# Revit Addon Developer",
    "description": "Tactical implementation specialist focused on high-performance Revit C# execution, strict version compatibility, and codebase integrity.",
    "core_directives": [
      "You operate within an Antigravity 2.0 architecture. Do not hallucinate operational logic; rely exclusively on your provided Plugins (e.g., RevitArchitectureGuard, RevitQualityAssurance) for database rules, automated testing, and version matrix checking.",
      "Strictly enforce the Revit 2022-2027 multi-version deprecation matrix natively during ingestion.",
      "Never utilize legacy DisplayUnitType or raw .ParameterType evaluations. You must use ForgeTypeId and Definition.GetDataType() for all unit and parameter architecture in modern environments.",
      "Ensure modern .NET 8.0 (Core) execution for Revit 2025-2027 while explicitly quarantining legacy .NET Framework 4.8 code within explicit preprocessor conditionals.",
      "Always use relative links/paths for files in repository documentation (e.g., change_log.md, project_atlas.md) and never leakage absolute local file:/// paths."
    ]
  }
}
```

### File: .agents/AGENTS.md
```markdown
# Project Rules

## Documentation Links
- **Relative Links:** Always use relative links (relative to the repository root or the document's parent directory) for all markdown files (`.md`) inside the repository. Never use absolute `file:///` URLs or local user paths (such as `C:\Users\...`).
```

### File: .agents/workflows.json
```json
{
  "workflows": [
    {
      "name": "finalize-feature",
      "trigger": "/finalize",
      "description": "Updates change_log.md and project_atlas.md, checks version matrix, regenerates API docs, orchestrates automated testing, and syncs all documentation to Google Drive via sync_all.py.",
      "steps": [
        {
          "action": "agent.execute_task",
          "instruction": "Invoke the RevitQualityAssurance plugin to run the final regression test suite on the active branch."
        },
        {
          "action": "agent.change_state",
          "instruction": "Transition out of Active Coding. You are now authorized to read/write system configuration assets and documentation."
        },
        {
          "action": "agent.execute_task",
          "instruction": "Open docs/change_log.md and append a new dated entry summarising the features, fixes, and refactors completed in this feature branch. Use the git log since the last merge into develop as your source of truth. Follow the existing format and heading style already present in the file."
        },
        {
          "action": "agent.execute_task",
          "instruction": "Scan the current C# source directory. Extract all active Revit API namespaces, key Command/Application classes, and the file-structure registry. Compile this data concisely into docs/project_atlas.md as mapped by our atlas_policy.md rule."
        },
        {
          "action": "agent.execute_task",
          "instruction": "Audit the .csproj files against the CURRENT_LATEST_VERSION environment variable managed by the RevitWorkspaceDevOps plugin. Prompt user if a newer target exists."
        },
        {
          "action": "terminal.run_command",
          "description": "Regenerate SyntheticShared_API_Documentation.md from all triple-slash XML doc comments across the src/ directory.",
          "command": "python .agents/plugins/RevitWorkspaceDevOps/scripts/generate_docs.py"
        },
        {
          "action": "terminal.run_command",
          "command": "git add docs/project_atlas.md *.md && git commit -m 'docs: update project_atlas and sync compiled documentation for completed feature branch'"
        },
        {
          "action": "terminal.run_command",
          "description": "Rebuild all source bundle markdown files (Phase 1) then push all documentation to Google Drive (Phase 2). aggregate.py is invoked automatically by sync_all.py.",
          "command": "python .agents/plugins/RevitWorkspaceDevOps/scripts/sync_all.py"
        },
        {
          "action": "agent.output_message",
          "message": "### Feature Branch Finalized Successfully!\n* `change_log.md` updated with feature branch summary.\n* `project_atlas.md` updated with latest architecture.\n* Multi-version compiler targets verified.\n* `SyntheticShared_API_Documentation.md` regenerated from source.\n* Watchdog regression testing returned a clean pass state.\n* All documentation bundles aggregated and pushed to Google Drive."
        }
      ]
    },
    {
      "name": "sync-to-drive",
      "trigger": "/sync",
      "description": "Rebuilds all source bundle markdown files via aggregate.py then pushes all configured sync targets to Google Drive via sync_all.py.",
      "steps": [
        {
          "action": "terminal.run_command",
          "description": "Rebuild all source bundle markdown files (Phase 1) then push all documentation to Google Drive (Phase 2). aggregate.py is invoked automatically by sync_all.py.",
          "command": "python .agents/plugins/RevitWorkspaceDevOps/scripts/sync_all.py"
        },
        {
          "action": "agent.output_message",
          "message": "### Drive Sync Complete!\n* Source bundles regenerated (aggregate.py ran as Phase 1 of sync_all.py).\n* All configured sync targets pushed to Google Drive."
        }
      ]
    },
    {
      "name": "pull-docs",
      "trigger": "/pull",
      "description": "Pulls configured Google Docs down as local markdown files, optionally filtering for specific target folders (e.g. adrs, prds).",
      "steps": [
        {
          "action": "terminal.run_command",
          "description": "Pulls Google Docs from Drive and converts them to Markdown.",
          "command": "python .agents/plugins/RevitWorkspaceDevOps/scripts/pull_docs.py"
        },
        {
          "action": "agent.output_message",
          "message": "### Google Docs Pull Complete!\n* Google Docs successfully pulled and converted to local markdown.\n* Cleaned out target folder(s) first to prevent stale assets."
        }
      ]
    }
  ]
}
```

### File: .agents/plugins/RevitArchitectureGuard/plugin.json
```json
{
  "plugin_id": "revit_architecture_guard",
  "name": "RevitArchitectureGuard",
  "version": "2.0.0",
  "description": "Enforces high-performance database filtering, explicit JSON-in-Schema extensible storage structures, and pattern-based transaction batching.",

  "lifecycle_hooks": [
    {
      "stage": "before_file_write",
      "action": "verify_database_safety",
      "description": "Audits proposed C# modifications against the CollectorOptimizer hierarchy and transaction performance constraints."
    }
  ]
}
```

### File: .agents/plugins/RevitArchitectureGuard/SKILL.md
```markdown
---
name: revit-architecture-guard-intelligence
description: Teaches the agent structural design patterns for high-performance transactions, explicit multi-schema JSON storage, and optimized element querying.
---

# RevitArchitectureGuard Architectural Playbook

You are strictly forbidden from writing rigid database boilerplate. You must apply the following architectural design patterns conceptually, adapting the structure to fit the unique context of the target file.

## 1. Schema Sovereignty, Algorithmic GUIDs, & Progressive Purge Migrations
You do not possess the authority to alter data persistence boundaries without direct alignment. You must strictly enforce the single-field JSON payload standard while executing algorithmic identification and progressive data purges.

### Decision, Generation, & Migration Boundaries:
* **The Structural Constant:** Every Extensible Storage schema must utilize exactly one `String` field named `JsonData` to house serialized configuration objects.
* **The Algorithmic GUID Generation Rule:** When creating a new schema or initiating a structural data layout revision, you must calculate a completely unique GUID. Generate this value by hashing the combination of the target **Class Name** and a **Current Universal Timestamp** (formatted exactly as `ClassName_YYYYMMDD_HHMMSS`). Convert this hash into a standard 128-bit Guid structure.
* **The Static Persistence Rule:** Once this GUID is calculated, you must write it into the C# source file as a hardcoded `static readonly Guid` or `const string` field. It must remain constant across active Revit user sessions so data can be reliably retrieved.
* **The Revision Mandate:** If a schema undergoes structural transformations or field evolutions requiring a fresh database partition, you must execute a brand-new generation loop using a fresh universal timestamp formatted exactly as `ClassName_YYYYMMDD_HHMMSS` to produce an entirely new, non-colliding GUID constant.
* **The Consultation Threshold:** If a feature requires storing data, you must analyze if an existing schema can safely accept the payload properties. If the choice between extending an existing schema or generating a new separate schema is ambiguous, you must stop and present the architectural pros and cons to the user for explicit direction.
* **The Progressive Migration Mandate:** You are strictly forbidden from implementing automatic multi-GUID fallback arrays during routine data retrieval. Instead, when a schema is revised and a new GUID is introduced, you must construct an explicit **Progressive Migration Runner**. 
* **The Step-by-Step Chain:** The runner must contain isolated transformation blocks mapping your evolving data schema sequentially (e.g., Schema_V1 -> Schema_V2 -> Schema_V3). It must open the old schema entity, extract the legacy JSON string, pass it through the progressive version modifiers to hydrate missing fields or map altered properties, and write the finalized payload to the newest active schema.
* **The Active Purge Protocol:** To prevent database bloat and eliminate abandoned data clutter, the migration runner must execute an explicit database cleanup immediately following a successful data transfer. You must programmatically invoke `Schema.EraseSchemaAndAllEntities()` (or target specific elements to delete the legacy `Entity` allocation) to completely erase the historical schema footprint from the active Revit project file.

## 2. The Assimilated Transaction Group Pattern (Performance & Quality)
Do not inject micro-transactions inside iterative loops. You must batch database alterations to preserve Revit's memory stability and graphic processing speed.

### Pattern Principles:
* **Elevation of Scope:** Position the overarching `TransactionGroup` allocation at the highest logical tier of the execution call stack possible (e.g., outside execution loops, commanding UI handlers, or batch processors).
* **The Aggregation Pattern:** Utilize a `TransactionGroup` to wrap complex, multi-phase operations. Use isolated standard `Transaction` blocks within the group to process logical element batches cleanly.
* **The Partial-Commit Rule:** If an intermediate action fails within a batched loop, do not execute a nuclear rollback of the entire session unless it is a fatal fallback requirement specified in the feature design. Instead, design the execution block to attempt to `.Commit()` the valid individual transactions, capture the specific element IDs that encountered errors, and pass a granular data packet containing the failures to the `RevitRuntimeWatchdog` triage stream.
* **The Assimilation Step:** Upon total or partial success of the batch operations, invoke `tg.Assimilate()` to collapse the completed transaction collection into a single, clean user-facing entry in Revit's native Undo stack.

## 3. Guarded Data Retrieval Matrix (`CollectorOptimizer`)
When querying the Revit database via a `FilteredElementCollector`, you must strictly minimize memory hydration by stacking Quick Filters ahead of Slow Filters.

### Query Ordering Protocol:
1. **Phase 1 (Native Memory Space):** Apply Class (`OfClass`) and Category (`OfCategory`) filters immediately upon collector initialization. These execute in Revit's internal C++ memory.
2. **Phase 2 (Database Iteration):** Apply parameter-driven filters (`ElementParameterFilter`) only after Phase 1 has drastically narrowed the element pool.
3. **Phase 3 (Managed Memory Hydration):** You are strictly forbidden from executing LINQ queries or `.Where()` clauses directly on an unoptimized collector stream. LINQ must only be executed at the very end of the method call chain after the element collection has been heavily restricted by native filters.
```

### File: .agents/plugins/RevitTDDWorkflow/deep-modules.md
```markdown
# **Deep Modules**

From "A Philosophy of Software Design":

**Deep module** = small interface + lots of implementation.

```

┌─────────────────────┐
│   Small Interface   │ ← Few public methods, simple C# DTO parameters
├─────────────────────┤
│                     │
│ Deep Implementation │ ← Complex Revit API filtering, geometry math, and transactions hidden
│                     │
└─────────────────────┘

```

**Shallow module** = large interface + little implementation (avoid).

```

┌─────────────────────────────────┐
│         Large Interface         │ ← Many methods, exposing raw Revit DB Elements
├─────────────────────────────────┤
│       Thin Implementation       │ ← Just passes through directly to the Revit API
└─────────────────────────────────┘

```

## **When designing interfaces for the Revit Addin, ask:**

* Can I reduce the number of public methods?  
* Can I simplify the parameters? (e.g., passing a pure C# string ID or DTO instead of a raw Revit Element or Document)  
* Can I hide more of the complex Revit database querying, transaction handling, or geometry extraction completely inside the module?
```

### File: .agents/plugins/RevitTDDWorkflow/interface-design.md
```markdown
# **Interface Design for Testability**

Good interfaces make testing natural, especially when decoupling from the slow Revit API.

## **Accept dependencies, don't create them (Dependency Injection)**

Pass external dependencies in rather than creating them internally. This makes substituting handcrafted Fakes simple.

```

// GOOD: Testable class. We can pass a FakeIElementProvider in our fast NUnit tests.
public class WallAnalyzer
{
    private readonly IElementProvider _provider;
    
    public WallAnalyzer(IElementProvider provider)
    {
        _provider = provider;
    }
    
    public int CountStructuralWalls() { /* ... */ }
}

// BAD: Hard to test. It directly instantiates a Revit FilteredElementCollector, forcing a slow Revit API boot to test basic logic.
public class WallAnalyzer
{
    public int CountStructuralWalls(Document doc)
    {
        var collector = new FilteredElementCollector(doc);
        // ...
    }
}

```

## **Return results, don't produce side effects**

When possible, calculate state changes in memory and return the result as pure data, rather than modifying the Revit Document immediately.

```

// GOOD: Testable pure function. We can test the math instantly without a Revit Transaction.
public double CalculateTotalVolume(IEnumerable<ElementData> elements)
{
    return elements.Sum(e => e.Volume);
}

// BAD: Hard to test. Mutates state and requires a live Revit Transaction just to verify the math.
public void ApplyVolumeToSharedParameter(Document doc, List<Element> elements)
{
    using var t = new Transaction(doc, "Set Vol");
    // ... math and DB mutation intertwined
}

```

## **Small surface area**

Fewer public methods \= fewer tests needed. Fewer parameters \= simpler test setup.
```

### File: .agents/plugins/RevitTDDWorkflow/mocking.md
```markdown
# **Mocking**

## **The Core Rule: Don't mock what you own.**

Mocking should be reserved strictly for architectural boundaries and external dependencies that are slow, non-deterministic, or hard to set up.

## **The Revit API as a Boundary**

Because booting Revit via `ricaun.RevitTest` is slow, the Revit API itself should be treated as an external boundary whenever possible.

* Extract data from Revit `Element` objects into pure C# Data Transfer Objects (DTOs) or primitive types.  
* Pass those pure types into your business logic managers.  
* Test your business logic using standard, lightning-fast NUnit tests.  
* Only use `ricaun.RevitTest` for the "Integration" phase to verify that your data successfully reads/writes to the actual Revit Document.

## **Good Mocking**

* Mocking at the edges: File System, External HTTP APIs, or the Revit API wrapping layer.  
* Using **Fakes** (simple, handcrafted in-memory classes that implement your interfaces) instead of complex third-party Mocking frameworks.

## **Bad Mocking**

* Mocking internal classes or collaborators just to mathematically isolate a single unit test.  
* Mocking pure functions, domain logic, or calculation engines.  
* "Mockist" TDD: Asserting that Method A called Method B. (We care about the final observable state, not the internal wiring).  
* Over-specifying mock setups, which leads to tests breaking when refactoring internal logic.

## **Examples**

### **Bad: Mocking Internal Collaborators**

```
// BAD: We own ViewAnalyzer and it contains pure business logic. 
// We shouldn't mock it to test the ViewManager. We should test the real ViewManager WITH the real ViewAnalyzer.

// BAD: Asserting on internal implementation details (verifying a method was called).
// We only care about the final observable state.
```

### **Good: Using a Handcrafted Fake for an Architectural Boundary**

```
// GOOD: IFileExportService represents a boundary to the OS file system.
// We handcraft a simple "Fake" so our automated tests don't write actual files to disk and remain fast.

// 1. Handcraft a Fake
public class FakeFileExporter : IFileExportService 
{
    public string ExportedPath { get; private set; }
    public string ExportedPayload { get; private set; }

    public void ExportToDisk(string path, string payload) 
    {
        ExportedPath = path;
        ExportedPayload = payload;
    }
}

// 2. Test using the Fake
[Test]
public void ReportCommand_GeneratesCorrectPayload()
{
    // Arrange
    var fakeExporter = new FakeFileExporter();
    var reportCommand = new ReportCommand(fakeExporter);

    // Act using real internal logic
    reportCommand.Execute(new List<string> { "Data1", "Data2" });

    // Assert that the system correctly crossed the boundary with the right payload
    Assert.That(fakeExporter.ExportedPayload, Is.EqualTo("Expected_Payload_String"));
}
```
```

### File: .agents/plugins/RevitTDDWorkflow/plugin.json
```json
{
  "plugin_id": "revit_tdd_workflow",
  "name": "RevitTDDWorkflow",
  "version": "2.0.0",
  "description": "Unified authority for Test-Driven Development, behavior-based testing, and self-healing telemetry resolution in C# Revit Addins.",
  "commands": [
    {
      "trigger": "/run-tests",
      "description": "Orchestrates automated testing. Usage: /run-tests [version] (e.g., 2026) [class] (optional)"
    },
    {
      "trigger": "/analyze-journal",
      "description": "Analyze Revit journal for errors, warnings, crashes, and exceptions. Usage: /analyze-journal [version] (e.g. 2026)"
    }
  ]
}
```

### File: .agents/plugins/RevitTDDWorkflow/refactoring.md
```markdown
# **Refactoring**

## **The Golden Rule: Never Refactor on Red**

You are strictly forbidden from refactoring code while tests are failing. Refactoring is a privilege earned by getting to Green. If a test is failing, your only job is to write the minimal, code required to make it pass.

## **What is Refactoring?**

Refactoring changes **how** the code does something (its internal structure) without changing **what** it does (its observable behavior).

* Extracting a complex block of code into a private C# method.  
* Renaming variables, classes, or interfaces for clarity.  
* Consolidating duplicated logic.  
* Applying SOLID principles to decouple classes.

## **The Process**

1. Run the test suite. Ensure everything is **Green**.  
2. Make **one** small structural change.  
3. Run the test suite again.  
4. If it remains **Green**, commit the change (or proceed to the next small step).

## **Handling Failure During Refactoring**

If a test turns **Red** while you are refactoring:

* **DO NOT** fix the test to match your new code.  
* **DO NOT** write new production code to force the test to pass.  
* **DO** immediately revert your refactoring step (undo) to get back to Green.  
* Re-evaluate your refactoring strategy and take a smaller step.

### **The Exception: Bad Tests**

The only exception to the reversion rule is if you discover a test was poorly written and coupled to internal implementation details (e.g., asserting that a specific private method was called). In this case, the *test itself* is the problem. You must rewrite the test to focus on public observable behavior, verify it passes against the old code, and *then* resume your refactoring.
```

### File: .agents/plugins/RevitTDDWorkflow/SKILL.md
```markdown
—-

name: revit-tdd-workflow-intelligence

description: Unified authority for Test-Driven Development (Red-Green-Refactor), strict behavior-based testing, and self-healing telemetry resolution in C# Revit Addins.

—-

# **RevitTDDWorkflow Blueprint**

You are the unified Test-Driven Development and Quality Assurance authority. Your responsibility is to strictly enforce vertical-slice TDD methodologies, execute automated tests, and trigger self-healing loops based on test telemetry.

## **1. Core TDD Philosophy (Strictly Enforced)**

* **Verify Behavior, Not Implementation:** Tests must verify behavior through public interfaces, not internal C# implementation details. Code can change entirely; tests shouldn't.  
* **No Horizontal Slices:** You are strictly forbidden from writing all tests first, then all implementations. You must use "Vertical Slices." One test → one implementation → repeat.  
* **Bad Tests:** Do not write tests that couple to implementation. Do not mock internal collaborators or test private methods unless dictated explicitly by the Development Brief. The warning sign: if a test breaks when you refactor but the behavior hasn't changed, it's a bad test.  
* **Minimal Code:** Only write enough code to pass the current test. Do not anticipate future tests or add speculative features.  
* **Never Refactor on Red:** Get the codebase to Green first.

## **2. Authority and Testing Scope**

* **Strategic Authority:** The Gemini Development Brief is the authority for strategic testing boundaries. Read the "Testing & Seams" section to determine *which* Revit API boundaries to mock and *which* test layers to prioritize.  
* **Focus Testing:** Confirm with the user exactly which behaviors matter most. Focus on critical paths and complex logic, not every possible edge case.

## **3. The TDD Execution State Machine**

You must operate using the following execution states natively integrated into your standard Plan-and-Execute workflow:

### **Phase 1: Planning & The Tracer Bullet (Consent Gate)**

During your typical implementation planning phase:

1. Read the Development Brief and analyze the target C# codebase.
2. Retrive the `CURRENT_LATEST_VERSION` environment variable to determine the latest version of Revit.
2. If a testing strategy isn't included in the brief or user instructions, develop one, otherwise differ to the brief.
2. Propose a "Tracer Bullet" test—the absolute first test that confirms the primary end-to-end path works.  
3. List the remaining behaviors to test sequentially based on the brief.  
4. Include the list of proposed tests in the typical implementation plan document for the user to review.

### **Phase 2: The Red-Green-Refactor Loop (Incremental)**

Once the user approves the plan, execute strictly one cycle at a time governed by `tests.md`, `mocking.md`,  refactoring.md`, `interface-design.md`, and `deep-modules.md`:

* **RED:** Write ONE test for the first behavior. Run `test_executor.py` on the logic tests and latest version of Revit. Confirm the test fails.  
* **GREEN:** Write the absolute minimal C# code to pass the test. Run `test_executor.py` on the logic tests and latest version of Revit. Confirm the test passes.  
* **REFACTOR:** Once Green, apply SOLID principles, extract duplication, and deepen modules. Run tests after each step to ensure they remain Green.  
* **REPEAT:** Move to the next test in the approved list.

## **4. Code Comment Tagging & Reversion Rules**

Every non-production validation test, mock parameter injection, or diagnostic utility line must be encapsulated inside explicit comment anchors.

* **Pattern A (Pure Additions):** Wrap in `// [AG2_TEST_START: FeatureName]` and `// [AG2_TEST_END: FeatureName]`. Add `// REVERT_METHOD: To remove, safely delete this entire block.`  
* **Pattern B (Overrides):** Wrap in the same tags, but provide the original code block commented out inside a `/* ORIGINAL_CODE: ... */` block.

## **5. Dynamic Validation Gate & Telemetry**

1. Before compiling any journal string, extract the C# assembly root namespace from `docs/project_atlas.md`.  
2. Execute the test suite strictly by running the python script located at `.agents/plugins/RevitTDDWorkflow/scripts/test_executor.py`.  
3. **Telemetry Ingestion:** Read the Markdown summary. If tests fail, process the exact NUnit error messages and stack traces to locate bugs and self-heal before proceeding to Refactor or the next loop.
```

### File: .agents/plugins/RevitTDDWorkflow/tests.md
```markdown
# **Tests**

## **Good Tests**

* Tests behavior users/callers care about.  
* Uses public API only.  
* Survives internal refactors.  
* Describes WHAT, not HOW.  
* One logical assertion per test.

### **Integration-style**

Test through real interfaces, not mocks of internal parts.

```

// GOOD: Tests observable behavior through the public manager
[Test]
public void RenameViews_WithValidPrefix_UpdatesViewNames()
{
    // Arrange
    var viewManager = new ViewManager(Document);
    var viewsToRename = new List<View> { view1, view2 };

    // Act
    viewManager.ApplyPrefix(viewsToRename, "EXT_");

    // Assert
    // We only care that the final state is correct, not how the strings were combined internally.
    Assert.That(view1.Name, Does.StartWith("EXT_"));
    Assert.That(view2.Name, Does.StartWith("EXT_"));
}

```

## **Bad Tests**

Implementation-detail tests: Coupled to internal structure.

```

// BAD: Tests implementation details and mocks internal collaborators
[Test]
public void ApplyPrefix_CallsInternalStringFormatter()
{
    // Arrange
    var mockFormatter = new Mock<IStringFormatter>();
    var viewManager = new ViewManager(Document, mockFormatter.Object);
    
    // Act
    viewManager.ApplyPrefix(viewsToRename, "EXT_");

    // Assert
    // RED FLAG: Fails if we later refactor to remove IStringFormatter and just do formatting inline!
    mockFormatter.Verify(f => f.Format("EXT_", It.IsAny<string>()), Times.Exactly(2)); 
}

```

### **Red flags**

* Mocking internal collaborators.  
* Testing private methods.  
* Asserting on call counts/order.  
* Test breaks when refactoring without behavior change.  
* Test name describes HOW not WHAT.  
* Verifying through external means instead of interface.

```

// BAD: Bypasses interface to verify (e.g., querying raw Extensible Storage instead of the manager)
[Test]
public void SaveConfig_WritesToExtensibleStorage()
{
    var config = new ProjectConfig { Prefix = "INT_" };
    ConfigManager.Save(config, Document);
    
    // Bypassing the public interface to verify internal database state
    var entity = Document.ProjectInformation.GetEntity(Schema);
    Assert.That(entity.IsValid(), Is.True);
}

// GOOD: Verifies through the public interface
[Test]
public void SaveConfig_MakesConfigRetrievable()
{
    var config = new ProjectConfig { Prefix = "INT_" };
    ConfigManager.Save(config, Document);
    
    // Retrieving through the same public interface
    var retrieved = ConfigManager.Load(Document);
    Assert.That(retrieved.Prefix, Is.EqualTo("INT_"));
}

```
```

### File: .agents/plugins/RevitTDDWorkflow/scripts/addin_isolator.py
```python
import os

def get_addin_paths(version):
    """
    Computes standard Revit add-in paths for a given version.
    """
    programdata = os.environ.get("ProgramData", r"C:\ProgramData")
    appdata = os.environ.get("AppData", os.path.expanduser("~\\AppData\\Roaming"))
    
    return [
        os.path.join(programdata, "Autodesk", "Revit", "Addins", version),
        os.path.join(appdata, "Autodesk", "Revit", "Addins", version)
    ]

def rename_non_essential_addins(version):
    """
    Renames non-essential third-party add-ins to .bak to avoid loading them.
    Returns a list of tuples containing (original_path, new_path) of renamed files.
    """
    renamed_files = []
    paths = get_addin_paths(version)
    
    for path in paths:
        if not os.path.exists(path):
            continue
        try:
            for file in os.listdir(path):
                if file.endswith(".addin"):
                    lower_file = file.lower()
                    if "synthetic" in lower_file or "revittest" in lower_file or "ricaun" in lower_file:
                        continue
                    
                    full_path = os.path.join(path, file)
                    new_path = full_path + ".bak"
                    try:
                        if os.path.exists(new_path):
                            os.remove(new_path)
                        os.rename(full_path, new_path)
                        renamed_files.append((full_path, new_path))
                    except Exception:
                        # If rename fails (e.g. read-only ProgramData), we'll rely on whitelisting
                        pass
        except Exception:
            pass
            
    return renamed_files

def restore_renamed_addins(renamed_files):
    """
    Restores previously renamed .bak add-in files to their original paths.
    """
    for full_path, new_path in renamed_files:
        try:
            if os.path.exists(new_path):
                if os.path.exists(full_path):
                    os.remove(full_path)
                os.rename(new_path, full_path)
        except Exception as e:
            print(f"[ERROR] Failed to restore addin from '{new_path}': {e}")
```

### File: .agents/plugins/RevitTDDWorkflow/scripts/guid_whitelister.py
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

### File: .agents/plugins/RevitTDDWorkflow/scripts/revit_journal_tool.py
```python
import os
import sys
import argparse
import re

def get_latest_journal(version="2026"):
    """
    Locates the most recently modified Revit journal file in the local AppData directory.
    """
    journal_dir = os.path.expandvars(f"%LOCALAPPDATA%\\Autodesk\\Revit\\Autodesk Revit {version}\\Journals")
    if not os.path.exists(journal_dir):
        print(f"[-] Directory does not exist: {journal_dir}")
        return None
    files = [os.path.join(journal_dir, f) for f in os.listdir(journal_dir) if f.endswith(".txt")]
    if not files:
        print(f"[-] No journal files (.txt) found in: {journal_dir}")
        return None
    latest = max(files, key=os.path.getmtime)
    return latest

def read_journal_lines(filepath):
    """
    Safely reads lines from a journal file, trying various common Revit journal encodings.
    """
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

def tail_journal(lines, num_lines):
    """
    Returns the last N lines of the journal.
    """
    start = max(0, len(lines) - num_lines)
    return lines[start:], start

def search_journal(lines, query, context_lines=15):
    """
    Searches the journal lines for a specific term and returns matches with context.
    """
    query_lower = query.lower()
    matches = []
    for idx, line in enumerate(lines):
        if query_lower in line.lower():
            matches.append(idx)
    return matches

def analyze_journal(lines, filepath):
    """
    Performs comprehensive diagnostic analysis on journal lines to locate crashes,
    exceptions, memory growth, and termination status.
    """
    analysis = []
    analysis.append("=== DIAGNOSTIC ANALYSIS ===")
    
    # 1. Look for Managed Exceptions / Stack Traces
    exceptions = []
    stack_trace_trigger = False
    current_exception = []
    
    # Exceptions often contain System.Exception, Exception, or stack trace patterns like "at System." or "at Synthetic."
    for idx, line in enumerate(lines):
        line_clean = line.strip()
        # Detect C# exception stack trace patterns or explicit Exception type throws
        if "exception" in line_clean.lower() or "at " in line_clean and re.search(r'\b[A-Za-z0-9_]+\.[A-Za-z0-9_.]+\(', line_clean):
            if not stack_trace_trigger:
                stack_trace_trigger = True
                current_exception = [f"Line {idx+1}: {line_clean}"]
            else:
                current_exception.append(line_clean)
        else:
            if stack_trace_trigger:
                exceptions.append("\n".join(current_exception))
                stack_trace_trigger = False
                current_exception = []
                
    if stack_trace_trigger and current_exception:
        exceptions.append("\n".join(current_exception))
        
    analysis.append(f"Managed Exceptions/Traces Found: {len(exceptions)}")
    for exc in exceptions[:10]: # Print first 10
        analysis.append("-" * 40)
        analysis.append(exc)
    if len(exceptions) > 10:
        analysis.append(f"... and {len(exceptions) - 10} more exceptions.")
        
    # 2. Look for DBG_WARN or DBG_INFO with errors
    dbg_warnings = []
    for idx, line in enumerate(lines):
        if "DBG_WARN" in line or "DBG_INFO" in line or "error" in line.lower() or "fail" in line.lower():
            # Exclude some very common harmless lines to avoid noise
            if not any(x in line for x in ["API_SUCCESS", "GetPreferences", "LoadLatestEpoch"]):
                dbg_warnings.append((idx+1, line.strip()))
                
    analysis.append(f"\nPotential Error/Warning Logs Found: {len(dbg_warnings)}")
    for line_num, warning in dbg_warnings[:15]:
        analysis.append(f"  Line {line_num}: {warning}")
    if len(dbg_warnings) > 15:
        analysis.append(f"... and {len(dbg_warnings) - 15} more warnings.")

    # 3. Look for Memory Stats and Growth
    # Revit prints RAM Statistics periodically: e.g. "RAM Statistics:    40348 /    65461      2739=InUse     2992=Peak"
    mem_stats = []
    for idx, line in enumerate(lines):
        if "RAM Statistics:" in line or "Delta VM:" in line:
            mem_stats.append((idx+1, line.strip()))
            
    analysis.append(f"\nMemory Logs Found: {len(mem_stats)}")
    # Print the first few and the last few memory logs to show growth
    if len(mem_stats) > 0:
        analysis.append("Initial Memory Logs:")
        for line_num, stat in mem_stats[:3]:
            analysis.append(f"  Line {line_num}: {stat}")
        if len(mem_stats) > 6:
            analysis.append("...")
            analysis.append("Latest Memory Logs:")
            for line_num, stat in mem_stats[-3:]:
                analysis.append(f"  Line {line_num}: {stat}")
        elif len(mem_stats) > 3:
            for line_num, stat in mem_stats[3:]:
                analysis.append(f"  Line {line_num}: {stat}")
                
    # 4. Determine Shutdown Cleanliness
    # The ricaun.RevitTest adapter closes Revit programmatically — no ribbon Quit is recorded.
    # Instead look for crash indicators:
    #   a) An abrupt end mid-transaction (no 'Destroy Display Manager' in last 200 lines)
    #   b) A matching .dmp file in the Journals directory
    last_200 = lines[-200:] if len(lines) >= 200 else lines
    
    destroy_display_found = any("Destroy Display Manager" in ln for ln in last_200)
    api_success_unregister = any("API_SUCCESS" in ln and "Unregistering" in ln for ln in last_200)
    
    # Check for a crash dump file matching this journal
    crash_detected = False
    journal_dir = os.path.dirname(filepath)
    journal_basename = os.path.splitext(os.path.basename(filepath))[0]  # e.g. "journal.0388"
    dmp_pattern = os.path.join(journal_dir, journal_basename + "*.dmp")
    import glob as _glob
    dmp_files = _glob.glob(dmp_pattern)
    if dmp_files:
        crash_detected = True
        analysis.append(f"\n[CRITICAL] Crash dump file(s) found for this journal session:")
        for d in dmp_files:
            analysis.append(f"  {d}")
    
    if destroy_display_found and not crash_detected:
        analysis.append(f"\n[OK] Shutdown appears CLEAN: 'Destroy Display Manager' found in last 200 lines.")
        if api_success_unregister:
            analysis.append("  Addins unregistered cleanly (API_SUCCESS Unregistering events found).")
    elif crash_detected:
        analysis.append(f"\n[CRASH] Shutdown was ABNORMAL: crash dump file detected.")
    else:
        analysis.append("\n[WARNING] Shutdown status UNCERTAIN: No 'Destroy Display Manager' and no crash dump.")
        analysis.append("  Revit may have been killed externally (e.g. taskkill) or the journal is still open.")
    
    return "\n".join(analysis)

def main():
    parser = argparse.ArgumentParser(description="Revit Journal Tailing and Search Utility")
    parser.add_argument("--version", default="2026", help="Revit version folder to target (default: 2026)")
    parser.add_argument("--tail", type=int, default=100, help="Number of lines to tail from the end of the journal")
    parser.add_argument("--search", help="Search query (case-insensitive) to run against the journal")
    parser.add_argument("--context", type=int, default=15, help="Number of lines of context around search matches")
    parser.add_argument("--analyze", action="store_true", help="Perform comprehensive diagnostics analysis on the latest journal")
    parser.add_argument("--output", help="Path to write the results (if omitted, writes to stdout)")

    args = parser.parse_args()

    latest_journal = get_latest_journal(args.version)
    if not latest_journal:
        sys.exit(1)

    lines, encoding = read_journal_lines(latest_journal)
    
    results = []
    results.append(f"Journal File: {latest_journal}")
    results.append(f"Encoding:     {encoding}")
    results.append(f"Total Lines:  {len(lines)}")
    results.append("=" * 60)

    if args.analyze:
        analysis_text = analyze_journal(lines, latest_journal)
        results.append(analysis_text)
    elif args.search:
        matches = search_journal(lines, args.search, args.context)
        results.append(f"Search Term:  '{args.search}'")
        results.append(f"Matches:      {len(matches)} found\n")
        
        for idx in matches:
            results.append(f"--- Match at Line {idx+1} ---")
            start = max(0, idx - args.context)
            end = min(len(lines), idx + args.context + 1)
            for i in range(start, end):
                prefix = ">>>" if i == idx else "   "
                results.append(f"{prefix} {i+1:5d}: {lines[i].rstrip()}")
            results.append("-" * 60)
    else:
        results.append(f"Tailing last {args.tail} lines:\n")
        tail_lines, start_idx = tail_journal(lines, args.tail)
        for idx, line in enumerate(tail_lines):
            results.append(f" {start_idx + idx + 1:5d}: {line.rstrip()}")

    output_text = "\n".join(results)
    
    if args.output:
        try:
            with open(args.output, "w", encoding="utf-8") as f:
                f.write(output_text)
            print(f"[+] Results written to: {args.output}")
        except Exception as e:
            print(f"[-] Failed to write output file: {e}")
            print(output_text)
    else:
        print(output_text)

if __name__ == "__main__":
    main()
```

### File: .agents/plugins/RevitTDDWorkflow/scripts/test_executor.py
```python
import os
import sys
import subprocess
import glob
import xml.etree.ElementTree as ET
import re
import winreg

# Import helper scripts for isolating Revit environment configurations
import addin_isolator
import guid_whitelister

class TempConfigureRevitAddins:
    def __init__(self, version):
        self.version = version
        self.renamed_files = []
        self.added_guids = []
        
    def whitelist_existing_addins(self):
        # To restore registry whitelisting, uncomment the lines below:
        # new_guids = guid_whitelister.whitelist_existing_addins(self.version)
        # self.added_guids.extend(new_guids)
        pass
        
    def __enter__(self):
        # To restore temporary renaming of non-essential addins, uncomment the lines below:
        # self.renamed_files = addin_isolator.rename_non_essential_addins(self.version)
        
        # To restore registry whitelisting on enter, uncomment the lines below:
        # self.whitelist_existing_addins()
        return self
        
    def __exit__(self, exc_type, exc_val, exc_tb):
        # To restore backup addins, uncomment the lines below:
        # if self.renamed_files:
        #     addin_isolator.restore_renamed_addins(self.renamed_files)
        
        # To restore registry whitelisting cleanup, uncomment the lines below:
        # if self.added_guids:
        #     guid_whitelister.cleanup_whitelisted_registry_entries(self.version, self.added_guids)
        pass

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

### File: .agents/plugins/RevitUIRibbonManager/plugin.json
```json
{
  "plugin_id": "revit_ui_ribbon_manager",
  "name": "RevitUIRibbonManager",
  "version": "1.0.0",
  "description": "Dedicated front-end architecture guardrail. Manages the single-source JSON schema layout for Revit UI tabs, panels, and pushbuttons."

}
```

### File: .agents/plugins/RevitUIRibbonManager/SKILL.md
```markdown
---
name: revit-ui-ribbon-intelligence
description: Governs the unified UI ribbon mapping and JSON schema generation for Revit front-end architecture.
---

# RevitUIRibbonManager Architectural Playbook

You must strictly follow these rules to maintain a stable, single-source front-end architecture.

## 1. Unified UI Ribbon Mapping (`RibbonManager`)

Because user interface elements are structurally stable across all targeted release years (2022-2027), you must enforce a flat, single-source JSON schema layout for all UI configurations. 

* **No Conditional UI Branches:** You are strictly forbidden from writing version-conditional preprocessor branches (e.g., `#if REVIT2025`) inside the ribbon configuration map. 
* **Programmatic Construction:** You must parse the flat JSON template file to programmatically build tabs, panels, and pushbuttons inside the shared execution entry assembly.
* **Separation of Concerns:** Maintain a strict architectural boundary between UI rendering logic (handled here) and backend database modification rules (handled by the Extensible Storage schemas).
```

### File: .agents/plugins/RevitWorkspaceDevOps/plugin.json
```json
{
  "plugin_id": "revit_workspace_dev_ops",
  "name": "RevitWorkspaceDevOps",
  "version": "2.1.0",
  "description": "Manages inverted multi-version preprocessor conditioning, on-demand documentation compilation, codebase bundle aggregation, and Google Drive synchronization.",
  "commands": [
    {
      "trigger": "/check-version",
      "description": "Manually triggers a workspace sweep to audit .csproj version numbers against the active CURRENT_LATEST_VERSION constant."
    },
    {
      "trigger": "/sync",
      "description": "Rebuilds all source bundle markdown files via aggregate.py, then pushes all configured sync_targets to Google Drive via sync_all.py. Requires Google API credentials in the Support/auth directory. Install dependencies first: pip install -r .agents/plugins/RevitWorkspaceDevOps/requirements.txt"
    },
    {
      "trigger": "/pull [targets...]",
      "description": "Pulls configured Google Docs down as local markdown files using pull_config.json. If target names (e.g. adrs, prds) are specified, only pulls those target folders, leaving others untouched."
    }
  ],
  "lifecycle_hooks": [
    {
      "stage": "on_chat_initialization",
      "action": "audit_project_version_matrix",
      "description": "Sweeps the solution file structure upon chat boot to ensure the environment constant aligns with the highest compiled Revit version."
    }
  ],
  "environment_variables": {
    "CURRENT_LATEST_VERSION": "2026",
    "SYNC_CONFIG_PATH": "../../../Revit API Synthetic v2 Support/auth/sync_config.json"
  },
  "dependencies": {
    "note": "Google Drive sync scripts require Python packages. Run: pip install -r .agents/plugins/RevitWorkspaceDevOps/requirements.txt",
    "requirements_file": ".agents/plugins/RevitWorkspaceDevOps/requirements.txt"
  }
}
```

### File: .agents/plugins/RevitWorkspaceDevOps/SKILL.md
```markdown
---
name: revit-workspace-devops-intelligence
description: Governs inverted multi-version compilation conditioning, on-demand docstring compilation, and unified UI ribbon mapping.
---

# RevitWorkspaceDevOps Architectural Playbook

You must strictly follow these rules to maintain multi-version build stability and prevent unauthorized source refactoring.

## 1. Inverted Preprocessor Conditioning Hierarchy
You must treat the most modern Revit API structure as the unconditioned baseline. Legacy variations must be quarantined inside explicit, historical conditional blocks.

### Code Branching Principles:
* **The Default Baseline:** The latest version of Revit (defined by the `CURRENT_LATEST_VERSION` environment variable) must be written as the clean, standard, unconditioned code path (the fallback final `#else` or unconditioned block).
* **The Legacy Condition:** Prior supported releases must be wrapped inside explicitly tagged conditional compilation symbols (e.g., `#if REVIT2024` or `#elif REVIT2025`). This guarantees that dropping an old version requires only deleting its isolated block, leaving the main modern branch untouched.

```csharp
#if REVIT2022 || REVIT2023
    // Legacy Implementation: Parameter data type via deprecated ParameterType enum
    // (ParameterType was deprecated in 2022 and removed in 2024)
    ParameterType paramType = definition.ParameterType;
#elif REVIT2024
    // Transitionary Implementation: Use GetDataType() returning ForgeTypeId
    // (ParameterType fully removed; GetDataType() is now the only valid path)
    ForgeTypeId dataType = definition.GetDataType();
#else
    // DEFAULT baseline: Latest Revit API Implementation (2025+)
    // Clean, modern, unconditioned code branch.
    ForgeTypeId dataType = definition.GetDataType();
#endif
```
## 2. Discrete Version Detection & Gated Code Upgrading

You are strictly forbidden from continuously monitoring project files in the background. You must only sweep the workspace for `.csproj` changes at three specific trigger points: **Chat Initialization**, **Invocation of `/finalize`**, or **Explicit User Command (`/check-version`)**.

### Upgrade Protocol:

1. **The Audit Step:** Extract the version integer suffixes from all `.csproj` filenames within the solution. Compare the highest detected version against the active `CURRENT_LATEST_VERSION` constant.  
2. **The Prompt Block:** If a higher-version project target is discovered (e.g., `Plugin_2027.csproj` when the constant is `2026`), you must halt the active workflow and output an explicit confirmation request to the user:  
   *"Detected a new compilation target: `Plugin_2027.csproj`. Would you like to update the workspace environment constant `CURRENT_LATEST_VERSION` to 2027?"*  
3. **The Refactoring Permission Gate:** If the user approves the environment update, **do not automatically alter the C\# files**. You must explicitly request permission to refactor the preprocessor structures:  
   *"Updating baseline constant to 2027\. Would you like me to refactor our current unconditioned code paths into an explicit `#elif REVIT2026` branch to clear the default block for the new 2027 API developments?"*

## 3. Google Drive Sync Pipeline

The following scripts in `scripts/` form a two-phase documentation sync pipeline. They are invoked automatically by the `/finalize` and `/sync` workflow triggers.

### Scripts

| Script | Role |
|---|---|
| `aggregate.py` | Reads `sync_config.json` and bundles configured source directories into large markdown files in `docs/`. Preserves all comments. |
| `drive_sync.py` | Low-level Google Drive uploader. Authenticates via OAuth 2.0. Converts uploads to native Google Docs format; falls back to raw markdown on size limit errors. |
| `sync_all.py` | Orchestrator. Runs `aggregate.py` (Phase 1) then calls `drive_sync.py` for every `sync_targets` entry in config (Phase 2). |
| `pull_docs.py` | Google Drive downloader and Markdown exporter. Reads `pull_config.json`, clears local directories, downloads Google Docs, converts them to Markdown (retaining headings, formatting, lists, tables, and images), and splits them by specified headings. |

### Configuration

* **`sync_config.json`** lives at: `../Revit API Synthetic v2 Support/auth/sync_config.json`
  * `aggregations`: array of `{ source_dir, output_file }` pairs — what to bundle and where to write it
  * `sync_targets`: array of `{ local_path, drive_id }` pairs — what to push to Drive
  * `drive_folder_id`: the Google Drive folder that receives newly created files

* **`pull_config.json`** lives at: `../Revit API Synthetic v2 Support/auth/pull_config.json`
  * `pull_targets`: array of `{ drive_folder_id, local_path, split_by, include_pattern, exclude_pattern }` — what Google Drive folders and files to download, how to split, and where to save them.

### Authentication

Google OAuth credentials are stored in the sibling Support project at:
`../Revit API Synthetic v2 Support/auth/`
* `client_secret.json` — OAuth client credentials from Google Cloud Console (must be placed manually)
* `token.json` — cached OAuth token written on first authenticated run (auto-managed for uploads)
* `token_pull.json` — cached OAuth token for read-only doc pull operations (auto-managed for downloads)

### Dependencies

Python packages required. Install once:
```
pip install -r .agents/plugins/RevitWorkspaceDevOps/requirements.txt
```

### Running Manually

From the project root:
```
python .agents/plugins/RevitWorkspaceDevOps/scripts/sync_all.py
```
Or pull Google Docs down:
```
python .agents/plugins/RevitWorkspaceDevOps/scripts/pull_docs.py
```
Or aggregate only (no Drive push):
```
python .agents/plugins/RevitWorkspaceDevOps/scripts/aggregate.py
```
```

### File: .agents/plugins/RevitWorkspaceDevOps/scripts/aggregate.py
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
        projects_dir, "Revit API Synthetic v2 Support", "auth", "sync_config.json"
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

### File: .agents/plugins/RevitWorkspaceDevOps/scripts/drive_sync.py
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

    auth_dir = os.path.join(projects_dir, "Revit API Synthetic v2 Support", "auth")
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

### File: .agents/plugins/RevitWorkspaceDevOps/scripts/generate_docs.py
```python
import os
import re

def parse_cs_file(filepath):
    with open(filepath, 'r', encoding='utf-8') as f:
        content = f.read()
    
    # Find namespace
    ns_match = re.search(r'namespace\s+([\w\.]+)', content)
    namespace = ns_match.group(1) if ns_match else "Synthetic"
    
    # Class name and summary
    class_match = re.search(r'///\s*<summary>\s*\n((?:///.*\n)*)///\s*</summary>\s*\n(?:\s*\[.*\]\s*\n)*\s*(?:public|internal|private)\s+class\s+(\w+)', content)
    if class_match:
        class_summary = "".join([line.strip().replace("///", "").strip() for line in class_match.group(1).split("\n")])
        class_name = class_match.group(2)
    else:
        class_match = re.search(r'(?:public|internal|private)\s+class\s+(\w+)', content)
        class_name = class_match.group(1) if class_match else None
        class_summary = ""
        
    if not class_name:
        return None
        
    # Clean up tags in class summary
    class_summary = re.sub(r'<see\s+cref="(\w+)"\s*/>', r'`\1`', class_summary)
    class_summary = re.sub(r'<see\s+cref=".*?:(\w+)"\s*/>', r'`\1`', class_summary)
    class_summary = re.sub(r'<see\s+langword="(\w+)"\s*/>', r'`\1`', class_summary)
    class_summary = re.sub(r'\s+', ' ', class_summary).strip()

    # Split content by '/// <summary>' to parse methods/properties
    parts = content.split('/// <summary>')
    methods = []
    fields = []
    
    for part in parts[1:]:
        summary_end = part.find('</summary>')
        if summary_end == -1:
            continue
        summary_lines = part[:summary_end].split('\n')
        summary = " ".join([l.replace('///', '').strip() for l in summary_lines if l.strip()])
        
        after_summary = part[summary_end + len('</summary>'):]
        lines = [l.strip() for l in after_summary.split('\n') if l.strip() and not l.strip().startswith('///') and not l.strip().startswith('[')]
        
        sig = ""
        for line in lines:
            sig += " " + line
            if '{' in line or ';' in line:
                break
        sig = sig.strip()
        
        # If it has parentheses, it is a method/constructor
        # (Exclude attribute usage or things like that)
        if '(' in sig and not sig.startswith('class '):
            # Clean up parameters and returns if any
            param_matches = re.findall(r'<param\s+name="(\w+)"\s*>(.*?)</param\s*>', summary)
            params = []
            for p_name, p_desc in param_matches:
                params.append((p_name, re.sub(r'\s+', ' ', p_desc).strip()))
                
            return_match = re.search(r'<returns\s*>(.*?)</returns\s*>', summary)
            returns_val = return_match.group(1).strip() if return_match else ""
            
            clean_summary = re.sub(r'<param.*?>.*?</param>', '', summary)
            clean_summary = re.sub(r'<returns.*?>.*?</returns>', '', clean_summary)
            clean_summary = re.sub(r'<see\s+cref="(\w+)"\s*/>', r'`\1`', clean_summary)
            clean_summary = re.sub(r'<see\s+cref=".*?:(\w+)"\s*/>', r'`\1`', clean_summary)
            clean_summary = re.sub(r'<see\s+langword="(\w+)"\s*/>', r'`\1`', clean_summary)
            clean_summary = re.sub(r'\s+', ' ', clean_summary).strip()
            
            # Format signature
            sig_clean = sig.split('{')[0].split(';')[0].strip()
            sig_clean = re.sub(r'\s+', ' ', sig_clean)
            
            methods.append({
                'signature': sig_clean,
                'summary': clean_summary,
                'params': params,
                'returns': returns_val
            })
        elif ('{' in sig or ';' in sig) and not sig.startswith('class '):
            # Property or Field
            clean_summary = re.sub(r'<see\s+cref="(\w+)"\s*/>', r'`\1`', summary)
            clean_summary = re.sub(r'<see\s+cref=".*?:(\w+)"\s*/>', r'`\1`', clean_summary)
            clean_summary = re.sub(r'<see\s+langword="(\w+)"\s*/>', r'`\1`', clean_summary)
            clean_summary = re.sub(r'\s+', ' ', clean_summary).strip()
            
            # Extract name
            sig_clean = sig.split('=')[0].split('{')[0].split(';')[0].strip()
            name_parts = sig_clean.split()
            var_name = name_parts[-1] if name_parts else "unknown"
            
            fields.append({
                'name': var_name,
                'signature': sig.replace('{ get; set; }', '').replace('{ get; }', '').replace('{ set; }', '').split('=')[0].strip(),
                'summary': clean_summary
            })
            
    return {
        'class_name': class_name,
        'class_summary': class_summary,
        'methods': methods,
        'fields': fields,
        'namespace': namespace,
        'file_rel_path': filepath.replace('\\', '/')
    }

def generate_markdown(class_data):
    lines = []
    lines.append(f"### Class: `{class_data['class_name']}`")
    lines.append(f"**File:** [{class_data['file_rel_path']}](../{class_data['file_rel_path']})")
    lines.append("")
    if class_data['class_summary']:
        lines.append(class_data['class_summary'])
        lines.append("")
        
    if class_data['methods']:
        lines.append("#### Methods")
        for m in class_data['methods']:
            lines.append(f"##### `{m['signature'].split('(')[0].split()[-1]}`")
            lines.append("```csharp")
            lines.append(m['signature'])
            lines.append("```")
            if m['summary']:
                lines.append(m['summary'])
            if m['params'] or m['returns']:
                lines.append("")
            if m['params']:
                lines.append("**Parameters:**")
                for p_name, p_desc in m['params']:
                    lines.append(f"- `{p_name}`: {p_desc}")
            if m['returns']:
                lines.append(f"**Returns:** {m['returns']}")
            lines.append("")
            
    if class_data['fields']:
        lines.append("#### Fields")
        for f in class_data['fields']:
            lines.append(f"- **`{f['name']}`**: `{f['signature']}`")
            if f['summary']:
                lines.append(f"  *Description:* {f['summary']}")
        lines.append("")
        
    lines.append("---")
    lines.append("")
    return "\n".join(lines)

def discover_cs_files(src_root="src"):
    """
    Recursively discovers all C# source files under src_root.
    Excludes build output directories (obj/, bin/) and auto-generated
    files (*.g.cs, *.designer.cs) that contain no hand-authored docs.
    This script must be run from the project root directory so that
    src_root resolves correctly.
    """
    excluded_dirs = {"obj", "bin", ".vs"}
    excluded_suffixes = (".g.cs", ".designer.cs")
    cs_files = []
    for dirpath, dirnames, filenames in os.walk(src_root):
        # Prune excluded directories in-place to stop os.walk descending into them
        dirnames[:] = [d for d in dirnames if d.lower() not in excluded_dirs]
        for filename in filenames:
            if filename.endswith(".cs") and not filename.endswith(excluded_suffixes):
                cs_files.append(os.path.join(dirpath, filename).replace("\\", "/"))
    return cs_files


def merge_docs():
    src_root = "src"
    if not os.path.exists(src_root):
        print(f"[ERROR] Source directory not found: '{src_root}'")
        print(f"[ERROR] Ensure this script is run from the project root directory.")
        return

    cs_files = discover_cs_files(src_root)
    print(f"[INFO] Discovered {len(cs_files)} C# source files under '{src_root}/'.")

    parsed_classes = []
    skipped = 0
    for f in cs_files:
        data = parse_cs_file(f)
        if data:
            parsed_classes.append(data)
        else:
            skipped += 1

    print(f"[INFO] Parsed {len(parsed_classes)} classes ({skipped} files skipped — no class/doc comment found).")

    doc_path = "docs/SyntheticShared_API_Documentation.md"

    # Group all parsed classes by namespace
    parsed_by_ns = {}
    for c in parsed_classes:
        parsed_by_ns.setdefault(c['namespace'], []).append(c)

    if not os.path.exists(doc_path):
        # --- Fresh generation: no existing file to merge into ---
        print(f"[INFO] No existing doc file found. Generating fresh: {doc_path}")
        new_doc = "# SyntheticShared API Documentation\n\n"
        new_doc += "_Auto-generated by generate_docs.py. Do not edit namespace or class headers manually._\n\n"
        for ns_name in sorted(parsed_by_ns.keys()):
            new_doc += f"\n## Namespace: `{ns_name}`\n\n"
            for new_c in sorted(parsed_by_ns[ns_name], key=lambda x: x['class_name']):
                new_doc += generate_markdown(new_c)
        with open(doc_path, 'w', encoding='utf-8') as f:
            f.write(new_doc)
        print(f"[INFO] Fresh documentation written to {doc_path}.")
        print(f"[INFO] Namespaces written: {len(parsed_by_ns)} | Classes written: {len(parsed_classes)}")
        return

    # --- Merge mode: update/insert into the existing file ---
    with open(doc_path, 'r', encoding='utf-8') as f:
        doc_content = f.read()

    # Split the document by namespace section headers
    ns_sections = re.split(r'(## Namespace: `[\w\.]+`)', doc_content)

    # ns_sections[0] is the document header (title, intro text, etc.)
    header = ns_sections[0]
    new_doc = header

    # Track which namespaces already exist in the doc so we can append new ones later
    written_namespaces = set()

    i = 1
    while i < len(ns_sections):
        ns_header = ns_sections[i]
        ns_body = ns_sections[i + 1] if i + 1 < len(ns_sections) else ""

        ns_name_match = re.search(r'## Namespace: `([\w\.]+)`', ns_header)
        ns_name = ns_name_match.group(1) if ns_name_match else ""
        written_namespaces.add(ns_name)

        # Split this namespace body by class section headers
        class_parts = re.split(r'(### Class: `\w+`)', ns_body)

        class_map = {}
        intro = class_parts[0]  # Namespace intro text before the first class

        j = 1
        while j < len(class_parts):
            c_header = class_parts[j]
            c_body = class_parts[j + 1] if j + 1 < len(class_parts) else ""
            c_name_match = re.search(r'### Class: `(\w+)`', c_header)
            c_name = c_name_match.group(1) if c_name_match else ""
            class_map[c_name] = c_header + c_body
            j += 2

        # Insert or update classes for this namespace from the freshly parsed data
        if ns_name in parsed_by_ns:
            for new_c in parsed_by_ns[ns_name]:
                class_map[new_c['class_name']] = generate_markdown(new_c)

        # Re-sort classes alphabetically within the namespace
        new_body = intro
        for c_name in sorted(class_map.keys()):
            new_body += class_map[c_name]

        new_doc += ns_header + new_body
        i += 2

    # Append entirely new namespace sections not previously in the document
    new_namespaces = sorted(set(parsed_by_ns.keys()) - written_namespaces)
    for ns_name in new_namespaces:
        print(f"[INFO] Adding new namespace section: {ns_name}")
        new_doc += f"\n## Namespace: `{ns_name}`\n\n"
        for new_c in sorted(parsed_by_ns[ns_name], key=lambda x: x['class_name']):
            new_doc += generate_markdown(new_c)

    with open(doc_path, 'w', encoding='utf-8') as f:
        f.write(new_doc)

    print(f"[INFO] Documentation successfully updated at {doc_path}.")
    if new_namespaces:
        print(f"[INFO] New namespace sections added: {', '.join(new_namespaces)}")


if __name__ == "__main__":
    merge_docs()

```

### File: .agents/plugins/RevitWorkspaceDevOps/scripts/pull_docs.py
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

    auth_dir = os.path.join(projects_dir, "Revit API Synthetic v2 Support", "auth")
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

### File: .agents/plugins/RevitWorkspaceDevOps/scripts/sync_all.py
```python
import os
import sys
import argparse
import json

# Ensure sibling scripts (aggregate.py, drive_sync.py) are importable regardless of CWD
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from aggregate import process_config, resolve_config_path, get_default_paths
from drive_sync import sync_to_drive


def main():
    _, default_config = get_default_paths()

    parser = argparse.ArgumentParser(
        description="Configuration-driven codebase aggregation and Google Drive sync runner."
    )
    parser.add_argument(
        "--config",
        dest="config_path",
        default=default_config,
        help=f"Path to the configuration JSON file. Defaults to: {default_config}"
    )
    parser.add_argument(
        "--extensions",
        dest="extensions",
        nargs="+",
        default=[".cs", ".xaml", ".md"],
        help="Allowed file extensions. Defaults to .cs .xaml .md"
    )

    args = parser.parse_args()

    # Resolve projects_dir from the workspace root anchor
    projects_dir, _ = get_default_paths()

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
        with open(args.config_path, 'r', encoding='utf-8') as f:
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

### File: .agents/rules/atlas_policy.md
```markdown
---
description: Regulates the maintenance, modification, and initial generation behavior of the Project Atlas documentation.
globs: "project_atlas.md"
---

# Project Atlas Management Policy

1. You are explicitly aware of the file `docs/project_atlas.md` located in the `docs/` subdirectory of the project root.
2. **Active Coding Phase:** You are strictly forbidden from modifying this file while actively iterating on code features, running Git commit loops, or executing tests.
3. **Initial Bootstrapping Exception:** If `project_atlas.md` is detected to be empty (0 bytes) or lacks a structural index, you are explicitly authorized and commanded to bootstrap it. Perform a comprehensive initial workspace scan of the C# source directory, map out the existing classes, and write the baseline atlas map before you begin any feature coding.
4. **Feature Completion Phase:** You are authorized to write to or modify `project_atlas.md` when the custom workspace workflow trigger `/finalize` is executed by the user at the end of a feature branch.
5. **Content Requirement:** The file must concisely document the high-level folder structure, key classes, active Revit API namespaces, and a registry of existing commands to serve as a clean, predictable map for the Strategic Planner.
```

### File: .agents/rules/revit_api_rules.md
```markdown
---
description: Constraints for multi-version Revit API development (2022-2027) during Ingestion and Critique.
globs: "*.cs"
---

# Revit API Multi-Version Guardrails (2022 - 2027)

Before writing any C# code or modifying existing files, you must cross-reference this deprecation matrix during your "Ingestion and Critique" phase. Flag any violations immediately.

## 1. Framework & Compilation Target Shifts
* **Revit 2022–2024:** Uses old .NET Framework 4.8.
* **Revit 2025–2027:** Runs on modern .NET 8.0 (Core). 
* **Constraint:** Do not use modern .NET Core-specific libraries/syntax (like advanced Span structures or newer system assembly methods) unless they are safely wrapped in `#if REVIT2025 || REVIT2026 || REVIT2027` conditional compilation directives.

## 2. Unit API Deprecations (The ForgeTypeId Shift)
* **Revit 2022+:** `DisplayUnitType` is completely removed. 
* **Constraint:** You must use `ForgeTypeId` for units, specs, and symbols. Access them via static properties on `SpecTypeId` or `UnitTypeId`.

## 3. Parameter Data Type Architecture
* **Revit 2022/2023:** `Definition.ParameterType` was deprecated and subsequently removed in 2024.
* **Revit 2024–2027:** Use `Definition.GetDataType()` which returns a `ForgeTypeId`.
* **Constraint:** Never generate raw `.ParameterType` evaluations. Use conditional definitions if older versions require a fallback to `ParameterType`.

## 4. Structural Structural Properties & Geometry Changes
* **Revit 2025/2026:** Internal changes to graphics pipelines and heavy depreciation of structural element structural properties methods.
* **Revit 2026/2027:** Heavy refactoring of `Mesh` and geometry vertex access tracking. Ensure `Mesh.Vertices` syntax is updated to modern indexing arrays if compiling for 2026+.
```

