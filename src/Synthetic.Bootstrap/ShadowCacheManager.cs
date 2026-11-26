using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;

namespace Synthetic.Bootstrap
{
    /// <summary>
    /// Describes the launch strategy computed for the add-in payload.
    /// </summary>
    public enum LaunchMode
    {
        TestWorkerBypass,
        DirectInPlace,
        ShadowCopy,
        Failed
    }

    /// <summary>
    /// Immutable launch plan produced by <see cref="ShadowCacheManager"/>.
    /// </summary>
    public sealed class LaunchPlan
    {
        public LaunchPlan(
            LaunchMode mode,
            string sourcePayloadDir,
            string shadowDir,
            string targetAssemblyPath,
            string errorMessage,
            IReadOnlyList<string>? diagnostics = null)
        {
            Mode = mode;
            SourcePayloadDir = sourcePayloadDir ?? string.Empty;
            ShadowDir = shadowDir ?? string.Empty;
            TargetAssemblyPath = targetAssemblyPath ?? string.Empty;
            ErrorMessage = errorMessage ?? string.Empty;
            Diagnostics = diagnostics ?? Array.Empty<string>();
        }

        public LaunchMode Mode { get; }

        public string SourcePayloadDir { get; }

        public string ShadowDir { get; }

        public string TargetAssemblyPath { get; }

        public string ErrorMessage { get; }

        public IReadOnlyList<string> Diagnostics { get; }
    }

    /// <summary>
    /// Optional injectable environment/process/filesystem hooks for deterministic headless testing.
    /// </summary>
    public sealed class ShadowCacheHooks
    {
        public Func<string, string?>? GetEnvironmentVariable { get; set; }

        public string? LocalAppDataDir { get; set; }

        public string? PayloadDirOverride { get; set; }

        public int? CurrentProcessId { get; set; }

        public long? CurrentProcessUtcStartTicks { get; set; }

        public Func<int, long>? GetProcessUtcStartTicks { get; set; }

        public Action<string, string>? CopyFile { get; set; }

        public Action<string>? WriteJournalComment { get; set; }
    }

    /// <summary>
    /// Pure BCL shadow-cache launch planner and directory stager for the Synthetic Add-In Bootstrap.
    /// Does not depend on Autodesk.Revit.* and does not mutate global AppContext state.
    /// </summary>
    public static class ShadowCacheManager
    {
        private const string TestWorkerEnvVar = "SYNTHETIC_TEST_WORKER";
        private const string DisableShadowCopyEnvVar = "SYNTHETIC_DISABLE_SHADOW_COPY";
        private const string RevitVersionMetadataKey = "RevitVersion";

        /// <summary>
        /// Resolves the user-local diagnostic logs directory (<c>%LOCALAPPDATA%\Synthetic\Logs</c>)
        /// independent of <c>SYNTHETIC_ADDIN_ROOT</c>.
        /// </summary>
        public static string ResolveDiagnosticLogsDir(string? localAppDataOverride = null)
        {
            string localAppData = !string.IsNullOrWhiteSpace(localAppDataOverride)
                ? localAppDataOverride!
                : Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            if (string.IsNullOrWhiteSpace(localAppData))
            {
                return string.Empty;
            }

            return Path.Combine(localAppData, "Synthetic", "Logs");
        }

        /// <summary>
        /// Resolves the Revit release year from <paramref name="hostVersionNumber"/> first, falling back
        /// to compile-time <c>RevitVersion</c> metadata on <paramref name="metadataAssembly"/>, and stages
        /// the host launch plan.
        /// </summary>
        public static LaunchPlan PrepareHostLaunch(
            string? hostVersionNumber,
            Assembly metadataAssembly,
            string bootstrapAssemblyPath,
            out string resolvedRevitVersion,
            ShadowCacheHooks? hooks = null)
        {
            if (metadataAssembly == null)
            {
                throw new ArgumentNullException(nameof(metadataAssembly));
            }

            if (!TryResolveRevitVersion(hostVersionNumber, metadataAssembly, out resolvedRevitVersion, out string versionError))
            {
                resolvedRevitVersion = string.Empty;
                return new LaunchPlan(
                    LaunchMode.Failed,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    versionError,
                    new[]
                    {
                        $"HostVersionNumber: {hostVersionNumber ?? "<null>"}",
                        versionError
                    });
            }

            return PrepareLaunch(bootstrapAssemblyPath, resolvedRevitVersion, hooks);
        }

        /// <summary>
        /// Resolves a four-digit Revit release year from <paramref name="hostVersionNumber"/> first,
        /// falling back to compile-time <c>RevitVersion</c> metadata on <paramref name="metadataAssembly"/>.
        /// </summary>
        public static bool TryResolveRevitVersion(
            string? hostVersionNumber,
            Assembly metadataAssembly,
            out string revitVersion,
            out string errorMessage)
        {
            if (metadataAssembly == null)
            {
                throw new ArgumentNullException(nameof(metadataAssembly));
            }

            if (TryParseRevitYear(hostVersionNumber, out string hostYear))
            {
                revitVersion = hostYear;
                errorMessage = string.Empty;
                return true;
            }

            return TryReadRevitVersionMetadata(metadataAssembly, out revitVersion, out errorMessage);
        }

        /// <summary>
        /// Reads and validates the compile-time <c>RevitVersion</c> assembly metadata attribute on <paramref name="assembly"/>.
        /// </summary>
        public static bool TryReadRevitVersionMetadata(Assembly assembly, out string revitVersion, out string errorMessage)
        {
            if (assembly == null)
            {
                throw new ArgumentNullException(nameof(assembly));
            }

            revitVersion = string.Empty;
            errorMessage = string.Empty;

            AssemblyMetadataAttribute? metadata = null;
            foreach (AssemblyMetadataAttribute attr in assembly.GetCustomAttributes<AssemblyMetadataAttribute>())
            {
                if (string.Equals(attr.Key, RevitVersionMetadataKey, StringComparison.Ordinal))
                {
                    metadata = attr;
                    break;
                }
            }

            string assemblyName = assembly.GetName().Name ?? assembly.FullName ?? "<unknown>";
            if (metadata == null)
            {
                errorMessage =
                    $"Assembly '{assemblyName}' is missing required [AssemblyMetadata(\"{RevitVersionMetadataKey}\", \"<Year>\")] attribute.";
                return false;
            }

            if (!TryParseRevitYear(metadata.Value, out string parsedYear))
            {
                errorMessage =
                    $"Assembly '{assemblyName}' has unparseable '{RevitVersionMetadataKey}' metadata value '{metadata.Value ?? "<null>"}'. Expected a four-digit Revit release year.";
                return false;
            }

            revitVersion = parsedYear;
            return true;
        }

        /// <summary>
        /// Validates and normalizes a four-digit Revit release year (e.g. <c>"2026"</c>).
        /// </summary>
        public static bool TryParseRevitYear(string? rawValue, out string normalizedYear)
        {
            normalizedYear = string.Empty;
            if (string.IsNullOrWhiteSpace(rawValue))
            {
                return false;
            }

            string trimmed = rawValue!.Trim();
            if (trimmed.Length != 4)
            {
                return false;
            }

            if (!int.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out int parsed) ||
                parsed < 2000 ||
                parsed > 9999)
            {
                return false;
            }

            normalizedYear = parsed.ToString(CultureInfo.InvariantCulture);
            return true;
        }

        /// <summary>
        /// Evaluates launch mode, scavenges stale sibling shadow directories, and stages the payload if needed.
        /// </summary>
        /// <param name="bootstrapAssemblyPath">Full path to the loaded Synthetic.Bootstrap assembly (or its directory).</param>
        /// <param name="revitVersion">Four-digit Revit release year (e.g. "2026").</param>
        /// <param name="hooks">Optional hooks for headless unit testing.</param>
        public static LaunchPlan PrepareLaunch(
            string bootstrapAssemblyPath,
            string revitVersion,
            ShadowCacheHooks? hooks = null)
        {
            var diagnostics = new List<string>();
            Func<string, string?> getEnv = hooks?.GetEnvironmentVariable ?? Environment.GetEnvironmentVariable;

            string? testWorker = getEnv(TestWorkerEnvVar);
            if (string.Equals(testWorker?.Trim(), "1", StringComparison.Ordinal))
            {
                diagnostics.Add("Bypassed payload staging because SYNTHETIC_TEST_WORKER=1.");
                return new LaunchPlan(
                    LaunchMode.TestWorkerBypass,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    diagnostics);
            }

            if (string.IsNullOrWhiteSpace(revitVersion))
            {
                return new LaunchPlan(
                    LaunchMode.Failed,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    "Revit version could not be determined for bootstrap launch.",
                    diagnostics);
            }

            string normalizedVersion = revitVersion.Trim();
            string payloadDir;
            try
            {
                payloadDir = ResolvePayloadDir(bootstrapAssemblyPath, normalizedVersion, hooks?.PayloadDirOverride);
            }
            catch (Exception ex)
            {
                return new LaunchPlan(
                    LaunchMode.Failed,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    $"Failed to resolve payload directory: {ex.Message}",
                    diagnostics);
            }

            string payloadDll = Path.Combine(payloadDir, $"Synthetic{normalizedVersion}.dll");

            string? disableShadow = getEnv(DisableShadowCopyEnvVar);
            if (string.Equals(disableShadow?.Trim(), "1", StringComparison.Ordinal))
            {
                if (!File.Exists(payloadDll))
                {
                    return new LaunchPlan(
                        LaunchMode.Failed,
                        payloadDir,
                        string.Empty,
                        string.Empty,
                        $"Payload assembly not found for in-place launch at '{payloadDll}'.",
                        diagnostics);
                }

                diagnostics.Add($"Shadow copy disabled ({DisableShadowCopyEnvVar}=1); loading in-place from '{payloadDll}'.");
                return new LaunchPlan(
                    LaunchMode.DirectInPlace,
                    payloadDir,
                    string.Empty,
                    payloadDll,
                    string.Empty,
                    diagnostics);
            }

            if (!File.Exists(payloadDll))
            {
                return new LaunchPlan(
                    LaunchMode.Failed,
                    payloadDir,
                    string.Empty,
                    string.Empty,
                    $"Payload assembly not found at '{payloadDll}'.",
                    diagnostics);
            }

            string localAppData = !string.IsNullOrWhiteSpace(hooks?.LocalAppDataDir)
                ? hooks!.LocalAppDataDir!
                : Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            if (string.IsNullOrWhiteSpace(localAppData))
            {
                return new LaunchPlan(
                    LaunchMode.Failed,
                    payloadDir,
                    string.Empty,
                    string.Empty,
                    "LocalApplicationData directory could not be resolved for shadow cache staging.",
                    diagnostics);
            }

            int currentPid;
            long currentTicks;
            if (hooks?.CurrentProcessId.HasValue == true && hooks?.CurrentProcessUtcStartTicks.HasValue == true)
            {
                currentPid = hooks.CurrentProcessId.Value;
                currentTicks = hooks.CurrentProcessUtcStartTicks.Value;
            }
            else
            {
                using (Process currentProc = Process.GetCurrentProcess())
                {
                    currentPid = hooks?.CurrentProcessId ?? currentProc.Id;
                    currentTicks = hooks?.CurrentProcessUtcStartTicks ?? currentProc.StartTime.ToUniversalTime().Ticks;
                }
            }

            string yearCacheRoot = Path.Combine(localAppData, "Synthetic", "ShadowCache", normalizedVersion);
            string currentFolderName = $"{currentPid}_{currentTicks}";
            string shadowDir = Path.Combine(yearCacheRoot, currentFolderName);

            Func<int, long> getProcTicks = hooks?.GetProcessUtcStartTicks ?? DefaultGetProcessUtcStartTicks;
            ScavengeSiblingFolders(yearCacheRoot, currentFolderName, getProcTicks, diagnostics);

            Action<string, string> copyFile = hooks?.CopyFile ?? DefaultCopyFile;
            try
            {
                ShadowCopyDirectory(payloadDir, shadowDir, copyFile);
            }
            catch (Exception ex)
            {
                TryDeleteDirectoryBestEffort(shadowDir, diagnostics);
                return new LaunchPlan(
                    LaunchMode.Failed,
                    payloadDir,
                    string.Empty,
                    string.Empty,
                    $"Failed to stage shadow copy from '{payloadDir}' to '{shadowDir}': {ex.Message}",
                    diagnostics);
            }

            string targetDll = Path.Combine(shadowDir, $"Synthetic{normalizedVersion}.dll");
            diagnostics.Add($"Staged shadow copy in '{shadowDir}'.");

            return new LaunchPlan(
                LaunchMode.ShadowCopy,
                payloadDir,
                shadowDir,
                targetDll,
                string.Empty,
                diagnostics);
        }

        private static string ResolvePayloadDir(string bootstrapAssemblyPath, string revitVersion, string? payloadDirOverride)
        {
            if (!string.IsNullOrWhiteSpace(payloadDirOverride))
            {
                return Path.GetFullPath(payloadDirOverride);
            }

            if (string.IsNullOrWhiteSpace(bootstrapAssemblyPath))
            {
                throw new ArgumentException("Bootstrap assembly path cannot be empty.", nameof(bootstrapAssemblyPath));
            }

            string? bootstrapDir = Directory.Exists(bootstrapAssemblyPath)
                ? bootstrapAssemblyPath
                : Path.GetDirectoryName(bootstrapAssemblyPath);

            if (string.IsNullOrWhiteSpace(bootstrapDir))
            {
                throw new InvalidOperationException($"Unable to determine directory from bootstrap path '{bootstrapAssemblyPath}'.");
            }

            return Path.GetFullPath(Path.Combine(bootstrapDir, "..", "..", "Payload", revitVersion));
        }

        private static void ScavengeSiblingFolders(
            string yearCacheRoot,
            string currentFolderName,
            Func<int, long> getProcessUtcStartTicks,
            List<string> diagnostics)
        {
            if (!Directory.Exists(yearCacheRoot))
            {
                return;
            }

            string[] siblingDirs;
            try
            {
                siblingDirs = Directory.GetDirectories(yearCacheRoot);
            }
            catch (Exception ex)
            {
                diagnostics.Add($"Unable to enumerate shadow cache directory '{yearCacheRoot}': {ex.Message}");
                return;
            }

            foreach (string siblingDir in siblingDirs)
            {
                string folderName = Path.GetFileName(siblingDir);
                if (string.IsNullOrEmpty(folderName) ||
                    string.Equals(folderName, currentFolderName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!TryParseSessionFolderName(folderName, out int siblingPid, out long siblingTicks))
                {
                    continue;
                }

                bool shouldDelete = ShouldDeleteSiblingFolder(siblingPid, siblingTicks, folderName, getProcessUtcStartTicks, diagnostics);
                if (shouldDelete)
                {
                    TryDeleteDirectoryBestEffort(siblingDir, diagnostics);
                }
            }
        }

        private static bool TryParseSessionFolderName(string folderName, out int pid, out long utcTicks)
        {
            pid = 0;
            utcTicks = 0;

            int separatorIdx = folderName.IndexOf('_');
            if (separatorIdx <= 0 || separatorIdx >= folderName.Length - 1)
            {
                return false;
            }

            // Ensure there is only a single underscore separating <PID>_<UtcStartTicks>
            if (folderName.IndexOf('_', separatorIdx + 1) >= 0)
            {
                return false;
            }

            string pidPart = folderName.Substring(0, separatorIdx);
            string ticksPart = folderName.Substring(separatorIdx + 1);

            return int.TryParse(pidPart, out pid) && pid > 0 && long.TryParse(ticksPart, out utcTicks);
        }

        private static bool ShouldDeleteSiblingFolder(
            int siblingPid,
            long siblingTicks,
            string folderName,
            Func<int, long> getProcessUtcStartTicks,
            List<string> diagnostics)
        {
            try
            {
                long liveTicks = getProcessUtcStartTicks(siblingPid);
                if (liveTicks != siblingTicks)
                {
                    diagnostics.Add($"Scavenging '{folderName}': PID {siblingPid} reused (ticks {liveTicks} != {siblingTicks}).");
                    return true;
                }

                diagnostics.Add($"Preserving live session '{folderName}' (PID {siblingPid} ticks match).");
                return false;
            }
            catch (ArgumentException)
            {
                diagnostics.Add($"Scavenging '{folderName}': PID {siblingPid} has exited.");
                return true;
            }
            catch (InvalidOperationException)
            {
                diagnostics.Add($"Scavenging '{folderName}': PID {siblingPid} exited during inspection.");
                return true;
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 5)
            {
                diagnostics.Add($"Conservatively preserving '{folderName}': AccessDenied inspecting PID {siblingPid}.");
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                diagnostics.Add($"Conservatively preserving '{folderName}': UnauthorizedAccess inspecting PID {siblingPid}.");
                return false;
            }
            catch (Win32Exception ex)
            {
                diagnostics.Add($"Conservatively preserving '{folderName}': Win32Error ({ex.NativeErrorCode}) inspecting PID {siblingPid}.");
                return false;
            }
        }

        private static long DefaultGetProcessUtcStartTicks(int pid)
        {
            using (Process proc = Process.GetProcessById(pid))
            {
                return proc.StartTime.ToUniversalTime().Ticks;
            }
        }

        private static void DefaultCopyFile(string sourcePath, string destinationPath)
        {
            File.Copy(sourcePath, destinationPath, overwrite: true);
        }

        private static void ShadowCopyDirectory(string sourceDir, string destinationDir, Action<string, string> copyFile)
        {
            Directory.CreateDirectory(destinationDir);

            foreach (string subDir in Directory.GetDirectories(sourceDir, "*", SearchOption.AllDirectories))
            {
                string relativeDir = GetRelativePathCompat(sourceDir, subDir);
                Directory.CreateDirectory(Path.Combine(destinationDir, relativeDir));
            }

            foreach (string srcFile in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
            {
                string relativeFile = GetRelativePathCompat(sourceDir, srcFile);
                string dstFile = Path.Combine(destinationDir, relativeFile);
                string? dstParent = Path.GetDirectoryName(dstFile);
                if (!string.IsNullOrEmpty(dstParent))
                {
                    Directory.CreateDirectory(dstParent);
                }

                copyFile(srcFile, dstFile);
            }
        }

        private static string GetRelativePathCompat(string basePath, string targetPath)
        {
            string normalizedBase = Path.GetFullPath(basePath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            string normalizedTarget = Path.GetFullPath(targetPath);

            if (normalizedTarget.StartsWith(normalizedBase, StringComparison.OrdinalIgnoreCase))
            {
                return normalizedTarget.Substring(normalizedBase.Length);
            }

            throw new InvalidOperationException($"Path '{targetPath}' is outside base directory '{basePath}'.");
        }

        private static void TryDeleteDirectoryBestEffort(string directoryPath, List<string> diagnostics)
        {
            try
            {
                if (Directory.Exists(directoryPath))
                {
                    Directory.Delete(directoryPath, recursive: true);
                }
            }
            catch (Exception ex)
            {
                diagnostics.Add($"Could not delete '{directoryPath}': {ex.Message}");
            }
        }
    }
}
