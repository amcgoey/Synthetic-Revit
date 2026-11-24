#include <windows.h>
#include <winternl.h>
#include "detours.h"
#include "PathFilter.h"

#include <cstddef>
#include <cstring>
#include <optional>
#include <string>
#include <string_view>
#include <unordered_map>
#include <unordered_set>
#include <vector>

using synthetic::shim::PathFilter;

namespace
{
    constexpr NTSTATUS kStatusSuccess = static_cast<NTSTATUS>(0x00000000L);
    constexpr NTSTATUS kStatusPending = static_cast<NTSTATUS>(0x00000103L);
    constexpr NTSTATUS kStatusNoMoreFiles = static_cast<NTSTATUS>(0x80000006L);
    constexpr NTSTATUS kStatusNoSuchFile = static_cast<NTSTATUS>(0xC000000FL);
    constexpr NTSTATUS kStatusObjectNameNotFound = static_cast<NTSTATUS>(0xC0000034L);

    constexpr ULONG kFileCreateDisposition = 0x00000002UL;
    constexpr ULONG kSlRestartScan = 0x00000001UL;

    constexpr ULONG kFileDirectoryInformation = 1UL;
    constexpr ULONG kFileFullDirectoryInformation = 2UL;
    constexpr ULONG kFileBothDirectoryInformation = 3UL;
    constexpr ULONG kFileNamesInformation = 12UL;
    constexpr ULONG kFileIdBothDirectoryInformation = 37UL;
    constexpr ULONG kFileIdFullDirectoryInformation = 38UL;

    struct SHIM_FILE_DIRECTORY_INFORMATION
    {
        ULONG NextEntryOffset;
        ULONG FileIndex;
        LARGE_INTEGER CreationTime;
        LARGE_INTEGER LastAccessTime;
        LARGE_INTEGER LastWriteTime;
        LARGE_INTEGER ChangeTime;
        LARGE_INTEGER EndOfFile;
        LARGE_INTEGER AllocationSize;
        ULONG FileAttributes;
        ULONG FileNameLength;
        WCHAR FileName[1];
    };
    static_assert(offsetof(SHIM_FILE_DIRECTORY_INFORMATION, FileName) == 64);

    struct SHIM_FILE_FULL_DIR_INFORMATION
    {
        ULONG NextEntryOffset;
        ULONG FileIndex;
        LARGE_INTEGER CreationTime;
        LARGE_INTEGER LastAccessTime;
        LARGE_INTEGER LastWriteTime;
        LARGE_INTEGER ChangeTime;
        LARGE_INTEGER EndOfFile;
        LARGE_INTEGER AllocationSize;
        ULONG FileAttributes;
        ULONG FileNameLength;
        ULONG EaSize;
        WCHAR FileName[1];
    };
    static_assert(offsetof(SHIM_FILE_FULL_DIR_INFORMATION, FileName) == 68);

    struct SHIM_FILE_BOTH_DIR_INFORMATION
    {
        ULONG NextEntryOffset;
        ULONG FileIndex;
        LARGE_INTEGER CreationTime;
        LARGE_INTEGER LastAccessTime;
        LARGE_INTEGER LastWriteTime;
        LARGE_INTEGER ChangeTime;
        LARGE_INTEGER EndOfFile;
        LARGE_INTEGER AllocationSize;
        ULONG FileAttributes;
        ULONG FileNameLength;
        ULONG EaSize;
        CCHAR ShortNameLength;
        WCHAR ShortName[12];
        WCHAR FileName[1];
    };
    static_assert(offsetof(SHIM_FILE_BOTH_DIR_INFORMATION, FileName) == 94);

    struct SHIM_FILE_NAMES_INFORMATION
    {
        ULONG NextEntryOffset;
        ULONG FileIndex;
        ULONG FileNameLength;
        WCHAR FileName[1];
    };
    static_assert(offsetof(SHIM_FILE_NAMES_INFORMATION, FileName) == 12);

    struct SHIM_FILE_ID_BOTH_DIR_INFORMATION
    {
        ULONG NextEntryOffset;
        ULONG FileIndex;
        LARGE_INTEGER CreationTime;
        LARGE_INTEGER LastAccessTime;
        LARGE_INTEGER LastWriteTime;
        LARGE_INTEGER ChangeTime;
        LARGE_INTEGER EndOfFile;
        LARGE_INTEGER AllocationSize;
        ULONG FileAttributes;
        ULONG FileNameLength;
        ULONG EaSize;
        CCHAR ShortNameLength;
        WCHAR ShortName[12];
        LARGE_INTEGER FileId;
        WCHAR FileName[1];
    };
    static_assert(offsetof(SHIM_FILE_ID_BOTH_DIR_INFORMATION, FileName) == 104);

    struct SHIM_FILE_ID_FULL_DIR_INFORMATION
    {
        ULONG NextEntryOffset;
        ULONG FileIndex;
        LARGE_INTEGER CreationTime;
        LARGE_INTEGER LastAccessTime;
        LARGE_INTEGER LastWriteTime;
        LARGE_INTEGER ChangeTime;
        LARGE_INTEGER EndOfFile;
        LARGE_INTEGER AllocationSize;
        ULONG FileAttributes;
        ULONG FileNameLength;
        ULONG EaSize;
        LARGE_INTEGER FileId;
        WCHAR FileName[1];
    };
    static_assert(offsetof(SHIM_FILE_ID_FULL_DIR_INFORMATION, FileName) == 80);

    struct DirectoryEntryLayout
    {
        size_t fileNameOffset;
        size_t fileNameLengthOffset;
        size_t fileAttributesOffset;
        bool hasAttributes;
    };

    using PFN_NtQueryDirectoryFile = NTSTATUS(NTAPI*)(
        HANDLE FileHandle,
        HANDLE Event,
        PIO_APC_ROUTINE ApcRoutine,
        PVOID ApcContext,
        PIO_STATUS_BLOCK IoStatusBlock,
        PVOID FileInformation,
        ULONG Length,
        ULONG FileInformationClass,
        BOOLEAN ReturnSingleEntry,
        PUNICODE_STRING FileName,
        BOOLEAN RestartScan);

    using PFN_NtQueryDirectoryFileEx = NTSTATUS(NTAPI*)(
        HANDLE FileHandle,
        HANDLE Event,
        PIO_APC_ROUTINE ApcRoutine,
        PVOID ApcContext,
        PIO_STATUS_BLOCK IoStatusBlock,
        PVOID FileInformation,
        ULONG Length,
        ULONG FileInformationClass,
        ULONG QueryFlags,
        PUNICODE_STRING FileName);

    using PFN_NtCreateFile = NTSTATUS(NTAPI*)(
        PHANDLE FileHandle,
        ACCESS_MASK DesiredAccess,
        POBJECT_ATTRIBUTES ObjectAttributes,
        PIO_STATUS_BLOCK IoStatusBlock,
        PLARGE_INTEGER AllocationSize,
        ULONG FileAttributes,
        ULONG ShareAccess,
        ULONG CreateDisposition,
        ULONG CreateOptions,
        PVOID EaBuffer,
        ULONG EaLength);

    using PFN_NtOpenFile = NTSTATUS(NTAPI*)(
        PHANDLE FileHandle,
        ACCESS_MASK DesiredAccess,
        POBJECT_ATTRIBUTES ObjectAttributes,
        PIO_STATUS_BLOCK IoStatusBlock,
        ULONG ShareAccess,
        ULONG OpenOptions);

    DWORD g_reentrancyTlsIndex = TLS_OUT_OF_INDEXES;
    SRWLOCK g_activeSearchesLock = SRWLOCK_INIT;
    std::unordered_map<HANDLE, std::wstring>* g_activeSearches = nullptr;
    SRWLOCK g_enumeratedDirsLock = SRWLOCK_INIT;
    std::unordered_set<HANDLE>* g_enumeratedDirsWithPriorResults = nullptr;
    SRWLOCK g_virtualSeedLock = SRWLOCK_INIT;

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

    PFN_NtQueryDirectoryFile RealNtQueryDirectoryFile = nullptr;
    PFN_NtQueryDirectoryFileEx RealNtQueryDirectoryFileEx = nullptr;
    PFN_NtCreateFile RealNtCreateFile = nullptr;
    PFN_NtOpenFile RealNtOpenFile = nullptr;

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

    bool HasPriorDirectoryResults(HANDLE hDir)
    {
        if (hDir == INVALID_HANDLE_VALUE || hDir == nullptr)
        {
            return false;
        }

        bool found = false;
        AcquireSRWLockShared(&g_enumeratedDirsLock);
        if (g_enumeratedDirsWithPriorResults != nullptr)
        {
            found = g_enumeratedDirsWithPriorResults->find(hDir) != g_enumeratedDirsWithPriorResults->end();
        }
        ReleaseSRWLockShared(&g_enumeratedDirsLock);
        return found;
    }

    void MarkDirectoryHandleHasResults(HANDLE hDir)
    {
        if (hDir == INVALID_HANDLE_VALUE || hDir == nullptr)
        {
            return;
        }

        AcquireSRWLockExclusive(&g_enumeratedDirsLock);
        if (g_enumeratedDirsWithPriorResults != nullptr)
        {
            g_enumeratedDirsWithPriorResults->insert(hDir);
        }
        ReleaseSRWLockExclusive(&g_enumeratedDirsLock);
    }

    void UntrackEnumeratedDirHandle(HANDLE hDir)
    {
        if (hDir == INVALID_HANDLE_VALUE || hDir == nullptr)
        {
            return;
        }

        AcquireSRWLockShared(&g_enumeratedDirsLock);
        const bool maybeTracked = g_enumeratedDirsWithPriorResults != nullptr &&
            !g_enumeratedDirsWithPriorResults->empty();
        ReleaseSRWLockShared(&g_enumeratedDirsLock);

        if (!maybeTracked)
        {
            return;
        }

        AcquireSRWLockExclusive(&g_enumeratedDirsLock);
        if (g_enumeratedDirsWithPriorResults != nullptr)
        {
            g_enumeratedDirsWithPriorResults->erase(hDir);
        }
        ReleaseSRWLockExclusive(&g_enumeratedDirsLock);
    }

    bool IsAsciiAlpha(wchar_t ch) noexcept
    {
        return (ch >= L'A' && ch <= L'Z') || (ch >= L'a' && ch <= L'z');
    }

    bool TryGetHandleCanonicalPath(HANDLE handle, std::wstring& canonicalOut)
    {
        canonicalOut.clear();
        if (handle == nullptr || handle == INVALID_HANDLE_VALUE)
        {
            return false;
        }

        wchar_t stackBuffer[MAX_PATH]{};
        DWORD chars = GetFinalPathNameByHandleW(
            handle,
            stackBuffer,
            MAX_PATH,
            FILE_NAME_NORMALIZED | VOLUME_NAME_DOS);
        if (chars == 0)
        {
            return false;
        }

        if (chars < MAX_PATH)
        {
            canonicalOut = PathFilter::CanonicalizePath(std::wstring_view(stackBuffer, chars));
            return !canonicalOut.empty();
        }

        std::vector<wchar_t> heapBuffer(static_cast<size_t>(chars) + 1, L'\0');
        chars = GetFinalPathNameByHandleW(
            handle,
            heapBuffer.data(),
            static_cast<DWORD>(heapBuffer.size()),
            FILE_NAME_NORMALIZED | VOLUME_NAME_DOS);
        if (chars == 0 || chars >= heapBuffer.size())
        {
            return false;
        }

        canonicalOut = PathFilter::CanonicalizePath(std::wstring_view(heapBuffer.data(), chars));
        return !canonicalOut.empty();
    }

    bool IsDosOrUncFilesystemNtPath(std::wstring_view rawPath) noexcept
    {
        if (rawPath.size() >= 7 &&
            (rawPath.compare(0, 4, L"\\??\\") == 0 || rawPath.compare(0, 4, L"\\\\?\\") == 0))
        {
            const std::wstring_view tail = rawPath.substr(4);
            if (tail.size() >= 3 &&
                IsAsciiAlpha(tail[0]) &&
                tail[1] == L':' &&
                (tail[2] == L'\\' || tail[2] == L'/'))
            {
                return true;
            }

            if (tail.size() >= 4 &&
                PathFilter::EqualsOrdinalIgnoreCase(tail.substr(0, 4), L"UNC\\"))
            {
                return true;
            }

            return false;
        }

        if (rawPath.size() >= 3 &&
            IsAsciiAlpha(rawPath[0]) &&
            rawPath[1] == L':' &&
            (rawPath[2] == L'\\' || rawPath[2] == L'/'))
        {
            return true;
        }

        if (rawPath.size() >= 3 && rawPath[0] == L'\\' && rawPath[1] == L'\\')
        {
            return rawPath[2] != L'.' && rawPath[2] != L'?';
        }

        return false;
    }

    bool TryResolveObjectAttributesPath(POBJECT_ATTRIBUTES objectAttributes, std::wstring& canonicalOut)
    {
        canonicalOut.clear();
        if (objectAttributes == nullptr ||
            objectAttributes->ObjectName == nullptr ||
            objectAttributes->ObjectName->Buffer == nullptr ||
            (objectAttributes->ObjectName->Length % sizeof(wchar_t)) != 0)
        {
            return false;
        }

        const size_t charCount = objectAttributes->ObjectName->Length / sizeof(wchar_t);
        std::wstring_view rawName(objectAttributes->ObjectName->Buffer, charCount);

        if (objectAttributes->RootDirectory != nullptr)
        {
            std::wstring rootCanonical;
            if (!TryGetHandleCanonicalPath(objectAttributes->RootDirectory, rootCanonical))
            {
                return false;
            }

            while (!rawName.empty() && (rawName.front() == L'\\' || rawName.front() == L'/'))
            {
                rawName.remove_prefix(1);
            }

            if (rawName.empty())
            {
                canonicalOut = std::move(rootCanonical);
                return true;
            }

            std::wstring combined = std::move(rootCanonical);
            if (combined.back() != L'\\')
            {
                combined.push_back(L'\\');
            }
            combined.append(rawName);
            canonicalOut = PathFilter::CanonicalizePath(combined);
            return !canonicalOut.empty();
        }

        if (rawName.empty() || !IsDosOrUncFilesystemNtPath(rawName))
        {
            return false;
        }

        canonicalOut = PathFilter::CanonicalizePath(rawName);
        return !canonicalOut.empty();
    }

    void EnsureDirectoryTreeExists(std::wstring_view dirPath)
    {
        if (dirPath.empty())
        {
            return;
        }

        std::wstring path(dirPath);
        if (RealGetFileAttributesW(path.c_str()) != INVALID_FILE_ATTRIBUTES)
        {
            return;
        }

        size_t pos = 0;
        if (path.size() >= 3 && IsAsciiAlpha(path[0]) && path[1] == L':' && path[2] == L'\\')
        {
            pos = 3;
        }
        else if (path.size() >= 2 && path[0] == L'\\' && path[1] == L'\\')
        {
            const size_t serverEnd = path.find(L'\\', 2);
            if (serverEnd == std::wstring::npos)
            {
                return;
            }
            const size_t shareEnd = path.find(L'\\', serverEnd + 1);
            if (shareEnd == std::wstring::npos)
            {
                return;
            }
            pos = shareEnd + 1;
        }

        while (pos <= path.size())
        {
            const size_t nextSlash = path.find(L'\\', pos);
            const size_t endIdx = (nextSlash == std::wstring::npos) ? path.size() : nextSlash;
            if (endIdx > pos)
            {
                const std::wstring prefix = path.substr(0, endIdx);
                if (!CreateDirectoryW(prefix.c_str(), nullptr))
                {
                    const DWORD err = GetLastError();
                    if (err != ERROR_ALREADY_EXISTS)
                    {
                        return;
                    }
                }
            }

            if (nextSlash == std::wstring::npos)
            {
                break;
            }
            pos = nextSlash + 1;
        }
    }

    void EnsureVirtualizedSeedFile(
        const std::wstring& virtualPath,
        const std::wstring& seedJson,
        bool allowCreateFile)
    {
        if (allowCreateFile && RealGetFileAttributesW(virtualPath.c_str()) != INVALID_FILE_ATTRIBUTES)
        {
            return;
        }

        AcquireSRWLockExclusive(&g_virtualSeedLock);

        const size_t lastSlash = virtualPath.rfind(L'\\');
        if (lastSlash != std::wstring::npos)
        {
            EnsureDirectoryTreeExists(std::wstring_view(virtualPath).substr(0, lastSlash));
        }

        if (allowCreateFile && RealGetFileAttributesW(virtualPath.c_str()) == INVALID_FILE_ATTRIBUTES)
        {
            std::string utf8Bytes;
            if (!seedJson.empty())
            {
                const int needed = WideCharToMultiByte(
                    CP_UTF8,
                    0,
                    seedJson.data(),
                    static_cast<int>(seedJson.size()),
                    nullptr,
                    0,
                    nullptr,
                    nullptr);
                if (needed > 0)
                {
                    utf8Bytes.resize(static_cast<size_t>(needed));
                    (void)WideCharToMultiByte(
                        CP_UTF8,
                        0,
                        seedJson.data(),
                        static_cast<int>(seedJson.size()),
                        utf8Bytes.data(),
                        needed,
                        nullptr,
                        nullptr);
                }
            }

            const HANDLE hSeed = RealCreateFileW(
                virtualPath.c_str(),
                GENERIC_WRITE,
                0,
                nullptr,
                CREATE_NEW,
                FILE_ATTRIBUTE_NORMAL,
                nullptr);
            if (hSeed != INVALID_HANDLE_VALUE)
            {
                size_t offset = 0;
                while (offset < utf8Bytes.size())
                {
                    DWORD written = 0;
                    const DWORD chunk = static_cast<DWORD>(utf8Bytes.size() - offset);
                    if (!WriteFile(hSeed, utf8Bytes.data() + offset, chunk, &written, nullptr) || written == 0)
                    {
                        break;
                    }
                    offset += written;
                }
                CloseHandle(hSeed);
            }
        }

        ReleaseSRWLockExclusive(&g_virtualSeedLock);
    }

    bool TryGetDirectoryEntryLayout(ULONG infoClass, DirectoryEntryLayout& layoutOut) noexcept
    {
        switch (infoClass)
        {
        case kFileDirectoryInformation:
            layoutOut = {
                offsetof(SHIM_FILE_DIRECTORY_INFORMATION, FileName),
                offsetof(SHIM_FILE_DIRECTORY_INFORMATION, FileNameLength),
                offsetof(SHIM_FILE_DIRECTORY_INFORMATION, FileAttributes),
                true
            };
            return true;

        case kFileFullDirectoryInformation:
            layoutOut = {
                offsetof(SHIM_FILE_FULL_DIR_INFORMATION, FileName),
                offsetof(SHIM_FILE_FULL_DIR_INFORMATION, FileNameLength),
                offsetof(SHIM_FILE_FULL_DIR_INFORMATION, FileAttributes),
                true
            };
            return true;

        case kFileBothDirectoryInformation:
            layoutOut = {
                offsetof(SHIM_FILE_BOTH_DIR_INFORMATION, FileName),
                offsetof(SHIM_FILE_BOTH_DIR_INFORMATION, FileNameLength),
                offsetof(SHIM_FILE_BOTH_DIR_INFORMATION, FileAttributes),
                true
            };
            return true;

        case kFileNamesInformation:
            layoutOut = {
                offsetof(SHIM_FILE_NAMES_INFORMATION, FileName),
                offsetof(SHIM_FILE_NAMES_INFORMATION, FileNameLength),
                0,
                false
            };
            return true;

        case kFileIdBothDirectoryInformation:
            layoutOut = {
                offsetof(SHIM_FILE_ID_BOTH_DIR_INFORMATION, FileName),
                offsetof(SHIM_FILE_ID_BOTH_DIR_INFORMATION, FileNameLength),
                offsetof(SHIM_FILE_ID_BOTH_DIR_INFORMATION, FileAttributes),
                true
            };
            return true;

        case kFileIdFullDirectoryInformation:
            layoutOut = {
                offsetof(SHIM_FILE_ID_FULL_DIR_INFORMATION, FileName),
                offsetof(SHIM_FILE_ID_FULL_DIR_INFORMATION, FileNameLength),
                offsetof(SHIM_FILE_ID_FULL_DIR_INFORMATION, FileAttributes),
                true
            };
            return true;

        default:
            return false;
        }
    }

    void FilterNtDirectoryBuffer(
        BYTE* buffer,
        size_t totalBytes,
        const DirectoryEntryLayout& layout,
        const std::wstring& canonicalDir,
        const PathFilter& filter,
        bool dirAllowed,
        size_t& outBytes)
    {
        outBytes = 0;
        if (buffer == nullptr || totalBytes == 0)
        {
            return;
        }

        constexpr size_t kInvalidOffset = static_cast<size_t>(-1);
        size_t readOffset = 0;
        size_t writeOffset = 0;
        size_t prevKeptOffset = kInvalidOffset;
        size_t finalEndOffset = 0;

        std::wstring entryPath;
        entryPath.reserve(canonicalDir.size() + 64);

        while (readOffset + sizeof(ULONG) <= totalBytes)
        {
            ULONG nextOffset = 0;
            std::memcpy(&nextOffset, buffer + readOffset, sizeof(ULONG));

            const size_t avail = totalBytes - readOffset;
            size_t rawSpan = avail;
            if (nextOffset != 0)
            {
                if (nextOffset > avail || nextOffset < layout.fileNameOffset)
                {
                    break;
                }
                rawSpan = nextOffset;
            }

            if (rawSpan < layout.fileNameOffset)
            {
                break;
            }

            ULONG fileNameLengthBytes = 0;
            std::memcpy(&fileNameLengthBytes, buffer + readOffset + layout.fileNameLengthOffset, sizeof(ULONG));
            if (fileNameLengthBytes > rawSpan - layout.fileNameOffset ||
                (fileNameLengthBytes % sizeof(wchar_t)) != 0)
            {
                break;
            }

            const size_t exactBytes = layout.fileNameOffset + fileNameLengthBytes;
            const std::wstring_view fileName(
                reinterpret_cast<const wchar_t*>(buffer + readOffset + layout.fileNameOffset),
                fileNameLengthBytes / sizeof(wchar_t));

            std::optional<bool> isDir;
            if (layout.hasAttributes)
            {
                ULONG attrs = 0;
                std::memcpy(&attrs, buffer + readOffset + layout.fileAttributesOffset, sizeof(ULONG));
                isDir = (attrs & FILE_ATTRIBUTE_DIRECTORY) != 0;
            }

            bool keep = false;
            if (!dirAllowed)
            {
                keep = false;
            }
            else if (fileName == L"." || fileName == L"..")
            {
                keep = true;
            }
            else
            {
                entryPath.assign(canonicalDir);
                if (!entryPath.empty() && entryPath.back() != L'\\')
                {
                    entryPath.push_back(L'\\');
                }
                entryPath.append(fileName);
                keep = filter.IsPathAllowed(entryPath, isDir);
            }

            if (keep)
            {
                if (writeOffset == readOffset)
                {
                    prevKeptOffset = writeOffset;
                    finalEndOffset = writeOffset + exactBytes;
                    writeOffset = (nextOffset == 0) ? finalEndOffset : (readOffset + nextOffset);
                }
                else
                {
                    std::memmove(buffer + writeOffset, buffer + readOffset, exactBytes);
                    if (prevKeptOffset != kInvalidOffset)
                    {
                        const ULONG linkOffset = static_cast<ULONG>(writeOffset - prevKeptOffset);
                        std::memcpy(buffer + prevKeptOffset, &linkOffset, sizeof(ULONG));
                    }

                    const ULONG zero = 0;
                    std::memcpy(buffer + writeOffset, &zero, sizeof(ULONG));

                    prevKeptOffset = writeOffset;
                    finalEndOffset = writeOffset + exactBytes;

                    const size_t align8End = (finalEndOffset + 7u) & ~static_cast<size_t>(7u);
                    const size_t align4End = (finalEndOffset + 3u) & ~static_cast<size_t>(3u);
                    const size_t nextReadPos = (nextOffset == 0) ? totalBytes : (readOffset + nextOffset);
                    writeOffset = (align8End <= nextReadPos)
                        ? align8End
                        : ((align4End <= nextReadPos) ? align4End : finalEndOffset);
                }
            }

            if (nextOffset == 0)
            {
                break;
            }

            readOffset += nextOffset;
        }

        if (prevKeptOffset != kInvalidOffset)
        {
            const ULONG zero = 0;
            std::memcpy(buffer + prevKeptOffset, &zero, sizeof(ULONG));
            outBytes = finalEndOffset;
        }
    }

    template <typename InvokeFn>
    NTSTATUS ExecuteFilteredDirectoryQuery(
        HANDLE fileHandle,
        PIO_STATUS_BLOCK ioStatusBlock,
        PVOID fileInformation,
        ULONG length,
        ULONG fileInformationClass,
        bool isRestartScan,
        InvokeFn&& invokeReal)
    {
        if (ioStatusBlock == nullptr || fileInformation == nullptr || length == 0)
        {
            return invokeReal(true);
        }

        DirectoryEntryLayout layout{};
        if (!TryGetDirectoryEntryLayout(fileInformationClass, layout))
        {
            return invokeReal(true);
        }

        std::wstring canonicalDir;
        if (!TryGetHandleCanonicalPath(fileHandle, canonicalDir))
        {
            return invokeReal(true);
        }

        PathFilter& filter = PathFilter::Instance();
        filter.EnsureInitialized();

        const bool dirAllowed = filter.IsPathAllowed(canonicalDir, true);
        const bool isFiltered = filter.IsDirectoryFiltered(canonicalDir);
        if (!isFiltered && dirAllowed)
        {
            return invokeReal(true);
        }

        if (isRestartScan)
        {
            UntrackEnumeratedDirHandle(fileHandle);
        }

        const bool hasPriorResults = !isRestartScan && HasPriorDirectoryResults(fileHandle);
        bool filteredOutAllEntries = false;
        bool firstIteration = true;

        while (true)
        {
            const NTSTATUS status = invokeReal(firstIteration);
            firstIteration = false;

            if (!NT_SUCCESS(status) || status == kStatusPending)
            {
                if (filteredOutAllEntries &&
                    (status == kStatusNoMoreFiles || status == kStatusNoSuchFile))
                {
                    const NTSTATUS emptyStatus = hasPriorResults ? kStatusNoMoreFiles : kStatusNoSuchFile;
                    ioStatusBlock->Status = emptyStatus;
                    ioStatusBlock->Information = 0;
                    return emptyStatus;
                }

                return status;
            }

            const size_t totalBytes = (ioStatusBlock->Information > length)
                ? static_cast<size_t>(length)
                : static_cast<size_t>(ioStatusBlock->Information);
            if (totalBytes == 0)
            {
                return status;
            }

            size_t remainingBytes = 0;
            FilterNtDirectoryBuffer(
                static_cast<BYTE*>(fileInformation),
                totalBytes,
                layout,
                canonicalDir,
                filter,
                dirAllowed,
                remainingBytes);

            if (remainingBytes > 0)
            {
                ioStatusBlock->Information = static_cast<ULONG_PTR>(remainingBytes);
                MarkDirectoryHandleHasResults(fileHandle);
                return status;
            }

            filteredOutAllEntries = true;
        }
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

        LPCWSTR effectiveFileName = lpFileName;
        std::wstring virtualPath;
        if (lpFileName != nullptr)
        {
            PathFilter& filter = PathFilter::Instance();
            filter.EnsureInitialized();

            std::wstring seedJson;
            if (filter.TryResolveVirtualizedPath(lpFileName, virtualPath, seedJson))
            {
                EnsureVirtualizedSeedFile(virtualPath, seedJson, dwCreationDisposition != CREATE_NEW);
                effectiveFileName = virtualPath.c_str();
            }
            else if (!filter.IsPathAllowed(lpFileName))
            {
                SetLastError(ERROR_FILE_NOT_FOUND);
                return INVALID_HANDLE_VALUE;
            }
        }

        const HANDLE hResult = RealCreateFileW(
            effectiveFileName,
            dwDesiredAccess,
            dwShareMode,
            lpSecurityAttributes,
            dwCreationDisposition,
            dwFlagsAndAttributes,
            hTemplateFile);
        if (hResult != INVALID_HANDLE_VALUE)
        {
            UntrackEnumeratedDirHandle(hResult);
        }
        return hResult;
    }

    DWORD WINAPI HookGetFileAttributesW(LPCWSTR lpFileName)
    {
        ReentrancyGuard guard;
        if (!guard.Entered())
        {
            return RealGetFileAttributesW(lpFileName);
        }

        LPCWSTR effectiveFileName = lpFileName;
        std::wstring virtualPath;
        if (lpFileName != nullptr)
        {
            PathFilter& filter = PathFilter::Instance();
            filter.EnsureInitialized();

            std::wstring seedJson;
            if (filter.TryResolveVirtualizedPath(lpFileName, virtualPath, seedJson))
            {
                EnsureVirtualizedSeedFile(virtualPath, seedJson, true);
                effectiveFileName = virtualPath.c_str();
            }
            else if (!filter.IsPathAllowed(lpFileName))
            {
                SetLastError(ERROR_FILE_NOT_FOUND);
                return INVALID_FILE_ATTRIBUTES;
            }
        }

        return RealGetFileAttributesW(effectiveFileName);
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

        LPCWSTR effectiveFileName = lpFileName;
        std::wstring virtualPath;
        if (lpFileName != nullptr)
        {
            PathFilter& filter = PathFilter::Instance();
            filter.EnsureInitialized();

            std::wstring seedJson;
            if (filter.TryResolveVirtualizedPath(lpFileName, virtualPath, seedJson))
            {
                EnsureVirtualizedSeedFile(virtualPath, seedJson, true);
                effectiveFileName = virtualPath.c_str();
            }
            else if (!filter.IsPathAllowed(lpFileName))
            {
                SetLastError(ERROR_FILE_NOT_FOUND);
                return FALSE;
            }
        }

        return RealGetFileAttributesExW(effectiveFileName, fInfoLevelId, lpFileInformation);
    }

    enum class NtTargetAction
    {
        PassThrough,
        Block,
        Redirect
    };

    NtTargetAction EvaluateNtFileTarget(
        POBJECT_ATTRIBUTES objectAttributes,
        ULONG options,
        bool allowCreateSeed,
        std::wstring& ntVirtualPathOut)
    {
        std::wstring canonicalPath;
        if (!TryResolveObjectAttributesPath(objectAttributes, canonicalPath))
        {
            return NtTargetAction::PassThrough;
        }

        PathFilter& filter = PathFilter::Instance();
        filter.EnsureInitialized();

        std::wstring virtualPath;
        std::wstring seedJson;
        if (filter.TryResolveVirtualizedPath(canonicalPath, virtualPath, seedJson))
        {
            if ((options & FILE_DIRECTORY_FILE) == 0)
            {
                EnsureVirtualizedSeedFile(virtualPath, seedJson, allowCreateSeed);
            }

            ntVirtualPathOut = PathFilter::ToNtPath(virtualPath);
            if (ntVirtualPathOut.empty() || ntVirtualPathOut.size() * sizeof(wchar_t) > 0xFFFEu)
            {
                return NtTargetAction::PassThrough;
            }
            return NtTargetAction::Redirect;
        }

        std::optional<bool> isDirHint;
        if ((options & FILE_DIRECTORY_FILE) != 0)
        {
            isDirHint = true;
        }
        else if ((options & FILE_NON_DIRECTORY_FILE) != 0)
        {
            isDirHint = false;
        }

        return filter.IsPathAllowed(canonicalPath, isDirHint)
            ? NtTargetAction::PassThrough
            : NtTargetAction::Block;
    }

    NTSTATUS NTAPI HookNtQueryDirectoryFile(
        HANDLE FileHandle,
        HANDLE Event,
        PIO_APC_ROUTINE ApcRoutine,
        PVOID ApcContext,
        PIO_STATUS_BLOCK IoStatusBlock,
        PVOID FileInformation,
        ULONG Length,
        ULONG FileInformationClass,
        BOOLEAN ReturnSingleEntry,
        PUNICODE_STRING FileName,
        BOOLEAN RestartScan)
    {
        ReentrancyGuard guard;
        if (!guard.Entered())
        {
            return RealNtQueryDirectoryFile(
                FileHandle,
                Event,
                ApcRoutine,
                ApcContext,
                IoStatusBlock,
                FileInformation,
                Length,
                FileInformationClass,
                ReturnSingleEntry,
                FileName,
                RestartScan);
        }

        return ExecuteFilteredDirectoryQuery(
            FileHandle,
            IoStatusBlock,
            FileInformation,
            Length,
            FileInformationClass,
            RestartScan != FALSE,
            [&](bool firstCall) -> NTSTATUS
            {
                return RealNtQueryDirectoryFile(
                    FileHandle,
                    Event,
                    ApcRoutine,
                    ApcContext,
                    IoStatusBlock,
                    FileInformation,
                    Length,
                    FileInformationClass,
                    ReturnSingleEntry,
                    FileName,
                    firstCall ? RestartScan : FALSE);
            });
    }

    NTSTATUS NTAPI HookNtQueryDirectoryFileEx(
        HANDLE FileHandle,
        HANDLE Event,
        PIO_APC_ROUTINE ApcRoutine,
        PVOID ApcContext,
        PIO_STATUS_BLOCK IoStatusBlock,
        PVOID FileInformation,
        ULONG Length,
        ULONG FileInformationClass,
        ULONG QueryFlags,
        PUNICODE_STRING FileName)
    {
        ReentrancyGuard guard;
        if (!guard.Entered())
        {
            return RealNtQueryDirectoryFileEx(
                FileHandle,
                Event,
                ApcRoutine,
                ApcContext,
                IoStatusBlock,
                FileInformation,
                Length,
                FileInformationClass,
                QueryFlags,
                FileName);
        }

        return ExecuteFilteredDirectoryQuery(
            FileHandle,
            IoStatusBlock,
            FileInformation,
            Length,
            FileInformationClass,
            (QueryFlags & kSlRestartScan) != 0,
            [&](bool firstCall) -> NTSTATUS
            {
                const ULONG effectiveFlags = firstCall ? QueryFlags : (QueryFlags & ~kSlRestartScan);
                return RealNtQueryDirectoryFileEx(
                    FileHandle,
                    Event,
                    ApcRoutine,
                    ApcContext,
                    IoStatusBlock,
                    FileInformation,
                    Length,
                    FileInformationClass,
                    effectiveFlags,
                    FileName);
            });
    }

    NTSTATUS NTAPI HookNtCreateFile(
        PHANDLE FileHandle,
        ACCESS_MASK DesiredAccess,
        POBJECT_ATTRIBUTES ObjectAttributes,
        PIO_STATUS_BLOCK IoStatusBlock,
        PLARGE_INTEGER AllocationSize,
        ULONG FileAttributes,
        ULONG ShareAccess,
        ULONG CreateDisposition,
        ULONG CreateOptions,
        PVOID EaBuffer,
        ULONG EaLength)
    {
        ReentrancyGuard guard;
        if (!guard.Entered())
        {
            return RealNtCreateFile(
                FileHandle,
                DesiredAccess,
                ObjectAttributes,
                IoStatusBlock,
                AllocationSize,
                FileAttributes,
                ShareAccess,
                CreateDisposition,
                CreateOptions,
                EaBuffer,
                EaLength);
        }

        std::wstring ntVirtualPath;
        const NtTargetAction action = EvaluateNtFileTarget(
            ObjectAttributes,
            CreateOptions,
            CreateDisposition != kFileCreateDisposition,
            ntVirtualPath);

        if (action == NtTargetAction::Block)
        {
            if (FileHandle != nullptr)
            {
                *FileHandle = nullptr;
            }
            if (IoStatusBlock != nullptr)
            {
                IoStatusBlock->Status = kStatusObjectNameNotFound;
                IoStatusBlock->Information = 0;
            }
            return kStatusObjectNameNotFound;
        }

        POBJECT_ATTRIBUTES effectiveAttributes = ObjectAttributes;
        UNICODE_STRING virtualUnicodeName{};
        OBJECT_ATTRIBUTES virtualObjectAttributes{};
        if (action == NtTargetAction::Redirect)
        {
            virtualUnicodeName.Length = static_cast<USHORT>(ntVirtualPath.size() * sizeof(wchar_t));
            virtualUnicodeName.MaximumLength = static_cast<USHORT>((ntVirtualPath.size() + 1) * sizeof(wchar_t));
            virtualUnicodeName.Buffer = const_cast<PWSTR>(ntVirtualPath.c_str());

            virtualObjectAttributes = *ObjectAttributes;
            virtualObjectAttributes.RootDirectory = nullptr;
            virtualObjectAttributes.ObjectName = &virtualUnicodeName;
            effectiveAttributes = &virtualObjectAttributes;
        }

        const NTSTATUS status = RealNtCreateFile(
            FileHandle,
            DesiredAccess,
            effectiveAttributes,
            IoStatusBlock,
            AllocationSize,
            FileAttributes,
            ShareAccess,
            CreateDisposition,
            CreateOptions,
            EaBuffer,
            EaLength);
        if (NT_SUCCESS(status) && FileHandle != nullptr && *FileHandle != nullptr)
        {
            UntrackEnumeratedDirHandle(*FileHandle);
        }
        return status;
    }

    NTSTATUS NTAPI HookNtOpenFile(
        PHANDLE FileHandle,
        ACCESS_MASK DesiredAccess,
        POBJECT_ATTRIBUTES ObjectAttributes,
        PIO_STATUS_BLOCK IoStatusBlock,
        ULONG ShareAccess,
        ULONG OpenOptions)
    {
        ReentrancyGuard guard;
        if (!guard.Entered())
        {
            return RealNtOpenFile(
                FileHandle,
                DesiredAccess,
                ObjectAttributes,
                IoStatusBlock,
                ShareAccess,
                OpenOptions);
        }

        std::wstring ntVirtualPath;
        const NtTargetAction action = EvaluateNtFileTarget(
            ObjectAttributes,
            OpenOptions,
            true,
            ntVirtualPath);

        if (action == NtTargetAction::Block)
        {
            if (FileHandle != nullptr)
            {
                *FileHandle = nullptr;
            }
            if (IoStatusBlock != nullptr)
            {
                IoStatusBlock->Status = kStatusObjectNameNotFound;
                IoStatusBlock->Information = 0;
            }
            return kStatusObjectNameNotFound;
        }

        POBJECT_ATTRIBUTES effectiveAttributes = ObjectAttributes;
        UNICODE_STRING virtualUnicodeName{};
        OBJECT_ATTRIBUTES virtualObjectAttributes{};
        if (action == NtTargetAction::Redirect)
        {
            virtualUnicodeName.Length = static_cast<USHORT>(ntVirtualPath.size() * sizeof(wchar_t));
            virtualUnicodeName.MaximumLength = static_cast<USHORT>((ntVirtualPath.size() + 1) * sizeof(wchar_t));
            virtualUnicodeName.Buffer = const_cast<PWSTR>(ntVirtualPath.c_str());

            virtualObjectAttributes = *ObjectAttributes;
            virtualObjectAttributes.RootDirectory = nullptr;
            virtualObjectAttributes.ObjectName = &virtualUnicodeName;
            effectiveAttributes = &virtualObjectAttributes;
        }

        const NTSTATUS status = RealNtOpenFile(
            FileHandle,
            DesiredAccess,
            effectiveAttributes,
            IoStatusBlock,
            ShareAccess,
            OpenOptions);
        if (NT_SUCCESS(status) && FileHandle != nullptr && *FileHandle != nullptr)
        {
            UntrackEnumeratedDirHandle(*FileHandle);
        }
        return status;
    }

    BOOL ResolveNtHooks() noexcept
    {
        const HMODULE ntdll = GetModuleHandleW(L"ntdll.dll");
        if (ntdll == nullptr)
        {
            return FALSE;
        }

        RealNtQueryDirectoryFile = reinterpret_cast<PFN_NtQueryDirectoryFile>(
            GetProcAddress(ntdll, "NtQueryDirectoryFile"));
        RealNtQueryDirectoryFileEx = reinterpret_cast<PFN_NtQueryDirectoryFileEx>(
            GetProcAddress(ntdll, "NtQueryDirectoryFileEx"));
        RealNtCreateFile = reinterpret_cast<PFN_NtCreateFile>(
            GetProcAddress(ntdll, "NtCreateFile"));
        RealNtOpenFile = reinterpret_cast<PFN_NtOpenFile>(
            GetProcAddress(ntdll, "NtOpenFile"));

        return RealNtQueryDirectoryFile != nullptr &&
            RealNtCreateFile != nullptr &&
            RealNtOpenFile != nullptr;
    }

    BOOL AttachHooks()
    {
        if (!ResolveNtHooks())
        {
            return FALSE;
        }

        DetourTransactionBegin();
        DetourUpdateThread(GetCurrentThread());
        DetourAttach(reinterpret_cast<PVOID*>(&RealFindFirstFileW), reinterpret_cast<PVOID>(HookFindFirstFileW));
        DetourAttach(reinterpret_cast<PVOID*>(&RealFindFirstFileExW), reinterpret_cast<PVOID>(HookFindFirstFileExW));
        DetourAttach(reinterpret_cast<PVOID*>(&RealFindNextFileW), reinterpret_cast<PVOID>(HookFindNextFileW));
        DetourAttach(reinterpret_cast<PVOID*>(&RealFindClose), reinterpret_cast<PVOID>(HookFindClose));
        DetourAttach(reinterpret_cast<PVOID*>(&RealCreateFileW), reinterpret_cast<PVOID>(HookCreateFileW));
        DetourAttach(reinterpret_cast<PVOID*>(&RealGetFileAttributesW), reinterpret_cast<PVOID>(HookGetFileAttributesW));
        DetourAttach(reinterpret_cast<PVOID*>(&RealGetFileAttributesExW), reinterpret_cast<PVOID>(HookGetFileAttributesExW));
        DetourAttach(reinterpret_cast<PVOID*>(&RealNtQueryDirectoryFile), reinterpret_cast<PVOID>(HookNtQueryDirectoryFile));
        if (RealNtQueryDirectoryFileEx != nullptr)
        {
            DetourAttach(reinterpret_cast<PVOID*>(&RealNtQueryDirectoryFileEx), reinterpret_cast<PVOID>(HookNtQueryDirectoryFileEx));
        }
        DetourAttach(reinterpret_cast<PVOID*>(&RealNtCreateFile), reinterpret_cast<PVOID>(HookNtCreateFile));
        DetourAttach(reinterpret_cast<PVOID*>(&RealNtOpenFile), reinterpret_cast<PVOID>(HookNtOpenFile));
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
        if (RealNtQueryDirectoryFile != nullptr)
        {
            DetourDetach(reinterpret_cast<PVOID*>(&RealNtQueryDirectoryFile), reinterpret_cast<PVOID>(HookNtQueryDirectoryFile));
        }
        if (RealNtQueryDirectoryFileEx != nullptr)
        {
            DetourDetach(reinterpret_cast<PVOID*>(&RealNtQueryDirectoryFileEx), reinterpret_cast<PVOID>(HookNtQueryDirectoryFileEx));
        }
        if (RealNtCreateFile != nullptr)
        {
            DetourDetach(reinterpret_cast<PVOID*>(&RealNtCreateFile), reinterpret_cast<PVOID>(HookNtCreateFile));
        }
        if (RealNtOpenFile != nullptr)
        {
            DetourDetach(reinterpret_cast<PVOID*>(&RealNtOpenFile), reinterpret_cast<PVOID>(HookNtOpenFile));
        }
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
        g_enumeratedDirsWithPriorResults = new std::unordered_set<HANDLE>();
        PathFilter::Instance().InitializeFromEnvironment();

        if (!AttachHooks())
        {
            delete g_activeSearches;
            g_activeSearches = nullptr;
            delete g_enumeratedDirsWithPriorResults;
            g_enumeratedDirsWithPriorResults = nullptr;
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

            AcquireSRWLockExclusive(&g_enumeratedDirsLock);
            delete g_enumeratedDirsWithPriorResults;
            g_enumeratedDirsWithPriorResults = nullptr;
            ReleaseSRWLockExclusive(&g_enumeratedDirsLock);

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
