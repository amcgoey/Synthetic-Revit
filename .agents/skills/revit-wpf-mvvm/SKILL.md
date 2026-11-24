---
name: revit-wpf-mvvm
description: Separate Core/ ViewModels, Addin/ SyntheticThemeDictionary views, and Revit API execution. Use when authoring WPF XAML, ViewModels, or UI adapters.
---

# Revit WPF & MVVM Playbook (`revit-wpf-mvvm`)

Keep WPF presentation cleanly partitioned across `Synthetic.Core` (`Core/`) and `Synthetic<Year>` (`Addin/`) per [CODING_STANDARDS.md](../../../CODING_STANDARDS.md), [ADR 0027](../../../docs/adrs/0027-tiered-vertical-slice-architecture-and-seam-cqs.md), and [ADR 0028](../../../docs/adrs/0028-hybrid-synthetic-core-assembly-and-colocated-partitions.md).

## 1. Partition Split & Decoupled Data Binding

| Layer & Partition | Assembly & Namespace | Rules & Primitives |
| :--- | :--- | :--- |
| **`Core/` ViewModels & UI Ports** (`src/Features/<Slice>/Core/ViewModels/`, `src/Platform/UI/Core/`) | `Synthetic.Core` (`net48;net8.0-windows8.0`) | Inherit [`ViewModelBase`](../../../src/Platform/UI/Core/ViewModelBase.cs) and bind commands with [`RelayCommand`](../../../src/Platform/UI/Core/RelayCommand.cs) using Pure DI at entry points (zero IoC containers or `CommunityToolkit.Mvvm`). Inject `Platform/UI` prompt ports (`IFileDialogService`, `IFolderDialogService`, `IUserPromptService`) and `I<Slice>Adapter` rather than opening modal dialogs inside ViewModels. |
| **`Addin/` WPF Views** (`src/Features/<Slice>/Addin/Views/`, `src/Platform/UI/Addin/`) | `Synthetic<Year>` | House all `.xaml` / `.xaml.cs` views exclusively in `Addin/` (`FitnessRule.CoLocatedPartitionLayout`) and merge `<theme:SyntheticThemeDictionary />` per [Synthetic_UI_Theme_Specification.md](../../../docs/Synthetic_UI_Theme_Specification.md) instead of relative `SyntheticTheme.xaml` URIs or `Application.Current.Resources` overrides. |

* **Revit-Free `Core/` ViewModels (`FitnessRule.InteractiveSliceSeamPurity`):** Exchange pure `Core/` POCOs (`SheetIndexSnapshot`, `ShadowStudyViewItem`, `Synthetic.RevitDOM.Models`) and `string` `UniqueId`s (or pure surrogate identifiers) across `I<Slice>Adapter` seams—never reference `Autodesk.Revit.*` (`Element`, `Document`, `Parameter`, `Autodesk.Revit.DB.ElementId`) inside `Core/` or `{ViewModels, Views, Models, Settings}`.
* **`Addin/`-Only `Synthetic.RevitDOM.Translation`:** Keep Revit-coupled translators (`IModelTranslator`, `ModelDispatcher`, `RevitDomExtensions`, `RevitIdentityService` in `src/RevitDOM/Addin/Translation/`) inside `Addin/` adapters and commands, never inside `Core/` ViewModels (`Synthetic.Core` contains only `IPocoIdentityService` / `PocoIdentityService` in `src/RevitDOM/Core/Translation/`).

## 2. UI Thread Dispatching

Modifying bound ViewModel properties or `ObservableCollection<T>` instances from a background thread or Revit API `ExternalEvent` turn throws a cross-thread `NotSupportedException`. Delegate UI state updates to the WPF UI thread via the dispatcher:

```csharp
System.Windows.Application.Current?.Dispatcher.Invoke(() =>
{
    Items.Add(item);
    StatusMessage = "Updated successfully.";
});
```

## 3. Window Presentation Contexts & Theme Compliance

* **Theme & XAML Merge ([Synthetic_UI_Theme_Specification.md](../../../docs/Synthetic_UI_Theme_Specification.md)):** Declare `xmlns:theme="clr-namespace:Synthetic.Platform.UI"`, merge `<theme:SyntheticThemeDictionary />` inside each `Window`/`UserControl` resource dictionary, add `xmlns:local="clr-namespace:Synthetic.Platform.UI;assembly=Synthetic.Core"` for [`WindowChromeBehavior`](../../../src/Platform/UI/Core/WindowChromeBehavior.cs) or core converters, and bind colors via `{DynamicResource Synthetic.Brushes.*}`.
* **Modal Windows (`window.ShowDialog()`):** Run synchronously inside `IExternalCommand.Execute` (blocking Revit's UI thread) using [`ProgressCoordinator`](../../../src/Platform/UI/Addin/ProgressCoordinator.cs) rather than dispatching through `ExternalEvent`.
* **Modeless Windows (`window.Show()`):** Keep Revit API execution out of code-behind/ViewModel button events; marshal operations onto Revit's main thread via the window-scoped `IExternalEventHandler` contract in [`revit-external-events`](../revit-external-events/SKILL.md).
