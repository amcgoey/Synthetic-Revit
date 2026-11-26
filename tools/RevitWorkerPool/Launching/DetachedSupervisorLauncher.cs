using System;
using System.Runtime.Versioning;
using RevitWorkerPool.Ipc;

namespace RevitWorkerPool.Launching;

/// <summary>
/// Composite detached supervisor launcher evaluating two adapters in priority order:
/// <list type="number">
/// <item><description>Per-user interactive Windows Scheduled Task (<c>Synthetic\RevitWorkerPoolSupervisor</c>) for the default production pipe.</description></item>
/// <item><description>Win32 <c>CREATE_BREAKAWAY_FROM_JOB</c> process launch as fallback (and primary for custom <c>--pipe-name</c> test supervisors).</description></item>
/// </list>
/// Fails closed with an aggregated diagnostic error when detached launch cannot succeed.
/// </summary>
internal sealed class DetachedSupervisorLauncher : ISupervisorLauncher
{
    private readonly ISupervisorLauncher _scheduledTaskLauncher;
    private readonly ISupervisorLauncher _breakawayLauncher;

    public DetachedSupervisorLauncher(
        ISupervisorLauncher scheduledTaskLauncher,
        ISupervisorLauncher breakawayLauncher)
    {
        _scheduledTaskLauncher = scheduledTaskLauncher ?? throw new ArgumentNullException(nameof(scheduledTaskLauncher));
        _breakawayLauncher = breakawayLauncher ?? throw new ArgumentNullException(nameof(breakawayLauncher));
    }

    [SupportedOSPlatform("windows")]
    public static DetachedSupervisorLauncher CreateDefault()
    {
        return new DetachedSupervisorLauncher(
            new ScheduledTaskSupervisorLauncher(),
            new BreakawayProcessSupervisorLauncher());
    }

    public SupervisorLaunchResult Launch(
        SupervisorClientLaunchOptions options,
        Action<string>? diagnosticLogger = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        bool isDefaultPipe = string.Equals(
            options.PipeName?.Trim(),
            SupervisorIpcConstants.DefaultPipeName,
            StringComparison.OrdinalIgnoreCase);

        if (!isDefaultPipe)
        {
            diagnosticLogger?.Invoke(
                $"[launcher] Custom pipe '{options.PipeName}' skips Scheduled Task; launching via breakaway adapter.");
            try
            {
                return _breakawayLauncher.Launch(options, diagnosticLogger);
            }
            catch (Exception ex)
            {
                throw new SupervisorLaunchException(
                    $"Failed to launch detached Worker Pool supervisor on custom pipe '{options.PipeName}' " +
                    $"via breakaway adapter (Scheduled Task is reserved for default pipe '{SupervisorIpcConstants.DefaultPipeName}'): {ex.Message}",
                    ex);
            }
        }

        try
        {
            return _scheduledTaskLauncher.Launch(options, diagnosticLogger);
        }
        catch (Exception scheduledEx)
        {
            diagnosticLogger?.Invoke(
                $"[launcher] Scheduled Task adapter failed ({scheduledEx.Message}); falling back to breakaway adapter.");

            try
            {
                return _breakawayLauncher.Launch(options, diagnosticLogger);
            }
            catch (Exception breakawayEx)
            {
                throw new SupervisorLaunchException(
                    $"Failed to launch detached Worker Pool supervisor on pipe '{options.PipeName}'. " +
                    $"Scheduled Task adapter failed: {scheduledEx.Message} | " +
                    $"Breakaway adapter failed: {breakawayEx.Message}",
                    new AggregateException(scheduledEx, breakawayEx));
            }
        }
    }
}
