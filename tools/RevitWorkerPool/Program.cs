using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using RevitWorkerPool.Discovery;
using RevitWorkerPool.Execution;
using RevitWorkerPool.Ipc;
using RevitWorkerPool.Launching;
using RevitWorkerPool.Pipe;
using RevitWorkerPool.Supervisor;
using RevitWorkerPool.Windowing;

namespace RevitWorkerPool;

/// <summary>
/// Standalone CLI entry point for <c>RevitWorkerPool</c>.
/// </summary>
public static class Program
{
    public static Task<int> Main(string[] args)
    {
        return RunCliAsync(args);
    }

    internal static async Task<int> RunCliAsync(
        string[] args,
        Func<OneShotRunOptions, IWorkerProcessLauncher>? launcherFactory = null,
        RevitInstallationDiscovery? discoveryOverride = null,
        TextWriter? stdout = null,
        TextWriter? stderr = null)
    {
        TextWriter outWriter = stdout ?? Console.Out;
        TextWriter errWriter = stderr ?? Console.Error;

        if (args.Length == 0 ||
            string.Equals(args[0], "--help", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(args[0], "-h", StringComparison.OrdinalIgnoreCase))
        {
            PrintUsage(errWriter);
            return 0;
        }

        string command = args[0];
        bool wantsJson = Array.Exists(args, a => string.Equals(a, "--json", StringComparison.OrdinalIgnoreCase));

        if (string.Equals(command, "--supervisor", StringComparison.OrdinalIgnoreCase))
        {
            return await ExecuteSupervisorCommandAsync(args[1..], errWriter).ConfigureAwait(false);
        }

        if (string.Equals(command, "warm", StringComparison.OrdinalIgnoreCase))
        {
            return await ExecuteWarmCommandAsync(args[1..], wantsJson, outWriter, errWriter).ConfigureAwait(false);
        }

        if (string.Equals(command, "run", StringComparison.OrdinalIgnoreCase))
        {
            return await ExecuteRunCommandAsync(
                args[1..],
                wantsJson,
                launcherFactory,
                discoveryOverride,
                outWriter,
                errWriter).ConfigureAwait(false);
        }

        if (string.Equals(command, "status", StringComparison.OrdinalIgnoreCase))
        {
            return await ExecuteStatusCommandAsync(args[1..], wantsJson, outWriter, errWriter).ConfigureAwait(false);
        }

        if (string.Equals(command, "drain", StringComparison.OrdinalIgnoreCase))
        {
            return await ExecuteDrainCommandAsync(args[1..], wantsJson, outWriter, errWriter).ConfigureAwait(false);
        }

        string unknownErr = $"Unknown command '{command}'.";
        if (wantsJson)
        {
            outWriter.WriteLine(JsonConvert.SerializeObject(
                new { success = false, exitCode = 2, error = unknownErr },
                Formatting.None));
        }
        else
        {
            errWriter.WriteLine(unknownErr);
            PrintUsage(errWriter);
        }

        return 2;
    }

    internal static bool ShouldUseCleanShim(
        bool noShimFlag,
        Func<string, string?>? getEnvironmentVariable = null)
    {
        if (noShimFlag)
        {
            return false;
        }

        Func<string, string?> envLookup = getEnvironmentVariable ?? Environment.GetEnvironmentVariable;
        string? rawValue = envLookup("SYNTHETIC_CLEAN_SHIM");
        if (!string.IsNullOrWhiteSpace(rawValue) &&
            string.Equals(rawValue.Trim(), "0", StringComparison.Ordinal))
        {
            return false;
        }

        return true;
    }

    internal static IWorkerProcessLauncher CreateWorkerLauncher(
        bool noShimFlag,
        Func<string, string?>? getEnvironmentVariable = null,
        string? shimDllPath = null,
        string? launcherDllPath = null)
    {
        if (!ShouldUseCleanShim(noShimFlag, getEnvironmentVariable))
        {
            return new StandardProcessLauncher();
        }

        if (!OperatingSystem.IsWindows())
        {
            throw new CleanShimLaunchException("CleanDetoursProcessLauncher requires Windows.");
        }

        return new CleanDetoursProcessLauncher(shimDllPath, launcherDllPath);
    }

    private static async Task<int> ExecuteSupervisorCommandAsync(string[] args, TextWriter errWriter)
    {
        if (!TryParseSupervisorArguments(args, out SupervisorCliOptions? cliOptions, out string? parseError))
        {
            errWriter.WriteLine($"[error] {parseError}");
            return 2;
        }

        Action<string>? logger = cliOptions!.Diagnostics
            ? msg => errWriter.WriteLine(msg)
            : null;

        IWorkerProcessLauncher baseLauncher;
        try
        {
            baseLauncher = CreateWorkerLauncher(cliOptions.NoShim);
        }
        catch (CleanShimLaunchException ex)
        {
            errWriter.WriteLine($"[RevitWorkerPool] Clean-launch shim failed closed: {ex.Message}");
            return 2;
        }

        OffScreenWindowingLauncher? windowingLauncher = null;
        IWorkerProcessLauncher effectiveLauncher = baseLauncher;
        if (OperatingSystem.IsWindows() && baseLauncher is not OffScreenWindowingLauncher)
        {
            windowingLauncher = new OffScreenWindowingLauncher(baseLauncher, logger);
            effectiveLauncher = windowingLauncher;
        }

        var discovery = new RevitInstallationDiscovery();
        var clientFactory = new DiagnosticPipeClientFactory(logger);
        var supervisorOptions = new SupervisorOptions
        {
            PipeName = cliOptions.PipeName,
            IdleTimeout = TimeSpan.FromMinutes(cliOptions.IdleTtlMinutes),
            ShimEnabled = effectiveLauncher.UsesCleanShim,
            ProjectRoot = cliOptions.ProjectRoot,
            Diagnostics = cliOptions.Diagnostics
        };

        await using var supervisor = new WorkerPoolSupervisor(
            supervisorOptions,
            discovery,
            effectiveLauncher,
            clientFactory,
            diagnosticLogger: logger);

        await using var ipcServer = new SupervisorIpcServer(
            cliOptions.PipeName,
            (req, ct) => supervisor.HandleIpcRequestAsync(req, ct),
            logger);

        supervisor.AttachIpcServer(ipcServer);
        supervisor.Start();
        ipcServer.Start();

        ConsoleCancelEventHandler cancelHandler = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            _ = Task.Run(() => supervisor.DrainAsync(CancellationToken.None));
        };
        Console.CancelKeyPress += cancelHandler;

        try
        {
            await supervisor.WaitForShutdownAsync().ConfigureAwait(false);
            using var drainFlushCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await ipcServer.StopAndDrainActiveResponsesAsync(drainFlushCts.Token).ConfigureAwait(false);
            return 0;
        }
        catch (Exception ex)
        {
            logger?.Invoke($"[supervisor] Fatal error: {ex.Message}");
            return 2;
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
            windowingLauncher?.Dispose();
        }
    }

    private static async Task<int> ExecuteWarmCommandAsync(
        string[] args,
        bool wantsJson,
        TextWriter outWriter,
        TextWriter errWriter)
    {
        if (!TryParseWarmArguments(args, out WarmCliOptions? options, out string? parseError))
        {
            EmitCliError(parseError!, wantsJson, outWriter, errWriter, () => new WarmResult
            {
                Success = false,
                ExitCode = 2,
                Error = parseError
            });
            return 2;
        }

        Action<string>? logger = options!.Diagnostics ? msg => errWriter.WriteLine(msg) : null;
        using var cancelScope = new CancelKeyPressScope();
        CancellationToken cancellationToken = cancelScope.Token;

        try
        {
            bool expectedShim = ShouldUseCleanShim(options.NoShim);
            var launchOptions = new SupervisorClientLaunchOptions
            {
                PipeName = options.PipeName,
                IdleTtlMinutes = options.IdleTtlMinutes,
                NoShim = !expectedShim,
                ProjectRoot = options.ProjectRoot ?? RevitInstallationDiscovery.FindRepositoryRoot(),
                Diagnostics = options.Diagnostics
            };

            await EnsureSupervisorMatchingShimModeAsync(
                launchOptions,
                expectedShim,
                logger,
                cancellationToken).ConfigureAwait(false);

            var request = new SupervisorIpcRequest
            {
                Command = SupervisorIpcCommands.Warm,
                Version = options.RevitVersion,
                Count = options.Count,
                BootTimeoutSeconds = options.BootTimeoutSeconds
            };

            TimeSpan connectTimeout = TimeSpan.FromSeconds(15);
            SupervisorIpcResponse response = await SupervisorIpcClient.SendAsync(
                options.PipeName,
                request,
                connectTimeout,
                cancellationToken).ConfigureAwait(false);

            WarmResult warmResult = response.WarmResult ?? new WarmResult
            {
                Success = response.Success,
                ExitCode = response.ExitCode,
                Error = response.Error,
                Status = response.Status
            };

            if (options.JsonOutput)
            {
                outWriter.WriteLine(JsonConvert.SerializeObject(warmResult, Formatting.None));
            }
            else if (warmResult.Success)
            {
                outWriter.WriteLine(
                    $"[RevitWorkerPool] Warmed Revit {warmResult.Version}: " +
                    $"readyStandby={warmResult.ReadyCount}, target={warmResult.TargetCount}, " +
                    $"totalResident={warmResult.Status?.TotalResident ?? warmResult.ReadyCount}/{warmResult.Status?.ActiveCap ?? SupervisorOptions.DefaultMaxActiveWorkers}");
            }
            else
            {
                errWriter.WriteLine($"[RevitWorkerPool] Warm failed: {warmResult.Error}");
            }

            return warmResult.ExitCode;
        }
        catch (Exception ex)
        {
            EmitCliError(ex.Message, options.JsonOutput, outWriter, errWriter, () => new WarmResult
            {
                Success = false,
                ExitCode = 2,
                Version = options.RevitVersion ?? 0,
                TargetCount = options.Count,
                Error = ex.Message
            });
            return 2;
        }
    }

    private static async Task<int> ExecuteRunCommandAsync(
        string[] args,
        bool wantsJson,
        Func<OneShotRunOptions, IWorkerProcessLauncher>? launcherFactory,
        RevitInstallationDiscovery? discoveryOverride,
        TextWriter outWriter,
        TextWriter errWriter)
    {
        if (!TryParseRunArguments(args, out OneShotRunOptions? options, out string? parseError))
        {
            EmitCliError(parseError!, wantsJson, outWriter, errWriter, () => new OneShotRunResult
            {
                Success = false,
                ExitCode = 2,
                Error = parseError
            });
            return 2;
        }

        Action<string>? logger = options!.Diagnostics ? msg => errWriter.WriteLine(msg) : null;
        using var cancelScope = new CancelKeyPressScope();
        CancellationToken cancellationToken = cancelScope.Token;

        if (options.OneShot || launcherFactory is not null || discoveryOverride is not null)
        {
            RevitInstallationDiscovery discovery = discoveryOverride ?? new RevitInstallationDiscovery();
            IWorkerProcessLauncher? baseLauncher = null;
            OffScreenWindowingLauncher? windowingLauncher = null;
            try
            {
                baseLauncher = launcherFactory is not null
                    ? launcherFactory(options)
                    : CreateWorkerLauncher(options.NoShim);

                IWorkerProcessLauncher effectiveLauncher = baseLauncher;
                if (OperatingSystem.IsWindows() && baseLauncher is not OffScreenWindowingLauncher)
                {
                    windowingLauncher = new OffScreenWindowingLauncher(baseLauncher, logger);
                    effectiveLauncher = windowingLauncher;
                }

                var clientFactory = new DiagnosticPipeClientFactory(logger);
                var runner = new OneShotTestRunner(
                    discovery,
                    effectiveLauncher,
                    clientFactory,
                    diagnosticLogger: logger);

                OneShotRunResult oneShotResult = await runner.RunAsync(options, cancellationToken).ConfigureAwait(false);
                WriteRunOutput(oneShotResult, options.JsonOutput, outWriter, errWriter);
                return oneShotResult.ExitCode;
            }
            catch (CleanShimLaunchException ex)
            {
                bool shimEnabled = baseLauncher?.UsesCleanShim ?? ShouldUseCleanShim(options.NoShim);
                if (options.JsonOutput)
                {
                    OneShotRunResult errorResult = TestExecutionHelper.InfrastructureFailureResult(
                        options.RevitVersion,
                        options.TrxPath,
                        Array.Empty<string>(),
                        shimEnabled,
                        ex.Message);
                    outWriter.WriteLine(JsonConvert.SerializeObject(errorResult, Formatting.None));
                }
                else
                {
                    errWriter.WriteLine($"[RevitWorkerPool] Clean-launch shim failed closed: {ex.Message}");
                }

                return 2;
            }
            finally
            {
                windowingLauncher?.Dispose();
            }
        }

        string fullAssemblyPath = Path.GetFullPath(options.AssemblyPath);
        string fullTrxPath = Path.GetFullPath(options.TrxPath);
        string resolvedProjectRoot = TestExecutionHelper.ResolveProjectRoot(options.ProjectRoot, fullAssemblyPath);
        bool expectedShim = ShouldUseCleanShim(options.NoShim);

        try
        {
            var launchOptions = new SupervisorClientLaunchOptions
            {
                PipeName = options.PipeName,
                IdleTtlMinutes = options.IdleTtlMinutes,
                NoShim = !expectedShim,
                ProjectRoot = resolvedProjectRoot,
                Diagnostics = options.Diagnostics
            };

            await EnsureSupervisorMatchingShimModeAsync(
                launchOptions,
                expectedShim,
                logger,
                cancellationToken).ConfigureAwait(false);

            var ipcRequest = new SupervisorIpcRequest
            {
                Command = SupervisorIpcCommands.Run,
                Run = new SupervisorRunPayload
                {
                    Version = options.RevitVersion,
                    AssemblyPath = fullAssemblyPath,
                    TrxPath = fullTrxPath,
                    Filters = options.Filters,
                    Category = options.Category,
                    Categories = string.IsNullOrWhiteSpace(options.Category)
                        ? Array.Empty<string>()
                        : new[] { options.Category },
                    BootTimeoutSeconds = options.BootTimeout.TotalSeconds,
                    ExecutionTimeoutSeconds = options.ExecutionTimeout.TotalSeconds,
                    WorkerId = options.WorkerId,
                    ProjectRoot = resolvedProjectRoot,
                    Shards = options.Shards,
                    NextVersion = options.NextVersion,
                    Diagnostics = options.Diagnostics
                }
            };

            SupervisorIpcResponse response = await SupervisorIpcClient.SendAsync(
                options.PipeName,
                ipcRequest,
                TimeSpan.FromSeconds(15),
                cancellationToken).ConfigureAwait(false);

            OneShotRunResult result = response.RunResult ?? TestExecutionHelper.InfrastructureFailureResult(
                options.RevitVersion,
                fullTrxPath,
                Array.Empty<string>(),
                expectedShim,
                response.Error ?? "Supervisor did not return a run result.");

            WriteRunOutput(result, options.JsonOutput, outWriter, errWriter);
            return result.ExitCode;
        }
        catch (Exception ex)
        {
            OneShotRunResult errorResult = TestExecutionHelper.InfrastructureFailureResult(
                options.RevitVersion,
                fullTrxPath,
                Array.Empty<string>(),
                expectedShim,
                ex.Message);
            WriteRunOutput(errorResult, options.JsonOutput, outWriter, errWriter);
            return errorResult.ExitCode;
        }
    }

    private static async Task EnsureSupervisorMatchingShimModeAsync(
        SupervisorClientLaunchOptions launchOptions,
        bool expectedShim,
        Action<string>? logger,
        CancellationToken cancellationToken)
    {
        SupervisorIpcResponse? prePing = await SupervisorIpcClient.TryPingAsync(
            launchOptions.PipeName,
            TimeSpan.FromMilliseconds(500),
            cancellationToken).ConfigureAwait(false);

        if (prePing is { Success: true, IsDraining: false, Status: { Running: true } preStatus })
        {
            ThrowIfShimModeMismatch(preStatus, launchOptions.PipeName, expectedShim);
            return;
        }

        await SupervisorIpcClient.EnsureSupervisorRunningAsync(launchOptions, logger, cancellationToken)
            .ConfigureAwait(false);

        SupervisorIpcResponse? postPing = await SupervisorIpcClient.TryPingAsync(
            launchOptions.PipeName,
            TimeSpan.FromSeconds(2),
            cancellationToken).ConfigureAwait(false);

        if (postPing is { Success: true, IsDraining: false, Status: { Running: true } postStatus })
        {
            ThrowIfShimModeMismatch(postStatus, launchOptions.PipeName, expectedShim);
        }
    }

    private static void ThrowIfShimModeMismatch(
        PoolStatusSnapshot status,
        string pipeName,
        bool expectedShim)
    {
        if (status.ShimEnabled == expectedShim)
        {
            return;
        }

        string activeMode = status.ShimEnabled ? "shimmed" : "--no-shim";
        string requestedMode = expectedShim ? "shimmed" : "--no-shim";
        throw new CleanShimLaunchException(
            $"Worker Pool supervisor PID={status.SupervisorPid} on pipe '{pipeName}' has " +
            $"shimEnabled={status.ShimEnabled.ToString().ToLowerInvariant()} ({activeMode}), " +
            $"which does not match requested expectedShim={expectedShim.ToString().ToLowerInvariant()} ({requestedMode}). " +
            $"Drain it first via: RevitWorkerPool drain --pipe-name \"{pipeName}\"");
    }

    private static async Task<int> ExecuteStatusCommandAsync(
        string[] args,
        bool wantsJson,
        TextWriter outWriter,
        TextWriter errWriter)
    {
        if (!TryParsePipeControlArguments(args, "status", out PipeControlCliOptions? options, out string? parseError))
        {
            EmitCliError(parseError!, wantsJson, outWriter, errWriter, () => new { success = false, exitCode = 2, error = parseError });
            return 2;
        }

        using var cancelScope = new CancelKeyPressScope();
        CancellationToken cancellationToken = cancelScope.Token;

        try
        {
            SupervisorIpcResponse? ping = await SupervisorIpcClient.TryPingAsync(
                options!.PipeName,
                TimeSpan.FromMilliseconds(500),
                cancellationToken).ConfigureAwait(false);

            PoolStatusSnapshot snapshot;
            if (ping is null)
            {
                snapshot = new PoolStatusSnapshot
                {
                    Running = false,
                    SupervisorPid = 0,
                    PipeName = options.PipeName,
                    ShimEnabled = false,
                    Draining = false,
                    Booting = 0,
                    ReadyStandby = 0,
                    Active = 0,
                    TotalResident = 0,
                    ActiveCap = SupervisorOptions.DefaultMaxActiveWorkers,
                    QueuedRuns = 0,
                    LastActivityUtc = DateTimeOffset.MinValue
                };
            }
            else
            {
                SupervisorIpcResponse response = await SupervisorIpcClient.SendAsync(
                    options.PipeName,
                    new SupervisorIpcRequest { Command = SupervisorIpcCommands.Status },
                    TimeSpan.FromSeconds(5),
                    cancellationToken).ConfigureAwait(false);

                snapshot = response.Status ?? new PoolStatusSnapshot
                {
                    Running = false,
                    PipeName = options.PipeName,
                    ActiveCap = SupervisorOptions.DefaultMaxActiveWorkers
                };
            }

            if (options.JsonOutput)
            {
                outWriter.WriteLine(JsonConvert.SerializeObject(snapshot, Formatting.None));
            }
            else if (!snapshot.Running)
            {
                outWriter.WriteLine(
                    $"[RevitWorkerPool] Supervisor not running (pipe='{snapshot.PipeName}', " +
                    $"booting=0, readyStandby=0, active=0, totalResident=0, activeCap={snapshot.ActiveCap}).");
            }
            else
            {
                outWriter.WriteLine(
                    $"[RevitWorkerPool] Supervisor PID={snapshot.SupervisorPid} pipe='{snapshot.PipeName}': " +
                    $"booting={snapshot.Booting}, readyStandby={snapshot.ReadyStandby}, active={snapshot.Active}, " +
                    $"totalResident={snapshot.TotalResident}/{snapshot.ActiveCap}, queued={snapshot.QueuedRuns}, " +
                    $"lastActivityUtc={snapshot.LastActivityUtc:O}");
                foreach ((int ver, VersionWorkerStatus vs) in snapshot.Versions)
                {
                    outWriter.WriteLine(
                        $"  Revit {ver}: target={vs.StandbyTarget}, booting={vs.Booting}, " +
                        $"readyStandby={vs.ReadyStandby}, active={vs.Active}, totalResident={vs.TotalResident}");
                }
            }

            return 0;
        }
        catch (Exception ex)
        {
            EmitCliError(ex.Message, options!.JsonOutput, outWriter, errWriter, () => new { success = false, exitCode = 2, error = ex.Message });
            return 2;
        }
    }

    private static async Task<int> ExecuteDrainCommandAsync(
        string[] args,
        bool wantsJson,
        TextWriter outWriter,
        TextWriter errWriter)
    {
        if (!TryParsePipeControlArguments(args, "drain", out PipeControlCliOptions? options, out string? parseError))
        {
            EmitCliError(parseError!, wantsJson, outWriter, errWriter, () => new DrainResult
            {
                Success = false,
                ExitCode = 2,
                Error = parseError
            });
            return 2;
        }

        using var cancelScope = new CancelKeyPressScope();
        CancellationToken cancellationToken = cancelScope.Token;

        try
        {
            SupervisorIpcResponse? ping = await SupervisorIpcClient.TryPingAsync(
                options!.PipeName,
                TimeSpan.FromMilliseconds(500),
                cancellationToken).ConfigureAwait(false);

            DrainResult drainResult;
            if (ping is null)
            {
                drainResult = new DrainResult
                {
                    Success = true,
                    ExitCode = 0,
                    TerminatedWorkers = 0,
                    Status = new PoolStatusSnapshot
                    {
                        Running = false,
                        PipeName = options.PipeName,
                        ActiveCap = SupervisorOptions.DefaultMaxActiveWorkers
                    }
                };
            }
            else
            {
                SupervisorIpcResponse response = await SupervisorIpcClient.SendAsync(
                    options.PipeName,
                    new SupervisorIpcRequest { Command = SupervisorIpcCommands.Drain },
                    TimeSpan.FromSeconds(5),
                    cancellationToken).ConfigureAwait(false);

                drainResult = response.DrainResult ?? new DrainResult
                {
                    Success = response.Success,
                    ExitCode = response.ExitCode,
                    Error = response.Error,
                    Status = response.Status
                };
            }

            if (options.JsonOutput)
            {
                outWriter.WriteLine(JsonConvert.SerializeObject(drainResult, Formatting.None));
            }
            else if (drainResult.Success)
            {
                outWriter.WriteLine(
                    $"[RevitWorkerPool] Drained supervisor (pipe='{options.PipeName}', " +
                    $"terminatedWorkers={drainResult.TerminatedWorkers}, totalResident=0).");
            }
            else
            {
                errWriter.WriteLine($"[RevitWorkerPool] Drain failed: {drainResult.Error}");
            }

            return drainResult.ExitCode;
        }
        catch (Exception ex)
        {
            EmitCliError(ex.Message, options!.JsonOutput, outWriter, errWriter, () => new DrainResult
            {
                Success = false,
                ExitCode = 2,
                Error = ex.Message
            });
            return 2;
        }
    }

    internal static bool TryParseRunArguments(
        IReadOnlyList<string> args,
        out OneShotRunOptions? options,
        out string? error)
    {
        options = null;
        error = null;

        int? version = null;
        string? assemblyPath = null;
        string? trxPath = null;
        var filters = new List<string>();
        string? category = null;
        int? timeoutSeconds = null;
        int? bootTimeoutSeconds = null;
        int? shards = null;
        int? nextVersion = null;
        string? workerId = null;
        string? projectRoot = null;
        string pipeName = SupervisorIpcConstants.DefaultPipeName;
        int idleTtlMinutes = SupervisorIpcConstants.DefaultIdleTtlMinutes;
        bool noShim = false;
        bool jsonOutput = false;
        bool oneShot = false;
        bool diagnostics = false;

        for (int i = 0; i < args.Count; i++)
        {
            string arg = args[i];
            if (TryParseCommonSupervisorOption(
                    args,
                    ref i,
                    ref pipeName,
                    ref idleTtlMinutes,
                    ref noShim,
                    ref projectRoot,
                    ref jsonOutput,
                    ref diagnostics,
                    out bool matchedCommon,
                    out error))
            {
                if (!string.IsNullOrEmpty(error))
                {
                    return false;
                }

                if (matchedCommon)
                {
                    continue;
                }
            }

            switch (arg)
            {
                case "--version":
                    if (!TryParseIntOption(args, ref i, arg, out int parsedVersion, out error))
                        return false;
                    version = parsedVersion;
                    break;

                case "--assembly":
                    if (!TryConsumeValue(args, ref i, arg, out assemblyPath, out error))
                        return false;
                    break;

                case "--trx":
                    if (!TryConsumeValue(args, ref i, arg, out trxPath, out error))
                        return false;
                    break;

                case "--filter":
                    if (!TryConsumeValue(args, ref i, arg, out string? filterVal, out error))
                        return false;
                    if (!string.IsNullOrWhiteSpace(filterVal))
                    {
                        filters.Add(filterVal);
                    }
                    break;

                case "--category":
                    if (!TryConsumeValue(args, ref i, arg, out string? categoryVal, out error))
                        return false;
                    if (!string.IsNullOrWhiteSpace(categoryVal))
                    {
                        category = categoryVal.Trim();
                    }
                    break;

                case "--timeout-seconds":
                    if (!TryParsePositiveIntOption(args, ref i, arg, out int parsedTimeout, out error))
                        return false;
                    timeoutSeconds = parsedTimeout;
                    break;

                case "--boot-timeout-seconds":
                    if (!TryParsePositiveIntOption(args, ref i, arg, out int parsedBoot, out error))
                        return false;
                    bootTimeoutSeconds = parsedBoot;
                    break;

                case "--shards":
                    if (!TryParsePositiveIntOption(args, ref i, arg, out int parsedShards, out error))
                        return false;
                    shards = parsedShards;
                    break;

                case "--next-version":
                    if (!TryParseIntOption(args, ref i, arg, out int parsedNextVersion, out error))
                        return false;
                    nextVersion = parsedNextVersion;
                    break;

                case "--worker-id":
                    if (!TryConsumeValue(args, ref i, arg, out workerId, out error))
                        return false;
                    break;

                case "--one-shot":
                    oneShot = true;
                    break;

                default:
                    error = $"Unknown option '{arg}'.";
                    return false;
            }
        }

        if (version is null)
        {
            error = "--version <Year> is required.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(assemblyPath))
        {
            error = "--assembly <DllPath> is required.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(trxPath))
        {
            error = "--trx <TrxPath> is required.";
            return false;
        }

        TimeSpan executionTimeout = timeoutSeconds.HasValue
            ? TimeSpan.FromSeconds(timeoutSeconds.Value)
            : TimeSpan.FromSeconds(600);
        TimeSpan bootTimeout = bootTimeoutSeconds.HasValue
            ? TimeSpan.FromSeconds(bootTimeoutSeconds.Value)
            : (timeoutSeconds.HasValue
                ? TimeSpan.FromSeconds(Math.Min(timeoutSeconds.Value, 120))
                : TimeSpan.FromSeconds(120));

        options = new OneShotRunOptions
        {
            RevitVersion = version.Value,
            AssemblyPath = assemblyPath,
            TrxPath = trxPath,
            Filters = filters,
            Category = category,
            BootTimeout = bootTimeout,
            ExecutionTimeout = executionTimeout,
            WorkerId = workerId,
            ProjectRoot = projectRoot,
            Shards = shards,
            NextVersion = nextVersion,
            PipeName = pipeName,
            IdleTtlMinutes = idleTtlMinutes,
            NoShim = noShim,
            JsonOutput = jsonOutput,
            OneShot = oneShot,
            Diagnostics = diagnostics
        };

        return true;
    }

    internal static bool TryParseWarmArguments(
        IReadOnlyList<string> args,
        out WarmCliOptions? options,
        out string? error)
    {
        options = null;
        error = null;

        int? version = null;
        int count = SupervisorOptions.DefaultWarmWorkerCount;
        double bootTimeoutSeconds = 120;
        string pipeName = SupervisorIpcConstants.DefaultPipeName;
        int idleTtlMinutes = SupervisorIpcConstants.DefaultIdleTtlMinutes;
        string? projectRoot = null;
        bool noShim = false;
        bool jsonOutput = false;
        bool diagnostics = false;

        for (int i = 0; i < args.Count; i++)
        {
            string arg = args[i];
            if (TryParseCommonSupervisorOption(
                    args,
                    ref i,
                    ref pipeName,
                    ref idleTtlMinutes,
                    ref noShim,
                    ref projectRoot,
                    ref jsonOutput,
                    ref diagnostics,
                    out bool matchedCommon,
                    out error))
            {
                if (!string.IsNullOrEmpty(error))
                {
                    return false;
                }

                if (matchedCommon)
                {
                    continue;
                }
            }

            switch (arg)
            {
                case "--version":
                    if (!TryParseIntOption(args, ref i, arg, out int parsedVersion, out error))
                        return false;
                    version = parsedVersion;
                    break;

                case "--count":
                    if (!TryParsePositiveIntOption(args, ref i, arg, out int parsedCount, out error))
                        return false;
                    count = parsedCount;
                    break;

                case "--boot-timeout-seconds":
                    if (!TryParsePositiveIntOption(args, ref i, arg, out int parsedBoot, out error))
                        return false;
                    bootTimeoutSeconds = parsedBoot;
                    break;

                default:
                    error = $"Unknown option '{arg}' for warm command.";
                    return false;
            }
        }

        options = new WarmCliOptions(
            version,
            count,
            bootTimeoutSeconds,
            pipeName,
            idleTtlMinutes,
            noShim,
            projectRoot,
            jsonOutput,
            diagnostics);
        return true;
    }

    internal static bool TryParseSupervisorArguments(
        IReadOnlyList<string> args,
        out SupervisorCliOptions? options,
        out string? error)
    {
        options = null;
        error = null;

        string pipeName = SupervisorIpcConstants.DefaultPipeName;
        int idleTtlMinutes = SupervisorIpcConstants.DefaultIdleTtlMinutes;
        string? projectRoot = null;
        bool noShim = false;
        bool jsonOutput = false;
        bool diagnostics = false;

        for (int i = 0; i < args.Count; i++)
        {
            string arg = args[i];
            if (TryParseCommonSupervisorOption(
                    args,
                    ref i,
                    ref pipeName,
                    ref idleTtlMinutes,
                    ref noShim,
                    ref projectRoot,
                    ref jsonOutput,
                    ref diagnostics,
                    out bool matchedCommon,
                    out error))
            {
                if (!string.IsNullOrEmpty(error))
                {
                    return false;
                }

                if (matchedCommon)
                {
                    continue;
                }
            }

            error = $"Unknown option '{arg}' for --supervisor.";
            return false;
        }

        options = new SupervisorCliOptions(pipeName, idleTtlMinutes, noShim, projectRoot, diagnostics);
        return true;
    }

    internal static bool TryParsePipeControlArguments(
        IReadOnlyList<string> args,
        string commandName,
        out PipeControlCliOptions? options,
        out string? error)
    {
        options = null;
        error = null;

        string pipeName = SupervisorIpcConstants.DefaultPipeName;
        bool jsonOutput = false;
        bool diagnostics = false;

        for (int i = 0; i < args.Count; i++)
        {
            string arg = args[i];
            switch (arg)
            {
                case "--pipe-name":
                    if (!TryConsumeValue(args, ref i, arg, out string? parsedPipe, out error))
                        return false;
                    if (string.IsNullOrWhiteSpace(parsedPipe))
                    {
                        error = "--pipe-name must not be empty.";
                        return false;
                    }
                    pipeName = parsedPipe.Trim();
                    break;

                case "--json":
                    jsonOutput = true;
                    break;

                case "--diagnostics":
                    diagnostics = true;
                    break;

                default:
                    error = $"Unknown option '{arg}' for {commandName} command.";
                    return false;
            }
        }

        options = new PipeControlCliOptions(pipeName, jsonOutput, diagnostics);
        return true;
    }

    private static bool TryParseCommonSupervisorOption(
        IReadOnlyList<string> args,
        ref int index,
        ref string pipeName,
        ref int idleTtlMinutes,
        ref bool noShim,
        ref string? projectRoot,
        ref bool jsonOutput,
        ref bool diagnostics,
        out bool matched,
        out string? error)
    {
        error = null;
        string arg = args[index];

        switch (arg)
        {
            case "--pipe-name":
                matched = true;
                if (!TryConsumeValue(args, ref index, arg, out string? parsedPipe, out error))
                    return false;
                if (string.IsNullOrWhiteSpace(parsedPipe))
                {
                    error = "--pipe-name must not be empty.";
                    return false;
                }
                pipeName = parsedPipe.Trim();
                return true;

            case "--idle-ttl-minutes":
                matched = true;
                if (!TryParseIntOption(args, ref index, arg, out int ttlMinutes, out error))
                    return false;
                if (ttlMinutes < SupervisorIpcConstants.MinIdleTtlMinutes ||
                    ttlMinutes > SupervisorIpcConstants.MaxIdleTtlMinutes)
                {
                    error = $"--idle-ttl-minutes must be between {SupervisorIpcConstants.MinIdleTtlMinutes} and {SupervisorIpcConstants.MaxIdleTtlMinutes} (got {ttlMinutes}).";
                    return false;
                }
                idleTtlMinutes = ttlMinutes;
                return true;

            case "--no-shim":
                matched = true;
                noShim = true;
                return true;

            case "--project-root":
                matched = true;
                return TryConsumeValue(args, ref index, arg, out projectRoot, out error);

            case "--json":
                matched = true;
                jsonOutput = true;
                return true;

            case "--diagnostics":
                matched = true;
                diagnostics = true;
                return true;

            default:
                matched = false;
                return false;
        }
    }

    private static bool TryParseIntOption(
        IReadOnlyList<string> args,
        ref int index,
        string optionName,
        out int value,
        out string? error)
    {
        value = 0;
        if (!TryConsumeValue(args, ref index, optionName, out string? raw, out error))
        {
            return false;
        }

        if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
        {
            error = $"Invalid {optionName} value '{raw}'.";
            return false;
        }

        return true;
    }

    private static bool TryParsePositiveIntOption(
        IReadOnlyList<string> args,
        ref int index,
        string optionName,
        out int value,
        out string? error)
    {
        if (!TryParseIntOption(args, ref index, optionName, out value, out error))
        {
            return false;
        }

        if (value <= 0)
        {
            error = $"Invalid {optionName} value '{value}' (must be > 0).";
            return false;
        }

        return true;
    }

    private static bool TryConsumeValue(
        IReadOnlyList<string> args,
        ref int index,
        string optionName,
        out string? value,
        out string? error)
    {
        if (index + 1 >= args.Count)
        {
            value = null;
            error = $"Missing value for {optionName}.";
            return false;
        }

        value = args[++index];
        error = null;
        return true;
    }

    private static void WriteRunOutput(
        OneShotRunResult result,
        bool jsonOutput,
        TextWriter outWriter,
        TextWriter errWriter)
    {
        if (jsonOutput)
        {
            outWriter.WriteLine(JsonConvert.SerializeObject(result, Formatting.None));
        }
        else if (result.Success)
        {
            outWriter.WriteLine(
                $"[RevitWorkerPool] Revit {result.Version} run completed: " +
                $"Total={result.TotalTests}, Passed={result.PassedTests}, Failed={result.FailedTests}, " +
                $"Skipped={result.SkippedTests}, Shards={result.ShardCount}, WorkerId={string.Join(",", result.WorkerIds)}, TRX='{result.TrxPath}'");
        }
        else
        {
            errWriter.WriteLine(
                $"[RevitWorkerPool] Revit {result.Version} run failed: {result.Error ?? $"{result.FailedTests} test(s) failed"}");
        }
    }

    private static void EmitCliError(
        string errorMessage,
        bool wantsJson,
        TextWriter outWriter,
        TextWriter errWriter,
        Func<object> jsonPayloadFactory)
    {
        if (wantsJson)
        {
            outWriter.WriteLine(JsonConvert.SerializeObject(jsonPayloadFactory(), Formatting.None));
        }
        else
        {
            errWriter.WriteLine($"[error] {errorMessage}");
        }
    }

    private static void PrintUsage(TextWriter? stderr = null)
    {
        TextWriter writer = stderr ?? Console.Error;
        writer.WriteLine("Usage:");
        writer.WriteLine(
            "  RevitWorkerPool warm [--version <Year>] [--count <N>] [--boot-timeout-seconds <Sec>] " +
            "[--pipe-name <Name>] [--idle-ttl-minutes <20-30>] [--no-shim] [--project-root <Path>] [--json] [--diagnostics]");
        writer.WriteLine(
            "  RevitWorkerPool run --version <Year> --assembly <DllPath> --trx <TrxPath> " +
            "[--filter <Fragment>] [--category <Name>] [--shards <N>] [--next-version <Year>] [--timeout-seconds <Sec>] [--boot-timeout-seconds <Sec>] " +
            "[--worker-id <Id>] [--project-root <Path>] [--pipe-name <Name>] [--idle-ttl-minutes <20-30>] [--no-shim] [--one-shot] [--json] [--diagnostics]");
        writer.WriteLine(
            "  RevitWorkerPool status [--pipe-name <Name>] [--json] [--diagnostics]");
        writer.WriteLine(
            "  RevitWorkerPool drain [--pipe-name <Name>] [--json] [--diagnostics]");
    }

    internal sealed record WarmCliOptions(
        int? RevitVersion,
        int Count,
        double BootTimeoutSeconds,
        string PipeName,
        int IdleTtlMinutes,
        bool NoShim,
        string? ProjectRoot,
        bool JsonOutput,
        bool Diagnostics);

    internal sealed record SupervisorCliOptions(
        string PipeName,
        int IdleTtlMinutes,
        bool NoShim,
        string? ProjectRoot,
        bool Diagnostics);

    internal sealed record PipeControlCliOptions(
        string PipeName,
        bool JsonOutput,
        bool Diagnostics);

    private sealed class CancelKeyPressScope : IDisposable
    {
        private readonly CancellationTokenSource _cts = new();
        private readonly ConsoleCancelEventHandler _handler;

        public CancelKeyPressScope()
        {
            _handler = (_, eventArgs) =>
            {
                eventArgs.Cancel = true;
                try
                {
                    _cts.Cancel();
                }
                catch (ObjectDisposedException)
                {
                }
            };
            Console.CancelKeyPress += _handler;
        }

        public CancellationToken Token => _cts.Token;

        public void Dispose()
        {
            Console.CancelKeyPress -= _handler;
            _cts.Dispose();
        }
    }

    private sealed class DiagnosticPipeClientFactory : IPipeTestClientFactory
    {
        private readonly Action<string>? _logger;

        public DiagnosticPipeClientFactory(Action<string>? logger)
        {
            _logger = logger;
        }

        public IPipeTestClient CreateClient(LaunchedWorkerProcess worker)
        {
            ArgumentNullException.ThrowIfNull(worker);
            return new RevitPipeTestClient(worker.PipeName, _logger);
        }
    }
}
