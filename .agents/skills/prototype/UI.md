# UI Prototype

Generate **several radically different UI variations** on a single view or screen. The user flips between variants, picks one (or borrows elements from each), and throws the rest away.

If the question is about business logic or state transitions rather than visual layout, this is the wrong branch. Use [LOGIC.md](LOGIC.md).

## When this is the right shape

- "What should this dialog or ribbon panel look like?"
- "I want to see a few options for this parameter mapping view before building the ViewModel."
- "Try a different layout for the batch export tool."
- Any time you would otherwise spend days debating mental layout options.

## Two formats in this repository

1. **Rapid HTML/CSS Mockups (Fastest feedback)**: A single HTML page with multiple structural variants and a floating switcher. Excellent for exploring typography, density, and layout before writing XAML.
2. **WPF / XAML Prototype Views**: When testing actual WPF control behavior, layout containers, and theme integration inside Revit or a standalone WPF previewer, adhering to `docs/Synthetic_UI_Theme_Specification.md`.

---

## Process: Rapid HTML/CSS Multi-Variant Mockup

### 1. State the question and pick N

Default to **3 variants** (maximum 5). Each variant must be **structurally different** (e.g., master-detail vs wizard steps vs flat accordion grid), not merely cosmetic color changes.

### 2. Build the floating switcher

A small fixed bar at the bottom with:
- Previous / Next arrows (or keyboard `←` / `→`).
- Current variant label (e.g. `B: Collapsible Sidebar`).
- URL search param update (`?variant=B`) so state persists across refresh.

---

## Process: WPF / XAML Variant Views

When prototyping WPF dialogs for the Revit Add-in:

### 1. Consult the theme specification

Read `docs/Synthetic_UI_Theme_Specification.md` to ensure styles, color palettes (accents, background neutrals), and control templates match Synthetic standards.

### 2. Scaffold structural variants

Create distinct UserControls or DataTemplates in a prototype folder (e.g. `SyntheticUI/Prototypes/`):
- `VariantA_CardGrid.xaml`
- `VariantB_DockPanelTree.xaml`
- `VariantC_TabbedInspector.xaml`

Ensure each variant explores a genuinely different information hierarchy and interaction model.

### 3. Wire a test switcher

In the prototype window or host view, provide a clean switcher:
- A floating or top-bar `ComboBox` or segmented `RadioButton` group bound to a `CurrentVariant` property on the prototype ViewModel.
- A `ContentControl` whose `ContentTemplate` dynamically switches based on `CurrentVariant`.

```xml
<ContentControl Content="{Binding}">
    <ContentControl.Style>
        <Style TargetType="ContentControl">
            <Style.Triggers>
                <DataTrigger Binding="{Binding CurrentVariant}" Value="A">
                    <Setter Property="ContentTemplate" Value="{StaticResource VariantATemplate}" />
                </DataTrigger>
                <DataTrigger Binding="{Binding CurrentVariant}" Value="B">
                    <Setter Property="ContentTemplate" Value="{StaticResource VariantBTemplate}" />
                </DataTrigger>
            </Style.Triggers>
        </Style>
    </ContentControl.Style>
</ContentControl>
```

---

## Hand it over and capture

1. **Review**: Present the variants to the user. The most common verdict is combining elements: "Take the header from Variant A with the list layout from Variant B."
2. **Capture**: Promote the winning structure into production XAML with MVVM bindings. Move the losing prototypes and switcher onto a throwaway branch (`prototype/<name>`) as a primary source.

## Anti-patterns

- **Cosmetic-only variants**: Tweaking only font weights or margins isn't prototyping. Real variants disagree about structure, layout, and user workflow.
- **Wiring to live Revit database mutations**: Keep UI prototypes read-only or bound to mock data objects. Real transactions belong in production implementations.
- **Shipping prototype XAML unpolished**: Clean up binding paths, resources, and accessibility when promoting the winner into production.
