using NUnit.Framework;
using RevitWorkerPool.Execution;

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
        Assert.That(options.JsonOutput, Is.True);
        Assert.That(options.Diagnostics, Is.True);
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
}
