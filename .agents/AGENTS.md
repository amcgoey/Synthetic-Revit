# Project Rules

## Documentation Links
- **Relative Links:** Always use relative links (relative to the repository root or the document's parent directory) for all markdown files (`.md`) inside the repository. Never use absolute `file:///` URLs or local user paths (such as `C:\Users\...`).

## Agent skills

### Issue tracker

GitHub Issues. See [issue-tracker.md](../docs/agents/issue-tracker.md).

### Domain docs

Single-context layout. See [domain.md](../docs/agents/domain.md).

### File editing

- **Tool constraint:** Always use direct, built-in file-editing tools (`replace_file_content`, `multi_replace_file_content`, `write_to_file`) to edit code files. Do **not** write or execute Python, PowerShell, or command-line scripts to perform search-and-replace, edit code, or update files.

