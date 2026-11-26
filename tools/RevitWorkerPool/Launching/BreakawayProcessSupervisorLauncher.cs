using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using RevitWorkerPool.Ipc;

namespace RevitWorkerPool.Launching;

/// <summary>
/// Detached supervisor launch adapter using Win32 <c>CreateProcessW</c> with
/// <c>CREATE_BREAKAWAY_FROM_JOB | CREATE_NEW_PROCESS_GROUP | DETACHED_PROCESS | CREATE_UNICODE_ENVIRONMENT</c>.
/// Fails closed on Job Object breakaway denial (<c>ERROR_ACCESS_DENIED</c>) without falling back
/// to an attached process launch.
/// </summary>
internal sealed class BreakawayProcessSupervisorLauncher : ISupervisorLauncher
{
    public const uint RequiredCreationFlags =
        Win32ProcessLaunchUtilities.CreateBreakawayFromJob |
        Win32ProcessLaunchUtilities.CreateNewProcessGroup |
        Win32ProcessLaunchUtilities.DetachedProcess |
        Win32ProcessLaunchUtilities.CreateUnicodeEnvironment;

    private readonly Func<SupervisorLaunchCommand, uint, int> _createProcess;
    private readonly string? _processPathOverride;
    private readonly string? _entryAssemblyPathOverride;

    [SupportedOSPlatform("windows")]
    public BreakawayProcessSupervisorLauncher()
        : this(CreateProcessWithBreakaway)
    {
    }

    internal BreakawayProcessSupervisorLauncher(
        Func<SupervisorLaunchCommand, uint, int> createProcessOverride,
        string? processPathOverride = null,
        string? entryAssemblyPathOverride = null)
    {
        _createProcess = createProcessOverride ?? throw new ArgumentNullException(nameof(createProcessOverride));
        _processPathOverride = processPathOverride;
        _entryAssemblyPathOverride = entryAssemblyPathOverride;
    }

    public SupervisorLaunchResult Launch(
        SupervisorClientLaunchOptions options,
        Action<string>? diagnosticLogger = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        SupervisorLaunchCommand command = SupervisorLaunchCommandLine.Resolve(
            options,
            _processPathOverride,
            _entryAssemblyPathOverride);

        return LaunchCommand(command, diagnosticLogger);
    }

    internal SupervisorLaunchResult LaunchCommand(
        SupervisorLaunchCommand command,
        Action<string>? diagnosticLogger = null)
    {
        ArgumentNullException.ThrowIfNull(command);

        try
        {
            int pid = _createProcess(command, RequiredCreationFlags);
            string detail = $"pid={pid}";
            diagnosticLogger?.Invoke($"[launcher] Spawned breakaway supervisor PID={pid}.");
            return new SupervisorLaunchResult(SupervisorLaunchMethod.BreakawayProcess, pid, detail);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == Win32ProcessLaunchUtilities.ErrorAccessDenied)
        {
            throw new SupervisorLaunchException(
                $"Breakaway supervisor launch failed for '{command.ExecutablePath}': " +
                "CREATE_BREAKAWAY_FROM_JOB was denied (Win32 error 5 / ERROR_ACCESS_DENIED; caller Job Object " +
                "does not permit JOB_OBJECT_LIMIT_BREAKAWAY_OK). Refusing attached fallback.",
                ex);
        }
        catch (SupervisorLaunchException)
        {
            throw;
        }
        catch (Win32Exception ex)
        {
            throw new SupervisorLaunchException(
                $"Breakaway supervisor launch failed for '{command.ExecutablePath}': {ex.Message} (Win32 error {ex.NativeErrorCode}).",
                ex);
        }
        catch (Exception ex)
        {
            throw new SupervisorLaunchException(
                $"Breakaway supervisor launch failed for '{command.ExecutablePath}': {ex.Message}",
                ex);
        }
    }

    [SupportedOSPlatform("windows")]
    private static int CreateProcessWithBreakaway(SupervisorLaunchCommand command, uint creationFlags)
    {
        var commandLineBuffer = new StringBuilder(command.CommandLine);
        var startupInfo = new Win32ProcessLaunchUtilities.StartupInfoW
        {
            cb = Marshal.SizeOf<Win32ProcessLaunchUtilities.StartupInfoW>()
        };

        // CREATE_BREAKAWAY_FROM_JOB + DETACHED_PROCESS + CREATE_NEW_PROCESS_GROUP + bInheritHandles=false
        // detaches both console/pipe handles and Job Object ownership so parent command exit never kills the supervisor.
        if (!Win32ProcessLaunchUtilities.CreateProcessW(
                command.ExecutablePath,
                commandLineBuffer,
                IntPtr.Zero,
                IntPtr.Zero,
                bInheritHandles: false,
                creationFlags,
                IntPtr.Zero,
                command.WorkingDirectory,
                ref startupInfo,
                out Win32ProcessLaunchUtilities.ProcessInformation pi))
        {
            int error = Marshal.GetLastWin32Error();
            throw new Win32Exception(error);
        }

        try
        {
            return unchecked((int)pi.dwProcessId);
        }
        finally
        {
            if (pi.hThread != IntPtr.Zero)
            {
                Win32ProcessLaunchUtilities.CloseHandle(pi.hThread);
            }

            if (pi.hProcess != IntPtr.Zero)
            {
                Win32ProcessLaunchUtilities.CloseHandle(pi.hProcess);
            }
        }
    }
}
