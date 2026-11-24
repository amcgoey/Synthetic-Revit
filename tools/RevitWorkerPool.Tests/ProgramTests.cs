using System;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json;
using NUnit.Framework;
using RevitWorkerPool.Discovery;
using RevitWorkerPool.Execution;
using RevitWorkerPool.Launching;

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
            "--one-shot",
            "--no-shim",
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
        Assert.That(options.OneShot, Is.True);
        Assert.That(options.NoShim, Is.True);
        Assert.That(options.JsonOutput, Is.True);
        Assert.That(options.Diagnostics, Is.True);
    }

    [Test]
    public void TryParseRunArguments_DefaultsNoShimToFalse()
    {
        string[] args =
        {
            "--version", "2026",
            "--assembly", @"C:\temp\Synthetic.RevitTests.2026.dll",
            "--trx", @"C:\temp\out.trx"
        };

        bool ok = Program.TryParseRunArguments(args, out OneShotRunOptions? options, out string? error);

        Assert.That(ok, Is.True, error);
        Assert.That(options!.NoShim, Is.False);
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
