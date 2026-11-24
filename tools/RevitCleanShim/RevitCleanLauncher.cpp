#include <windows.h>
#include "detours.h"

#include <string>
#include <vector>

namespace
{
    bool TryConvertWideToAnsiPath(LPCWSTR widePath, std::string& outAnsi)
    {
        outAnsi.clear();
        if (widePath == nullptr || widePath[0] == L'\0')
        {
            return false;
        }

        auto convert = [](LPCWSTR src, std::string& dest, BOOL* usedDefault) -> bool
        {
            const int needed = WideCharToMultiByte(CP_ACP, WC_NO_BEST_FIT_CHARS, src, -1, nullptr, 0, nullptr, usedDefault);
            if (needed <= 1)
            {
                return false;
            }

            std::vector<char> buf(static_cast<size_t>(needed), '\0');
            const int written = WideCharToMultiByte(CP_ACP, WC_NO_BEST_FIT_CHARS, src, -1, buf.data(), needed, nullptr, usedDefault);
            if (written <= 1)
            {
                return false;
            }

            dest.assign(buf.data(), static_cast<size_t>(written - 1));
            return true;
        };

        BOOL usedDefaultChar = FALSE;
        if (convert(widePath, outAnsi, &usedDefaultChar) && !usedDefaultChar)
        {
            return true;
        }

        // Fall back to 8.3 short path for PE import table compatibility when path contains non-ACP characters.
        const DWORD shortChars = GetShortPathNameW(widePath, nullptr, 0);
        if (shortChars > 0)
        {
            std::vector<wchar_t> shortBuf(shortChars + 1, L'\0');
            const DWORD written = GetShortPathNameW(widePath, shortBuf.data(), shortChars);
            if (written > 0 && written < shortBuf.size())
            {
                usedDefaultChar = FALSE;
                if (convert(shortBuf.data(), outAnsi, &usedDefaultChar) && !usedDefaultChar)
                {
                    return true;
                }
            }
        }

        return false;
    }
}

extern "C" __declspec(dllexport) DWORD WINAPI SyntheticLaunchSuspendedWithShimW(
    LPCWSTR applicationPath,
    LPWSTR commandLine,
    LPCWSTR currentDirectory,
    LPVOID unicodeEnvironmentBlock,
    LPCWSTR shimDllPath,
    HANDLE* outProcessHandle,
    HANDLE* outThreadHandle,
    DWORD* outProcessId)
{
    if (outProcessHandle != nullptr)
    {
        *outProcessHandle = nullptr;
    }
    if (outThreadHandle != nullptr)
    {
        *outThreadHandle = nullptr;
    }
    if (outProcessId != nullptr)
    {
        *outProcessId = 0;
    }

    if (applicationPath == nullptr || applicationPath[0] == L'\0' ||
        shimDllPath == nullptr || shimDllPath[0] == L'\0' ||
        outProcessHandle == nullptr || outThreadHandle == nullptr || outProcessId == nullptr)
    {
        return ERROR_INVALID_PARAMETER;
    }

    if (GetFileAttributesW(applicationPath) == INVALID_FILE_ATTRIBUTES)
    {
        const DWORD err = GetLastError();
        return err != ERROR_SUCCESS ? err : ERROR_FILE_NOT_FOUND;
    }

    if (GetFileAttributesW(shimDllPath) == INVALID_FILE_ATTRIBUTES)
    {
        const DWORD err = GetLastError();
        return err != ERROR_SUCCESS ? err : ERROR_FILE_NOT_FOUND;
    }

    std::string ansiShimDllPath;
    if (!TryConvertWideToAnsiPath(shimDllPath, ansiShimDllPath))
    {
        return ERROR_INVALID_NAME;
    }

    std::wstring mutableCommandLine;
    LPWSTR cmdLinePtr = nullptr;
    if (commandLine != nullptr && commandLine[0] != L'\0')
    {
        mutableCommandLine.assign(commandLine);
        cmdLinePtr = mutableCommandLine.data();
    }

    STARTUPINFOW startupInfo{};
    startupInfo.cb = sizeof(startupInfo);
    startupInfo.dwFlags = STARTF_USESHOWWINDOW;
    startupInfo.wShowWindow = SW_SHOWMINNOACTIVE;

    PROCESS_INFORMATION processInfo{};
    const BOOL created = DetourCreateProcessWithDllExW(
        applicationPath,
        cmdLinePtr,
        nullptr,
        nullptr,
        FALSE,
        CREATE_SUSPENDED | CREATE_UNICODE_ENVIRONMENT,
        unicodeEnvironmentBlock,
        currentDirectory,
        &startupInfo,
        &processInfo,
        ansiShimDllPath.c_str(),
        nullptr);

    if (!created)
    {
        const DWORD err = GetLastError();
        return err != ERROR_SUCCESS ? err : ERROR_DLL_INIT_FAILED;
    }

    *outProcessHandle = processInfo.hProcess;
    *outThreadHandle = processInfo.hThread;
    *outProcessId = processInfo.dwProcessId;
    return ERROR_SUCCESS;
}

BOOL APIENTRY DllMain(HMODULE hModule, DWORD ulReasonForCall, LPVOID)
{
    if (ulReasonForCall == DLL_PROCESS_ATTACH)
    {
        DisableThreadLibraryCalls(hModule);
    }
    return TRUE;
}
