## ADR 006 - Stateless Translators via Static Method Groups

Status: Deprecated (Applicable to RevitSerialization module only. Superseded by ADR 010)
We handled complex embedded value-objects by passing static C# Method Groups into the .Embed() registry configuration. This is replaced by the Pipeline Approach using specific IModelTranslator classes.
