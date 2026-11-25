using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using NUnit.Framework;
using RevitWorkerPool.Ipc;
using RevitWorkerPool.Launching;
using RevitWorkerPool.Pipe;
using ricaun.NUnit.Models;

namespace RevitWorkerPool.Tests;

[TestFixture]
public class PipeProtocolTests
{
    [Test]
    public async Task ConnectAndWaitForReady_RejectsHungPipeWhereIsBusyStaysTrue()
    {
        using var launcher = new StubWorkerProcessLauncher((server, _) =>
        {
            server.InitialIsBusy = true;
        });

        await using LaunchedWorkerProcess worker = await launcher.LaunchAsync(new WorkerLaunchRequest
        {
            RevitVersion = 2026,
            ExecutablePath = "Revit.exe",
            WorkerId = "w_hung_busy"
        });

        using var client = new RevitPipeTestClient(worker.PipeName);

        Assert.ThrowsAsync<TimeoutException>(async () =>
            await client.ConnectAndWaitForReadyAsync(worker, TimeSpan.FromMilliseconds(250)));
    }

    [Test]
    public async Task ConnectAndWaitForReady_RejectsSilentPipeWithNoServerMessage()
    {
        using var launcher = new StubWorkerProcessLauncher((server, _) =>
        {
            server.SuppressInitialMessage = true;
        });

        await using LaunchedWorkerProcess worker = await launcher.LaunchAsync(new WorkerLaunchRequest
        {
            RevitVersion = 2026,
            ExecutablePath = "Revit.exe",
            WorkerId = "w_silent_pipe"
        });

        using var client = new RevitPipeTestClient(worker.PipeName);

        // Default TestResponse.IsBusy is false; client must NOT treat pipe connection alone as ready.
        Assert.ThrowsAsync<TimeoutException>(async () =>
            await client.ConnectAndWaitForReadyAsync(worker, TimeSpan.FromMilliseconds(250)));
    }

    [Test]
    public async Task ExecuteTests_WaitsForDelayedReady_AndStaysConnectedUntilCompletion()
    {
        bool disconnectedDuringRun = false;
        InProcessPipeTestServer? activeServer = null;

        using var launcher = new StubWorkerProcessLauncher((server, _) =>
        {
            activeServer = server;
            server.InitialIsBusy = true;
            server.AutoReadyDelay = TimeSpan.FromMilliseconds(80);
            server.ExecutionHandler = async (req, emitTest, ct) =>
            {
                await Task.Delay(50, ct);
                disconnectedDuringRun = server.ClientDisconnected;

                var t1 = new TestModel
                {
                    Name = "TestOne",
                    FullName = "Ns.Fixture.TestOne",
                    Success = true,
                    Time = 123.4
                };
                emitTest(t1);

                return new TestAssemblyModel
                {
                    FileName = req.TestPathFile ?? string.Empty,
                    Name = "Sample.Tests",
                    Success = true,
                    Tests = new List<TestTypeModel>
                    {
                        new()
                        {
                            Name = "Fixture",
                            FullName = "Ns.Fixture",
                            Success = true,
                            Tests = new List<TestModel> { t1 }
                        }
                    }
                };
            };
        });

        await using LaunchedWorkerProcess worker = await launcher.LaunchAsync(new WorkerLaunchRequest
        {
            RevitVersion = 2026,
            ExecutablePath = "Revit.exe",
            WorkerId = "w_ready_run"
        });

        using var client = new RevitPipeTestClient(worker.PipeName);
        await client.ConnectAndWaitForReadyAsync(worker, TimeSpan.FromSeconds(3));

        var streamed = new List<TestModel>();
        PipeTestExecutionResult result = await client.ExecuteTestsAsync(
            new TestRequest
            {
                TestPathFile = @"C:\temp\Sample.Tests.dll",
                TestFilters = new[] { "*Fixture*" }
            },
            worker,
            TimeSpan.FromSeconds(3),
            onTestCompleted: streamed.Add);

        Assert.That(disconnectedDuringRun, Is.False);
        Assert.That(activeServer!.ClientDisconnected, Is.False);
        Assert.That(streamed.Count, Is.EqualTo(1));
        Assert.That(result.AssemblyResult, Is.Not.Null);
        Assert.That(result.AssemblyResult!.Tests.Count, Is.EqualTo(1));
        Assert.That(result.AssemblyResult.Tests[0].Tests[0].Time, Is.EqualTo(123.4));
    }

    [TestCase("", "\"\"")]
    [TestCase(@"C:\repo\", @"C:\repo\")]
    [TestCase(@"C:\my repo\", "\"C:\\my repo\\\\\"")]
    [TestCase("a\"b", "\"a\\\"b\"")]
    [TestCase(@"a\""b", "\"a\\\\\\\"b\"")]
    [TestCase("tab\tpath\\", "\"tab\tpath\\\\\"")]
    public void QuoteArg_HandlesTrailingBackslashesAndEmbeddedQuotes_PerCommandLineToArgvW(
        string rawArgument,
        string expectedQuoted)
    {
        string quoted = SupervisorIpcClient.QuoteArg(rawArgument);
        Assert.That(quoted, Is.EqualTo(expectedQuoted));

        if (OperatingSystem.IsWindows())
        {
            string[] parsed = ParseWindowsCommandLine($"app.exe {quoted}");
            Assert.That(parsed, Is.EqualTo(new[] { "app.exe", rawArgument }));
        }
    }

    [SupportedOSPlatform("windows")]
    private static string[] ParseWindowsCommandLine(string commandLine)
    {
        IntPtr argvPtr = CommandLineToArgvW(commandLine, out int argc);
        if (argvPtr == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        try
        {
            var results = new string[argc];
            for (int i = 0; i < argc; i++)
            {
                IntPtr argPtr = Marshal.ReadIntPtr(argvPtr, i * IntPtr.Size);
                results[i] = Marshal.PtrToStringUni(argPtr) ?? string.Empty;
            }

            return results;
        }
        finally
        {
            _ = LocalFree(argvPtr);
        }
    }

    [DllImport("shell32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CommandLineToArgvW(string lpCmdLine, out int pNumArgs);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LocalFree(IntPtr hMem);
}
