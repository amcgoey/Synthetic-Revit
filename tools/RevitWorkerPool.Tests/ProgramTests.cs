using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using NUnit.Framework;
using RevitWorkerPool.Discovery;
using RevitWorkerPool.Execution;
using RevitWorkerPool.Ipc;
using RevitWorkerPool.Launching;
using RevitWorkerPool.Supervisor;

namespace RevitWorkerPool.Tests;

[TestFixture]
public class ProgramTests
{
    [Test]
    public void TryParseRunArguments_ParsesAllOptionsCorrectly()
    {
        string[] args =
        {
            "--version", "2026",
            "--assembly", @"C:\temp\Synthetic.RevitTests.2026.dll",
            "--trx", @"C:\temp\out.trx",
            "--filter", "BootstrapWorkerIsolationRevitTests",
            "--category", "RevitSmoke",
            "--timeout-seconds", "90",
            "--boot-timeout-seconds", "45",
            "--shards", "3",
            "--worker-id", "w_cli_1",
            "--project-root", @"C:\repo",
            "--pipe-name", "CustomControlPipe",
            "--idle-ttl-minutes", "28",
            "--no-shim",
            "--one-shot",
            "--json",
            "--diagnostics"
        };

        bool ok = Program.TryParseRunArguments(args, out OneShotRunOptions? options, out string? error);

        Assert.That(ok, Is.True, error);
        Assert.That(error, Is.Null);
        Assert.That(options, Is.Not.Null);
        Assert.That(options!.RevitVersion, Is.EqualTo(2026));
        Assert.That(options.AssemblyPath, Is.EqualTo(@"C:\temp\Synthetic.RevitTests.2026.dll"));
        Assert.That(options.TrxPath, Is.EqualTo(@"C:\temp\out.trx"));
        Assert.That(options.Filters, Is.EqualTo(new[] { "BootstrapWorkerIsolationRevitTests" }));
        Assert.That(options.Category, Is.EqualTo("RevitSmoke"));
        Assert.That(options.ExecutionTimeout.TotalSeconds, Is.EqualTo(90));
        Assert.That(options.BootTimeout.TotalSeconds, Is.EqualTo(45));
        Assert.That(options.Shards, Is.EqualTo(3));
        Assert.That(options.WorkerId, Is.EqualTo("w_cli_1"));
        Assert.That(options.ProjectRoot, Is.EqualTo(@"C:\repo"));
        Assert.That(options.PipeName, Is.EqualTo("CustomControlPipe"));
        Assert.That(options.IdleTtlMinutes, Is.EqualTo(28));
        Assert.That(options.NoShim, Is.True);
        Assert.That(options.OneShot, Is.True);
        Assert.That(options.JsonOutput, Is.True);
        Assert.That(options.Diagnostics, Is.True);
    }

    [Test]
    public void TryParseRunArguments_DefaultsToSupervisorMode_AndDefaultPipeTtlAndNoShimFalse()
    {
        string[] args =
        {
            "--version", "2026",
            "--assembly", @"C:\temp\Synthetic.RevitTests.2026.dll",
            "--trx", @"C:\temp\out.trx"
        };

        bool ok = Program.TryParseRunArguments(args, out OneShotRunOptions? options, out string? error);

        Assert.That(ok, Is.True, error);
        Assert.That(options!.OneShot, Is.False);
        Assert.That(options.Shards, Is.Null);
        Assert.That(options.PipeName, Is.EqualTo(SupervisorIpcConstants.DefaultPipeName));
        Assert.That(options.IdleTtlMinutes, Is.EqualTo(SupervisorIpcConstants.DefaultIdleTtlMinutes));
        Assert.That(options.NoShim, Is.False);
    }

    [Test]
    public void TryParseRunArguments_RejectsMissingRequiredOrInvalidOptions()
    {
        Assert.That(
            Program.TryParseRunArguments(new[] { "--assembly", "a.dll", "--trx", "a.trx" }, out _, out string? err1),
            Is.False);
        Assert.That(err1, Does.Contain("--version"));

        Assert.That(
            Program.TryParseRunArguments(new[] { "--version", "abc", "--assembly", "a.dll", "--trx", "a.trx" }, out _, out string? err2),
            Is.False);
        Assert.That(err2, Does.Contain("--version"));

        Assert.That(
            Program.TryParseRunArguments(new[] { "--version", "2026", "--trx", "a.trx" }, out _, out string? err3),
            Is.False);
        Assert.That(err3, Does.Contain("--assembly"));

        Assert.That(
            Program.TryParseRunArguments(new[] { "--version", "2026", "--assembly", "a.dll" }, out _, out string? err4),
            Is.False);
        Assert.That(err4, Does.Contain("--trx"));

        Assert.That(
            Program.TryParseRunArguments(new[] { "--version", "2026", "--assembly", "a.dll", "--trx", "a.trx", "--timeout-seconds", "0" }, out _, out string? err5),
            Is.False);
        Assert.That(err5, Does.Contain("--timeout-seconds"));

        Assert.That(
            Program.TryParseRunArguments(new[] { "--version", "2026", "--assembly", "a.dll", "--trx", "a.trx", "--category" }, out _, out string? err6),
            Is.False);
        Assert.That(err6, Does.Contain("--category"));

        Assert.That(
            Program.TryParseRunArguments(new[] { "--version", "2026", "--assembly", "a.dll", "--trx", "a.trx", "--idle-ttl-minutes", "19" }, out _, out string? err7),
            Is.False);
        Assert.That(err7, Does.Contain("--idle-ttl-minutes"));

        Assert.That(
            Program.TryParseRunArguments(new[] { "--version", "2026", "--assembly", "a.dll", "--trx", "a.trx", "--idle-ttl-minutes", "31" }, out _, out string? err8),
            Is.False);
        Assert.That(err8, Does.Contain("--idle-ttl-minutes"));

        Assert.That(
            Program.TryParseRunArguments(new[] { "--version", "2026", "--assembly", "a.dll", "--trx", "a.trx", "--shards", "0" }, out _, out string? err9),
            Is.False);
        Assert.That(err9, Does.Contain("--shards"));

        Assert.That(
            Program.TryParseRunArguments(new[] { "--version", "2026", "--assembly", "a.dll", "--trx", "a.trx", "--shards", "-2" }, out _, out string? err10),
            Is.False);
        Assert.That(err10, Does.Contain("--shards"));

        Assert.That(
            Program.TryParseRunArguments(new[] { "--version", "2026", "--assembly", "a.dll", "--trx", "a.trx", "--next-version", "2025" }, out _, out string? err11),
            Is.False);
        Assert.That(err11, Does.Contain("Unknown option '--next-version'"));
    }

    [Test]
    public void TryParseWarmArguments_SupportsDefaultsAndExplicitOverrides()
    {
        Assert.That(
            Program.TryParseWarmArguments(Array.Empty<string>(), out Program.WarmCliOptions? defaults, out string? errDefault),
            Is.True,
            errDefault);
        Assert.That(defaults!.RevitVersion, Is.Null);
        Assert.That(defaults.Count, Is.EqualTo(SupervisorOptions.DefaultWarmWorkerCount));
        Assert.That(defaults.PipeName, Is.EqualTo(SupervisorIpcConstants.DefaultPipeName));
        Assert.That(defaults.IdleTtlMinutes, Is.EqualTo(25));

        string[] explicitArgs =
        {
            "--version", "2024",
            "--count", "3",
            "--pipe-name", "WarmPipe",
            "--idle-ttl-minutes", "20",
            "--no-shim",
            "--json",
            "--diagnostics"
        };
        Assert.That(
            Program.TryParseWarmArguments(explicitArgs, out Program.WarmCliOptions? parsed, out string? errExplicit),
            Is.True,
            errExplicit);
        Assert.That(parsed!.RevitVersion, Is.EqualTo(2024));
        Assert.That(parsed.Count, Is.EqualTo(3));
        Assert.That(parsed.PipeName, Is.EqualTo("WarmPipe"));
        Assert.That(parsed.IdleTtlMinutes, Is.EqualTo(20));
        Assert.That(parsed.NoShim, Is.True);
        Assert.That(parsed.JsonOutput, Is.True);
        Assert.That(parsed.Diagnostics, Is.True);

        Assert.That(
            Program.TryParseWarmArguments(new[] { "--count", "0" }, out _, out string? errCount),
            Is.False);
        Assert.That(errCount, Does.Contain("--count"));
    }

    [Test]
    public void TryParseSupervisorAndControlArguments_ParseAndValidate()
    {
        string[] supArgs =
        {
            "--pipe-name", "SupPipe",
            "--idle-ttl-minutes", "30",
            "--no-shim",
            "--project-root", @"C:\repo",
            "--diagnostics"
        };
        Assert.That(
            Program.TryParseSupervisorArguments(supArgs, out Program.SupervisorCliOptions? supOpts, out string? supErr),
            Is.True,
            supErr);
        Assert.That(supOpts!.PipeName, Is.EqualTo("SupPipe"));
        Assert.That(supOpts.IdleTtlMinutes, Is.EqualTo(30));
        Assert.That(supOpts.NoShim, Is.True);
        Assert.That(supOpts.ProjectRoot, Is.EqualTo(@"C:\repo"));
        Assert.That(supOpts.Diagnostics, Is.True);

        Assert.That(
            Program.TryParseSupervisorArguments(new[] { "--idle-ttl-minutes", "10" }, out _, out string? ttlErr),
            Is.False);
        Assert.That(ttlErr, Does.Contain("--idle-ttl-minutes"));

        Assert.That(
            Program.TryParsePipeControlArguments(
                new[] { "--pipe-name", "CtrlPipe", "--json" },
                "status",
                out Program.PipeControlCliOptions? ctrlOpts,
                out string? ctrlErr),
            Is.True,
            ctrlErr);
        Assert.That(ctrlOpts!.PipeName, Is.EqualTo("CtrlPipe"));
        Assert.That(ctrlOpts.JsonOutput, Is.True);
        Assert.That(ctrlOpts.WhenIdle, Is.False);
        Assert.That(ctrlOpts.Force, Is.False);

        Assert.That(
            Program.TryParsePipeControlArguments(
                new[] { "--when-idle", "--pipe-name", "DrainPipe", "--json" },
                "drain",
                out Program.PipeControlCliOptions? whenIdleOpts,
                out string? whenIdleErr),
            Is.True,
            whenIdleErr);
        Assert.That(whenIdleOpts!.WhenIdle, Is.True);
        Assert.That(whenIdleOpts.Force, Is.False);

        Assert.That(
            Program.TryParsePipeControlArguments(
                new[] { "--force", "--pipe-name", "DrainPipe" },
                "drain",
                out Program.PipeControlCliOptions? forceOpts,
                out string? forceErr),
            Is.True,
            forceErr);
        Assert.That(forceOpts!.Force, Is.True);
        Assert.That(forceOpts.WhenIdle, Is.False);

        Assert.That(
            Program.TryParsePipeControlArguments(
                new[] { "--when-idle", "--force" },
                "drain",
                out _,
                out string? conflictErr),
            Is.False);
        Assert.That(conflictErr, Does.Contain("--when-idle"));
        Assert.That(conflictErr, Does.Contain("--force"));

        Assert.That(
            Program.TryParsePipeControlArguments(
                new[] { "--when-idle" },
                "status",
                out _,
                out string? statusWhenIdleErr),
            Is.False);
        Assert.That(statusWhenIdleErr, Does.Contain("Unknown option"));
    }

    [Test]
    public void MaxWorkersOption_ValidatesRange1To10_ReadsEnvFallback_AndCliOverridesEnv()
    {
        Assert.That(SupervisorOptions.DefaultMaxActiveWorkers, Is.EqualTo(8));
        Assert.That(SupervisorOptions.MinMaxActiveWorkers, Is.EqualTo(1));
        Assert.That(SupervisorOptions.MaxMaxActiveWorkers, Is.EqualTo(10));

        // 1. Accepted on --supervisor, warm, and run (values 1..10).
        Assert.That(
            Program.TryParseSupervisorArguments(
                new[] { "--max-workers", "10" },
                out Program.SupervisorCliOptions? sup10,
                out string? supErr,
                _ => null),
            Is.True,
            supErr);
        Assert.That(sup10!.MaxWorkers, Is.EqualTo(10));

        Assert.That(
            Program.TryParseWarmArguments(
                new[] { "--max-workers", "1" },
                out Program.WarmCliOptions? warm1,
                out string? warmErr,
                _ => null),
            Is.True,
            warmErr);
        Assert.That(warm1!.MaxWorkers, Is.EqualTo(1));

        Assert.That(
            Program.TryParseRunArguments(
                new[] { "--version", "2026", "--assembly", "a.dll", "--trx", "a.trx", "--max-workers", "6" },
                out OneShotRunOptions? run6,
                out string? runErr,
                _ => null),
            Is.True,
            runErr);
        Assert.That(run6!.MaxWorkers, Is.EqualTo(6));

        // 2. SYNTHETIC_WORKER_POOL_MAX_WORKERS fallback when --max-workers is omitted.
        string? EnvNine(string name) => name == "SYNTHETIC_WORKER_POOL_MAX_WORKERS" ? "9" : null;
        Assert.That(
            Program.TryParseSupervisorArguments(Array.Empty<string>(), out Program.SupervisorCliOptions? supEnv, out _, EnvNine),
            Is.True);
        Assert.That(supEnv!.MaxWorkers, Is.EqualTo(9));
        Assert.That(
            Program.TryParseWarmArguments(Array.Empty<string>(), out Program.WarmCliOptions? warmEnv, out _, EnvNine),
            Is.True);
        Assert.That(warmEnv!.MaxWorkers, Is.EqualTo(9));
        Assert.That(
            Program.TryParseRunArguments(new[] { "--version", "2026", "--assembly", "a.dll", "--trx", "a.trx" }, out OneShotRunOptions? runEnv, out _, EnvNine),
            Is.True);
        Assert.That(runEnv!.MaxWorkers, Is.EqualTo(9));

        // 3. CLI --max-workers overrides SYNTHETIC_WORKER_POOL_MAX_WORKERS.
        Assert.That(
            Program.TryParseRunArguments(
                new[] { "--version", "2026", "--assembly", "a.dll", "--trx", "a.trx", "--max-workers", "5" },
                out OneShotRunOptions? runOverride,
                out _,
                EnvNine),
            Is.True);
        Assert.That(runOverride!.MaxWorkers, Is.EqualTo(5));

        // 4. Out-of-range and non-numeric values rejected on CLI and env var.
        foreach (string bad in new[] { "0", "-1", "11", "abc" })
        {
            Assert.That(
                Program.TryParseSupervisorArguments(new[] { "--max-workers", bad }, out _, out string? e1, _ => null),
                Is.False);
            Assert.That(e1, Does.Contain("--max-workers"));

            Assert.That(
                Program.TryParseWarmArguments(new[] { "--max-workers", bad }, out _, out string? e2, _ => null),
                Is.False);
            Assert.That(e2, Does.Contain("--max-workers"));

            Assert.That(
                Program.TryParseRunArguments(new[] { "--version", "2026", "--assembly", "a.dll", "--trx", "a.trx", "--max-workers", bad }, out _, out string? e3, _ => null),
                Is.False);
            Assert.That(e3, Does.Contain("--max-workers"));

            string? EnvBad(string name) => name == "SYNTHETIC_WORKER_POOL_MAX_WORKERS" ? bad : null;
            Assert.That(
                Program.TryParseWarmArguments(Array.Empty<string>(), out _, out string? e4, EnvBad),
                Is.False);
            Assert.That(e4, Does.Contain("SYNTHETIC_WORKER_POOL_MAX_WORKERS"));
        }

        // 5. status and drain reject --max-workers.
        Assert.That(
            Program.TryParsePipeControlArguments(new[] { "--max-workers", "8" }, "status", out _, out _),
            Is.False);
        Assert.That(
            Program.TryParsePipeControlArguments(new[] { "--max-workers", "8" }, "drain", out _, out _),
            Is.False);
    }

    [Test]
    public async Task RunCliAsync_MaxWorkersOutOfRange_ReturnsExitCode2_InTextAndJsonModes()
    {
        foreach (string[] args in new[]
        {
            new[] { "--supervisor", "--max-workers", "0" },
            new[] { "--supervisor", "--max-workers", "11" },
            new[] { "warm", "--max-workers", "0" },
            new[] { "warm", "--max-workers", "11", "--json" },
            new[] { "run", "--version", "2026", "--assembly", "a.dll", "--trx", "a.trx", "--max-workers", "0" },
            new[] { "run", "--version", "2026", "--assembly", "a.dll", "--trx", "a.trx", "--max-workers", "11", "--json" }
        })
        {
            using var outWriter = new StringWriter();
            using var errWriter = new StringWriter();
            int exitCode = await Program.RunCliAsync(args, stdout: outWriter, stderr: errWriter);
            Assert.That(exitCode, Is.EqualTo(2), $"Expected exit code 2 for args: {string.Join(" ", args)}");
        }
    }

    [Test]
    public async Task RunCliAsync_WhenRunningSupervisorHasDifferentCap_EmitsWarningAndProceedsWithoutDisrupting()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "RevitWorkerPool_CapWarn_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            string revitDir = Path.Combine(tempRoot, "Autodesk", "Revit 2026");
            Directory.CreateDirectory(revitDir);
            File.WriteAllText(Path.Combine(revitDir, "Revit.exe"), string.Empty);

            string assemblyPath = TestAssemblyCompiler.CompileToFile(
                Path.Combine(tempRoot, "Synthetic.RevitTests.2026.dll"),
                "Synthetic.RevitTests.2026",
                """
                using NUnit.Framework;

                namespace CapWarnSuite
                {
                    [TestFixture]
                    public class SampleFixture
                    {
                        [Test]
                        public void SampleTest() { }
                    }
                }
                """);

            var discovery = new RevitInstallationDiscovery(
                () => new[] { Path.Combine(tempRoot, "Autodesk") },
                () => Array.Empty<UninstallRegistryEntry>());

            string pipeName = $"Synthetic_CapWarn_{Guid.NewGuid():N}";
            using var launcher = new StubWorkerProcessLauncher((server, _) =>
            {
                server.InitialIsBusy = false;
                server.ExecutionHandler = (req, emitTest, _) =>
                {
                    var t = new ricaun.NUnit.Models.TestModel
                    {
                        Name = "SampleTest",
                        FullName = "CapWarnSuite.SampleFixture.SampleTest",
                        Success = true,
                        Time = 5.0
                    };
                    emitTest(t);
                    return Task.FromResult(new ricaun.NUnit.Models.TestAssemblyModel
                    {
                        FileName = req.TestPathFile ?? string.Empty,
                        Name = "Synthetic.RevitTests.2026",
                        Success = true,
                        Tests = new System.Collections.Generic.List<ricaun.NUnit.Models.TestTypeModel>
                        {
                            new()
                            {
                                Name = "SampleFixture",
                                FullName = "CapWarnSuite.SampleFixture",
                                Success = true,
                                Tests = new System.Collections.Generic.List<ricaun.NUnit.Models.TestModel> { t }
                            }
                        }
                    });
                };
            });

            await using var supervisor = new WorkerPoolSupervisor(
                new SupervisorOptions
                {
                    PipeName = pipeName,
                    MaxActiveWorkers = 4,
                    CurrentLatestVersion = 2026,
                    IdleTimeout = TimeSpan.FromMinutes(25),
                    TimingCacheDirectory = Path.Combine(tempRoot, "timing_cache")
                },
                discovery,
                launcher,
                new RevitWorkerPool.Pipe.RevitPipeTestClientFactory(),
                bundleLeaseFactory: _ => new NoOpDisposable());
            await using var ipcServer = new SupervisorIpcServer(
                pipeName,
                (req, ct) => supervisor.HandleIpcRequestAsync(req, ct));
            supervisor.AttachIpcServer(ipcServer);
            supervisor.Start();
            ipcServer.Start();

            // warm --max-workers 10 against running cap=4 supervisor -> warns on stderr, proceeds with cap=4.
            using var warmOut = new StringWriter();
            using var warmErr = new StringWriter();
            int warmExit = await Program.RunCliAsync(
                new[] { "warm", "--version", "2026", "--count", "2", "--max-workers", "10", "--pipe-name", pipeName, "--no-shim", "--json" },
                stdout: warmOut,
                stderr: warmErr);
            Assert.That(warmExit, Is.EqualTo(0), warmErr.ToString());
            Assert.That(warmErr.ToString(), Does.Contain("activeCap=4"));
            Assert.That(warmErr.ToString(), Does.Contain("10"));
            WarmResult? warmRes = JsonConvert.DeserializeObject<WarmResult>(warmOut.ToString().Trim());
            Assert.That(warmRes!.Success, Is.True);
            Assert.That(warmRes.Status!.ActiveCap, Is.EqualTo(4));

            // run --max-workers 8 against running cap=4 supervisor -> warns on stderr, succeeds without disrupting cap=4.
            using var runOut = new StringWriter();
            using var runErr = new StringWriter();
            int runExit = await Program.RunCliAsync(
                new[]
                {
                    "run", "--version", "2026", "--assembly", assemblyPath,
                    "--trx", Path.Combine(tempRoot, "out", "capwarn.trx"),
                    "--max-workers", "8", "--pipe-name", pipeName, "--no-shim", "--json"
                },
                stdout: runOut,
                stderr: runErr);
            Assert.That(runExit, Is.EqualTo(0), runErr.ToString());
            Assert.That(runErr.ToString(), Does.Contain("activeCap=4"));
            Assert.That(runErr.ToString(), Does.Contain("8"));
            OneShotRunResult? runRes = JsonConvert.DeserializeObject<OneShotRunResult>(runOut.ToString().Trim());
            Assert.That(runRes!.Success, Is.True);
            Assert.That(supervisor.GetStatus().ActiveCap, Is.EqualTo(4));
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Test]
    public void WorkerPoolSupervisor_RejectsMaxActiveWorkersOutside1To10()
    {
        var emptyDiscovery = new RevitInstallationDiscovery(
            () => Array.Empty<string>(),
            () => Array.Empty<UninstallRegistryEntry>());
        using var launcher = new StubWorkerProcessLauncher();
        var clientFactory = new RevitWorkerPool.Pipe.RevitPipeTestClientFactory();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new WorkerPoolSupervisor(
                new SupervisorOptions { MaxActiveWorkers = 0 },
                emptyDiscovery,
                launcher,
                clientFactory));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new WorkerPoolSupervisor(
                new SupervisorOptions { MaxActiveWorkers = 11 },
                emptyDiscovery,
                launcher,
                clientFactory));
    }

    [Test]
    public void CreateWorkerLauncher_DefaultsToCleanDetours_AndDowngradesOnlyOnNoShimOrEnvZero()
    {
        IWorkerProcessLauncher defaultLauncher = Program.CreateWorkerLauncher(
            noShimFlag: false,
            getEnvironmentVariable: _ => null);
        Assert.That(defaultLauncher, Is.TypeOf<CleanDetoursProcessLauncher>());
        Assert.That(defaultLauncher.UsesCleanShim, Is.True);

        IWorkerProcessLauncher envOneLauncher = Program.CreateWorkerLauncher(
            noShimFlag: false,
            getEnvironmentVariable: name => name == "SYNTHETIC_CLEAN_SHIM" ? "1" : null);
        Assert.That(envOneLauncher, Is.TypeOf<CleanDetoursProcessLauncher>());
        Assert.That(envOneLauncher.UsesCleanShim, Is.True);

        IWorkerProcessLauncher noShimLauncher = Program.CreateWorkerLauncher(
            noShimFlag: true,
            getEnvironmentVariable: _ => null);
        Assert.That(noShimLauncher, Is.TypeOf<StandardProcessLauncher>());
        Assert.That(noShimLauncher.UsesCleanShim, Is.False);

        IWorkerProcessLauncher envZeroLauncher = Program.CreateWorkerLauncher(
            noShimFlag: false,
            getEnvironmentVariable: name => name == "SYNTHETIC_CLEAN_SHIM" ? "0" : null);
        Assert.That(envZeroLauncher, Is.TypeOf<StandardProcessLauncher>());
        Assert.That(envZeroLauncher.UsesCleanShim, Is.False);
    }

    [Test]
    public async Task RunCliAsync_ReturnsExitCode2_OnFailClosedShimError_InBothTextAndJsonModes()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "RevitWorkerPool_CliFailClosed_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            string revitDir = Path.Combine(tempRoot, "Autodesk", "Revit 2024");
            Directory.CreateDirectory(revitDir);
            File.WriteAllText(Path.Combine(revitDir, "Revit.exe"), string.Empty);

            string assemblyPath = Path.Combine(tempRoot, "Synthetic.RevitTests.2024.dll");
            File.WriteAllText(assemblyPath, string.Empty);
            string trxPath = Path.Combine(tempRoot, "out.trx");
            string missingShimPath = Path.Combine(tempRoot, "MissingRevitCleanShim.dll");

            var discovery = new RevitInstallationDiscovery(
                () => new[] { Path.Combine(tempRoot, "Autodesk") },
                () => Array.Empty<UninstallRegistryEntry>());

            IWorkerProcessLauncher FailingLauncherFactory(OneShotRunOptions _) =>
                new CleanDetoursProcessLauncher(shimDllPath: missingShimPath, launcherDllPath: missingShimPath);

            using var textOut = new StringWriter();
            using var textErr = new StringWriter();
            int textExit = await Program.RunCliAsync(
                new[] { "run", "--version", "2024", "--assembly", assemblyPath, "--trx", trxPath },
                FailingLauncherFactory,
                discovery,
                textOut,
                textErr);

            Assert.That(textExit, Is.EqualTo(2));
            Assert.That(textErr.ToString(), Does.Contain("MissingRevitCleanShim.dll"));

            using var jsonOut = new StringWriter();
            using var jsonErr = new StringWriter();
            int jsonExit = await Program.RunCliAsync(
                new[] { "run", "--version", "2024", "--assembly", assemblyPath, "--trx", trxPath, "--json" },
                FailingLauncherFactory,
                discovery,
                jsonOut,
                jsonErr);

            Assert.That(jsonExit, Is.EqualTo(2));
            OneShotRunResult? parsed = JsonConvert.DeserializeObject<OneShotRunResult>(jsonOut.ToString().Trim());
            Assert.That(parsed, Is.Not.Null);
            Assert.That(parsed!.Success, Is.False);
            Assert.That(parsed.ExitCode, Is.EqualTo(2));
            Assert.That(parsed.ShimEnabled, Is.True);
            Assert.That(parsed.Error, Does.Contain("MissingRevitCleanShim.dll"));
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Test]
    public async Task RunCliAsync_SupervisorMode_ForwardsCategoryAndFilter_AndShortCircuitsZeroMatch()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "RevitWorkerPool_CliSup_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            string revitDir = Path.Combine(tempRoot, "Autodesk", "Revit 2026");
            Directory.CreateDirectory(revitDir);
            File.WriteAllText(Path.Combine(revitDir, "Revit.exe"), string.Empty);

            string assemblyPath = TestAssemblyCompiler.CompileToFile(
                Path.Combine(tempRoot, "Synthetic.RevitTests.2026.dll"),
                "Synthetic.RevitTests.2026",
                """
                using NUnit.Framework;

                namespace CliSuite
                {
                    [TestFixture]
                    [Category("RevitSmoke")]
                    public class SmokeFixture
                    {
                        [Test]
                        public void PingTest() { }
                    }

                    [TestFixture]
                    [Category("Unit")]
                    public class UnitFixture
                    {
                        [Test]
                        public void OtherTest() { }
                    }
                }
                """);

            var discovery = new RevitInstallationDiscovery(
                () => new[] { Path.Combine(tempRoot, "Autodesk") },
                () => Array.Empty<UninstallRegistryEntry>());

            string pipeName = $"Synthetic_CliSupTest_{Guid.NewGuid():N}";
            using var launcher = new StubWorkerProcessLauncher((server, _) =>
            {
                server.InitialIsBusy = false;
                server.ExecutionHandler = (req, emitTest, _) =>
                {
                    var t = new ricaun.NUnit.Models.TestModel
                    {
                        Name = "PingTest",
                        FullName = "CliSuite.SmokeFixture.PingTest.PingTest",
                        Success = true,
                        Time = 5.0
                    };
                    emitTest(t);
                    return Task.FromResult(new ricaun.NUnit.Models.TestAssemblyModel
                    {
                        FileName = req.TestPathFile ?? string.Empty,
                        Name = "Synthetic.RevitTests.2026",
                        Success = true,
                        Tests = new System.Collections.Generic.List<ricaun.NUnit.Models.TestTypeModel>
                        {
                            new()
                            {
                                Name = "SmokeFixture",
                                FullName = "CliSuite.SmokeFixture",
                                Success = true,
                                Tests = new System.Collections.Generic.List<ricaun.NUnit.Models.TestModel> { t }
                            }
                        }
                    });
                };
            });

            await using var supervisor = new WorkerPoolSupervisor(
                new SupervisorOptions
                {
                    PipeName = pipeName,
                    CurrentLatestVersion = 2026,
                    IdleTimeout = TimeSpan.FromMinutes(25),
                    TimingCacheDirectory = Path.Combine(tempRoot, "timing_cache")
                },
                discovery,
                launcher,
                new RevitWorkerPool.Pipe.RevitPipeTestClientFactory(),
                bundleLeaseFactory: _ => new NoOpDisposable());
            await using var ipcServer = new SupervisorIpcServer(
                pipeName,
                (req, ct) => supervisor.HandleIpcRequestAsync(req, ct));
            supervisor.AttachIpcServer(ipcServer);
            supervisor.Start();
            ipcServer.Start();

            WarmResult warm = await supervisor.WarmAsync(2026, 1);
            Assert.That(warm.Success, Is.True);

            // 1. Zero-match via CLI supervisor IPC does not consume standby worker.
            string zeroTrx = Path.Combine(tempRoot, "out", "zero.trx");
            using var zeroOut = new StringWriter();
            using var zeroErr = new StringWriter();
            int zeroExit = await Program.RunCliAsync(
                new[]
                {
                    "run",
                    "--version", "2026",
                    "--assembly", assemblyPath,
                    "--trx", zeroTrx,
                    "--category", "MissingCat",
                    "--pipe-name", pipeName,
                    "--no-shim",
                    "--json"
                },
                stdout: zeroOut,
                stderr: zeroErr);

            Assert.That(zeroExit, Is.EqualTo(0));
            OneShotRunResult? zeroResult = JsonConvert.DeserializeObject<OneShotRunResult>(zeroOut.ToString().Trim());
            Assert.That(zeroResult, Is.Not.Null);
            Assert.That(zeroResult!.ZeroMatches, Is.True);
            Assert.That(zeroResult.WorkerIds, Is.Empty);
            Assert.That(supervisor.GetStatus().ReadyStandby, Is.EqualTo(1));

            // 2. Matching --category + --filter via CLI supervisor IPC runs exact expanded Ricaun filter.
            string matchTrx = Path.Combine(tempRoot, "out", "match.trx");
            using var matchOut = new StringWriter();
            using var matchErr = new StringWriter();
            int matchExit = await Program.RunCliAsync(
                new[]
                {
                    "run",
                    "--version", "2026",
                    "--assembly", assemblyPath,
                    "--trx", matchTrx,
                    "--category", "RevitSmoke",
                    "--filter", "SmokeFixture",
                    "--pipe-name", pipeName,
                    "--no-shim",
                    "--json"
                },
                stdout: matchOut,
                stderr: matchErr);

            Assert.That(matchExit, Is.EqualTo(0));
            OneShotRunResult? matchResult = JsonConvert.DeserializeObject<OneShotRunResult>(matchOut.ToString().Trim());
            Assert.That(matchResult, Is.Not.Null);
            Assert.That(matchResult!.Success, Is.True);
            Assert.That(matchResult.ZeroMatches, Is.False);
            Assert.That(matchResult.TotalTests, Is.EqualTo(1));

            string consumedId = matchResult.WorkerIds.Single();
            var consumedWorker = launcher.Workers.Single(w => w.WorkerId == consumedId);
            Assert.That(
                consumedWorker.Server.ReceivedRequests.Single().TestFilters,
                Is.EqualTo(new[] { "CliSuite.SmokeFixture.PingTest.*" }));
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [TestCase(true, "warm", true)]
    [TestCase(false, "warm", false)]
    [TestCase(true, "run", true)]
    [TestCase(false, "run", false)]
    public async Task RunCliAsync_SupervisorShimModeMismatch_FailsClosedInTextAndJsonModes_WithZeroWorkersLaunched(
        bool supervisorUsesShim,
        string command,
        bool callerNoShim)
    {
        string? priorCleanShimEnv = Environment.GetEnvironmentVariable("SYNTHETIC_CLEAN_SHIM");
        Environment.SetEnvironmentVariable("SYNTHETIC_CLEAN_SHIM", null);

        string tempRoot = Path.Combine(Path.GetTempPath(), "RevitWorkerPool_ShimMismatch_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            string revitDir = Path.Combine(tempRoot, "Autodesk", "Revit 2026");
            Directory.CreateDirectory(revitDir);
            File.WriteAllText(Path.Combine(revitDir, "Revit.exe"), string.Empty);

            string assemblyPath = Path.Combine(tempRoot, "Synthetic.RevitTests.2026.dll");
            File.WriteAllText(assemblyPath, string.Empty);
            string trxPath = Path.Combine(tempRoot, "out", "mismatch.trx");

            var discovery = new RevitInstallationDiscovery(
                () => new[] { Path.Combine(tempRoot, "Autodesk") },
                () => Array.Empty<UninstallRegistryEntry>());

            string pipeName = $"Synthetic_ShimMismatch_{Guid.NewGuid():N}";
            using var launcher = new StubWorkerProcessLauncher(usesCleanShim: supervisorUsesShim);
            await using var supervisor = new WorkerPoolSupervisor(
                new SupervisorOptions
                {
                    PipeName = pipeName,
                    CurrentLatestVersion = 2026,
                    IdleTimeout = TimeSpan.FromMinutes(25),
                    ShimEnabled = supervisorUsesShim,
                    TimingCacheDirectory = Path.Combine(tempRoot, "timing_cache")
                },
                discovery,
                launcher,
                new RevitWorkerPool.Pipe.RevitPipeTestClientFactory(),
                bundleLeaseFactory: _ => new NoOpDisposable());
            await using var ipcServer = new SupervisorIpcServer(
                pipeName,
                (req, ct) => supervisor.HandleIpcRequestAsync(req, ct));
            supervisor.AttachIpcServer(ipcServer);
            supervisor.Start();
            ipcServer.Start();

            int expectedSupervisorPid = Environment.ProcessId;
            string expectedActualToken = $"shimEnabled={supervisorUsesShim.ToString().ToLowerInvariant()}";
            string expectedRequestedToken = $"expectedShim={(!callerNoShim).ToString().ToLowerInvariant()}";
            string expectedRemediation = $"RevitWorkerPool drain --when-idle --pipe-name \"{pipeName}\"";
            string expectedFallbackHint = "--no-pool";

            string[] BuildArgs(bool json)
            {
                var argList = new System.Collections.Generic.List<string> { command, "--version", "2026" };
                if (command == "run")
                {
                    argList.Add("--assembly");
                    argList.Add(assemblyPath);
                    argList.Add("--trx");
                    argList.Add(trxPath);
                }

                argList.Add("--pipe-name");
                argList.Add(pipeName);
                if (callerNoShim)
                {
                    argList.Add("--no-shim");
                }

                if (json)
                {
                    argList.Add("--json");
                }

                return argList.ToArray();
            }

            // Text mode
            using var textOut = new StringWriter();
            using var textErr = new StringWriter();
            int textExit = await Program.RunCliAsync(BuildArgs(json: false), stdout: textOut, stderr: textErr);

            Assert.That(textExit, Is.EqualTo(2));
            string textErrOutput = textErr.ToString();
            Assert.That(textErrOutput, Does.Contain(pipeName));
            Assert.That(textErrOutput, Does.Contain(expectedSupervisorPid.ToString()));
            Assert.That(textErrOutput, Does.Contain(expectedActualToken));
            Assert.That(textErrOutput, Does.Contain(expectedRequestedToken));
            Assert.That(textErrOutput, Does.Contain(expectedRemediation));
            Assert.That(textErrOutput, Does.Contain(expectedFallbackHint));
            Assert.That(launcher.Workers.Count, Is.EqualTo(0));

            // JSON mode
            using var jsonOut = new StringWriter();
            using var jsonErr = new StringWriter();
            int jsonExit = await Program.RunCliAsync(BuildArgs(json: true), stdout: jsonOut, stderr: jsonErr);

            Assert.That(jsonExit, Is.EqualTo(2));
            string jsonLine = jsonOut.ToString().Trim();
            if (command == "warm")
            {
                WarmResult? warmResult = JsonConvert.DeserializeObject<WarmResult>(jsonLine);
                Assert.That(warmResult, Is.Not.Null);
                Assert.That(warmResult!.Success, Is.False);
                Assert.That(warmResult.ExitCode, Is.EqualTo(2));
                Assert.That(warmResult.Error, Does.Contain(pipeName));
                Assert.That(warmResult.Error, Does.Contain(expectedActualToken));
                Assert.That(warmResult.Error, Does.Contain(expectedRequestedToken));
                Assert.That(warmResult.Error, Does.Contain(expectedRemediation));
                Assert.That(warmResult.Error, Does.Contain(expectedFallbackHint));
            }
            else
            {
                OneShotRunResult? runResult = JsonConvert.DeserializeObject<OneShotRunResult>(jsonLine);
                Assert.That(runResult, Is.Not.Null);
                Assert.That(runResult!.Success, Is.False);
                Assert.That(runResult.ExitCode, Is.EqualTo(2));
                Assert.That(runResult.Error, Does.Contain(pipeName));
                Assert.That(runResult.Error, Does.Contain(expectedActualToken));
                Assert.That(runResult.Error, Does.Contain(expectedRequestedToken));
                Assert.That(runResult.Error, Does.Contain(expectedRemediation));
                Assert.That(runResult.Error, Does.Contain(expectedFallbackHint));
            }

            Assert.That(launcher.Workers.Count, Is.EqualTo(0));

            // status and drain remain mode-agnostic
            using var statusOut = new StringWriter();
            using var statusErr = new StringWriter();
            int statusExit = await Program.RunCliAsync(
                new[] { "status", "--pipe-name", pipeName, "--json" },
                stdout: statusOut,
                stderr: statusErr);
            Assert.That(statusExit, Is.EqualTo(0));

            using var drainOut = new StringWriter();
            using var drainErr = new StringWriter();
            int drainExit = await Program.RunCliAsync(
                new[] { "drain", "--pipe-name", pipeName, "--json" },
                stdout: drainOut,
                stderr: drainErr);
            Assert.That(drainExit, Is.EqualTo(0));
            Assert.That(launcher.Workers.Count, Is.EqualTo(0));
        }
        finally
        {
            Environment.SetEnvironmentVariable("SYNTHETIC_CLEAN_SHIM", priorCleanShimEnv);
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Test]
    public void TryParseRunArguments_AccumulatesMultipleCategoryFlags()
    {
        string[] args =
        {
            "--version", "2026",
            "--assembly", @"C:\temp\Synthetic.RevitTests.2026.dll",
            "--trx", @"C:\temp\out.trx",
            "--category", "RevitSmoke",
            "--category", "Unit",
            "--category", "SmokeExtra"
        };

        bool ok = Program.TryParseRunArguments(args, out OneShotRunOptions? options, out string? error);

        Assert.That(ok, Is.True, error);
        Assert.That(options!.Category, Is.EqualTo("RevitSmoke"));
        Assert.That(options.Categories, Is.EqualTo(new[] { "RevitSmoke", "Unit", "SmokeExtra" }));
    }

    [Test]
    public async Task RunCliAsync_SupervisorMode_ForwardsMultipleCategories_AndRunsAllMatchingFixtures()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "RevitWorkerPool_MultiCat_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            string revitDir = Path.Combine(tempRoot, "Autodesk", "Revit 2026");
            Directory.CreateDirectory(revitDir);
            File.WriteAllText(Path.Combine(revitDir, "Revit.exe"), string.Empty);

            string assemblyPath = TestAssemblyCompiler.CompileToFile(
                Path.Combine(tempRoot, "Synthetic.RevitTests.2026.dll"),
                "Synthetic.RevitTests.2026",
                """
                using NUnit.Framework;

                namespace MultiCatSuite
                {
                    [TestFixture]
                    [Category("CatA")]
                    public class FixtureA
                    {
                        [Test]
                        public void TestA() { }
                    }

                    [TestFixture]
                    [Category("CatB")]
                    public class FixtureB
                    {
                        [Test]
                        public void TestB() { }
                    }

                    [TestFixture]
                    [Category("CatC")]
                    public class FixtureC
                    {
                        [Test]
                        public void TestC() { }
                    }
                }
                """);

            var discovery = new RevitInstallationDiscovery(
                () => new[] { Path.Combine(tempRoot, "Autodesk") },
                () => Array.Empty<UninstallRegistryEntry>());

            string pipeName = $"Synthetic_MultiCat_{Guid.NewGuid():N}";
            using var launcher = new StubWorkerProcessLauncher((server, _) =>
            {
                server.InitialIsBusy = false;
                server.ExecutionHandler = (req, emitTest, _) =>
                {
                    var tests = (req.TestFilters ?? Array.Empty<string>()).Select(f =>
                    {
                        string name = f.TrimEnd('*').TrimEnd('.');
                        var model = new ricaun.NUnit.Models.TestModel
                        {
                            Name = name,
                            FullName = name,
                            Success = true,
                            Time = 1.0
                        };
                        emitTest(model);
                        return model;
                    }).ToList();

                    return Task.FromResult(new ricaun.NUnit.Models.TestAssemblyModel
                    {
                        FileName = req.TestPathFile ?? string.Empty,
                        Name = "Synthetic.RevitTests.2026",
                        Success = true,
                        Tests = new System.Collections.Generic.List<ricaun.NUnit.Models.TestTypeModel>
                        {
                            new()
                            {
                                Name = "MultiCat",
                                FullName = "MultiCatSuite",
                                Success = true,
                                Tests = tests
                            }
                        }
                    });
                };
            });

            await using var supervisor = new WorkerPoolSupervisor(
                new SupervisorOptions
                {
                    PipeName = pipeName,
                    CurrentLatestVersion = 2026,
                    IdleTimeout = TimeSpan.FromMinutes(25),
                    TimingCacheDirectory = Path.Combine(tempRoot, "timing_cache")
                },
                discovery,
                launcher,
                new RevitWorkerPool.Pipe.RevitPipeTestClientFactory(),
                bundleLeaseFactory: _ => new NoOpDisposable());
            await using var ipcServer = new SupervisorIpcServer(
                pipeName,
                (req, ct) => supervisor.HandleIpcRequestAsync(req, ct));
            supervisor.AttachIpcServer(ipcServer);
            supervisor.Start();
            ipcServer.Start();

            string trxPath = Path.Combine(tempRoot, "out", "multicat.trx");
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            int exitCode = await Program.RunCliAsync(
                new[]
                {
                    "run",
                    "--version", "2026",
                    "--assembly", assemblyPath,
                    "--trx", trxPath,
                    "--category", "CatA",
                    "--category", "CatB",
                    "--pipe-name", pipeName,
                    "--no-shim",
                    "--json"
                },
                stdout: stdout,
                stderr: stderr);

            Assert.That(exitCode, Is.EqualTo(0));
            OneShotRunResult? result = JsonConvert.DeserializeObject<OneShotRunResult>(stdout.ToString().Trim());
            Assert.That(result, Is.Not.Null);
            Assert.That(result!.Success, Is.True);
            Assert.That(result.TotalTests, Is.EqualTo(2));

            var filters = launcher.Workers
                .SelectMany(w => w.Server.ReceivedRequests)
                .SelectMany(r => r.TestFilters ?? Array.Empty<string>())
                .ToArray();
            Assert.That(filters, Is.EquivalentTo(new[] { "MultiCatSuite.FixtureA.TestA.*", "MultiCatSuite.FixtureB.TestB.*" }));
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Test]
    public async Task RunCliAsync_OneShotGeneralException_ReturnsExitCode2_InTextAndJsonModes()
    {
        var emptyDiscovery = new RevitInstallationDiscovery(
            () => Array.Empty<string>(),
            () => Array.Empty<UninstallRegistryEntry>());

        IWorkerProcessLauncher ThrowingLauncherFactory(OneShotRunOptions _) =>
            throw new InvalidOperationException("Simulated one-shot infrastructure failure.");

        using var textOut = new StringWriter();
        using var textErr = new StringWriter();
        int textExit = await Program.RunCliAsync(
            new[] { "run", "--version", "2026", "--assembly", "a.dll", "--trx", "a.trx", "--one-shot" },
            ThrowingLauncherFactory,
            emptyDiscovery,
            textOut,
            textErr);

        Assert.That(textExit, Is.EqualTo(2));
        Assert.That(textErr.ToString(), Does.Contain("Simulated one-shot infrastructure failure."));

        using var jsonOut = new StringWriter();
        using var jsonErr = new StringWriter();
        int jsonExit = await Program.RunCliAsync(
            new[] { "run", "--version", "2026", "--assembly", "a.dll", "--trx", "a.trx", "--one-shot", "--json" },
            ThrowingLauncherFactory,
            emptyDiscovery,
            jsonOut,
            jsonErr);

        Assert.That(jsonExit, Is.EqualTo(2));
        OneShotRunResult? parsed = JsonConvert.DeserializeObject<OneShotRunResult>(jsonOut.ToString().Trim());
        Assert.That(parsed, Is.Not.Null);
        Assert.That(parsed!.Success, Is.False);
        Assert.That(parsed.ExitCode, Is.EqualTo(2));
        Assert.That(parsed.Error, Does.Contain("Simulated one-shot infrastructure failure."));
    }

    [Test]
    public async Task RunCliAsync_DrainModes_PlainRefusesActive_WhenIdleWaits_AndForceCancelsImmediately()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "RevitWorkerPool_CliDrainModes_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            string revitDir = Path.Combine(tempRoot, "Autodesk", "Revit 2026");
            Directory.CreateDirectory(revitDir);
            File.WriteAllText(Path.Combine(revitDir, "Revit.exe"), string.Empty);

            string assemblyPath = TestAssemblyCompiler.CompileToFile(
                Path.Combine(tempRoot, "Synthetic.RevitTests.2026.dll"),
                "Synthetic.RevitTests.2026",
                """
                using NUnit.Framework;

                namespace DrainCliSuite
                {
                    [TestFixture]
                    public class SampleFixture
                    {
                        [Test]
                        public void SampleTest() { }
                    }
                }
                """);

            var discovery = new RevitInstallationDiscovery(
                () => new[] { Path.Combine(tempRoot, "Autodesk") },
                () => Array.Empty<UninstallRegistryEntry>());

            var execStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseExec = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            string pipeName = $"Synthetic_CliDrainModes_{Guid.NewGuid():N}";

            using var launcher = new StubWorkerProcessLauncher((server, _) =>
            {
                server.InitialIsBusy = false;
                server.ExecutionHandler = async (req, emitTest, ct) =>
                {
                    execStarted.TrySetResult(true);
                    await releaseExec.Task.WaitAsync(ct).ConfigureAwait(false);
                    var t = new ricaun.NUnit.Models.TestModel
                    {
                        Name = "SampleTest",
                        FullName = "DrainCliSuite.SampleFixture.SampleTest",
                        Success = true,
                        Time = 5.0
                    };
                    emitTest(t);
                    return new ricaun.NUnit.Models.TestAssemblyModel
                    {
                        FileName = req.TestPathFile ?? string.Empty,
                        Name = "Synthetic.RevitTests.2026",
                        Success = true,
                        Tests = new System.Collections.Generic.List<ricaun.NUnit.Models.TestTypeModel>
                        {
                            new()
                            {
                                Name = "SampleFixture",
                                FullName = "DrainCliSuite.SampleFixture",
                                Success = true,
                                Tests = new System.Collections.Generic.List<ricaun.NUnit.Models.TestModel> { t }
                            }
                        }
                    };
                };
            });

            await using var supervisor = new WorkerPoolSupervisor(
                new SupervisorOptions
                {
                    PipeName = pipeName,
                    CurrentLatestVersion = 2026,
                    IdleTimeout = TimeSpan.FromMinutes(25),
                    DrainGraceTimeout = TimeSpan.FromSeconds(30),
                    TimingCacheDirectory = Path.Combine(tempRoot, "timing_cache")
                },
                discovery,
                launcher,
                new RevitWorkerPool.Pipe.RevitPipeTestClientFactory(),
                bundleLeaseFactory: _ => new NoOpDisposable());
            await using var ipcServer = new SupervisorIpcServer(
                pipeName,
                (req, ct) => supervisor.HandleIpcRequestAsync(req, ct));
            supervisor.AttachIpcServer(ipcServer);
            supervisor.Start();
            ipcServer.Start();

            // 1. Conflicting flags --when-idle + --force fail with exit code 2.
            using var conflictOut = new StringWriter();
            using var conflictErr = new StringWriter();
            int conflictExit = await Program.RunCliAsync(
                new[] { "drain", "--when-idle", "--force", "--pipe-name", pipeName, "--json" },
                stdout: conflictOut,
                stderr: conflictErr);
            Assert.That(conflictExit, Is.EqualTo(2));

            // Start an active run.
            Task<int> activeRunCliTask = Program.RunCliAsync(
                new[]
                {
                    "run", "--version", "2026", "--assembly", assemblyPath,
                    "--trx", Path.Combine(tempRoot, "out", "active.trx"),
                    "--pipe-name", pipeName, "--no-shim", "--json"
                },
                stdout: new StringWriter(),
                stderr: new StringWriter());
            await execStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));

            // 2. Plain drain refuses with exit code 2 and structured counts in JSON and text.
            using var plainJsonOut = new StringWriter();
            using var plainJsonErr = new StringWriter();
            int plainJsonExit = await Program.RunCliAsync(
                new[] { "drain", "--pipe-name", pipeName, "--json" },
                stdout: plainJsonOut,
                stderr: plainJsonErr);
            Assert.That(plainJsonExit, Is.EqualTo(2));
            DrainResult? refused = JsonConvert.DeserializeObject<DrainResult>(plainJsonOut.ToString().Trim());
            Assert.That(refused, Is.Not.Null);
            Assert.That(refused!.Success, Is.False);
            Assert.That(refused.ExitCode, Is.EqualTo(2));
            Assert.That(refused.ActiveRuns, Is.EqualTo(1));
            Assert.That(refused.QueuedRuns, Is.EqualTo(0));
            Assert.That(refused.ActiveWarmCalls, Is.EqualTo(0));

            using var plainTextOut = new StringWriter();
            using var plainTextErr = new StringWriter();
            int plainTextExit = await Program.RunCliAsync(
                new[] { "drain", "--pipe-name", pipeName },
                stdout: plainTextOut,
                stderr: plainTextErr);
            Assert.That(plainTextExit, Is.EqualTo(2));
            Assert.That(plainTextErr.ToString(), Does.Contain("activeRuns=1"));

            // 3. drain --when-idle waits for active run, rejects new run, and completes cleanly on release.
            using var whenIdleOut = new StringWriter();
            using var whenIdleErr = new StringWriter();
            Task<int> whenIdleTask = Program.RunCliAsync(
                new[] { "drain", "--when-idle", "--pipe-name", pipeName, "--json" },
                stdout: whenIdleOut,
                stderr: whenIdleErr);

            DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
            while (!supervisor.IsDrainingWhenIdle && DateTime.UtcNow < deadline)
            {
                await Task.Delay(15);
            }
            Assert.That(supervisor.IsDrainingWhenIdle, Is.True);

            using var rejectedOut = new StringWriter();
            using var rejectedErr = new StringWriter();
            int rejectedExit = await Program.RunCliAsync(
                new[]
                {
                    "run", "--version", "2026", "--assembly", assemblyPath,
                    "--trx", Path.Combine(tempRoot, "out", "rejected.trx"),
                    "--pipe-name", pipeName, "--no-shim", "--json"
                },
                stdout: rejectedOut,
                stderr: rejectedErr);
            Assert.That(rejectedExit, Is.EqualTo(2));
            OneShotRunResult? rejectedRun = JsonConvert.DeserializeObject<OneShotRunResult>(rejectedOut.ToString().Trim());
            Assert.That(rejectedRun!.Error, Does.Contain("Supervisor is draining when idle; retry after drain completes or run --no-pool"));

            releaseExec.TrySetResult(true);
            Assert.That(await activeRunCliTask.WaitAsync(TimeSpan.FromSeconds(5)), Is.EqualTo(0));
            Assert.That(await whenIdleTask.WaitAsync(TimeSpan.FromSeconds(5)), Is.EqualTo(0));
            DrainResult? whenIdleResult = JsonConvert.DeserializeObject<DrainResult>(whenIdleOut.ToString().Trim());
            Assert.That(whenIdleResult!.Success, Is.True);
            Assert.That(whenIdleResult.Status!.TotalResident, Is.EqualTo(0));
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Test]
    public async Task RunCliAsync_DrainForce_CancelsActiveRunImmediatelyWithZeroGraceWait()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "RevitWorkerPool_CliForceDrain_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            string revitDir = Path.Combine(tempRoot, "Autodesk", "Revit 2026");
            Directory.CreateDirectory(revitDir);
            File.WriteAllText(Path.Combine(revitDir, "Revit.exe"), string.Empty);

            string assemblyPath = TestAssemblyCompiler.CompileToFile(
                Path.Combine(tempRoot, "Synthetic.RevitTests.2026.dll"),
                "Synthetic.RevitTests.2026",
                """
                using NUnit.Framework;

                namespace ForceDrainCliSuite
                {
                    [TestFixture]
                    public class SampleFixture
                    {
                        [Test]
                        public void SampleTest() { }
                    }
                }
                """);

            var discovery = new RevitInstallationDiscovery(
                () => new[] { Path.Combine(tempRoot, "Autodesk") },
                () => Array.Empty<UninstallRegistryEntry>());

            var execStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            string pipeName = $"Synthetic_CliForceDrain_{Guid.NewGuid():N}";

            using var launcher = new StubWorkerProcessLauncher((server, _) =>
            {
                server.InitialIsBusy = false;
                server.ExecutionHandler = async (req, _, ct) =>
                {
                    execStarted.TrySetResult(true);
                    await Task.Delay(TimeSpan.FromSeconds(20), ct).ConfigureAwait(false);
                    return new ricaun.NUnit.Models.TestAssemblyModel
                    {
                        FileName = req.TestPathFile ?? string.Empty,
                        Name = "Synthetic.RevitTests.2026",
                        Success = true
                    };
                };
            });

            await using var supervisor = new WorkerPoolSupervisor(
                new SupervisorOptions
                {
                    PipeName = pipeName,
                    CurrentLatestVersion = 2026,
                    IdleTimeout = TimeSpan.FromMinutes(25),
                    DrainGraceTimeout = TimeSpan.FromSeconds(30),
                    TimingCacheDirectory = Path.Combine(tempRoot, "timing_cache")
                },
                discovery,
                launcher,
                new RevitWorkerPool.Pipe.RevitPipeTestClientFactory(),
                bundleLeaseFactory: _ => new NoOpDisposable());
            await using var ipcServer = new SupervisorIpcServer(
                pipeName,
                (req, ct) => supervisor.HandleIpcRequestAsync(req, ct));
            supervisor.AttachIpcServer(ipcServer);
            supervisor.Start();
            ipcServer.Start();

            Task<int> slowRunTask = Program.RunCliAsync(
                new[]
                {
                    "run", "--version", "2026", "--assembly", assemblyPath,
                    "--trx", Path.Combine(tempRoot, "out", "slow.trx"),
                    "--pipe-name", pipeName, "--no-shim", "--json"
                },
                stdout: new StringWriter(),
                stderr: new StringWriter());
            await execStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));

            var sw = System.Diagnostics.Stopwatch.StartNew();
            using var forceOut = new StringWriter();
            using var forceErr = new StringWriter();
            int forceExit = await Program.RunCliAsync(
                new[] { "drain", "--force", "--pipe-name", pipeName, "--json" },
                stdout: forceOut,
                stderr: forceErr);
            sw.Stop();

            Assert.That(forceExit, Is.EqualTo(0));
            Assert.That(sw.Elapsed, Is.LessThan(TimeSpan.FromSeconds(3)));
            DrainResult? forceResult = JsonConvert.DeserializeObject<DrainResult>(forceOut.ToString().Trim());
            Assert.That(forceResult!.Success, Is.True);
            Assert.That(forceResult.Status!.TotalResident, Is.EqualTo(0));
            Assert.That(await slowRunTask.WaitAsync(TimeSpan.FromSeconds(3)), Is.EqualTo(2));
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    private sealed class NoOpDisposable : IDisposable
    {
        public void Dispose()
        {
        }
    }
}
