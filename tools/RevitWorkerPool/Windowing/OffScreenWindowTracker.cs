using System;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;

namespace RevitWorkerPool.Windowing;

/// <summary>
/// Tracks worker PIDs and repositions their top-level Win32 windows off-screen
/// (<c>-32000, -32000</c>) with <c>SWP_NOACTIVATE</c> via an out-of-context
/// <c>SetWinEventHook</c> STA message pump, preserving <c>IsWindowVisible == true</c>
/// while never altering <c>GetForegroundWindow()</c>.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class OffScreenWindowTracker : IDisposable
{
    public const int DefaultOffScreenX = -32000;
    public const int DefaultOffScreenY = -32000;

    private const uint EventObjectCreate = 0x8000;
    private const uint EventObjectShow = 0x8002;
    private const uint WinEventOutOfContext = 0x0000;
    private const int ObjIdWindow = 0;
    private const int ChildIdSelf = 0;
    private const uint GaRoot = 2;
    private const int GwlStyle = -16;
    private const long WsChild = 0x40000000L;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint WmQuit = 0x0012;
    private const uint PmNoRemove = 0x0000;

    private readonly int _offScreenX;
    private readonly int _offScreenY;
    private readonly Action<string>? _diagnosticLogger;
    private readonly ConcurrentDictionary<int, byte> _trackedPids = new();
    private readonly ManualResetEventSlim _readyEvent = new(false);
    private readonly NativeMethods.WinEventDelegate _winEventDelegate;
    private readonly Thread _messageThread;

    private uint _nativeThreadId;
    private ExceptionDispatchInfo? _startupException;
    private long _repositionedCount;
    private int _disposed;

    public OffScreenWindowTracker(
        int offScreenX = DefaultOffScreenX,
        int offScreenY = DefaultOffScreenY,
        Action<string>? diagnosticLogger = null)
    {
        _offScreenX = offScreenX;
        _offScreenY = offScreenY;
        _diagnosticLogger = diagnosticLogger;
        _winEventDelegate = OnWinEvent;

        _messageThread = new Thread(RunMessageLoop)
        {
            IsBackground = true,
            Name = "RevitWorkerPool.OffScreenWindowTracker"
        };
        _messageThread.SetApartmentState(ApartmentState.STA);
        _messageThread.Start();

        _readyEvent.Wait();
        _startupException?.Throw();
    }

    public int OffScreenX => _offScreenX;

    public int OffScreenY => _offScreenY;

    public long RepositionedCount => Interlocked.Read(ref _repositionedCount);

    public int TrackedProcessCount => _trackedPids.Count;

    public bool IsTracked(int processId) => _trackedPids.ContainsKey(processId);

    /// <summary>
    /// Registers <paramref name="processId"/> for off-screen window tracking and sweeps
    /// any existing top-level windows already created by that process.
    /// </summary>
    public void TrackProcess(int processId)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (processId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(processId), "Process ID must be positive.");
        }

        _trackedPids.TryAdd(processId, 0);
        SweepExistingWindowsForProcess(processId);
    }

    /// <summary>
    /// Stops tracking <paramref name="processId"/>.
    /// </summary>
    public bool UntrackProcess(int processId)
    {
        if (processId <= 0)
        {
            return false;
        }

        return _trackedPids.TryRemove(processId, out _);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _trackedPids.Clear();

        uint threadId = Volatile.Read(ref _nativeThreadId);
        if (threadId != 0)
        {
            NativeMethods.PostThreadMessageW(threadId, WmQuit, UIntPtr.Zero, IntPtr.Zero);
        }

        if (_messageThread.IsAlive && Thread.CurrentThread != _messageThread)
        {
            _messageThread.Join(TimeSpan.FromSeconds(5));
        }

        _readyEvent.Dispose();
    }

    private void RunMessageLoop()
    {
        IntPtr createHook = IntPtr.Zero;
        IntPtr showHook = IntPtr.Zero;

        try
        {
            // Force message queue creation on this STA thread before hooking WinEvents.
            NativeMethods.PeekMessageW(out _, IntPtr.Zero, 0, 0, PmNoRemove);
            Volatile.Write(ref _nativeThreadId, NativeMethods.GetCurrentThreadId());

            createHook = NativeMethods.SetWinEventHook(
                EventObjectCreate,
                EventObjectCreate,
                IntPtr.Zero,
                _winEventDelegate,
                0,
                0,
                WinEventOutOfContext);
            if (createHook == IntPtr.Zero)
            {
                int error = Marshal.GetLastWin32Error();
                throw new Win32Exception(error, $"SetWinEventHook(EVENT_OBJECT_CREATE) failed (Win32 error {error}).");
            }

            showHook = NativeMethods.SetWinEventHook(
                EventObjectShow,
                EventObjectShow,
                IntPtr.Zero,
                _winEventDelegate,
                0,
                0,
                WinEventOutOfContext);
            if (showHook == IntPtr.Zero)
            {
                int error = Marshal.GetLastWin32Error();
                throw new Win32Exception(error, $"SetWinEventHook(EVENT_OBJECT_SHOW) failed (Win32 error {error}).");
            }

            _readyEvent.Set();

            while (Volatile.Read(ref _disposed) == 0)
            {
                int msgResult = NativeMethods.GetMessageW(out NativeMethods.Msg msg, IntPtr.Zero, 0, 0);
                if (msgResult <= 0)
                {
                    break;
                }

                NativeMethods.TranslateMessage(ref msg);
                NativeMethods.DispatchMessageW(ref msg);
            }
        }
        catch (Exception ex) when (!_readyEvent.IsSet)
        {
            _startupException = ExceptionDispatchInfo.Capture(ex);
            _readyEvent.Set();
        }
        finally
        {
            if (showHook != IntPtr.Zero)
            {
                NativeMethods.UnhookWinEvent(showHook);
            }

            if (createHook != IntPtr.Zero)
            {
                NativeMethods.UnhookWinEvent(createHook);
            }

            GC.KeepAlive(_winEventDelegate);
        }
    }

    private void OnWinEvent(
        IntPtr hWinEventHook,
        uint eventType,
        IntPtr hwnd,
        int idObject,
        int idChild,
        uint idEventThread,
        uint dwmsEventTime)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        if (hwnd == IntPtr.Zero || idObject != ObjIdWindow || idChild != ChildIdSelf)
        {
            return;
        }

        if (eventType != EventObjectShow && eventType != EventObjectCreate)
        {
            return;
        }

        TryRepositionIfTracked(hwnd);
    }

    private void SweepExistingWindowsForProcess(int processId)
    {
        NativeMethods.EnumWindowsProc enumProc = (hwnd, _) =>
        {
            if (Volatile.Read(ref _disposed) != 0 || !_trackedPids.ContainsKey(processId))
            {
                return false;
            }

            NativeMethods.GetWindowThreadProcessId(hwnd, out uint rawPid);
            if (unchecked((int)rawPid) == processId)
            {
                TryRepositionIfTracked(hwnd);
            }

            return true;
        };

        NativeMethods.EnumWindows(enumProc, IntPtr.Zero);
        GC.KeepAlive(enumProc);
    }

    private void TryRepositionIfTracked(IntPtr hwnd)
    {
        if (!NativeMethods.IsWindow(hwnd))
        {
            return;
        }

        NativeMethods.GetWindowThreadProcessId(hwnd, out uint rawPid);
        int pid = unchecked((int)rawPid);
        if (pid <= 0 || !_trackedPids.ContainsKey(pid))
        {
            return;
        }

        IntPtr rootHwnd = NativeMethods.GetAncestor(hwnd, GaRoot);
        if (rootHwnd == IntPtr.Zero)
        {
            rootHwnd = hwnd;
        }

        if (!NativeMethods.IsWindow(rootHwnd))
        {
            return;
        }

        if (rootHwnd != hwnd)
        {
            NativeMethods.GetWindowThreadProcessId(rootHwnd, out uint rootRawPid);
            int rootPid = unchecked((int)rootRawPid);
            if (rootPid <= 0 || !_trackedPids.ContainsKey(rootPid))
            {
                return;
            }
        }

        long style = NativeMethods.GetWindowLongPtr(rootHwnd, GwlStyle).ToInt64();
        if ((style & WsChild) != 0)
        {
            return;
        }

        if (NativeMethods.IsIconic(rootHwnd))
        {
            return;
        }

        if (NativeMethods.GetWindowRect(rootHwnd, out NativeMethods.Rect rect) &&
            rect.Left == _offScreenX &&
            rect.Top == _offScreenY)
        {
            return;
        }

        if (NativeMethods.SetWindowPos(
                rootHwnd,
                IntPtr.Zero,
                _offScreenX,
                _offScreenY,
                0,
                0,
                SwpNoSize | SwpNoZOrder | SwpNoActivate))
        {
            Interlocked.Increment(ref _repositionedCount);
            _diagnosticLogger?.Invoke(
                $"[windowing] Moved HWND 0x{rootHwnd.ToInt64():X} (PID {pid}) to ({_offScreenX}, {_offScreenY}) with SWP_NOACTIVATE.");
        }
    }

    private static class NativeMethods
    {
        public delegate void WinEventDelegate(
            IntPtr hWinEventHook,
            uint eventType,
            IntPtr hwnd,
            int idObject,
            int idChild,
            uint idEventThread,
            uint dwmsEventTime);

        [return: MarshalAs(UnmanagedType.Bool)]
        public delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

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

        [DllImport("kernel32.dll")]
        public static extern uint GetCurrentThreadId();

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SetWinEventHook(
            uint eventMin,
            uint eventMax,
            IntPtr hmodWinEventProc,
            WinEventDelegate lpfnWinEventProc,
            uint idProcess,
            uint idThread,
            uint dwFlags);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool UnhookWinEvent(IntPtr hWinEventHook);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool PeekMessageW(
            out Msg lpMsg,
            IntPtr hWnd,
            uint wMsgFilterMin,
            uint wMsgFilterMax,
            uint wRemoveMsg);

        [DllImport("user32.dll")]
        public static extern int GetMessageW(
            out Msg lpMsg,
            IntPtr hWnd,
            uint wMsgFilterMin,
            uint wMsgFilterMax);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool TranslateMessage(ref Msg lpMsg);

        [DllImport("user32.dll")]
        public static extern IntPtr DispatchMessageW(ref Msg lpMsg);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool PostThreadMessageW(
            uint idThread,
            uint msg,
            UIntPtr wParam,
            IntPtr lParam);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        public static extern IntPtr GetAncestor(IntPtr hWnd, uint gaFlags);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetWindowRect(IntPtr hWnd, out Rect lpRect);

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

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
        private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
        private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

        public static IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex)
        {
            return IntPtr.Size == 8
                ? GetWindowLongPtr64(hWnd, nIndex)
                : new IntPtr(GetWindowLong32(hWnd, nIndex));
        }
    }
}
