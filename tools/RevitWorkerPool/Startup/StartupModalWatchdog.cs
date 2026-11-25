using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;
using RevitWorkerPool.Launching;

namespace RevitWorkerPool.Startup;

public enum StartupModalSource
{
    Journal,
    Window
}

public sealed record StartupModalDetection(
    StartupModalSource Source,
    int WorkerProcessId,
    string WorkerId,
    string Reason,
    string? JournalPath = null,
    int? ModalProcessId = null,
    IntPtr? ModalHwnd = null,
    string? ModalTitle = null);

public sealed record ProcessSnapshotEntry(int ProcessId, int ParentProcessId, string ExecutableName);

public sealed record TopLevelDialogWindow(
    IntPtr Hwnd,
    int ProcessId,
    string ClassName,
    string Title,
    IReadOnlyList<string> ChildTexts);

/// <summary>
/// Monitors a booting worker process for <c>TaskDialog_External_Tools_External_Tool_Failure</c>
/// via both live <c>Revit Journal</c> inspection (<c>SYNTHETIC_WORKER_ID:&lt;WorkerId&gt;</c>)
/// and top-level <c>#32770</c> / <c>TaskDialog</c> HWND scanning scoped to the worker PID tree.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class StartupModalWatchdog
{
    public const string StartupFailureDialogId = "TaskDialog_External_Tools_External_Tool_Failure";

    private static readonly string[] WindowFailureMarkers =
    {
        "external tools - external tool failure",
        "taskdialog_external_tools_external_tool_failure",
        "revit cannot run the external application"
    };

    private readonly Func<IReadOnlyList<ProcessSnapshotEntry>> _processSnapshotProvider;
    private readonly Func<IReadOnlySet<int>, IReadOnlyList<TopLevelDialogWindow>> _windowEnumerator;
    private readonly Func<int, string, StartupModalDetection?>? _customWindowDetector;

    public StartupModalWatchdog(
        Func<IReadOnlyList<ProcessSnapshotEntry>>? processSnapshotProvider = null,
        Func<IReadOnlySet<int>, IReadOnlyList<TopLevelDialogWindow>>? windowEnumerator = null,
        Func<int, string, StartupModalDetection?>? customWindowDetector = null)
    {
        _processSnapshotProvider = processSnapshotProvider ?? CaptureProcessSnapshot;
        _windowEnumerator = windowEnumerator ?? EnumerateDialogWindowsForPids;
        _customWindowDetector = customWindowDetector;
    }

    public static string GetDefaultJournalsDirectory(int revitVersion)
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "Autodesk", "Revit", $"Autodesk Revit {revitVersion}", "Journals");
    }

    public static HashSet<string> SnapshotKnownJournals(string? journalsDirectory)
    {
        var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string path in ListJournalFilesNewestFirst(journalsDirectory))
        {
            known.Add(Path.GetFullPath(path));
        }

        return known;
    }

    public static bool ContainsWorkerIdMarker(string? text, string? workerId)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrWhiteSpace(workerId))
        {
            return false;
        }

        string pattern = $@"SYNTHETIC_WORKER_ID:{Regex.Escape(workerId)}(?![A-Za-z0-9_.-])";
        return Regex.IsMatch(text, pattern, RegexOptions.CultureInvariant);
    }

    public StartupModalDetection? CheckOnce(
        int workerProcessId,
        string workerId,
        string? journalsDirectory,
        IReadOnlySet<string>? knownJournals = null)
    {
        StartupModalDetection? journalDetection = CheckJournals(
            workerProcessId,
            workerId,
            journalsDirectory,
            knownJournals);
        if (journalDetection is not null)
        {
            return journalDetection;
        }

        return CheckWindows(workerProcessId, workerId);
    }

    public async Task<StartupModalDetection?> WaitForModalAsync(
        LaunchedWorkerProcess worker,
        string? journalsDirectory,
        IReadOnlySet<string>? knownJournals,
        TimeSpan pollInterval,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(worker);

        TimeSpan step = pollInterval > TimeSpan.Zero ? pollInterval : TimeSpan.FromMilliseconds(250);

        while (!cancellationToken.IsCancellationRequested)
        {
            bool exited = worker.HasExited;
            StartupModalDetection? match = CheckOnce(
                worker.ProcessId,
                worker.WorkerId,
                journalsDirectory,
                knownJournals);
            if (match is not null)
            {
                return match;
            }

            if (exited)
            {
                return null;
            }

            await Task.Delay(step, cancellationToken).ConfigureAwait(false);
        }

        return null;
    }

    public StartupModalDetection? CheckJournals(
        int workerProcessId,
        string workerId,
        string? journalsDirectory,
        IReadOnlySet<string>? knownJournals = null)
    {
        if (string.IsNullOrWhiteSpace(journalsDirectory) || !Directory.Exists(journalsDirectory))
        {
            return null;
        }

        List<string> candidates = ListJournalFilesNewestFirst(journalsDirectory);
        if (candidates.Count == 0)
        {
            return null;
        }

        var contents = new List<(string Path, string Text, bool IsKnown)>(candidates.Count);
        foreach (string path in candidates)
        {
            string fullPath = Path.GetFullPath(path);
            bool isKnown = knownJournals is not null && knownJournals.Contains(fullPath);
            string text = ReadJournalTextBestEffort(fullPath);
            contents.Add((fullPath, text, isKnown));
        }

        // Pass 1: Deterministic match by SYNTHETIC_WORKER_ID:<WorkerId>
        bool foundWorkerJournal = false;
        foreach ((string path, string text, bool isKnown) in contents)
        {
            if (isKnown || !ContainsWorkerIdMarker(text, workerId))
            {
                continue;
            }

            foundWorkerJournal = true;
            if (text.Contains(StartupFailureDialogId, StringComparison.Ordinal))
            {
                return new StartupModalDetection(
                    StartupModalSource.Journal,
                    workerProcessId,
                    workerId,
                    $"journal '{path}' ({StartupFailureDialogId})",
                    JournalPath: path);
            }
        }

        if (foundWorkerJournal)
        {
            return null;
        }

        // Pass 2: Fallback for newly created unattributed journal before BootstrapApp logged marker
        foreach ((string path, string text, bool isKnown) in contents)
        {
            if (isKnown || string.IsNullOrEmpty(text))
            {
                continue;
            }

            if (text.Contains("SYNTHETIC_WORKER_ID:", StringComparison.Ordinal) ||
                text.Contains("SYNTHETIC_TEST_RUN:", StringComparison.Ordinal) ||
                text.Contains("SYNTHETIC_BOOTSTRAP:", StringComparison.Ordinal))
            {
                continue;
            }

            if (text.Contains(StartupFailureDialogId, StringComparison.Ordinal))
            {
                return new StartupModalDetection(
                    StartupModalSource.Journal,
                    workerProcessId,
                    workerId,
                    $"new unattributed journal '{path}' ({StartupFailureDialogId})",
                    JournalPath: path);
            }
        }

        return null;
    }

    public StartupModalDetection? CheckWindows(int workerProcessId, string workerId)
    {
        if (workerProcessId <= 0)
        {
            return null;
        }

        if (_customWindowDetector is not null)
        {
            return _customWindowDetector(workerProcessId, workerId);
        }

        HashSet<int> treePids = GetProcessTreePids(workerProcessId, _processSnapshotProvider());
        IReadOnlyList<TopLevelDialogWindow> windows = _windowEnumerator(treePids);

        foreach (TopLevelDialogWindow win in windows)
        {
            if (!treePids.Contains(win.ProcessId))
            {
                continue;
            }

            if (!IsTargetModalClass(win.ClassName))
            {
                continue;
            }

            if (MatchesFailureText(win.Title))
            {
                return BuildWindowDetection(workerProcessId, workerId, win);
            }

            foreach (string childText in win.ChildTexts)
            {
                if (MatchesFailureText(childText))
                {
                    return BuildWindowDetection(workerProcessId, workerId, win);
                }
            }
        }

        return null;
    }

    public static HashSet<int> GetProcessTreePids(
        int rootProcessId,
        IReadOnlyList<ProcessSnapshotEntry> snapshot)
    {
        var treePids = new HashSet<int>();
        if (rootProcessId <= 0)
        {
            return treePids;
        }

        var childrenByParent = new Dictionary<int, List<int>>();
        foreach (ProcessSnapshotEntry entry in snapshot)
        {
            if (entry.ProcessId <= 0)
            {
                continue;
            }

            if (!childrenByParent.TryGetValue(entry.ParentProcessId, out List<int>? children))
            {
                children = new List<int>();
                childrenByParent[entry.ParentProcessId] = children;
            }

            children.Add(entry.ProcessId);
        }

        var queue = new Queue<int>();
        queue.Enqueue(rootProcessId);
        while (queue.Count > 0)
        {
            int current = queue.Dequeue();
            if (current <= 0 || !treePids.Add(current))
            {
                continue;
            }

            if (childrenByParent.TryGetValue(current, out List<int>? children))
            {
                foreach (int child in children)
                {
                    if (!treePids.Contains(child))
                    {
                        queue.Enqueue(child);
                    }
                }
            }
        }

        return treePids;
    }

    internal static List<string> ListJournalFilesNewestFirst(string? journalsDirectory)
    {
        if (string.IsNullOrWhiteSpace(journalsDirectory) || !Directory.Exists(journalsDirectory))
        {
            return new List<string>();
        }

        var items = new List<(DateTime LastWriteUtc, string FullPath)>();
        try
        {
            foreach (string file in Directory.EnumerateFiles(journalsDirectory, "journal*.txt"))
            {
                try
                {
                    string fullPath = Path.GetFullPath(file);
                    DateTime mtime = File.GetLastWriteTimeUtc(fullPath);
                    items.Add((mtime, fullPath));
                }
                catch (IOException)
                {
                    // Skip transient/inaccessible file entry.
                }
                catch (UnauthorizedAccessException)
                {
                    // Skip inaccessible file entry.
                }
            }
        }
        catch (IOException)
        {
            return new List<string>();
        }
        catch (UnauthorizedAccessException)
        {
            return new List<string>();
        }

        return items
            .OrderByDescending(i => i.LastWriteUtc)
            .ThenByDescending(i => i.FullPath, StringComparer.OrdinalIgnoreCase)
            .Select(i => i.FullPath)
            .ToList();
    }

    internal static string ReadJournalTextBestEffort(string filePath)
    {
        byte[] raw;
        try
        {
            using var fs = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var ms = new MemoryStream();
            fs.CopyTo(ms);
            raw = ms.ToArray();
        }
        catch (IOException)
        {
            return string.Empty;
        }
        catch (UnauthorizedAccessException)
        {
            return string.Empty;
        }

        if (raw.Length == 0)
        {
            return string.Empty;
        }

        if (raw.Length >= 2 && raw[0] == 0xFF && raw[1] == 0xFE)
        {
            return Encoding.Unicode.GetString(raw, 2, raw.Length - 2);
        }

        if (raw.Length >= 2 && raw[0] == 0xFE && raw[1] == 0xFF)
        {
            return Encoding.BigEndianUnicode.GetString(raw, 2, raw.Length - 2);
        }

        if (raw.Length >= 3 && raw[0] == 0xEF && raw[1] == 0xBB && raw[2] == 0xBF)
        {
            return Encoding.UTF8.GetString(raw, 3, raw.Length - 3);
        }

        return Encoding.UTF8.GetString(raw);
    }

    private static StartupModalDetection BuildWindowDetection(
        int workerProcessId,
        string workerId,
        TopLevelDialogWindow win)
    {
        return new StartupModalDetection(
            StartupModalSource.Window,
            workerProcessId,
            workerId,
            $"top-level '{win.ClassName}' modal window (PID={win.ProcessId}, title='{win.Title}')",
            ModalProcessId: win.ProcessId,
            ModalHwnd: win.Hwnd,
            ModalTitle: win.Title);
    }

    private static bool IsTargetModalClass(string? className)
    {
        if (string.IsNullOrEmpty(className))
        {
            return false;
        }

        return string.Equals(className, "#32770", StringComparison.Ordinal) ||
               className.Contains("TaskDialog", StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesFailureText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        foreach (string marker in WindowFailureMarkers)
        {
            if (text.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public static IReadOnlyList<ProcessSnapshotEntry> CaptureProcessSnapshot()
    {
        const uint Th32csSnapProcess = 0x00000002;
        using SafeSnapshotHandle snap = NativeMethods.CreateToolhelp32Snapshot(Th32csSnapProcess, 0);
        if (snap.IsInvalid)
        {
            return Array.Empty<ProcessSnapshotEntry>();
        }

        var entries = new List<ProcessSnapshotEntry>();
        var pe = new NativeMethods.ProcessEntry32W
        {
            dwSize = (uint)Marshal.SizeOf<NativeMethods.ProcessEntry32W>()
        };

        if (!NativeMethods.Process32FirstW(snap, ref pe))
        {
            return entries;
        }

        do
        {
            entries.Add(new ProcessSnapshotEntry(
                unchecked((int)pe.th32ProcessID),
                unchecked((int)pe.th32ParentProcessID),
                pe.szExeFile ?? string.Empty));
        }
        while (NativeMethods.Process32NextW(snap, ref pe));

        return entries;
    }

    public static IReadOnlyList<TopLevelDialogWindow> EnumerateDialogWindowsForPids(
        IReadOnlySet<int> targetPids)
    {
        if (targetPids.Count == 0)
        {
            return Array.Empty<TopLevelDialogWindow>();
        }

        var results = new List<TopLevelDialogWindow>();

        NativeMethods.EnumWindowsProc topProc = (hwnd, _) =>
        {
            NativeMethods.GetWindowThreadProcessId(hwnd, out uint rawPid);
            int pid = unchecked((int)rawPid);
            if (!targetPids.Contains(pid))
            {
                return true;
            }

            string className = ReadClassName(hwnd);
            if (!IsTargetModalClass(className))
            {
                return true;
            }

            string title = ReadWindowText(hwnd);
            var childTexts = new List<string>();

            NativeMethods.EnumWindowsProc childProc = (childHwnd, _) =>
            {
                string childText = ReadWindowText(childHwnd);
                if (!string.IsNullOrWhiteSpace(childText))
                {
                    childTexts.Add(childText);
                }

                return true;
            };

            NativeMethods.EnumChildWindows(hwnd, childProc, IntPtr.Zero);
            GC.KeepAlive(childProc);

            results.Add(new TopLevelDialogWindow(hwnd, pid, className, title, childTexts));
            return true;
        };

        NativeMethods.EnumWindows(topProc, IntPtr.Zero);
        GC.KeepAlive(topProc);
        return results;
    }

    private static string ReadClassName(IntPtr hwnd)
    {
        Span<char> buffer = stackalloc char[256];
        int len = NativeMethods.GetClassNameW(hwnd, ref MemoryMarshal.GetReference(buffer), buffer.Length);
        return len > 0 ? new string(buffer[..Math.Min(len, buffer.Length)]) : string.Empty;
    }

    private static string ReadWindowText(IntPtr hwnd)
    {
        int length = NativeMethods.GetWindowTextLengthW(hwnd);
        int capacity = Math.Clamp(length + 1, 256, 4096);
        char[] buffer = new char[capacity];
        int copied = NativeMethods.GetWindowTextW(hwnd, buffer, capacity);
        return copied > 0 ? new string(buffer, 0, Math.Min(copied, capacity)) : string.Empty;
    }

    private sealed class SafeSnapshotHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public SafeSnapshotHandle()
            : base(ownsHandle: true)
        {
        }

        protected override bool ReleaseHandle()
        {
            return NativeMethods.CloseHandle(handle);
        }
    }

    private static class NativeMethods
    {
        [return: MarshalAs(UnmanagedType.Bool)]
        public delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct ProcessEntry32W
        {
            public uint dwSize;
            public uint cntUsage;
            public uint th32ProcessID;
            public UIntPtr th32DefaultHeapID;
            public uint th32ModuleID;
            public uint cntThreads;
            public uint th32ParentProcessID;
            public int pcPriClassBase;
            public uint dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szExeFile;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern SafeSnapshotHandle CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool Process32FirstW(SafeSnapshotHandle hSnapshot, ref ProcessEntry32W lppe);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool Process32NextW(SafeSnapshotHandle hSnapshot, ref ProcessEntry32W lppe);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CloseHandle(IntPtr hObject);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool EnumChildWindows(IntPtr hWndParent, EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetClassNameW(IntPtr hWnd, ref char lpClassName, int nMaxCount);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetWindowTextLengthW(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetWindowTextW(IntPtr hWnd, [Out] char[] lpString, int nMaxCount);
    }
}
