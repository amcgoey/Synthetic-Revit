# **Deep Modules**

From "A Philosophy of Software Design":

**Deep module** = small interface + lots of implementation.

```

┌─────────────────────┐
│   Small Interface   │ ← Few public methods, simple C# DTO parameters
├─────────────────────┤
│                     │
│ Deep Implementation │ ← Complex Revit API filtering, geometry math, and transactions hidden
│                     │
└─────────────────────┘

```

**Shallow module** = large interface + little implementation (avoid).

```

┌─────────────────────────────────┐
│         Large Interface         │ ← Many methods, exposing raw Revit DB Elements
├─────────────────────────────────┤
│       Thin Implementation       │ ← Just passes through directly to the Revit API
└─────────────────────────────────┘

```

## **When designing interfaces for the Revit Addin, ask:**

* Can I reduce the number of public methods?  
* Can I simplify the parameters? (e.g., passing a pure C# string ID or DTO instead of a raw Revit Element or Document)  
* Can I hide more of the complex Revit database querying, transaction handling, or geometry extraction completely inside the module?
