using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using ricaun.NamedPipeWrapper;

namespace RevitWorkerPool.Ipc;

/// <summary>
/// Options controlling auto-start of the singleton background supervisor process.
/// </summary>
public sealed class SupervisorClientLaunchOptions
{
    public string PipeName { get; init; } = SupervisorIpcConstants.DefaultPipeName;

    public int IdleTtlMinutes { get; init; } = SupervisorIpcConstants.DefaultIdleTtlMinutes;

    public bool NoShim { get; init; }

    public string? ProjectRoot { get; init; }

    public bool Diagnostics { get; init; }

    public TimeSpan StartupTimeout { get; init; } = TimeSpan.FromSeconds(15);
}

/// <summary>
/// Named-pipe client for <see cref="SupervisorIpcServer"/> and singleton supervisor auto-starter
/// coordinated via <c>Local\Synthetic_RevitWorkerPool_Singleton_&lt;pipeName&gt;</c>.
/// </summary>
public static class SupervisorIpcClient
{
    private const uint DetachedProcess = 0x00000008;
    private const uint CreateUnicodeEnvironment = 0x00000400;

    public static async Task<SupervisorIpcResponse?> TryPingAsync(
        string pipeName,
        TimeSpan probeTimeout,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(pipeName) || !NamedPipeUtils.PipeFileExists(pipeName))
        {
            return null;
        }

        try
        {
            return await SendAsync(
                pipeName,
                new SupervisorIpcRequest { Command = SupervisorIpcCommands.Ping },
                probeTimeout,
                cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    public static async Task<SupervisorIpcResponse> SendAsync(
        string pipeName,
        SupervisorIpcRequest request,
        TimeSpan connectTimeout,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(pipeName))
        {
            throw new ArgumentException("Pipe name must not be empty.", nameof(pipeName));
        }

        ArgumentNullException.ThrowIfNull(request);

        DateTime deadlineUtc = DateTime.UtcNow + connectTimeout;
        NamedPipeClientStream? client = null;

        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                TimeSpan remaining = deadlineUtc - DateTime.UtcNow;
                if (remaining <= TimeSpan.Zero)
                {
                    throw new TimeoutException(
                        $"Timed out after {connectTimeout.TotalSeconds:F1}s connecting to Worker Pool pipe '{pipeName}'.");
                }

                var candidate = new NamedPipeClientStream(
                    ".",
                    pipeName,
                    PipeDirection.InOut,
                    PipeOptions.Asynchronous);

                int attemptMs = Math.Clamp((int)remaining.TotalMilliseconds, 1, 250);
                try
                {
                    await candidate.ConnectAsync(attemptMs, cancellationToken).ConfigureAwait(false);
                    client = candidate;
                    break;
                }
                catch (TimeoutException)
                {
                    candidate.Dispose();
                }
                catch (IOException)
                {
                    candidate.Dispose();
                }

                await Task.Delay(20, cancellationToken).ConfigureAwait(false);
            }

            string requestJson = JsonConvert.SerializeObject(request, Formatting.None);
            await IpcFraming.WriteUtf8LineAsync(client, requestJson, cancellationToken).ConfigureAwait(false);

            string? responseLine = await IpcFraming.ReadUtf8LineAsync(
                client,
                IpcFraming.DefaultMaxLineBytes,
                cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(responseLine))
            {
                throw new IOException($"Supervisor pipe '{pipeName}' closed before returning a response.");
            }

            return JsonConvert.DeserializeObject<SupervisorIpcResponse>(responseLine)
                ?? throw new IOException($"Supervisor pipe '{pipeName}' returned null JSON response.");
        }
        finally
        {
            if (client is not null)
            {
                await client.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Ensures a single supervisor instance is listening on <paramref name="options"/>.<see cref="SupervisorClientLaunchOptions.PipeName"/>,
    /// using the named mutex <c>Local\Synthetic_RevitWorkerPool_Singleton_&lt;pipeName&gt;</c> so concurrent callers
    /// spawn at most one supervisor process.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public static Task EnsureSupervisorRunningAsync(
        SupervisorClientLaunchOptions options,
        Action<string>? diagnosticLogger = null,
        CancellationToken cancellationToken = default)
    {
        return EnsureSupervisorRunningAsync(
            options,
            spawnSupervisorAction: (opts, logger) =>
            {
                SpawnDetachedSupervisorProcess(opts, logger);
            },
            diagnosticLogger,
            cancellationToken);
    }

    /// <summary>
    /// Internal overload accepting an injectable spawn delegate for deterministic concurrency tests.
    /// </summary>
    internal static async Task EnsureSupervisorRunningAsync(
        SupervisorClientLaunchOptions options,
        Action<SupervisorClientLaunchOptions, Action<string>?> spawnSupervisorAction,
        Action<string>? diagnosticLogger = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(spawnSupervisorAction);

        SupervisorIpcResponse? fastPing = await TryPingAsync(
            options.PipeName,
            TimeSpan.FromMilliseconds(200),
            cancellationToken).ConfigureAwait(false);

        if (fastPing is { Success: true, IsDraining: false })
        {
            return;
        }

        // Mutex ownership has OS thread affinity; perform wait/spawn/release on a dedicated thread
        // so async continuations never release the mutex from a different thread.
        await Task.Factory.StartNew(
            () => EnsureSupervisorRunningOnDedicatedThread(options, spawnSupervisorAction, diagnosticLogger, cancellationToken),
            cancellationToken,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default).ConfigureAwait(false);
    }

    private static void EnsureSupervisorRunningOnDedicatedThread(
        SupervisorClientLaunchOptions options,
        Action<SupervisorClientLaunchOptions, Action<string>?> spawnSupervisorAction,
        Action<string>? diagnosticLogger,
        CancellationToken cancellationToken)
    {
        string mutexName = SupervisorIpcConstants.BuildSingletonMutexName(options.PipeName);
        using var mutex = new Mutex(initiallyOwned: false, mutexName);

        bool acquired = false;
        try
        {
            try
            {
                int timeoutMs = Math.Max(1000, (int)options.StartupTimeout.TotalMilliseconds);
                acquired = mutex.WaitOne(timeoutMs);
            }
            catch (AbandonedMutexException)
            {
                acquired = true;
            }

            if (!acquired)
            {
                throw new TimeoutException(
                    $"Timed out waiting for supervisor singleton mutex '{mutexName}'.");
            }

            cancellationToken.ThrowIfCancellationRequested();

            SupervisorIpcResponse? existingPing = TryPingAsync(
                options.PipeName,
                TimeSpan.FromMilliseconds(250),
                cancellationToken).GetAwaiter().GetResult();

            if (existingPing is { Success: true, IsDraining: false })
            {
                return;
            }

            if (existingPing is { IsDraining: true })
            {
                DateTime drainWaitDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
                while (DateTime.UtcNow < drainWaitDeadline)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    SupervisorIpcResponse? probe = TryPingAsync(
                        options.PipeName,
                        TimeSpan.FromMilliseconds(150),
                        cancellationToken).GetAwaiter().GetResult();
                    if (probe is null)
                    {
                        break;
                    }

                    Thread.Sleep(50);
                }
            }

            diagnosticLogger?.Invoke($"[ipc] Spawning singleton supervisor for pipe '{options.PipeName}'...");
            spawnSupervisorAction(options, diagnosticLogger);

            DateTime readyDeadline = DateTime.UtcNow + options.StartupTimeout;
            while (DateTime.UtcNow < readyDeadline)
            {
                cancellationToken.ThrowIfCancellationRequested();

                SupervisorIpcResponse? ping = TryPingAsync(
                    options.PipeName,
                    TimeSpan.FromMilliseconds(250),
                    cancellationToken).GetAwaiter().GetResult();

                if (ping is { Success: true, IsDraining: false })
                {
                    diagnosticLogger?.Invoke(
                        $"[ipc] Supervisor ready (PID={ping.Status?.SupervisorPid}, pipe='{options.PipeName}').");
                    return;
                }

                Thread.Sleep(25);
            }

            throw new TimeoutException(
                $"Timed out after {options.StartupTimeout.TotalSeconds:F1}s waiting for Worker Pool supervisor pipe '{options.PipeName}' to become ready.");
        }
        finally
        {
            if (acquired)
            {
                mutex.ReleaseMutex();
            }
        }
    }

    [SupportedOSPlatform("windows")]
    internal static int SpawnDetachedSupervisorProcess(
        SupervisorClientLaunchOptions options,
        Action<string>? diagnosticLogger = null)
    {
        string exePath = Environment.ProcessPath
            ?? throw new InvalidOperationException("Could not determine current executable path to spawn supervisor.");

        var args = new List<string>();
        if (string.Equals(Path.GetFileNameWithoutExtension(exePath), "dotnet", StringComparison.OrdinalIgnoreCase))
        {
            string? entryAssembly = Assembly.GetEntryAssembly()?.Location;
            if (string.IsNullOrWhiteSpace(entryAssembly))
            {
                throw new InvalidOperationException("Could not determine entry assembly path for dotnet host.");
            }

            args.Add(QuoteArg(entryAssembly));
        }

        args.Add("--supervisor");
        args.Add("--pipe-name");
        args.Add(QuoteArg(options.PipeName));
        args.Add("--idle-ttl-minutes");
        args.Add(options.IdleTtlMinutes.ToString(System.Globalization.CultureInfo.InvariantCulture));

        if (options.NoShim)
        {
            args.Add("--no-shim");
        }

        if (!string.IsNullOrWhiteSpace(options.ProjectRoot))
        {
            args.Add("--project-root");
            args.Add(QuoteArg(Path.GetFullPath(options.ProjectRoot)));
        }

        if (options.Diagnostics)
        {
            args.Add("--diagnostics");
        }

        string commandLine = $"{QuoteArg(exePath)} {string.Join(" ", args)}";
        var commandLineBuffer = new StringBuilder(commandLine);
        string workingDir = Path.GetDirectoryName(exePath) ?? Directory.GetCurrentDirectory();

        var startupInfo = new StartupInfoW
        {
            cb = Marshal.SizeOf<StartupInfoW>()
        };

        // DETACHED_PROCESS + bInheritHandles=false ensures zero inherited stdout/stderr pipe handles
        // so calling scripts using stdout/stderr capture never block waiting on the daemon.
        if (!NativeMethods.CreateProcessW(
                exePath,
                commandLineBuffer,
                IntPtr.Zero,
                IntPtr.Zero,
                bInheritHandles: false,
                DetachedProcess | CreateUnicodeEnvironment,
                IntPtr.Zero,
                workingDir,
                ref startupInfo,
                out ProcessInformation pi))
        {
            int error = Marshal.GetLastWin32Error();
            throw new Win32Exception(error, $"Failed to start Worker Pool supervisor '{exePath}' (Win32 error {error}).");
        }

        try
        {
            diagnosticLogger?.Invoke($"[ipc] Spawned supervisor PID={pi.dwProcessId}.");
            return unchecked((int)pi.dwProcessId);
        }
        finally
        {
            if (pi.hThread != IntPtr.Zero)
            {
                NativeMethods.CloseHandle(pi.hThread);
            }

            if (pi.hProcess != IntPtr.Zero)
            {
                NativeMethods.CloseHandle(pi.hProcess);
            }
        }
    }

    internal static string QuoteArg(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "\"\"";
        }

        if (!value.Contains(' ') && !value.Contains('"') && !value.Contains('\t'))
        {
            return value;
        }

        var sb = new StringBuilder(value.Length + 4);
        sb.Append('"');
        int backslashCount = 0;

        foreach (char c in value)
        {
            if (c == '\\')
            {
                backslashCount++;
            }
            else if (c == '"')
            {
                sb.Append('\\', backslashCount * 2 + 1);
                sb.Append('"');
                backslashCount = 0;
            }
            else
            {
                if (backslashCount > 0)
                {
                    sb.Append('\\', backslashCount);
                    backslashCount = 0;
                }

                sb.Append(c);
            }
        }

        if (backslashCount > 0)
        {
            sb.Append('\\', backslashCount * 2);
        }

        sb.Append('"');
        return sb.ToString();
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
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CloseHandle(IntPtr hObject);
    }
}
