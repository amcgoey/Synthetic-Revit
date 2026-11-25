using System;
using System.Threading;

namespace Synthetic.Platform.Settings
{
    /// <summary>
    /// Runtime options controlling platform settings resolution behavior without coupling to UI state.
    /// </summary>
    public static class SettingsRuntimeOptions
    {
        private static int _syncDriftSuppressionDepth;
        private static volatile bool _suppressSyncDriftNotifications;

        /// <summary>
        /// When <c>true</c>, <see cref="SettingsManager.Get{T}"/> skips reading firmwide defaults
        /// from <c>SyntheticSettings.json</c> on disk and falls back directly to in-memory defaults.
        /// </summary>
        public static bool SuppressFirmwideDiskFallback { get; set; } = false;

        /// <summary>
        /// Gets or sets a value indicating whether sync-drift checks and <c>SyncToastNotification</c>
        /// prompts are suppressed. Prefer <see cref="BeginSuppressSyncDriftNotifications"/> for
        /// exception-safe, re-entrant scoped suppression.
        /// </summary>
        public static bool SuppressSyncDriftNotifications
        {
            get => _suppressSyncDriftNotifications || Volatile.Read(ref _syncDriftSuppressionDepth) > 0;
            set => _suppressSyncDriftNotifications = value;
        }

        /// <summary>
        /// Gets the current active depth of re-entrant <see cref="BeginSuppressSyncDriftNotifications"/> scopes.
        /// </summary>
        public static int SyncDriftSuppressionDepth => Volatile.Read(ref _syncDriftSuppressionDepth);

        /// <summary>
        /// Enters a thread-safe, re-entrant scope that suppresses sync-drift checks and toast notifications
        /// until the returned handle is disposed.
        /// </summary>
        public static IDisposable BeginSuppressSyncDriftNotifications()
        {
            Interlocked.Increment(ref _syncDriftSuppressionDepth);
            return new SyncDriftSuppressionScope();
        }

        private sealed class SyncDriftSuppressionScope : IDisposable
        {
            private int _disposed;

            public void Dispose()
            {
                if (Interlocked.Exchange(ref _disposed, 1) == 0)
                {
                    Interlocked.Decrement(ref _syncDriftSuppressionDepth);
                }
            }
        }
    }
}
