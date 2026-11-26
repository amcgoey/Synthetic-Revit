using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Newtonsoft.Json;
using NUnit.Framework;
using RevitWorkerPool.Discovery;
using RevitWorkerPool.Execution;
using RevitWorkerPool.Ipc;
using RevitWorkerPool.Launching;

namespace RevitWorkerPool.Tests;

[TestFixture]
public class DetachedSupervisorLauncherTests
{
    [Test]
    public void SupervisorLaunchCommandLine_ResolvesAllExplicitSupervisorArguments_ForExeAndDotnetHosts()
    {
        var options = new SupervisorClientLaunchOptions
        {
            PipeName = "Custom Pipe",
            IdleTtlMinutes = 27,
            MaxWorkers = 8,
            NoShim = true,
            ProjectRoot = @"C:\my repo\worktree\",
            Diagnostics = true
        };

        SupervisorLaunchCommand exeCommand = SupervisorLaunchCommandLine.Resolve(
            options,
            processPathOverride: @"C:\tools\RevitWorkerPool.exe",
            entryAssemblyPathOverride: null);

        Assert.That(exeCommand.ExecutablePath, Is.EqualTo(@"C:\tools\RevitWorkerPool.exe"));
        Assert.That(exeCommand.WorkingDirectory, Is.EqualTo(@"C:\tools"));
        Assert.That(exeCommand.Arguments, Does.StartWith("--supervisor "));
        Assert.That(exeCommand.Arguments, Does.Contain("--pipe-name \"Custom Pipe\""));
        Assert.That(exeCommand.Arguments, Does.Contain("--idle-ttl-minutes 27"));
        Assert.That(exeCommand.Arguments, Does.Contain("--max-workers 8"));
        Assert.That(exeCommand.Arguments, Does.Contain("--no-shim"));
        Assert.That(exeCommand.Arguments, Does.Contain("--project-root \"C:\\my repo\\worktree\\\\\""));
        Assert.That(exeCommand.Arguments, Does.Contain("--diagnostics"));

        SupervisorLaunchCommand dotnetCommand = SupervisorLaunchCommandLine.Resolve(
            options,
            processPathOverride: @"C:\Program Files\dotnet\dotnet.exe",
            entryAssemblyPathOverride: @"C:\tools\RevitWorkerPool.dll");

        Assert.That(dotnetCommand.ExecutablePath, Is.EqualTo(@"C:\Program Files\dotnet\dotnet.exe"));
        Assert.That(dotnetCommand.Arguments, Does.StartWith(@"C:\tools\RevitWorkerPool.dll --supervisor "));
        Assert.That(dotnetCommand.CommandLine, Does.StartWith("\"C:\\Program Files\\dotnet\\dotnet.exe\" "));
    }

    [Test]
    public void ScheduledTaskXml_ConfiguresInteractiveTokenParallelNoBatteryHiddenConhostAndExplicitArgs()
    {
        string xml = ScheduledTaskSupervisorLauncher.BuildTaskXml(
            conhostPath: @"C:\Windows\System32\conhost.exe",
            supervisorExecutablePath: @"C:\tools\Revit Worker Pool\RevitWorkerPool.exe",
            workingDirectory: @"C:\tools\Revit Worker Pool");

        XDocument doc = XDocument.Parse(xml);
        XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";

        Assert.That(doc.Root?.Name, Is.EqualTo(ns + "Task"));
        Assert.That(doc.Descendants(ns + "LogonType").Single().Value, Is.EqualTo("InteractiveToken"));
        Assert.That(doc.Descendants(ns + "RunLevel").Single().Value, Is.EqualTo("LeastPrivilege"));
        Assert.That(doc.Descendants(ns + "MultipleInstancesPolicy").Single().Value, Is.EqualTo("Parallel"));
        Assert.That(doc.Descendants(ns + "DisallowStartIfOnBatteries").Single().Value, Is.EqualTo("false"));
        Assert.That(doc.Descendants(ns + "StopIfGoingOnBatteries").Single().Value, Is.EqualTo("false"));
        Assert.That(doc.Descendants(ns + "AllowStartOnDemand").Single().Value, Is.EqualTo("true"));
        Assert.That(doc.Descendants(ns + "Enabled").Single().Value, Is.EqualTo("true"));
        Assert.That(doc.Descendants(ns + "Hidden").Single().Value, Is.EqualTo("true"));
        Assert.That(doc.Descendants(ns + "ExecutionTimeLimit").Single().Value, Is.EqualTo("PT0S"));
        Assert.That(doc.Descendants(ns + "Command").Single().Value, Is.EqualTo(@"C:\Windows\System32\conhost.exe"));
        Assert.That(
            doc.Descendants(ns + "Arguments").Single().Value,
            Is.EqualTo("--headless \"C:\\tools\\Revit Worker Pool\\RevitWorkerPool.exe\" $(Arg0)"));
        Assert.That(doc.Descendants(ns + "WorkingDirectory").Single().Value, Is.EqualTo(@"C:\tools\Revit Worker Pool"));
    }

    [Test]
    public void ScheduledTaskSupervisorLauncher_AutoRegistersWhenMissing_SkipsWhenCurrent_AndUpdatesWhenDrifted()
    {
        var fakeService = new FakeScheduledTaskService();
        var launcher = new ScheduledTaskSupervisorLauncher(
            fakeService,
            conhostPathOverride: @"C:\Windows\System32\conhost.exe",
            processPathOverride: @"C:\tools\RevitWorkerPool.exe");

        var options = new SupervisorClientLaunchOptions
        {
            PipeName = SupervisorIpcConstants.DefaultPipeName,
            IdleTtlMinutes = 25,
            MaxWorkers = 8,
            NoShim = true,
            ProjectRoot = @"C:\repo"
        };

        // 1. Missing -> registers and runs with explicit resolved args.
        SupervisorLaunchResult first = launcher.Launch(options);
        Assert.That(first.Method, Is.EqualTo(SupervisorLaunchMethod.ScheduledTask));
        Assert.That(fakeService.RegisterCount, Is.EqualTo(1));
        Assert.That(fakeService.RunCalls.Count, Is.EqualTo(1));
        Assert.That(fakeService.LastFolderPath, Is.EqualTo(ScheduledTaskSupervisorLauncher.TaskFolderPath));
        Assert.That(fakeService.LastTaskName, Is.EqualTo(ScheduledTaskSupervisorLauncher.TaskName));
        Assert.That(fakeService.RunCalls[0], Does.Contain("--supervisor"));
        Assert.That(fakeService.RunCalls[0], Does.Contain("--max-workers 8"));
        Assert.That(fakeService.RunCalls[0], Does.Contain("--no-shim"));
        Assert.That(fakeService.RunCalls[0], Does.Contain("--idle-ttl-minutes 25"));
        Assert.That(fakeService.RunCalls[0], Does.Contain($"--pipe-name {SupervisorIpcConstants.DefaultPipeName}"));
        Assert.That(fakeService.RunCalls[0], Does.Contain("--project-root C:\\repo"));

        // 2. Existing task matches (even with Task Scheduler-injected Date/Author/UserId metadata) -> no re-register.
        fakeService.StoredXml = fakeService.StoredXml!.Replace(
            "<Principals>",
            "<RegistrationInfo><Date>2026-01-01T00:00:00</Date><URI>\\Synthetic\\RevitWorkerPoolSupervisor</URI></RegistrationInfo><Principals>");
        SupervisorLaunchResult second = launcher.Launch(options);
        Assert.That(second.Method, Is.EqualTo(SupervisorLaunchMethod.ScheduledTask));
        Assert.That(fakeService.RegisterCount, Is.EqualTo(1));
        Assert.That(fakeService.RunCalls.Count, Is.EqualTo(2));

        // 3. Path or settings drift -> re-registers and runs.
        fakeService.StoredXml = fakeService.StoredXml.Replace(@"C:\tools\RevitWorkerPool.exe", @"C:\old\RevitWorkerPool.exe");
        SupervisorLaunchResult third = launcher.Launch(options);
        Assert.That(third.Method, Is.EqualTo(SupervisorLaunchMethod.ScheduledTask));
        Assert.That(fakeService.RegisterCount, Is.EqualTo(2));
        Assert.That(fakeService.RunCalls.Count, Is.EqualTo(3));

        fakeService.StoredXml = fakeService.StoredXml.Replace("<DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>", "<DisallowStartIfOnBatteries>true</DisallowStartIfOnBatteries>");
        SupervisorLaunchResult fourth = launcher.Launch(options);
        Assert.That(fourth.Method, Is.EqualTo(SupervisorLaunchMethod.ScheduledTask));
        Assert.That(fakeService.RegisterCount, Is.EqualTo(3));
        Assert.That(fakeService.RunCalls.Count, Is.EqualTo(4));
    }

    [Test]
    public void DetachedSupervisorLauncher_DefaultPipe_UsesScheduledTaskPrimary_AndDoesNotInvokeBreakaway()
    {
        var sched = new StubSupervisorLauncher((_, _) =>
            new SupervisorLaunchResult(SupervisorLaunchMethod.ScheduledTask, 1001, "sched ok"));
        var breakaway = new StubSupervisorLauncher((_, _) =>
            new SupervisorLaunchResult(SupervisorLaunchMethod.BreakawayProcess, 2002, "breakaway ok"));

        var composite = new DetachedSupervisorLauncher(sched, breakaway);
        SupervisorLaunchResult result = composite.Launch(new SupervisorClientLaunchOptions
        {
            PipeName = SupervisorIpcConstants.DefaultPipeName
        });

        Assert.That(result.Method, Is.EqualTo(SupervisorLaunchMethod.ScheduledTask));
        Assert.That(sched.CallCount, Is.EqualTo(1));
        Assert.That(breakaway.CallCount, Is.EqualTo(0));
    }

    [Test]
    public void DetachedSupervisorLauncher_CustomPipe_RoutesDirectlyToBreakaway_WithoutTouchingScheduledTask()
    {
        var sched = new StubSupervisorLauncher((_, _) =>
            throw new AssertionException("Scheduled Task adapter must not be called for custom pipe names."));
        var breakaway = new StubSupervisorLauncher((_, _) =>
            new SupervisorLaunchResult(SupervisorLaunchMethod.BreakawayProcess, 2002, "breakaway ok"));

        var composite = new DetachedSupervisorLauncher(sched, breakaway);
        SupervisorLaunchResult result = composite.Launch(new SupervisorClientLaunchOptions
        {
            PipeName = "CustomTestPipe_123"
        });

        Assert.That(result.Method, Is.EqualTo(SupervisorLaunchMethod.BreakawayProcess));
        Assert.That(result.ProcessId, Is.EqualTo(2002));
        Assert.That(sched.CallCount, Is.EqualTo(0));
        Assert.That(breakaway.CallCount, Is.EqualTo(1));
    }

    [Test]
    public void DetachedSupervisorLauncher_DefaultPipe_FallsBackToBreakaway_WhenScheduledTaskFails()
    {
        var logs = new List<string>();
        var sched = new StubSupervisorLauncher((_, _) =>
            throw new SupervisorLaunchException("Task Scheduler COM unavailable."));
        var breakaway = new StubSupervisorLauncher((_, _) =>
            new SupervisorLaunchResult(SupervisorLaunchMethod.BreakawayProcess, 3003, "breakaway fallback ok"));

        var composite = new DetachedSupervisorLauncher(sched, breakaway);
        SupervisorLaunchResult result = composite.Launch(
            new SupervisorClientLaunchOptions { PipeName = SupervisorIpcConstants.DefaultPipeName },
            msg => logs.Add(msg));

        Assert.That(result.Method, Is.EqualTo(SupervisorLaunchMethod.BreakawayProcess));
        Assert.That(result.ProcessId, Is.EqualTo(3003));
        Assert.That(sched.CallCount, Is.EqualTo(1));
        Assert.That(breakaway.CallCount, Is.EqualTo(1));
        Assert.That(logs, Has.Some.Contains("falling back to breakaway"));
    }

    [Test]
    public void DetachedSupervisorLauncher_WhenBothAdaptersFail_FailsClosedWithAggregatedDiagnostic()
    {
        var sched = new StubSupervisorLauncher((_, _) =>
            throw new SupervisorLaunchException("Scheduled Task registration denied."));
        var breakaway = new StubSupervisorLauncher((_, _) =>
            throw new SupervisorLaunchException("CREATE_BREAKAWAY_FROM_JOB denied (Win32 error 5)."));

        var composite = new DetachedSupervisorLauncher(sched, breakaway);
        var ex = Assert.Throws<SupervisorLaunchException>(() =>
            composite.Launch(new SupervisorClientLaunchOptions
            {
                PipeName = SupervisorIpcConstants.DefaultPipeName
            }));

        Assert.That(ex, Is.Not.Null);
        Assert.That(ex!.Message, Does.Contain("Scheduled Task registration denied."));
        Assert.That(ex.Message, Does.Contain("CREATE_BREAKAWAY_FROM_JOB denied (Win32 error 5)."));
        Assert.That(ex.InnerException, Is.TypeOf<AggregateException>());
        Assert.That(sched.CallCount, Is.EqualTo(1));
        Assert.That(breakaway.CallCount, Is.EqualTo(1));
    }

    [Test]
    public void BreakawayProcessSupervisorLauncher_RequestsBreakawayFlags_AndFailsClosedWithoutAttachedFallbackOnError5()
    {
        uint? observedFlags = null;
        int callCount = 0;

        var launcher = new BreakawayProcessSupervisorLauncher(
            createProcessOverride: (command, flags) =>
            {
                callCount++;
                observedFlags = flags;
                throw new Win32Exception(Win32ProcessLaunchUtilities.ErrorAccessDenied, "Access is denied.");
            },
            processPathOverride: @"C:\tools\RevitWorkerPool.exe");

        var ex = Assert.Throws<SupervisorLaunchException>(() =>
            launcher.Launch(new SupervisorClientLaunchOptions
            {
                PipeName = "CustomPipe"
            }));

        Assert.That(callCount, Is.EqualTo(1));
        Assert.That(observedFlags, Is.EqualTo(BreakawayProcessSupervisorLauncher.RequiredCreationFlags));
        Assert.That(ex!.Message, Does.Contain("CREATE_BREAKAWAY_FROM_JOB"));
        Assert.That(ex.Message, Does.Contain("ERROR_ACCESS_DENIED"));
    }

    [Test]
    public void BreakawayProcessSupervisorLauncher_RealWin32_DetachesFromJobOrFailsClosedWhenHostJobForbidsBreakaway()
    {
        string cmdExe = Path.Combine(Environment.SystemDirectory, "cmd.exe");
        var cmd = new SupervisorLaunchCommand(
            cmdExe,
            Environment.SystemDirectory,
            "/c ping -n 30 127.0.0.1 >nul",
            $"\"{cmdExe}\" /c ping -n 30 127.0.0.1 >nul");

        var launcher = new BreakawayProcessSupervisorLauncher();
        bool hostAllowsBreakaway = Win32ProcessLaunchUtilities.CurrentProcessAllowsBreakaway();

        if (hostAllowsBreakaway)
        {
            SupervisorLaunchResult result = launcher.LaunchCommand(cmd);
            Assert.That(result.Method, Is.EqualTo(SupervisorLaunchMethod.BreakawayProcess));
            Assert.That(result.ProcessId, Is.Not.Null);

            using Process child = Process.GetProcessById(result.ProcessId!.Value);
            try
            {
                Assert.That(child.HasExited, Is.False);
                Assert.That(
                    Win32ProcessLaunchUtilities.IsProcessInJob(child.Handle, IntPtr.Zero, out bool childInJob),
                    Is.True);
                Assert.That(childInJob, Is.False, "Breakaway process must not remain inside caller's Job Object.");
            }
            finally
            {
                if (!child.HasExited)
                {
                    child.Kill(entireProcessTree: true);
                    child.WaitForExit(3000);
                }
            }
        }
        else
        {
            var ex = Assert.Throws<SupervisorLaunchException>(() => launcher.LaunchCommand(cmd));
            Assert.That(ex, Is.Not.Null);
            Assert.That(ex!.Message, Does.Contain("CREATE_BREAKAWAY_FROM_JOB"));
            Assert.That(ex.Message, Does.Contain("ERROR_ACCESS_DENIED"));
        }
    }

    [Test]
    public async System.Threading.Tasks.Task ProgramRunAndWarm_AutoStartViaDetachedSeam_FailsClosedWhenBothAdaptersFail()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "RevitWorkerPool_DetachedFail_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            string pipeName = $"Synthetic_FailClosed_{Guid.NewGuid():N}";
            string assemblyPath = Path.Combine(tempRoot, "Synthetic.RevitTests.2026.dll");
            File.WriteAllText(assemblyPath, string.Empty);
            string trxPath = Path.Combine(tempRoot, "out.trx");

            var sched = new StubSupervisorLauncher((_, _) =>
                throw new SupervisorLaunchException("sched error"));
            var breakaway = new StubSupervisorLauncher((_, _) =>
                throw new SupervisorLaunchException("breakaway error"));
            var composite = new DetachedSupervisorLauncher(sched, breakaway);

            using var jsonOut = new StringWriter();
            using var jsonErr = new StringWriter();
            int exitCode = await Program.RunCliAsync(
                new[]
                {
                    "run",
                    "--version", "2026",
                    "--assembly", assemblyPath,
                    "--trx", trxPath,
                    "--pipe-name", pipeName,
                    "--json"
                },
                stdout: jsonOut,
                stderr: jsonErr,
                supervisorLauncher: composite);

            Assert.That(exitCode, Is.EqualTo(2));
            OneShotRunResult? parsed = JsonConvert.DeserializeObject<OneShotRunResult>(jsonOut.ToString().Trim());
            Assert.That(parsed, Is.Not.Null);
            Assert.That(parsed!.Success, Is.False);
            Assert.That(parsed.ExitCode, Is.EqualTo(2));
            Assert.That(parsed.Error, Does.Contain("breakaway error"));

            using var warmOut = new StringWriter();
            using var warmErr = new StringWriter();
            int warmExit = await Program.RunCliAsync(
                new[]
                {
                    "warm",
                    "--version", "2026",
                    "--pipe-name", pipeName,
                    "--json"
                },
                stdout: warmOut,
                stderr: warmErr,
                supervisorLauncher: composite);

            Assert.That(warmExit, Is.EqualTo(2));
            WarmResult? warmParsed = JsonConvert.DeserializeObject<WarmResult>(warmOut.ToString().Trim());
            Assert.That(warmParsed, Is.Not.Null);
            Assert.That(warmParsed!.Success, Is.False);
            Assert.That(warmParsed.ExitCode, Is.EqualTo(2));
            Assert.That(warmParsed.Error, Does.Contain("breakaway error"));
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    private sealed class StubSupervisorLauncher : ISupervisorLauncher
    {
        private readonly Func<SupervisorClientLaunchOptions, Action<string>?, SupervisorLaunchResult> _handler;

        public StubSupervisorLauncher(
            Func<SupervisorClientLaunchOptions, Action<string>?, SupervisorLaunchResult> handler)
        {
            _handler = handler;
        }

        public int CallCount { get; private set; }

        public SupervisorLaunchResult Launch(
            SupervisorClientLaunchOptions options,
            Action<string>? diagnosticLogger = null)
        {
            CallCount++;
            return _handler(options, diagnosticLogger);
        }
    }

    private sealed class FakeScheduledTaskService : IScheduledTaskService
    {
        public string? StoredXml { get; set; }
        public int RegisterCount { get; private set; }
        public string? LastFolderPath { get; private set; }
        public string? LastTaskName { get; private set; }
        public List<string> RunCalls { get; } = new();
        public Exception? ExceptionToThrow { get; set; }

        public string? GetTaskXml(string folderPath, string taskName)
        {
            LastFolderPath = folderPath;
            LastTaskName = taskName;
            if (ExceptionToThrow is not null)
            {
                throw ExceptionToThrow;
            }

            return StoredXml;
        }

        public void RegisterOrUpdateTask(string folderPath, string taskName, string taskXml)
        {
            LastFolderPath = folderPath;
            LastTaskName = taskName;
            RegisterCount++;
            StoredXml = taskXml;
        }

        public int? RunTask(string folderPath, string taskName, string arguments)
        {
            LastFolderPath = folderPath;
            LastTaskName = taskName;
            if (ExceptionToThrow is not null)
            {
                throw ExceptionToThrow;
            }

            RunCalls.Add(arguments);
            return 4242;
        }
    }
}
