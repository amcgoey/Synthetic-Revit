# PRD 011 - Project Standards Edit Pane Refactoring - Product Requirement Document

## Problem Statement

Currently, the Project Standards Dashboard's Edit Pane displays Revit parameters correctly, but it fails to expose the underlying general properties of the serialized element POCOs. Furthermore, critical identity properties (Name, Class, Aliases) are not prominently elevated, causing user friction when identifying elements. The pane's title ("Batch Edit Staged Elements") is inaccurate for single-element selections, and the layout hierarchy (placing the Find & Replace expander above the parameters grid) pushes critical editable data off-screen.
## Solution

Refactor the Edit Pane's layout and data-binding architecture to unify Revit parameters and POCO properties into a single, cohesive editing experience. The pane will be renamed to "Edit Elements." Core identity attributes (Name, Class, Aliases) will be elevated to the top of the pane with specialized multi-selection handling. Below these, a dynamic reflection engine will harvest all POCO properties—respecting read-only states for primitives and delegating complex objects to a nested editor—and inject them into the existing scrollable parameter grid. The Find & Replace utility will be moved to the bottom of the pane, prioritizing immediate data visibility.
## User Stories

- As a user editing standards, I want the pane title to be "Edit Elements", so that its purpose is clearly defined whether I am editing one or multiple items.
- As a user, I want the Name, Class, and Aliases of the element prominently displayed at the top of the pane, so that I can instantly verify the identity of the standard I am modifying.
- As a user selecting multiple elements, I want the Name input to hide and display the total count of selected elements, so that I understand I am executing a batch operation.
- As a user selecting multiple elements, I want the Class field to display a dense, word-wrapped list of all selected classes, so that I know exactly what categories of objects are being edited.
- As a user selecting multiple elements, I want the Aliases input to be disabled and display <Varies>, so that I am prevented from accidentally overwriting unique legacy alias mappings in bulk.
- As a BIM manager, I want general POCO properties (e.g., simple string, number, boolean, or enum fields) to appear in the same scrollable list as Revit parameters, so that I can audit and edit all flat data in one unified view.
- As a system architect, I want the system to detect if a POCO primitive property lacks a public setter and automatically render it as read-only in the UI, so that data integrity is protected and the UX remains consistent with Revit parameters.
- As a user, I want complex nested POCO properties (like compound structures or layer lists) to appear in the grid with an 'Edit Nested Data' button, so I can access their specific sub-editors without them being hidden from the main view.
- As a user, I want the "Find & Replace" expander moved below the main properties grid, so that my primary workspace is dedicated to the data itself.
## Implementation Decisions

- **UI Layout Adjustments**: The ProjectStandardsDashboardWindow.xaml DataTemplate for the Edit Workspace will be restructured. The title will be statically changed. The layout order will be: Elevated Identity Properties -> Scrollable DataGrid -> Find & Replace Expander -> Action Buttons.
- **Multi-Select State Management**: The ProjectStandardsDashboardViewModel (or equivalent context manager) will handle the visibility and string aggregation for the elevated properties (IsSingleElementSelected, SelectedAliasesString, SelectedDisplayClass).
- **Dynamic POCO Property Harvesting**: During the initialization of ElementTypeWrapperVM (or when preparing the edit session), a reflection sweep will execute against the underlying ObjectModel.
- **Property Filtering**: The reflection sweep will target *all* public properties on the POCO.
- **Exclusion List**: The properties Name, Class, and Aliases will be explicitly excluded from the reflection sweep so they do not duplicate the elevated header inputs.
- **Read-Only Mapping for Primitives**: The reflection engine will evaluate the CanWrite property of the PropertyInfo for primitives. If the setter is private or missing, the resulting ParameterWrapperVM will have IsReadOnly set to true.
- **Complex Object Delegation**: For properties that represent complex objects or collections, the generated ParameterWrapperVM will be flagged to render the [ Edit Nested Data... ] button, which hooks into the existing NestedDataEditorWindow architecture.
- **Unified ViewModel**: The harvested POCO properties will be wrapped into ParameterWrapperVM instances and appended to the existing Parameters observable collection, allowing them to leverage the existing <Varies> intersection logic automatically.
## Testing Decisions

- **Testing Philosophy**: A good test for this feature will verify the business logic of property extraction and UI state management without needing to spin up the actual WPF window.
- **Reflection Harvesting Test**: Write a unit test targeting ElementTypeWrapperVM to ensure that passing a POCO with mixed properties results in a Parameters collection containing both primitives and complex property wrappers, excluding Name, Class, and Aliases.
- **Multi-Select State Test**: Test the ViewModel context to verify that when 2+ items are selected, the alias string evaluates to <Varies>, name editing is blocked, and classes are correctly concatenated.
## Out of Scope

- Creating a completely new Grid UI component; the feature must reuse the existing ParameterWrapperVM and DataGrid infrastructure.
## Further Notes

By mapping all POCO properties to ParameterWrapperVM, we gain the massive advantage of inheriting the existing bulk-edit intersection logic, IsDirty state tracking, Find & Replace targeting, and Nested Editor triggers essentially for free.

