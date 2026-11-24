using System;
using System.IO;
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
            "--timeout-seconds", "90",
            "--boot-timeout-seconds", "45",
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
        Assert.That(options.ExecutionTimeout.TotalSeconds, Is.EqualTo(90));
        Assert.That(options.BootTimeout.TotalSeconds, Is.EqualTo(45));
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
            Program.TryParseRunArguments(new[] { "--version", "2026", "--assembly", "a.dll", "--trx", "a.trx", "--idle-ttl-minutes", "19" }, out _, out string? err6),
            Is.False);
        Assert.That(err6, Does.Contain("--idle-ttl-minutes"));

        Assert.That(
            Program.TryParseRunArguments(new[] { "--version", "2026", "--assembly", "a.dll", "--trx", "a.trx", "--idle-ttl-minutes", "31" }, out _, out string? err7),
            Is.False);
        Assert.That(err7, Does.Contain("--idle-ttl-minutes"));
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
}
