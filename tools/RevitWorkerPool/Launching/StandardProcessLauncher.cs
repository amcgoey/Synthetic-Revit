using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;
using RevitWorkerPool.Containment;

namespace RevitWorkerPool.Launching;

/// <summary>
/// Zero-hook production worker launcher using Win32 <c>CreateProcessW</c> with
/// <c>CREATE_SUSPENDED</c>, <c>SW_SHOWMINNOACTIVE</c>, Job Object assignment prior to
/// <c>ResumeThread</c>, and worker isolation environment variables.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class StandardProcessLauncher : IWorkerProcessLauncher
{
    private const uint CreateSuspended = 0x00000004;
    private const uint CreateUnicodeEnvironment = 0x00000400;
    private const uint StartfUseShowWindow = 0x00000001;
    private const short SwShowMinNoActive = 7;

    public Task<LaunchedWorkerProcess> LaunchAsync(
        WorkerLaunchRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(request.ExecutablePath) || !File.Exists(request.ExecutablePath))
        {
            throw new FileNotFoundException(
                $"Worker executable not found: '{request.ExecutablePath}'.",
                request.ExecutablePath);
        }

        string fullExePath = Path.GetFullPath(request.ExecutablePath);
        string workingDir = !string.IsNullOrWhiteSpace(request.WorkingDirectory)
            ? Path.GetFullPath(request.WorkingDirectory)
            : Path.GetDirectoryName(fullExePath) ?? Directory.GetCurrentDirectory();

        string workerId = string.IsNullOrWhiteSpace(request.WorkerId)
            ? WorkerLaunchRequest.CreateWorkerId()
            : request.WorkerId.Trim();

        string commandLineText = string.IsNullOrWhiteSpace(request.Arguments)
            ? $"\"{fullExePath}\""
            : $"\"{fullExePath}\" {request.Arguments}";
        var commandLineBuffer = new StringBuilder(commandLineText);

        byte[] environmentBlock = BuildUnicodeEnvironmentBlock(request, workerId);

        var startupInfo = new StartupInfoW
        {
            cb = Marshal.SizeOf<StartupInfoW>(),
            dwFlags = StartfUseShowWindow,
            wShowWindow = SwShowMinNoActive
        };

        ProcessInformation pi = default;
        GCHandle envPin = GCHandle.Alloc(environmentBlock, GCHandleType.Pinned);
        try
        {
            if (!NativeMethods.CreateProcessW(
                    fullExePath,
                    commandLineBuffer,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    bInheritHandles: false,
                    CreateSuspended | CreateUnicodeEnvironment,
                    envPin.AddrOfPinnedObject(),
                    workingDir,
                    ref startupInfo,
                    out pi))
            {
                int error = Marshal.GetLastWin32Error();
                throw new Win32Exception(error, $"CreateProcessW failed for '{fullExePath}' (Win32 error {error}).");
            }
        }
        finally
        {
            envPin.Free();
        }

        var job = new JobObjectContainer();
        var processHandle = new SafeProcessHandle(pi.hProcess, ownsHandle: true);
        try
        {
            // Assign to Job Object BEFORE ResumeThread so no child process can escape containment.
            job.AssignProcessHandle(pi.hProcess);

            request.OnProcessCreatedBeforeResume?.Invoke(unchecked((int)pi.dwProcessId));

            uint resumeResult = NativeMethods.ResumeThread(pi.hThread);
            if (resumeResult == uint.MaxValue)
            {
                int error = Marshal.GetLastWin32Error();
                throw new Win32Exception(error, $"ResumeThread failed for PID {pi.dwProcessId} (Win32 error {error}).");
            }

            string processName = Path.GetFileNameWithoutExtension(fullExePath);
            var launched = new LaunchedWorkerProcess(
                unchecked((int)pi.dwProcessId),
                processName,
                workerId,
                processHandle,
                job);

            // Prevent cleanup below from disposing transferred ownership.
            processHandle = null!;
            job = null!;
            return Task.FromResult(launched);
        }
        catch
        {
            if (pi.hProcess != IntPtr.Zero)
            {
                NativeMethods.TerminateProcess(pi.hProcess, 1);
            }

            job?.Dispose();
            processHandle?.Dispose();
            throw;
        }
        finally
        {
            if (pi.hThread != IntPtr.Zero)
            {
                NativeMethods.CloseHandle(pi.hThread);
            }
        }
    }

    internal static byte[] BuildUnicodeEnvironmentBlock(WorkerLaunchRequest request, string workerId)
    {
        var env = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            if (entry.Key is string key && !string.IsNullOrEmpty(key) && entry.Value is string val)
            {
                env[key] = val;
            }
        }

        env["SYNTHETIC_TEST_WORKER"] = "1";
        env["SYNTHETIC_WORKER_ID"] = workerId;

        if (!string.IsNullOrWhiteSpace(request.ProjectRoot))
        {
            env["SYNTHETIC_PROJECT_ROOT"] = Path.GetFullPath(request.ProjectRoot);
        }

        if (request.AdditionalEnvironmentVariables is not null)
        {
            foreach (KeyValuePair<string, string?> kvp in request.AdditionalEnvironmentVariables)
            {
                if (string.IsNullOrWhiteSpace(kvp.Key))
                {
                    continue;
                }

                if (kvp.Value is null)
                {
                    env.Remove(kvp.Key);
                }
                else
                {
                    env[kvp.Key] = kvp.Value;
                }
            }
        }

        var sb = new StringBuilder();
        foreach (KeyValuePair<string, string> kvp in env)
        {
            sb.Append(kvp.Key);
            sb.Append('=');
            sb.Append(kvp.Value);
            sb.Append('\0');
        }

        sb.Append('\0');
        return Encoding.Unicode.GetBytes(sb.ToString());
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfoW
    {
        public int cb;
        public string? lpReserved;
        public string? lpDesktop;
        public string? lpTitle;
        public int dwX;
        public int dwY;
        public int dwXSize;
        public int dwYSize;
        public int dwXCountChars;
        public int dwYCountChars;
        public int dwFillAttribute;
        public uint dwFlags;
        public short wShowWindow;
        public short cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput;
        public IntPtr hStdOutput;
        public IntPtr hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public uint dwProcessId;
        public uint dwThreadId;
    }

    private static class NativeMethods
    {
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CreateProcessW(
            string? lpApplicationName,
            StringBuilder? lpCommandLine,
            IntPtr lpProcessAttributes,
            IntPtr lpThreadAttributes,
            [MarshalAs(UnmanagedType.Bool)] bool bInheritHandles,
            uint dwCreationFlags,
            IntPtr lpEnvironment,
            string? lpCurrentDirectory,
            ref StartupInfoW lpStartupInfo,
            out ProcessInformation lpProcessInformation);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern uint ResumeThread(IntPtr hThread);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool TerminateProcess(IntPtr hProcess, uint uExitCode);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CloseHandle(IntPtr hObject);
    }
}
