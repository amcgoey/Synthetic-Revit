# PRD 004 - View Serialization Architecture - Product Requirement Document

## Problem Statement

The current view translation implementation treats all Revit View elements monolithically. When extracting graphical properties—such as Discipline, DisplayStyle, Scale, and DetailLevel—the engine queries these directly from the base View object. For non-graphical views (e.g., Schedules, Legends, Sheets) or certain view templates, the Revit API throws a runtime exception when these properties are accessed. While currently bypassed using expensive try-catch blocks, this approach degrades performance, relies on exceptions for control flow, and results in bloated JSON schemas containing non-applicable null properties.
## Solution

Implement a polymorphic translation model that restructures the serialization pipeline to dispatch views to specialized translators based on their underlying subclass. The solution will establish a symmetrical, 2-tier C# POCO and Translator hierarchy that strictly mirrors the native Revit API class inheritance to maximize maintainability and resilience against future API changes. Furthermore, the architecture will replace all exception-handling blocks with explicit capability guardrails (e.g., checking view.ViewType against an IsGraphicalView filter) before querying the database, ensuring schema purity and zero runtime exceptions.
## User Stories

- As a developer exporting styles, I want views to be processed by specialized translators, so that I don't incur performance penalties from thousands of caught exceptions on unsupported properties.
- As a maintainer reviewing JSON schemas, I want non-graphical views (like Sheets or Schedules) to omit graphical properties entirely, so that the JSON standard payload is clean, strictly typed, and devoid of noise.
- As a developer extending the addon, I want the POCO models and translators to mirror Revit's inheritance tree (e.g., ViewPlanModel inheriting from ViewModel), so that the architecture is intuitive and aligned with the native Revit database.
- As a user transferring standards, I want view templates to be handled seamlessly within the base ViewModel using an IsTemplate flag, so that template-specific data like NonControlledParameterIds is accurately captured without requiring complex or conflicting multiple-inheritance C# classes.
- As a UI developer, I want the bulk execution pipeline to route standard views and view templates into separate dictionary buckets inside the serialization container based on their IsTemplate flag, so that I can easily populate the Standards Editor shopping cart with logically grouped data.
- As an integration tester, I want to extract a mixed batch of schedules, plans, sheets, and templates, so that I can verify zero runtime exceptions occur during the end-to-end serialization process.
- As a UI user, I want the export of large models with hundreds of sheets to be fast and responsive, so that I am not waiting on silent backend errors to clear.
## Implementation Decisions

- **Revit-Mirrored POCO Hierarchy:** We will implement specialized POCOs (e.g., ViewPlanModel, ViewSheetModel) that inherit from the base ViewModel to exactly mirror the native Revit C# inheritance tree.
- **ViewTemplateModel Absorption:** The existing ViewTemplateModel C# class will be completely deleted. Its unique properties (e.g., NonControlledParameterIds) will be absorbed into the base ViewModel. Template behavior will be entirely governed by the IsTemplate boolean.
- **Internal Dictionary Segregation:** Inside the ModelsToSerialize data container, the explicit ViewTemplates and Views dictionaries will be maintained. The container will utilize the IsTemplate boolean to route the base ViewModel into the correct logical bucket.
- **Symmetrical Translator Hierarchy:** We will implement specialized translators (e.g., ViewPlanTranslator) that inherit from a base ViewTranslator, maintaining perfect symmetry with the model hierarchy.
- **ViewType Capability Guardrails:** The base ViewTranslator will implement a strict capability check (e.g., an IsGraphicalView(ViewType) helper) before attempting to extract properties like Scale, DetailLevel, and DisplayStyle. This completely deprecates the legacy try-catch exception handling pattern.
## Testing Decisions

- **Philosophy:** We will test external behavior through the deepest public seams (the StandardSerializationEngine and ModelDispatcher) and avoid mocking internal database methods or private translator queries.
- **Tier 1 Modules (Logic Tests):** Target MVVM state transitions and pure C# POCO instantiation, inheritance structure, and JSON string schema purity using headless NUnit tests. We will verify that a serialized ViewSheetModel does not output empty graphical property fields.
- **Tier 2 Modules (Integration Tests):** Execute an end-to-end extraction batch using the StandardSerializationEngine.ByRevit() seam against a live Revit document containing a comprehensive mix of ViewPlans, ViewSheets, ViewSchedules, and ViewTemplates via the ricaun.RevitTest runner. Assert that the execution completes with zero caught or uncaught InvalidOperationException occurrences and that the resulting models route to the correct payload dictionaries.
- **Prior Art:** We will leverage the setup and rollback transaction patterns found in Tier2_SerializationIntegrationTests.cs (The Zero-Leak Rule).
## Out of Scope

- Modifying the logic for deep-serializing ViewFilter parameters or CategoryGraphicOverrides, which are handled by their own independent translators.
- Altering the WPF UI components or visual styling for the JSON Standards Editor.
- Physical instance canvas view placement or manipulation.
## Further Notes

This architecture resolves the deep tension between Revit's flawed base View object properties and clean C# object-oriented design by utilizing explicit capability checks. This paves the way for faster, exception-free batch processing of highly complex project files.
