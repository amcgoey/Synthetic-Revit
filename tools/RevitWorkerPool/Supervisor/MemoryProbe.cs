using System;
using System.Runtime.InteropServices;

namespace RevitWorkerPool.Supervisor;

/// <summary>
/// Queries host physical memory availability for demand-gated lookahead admission control.
/// </summary>
public interface IMemoryProbe
{
    /// <summary>
    /// Returns the currently available physical RAM on the host in bytes.
    /// </summary>
    ulong GetAvailablePhysicalBytes();
}

/// <summary>
/// Production <see cref="IMemoryProbe"/> backed by Win32 <c>kernel32!GlobalMemoryStatusEx</c>
/// (<c>MEMORYSTATUSEX.ullAvailPhys</c>).
/// </summary>
public sealed class Win32MemoryProbe : IMemoryProbe
{
    public ulong GetAvailablePhysicalBytes()
    {
        if (!OperatingSystem.IsWindows())
        {
            return 0UL;
        }

        var status = new MEMORYSTATUSEX
        {
            dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>()
        };

        return GlobalMemoryStatusEx(ref status)
            ? status.ullAvailPhys
            : 0UL;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);
}
