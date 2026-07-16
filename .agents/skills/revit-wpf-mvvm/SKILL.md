---
name: revit-wpf-mvvm
description: How to separate WPF UI binding and C# Revit API thread execution. Use when writing user interface (WPF) or view model code.
---

# Revit WPF & MVVM Playbook (`revit-wpf-mvvm`)

Use this playbook to maintain clean architectural boundaries between the WPF presentation layer (MVVM) and the Revit database thread execution.

## 1. Decoupled Data Binding (RevitDOM DRY Rule)

WPF Views must never bind directly to live Revit API database elements (`Element`, `Parameter`, `Category`, etc.) to prevent `InvalidObjectException` crashes when elements become invalid or deleted in Revit:

* **Map to POCOs:** Always map Revit database elements to presentation-safe C# DTOs or custom sub-ViewModels.
* **Reuse RevitDOM:** To remain DRY, you are required to reuse the existing POCO/wrapper classes and translators under the `Synthetic.Modules.RevitDOM` namespace (such as `ElementModel`, `ParameterModel`, `CategoryModel`, and `XYZModel`) rather than writing redundant wrapper classes.
* **Reference by ID:** Store the element's `ElementId` or `UniqueId` string inside the ViewModel to reference it, rather than holding references to active `Element` object instances.

## 2. UI Thread Dispatching

Modifying bound ViewModel properties or collections (like `ObservableCollection<T>`) from an external thread (like a background task or Revit's main thread running inside an external event) will throw a cross-thread `NotSupportedException`.

* **WPF Dispatcher Delegation:** You must explicitly delegate all collection modifications or property updates to the WPF UI thread using the dispatcher:
  ```csharp
  System.Windows.Application.Current.Dispatcher.Invoke(() =>
  {
      MyObservableCollection.Add(newModel);
      StatusMessage = "Updated successfully.";
  });
  ```

## 3. Window Presentation Contexts

Understand the thread context of the active WPF window to handle Revit API calls correctly:

### Modal Windows (`window.ShowDialog()`)
* **Context:** Modal windows run synchronously, blocking the Revit UI thread.
* **API Access:** You can call the Revit API and execute database modifications directly within transactions because Revit is suspended waiting for the dialog to close.

### Modeless Windows (`window.Show()`)
* **Context:** Modeless windows run alongside Revit asynchronously.
* **API Access:** You are **strictly forbidden** from calling the Revit API directly from modeless events. You must queue operations and trigger them using the `IExternalEventHandler` pattern (see the `revit-external-events` playbook).
