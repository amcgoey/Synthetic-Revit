#include "PathFilter.h"

#include <shlobj.h>
#include <vector>

namespace synthetic::shim
{
    namespace
    {
        constexpr wchar_t kEnvRevitYear[] = L"SYNTHETIC_CLEAN_SHIM_REVIT_YEAR";
        constexpr wchar_t kEnvAppDataRoot[] = L"SYNTHETIC_CLEAN_SHIM_APPDATA_ROOT";
        constexpr wchar_t kEnvProgramDataRoot[] = L"SYNTHETIC_CLEAN_SHIM_PROGRAMDATA_ROOT";
        constexpr wchar_t kEnvProgramFilesRoot[] = L"SYNTHETIC_CLEAN_SHIM_PROGRAMFILES_ROOT";
        constexpr wchar_t kEnvProfileDir[] = L"SYNTHETIC_CLEAN_SHIM_PROFILE_DIR";
        constexpr wchar_t kAllowedBundleName[] = L"ricaun.RevitTest.Application.bundle";
        constexpr wchar_t kETransmitDbName[] = L"eTransmitForRevitDB.dll";
        constexpr wchar_t kAddInsSettingsSeed[] = L"{\"DisableAllAddIns\":false,\"AddInItemSettings\":[]}";
        constexpr wchar_t kAdminSettingsSeed[] = L"{\"DisableAllAddIns\":false}";
        constexpr wchar_t kDefaultProfileSubdir[] = L"SyntheticCleanProfile";

        bool IsDriveRoot(std::wstring_view path) noexcept
        {
            return path.size() == 3 &&
                ((path[0] >= L'A' && path[0] <= L'Z') || (path[0] >= L'a' && path[0] <= L'z')) &&
                path[1] == L':' &&
                path[2] == L'\\';
        }

        bool IsUncShareRoot(std::wstring_view path) noexcept
        {
            if (path.size() < 5 || path[0] != L'\\' || path[1] != L'\\')
            {
                return false;
            }

            size_t slashCount = 0;
            for (size_t i = 2; i < path.size(); ++i)
            {
                if (path[i] == L'\\')
                {
                    ++slashCount;
                }
            }

            return slashCount == 1 && path.back() != L'\\';
        }

        std::wstring JoinPath(std::wstring_view left, std::wstring_view right)
        {
            if (left.empty())
            {
                return std::wstring(right);
            }

            std::wstring result(left);
            if (result.back() != L'\\' && !right.empty() && right.front() != L'\\')
            {
                result.push_back(L'\\');
            }
            result.append(right);
            return result;
        }
    }

    PathFilter& PathFilter::Instance() noexcept
    {
        static PathFilter instance;
        return instance;
    }

    void PathFilter::InitializeFromEnvironment()
    {
        std::wstring year;
        (void)TryGetEnvironmentVar(kEnvRevitYear, year);

        std::wstring appData;
        std::wstring programData;
        std::wstring programFiles;
        std::wstring profileDir;
        (void)TryGetEnvironmentVar(kEnvAppDataRoot, appData);
        (void)TryGetEnvironmentVar(kEnvProgramDataRoot, programData);
        (void)TryGetEnvironmentVar(kEnvProgramFilesRoot, programFiles);
        (void)TryGetEnvironmentVar(kEnvProfileDir, profileDir);

        m_envAppDataRoot = NormalizeRoot(appData);
        m_envProgramDataRoot = NormalizeRoot(programData);
        m_envProgramFilesRoot = NormalizeRoot(programFiles);
        m_envProfileRoot = NormalizeRoot(profileDir);

        Configure(year, m_envAppDataRoot, m_envProgramDataRoot, m_envProgramFilesRoot, m_envProfileRoot);
    }

    void PathFilter::EnsureInitialized()
    {
        (void)InitOnceExecuteOnce(&m_knownFoldersInitOnce, InitOnceCallback, this, nullptr);
    }

    BOOL CALLBACK PathFilter::InitOnceCallback(PINIT_ONCE, PVOID parameter, PVOID*)
    {
        auto* filter = static_cast<PathFilter*>(parameter);
        if (filter != nullptr)
        {
            filter->ResolveKnownFolderFallbacks();
        }
        return TRUE;
    }

    void PathFilter::ResolveKnownFolderFallbacks()
    {
        std::wstring appData = !m_envAppDataRoot.empty()
            ? m_envAppDataRoot
            : ResolveKnownFolder(FOLDERID_RoamingAppData, L"APPDATA");

        std::wstring programData = !m_envProgramDataRoot.empty()
            ? m_envProgramDataRoot
            : ResolveKnownFolder(FOLDERID_ProgramData, L"ProgramData");

        std::wstring programFiles = !m_envProgramFilesRoot.empty()
            ? m_envProgramFilesRoot
            : ResolveKnownFolder(FOLDERID_ProgramFiles, L"ProgramFiles");

        std::wstring profileRoot = !m_envProfileRoot.empty()
            ? m_envProfileRoot
            : ResolveDefaultProfileRoot();

        Configure(m_year, appData, programData, programFiles, profileRoot);
    }

    void PathFilter::Configure(
        std::wstring_view year,
        std::wstring_view appDataRoot,
        std::wstring_view programDataRoot,
        std::wstring_view programFilesRoot,
        std::wstring_view profileRoot)
    {
        // Trim whitespace around year value.
        while (!year.empty() && (year.front() == L' ' || year.front() == L'\t'))
        {
            year.remove_prefix(1);
        }
        while (!year.empty() && (year.back() == L' ' || year.back() == L'\t'))
        {
            year.remove_suffix(1);
        }

        m_year.assign(year);
        m_hasValidYear = IsValidFourDigitYear(m_year);
        if (m_hasValidYear)
        {
            m_allowedAppDataAddinFile = L"Synthetic" + m_year + L".addin";
        }
        else
        {
            m_allowedAppDataAddinFile.clear();
        }

        m_appDataRoot = NormalizeRoot(appDataRoot);
        m_programDataRoot = NormalizeRoot(programDataRoot);
        m_programFilesRoot = NormalizeRoot(programFilesRoot);
        m_profileRoot = NormalizeRoot(profileRoot);
        m_profilePidDir = m_profileRoot.empty()
            ? std::wstring()
            : JoinPath(m_profileRoot, std::to_wstring(GetCurrentProcessId()));

        m_appDataAddinsRoot = m_appDataRoot.empty()
            ? std::wstring()
            : JoinPath(m_appDataRoot, L"Autodesk\\Revit\\Addins");
        m_appDataAddinsYearDir = (m_appDataAddinsRoot.empty() || !m_hasValidYear)
            ? std::wstring()
            : JoinPath(m_appDataAddinsRoot, m_year);

        m_programDataAddinsRoot = m_programDataRoot.empty()
            ? std::wstring()
            : JoinPath(m_programDataRoot, L"Autodesk\\Revit\\Addins");
        m_programDataAddinsYearDir = (m_programDataAddinsRoot.empty() || !m_hasValidYear)
            ? std::wstring()
            : JoinPath(m_programDataAddinsRoot, m_year);

        m_programFilesAddinsRoot = m_programFilesRoot.empty()
            ? std::wstring()
            : JoinPath(m_programFilesRoot, L"Autodesk\\Revit\\Addins");
        m_programFilesAddinsYearDir = (m_programFilesAddinsRoot.empty() || !m_hasValidYear)
            ? std::wstring()
            : JoinPath(m_programFilesAddinsRoot, m_year);

        m_appDataPluginsRoot = m_appDataRoot.empty()
            ? std::wstring()
            : JoinPath(m_appDataRoot, L"Autodesk\\ApplicationPlugins");
        m_programDataPluginsRoot = m_programDataRoot.empty()
            ? std::wstring()
            : JoinPath(m_programDataRoot, L"Autodesk\\ApplicationPlugins");
        m_programFilesPluginsRoot = m_programFilesRoot.empty()
            ? std::wstring()
            : JoinPath(m_programFilesRoot, L"Autodesk\\ApplicationPlugins");

        if (!m_programFilesRoot.empty() && m_hasValidYear)
        {
            m_programFilesRevitInternalAddinsDir = JoinPath(m_programFilesRoot, L"Autodesk\\Revit " + m_year + L"\\AddIns");
            m_programFilesETransmitDir = JoinPath(m_programFilesRoot, L"Autodesk\\eTransmit for Revit " + m_year);
        }
        else
        {
            m_programFilesRevitInternalAddinsDir.clear();
            m_programFilesETransmitDir.clear();
        }

        m_appDataSettingsRoot = m_appDataRoot.empty()
            ? std::wstring()
            : JoinPath(m_appDataRoot, L"Autodesk\\Revit");
        m_programDataAdminRoot = m_programDataRoot.empty()
            ? std::wstring()
            : JoinPath(m_programDataRoot, L"Autodesk");
        m_programFilesAdminRoot = m_programFilesRoot.empty()
            ? std::wstring()
            : JoinPath(m_programFilesRoot, L"Autodesk\\Revit\\Admin");
    }

    bool PathFilter::IsFilteredAddinsYearDirectory(std::wstring_view canonicalDir, std::wstring_view addinsRoot) const
    {
        if (addinsRoot.empty())
        {
            return false;
        }

        std::wstring_view rel;
        if (!TryGetRelativeSubpath(canonicalDir, addinsRoot, rel) || rel.empty())
        {
            return false;
        }

        std::wstring_view first;
        std::wstring_view rest;
        SplitFirstSegment(rel, first, rest);
        return rest.empty() && IsValidFourDigitYear(first);
    }

    bool PathFilter::IsDirectoryFiltered(std::wstring_view canonicalDir) const
    {
        if (canonicalDir.empty())
        {
            return false;
        }

        if ((!m_appDataPluginsRoot.empty() && EqualsOrdinalIgnoreCase(canonicalDir, m_appDataPluginsRoot)) ||
            (!m_programDataPluginsRoot.empty() && EqualsOrdinalIgnoreCase(canonicalDir, m_programDataPluginsRoot)) ||
            (!m_programFilesPluginsRoot.empty() && EqualsOrdinalIgnoreCase(canonicalDir, m_programFilesPluginsRoot)))
        {
            return true;
        }

        if (m_hasValidYear)
        {
            return (!m_appDataAddinsYearDir.empty() && EqualsOrdinalIgnoreCase(canonicalDir, m_appDataAddinsYearDir)) ||
                (!m_programDataAddinsYearDir.empty() && EqualsOrdinalIgnoreCase(canonicalDir, m_programDataAddinsYearDir)) ||
                (!m_programFilesAddinsYearDir.empty() && EqualsOrdinalIgnoreCase(canonicalDir, m_programFilesAddinsYearDir));
        }

        // Fail-closed mode when year is missing/invalid: filter any year folder under Addins roots.
        return IsFilteredAddinsYearDirectory(canonicalDir, m_appDataAddinsRoot) ||
            IsFilteredAddinsYearDirectory(canonicalDir, m_programDataAddinsRoot) ||
            IsFilteredAddinsYearDirectory(canonicalDir, m_programFilesAddinsRoot);
    }

    bool PathFilter::IsHiddenAllUsersAddinsPath(std::wstring_view canonical, std::wstring_view addinsRoot) const
    {
        std::wstring_view rel;
        if (addinsRoot.empty() || !TryGetRelativeSubpath(canonical, addinsRoot, rel))
        {
            return false;
        }

        if (rel.empty())
        {
            return false;
        }

        std::wstring_view firstSeg;
        std::wstring_view rest;
        SplitFirstSegment(rel, firstSeg, rest);

        if (!m_hasValidYear)
        {
            return IsValidFourDigitYear(firstSeg) && !rest.empty();
        }

        return EqualsOrdinalIgnoreCase(firstSeg, m_year) && !rest.empty();
    }

    bool PathFilter::IsAllowedApplicationPluginsPath(std::wstring_view canonical) const
    {
        for (const std::wstring* root : { &m_appDataPluginsRoot, &m_programDataPluginsRoot, &m_programFilesPluginsRoot })
        {
            std::wstring_view rel;
            if (root->empty() || !TryGetRelativeSubpath(canonical, *root, rel))
            {
                continue;
            }

            if (rel.empty())
            {
                return true;
            }

            if (!m_hasValidYear)
            {
                return false;
            }

            std::wstring_view firstSeg;
            std::wstring_view rest;
            SplitFirstSegment(rel, firstSeg, rest);
            return EqualsOrdinalIgnoreCase(firstSeg, kAllowedBundleName);
        }

        return true;
    }

    bool PathFilter::IsPathAllowed(std::wstring_view rawPath, std::optional<bool> isDirectory) const
    {
        const std::wstring canonical = CanonicalizePath(rawPath);
        if (canonical.empty())
        {
            return true;
        }

        std::wstring_view rel;

        // Rule 0: Always preserve Autodesk internal Revit AddIns and eTransmitForRevitDB.
        if (!m_programFilesRevitInternalAddinsDir.empty() &&
            TryGetRelativeSubpath(canonical, m_programFilesRevitInternalAddinsDir, rel))
        {
            return true;
        }
        if (!m_programFilesETransmitDir.empty() &&
            TryGetRelativeSubpath(canonical, m_programFilesETransmitDir, rel))
        {
            return true;
        }
        if (ContainsCaseInsensitiveSegment(canonical, kETransmitDbName))
        {
            return true;
        }

        // Rule 1: %APPDATA%\Autodesk\Revit\Addins\<Year> -> allow ONLY Synthetic<Year>.addin
        if (!m_appDataAddinsRoot.empty() && TryGetRelativeSubpath(canonical, m_appDataAddinsRoot, rel))
        {
            if (rel.empty())
            {
                return true;
            }

            std::wstring_view firstSeg;
            std::wstring_view rest;
            SplitFirstSegment(rel, firstSeg, rest);

            if (!m_hasValidYear)
            {
                // Allow enumerating the <Year> directory itself, block all entries inside any <Year>.
                return rest.empty();
            }

            if (EqualsOrdinalIgnoreCase(firstSeg, m_year))
            {
                if (rest.empty())
                {
                    return true;
                }

                // Only a direct file named Synthetic<Year>.addin is allowed.
                if (rest.find(L'\\') != std::wstring_view::npos)
                {
                    return false;
                }

                if (isDirectory.has_value() && *isDirectory)
                {
                    return false;
                }

                return EqualsOrdinalIgnoreCase(rest, m_allowedAppDataAddinFile);
            }

            return true;
        }

        // Rule 2: %ProgramData%\Autodesk\Revit\Addins\<Year> and %ProgramFiles%\Autodesk\Revit\Addins\<Year>
        // -> hide ALL entries (including stale Synthetic<Year>.addin, third-party manifests, and subfolders).
        if (IsHiddenAllUsersAddinsPath(canonical, m_programDataAddinsRoot) ||
            IsHiddenAllUsersAddinsPath(canonical, m_programFilesAddinsRoot))
        {
            return false;
        }

        // Rule 3: Across user, ProgramData, and Program Files ApplicationPlugins bundle directories,
        // expose ONLY ricaun.RevitTest.Application.bundle (and descendants).
        return IsAllowedApplicationPluginsPath(canonical);
    }

    bool PathFilter::TryResolveVirtualizedPath(
        std::wstring_view rawPath,
        std::wstring& virtualPathOut,
        std::wstring& seedJsonOut) const
    {
        virtualPathOut.clear();
        seedJsonOut.clear();

        if (m_profilePidDir.empty())
        {
            return false;
        }

        const std::wstring canonical = CanonicalizePath(rawPath);
        if (canonical.empty())
        {
            return false;
        }

        auto matchesTargetYear = [this](std::wstring_view candidate) noexcept -> bool
        {
            return m_hasValidYear
                ? EqualsOrdinalIgnoreCase(candidate, m_year)
                : IsValidFourDigitYear(candidate);
        };

        std::wstring_view rel;

        // 1. %APPDATA%\Autodesk\Revit\Autodesk Revit <Year>\AddinsData\AddInsSettings.json
        if (!m_appDataSettingsRoot.empty() && TryGetRelativeSubpath(canonical, m_appDataSettingsRoot, rel) && !rel.empty())
        {
            std::wstring_view seg1;
            std::wstring_view rem1;
            SplitFirstSegment(rel, seg1, rem1);

            constexpr std::wstring_view kAppDataPrefix = L"Autodesk Revit ";
            if (seg1.size() > kAppDataPrefix.size() &&
                EqualsOrdinalIgnoreCase(seg1.substr(0, kAppDataPrefix.size()), kAppDataPrefix) &&
                matchesTargetYear(seg1.substr(kAppDataPrefix.size())) &&
                !rem1.empty())
            {
                std::wstring_view seg2;
                std::wstring_view rem2;
                SplitFirstSegment(rem1, seg2, rem2);
                if (EqualsOrdinalIgnoreCase(seg2, L"AddinsData") &&
                    EqualsOrdinalIgnoreCase(rem2, L"AddInsSettings.json"))
                {
                    virtualPathOut = JoinPath(m_profilePidDir, L"AddInsSettings.json");
                    seedJsonOut = kAddInsSettingsSeed;
                    return true;
                }
            }
        }

        // 2. %ProgramData%\Autodesk\RVT <Year>\Admin\AdminSettings.json
        if (!m_programDataAdminRoot.empty() && TryGetRelativeSubpath(canonical, m_programDataAdminRoot, rel) && !rel.empty())
        {
            std::wstring_view seg1;
            std::wstring_view rem1;
            SplitFirstSegment(rel, seg1, rem1);

            constexpr std::wstring_view kRvtPrefix = L"RVT ";
            if (seg1.size() > kRvtPrefix.size() &&
                EqualsOrdinalIgnoreCase(seg1.substr(0, kRvtPrefix.size()), kRvtPrefix) &&
                matchesTargetYear(seg1.substr(kRvtPrefix.size())) &&
                !rem1.empty())
            {
                std::wstring_view seg2;
                std::wstring_view rem2;
                SplitFirstSegment(rem1, seg2, rem2);
                if (EqualsOrdinalIgnoreCase(seg2, L"Admin") &&
                    EqualsOrdinalIgnoreCase(rem2, L"AdminSettings.json"))
                {
                    virtualPathOut = JoinPath(m_profilePidDir, L"AdminSettings.json");
                    seedJsonOut = kAdminSettingsSeed;
                    return true;
                }
            }
        }

        // 3. %ProgramFiles%\Autodesk\Revit\Admin\<Year>\AdminSettings.json
        if (!m_programFilesAdminRoot.empty() && TryGetRelativeSubpath(canonical, m_programFilesAdminRoot, rel) && !rel.empty())
        {
            std::wstring_view seg1;
            std::wstring_view rem1;
            SplitFirstSegment(rel, seg1, rem1);
            if (matchesTargetYear(seg1) &&
                EqualsOrdinalIgnoreCase(rem1, L"AdminSettings.json"))
            {
                virtualPathOut = JoinPath(m_profilePidDir, L"AdminSettings.json");
                seedJsonOut = kAdminSettingsSeed;
                return true;
            }
        }

        return false;
    }

    bool PathFilter::IsFindDataAllowed(std::wstring_view canonicalSearchDir, const WIN32_FIND_DATAW& findData) const
    {
        const wchar_t* fileName = findData.cFileName;
        if (fileName == nullptr || fileName[0] == L'\0')
        {
            return true;
        }

        if ((fileName[0] == L'.' && fileName[1] == L'\0') ||
            (fileName[0] == L'.' && fileName[1] == L'.' && fileName[2] == L'\0'))
        {
            return true;
        }

        std::wstring fullEntryPath = JoinPath(canonicalSearchDir, fileName);
        const bool isDir = (findData.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) != 0;
        return IsPathAllowed(fullEntryPath, isDir);
    }

    std::wstring PathFilter::ExtractSearchDirectory(std::wstring_view rawPattern)
    {
        const std::wstring canonical = CanonicalizePath(rawPattern);
        if (canonical.empty())
        {
            return std::wstring();
        }

        const size_t lastSlash = canonical.rfind(L'\\');
        if (lastSlash == std::wstring::npos)
        {
            return std::wstring();
        }

        if (lastSlash == 2 && canonical.size() >= 3 && canonical[1] == L':')
        {
            return canonical.substr(0, 3);
        }

        if (lastSlash <= 1)
        {
            return std::wstring();
        }

        return canonical.substr(0, lastSlash);
    }

    std::wstring PathFilter::CanonicalizePath(std::wstring_view rawPath)
    {
        if (rawPath.empty())
        {
            return std::wstring();
        }

        // Skip Win32 device namespace paths (such as \\.\pipe\...).
        if (rawPath.size() >= 4 &&
            (rawPath[0] == L'\\' || rawPath[0] == L'/') &&
            (rawPath[1] == L'\\' || rawPath[1] == L'/') &&
            rawPath[2] == L'.' &&
            (rawPath[3] == L'\\' || rawPath[3] == L'/'))
        {
            return std::wstring();
        }

        std::wstring working(rawPath);
        for (wchar_t& ch : working)
        {
            if (ch == L'/')
            {
                ch = L'\\';
            }
        }

        // Strip Win32 extended-length or NT object prefixes before resolution.
        if (working.size() >= 8 &&
            (EqualsOrdinalIgnoreCase(std::wstring_view(working).substr(0, 8), L"\\\\?\\UNC\\") ||
             EqualsOrdinalIgnoreCase(std::wstring_view(working).substr(0, 8), L"\\??\\UNC\\")))
        {
            working = L"\\\\" + working.substr(8);
        }
        else if (working.size() >= 4 &&
            (working.compare(0, 4, L"\\\\?\\") == 0 || working.compare(0, 4, L"\\??\\") == 0))
        {
            working = working.substr(4);
        }

        // Resolve relative segments and . / .. using GetFullPathNameW (user-mode PEB CWD math).
        const DWORD requiredChars = GetFullPathNameW(working.c_str(), 0, nullptr, nullptr);
        if (requiredChars > 0)
        {
            std::vector<wchar_t> buffer(requiredChars + 1, L'\0');
            const DWORD written = GetFullPathNameW(working.c_str(), requiredChars, buffer.data(), nullptr);
            if (written > 0 && written < buffer.size())
            {
                working.assign(buffer.data(), written);
            }
        }

        // Collapse duplicate internal backslashes while preserving leading UNC \\.
        std::wstring collapsed;
        collapsed.reserve(working.size());
        const bool isUnc = working.size() >= 2 && working[0] == L'\\' && working[1] == L'\\';
        size_t startIdx = 0;
        if (isUnc)
        {
            collapsed.append(L"\\\\");
            startIdx = 2;
        }

        bool prevSlash = false;
        for (size_t i = startIdx; i < working.size(); ++i)
        {
            const wchar_t ch = working[i];
            if (ch == L'\\')
            {
                if (!prevSlash)
                {
                    collapsed.push_back(ch);
                }
                prevSlash = true;
            }
            else
            {
                collapsed.push_back(ch);
                prevSlash = false;
            }
        }

        // Strip trailing backslashes except for drive root (C:\) or UNC share root (\\server\share).
        while (collapsed.size() > 3 && collapsed.back() == L'\\' && !IsDriveRoot(collapsed) && !IsUncShareRoot(collapsed))
        {
            collapsed.pop_back();
        }

        return collapsed;
    }

    std::wstring PathFilter::ToNtPath(std::wstring_view canonicalWin32Path)
    {
        if (canonicalWin32Path.empty())
        {
            return std::wstring();
        }

        if (canonicalWin32Path.size() >= 2 &&
            canonicalWin32Path[0] == L'\\' &&
            canonicalWin32Path[1] == L'\\')
        {
            if (canonicalWin32Path.size() >= 4 &&
                canonicalWin32Path[2] == L'?' &&
                canonicalWin32Path[3] == L'\\')
            {
                return L"\\??\\" + std::wstring(canonicalWin32Path.substr(4));
            }

            return L"\\??\\UNC\\" + std::wstring(canonicalWin32Path.substr(2));
        }

        return L"\\??\\" + std::wstring(canonicalWin32Path);
    }

    bool PathFilter::EqualsOrdinalIgnoreCase(std::wstring_view left, std::wstring_view right) noexcept
    {
        if (left.size() != right.size())
        {
            return false;
        }

        if (left.empty())
        {
            return true;
        }

        return CompareStringOrdinal(
            left.data(),
            static_cast<int>(left.size()),
            right.data(),
            static_cast<int>(right.size()),
            TRUE) == CSTR_EQUAL;
    }

    bool PathFilter::TryGetEnvironmentVar(const wchar_t* name, std::wstring& value)
    {
        value.clear();
        const DWORD len = GetEnvironmentVariableW(name, nullptr, 0);
        if (len == 0)
        {
            return false;
        }

        std::vector<wchar_t> buffer(len, L'\0');
        const DWORD written = GetEnvironmentVariableW(name, buffer.data(), len);
        if (written == 0 || written >= len)
        {
            return false;
        }

        value.assign(buffer.data(), written);
        return true;
    }

    std::wstring PathFilter::ResolveKnownFolder(const GUID& folderId, const wchar_t* fallbackEnvVar)
    {
        PWSTR rawFolder = nullptr;
        const HRESULT hr = SHGetKnownFolderPath(folderId, KF_FLAG_DEFAULT, nullptr, &rawFolder);
        if (SUCCEEDED(hr) && rawFolder != nullptr)
        {
            std::wstring result(rawFolder);
            CoTaskMemFree(rawFolder);
            return NormalizeRoot(result);
        }

        if (rawFolder != nullptr)
        {
            CoTaskMemFree(rawFolder);
        }

        std::wstring fallback;
        if (fallbackEnvVar != nullptr && TryGetEnvironmentVar(fallbackEnvVar, fallback))
        {
            return NormalizeRoot(fallback);
        }

        return std::wstring();
    }

    std::wstring PathFilter::ResolveDefaultProfileRoot()
    {
        DWORD required = GetTempPathW(0, nullptr);
        if (required > 0)
        {
            std::vector<wchar_t> buffer(required + 1, L'\0');
            const DWORD written = GetTempPathW(required, buffer.data());
            if (written > 0 && written < buffer.size())
            {
                const std::wstring normalizedTemp = NormalizeRoot(std::wstring_view(buffer.data(), written));
                if (!normalizedTemp.empty())
                {
                    return JoinPath(normalizedTemp, kDefaultProfileSubdir);
                }
            }
        }

        std::wstring envTemp;
        if (TryGetEnvironmentVar(L"TEMP", envTemp) || TryGetEnvironmentVar(L"TMP", envTemp))
        {
            const std::wstring normalizedTemp = NormalizeRoot(envTemp);
            if (!normalizedTemp.empty())
            {
                return JoinPath(normalizedTemp, kDefaultProfileSubdir);
            }
        }

        return std::wstring();
    }

    std::wstring PathFilter::NormalizeRoot(std::wstring_view rawRoot)
    {
        if (rawRoot.empty())
        {
            return std::wstring();
        }

        std::wstring canonical = CanonicalizePath(rawRoot);
        if (canonical.empty())
        {
            return std::wstring();
        }

        // Canonicalize 8.3 short paths on root overrides if the root exists on disk.
        const DWORD longChars = GetLongPathNameW(canonical.c_str(), nullptr, 0);
        if (longChars > 0)
        {
            std::vector<wchar_t> longBuf(longChars + 1, L'\0');
            const DWORD written = GetLongPathNameW(canonical.c_str(), longBuf.data(), longChars);
            if (written > 0 && written < longBuf.size())
            {
                canonical = CanonicalizePath(std::wstring_view(longBuf.data(), written));
            }
        }

        return canonical;
    }

    bool PathFilter::IsValidFourDigitYear(std::wstring_view text) noexcept
    {
        if (text.size() != 4)
        {
            return false;
        }

        for (wchar_t ch : text)
        {
            if (ch < L'0' || ch > L'9')
            {
                return false;
            }
        }

        return text[0] == L'2' && text[1] == L'0';
    }

    bool PathFilter::TryGetRelativeSubpath(
        std::wstring_view canonicalPath,
        std::wstring_view rootPrefix,
        std::wstring_view& relativeOut) noexcept
    {
        if (rootPrefix.empty() || canonicalPath.size() < rootPrefix.size())
        {
            return false;
        }

        if (!EqualsOrdinalIgnoreCase(canonicalPath.substr(0, rootPrefix.size()), rootPrefix))
        {
            return false;
        }

        if (canonicalPath.size() == rootPrefix.size())
        {
            relativeOut = std::wstring_view();
            return true;
        }

        if (canonicalPath[rootPrefix.size()] == L'\\')
        {
            relativeOut = canonicalPath.substr(rootPrefix.size() + 1);
            return true;
        }

        if (rootPrefix.back() == L'\\')
        {
            relativeOut = canonicalPath.substr(rootPrefix.size());
            return true;
        }

        return false;
    }

    void PathFilter::SplitFirstSegment(
        std::wstring_view relativePath,
        std::wstring_view& firstSegmentOut,
        std::wstring_view& remainderOut) noexcept
    {
        const size_t sep = relativePath.find(L'\\');
        if (sep == std::wstring_view::npos)
        {
            firstSegmentOut = relativePath;
            remainderOut = std::wstring_view();
        }
        else
        {
            firstSegmentOut = relativePath.substr(0, sep);
            remainderOut = relativePath.substr(sep + 1);
        }
    }

    bool PathFilter::ContainsCaseInsensitiveSegment(std::wstring_view path, std::wstring_view needle) noexcept
    {
        if (path.size() < needle.size() || needle.empty())
        {
            return false;
        }

        const size_t lastSlash = path.rfind(L'\\');
        const std::wstring_view fileName = lastSlash == std::wstring_view::npos
            ? path
            : path.substr(lastSlash + 1);
        return EqualsOrdinalIgnoreCase(fileName, needle);
    }
}
