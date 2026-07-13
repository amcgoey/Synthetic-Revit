## ADR 010 - Hybrid Dispatcher/Translator Architecture

Status: Accepted
The RevitSerialization generic PropertyRegistry and GenericElementModel caused configuration bloat and destroyed domain expressiveness. We decided to revert to strongly-typed RevitDOM POCOs, enforce them as 100% pure C# data containers, and orchestrate them via an internal Dispatcher pipeline.
Consequences: * This provides a deep public seam (ByRevit, Analyze, ToRevit) offering massive leverage to callers.
- Translators (IModelTranslator<TRev, TModel>) are explicitly registered to avoid reflection penalties.
- The Dispatcher uses a "One-to-Many" mapping (e.g., routing WallType, FloorType, and RoofType to a single HostObjTypeTranslator), preserving the immense efficiency of the legacy RevitDOM inheritance model.
