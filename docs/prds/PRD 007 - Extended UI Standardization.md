# PRD 007 - Extended UI Standardization (Tabs, Scrollbars, Tooltips, Responsive Sizing) - Product Requirement Document

## Problem Statement

While PRD 006 successfully established a centralized, dark-themed UI architecture, certain deeply-nested native WPF controls—specifically TabControl / TabItem, ScrollBar, and ToolTip—were overlooked. Because these elements default to standard Windows Aero/Metro rendering, they still display jarring light-blue system highlights, bright gradients, and dated "file folder" tab shapes.
Furthermore, the codebase relies heavily on rigid pixel constraints (Height, Width) for text inputs, action buttons, dialog windows, and DataGrid columns. These hardcoded dimensions clip the newly applied fonts, break fluid layouts on high-resolution displays, and prevent the UI from scaling gracefully. Coupled with this, the custom WindowChrome titles are currently rendered too small to comfortably establish dialog context. This combined rigidity and visual friction breaks the immersion of the bespoke dark theme.
## Solution

Author custom ControlTemplate overrides for TabControl, TabItem, ScrollBar, and ToolTip directly within the centralized SyntheticTheme.xaml resource dictionary. The designs will adhere strictly to the "Tailored Minimal Density" specification.
Simultaneously, execute a comprehensive responsive sizing sweep: strip hardcoded Height and Width attributes from buttons, input controls, and DataGrid columns across the UI in favor of global, intrinsic, content-driven scaling regulated by flexible MinHeight and MinWidth constraints. Small dialog windows will be updated to size to their content, and the window title headers will be scaled up 1.5x with slightly expanded WindowChrome boundaries to comfortably frame the larger text.
## User Stories

- As a user navigating the Standards Editor, I want the tabs to feature a clean, flat design with a highlighted underline for the selected state, so that the interface feels modern and devoid of dated "file folder" metaphors.
- As a user, I want unselected tabs to have a transparent resting state and a subtle hover highlight, so that the visual hierarchy clearly draws my eye only to the active tab.
- As a user scrolling through large data grids or trees, I want the scrollbars to be dark and unobtrusive, so that bright system gradients do not distract my eyes from the primary data.
- As a user hovering over UI elements, I want the tooltips to match the dark theme with a subtle border, so that I am not blinded by bright white or yellow system popup boxes.
- As a UI developer, I want these overrides housed centrally in SyntheticTheme.xaml, so that any new views utilizing tabs, tooltips, or scrolling automatically inherit the firmwide styling without additional XAML boilerplate.
- As a user, I want text inputs and action buttons to scale responsively with their content and window resolution without clipping, so that my data is always legible regardless of display scaling.
- As a user stretching a data-heavy window across a large monitor, I want the DataGrid columns to proportionally expand to fill the available horizontal space, avoiding cramped text and dead UI zones.
- As a user, I want the dialog window titles to be 1.5x larger, so they are easily readable and clearly establish the context of the window.
## Implementation Decisions

- **Target Module:** src/SyntheticShared/Shared/UI/SyntheticTheme.xaml and associated modernized XAML views.
- **Responsive Input & Button Sizing (Intrinsic Scaling):**
  - Strip explicit Height and Width properties (e.g., Width="80" Height="28", Height="26") from Button, TextBox, and ComboBox instances across all modernized XAML views.
  - Define MinHeight (e.g., 28 or 30) and MinWidth (e.g., 80 for buttons) in the implicit styles within SyntheticTheme.xaml.
  - Utilize Padding (e.g., Padding="12,6" for buttons, Padding="4,4" for text inputs) to allow the controls to flex and "shrink-wrap" their text contents naturally based on the system DPI and active font.
- **Responsive DataGrid Columns:**
  - Replace rigid pixel widths (e.g., Width="220") on DataGrid columns with proportional star-sizing (Width="*", Width="2*") or intrinsic sizing (Width="Auto").
- **Window Sizing & Chrome Scaling:**
  - Remove fixed Height and Width attributes from smaller dialogs (e.g., FindReplaceWindow, SyncResolutionWindow) and apply SizeToContent="WidthAndHeight" while retaining MinHeight and MinWidth safety constraints.
  - Scale the default Window Title TextBlock FontSize up by 1.5x (e.g., from ~12 to ~18).
  - Increase the CaptionHeight property of the WindowChrome configuration (located in ViewModelBase.cs or the respective initialization helper) from 40 to 45 to provide adequate breathing room and padding for the newly enlarged text.
- **TabItem Override:** Implement a custom ControlTemplate.
  - Resting state: Transparent background, TextSecondary foreground, no border.
  - Hover state: ControlSurfaceLighter (#2E2D2C) background.
  - Selected state: ControlSurfaceLighter background, TextPrimary foreground, and a 2px AccentActive (#E65100) bottom border to act as an underline.
- **TabControl Override:** Implement a custom ControlTemplate to map the main content area background to ControlSurface (#252423) and its border to BorderNormal (#3A3938).
- **ScrollBar Override:** Implement custom ControlTemplate overrides for both ScrollBar and its internal Thumb.
  - Strip all native Windows gradients.
  - Track: BackgroundBase (#1C1B1A) or transparent.
  - Thumb: ControlSurfaceLighter (#2E2D2C), lighting up slightly on hover.
- **ToolTip Override:** Implement a custom ControlTemplate.
  - Background: ControlSurface (#252423).
  - Foreground: TextPrimary (#F1F1F1).
  - Border: 1px BorderNormal (#3A3938).
  - Drop shadows should be removed to align with flat design rules.
## Testing Decisions

- **Testing Seam:** Tier 1 Logic tests targeting the centralized ResourceDictionary (ThemeTests.cs).
- **Assertions:** Add explicit assertions in SyntheticTheme_ContainsKeyedControlStyles_AllPresent to verify that implicit styles (TargetType) for TabControl, TabItem, ScrollBar, and ToolTip are successfully loaded and available in the dictionary at runtime without throwing ResourceReferenceKeyNotFoundException.
## Out of Scope

- Modifying the underlying C# business logic or data bindings inside any view models.
- Refactoring other unrelated WPF controls not explicitly defined in this PRD.
- Restyling standard Windows MessageBoxes (which cannot be styled via XAML and would require a custom window implementation).
## Further Notes

This completes the visual overhaul initiated in PRD 006, fully severing the application's reliance on Windows OS-level accent colors for its core layout components, while guaranteeing a fluid, resolution-independent layout.
