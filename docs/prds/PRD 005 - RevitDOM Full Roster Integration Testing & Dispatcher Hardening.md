# PRD 005 - RevitDOM Full Roster Integration Testing & Dispatcher Hardening

## Problem Statement

The StandardSerializationEngine recently crashed during manual QA with an InvalidCastException when a ModelTextType element was inadvertently passed into the export collector. Because the ModelDispatcher rigidly binds native Revit classes to specific translators, passing an unsupported derived class (like ModelTextType deriving from TextElementType) causes a hard crash rather than a graceful degradation. This reveals a gap in our integration testing: we lack a mechanism to verify how the dispatcher handles the vast, unpredictable inheritance tree of the live Revit database.
## Solution

We will harden the ModelDispatcher by introducing explicit registries for "Ignored" and "Pending" types, alongside a safe catch-all rejection for unknown types. To prove this routing logic, we will build a "Full Roster Sweep" integration test. This test will collect a heterogeneous sample of every native Revit type in a standard template document and run a full extraction-injection round-trip. Using a Hybrid Loop transaction strategy, the test will provide a granular, consolidated diagnostic report of all dispatcher failures without destabilizing the Revit environment.
## User Stories

- As a developer extending the addon, I want the ModelDispatcher to feature a RegisterPending(Type) method, so that I can explicitly flag known-but-unsupported Revit classes for future development without writing empty POCO stubs.
- As a maintainer managing physical instances, I want the ModelDispatcher to feature a RegisterIgnored(Type) method, so that I can explicitly instruct the engine to safely drop specific classes without logging noisy warnings.
- As a UI developer executing batch exports, I want the ModelDispatcher to catch completely unknown types and return a safe null while logging a SerializationResultModel warning, so that unexpected elements do not crash the entire batch process.
- As a QA developer running integration tests, I want the test suite to execute a "Full Roster Sweep" by collecting exactly one instance of every native Revit type in the document, so that the test payload remains lean while achieving 100% class coverage.
- As a QA developer diagnosing failures, I want the test to evaluate elements iteratively and consolidate errors into a single report at the end, so that I can see every failing class in a single test run rather than fixing crashes one by one.
- As a QA developer preserving Revit's stability, I want the integration test to wrap the entire loop in a global TransactionGroup but isolate each iteration inside a standard Transaction that is immediately rolled back, so that I get clean-room isolation without blowing up Revit's macro-level undo stack.
## Implementation Decisions

- **Dispatcher Hardening:** * Add RegisterIgnored(params Type[] revitTypes) to ModelDispatcher for silent drops (returns null, no warning).
  - Add RegisterPending(params Type[] revitTypes) to ModelDispatcher for known technical debt (returns null, logs "Pending future support" warning).
  - Add a generic catch-all for anything completely unregistered (returns null, logs "Type currently unsupported" warning).
- **The Hybrid Loop:** Test architecture will combine a macro-level TransactionGroup (for final cleanup) with micro-level Transaction blocks (for per-element rollback isolation).
- **Optimized Collection:** The test will use a generic collector, group by .GetType(), and .Select(g => g.First()) to ensure only one of each native class is tested.
## Testing Decisions

- This feature is inherently a testing infrastructure upgrade, targeting Tier 2 (Live Revit Database).
- The primary test fixture will be a new dedicated Tier2_DispatcherSweepTests.cs.
- It asserts that ByRevit and ToRevit handle *every* element gracefully (either translating it successfully, dropping it silently, or logging a controlled warning) without throwing unhandled native or .NET exceptions.
## Out of Scope

- Implementing the actual POCOs or Translators for the newly discovered "Pending" types. This PRD is strictly about building the telemetry and routing infrastructure to safely identify them.
- Modifying the UI to expose the minimal/verbose logging (that will be handled in a separate UI-focused issue).
## Further Notes

- This architecture directly addresses the tension between explicit type safety (our defense against "configuration hell") and the wild west of the native Revit API inheritance tree.
