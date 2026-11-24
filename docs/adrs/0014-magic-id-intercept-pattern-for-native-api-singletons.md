# ADR 0014 - Magic ID Intercept Pattern for Native API Singletons

Status: Accepted

## Context
The Revit API uses stable negative IDs for certain built-in singleton elements (like `-3000010` for the `"Solid"` line pattern or `-1` for `""` materials), but passing these IDs to `doc.GetElement(id)` returns `null`, causing standard resolution pipelines to fail.

## Decision
We implement a "Magic ID" intercept pattern directly inside the identity resolver (`RevitIdentityService`) that catches these specific IDs (or their string equivalents) and short-circuits the search, immediately returning the native API property (e.g., `LinePatternElement.GetSolidPatternId()`).

## Consequences
This abstracts the API's resolution behavior away from the translators, keeping `CategoryTranslator` pure and boilerplate-free while allowing JSON payloads to retain clean, human-readable labels like `"Solid"`.
