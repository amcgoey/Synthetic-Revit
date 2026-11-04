---
name: resolving-merge-conflicts
description: "Resolve an in-progress git merge or rebase conflict. Use when git merge or rebase pauses with conflicts."
---

1. **See the current state** of the merge or rebase. Check git status, recent commit history on both branches, and the list of conflicting files.

2. **Find the primary sources** for each conflict. Understand deeply why each change was made and what the original intent was. Read commit messages, PRs, and the originating tickets or specs.

3. **Resolve each hunk.** Preserve both intents where possible. Where incompatible, pick the one matching the merge's stated goal and note the trade-off. Do **not** invent new behaviour. Always resolve; never `--abort`.

4. Discover the project's **automated checks** and run them. In this repo, compile the solution (`MSBuild.exe`) and run `revit-tests` starting with fast headless logic tests (`python .agents/skills/revit-tests/scripts/test_executor.py --version Logic`), followed by target Revit version tests (`python .agents/skills/revit-tests/scripts/test_executor.py --version 2026`). Fix anything the merge broke before proceeding.

5. **Finish the merge or rebase.** Stage resolved files and commit. If rebasing, continue the rebase (`git rebase --continue`) until all commits are rebased.
