# ADR 0031 - Multi-Model Native PDF Export and Slice Graduation

Status: Accepted

## Context
The legacy `BatchPrint` slice (`PrintBatchMultiDoc` and `BBPrinterSettingsUtils`) automated multi-document printing by driving `"Bluebeam PDF"` through Revit's `PrintManager` and overwriting five Windows Registry values under `HKCU\SOFTWARE\Bluebeam Software\21\Brewery\V45\Printer Driver`. That design suffered from four structural failures:

1. **Workstation Driver & Registry Fragility:** Bluebeam upgrades break the hardcoded registry path, missing baseline keys cause fallback defaults to be written even when the user cancels, and any unhandled exception during printing leaves the workstation's Bluebeam settings permanently altered.
2. **Unsafe Linked-Document & Multi-Tab Lifecycle:** Querying `ViewSheetSet` and `PrintSetting` against read-only linked `Document` handles before opening standalone files, closing documents via a title comparison outside a `finally` block, and opening background files without suppressing `SyncToastNotification` drift checks caused silent skips, leaked background models, and accidental closure of unrelated open tabs.
3. **Sequential Modal UX & Ambiguous Naming:** Four sequential modal dialogs offered no early exit, forced one active-model **Print Set** name onto every linked document, and shipped under the name `BatchPrint` (`"Batch Print\nMulti Doc"`), which inaccurately implied single-model sheet printing rather than cross-model deliverable export.
4. **Unverified Execution Path:** Depending on live `"Bluebeam PDF"` drivers and Windows Registry mutations prevented automated **Logic Tests** and forced **Revit Tests** (`BatchPrintSmokeRevitTests`) to stay non-executing, leaving PDF generation dependent on manual human verification.

With Autodesk Revit 2022 retired ([ADR 0018](0018-universal-add-in-bootstrap-manifest-routing-and-studio-deployment.md)), every supported **Revit Version** (`2023`–`2027`) supports native `Document.Export(..., PDFExportOptions)` and saved `ExportPDFSettings`.

## Decision
We retire `"Bluebeam PDF"` / `PrintManager` / Windows Registry integration and **rename and graduate** the slice to **Multi-Model PDF Export** (`MultiModelPdfExport` / `CmdMultiModelPdfExport`):

1. **Native Driver-Free PDF Export (`2023`–`2027`):**
   - Replace `PrintManager` and `BBPrinterSettingsUtils` entirely with native `Document.Export(folder, viewIds, PDFExportOptions)` and active-document `ExportPDFSettings` (plus a built-in `<Default — Use Sheet Size>` fallback).
   - Force `PDFExportOptions.SetExportInBackground(false)` on **Revit Versions** `2025+` behind an inverted `#if REVIT2023 || REVIT2024 ... #else` guard, remap non-built-in `NamingRule` parameters across target documents by `Definition.Name`, stage exports in a scoped temp subfolder before atomic move/replace, and preserve `OrderedViewList` sequence (falling back to `.Views`) while filtering placeholder sheets and non-printable views.
2. **Slice Rename & Interactive Slice Graduation (`ADR 0027` Tier C):**
   - Rename `BatchPrint` → `MultiModelPdfExport` and `PrintBatchMultiDoc` → `CmdMultiModelPdfExport` across production partitions, test partitions, ribbon configuration, [ADR 0027](0027-tiered-vertical-slice-architecture-and-seam-cqs.md), [ADR 0029](0029-isomorphic-test-mirror-and-architecture-fitness-guardrails.md), [CODING_STANDARDS.md](../../CODING_STANDARDS.md), `DEEPENING.md`, and the Architecture Fitness Suite, matching the canonical **Multi-Model PDF Export** term in [GLOSSARY.md](../../GLOSSARY.md).
   - Graduate the slice from a **Direct Slice** to an **Interactive Slice** with `0` allowlist entries behind a single **Single-Seam Adapter** (`IMultiModelPdfExportAdapter` exposing `CaptureSessionSnapshot()` and `ExportDocument(...)`), a pure `Core/` orchestration pipeline (`MultiModelPdfExportPipeline`), immutable `public sealed class` POCOs compatible with `net48` C# 9.0, **Local User Preferences** outside the `.rvt` model, and a unified `660×740px` Variant A modal window (`MultiModelPdfExportWindow`, prototype captured on `prototype/batch-print-ui` at `361b61d`).
   - Isolate linked-document lifecycle inside `RevitMultiModelPdfExportAdapter` with `Active > OpenStandalone > Linked` deduplication, scoped `SyncToastNotification` suppression, dual `FailuresProcessing` + `DialogBoxShowing` suppression in `try/finally`, and explicit `openedStandaloneDoc` closure guards.

## Alternatives Considered
1. **Keep `PrintManager` with User Printer Selection & Dynamic Bluebeam Registry Discovery:** Rejected because third-party PDF drivers vary wildly in silent-save behavior, workstation registry heuristics remain brittle across driver versions, and live Bluebeam runs cannot be verified automatically in headless/Revit CI.
2. **Keep `BatchPrint` as a Direct Slice with Sequential Popups:** Rejected because per-project **Print Set** selection, pre-export validation, and path disambiguation require stateful UI interaction that belongs in `Core/` ViewModels tested headlessly in **Logic Tests**.
3. **Keep the Internal `BatchPrint` Slice Name While Renaming Only the Ribbon Text:** Rejected because keeping `"BatchPrint"` in code and fitness suites preserves ambiguity between single-model sheet printing and multi-model PDF export; aligning code and [GLOSSARY.md](../../GLOSSARY.md) removes permanent vocabulary drift.

## Consequences
* **Pros:** Eliminates Windows Registry corruption and manual Bluebeam HITM verification; unblocks full headless **Logic Tests** (`net48` + `net8.0-windows8.0` + `net10.0-windows8.0`) and live executing `[RevitIsolatedDocTest]` **Revit Tests** across `2023–2027` (built via `.NET 10` `dotnet build`); prevents leaked background documents, orphaned sync toasts, and accidental closure of open user tabs; and unifies the `Publish` panel slices (`MultiModelPdfExport`, `SheetIndex`, `ShadowStudy`) around consistent **Interactive Slice** conventions.
* **Cons:** Drops physical plotter / third-party print-driver printing from this workflow; and requires a coordinated slice-rename pass across source, tests, docs, ribbon configuration, and Architecture Fitness rules during implementation.
