## ADR 007 - Decoupled Instantiation via Registry Delegates

Status: Deprecated (Applicable to RevitSerialization module only. Superseded by ADR 011)
Element instantiation logic was embedded directly into the PropertyRegistry. Constructor dependencies are now explicitly handled by their respective Translators.
