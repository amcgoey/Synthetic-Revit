# Deepening

How to deepen a cluster of shallow modules safely, given its dependencies. Assumes the vocabulary in [SKILL.md](SKILL.md): **module**, **interface**, **seam**, **adapter**.

## Dependency categories

When assessing a candidate for deepening, classify its dependencies. The category determines how the deepened module is tested across its seam.

### 1. In-process

Pure C# models and computation, in-memory state, no I/O. In Synthetic: `RevitDOM` models (`ElementModel`, `ParameterModel`, `CategoryModel`), comparison engines (`IdentityComparison`, `DeepDiffComparison`), and merge state machines. Always deepenable: merge the shallow helpers and test through the new interface directly in `SyntheticLogic.Tests`. No adapter needed.

### 2. Local-substitutable

Dependencies that have local test stand-ins (local JSON settings/template serialization via `Newtonsoft.Json`, file system operations, and Excel export via `Dot Sheet Index`). Deepenable if the stand-in exists. The deepened module is tested using local temp directories or memory streams. The seam is internal; no port at the module's external interface.

### 3. Remote but owned (Ports & Adapters)

Revit database operations across the unmanaged C++ boundary (`Autodesk.Revit.DB.Document`, `FilteredElementCollector`, `Transaction`, `ExtensibleStorage`). Define a **port** (interface) at the seam. The deep module owns the BIM logic; `RevitDOM` translators act as the adapter. In headless tests, fake/in-memory adapters (`FakeIdentityService`, `PocoIdentityService`) or POCO fixtures are used; in `Synthetic.Tests`, real Revit documents verify the adapter inside the Revit process.

Recommendation shape: *"Define a port at the RevitDOM seam, implement a Revit database adapter for production and a POCO adapter for headless testing, so the BIM standards logic sits in one deep module."*

### 4. True external / Thread boundary (Mock)

Platform thread boundaries and third-party APIs (`UIApplication`, `IExternalEventHandler` / `ExternalEvent`, WPF `Dispatcher`, Google Drive cloud APIs) that you don't control. The deepened module takes the external dependency as an injected port; tests provide a mock or stub adapter.

## Seam discipline

- **One adapter means a hypothetical seam. Two adapters means a real one.** Don't introduce a port unless at least two adapters are justified (typically production + test). A single-adapter seam is just indirection.
- **Internal seams vs external seams.** A deep module can have internal seams (private to its implementation, used by its own tests) as well as the external seam at its interface. Don't expose internal seams through the interface just because tests use them.

## Testing strategy: replace, don't layer

- Old unit tests on shallow modules become waste once tests at the deepened module's interface exist; delete them.
- Write new tests at the deepened module's interface. The **interface is the test surface**.
- Tests assert on observable outcomes through the interface, not internal state.
- Tests should survive internal refactors, since they describe behaviour, not implementation. If a test has to change when the implementation changes, it's testing past the interface.
