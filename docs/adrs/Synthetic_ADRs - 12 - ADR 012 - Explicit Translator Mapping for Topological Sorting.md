## ADR 012 - Explicit Translator Mapping for Topological Sorting

Status: Accepted
The ImportExecutionRunner previously scavenged the PropertyRegistry to build its dependency graph. With the registry archived, we needed a new way to resolve dependencies without relying on fragile reflection magic. We decided to use Explicit Translator Mapping, driven by a hardcoded, bottom-up DAG execution hierarchy (Primitives -> Foundations -> Annotations -> Categories -> System Families -> Views).
Consequences:
- The execution order is deterministic and transparent.
- It prevents false circular dependencies by strictly adhering to the architectural dependency chain established by the legacy ModelsToSerialize dictionary order.
