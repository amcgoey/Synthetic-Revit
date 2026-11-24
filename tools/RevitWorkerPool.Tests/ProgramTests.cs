using NUnit.Framework;
using RevitWorkerPool.Execution;
using RevitWorkerPool.Ipc;
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
    public void TryParseRunArguments_DefaultsToSupervisorMode_AndDefaultPipeAndTtl()
    {
        string[] args =
        {
            "--version", "2026",
            "--assembly", "a.dll",
            "--trx", "a.trx"
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
            Program.TryParseWarmArguments(System.Array.Empty<string>(), out Program.WarmCliOptions? defaults, out string? errDefault),
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
}
