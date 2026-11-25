using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Synthetic.Platform.Host;

namespace SyntheticTests.Architecture
{
    [TestFixture]
    [ArchitectureTest]
    public class RoslynSyntaxNormalizerTests
    {
        private static IReadOnlyList<WorkspaceXmlFile> CreateMinimalPureProjectFiles()
        {
            const string coreXml = "<Project><PropertyGroup><IsCoreProject>true</IsCoreProject></PropertyGroup></Project>";
            const string logicXml = "<Project><PropertyGroup><IsTestProject>true</IsTestProject></PropertyGroup></Project>";
            return new[]
            {
                new WorkspaceXmlFile(
                    SourceArea.Src,
                    "src/Synthetic.Core/Synthetic.Core.csproj",
                    "src/Synthetic.Core/Synthetic.Core.csproj",
                    "Synthetic.Core/Synthetic.Core.csproj",
                    ".csproj",
                    coreXml,
                    XDocument.Parse(coreXml)),
                new WorkspaceXmlFile(
                    SourceArea.Tests,
                    "tests/Projects/Synthetic.LogicTests/Synthetic.LogicTests.csproj",
                    "tests/Projects/Synthetic.LogicTests/Synthetic.LogicTests.csproj",
                    "Projects/Synthetic.LogicTests/Synthetic.LogicTests.csproj",
                    ".csproj",
                    logicXml,
                    XDocument.Parse(logicXml))
            };
        }

        [Test]
        public void ParseVariants_ReparsesConditionalIfBranches_EliminatingDisabledTriviaBlindSpots()
        {
            const string source = @"
namespace Synthetic.Features.SheetIndex.Core
{
    public class ConditionalSample
    {
        public void Run()
        {
#if REVIT2023
            var hidden = Autodesk.Revit.DB.ElementId.InvalidElementId;
#elif NETFRAMEWORK
            Synthetic.Platform.UI.ProgressCoordinator.Close();
#else
            int safe = 42;
#endif
        }
    }
}";

            WorkspaceSourceFile coreFile = ArchitectureWorkspaceSnapshot.CreateSyntheticSourceFile(
                SourceArea.Src,
                "Features/SheetIndex/Core/ConditionalSample.cs",
                source,
                currentLatestVersion: 2026);

            Assert.GreaterOrEqual(coreFile.Variants.Count, 3, "Expected primary + REVIT2023 + NETFRAMEWORK variants.");

            var snapshot = ArchitectureWorkspaceSnapshot.CreateSyntheticForTest(
                2026,
                new[] { coreFile },
                projectAndBuildFiles: CreateMinimalPureProjectFiles());
            IReadOnlyList<string> violations = ArchitectureFitnessRules.EvaluateCorePurityAndZeroPreprocessor(
                snapshot,
                typeof(RibbonItemFilter).Assembly,
                typeof(RoslynSyntaxNormalizerTests).Assembly);

            Assert.IsTrue(
                violations.Any(v => v.Contains("Autodesk.Revit")),
                "Expected #if REVIT2023 branch Autodesk.Revit reference to be caught.");
            Assert.IsTrue(
                violations.Any(v => v.Contains("ProgressCoordinator")),
                "Expected #elif NETFRAMEWORK branch ProgressCoordinator reference to be caught.");
            Assert.IsTrue(
                violations.Any(v => v.Contains("REVIT2023")),
                "Expected #if REVIT2023 directive inside Core/ to be caught.");
        }

        [Test]
        public void Normalizer_ExpandsUsingAliases_StripsGlobalPrefix_AndResolvesCSharp9TargetTypedNew()
        {
            const string source = @"
using RevitDB = Autodesk.Revit.DB;
using TaskDialogAlias = global::Autodesk.Revit.UI.TaskDialog;
using HostApp = global::Synthetic.Platform.Host.App;

namespace Synthetic.Features.Sample.Addin
{
    public class NormalizationSample
    {
        private readonly RevitDB.Transaction _fieldTx = new(null, ""field"");

        public RevitDB.Transaction CreateTx()
        {
            global::Autodesk.Revit.DB.Transaction localTx = new(null, ""local"");
            RevitDB.Transaction assignedTx;
            assignedTx = new(null, ""assigned"");
            TaskDialogAlias.Show(""Title"", ""Message"");
            var hostRef = HostApp.AppControlled;
            var globalHostRef = global::Synthetic.Platform.Host.App.AppControlled;
            return new(null, ""return"");
        }

        public RevitDB.TransactionGroup CreateGroup() => new(null, ""arrow"");
    }
}";

            ParsedSyntaxVariant variant = RoslynSyntaxNormalizer.ParseSingle(source, "NormalizationSample.cs");

            List<string> createdTypes = variant.ObjectCreations
                .Select(c => c.NormalizedTypeName)
                .ToList();

            Assert.AreEqual(5, createdTypes.Count, "Expected field, local, assigned, return, and arrow object creations.");
            foreach (string created in createdTypes)
            {
                Assert.IsTrue(
                    created == "Autodesk.Revit.DB.Transaction" || created == "Autodesk.Revit.DB.TransactionGroup",
                    $"Unexpected normalized creation type '{created}'.");
            }

            Assert.IsTrue(
                variant.ObjectCreations.Any(c => c.IsImplicit && c.NormalizedTypeName == "Autodesk.Revit.DB.TransactionGroup"),
                "Expected arrow-body implicit new() to resolve to Autodesk.Revit.DB.TransactionGroup.");
            Assert.AreEqual(
                4,
                variant.ObjectCreations.Count(c => c.IsImplicit && c.NormalizedTypeName == "Autodesk.Revit.DB.Transaction"),
                "Expected field, local, assigned, and return implicit new() sites to normalize to Autodesk.Revit.DB.Transaction.");

            Assert.IsTrue(
                variant.Invocations.Any(i => i.NormalizedTarget == "Autodesk.Revit.UI.TaskDialog.Show"),
                "Expected alias + global:: invocation TaskDialogAlias.Show to normalize to Autodesk.Revit.UI.TaskDialog.Show.");

            Assert.AreEqual(
                2,
                variant.MemberAccesses.Count(m => m.NormalizedFullName == "Synthetic.Platform.Host.App.AppControlled"),
                "Expected both aliased and global:: App.AppControlled references to normalize.");
        }

        [Test]
        public void Rule02_LatestVersionPreprocessorCheck_InspectsDirectiveTriviaAndIgnoresStringLiterals()
        {
            const string safeFileWithStrings = @"
namespace SyntheticTests.Architecture
{
    public class StringSample
    {
        // #if REVIT2026
        private const string Marker = ""#if REVIT2026"";
    }
}";
            const string violatingFile = @"
namespace Synthetic.Features.Sample.Addin
{
    public class BadLatestIfdef
    {
        public void Run()
        {
#if REVIT2025
            int a = 1;
#elif REVIT2026
            int b = 2;
#else
            int c = 3;
#endif
        }
    }
}";

            WorkspaceSourceFile cleanFile = ArchitectureWorkspaceSnapshot.CreateSyntheticSourceFile(
                SourceArea.Tests,
                "Architecture/Logic/StringSample.cs",
                safeFileWithStrings,
                currentLatestVersion: 2026);
            WorkspaceSourceFile badFile = ArchitectureWorkspaceSnapshot.CreateSyntheticSourceFile(
                SourceArea.Src,
                "Features/Sample/Addin/BadLatestIfdef.cs",
                violatingFile,
                currentLatestVersion: 2026);

            var cleanSnapshot = ArchitectureWorkspaceSnapshot.CreateSyntheticForTest(
                2026,
                new[] { cleanFile },
                projectAndBuildFiles: CreateMinimalPureProjectFiles());
            IReadOnlyList<string> cleanViolations = ArchitectureFitnessRules.EvaluateCorePurityAndZeroPreprocessor(
                cleanSnapshot,
                typeof(RibbonItemFilter).Assembly,
                typeof(RoslynSyntaxNormalizerTests).Assembly);
            Assert.IsEmpty(cleanViolations, "String literals and comments mentioning REVIT2026 must never trigger false positives.");

            var badSnapshot = ArchitectureWorkspaceSnapshot.CreateSyntheticForTest(
                2026,
                new[] { badFile },
                projectAndBuildFiles: CreateMinimalPureProjectFiles());
            IReadOnlyList<string> badViolations = ArchitectureFitnessRules.EvaluateCorePurityAndZeroPreprocessor(
                badSnapshot,
                typeof(RibbonItemFilter).Assembly,
                typeof(RoslynSyntaxNormalizerTests).Assembly);
            Assert.IsTrue(
                badViolations.Any(v => v.Contains("#elif REVIT2026")),
                "Expected #elif REVIT2026 directive trivia to fail CorePurityAndZeroPreprocessor.");
        }

        [Test]
        public void Rule04_DomainGraduationThreshold_EnforcesSubModuleCountSliceConsumersAndSiblingIsolation()
        {
            WorkspaceSourceFile domainAlpha = ArchitectureWorkspaceSnapshot.CreateSyntheticSourceFile(
                SourceArea.Src,
                "Domain/Alpha/Core/AlphaService.cs",
                "namespace Synthetic.Domain.Alpha { public class AlphaService {} }");
            WorkspaceSourceFile domainBetaSiblingImport = ArchitectureWorkspaceSnapshot.CreateSyntheticSourceFile(
                SourceArea.Src,
                "Domain/Beta/Core/BetaService.cs",
                "using Synthetic.Domain.Alpha; namespace Synthetic.Domain.Beta { public class BetaService {} }");
            WorkspaceSourceFile slice1 = ArchitectureWorkspaceSnapshot.CreateSyntheticSourceFile(
                SourceArea.Src,
                "Features/SheetIndex/Core/UseAlpha.cs",
                "using Synthetic.Domain.Alpha; namespace Synthetic.Features.SheetIndex { public class UseAlpha {} }");
            WorkspaceSourceFile slice2 = ArchitectureWorkspaceSnapshot.CreateSyntheticSourceFile(
                SourceArea.Src,
                "Features/ShadowStudy/Core/UseBoth.cs",
                "using Synthetic.Domain.Alpha; using Synthetic.Domain.Beta; namespace Synthetic.Features.ShadowStudy { public class UseBoth {} }");

            var failingSnapshot = ArchitectureWorkspaceSnapshot.CreateSyntheticForTest(
                2026,
                new[] { domainAlpha, domainBetaSiblingImport, slice1, slice2 });
            IReadOnlyList<string> failures = ArchitectureFitnessRules.EvaluateDomainGraduationThreshold(failingSnapshot);

            Assert.IsTrue(
                failures.Any(f => f.Contains("sibling Domain sub-module Synthetic.Domain.Alpha")),
                "Expected sibling Domain import violation.");
            Assert.IsTrue(
                failures.Any(f => f.Contains("'Beta'") && f.Contains("requires >= 2 distinct slices")),
                "Expected Beta sub-module with only 1 consuming slice to fail graduation threshold.");

            WorkspaceSourceFile domainBetaClean = ArchitectureWorkspaceSnapshot.CreateSyntheticSourceFile(
                SourceArea.Src,
                "Domain/Beta/Core/BetaService.cs",
                "namespace Synthetic.Domain.Beta { public class BetaService {} }");
            WorkspaceSourceFile slice1UsesBetaToo = ArchitectureWorkspaceSnapshot.CreateSyntheticSourceFile(
                SourceArea.Src,
                "Features/SheetIndex/Core/UseAlpha.cs",
                "using Synthetic.Domain.Alpha; using Synthetic.Domain.Beta; namespace Synthetic.Features.SheetIndex { public class UseAlpha {} }");

            var passingSnapshot = ArchitectureWorkspaceSnapshot.CreateSyntheticForTest(
                2026,
                new[] { domainAlpha, domainBetaClean, slice1UsesBetaToo, slice2 });
            IReadOnlyList<string> passingViolations = ArchitectureFitnessRules.EvaluateDomainGraduationThreshold(passingSnapshot);
            Assert.IsEmpty(passingViolations, string.Join(Environment.NewLine, passingViolations));
        }

        [Test]
        public void Rule07_TestCategoryCheck_SupportsClassInheritedOrMethodLevelCanonicalAttributesOnly()
        {
            WorkspaceSourceFile baseFixture = ArchitectureWorkspaceSnapshot.CreateSyntheticSourceFile(
                SourceArea.Tests,
                "Fixtures/Revit/BaseFixture.cs",
                @"
namespace SyntheticTests.Fixtures
{
    [RevitAdapterTest]
    public abstract class BaseFixture { }
}");
            WorkspaceSourceFile inheritedTest = ArchitectureWorkspaceSnapshot.CreateSyntheticSourceFile(
                SourceArea.Tests,
                "Features/Sample/Revit/InheritedTests.cs",
                @"
using NUnit.Framework;
using SyntheticTests.Fixtures;
namespace SyntheticTests.Features.Sample
{
    [TestFixture]
    public class InheritedTests : BaseFixture
    {
        [Test] public void Ok() { }
    }
}");
            WorkspaceSourceFile perMethodTest = ArchitectureWorkspaceSnapshot.CreateSyntheticSourceFile(
                SourceArea.Tests,
                "Platform/Host/Revit/PerMethodTests.cs",
                @"
using NUnit.Framework;
namespace SyntheticTests.Platform.Host
{
    [TestFixture]
    public class PerMethodTests
    {
        [Test, RevitSmokeTest] public void Smoke() { }
        [Test, RevitIsolatedDocTest] public void Iso() { }
    }
}");
            WorkspaceSourceFile rawCategoryViolator = ArchitectureWorkspaceSnapshot.CreateSyntheticSourceFile(
                SourceArea.Tests,
                "Features/Sample/Logic/RawCategoryTests.cs",
                @"
using NUnit.Framework;
namespace SyntheticTests.Features.Sample
{
    [TestFixture]
    [Category(""Unit"")]
    public class RawCategoryTests
    {
        [Test] public void NotCanonical() { }
    }
}");
            WorkspaceSourceFile partialMethodViolator = ArchitectureWorkspaceSnapshot.CreateSyntheticSourceFile(
                SourceArea.Tests,
                "Features/Sample/Revit/PartialMethodTests.cs",
                @"
using NUnit.Framework;
namespace SyntheticTests.Features.Sample
{
    [TestFixture]
    public class PartialMethodTests
    {
        [Test, RevitSmokeTest] public void First() { }
        [Test] public void MissingCategoryOnSecond() { }
    }
}");

            var snapshot = ArchitectureWorkspaceSnapshot.CreateSyntheticForTest(
                2026,
                new[] { baseFixture, inheritedTest, perMethodTest, rawCategoryViolator, partialMethodViolator });
            IReadOnlyList<string> violations = ArchitectureFitnessRules.EvaluateTestCategoryAndLegacyMockRatchet(snapshot);

            Assert.IsFalse(violations.Any(v => v.Contains("InheritedTests")), "Inherited canonical category must pass.");
            Assert.IsFalse(violations.Any(v => v.Contains("PerMethodTests")), "100%-decorated test methods must pass.");
            Assert.IsTrue(violations.Any(v => v.Contains("RawCategoryTests")), "Raw [Category(\"Unit\")] must fail.");
            Assert.IsTrue(violations.Any(v => v.Contains("PartialMethodTests")), "Partially decorated methods without class category must fail.");
        }

        [Test]
        public void Rule05_RibbonConfigIntegrity_DetectsMissingCommandClassAndMissingPngAsset()
        {
            WorkspaceSourceFile validCommand = ArchitectureWorkspaceSnapshot.CreateSyntheticSourceFile(
                SourceArea.Src,
                "Features/Sample/Addin/Commands/CmdOk.cs",
                @"
using Autodesk.Revit.UI;
namespace Synthetic.Features.Sample.Commands
{
    public class CmdOk : IExternalCommand { }
}");
            WorkspaceSourceFile nonCommandClass = ArchitectureWorkspaceSnapshot.CreateSyntheticSourceFile(
                SourceArea.Src,
                "Features/Sample/Addin/Commands/NotACommand.cs",
                @"
namespace Synthetic.Features.Sample.Commands
{
    public class NotACommand { }
}");

            var ribbon = new RibbonConfigSnapshot(
                "ribbon_config.json",
                "{}",
                new JObject(),
                new[]
                {
                    new RibbonEntryRecord("PushButton", "Ok", "Synthetic.Features.Sample.Commands.CmdOk", null, "ok_16.png", "ok_32.png"),
                    new RibbonEntryRecord("PushButton", "Bad", "Synthetic.Features.Sample.Commands.NotACommand", null, "missing_16.png", "ok_32.png")
                },
                new[] { "ok_16.png", "ok_32.png", "placeholder_16.png", "placeholder_32.png" });

            var snapshot = ArchitectureWorkspaceSnapshot.CreateSyntheticForTest(
                2026,
                new[] { validCommand, nonCommandClass },
                ribbonConfig: ribbon);

            IReadOnlyList<string> violations = ArchitectureFitnessRules.EvaluateRibbonConfigIntegrity(snapshot);

            Assert.IsFalse(violations.Any(v => v.Contains("CmdOk")), "Valid concrete IExternalCommand must resolve.");
            Assert.IsTrue(violations.Any(v => v.Contains("NotACommand")), "Class not implementing IExternalCommand must fail.");
            Assert.IsTrue(violations.Any(v => v.Contains("missing_16.png")), "Missing PNG icon asset must fail.");
        }

        [Test]
        public void Rule06_SettingsRegistryTemplateParity_ResolvesCrossFileConstantsAndDetectsDrift()
        {
            WorkspaceSourceFile keysFile = ArchitectureWorkspaceSnapshot.CreateSyntheticSourceFile(
                SourceArea.Src,
                "Platform/Settings/Core/SettingsModuleKeys.cs",
                @"
namespace Synthetic.Platform.Settings
{
    public static class SettingsModuleKeys
    {
        public const string Sync = ""SyncSettings"";
    }
}");
            WorkspaceSourceFile syncSettingsFile = ArchitectureWorkspaceSnapshot.CreateSyntheticSourceFile(
                SourceArea.Src,
                "Platform/Settings/Addin/SyncSettings.cs",
                @"
namespace Synthetic.Platform.Settings
{
    public class SyncSettings : ISettingModule
    {
        public const string Name = SettingsModuleKeys.Sync;
        public string ModuleKey => Name;
    }
}");
            WorkspaceSourceFile sampleSettingsFile = ArchitectureWorkspaceSnapshot.CreateSyntheticSourceFile(
                SourceArea.Src,
                "Features/Sample/Core/SampleSettings.cs",
                @"
using Synthetic.Platform.Settings;
namespace Synthetic.Features.Sample.Settings
{
    public class SampleSettings : ISettingModule
    {
        public const string Name = ""Sample"";
        public string ModuleKey => Name;
    }
}");
            WorkspaceSourceFile vmFile = ArchitectureWorkspaceSnapshot.CreateSyntheticSourceFile(
                SourceArea.Src,
                "Features/Sample/Core/SampleSettingsViewModel.cs",
                @"
namespace Synthetic.Features.Sample.ViewModels
{
    public class SampleSettingsViewModel : ISettingModuleViewModel
    {
        public string ModuleKey => ""SampleVM"";
    }
}");
            WorkspaceSourceFile registrarFile = ArchitectureWorkspaceSnapshot.CreateSyntheticSourceFile(
                SourceArea.Src,
                "Platform/Host/Addin/HostSettingsRegistrar.cs",
                @"
using Synthetic.Features.Sample.Settings;
using Synthetic.Platform.Settings;
namespace Synthetic.Platform.Host
{
    public static class HostSettingsRegistrar
    {
        public static void EnsureRegistered()
        {
            SettingsModuleRegistry.Register(SampleSettings.Name, () => new SampleSettings());
        }
    }
}");

            const string matchingTemplateJson = @"{ ""Settings"": { ""Sample"": {} } }";
            var matchingSnapshot = ArchitectureWorkspaceSnapshot.CreateSyntheticForTest(
                2026,
                new[] { keysFile, syncSettingsFile, sampleSettingsFile, vmFile, registrarFile },
                settingsTemplate: new SettingsTemplateSnapshot(
                    "SyntheticSettings.template.json",
                    matchingTemplateJson,
                    JObject.Parse(matchingTemplateJson),
                    new[] { "Sample" }));

            IReadOnlyList<string> matchingViolations = ArchitectureFitnessRules.EvaluateSettingsRegistryTemplateParity(matchingSnapshot);
            Assert.IsEmpty(matchingViolations, string.Join(Environment.NewLine, matchingViolations));

            const string driftedTemplateJson = @"{ ""Settings"": { ""SyncSettings"": {} } }";
            var driftedSnapshot = ArchitectureWorkspaceSnapshot.CreateSyntheticForTest(
                2026,
                new[] { keysFile, syncSettingsFile, sampleSettingsFile, vmFile, registrarFile },
                settingsTemplate: new SettingsTemplateSnapshot(
                    "SyntheticSettings.template.json",
                    driftedTemplateJson,
                    JObject.Parse(driftedTemplateJson),
                    new[] { "SyncSettings" }));

            IReadOnlyList<string> driftedViolations = ArchitectureFitnessRules.EvaluateSettingsRegistryTemplateParity(driftedSnapshot);
            Assert.IsTrue(driftedViolations.Any(v => v.Contains("Sample")), "Missing template key 'Sample' must fail.");
            Assert.IsTrue(driftedViolations.Any(v => v.Contains("SyncSettings")), "Template key 'SyncSettings' must be rejected.");
        }

        [Test]
        public void Rules08To12_HandleAliasesGlobalTargetTypedNewIfBranchesAndExemptions()
        {
            WorkspaceSourceFile cleanAdapter = ArchitectureWorkspaceSnapshot.CreateSyntheticSourceFile(
                SourceArea.Src,
                "Features/SheetIndex/Addin/Adapters/RevitSampleAdapter.cs",
                @"
using RevitDB = global::Autodesk.Revit.DB;
using RevitUI = global::Autodesk.Revit.UI;
namespace Synthetic.Features.SheetIndex.Adapters
{
    public interface ISampleAdapter { }
    public class RevitSampleAdapter : ISampleAdapter
    {
        public void Run(RevitDB.Document doc, object args)
        {
            if (args is RevitUI.Events.TaskDialogShowingEventArgs taskArgs)
            {
                _ = taskArgs.DialogId;
            }
            using (RevitDB.Transaction tx = new(doc, ""Guarded""))
            {
                var opts = tx.GetFailureHandlingOptions();
                opts.SetFailuresPreprocessor(FailurePipelines.ObserveOnly());
                tx.SetFailureHandlingOptions(opts);
            }
        }
    }
}");

            WorkspaceSourceFile platformRevitHelper = ArchitectureWorkspaceSnapshot.CreateSyntheticSourceFile(
                SourceArea.Src,
                "Platform/Revit/Addin/DocumentTransactionExtensions.cs",
                @"
using Autodesk.Revit.DB;
namespace Synthetic.Platform.Revit
{
    public static class DocumentTransactionExtensions
    {
        public static void Batch(Document doc)
        {
            using (TransactionGroup tg = new(doc, ""Batch"")) { }
        }
        public static void DeleteRaw(FailuresAccessor accessor) => accessor.DeleteAllWarnings();
    }
}");

            WorkspaceSourceFile platformUiAdapter = ArchitectureWorkspaceSnapshot.CreateSyntheticSourceFile(
                SourceArea.Src,
                "Platform/UI/Addin/WindowsPromptAdapter.cs",
                @"
namespace Synthetic.Platform.UI
{
    public class WindowsPromptAdapter
    {
        public void Show() => System.Windows.MessageBox.Show(""ok"");
    }
}");

            WorkspaceSourceFile summaryDisplayService = ArchitectureWorkspaceSnapshot.CreateSyntheticSourceFile(
                SourceArea.Src,
                "Features/StandardsManagement/Addin/WindowsSummaryDisplayService.cs",
                @"
namespace Synthetic.Features.StandardsManagement
{
    public class WindowsSummaryDisplayService
    {
        public void ShowSummary(System.Windows.Window w) => w.ShowDialog();
    }
}");

            WorkspaceSourceFile platformSettingsSchema = ArchitectureWorkspaceSnapshot.CreateSyntheticSourceFile(
                SourceArea.Src,
                "Platform/Settings/Addin/SettingsSchema.cs",
                @"
using Ext = global::Autodesk.Revit.DB.ExtensibleStorage;
namespace Synthetic.Platform.Settings
{
    public static class SettingsSchema
    {
        public static void Build(System.Guid id)
        {
            Ext.SchemaBuilder sb = new(id);
        }
    }
}");

            WorkspaceSourceFile directSliceCommandWithTxGroup = ArchitectureWorkspaceSnapshot.CreateSyntheticSourceFile(
                SourceArea.Src,
                "Features/Worksets/Addin/Commands/CmdBadTxGroup.cs",
                @"
using RevitDB = Autodesk.Revit.DB;
using Autodesk.Revit.UI;
namespace Synthetic.Features.Worksets.Commands
{
    public class CmdBadTxGroup : IExternalCommand
    {
        private static ExternalEvent? _staticEvent;
        public void Run(RevitDB.Document doc)
        {
            RevitDB.TransactionGroup tg = new(doc, ""BadGroup"");
            using (RevitDB.Transaction t1 = new(doc, ""Guarded""))
            {
                var opts = t1.GetFailureHandlingOptions();
                opts.SetFailuresPreprocessor(FailurePipelines.ObserveOnly());
            }
            using (RevitDB.Transaction t2 = new(doc, ""Unguarded""))
            {
                t2.Start();
            }
            using (RevitDB.Transaction t3 = new(doc, ""NullPreprocessor""))
            {
                var opts3 = t3.GetFailureHandlingOptions();
                opts3.SetFailuresPreprocessor(null);
            }
        }
    }
}");

            WorkspaceSourceFile sequentialTxCommand = ArchitectureWorkspaceSnapshot.CreateSyntheticSourceFile(
                SourceArea.Src,
                "Features/MultiModelPdfExport/Addin/Commands/CmdSequentialTx.cs",
                @"
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
namespace Synthetic.Features.MultiModelPdfExport.Commands
{
    public class CmdSequentialTx : IExternalCommand
    {
        public void Run(Document doc)
        {
            using Transaction t1 = new(doc, ""FirstUnguarded"");
            t1.Start();
            t1.Commit();

            using Transaction t2 = new(doc, ""SecondGuarded"");
            var opts = t2.GetFailureHandlingOptions();
            opts.SetFailuresPreprocessor(FailurePipelines.ObserveOnly());
            t2.SetFailureHandlingOptions(opts);
            t2.Start();
            t2.Commit();
        }
    }
}");

            WorkspaceSourceFile standardSerializationStyle = ArchitectureWorkspaceSnapshot.CreateSyntheticSourceFile(
                SourceArea.Src,
                "RevitDOM/Addin/Operations/StandardSerializationEngine.cs",
                @"
using Autodesk.Revit.DB;
using Synthetic.Platform.Revit;
namespace Synthetic.RevitDOM.Operations
{
    public class StandardSerializationEngine
    {
        public void Write(Document doc)
        {
            doc.ExecuteCommittedBatchGroup(""Import"", () =>
            {
                doc.ExecuteTransaction(""Inner"", FailurePipelines.ObserveOnly(), () => { });
            });
        }
    }
}");

            WorkspaceSourceFile leakyVmInIfBranch = ArchitectureWorkspaceSnapshot.CreateSyntheticSourceFile(
                SourceArea.Src,
                "Features/AutoTagger/Addin/ViewModels/LeakyViewModel.cs",
                @"
using TD = global::Autodesk.Revit.UI.TaskDialog;
namespace Synthetic.Features.AutoTagger.ViewModels
{
    public class LeakyViewModel
    {
        public void Trigger(Autodesk.Revit.UI.IExternalEventHandler handler)
        {
#if REVIT2023
            var ev = global::Autodesk.Revit.UI.ExternalEvent.Create(handler);
            TD.Show(""Title"", ""Msg"");
            TD dialog = new(""Inline"");
#endif
        }
    }
}",
                currentLatestVersion: 2026);

            WorkspaceSourceFile leakySchemaAndDeleteWarnings = ArchitectureWorkspaceSnapshot.CreateSyntheticSourceFile(
                SourceArea.Src,
                "Features/AutoTagger/Addin/Repositories/LeakyRepo.cs",
                @"
using Ext = Autodesk.Revit.DB.ExtensibleStorage;
namespace Synthetic.Features.AutoTagger.Repositories
{
    public class LeakyRepo
    {
        public void Save(System.Guid id, Autodesk.Revit.DB.FailuresAccessor acc)
        {
            Ext.SchemaBuilder sb = new(id);
            acc.DeleteAllWarnings();
        }
    }
}");

            var snapshot = ArchitectureWorkspaceSnapshot.CreateSyntheticForTest(
                2026,
                new[]
                {
                    cleanAdapter,
                    platformRevitHelper,
                    platformUiAdapter,
                    summaryDisplayService,
                    platformSettingsSchema,
                    directSliceCommandWithTxGroup,
                    sequentialTxCommand,
                    standardSerializationStyle,
                    leakyVmInIfBranch,
                    leakySchemaAndDeleteWarnings
                });

            IReadOnlyList<TypeLevelViolation> rule8 = ArchitectureFitnessRules.CollectInteractiveSliceSeamPurityViolations(snapshot);
            Assert.IsTrue(rule8.Any(v => v.Slice == "AutoTagger" && v.TypeName == "LeakyViewModel"));
            Assert.IsFalse(rule8.Any(v => v.Slice == "SheetIndex"));

            IReadOnlyList<TypeLevelViolation> rule9 = ArchitectureFitnessRules.CollectTransactionPlacementViolations(snapshot);
            Assert.IsTrue(rule9.Any(v => v.Slice == "Worksets" && v.TypeName == "CmdBadTxGroup"));
            Assert.IsFalse(rule9.Any(v => v.TypeName == "RevitSampleAdapter"));
            Assert.IsFalse(rule9.Any(v => v.TypeName == "DocumentTransactionExtensions"));
            Assert.IsFalse(rule9.Any(v => v.TypeName == "StandardSerializationEngine"));
            Assert.IsFalse(rule9.Any(v => v.TypeName == "CmdSequentialTx"));

            IReadOnlyList<TypeLevelViolation> rule10 = ArchitectureFitnessRules.CollectTransactionFailureGuardViolations(snapshot);
            Assert.AreEqual(2, rule10.Count(v => v.TypeName == "CmdBadTxGroup"), "Unguarded t2 and null-preprocessor t3 (not guarded t1) should fail.");
            Assert.IsTrue(rule10.Any(v => v.TypeName == "CmdBadTxGroup" && v.Detail.Contains(":17 ")), "Expected unguarded t2 at line 17 to fail.");
            Assert.IsTrue(rule10.Any(v => v.TypeName == "CmdBadTxGroup" && v.Detail.Contains(":21 ")), "Expected null-preprocessor t3 at line 21 to fail.");
            Assert.AreEqual(1, rule10.Count(v => v.TypeName == "CmdSequentialTx"), "Only sequential t1 (not guarded t2) should fail.");
            Assert.IsTrue(rule10.Any(v => v.TypeName == "CmdSequentialTx" && v.Detail.Contains(":10 ")), "Expected sequential t1 at line 10 to fail.");
            Assert.IsFalse(rule10.Any(v => v.TypeName == "RevitSampleAdapter"));
            Assert.IsFalse(rule10.Any(v => v.TypeName == "StandardSerializationEngine"));

            IReadOnlyList<TypeLevelViolation> rule11 = ArchitectureFitnessRules.CollectExternalEventAndDialogPurityViolations(snapshot);
            Assert.IsTrue(rule11.Any(v => v.TypeName == "CmdBadTxGroup" && v.Detail.Contains("static ExternalEvent")));
            Assert.IsTrue(rule11.Any(v => v.TypeName == "LeakyViewModel" && v.Detail.Contains("ExternalEvent.Create")));
            Assert.IsTrue(rule11.Any(v => v.TypeName == "LeakyViewModel" && v.Detail.Contains("TaskDialog.Show")));
            Assert.IsTrue(rule11.Any(v => v.TypeName == "LeakyViewModel" && v.Detail.Contains("instantiates TaskDialog")));
            Assert.IsTrue(rule11.Any(v => v.TypeName == "LeakyRepo" && v.Detail.Contains("DeleteAllWarnings")));
            Assert.IsFalse(rule11.Any(v => v.TypeName == "RevitSampleAdapter"), "TaskDialogShowingEventArgs interception must be ignored.");
            Assert.IsFalse(rule11.Any(v => v.TypeName == "WindowsPromptAdapter"), "Platform/UI/Addin must be exempt.");
            Assert.IsFalse(rule11.Any(v => v.TypeName == "WindowsSummaryDisplayService"), "WindowsSummaryDisplayService must be exempt.");
            Assert.IsFalse(rule11.Any(v => v.TypeName == "DocumentTransactionExtensions"));

            IReadOnlyList<TypeLevelViolation> rule12 = ArchitectureFitnessRules.CollectSingleSchemaExtensibleStorageViolations(snapshot);
            Assert.AreEqual(1, rule12.Count);
            Assert.AreEqual("LeakyRepo", rule12[0].TypeName);
        }

        [Test]
        public void Rule10_TransactionFailureGuard_FlagsUnguardedNonUsingTransactionInsideOuterGuardedUsingBlock()
        {
            WorkspaceSourceFile nestedNonUsingInGuardedOuter = ArchitectureWorkspaceSnapshot.CreateSyntheticSourceFile(
                SourceArea.Src,
                "Features/Worksets/Addin/Commands/CmdNestedTransactions.cs",
                @"
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
namespace Synthetic.Features.Worksets.Commands
{
    public class CmdNestedTransactions : IExternalCommand
    {
        public void Run(Document doc, bool flag)
        {
            using (Transaction outerGuarded = new Transaction(doc, ""OuterGuarded""))
            {
                var outerOpts = outerGuarded.GetFailureHandlingOptions();
                outerOpts.SetFailuresPreprocessor(FailurePipelines.ObserveOnly());
                outerGuarded.Start();

                Transaction innerUnguarded = new Transaction(doc, ""InnerUnguarded"");
                innerUnguarded.Start();
                innerUnguarded.Commit();
            }

            using (Transaction outerUnguarded = new Transaction(doc, ""OuterUnguarded""))
            {
                if (flag)
                {
                    Transaction innerGuarded = new Transaction(doc, ""InnerGuarded"");
                    var innerOpts = innerGuarded.GetFailureHandlingOptions();
                    innerOpts.SetFailuresPreprocessor(FailurePipelines.ObserveOnly());
                }

                Transaction flatInnerGuarded = new Transaction(doc, ""FlatInnerGuarded"");
                var flatOpts = flatInnerGuarded.GetFailureHandlingOptions();
                flatOpts.SetFailuresPreprocessor(FailurePipelines.ObserveOnly());
            }

            using (Transaction outerWithNestedUsing = new Transaction(doc, ""OuterWithNestedUsing""))
            {
                using (Transaction nestedUsingGuarded = new Transaction(doc, ""NestedUsingGuarded""))
                {
                    var nestedOpts = nestedUsingGuarded.GetFailureHandlingOptions();
                    nestedOpts.SetFailuresPreprocessor(FailurePipelines.ObserveOnly());
                }

                var outerAfterOpts = outerWithNestedUsing.GetFailureHandlingOptions();
                outerAfterOpts.SetFailuresPreprocessor(FailurePipelines.ObserveOnly());
            }
        }
    }
}");

            var snapshot = ArchitectureWorkspaceSnapshot.CreateSyntheticForTest(
                2026,
                new[] { nestedNonUsingInGuardedOuter });

            IReadOnlyList<TypeLevelViolation> violations = ArchitectureFitnessRules.CollectTransactionFailureGuardViolations(snapshot);

            Assert.AreEqual(2, violations.Count, "Expected exactly InnerUnguarded (line 16) and OuterUnguarded (line 21) to fail.");
            Assert.IsTrue(
                violations.Any(v => v.Detail.Contains(":16 ")),
                $"Expected unguarded non-using inner Transaction at line 16 to be flagged. Actual: {string.Join("; ", violations.Select(v => v.Detail))}");
            Assert.IsTrue(
                violations.Any(v => v.Detail.Contains(":21 ")),
                $"Expected outer unguarded Transaction at line 21 not to borrow inner guard. Actual: {string.Join("; ", violations.Select(v => v.Detail))}");
        }
    }
}
