## ADR 009 - Exclusion of Physical Instance Serialization

Status: Accepted
We restrict the serialization pipeline strictly to Element Types, Base Classes, and Project Standards, explicitly abandoning the serialization of Physical Model Instances (e.g., Walls, Floors) and Canvas Elements. Attempting to serialize physical instances requires mapping complex spatial coordinates and topological host relationships, which would critically bloat the models and poison the clean "Styles and Standards" architecture.
