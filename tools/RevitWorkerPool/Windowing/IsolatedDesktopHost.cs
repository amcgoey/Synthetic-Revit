using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace RevitWorkerPool.Windowing;

/// <summary>
/// Owns a secondary Win32 desktop (<c>HDESK</c>) inside the current interactive WindowStation
/// (<c>WinSta0</c>), isolating all worker UI, splash screens, taskbar entries, Alt-Tab icons,
/// and focus changes from <c>WinSta0\Default</c> without calling <c>SetThreadDesktop</c>.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class IsolatedDesktopHost : IDisposable
{
    private const uint DesktopReadObjects = 0x0001;
    private const uint DesktopCreateWindow = 0x0002;
    private const uint DesktopCreateMenu = 0x0004;
    private const uint DesktopHookControl = 0x0008;
    private const uint DesktopJournalRecord = 0x0010;
    private const uint DesktopJournalPlayback = 0x0020;
    private const uint DesktopEnumerate = 0x0040;
    private const uint DesktopWriteObjects = 0x0080;
    private const uint DesktopSwitchDesktop = 0x0100;
    private const uint StandardRightsRequired = 0x000F0000;
    private const uint DesktopAllAccess =
        StandardRightsRequired |
        DesktopReadObjects |
        DesktopCreateWindow |
        DesktopCreateMenu |
        DesktopHookControl |
        DesktopJournalRecord |
        DesktopJournalPlayback |
        DesktopEnumerate |
        DesktopWriteObjects |
        DesktopSwitchDesktop;

    private const int UoiName = 2;

    private readonly SafeDesktopHandle _desktopHandle;
    private int _disposed;

    public IsolatedDesktopHost(string? desktopName = null)
    {
        WindowStationName = ResolveCurrentWindowStationName();
        ShortName = NormalizeDesktopShortName(desktopName);
        FullDesktopName = $@"{WindowStationName}\{ShortName}";

        _desktopHandle = NativeMethods.CreateDesktopW(
            ShortName,
            IntPtr.Zero,
            IntPtr.Zero,
            0,
            DesktopAllAccess,
            IntPtr.Zero);

        if (_desktopHandle.IsInvalid)
        {
            int error = Marshal.GetLastWin32Error();
            _desktopHandle.Dispose();
            throw new Win32Exception(error, $"CreateDesktopW('{FullDesktopName}') failed (Win32 error {error}).");
        }
    }

    public string WindowStationName { get; }

    public string ShortName { get; }

    public string FullDesktopName { get; }

    public bool IsDisposed => System.Threading.Volatile.Read(ref _disposed) != 0;

    public IntPtr DangerousGetHandle()
    {
        return IsDisposed || _desktopHandle.IsInvalid || _desktopHandle.IsClosed
            ? IntPtr.Zero
            : _desktopHandle.DangerousGetHandle();
    }

    public static string ResolveCurrentWindowStationName()
    {
        IntPtr hWinSta = NativeMethods.GetProcessWindowStation();
        if (hWinSta == IntPtr.Zero)
        {
            return "WinSta0";
        }

        byte[] buffer = new byte[512];
        if (!NativeMethods.GetUserObjectInformationW(hWinSta, UoiName, buffer, buffer.Length, out int neededBytes) ||
            neededBytes <= 2)
        {
            return "WinSta0";
        }

        int charBytes = Math.Min(neededBytes, buffer.Length);
        string decoded = Encoding.Unicode.GetString(buffer, 0, charBytes).TrimEnd('\0');
        return string.IsNullOrWhiteSpace(decoded) ? "WinSta0" : decoded;
    }

    public static bool DesktopExists(string desktopName)
    {
        if (string.IsNullOrWhiteSpace(desktopName))
        {
            return false;
        }

        string shortDesktopName = NormalizeDesktopShortName(desktopName);
        IntPtr handle = NativeMethods.OpenDesktopW(
            shortDesktopName,
            0,
            fInherit: false,
            DesktopReadObjects | DesktopEnumerate);
        if (handle == IntPtr.Zero)
        {
            return false;
        }

        NativeMethods.CloseDesktop(handle);
        return true;
    }

    public static IReadOnlyList<IntPtr> EnumerateTopLevelWindowsForProcess(IntPtr desktopHandle, int processId)
    {
        if (desktopHandle == IntPtr.Zero || processId <= 0)
        {
            return Array.Empty<IntPtr>();
        }

        var hwnds = new List<IntPtr>();
        NativeMethods.EnumWindowsProc proc = (hwnd, _) =>
        {
            NativeMethods.GetWindowThreadProcessId(hwnd, out uint rawPid);
            if (unchecked((int)rawPid) == processId)
            {
                hwnds.Add(hwnd);
            }

            return true;
        };

        NativeMethods.EnumDesktopWindows(desktopHandle, proc, IntPtr.Zero);
        GC.KeepAlive(proc);
        return hwnds;
    }

    public void Dispose()
    {
        if (System.Threading.Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _desktopHandle.Dispose();
    }

    private static string NormalizeDesktopShortName(string? desktopName)
    {
        string raw = string.IsNullOrWhiteSpace(desktopName)
            ? $"SyntheticWorker_{Guid.NewGuid():N}"
            : desktopName.Trim();

        int slashIdx = raw.LastIndexOf('\\');
        if (slashIdx >= 0 && slashIdx + 1 < raw.Length)
        {
            raw = raw[(slashIdx + 1)..];
        }

        return raw.Length > 120 ? raw[..120] : raw;
    }

    private sealed class SafeDesktopHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public SafeDesktopHandle()
            : base(ownsHandle: true)
        {
        }

        protected override bool ReleaseHandle()
        {
            return NativeMethods.CloseDesktop(handle);
        }
    }

    private static class NativeMethods
    {
        [return: MarshalAs(UnmanagedType.Bool)]
        public delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr GetProcessWindowStation();

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetUserObjectInformationW(
            IntPtr hObj,
            int nIndex,
            [Out] byte[] pvInfo,
            int nLength,
            out int lpnLengthNeeded);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern SafeDesktopHandle CreateDesktopW(
            string lpszDesktop,
            IntPtr lpszDevice,
            IntPtr pDevmode,
            uint dwFlags,
            uint dwDesiredAccess,
            IntPtr lpsa);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern IntPtr OpenDesktopW(
            string lpszDesktop,
            uint dwFlags,
            [MarshalAs(UnmanagedType.Bool)] bool fInherit,
            uint dwDesiredAccess);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CloseDesktop(IntPtr hDesktop);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool EnumDesktopWindows(
            IntPtr hDesktop,
            EnumWindowsProc lpfn,
            IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
    }
}
