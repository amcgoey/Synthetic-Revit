## ADR 004 - Property Execution via Pure C# Delegates

Status: Deprecated (Applicable to RevitSerialization module only. Superseded by ADR 010)
The PropertyRegistry used a Fluent API with strongly-typed C# delegates to guarantee maximum execution speed. This was abandoned due to the excessive boilerplate required to map thousands of properties manually.
