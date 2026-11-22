using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
#if !NETFRAMEWORK
using System.Runtime.Loader;
#endif
using Autodesk.Revit.UI;

namespace Synthetic.Bootstrap
{
    /// <summary>
    /// Universal Revit IExternalApplication bootstrapper.
    /// Bypasses payload loading in automated test workers and otherwise stages the payload
    /// into a per-process shadow cache before loading it into the default CLR context.
    /// </summary>
    public class BootstrapApp : IExternalApplication
    {
        private static readonly object ResolverLock = new object();
        private static bool _resolverRegistered;
        private static string _activeTargetDir = string.Empty;
#if !NETFRAMEWORK
        private static AssemblyDependencyResolver? _dependencyResolver;
#endif

        [ThreadStatic]
        private static bool _resolvingAssembly;

        private IExternalApplication? _innerApp;

        /// <inheritdoc />
        public Result OnStartup(UIControlledApplication appControlled)
        {
            string? testWorker = Environment.GetEnvironmentVariable("SYNTHETIC_TEST_WORKER");
            if (string.Equals(testWorker?.Trim(), "1", StringComparison.Ordinal))
            {
                string? runId = Environment.GetEnvironmentVariable("SYNTHETIC_TEST_RUN_ID");
                if (!string.IsNullOrWhiteSpace(runId))
                {
                    TryWriteJournalComment(appControlled, $"SYNTHETIC_TEST_RUN:{runId.Trim()}");
                }

                return Result.Succeeded;
            }

            string revitVersion = ResolveRevitVersion(appControlled);
            string bootstrapPath = typeof(BootstrapApp).Assembly.Location;
            LaunchPlan plan = ShadowCacheManager.PrepareLaunch(bootstrapPath, revitVersion);

            if (plan.Mode == LaunchMode.Failed)
            {
                WriteBootstrapDiagnosticLog(revitVersion, plan.ErrorMessage, plan.Diagnostics, exception: null);
                TryWriteJournalComment(appControlled, $"SYNTHETIC_BOOTSTRAP_ERROR: {plan.ErrorMessage}");
                return Result.Failed;
            }

#if NETFRAMEWORK
            AppDomain.CurrentDomain.SetData("SYNTHETIC_ADDIN_ROOT", plan.SourcePayloadDir);
#else
            AppContext.SetData("SYNTHETIC_ADDIN_ROOT", plan.SourcePayloadDir);
#endif

            try
            {
                Assembly payloadAssembly = LoadPayloadIntoDefaultContext(plan.TargetAssemblyPath);
                Type hostType = DiscoverHostApplicationType(payloadAssembly);

                IExternalApplication? hostApp = Activator.CreateInstance(hostType) as IExternalApplication;
                if (hostApp == null)
                {
                    throw new InvalidOperationException($"Type '{hostType.FullName}' did not instantiate as IExternalApplication.");
                }

                Result startupResult = hostApp.OnStartup(appControlled);
                if (startupResult == Result.Succeeded)
                {
                    _innerApp = hostApp;
                }

                TryWriteJournalComment(
                    appControlled,
                    $"SYNTHETIC_BOOTSTRAP: Loaded {plan.TargetAssemblyPath} (Root={plan.SourcePayloadDir}, Mode={plan.Mode})");

                return startupResult;
            }
            catch (Exception ex)
            {
                if (plan.Mode == LaunchMode.ShadowCopy && !string.IsNullOrEmpty(plan.ShadowDir))
                {
                    TryDeleteDirectoryBestEffort(plan.ShadowDir);
                }

                WriteBootstrapDiagnosticLog(revitVersion, ex.Message, plan.Diagnostics, ex);
                TryWriteJournalComment(appControlled, $"SYNTHETIC_BOOTSTRAP_ERROR: {ex.Message}");
                return Result.Failed;
            }
        }

        /// <inheritdoc />
        public Result OnShutdown(UIControlledApplication appControlled)
        {
            if (_innerApp == null)
            {
                return Result.Succeeded;
            }

            return _innerApp.OnShutdown(appControlled);
        }

        private static string ResolveRevitVersion(UIControlledApplication? appControlled)
        {
            string? versionNumber = appControlled?.ControlledApplication?.VersionNumber;
            if (!string.IsNullOrWhiteSpace(versionNumber))
            {
                return versionNumber.Trim();
            }

#if REVIT2023
            return "2023";
#elif REVIT2024
            return "2024";
#elif REVIT2025
            return "2025";
#else
            return "2026";
#endif
        }

        private static Assembly LoadPayloadIntoDefaultContext(string targetAssemblyPath)
        {
            string targetDir = Path.GetDirectoryName(targetAssemblyPath) ?? string.Empty;

#if NETFRAMEWORK
            lock (ResolverLock)
            {
                _activeTargetDir = targetDir;
                if (!_resolverRegistered)
                {
                    AppDomain.CurrentDomain.AssemblyResolve += OnNetFrameworkAssemblyResolve;
                    _resolverRegistered = true;
                }
            }

            return Assembly.LoadFrom(targetAssemblyPath);
#else
            lock (ResolverLock)
            {
                _activeTargetDir = targetDir;
                _dependencyResolver = new AssemblyDependencyResolver(targetAssemblyPath);
                if (!_resolverRegistered)
                {
                    AssemblyLoadContext.Default.Resolving += OnDefaultContextResolving;
                    _resolverRegistered = true;
                }
            }

            return AssemblyLoadContext.Default.LoadFromAssemblyPath(targetAssemblyPath);
#endif
        }

#if NETFRAMEWORK
        private static Assembly? OnNetFrameworkAssemblyResolve(object? sender, ResolveEventArgs args)
        {
            if (_resolvingAssembly || string.IsNullOrEmpty(args.Name))
            {
                return null;
            }

            try
            {
                _resolvingAssembly = true;
                AssemblyName requestedName = new AssemblyName(args.Name);
                if (string.IsNullOrEmpty(requestedName.Name))
                {
                    return null;
                }

                foreach (Assembly loaded in AppDomain.CurrentDomain.GetAssemblies())
                {
                    AssemblyName loadedName = loaded.GetName();
                    if (string.Equals(loadedName.Name, requestedName.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        return loaded;
                    }
                }

                string targetDir = _activeTargetDir;
                if (!string.IsNullOrEmpty(targetDir))
                {
                    string candidate = Path.Combine(targetDir, requestedName.Name + ".dll");
                    if (File.Exists(candidate))
                    {
                        return Assembly.LoadFrom(candidate);
                    }
                }

                return null;
            }
            finally
            {
                _resolvingAssembly = false;
            }
        }
#else
        private static Assembly? OnDefaultContextResolving(AssemblyLoadContext alc, AssemblyName assemblyName)
        {
            if (_resolvingAssembly || string.IsNullOrEmpty(assemblyName.Name))
            {
                return null;
            }

            try
            {
                _resolvingAssembly = true;

                foreach (Assembly loaded in alc.Assemblies)
                {
                    if (string.Equals(loaded.GetName().Name, assemblyName.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        return loaded;
                    }
                }

                AssemblyDependencyResolver? resolver = _dependencyResolver;
                if (resolver != null)
                {
                    string? resolvedPath = resolver.ResolveAssemblyToPath(assemblyName);
                    if (!string.IsNullOrEmpty(resolvedPath) && File.Exists(resolvedPath))
                    {
                        return alc.LoadFromAssemblyPath(resolvedPath);
                    }
                }

                string targetDir = _activeTargetDir;
                if (!string.IsNullOrEmpty(targetDir))
                {
                    string localCandidate = Path.Combine(targetDir, assemblyName.Name + ".dll");
                    if (File.Exists(localCandidate))
                    {
                        return alc.LoadFromAssemblyPath(localCandidate);
                    }
                }

                return null;
            }
            finally
            {
                _resolvingAssembly = false;
            }
        }
#endif

        private static Type DiscoverHostApplicationType(Assembly payloadAssembly)
        {
            Type? hostType = payloadAssembly.GetType("Synthetic.Platform.Host.App", throwOnError: false);
            if (IsValidExternalApplicationType(hostType))
            {
                return hostType!;
            }

            hostType = payloadAssembly.GetType("Synthetic.Core.App", throwOnError: false);
            if (IsValidExternalApplicationType(hostType))
            {
                return hostType!;
            }

            foreach (Type exportedType in GetExportedTypesSafe(payloadAssembly))
            {
                if (IsValidExternalApplicationType(exportedType))
                {
                    return exportedType;
                }
            }

            throw new InvalidOperationException(
                $"No non-abstract IExternalApplication implementation found in '{payloadAssembly.FullName}'.");
        }

        private static bool IsValidExternalApplicationType(Type? candidate)
        {
            return candidate != null &&
                   candidate.IsClass &&
                   !candidate.IsAbstract &&
                   typeof(IExternalApplication).IsAssignableFrom(candidate);
        }

        private static IEnumerable<Type> GetExportedTypesSafe(Assembly assembly)
        {
            try
            {
                return assembly.GetExportedTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                return ex.Types.Where(t => t != null)!;
            }
        }

        private static void TryWriteJournalComment(UIControlledApplication? appControlled, string comment)
        {
            try
            {
                appControlled?.ControlledApplication?.WriteJournalComment(comment, true);
            }
            catch
            {
                // Best-effort journal telemetry
            }
        }

        private static void TryDeleteDirectoryBestEffort(string directoryPath)
        {
            try
            {
                if (Directory.Exists(directoryPath))
                {
                    Directory.Delete(directoryPath, recursive: true);
                }
            }
            catch
            {
                // Best-effort cleanup; Windows may lock an already-loaded DLL until exit
            }
        }

        private static void WriteBootstrapDiagnosticLog(
            string revitVersion,
            string errorMessage,
            IReadOnlyList<string>? diagnostics,
            Exception? exception)
        {
            try
            {
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                if (string.IsNullOrWhiteSpace(localAppData))
                {
                    return;
                }

                string logsDir = Path.Combine(localAppData, "Synthetic", "Logs");
                Directory.CreateDirectory(logsDir);

                int pid;
                using (Process currentProc = Process.GetCurrentProcess())
                {
                    pid = currentProc.Id;
                }

                string timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff");
                string logFile = Path.Combine(logsDir, $"Synthetic_Bootstrap_{revitVersion}_{timestamp}_{pid}.log");

                var lines = new List<string>
                {
                    $"TimestampUtc: {DateTime.UtcNow:O}",
                    $"RevitVersion: {revitVersion}",
                    $"ProcessId: {pid}",
                    $"Error: {errorMessage}"
                };

                if (diagnostics != null && diagnostics.Count > 0)
                {
                    lines.Add("Diagnostics:");
                    foreach (string diag in diagnostics)
                    {
                        lines.Add($"  - {diag}");
                    }
                }

                if (exception != null)
                {
                    lines.Add("Exception:");
                    lines.Add(exception.ToString());
                }

                File.WriteAllLines(logFile, lines);
            }
            catch
            {
                // Never throw from bootstrap diagnostic logging
            }
        }
    }
}
