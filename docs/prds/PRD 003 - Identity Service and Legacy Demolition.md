# PRD 003 - Identity Service & Legacy Demolition - Product Requirement Document

## Problem Statement

The LegacyDomExtensions.cs file was intended as a temporary bridge during the initial RevitDOM refactoring but remains actively used across the serialization pipeline. It houses global .ToModel() extension methods for both ubiquitous types (like ElementId), complex nested objects (like CompoundStructure, FillGrid, and WallSweeps), and other translation bridges (like CategoryIdModel, Color, and EnumModel). This violates the Open/Closed Principle, creates a "utility dump," and leaks Revit API dependencies by breaking the absolute Locality of our IModelTranslator adapters.
Furthermore, the ReferenceResolver.cs acts as a static database query engine for injecting and resolving element IDs. Because translators statically call ReferenceResolver.ResolveElementId(), they are tightly coupled to the live Revit database. This makes it impossible to unit-test the translators in isolation using fast, headless NUnit tests, violating our core TDD guideline to "Mock at the Edges".
## Solution

We will dismantle the static utility patterns and refactor the module to maximize Locality and Testability via Dependency Injection.
First, parent-specific nested objects and remaining translation mappings (e.g., CompoundStructure, FillGrid, WallSweeps, CategoryIdModel, Color, Enum) will be absorbed directly into their designated structural homes (e.g., specific translators or math utilities).
Second, we will extract the ubiquitous ElementId mapping logic and the complex identity resolution logic (from ReferenceResolver) into a unified, injectable IIdentityService. By injecting a concrete RevitIdentityService in production and a FakeIdentityService during testing, we create a deep architectural seam that completely severs the translators' dependency on the live Revit API, enabling lightning-fast unit tests. Finally, LegacyDomExtensions.cs, ReferenceResolver.cs, and their associated obsolete test fixtures will be permanently deleted.
## User Stories

- As a maintainer extending the addon, I want the mapping logic for CompoundStructure and WallSweeps to live entirely within the HostObjTypeTranslator, so that I have absolute Locality when modifying system family serialization.
- As a maintainer, I want the mapping logic for FillGrid to live entirely within the FillPatternTranslator, so that all pattern translation rules are isolated to a single file.
- As a developer writing tests, I want translators to rely on an injected IIdentityService rather than a static ReferenceResolver, so that I can substitute a FakeIdentityService and test translator logic without booting a live Revit instance.
- As a UI developer executing transactions, I want the StandardSerializationEngine to automatically instantiate the concrete RevitIdentityService and pass it down the pipeline, so that I don't have to manage dependency injection containers at the UI command level.
- As an architectural guard, I want LegacyDomExtensions.cs completely deleted, so that developers are prevented from adding new global extension methods that leak Revit API dependencies.
- As a developer verifying my code, I want the existing test cases in LegacyDomExtensionsTests.cs relocated 1:1 to their respective translator tests (e.g., HostObjTypeTranslatorTests.cs, ColorTranslatorTests.cs), so that test coverage is maintained and clearly associated with the correct module.
- As a maintainer, I want the remaining legacy mappings like CategoryIdModel, Color, and EnumModel moved to their correct utility and translator classes so that the architecture remains consistent and free of catch-all utility dumps.
## Implementation Decisions

- **Parent-Specific & Fragment Absorption:** The extraction and injection logic for complex sub-objects and mappings will be relocated to precise boundaries:
  - CompoundStructure, CompoundStructureLayer, and WallSweeps mapping -> HostObjTypeTranslator.cs (as private helpers).
  - FillGrid mapping -> FillPatternTranslator.cs (as private helpers).
  - CategoryIdModel mapping -> CategoryTranslator.cs (as public static helpers, due to doc.Settings.Categories querying).
  - Color.ToColor() / ColorModel.ToModel() -> ColorTranslator.cs (preserving the pure-math extension method pattern).
  - EnumModel.ToEnum() / Enum.ToModel() -> EnumUtil.cs (static utilities).
- **The** **IIdentityService** **Seam:** A new interface IIdentityService will be created to govern both the extraction of ElementId (formerly ToModel()) and the injection/resolution of IDs (formerly ReferenceResolver.Resolve()).
- **Concrete Implementations:** * RevitIdentityService: Contains the actual Revit API database logic, including the 5-step fallback protocol, "Magic ID" intercepts, and cross-document type guarding.
  - FakeIdentityService: A lightweight, in-memory dictionary-based mock used exclusively for NUnit logic tests.
- **Dependency Injection:** * The IModelTranslator interface signatures will be updated (or translators refactored to accept constructor injection, depending on the Dispatcher capability) to receive the IIdentityService.
  - StandardSerializationEngine will instantiate RevitIdentityService and provide it to the ModelDispatcher.
- **File Demolition:** LegacyDomExtensions.cs and ReferenceResolver.cs will be physically deleted from the Synthetic.Modules.RevitDOM namespace.
## Testing Decisions

- **Test Realignment (Never Refactor on Red):** Before deleting the legacy files, the test cases inside LegacyDomExtensionsTests.cs that cover the relocated logic must be moved to their corresponding fixtures (e.g., HostObjTypeTranslatorTests.cs, CategoryTranslatorTests.cs). Tests for math structs like Color must get new fixtures (e.g., ColorTranslatorTests.cs).
- **Tier 1 Logic Tests:** The introduction of FakeIdentityService will allow us to write fast, headless NUnit tests for HostObjTypeTranslator and others, verifying their logic without ricaun.RevitTest.
- **Test Demolition:** The Tier2_ReferenceResolverIntegrationTests.cs and LegacyDomExtensionsTests.cs files will be removed or heavily refactored to target the new RevitIdentityService through integration testing.
## Out of Scope

- Refactoring pure mathematical geometry translators (e.g., TransformTranslator.cs, XYZTranslator.cs, BoundingBoxTranslator.cs). These will retain their C# extension method patterns, as they operate on pure mathematical structs that require zero Revit database context.
- Altering the actual 5-step fallback logic or "Magic ID" intercept behavior. The business logic of identity resolution remains exactly as defined in ADR 014 and ADR 015; it is only changing its structural home.
## Further Notes

This refactor officially deprecates the static utility pattern for database-dependent translation, establishing the injectable IIdentityService pattern for maximizing module depth and testability.

