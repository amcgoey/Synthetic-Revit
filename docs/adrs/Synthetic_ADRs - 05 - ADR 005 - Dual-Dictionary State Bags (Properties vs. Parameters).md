## ADR 005 - Dual-Dictionary State Bags (Properties vs. Parameters)

Status: Deprecated (Applicable to RevitSerialization module only. Superseded by ADR 010)
We decided to split GenericElementModel data into PropertyState and ParameterState dictionaries. This flattened the domain model too much, reducing readability and architectural leverage compared to explicitly typed POCOs.
