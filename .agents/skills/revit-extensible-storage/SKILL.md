---
name: revit-extensible-storage
description: Requirements for single-field JSON payload extensible storage schemas and progressive migration. Use when writing database serialization or settings persistence code.
---

# Revit Extensible Storage Playbook (`revit-extensible-storage`)

Use these rules to persist settings or complex payloads inside the Revit document database using Extensible Storage.

## 1. Single-Field JSON Payload Standard

To keep database schemas stable and minimize database schema modifications, wrap all configuration data in JSON payloads:

* **Field Signature:** Every settings schema must define exactly one `string` field named `JsonData` to store the serialized JSON payload (along with an optional `ModuleKey` string if storing multiple settings under one schema).
* **JSON-Level Upgrades:** Prefer handling configuration additions, renames, or default value updates purely within C# (using `Newtonsoft.Json` settings, converters, or ignore-missing attributes) rather than altering the Revit schema structure.

## 2. Algorithmic GUID Generation

When a new Revit schema is required, generate its Guid using this deterministic algorithm:

1. Format a string combining the target schema class name and the current universal timestamp: `ClassName_YYYYMMDD_HHMMSS`.
2. Compute the **MD5 hash** of this string (encoded as UTF-8) to produce a 128-bit (16-byte) hash.
3. Pass the hash bytes to the `new Guid(byte[])` constructor.
4. Hardcode the calculated Guid into your C# source file as a static constant (e.g. `private static readonly Guid SchemaGuid = new Guid("...")`).

## 3. When Revit Schema Migration is Needed

Revit schema migrations (creating a new schema and running a migration runner) are rare and must **only** be performed when:
* The Revit schema structure itself changes (e.g. adding new fields to the `SchemaBuilder`, or changing read/write access permissions).
* The storage container architecture changes (e.g. moving from `DataStorage` elements to another element category).

## 4. Progressive Migration & Dual Cleanup

If a Revit schema migration is necessary:

1. **Sequential Runner:** Implement an explicit migration runner that maps data sequentially: V1 -> V2 -> V3. Read the V1 JSON string, deserialize/map it, and save the updated JSON payload to the V2 schema.
2. **Dual Cleanup Protocol:** To prevent database bloat, the migration runner must delete the legacy data immediately after a successful transfer:
   * Delete the legacy `Entity` from the host elements using `element.DeleteEntity(legacySchema)`.
   * If the host element is a dedicated `DataStorage` element containing no other active entities, delete the `DataStorage` element from the document.
   * Erase the legacy schema metadata definition from the document by invoking `Schema.EraseSchemaAndAllEntities(legacySchema)`.
