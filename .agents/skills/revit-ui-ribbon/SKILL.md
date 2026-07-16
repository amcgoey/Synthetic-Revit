---
name: revit-ui-ribbon
description: Tactics for UI ribbon commands. Use when modifying or building the Revit ribbon interface.
---

# Revit UI Ribbon Playbook (`revit-ui-ribbon`)

Use these rules to build and maintain the Revit application ribbon programmatically from a single JSON configuration source.

## 1. Unified UI Ribbon Mapping

To keep the UI configuration stable and easily editable across all Revit target versions:
* **No Preprocessor Directives:** Preprocessor compilation directives (like `#if REVIT2025`) are **strictly prohibited** inside the ribbon assembly logic.
* **Configuration Location:** Define the ribbon layout in a loose JSON file named `ribbon_config.json` inside the `Assets/` directory next to the compiled assembly.

## 2. Structured JSON Schema

The `ribbon_config.json` file must follow a structured panel-and-item layout using type discriminators for pushbuttons and stacked groupings:

```json
{
  "tab_name": "Synthetic",
  "panels": [
    {
      "name": "Views & Tags",
      "items": [
        {
          "type": "PushButton",
          "name": "AutonumberViews",
          "text": " Autonumber\nViews ",
          "class": "Synthetic.Modules.ViewManagement.Commands.ViewsAutoNumber",
          "tooltip": "Autonumber Views on the Active Sheet",
          "large_image": "autonumber_32.png",
          "image": "autonumber_16.png"
        },
        {
          "type": "StackedGroup",
          "sub_items": [
            {
              "type": "PushButton",
              "name": "ManageTemplates",
              "text": "Manage Templates",
              "class": "Synthetic.Modules.AutoTagger.Commands.CmdManageTemplates",
              "tooltip": "Open control panel to view templates.",
              "large_image": "autotag_32.png",
              "image": "autotag_16.png"
            }
          ]
        }
      ]
    }
  ]
}
```

## 3. Placeholder Recovery Pattern

To ensure the add-in boots reliably even if configuration assets or assembly class structures are out of sync:

* **Missing Icon Recovery:** If an icon image file specified in the JSON cannot be found in the `Assets/` directory, load a default fallback placeholder image (`placeholder_16.png` or `placeholder_32.png`) instead of throwing an exception.
* **Missing Class Recovery:** If a command class cannot be resolved within the assembly at runtime:
  1. Add the button to the panel.
  2. Programmatically disable the button (`button.Enabled = false`).
  3. Override the tooltip to display a prominent warning: `[WARNING] Command class not found: <ClassPath>`.
