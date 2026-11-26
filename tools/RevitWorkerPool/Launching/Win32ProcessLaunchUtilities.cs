using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using RevitWorkerPool.Containment;

namespace RevitWorkerPool.Launching;

/// <summary>
/// Shared Win32 process-launch P/Invokes, Job Object queries, and <c>CommandLineToArgvW</c>-compliant
/// argument quoting consolidated across worker and supervisor launchers.
/// </summary>
internal static class Win32ProcessLaunchUtilities
{
    public const uint CreateSuspended = 0x00000004;
    public const uint DetachedProcess = 0x00000008;
    public const uint CreateNewProcessGroup = 0x00000200;
    public const uint CreateUnicodeEnvironment = 0x00000400;
    public const uint CreateBreakawayFromJob = 0x01000000;
    public const uint CreateNoWindow = 0x08000000;
    public const uint StartfUseShowWindow = 0x00000001;
    public const short SwShowNormal = 1;
    public const short SwShowMinNoActive = 7;

    public const uint JobObjectLimitBreakawayOk = 0x00000800;
    public const uint JobObjectLimitSilentBreakawayOk = 0x00001000;
    public const int JobObjectExtendedLimitInformation = 9;
    public const int ErrorAccessDenied = 5;

    /// <summary>
    /// Quotes a single argument according to Win32 <c>CommandLineToArgvW</c> rules.
    /// </summary>
    public static string QuoteArg(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "\"\"";
        }

        if (!value.Contains(' ') && !value.Contains('"') && !value.Contains('\t'))
        {
            return value;
        }

        var sb = new StringBuilder(value.Length + 4);
        sb.Append('"');
        int backslashCount = 0;

        foreach (char c in value)
        {
            if (c == '\\')
            {
                backslashCount++;
            }
            else if (c == '"')
            {
                sb.Append('\\', backslashCount * 2 + 1);
                sb.Append('"');
                backslashCount = 0;
            }
            else
            {
                if (backslashCount > 0)
                {
                    sb.Append('\\', backslashCount);
                    backslashCount = 0;
                }

                sb.Append(c);
            }
        }

        if (backslashCount > 0)
        {
            sb.Append('\\', backslashCount * 2);
        }

        sb.Append('"');
        return sb.ToString();
    }

    /// <summary>
    /// Returns <see langword="true"/> when the current process can spawn child processes with
    /// <c>CREATE_BREAKAWAY_FROM_JOB</c> (either not inside a Job Object, or inside a Job Object
    /// permitting <c>JOB_OBJECT_LIMIT_BREAKAWAY_OK</c> / <c>JOB_OBJECT_LIMIT_SILENT_BREAKAWAY_OK</c>).
    /// </summary>
    [SupportedOSPlatform("windows")]
    public static bool CurrentProcessAllowsBreakaway()
    {
        if (!IsProcessInJob(GetCurrentProcess(), IntPtr.Zero, out bool inJob))
        {
            int error = Marshal.GetLastWin32Error();
            throw new Win32Exception(error, $"IsProcessInJob failed (Win32 error {error}).");
        }

        if (!inJob)
        {
            return true;
        }

        int size = Marshal.SizeOf<JobObjectContainer.JobObjectExtendedLimitInformationNative>();
        IntPtr infoPtr = Marshal.AllocHGlobal(size);
        try
        {
            if (!QueryInformationJobObject(
                    IntPtr.Zero,
                    JobObjectExtendedLimitInformation,
                    infoPtr,
                    (uint)size,
                    out _))
            {
                int error = Marshal.GetLastWin32Error();
                throw new Win32Exception(error, $"QueryInformationJobObject failed (Win32 error {error}).");
            }

            var info = Marshal.PtrToStructure<JobObjectContainer.JobObjectExtendedLimitInformationNative>(infoPtr);
            uint limitFlags = info.BasicLimitInformation.LimitFlags;
            return (limitFlags & (JobObjectLimitBreakawayOk | JobObjectLimitSilentBreakawayOk)) != 0;
        }
        finally
        {
            Marshal.FreeHGlobal(infoPtr);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct StartupInfoW
    {
        public int cb;
        public string? lpReserved;
        public string? lpDesktop;
        public string? lpTitle;
        public int dwX;
        public int dwY;
        public int dwXSize;
        public int dwYSize;
        public int dwXCountChars;
        public int dwYCountChars;
        public int dwFillAttribute;
        public uint dwFlags;
        public short wShowWindow;
        public short cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput;
        public IntPtr hStdOutput;
        public IntPtr hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ProcessInformation
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public uint dwProcessId;
        public uint dwThreadId;
    }

    [SupportedOSPlatform("windows")]
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool CreateProcessW(
        string? lpApplicationName,
        StringBuilder? lpCommandLine,
        IntPtr lpProcessAttributes,
        IntPtr lpThreadAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool bInheritHandles,
        uint dwCreationFlags,
        IntPtr lpEnvironment,
        string? lpCurrentDirectory,
        ref StartupInfoW lpStartupInfo,
        out ProcessInformation lpProcessInformation);

    [SupportedOSPlatform("windows")]
    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern uint ResumeThread(IntPtr hThread);

    [SupportedOSPlatform("windows")]
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool TerminateProcess(IntPtr hProcess, uint uExitCode);

    [SupportedOSPlatform("windows")]
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool CloseHandle(IntPtr hObject);

    [SupportedOSPlatform("windows")]
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsProcessInJob(
        IntPtr hProcess,
        IntPtr hJob,
        [MarshalAs(UnmanagedType.Bool)] out bool pfResult);

    [SupportedOSPlatform("windows")]
    [DllImport("kernel32.dll")]
    public static extern IntPtr GetCurrentProcess();

    [SupportedOSPlatform("windows")]
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool QueryInformationJobObject(
        IntPtr hJob,
        int jobObjectInformationClass,
        IntPtr lpJobObjectInformation,
        uint cbJobObjectInformationLength,
        out uint lpReturnLength);
}
