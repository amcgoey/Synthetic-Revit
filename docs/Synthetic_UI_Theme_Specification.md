# Synthetic Revit Add-on: UI Theme Specification & Design System

## 1. Executive Summary & Strategy

This document establishes the official visual specification for the **Synthetic Revit Add-on** user interface.
These choices are optimized specifically for the Revit user ecosystem to:
- Ensure visual immersion when docked within native dark-mode hosting frames (e.g., Revit 2025/2026 modern dark environments).
- Limit eye strain by maintaining desaturated surface spaces.
- Meet mathematical accessibility contrast compliance thresholds under **WCAG 2.1 AA** without sacrificing high-end UI design.
- Strictly enforce structural clarity using flat elevation, eliminating performance-heavy drop shadow overheads.
## 2. Locked-In Design Decision Matrix

The following tokens represent the foundational design rules approved via interactive prototyping. All future WPF windows, control templates, user controls, and dialog layouts must inherit these styles from the centralized theme resource dictionary ([SyntheticTheme.xaml](../src/Platform/UI/Addin/SyntheticTheme.xaml)), merged per view via [SyntheticThemeDictionary.cs](../src/Platform/UI/Addin/SyntheticThemeDictionary.cs).
| **Visual Token** | **Approved Property** | **Technical Value / Rule** |
| --- | --- | --- |
| **Base Background** | Warm Dark Gray | #1C1B1A (Strict neutral black or cold-blue grays rejected) |
| **Control Surface** | Slate Warm Surface | #252423 (Used for nested list borders & dialog boxes) |
| **Active Controls** | Lighter Surface Contrast | #2E2D2C (Input text boxes, inactive check backgrounds) |
| **Primary Action Accent** | Option 3: Rust Orange | #E65100 (Highly visible, colorblind-distinct from Warning) |
| **Standard Borders** | 1px Solid Medium Gray | #3A3938 (Thin borders used in place of shadow elevations) |
| **Success Color** | Sage Green | #7A8F75 (Desaturated, indicating standard compliance) |
| **Warning Color** | Muted Sand Gold | #C2A26A (Indicating parameter discrepancies) |
| **Error Color** | Soft Coral Red | #D37B75 (Indicating unresolvable structural failures) |
| **Text Primary** | High-Contrast White | #F1F1F1 |
| **Text Secondary** | Desaturated Light Gray | #A1A1AA |
| **Corner Geometry** | Moderate Rounded Fillet | 4px radius (Applied universally across buttons, boxes, inputs) |
| **Grid Data Density** | Breathable Flat Grid | 1px solid bottom borders; Zebra striping explicitly rejected |
| **Highlight Density** | Tailored Minimal Scope | Accent border strictly on IsFocused; Action button fully colored |

## 3. WCAG 2.1 AA Compliance Verification

The relative luminance of elements should match WCAG 2.1 AA compliance.
## 4. UI Elements Specific Styling & Control Templates

### 4.1 Window Chrome

The default operating system window frame is completely disabled in favor of a custom, seamless title bar.
- **Chrome Title Bar Background:** #1C1B1A.
- **Title Text:** #F1F1F1 in Gotham Condensed (Gotham Condensed aesthetic) bold uppercase.  Fallback first to Barlow Condensed then to Segoe UI
- **Separator:** 1px solid line (#3A3938) directly below.
- **Min/Max/Close Control Buttons:** Flat, desaturated elements, transforming to #E65100 or #D37B75 on hover.
### 4.2 Form Inputs (TextBox & ComboBox)

- **Resting State:** #2E2D2C flat background with 1px #3A3938 border.
- **Focus State (IsKeyboardFocusWithin):** Highlighted with a crisp 1px #E65100 border. No drop shadows.
- **Text Styling:** Gotham XNarrow Book (Gotham XNarrow Book aesthetic) with a line-height multiplier of 1.15 for readability.  Fallback first to PT Sans Narrow, then to Segoe UI.
### 4.3 Custom Theme Checkboxes

Standard Windows Blue or default OS styling on checkboxes is completely overridden.
- **Resting Checkbox Box:** #2E2D2C background, #3A3938 border, 3px corner geometry.
- **Checked Checkbox Box:** Fully colored background in #E65100 with the border colored #E65100.
- **Checkbox Icon Checkmark:** High-contrast #121111 (dark-contrast vector geometry path checkmark) centered in the box on check state. Blue indicators are strictly prohibited.
- **Disabled Checkbox State:** Reduced opacity to 25% with #121111 background.
### 4.4 Data Grids & Trees

Zebra striping is banned. To maintain visual depth and clean structure, elements are structured dynamically as follows:
- **Background:** Flat #252423.
- **Row Selections:** Light hover highlight overlay (#2E2D2C with 20% transparency), returning to the flat background when cursor leaves.
- **Cell Bottom Borders:** Thin 1px #3A3938 borders to cleanly separate rows.
- **Standards Selection Tree:** Hierarchical nesting via explicit padding. No zebra colors.
## 5. Major Action Buttons Specs (Minimal Tailored Density)

Under the tailored Minimal density scheme:
- **The Primary Action Button:** Fully colored with #E65100 background, #121111 black text, and no border.
- **The Secondary Action Button (Analyze Diff):** Styled flat using #2E2D2C background, #F1F1F1 text, and #3A3938 thin border.
- **The Neutral Button (Cancel):** Clean text layout, #A1A1AA secondary text, and no background/border, highlighting on hover to #F1F1F1.
## 6. Implementation Guidelines for WPF

### 6.1 Cross-Assembly XAML Theme Merge Pattern (Mandatory)

Synthetic splits WPF UI primitives across two assemblies that share the `Synthetic.Platform.UI` CLR namespace:
- **`Synthetic.Core`** (`src/Platform/UI/Core/`): Assembly-neutral helpers including [WindowChromeBehavior.cs](../src/Platform/UI/Core/WindowChromeBehavior.cs), [EnumToBooleanConverter.cs](../src/Platform/UI/Core/EnumToBooleanConverter.cs), and [SingleItemSelectionDisplayConverter.cs](../src/Platform/UI/Core/SingleItemSelectionDisplayConverter.cs).
- **Owning Revit Add-in Assembly** (`Synthetic2023`–`Synthetic2026`, compiled from `src/Platform/UI/Addin/`): Hosts [SyntheticTheme.xaml](../src/Platform/UI/Addin/SyntheticTheme.xaml) (embedded as BAML at `Platform/UI/Addin/SyntheticTheme.xaml`) and [SyntheticThemeDictionary.cs](../src/Platform/UI/Addin/SyntheticThemeDictionary.cs).

Every `Window` and `UserControl` XAML view must merge the theme locally using `SyntheticThemeDictionary`:
1. **Local theme namespace:** Declare `xmlns:theme="clr-namespace:Synthetic.Platform.UI"` (with no `;assembly=...` suffix) so XAML resolves `SyntheticThemeDictionary` from the owning add-in assembly.
2. **Per-view dictionary merge:** Place `<theme:SyntheticThemeDictionary />` inside `<Window.Resources>` or `<UserControl.Resources>` under `<ResourceDictionary.MergedDictionaries>`. `SyntheticThemeDictionary` resolves `/{AssemblyName};component/Platform/UI/Addin/SyntheticTheme.xaml` from its own assembly at runtime, preventing BAML resolution failures across folder depths and avoiding pollution of host `Application.Current.Resources` (which belongs to `Revit.exe` or an external test host).
3. **Core UI namespace:** Declare `xmlns:local="clr-namespace:Synthetic.Platform.UI;assembly=Synthetic.Core"` whenever the view uses `WindowChromeBehavior` (`local:WindowChromeBehavior.EnableWindowCommands="True"`) or core converters (`local:EnumToBooleanConverter`, `local:SingleItemSelectionDisplayConverter`).
4. **Dynamic resource lookup in views:** Reference `SyntheticWindowStyle` and `Synthetic.Brushes.*` tokens via `{DynamicResource ...}` inside view XAML so resources resolve reliably across merged dictionaries.

**Guardrail:** Never use relative `Source="..."` paths (e.g., `Source="../../Shared/UI/SyntheticTheme.xaml"`), hardcoded versioned pack URIs, or `Application.Current.Resources.MergedDictionaries` to load `SyntheticTheme.xaml`. Always merge `<theme:SyntheticThemeDictionary />` inside each view's own resource dictionary.

**Window Template:**
```xml
<Window x:Class="Synthetic.Modules.Example.Views.ExampleWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:local="clr-namespace:Synthetic.Platform.UI;assembly=Synthetic.Core"
        xmlns:theme="clr-namespace:Synthetic.Platform.UI"
        Title="Example Window"
        Style="{DynamicResource SyntheticWindowStyle}"
        local:WindowChromeBehavior.EnableWindowCommands="True">

    <Window.Resources>
        <ResourceDictionary>
            <ResourceDictionary.MergedDictionaries>
                <theme:SyntheticThemeDictionary />
            </ResourceDictionary.MergedDictionaries>
            <local:EnumToBooleanConverter x:Key="EnumToBooleanConverter"/>
        </ResourceDictionary>
    </Window.Resources>
</Window>
```

**UserControl Template:**
```xml
<UserControl x:Class="Synthetic.Modules.Example.Views.ExampleSettingsView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:theme="clr-namespace:Synthetic.Platform.UI"
             Background="Transparent"
             Foreground="{DynamicResource Synthetic.Brushes.TextPrimary}">

    <UserControl.Resources>
        <ResourceDictionary>
            <ResourceDictionary.MergedDictionaries>
                <theme:SyntheticThemeDictionary />
            </ResourceDictionary.MergedDictionaries>
        </ResourceDictionary>
    </UserControl.Resources>
</UserControl>
```
*(Add `xmlns:local="clr-namespace:Synthetic.Platform.UI;assembly=Synthetic.Core"` to a `UserControl` if it binds a core converter from `Synthetic.Core`.)*

### 6.2 XAML Colors, Brushes, and Control Templates

The semantic color and brush palette is defined in [SyntheticTheme.xaml](../src/Platform/UI/Addin/SyntheticTheme.xaml):

```xml
<!-- Core Colors -->
<Color x:Key="Synthetic.Colors.BackgroundBase">#1C1B1A</Color>
<Color x:Key="Synthetic.Colors.ControlSurface">#252423</Color>
<Color x:Key="Synthetic.Colors.ControlSurfaceLighter">#2E2D2C</Color>
<Color x:Key="Synthetic.Colors.BorderNormal">#3A3938</Color>
<Color x:Key="Synthetic.Colors.AccentActive">#E65100</Color>
<Color x:Key="Synthetic.Colors.TextPrimary">#F1F1F1</Color>
<Color x:Key="Synthetic.Colors.TextSecondary">#A1A1AA</Color>
<Color x:Key="Synthetic.Colors.TextDark">#121111</Color>
<Color x:Key="Synthetic.Colors.Success">#7A8F75</Color>
<Color x:Key="Synthetic.Colors.Warning">#C2A26A</Color>
<Color x:Key="Synthetic.Colors.Error">#D37B75</Color>

<!-- Core Brushes -->
<SolidColorBrush x:Key="Synthetic.Brushes.BackgroundBase"        Color="{StaticResource Synthetic.Colors.BackgroundBase}"/>
<SolidColorBrush x:Key="Synthetic.Brushes.ControlSurface"        Color="{StaticResource Synthetic.Colors.ControlSurface}"/>
<SolidColorBrush x:Key="Synthetic.Brushes.ControlSurfaceLighter" Color="{StaticResource Synthetic.Colors.ControlSurfaceLighter}"/>
<SolidColorBrush x:Key="Synthetic.Brushes.BorderNormal"          Color="{StaticResource Synthetic.Colors.BorderNormal}"/>
<SolidColorBrush x:Key="Synthetic.Brushes.AccentActive"          Color="{StaticResource Synthetic.Colors.AccentActive}"/>
<SolidColorBrush x:Key="Synthetic.Brushes.TextPrimary"           Color="{StaticResource Synthetic.Colors.TextPrimary}"/>
<SolidColorBrush x:Key="Synthetic.Brushes.TextSecondary"         Color="{StaticResource Synthetic.Colors.TextSecondary}"/>
<SolidColorBrush x:Key="Synthetic.Brushes.TextDark"              Color="{StaticResource Synthetic.Colors.TextDark}"/>
<SolidColorBrush x:Key="Synthetic.Brushes.Success"               Color="{StaticResource Synthetic.Colors.Success}"/>
<SolidColorBrush x:Key="Synthetic.Brushes.Warning"               Color="{StaticResource Synthetic.Colors.Warning}"/>
<SolidColorBrush x:Key="Synthetic.Brushes.Error"                 Color="{StaticResource Synthetic.Colors.Error}"/>
<SolidColorBrush x:Key="Synthetic.Brushes.Transparent"           Color="Transparent"/>
```

All standard WPF templates (`ControlTemplate TargetType="Button"`, `ControlTemplate TargetType="CheckBox"`, etc.) must target these brushes to ensure complete design compliance across all loaded commands and modules.
