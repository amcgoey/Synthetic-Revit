# Research Report: Revit Shadow Study Export & Multiply-Blend Compositing (Revit 2022–2026)

**Date:** 2026-11-19  
**Scope:** Autodesk Revit API (`Revit 2022`–`Revit 2026`), .NET Multi-Targeting (`net48` + `net8.0-windows8.0`), Image Export Registration, and Multiply-Blend Compositing  
**Primary Sources Verified:**
- Installed NuGet `Revit_All_Main_Versions_API_x64` XML documentation and assemblies (`2022.1.0`, `2023.0.0`, `2024.2.0`, `2025.0.0`, `2026.0.0`)
- Repo project configurations ([Synthetic2022.csproj](../../src/Synthetic2022/Synthetic2022.csproj), [Synthetic2023.csproj](../../src/Synthetic2023/Synthetic2023.csproj), [Synthetic2024.csproj](../../src/Synthetic2024/Synthetic2024.csproj), [Synthetic2025.csproj](../../src/Synthetic2025/Synthetic2025.csproj), [Synthetic2026.csproj](../../src/Synthetic2026/Synthetic2026.csproj), [SyntheticTests.Logic.csproj](../../tests/SyntheticTests.Logic/SyntheticTests.Logic.csproj)) and existing view/transaction code ([ViewTranslator.cs](../../src/SyntheticShared/RevitDOM/Translation/ViewTranslator.cs), [RibbonManager.cs](../../src/SyntheticShared/Core/RibbonManager.cs))
- Official Autodesk Revit API documentation (`help.autodesk.com`), Autodesk Community Revit API Forums (`forums.autodesk.com`), Microsoft Learn WPF / .NET 8 docs (`learn.microsoft.com`), and W3C Compositing and Blending Level 1 (`w3.org`)

---

## Executive Summary

Automating a multi-frame Shadow Study export and Multiply-blend compositor across Revit 2022–2026 is fully viable using a single shared codebase with **zero external NuGet dependencies** for imaging, provided several critical Revit API constraints and timezone/blending pitfalls are designed around upfront:

1. **No API toggle exists for "Cast Shadows" or "Ambient Shadows" (`REVIT-222419`):** `View.AreShadowsEnabled` does **not** exist in Revit 2022–2026. Direct programmatic toggling of Cast Shadows / Ambient Shadows is an unexposed Revit limitation tracked by Autodesk as `REVIT-222419`. Views can still have `DisplayStyle` (`HLR`, `Shading`, etc.), `ShadowIntensity` (`0–100`), `SunlightIntensity` (`0–100`), and `View.SunAndShadowSettings` mutated, while Cast Shadows must either be enabled by the user or applied via a pre-configured View Template.
2. **View Templates lock sun/shadow settings (`ViewTemplateId`):** When a view has a controlling template (`View.ViewTemplateId != ElementId.InvalidElementId`), `BuiltInParameter.GRAPHIC_DISPLAY_OPTIONS_SHADOWS` / `GRAPHIC_DISPLAY_OPTIONS_LIGHTING` / `GRAPHIC_DISPLAY_OPTIONS_SS_INTENSITY` may lock view overrides. Duplicating a temporary view (or detaching `ViewTemplateId = ElementId.InvalidElementId` on a working view) inside a `TransactionGroup` that calls `RollBack()` preserves all template-inherited graphics while unlocking `SunAndShadowSettings` without mutating the user's model or undo stack.
3. **UTC vs. Project Time & DST pitfalls in `SunAndShadowSettings`:**
   - `StartDateAndTime` and `EndDateAndTime` throw `ArgumentException` for `DateTimeKind.Unspecified`, always **return** `DateTimeKind.Utc`, and interpret `DateTimeKind.Local` against the **host OS timezone**, not Revit's `SiteLocation.TimeZone`.
   - `SiteLocation.ConvertFromProjectTime(DateTime)` expects `DateTimeKind.Unspecified` and **ignores DST** (`RevitAPI.xml`: *"Daylight savings time is not considered during this conversion"*).
   - `GetSunrise(DateTime)` and `GetSunset(DateTime)` exist across 2022–2026 and return **UTC**, with a documented `RevitAPI.xml` quirk: if `UsesDST == true`, returned values are adjusted for DST *"regardless of the value of the input date."*
   - `IsAfterSunrise` and `IsBeforeSunset` do **not** exist in Revit 2022–2026; compare UTC timestamps against `GetSunrise`/`GetSunset` or check `GetFrameAltitude(double)` (`> 0` radians) guarded by `IsFrameValid(double)`.
4. **Identical pixel dimensions & crisp Multiply compositing:**
   - `ImageExportOptions` defaults **both** `HLRandWFViewsFileType` and `ShadowViewsFileType` to `ImageFileType.JPEGMedium`; **both** must be set to `ImageFileType.PNG`.
   - Use `ExportRange.SetOfViews` (works without switching UI active view) or `CurrentView` (never `VisibleRegionOfCurrentView`), with `ZoomFitType.FitToPage`, fixed `FitDirection` + `PixelSize`, `SunAndShadowSettings.Visible = false` (hide Sun Path compass), and `CropBoxActive = true` for pixel-for-pixel alignment.
   - Use **WPF `System.Windows.Media.Imaging`** (`PngBitmapDecoder`, `FormatConvertedBitmap` to `PixelFormats.Bgra32`, `WriteableBitmap`, `PngBitmapEncoder`)—already referenced in both `net48` (2022–2024) and `net8.0-windows8.0` (2025–2026) projects—for fast, zero-dependency, conflict-free pixel compositing.

---

## 1. Sun & Shadow Settings (`SunAndShadowSettings`)

### 1.1 API Stability Across Revit 2022–2026

Diffing `RevitAPI.xml` across `2022.1.0`, `2023.0.0`, `2024.2.0`, `2025.0.0`, and `2026.0.0` confirms that `SunAndShadowSettings`, `SunAndShadowType`, `SiteLocation`, and `ImageExportOptions` are **completely stable** across all five releases, with only two relevant additions in Revit 2024+:
- **Revit 2024+:** `SunStudyTimeInterval` added sub-minute/minute values (`Minute`, `Seconds15`, `Seconds30`, `Seconds45`) alongside `Minutes15`, `Minutes30`, `Minutes45`, `Hour`, `Day`, `Week`, `Month` (all present since 2022).
- **Revit 2024+:** `ElementId.IntegerValue` and `new ElementId(int)` were deprecated in 2024 and **removed in Revit 2026** in favor of `ElementId.Value` (`long`) and `new ElementId(long)`. However, `new ElementId(BuiltInParameter)` exists in all versions 2022–2026 (`RevitAPI.xml`). *(Repo convention for numeric IDs uses `#if REVIT2022 || REVIT2023` as seen in [ViewTranslator.cs](../../src/SyntheticShared/RevitDOM/Translation/ViewTranslator.cs#L130-L134).)*

### 1.2 View Type Support (`View.SunAndShadowSettings`)

Per `RevitAPI.xml` (`P:Autodesk.Revit.DB.View.SunAndShadowSettings`):
> *"The sun and shadow settings assigned to the view for shadow calculation and rendering. Returns: The sun and shadow settings, or null for views which don't allow this (such as schedules)."*

| View Category | `ViewType` / Class | `SunAndShadowSettings` | Notes |
|---|---|---|---|
| Floor Plan / Site Plan | `ViewType.FloorPlan` (`ViewPlan`) | Non-null | Site Plans in Revit use `ViewPlan` with `ViewType.FloorPlan` and a higher view range. |
| Ceiling / Area / Structural Plan | `CeilingPlan`, `AreaPlan`, `EngineeringPlan` (`ViewPlan`) | Non-null | Supports sun settings when `DisplayStyle` allows shadows (`HLR`, `Shading`, etc.). |
| 3D View / Walkthrough | `ViewType.ThreeD` (`View3D`) | Non-null | Standard orthographic and perspective 3D views support full solar studies. |
| Section / Elevation / Detail | `Section`, `Elevation`, `Detail` (`ViewSection`) | Non-null | Supports sun settings and shadows. |
| Drafting / Legend / Sheet / Schedule / Reports | `DraftingView`, `Legend`, `DrawingSheet`, `Schedule`, `ColumnSchedule`, `PanelSchedule`, `*Report`, `*Browser` | `null` (or non-displayable) | Guard with `!view.IsTemplate && view.HasDisplayStyle() && view.SunAndShadowSettings != null`. |

**Global vs. Per-View Settings (`SharesSettings`):**
Per `RevitAPI.xml` (`T:Autodesk.Revit.DB.SunAndShadowSettings` and `P:Autodesk.Revit.DB.SunAndShadowSettings.SharesSettings`):
- Every graphical view receives a per-view `SunAndShadowSettings` element (`ViewSpecific == true`, `OwnerViewId == view.Id`).
- If `sunSettings.SharesSettings == true`, modifying its properties mutates the shared global state across all views linked to global settings. Setting `sunSettings.SharesSettings = false` (inside the temporary transaction) isolates mutations to that view only, and rolling back the outer `TransactionGroup` reverts everything.

### 1.3 View Templates Controlling Shadows & Sun Settings

When `view.ViewTemplateId != ElementId.InvalidElementId`, parameters included in the template cannot be changed directly on the view (`RevitAPI.xml`, `P:Autodesk.Revit.DB.View.ViewTemplateId`).

**Inspecting Controlled Parameters:**
1. Retrieve the template `View`: `View? template = doc.GetElement(view.ViewTemplateId) as View;`
2. `template.GetTemplateParameterIds()` returns all parameter `ElementId`s controllable by that template type.
3. `template.GetNonControlledTemplateParameterIds()` returns the subset **unchecked** ("not included") in the template UI.
4. Relevant `BuiltInParameter` IDs (verified in `RevitAPI.xml` 2022–2026):
   - `BuiltInParameter.GRAPHIC_DISPLAY_OPTIONS_SHADOWS` (`"Shadows"` — Cast Shadows & Ambient Shadows)
   - `BuiltInParameter.GRAPHIC_DISPLAY_OPTIONS_LIGHTING` (`"Lighting"` — Sun Settings, Sun & Ambient Light)
   - `BuiltInParameter.GRAPHIC_DISPLAY_OPTIONS_SS_INTENSITY` (`"Sun and Shadow Intensity"`)
   - `BuiltInParameter.GRAPHIC_DISPLAY_OPTIONS_MODEL` (`"Model Display"` — Visual Style, Smooth Lines, Silhouettes, Transparency)
   - `BuiltInParameter.VIEW_GRAPH_SUN_PATH` (`"Sun Path"`)

Check if a parameter is locked by the template across Revit 2022–2026 without `#if` directives using `new ElementId(BuiltInParameter)`:
```csharp
var lightingParamId = new ElementId(BuiltInParameter.GRAPHIC_DISPLAY_OPTIONS_LIGHTING);
bool isLightingLocked = view.ViewTemplateId != ElementId.InvalidElementId
    && doc.GetElement(view.ViewTemplateId) is View template
    && template.GetTemplateParameterIds().Contains(lightingParamId)
    && !template.GetNonControlledTemplateParameterIds().Contains(lightingParamId);
```

**Handling Template-Locked Views Safely:**
- **Never** mutate the shared `ViewTemplate` directly for a temporary export—that affects every other view assigned to the template and causes unnecessary regeneration across the project.
- **Recommended pattern (Duplicate + Detach inside `TransactionGroup.RollBack()`):**
  1. Check `view.CanViewBeDuplicated(ViewDuplicateOption.Duplicate)`.
  2. Inside a `TransactionGroup` (`tg.Start()`), duplicate the view (or operate on the target view directly inside a rolled-back `TransactionGroup`). Note that `view.Duplicate(...)` preserves `ViewTemplateId` on the clone; setting `exportView.ViewTemplateId = ElementId.InvalidElementId` detaches the template while retaining all visual/visibility/shadow settings inherited from that template as unlocked local properties on `exportView`.
  3. Set `exportView.SunAndShadowSettings.SharesSettings = false` and `exportView.SunAndShadowSettings.Visible = false` (hides the Sun Path compass widget if visible).
  4. Step through timestamps, committing each inner `Transaction` before `doc.ExportImage(...)`.
  5. Call `tg.RollBack()` at the end—the temporary view and all temporary property changes vanish with zero impact on the model or Undo history.
- *Alternative (`EnableTemporaryViewPropertiesMode`):* `View.EnableTemporaryViewPropertiesMode(view.Id)` (documented in `RevitAPI.xml` since 2014) enables Temporary View Properties mode without applying a different template, allowing temporary overrides without changing `ViewTemplateId`. However, duplicating/detaching inside a rolled-back `TransactionGroup` works reliably with `ExportRange.SetOfViews` even for non-active views.

### 1.4 `SunAndShadowType`, Date/Time, Timezone, and DST Gotchas

**Exact `SunAndShadowType` Enum Values** (`RevitAPI.xml` 2022–2026):
- `SunAndShadowType.StillImage` — single point in time *(Note: named `StillImage`, **not** `SingleDay`)*
- `SunAndShadowType.OneDayStudy` — single day range *(Note: named `OneDayStudy`, **not** `SingleDay`)*
- `SunAndShadowType.MultiDayStudy` — multi-day range *(Note: named `MultiDayStudy`, **not** `MultiDay`)*
- `SunAndShadowType.Lighting` — fixed azimuth/altitude lighting mode (ignores date/time)

For frame-by-frame shadow study export, set `SunAndShadowType = SunAndShadowType.StillImage` (or set `SunriseToSunset = false` before modifying `StartDateAndTime`, because `RevitAPI.xml` explicitly warns: *"Note that Revit may ignore this value if the SunriseToSunset flag is set"*).

**Critical `DateTime` / Timezone / DST Rules (`RevitAPI.xml`):**
1. **`DateTimeKind.Unspecified` is rejected:** Setting `StartDateAndTime` or `EndDateAndTime` with `DateTimeKind.Unspecified` throws `Autodesk.Revit.Exceptions.ArgumentException`.
2. **Getters always return UTC:** `StartDateAndTime`, `EndDateAndTime`, `GetFrameTime(double)`, `GetSunrise(DateTime)`, and `GetSunset(DateTime)` all return `DateTimeKind.Utc`.
3. **Avoid `DateTimeKind.Local` trap:** `StartDateAndTime` accepts `DateTimeKind.Local`, but converts it using the **local Windows OS timezone** (`TimeZoneInfo.Local`), **not** the Revit project's `SiteLocation.TimeZone`. If the user's workstation is in EST (`UTC-5`) and the Revit site is in Seattle (`UTC-8`), passing `DateTimeKind.Local` produces a 3-hour shadow error.
4. **`SiteLocation.ConvertFromProjectTime` ignores DST:** `SiteLocation.ConvertFromProjectTime(DateTime)` requires `DateTimeKind.Unspecified` (throwing `ArgumentException` otherwise) and explicitly documents in `RevitAPI.xml`: *"Daylight savings time is not considered during this conversion."*
5. **Safe Project Wall-Clock $\rightarrow$ UTC Conversion:**
   ```csharp
   // Read the effective offset stored on the view's SunAndShadowSettings:
   // TimeZone is hours from GMT (-12.0 .. +12.0); UsesDST adds +1.0 hour when enabled.
   double effectiveOffsetHours = sunSettings.TimeZone + (sunSettings.UsesDST ? 1.0 : 0.0);
   DateTime targetUtc = DateTime.SpecifyKind(
       projectWallClockTime.AddHours(-effectiveOffsetHours),
       DateTimeKind.Utc);
   sunSettings.StartDateAndTime = targetUtc;
   ```
   *(Verify `sunSettings.TimeZone` vs `doc.SiteLocation.TimeZone` and `sunSettings.UsesDST` match project expectations; `SunAndShadowSettings.CalculateTimeZone(lat, lon)` exists if geographic coordinates were edited programmatically, though `RevitAPI.xml` warns boundary cases may require manual `SiteLocation.TimeZone` correction.)*

### 1.5 Querying Sunrise, Sunset, and Sun Altitude (2022–2026)

- **`GetSunrise(DateTime date)` & `GetSunset(DateTime date)`:** Present in **all versions 2022–2026** (`<since>2011</since>` in `RevitAPI.xml`).
  - **Input:** Calendar date for the query (pass `DateTimeKind.Utc` or `DateTimeKind.Local`; do not pass `Unspecified`).
  - **Return value:** `DateTime` in **Coordinated Universal Time (UTC)**.
  - **Critical `RevitAPI.xml` DST quirk:** Both methods contain the identical verbatim warning:
    > *"The value returned is affected by the value of the `UsesDST` flag set for the current location. If this value is true, the sunrise [or sunset] value will be adjusted for Daylight Savings Time, regardless of the value of the input date."*
    Because of this documented behavior, verify against live Revit in integration tests whether toggling `UsesDST` shifts the UTC timestamp returned by `GetSunrise`/`GetSunset` by $\pm 1$ hour when converting to/from project clock time on dates outside DST.
- **`IsAfterSunrise` / `IsBeforeSunset` do NOT exist:** Verified via full XML and `RevitAPI.dll` reflection—neither method exists anywhere in Revit 2022–2026. Compare frame UTC timestamps directly (`frameUtc >= sunriseUtc && frameUtc <= sunsetUtc`).
- **Altitude / Azimuth queries:**
  - `GetFrameAltitude(double frame)` and `GetFrameAzimuth(double frame)` exist in 2022–2026 (`<since>2011</since>`). Both throw `ArgumentException` if `!IsFrameValid(frame)`.
  - `ActiveFrame` starts at `1.0` and increments by `1.0` up to `NumberOfFrames` for `OneDayStudy` and `MultiDayStudy`. Note that `GetFrameAzimuth` is measured **counterclockwise from East (`+X`)**, which differs from the Lighting Study `Azimuth` convention.
  - Because `ActiveFrame` / `NumberOfFrames` are scoped in `RevitAPI.xml` to single-day / multi-day studies, always call `IsFrameValid(frame)` before `GetFrameAltitude(frame)`, or rely on `GetSunrise(date)` / `GetSunset(date)` when stepping in `StillImage` mode.

### 1.6 Turning Shadows On & Graphic Display Options

- **`View.AreShadowsEnabled` / `AreShadowsOn` does NOT exist** in Revit 2022–2026 (verified by reflecting `Autodesk.Revit.DB.View` in `RevitAPI.dll` and scanning `RevitAPI.xml`).
- **Cast Shadows & Ambient Shadows are not directly toggleable via API:** Confirmed in Autodesk Community Revit API Forum discussions tracking internal issue **`REVIT-222419`** (*"(Revit API) expose shadows and ambient shadows"*). `BuiltInParameter.GRAPHIC_DISPLAY_OPTIONS_SHADOWS` (`ParameterTypeId.GraphicDisplayOptionsShadows`) represents the Graphic Display Options dialog entry / template inclusion key—it is not a writable boolean parameter on `View`.
- **`SunAndShadowSettings.Visible` is the Sun Path, NOT Cast Shadows:** Controls visibility of the 3D sun path diagram in the view (`VIEW_GRAPH_SUN_PATH`). Set `sunSettings.Visible = false` during export so the sun path widget does not clutter frames or alter uncropped extents.
- **Writable Shadow/Display API Properties on `View` (2022–2026):**
  - `view.DisplayStyle` (`DisplayStyle` enum): Shadows **do not render** in `DisplayStyle.Wireframe` or `DisplayStyle.Rendering`. They render in `DisplayStyle.HLR` (Hidden Line—**recommended** for clean Multiply compositing), `Shading`, `ShadingWithEdges`, `FlatColors` (Consistent Colors), `Realistic`, `RealisticWithEdges`, and `Textures`. Check `view.HasDisplayStyle()` and `view.CanModifyDisplayStyle()` first.
  - `view.ShadowIntensity` (`int`, `0..100`): Darkness of cast shadows (`0` = invisible, `100` = black). Useful for preventing early saturation across multi-frame Multiply stacks—*only effective if Cast Shadows is enabled on the view*.
  - `view.SunlightIntensity` (`int`, `0..100`): Directional sunlight brightness.
  - `view.GetViewDisplayModel()` / `SetViewDisplayModel(ViewDisplayModel)`: Controls `SmoothEdges`, `ShowHiddenLines`, `EnableSilhouettes`, `Transparency`.
- **Workaround when Cast Shadows is OFF:** Either validate/prompt the user to ensure Cast Shadows is checked in Graphic Display Options, or transfer a minimal pre-authored **View Template** (from an embedded `.rvt` resource via `ElementTransformUtils.CopyElements`) that has **Cast Shadows = ON**, **Ambient Shadows = OFF**, and only `GRAPHIC_DISPLAY_OPTIONS_SHADOWS` included, then apply it temporarily via `ApplyViewTemplateParameters` or `ViewTemplateId` inside the rolled-back `TransactionGroup`.

---

## 2. Image Export (`ImageExportOptions` & `Document.ExportImage`)

All `ImageExportOptions` members and `Document.ExportImage(ImageExportOptions)` are identical across Revit 2022–2026 (`RevitAPI.xml` diff).

### 2.1 `ExportRange` Selection & Dual File-Type Gotcha

- **`ExportRange.SetOfViews` (Recommended):** Supply `options.SetViewsAndSheets(new List<ElementId> { exportView.Id })`. Exports any valid graphical view without requiring it to be active in `UIDocument`.
- **`ExportRange.CurrentView`:** Exports `doc.ActiveView` zoomed to fit.
- **`ExportRange.VisibleRegionOfCurrentView`:** Exports only the current screen zoom/pan rectangle—**do not use** for shadow studies, as pan/zoom state breaks repeatability.
- **Critical File Type Gotcha:** `ImageExportOptions` has **two** separate format properties, **both defaulting to `ImageFileType.JPEGMedium`**:
  - `HLRandWFViewsFileType`
  - `ShadowViewsFileType`
  You **must** set **both** to `ImageFileType.PNG`, otherwise views with shadows/shading export as lossy JPEGs (introducing compression artifacts and mismatched extensions).

### 2.2 Sizing, Resolution, and Pixel-for-Pixel Registration

- Use `ZoomType = ZoomFitType.FitToPage` with `FitDirection = FitDirectionType.Horizontal` (or `Vertical`) and `PixelSize = targetPixels` (default `512`).
- Note `RevitAPI.xml` on `ImageResolution`: *"This is used only if the Zoom option for ZoomType is set."* For `FitToPage`, `PixelSize` directly sets the pixel dimension along `FitDirection`, and Revit computes the orthogonal dimension from the view's aspect ratio.
- `PixelSize` and `Zoom` throw `Autodesk.Revit.Exceptions.ArgumentException` (`"outside the permitted range"`) if invalid (e.g., `<= 0` or exceeding Revit's upper limit—practically ~15,000–16,384 px depending on version/GPU memory).
- **Guaranteeing Pixel-for-Pixel Alignment Across All Frames:**
  1. Keep `exportView.CropBoxActive = true` (if uncropped, Revit fits to the visible model bounding box; changing annotations/sun path would shift framing!).
  2. Set `sunSettings.Visible = false` so the Sun Path ring is hidden and cannot expand uncropped extents.
  3. Keep `PixelSize`, `FitDirection`, `ZoomType`, `Scale`, and `CropBox` constant for all frames in the run.
  4. Verify in the compositor that every decoded PNG has identical `PixelWidth` and `PixelHeight` before blending.

### 2.3 File Naming Quirks (`ImageExportOptions.GetFileName`)

- When `ExportRange == ExportRange.SetOfViews`, Revit appends the sanitized view type and view name to `ImageExportOptions.FilePath` (`RevitAPI.xml`: *"If ExportRange is SetOfViews, the name of each view or sheet will be appended to the name provided for each exported file"*).
- `ImageExportOptions.GetFileName(Document aDoc, ElementId dbViewId)` (static, `<since>2015</since>`) returns the sanitized view-derived name fragment (without directory path or `.png` extension), replacing illegal path characters.
- **Zero-ambiguity temp export pattern:** Export each frame `i` into an isolated empty subdirectory (`Path.Combine(tempRoot, $"f_{i:D3}")`) with `options.FilePath = Path.Combine(frameDir, "frame")`, then resolve the output file via `Directory.GetFiles(frameDir, "*.png").Single()`. This avoids any dependence on localized Revit view-type strings or prefix formatting across English/non-English Revit installs.

### 2.4 Transaction Lifecycle & Graphics Regeneration

`Document.ExportImage` **cannot** capture uncommitted changes inside an open `Transaction`. To mutate `SunAndShadowSettings` per frame without dirtying the document or polluting the Undo stack:

```csharp
using (var tg = new TransactionGroup(doc, "Shadow Study Export"))
{
    tg.Start();

    // Setup transaction: duplicate view / detach ViewTemplate / hide Sun Path / tune ShadowIntensity
    // ... tx.Commit();

    foreach (DateTime frameUtc in frameTimestampsUtc)
    {
        using (var tx = new Transaction(doc, "Set Sun Frame"))
        {
            tx.Start();
            sunSettings.SunAndShadowType = SunAndShadowType.StillImage;
            sunSettings.SunriseToSunset = false;
            sunSettings.StartDateAndTime = frameUtc;
            doc.Regenerate();
            tx.Commit();
        }

        // If exporting the currently active UI view with CurrentView, refresh graphics pipeline:
        if (exportView.Id == uidoc.ActiveView?.Id)
            uidoc.RefreshActiveView();

        doc.ExportImage(options); // SetOfViews targeting exportView.Id
    }

    // Discards temporary view and all SunAndShadowSettings mutations cleanly
    tg.RollBack();
}
```
*(This matches the repo's existing `TransactionGroup` + `RollBack()` discipline in `MergeDuplicates` and `SharedCoordinates`.)*

---

## 3. Multiply Blend Compositing in .NET (`net48` + `net8.0-windows8.0`)

### 3.1 Repo Target Frameworks & Imaging Stack Assessment

Inspecting the repo's `.csproj` files establishes the exact runtime constraints:
- **Revit 2022–2024** ([Synthetic2022.csproj](../../src/Synthetic2022/Synthetic2022.csproj), [Synthetic2023.csproj](../../src/Synthetic2023/Synthetic2023.csproj), [Synthetic2024.csproj](../../src/Synthetic2024/Synthetic2024.csproj)): Target `.NET Framework 4.8` (`v4.8`, C# `9.0`), reference `PresentationCore`, `PresentationFramework`, `WindowsBase`, `System.Xaml`, `System.Windows.Forms`. They **do not** reference `System.Drawing` or any third-party imaging NuGet package.
- **Revit 2025–2026** ([Synthetic2025.csproj](../../src/Synthetic2025/Synthetic2025.csproj), [Synthetic2026.csproj](../../src/Synthetic2026/Synthetic2026.csproj)) and **Logic Tests** ([SyntheticTests.Logic.csproj](../../tests/SyntheticTests.Logic/SyntheticTests.Logic.csproj)): Target `net8.0-windows8.0` with `<UseWPF>true</UseWPF>` and `<UseWindowsForms>true</UseWindowsForms>`, with **no** `System.Drawing.Common`, `SkiaSharp`, or `ImageSharp` packages.
- Existing code in [RibbonManager.cs](../../src/SyntheticShared/Core/RibbonManager.cs#L6) already uses `System.Windows.Media.Imaging`.

| Option | `net48` + `net8.0-windows8.0` Support | Revit Host Conflict Risk | Verdict |
|---|---|---|---|
| **WPF `System.Windows.Media.Imaging`** (`PngBitmapDecoder`, `FormatConvertedBitmap`, `WriteableBitmap`, `PngBitmapEncoder`) | Built into `PresentationCore` + `WindowsBase` in **both** `net48` and `net8.0-windows8.0` (`<UseWPF>true</UseWPF>`) | **Zero** — shared framework assemblies loaded by Revit itself; works headlessly in NUnit (`SyntheticTests.Logic`) without a UI `Window` | **Recommended** |
| **`System.Drawing.Common`** | Built-in on `net48`, but **removed from `.NET 8` shared framework**; requires NuGet package on `net8.0-windows8.0` | **Medium** — NuGet version mismatch in Revit 2025/2026 shared `AssemblyLoadContext` can cause type/version conflicts across add-ins | Avoid |
| **`SkiaSharp` / `SixLabors.ImageSharp`** | NuGet + native binaries (`libSkiaSharp.dll`) | **High** — native/managed dependency collisions in Revit's shared `AppDomain` (2022–2024) or `AssemblyLoadContext` (2025–2026) | Avoid |

### 3.2 Mathematical Formulation & Alpha Handling

Per **W3C Compositing and Blending Level 1** (Separable Blend Modes, `multiply`), with channels normalized to $[0, 1]$:
$$B(C_b, C_s) = C_b \cdot C_s$$

1. **Opaque Pixels ($\alpha = 1$, standard for Revit PNG exports):**
   In 8-bit integer arithmetic (`0..255`), exact rounded Multiply is:
   $$C_{\text{out}} = \frac{C_b \cdot C_s + 127}{255}$$
   - Pure white `(255, 255, 255)` is the identity element: `(C * 255 + 127) / 255 = C`.
   - Pure black linework `(0, 0, 0)` stays `(0, 0, 0)`.

2. **General Straight-Alpha (`PixelFormats.Bgra32`) with Optional Layer Opacity $w \in [0, 1]$:**
   Always normalize decoded frames via `new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0)` so pixels are **straight (unpremultiplied) BGRA**—never blend raw `Pbgra32` bytes directly without un-premultiplying, or semi-transparent edges develop dark halos.
   With $\alpha_s' = w \cdot (\alpha_s / 255)$ and $\alpha_b = A_b / 255$:
   $$C_r = (1 - \alpha_s') C_b + (1 - \alpha_b) C_s + \alpha_b \alpha_s' (C_b C_s), \qquad \alpha_r = \alpha_s' + \alpha_b (1 - \alpha_s')$$
   For fully opaque Revit PNGs ($\alpha_b = \alpha_s = 1$) at opacity $w$, this simplifies cleanly to:
   $$C_{\text{out}} = (1 - w) C_b + w \cdot \frac{C_b \cdot C_s}{255}$$

3. **No `unsafe` Compiler Flag Needed:**
   None of `Synthetic2022`–`Synthetic2026` enable `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>`. Using `FormatConvertedBitmap.CopyPixels(pixels, stride, 0)` and `WriteableBitmap.WritePixels(new Int32Rect(0, 0, w, h), accBuffer, stride, 0)` operates purely on managed `byte[]` arrays at high speed (e.g., 4K `3840×2160` is ~8.3M pixels, taking tens of milliseconds in C#).

### 3.3 Shadow-Study Compositing Pitfalls & Mitigations

| Pitfall | Mathematical Cause | Mitigation |
|---|---|---|
| **Non-white surfaces/backgrounds turn black** | Multiplying $N$ frames where a shaded surface or gray background has value $C < 1$ produces $C^N$ (e.g., `(200/255)^12 = 0.054` $\approx 14/255$ black!). | Export in **`DisplayStyle.HLR` (Hidden Line)** with a pure white background `(255,255,255)` so sunlit surfaces remain `255`. For colored/shaded views, export **1 unshadowed base frame** or use reduced layer opacity $w$ on subsequent frames. |
| **Shadows saturate to pure black too fast** | At default `ShadowIntensity = 50`, shadows are ~50% gray (`0.5`). After just 6 overlapping frames, $0.5^6 = 0.0156$ (`4/255`), making 6h and 12h of shade visually indistinguishable. | Either temporarily set `exportView.ShadowIntensity` lower (e.g., `15`–`25`, scaled roughly with frame count) inside the rolled-back `TransactionGroup`, **or** keep Frame 0 as opaque base (`100%`) and Multiply frames $1 \dots N-1$ with partial layer opacity $w \in (0, 1]$ (e.g., $w = 0.25$–$0.5$). Note: keeping Frame 0 at `100%` ensures black linework `(0,0,0)` stays crisp `(0,0,0)` across all subsequent Multiply frames regardless of $w$! |
| **Anti-aliased line thickening** | Soft anti-aliased edge pixels (`~180–230`) darken exponentially over $N$ frames (`0.8^12 = 0.07`), thickening thin lines slightly. | Acceptable for standard Photoshop-style Multiply stacks; disabling `SmoothEdges` in `ViewDisplayModel` keeps 1px edges sharper if desired. |

---

## 4. Astronomical Dates (Solstices & Equinoxes)

- **No Revit API Enum or Constants:** `RevitAPI.xml` and `RevitAPI.dll` (2022–2026) contain **no** built-in enum or constant for Solstices or Equinoxes. `SunAndShadowSettings.GetMatchingPreset()` only returns the string name of per-document `SunAndShadowSettings` preset elements.
- **Northern-Hemisphere Hardcoded UI Presets Gotcha:** Revit's default UI presets ("Summer Solstice" = June 21, "Winter Solstice" = December 21) do **not** flip automatically for Southern Hemisphere sites.
- **Standard Architectural Dates & Hemisphere Mapping via `SiteLocation.Latitude`:**
  Check `doc.SiteLocation.Latitude` (radians in $[-\pi/2, +\pi/2]$; `< 0` = Southern Hemisphere, `>= 0` = Northern Hemisphere):

| Astronomical Event | Standard Calendar Date | Northern Hemisphere (`Latitude >= 0`) | Southern Hemisphere (`Latitude < 0`) |
|---|---|---|---|
| **June Solstice** | June 21 *(actual astronomical range: Jun 20–21)* | **Summer Solstice** (longest day / highest sun) | **Winter Solstice** (shortest day / lowest sun) |
| **December Solstice** | December 21 *(actual astronomical range: Dec 20–22)* | **Winter Solstice** (shortest day / lowest sun) | **Summer Solstice** (longest day / highest sun) |
| **March Equinox** | March 20 *(or Mar 21; range: Mar 19–21)* | **Spring (Vernal) Equinox** | **Autumnal (Fall) Equinox** |
| **September Equinox** | September 22 *(or Sep 21/23; range: Sep 21–24)* | **Autumnal (Fall) Equinox** | **Spring (Vernal) Equinox** |
