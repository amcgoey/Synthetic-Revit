using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;
using RevitWorkerPool.Containment;
using RevitWorkerPool.Launching;

namespace RevitWorkerPool.Tests;

[TestFixture]
public class JobObjectContainerTests
{
    private string _tempRoot = string.Empty;

    [SetUp]
    public void SetUp()
    {
        _tempRoot = Path.Combine(
            Path.GetTempPath(),
            "RevitWorkerPool_JobObjectTests",
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

    [Test]
    public void ExtendedLimitStructSize_MatchesWin32X64Layout()
    {
        Assert.That(JobObjectContainer.ExtendedLimitStructSize, Is.EqualTo(144));
    }

    [Test]
    public void Dispose_KillsAssignedChildProcess_ViaKillOnJobClose()
    {
        string cmdExe = Path.Combine(Environment.SystemDirectory, "cmd.exe");
        var psi = new ProcessStartInfo
        {
            FileName = cmdExe,
            Arguments = "/c ping -n 60 127.0.0.1 >nul",
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi)!;
        Assert.That(process.HasExited, Is.False);

        using (var job = new JobObjectContainer())
        {
            job.AssignProcess(process);
            Assert.That(process.HasExited, Is.False);
        }

        // JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE must terminate the assigned process when the handle closes.
        bool exited = process.WaitForExit(3000);
        Assert.That(exited, Is.True, "Assigned child process should terminate immediately on JobObjectContainer.Dispose().");
    }

    [Test]
    public async Task StandardProcessLauncher_PropagatesWorkerEnvironment_AndInvokesPreResumeHook()
    {
        string cmdExe = Path.Combine(Environment.SystemDirectory, "cmd.exe");
        string envDumpFile = Path.Combine(_tempRoot, "worker_env.txt");
        string expectedWorkerId = "w_test_prop_12345";
        string expectedProjectRoot = Path.Combine(_tempRoot, "RepoRoot");
        Directory.CreateDirectory(expectedProjectRoot);

        int? hookPid = null;
        var launcher = new StandardProcessLauncher();
        var request = new WorkerLaunchRequest
        {
            RevitVersion = 2026,
            ExecutablePath = cmdExe,
            Arguments = $"/c \"echo %SYNTHETIC_TEST_WORKER%^|%SYNTHETIC_WORKER_ID%^|%SYNTHETIC_PROJECT_ROOT% > \"{envDumpFile}\"\"",
            WorkerId = expectedWorkerId,
            ProjectRoot = expectedProjectRoot,
            OnProcessCreatedBeforeResume = pid => hookPid = pid
        };

        await using LaunchedWorkerProcess worker = await launcher.LaunchAsync(request);
        Assert.That(hookPid, Is.EqualTo(worker.ProcessId));
        Assert.That(worker.PipeName, Is.EqualTo($"ricaun.cmd.{worker.ProcessId}"));

        await worker.WaitForExitAsync();
        Assert.That(worker.HasExited, Is.True);
        Assert.That(worker.ExitCode, Is.EqualTo(0));

        string content = File.ReadAllText(envDumpFile).Trim();
        Assert.That(content, Is.EqualTo($"1|{expectedWorkerId}|{expectedProjectRoot}"));
    }

    [Test]
    public async Task StandardProcessLauncher_TerminateAsync_KillsRunningWorkerProcessTree()
    {
        string cmdExe = Path.Combine(Environment.SystemDirectory, "cmd.exe");
        var launcher = new StandardProcessLauncher();
        var request = new WorkerLaunchRequest
        {
            RevitVersion = 2026,
            ExecutablePath = cmdExe,
            Arguments = "/c ping -n 60 127.0.0.1 >nul",
            WorkerId = "w_terminate_test",
            ProjectRoot = _tempRoot
        };

        LaunchedWorkerProcess worker = await launcher.LaunchAsync(request);
        try
        {
            Assert.That(worker.HasExited, Is.False);
            await worker.TerminateAsync();
            Assert.That(worker.HasExited, Is.True);
        }
        finally
        {
            worker.Dispose();
        }
    }
}
