---
name: revit-ui-ribbon-intelligence
description: Governs the unified UI ribbon mapping and JSON schema generation for Revit front-end architecture.
---

# RevitUIRibbonManager Architectural Playbook

You must strictly follow these rules to maintain a stable, single-source front-end architecture.

## 1. Unified UI Ribbon Mapping (`RibbonManager`)

Because user interface elements are structurally stable across all targeted release years (2022-2027), you must enforce a flat, single-source JSON schema layout for all UI configurations. 

* **No Conditional UI Branches:** You are strictly forbidden from writing version-conditional preprocessor branches (e.g., `#if REVIT2025`) inside the ribbon configuration map. 
* **Programmatic Construction:** You must parse the flat JSON template file to programmatically build tabs, panels, and pushbuttons inside the shared execution entry assembly.
* **Separation of Concerns:** Maintain a strict architectural boundary between UI rendering logic (handled here) and backend database modification rules (handled by the Extensible Storage schemas).
