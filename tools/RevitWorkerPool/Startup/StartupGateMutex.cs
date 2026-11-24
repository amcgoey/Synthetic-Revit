using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;

namespace RevitWorkerPool.Startup;

/// <summary>
/// Coordinates per-Revit-version startup serialization across unshimmed worker boots
/// and cold <c>test_executor.py</c> runs via <c>Local\Synthetic_RevitStartupGate_&lt;Year&gt;</c>.
/// Acquires and releases the underlying Win32 <see cref="Mutex"/> on the exact same
/// dedicated OS thread so async <c>await</c> continuations never hit <c>ERROR_NOT_OWNER</c>.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class StartupGateMutex : IDisposable
{
    public const string DefaultMutexPrefix = @"Local\Synthetic_RevitStartupGate_";
    public static readonly TimeSpan DefaultAcquireTimeout = TimeSpan.FromMilliseconds(300_000);
    public static readonly TimeSpan DefaultHoldCap = TimeSpan.FromSeconds(90);

    private readonly TimeSpan _acquireTimeout;
    private readonly TimeSpan _holdCap;
    private readonly Action<string>? _diagnosticLogger;
    private readonly object _signalLock = new();
    private readonly ManualResetEventSlim _cancelAcquireSignal = new(false);
    private readonly ManualResetEventSlim _releaseSignal = new(false);
    private readonly ManualResetEventSlim _threadExitedSignal = new(false);
    private readonly TaskCompletionSource<bool> _acquireTcs =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private Thread? _holderThread;
    private int _started;
    private int _isHeld;
    private int _releasedByHoldCap;
    private int _releaseClaimed;
    private int _disposed;
    private bool _signalsDisposed;
    private uint _acquireOsThreadId;
    private uint _releaseOsThreadId;

    public StartupGateMutex(
        int revitVersion,
        string mutexPrefix = DefaultMutexPrefix,
        TimeSpan? acquireTimeout = null,
        TimeSpan? holdCap = null,
        Action<string>? diagnosticLogger = null)
    {
        MutexName = GetMutexName(revitVersion, mutexPrefix);
        RevitVersion = revitVersion;
        _acquireTimeout = acquireTimeout ?? DefaultAcquireTimeout;
        _holdCap = holdCap ?? DefaultHoldCap;
        _diagnosticLogger = diagnosticLogger;
    }

    public int RevitVersion { get; }

    public string MutexName { get; }

    public bool IsHeld => Volatile.Read(ref _isHeld) != 0;

    public bool ReleasedByHoldCap => Volatile.Read(ref _releasedByHoldCap) != 0;

    internal uint AcquireOsThreadId => Volatile.Read(ref _acquireOsThreadId);

    internal uint ReleaseOsThreadId => Volatile.Read(ref _releaseOsThreadId);

    public static string GetMutexName(int revitVersion, string mutexPrefix = DefaultMutexPrefix)
    {
        if (revitVersion <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(revitVersion), "Revit version must be positive.");
        }

        if (string.IsNullOrWhiteSpace(mutexPrefix))
        {
            throw new ArgumentException("Mutex prefix must not be empty.", nameof(mutexPrefix));
        }

        return $"{mutexPrefix}{revitVersion}";
    }

    /// <summary>
    /// Acquires and holds the per-version startup mutex on a dedicated OS holder thread
    /// until <see cref="Release"/>, <see cref="Dispose"/>, or the hold cap expires.
    /// </summary>
    public static async Task<StartupGateMutex> AcquireAsync(
        int revitVersion,
        string mutexPrefix = DefaultMutexPrefix,
        TimeSpan? acquireTimeout = null,
        TimeSpan? holdCap = null,
        Action<string>? diagnosticLogger = null,
        CancellationToken cancellationToken = default)
    {
        var gate = new StartupGateMutex(revitVersion, mutexPrefix, acquireTimeout, holdCap, diagnosticLogger);
        try
        {
            await gate.AcquireAsync(cancellationToken).ConfigureAwait(false);
            return gate;
        }
        catch
        {
            gate.Dispose();
            throw;
        }
    }

    public async Task AcquireAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (Interlocked.Exchange(ref _started, 1) != 0)
        {
            throw new InvalidOperationException($"Startup gate '{MutexName}' has already been started.");
        }

        if (cancellationToken.IsCancellationRequested)
        {
            _threadExitedSignal.Set();
            throw new OperationCanceledException(cancellationToken);
        }

        _holderThread = new Thread(RunHolderThread)
        {
            IsBackground = true,
            Name = $"RevitWorkerPool.StartupGate.{RevitVersion}"
        };
        _holderThread.Start();

        using CancellationTokenRegistration cancelReg = cancellationToken.Register(
            static state => ((StartupGateMutex)state!).SignalCancelAcquire(),
            this);

        await _acquireTcs.Task.ConfigureAwait(false);
    }

    /// <summary>
    /// Signals the dedicated OS holder thread to release the mutex and waits until
    /// <c>ReleaseMutex</c> completes. Safe to invoke from any thread or multiple times.
    /// </summary>
    public bool Release()
    {
        if (Volatile.Read(ref _started) == 0)
        {
            return false;
        }

        bool wasHeld = Volatile.Read(ref _isHeld) != 0;

        lock (_signalLock)
        {
            if (!_signalsDisposed)
            {
                _cancelAcquireSignal.Set();
                _releaseSignal.Set();
            }
        }

        if (_holderThread is not null && Thread.CurrentThread != _holderThread)
        {
            _threadExitedSignal.Wait();
        }

        return wasHeld &&
               Volatile.Read(ref _releasedByHoldCap) == 0 &&
               Interlocked.Exchange(ref _releaseClaimed, 1) == 0;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        Release();

        lock (_signalLock)
        {
            if (!_signalsDisposed)
            {
                _signalsDisposed = true;
                _cancelAcquireSignal.Dispose();
                _releaseSignal.Dispose();
                _threadExitedSignal.Dispose();
            }
        }
    }

    private void SignalCancelAcquire()
    {
        lock (_signalLock)
        {
            if (!_signalsDisposed)
            {
                _cancelAcquireSignal.Set();
            }
        }
    }

    private void RunHolderThread()
    {
        Thread.BeginThreadAffinity();
        Mutex? mutex = null;
        bool ownsMutex = false;

        try
        {
            mutex = new Mutex(initiallyOwned: false, MutexName);
            int acquireMs = _acquireTimeout == Timeout.InfiniteTimeSpan
                ? Timeout.Infinite
                : (int)Math.Clamp(_acquireTimeout.TotalMilliseconds, 0, int.MaxValue);

            WaitHandle[] waitHandles = { mutex, _cancelAcquireSignal.WaitHandle };
            int waitIndex;
            try
            {
                waitIndex = WaitHandle.WaitAny(waitHandles, acquireMs);
            }
            catch (AbandonedMutexException ex) when (ex.MutexIndex == 0)
            {
                _diagnosticLogger?.Invoke(
                    $"[startup-gate] Acquired abandoned mutex '{MutexName}' after prior holder termination.");
                waitIndex = 0;
            }

            if (waitIndex == 1)
            {
                _acquireTcs.TrySetCanceled();
                return;
            }

            if (waitIndex == WaitHandle.WaitTimeout)
            {
                _acquireTcs.TrySetException(new TimeoutException(
                    $"Timed out after {_acquireTimeout.TotalMilliseconds:F0}ms waiting for Startup Gate mutex '{MutexName}'."));
                return;
            }

            ownsMutex = true;
            Volatile.Write(ref _acquireOsThreadId, NativeMethods.GetCurrentThreadId());
            Volatile.Write(ref _isHeld, 1);
            _acquireTcs.TrySetResult(true);

            bool signaled;
            if (_holdCap == Timeout.InfiniteTimeSpan)
            {
                _releaseSignal.Wait();
                signaled = true;
            }
            else
            {
                signaled = _releaseSignal.Wait(_holdCap);
            }

            if (!signaled)
            {
                Volatile.Write(ref _releasedByHoldCap, 1);
                _diagnosticLogger?.Invoke(
                    $"[startup-gate] Hold cap ({_holdCap.TotalSeconds:F1}s) expired for '{MutexName}'; releasing Startup Gate.");
            }
        }
        catch (Exception ex) when (!_acquireTcs.Task.IsCompleted)
        {
            _acquireTcs.TrySetException(ex);
        }
        finally
        {
            if (ownsMutex && mutex is not null)
            {
                Volatile.Write(ref _isHeld, 0);
                Volatile.Write(ref _releaseOsThreadId, NativeMethods.GetCurrentThreadId());
                try
                {
                    mutex.ReleaseMutex();
                }
                catch (Exception ex)
                {
                    _diagnosticLogger?.Invoke(
                        $"[startup-gate] Warning releasing mutex '{MutexName}': {ex.Message}");
                }
            }

            mutex?.Dispose();
            _threadExitedSignal.Set();
            Thread.EndThreadAffinity();
        }
    }

    private static class NativeMethods
    {
        [DllImport("kernel32.dll")]
        public static extern uint GetCurrentThreadId();
    }
}
