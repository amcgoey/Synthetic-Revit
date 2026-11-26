using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;
using RevitWorkerPool.Containment;

namespace RevitWorkerPool.Launching;

/// <summary>
/// Fail-closed production worker launcher that verifies Authenticode signatures on
/// <c>RevitCleanShim.dll</c> and <c>RevitCleanLauncher.dll</c>, launches the worker
/// suspended with Detours DLL injection via <c>SyntheticLaunchSuspendedWithShimW</c>,
/// assigns the process to a Win32 Job Object prior to <c>ResumeThread</c>, and never
/// silently degrades to <see cref="StandardProcessLauncher"/>.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class CleanDetoursProcessLauncher : IWorkerProcessLauncher
{
    private const uint WtdUiNone = 2;
    private const uint WtdRevokeNone = 0;
    private const uint WtdChoiceFile = 1;
    private const uint WtdStateActionVerify = 1;
    private const uint WtdStateActionClose = 2;
    private const uint WtdProvFlagRevocationCheckNone = 0x00000010;
    private const string ExpectedSignerCommonName = "Synthetic";
    private static readonly Guid WinTrustActionGenericVerifyV2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

    private readonly string _shimDllPath;
    private readonly string _launcherDllPath;
    private readonly Action<string>? _diagnosticLogger;

    public CleanDetoursProcessLauncher(
        string? shimDllPath = null,
        string? launcherDllPath = null,
        Action<string>? diagnosticLogger = null)
    {
        _shimDllPath = string.IsNullOrWhiteSpace(shimDllPath)
            ? Path.Combine(AppContext.BaseDirectory, "RevitCleanShim.dll")
            : Path.GetFullPath(shimDllPath);

        _launcherDllPath = string.IsNullOrWhiteSpace(launcherDllPath)
            ? Path.Combine(AppContext.BaseDirectory, "RevitCleanLauncher.dll")
            : Path.GetFullPath(launcherDllPath);

        _diagnosticLogger = diagnosticLogger;
    }

    public string ShimDllPath => _shimDllPath;

    public string LauncherDllPath => _launcherDllPath;

    public bool UsesCleanShim => true;

    public Task<LaunchedWorkerProcess> LaunchAsync(
        WorkerLaunchRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        VerifySignedNativeBinary(_shimDllPath, "Clean-Launch Shim DLL (RevitCleanShim.dll)");
        VerifySignedNativeBinary(_launcherDllPath, "Clean-Launch Launcher DLL (RevitCleanLauncher.dll)");

        if (string.IsNullOrWhiteSpace(request.ExecutablePath) || !File.Exists(request.ExecutablePath))
        {
            throw new CleanShimLaunchException(
                $"Worker executable not found: '{request.ExecutablePath}'.");
        }

        string fullExePath = Path.GetFullPath(request.ExecutablePath);
        string workingDir = !string.IsNullOrWhiteSpace(request.WorkingDirectory)
            ? Path.GetFullPath(request.WorkingDirectory)
            : Path.GetDirectoryName(fullExePath) ?? Directory.GetCurrentDirectory();

        string workerId = string.IsNullOrWhiteSpace(request.WorkerId)
            ? WorkerLaunchRequest.CreateWorkerId()
            : request.WorkerId.Trim();

        bool hasExplicitYearOverride = request.AdditionalEnvironmentVariables is not null &&
            request.AdditionalEnvironmentVariables.ContainsKey("SYNTHETIC_CLEAN_SHIM_REVIT_YEAR");
        if (request.RevitVersion <= 0 && !hasExplicitYearOverride)
        {
            throw new CleanShimLaunchException(
                $"Invalid RevitVersion '{request.RevitVersion}' for clean-launch shim.");
        }

        Dictionary<string, string?>? extraEnv = null;
        if (request.RevitVersion > 0)
        {
            extraEnv = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["SYNTHETIC_CLEAN_SHIM_REVIT_YEAR"] = request.RevitVersion.ToString(CultureInfo.InvariantCulture)
            };
        }

        byte[] environmentBlock = StandardProcessLauncher.BuildUnicodeEnvironmentBlock(request, workerId, extraEnv);
        string commandLineText = string.IsNullOrWhiteSpace(request.Arguments)
            ? $"\"{fullExePath}\""
            : $"\"{fullExePath}\" {request.Arguments}";
        string profileRoot = ResolveProfileRootDirectory(request);

        IntPtr launcherModule = IntPtr.Zero;
        IntPtr rawProcessHandle = IntPtr.Zero;
        IntPtr rawThreadHandle = IntPtr.Zero;
        uint rawProcessId = 0;

        try
        {
            try
            {
                launcherModule = NativeLibrary.Load(_launcherDllPath);
            }
            catch (Exception ex)
            {
                throw new CleanShimLaunchException(
                    $"Failed to load Clean-Launch Launcher DLL '{_launcherDllPath}': {ex.Message}",
                    ex);
            }

            if (!NativeLibrary.TryGetExport(launcherModule, "SyntheticLaunchSuspendedWithShimW", out IntPtr exportPtr))
            {
                throw new CleanShimLaunchException(
                    $"Clean-Launch Launcher DLL '{_launcherDllPath}' does not export 'SyntheticLaunchSuspendedWithShimW'.");
            }

            var launchProc = Marshal.GetDelegateForFunctionPointer<SyntheticLaunchSuspendedWithShimDelegate>(exportPtr);

            GCHandle envPin = GCHandle.Alloc(environmentBlock, GCHandleType.Pinned);
            try
            {
                uint win32Error = launchProc(
                    fullExePath,
                    commandLineText,
                    workingDir,
                    envPin.AddrOfPinnedObject(),
                    _shimDllPath,
                    out rawProcessHandle,
                    out rawThreadHandle,
                    out rawProcessId);

                if (win32Error != 0 ||
                    rawProcessHandle == IntPtr.Zero ||
                    rawThreadHandle == IntPtr.Zero ||
                    rawProcessId == 0)
                {
                    if (rawThreadHandle != IntPtr.Zero)
                    {
                        Win32ProcessLaunchUtilities.CloseHandle(rawThreadHandle);
                        rawThreadHandle = IntPtr.Zero;
                    }

                    if (rawProcessHandle != IntPtr.Zero)
                    {
                        Win32ProcessLaunchUtilities.TerminateProcess(rawProcessHandle, 1);
                        Win32ProcessLaunchUtilities.CloseHandle(rawProcessHandle);
                        rawProcessHandle = IntPtr.Zero;
                    }

                    string win32Msg = win32Error != 0
                        ? new Win32Exception(unchecked((int)win32Error)).Message
                        : "Detours injection returned invalid process/thread handles";
                    throw new CleanShimLaunchException(
                        $"Clean-Launch Shim injection failed for '{fullExePath}' using '{_shimDllPath}': " +
                        $"{win32Msg} (Win32 error {win32Error}).");
                }
            }
            finally
            {
                envPin.Free();
            }
        }
        finally
        {
            if (launcherModule != IntPtr.Zero)
            {
                NativeLibrary.Free(launcherModule);
            }
        }

        try
        {
            string processName = Path.GetFileNameWithoutExtension(fullExePath);
            string pidProfileDir = Path.Combine(
                profileRoot,
                unchecked((int)rawProcessId).ToString(CultureInfo.InvariantCulture));

            LaunchedWorkerProcess launched = StandardProcessLauncher.FinalizeSuspendedWorkerProcess(
                rawProcessHandle,
                rawThreadHandle,
                unchecked((int)rawProcessId),
                processName,
                workerId,
                request.OnProcessCreatedBeforeResume,
                resumeFailureFactory: (error, _) => new CleanShimLaunchException(
                    $"ResumeThread failed for shimmed worker PID {rawProcessId} (Win32 error {error}).",
                    new Win32Exception(error)),
                onDispose: () => TryDeleteDirectoryBestEffort(pidProfileDir, _diagnosticLogger));
            return Task.FromResult(launched);
        }
        catch (Exception ex) when (ex is not CleanShimLaunchException)
        {
            throw new CleanShimLaunchException(
                $"Failed to finalize shimmed worker PID {rawProcessId} before resume: {ex.Message}",
                ex);
        }
    }

    internal static string ResolveProfileRootDirectory(WorkerLaunchRequest request)
    {
        string? raw = null;
        if (request.AdditionalEnvironmentVariables is not null &&
            request.AdditionalEnvironmentVariables.TryGetValue("SYNTHETIC_CLEAN_SHIM_PROFILE_DIR", out string? overrideValue))
        {
            raw = overrideValue;
        }
        else
        {
            raw = Environment.GetEnvironmentVariable("SYNTHETIC_CLEAN_SHIM_PROFILE_DIR");
        }

        if (!string.IsNullOrWhiteSpace(raw))
        {
            return Path.GetFullPath(raw.Trim());
        }

        return Path.Combine(Path.GetTempPath(), "SyntheticCleanProfile");
    }

    private static void TryDeleteDirectoryBestEffort(string directoryPath, Action<string>? diagnosticLogger)
    {
        if (string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath))
        {
            return;
        }

        try
        {
            Directory.Delete(directoryPath, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best-effort cleanup of disposable per-PID profile folder.
            diagnosticLogger?.Invoke(
                $"[clean-shim] Warning: failed to remove disposable profile directory '{directoryPath}': {ex.Message}");
        }
    }

    internal static void VerifySignedNativeBinary(string filePath, string description)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            throw new CleanShimLaunchException($"{description} not found at '{filePath}'.");
        }

        string fullPath = Path.GetFullPath(filePath);
        int trustStatus = VerifyWinTrust(fullPath);
        if (trustStatus != 0)
        {
            throw new CleanShimLaunchException(
                $"{description} at '{fullPath}' failed Authenticode verification (WinVerifyTrust status 0x{trustStatus:X8}).");
        }

        try
        {
#pragma warning disable SYSLIB0057 // CreateFromSignedFile is the supported net8.0 API for Authenticode signer inspection.
            using X509Certificate baseCert = X509Certificate.CreateFromSignedFile(fullPath);
#pragma warning restore SYSLIB0057
            using var cert = new X509Certificate2(baseCert);
            string signerName = cert.GetNameInfo(X509NameType.SimpleName, forIssuer: false);
            if (!string.Equals(signerName, ExpectedSignerCommonName, StringComparison.OrdinalIgnoreCase))
            {
                throw new CleanShimLaunchException(
                    $"{description} at '{fullPath}' is signed by '{cert.Subject}', expected CN={ExpectedSignerCommonName}.");
            }
        }
        catch (CleanShimLaunchException)
        {
            throw;
        }
        catch (CryptographicException ex)
        {
            throw new CleanShimLaunchException(
                $"{description} at '{fullPath}' has an unreadable or missing Authenticode certificate: {ex.Message}",
                ex);
        }
    }

    private static int VerifyWinTrust(string fullPath)
    {
        IntPtr filePathPtr = Marshal.StringToCoTaskMemUni(fullPath);
        IntPtr fileInfoPtr = IntPtr.Zero;
        Guid actionGuid = WinTrustActionGenericVerifyV2;

        var winTrustData = new WinTrustData
        {
            cbStruct = (uint)Marshal.SizeOf<WinTrustData>(),
            pPolicyCallbackData = IntPtr.Zero,
            pSIPClientData = IntPtr.Zero,
            dwUIChoice = WtdUiNone,
            fdwRevocationChecks = WtdRevokeNone,
            dwUnionChoice = WtdChoiceFile,
            pFile = IntPtr.Zero,
            dwStateAction = WtdStateActionVerify,
            hWVTStateData = IntPtr.Zero,
            pwszURLReference = IntPtr.Zero,
            dwProvFlags = WtdProvFlagRevocationCheckNone,
            dwUIContext = 0,
            pSignatureSettings = IntPtr.Zero
        };

        try
        {
            var fileInfo = new WinTrustFileInfo
            {
                cbStruct = (uint)Marshal.SizeOf<WinTrustFileInfo>(),
                pcwszFilePath = filePathPtr,
                hFile = IntPtr.Zero,
                pgKnownSubject = IntPtr.Zero
            };

            fileInfoPtr = Marshal.AllocHGlobal(Marshal.SizeOf<WinTrustFileInfo>());
            Marshal.StructureToPtr(fileInfo, fileInfoPtr, fDeleteOld: false);
            winTrustData.pFile = fileInfoPtr;

            return NativeMethods.WinVerifyTrust(IntPtr.Zero, ref actionGuid, ref winTrustData);
        }
        finally
        {
            if (winTrustData.hWVTStateData != IntPtr.Zero)
            {
                winTrustData.dwStateAction = WtdStateActionClose;
                _ = NativeMethods.WinVerifyTrust(IntPtr.Zero, ref actionGuid, ref winTrustData);
            }

            if (fileInfoPtr != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(fileInfoPtr);
            }

            if (filePathPtr != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(filePathPtr);
            }
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode)]
    private delegate uint SyntheticLaunchSuspendedWithShimDelegate(
        [MarshalAs(UnmanagedType.LPWStr)] string applicationPath,
        [MarshalAs(UnmanagedType.LPWStr)] string? commandLine,
        [MarshalAs(UnmanagedType.LPWStr)] string? currentDirectory,
        IntPtr unicodeEnvironmentBlock,
        [MarshalAs(UnmanagedType.LPWStr)] string shimDllPath,
        out IntPtr outProcessHandle,
        out IntPtr outThreadHandle,
        out uint outProcessId);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WinTrustFileInfo
    {
        public uint cbStruct;
        public IntPtr pcwszFilePath;
        public IntPtr hFile;
        public IntPtr pgKnownSubject;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WinTrustData
    {
        public uint cbStruct;
        public IntPtr pPolicyCallbackData;
        public IntPtr pSIPClientData;
        public uint dwUIChoice;
        public uint fdwRevocationChecks;
        public uint dwUnionChoice;
        public IntPtr pFile;
        public uint dwStateAction;
        public IntPtr hWVTStateData;
        public IntPtr pwszURLReference;
        public uint dwProvFlags;
        public uint dwUIContext;
        public IntPtr pSignatureSettings;
    }

    private static class NativeMethods
    {
        [DllImport("wintrust.dll", ExactSpelling = true, SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern int WinVerifyTrust(
            IntPtr hwnd,
            ref Guid pgActionID,
            ref WinTrustData pWVTData);
    }
}
