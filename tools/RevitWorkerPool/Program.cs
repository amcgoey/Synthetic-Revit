using System;
using System.Collections.Generic;
using System.Globalization;
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
    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 0 ||
            string.Equals(args[0], "--help", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(args[0], "-h", StringComparison.OrdinalIgnoreCase))
        {
            PrintUsage();
            return 0;
        }

        string command = args[0];
        if (!string.Equals(command, "run", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine($"Unknown command '{command}'.");
            PrintUsage();
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
                Console.WriteLine(JsonConvert.SerializeObject(errorResult, Formatting.None));
            }
            else
            {
                Console.Error.WriteLine($"[error] {parseError}");
                PrintUsage();
            }

            return 2;
        }

        Action<string>? logger = options!.Diagnostics
            ? msg => Console.Error.WriteLine(msg)
            : null;

        var discovery = new RevitInstallationDiscovery();
        var launcher = new StandardProcessLauncher();
        var clientFactory = new DiagnosticPipeClientFactory(logger);
        var runner = new OneShotTestRunner(
            discovery,
            launcher,
            clientFactory,
            diagnosticLogger: logger);

        using var cts = new CancellationTokenSource();
        ConsoleCancelEventHandler cancelHandler = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cts.Cancel();
        };
        Console.CancelKeyPress += cancelHandler;

        try
        {
            OneShotRunResult result = await runner.RunAsync(options, cts.Token).ConfigureAwait(false);

            if (options.JsonOutput)
            {
                Console.WriteLine(JsonConvert.SerializeObject(result, Formatting.None));
            }
            else if (result.Success)
            {
                Console.WriteLine(
                    $"[RevitWorkerPool] Revit {result.Version} run completed: " +
                    $"Total={result.TotalTests}, Passed={result.PassedTests}, Failed={result.FailedTests}, " +
                    $"Skipped={result.SkippedTests}, WorkerId={string.Join(",", result.WorkerIds)}, TRX='{result.TrxPath}'");
            }
            else
            {
                Console.Error.WriteLine(
                    $"[RevitWorkerPool] Revit {result.Version} run failed: {result.Error ?? $"{result.FailedTests} test(s) failed"}");
            }

            return result.ExitCode;
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
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
        int? timeoutSeconds = null;
        int? bootTimeoutSeconds = null;
        string? workerId = null;
        string? projectRoot = null;
        bool jsonOutput = false;
        bool oneShot = true;
        bool diagnostics = false;

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
            BootTimeout = bootTimeout,
            ExecutionTimeout = executionTimeout,
            WorkerId = workerId,
            ProjectRoot = projectRoot,
            JsonOutput = jsonOutput,
            OneShot = oneShot,
            Diagnostics = diagnostics
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

    private static void PrintUsage()
    {
        Console.Error.WriteLine(
            "Usage: RevitWorkerPool run --version <Year> --assembly <DllPath> --trx <TrxPath> " +
            "[--filter <Fragment>] [--timeout-seconds <Sec>] [--boot-timeout-seconds <Sec>] " +
            "[--worker-id <Id>] [--project-root <Path>] [--one-shot] [--json] [--diagnostics]");
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
