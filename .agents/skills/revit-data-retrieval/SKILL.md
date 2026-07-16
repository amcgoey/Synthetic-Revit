---
name: revit-data-retrieval
description: Describes tactics for optimizing FilteredElementCollectors. Use when querying elements from the Revit database.
---

# Revit Data Retrieval Playbook (`revit-data-retrieval`)

Use these rules to optimize database queries and minimize memory hydration when retrieving elements using `FilteredElementCollector`.

## 1. The Three-Phase Query Protocol

Always stack filters from fastest (native C++) to slowest (managed C#) to prevent premature object hydration.

```csharp
// GOOD: Stacking Quick Filters -> Slow Filters -> Managed LINQ
var walls = new FilteredElementCollector(doc)
    .OfClass(typeof(Wall))                  // Phase 1: Quick Filter (C++)
    .WhereElementIsNotElementType()         // Phase 1: Quick Filter (C++)
    .WherePasses(parameterFilter)           // Phase 2: Slow Filter (C++)
    .Cast<Wall>()                           // Phase 3: Managed Boundary
    .Where(w => w.Name.StartsWith("EXT_")); // Phase 3: Managed LINQ (C#)
```

### Phase 1: Quick Filters (Native C++)
Apply quick filters immediately upon collector initialization. These execute in Revit's internal C++ memory and do not hydrate C# objects.
* Use `OfClass()` and `OfCategory()`.
* Use `WhereElementIsNotElementType()` or `WhereElementIsElementType()`.
* Apply other native quick filters like `WherePasses(ElementFilter)` where applicable.

### Phase 2: Slow Filters (Native C++)
Apply parameter-driven native filters (`ElementParameterFilter`) only after Phase 1 filters have already narrowed down the element pool. These execute in C++ but must open elements to read parameter data.

### Phase 3: Managed Queries (C# Memory)
Casting (`.Cast<T>()`, `.OfType<T>()`) and managed LINQ queries (`.Where()`, `.First()`, etc.) are **strictly prohibited** at the beginning of the collector chain. They must only run at the very end of the chain after the element pool has been heavily restricted by Phase 1 and Phase 2.

## 2. Parameter Queries Optimization

* **Large Pools:** Always use native `ElementParameterFilter` (Phase 2) when searching an unconstrained or large database pool for parameter values.
* **Small Pools:** You may use C# managed property evaluations (e.g. `el.LookupParameter("Name")?.AsString()`) in Phase 3 LINQ **only** if the element pool has already been filtered down to a small set (under ~100 elements) by Phase 1 filters.

## 3. Context-Aware Scoping

Match the collector's constructor scope to the business requirements of the command:
* **Active View / Visible Elements:** If the feature only affects visible elements or the current view, use the view-scoped constructor:
  ```csharp
  var collector = new FilteredElementCollector(doc, activeView.Id);
  ```
* **Document-Wide:** Use `new FilteredElementCollector(doc)` when the operation requires processing elements across the entire project (including invisible or unplaced elements).
