---
description: Regulates the maintenance, modification, and initial generation behavior of the Project Atlas documentation.
globs: "project_atlas.md"
---

# Project Atlas Management Policy

1. You are explicitly aware of the file `docs/project_atlas.md` located in the `docs/` subdirectory of the project root.
2. **Active Coding Phase:** You are strictly forbidden from modifying this file while actively iterating on code features, running Git commit loops, or executing tests.
3. **Initial Bootstrapping Exception:** If `project_atlas.md` is detected to be empty (0 bytes) or lacks a structural index, you are explicitly authorized and commanded to bootstrap it. Perform a comprehensive initial workspace scan of the C# source directory, map out the existing classes, and write the baseline atlas map before you begin any feature coding.
4. **Feature Completion Phase:** You are authorized to write to or modify `project_atlas.md` when the custom workspace workflow trigger `/finalize` is executed by the user at the end of a feature branch.
5. **Content Requirement:** The file must concisely document the high-level folder structure, key classes, active Revit API namespaces, and a registry of existing commands to serve as a clean, predictable map for the Strategic Planner.