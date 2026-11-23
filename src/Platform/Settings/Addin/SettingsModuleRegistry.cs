using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;

namespace Synthetic.Platform.Settings
{
    /// <summary>
    /// Describes a settings module that participates in Settings Dashboard export, import, defaults, and sync drift detection.
    /// </summary>
    public sealed class SettingsModuleDescriptor
    {
        private readonly Func<Document, ISettingModule> _loadFromDocument;
        private readonly Func<Document, JToken, bool> _importFromToken;
        private readonly Func<ISettingModule> _createDefault;

        /// <summary>
        /// Initializes a new instance of the <see cref="SettingsModuleDescriptor"/> class.
        /// </summary>
        /// <param name="moduleKey">The unique JSON and Extensible Storage key for the module.</param>
        /// <param name="settingsType">The concrete settings class type implementing <see cref="ISettingModule"/>.</param>
        /// <param name="loadFromDocument">Delegate that loads the module from a Revit document.</param>
        /// <param name="importFromToken">Delegate that deserializes a JSON token and saves the module to a Revit document.</param>
        /// <param name="createDefault">Delegate that creates a default instance of the settings module.</param>
        public SettingsModuleDescriptor(
            string moduleKey,
            Type settingsType,
            Func<Document, ISettingModule> loadFromDocument,
            Func<Document, JToken, bool> importFromToken,
            Func<ISettingModule> createDefault)
        {
            if (string.IsNullOrEmpty(moduleKey)) throw new ArgumentException("Module key cannot be null or empty.", nameof(moduleKey));
            ModuleKey = moduleKey;
            SettingsType = settingsType ?? throw new ArgumentNullException(nameof(settingsType));
            _loadFromDocument = loadFromDocument ?? throw new ArgumentNullException(nameof(loadFromDocument));
            _importFromToken = importFromToken ?? throw new ArgumentNullException(nameof(importFromToken));
            _createDefault = createDefault ?? throw new ArgumentNullException(nameof(createDefault));
        }

        /// <summary>
        /// Gets the unique settings module key.
        /// </summary>
        public string ModuleKey { get; }

        /// <summary>
        /// Gets the concrete settings class type.
        /// </summary>
        public Type SettingsType { get; }

        /// <summary>
        /// Loads the settings module instance from the given Revit document.
        /// </summary>
        /// <param name="doc">The Revit document.</param>
        /// <returns>The loaded settings module.</returns>
        public ISettingModule LoadFromDocument(Document doc)
        {
            return _loadFromDocument(doc);
        }

        /// <summary>
        /// Deserializes the JSON token and saves the resulting settings module into the given Revit document.
        /// </summary>
        /// <param name="doc">The Revit document.</param>
        /// <param name="token">The JSON token representing the module settings.</param>
        /// <returns>True if the token deserialized to a non-null settings object and was saved; otherwise, false.</returns>
        public bool ImportFromToken(Document doc, JToken token)
        {
            return _importFromToken(doc, token);
        }

        /// <summary>
        /// Creates a default instance of the settings module.
        /// </summary>
        /// <returns>A default settings module instance.</returns>
        public ISettingModule CreateDefault()
        {
            return _createDefault();
        }
    }

    /// <summary>
    /// Host-populated registry of synchronized settings modules.
    /// </summary>
    public static class SettingsModuleRegistry
    {
        private static readonly object _syncLock = new object();
        private static readonly List<SettingsModuleDescriptor> _syncedModules = new List<SettingsModuleDescriptor>();
        private static readonly HashSet<string> _registeredKeys = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>
        /// Gets all settings modules participating in firmwide sync, import/export, and default config generation (excluding <see cref="SyncSettings"/>).
        /// </summary>
        public static IReadOnlyList<SettingsModuleDescriptor> SyncedModules
        {
            get
            {
                lock (_syncLock)
                {
                    return new ReadOnlyCollection<SettingsModuleDescriptor>(_syncedModules.ToList());
                }
            }
        }

        /// <summary>
        /// Idempotently registers a synchronized settings module descriptor by its <see cref="SettingsModuleDescriptor.ModuleKey"/>.
        /// </summary>
        public static void Register(SettingsModuleDescriptor descriptor)
        {
            if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));

            lock (_syncLock)
            {
                if (_registeredKeys.Add(descriptor.ModuleKey))
                {
                    _syncedModules.Add(descriptor);
                }
            }
        }

        /// <summary>
        /// Idempotently creates and registers a <see cref="SettingsModuleDescriptor"/> for <typeparamref name="T"/>.
        /// </summary>
        public static void Register<T>(string moduleKey, Func<T> createDefault)
            where T : class, ISettingModule, new()
        {
            if (createDefault == null) throw new ArgumentNullException(nameof(createDefault));
            Register(CreateDescriptor(moduleKey, createDefault));
        }

        /// <summary>
        /// Builds a <see cref="SettingsModuleDescriptor"/> wired to <see cref="SettingsManager"/> for <typeparamref name="T"/>.
        /// </summary>
        public static SettingsModuleDescriptor CreateDescriptor<T>(string moduleKey, Func<T> createDefault)
            where T : class, ISettingModule, new()
        {
            if (createDefault == null) throw new ArgumentNullException(nameof(createDefault));

            return new SettingsModuleDescriptor(
                moduleKey,
                typeof(T),
                doc => SettingsManager.Get<T>(doc),
                (doc, token) =>
                {
                    if (doc == null || token == null) return false;
                    T? settings = token.ToObject<T>();
                    if (settings == null) return false;
                    SettingsManager.Save(doc, settings);
                    return true;
                },
                () => createDefault());
        }

        /// <summary>
        /// Compares an external linked settings JSON payload against active document settings while tolerating legacy files missing newer module keys.
        /// </summary>
        public static bool HasSyncDrift(string externalJson, IReadOnlyDictionary<string, object> documentSettings)
        {
            return SyncDriftComparer.HasSyncDrift(externalJson, documentSettings);
        }
    }

    /// <summary>
    /// Extension helpers connecting <see cref="Config"/> to the host-registered <see cref="SettingsModuleRegistry"/>.
    /// </summary>
    public static class ConfigExtensions
    {
        /// <summary>
        /// Populates <paramref name="config"/> with default instances from <see cref="SettingsModuleRegistry.SyncedModules"/>.
        /// </summary>
        public static Config Defaults(this Config config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));

            return config.Defaults(
                SettingsModuleRegistry.SyncedModules.Select(
                    descriptor => new KeyValuePair<string, Func<ISettingModule>>(descriptor.ModuleKey, descriptor.CreateDefault)));
        }
    }
}
