---
name: revit-ui-ribbon
description: Configure the JSON-driven Revit ribbon (ribbon_config.json), Core visibility filtering (RibbonItemFilter), and Addin startup loading or fallback recovery (RibbonManager, App). Use when adding or modifying ribbon tabs, panels, buttons, debug/version filters, command classes, or missing-icon/class recovery.
---

# Revit UI Ribbon Playbook (`revit-ui-ribbon`)

Configure the Revit ribbon declaratively in [`ribbon_config.json`](../../../src/Assets/ribbon_config.json) so layout edits require zero `#if REVIT202*` preprocessor directives across **Revit Versions**. Pure schema DTOs and filtering live in [`RibbonItemFilter.cs`](../../../src/Platform/Host/Core/RibbonItemFilter.cs) (`Synthetic.Core`), while Revit UI construction and fallback recovery live in [`RibbonManager.cs`](../../../src/Platform/Host/Addin/RibbonManager.cs) and [`App.cs`](../../../src/Platform/Host/Addin/App.cs) (`Synthetic<Year>`).

## 1. Dual-Seam Host Architecture (`Core/` + `Addin/`)

Following [ADR 0028](../../../docs/adrs/0028-hybrid-synthetic-core-assembly-and-colocated-partitions.md), both partitions share `namespace Synthetic.Platform.Host` while separating pure filtering from Revit API construction:

| Seam & Assembly | Source Files & Key Types | Responsibility |
| :--- | :--- | :--- |
| **`src/Platform/Host/Core/`** (`Synthetic.Core`, `net48;net8.0-windows8.0`) | [`RibbonItemFilter.cs`](../../../src/Platform/Host/Core/RibbonItemFilter.cs): `RibbonConfig`, `PanelConfig`, `RibbonItemConfig`, `RibbonItemFilter` | Depends solely on BCL + `Newtonsoft.Json` (zero `Autodesk.Revit.*` or WPF dependencies). Evaluates `ShouldIncludeItem(item, isDebug, activeVersion)` (excludes `DebugOnly` when `!isDebug`) and `IsVersionMatch(item, currentVersion)` (inclusive optional `MinVersion`/`MaxVersion` bounds). |
| **`src/Platform/Host/Addin/`** (`Synthetic<Year>`) | [`App.cs`](../../../src/Platform/Host/Addin/App.cs) (`App`), [`RibbonManager.cs`](../../../src/Platform/Host/Addin/RibbonManager.cs) (`RibbonManager`) | `App.OnStartup` registers settings and invokes `RibbonManager.Create(appControlled, _path)`. `RibbonManager` reads `Assets/ribbon_config.json` beside the payload assembly, determines `isDebug` (`#if DEBUG`) and `activeVersion` (`ControlledApplication.VersionNumber`, defaulting to `2026`), filters items via `RibbonItemFilter.ShouldIncludeItem`, and builds Revit controls. |

## 2. Asset Staging, Command Mapping, & JSON Schema

* **Authoritative Source & Staging:** Edit [`src/Assets/ribbon_config.json`](../../../src/Assets/ribbon_config.json) and store PNG icons in `src/Assets/`. [Directory.Build.targets](../../../src/Directory.Build.targets) (`CopyToOutputDirectory="PreserveNewest"`) and [Directory.Build.props](../../../src/Directory.Build.props) stage `src/Assets/` into `output/Synthetic/Payload/<Year>/Assets/ribbon_config.json` (see [`revit-build-deploy`](../revit-build-deploy/SKILL.md)).
* **Command Naming (`Synthetic.Features.<Slice>.Commands.*`):** Place command classes under `src/Features/<Slice>/Addin/Commands/` in namespace `Synthetic.Features.<Slice>.Commands` implementing `IExternalCommand`. Set `"class"` (and `"name"` on `PushButton` entries) to `Synthetic.Features.<Slice>.Commands.<CommandClass>` so `Assembly.GetExecutingAssembly().GetType(classPath)` resolves inside `Synthetic<Year>`. Use `"Synthetic.Split.<Group>"` for `SplitButton` `"name"` entries.
* **Schema & Item Builders:** `RibbonConfig` (`tab_name`, `panels`) defines `PanelConfig` entries (`name`, `items`, optional `slideout_items`). `RibbonManager` resolves `type` case-insensitively (`PushButton`, `StackedGroup`, `SplitButton`) and calls `panel.AddSlideOut()` only when at least one `slideout_items` entry passes `RibbonItemFilter.ShouldIncludeItem`.
  * `RibbonItemConfig` JSON fields: `type`, `name`, `text`, `class`, optional `tooltip`, `large_image` (32x32 PNG under `Assets/`), `image` (16x16 PNG under `Assets/`), `availability_class` (`IExternalCommandAvailability` type name), `debugOnly` (`bool`), `minVersion` (`int?`), `maxVersion` (`int?`), and `sub_items` (nested items for `StackedGroup` and `SplitButton`).
  * Keep `StackedGroup.sub_items` to `2–3` items (`StackedGroupBuilder` filters `sub_items` via `RibbonItemFilter.ShouldIncludeItem`, calls `panel.AddStackedItems` for `2` or `3` surviving items, falls back to `panel.AddItem` for `1`, and skips creation when `0` survive).

```json
{
  "tab_name": "Synthetic",
  "panels": [
    {
      "name": "Views & Tags",
      "items": [
        {
          "type": "PushButton",
          "name": "Synthetic.Features.ViewManagement.Commands.ViewsAutoNumber",
          "text": " Autonumber\nViews ",
          "class": "Synthetic.Features.ViewManagement.Commands.ViewsAutoNumber",
          "tooltip": "Autonumber Views on the Active Sheet",
          "large_image": "autonumber_32.png",
          "image": "autonumber_16.png"
        },
        {
          "type": "StackedGroup",
          "sub_items": [
            {
              "type": "PushButton",
              "name": "Synthetic.Features.AutoTagger.Commands.CmdManageTemplates",
              "text": "Manage Templates",
              "class": "Synthetic.Features.AutoTagger.Commands.CmdManageTemplates",
              "tooltip": "Open the Universal Auto-Tagger control panel to view, delete, import, or export tag templates.",
              "large_image": "autotag_32.png",
              "image": "autotag_16.png"
            },
            {
              "type": "PushButton",
              "name": "Synthetic.Features.ViewManagement.Commands.ViewAutoNumberConfig",
              "text": "Autonumber Config",
              "class": "Synthetic.Features.ViewManagement.Commands.ViewAutoNumberConfig",
              "tooltip": "Configure Autonumber View",
              "large_image": "autonumber_32.png",
              "image": "autonumber_16.png"
            }
          ]
        },
        {
          "type": "PushButton",
          "name": "Synthetic.Features.DetailItemFactory.Commands.CmdDetailItemFactory",
          "text": "Detail Item\nFactory",
          "class": "Synthetic.Features.DetailItemFactory.Commands.CmdDetailItemFactory",
          "tooltip": "Batch process selected elements into 2D Detail Item families via DWG tracing.",
          "large_image": "convert_32.png",
          "image": "convert_16.png",
          "minVersion": 2024
        }
      ]
    }
  ]
}
```

## 3. Startup Resilience & Fallback Recovery (`RibbonManager.cs`)

* **Missing Config / Unmatched Type:** `RibbonManager.Create` throws `FileNotFoundException` when `Assets/ribbon_config.json` is absent on disk, returns early when deserialized `RibbonConfig` is `null`, and skips items whose `type` is absent from `Builders`.
* **Missing Icon Recovery (`ResolveImagePath` + `LoadBitmapImage`):** For `large_image` (fallback [`placeholder_32.png`](../../../src/Assets/placeholder_32.png)) and `image` (fallback [`placeholder_16.png`](../../../src/Assets/placeholder_16.png)), `ResolveImagePath` selects the fallback filename when the requested name is null/empty or the target file is absent under `Assets/`, then loads a frozen, stream-closed bitmap (`BitmapCacheOption.OnLoad`) via [`BitmapImageLoader.Load`](../../../src/Platform/UI/Core/BitmapImageLoader.cs). Keep both placeholder PNGs in `src/Assets/`.
* **Missing Class Recovery (`ValidateCommandClass`):** After adding each standalone, stacked, or split `PushButton`, `RibbonManager` returns early if `button == null` or `classPath` is empty, otherwise inspects `Assembly.GetExecutingAssembly().GetType(classPath)`. When `type == null`, it keeps the button on the panel, sets `button.Enabled = false`, and sets `button.ToolTip = $"[WARNING] Command class not found: {classPath}"`.

## 4. Verification

Confirm completion via [`revit-tests`](../revit-tests/SKILL.md) after modifying `ribbon_config.json`, `RibbonItemFilter`, or `RibbonManager`:

1. **Headless Schema, Filter, & Roslyn Integrity Tests (`Logic`):** Run `python .agents/skills/revit-tests/scripts/test_executor.py --version Logic --filter "Ribbon"` so [`RibbonConfigTests`](../../../tests/Platform/Host/Logic/RibbonConfigTests.cs) and [`RibbonItemFilterTests`](../../../tests/Platform/Host/Logic/RibbonItemFilterTests.cs) pass across `net48;net8.0-windows8.0`, and run `--version Logic --filter "ArchitectureFitnessTests"` after `Core/`, `Addin/`, or `ribbon_config.json` edits to verify `FitnessRule.CoLocatedPartitionLayout` and `FitnessRule.RibbonConfigIntegrity` pass in [`ArchitectureFitnessTests`](../../../tests/Architecture/Logic/ArchitectureFitnessTests.cs).
2. **Live Revit Command Resolution (`Revit`):** Run `python .agents/skills/revit-tests/scripts/test_executor.py --version <Year> --filter "HostRibbonSmokeRevitTests"` so [`HostRibbonSmokeRevitTests`](../../../tests/Platform/Host/Revit/HostRibbonSmokeRevitTests.cs) confirms every `ribbon_config.json` entry resolves as a concrete `IExternalCommand` under `Synthetic.Features.<Slice>.Commands.*` and clears the `--assert-clean` journal gate (update the test's expected button count when adding or removing commands).
3. **Icons & Payload Staging:** Confirm every non-empty `image` and `large_image` referenced in `src/Assets/ribbon_config.json` exists in `src/Assets/` alongside `placeholder_16.png` and `placeholder_32.png`, and verify building stages `Assets/ribbon_config.json` to `Payload/<Year>/Assets/ribbon_config.json`.
