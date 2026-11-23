namespace Synthetic.Platform.Settings
{
    /// <summary>
    /// Runtime options controlling platform settings resolution behavior without coupling to UI state.
    /// </summary>
    public static class SettingsRuntimeOptions
    {
        /// <summary>
        /// When <c>true</c>, <see cref="SettingsManager.Get{T}"/> skips reading firmwide defaults
        /// from <c>SyntheticSettings.json</c> on disk and falls back directly to in-memory defaults.
        /// </summary>
        public static bool SuppressFirmwideDiskFallback { get; set; } = false;
    }
}
