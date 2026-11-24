using System;

namespace SyntheticTests.Architecture
{
    /// <summary>
    /// Strongly-typed architectural fitness gates enforced across the Synthetic codebase.
    /// Rules 1-7 are global 0%-allowlist gates; Rules 8-12 are type-level bi-directionally ratcheted gates.
    /// </summary>
    public enum FitnessRule
    {
        /// <summary>
        /// Enforces the 3-root co-located partition layout (<c>Core/</c>/<c>Addin/</c>, <c>Logic/</c>/<c>Revit/</c>),
        /// valid project head locations, zero legacy clutter, and themed XAML dictionary merging.
        /// </summary>
        CoLocatedPartitionLayout = 1,

        /// <summary>
        /// Enforces zero Revit API, XAML/BAML, Excel PIA, or <c>ProgressCoordinator</c> leakage in <c>Core/</c> and <c>Logic/</c>,
        /// and bans <c>#if</c>/<c>#elif REVIT*</c> in <c>Core/</c> as well as <c>REVIT&lt;CURRENT_LATEST_VERSION&gt;</c> directives repo-wide.
        /// </summary>
        CorePurityAndZeroPreprocessor = 2,

        /// <summary>
        /// Enforces the 4-band acyclic dependency graph across C# Roslyn AST and XAML <c>xmlns:</c> declarations,
        /// including zero sibling feature imports, <c>Platform.Host</c> isolation, and retired namespace bans.
        /// </summary>
        FourBandDependencyGraph = 3,

        /// <summary>
        /// Enforces <c>Domain/</c> graduation thresholds once <c>src/Domain/</c> exists:
        /// at least 2 sub-modules, each consumed by at least 2 distinct feature slices, with zero sibling <c>Domain</c> imports.
        /// </summary>
        DomainGraduationThreshold = 4,

        /// <summary>
        /// Verifies every <c>ribbon_config.json</c> command class resolves via Roslyn to a concrete <c>IExternalCommand</c>
        /// in <c>src/**/Addin/**</c> and every referenced PNG icon asset exists under <c>src/Assets/</c>.
        /// </summary>
        RibbonConfigIntegrity = 5,

        /// <summary>
        /// Enforces 1-to-1 parity across concrete <c>ISettingModule</c> definitions (excluding <c>SyncSettings</c>),
        /// <c>HostSettingsRegistrar</c> registrations, and <c>SyntheticSettings.template.json</c> keys.
        /// </summary>
        SettingsRegistryTemplateParity = 6,

        /// <summary>
        /// Enforces canonical typed <c>[Category]</c> attribution across all test classes/methods,
        /// zero <c>Tier1_*</c>/<c>Tier2_*</c> or retired failure/config symbols, and the exact <c>Synthetic.LegacyMockTests</c> file ceiling.
        /// </summary>
        TestCategoryAndLegacyMockRatchet = 7,

        /// <summary>
        /// Enforces Revit-free purity inside interactive slice UI/model/settings folders across all feature slices.
        /// </summary>
        InteractiveSliceSeamPurity = 8,

        /// <summary>
        /// Restricts raw <c>new Transaction</c> and <c>new TransactionGroup</c> instantiations to permitted roles.
        /// </summary>
        TransactionPlacement = 9,

        /// <summary>
        /// Requires every transaction outside <c>Platform/Revit/Addin</c> to attach a failure guard or route through <c>DocumentTransactionExtensions</c>.
        /// </summary>
        TransactionFailureGuard = 10,

        /// <summary>
        /// Enforces zero static <c>ExternalEvent</c> fields, zero raw <c>DeleteAllWarnings()</c> outside <c>Platform/Revit/Addin</c>,
        /// and UI/ViewModel/Handler dialog purity.
        /// </summary>
        ExternalEventAndDialogPurity = 11,

        /// <summary>
        /// Restricts <c>new SchemaBuilder(...)</c> instantiations to <c>Platform/Settings/Addin</c>.
        /// </summary>
        SingleSchemaExtensibleStorage = 12
    }

    /// <summary>
    /// Formatting helpers for reporting <see cref="FitnessRule"/> violations.
    /// </summary>
    public static class FitnessRuleExtensions
    {
        /// <summary>
        /// Prefixes a violation detail with the strongly-typed <see cref="FitnessRule"/> identifier.
        /// </summary>
        public static string FormatViolation(this FitnessRule rule, string detail)
        {
            return $"[{rule}] {detail}";
        }
    }
}
