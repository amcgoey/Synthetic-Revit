# Project Rules

## Context Pointers
- **Coding standards:** Read [CODING_STANDARDS.md](../CODING_STANDARDS.md) when authoring, refactoring, or reviewing C#, XAML, ribbon/settings configs, or tests.
- **Domain vocabulary:** Consult [GLOSSARY.md](../GLOSSARY.md) when naming features, modules, types, tests, or issues.
- **Architecture decisions:** Check [docs/adrs/](../docs/adrs/) before modifying an existing subsystem's architecture or lifecycle.
- **Test execution:** Follow [revit-tests](skills/revit-tests/SKILL.md) to execute **Logic Tests**, **Architecture Fitness Tests**, or live **Revit Tests**.

## Documentation Links
- **Relative Links:** Always use relative links (relative to the repository root or the document's parent directory) for all markdown files (`.md`) inside the repository. Never use absolute `file:///` URLs or local user paths (such as `C:\Users\...`).

## Agent skills
### Issue tracker
GitHub Issues. See [issue-tracker.md](../docs/agents/issue-tracker.md).
### Domain docs
Single-context layout. See [domain.md](../docs/agents/domain.md).
### File editing
- **Tool constraint:** Always use direct, built-in file-editing tools (`replace_file_content`, `multi_replace_file_content`, `write_to_file`) to edit code files. Do **not** write or execute Python, PowerShell, or command-line scripts to perform search-and-replace, edit code, or update files.

