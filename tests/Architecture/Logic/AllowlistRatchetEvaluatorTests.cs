using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace SyntheticTests.Architecture
{
    /// <summary>
    /// Unit tests verifying the bi-directional allowlist ratchet evaluator directly against
    /// synthetic pass, unlisted-violation (ceiling lock), and stale-allowlist (one-way ratchet) fixtures.
    /// </summary>
    [TestFixture]
    [UnitTest]
    public class AllowlistRatchetEvaluatorTests
    {
        [Test]
        public void Evaluate_PassesCleanAndExactAllowlistParityFixtures()
        {
            IReadOnlyList<string> emptyFailures = AllowlistRatchetEvaluator.Evaluate(
                FitnessRule.TransactionPlacement,
                Array.Empty<TypeLevelViolation>(),
                Array.Empty<AllowlistEntry>());

            Assert.IsEmpty(emptyFailures, string.Join(Environment.NewLine, emptyFailures));

            var observed = new[]
            {
                new TypeLevelViolation(
                    "Worksets",
                    FitnessRule.TransactionPlacement,
                    "ScopeBoxUtil",
                    "src/Features/Worksets/Addin/Utilities/ScopeBoxUtil.cs:42 uses raw new Transaction(...)"),
                new TypeLevelViolation(
                    "ViewManagement",
                    FitnessRule.TransactionFailureGuard,
                    "LegendsUtil",
                    "t2 unguarded"),
                new TypeLevelViolation(
                    "ViewManagement",
                    FitnessRule.TransactionFailureGuard,
                    "LegendsUtil",
                    "t3 unguarded")
            };

            var allowlist = new[]
            {
                new AllowlistEntry(
                    "Worksets",
                    FitnessRule.TransactionPlacement,
                    "ScopeBoxUtil",
                    AllowlistRegistryKind.TransitionalSpecBacklog),
                new AllowlistEntry(
                    "ViewManagement",
                    FitnessRule.TransactionFailureGuard,
                    "LegendsUtil",
                    AllowlistRegistryKind.TransitionalSpecBacklog),
                new AllowlistEntry(
                    "AutoTagger",
                    FitnessRule.SingleSchemaExtensibleStorage,
                    "TemplateStorageRepository",
                    AllowlistRegistryKind.DormantBoyScoutAllowlist)
            };

            IReadOnlyList<string> placementFailures = AllowlistRatchetEvaluator.Evaluate(
                FitnessRule.TransactionPlacement,
                observed.Where(v => v.Rule == FitnessRule.TransactionPlacement),
                allowlist);
            IReadOnlyList<string> guardFailures = AllowlistRatchetEvaluator.Evaluate(
                FitnessRule.TransactionFailureGuard,
                observed.Where(v => v.Rule == FitnessRule.TransactionFailureGuard),
                allowlist);

            Assert.IsEmpty(placementFailures, string.Join(Environment.NewLine, placementFailures));
            Assert.IsEmpty(guardFailures, string.Join(Environment.NewLine, guardFailures));
        }

        [Test]
        public void Evaluate_FailsOnUnlistedViolatingType_EnforcingCeilingLock()
        {
            var observed = new[]
            {
                new TypeLevelViolation(
                    "BatchPrint",
                    FitnessRule.TransactionPlacement,
                    "SneakyBatchHelper",
                    "src/Features/BatchPrint/Addin/Utilities/SneakyBatchHelper.cs:19 uses raw new Transaction(...)"),
                new TypeLevelViolation(
                    "Worksets",
                    FitnessRule.TransactionPlacement,
                    "ScopeBoxUtil",
                    "src/Features/Worksets/Addin/Utilities/ScopeBoxUtil.cs:42 uses raw new Transaction(...)")
            };

            var allowlist = new[]
            {
                new AllowlistEntry(
                    "Worksets",
                    FitnessRule.TransactionPlacement,
                    "ScopeBoxUtil",
                    AllowlistRegistryKind.TransitionalSpecBacklog)
            };

            IReadOnlyList<string> failures = AllowlistRatchetEvaluator.Evaluate(
                FitnessRule.TransactionPlacement,
                observed,
                allowlist);

            Assert.AreEqual(1, failures.Count, string.Join(Environment.NewLine, failures));
            Assert.IsTrue(failures[0].Contains("Unlisted violation"), failures[0]);
            Assert.IsTrue(failures[0].Contains("BatchPrint"), failures[0]);
            Assert.IsTrue(failures[0].Contains("SneakyBatchHelper"), failures[0]);
        }

        [Test]
        public void Evaluate_FailsOnStaleAllowlistEntry_EnforcingOneWayRatchet()
        {
            var observed = new[]
            {
                new TypeLevelViolation(
                    "Worksets",
                    FitnessRule.TransactionFailureGuard,
                    "ScopeBoxUtil",
                    "unguarded transaction")
            };

            var allowlist = new[]
            {
                new AllowlistEntry(
                    "Worksets",
                    FitnessRule.TransactionFailureGuard,
                    "ScopeBoxUtil",
                    AllowlistRegistryKind.TransitionalSpecBacklog),
                new AllowlistEntry(
                    "AutoTagger",
                    FitnessRule.TransactionFailureGuard,
                    "CmdBatchTag",
                    AllowlistRegistryKind.DormantBoyScoutAllowlist)
            };

            IReadOnlyList<string> failures = AllowlistRatchetEvaluator.Evaluate(
                FitnessRule.TransactionFailureGuard,
                observed,
                allowlist);

            Assert.AreEqual(1, failures.Count, string.Join(Environment.NewLine, failures));
            Assert.IsTrue(failures[0].Contains("Stale allowlist entry"), failures[0]);
            Assert.IsTrue(failures[0].Contains("AutoTagger"), failures[0]);
            Assert.IsTrue(failures[0].Contains("CmdBatchTag"), failures[0]);
            Assert.IsTrue(failures[0].Contains("DormantBoyScoutAllowlist"), failures[0]);
        }

        [Test]
        public void Evaluate_DetectsBothDirectionsSimultaneouslyAndRejectsWildcardsOrDuplicates()
        {
            Assert.Throws<ArgumentException>(() => new AllowlistEntry(
                "*",
                FitnessRule.InteractiveSliceSeamPurity,
                "AnyType",
                AllowlistRegistryKind.TransitionalSpecBacklog));

            Assert.Throws<ArgumentException>(() => new AllowlistEntry(
                "AutoTagger",
                FitnessRule.InteractiveSliceSeamPurity,
                "*ViewModel",
                AllowlistRegistryKind.DormantBoyScoutAllowlist));

            Assert.Throws<ArgumentException>(() => new AllowlistEntry(
                "ShadowStudy",
                FitnessRule.CorePurityAndZeroPreprocessor,
                "SomeType",
                AllowlistRegistryKind.TransitionalSpecBacklog));

            var observed = new[]
            {
                new TypeLevelViolation(
                    "SheetIndex",
                    FitnessRule.InteractiveSliceSeamPurity,
                    "UnlistedViewModel",
                    "forbidden Revit import")
            };

            var allowlist = new[]
            {
                new AllowlistEntry(
                    "MergeDuplicates",
                    FitnessRule.InteractiveSliceSeamPurity,
                    "MigratedViewModel",
                    AllowlistRegistryKind.TransitionalSpecBacklog),
                new AllowlistEntry(
                    "MergeDuplicates",
                    FitnessRule.InteractiveSliceSeamPurity,
                    "MigratedViewModel",
                    AllowlistRegistryKind.DormantBoyScoutAllowlist)
            };

            IReadOnlyList<string> failures = AllowlistRatchetEvaluator.Evaluate(
                FitnessRule.InteractiveSliceSeamPurity,
                observed,
                allowlist);

            Assert.IsTrue(failures.Any(f => f.Contains("Duplicate allowlist entry")));
            Assert.IsTrue(failures.Any(f => f.Contains("Unlisted violation") && f.Contains("UnlistedViewModel")));
            Assert.IsTrue(failures.Any(f => f.Contains("Stale allowlist entry") && f.Contains("MigratedViewModel")));
        }

        [Test]
        public void EvaluateRuleEndToEnd_VerifiesPassUnlistedViolationAndStaleAllowlistOnSyntheticSnapshot()
        {
            WorkspaceSourceFile cleanCommand = ArchitectureWorkspaceSnapshot.CreateSyntheticSourceFile(
                SourceArea.Src,
                "Features/BatchPrint/Addin/Commands/CmdClean.cs",
                @"
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
namespace Synthetic.Features.BatchPrint.Commands
{
    public class CmdClean : IExternalCommand
    {
        public void Run(Document doc)
        {
            using Transaction t = new(doc, ""Clean"");
        }
    }
}");
            WorkspaceSourceFile helperViolation = ArchitectureWorkspaceSnapshot.CreateSyntheticSourceFile(
                SourceArea.Src,
                "Features/Worksets/Addin/Utilities/HelperViolation.cs",
                @"
using Autodesk.Revit.DB;
namespace Synthetic.Features.Worksets.Utilities
{
    public static class HelperViolation
    {
        public static void Run(Document doc)
        {
            using var t = new Transaction(doc, ""Bad"");
        }
    }
}");

            var passSnapshot = ArchitectureWorkspaceSnapshot.CreateSyntheticForTest(2026, new[] { cleanCommand });
            IReadOnlyList<string> passFailures = ArchitectureFitnessRules.EvaluateTransactionPlacement(
                passSnapshot,
                Array.Empty<AllowlistEntry>());
            Assert.IsEmpty(passFailures, string.Join(Environment.NewLine, passFailures));

            var unlistedSnapshot = ArchitectureWorkspaceSnapshot.CreateSyntheticForTest(2026, new[] { cleanCommand, helperViolation });
            IReadOnlyList<string> unlistedFailures = ArchitectureFitnessRules.EvaluateTransactionPlacement(
                unlistedSnapshot,
                Array.Empty<AllowlistEntry>());
            Assert.AreEqual(1, unlistedFailures.Count);
            Assert.IsTrue(unlistedFailures[0].Contains("Unlisted violation") && unlistedFailures[0].Contains("HelperViolation"));

            var staleAllowlist = new[]
            {
                new AllowlistEntry(
                    "Worksets",
                    FitnessRule.TransactionPlacement,
                    "HelperViolation",
                    AllowlistRegistryKind.TransitionalSpecBacklog)
            };
            IReadOnlyList<string> exactParity = ArchitectureFitnessRules.EvaluateTransactionPlacement(unlistedSnapshot, staleAllowlist);
            Assert.IsEmpty(exactParity, string.Join(Environment.NewLine, exactParity));

            IReadOnlyList<string> staleFailures = ArchitectureFitnessRules.EvaluateTransactionPlacement(passSnapshot, staleAllowlist);
            Assert.AreEqual(1, staleFailures.Count);
            Assert.IsTrue(staleFailures[0].Contains("Stale allowlist entry") && staleFailures[0].Contains("HelperViolation"));
        }
    }
}
