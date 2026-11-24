#include <windows.h>
#include "detours.h"
#include "PathFilter.h"

#include <string>
#include <unordered_map>

using synthetic::shim::PathFilter;

namespace
{
    DWORD g_reentrancyTlsIndex = TLS_OUT_OF_INDEXES;
    SRWLOCK g_activeSearchesLock = SRWLOCK_INIT;
    std::unordered_map<HANDLE, std::wstring>* g_activeSearches = nullptr;

    class ReentrancyGuard
    {
    public:
        ReentrancyGuard() noexcept : m_entered(false)
        {
            if (g_reentrancyTlsIndex != TLS_OUT_OF_INDEXES)
            {
                const DWORD savedError = GetLastError();
                if (TlsGetValue(g_reentrancyTlsIndex) == nullptr)
                {
                    TlsSetValue(g_reentrancyTlsIndex, reinterpret_cast<void*>(static_cast<ULONG_PTR>(1)));
                    m_entered = true;
                }
                SetLastError(savedError);
            }
        }

        ~ReentrancyGuard() noexcept
        {
            if (m_entered && g_reentrancyTlsIndex != TLS_OUT_OF_INDEXES)
            {
                const DWORD savedError = GetLastError();
                TlsSetValue(g_reentrancyTlsIndex, nullptr);
                SetLastError(savedError);
            }
        }

        ReentrancyGuard(const ReentrancyGuard&) = delete;
        ReentrancyGuard& operator=(const ReentrancyGuard&) = delete;

        bool Entered() const noexcept { return m_entered; }

    private:
        bool m_entered;
    };

    decltype(&FindFirstFileW) RealFindFirstFileW = FindFirstFileW;
    decltype(&FindFirstFileExW) RealFindFirstFileExW = FindFirstFileExW;
    decltype(&FindNextFileW) RealFindNextFileW = FindNextFileW;
    decltype(&FindClose) RealFindClose = FindClose;
    decltype(&CreateFileW) RealCreateFileW = CreateFileW;
    decltype(&GetFileAttributesW) RealGetFileAttributesW = GetFileAttributesW;
    decltype(&GetFileAttributesExW) RealGetFileAttributesExW = GetFileAttributesExW;

    void TrackFilteredSearchHandle(HANDLE hFind, std::wstring searchDir)
    {
        if (hFind == INVALID_HANDLE_VALUE || hFind == nullptr)
        {
            return;
        }

        AcquireSRWLockExclusive(&g_activeSearchesLock);
        if (g_activeSearches != nullptr)
        {
            (*g_activeSearches)[hFind] = std::move(searchDir);
        }
        ReleaseSRWLockExclusive(&g_activeSearchesLock);
    }

    bool TryGetTrackedSearchDirectory(HANDLE hFind, std::wstring& searchDirOut)
    {
        if (hFind == INVALID_HANDLE_VALUE || hFind == nullptr)
        {
            return false;
        }

        bool found = false;
        AcquireSRWLockShared(&g_activeSearchesLock);
        if (g_activeSearches != nullptr)
        {
            const auto it = g_activeSearches->find(hFind);
            if (it != g_activeSearches->end())
            {
                searchDirOut = it->second;
                found = true;
            }
        }
        ReleaseSRWLockShared(&g_activeSearchesLock);
        return found;
    }

    void UntrackSearchHandle(HANDLE hFind)
    {
        if (hFind == INVALID_HANDLE_VALUE || hFind == nullptr)
        {
            return;
        }

        AcquireSRWLockExclusive(&g_activeSearchesLock);
        if (g_activeSearches != nullptr)
        {
            g_activeSearches->erase(hFind);
        }
        ReleaseSRWLockExclusive(&g_activeSearchesLock);
    }

    HANDLE FilterFirstSearchResult(HANDLE hFind, const std::wstring& searchDir, LPWIN32_FIND_DATAW findData)
    {
        if (hFind == INVALID_HANDLE_VALUE || findData == nullptr)
        {
            return hFind;
        }

        PathFilter& filter = PathFilter::Instance();
        if (!filter.IsDirectoryFiltered(searchDir))
        {
            return hFind;
        }

        while (!filter.IsFindDataAllowed(searchDir, *findData))
        {
            if (!RealFindNextFileW(hFind, findData))
            {
                const DWORD err = GetLastError();
                RealFindClose(hFind);
                SetLastError(err == ERROR_NO_MORE_FILES ? ERROR_FILE_NOT_FOUND : err);
                return INVALID_HANDLE_VALUE;
            }
        }

        TrackFilteredSearchHandle(hFind, searchDir);
        return hFind;
    }

    HANDLE WINAPI HookFindFirstFileW(
        LPCWSTR lpFileName,
        LPWIN32_FIND_DATAW lpFindFileData)
    {
        ReentrancyGuard guard;
        if (!guard.Entered())
        {
            return RealFindFirstFileW(lpFileName, lpFindFileData);
        }

        PathFilter& filter = PathFilter::Instance();
        filter.EnsureInitialized();

        const std::wstring searchDir = lpFileName != nullptr
            ? PathFilter::ExtractSearchDirectory(lpFileName)
            : std::wstring();
        if (!searchDir.empty() && !filter.IsPathAllowed(searchDir, true))
        {
            SetLastError(ERROR_PATH_NOT_FOUND);
            return INVALID_HANDLE_VALUE;
        }

        const HANDLE hFind = RealFindFirstFileW(lpFileName, lpFindFileData);
        if (hFind == INVALID_HANDLE_VALUE)
        {
            return INVALID_HANDLE_VALUE;
        }

        return FilterFirstSearchResult(hFind, searchDir, lpFindFileData);
    }

    HANDLE WINAPI HookFindFirstFileExW(
        LPCWSTR lpFileName,
        FINDEX_INFO_LEVELS fInfoLevelId,
        LPVOID lpFindFileData,
        FINDEX_SEARCH_OPS fSearchOp,
        LPVOID lpSearchFilter,
        DWORD dwAdditionalFlags)
    {
        ReentrancyGuard guard;
        if (!guard.Entered())
        {
            return RealFindFirstFileExW(
                lpFileName,
                fInfoLevelId,
                lpFindFileData,
                fSearchOp,
                lpSearchFilter,
                dwAdditionalFlags);
        }

        PathFilter& filter = PathFilter::Instance();
        filter.EnsureInitialized();

        const std::wstring searchDir = lpFileName != nullptr
            ? PathFilter::ExtractSearchDirectory(lpFileName)
            : std::wstring();
        if (!searchDir.empty() && !filter.IsPathAllowed(searchDir, true))
        {
            SetLastError(ERROR_PATH_NOT_FOUND);
            return INVALID_HANDLE_VALUE;
        }

        const HANDLE hFind = RealFindFirstFileExW(
            lpFileName,
            fInfoLevelId,
            lpFindFileData,
            fSearchOp,
            lpSearchFilter,
            dwAdditionalFlags);
        if (hFind == INVALID_HANDLE_VALUE)
        {
            return INVALID_HANDLE_VALUE;
        }

        if (fInfoLevelId != FindExInfoStandard && fInfoLevelId != FindExInfoBasic)
        {
            return hFind;
        }

        return FilterFirstSearchResult(hFind, searchDir, static_cast<LPWIN32_FIND_DATAW>(lpFindFileData));
    }

    BOOL WINAPI HookFindNextFileW(
        HANDLE hFindFile,
        LPWIN32_FIND_DATAW lpFindFileData)
    {
        ReentrancyGuard guard;
        if (!guard.Entered())
        {
            return RealFindNextFileW(hFindFile, lpFindFileData);
        }

        std::wstring searchDir;
        if (!TryGetTrackedSearchDirectory(hFindFile, searchDir))
        {
            return RealFindNextFileW(hFindFile, lpFindFileData);
        }

        PathFilter& filter = PathFilter::Instance();
        while (true)
        {
            if (!RealFindNextFileW(hFindFile, lpFindFileData))
            {
                return FALSE;
            }

            if (lpFindFileData == nullptr || filter.IsFindDataAllowed(searchDir, *lpFindFileData))
            {
                return TRUE;
            }
        }
    }

    BOOL WINAPI HookFindClose(HANDLE hFindFile)
    {
        ReentrancyGuard guard;
        if (!guard.Entered())
        {
            return RealFindClose(hFindFile);
        }

        UntrackSearchHandle(hFindFile);
        return RealFindClose(hFindFile);
    }

    HANDLE WINAPI HookCreateFileW(
        LPCWSTR lpFileName,
        DWORD dwDesiredAccess,
        DWORD dwShareMode,
        LPSECURITY_ATTRIBUTES lpSecurityAttributes,
        DWORD dwCreationDisposition,
        DWORD dwFlagsAndAttributes,
        HANDLE hTemplateFile)
    {
        ReentrancyGuard guard;
        if (!guard.Entered())
        {
            return RealCreateFileW(
                lpFileName,
                dwDesiredAccess,
                dwShareMode,
                lpSecurityAttributes,
                dwCreationDisposition,
                dwFlagsAndAttributes,
                hTemplateFile);
        }

        if (lpFileName != nullptr)
        {
            PathFilter& filter = PathFilter::Instance();
            filter.EnsureInitialized();
            if (!filter.IsPathAllowed(lpFileName))
            {
                SetLastError(ERROR_FILE_NOT_FOUND);
                return INVALID_HANDLE_VALUE;
            }
        }

        return RealCreateFileW(
            lpFileName,
            dwDesiredAccess,
            dwShareMode,
            lpSecurityAttributes,
            dwCreationDisposition,
            dwFlagsAndAttributes,
            hTemplateFile);
    }

    DWORD WINAPI HookGetFileAttributesW(LPCWSTR lpFileName)
    {
        ReentrancyGuard guard;
        if (!guard.Entered())
        {
            return RealGetFileAttributesW(lpFileName);
        }

        if (lpFileName != nullptr)
        {
            PathFilter& filter = PathFilter::Instance();
            filter.EnsureInitialized();
            if (!filter.IsPathAllowed(lpFileName))
            {
                SetLastError(ERROR_FILE_NOT_FOUND);
                return INVALID_FILE_ATTRIBUTES;
            }
        }

        return RealGetFileAttributesW(lpFileName);
    }

    BOOL WINAPI HookGetFileAttributesExW(
        LPCWSTR lpFileName,
        GET_FILEEX_INFO_LEVELS fInfoLevelId,
        LPVOID lpFileInformation)
    {
        ReentrancyGuard guard;
        if (!guard.Entered())
        {
            return RealGetFileAttributesExW(lpFileName, fInfoLevelId, lpFileInformation);
        }

        if (lpFileName != nullptr)
        {
            PathFilter& filter = PathFilter::Instance();
            filter.EnsureInitialized();
            if (!filter.IsPathAllowed(lpFileName))
            {
                SetLastError(ERROR_FILE_NOT_FOUND);
                return FALSE;
            }
        }

        return RealGetFileAttributesExW(lpFileName, fInfoLevelId, lpFileInformation);
    }

    BOOL AttachHooks()
    {
        DetourTransactionBegin();
        DetourUpdateThread(GetCurrentThread());
        DetourAttach(reinterpret_cast<PVOID*>(&RealFindFirstFileW), reinterpret_cast<PVOID>(HookFindFirstFileW));
        DetourAttach(reinterpret_cast<PVOID*>(&RealFindFirstFileExW), reinterpret_cast<PVOID>(HookFindFirstFileExW));
        DetourAttach(reinterpret_cast<PVOID*>(&RealFindNextFileW), reinterpret_cast<PVOID>(HookFindNextFileW));
        DetourAttach(reinterpret_cast<PVOID*>(&RealFindClose), reinterpret_cast<PVOID>(HookFindClose));
        DetourAttach(reinterpret_cast<PVOID*>(&RealCreateFileW), reinterpret_cast<PVOID>(HookCreateFileW));
        DetourAttach(reinterpret_cast<PVOID*>(&RealGetFileAttributesW), reinterpret_cast<PVOID>(HookGetFileAttributesW));
        DetourAttach(reinterpret_cast<PVOID*>(&RealGetFileAttributesExW), reinterpret_cast<PVOID>(HookGetFileAttributesExW));
        return DetourTransactionCommit() == NO_ERROR;
    }

    void DetachHooks()
    {
        DetourTransactionBegin();
        DetourUpdateThread(GetCurrentThread());
        DetourDetach(reinterpret_cast<PVOID*>(&RealFindFirstFileW), reinterpret_cast<PVOID>(HookFindFirstFileW));
        DetourDetach(reinterpret_cast<PVOID*>(&RealFindFirstFileExW), reinterpret_cast<PVOID>(HookFindFirstFileExW));
        DetourDetach(reinterpret_cast<PVOID*>(&RealFindNextFileW), reinterpret_cast<PVOID>(HookFindNextFileW));
        DetourDetach(reinterpret_cast<PVOID*>(&RealFindClose), reinterpret_cast<PVOID>(HookFindClose));
        DetourDetach(reinterpret_cast<PVOID*>(&RealCreateFileW), reinterpret_cast<PVOID>(HookCreateFileW));
        DetourDetach(reinterpret_cast<PVOID*>(&RealGetFileAttributesW), reinterpret_cast<PVOID>(HookGetFileAttributesW));
        DetourDetach(reinterpret_cast<PVOID*>(&RealGetFileAttributesExW), reinterpret_cast<PVOID>(HookGetFileAttributesExW));
        DetourTransactionCommit();
    }
}

BOOL APIENTRY DllMain(HMODULE hModule, DWORD ulReasonForCall, LPVOID lpvReserved)
{
    if (DetourIsHelperProcess())
    {
        return TRUE;
    }

    switch (ulReasonForCall)
    {
    case DLL_PROCESS_ATTACH:
        DisableThreadLibraryCalls(hModule);
        DetourRestoreAfterWith();

        g_reentrancyTlsIndex = TlsAlloc();
        if (g_reentrancyTlsIndex == TLS_OUT_OF_INDEXES)
        {
            return FALSE;
        }

        g_activeSearches = new std::unordered_map<HANDLE, std::wstring>();
        PathFilter::Instance().InitializeFromEnvironment();

        if (!AttachHooks())
        {
            delete g_activeSearches;
            g_activeSearches = nullptr;
            TlsFree(g_reentrancyTlsIndex);
            g_reentrancyTlsIndex = TLS_OUT_OF_INDEXES;
            return FALSE;
        }
        break;

    case DLL_PROCESS_DETACH:
        if (lpvReserved == nullptr)
        {
            DetachHooks();

            AcquireSRWLockExclusive(&g_activeSearchesLock);
            delete g_activeSearches;
            g_activeSearches = nullptr;
            ReleaseSRWLockExclusive(&g_activeSearchesLock);

            if (g_reentrancyTlsIndex != TLS_OUT_OF_INDEXES)
            {
                TlsFree(g_reentrancyTlsIndex);
                g_reentrancyTlsIndex = TLS_OUT_OF_INDEXES;
            }
        }
        break;

    default:
        break;
    }

    return TRUE;
}
