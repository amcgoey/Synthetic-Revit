using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using RevitWorkerPool.Discovery;
using RevitWorkerPool.Execution;
using RevitWorkerPool.Pipe;
using ricaun.NUnit.Models;

namespace RevitWorkerPool.Tests;

[TestFixture]
public class OneShotTestRunnerTests
{
    private string _tempRoot = string.Empty;
    private string _fakeAssemblyPath = string.Empty;
    private RevitInstallationDiscovery _discovery = null!;
    private static readonly byte[] CompiledRunnerTestDllBytes = TestAssemblyCompiler.CompileToBytes(
        "Synthetic.RevitTests.2026",
        """
        using NUnit.Framework;

        namespace SyntheticTests.Platform.Host
        {
            [TestFixture]
            public class BootstrapWorkerIsolationRevitTests
            {
                [Test]
                public void SmokeTest() { }
            }
        }

        namespace Ns
        {
            [TestFixture]
            public class FailingFixture
            {
                [Test]
                public void Boom() { }
            }
        }
        """);

    [SetUp]
    public void SetUp()
    {
        _tempRoot = Path.Combine(
            Path.GetTempPath(),
            "RevitWorkerPool_RunnerTests",
            Guid.NewGuid().ToString("N"));
        string revit2026Dir = Path.Combine(_tempRoot, "Autodesk", "Revit 2026");
        Directory.CreateDirectory(revit2026Dir);
        File.WriteAllText(Path.Combine(revit2026Dir, "Revit.exe"), string.Empty);

        _fakeAssemblyPath = Path.Combine(_tempRoot, "Synthetic.RevitTests.2026.dll");
        File.WriteAllBytes(_fakeAssemblyPath, CompiledRunnerTestDllBytes);

        _discovery = new RevitInstallationDiscovery(
            () => new[] { Path.Combine(_tempRoot, "Autodesk") },
            () => Array.Empty<UninstallRegistryEntry>());
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }

    [Test]
    public async Task RunAsync_PropagatesWorkerId_GeneratesTrx_AndTerminatesWorkerImmediately()
    {
        using var launcher = new StubWorkerProcessLauncher((server, _) =>
        {
            server.InitialIsBusy = true;
            server.AutoReadyDelay = TimeSpan.FromMilliseconds(30);
            server.ExecutionHandler = (req, emitTest, _) =>
            {
                var test = new TestModel
                {
                    Name = "SmokeTest",
                    FullName = "SyntheticTests.Platform.Host.BootstrapWorkerIsolationRevitTests.SmokeTest",
                    Success = true,
                    Time = 42.0
                };
                emitTest(test);
                return Task.FromResult(new TestAssemblyModel
                {
                    FileName = req.TestPathFile ?? string.Empty,
                    Name = "Synthetic.RevitTests.2026",
                    Success = true,
                    Tests = new List<TestTypeModel>
                    {
                        new()
                        {
                            Name = "BootstrapWorkerIsolationRevitTests",
                            FullName = "SyntheticTests.Platform.Host.BootstrapWorkerIsolationRevitTests",
                            Success = true,
                            Tests = new List<TestModel> { test }
                        }
                    }
                });
            };
        });

        var runner = new OneShotTestRunner(
            _discovery,
            launcher,
            new RevitPipeTestClientFactory(),
            bundleLeaseFactory: _ => new NoOpDisposable());

        string trxPath = Path.Combine(_tempRoot, "out", "smoke.trx");
        OneShotRunResult result = await runner.RunAsync(new OneShotRunOptions
        {
            RevitVersion = 2026,
            AssemblyPath = _fakeAssemblyPath,
            TrxPath = trxPath,
            Filters = new[] { "BootstrapWorkerIsolationRevitTests" },
            WorkerId = "w_explicit_228",
            BootTimeout = TimeSpan.FromSeconds(3),
            ExecutionTimeout = TimeSpan.FromSeconds(5)
        });

        Assert.That(result.Success, Is.True);
        Assert.That(result.ExitCode, Is.EqualTo(0));
        Assert.That(result.Version, Is.EqualTo(2026));
        Assert.That(result.WorkerIds, Is.EqualTo(new[] { "w_explicit_228" }));
        Assert.That(result.ShardCount, Is.EqualTo(1));
        Assert.That(result.TotalTests, Is.EqualTo(1));
        Assert.That(result.PassedTests, Is.EqualTo(1));
        Assert.That(result.FailedTests, Is.EqualTo(0));
        Assert.That(result.SkippedTests, Is.EqualTo(0));
        Assert.That(result.ZeroMatches, Is.False);
        Assert.That(result.ShimEnabled, Is.False);
        Assert.That(File.Exists(trxPath), Is.True);

        var launchedRequest = launcher.LaunchedRequests.Single();
        Assert.That(launchedRequest.WorkerId, Is.EqualTo("w_explicit_228"));

        var workerRecord = launcher.Workers.Single();
        Assert.That(workerRecord.IsTerminated, Is.True, "Worker must be terminated immediately upon run completion.");
        Assert.That(
            workerRecord.Server.ReceivedRequests.Single().TestFilters,
            Is.EqualTo(new[] { "SyntheticTests.Platform.Host.BootstrapWorkerIsolationRevitTests.SmokeTest.*" }));
    }

    [Test]
    public async Task RunAsync_FailsAndTerminatesWorker_WhenBootHangsBusy()
    {
        using var launcher = new StubWorkerProcessLauncher((server, _) =>
        {
            server.InitialIsBusy = true;
        });

        var runner = new OneShotTestRunner(
            _discovery,
            launcher,
            new RevitPipeTestClientFactory(),
            bundleLeaseFactory: _ => new NoOpDisposable());

        string trxPath = Path.Combine(_tempRoot, "out", "hung.trx");
        OneShotRunResult result = await runner.RunAsync(new OneShotRunOptions
        {
            RevitVersion = 2026,
            AssemblyPath = _fakeAssemblyPath,
            TrxPath = trxPath,
            BootTimeout = TimeSpan.FromMilliseconds(200),
            ExecutionTimeout = TimeSpan.FromSeconds(1)
        });

        Assert.That(result.Success, Is.False);
        Assert.That(result.ExitCode, Is.EqualTo(1));
        Assert.That(result.Error, Does.Contain("IsBusy == false"));
        Assert.That(result.WorkerIds.Count, Is.EqualTo(1));
        Assert.That(launcher.Workers.Single().IsTerminated, Is.True);
    }

    [Test]
    public void NormalizeTestFilters_WrapsPlainFragmentsWithWildcards_AndPreservesExplicitWildcards()
    {
        string[] normalized = OneShotTestRunner.NormalizeTestFilters(new[]
        {
            "BootstrapWorkerIsolationRevitTests",
            "*.MyTest*",
            "Ns.Fixture.Test?"
        });

        Assert.That(normalized, Is.EqualTo(new[]
        {
            "*BootstrapWorkerIsolationRevitTests*",
            "*.MyTest*",
            "Ns.Fixture.Test?"
        }));
    }

    [Test]
    public async Task RunAsync_ReportsZeroMatchesAndFailedTestsCorrectly()
    {
        using var zeroLauncher = new StubWorkerProcessLauncher((server, _) =>
        {
            server.InitialIsBusy = false;
            server.ExecutionHandler = (req, _, _) => Task.FromResult(new TestAssemblyModel
            {
                FileName = req.TestPathFile ?? string.Empty,
                Name = "Synthetic.RevitTests.2026",
                Success = true,
                Tests = new List<TestTypeModel>()
            });
        });

        var zeroRunner = new OneShotTestRunner(
            _discovery,
            zeroLauncher,
            new RevitPipeTestClientFactory(),
            bundleLeaseFactory: _ => new NoOpDisposable());

        string zeroTrx = Path.Combine(_tempRoot, "out", "zero.trx");
        OneShotRunResult zeroResult = await zeroRunner.RunAsync(new OneShotRunOptions
        {
            RevitVersion = 2026,
            AssemblyPath = _fakeAssemblyPath,
            TrxPath = zeroTrx,
            Filters = new[] { "NonExistentFilter" }
        });

        Assert.That(zeroResult.Success, Is.True);
        Assert.That(zeroResult.ExitCode, Is.EqualTo(0));
        Assert.That(zeroResult.ZeroMatches, Is.True);
        Assert.That(zeroResult.TotalTests, Is.EqualTo(0));
        Assert.That(zeroResult.WorkerIds, Is.Empty);
        Assert.That(zeroResult.ShardCount, Is.EqualTo(0));
        Assert.That(File.Exists(zeroTrx), Is.True);
        Assert.That(zeroLauncher.Workers, Is.Empty, "Zero-match pre-discovery must short-circuit without launching a worker.");

        using var failLauncher = new StubWorkerProcessLauncher((server, _) =>
        {
            server.InitialIsBusy = false;
            server.ExecutionHandler = (req, _, _) => Task.FromResult(new TestAssemblyModel
            {
                FileName = req.TestPathFile ?? string.Empty,
                Name = "Synthetic.RevitTests.2026",
                Success = false,
                Tests = new List<TestTypeModel>
                {
                    new()
                    {
                        Name = "FailingFixture",
                        FullName = "Ns.FailingFixture",
                        Success = false,
                        Tests = new List<TestModel>
                        {
                            new()
                            {
                                Name = "Boom",
                                FullName = "Ns.FailingFixture.Boom",
                                Success = false,
                                Message = "Assertion failed",
                                Time = 15.0
                            }
                        }
                    }
                }
            });
        });

        var failRunner = new OneShotTestRunner(
            _discovery,
            failLauncher,
            new RevitPipeTestClientFactory(),
            bundleLeaseFactory: _ => new NoOpDisposable());

        string failTrx = Path.Combine(_tempRoot, "out", "fail.trx");
        OneShotRunResult failResult = await failRunner.RunAsync(new OneShotRunOptions
        {
            RevitVersion = 2026,
            AssemblyPath = _fakeAssemblyPath,
            TrxPath = failTrx
        });

        Assert.That(failResult.Success, Is.False);
        Assert.That(failResult.ExitCode, Is.EqualTo(1));
        Assert.That(failResult.FailedTests, Is.EqualTo(1));
        Assert.That(failResult.TotalTests, Is.EqualTo(1));
        Assert.That(failResult.ZeroMatches, Is.False);
        Assert.That(failLauncher.Workers.Single().IsTerminated, Is.True);
    }

    private sealed class NoOpDisposable : IDisposable
    {
        public void Dispose()
        {
        }
    }
}
