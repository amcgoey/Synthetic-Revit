using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using RevitWorkerPool.Discovery;
using RevitWorkerPool.Execution;
using RevitWorkerPool.Launching;
using RevitWorkerPool.Pipe;

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
        if (!string.Equals(command, "run", StringComparison.OrdinalIgnoreCase))
        {
            errWriter.WriteLine($"Unknown command '{command}'.");
            PrintUsage(errWriter);
            return 2;
        }

        if (!TryParseRunArguments(args[1..], out OneShotRunOptions? options, out string? parseError))
        {
            bool wantsJson = Array.Exists(args, a => string.Equals(a, "--json", StringComparison.OrdinalIgnoreCase));
            if (wantsJson)
            {
                var errorResult = new OneShotRunResult
                {
                    Success = false,
                    ExitCode = 2,
                    Error = parseError
                };
                outWriter.WriteLine(JsonConvert.SerializeObject(errorResult, Formatting.None));
            }
            else
            {
                errWriter.WriteLine($"[error] {parseError}");
                PrintUsage(errWriter);
            }

            return 2;
        }

        Action<string>? logger = options!.Diagnostics
            ? msg => errWriter.WriteLine(msg)
            : null;

        RevitInstallationDiscovery discovery = discoveryOverride ?? new RevitInstallationDiscovery();
        IWorkerProcessLauncher? launcher = null;

        using var cts = new CancellationTokenSource();
        ConsoleCancelEventHandler cancelHandler = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cts.Cancel();
        };
        Console.CancelKeyPress += cancelHandler;

        try
        {
            launcher = launcherFactory is not null
                ? launcherFactory(options)
                : CreateWorkerLauncher(options.NoShim);

            var clientFactory = new DiagnosticPipeClientFactory(logger);
            var runner = new OneShotTestRunner(
                discovery,
                launcher,
                clientFactory,
                diagnosticLogger: logger);

            OneShotRunResult result = await runner.RunAsync(options, cts.Token).ConfigureAwait(false);

            if (options.JsonOutput)
            {
                outWriter.WriteLine(JsonConvert.SerializeObject(result, Formatting.None));
            }
            else if (result.Success)
            {
                outWriter.WriteLine(
                    $"[RevitWorkerPool] Revit {result.Version} run completed: " +
                    $"Total={result.TotalTests}, Passed={result.PassedTests}, Failed={result.FailedTests}, " +
                    $"Skipped={result.SkippedTests}, WorkerId={string.Join(",", result.WorkerIds)}, TRX='{result.TrxPath}'");
            }
            else
            {
                errWriter.WriteLine(
                    $"[RevitWorkerPool] Revit {result.Version} run failed: {result.Error ?? $"{result.FailedTests} test(s) failed"}");
            }

            return result.ExitCode;
        }
        catch (CleanShimLaunchException ex)
        {
            if (options.JsonOutput)
            {
                var errorResult = new OneShotRunResult
                {
                    Success = false,
                    ExitCode = 2,
                    Version = options.RevitVersion,
                    TrxPath = options.TrxPath,
                    ShimEnabled = launcher?.UsesCleanShim ?? ShouldUseCleanShim(options.NoShim),
                    Error = ex.Message
                };
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
            Console.CancelKeyPress -= cancelHandler;
        }
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
        string? workerId = null;
        string? projectRoot = null;
        bool jsonOutput = false;
        bool oneShot = true;
        bool diagnostics = false;
        bool noShim = false;

        for (int i = 0; i < args.Count; i++)
        {
            string arg = args[i];
            switch (arg)
            {
                case "--version":
                    if (!TryConsumeValue(args, ref i, arg, out string? versionStr, out error))
                        return false;
                    if (!int.TryParse(versionStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedVersion))
                    {
                        error = $"Invalid --version value '{versionStr}'.";
                        return false;
                    }
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
                    if (!TryConsumeValue(args, ref i, arg, out string? timeoutStr, out error))
                        return false;
                    if (!int.TryParse(timeoutStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedTimeout) ||
                        parsedTimeout <= 0)
                    {
                        error = $"Invalid --timeout-seconds value '{timeoutStr}'.";
                        return false;
                    }
                    timeoutSeconds = parsedTimeout;
                    break;

                case "--boot-timeout-seconds":
                    if (!TryConsumeValue(args, ref i, arg, out string? bootStr, out error))
                        return false;
                    if (!int.TryParse(bootStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedBoot) ||
                        parsedBoot <= 0)
                    {
                        error = $"Invalid --boot-timeout-seconds value '{bootStr}'.";
                        return false;
                    }
                    bootTimeoutSeconds = parsedBoot;
                    break;

                case "--worker-id":
                    if (!TryConsumeValue(args, ref i, arg, out workerId, out error))
                        return false;
                    break;

                case "--project-root":
                    if (!TryConsumeValue(args, ref i, arg, out projectRoot, out error))
                        return false;
                    break;

                case "--json":
                    jsonOutput = true;
                    break;

                case "--one-shot":
                    oneShot = true;
                    break;

                case "--diagnostics":
                    diagnostics = true;
                    break;

                case "--no-shim":
                    noShim = true;
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
            JsonOutput = jsonOutput,
            OneShot = oneShot,
            Diagnostics = diagnostics,
            NoShim = noShim
        };

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

    private static void PrintUsage(TextWriter? stderr = null)
    {
        (stderr ?? Console.Error).WriteLine(
            "Usage: RevitWorkerPool run --version <Year> --assembly <DllPath> --trx <TrxPath> " +
            "[--filter <Fragment>] [--category <Name>] [--timeout-seconds <Sec>] [--boot-timeout-seconds <Sec>] " +
            "[--worker-id <Id>] [--project-root <Path>] [--one-shot] [--no-shim] [--json] [--diagnostics]");
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
