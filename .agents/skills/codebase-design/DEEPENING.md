# Deepening

How to deepen a cluster of shallow modules safely, given its dependencies. Assumes [SKILL.md](SKILL.md) vocabulary (**module**, **interface**, **seam**, **adapter**) and [CODING_STANDARDS.md](../../../CODING_STANDARDS.md) partition rules (`Core/` in `Synthetic.Core`, `Addin/` in `Synthetic<Year>`).

## Dependency categories

Classify dependencies first; the category selects where the seam lives and how [`Synthetic.LogicTests`](../../../tests/Projects/Synthetic.LogicTests/Synthetic.LogicTests.csproj) vs. `Synthetic.RevitTests.<Year>` test it.

### 1. In-process

Pure C# models and computation in `Core/` (`Synthetic.Core`, `net48;net8.0-windows8.0`) with zero Revit references: **RevitDOM** POCOs (`Synthetic.RevitDOM.Models`), `PocoIdentityService` (`Synthetic.RevitDOM.Translation`), `MergeAnalysisEngine` (`Synthetic.Features.MergeDuplicates.Services`), `SharedCoordinatesMath` (`Synthetic.Features.SharedCoordinates.Models`), and `SolarScheduleCalculator` (`Synthetic.Features.ShadowStudy.Services`). Collapse shallow helpers directly into deep `Core/` modules and test through the public interface in **Logic Tests** (`[UnitTest]`).

### 2. Local-substitutable

Dependencies with deterministic local stand-ins: JSON serialization (`PocoJsonSerializer`, `Config` in `Synthetic.Platform.Settings`), local user preferences (`ShadowStudyUserPrefsStore`), filesystem streams, and OpenXML `.xlsx` packaging (`SheetIndexExporter` in `Synthetic.Features.SheetIndex.Services`). Keep the seam internal and test directly against streams or per-test temp folders in **Logic Tests**.

### 3. Remote but owned (Ports & Adapters)

Revit database access across the unmanaged C++ boundary (`Document`, `FilteredElementCollector`, `DocumentTransactionExtensions`, `ExtensibleStorage`, `ModelDispatcher`). Define **one** narrow port `I<Slice>Adapter` in `src/Features/<Slice>/Core/Adapters/` (`ISheetIndexAdapter`, `ISharedCoordinatesAdapter`, `IShadowStudyAdapter`), implement `Revit<Slice>Adapter` in `Addin/Adapters/`, and implement `Fake<Slice>Adapter` in [`tests/Fixtures/Shared/`](../../../tests/Fixtures/Shared/). Verify `Core/` orchestration in **Logic Tests** and verify `Revit<Slice>Adapter` in live **Revit Tests** (`RevitTemplateRollbackFixture` / `RevitIsolatedDocumentFixture`).

### 4. True external / Thread boundary (Port / Entry Injection)

External UI prompts (`IFileDialogService`, `IFolderDialogService`, `IUserPromptService` in `Synthetic.Platform.UI`) and Revit's modeless UI thread (`UIApplication`, `IExternalEventHandler` / `ExternalEvent`, WPF `Dispatcher`). Inject `Platform/UI` prompt ports and `ExternalEvent` + `IExternalEventHandler` from `IExternalCommand.Execute` (`CmdProjectStandards.cs` window-scoped single-instance contract until `ICoalescedRevitDispatcher<TAdapter>` ships in Spec 5 per [ADR 0027](../../../docs/adrs/0027-tiered-vertical-slice-architecture-and-seam-cqs.md)); marshal UI updates back via `Dispatcher.Invoke` with zero modal dialogs inside handlers or ViewModels.

## Canonical Seam Patterns (Worked Examples)

### `Harvest -> Pure Plan -> Atomic Apply`

Keep multi-step BIM workflows split across `Core/` and `Addin/` (`SheetIndex`, `SharedCoordinates`, `ShadowStudy`):

```csharp
// 1. Harvest at I<Slice>Adapter seam (Addin -> immutable Core snapshot)
IReadOnlyList<LinkRepositionGroupItem> groups = _adapter.GetLinkRepositionGroups();

// 2. Pure Plan in Core (zero Revit API; verified in Synthetic.LogicTests)
CoordinateTransformResult delta = SharedCoordinatesMath.ComputeLinkAlignment(
    currentLinkTransform,
    expectedLinkTransform);

// 3. Atomic Apply at Revit<Slice>Adapter using DocumentTransactionExtensions
bool committed = _doc.ExecuteCommittedBatchGroup("Move Links to Shared Sites", () =>
{
    using Transaction tx = _doc.CreateTransaction(
        $"Reposition {group.LinkName}",
        FailurePipelines.ObserveOnly());
    tx.Start();
    // Apply pure plan transforms to Revit link instances...
    return tx.Commit() == TransactionStatus.Committed;
});
```

### `Seam CQS` (Command–Query Separation)

Keep `I<Slice>Adapter` queries side-effect-free (returning immutable DTOs/snapshots without mutating Revit or Undo history) and pass immutable request/plan objects into command methods:

```csharp
namespace Synthetic.Features.SharedCoordinates.Adapters
{
    public interface ISharedCoordinatesAdapter
    {
        // Queries: read-only, immutable DTO lists
        IReadOnlyList<LinkedModelItem> GetLoadedLinkedModels();
        IReadOnlyList<LinkRepositionGroupItem> GetLinkRepositionGroups();

        // Commands: pure request POCO in -> Typed Outcome Object out
        SyncSharedSitesSummary SyncSharedSites(SyncSharedSitesRequest request);
        RepositionLinksSummary RepositionLinks(RepositionLinksRequest request);
    }
}
```

### `Typed Outcome Object`

Return structured outcome POCOs (`SheetIndexExportOutcome`, `RepositionLinksSummary`, `RawFrameExportResult`) from deep modules and adapters so callers (`IExternalCommand` or `Core/` ViewModels) decide presentation or assertions:

```csharp
// Deep Core exporter returns structured outcome rather than throwing or showing dialogs
SheetIndexExportOutcome outcome = new SheetIndexExporter().Export(
    selectedSheets,
    selectedRevisions,
    savePath,
    viewModel.PreserveSheetOrder);

// Entry command owns presentation decisions
if (!outcome.Succeeded)
{
    TaskDialog.Show("Export Dot Sheet Index", outcome.ErrorMessage ?? "Export failed.");
    return Result.Failed;
}
```

## Seam discipline

- **One adapter means a hypothetical seam. Two adapters means a real one.** Direct Slices (`BatchPrint`, `Diagnostics`, `ViewManagement`, `Worksets`) execute in `Addin/Commands/` + `Addin/Utilities/` with zero pass-through interfaces until UI state or math earns `Fake<Slice>Adapter`.
- **Internal seams vs external seams.** A deep module can compose internal helpers privately; keep the external seam small and test through it.

## Testing strategy: replace, don't layer

- Delete obsolete unit tests on collapsed shallow helpers once tests at the deepened module's interface exist.
- Write new tests at the deepened module's interface (`SheetIndexExporter.Export`, `ISharedCoordinatesAdapter`, `IShadowStudyAdapter`); assert observable outcomes and survive internal refactors.
