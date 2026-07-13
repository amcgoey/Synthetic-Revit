# PRD 014 - Shared UI Generalization: Item Selection Refactor - Product Requirement Document

## Problem Statement

The current architecture contains a highly specific UI component (MergeSelectionWindow and MergeSelectionViewModel) trapped inside the Synthetic.Modules.MergeDuplicates namespace. Other modules, such as StandardsManagement (specifically within the JSON Editor and Project Standards Dashboard), are forced to reach across module boundaries to borrow this UI when a user needs to pick a single survivor item from a list of duplicates. This creates tight coupling, violates deep module boundaries, and prevents the codebase from leveraging a standard, reusable pattern for single-item selection scenarios.
## Solution

Abstract the selection logic into a universally reusable, generic SingleItemSelectionViewModel<T> within the Synthetic.Shared.UI core library. To maintain a clean UI layer and avoid XAML compiler limitations with generic root classes, pair this with a standard, non-generic SingleItemSelectionWindow.xaml that utilizes runtime data binding to render the generic choices. Finally, deprecate the legacy MergeSelectionWindow and refactor existing callers across all modules to instantiate the new shared generic component.
## User Stories

- As a UI developer, I want a reusable generic ViewModel (SingleItemSelectionViewModel<T>), so that I can easily prompt users to select an item from a list of any data type without writing custom UI code.
- As a UI developer, I want to pass a DisplayMemberPath delegate during ViewModel instantiation, so that I can control exactly how complex domain objects (like QueueItemModel or ElementTypeWrapperVM) are rendered as readable text in the list.
- As an architectural guard, I want the WPF XAML window to remain non-generic, so that we avoid complex XAML compiler workarounds while still benefiting from strongly typed C# ViewModels.
- As a user, I want the selection window to strictly enforce single-selection via RadioButtons, so that I cannot accidentally choose multiple primary items.
- As a UI developer, I want the generic window to automatically inherit the SyntheticTheme (dark scrollbars, flat headers, window chrome), so that the application maintains visual consistency without boilerplate XAML.
- As a maintainer, I want the legacy MergeSelectionViewModel and MergeSelectionWindow completely deleted from the MergeDuplicates namespace, so that the codebase remains clean and free of duplicate logic.
## Implementation Decisions

- **Generic ViewModel (Synthetic.Shared.UI):** Create SingleItemSelectionViewModel<T> inheriting from ViewModelBase. It will expose ObservableCollection<T> Items, T SelectedItem, a string Prompt, and rely on a Func<T, string> DisplayMemberPath provided via the constructor to resolve display names.
- **Non-Generic Window (Synthetic.Shared.UI):** Create SingleItemSelectionWindow.xaml as a standard Window. The core control will be an ItemsControl or ListBox with an ItemTemplate consisting of RadioButton elements.
- **Data Binding Strategy:** The RadioButton.Content will bind to a property (e.g., DisplayName) exposed by wrapping the items, or via a converter that leverages the DisplayMemberPath delegate, circumventing the need for the XAML to know the type of T.
- **Legacy Deprecation:** * Locate usages of MergeSelectionWindow in JsonEditorMainViewModel.cs and ProjectStandardsDashboardViewModel.cs.
  - Swap them to instantiate SingleItemSelectionViewModel<ElementTypeWrapperVM> or SingleItemSelectionViewModel<QueueItemModel>.
  - Delete MergeSelectionViewModel.cs and MergeSelectionWindow.xaml from the MergeDuplicates module.
## Testing Decisions

- **Seam:** Tier 1 (Headless UI Logic)
- **Focus:** NUnit tests targeting SingleItemSelectionViewModel<T> directly, bypassing the slow WPF window instantiation.
- **Scenario:** * Instantiate the generic ViewModel with a list of dummy POCOs.
  - Provide a DisplayMemberPath delegate (e.g., x => x.Name).
  - Assert that SelectedItem correctly tracks state changes.
  - Assert that the ConfirmCommand correctly assigns the dialog result and maintains the selected item state.
## Out of Scope

- Creating multi-selection list components (this is strictly for single-item radio-button picking; multi-select is handled by ListByCheckboxView).
- Modifying the core Revit database swapping logic (this is strictly a UI/ViewModel refactor).
## Further Notes

This refactoring removes a significant architectural boundary bleed and provides a robust, type-safe pattern for any future features requiring user disambiguation.
