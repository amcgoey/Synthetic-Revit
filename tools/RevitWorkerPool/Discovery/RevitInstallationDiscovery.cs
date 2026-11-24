using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace RevitWorkerPool.Discovery;

/// <summary>
/// Represents an installed Autodesk Revit version and its executable path.
/// </summary>
public sealed record RevitInstallation(int Version, string InstallDirectory, string ExecutablePath);

/// <summary>
/// Represents an entry read from the Windows Uninstall registry keys.
/// </summary>
public sealed record UninstallRegistryEntry(string DisplayName, string? InstallLocation);

/// <summary>
/// Dynamically discovers installed Autodesk Revit versions and repository configuration
/// without hardcoded year lists.
/// </summary>
public sealed class RevitInstallationDiscovery
{
    private static readonly Regex DirectoryYearRegex = new(
        @"^Revit\s+(20\d{2})$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex RegistryDisplayYearRegex = new(
        @"\bRevit\s+(20\d{2})\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex CanonicalRevitDisplayRegex = new(
        @"^(?:Autodesk\s+)?Revit\s+(20\d{2})$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex TableLatestVersionRegex = new(
        @"\|\s*`?CURRENT_LATEST_VERSION`?\s*\|\s*[`""]?(20\d{2})[`""]?\s*\|",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex InlineLatestVersionRegex = new(
        @"\bCURRENT_LATEST_VERSION\b[^\r\n]*?\b(20\d{2})\b",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private readonly Func<IEnumerable<string>> _autodeskRootDirectoriesProvider;
    private readonly Func<IEnumerable<UninstallRegistryEntry>> _uninstallEntriesProvider;

    /// <summary>
    /// Initializes discovery using default Windows Program Files and Uninstall registry providers.
    /// </summary>
    public RevitInstallationDiscovery()
        : this(GetDefaultAutodeskRootDirectories, ReadDefaultUninstallEntries)
    {
    }

    /// <summary>
    /// Initializes discovery with injectable directory and registry providers for testing.
    /// </summary>
    public RevitInstallationDiscovery(
        Func<IEnumerable<string>> autodeskRootDirectoriesProvider,
        Func<IEnumerable<UninstallRegistryEntry>> uninstallEntriesProvider)
    {
        _autodeskRootDirectoriesProvider = autodeskRootDirectoriesProvider
            ?? throw new ArgumentNullException(nameof(autodeskRootDirectoriesProvider));
        _uninstallEntriesProvider = uninstallEntriesProvider
            ?? throw new ArgumentNullException(nameof(uninstallEntriesProvider));
    }

    /// <summary>
    /// Discovers all installed Revit versions sorted ascending by version year.
    /// </summary>
    public IReadOnlyList<RevitInstallation> DiscoverInstalledVersions()
    {
        var bestByVersion = new Dictionary<int, (int Priority, RevitInstallation Installation)>();

        foreach (string rootDir in _autodeskRootDirectoriesProvider())
        {
            if (string.IsNullOrWhiteSpace(rootDir) || !Directory.Exists(rootDir))
            {
                continue;
            }

            IEnumerable<string> subDirs;
            try
            {
                subDirs = Directory.EnumerateDirectories(rootDir);
            }
            catch (IOException)
            {
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            foreach (string subDir in subDirs)
            {
                string folderName = Path.GetFileName(subDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                Match match = DirectoryYearRegex.Match(folderName);
                if (!match.Success ||
                    !int.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int version))
                {
                    continue;
                }

                TryRecordCandidate(bestByVersion, version, subDir, priority: 2);
            }
        }

        foreach (UninstallRegistryEntry entry in _uninstallEntriesProvider())
        {
            if (string.IsNullOrWhiteSpace(entry.DisplayName) || string.IsNullOrWhiteSpace(entry.InstallLocation))
            {
                continue;
            }

            Match match = RegistryDisplayYearRegex.Match(entry.DisplayName.Trim());
            if (!match.Success ||
                !int.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int version))
            {
                continue;
            }

            int priority = CanonicalRevitDisplayRegex.IsMatch(entry.DisplayName.Trim()) ? 3 : 1;
            TryRecordCandidate(bestByVersion, version, entry.InstallLocation, priority);
        }

        return bestByVersion.Values
            .Select(item => item.Installation)
            .OrderBy(item => item.Version)
            .ToArray();
    }

    /// <summary>
    /// Finds an installed Revit version, returning null if it is not installed.
    /// </summary>
    public RevitInstallation? TryFindInstallation(int version)
    {
        return DiscoverInstalledVersions().FirstOrDefault(item => item.Version == version);
    }

    /// <summary>
    /// Finds an installed Revit version or throws a clear <see cref="DirectoryNotFoundException"/>.
    /// </summary>
    public RevitInstallation GetInstallation(int version)
    {
        RevitInstallation? installation = TryFindInstallation(version);
        if (installation is null)
        {
            IReadOnlyList<RevitInstallation> installed = DiscoverInstalledVersions();
            string available = installed.Count == 0
                ? "none"
                : string.Join(", ", installed.Select(i => i.Version));
            throw new DirectoryNotFoundException(
                $"Autodesk Revit {version} installation was not found. Discovered versions: {available}.");
        }

        return installation;
    }

    /// <summary>
    /// Walks upward from <paramref name="startDirectory"/> (or CWD / application base directory)
    /// to find the repository root containing a <c>.git</c> directory or worktree file.
    /// </summary>
    public static string? FindRepositoryRoot(string? startDirectory = null)
    {
        if (!string.IsNullOrWhiteSpace(startDirectory))
        {
            return WalkUpForGitMarker(startDirectory);
        }

        return WalkUpForGitMarker(Directory.GetCurrentDirectory())
            ?? WalkUpForGitMarker(AppContext.BaseDirectory);
    }

    /// <summary>
    /// Reads <c>CURRENT_LATEST_VERSION</c> dynamically from <c>docs/agents/revit-config.md</c>.
    /// </summary>
    public static int ReadCurrentLatestVersion(string? startDirectory = null)
    {
        string? repoRoot = FindRepositoryRoot(startDirectory);
        if (string.IsNullOrWhiteSpace(repoRoot))
        {
            throw new DirectoryNotFoundException(
                "Could not locate repository root (.git) to read docs/agents/revit-config.md.");
        }

        string configPath = Path.Combine(repoRoot, "docs", "agents", "revit-config.md");
        if (!File.Exists(configPath))
        {
            throw new FileNotFoundException(
                $"Expected Revit configuration file at '{configPath}'.",
                configPath);
        }

        string content = File.ReadAllText(configPath);
        return ParseCurrentLatestVersion(content);
    }

    /// <summary>
    /// Parses <c>CURRENT_LATEST_VERSION</c> from markdown content of <c>revit-config.md</c>.
    /// </summary>
    public static int ParseCurrentLatestVersion(string markdownContent)
    {
        if (string.IsNullOrWhiteSpace(markdownContent))
        {
            throw new InvalidOperationException("revit-config.md content is empty.");
        }

        Match tableMatch = TableLatestVersionRegex.Match(markdownContent);
        if (tableMatch.Success &&
            int.TryParse(tableMatch.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int tableVersion))
        {
            return tableVersion;
        }

        Match inlineMatch = InlineLatestVersionRegex.Match(markdownContent);
        if (inlineMatch.Success &&
            int.TryParse(inlineMatch.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int inlineVersion))
        {
            return inlineVersion;
        }

        throw new InvalidOperationException(
            "Failed to parse CURRENT_LATEST_VERSION from docs/agents/revit-config.md.");
    }

    private static string? WalkUpForGitMarker(string startingPath)
    {
        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(startingPath);
        }
        catch (Exception)
        {
            return null;
        }

        if (File.Exists(fullPath))
        {
            fullPath = Path.GetDirectoryName(fullPath) ?? fullPath;
        }

        var current = new DirectoryInfo(fullPath);
        while (current is not null)
        {
            string gitMarker = Path.Combine(current.FullName, ".git");
            if (Directory.Exists(gitMarker) || File.Exists(gitMarker))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        return null;
    }

    private static void TryRecordCandidate(
        IDictionary<int, (int Priority, RevitInstallation Installation)> bestByVersion,
        int version,
        string rawInstallDir,
        int priority)
    {
        string normalizedDir;
        try
        {
            normalizedDir = Path.GetFullPath(rawInstallDir.Trim().Trim('"'))
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch (Exception)
        {
            return;
        }

        string exePath = Path.Combine(normalizedDir, "Revit.exe");
        if (!File.Exists(exePath))
        {
            return;
        }

        if (!bestByVersion.TryGetValue(version, out var existing) || priority > existing.Priority)
        {
            bestByVersion[version] = (priority, new RevitInstallation(version, normalizedDir, exePath));
        }
    }

    private static IEnumerable<string> GetDefaultAutodeskRootDirectories()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddRoot(string? programFilesDir)
        {
            if (string.IsNullOrWhiteSpace(programFilesDir))
            {
                return;
            }

            string autodeskDir = Path.Combine(programFilesDir, "Autodesk");
            seen.Add(autodeskDir);
        }

        AddRoot(Environment.GetEnvironmentVariable("ProgramW6432"));
        AddRoot(Environment.GetEnvironmentVariable("ProgramFiles"));
        AddRoot(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));

        return seen;
    }

    [SupportedOSPlatform("windows")]
    private static IEnumerable<UninstallRegistryEntry> ReadDefaultUninstallEntries()
    {
        var results = new List<UninstallRegistryEntry>();
        const string uninstallPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

        ReadUninstallHive(RegistryHive.LocalMachine, RegistryView.Registry64, uninstallPath, results);
        ReadUninstallHive(RegistryHive.LocalMachine, RegistryView.Registry32, uninstallPath, results);
        ReadUninstallHive(RegistryHive.CurrentUser, RegistryView.Default, uninstallPath, results);

        return results;
    }

    [SupportedOSPlatform("windows")]
    private static void ReadUninstallHive(
        RegistryHive hive,
        RegistryView view,
        string subKeyPath,
        ICollection<UninstallRegistryEntry> results)
    {
        try
        {
            using RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, view);
            using RegistryKey? uninstallKey = baseKey.OpenSubKey(subKeyPath, writable: false);
            if (uninstallKey is null)
            {
                return;
            }

            foreach (string subKeyName in uninstallKey.GetSubKeyNames())
            {
                try
                {
                    using RegistryKey? itemKey = uninstallKey.OpenSubKey(subKeyName, writable: false);
                    if (itemKey is null)
                    {
                        continue;
                    }

                    string? displayName = itemKey.GetValue("DisplayName") as string;
                    if (string.IsNullOrWhiteSpace(displayName))
                    {
                        continue;
                    }

                    string? installLocation = itemKey.GetValue("InstallLocation") as string;
                    results.Add(new UninstallRegistryEntry(displayName, installLocation));
                }
                catch (Exception)
                {
                    // Skip unreadable subkeys.
                }
            }
        }
        catch (Exception)
        {
            // Skip inaccessible registry hives.
        }
    }
}
