using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using RevitWorkerPool.Ipc;

namespace RevitWorkerPool.Launching;

/// <summary>
/// Identifies the detached launch adapter used to start the Worker Pool supervisor.
/// </summary>
internal enum SupervisorLaunchMethod
{
    ScheduledTask,
    BreakawayProcess
}

/// <summary>
/// Outcome returned by an <see cref="ISupervisorLauncher"/> adapter after starting a detached supervisor.
/// </summary>
internal sealed record SupervisorLaunchResult(
    SupervisorLaunchMethod Method,
    int? ProcessId,
    string Detail);

/// <summary>
/// Fail-closed exception thrown when detached supervisor launch fails.
/// </summary>
internal sealed class SupervisorLaunchException : InvalidOperationException
{
    public SupervisorLaunchException(string message)
        : base(message)
    {
    }

    public SupervisorLaunchException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Seam for launching a detached Worker Pool supervisor process outside the caller's Windows Job Object.
/// </summary>
internal interface ISupervisorLauncher
{
    SupervisorLaunchResult Launch(
        SupervisorClientLaunchOptions options,
        Action<string>? diagnosticLogger = null);
}

/// <summary>
/// Resolved executable path, working directory, and quoted supervisor CLI arguments.
/// </summary>
internal sealed record SupervisorLaunchCommand(
    string ExecutablePath,
    string WorkingDirectory,
    string Arguments,
    string CommandLine);

/// <summary>
/// Resolves explicit supervisor launch commands and CLI arguments for both Scheduled Task
/// and Win32 breakaway adapters.
/// </summary>
internal static class SupervisorLaunchCommandLine
{
    public static SupervisorLaunchCommand Resolve(
        SupervisorClientLaunchOptions options,
        string? processPathOverride = null,
        string? entryAssemblyPathOverride = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        string exePath = !string.IsNullOrWhiteSpace(processPathOverride)
            ? processPathOverride
            : Environment.ProcessPath
                ?? throw new InvalidOperationException("Could not determine current executable path to spawn supervisor.");

        var args = new List<string>();
        if (string.Equals(Path.GetFileNameWithoutExtension(exePath), "dotnet", StringComparison.OrdinalIgnoreCase))
        {
            string? entryAssembly = !string.IsNullOrWhiteSpace(entryAssemblyPathOverride)
                ? entryAssemblyPathOverride
                : Assembly.GetEntryAssembly()?.Location;
            if (string.IsNullOrWhiteSpace(entryAssembly))
            {
                throw new InvalidOperationException("Could not determine entry assembly path for dotnet host.");
            }

            args.Add(Win32ProcessLaunchUtilities.QuoteArg(entryAssembly));
        }

        args.Add("--supervisor");
        args.Add("--pipe-name");
        args.Add(Win32ProcessLaunchUtilities.QuoteArg(options.PipeName));
        args.Add("--idle-ttl-minutes");
        args.Add(options.IdleTtlMinutes.ToString(CultureInfo.InvariantCulture));

        if (options.MaxWorkers is int maxWorkers)
        {
            args.Add("--max-workers");
            args.Add(maxWorkers.ToString(CultureInfo.InvariantCulture));
        }

        if (options.NoShim)
        {
            args.Add("--no-shim");
        }

        if (!string.IsNullOrWhiteSpace(options.ProjectRoot))
        {
            args.Add("--project-root");
            args.Add(Win32ProcessLaunchUtilities.QuoteArg(Path.GetFullPath(options.ProjectRoot)));
        }

        if (options.Diagnostics)
        {
            args.Add("--diagnostics");
        }

        string arguments = string.Join(" ", args);
        string workingDirectory = Path.GetDirectoryName(exePath) ?? Directory.GetCurrentDirectory();
        string commandLine = $"{Win32ProcessLaunchUtilities.QuoteArg(exePath)} {arguments}";

        return new SupervisorLaunchCommand(exePath, workingDirectory, arguments, commandLine);
    }
}
