using System;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Threading;

namespace GuiFocusProbe;

[SupportedOSPlatform("windows")]
internal static class Program
{
    private const uint WsOverlappedWindow = 0x00CF0000;
    private const uint WsMaximize = 0x01000000;
    private const uint WsPopup = 0x80000000;
    private const uint WsCaption = 0x00C00000;
    private const uint WsSysMenu = 0x00080000;
    private const uint WsChild = 0x40000000;
    private const uint WsVisible = 0x10000000;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const int SwShowNa = 8;
    private const uint WmDestroy = 0x0002;
    private const uint WmClose = 0x0010;
    private const uint WmWindowPosChanging = 0x0046;
    private const uint WmWindowPosChanged = 0x0047;
    private const uint WmTimer = 0x0113;
    private const ulong RepositionTimerId = 2;
    private const uint StdInputHandle = unchecked((uint)-10);
    private const uint FileTypeUnknown = 0x0000;
    private static readonly IntPtr InvalidHandleValue = new(-1);

    private static NativeMethods.WndProc? _wndProcDelegate;
    private static string? _statusFilePath;
    private static string? _lastPrintedState;
    private static IntPtr _mainHwnd;
    private static bool _clampOnScreen;

    [STAThread]
    private static int Main(string[] args)
    {
        string mode = "window";
        string title = "GuiFocusProbe";
        string body = "Probe window";
        string? exitEventName = Environment.GetEnvironmentVariable("GUI_FOCUS_PROBE_EXIT_EVENT");
        _statusFilePath = Environment.GetEnvironmentVariable("GUI_FOCUS_PROBE_STATUS_FILE");
        int timeoutMs = 15_000;
        int repositionAfterMs = 0;
        _clampOnScreen = false;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--mode" when i + 1 < args.Length:
                    mode = args[++i];
                    break;
                case "--title" when i + 1 < args.Length:
                    title = args[++i];
                    break;
                case "--body" when i + 1 < args.Length:
                    body = args[++i];
                    break;
                case "--status-file" when i + 1 < args.Length:
                    _statusFilePath = args[++i];
                    break;
                case "--exit-event" when i + 1 < args.Length:
                    exitEventName = args[++i];
                    break;
                case "--timeout-ms" when i + 1 < args.Length &&
                                          int.TryParse(args[++i], NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedMs):
                    timeoutMs = Math.Max(100, parsedMs);
                    break;
                case "--reposition-after-ms" when i + 1 < args.Length &&
                                                 int.TryParse(args[++i], NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedRepositionMs):
                    repositionAfterMs = Math.Max(0, parsedRepositionMs);
                    break;
                case "--clamp-pos":
                    _clampOnScreen = true;
                    break;
            }
        }

        IntPtr hInstance = NativeMethods.GetModuleHandleW(null);
        _wndProcDelegate = WindowProc;

        IntPtr childHwnd = IntPtr.Zero;
        if (string.Equals(mode, "modal", StringComparison.OrdinalIgnoreCase))
        {
            _mainHwnd = NativeMethods.CreateWindowExW(
                0,
                "#32770",
                title,
                WsPopup | WsCaption | WsSysMenu,
                120,
                120,
                360,
                180,
                IntPtr.Zero,
                IntPtr.Zero,
                hInstance,
                IntPtr.Zero);

            if (_mainHwnd != IntPtr.Zero)
            {
                childHwnd = NativeMethods.CreateWindowExW(
                    0,
                    "STATIC",
                    body,
                    WsChild | WsVisible,
                    12,
                    12,
                    320,
                    80,
                    _mainHwnd,
                    IntPtr.Zero,
                    hInstance,
                    IntPtr.Zero);
            }
        }
        else
        {
            string className = $"SyntheticGuiFocusProbe_{Environment.ProcessId}";
            var wc = new NativeMethods.WndClassExW
            {
                cbSize = (uint)Marshal.SizeOf<NativeMethods.WndClassExW>(),
                lpfnWndProc = _wndProcDelegate,
                hInstance = hInstance,
                lpszClassName = className
            };

            ushort atom = NativeMethods.RegisterClassExW(ref wc);
            if (atom == 0)
            {
                return 2;
            }

            uint style = string.Equals(mode, "maximized", StringComparison.OrdinalIgnoreCase)
                ? WsOverlappedWindow | WsMaximize
                : WsOverlappedWindow;

            _mainHwnd = NativeMethods.CreateWindowExW(
                0,
                className,
                title,
                style,
                100,
                100,
                320,
                240,
                IntPtr.Zero,
                IntPtr.Zero,
                hInstance,
                IntPtr.Zero);
        }

        if (_mainHwnd == IntPtr.Zero)
        {
            return 3;
        }

        NativeMethods.ShowWindow(_mainHwnd, SwShowNa);
        NativeMethods.UpdateWindow(_mainHwnd);
        PublishSnapshot(_mainHwnd, forceOutput: true);

        NativeMethods.SetTimer(_mainHwnd, new UIntPtr(1), 25, IntPtr.Zero);
        if (repositionAfterMs > 0)
        {
            NativeMethods.SetTimer(_mainHwnd, new UIntPtr(RepositionTimerId), unchecked((uint)repositionAfterMs), IntPtr.Zero);
        }

        using var stopCts = new CancellationTokenSource(timeoutMs);
        var exitThread = StartExitWatcher(_mainHwnd, exitEventName, stopCts);

        while (NativeMethods.GetMessageW(out NativeMethods.Msg msg, IntPtr.Zero, 0, 0) > 0)
        {
            NativeMethods.TranslateMessage(ref msg);
            NativeMethods.DispatchMessageW(ref msg);
        }

        stopCts.Cancel();
        if (childHwnd != IntPtr.Zero)
        {
            NativeMethods.DestroyWindow(childHwnd);
        }

        GC.KeepAlive(_wndProcDelegate);
        GC.KeepAlive(exitThread);
        return 0;
    }

    private static IntPtr WindowProc(IntPtr hwnd, uint msg, UIntPtr wParam, IntPtr lParam)
    {
        switch (msg)
        {
            case WmWindowPosChanging:
                if (_clampOnScreen && lParam != IntPtr.Zero)
                {
                    NativeMethods.WindowPos wp = Marshal.PtrToStructure<NativeMethods.WindowPos>(lParam);
                    if (wp.x < 0)
                    {
                        wp.x = 0;
                    }

                    if (wp.y < 0)
                    {
                        wp.y = 0;
                    }

                    Marshal.StructureToPtr(wp, lParam, fDeleteOld: false);
                }
                break;

            case WmWindowPosChanged:
                PublishSnapshot(hwnd, forceOutput: false);
                break;

            case WmTimer:
                if (wParam.ToUInt64() == RepositionTimerId)
                {
                    NativeMethods.KillTimer(hwnd, new UIntPtr(RepositionTimerId));
                    NativeMethods.SetWindowPos(hwnd, IntPtr.Zero, 100, 100, 0, 0, SwpNoSize | SwpNoZOrder | SwpNoActivate);
                }

                PublishSnapshot(hwnd, forceOutput: false);
                break;

            case WmDestroy:
                NativeMethods.KillTimer(hwnd, new UIntPtr(RepositionTimerId));
                NativeMethods.KillTimer(hwnd, new UIntPtr(1));
                NativeMethods.PostQuitMessage(0);
                return IntPtr.Zero;
        }

        return NativeMethods.DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    private static void PublishSnapshot(IntPtr hwnd, bool forceOutput)
    {
        if (hwnd == IntPtr.Zero || !NativeMethods.GetWindowRect(hwnd, out NativeMethods.Rect rect))
        {
            return;
        }

        bool visible = NativeMethods.IsWindowVisible(hwnd);
        bool iconic = NativeMethods.IsIconic(hwnd);
        bool zoomed = NativeMethods.IsZoomed(hwnd);
        int width = rect.Right - rect.Left;
        int height = rect.Bottom - rect.Top;
        string state = string.Create(
            CultureInfo.InvariantCulture,
            $"HWND=0x{hwnd.ToInt64():X} IsWindowVisible={visible} IsIconic={iconic} IsZoomed={zoomed} X={rect.Left} Y={rect.Top} Width={width} Height={height}");

        if (!forceOutput && string.Equals(state, _lastPrintedState, StringComparison.Ordinal))
        {
            return;
        }

        _lastPrintedState = state;

        try
        {
            Console.Out.WriteLine(state);
            Console.Out.Flush();
        }
        catch (IOException)
        {
            // Stdout may be disconnected when spawned without inherited handles.
        }

        if (!string.IsNullOrWhiteSpace(_statusFilePath))
        {
            try
            {
                string? dir = Path.GetDirectoryName(_statusFilePath);
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                using var fs = new FileStream(
                    _statusFilePath,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.ReadWrite);
                using var writer = new StreamWriter(fs, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                writer.WriteLine(state);
            }
            catch (IOException)
            {
                // Best-effort status snapshot update.
            }
        }
    }

    private static Thread StartExitWatcher(
        IntPtr hwnd,
        string? exitEventName,
        CancellationTokenSource stopCts)
    {
        var thread = new Thread(() =>
        {
            if (!string.IsNullOrWhiteSpace(exitEventName))
            {
                try
                {
                    using var exitEvent = new EventWaitHandle(
                        initialState: false,
                        EventResetMode.ManualReset,
                        exitEventName);
                    WaitHandle.WaitAny(new WaitHandle[] { exitEvent, stopCts.Token.WaitHandle });
                }
                catch (Exception)
                {
                    // Fall through and close window cleanly.
                }

                NativeMethods.PostMessageW(hwnd, WmClose, UIntPtr.Zero, IntPtr.Zero);
                return;
            }

            if (HasUsableStdinHandle())
            {
                try
                {
                    Console.In.ReadLine();
                }
                catch (IOException)
                {
                    // Stdin closed.
                }

                NativeMethods.PostMessageW(hwnd, WmClose, UIntPtr.Zero, IntPtr.Zero);
                return;
            }

            stopCts.Token.WaitHandle.WaitOne();
            NativeMethods.PostMessageW(hwnd, WmClose, UIntPtr.Zero, IntPtr.Zero);
        })
        {
            IsBackground = true,
            Name = "GuiFocusProbe.ExitWatcher"
        };
        thread.Start();
        return thread;
    }

    private static bool HasUsableStdinHandle()
    {
        IntPtr hStdIn = NativeMethods.GetStdHandle(StdInputHandle);
        if (hStdIn == IntPtr.Zero || hStdIn == InvalidHandleValue)
        {
            return false;
        }

        return NativeMethods.GetFileType(hStdIn) != FileTypeUnknown;
    }

    private static class NativeMethods
    {
        public delegate IntPtr WndProc(IntPtr hWnd, uint msg, UIntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct WndClassExW
        {
            public uint cbSize;
            public uint style;
            public WndProc lpfnWndProc;
            public int cbClsExtra;
            public int cbWndExtra;
            public IntPtr hInstance;
            public IntPtr hIcon;
            public IntPtr hCursor;
            public IntPtr hbrBackground;
            public string? lpszMenuName;
            public string lpszClassName;
            public IntPtr hIconSm;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct Point
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct Msg
        {
            public IntPtr Hwnd;
            public uint Message;
            public UIntPtr WParam;
            public IntPtr LParam;
            public uint Time;
            public Point Pt;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct Rect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct WindowPos
        {
            public IntPtr hwnd;
            public IntPtr hwndInsertAfter;
            public int x;
            public int y;
            public int cx;
            public int cy;
            public uint flags;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr GetModuleHandleW(string? lpModuleName);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr GetStdHandle(uint nStdHandle);

        [DllImport("kernel32.dll")]
        public static extern uint GetFileType(IntPtr hFile);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern ushort RegisterClassExW(ref WndClassExW lpwcx);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern IntPtr CreateWindowExW(
            uint dwExStyle,
            string lpClassName,
            string lpWindowName,
            uint dwStyle,
            int x,
            int y,
            int nWidth,
            int nHeight,
            IntPtr hWndParent,
            IntPtr hMenu,
            IntPtr hInstance,
            IntPtr lpParam);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool UpdateWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DestroyWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern IntPtr DefWindowProcW(IntPtr hWnd, uint msg, UIntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern void PostQuitMessage(int nExitCode);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool PostMessageW(IntPtr hWnd, uint msg, UIntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern int GetMessageW(out Msg lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool TranslateMessage(ref Msg lpMsg);

        [DllImport("user32.dll")]
        public static extern IntPtr DispatchMessageW(ref Msg lpMsg);

        [DllImport("user32.dll")]
        public static extern UIntPtr SetTimer(IntPtr hWnd, UIntPtr nIDEvent, uint uElapse, IntPtr lpTimerFunc);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool KillTimer(IntPtr hWnd, UIntPtr uIDEvent);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetWindowRect(IntPtr hWnd, out Rect lpRect);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsZoomed(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetWindowPos(
            IntPtr hWnd,
            IntPtr hWndInsertAfter,
            int x,
            int y,
            int cx,
            int cy,
            uint uFlags);
    }
}
