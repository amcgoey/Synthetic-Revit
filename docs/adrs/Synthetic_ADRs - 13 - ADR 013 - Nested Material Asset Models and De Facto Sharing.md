## ADR 013 - Nested Material Asset Models and De Facto Sharing

Status: Accepted
Context: The native Revit database treats Material Assets (Structural, Thermal, Appearance) as independent, globally shared elements (PropertySetElement, AppearanceAssetElement). The legacy RevitDOM architecture mirrored this 1:1, resulting in disjointed JSON payloads where materials only stored reference IDs, and completely missed extracting visual Appearance assets because their data is buried in an AssetProperty tree rather than standard parameters.
Decision: We decided to break 1:1 database parity and model Material Assets as nested objects (AppearanceAssetModel, StructuralAssetModel, ThermalAssetModel) directly within the MaterialModel POCO. To reconcile this with Revit's independent element structure, the internal MaterialAssetEngine will employ a "Name-Matching Fallback Protocol" during injection.
Consequences:
- Pros: Greatly improves JSON readability and encapsulation (a Material's definition is fully self-contained in the payload). Captures critical rendering data previously lost.
- Cons: The engine must manage the discrepancy between nested C# data and flat native database elements.
- Mitigation: The Name-Matching fallback creates "de facto" shared assets, preventing database bloat by reusing existing assets of the same name rather than creating new independent elements for every material.
- DAG Impact: Material Assets are removed from the top-level topological sorting (DAG) as they are now resolved entirely within the Material node's execution pipeline.
