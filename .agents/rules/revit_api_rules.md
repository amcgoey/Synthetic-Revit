---
description: Constraints for multi-version Revit API development (2022-2027) during Ingestion and Critique.
globs: "*.cs"
---

# Revit API Multi-Version Guardrails (2022 - 2027)

Before writing any C# code or modifying existing files, you must cross-reference this deprecation matrix during your "Ingestion and Critique" phase. Flag any violations immediately.

## 1. Framework & Compilation Target Shifts
* **Revit 2022–2024:** Uses old .NET Framework 4.8.
* **Revit 2025–2027:** Runs on modern .NET 8.0 (Core). 
* **Constraint:** Do not use modern .NET Core-specific libraries/syntax (like advanced Span structures or newer system assembly methods) unless they are safely wrapped in `#if REVIT2025 || REVIT2026 || REVIT2027` conditional compilation directives.

## 2. Unit API Deprecations (The ForgeTypeId Shift)
* **Revit 2022+:** `DisplayUnitType` is completely removed. 
* **Constraint:** You must use `ForgeTypeId` for units, specs, and symbols. Access them via static properties on `SpecTypeId` or `UnitTypeId`.

## 3. Parameter Data Type Architecture
* **Revit 2022/2023:** `Definition.ParameterType` was deprecated and subsequently removed in 2024.
* **Revit 2024–2027:** Use `Definition.GetDataType()` which returns a `ForgeTypeId`.
* **Constraint:** Never generate raw `.ParameterType` evaluations. Use conditional definitions if older versions require a fallback to `ParameterType`.

## 4. Structural Structural Properties & Geometry Changes
* **Revit 2025/2026:** Internal changes to graphics pipelines and heavy depreciation of structural element structural properties methods.
* **Revit 2026/2027:** Heavy refactoring of `Mesh` and geometry vertex access tracking. Ensure `Mesh.Vertices` syntax is updated to modern indexing arrays if compiling for 2026+.