## ADR 017 - Generic POCO Consolidation for Parameter-Driven Types

Status: Accepted
Context: We frequently encounter Revit system families (like TextNoteType, ModelTextType, and SpotDimensionType) that must be instantiated via the .Duplicate() creation API, but whose properties are entirely managed by the generic ParameterEngine. Previously, we considered creating empty, strongly-typed POCOs (e.g., TextNoteTypeModel : ElementTypeModel) simply to force them into distinct JSON dictionaries for clean UI organization and schema purity.
Decision: We decided to abandon empty boilerplate POCOs. For elements that do not require explicit C# property elevation, we will universally route them to the base ElementTypeModel (or ElementModel). To maintain domain expressiveness and separate JSON schemas, we will sort them into distinct dictionaries inside ModelsToSerialize.cs by evaluating their model.Class string during deserialization. The TemplateDuplicatingTranslator will similarly use this string to dynamically cast the duplicated element.
Consequences:
- Pros: Drastically reduces boilerplate code (POCOs and Translators). Maximizes the leverage of the generic parameter engine and our base duplicating translator.
- Cons: The sorting logic in ModelsToSerialize.DeserializeByJson() becomes slightly heavier as it must inspect the .Class string for generic models to route them correctly.

