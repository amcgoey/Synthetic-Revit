using System;
using System.IO;
using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using RevitWorkerPool.Launching;
using ricaun.NamedPipeWrapper;

namespace RevitWorkerPool.Ipc;

/// <summary>
/// Options controlling auto-start of the singleton background supervisor process.
/// </summary>
public sealed class SupervisorClientLaunchOptions
{
    public string PipeName { get; init; } = SupervisorIpcConstants.DefaultPipeName;

    public int IdleTtlMinutes { get; init; } = SupervisorIpcConstants.DefaultIdleTtlMinutes;

    public int? MaxWorkers { get; init; }

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
            DetachedSupervisorLauncher.CreateDefault(),
            diagnosticLogger,
            cancellationToken);
    }

    /// <summary>
    /// Internal overload accepting an injectable <see cref="ISupervisorLauncher"/> seam for deterministic tests.
    /// </summary>
    internal static async Task EnsureSupervisorRunningAsync(
        SupervisorClientLaunchOptions options,
        ISupervisorLauncher supervisorLauncher,
        Action<string>? diagnosticLogger = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(supervisorLauncher);

        SupervisorIpcResponse? fastPing = await TryPingAsync(
            options.PipeName,
            TimeSpan.FromMilliseconds(200),
            cancellationToken).ConfigureAwait(false);

        if (fastPing is { Success: true, IsDraining: false } || fastPing is { IsDrainingWhenIdle: true })
        {
            return;
        }

        // Mutex ownership has OS thread affinity; perform wait/spawn/release on a dedicated thread
        // so async continuations never release the mutex from a different thread.
        await Task.Factory.StartNew(
            () => EnsureSupervisorRunningOnDedicatedThread(options, supervisorLauncher, diagnosticLogger, cancellationToken),
            cancellationToken,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default).ConfigureAwait(false);
    }

    private static void EnsureSupervisorRunningOnDedicatedThread(
        SupervisorClientLaunchOptions options,
        ISupervisorLauncher supervisorLauncher,
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

            if (existingPing is { Success: true, IsDraining: false } || existingPing is { IsDrainingWhenIdle: true })
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
            SupervisorLaunchResult launchResult = supervisorLauncher.Launch(options, diagnosticLogger);
            diagnosticLogger?.Invoke($"[ipc] Supervisor launch initiated via {launchResult.Method} ({launchResult.Detail}).");

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
}
