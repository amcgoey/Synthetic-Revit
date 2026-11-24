# ADR 0003 - Symmetrical Topological Dependency Sorter

Status: Accepted (Implementation modified by [ADR 0012](0012-explicit-translator-mapping-for-topological-sorting.md))
To prevent serialization graph explosions and circular dependency loops, we enforce a strict separation between embedded value-objects (e.g., Color, CompoundStructure) and independent referenced elements (e.g., Material, FillPatternElement). We use a two-pass ImportExecutionRunner that acts as a topological dependency sorter to ensure primitive database elements are instantiated before complex hosts.
