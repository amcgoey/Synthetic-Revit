---
name: docs-aggregate
description: How the codebase aggregation script (aggregate.py) resolves source files based on configuration. Use when bundling codebase directories into markdown files.
---

# Codebase Aggregation Playbook (`docs-aggregate`)

Use this playbook to aggregate codebase source directories into structured Markdown files for cloud documentation.

## 1. Stand-alone Aggregation Command

To run the codebase aggregation pipeline on its own:
```powershell
python .agents/skills/docs-aggregate/scripts/aggregate.py
```

## 2. Configuration Structure

The aggregation is driven by `aggregate_config.json` (located in `Revit API Synthetic v2 Support/auth/`). It maps specific source directories to target markdown files:

```json
{
  "aggregations": [
    {
      "source_dir": "Revit API Synthetic v2\\.agents",
      "output_file": "Revit API Synthetic v2\\docs\\source_code\\Synthetic_agents.md",
      "extensions": [".md", ".json", ".py"]
    },
    {
      "source_dir": "Revit API Synthetic v2\\src\\SyntheticShared\\Core",
      "output_file": "Revit API Synthetic v2\\docs\\source_code\\Synthetic_src_Foundation.md"
    }
  ]
}
```

## 3. Aggregation Engine Execution Rules

When the script runs, it resolves files using the following rules:

* **Directory Exclusions:** Recursively walks the target `source_dir` but automatically prunes directories defined in `DEFAULT_EXCLUDES` (e.g. `.git`, `bin`, `obj`, `packages`, `scratch`, `build`) to avoid junk file clutter.
* **Extension Filtering:** Only processes files matching allowed extensions (defaults to `.cs`, `.xaml`, `.md` unless specified per-aggregation).
* **Bundling Append Mode:** If multiple source directories map to the same `output_file`, the script overwrites the file on the first match, and then switches to append mode (`'a'`) to bundle subsequent directories together.
* **Markdown Formatting:** Wraps each file's contents inside a Markdown code block with its relative path header and the appropriate language-specific tag (e.g. `csharp`, `xml`):
  
  ```markdown
  ### File: SyntheticShared/Core/App.cs
  ```csharp
  // Source contents...
  ```
  ```
