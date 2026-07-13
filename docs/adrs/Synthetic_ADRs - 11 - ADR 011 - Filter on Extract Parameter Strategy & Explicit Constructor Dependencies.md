## ADR 011 - "Filter on Extract" Parameter Strategy & Explicit Constructor Dependencies

Status: Accepted
The previous "Extract All, Filter on Write" parameter strategy generated noisy JSON payloads, and a generic parameter engine cannot reliably provide constructor dependencies (e.g., LinePatternElement segments) since that data often isn't exposed as standard parameters. We decided to use a "Filter on Extract" strategy where the generic Parameter Engine aggressively drops read-only or empty parameters when exporting templates (isTemplate == true).
Consequences:
- Constructor dependencies bypass the generic parameter engine entirely. They must be explicitly modeled in the POCOs and handled by the specific IModelTranslator. This trades generic "magic" for absolute locality and explicit data mapping.
