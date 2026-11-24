using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;
using RevitWorkerPool.Containment;

namespace RevitWorkerPool.Launching;

/// <summary>
/// Represents a running worker process and owns its containment lifecycle.
/// </summary>
public sealed class LaunchedWorkerProcess : IAsyncDisposable, IDisposable
{
    private const uint WaitObject0 = 0x00000000;
    private const uint DuplicateSameAccess = 0x00000002;

    private readonly SafeProcessHandle? _processHandle;
    private readonly JobObjectContainer? _jobObject;
    private readonly Func<bool>? _stubHasExited;
    private readonly Func<int?>? _stubExitCode;
    private readonly Func<CancellationToken, Task>? _stubTerminateAsync;
    private readonly Action? _onDispose;
    private readonly object _cleanupSync = new();
    private readonly List<Action> _exitOrDisposeCallbacks = new();

    private RegisteredWaitHandle? _registeredExitWait;
    private WaitHandle? _exitWaitHandle;
    private int _cleanupInvoked;
    private int _disposed;

    /// <summary>
    /// Initializes a handle-backed OS worker process inside a <see cref="JobObjectContainer"/>.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public LaunchedWorkerProcess(
        int processId,
        string processName,
        string workerId,
        SafeProcessHandle processHandle,
        JobObjectContainer jobObject,
        Action? onDispose = null)
    {
        ProcessId = processId;
        ProcessName = processName ?? throw new ArgumentNullException(nameof(processName));
        WorkerId = workerId ?? throw new ArgumentNullException(nameof(workerId));
        _processHandle = processHandle ?? throw new ArgumentNullException(nameof(processHandle));
        _jobObject = jobObject ?? throw new ArgumentNullException(nameof(jobObject));
        _onDispose = onDispose;
    }

    /// <summary>
    /// Initializes an in-process stub worker representation for headless tests.
    /// </summary>
    public LaunchedWorkerProcess(
        int processId,
        string processName,
        string workerId,
        Func<bool> hasExited,
        Func<int?> exitCode,
        Func<CancellationToken, Task> terminateAsync,
        Action? onDispose = null)
    {
        ProcessId = processId;
        ProcessName = processName ?? throw new ArgumentNullException(nameof(processName));
        WorkerId = workerId ?? throw new ArgumentNullException(nameof(workerId));
        _stubHasExited = hasExited ?? throw new ArgumentNullException(nameof(hasExited));
        _stubExitCode = exitCode ?? throw new ArgumentNullException(nameof(exitCode));
        _stubTerminateAsync = terminateAsync ?? throw new ArgumentNullException(nameof(terminateAsync));
        _onDispose = onDispose;
    }

    public int ProcessId { get; }

    public string ProcessName { get; }

    public string WorkerId { get; }

    public string PipeName => $"ricaun.{ProcessName}.{ProcessId}";

    public bool HasExited
    {
        get
        {
            bool exited;
            if (_stubHasExited is not null)
            {
                exited = _stubHasExited();
            }
            else if (_processHandle is null || _processHandle.IsInvalid || _processHandle.IsClosed)
            {
                exited = true;
            }
            else
            {
                exited = NativeMethods.WaitForSingleObject(_processHandle, 0) == WaitObject0;
            }

            if (exited)
            {
                InvokeCleanupCallbacks();
            }

            return exited;
        }
    }

    public int? ExitCode
    {
        get
        {
            if (_stubExitCode is not null)
            {
                return _stubExitCode();
            }

            if (!HasExited || _processHandle is null || _processHandle.IsInvalid || _processHandle.IsClosed)
            {
                return null;
            }

            return NativeMethods.GetExitCodeProcess(_processHandle, out uint code)
                ? unchecked((int)code)
                : null;
        }
    }

    /// <summary>
    /// Registers a callback invoked once when the worker process exits or is disposed.
    /// </summary>
    internal void RegisterExitOrDisposeCallback(Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);

        if (Volatile.Read(ref _cleanupInvoked) != 0 || Volatile.Read(ref _disposed) != 0 || HasExited)
        {
            callback();
            return;
        }

        lock (_cleanupSync)
        {
            if (Volatile.Read(ref _cleanupInvoked) != 0 || Volatile.Read(ref _disposed) != 0)
            {
                callback();
                return;
            }

            _exitOrDisposeCallbacks.Add(callback);
            if (_registeredExitWait is null &&
                _processHandle is not null &&
                !_processHandle.IsInvalid &&
                !_processHandle.IsClosed &&
                OperatingSystem.IsWindows())
            {
                TryAttachKernelExitWaitLocked();
            }
        }

        if (HasExited)
        {
            InvokeCleanupCallbacks();
        }
    }

    public async Task WaitForExitAsync(CancellationToken cancellationToken = default)
    {
        while (!HasExited)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(25, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task TerminateAsync(CancellationToken cancellationToken = default)
    {
        if (_stubTerminateAsync is not null)
        {
            await _stubTerminateAsync(cancellationToken).ConfigureAwait(false);
            InvokeCleanupCallbacks();
            return;
        }

        if (_disposed != 0)
        {
            return;
        }

        if (!HasExited)
        {
            _jobObject?.Terminate(1);

            using var waitCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            waitCts.CancelAfter(TimeSpan.FromSeconds(5));
            try
            {
                await WaitForExitAsync(waitCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // Fallback direct process termination if WaitForExit timed out.
                if (_processHandle is not null && !_processHandle.IsInvalid && !_processHandle.IsClosed)
                {
                    NativeMethods.TerminateProcess(_processHandle, 1);
                }
            }
        }

        InvokeCleanupCallbacks();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        InvokeCleanupCallbacks();

        RegisteredWaitHandle? regWait;
        WaitHandle? exitWaitHandle;
        lock (_cleanupSync)
        {
            regWait = _registeredExitWait;
            exitWaitHandle = _exitWaitHandle;
            _registeredExitWait = null;
            _exitWaitHandle = null;
        }

        if (regWait is not null)
        {
            using var unregistered = new ManualResetEvent(false);
            if (regWait.Unregister(unregistered))
            {
                unregistered.WaitOne(TimeSpan.FromSeconds(1));
            }
        }

        exitWaitHandle?.Dispose();
        _jobObject?.Dispose();
        _processHandle?.Dispose();
        _onDispose?.Invoke();
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await TerminateAsync(CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            Dispose();
        }
    }

    [SupportedOSPlatform("windows")]
    private void TryAttachKernelExitWaitLocked()
    {
        if (_processHandle is null || _processHandle.IsInvalid || _processHandle.IsClosed)
        {
            return;
        }

        IntPtr currentProcess = NativeMethods.GetCurrentProcess();
        if (!NativeMethods.DuplicateHandle(
                currentProcess,
                _processHandle,
                currentProcess,
                out SafeWaitHandle duplicatedHandle,
                0,
                bInheritHandle: false,
                DuplicateSameAccess))
        {
            duplicatedHandle.Dispose();
            return;
        }

        var waitHandle = new ProcessExitWaitHandle(duplicatedHandle);
        _exitWaitHandle = waitHandle;
        _registeredExitWait = ThreadPool.RegisterWaitForSingleObject(
            waitHandle,
            static (state, _) => ((LaunchedWorkerProcess)state!).InvokeCleanupCallbacks(),
            this,
            Timeout.Infinite,
            executeOnlyOnce: true);
    }

    private void InvokeCleanupCallbacks()
    {
        if (Interlocked.Exchange(ref _cleanupInvoked, 1) != 0)
        {
            return;
        }

        Action[] snapshot;
        lock (_cleanupSync)
        {
            snapshot = _exitOrDisposeCallbacks.ToArray();
            _exitOrDisposeCallbacks.Clear();
        }

        foreach (Action callback in snapshot)
        {
            try
            {
                callback();
            }
            catch (Exception)
            {
                // Best-effort cleanup callbacks must not break exit polling or disposal.
            }
        }
    }

    private sealed class ProcessExitWaitHandle : WaitHandle
    {
        public ProcessExitWaitHandle(SafeWaitHandle handle)
        {
            SafeWaitHandle = handle;
        }
    }

    private static class NativeMethods
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern uint WaitForSingleObject(SafeProcessHandle hHandle, uint dwMilliseconds);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetExitCodeProcess(SafeProcessHandle hProcess, out uint lpExitCode);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool TerminateProcess(SafeProcessHandle hProcess, uint uExitCode);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DuplicateHandle(
            IntPtr hSourceProcessHandle,
            SafeProcessHandle hSourceHandle,
            IntPtr hTargetProcessHandle,
            out SafeWaitHandle lpTargetHandle,
            uint dwDesiredAccess,
            [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle,
            uint dwOptions);

        [DllImport("kernel32.dll")]
        public static extern IntPtr GetCurrentProcess();
    }
}
