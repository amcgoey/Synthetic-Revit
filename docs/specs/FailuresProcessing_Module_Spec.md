# Technical Specification: Common Revit FailuresProcessing Module

**Status:** Approved  
**Author:** Antigravity / Synthetic Architecture  
**Target Project:** `SyntheticShared` (`SyntheticShared.Infrastructure.FailureProcessing`)  
**Parent Wayfinder Map:** [Issue #43](https://github.com/amcgoey/Synthetic-Revit/issues/43)  

---

## 1. Overview & Architectural Goals

Revit database transactions — across single element operations and batch loops — frequently trigger native Revit warning and error alerts. In automated add-in workflows, unhandled failure dialogs freeze execution threads.

This specification defines the architecture, C# API surface, transaction extension helpers, telemetry reporting, and Option B migration plan for a centralized **`FailuresProcessing`** module in `SyntheticShared`.

### Key Design Principles:
1. **Targeted Rule Matching (Anti-Blanket Suppression Gate):** Strictly enforces repository standards (`revit-failure-handling` skill) by matching specific `FailureDefinitionId`s by default. Blanket warning suppression is prohibited unless explicitly enabled via configuration.
2. **Dual-Layer Architecture:** Provides a standard `IFailuresPreprocessor` implementation alongside fluent `Document` and `Transaction` convenience helpers.
3. **Thread-Safe Telemetry & UI Correlation:** Captures lightweight, thread-safe `FailureRecord` DTOs in a `FailureProcessingReport`, allowing ViewModels to safely bind and display failure messaging on the WPF UI main thread.
4. **Option B Direct Migration:** Direct replacement of legacy ad-hoc preprocessors (`PurgeFailuresPreprocessor`, `SuppressConstraintsPreprocessor`, `DeleteWarningsPreprocessor`) across all callsites, leaving zero legacy tech debt.

---

## 2. API Surface & Core Types

### 2.1 Rule Interface (`IFailureRule`)

```csharp
namespace Synthetic.Infrastructure.FailureProcessing
{
    /// <summary>
    /// Represents a single failure matching and resolution rule.
    /// </summary>
    public interface IFailureRule
    {
        /// <summary>
        /// Evaluates whether this rule applies to the given failure message.
        /// </summary>
        bool Evaluates(FailureMessageAccessor failure);

        /// <summary>
        /// Executes the failure resolution action against the failures accessor.
        /// </summary>
        void Process(FailuresAccessor accessor, FailureMessageAccessor failure, FailureProcessingReport report);
    }
}
```

---

### 2.2 Composite Preprocessor (`CompositeFailuresPreprocessor`)

```csharp
using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace Synthetic.Infrastructure.FailureProcessing
{
    /// <summary>
    /// Composite implementation of Revit's IFailuresPreprocessor executing registered rule chains.
    /// </summary>
    public class CompositeFailuresPreprocessor : IFailuresPreprocessor
    {
        private readonly List<IFailureRule> _rules;
        private readonly bool _allowBlanketSuppression;
        private string _currentContextTag = string.Empty;

        public FailureProcessingReport LastReport { get; } = new FailureProcessingReport();

        public CompositeFailuresPreprocessor(IEnumerable<IFailureRule> rules, bool allowBlanketSuppression = false)
        {
            _rules = new List<IFailureRule>(rules);
            _allowBlanketSuppression = allowBlanketSuppression;
        }

        /// <summary>
        /// Sets a contextual scope tag for batch loops (e.g. "Processing Family: Column.rfa").
        /// </summary>
        public void SetContext(string contextTag)
        {
            _currentContextTag = contextTag ?? string.Empty;
        }

        public FailureProcessingResult PreprocessFailures(FailuresAccessor failuresAccessor)
        {
            IList<FailureMessageAccessor> failures = failuresAccessor.GetFailureMessages();
            if (failures.Count == 0)
            {
                return FailureProcessingResult.Continue;
            }

            bool hasRollback = false;

            foreach (FailureMessageAccessor failure in failures)
            {
                bool matched = false;
                foreach (var rule in _rules)
                {
                    if (rule.Evaluates(failure))
                    {
                        rule.Process(failuresAccessor, failure, LastReport);
                        matched = true;
                        break;
                    }
                }

                if (!matched)
                {
                    FailureSeverity severity = failure.GetSeverity();
                    if (severity == FailureSeverity.Warning && _allowBlanketSuppression)
                    {
                        failuresAccessor.DeleteWarning(failure);
                        LastReport.AddRecord(new FailureRecord(failure, FailureHandlingAction.DeletedWarning, _currentContextTag));
                    }
                    else if (severity == FailureSeverity.Error)
                    {
                        if (failure.HasResolution())
                        {
                            failuresAccessor.ResolveFailure(failure);
                            LastReport.AddRecord(new FailureRecord(failure, FailureHandlingAction.ResolvedFailure, _currentContextTag));
                        }
                        else
                        {
                            hasRollback = true;
                            LastReport.AddRecord(new FailureRecord(failure, FailureHandlingAction.ForcedRollback, _currentContextTag));
                        }
                    }
                    else
                    {
                        LastReport.AddRecord(new FailureRecord(failure, FailureHandlingAction.LoggedAndContinued, _currentContextTag));
                    }
                }
            }

            return hasRollback ? FailureProcessingResult.ProceedWithRollBack : FailureProcessingResult.ProceedWithCommit;
        }
    }
}
```

---

### 2.3 Fluent Builder (`FailurePipelineBuilder`)

```csharp
namespace Synthetic.Infrastructure.FailureProcessing
{
    public class FailurePipelineBuilder
    {
        private readonly List<IFailureRule> _rules = new List<IFailureRule>();
        private bool _allowBlanketSuppression = false;

        public FailurePipelineBuilder OnFailure(FailureDefinitionId id, Action<FailureRuleConfigurator> configure)
        {
            var configurator = new FailureRuleConfigurator(id);
            configure(configurator);
            _rules.Add(configurator.BuildRule());
            return this;
        }

        public FailurePipelineBuilder AllowBlanketSuppression(bool allow = true)
        {
            _allowBlanketSuppression = allow;
            return this;
        }

        public CompositeFailuresPreprocessor Build()
        {
            return new CompositeFailuresPreprocessor(_rules, _allowBlanketSuppression);
        }
    }
}
```

---

### 2.4 Diagnostic Report & DTO (`FailureProcessingReport` / `FailureRecord`)

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace Synthetic.Infrastructure.FailureProcessing
{
    public enum FailureHandlingAction
    {
        DeletedWarning,
        ResolvedFailure,
        LoggedAndContinued,
        ForcedRollback
    }

    public class FailureRecord
    {
        public string FailureDefinitionId { get; }
        public FailureSeverity Severity { get; }
        public string DescriptionText { get; }
        public IReadOnlyList<ElementId> FailingElementIds { get; }
        public FailureHandlingAction ActionTaken { get; }
        public string ContextTag { get; }
        public DateTime Timestamp { get; }

        public FailureRecord(FailureMessageAccessor accessor, FailureHandlingAction action, string contextTag = "")
        {
            FailureDefinitionId = accessor.GetFailureDefinitionId()?.Guid.ToString() ?? string.Empty;
            Severity = accessor.GetSeverity();
            DescriptionText = accessor.GetDescriptionText() ?? string.Empty;
            FailingElementIds = accessor.GetFailingElementIds()?.ToList() ?? new List<ElementId>();
            ActionTaken = action;
            ContextTag = contextTag;
            Timestamp = DateTime.Now;
        }
    }

    public class FailureProcessingReport
    {
        private readonly List<FailureRecord> _records = new List<FailureRecord>();

        public IReadOnlyList<FailureRecord> Records => _records;
        public int TotalEvaluated => _records.Count;
        public int WarningsDeletedCount => _records.Count(r => r.ActionTaken == FailureHandlingAction.DeletedWarning);
        public int FailuresResolvedCount => _records.Count(r => r.ActionTaken == FailureHandlingAction.ResolvedFailure);
        
        /// <summary>
        /// Backward-compatibility property replacing SuppressConstraintsPreprocessor.WarningTripped
        /// </summary>
        public bool WarningTripped => WarningsDeletedCount > 0;

        public void AddRecord(FailureRecord record)
        {
            _records.Add(record);
        }
    }
}
```

---

## 3. Transaction Helper Extensions

```csharp
using System;
using Autodesk.Revit.DB;

namespace Synthetic.Infrastructure.FailureProcessing
{
    public static class DocumentTransactionExtensions
    {
        public static Transaction CreateTransaction(this Document doc, string name, IFailuresPreprocessor? preprocessor = null)
        {
            var tx = new Transaction(doc, name);
            if (preprocessor != null)
            {
                FailureHandlingOptions options = tx.GetFailureHandlingOptions();
                options.SetFailuresPreprocessor(preprocessor);
                tx.SetFailureHandlingOptions(options);
            }
            return tx;
        }

        public static FailureProcessingReport ExecuteTransaction(
            this Document doc, 
            string name, 
            IFailuresPreprocessor preprocessor, 
            Action<Transaction> action)
        {
            using (Transaction tx = doc.CreateTransaction(name, preprocessor))
            {
                tx.Start();
                try
                {
                    action(tx);
                    tx.Commit();
                }
                catch (Exception)
                {
                    if (tx.GetStatus() == TransactionStatus.Started)
                    {
                        tx.RollBack();
                    }
                    throw;
                }

                if (preprocessor is CompositeFailuresPreprocessor composite)
                {
                    return composite.LastReport;
                }

                return new FailureProcessingReport();
            }
        }
    }
}
```

---

## 4. Preset Library (`FailurePipelines`)

```csharp
namespace Synthetic.Infrastructure.FailureProcessing
{
    public static class FailurePipelines
    {
        public static CompositeFailuresPreprocessor Purge()
        {
            return new FailurePipelineBuilder()
                .OnFailure(BuiltInFailures.PurgeFailures.PurgeUnusedElementsFailed, c => c.DeleteWarning())
                .OnFailure(BuiltInFailures.DeletionFailures.ElementsWillBeDeleted, c => c.DeleteWarning())
                .Build();
        }

        public static CompositeFailuresPreprocessor SuppressConstraints()
        {
            return new FailurePipelineBuilder()
                .OnFailure(BuiltInFailures.GroupFailures.GroupConstraintsFailed, c => c.DeleteWarning())
                .Build();
        }

        public static CompositeFailuresPreprocessor DeleteWarnings()
        {
            return new FailurePipelineBuilder()
                .AllowBlanketSuppression(true)
                .Build();
        }
    }
}
```

---

## 5. Option B Direct Migration Plan

All legacy ad-hoc preprocessor classes will be replaced directly at their callsites:

| Legacy Class | Call Site File | Replacement Code |
| :--- | :--- | :--- |
| `PurgeFailuresPreprocessor` | `src/.../FamilyManagement/Handlers/AuditPurgeEventHandler.cs:L168` | `options.SetFailuresPreprocessor(FailurePipelines.Purge());` |
| `SuppressConstraintsPreprocessor` | `src/.../ViewManagement/Utilities/LegendsUtil.cs:L53 & L394` | Replace `new SuppressConstraintsPreprocessor()` with `FailurePipelines.SuppressConstraints()`, query `report.WarningTripped`, delete legacy class. |
| `DeleteWarningsPreprocessor` | `src/.../RevitDOM/Operations/Standards/RevitFamilyEnforcer.cs:L215, L245` | Replace with `FailurePipelines.DeleteWarnings()`, remove private nested class. |

### File Cleanups:
- Delete `src/SyntheticShared/Modules/FamilyManagement/Utilities/PurgeFailuresPreprocessor.cs`.
- Remove `PurgeFailuresPreprocessor.cs` line from `SyntheticShared.projitems`.

---

## 6. Verification Plan

1. **Unit Tests:** Add unit tests in `SyntheticTests.Shared` validating rule matching, report record generation, and context tag correlation.
2. **Integration Verification:** Run `AuditPurgeEventHandler`, `LegendsUtil`, and `RevitFamilyEnforcer` tests to confirm failure processing behavior is preserved.
