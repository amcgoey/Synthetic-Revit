# UI Standardization and Theme Modernization - Product Requirement Document

## Problem Statement

The user interface currently suffers from scattered, hardcoded styling across dozens of individual WPF files, leading to a fragmented and inconsistent user experience. This lack of a centralized styling architecture manifests as severe visual contrast issues—specifically, light text washing out against native Windows light-blue system highlights in dropdowns. Furthermore, the reliance on default OS title bars and randomly colored raster icons (PNGs) breaks the immersion of the dark theme and draws the user's eye away from primary actions, resulting in a cluttered and unpolished appearance.
## Solution

Implement a centralized, semantic styling architecture to act as a single source of truth for all WPF visuals. The solution will completely override native Windows control templates to enforce a clean, minimalist dark theme that mathematically meets WCAG 2.1 AA contrast standards. The OS title bar will be replaced with a custom, edge-to-edge WindowChrome, and all dialog icons will be migrated to scalable, monochrome XAML vectors. Color will be stripped from the UI entirely, reserved only for indicating active states or critical primary actions.
## User Stories

- As a user, I want the ComboBox dropdown to have high-contrast text and a dark selection highlight, so that I can easily read my selection without a jarring light-blue system color bleeding through.
- As a user, I want the entire application window (including the title bar) to share the exact same dark background color, so that the immersion isn't broken by a bright white or system-accented header.
- As a user, I want dialog icons to be monochrome by default and only use color to indicate active states or warnings, so that my eyes are only drawn to important, actionable areas of the screen.
- As a visually impaired user, I want all disabled text, borders, and input fields to maintain a minimum 3:1 or 4.5:1 contrast ratio against the background, so that I can easily distinguish inputs and read information without eye strain.
- As a developer, I want all background and text colors defined in a single semantic palette, so that I can globally adjust the contrast without hunting down hardcoded hex values in dozens of individual XAML files.
- As a developer, I want all standard input controls (TextBox, ComboBox, DataGrid) to automatically inherit the firmwide styling, so that new dialogs seamlessly match the rest of the application without requiring repetitive configuration.
- As a maintainer, I want scalable XAML vector paths used for dialog icons instead of PNGs, so that the icons scale cleanly on high-DPI displays and dynamically adapt to text color brushes without needing new image files.
- As a user, I want custom window control buttons (Minimize, Maximize, Close) to match the dark minimalist theme while retaining standard Windows snapping and dragging behaviors, so that the UI feels native but highly polished.
## Implementation Decisions

- **Centralized Resource Dictionary:** Establish a shared ResourceDictionary (e.g., SyntheticTheme.xaml) in the SyntheticShared/Shared/UI/ directory to house all global styles, serving as the single source of truth.
- **Semantic Color Palette:** Define global SolidColorBrush resources using semantic, tiered naming conventions (e.g., Synthetic.Brushes.BackgroundBase, Synthetic.Brushes.ControlSurface, Synthetic.Brushes.TextPrimary, Synthetic.Brushes.AccentActive) rather than control-specific names.
- **WCAG AA Contrast Compliance:** Mathematically calibrate the semantic hex values to ensure a minimum 4.5:1 contrast ratio for text and 3:1 for UI component borders/backgrounds.
- **Complete ControlTemplate Overrides:** Write custom WPF ControlTemplates for all interactive UI elements (ComboBox, TextBox, Button, DataGrid, CheckBox, ListBox) to completely eradicate legacy Windows Aero/Metro rendering and system highlights.
- **Custom Window Chrome:** Implement a custom WindowStyle utilizing WindowChrome to remove the default OS title bar. This includes building a custom title bar layout with stylized Minimize, Maximize, and Close buttons.
- **Vector Iconography:** Replace in-dialog raster PNG assets with XAML Path geometries. These paths will use dynamic resource bindings to text foreground brushes to ensure a strict monochrome appearance.
- **Revit Ribbon Exemption:** The Revit Ribbon icons (16x16 and 32x32) will remain as PNGs due to the strict limitations of the native Revit API, though they may be flattened/desaturated as a separate asset update.
## Testing Decisions

- **Visual and Resource Testing:** Tests should verify that the centralized ResourceDictionary correctly merges and applies at runtime across different assemblies without throwing ResourceReferenceKeyNotFoundException.
- **Window Behavior Verification:** While mostly XAML, any C# code-behind required to support dragging or snapping the custom WindowChrome must be verified via Tier 1 Logic tests to ensure the window behaves natively.
- **Prior Art:** Leverage the existing WPF Window instantiation patterns found in the SyntheticShared/Shared/UI module (e.g., DropdownSelectionView, ListByCheckboxView) to ensure the new styles apply cleanly to our reusable dialog wrappers.
## Out of Scope

- Refactoring or replacing the native Revit Ribbon PNG assets into XAML paths (as the Revit API strictly requires ImageSource/Bitmaps for PushButtonData).
- Modifying the underlying business logic, state management, or DataContext bindings of any ViewModels; this is strictly a visual presentation and styling architecture overhaul.
- Adding a "Light Theme" toggle. While the new semantic architecture will easily support theming in the future, defining and implementing a secondary light palette is out of scope for this immediate fix.
## Further Notes

This architectural pivot ensures that any future UI additions automatically comply with the firm's visual standards, drastically reducing the technical debt currently incurred by duplicating styles across every individual view.
