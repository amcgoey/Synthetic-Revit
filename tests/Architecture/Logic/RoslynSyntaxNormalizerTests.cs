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
    }
}
