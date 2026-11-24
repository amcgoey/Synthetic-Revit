using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Synthetic.Features.ShadowStudy.Settings;
using Synthetic.Platform.Host;
using Synthetic.Platform.IO;
using Synthetic.Platform.Settings;
using SyntheticTests.Helpers;

namespace SyntheticTests.Architecture
{
    /// <summary>
    /// Single-parse Roslyn + XML/JSON Architecture Fitness Tests enforcing all 12
    /// <see cref="FitnessRule"/> gates (7 global 0%-allowlist rules and 5 bi-directionally
    /// ratcheted type-level rules) across <c>src/</c> and <c>tests/</c>.
    /// </summary>
    [TestFixture]
    [ArchitectureTest]
    public class ArchitectureFitnessTests
    {
        private ArchitectureWorkspaceSnapshot? _snapshot;

        private ArchitectureWorkspaceSnapshot Snapshot =>
            _snapshot ?? throw new InvalidOperationException("ArchitectureWorkspaceSnapshot was not initialized in [OneTimeSetUp].");

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            string projectRoot = TestPathHelper.GetProjectRoot();
            _snapshot = ArchitectureWorkspaceSnapshot.GetShared(projectRoot);
            TestContext.WriteLine(
                $"ArchitectureWorkspaceSnapshot loaded {_snapshot.SourceFiles.Count} .cs, {_snapshot.XamlFiles.Count} .xaml, and {_snapshot.ProjectAndBuildFiles.Count} build files in {_snapshot.LoadDuration.TotalMilliseconds:F1} ms (CURRENT_LATEST_VERSION={_snapshot.CurrentLatestVersion}).");
        }

        [Test]
        public void FitnessRuleEnum_DefinesAllTwelveStronglyTypedGates()
        {
            FitnessRule[] values = (FitnessRule[])Enum.GetValues(typeof(FitnessRule));
            Assert.AreEqual(12, values.Length);

            var expectedNames = new[]
            {
                nameof(FitnessRule.CoLocatedPartitionLayout),
                nameof(FitnessRule.CorePurityAndZeroPreprocessor),
                nameof(FitnessRule.FourBandDependencyGraph),
                nameof(FitnessRule.DomainGraduationThreshold),
                nameof(FitnessRule.RibbonConfigIntegrity),
                nameof(FitnessRule.SettingsRegistryTemplateParity),
                nameof(FitnessRule.TestCategoryAndLegacyMockRatchet),
                nameof(FitnessRule.InteractiveSliceSeamPurity),
                nameof(FitnessRule.TransactionPlacement),
                nameof(FitnessRule.TransactionFailureGuard),
                nameof(FitnessRule.ExternalEventAndDialogPurity),
                nameof(FitnessRule.SingleSchemaExtensibleStorage)
            };

            CollectionAssert.AreEquivalent(expectedNames, values.Select(v => v.ToString()).ToList());
            Assert.GreaterOrEqual(Snapshot.CurrentLatestVersion, 2026);
            Assert.IsNotEmpty(Snapshot.SourceFiles);
        }

        [Test]
        public void Rule01_CoLocatedPartitionLayout_EnforcesThreeRootLayoutAndThemedViews()
        {
            IReadOnlyList<string> violations = ArchitectureFitnessRules.EvaluateCoLocatedPartitionLayout(Snapshot);
            Assert.IsEmpty(violations, string.Join(Environment.NewLine, violations));
        }

        [Test]
        public void Rule02_CorePurityAndZeroPreprocessor_EnforcesPureCoreAndLatestBaseline()
        {
            Assembly coreAssembly = typeof(RibbonItemFilter).Assembly;
            Assembly fileUtilAssembly = typeof(FileUtil).Assembly;
            Assert.AreSame(coreAssembly, fileUtilAssembly);

            Assembly logicTestsAssembly = typeof(ArchitectureFitnessTests).Assembly;
            IReadOnlyList<string> violations = ArchitectureFitnessRules.EvaluateCorePurityAndZeroPreprocessor(
                Snapshot,
                coreAssembly,
                logicTestsAssembly);

            Assert.IsEmpty(violations, string.Join(Environment.NewLine, violations));
        }

        [Test]
        public void Rule03_FourBandDependencyGraph_EnforcesAcyclicBandBoundariesAcrossCSharpAndXaml()
        {
            IReadOnlyList<string> violations = ArchitectureFitnessRules.EvaluateFourBandDependencyGraph(Snapshot);
            Assert.IsEmpty(violations, string.Join(Environment.NewLine, violations));
        }

        [Test]
        public void Rule04_DomainGraduationThreshold_PassesVacuouslyOrEnforcesMultiSliceThreshold()
        {
            IReadOnlyList<string> violations = ArchitectureFitnessRules.EvaluateDomainGraduationThreshold(Snapshot);
            Assert.IsEmpty(violations, string.Join(Environment.NewLine, violations));
        }

        [Test]
        public void Rule05_RibbonConfigIntegrity_ResolvesAllCommandClassesAndPngAssetsViaRoslyn()
        {
            IReadOnlyList<string> violations = ArchitectureFitnessRules.EvaluateRibbonConfigIntegrity(Snapshot);
            Assert.IsEmpty(violations, string.Join(Environment.NewLine, violations));
        }

        [Test]
        public void Rule06_SettingsRegistryTemplateParity_EnforcesOneToOneParityAndCoreDefaultsRoundTrip()
        {
            IReadOnlyList<string> violations = ArchitectureFitnessRules.EvaluateSettingsRegistryTemplateParity(Snapshot);
            Assert.IsEmpty(violations, string.Join(Environment.NewLine, violations));

            Config? templateConfig = Config.ByJson(Snapshot.SettingsTemplate.RawJson);
            Assert.IsNotNull(templateConfig);
            Assert.IsNotNull(templateConfig.settings);
            Assert.IsFalse(templateConfig.Contains(SettingsModuleKeys.Sync), "SyntheticSettings.template.json must exclude SyncSettings.");

            foreach (string key in Snapshot.SettingsTemplate.SettingsKeys)
            {
                Assert.IsTrue(templateConfig.Contains(key), $"Config.ByJson lost template key '{key}'.");
            }

            ShadowStudySettings fromTemplate = templateConfig.GetSettings<ShadowStudySettings>(ShadowStudySettings.Name);
            ShadowStudySettings coreDefaults = new ShadowStudySettings().Defaults();
            Assert.IsTrue(fromTemplate.IsValid());
            Assert.IsTrue(
                JToken.DeepEquals(JObject.FromObject(coreDefaults), JObject.FromObject(fromTemplate)),
                "ShadowStudySettings defaults in Synthetic.Core must match SyntheticSettings.template.json.");

            var coreConfig = new Config().Defaults(new[]
            {
                new KeyValuePair<string, Func<ISettingModule>>(ShadowStudySettings.Name, () => new ShadowStudySettings().Defaults())
            });
            Config roundTripped = Config.ByJson(JsonConvert.SerializeObject(coreConfig, Formatting.Indented));
            ShadowStudySettings roundTrippedShadow = roundTripped.GetSettings<ShadowStudySettings>(ShadowStudySettings.Name);
            Assert.IsTrue(roundTrippedShadow.IsValid());
            Assert.AreEqual(coreDefaults.DefaultTimeIncrementMinutes, roundTrippedShadow.DefaultTimeIncrementMinutes);
        }

        [Test]
        public void Rule07_TestCategoryAndLegacyMockRatchet_EnforcesTypedCategoriesRetiredSymbolsAndExactCeiling()
        {
            IReadOnlyList<string> violations = ArchitectureFitnessRules.EvaluateTestCategoryAndLegacyMockRatchet(Snapshot);
            Assert.IsEmpty(violations, string.Join(Environment.NewLine, violations));
        }

        [Test]
        public void AllowlistRegistries_HaveZeroEntriesForExemplarSlicesDiagnosticsAndGlobalRules()
        {
            IReadOnlyList<string> integrityErrors = ArchitectureAllowlists.ValidateRegistryIntegrity(
                ArchitectureAllowlists.TransitionalSpecBacklog,
                ArchitectureAllowlists.DormantBoyScoutAllowlist);

            Assert.IsEmpty(integrityErrors, string.Join(Environment.NewLine, integrityErrors));

            foreach (string zeroSlice in new[] { "BatchPrint", "ShadowStudy", "SharedCoordinates", "SheetIndex", "Diagnostics", "Platform/Revit" })
            {
                Assert.AreEqual(
                    0,
                    ArchitectureAllowlists.AllEntries.Count(e => string.Equals(e.Slice, zeroSlice, StringComparison.Ordinal)),
                    $"Slice '{zeroSlice}' must have 0 allowlist entries.");
            }
        }

        [Test]
        public void Rule08_InteractiveSliceSeamPurity_EnforcesUniformSeamPurityAndBiDirectionalRatchet()
        {
            IReadOnlyList<string> failures = ArchitectureFitnessRules.EvaluateInteractiveSliceSeamPurity(Snapshot);
            Assert.IsEmpty(failures, string.Join(Environment.NewLine, failures));
        }

        [Test]
        public void Rule09_TransactionPlacement_EnforcesPermittedRolesAndBiDirectionalRatchet()
        {
            IReadOnlyList<string> failures = ArchitectureFitnessRules.EvaluateTransactionPlacement(Snapshot);
            Assert.IsEmpty(failures, string.Join(Environment.NewLine, failures));
        }

        [Test]
        public void Rule10_TransactionFailureGuard_EnforcesPerTransactionFailureGuardsAndBiDirectionalRatchet()
        {
            IReadOnlyList<string> failures = ArchitectureFitnessRules.EvaluateTransactionFailureGuard(Snapshot);
            Assert.IsEmpty(failures, string.Join(Environment.NewLine, failures));
        }

        [Test]
        public void Rule11_ExternalEventAndDialogPurity_EnforcesPurityAndBiDirectionalRatchet()
        {
            IReadOnlyList<string> failures = ArchitectureFitnessRules.EvaluateExternalEventAndDialogPurity(Snapshot);
            Assert.IsEmpty(failures, string.Join(Environment.NewLine, failures));
        }

        [Test]
        public void Rule12_SingleSchemaExtensibleStorage_EnforcesPlatformSettingsOwnershipAndBiDirectionalRatchet()
        {
            IReadOnlyList<string> failures = ArchitectureFitnessRules.EvaluateSingleSchemaExtensibleStorage(Snapshot);
            Assert.IsEmpty(failures, string.Join(Environment.NewLine, failures));
        }
    }
}
