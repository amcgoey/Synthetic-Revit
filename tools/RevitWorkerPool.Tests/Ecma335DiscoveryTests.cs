using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using NUnit.Framework;
using RevitWorkerPool.Discovery;
using RevitWorkerPool.Execution;
using RevitWorkerPool.Pipe;
using RevitWorkerPool.Reporting;
using ricaun.NUnit;
using ricaun.NUnit.Models;

namespace RevitWorkerPool.Tests;

[TestFixture]
public class Ecma335DiscoveryTests
{
    private string _tempRoot = string.Empty;
    private static readonly Lazy<FixtureAssemblyArtifacts> SharedArtifacts = new(BuildSharedAssemblies);

    [SetUp]
    public void SetUp()
    {
        _tempRoot = Path.Combine(
            Path.GetTempPath(),
            "RevitWorkerPool_Ecma335Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        if (!SharedArtifacts.IsValueCreated)
        {
            return;
        }

        try
        {
            if (Directory.Exists(SharedArtifacts.Value.RootDir))
            {
                Directory.Delete(SharedArtifacts.Value.RootDir, recursive: true);
            }
        }
        catch (IOException)
        {
            // Ignore best-effort cleanup errors.
        }
        catch (UnauthorizedAccessException)
        {
            // ricaun.NUnit TestEngine loads RicaunFilterCheck.dll into the default load context for process lifetime.
        }
    }

    [Test]
    public void DiscoverAndFilter_ReadsNet48Net80AndSyntheticNet100_WithoutLoadingAssemblies()
    {
        FixtureAssemblyArtifacts artifacts = SharedArtifacts.Value;

        TestDiscoveryResult net48 = Ecma335TestDiscovery.DiscoverAndFilter(artifacts.Net48DllPath);
        TestDiscoveryResult net80 = Ecma335TestDiscovery.DiscoverAndFilter(artifacts.Net80DllPath);
        TestDiscoveryResult net100 = Ecma335TestDiscovery.DiscoverAndFilter(artifacts.Net100DllPath);

        Assert.That(net48.TargetFramework, Is.EqualTo(".NETFramework,Version=v4.8"));
        Assert.That(net80.TargetFramework, Is.EqualTo(".NETCoreApp,Version=v8.0"));
        Assert.That(net100.TargetFramework, Is.EqualTo(".NETCoreApp,Version=v10.0"));

        string[] expectedLogicalTests =
        {
            "SampleSuite.DerivedAdapterTests.AdapterOwnTest",
            "SampleSuite.DerivedAdapterTests.BaseCase",
            "SampleSuite.DerivedAdapterTests.BaseSharedTest",
            "SampleSuite.DerivedAdapterTests.OverrideableTest",
            "SampleSuite.GenericDerivedTests.GenericBaseTest",
            "SampleSuite.GenericDerivedTests.GenericDerivedOwnTest",
            "SampleSuite.MixedMethodsFixture.ArchitectureMethod",
            "SampleSuite.MixedMethodsFixture.CustomCase",
            "SampleSuite.MixedMethodsFixture.QuarantinedMethod",
            "SampleSuite.MixedMethodsFixture.SmokeMethod",
            "SampleSuite.MixedMethodsFixture.SourceMethod",
            "SampleSuite.MixedMethodsFixture.TheoryMethod",
            "SampleSuite.OuterContainer+InnerFixture.InnerTest",
            "SampleSuite.OuterContainer+InnerFixture+DeepNestedFixture.DeepTest",
            "SampleSuite.OuterContainer+InnerFixture+DeepUncategorizedNested.UnmarkedNestedTest",
            "SampleSuite.PlainImplicitFixture.ImplicitUnitTest",
        };

        foreach (TestDiscoveryResult res in new[] { net48, net80, net100 })
        {
            Assert.That(res.TestFullNames, Is.EquivalentTo(expectedLogicalTests));
            Assert.That(
                res.Fixtures.Select(f => f.FixtureFullName),
                Does.Not.Contain("SampleSuite.AbstractAdapterBase"));
            Assert.That(
                res.Fixtures.Select(f => f.FixtureFullName),
                Does.Not.Contain("SampleSuite.GenericAbstractBase`1"));
        }

        // Verify none of the inspected test assemblies or missing dependency assembly were loaded into the AppDomain.
        var loadedNames = new HashSet<string>(
            AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetName().Name ?? string.Empty),
            StringComparer.OrdinalIgnoreCase);

        Assert.That(loadedNames, Does.Not.Contain(artifacts.Net48AssemblyName));
        Assert.That(loadedNames, Does.Not.Contain(artifacts.Net80AssemblyName));
        Assert.That(loadedNames, Does.Not.Contain(artifacts.Net100AssemblyName));
        Assert.That(loadedNames, Does.Not.Contain(artifacts.MissingExternalAssemblyName));
    }

    [Test]
    public void DiscoverAndFilter_ExpandsAllTypedCategoriesAndInheritedBaseCategories()
    {
        string dllPath = SharedArtifacts.Value.Net80DllPath;

        TestDiscoveryResult unit = Ecma335TestDiscovery.DiscoverAndFilter(dllPath, category: "unit");
        Assert.That(unit.TestFullNames, Is.EqualTo(new[]
        {
            "SampleSuite.OuterContainer+InnerFixture.InnerTest",
            "SampleSuite.OuterContainer+InnerFixture+DeepNestedFixture.DeepTest",
            "SampleSuite.PlainImplicitFixture.ImplicitUnitTest",
        }));

        // Nested class without its own category must NOT accidentally inherit from enclosing outer class.
        TestDiscoveryResult uncategorizedNested = Ecma335TestDiscovery.DiscoverAndFilter(
            dllPath,
            filters: new[] { "DeepUncategorizedNested" },
            category: "unit");
        Assert.That(uncategorizedNested.HasMatches, Is.False);

        TestDiscoveryResult arch = Ecma335TestDiscovery.DiscoverAndFilter(dllPath, category: "Architecture");
        Assert.That(arch.TestFullNames, Is.EqualTo(new[]
        {
            "SampleSuite.MixedMethodsFixture.ArchitectureMethod",
        }));

        TestDiscoveryResult smoke = Ecma335TestDiscovery.DiscoverAndFilter(dllPath, category: "RevitSmoke");
        Assert.That(smoke.TestFullNames, Is.EqualTo(new[]
        {
            "SampleSuite.MixedMethodsFixture.SmokeMethod",
        }));

        TestDiscoveryResult adapter = Ecma335TestDiscovery.DiscoverAndFilter(dllPath, category: "RevitAdapter");
        Assert.That(adapter.TestFullNames, Is.EqualTo(new[]
        {
            "SampleSuite.DerivedAdapterTests.AdapterOwnTest",
            "SampleSuite.DerivedAdapterTests.BaseCase",
            "SampleSuite.DerivedAdapterTests.BaseSharedTest",
            "SampleSuite.DerivedAdapterTests.OverrideableTest",
        }));

        TestDiscoveryResult isolated = Ecma335TestDiscovery.DiscoverAndFilter(dllPath, category: "RevitIsolatedDoc");
        Assert.That(isolated.TestFullNames, Is.EqualTo(new[]
        {
            "SampleSuite.GenericDerivedTests.GenericBaseTest",
            "SampleSuite.GenericDerivedTests.GenericDerivedOwnTest",
        }));

        TestDiscoveryResult quarantine = Ecma335TestDiscovery.DiscoverAndFilter(dllPath, category: "QUARANTINE");
        Assert.That(quarantine.TestFullNames, Is.EqualTo(new[]
        {
            "SampleSuite.MixedMethodsFixture.QuarantinedMethod",
        }));

        TestDiscoveryResult inheritedCustomCat = Ecma335TestDiscovery.DiscoverAndFilter(dllPath, category: "BaseSharedCat");
        Assert.That(inheritedCustomCat.TestFullNames, Is.EqualTo(new[]
        {
            "SampleSuite.DerivedAdapterTests.AdapterOwnTest",
            "SampleSuite.DerivedAdapterTests.BaseCase",
            "SampleSuite.DerivedAdapterTests.BaseSharedTest",
            "SampleSuite.DerivedAdapterTests.OverrideableTest",
        }));
    }

    [Test]
    public void DiscoverAndFilter_GroupsByConcreteFixtureWithTotalMethodCounts_AndHandlesCombinedFilters()
    {
        string dllPath = SharedArtifacts.Value.Net80DllPath;

        // Method-level category inside MixedMethodsFixture (6 total methods, only 1 matches).
        TestDiscoveryResult methodOnly = Ecma335TestDiscovery.DiscoverAndFilter(dllPath, category: "MethodOnlyCat");
        DiscoveredFixtureTests mixedGroup = methodOnly.Fixtures.Single();
        Assert.That(mixedGroup.FixtureFullName, Is.EqualTo("SampleSuite.MixedMethodsFixture"));
        Assert.That(mixedGroup.TestFullNames, Is.EqualTo(new[] { "SampleSuite.MixedMethodsFixture.CustomCase" }));
        Assert.That(mixedGroup.TotalMethodsInFixture, Is.EqualTo(6));

        // Combined --category + --filter (multiple fragments + VSTest expression).
        TestDiscoveryResult combined = Ecma335TestDiscovery.DiscoverAndFilter(
            dllPath,
            filters: new[] { "(FullyQualifiedName~DerivedAdapterTests|FullyQualifiedName~InnerFixture)&!FullyQualifiedName~BaseCase" },
            category: "RevitAdapter");
        Assert.That(combined.TestFullNames, Is.EqualTo(new[]
        {
            "SampleSuite.DerivedAdapterTests.AdapterOwnTest",
            "SampleSuite.DerivedAdapterTests.BaseSharedTest",
            "SampleSuite.DerivedAdapterTests.OverrideableTest",
        }));

        TestDiscoveryResult nestedFilter = Ecma335TestDiscovery.DiscoverAndFilter(
            dllPath,
            filters: new[] { "OuterContainer+InnerFixture+DeepNestedFixture" });
        Assert.That(nestedFilter.TestFullNames, Is.EqualTo(new[]
        {
            "SampleSuite.OuterContainer+InnerFixture+DeepNestedFixture.DeepTest",
        }));

        TestDiscoveryResult multiFilters = Ecma335TestDiscovery.DiscoverAndFilter(
            dllPath,
            filters: new[] { "ImplicitUnitTest", "DeepTest" });
        Assert.That(multiFilters.TestFullNames, Is.EqualTo(new[]
        {
            "SampleSuite.OuterContainer+InnerFixture+DeepNestedFixture.DeepTest",
            "SampleSuite.PlainImplicitFixture.ImplicitUnitTest",
        }));

        TestDiscoveryResult exactFqn = Ecma335TestDiscovery.DiscoverAndFilter(
            dllPath,
            filters: new[] { "FullyQualifiedName=SampleSuite.DerivedAdapterTests.BaseCase" });
        Assert.That(exactFqn.TestFullNames, Is.EqualTo(new[] { "SampleSuite.DerivedAdapterTests.BaseCase" }));

        TestDiscoveryResult byNameAndCat = Ecma335TestDiscovery.DiscoverAndFilter(
            dllPath,
            filters: new[] { "Name~Smoke&TestCategory=RevitSmoke" });
        Assert.That(byNameAndCat.TestFullNames, Is.EqualTo(new[] { "SampleSuite.MixedMethodsFixture.SmokeMethod" }));

        Assert.Throws<FormatException>(() =>
            Ecma335TestDiscovery.DiscoverAndFilter(dllPath, filters: new[] { "(FullyQualifiedName~Unbalanced" }));
    }

    [Test]
    public void DiscoverAndFilter_RicaunTestFiltersMatchTestAndTestCaseMethodsInRicaunNUnit()
    {
        // Compile a net8.0 test assembly loadable in-process solely to verify ricaun.NUnit.TestEngineFilter
        // against the exact filter patterns produced by Ecma335TestDiscovery.
        string dllPath = SharedArtifacts.Value.RicaunVerifyDllPath;

        TestDiscoveryResult discovery = Ecma335TestDiscovery.DiscoverAndFilter(dllPath, category: "PickMe");
        Assert.That(discovery.TestFullNames, Is.EqualTo(new[]
        {
            "FilterVerify.SampleFixture.ExactMethod",
            "FilterVerify.SampleFixture.ParameterizedCase",
            "FilterVerify.SampleFixture.SourceDrivenCase",
            "FilterVerify.SampleFixture+Nested.NestedCase",
        }));

        Assert.That(TestEngine.Initialize(out string? initMsg), Is.True, initMsg);

        TestEngineFilter.Reset();
        try
        {
            foreach (string pattern in discovery.RicaunTestFilters)
            {
                TestEngineFilter.Add(pattern);
            }

            TestAssemblyModel model = TestEngine.TestAssembly(dllPath, Array.Empty<object>());
            List<string> matchedByRicaun = model.Tests
                .SelectMany(t => t.Tests)
                .Select(t => t.FullName)
                .ToList();

            Assert.That(matchedByRicaun, Has.Some.EqualTo("FilterVerify.SampleFixture.ExactMethod.ExactMethod"));
            Assert.That(matchedByRicaun, Has.Some.EqualTo("FilterVerify.SampleFixture.ParameterizedCase.ParameterizedCase(1)"));
            Assert.That(matchedByRicaun, Has.Some.EqualTo("FilterVerify.SampleFixture.ParameterizedCase.NamedCase"));
            Assert.That(matchedByRicaun, Has.Some.EqualTo("FilterVerify.SampleFixture.SourceDrivenCase.SourceDrivenCase(10)"));
            Assert.That(matchedByRicaun, Has.Some.EqualTo("FilterVerify.SampleFixture.SourceDrivenCase.SourceDrivenCase(20)"));
            Assert.That(matchedByRicaun, Has.Some.EqualTo("FilterVerify.SampleFixture+Nested.NestedCase.NestedCase"));
            Assert.That(matchedByRicaun, Has.None.Contains("ExactMethodTwo"), "Sibling prefix-matching method must not match.");
        }
        finally
        {
            TestEngineFilter.Reset();
        }
    }

    [Test]
    public void CustomAttributeDecoder_DecodesStringsNamedPropsEnumsAndTypes_WithoutLoadingReferences()
    {
        byte[] missingExtBytes = TestAssemblyCompiler.CompileToBytes(
            "MissingDecoderRef",
            """
            using System;
            namespace MissingDecoderRef
            {
                public enum ExtEnum { Alpha = 0, Beta = 7 }
                public sealed class ExtMetaAttribute : Attribute
                {
                    public ExtMetaAttribute(string label, ExtEnum kind, Type target) { }
                    public string? Tag { get; set; }
                }
            }
            """);

        string targetDll = Path.Combine(_tempRoot, "AttributeProbe.dll");
        TestAssemblyCompiler.CompileToFile(
            targetDll,
            "AttributeProbe",
            """
            using System;
            using MissingDecoderRef;

            namespace ProbeNs
            {
                public enum LocalByteEnum : byte { First = 1, Second = 42 }

                [AttributeUsage(AttributeTargets.Class)]
                public sealed class LocalMetaAttribute : Attribute
                {
                    public LocalMetaAttribute(string name, LocalByteEnum code, DayOfWeek day, Type targetType) { }
                    public string? Note { get; set; }
                    public string[]? Tags { get; set; }
                }

                [LocalMeta("hello", LocalByteEnum.Second, DayOfWeek.Wednesday, typeof(SampleTarget), Note = "named", Tags = new[] { "a", "b" })]
                [ExtMeta("ext", ExtEnum.Beta, typeof(SampleTarget), Tag = "extTag")]
                public sealed class SampleTarget { }
            }
            """,
            extraReferences: new[] { MetadataReference.CreateFromImage(missingExtBytes) });

        using var stream = File.OpenRead(targetDll);
        using var peReader = new PEReader(stream);
        MetadataReader reader = peReader.GetMetadataReader();

        TypeDefinition targetType = reader.TypeDefinitions
            .Select(h => reader.GetTypeDefinition(h))
            .Single(td => reader.GetString(td.Name) == "SampleTarget");

        IReadOnlyList<DecodedCustomAttribute> decoded =
            Ecma335TestDiscovery.DecodeCustomAttributes(reader, targetType.GetCustomAttributes());

        DecodedCustomAttribute local = decoded.Single(a => a.AttributeFullName == "ProbeNs.LocalMetaAttribute");
        Assert.That(local.FixedArguments[0], Is.EqualTo("hello"));
        Assert.That(local.FixedArguments[1], Is.EqualTo((byte)42));
        Assert.That(local.FixedArguments[2], Is.EqualTo((int)DayOfWeek.Wednesday));
        Assert.That(local.FixedArguments[3]?.ToString(), Is.EqualTo("ProbeNs.SampleTarget"));
        Assert.That(local.NamedArguments["Note"], Is.EqualTo("named"));
        Assert.That(local.NamedArguments["Tags"], Is.EqualTo(new object?[] { "a", "b" }));

        DecodedCustomAttribute ext = decoded.Single(a => a.AttributeFullName == "MissingDecoderRef.ExtMetaAttribute");
        Assert.That(ext.FixedArguments[0], Is.EqualTo("ext"));
        Assert.That(ext.FixedArguments[1], Is.EqualTo(7));
        Assert.That(ext.FixedArguments[2]?.ToString(), Is.EqualTo("ProbeNs.SampleTarget"));
        Assert.That(ext.NamedArguments["Tag"], Is.EqualTo("extTag"));
    }

    [Test]
    public async Task OneShotTestRunner_CategoryAndZeroMatchShortCircuitEndToEnd()
    {
        string revit2026Dir = Path.Combine(_tempRoot, "Autodesk", "Revit 2026");
        Directory.CreateDirectory(revit2026Dir);
        File.WriteAllText(Path.Combine(revit2026Dir, "Revit.exe"), string.Empty);

        var revitDiscovery = new RevitInstallationDiscovery(
            () => new[] { Path.Combine(_tempRoot, "Autodesk") },
            () => Array.Empty<UninstallRegistryEntry>());

        string dllPath = SharedArtifacts.Value.Net80DllPath;

        // 1. Zero-match category short-circuits before booting any worker and writes valid empty TRX.
        using var zeroLauncher = new StubWorkerProcessLauncher();
        var zeroRunner = new OneShotTestRunner(
            revitDiscovery,
            zeroLauncher,
            new RevitPipeTestClientFactory(),
            bundleLeaseFactory: _ => new NoOpDisposable());

        string zeroTrxPath = Path.Combine(_tempRoot, "out", "zero_cat.trx");
        OneShotRunResult zeroResult = await zeroRunner.RunAsync(new OneShotRunOptions
        {
            RevitVersion = 2026,
            AssemblyPath = dllPath,
            TrxPath = zeroTrxPath,
            Category = "CategoryThatDoesNotExist"
        });

        Assert.That(zeroResult.Success, Is.True);
        Assert.That(zeroResult.ExitCode, Is.EqualTo(0));
        Assert.That(zeroResult.ZeroMatches, Is.True);
        Assert.That(zeroResult.TotalTests, Is.EqualTo(0));
        Assert.That(zeroResult.WorkerIds, Is.Empty);
        Assert.That(zeroResult.ShardCount, Is.EqualTo(0));
        Assert.That(zeroLauncher.Workers, Is.Empty, "Zero-match discovery must not boot a Revit worker.");
        Assert.That(File.Exists(zeroTrxPath), Is.True);

        XDocument trxDoc = XDocument.Load(zeroTrxPath);
        XElement? counters = trxDoc.Descendants(TrxWriter.TrxNamespace + "Counters").SingleOrDefault();
        Assert.That(counters, Is.Not.Null);
        Assert.That(counters!.Attribute("total")?.Value, Is.EqualTo("0"));

        // 2. Combined --category + --filter launches 1 worker with pre-expanded exact Ricaun filters.
        using var matchLauncher = new StubWorkerProcessLauncher((server, _) =>
        {
            server.InitialIsBusy = false;
            server.ExecutionHandler = (req, emitTest, _) =>
            {
                var test = new TestModel
                {
                    Name = "SmokeMethod",
                    FullName = "SampleSuite.MixedMethodsFixture.SmokeMethod.SmokeMethod",
                    Success = true,
                    Time = 12.0
                };
                emitTest(test);
                return Task.FromResult(new TestAssemblyModel
                {
                    FileName = req.TestPathFile ?? string.Empty,
                    Name = "SampleNet80",
                    Success = true,
                    Tests = new List<TestTypeModel>
                    {
                        new()
                        {
                            Name = "MixedMethodsFixture",
                            FullName = "SampleSuite.MixedMethodsFixture",
                            Success = true,
                            Tests = new List<TestModel> { test }
                        }
                    }
                });
            };
        });

        var matchRunner = new OneShotTestRunner(
            revitDiscovery,
            matchLauncher,
            new RevitPipeTestClientFactory(),
            bundleLeaseFactory: _ => new NoOpDisposable());

        string matchTrxPath = Path.Combine(_tempRoot, "out", "match_cat.trx");
        OneShotRunResult matchResult = await matchRunner.RunAsync(new OneShotRunOptions
        {
            RevitVersion = 2026,
            AssemblyPath = dllPath,
            TrxPath = matchTrxPath,
            Category = "RevitSmoke",
            Filters = new[] { "MixedMethodsFixture" },
            WorkerId = "w_231_match"
        });

        Assert.That(matchResult.Success, Is.True);
        Assert.That(matchResult.ZeroMatches, Is.False);
        Assert.That(matchResult.TotalTests, Is.EqualTo(1));
        Assert.That(matchResult.WorkerIds, Is.EqualTo(new[] { "w_231_match" }));
        Assert.That(matchLauncher.Workers.Count, Is.EqualTo(1));
        Assert.That(
            matchLauncher.Workers.Single().Server.ReceivedRequests.Single().TestFilters,
            Is.EqualTo(new[] { "SampleSuite.MixedMethodsFixture.SmokeMethod.*" }));
    }

    private static FixtureAssemblyArtifacts BuildSharedAssemblies()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "RevitWorkerPool_Ecma335Shared",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        const string missingDepName = "MissingExternalApi";
        byte[] missingDepBytes = TestAssemblyCompiler.CompileToBytes(
            missingDepName,
            """
            using System;
            namespace MissingExternalApi
            {
                public enum UnloadedMode { Fast = 1, Safe = 2 }
                public sealed class UnloadedType { }
                [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
                public sealed class UnloadedApiAttribute : Attribute
                {
                    public UnloadedApiAttribute(UnloadedMode mode) { }
                }
            }
            """);
        MetadataReference missingDepRef = MetadataReference.CreateFromImage(missingDepBytes);

        string net48Source = BuildSharedSource(".NETFramework,Version=v4.8");
        string net80Source = BuildSharedSource(".NETCoreApp,Version=v8.0");
        string net100Source = BuildSharedSource(".NETCoreApp,Version=v10.0");

        string net48Dll = TestAssemblyCompiler.CompileToFile(
            Path.Combine(root, "net48", "SyntheticTestFixtureSample.Net48.dll"),
            "SyntheticTestFixtureSample.Net48",
            net48Source,
            targetNet48: true);

        string net80Dll = TestAssemblyCompiler.CompileToFile(
            Path.Combine(root, "net80", "SyntheticTestFixtureSample.Net80.dll"),
            "SyntheticTestFixtureSample.Net80",
            net80Source,
            targetNet48: false,
            extraReferences: new[] { missingDepRef });

        string net100Dll = TestAssemblyCompiler.CompileToFile(
            Path.Combine(root, "net100", "SyntheticTestFixtureSample.Net100.dll"),
            "SyntheticTestFixtureSample.Net100",
            net100Source,
            targetNet48: false,
            extraReferences: new[] { missingDepRef });

        string ricaunVerifyDll = TestAssemblyCompiler.CompileToFile(
            Path.Combine(root, "ricaun", "RicaunFilterCheck.dll"),
            "RicaunFilterCheck",
            """
            using NUnit.Framework;

            namespace FilterVerify
            {
                public class SampleFixture
                {
                    [Test]
                    [Category("PickMe")]
                    public void ExactMethod() { }

                    [Test]
                    public void ExactMethodTwo() { }

                    [TestCase(1)]
                    [TestCase(2, TestName = "NamedCase")]
                    [Category("PickMe")]
                    public void ParameterizedCase(int value) { }

                    [TestCaseSource(nameof(SourceValues))]
                    [Category("PickMe")]
                    public void SourceDrivenCase(int value) { }

                    public static int[] SourceValues => new[] { 10, 20 };

                    public class Nested
                    {
                        [Test]
                        [Category("PickMe")]
                        public void NestedCase() { }
                    }
                }
            }
            """);

        return new FixtureAssemblyArtifacts(
            root,
            net48Dll,
            net80Dll,
            net100Dll,
            ricaunVerifyDll,
            "SyntheticTestFixtureSample.Net48",
            "SyntheticTestFixtureSample.Net80",
            "SyntheticTestFixtureSample.Net100",
            missingDepName);
    }

    private static string BuildSharedSource(string targetFramework)
    {
        bool useMissingRef = targetFramework.StartsWith(".NETCoreApp", StringComparison.Ordinal);
        string missingUsing = useMissingRef ? "using MissingExternalApi;\n" : string.Empty;
        string missingAttr = useMissingRef ? "[UnloadedApi(UnloadedMode.Safe)]\n" : string.Empty;
        string missingField = useMissingRef ? "private UnloadedType? _unused;\n" : string.Empty;

        return $$"""
            using System;
            using System.Runtime.Versioning;
            using NUnit.Framework;
            {{missingUsing}}
            [assembly: TargetFramework("{{targetFramework}}")]

            namespace SyntheticTests
            {
                [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
                public sealed class UnitTestAttribute : CategoryAttribute { public UnitTestAttribute() : base("Unit") { } }

                [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
                public sealed class ArchitectureTestAttribute : CategoryAttribute { public ArchitectureTestAttribute() : base("Architecture") { } }

                [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
                public sealed class RevitSmokeTestAttribute : CategoryAttribute { public RevitSmokeTestAttribute() : base("RevitSmoke") { } }

                [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
                public sealed class RevitAdapterTestAttribute : CategoryAttribute { public RevitAdapterTestAttribute() : base("RevitAdapter") { } }

                [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
                public sealed class RevitIsolatedDocTestAttribute : CategoryAttribute { public RevitIsolatedDocTestAttribute() : base("RevitIsolatedDoc") { } }

                [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
                public sealed class QuarantineTestAttribute : CategoryAttribute { public QuarantineTestAttribute() : base("Quarantine") { } }
            }

            namespace SampleSuite
            {
                using SyntheticTests;

                [RevitAdapterTest]
                [Category("BaseSharedCat")]
                public abstract class AbstractAdapterBase
                {
                    [Test]
                    public void BaseSharedTest() { }

                    [TestCase(1)]
                    public void BaseCase(int value) { }

                    [Test]
                    public virtual void OverrideableTest() { }
                }

                [TestFixture]
                {{missingAttr}}public class DerivedAdapterTests : AbstractAdapterBase
                {
                    {{missingField}}
                    [Test]
                    public void AdapterOwnTest() { }

                    public override void OverrideableTest() { }
                }

                [RevitIsolatedDocTest]
                public abstract class GenericAbstractBase<T>
                {
                    [Test]
                    public void GenericBaseTest() { }
                }

                [TestFixture]
                public class GenericDerivedTests : GenericAbstractBase<string>
                {
                    [Test]
                    public void GenericDerivedOwnTest() { }
                }

                [TestFixture]
                public class MixedMethodsFixture
                {
                    [Test]
                    [ArchitectureTestAttribute]
                    public void ArchitectureMethod() { }

                    [Test]
                    [RevitSmokeTest]
                    public void SmokeMethod() { }

                    [Test]
                    [QuarantineTest]
                    [Explicit]
                    public void QuarantinedMethod() { }

                    [TestCase("alpha", TestName = "AliasAlpha")]
                    [Category("MethodOnlyCat")]
                    public void CustomCase(string name) { }

                    [TestCaseSource(nameof(Items))]
                    public void SourceMethod(int x) { }

                    public static int[] Items = new[] { 1, 2 };

                    [Theory]
                    public void TheoryMethod(bool flag) { }
                }

                public class OuterContainer
                {
                    [UnitTest]
                    public class InnerFixture
                    {
                        [Test]
                        public void InnerTest() { }

                        [UnitTest]
                        public class DeepNestedFixture
                        {
                            [Test]
                            public void DeepTest() { }
                        }

                        public class DeepUncategorizedNested
                        {
                            [Test]
                            public void UnmarkedNestedTest() { }
                        }
                    }
                }

                public class PlainImplicitFixture
                {
                    [Test]
                    [UnitTestAttribute]
                    public void ImplicitUnitTest() { }
                }
            }
            """;
    }

    private sealed record FixtureAssemblyArtifacts(
        string RootDir,
        string Net48DllPath,
        string Net80DllPath,
        string Net100DllPath,
        string RicaunVerifyDllPath,
        string Net48AssemblyName,
        string Net80AssemblyName,
        string Net100AssemblyName,
        string MissingExternalAssemblyName);

    private sealed class NoOpDisposable : IDisposable
    {
        public void Dispose()
        {
        }
    }
}
