using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

namespace RevitWorkerPool.Containment;

/// <summary>
/// Wraps a Win32 Job Object configured with <c>JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE</c>
/// so assigned worker process trees terminate deterministically on disposal or supervisor crash.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class JobObjectContainer : IDisposable
{
    private const uint JobObjectLimitKillOnJobClose = 0x00002000;
    private const int JobObjectBasicAccountingInformation = 1;
    private const int JobObjectExtendedLimitInformation = 9;
    private const uint ProcessSetQuota = 0x0100;
    private const uint ProcessTerminate = 0x0001;
    private const uint Synchronize = 0x00100000;

    private readonly SafeJobHandle _jobHandle;
    private bool _disposed;

    /// <summary>
    /// Creates a new anonymous Win32 Job Object with <c>JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE</c>.
    /// </summary>
    public JobObjectContainer(string? jobName = null)
    {
        _jobHandle = NativeMethods.CreateJobObjectW(IntPtr.Zero, jobName);
        if (_jobHandle.IsInvalid)
        {
            int error = Marshal.GetLastWin32Error();
            _jobHandle.Dispose();
            throw new Win32Exception(error, $"CreateJobObjectW failed (Win32 error {error}).");
        }

        try
        {
            var extendedInfo = new JobObjectExtendedLimitInformationNative
            {
                BasicLimitInformation = new JobObjectBasicLimitInformationNative
                {
                    LimitFlags = JobObjectLimitKillOnJobClose
                }
            };

            int length = Marshal.SizeOf<JobObjectExtendedLimitInformationNative>();
            IntPtr infoPtr = Marshal.AllocHGlobal(length);
            try
            {
                Marshal.StructureToPtr(extendedInfo, infoPtr, fDeleteOld: false);
                if (!NativeMethods.SetInformationJobObject(
                        _jobHandle,
                        JobObjectExtendedLimitInformation,
                        infoPtr,
                        (uint)length))
                {
                    int error = Marshal.GetLastWin32Error();
                    throw new Win32Exception(error, $"SetInformationJobObject failed (Win32 error {error}).");
                }
            }
            finally
            {
                Marshal.FreeHGlobal(infoPtr);
            }
        }
        catch
        {
            _jobHandle.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Associates an open native process handle with this Job Object.
    /// </summary>
    public void AssignProcessHandle(IntPtr processHandle)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (processHandle == IntPtr.Zero)
        {
            throw new ArgumentException("Process handle must not be zero.", nameof(processHandle));
        }

        if (!NativeMethods.AssignProcessToJobObject(_jobHandle, processHandle))
        {
            int error = Marshal.GetLastWin32Error();
            throw new Win32Exception(error, $"AssignProcessToJobObject failed (Win32 error {error}).");
        }
    }

    /// <summary>
    /// Opens the specified process by PID and associates it with this Job Object.
    /// </summary>
    public void AssignProcess(int processId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        using SafeProcessHandle processHandle = NativeMethods.OpenProcess(
            ProcessSetQuota | ProcessTerminate | Synchronize,
            bInheritHandle: false,
            (uint)processId);
        if (processHandle.IsInvalid)
        {
            int error = Marshal.GetLastWin32Error();
            throw new Win32Exception(error, $"OpenProcess({processId}) failed (Win32 error {error}).");
        }

        AssignProcessHandle(processHandle.DangerousGetHandle());
    }

    /// <summary>
    /// Associates an existing <see cref="Process"/> instance with this Job Object.
    /// </summary>
    public void AssignProcess(Process process)
    {
        ArgumentNullException.ThrowIfNull(process);
        AssignProcessHandle(process.Handle);
    }

    /// <summary>
    /// Terminates all processes currently assigned to this Job Object.
    /// </summary>
    public void Terminate(uint exitCode = 1)
    {
        if (_disposed || _jobHandle.IsInvalid || _jobHandle.IsClosed)
        {
            return;
        }

        if (!NativeMethods.TerminateJobObject(_jobHandle, exitCode))
        {
            int error = Marshal.GetLastWin32Error();
            // ERROR_ACCESS_DENIED can occur if the job was already terminated; ignore on cleanup.
            if (error != 5)
            {
                throw new Win32Exception(error, $"TerminateJobObject failed (Win32 error {error}).");
            }
        }
    }

    /// <summary>
    /// Queries the number of currently active processes assigned to this Job Object.
    /// </summary>
    public uint GetActiveProcessCount()
    {
        if (_disposed || _jobHandle.IsInvalid || _jobHandle.IsClosed)
        {
            return 0;
        }

        int length = Marshal.SizeOf<JobObjectBasicAccountingInformationNative>();
        IntPtr infoPtr = Marshal.AllocHGlobal(length);
        try
        {
            if (!NativeMethods.QueryInformationJobObject(
                    _jobHandle,
                    JobObjectBasicAccountingInformation,
                    infoPtr,
                    (uint)length,
                    out _))
            {
                return 0;
            }

            var info = Marshal.PtrToStructure<JobObjectBasicAccountingInformationNative>(infoPtr);
            return info.ActiveProcesses;
        }
        finally
        {
            Marshal.FreeHGlobal(infoPtr);
        }
    }

    /// <summary>
    /// Waits up to <paramref name="timeout"/> for all processes in this Job Object to exit.
    /// </summary>
    public bool WaitForAllProcessesExited(TimeSpan timeout)
    {
        if (_disposed || _jobHandle.IsInvalid || _jobHandle.IsClosed)
        {
            return true;
        }

        var sw = Stopwatch.StartNew();
        while (GetActiveProcessCount() > 0)
        {
            if (sw.Elapsed >= timeout)
            {
                return false;
            }

            Thread.Sleep(10);
        }

        return true;
    }

    /// <summary>
    /// Asynchronously waits up to <paramref name="timeout"/> for all processes in this Job Object to exit.
    /// </summary>
    public async Task<bool> WaitForAllProcessesExitedAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        if (_disposed || _jobHandle.IsInvalid || _jobHandle.IsClosed)
        {
            return true;
        }

        var sw = Stopwatch.StartNew();
        while (GetActiveProcessCount() > 0)
        {
            if (sw.Elapsed >= timeout)
            {
                return false;
            }

            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(10, cancellationToken).ConfigureAwait(false);
        }

        return true;
    }

    /// <summary>
    /// Closes the Job Object handle, triggering <c>JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE</c>
    /// on any remaining assigned processes.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _jobHandle.Dispose();
    }

    internal static int ExtendedLimitStructSize => Marshal.SizeOf<JobObjectExtendedLimitInformationNative>();

    private sealed class SafeJobHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public SafeJobHandle()
            : base(ownsHandle: true)
        {
        }

        protected override bool ReleaseHandle()
        {
            return NativeMethods.CloseHandle(handle);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct JobObjectBasicAccountingInformationNative
    {
        public long TotalUserTime;
        public long TotalKernelTime;
        public long ThisPeriodTotalUserTime;
        public long ThisPeriodTotalKernelTime;
        public uint TotalPageFaultCount;
        public uint TotalProcesses;
        public uint ActiveProcesses;
        public uint TotalTerminatedProcesses;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct JobObjectBasicLimitInformationNative
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct IoCountersNative
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct JobObjectExtendedLimitInformationNative
    {
        public JobObjectBasicLimitInformationNative BasicLimitInformation;
        public IoCountersNative IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }

    private static class NativeMethods
    {
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern SafeJobHandle CreateJobObjectW(IntPtr lpJobAttributes, string? lpName);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetInformationJobObject(
            SafeJobHandle hJob,
            int jobObjectInfoClass,
            IntPtr lpJobObjectInfo,
            uint cbJobObjectInfoLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool QueryInformationJobObject(
            SafeJobHandle hJob,
            int jobObjectInfoClass,
            IntPtr lpJobObjectInfo,
            uint cbJobObjectInfoLength,
            out uint lpReturnLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool AssignProcessToJobObject(SafeJobHandle hJob, IntPtr hProcess);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool TerminateJobObject(SafeJobHandle hJob, uint uExitCode);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern SafeProcessHandle OpenProcess(
            uint dwDesiredAccess,
            [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle,
            uint dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CloseHandle(IntPtr hObject);
    }
}
