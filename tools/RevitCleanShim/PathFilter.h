#pragma once

#include <optional>
#include <string>
#include <string_view>
#include <windows.h>

namespace synthetic::shim
{
    /// <summary>
    /// Normalized path-prefix allowlist filter for clean-launch Revit worker isolation.
    /// Encapsulates environment/Known-Folder root resolution and per-root visibility rules
    /// so Win32 (and future NT) filesystem hooks share one deterministic policy.
    /// </summary>
    class PathFilter
    {
    public:
        static PathFilter& Instance() noexcept;

        /// <summary>
        /// Reads target Revit year and optional root overrides from process environment variables.
        /// Safe to call inside DllMain (uses kernel32 GetEnvironmentVariableW only).
        /// </summary>
        void InitializeFromEnvironment();

        /// <summary>
        /// Ensures Known Folder fallbacks (via SHGetKnownFolderPath) are resolved once outside loader lock.
        /// </summary>
        void EnsureInitialized();

        /// <summary>
        /// Explicit configuration entry point for deterministic testing or custom roots.
        /// </summary>
        void Configure(
            std::wstring_view year,
            std::wstring_view appDataRoot,
            std::wstring_view programDataRoot,
            std::wstring_view programFilesRoot,
            std::wstring_view profileRoot = {});

        bool HasValidYear() const noexcept { return m_hasValidYear; }

        /// <summary>
        /// Returns true if enumerating <paramref name="canonicalDir"/> requires entry-level filtering.
        /// </summary>
        bool IsDirectoryFiltered(std::wstring_view canonicalDir) const;

        /// <summary>
        /// Evaluates whether a path is visible to the shimmed worker process.
        /// </summary>
        bool IsPathAllowed(std::wstring_view rawPath, std::optional<bool> isDirectory = std::nullopt) const;

        /// <summary>
        /// Resolves per-PID virtualized paths for AddInsSettings.json and AdminSettings.json.
        /// Returns true if <paramref name="rawPath"/> must be redirected to <paramref name="virtualPathOut"/>.
        /// </summary>
        bool TryResolveVirtualizedPath(
            std::wstring_view rawPath,
            std::wstring& virtualPathOut,
            std::wstring& seedJsonOut) const;

        /// <summary>
        /// Evaluates whether a FindFirstFile/FindNextFile entry inside <paramref name="canonicalSearchDir"/> is visible.
        /// </summary>
        bool IsFindDataAllowed(std::wstring_view canonicalSearchDir, const WIN32_FIND_DATAW& findData) const;

        /// <summary>
        /// Extracts the canonical parent directory of a FindFirstFile/FindFirstFileEx pattern.
        /// Returns empty if the pattern is a Win32 device namespace path or cannot be normalized.
        /// </summary>
        static std::wstring ExtractSearchDirectory(std::wstring_view rawPattern);

        /// <summary>
        /// Normalizes slashes, resolves relative paths, strips \\?\ prefixes, and removes trailing backslashes.
        /// </summary>
        static std::wstring CanonicalizePath(std::wstring_view rawPath);

        /// <summary>
        /// Converts a canonical Win32 DOS or UNC path into an NT object path (\??\...).
        /// </summary>
        static std::wstring ToNtPath(std::wstring_view canonicalWin32Path);

        static bool EqualsOrdinalIgnoreCase(std::wstring_view left, std::wstring_view right) noexcept;

    private:
        PathFilter() = default;

        void ResolveKnownFolderFallbacks();
        static BOOL CALLBACK InitOnceCallback(PINIT_ONCE initOnce, PVOID parameter, PVOID* context);

        bool IsAllowedApplicationPluginsPath(std::wstring_view canonical) const;
        bool IsHiddenAllUsersAddinsPath(std::wstring_view canonical, std::wstring_view addinsRoot) const;
        bool IsFilteredAddinsYearDirectory(std::wstring_view canonicalDir, std::wstring_view addinsRoot) const;

        static bool TryGetEnvironmentVar(const wchar_t* name, std::wstring& value);
        static std::wstring ResolveKnownFolder(const GUID& folderId, const wchar_t* fallbackEnvVar);
        static std::wstring ResolveDefaultProfileRoot();
        static std::wstring NormalizeRoot(std::wstring_view rawRoot);
        static bool IsValidFourDigitYear(std::wstring_view text) noexcept;
        static bool TryGetRelativeSubpath(
            std::wstring_view canonicalPath,
            std::wstring_view rootPrefix,
            std::wstring_view& relativeOut) noexcept;
        static void SplitFirstSegment(
            std::wstring_view relativePath,
            std::wstring_view& firstSegmentOut,
            std::wstring_view& remainderOut) noexcept;
        static bool HasCaseInsensitiveFileName(std::wstring_view path, std::wstring_view needle) noexcept;

        INIT_ONCE m_knownFoldersInitOnce = INIT_ONCE_STATIC_INIT;
        bool m_hasValidYear = false;
        std::wstring m_year;
        std::wstring m_allowedAppDataAddinFile;

        std::wstring m_envAppDataRoot;
        std::wstring m_envProgramDataRoot;
        std::wstring m_envProgramFilesRoot;
        std::wstring m_envProfileRoot;

        std::wstring m_appDataRoot;
        std::wstring m_programDataRoot;
        std::wstring m_programFilesRoot;
        std::wstring m_profileRoot;
        std::wstring m_profilePidDir;

        std::wstring m_appDataAddinsRoot;
        std::wstring m_appDataAddinsYearDir;
        std::wstring m_programDataAddinsRoot;
        std::wstring m_programDataAddinsYearDir;
        std::wstring m_programFilesAddinsRoot;
        std::wstring m_programFilesAddinsYearDir;
        std::wstring m_appDataPluginsRoot;
        std::wstring m_programDataPluginsRoot;
        std::wstring m_programFilesPluginsRoot;
        std::wstring m_programFilesRevitInternalAddinsDir;
        std::wstring m_programFilesETransmitDir;

        std::wstring m_appDataSettingsRoot;
        std::wstring m_programDataAdminRoot;
        std::wstring m_programFilesAdminRoot;
    };
}
