## ADR 002 - Metadata-Driven Serialization via Fluent Property Registry

Status: Deprecated (Applicable to RevitSerialization module only. Superseded by ADR 010)
We transitioned the RevitDOM serialization architecture to a singular, dynamic state container (GenericElementModel) governed by a compiled C# PropertyRegistry. This decision was meant to solve maintainability issues but resulted in configuration bloat and loss of domain expressiveness.
