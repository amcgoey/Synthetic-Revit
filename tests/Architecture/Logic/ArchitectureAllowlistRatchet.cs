using System;
using System.Collections.Generic;
using System.Linq;

namespace SyntheticTests.Architecture
{
    /// <summary>
    /// Identifies which legacy governance registry owns an allowlisted type-level violation.
    /// </summary>
    public enum AllowlistRegistryKind
    {
        TransitionalSpecBacklog,
        DormantBoyScoutAllowlist
    }

    /// <summary>
    /// Immutable type-level allowlist entry keyed strictly by <c>(Slice, Rule, TypeName)</c>
    /// (never whole-slice wildcards).
    /// </summary>
    public sealed class AllowlistEntry : IEquatable<AllowlistEntry>
    {
        public AllowlistEntry(
            string slice,
            FitnessRule rule,
            string typeName,
            AllowlistRegistryKind registry,
            string? targetSpec = null)
        {
            if (string.IsNullOrWhiteSpace(slice))
            {
                throw new ArgumentException("Allowlist slice cannot be null or whitespace.", nameof(slice));
            }

            if (string.IsNullOrWhiteSpace(typeName))
            {
                throw new ArgumentException("Allowlist typeName cannot be null or whitespace.", nameof(typeName));
            }

            if (slice.IndexOf('*') >= 0 || slice.IndexOf('?') >= 0)
            {
                throw new ArgumentException($"Whole-slice or wildcard entries are forbidden in Slice ('{slice}').", nameof(slice));
            }

            if (typeName.IndexOf('*') >= 0 || typeName.IndexOf('?') >= 0)
            {
                throw new ArgumentException($"Wildcard entries are forbidden in TypeName ('{typeName}').", nameof(typeName));
            }

            if (rule < FitnessRule.InteractiveSliceSeamPurity)
            {
                throw new ArgumentException($"Global 0%-allowlist rule '{rule}' cannot have allowlist entries.", nameof(rule));
            }

            Slice = slice;
            Rule = rule;
            TypeName = typeName;
            Registry = registry;
            TargetSpec = targetSpec;
        }

        public string Slice { get; }
        public FitnessRule Rule { get; }
        public string TypeName { get; }
        public AllowlistRegistryKind Registry { get; }
        public string? TargetSpec { get; }

        public bool Equals(AllowlistEntry? other)
        {
            if (other is null)
            {
                return false;
            }

            return Rule == other.Rule &&
                   string.Equals(Slice, other.Slice, StringComparison.Ordinal) &&
                   string.Equals(TypeName, other.TypeName, StringComparison.Ordinal);
        }

        public override bool Equals(object? obj) => Equals(obj as AllowlistEntry);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = (int)Rule;
                hash = (hash * 397) ^ StringComparer.Ordinal.GetHashCode(Slice);
                hash = (hash * 397) ^ StringComparer.Ordinal.GetHashCode(TypeName);
                return hash;
            }
        }

        public override string ToString() => $"({Slice}, {Rule}, {TypeName})";
    }

    /// <summary>
    /// Concrete type-level rule violation observed during Roslyn AST inspection.
    /// </summary>
    public sealed class TypeLevelViolation
    {
        public TypeLevelViolation(string slice, FitnessRule rule, string typeName, string detail)
        {
            if (string.IsNullOrWhiteSpace(slice))
            {
                throw new ArgumentException("Violation slice cannot be null or whitespace.", nameof(slice));
            }

            if (string.IsNullOrWhiteSpace(typeName))
            {
                throw new ArgumentException("Violation typeName cannot be null or whitespace.", nameof(typeName));
            }

            Slice = slice;
            Rule = rule;
            TypeName = typeName;
            Detail = detail ?? string.Empty;
        }

        public string Slice { get; }
        public FitnessRule Rule { get; }
        public string TypeName { get; }
        public string Detail { get; }
    }

    /// <summary>
    /// Bi-directional ratchet evaluator asserting both directions for type-level fitness gates:
    /// (1) ceiling lock failing on any unlisted violating type, and
    /// (2) one-way ratchet failing on any allowlisted entry that no longer violates the rule.
    /// </summary>
    public static class AllowlistRatchetEvaluator
    {
        private readonly struct RatchetKey : IEquatable<RatchetKey>
        {
            public RatchetKey(string slice, FitnessRule rule, string typeName)
            {
                Slice = slice;
                Rule = rule;
                TypeName = typeName;
            }

            public string Slice { get; }
            public FitnessRule Rule { get; }
            public string TypeName { get; }

            public bool Equals(RatchetKey other)
            {
                return Rule == other.Rule &&
                       string.Equals(Slice, other.Slice, StringComparison.Ordinal) &&
                       string.Equals(TypeName, other.TypeName, StringComparison.Ordinal);
            }

            public override bool Equals(object? obj) => obj is RatchetKey other && Equals(other);

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = (int)Rule;
                    hash = (hash * 397) ^ StringComparer.Ordinal.GetHashCode(Slice);
                    hash = (hash * 397) ^ StringComparer.Ordinal.GetHashCode(TypeName);
                    return hash;
                }
            }
        }

        public static IReadOnlyList<string> Evaluate(
            FitnessRule rule,
            IEnumerable<TypeLevelViolation> observedViolations,
            IEnumerable<AllowlistEntry> allowlistEntries)
        {
            if (observedViolations == null) throw new ArgumentNullException(nameof(observedViolations));
            if (allowlistEntries == null) throw new ArgumentNullException(nameof(allowlistEntries));

            var failures = new List<string>();
            var allowedByKey = new Dictionary<RatchetKey, AllowlistEntry>();

            foreach (AllowlistEntry entry in allowlistEntries)
            {
                if (entry.Rule != rule)
                {
                    continue;
                }

                var key = new RatchetKey(entry.Slice, entry.Rule, entry.TypeName);
                if (allowedByKey.ContainsKey(key))
                {
                    failures.Add(rule.FormatViolation(
                        $"Duplicate allowlist entry ({entry.Slice}, {entry.Rule}, {entry.TypeName}) in {entry.Registry}."));
                }
                else
                {
                    allowedByKey[key] = entry;
                }
            }

            var observedByKey = new Dictionary<RatchetKey, List<string>>();
            foreach (TypeLevelViolation violation in observedViolations)
            {
                if (violation.Rule != rule)
                {
                    failures.Add(rule.FormatViolation(
                        $"Rule mismatch while evaluating {rule}: observed violation carried rule {violation.Rule} for ({violation.Slice}, {violation.TypeName})."));
                    continue;
                }

                var key = new RatchetKey(violation.Slice, violation.Rule, violation.TypeName);
                if (!observedByKey.TryGetValue(key, out List<string>? details))
                {
                    details = new List<string>();
                    observedByKey[key] = details;
                }

                if (!string.IsNullOrWhiteSpace(violation.Detail) && !details.Contains(violation.Detail, StringComparer.Ordinal))
                {
                    details.Add(violation.Detail);
                }
            }

            // Direction 1: Ceiling lock — fail on any unlisted violating type
            foreach (KeyValuePair<RatchetKey, List<string>> kvp in observedByKey
                .OrderBy(k => k.Key.Slice, StringComparer.Ordinal)
                .ThenBy(k => k.Key.TypeName, StringComparer.Ordinal))
            {
                if (!allowedByKey.ContainsKey(kvp.Key))
                {
                    string detailText = kvp.Value.Count > 0
                        ? string.Join(" | ", kvp.Value)
                        : "Violation detected.";
                    failures.Add(rule.FormatViolation(
                        $"Unlisted violation ({kvp.Key.Slice}, {kvp.Key.Rule}, {kvp.Key.TypeName}): {detailText} Fix the violation rather than adding allowlist debt."));
                }
            }

            // Direction 2: One-way ratchet — fail on any stale allowlist entry
            foreach (KeyValuePair<RatchetKey, AllowlistEntry> kvp in allowedByKey
                .OrderBy(k => k.Key.Slice, StringComparer.Ordinal)
                .ThenBy(k => k.Key.TypeName, StringComparer.Ordinal))
            {
                if (!observedByKey.ContainsKey(kvp.Key))
                {
                    failures.Add(rule.FormatViolation(
                        $"Stale allowlist entry ({kvp.Key.Slice}, {kvp.Key.Rule}, {kvp.Key.TypeName}) in {kvp.Value.Registry}: type no longer violates {rule}. Remove this entry to ratchet debt down."));
                }
            }

            return failures;
        }
    }

    /// <summary>
    /// Explicit type-level allowlist registries governed by the bi-directional ratchet.
    /// Note: <c>Diagnostics</c> / <c>StorageUtil.PurgeSchema</c> silent-catch and helper hardening
    /// is intentionally kept OUT of these registries as a Spec 6 prose backlog note because none of the
    /// 12 Roslyn rules inspect swallowed exceptions.
    /// </summary>
    public static class ArchitectureAllowlists
    {
        public static readonly IReadOnlyCollection<string> ExemplarZeroAllowlistSlices = new[]
        {
            "Diagnostics",
            "MultiModelPdfExport",
            "ShadowStudy",
            "SharedCoordinates",
            "SheetIndex",
            "Platform/Revit"
        };

        public static readonly IReadOnlyCollection<string> ExpectedTransitionalSlices = new[]
        {
            "MergeDuplicates",
            "SettingsDashboard",
            "StandardsManagement",
            "ViewManagement",
            "Worksets",
            "Platform/Settings",
            "RevitDOM"
        };

        public static readonly IReadOnlyCollection<string> ExpectedDormantSlices = new[]
        {
            "AutoTagger",
            "DetailItemFactory",
            "FamilyManagement",
            "MaterialManagement"
        };

        /// <summary>
        /// Transitional Spec backlog entries targeting active Specs 5–9 migration burn-down
        /// (excluding <c>Diagnostics</c>/<c>StorageUtil</c>, tracked in prose for Spec 6).
        /// </summary>
        public static readonly IReadOnlyList<AllowlistEntry> TransitionalSpecBacklog = new[]
        {
            // MergeDuplicates (Spec 5)
            Transitional("MergeDuplicates", FitnessRule.InteractiveSliceSeamPurity, "MergeDuplicatesViewModel", "Spec 5"),
            Transitional("MergeDuplicates", FitnessRule.TransactionPlacement, "ProcessMergeEventHandler", "Spec 5"),
            Transitional("MergeDuplicates", FitnessRule.TransactionFailureGuard, "ProcessMergeEventHandler", "Spec 5"),
            Transitional("MergeDuplicates", FitnessRule.ExternalEventAndDialogPurity, "MergeDuplicatesViewModel", "Spec 5"),
            Transitional("MergeDuplicates", FitnessRule.ExternalEventAndDialogPurity, "ProcessMergeEventHandler", "Spec 5"),

            // SettingsDashboard (Spec 6)
            Transitional("SettingsDashboard", FitnessRule.InteractiveSliceSeamPurity, "FileUtilitySettingsViewModel", "Spec 6"),
            Transitional("SettingsDashboard", FitnessRule.InteractiveSliceSeamPurity, "NetworkPathsWizardViewModel", "Spec 6"),
            Transitional("SettingsDashboard", FitnessRule.InteractiveSliceSeamPurity, "SettingsDashboardViewModel", "Spec 6"),
            Transitional("SettingsDashboard", FitnessRule.InteractiveSliceSeamPurity, "SyncResolutionWindow", "Spec 6"),
            Transitional("SettingsDashboard", FitnessRule.InteractiveSliceSeamPurity, "SyncSettingsViewModel", "Spec 6"),
            Transitional("SettingsDashboard", FitnessRule.InteractiveSliceSeamPurity, "SyncToastNotification", "Spec 6"),
            Transitional("SettingsDashboard", FitnessRule.ExternalEventAndDialogPurity, "FileUtilitySettingsViewModel", "Spec 6"),
            Transitional("SettingsDashboard", FitnessRule.ExternalEventAndDialogPurity, "NetworkPathsWizardViewModel", "Spec 6"),
            Transitional("SettingsDashboard", FitnessRule.ExternalEventAndDialogPurity, "SettingsDashboardViewModel", "Spec 6"),
            Transitional("SettingsDashboard", FitnessRule.ExternalEventAndDialogPurity, "SyncExternalEventHandler", "Spec 6"),
            Transitional("SettingsDashboard", FitnessRule.ExternalEventAndDialogPurity, "SyncResolutionWindow", "Spec 6"),
            Transitional("SettingsDashboard", FitnessRule.ExternalEventAndDialogPurity, "SyncToastNotification", "Spec 6"),

            // StandardsManagement (Specs 6 & 7)
            Transitional("StandardsManagement", FitnessRule.InteractiveSliceSeamPurity, "CategorySelectionItem", "Spec 7"),
            Transitional("StandardsManagement", FitnessRule.InteractiveSliceSeamPurity, "CategorySelectionViewModel", "Spec 7"),
            Transitional("StandardsManagement", FitnessRule.InteractiveSliceSeamPurity, "CompoundLayerRowVM", "Spec 7"),
            Transitional("StandardsManagement", FitnessRule.InteractiveSliceSeamPurity, "ElementTypeWrapperVM", "Spec 7"),
            Transitional("StandardsManagement", FitnessRule.InteractiveSliceSeamPurity, "IProjectStandardsDashboard", "Spec 7"),
            Transitional("StandardsManagement", FitnessRule.InteractiveSliceSeamPurity, "NestedDataEditorViewModel", "Spec 7"),
            Transitional("StandardsManagement", FitnessRule.InteractiveSliceSeamPurity, "ParameterWrapperVM", "Spec 7"),
            Transitional("StandardsManagement", FitnessRule.InteractiveSliceSeamPurity, "ProjectStandardsDashboardViewModel", "Spec 7"),
            Transitional("StandardsManagement", FitnessRule.InteractiveSliceSeamPurity, "ProjectStandardsExternalEventHandler", "Spec 7"),
            Transitional("StandardsManagement", FitnessRule.InteractiveSliceSeamPurity, "RevitDocumentItem", "Spec 7"),
            Transitional("StandardsManagement", FitnessRule.InteractiveSliceSeamPurity, "RevitSourceExtractionRequest", "Spec 7"),
            Transitional("StandardsManagement", FitnessRule.InteractiveSliceSeamPurity, "SelectRevitDocumentViewModel", "Spec 7"),
            Transitional("StandardsManagement", FitnessRule.InteractiveSliceSeamPurity, "StagingQueueViewModel", "Spec 7"),
            Transitional("StandardsManagement", FitnessRule.InteractiveSliceSeamPurity, "StandardsExecutionPipelineViewModel", "Spec 7"),
            Transitional("StandardsManagement", FitnessRule.InteractiveSliceSeamPurity, "StandardsReviewViewModel", "Spec 7"),
            Transitional("StandardsManagement", FitnessRule.InteractiveSliceSeamPurity, "StandardsSettings", "Spec 6"),
            Transitional("StandardsManagement", FitnessRule.InteractiveSliceSeamPurity, "StandardsSettingsViewModel", "Spec 6"),
            Transitional("StandardsManagement", FitnessRule.InteractiveSliceSeamPurity, "StandardsSourceTreeViewModel", "Spec 7"),
            Transitional("StandardsManagement", FitnessRule.InteractiveSliceSeamPurity, "VisibilityOverrideRowVM", "Spec 7"),
            Transitional("StandardsManagement", FitnessRule.TransactionPlacement, "RevitFamilyEnforcer", "Spec 7"),
            Transitional("StandardsManagement", FitnessRule.TransactionPlacement, "StandardsExecutionPipeline", "Spec 7"),
            Transitional("StandardsManagement", FitnessRule.TransactionPlacement, "StandardsReviewViewModel", "Spec 7"),
            Transitional("StandardsManagement", FitnessRule.TransactionFailureGuard, "StandardsReviewViewModel", "Spec 7"),
            Transitional("StandardsManagement", FitnessRule.ExternalEventAndDialogPurity, "ParameterWrapperVM", "Spec 7"),
            Transitional("StandardsManagement", FitnessRule.ExternalEventAndDialogPurity, "ProjectStandardsDashboardViewModel", "Spec 7"),

            // ViewManagement (Specs 6 & 8)
            Transitional("ViewManagement", FitnessRule.InteractiveSliceSeamPurity, "ViewAutoNumModel", "Spec 8"),
            Transitional("ViewManagement", FitnessRule.InteractiveSliceSeamPurity, "ViewAutoNumSettings", "Spec 6"),
            Transitional("ViewManagement", FitnessRule.InteractiveSliceSeamPurity, "ViewAutoNumSettingsViewModel", "Spec 6"),
            Transitional("ViewManagement", FitnessRule.InteractiveSliceSeamPurity, "ViewAutoNumWizardViewModel", "Spec 6"),
            Transitional("ViewManagement", FitnessRule.TransactionPlacement, "ConvertDraftingToLegend", "Spec 8"),
            Transitional("ViewManagement", FitnessRule.TransactionPlacement, "LegendsUtil", "Spec 8"),
            Transitional("ViewManagement", FitnessRule.TransactionFailureGuard, "ConvertLegendToDrafting", "Spec 8"),
            Transitional("ViewManagement", FitnessRule.TransactionFailureGuard, "LegendsUtil", "Spec 8"),
            Transitional("ViewManagement", FitnessRule.TransactionFailureGuard, "ViewsAutoNumber", "Spec 8"),
            Transitional("ViewManagement", FitnessRule.ExternalEventAndDialogPurity, "ViewAutoNumSettingsViewModel", "Spec 6"),

            // Worksets (Specs 6 & 9)
            Transitional("Worksets", FitnessRule.InteractiveSliceSeamPurity, "ElementsOnWorkset", "Spec 9"),
            Transitional("Worksets", FitnessRule.InteractiveSliceSeamPurity, "WorksetSettings", "Spec 6"),
            Transitional("Worksets", FitnessRule.InteractiveSliceSeamPurity, "WorksetSettingsViewModel", "Spec 6"),
            Transitional("Worksets", FitnessRule.TransactionPlacement, "ScopeBoxUtil", "Spec 9"),
            Transitional("Worksets", FitnessRule.TransactionPlacement, "WorksetUtil", "Spec 9"),
            Transitional("Worksets", FitnessRule.TransactionFailureGuard, "ScopeBoxesMoveToWorkset", "Spec 9"),
            Transitional("Worksets", FitnessRule.TransactionFailureGuard, "ScopeBoxUtil", "Spec 9"),
            Transitional("Worksets", FitnessRule.TransactionFailureGuard, "WorksetSetFile", "Spec 9"),
            Transitional("Worksets", FitnessRule.TransactionFailureGuard, "WorksetUtil", "Spec 9"),
            Transitional("Worksets", FitnessRule.ExternalEventAndDialogPurity, "WorksetSettingsViewModel", "Spec 6"),

            // Platform/Settings (Spec 6)
            Transitional("Platform/Settings", FitnessRule.TransactionPlacement, "LegacySettingsMigration", "Spec 6"),
            Transitional("Platform/Settings", FitnessRule.TransactionPlacement, "SettingsManager", "Spec 6"),
            Transitional("Platform/Settings", FitnessRule.TransactionFailureGuard, "LegacySettingsMigration", "Spec 6"),
            Transitional("Platform/Settings", FitnessRule.TransactionFailureGuard, "SettingsManager", "Spec 6"),

            // RevitDOM (Spec 5)
            Transitional("RevitDOM", FitnessRule.TransactionPlacement, "AliasSwapEngine", "Spec 5"),
            Transitional("RevitDOM", FitnessRule.TransactionFailureGuard, "AliasSwapEngine", "Spec 5")
        };

        /// <summary>
        /// Dormant low-churn feature slices governed by the 3-Tier Boy-Scout Rule.
        /// </summary>
        public static readonly IReadOnlyList<AllowlistEntry> DormantBoyScoutAllowlist = new[]
        {
            // AutoTagger
            Dormant("AutoTagger", FitnessRule.InteractiveSliceSeamPurity, "ManageTemplatesViewModel", "BoyScout"),
            Dormant("AutoTagger", FitnessRule.InteractiveSliceSeamPurity, "SetTemplateViewModel", "BoyScout"),
            Dormant("AutoTagger", FitnessRule.InteractiveSliceSeamPurity, "TagTemplate", "BoyScout"),
            Dormant("AutoTagger", FitnessRule.TransactionPlacement, "TemplateStorageRepository", "BoyScout"),
            Dormant("AutoTagger", FitnessRule.TransactionFailureGuard, "CmdBatchTag", "BoyScout"),
            Dormant("AutoTagger", FitnessRule.TransactionFailureGuard, "TemplateStorageRepository", "BoyScout"),
            Dormant("AutoTagger", FitnessRule.SingleSchemaExtensibleStorage, "TemplateStorageRepository", "BoyScout"),

            // DetailItemFactory
            Dormant("DetailItemFactory", FitnessRule.InteractiveSliceSeamPurity, "DetailItemFactoryResultsViewModel", "BoyScout"),
            Dormant("DetailItemFactory", FitnessRule.InteractiveSliceSeamPurity, "DetailItemFactorySettings", "BoyScout"),
            Dormant("DetailItemFactory", FitnessRule.InteractiveSliceSeamPurity, "DetailItemFactorySettingsViewModel", "BoyScout"),
            Dormant("DetailItemFactory", FitnessRule.InteractiveSliceSeamPurity, "DetailItemFactoryViewModel", "BoyScout"),
            Dormant("DetailItemFactory", FitnessRule.InteractiveSliceSeamPurity, "SelectedElementItemViewModel", "BoyScout"),
            Dormant("DetailItemFactory", FitnessRule.TransactionFailureGuard, "DetailItemFactoryEventHandler", "BoyScout"),
            Dormant("DetailItemFactory", FitnessRule.ExternalEventAndDialogPurity, "CmdDetailItemFactory", "BoyScout"),
            Dormant("DetailItemFactory", FitnessRule.ExternalEventAndDialogPurity, "DetailItemFactoryEventHandler", "BoyScout"),
            Dormant("DetailItemFactory", FitnessRule.ExternalEventAndDialogPurity, "DetailItemFactoryResultsViewModel", "BoyScout"),

            // FamilyManagement
            Dormant("FamilyManagement", FitnessRule.ExternalEventAndDialogPurity, "AuditPurgeAllFamilies", "BoyScout"),

            // MaterialManagement
            Dormant("MaterialManagement", FitnessRule.InteractiveSliceSeamPurity, "MaterialLibrarySettings", "BoyScout"),
            Dormant("MaterialManagement", FitnessRule.InteractiveSliceSeamPurity, "MaterialLibraryViewModel", "BoyScout"),
            Dormant("MaterialManagement", FitnessRule.InteractiveSliceSeamPurity, "ProjectMaterialSettings", "BoyScout"),
            Dormant("MaterialManagement", FitnessRule.InteractiveSliceSeamPurity, "ProjectMaterialsViewModel", "BoyScout"),
            Dormant("MaterialManagement", FitnessRule.TransactionFailureGuard, "MaterialsRepathAll", "BoyScout"),
            Dormant("MaterialManagement", FitnessRule.TransactionFailureGuard, "PaintElements", "BoyScout")
        };

        public static IReadOnlyList<AllowlistEntry> AllEntries { get; } =
            TransitionalSpecBacklog.Concat(DormantBoyScoutAllowlist).ToArray();

        public static IReadOnlyList<string> ValidateRegistryIntegrity(
            IReadOnlyList<AllowlistEntry> transitional,
            IReadOnlyList<AllowlistEntry> dormant)
        {
            var errors = new List<string>();
            var transitionalSet = new HashSet<string>(ExpectedTransitionalSlices, StringComparer.Ordinal);
            var dormantSet = new HashSet<string>(ExpectedDormantSlices, StringComparer.Ordinal);
            var exemplarSet = new HashSet<string>(ExemplarZeroAllowlistSlices, StringComparer.Ordinal);
            var seen = new HashSet<AllowlistEntry>();

            foreach (AllowlistEntry entry in transitional)
            {
                if (entry.Registry != AllowlistRegistryKind.TransitionalSpecBacklog)
                {
                    errors.Add($"TransitionalSpecBacklog contains entry with wrong Registry '{entry.Registry}': {entry}.");
                }

                if (exemplarSet.Contains(entry.Slice))
                {
                    errors.Add($"Exemplar/clean slice '{entry.Slice}' must have 0 allowlist entries, found {entry}.");
                }
                else if (!transitionalSet.Contains(entry.Slice))
                {
                    errors.Add($"TransitionalSpecBacklog entry {entry} belongs to unexpected slice '{entry.Slice}'.");
                }

                if (!seen.Add(entry))
                {
                    errors.Add($"Duplicate allowlist entry {entry} in TransitionalSpecBacklog.");
                }
            }

            foreach (AllowlistEntry entry in dormant)
            {
                if (entry.Registry != AllowlistRegistryKind.DormantBoyScoutAllowlist)
                {
                    errors.Add($"DormantBoyScoutAllowlist contains entry with wrong Registry '{entry.Registry}': {entry}.");
                }

                if (exemplarSet.Contains(entry.Slice))
                {
                    errors.Add($"Exemplar/clean slice '{entry.Slice}' must have 0 allowlist entries, found {entry}.");
                }
                else if (!dormantSet.Contains(entry.Slice))
                {
                    errors.Add($"DormantBoyScoutAllowlist entry {entry} belongs to unexpected slice '{entry.Slice}'.");
                }

                if (!seen.Add(entry))
                {
                    errors.Add($"Duplicate/overlapping allowlist entry {entry} across registries.");
                }
            }

            return errors;
        }

        private static AllowlistEntry Transitional(string slice, FitnessRule rule, string typeName, string targetSpec) =>
            new AllowlistEntry(slice, rule, typeName, AllowlistRegistryKind.TransitionalSpecBacklog, targetSpec);

        private static AllowlistEntry Dormant(string slice, FitnessRule rule, string typeName, string targetSpec) =>
            new AllowlistEntry(slice, rule, typeName, AllowlistRegistryKind.DormantBoyScoutAllowlist, targetSpec);
    }
}
