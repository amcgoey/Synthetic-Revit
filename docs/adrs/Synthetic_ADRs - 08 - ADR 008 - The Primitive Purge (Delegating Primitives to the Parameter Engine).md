## ADR 008 - The Primitive Purge (Delegating Primitives to the Parameter Engine)

Status: Deprecated (Applicable to RevitSerialization module only. Superseded by ADR 011)
We explicitly excluded native primitive properties from the compiled PropertyRegistry if mirrored by the parameter engine. This logic was tied to the Fluent API and is replaced by the "Filter on Extract" strategy.
