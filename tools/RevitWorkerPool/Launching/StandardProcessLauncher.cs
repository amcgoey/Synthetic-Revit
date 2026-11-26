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
    public bool UsesCleanShim => false;

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

        string? desktopName = string.IsNullOrWhiteSpace(request.DesktopName)
            ? null
            : request.DesktopName.Trim();

        var startupInfo = new Win32ProcessLaunchUtilities.StartupInfoW
        {
            cb = Marshal.SizeOf<Win32ProcessLaunchUtilities.StartupInfoW>(),
            lpDesktop = desktopName,
            dwFlags = Win32ProcessLaunchUtilities.StartfUseShowWindow,
            wShowWindow = desktopName is not null
                ? Win32ProcessLaunchUtilities.SwShowNormal
                : Win32ProcessLaunchUtilities.SwShowMinNoActive
        };

        Win32ProcessLaunchUtilities.ProcessInformation pi = default;
        GCHandle envPin = GCHandle.Alloc(environmentBlock, GCHandleType.Pinned);
        try
        {
            if (!Win32ProcessLaunchUtilities.CreateProcessW(
                    fullExePath,
                    commandLineBuffer,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    bInheritHandles: false,
                    Win32ProcessLaunchUtilities.CreateSuspended | Win32ProcessLaunchUtilities.CreateUnicodeEnvironment,
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

        string processName = Path.GetFileNameWithoutExtension(fullExePath);
        LaunchedWorkerProcess launched = FinalizeSuspendedWorkerProcess(
            pi.hProcess,
            pi.hThread,
            unchecked((int)pi.dwProcessId),
            processName,
            workerId,
            request.OnProcessCreatedBeforeResume);
        return Task.FromResult(launched);
    }

    internal static LaunchedWorkerProcess FinalizeSuspendedWorkerProcess(
        IntPtr rawProcessHandle,
        IntPtr rawThreadHandle,
        int processId,
        string processName,
        string workerId,
        Action<int>? onProcessCreatedBeforeResume,
        Func<int, string, Exception>? resumeFailureFactory = null,
        Action? onDispose = null)
    {
        JobObjectContainer? job = null;
        SafeProcessHandle? processHandle = null;
        try
        {
            job = new JobObjectContainer();
            processHandle = new SafeProcessHandle(rawProcessHandle, ownsHandle: true);

            // Assign to Job Object BEFORE ResumeThread so no child process can escape containment.
            job.AssignProcessHandle(rawProcessHandle);

            onProcessCreatedBeforeResume?.Invoke(processId);

            uint resumeResult = Win32ProcessLaunchUtilities.ResumeThread(rawThreadHandle);
            if (resumeResult == uint.MaxValue)
            {
                int error = Marshal.GetLastWin32Error();
                string message = new Win32Exception(error).Message;
                throw resumeFailureFactory?.Invoke(error, message)
                    ?? new Win32Exception(error, $"ResumeThread failed for PID {processId} (Win32 error {error}).");
            }

            var launched = new LaunchedWorkerProcess(
                processId,
                processName,
                workerId,
                processHandle,
                job,
                onDispose: onDispose);

            // Ownership transferred to LaunchedWorkerProcess.
            processHandle = null;
            job = null;
            return launched;
        }
        catch
        {
            if (rawProcessHandle != IntPtr.Zero)
            {
                Win32ProcessLaunchUtilities.TerminateProcess(rawProcessHandle, 1);
                if (processHandle is null)
                {
                    Win32ProcessLaunchUtilities.CloseHandle(rawProcessHandle);
                }
            }

            job?.Dispose();
            processHandle?.Dispose();
            throw;
        }
        finally
        {
            if (rawThreadHandle != IntPtr.Zero)
            {
                Win32ProcessLaunchUtilities.CloseHandle(rawThreadHandle);
            }
        }
    }

    internal static byte[] BuildUnicodeEnvironmentBlock(
        WorkerLaunchRequest request,
        string workerId,
        IReadOnlyDictionary<string, string?>? extraDefaults = null)
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

        if (extraDefaults is not null)
        {
            foreach (KeyValuePair<string, string?> kvp in extraDefaults)
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
}
