# RevitDOM Alias Assimilation Engine Refactoring - Product Requirement Document

## Problem Statement

Currently, the "Merge Elements" logic—which involves picking a primary survivor standard, finding duplicate elements by name, deep-swapping their references across the Revit document, and deleting the redundant duplicates—is tightly coupled to the legacy JSON Editor's JsonEditorMainViewModel. This violates the Single Responsibility Principle (SRP) and creates a shallow-module anti-pattern where the UI layer is burdened with highly destructive Revit API database transactions.
Furthermore, naively invoking the existing AliasSwapEngine during batch imports conflicts with the active transaction wrappers of the StandardSerializationEngine, causing fatal nested transaction exceptions if executed concurrently.
## Solution

Push the destructive database operations deep into the StandardSerializationEngine, creating a highly leveraged public seam where callers only need to provide POCOs populated with target Aliases. The AliasSwapEngine will be formally moved into the RevitDOM module but will retain its discrete internal transaction boundaries to manage its deep-scan operations safely.
To prevent nested transaction exceptions and guarantee graceful degradation, StandardSerializationEngine.ToRevit() will utilize a **Sequenced Post-Commit** architecture. The engine will inject and commit all primary elements first, queueing any aliases it encounters. Once the main injection loop is complete, it will process the alias queue, delegating identity resolution, deep reference swapping, redundant element deletion, and telemetry logging to the alias sub-module.
## User Stories

- As a UI developer, I want to pass a POCO with populated aliases directly to StandardSerializationEngine.ToRevit(), so that I don't have to manage complex Revit database swapping logic or transactions in the ViewModels.
- As a BIM manager merging standards, I want the engine to safely swap references (parameters, view overrides, compound structures) from the redundant standard to the primary standard, so that project models do not break when duplicates are deleted.
- As an architectural guard, I want the StandardSerializationEngine to queue alias processing until *after* primary elements are committed, so that I avoid fatal nested transaction errors while inherently preserving primary element creation if an alias swap fails.
- As an architectural guard, I want the AliasSwapEngine to retain its own discrete transaction boundaries, so that legacy callers (like the Modeless Merge UI) are not broken by this architectural shift.
- As a user reviewing logs, I want alias assimilation operations to be reported in the SerializationResultModel, so that I can see exactly which duplicate elements were purged and merged in the summary UI.
## Implementation Decisions

- **Namespace and Structural Realignment:** Move the AliasSwapEngine and any associated alias logic files out of their legacy locations and into the Synthetic.Modules.RevitDOM namespace and corresponding directory structure.
- **Retained Transaction Boundaries:** The AliasSwapEngine.SwapElementReferences() method will keep its internal using (Transaction ...) blocks. It will remain responsible for its own immediate database state, allowing it to safely process deep-scans across views and categories.
- **The Sequenced Post-Commit Queue:** Inside StandardSerializationEngine.ToRevit(), create an in-memory queue (e.g., a list of tuples containing the successfully injected primary ElementId and its Aliases list). During the primary injection loop, if an element is successfully committed and has aliases, add it to this queue.
- **Post-Processing Execution:** After the primary injection loop finishes, iterate through the post-processing queue. Utilize the injected IIdentityService to resolve the live ElementId of each alias string.
- **Deep Reference Swapping & Deletion:** If the alias is found, call AliasSwapEngine.SwapElementReferences() to redirect references, followed by wrapping doc.Delete(aliasId) in a small transaction to purge the redundant element.
- **Inherent Graceful Degradation & Telemetry:** Because primary elements are fully committed before the queue is processed, any exceptions thrown by the AliasSwapEngine will only roll back that specific alias's internal transaction. The primary element is inherently safe. Catch the exception, log the failure to the associated SerializationResultModel, and proceed to the next alias in the queue.
## Testing Decisions

- **Testing Seam:** This is a Tier 2 (Live Revit Database) feature. Tests will target the deep public seam of StandardSerializationEngine.ToRevit().
- **Integration Test Fixture:** Update or create a fixture within Tier2_SerializationIntegrationTests.cs.
- **Scenario Verification:** Serialize a dummy MaterialModel containing an alias. Inject it via ToRevit(). Assert that:
  - The primary material was successfully created or updated.
  - The alias material was successfully deleted from the document.
  - A secondary element (e.g., a WallType or another Material) that previously referenced the alias material now correctly references the primary material.
- **Primary Preservation Verification:** Simulate an alias failure (e.g., by mocking a locked element or bad ID) and assert that the primary element was still successfully created/updated in the document despite the alias swap failing.
- **The Zero-Leak Rule:** Ensure the entire test setup, execution, and assertion is wrapped in a TransactionGroup that is explicitly rolled back in the test teardown to prevent database pollution.
## Out of Scope

- Modifying the UI view models (like JsonEditorMainViewModel or ProjectStandardsDashboardViewModel) beyond ensuring they pass the Aliases strings correctly to the engine.
- Expanding the scope of what AliasSwapEngine swaps. It will remain scoped to the existing parameters, category styles, compound structures, and view overrides.
- Stripping transactions from AliasSwapEngine or remediating legacy callers (like ProcessMergeEventHandler), as this was deemed architecturally unsafe.
## Further Notes

This architecture relies on the overarching TransactionGroup typically opened by the UI orchestrators (like ProjectStandardsDashboardViewModel) to assimilate the primary transactions and the deferred alias transactions into a single Undo step for the user.

