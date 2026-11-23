namespace Synthetic.Platform.Settings
{
    /// <summary>
    /// Represents a settings module that can be serialized and deserialized by <see cref="Config"/> and platform persistence services.
    /// </summary>
    public interface ISettingModule
    {
        /// <summary>
        /// Gets the unique key identifying the settings module.
        /// </summary>
        string ModuleKey { get; }
    }
}
