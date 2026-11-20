using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Autodesk.Revit.DB;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Synthetic.Modules.DetailItemFactory.Settings;
using Synthetic.Settings;

namespace Synthetic.Infrastructure.Persistence
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
    /// Central registry of synchronized settings modules and pure sync-drift comparison logic.
    /// </summary>
    public static class SettingsModuleRegistry
    {
        private static readonly IReadOnlyList<SettingsModuleDescriptor> _syncedModules =
            new ReadOnlyCollection<SettingsModuleDescriptor>(new List<SettingsModuleDescriptor>
            {
                CreateDescriptor(WorksetSettings.Name, () => new WorksetSettings().Defaults()),
                CreateDescriptor(ViewAutoNumSettings.Name, () => new ViewAutoNumSettings().Defaults()),
                CreateDescriptor(MaterialLibrarySettings.Name, () => new MaterialLibrarySettings().Defaults()),
                CreateDescriptor(ProjectMaterialSettings.Name, () => new ProjectMaterialSettings().Defaults()),
                CreateDescriptor(FileUtilitySettings.Name, () => new FileUtilitySettings().Defaults()),
                CreateDescriptor(StandardsSettings.Name, () => new StandardsSettings().Defaults()),
                CreateDescriptor(DetailItemFactorySettings.Name, () => new DetailItemFactorySettings().Defaults())
            });

        /// <summary>
        /// Gets all settings modules participating in firmwide sync, import/export, and default config generation (excluding <see cref="SyncSettings"/>).
        /// </summary>
        public static IReadOnlyList<SettingsModuleDescriptor> SyncedModules => _syncedModules;

        private static SettingsModuleDescriptor CreateDescriptor<T>(string moduleKey, Func<T> createDefault)
            where T : class, ISettingModule, new()
        {
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
        /// <param name="externalJson">The raw JSON text read from the linked settings file.</param>
        /// <param name="documentSettings">The dictionary of active document settings keyed by module key.</param>
        /// <returns>True if external JSON is null/invalid or any present synced module differs structurally from the document settings; otherwise, false.</returns>
        public static bool HasSyncDrift(string externalJson, IReadOnlyDictionary<string, object> documentSettings)
        {
            if (string.IsNullOrWhiteSpace(externalJson))
            {
                return true;
            }

            Dictionary<string, JToken>? extDict;
            try
            {
                extDict = JsonConvert.DeserializeObject<Dictionary<string, JToken>>(externalJson);
            }
            catch (JsonException)
            {
                return true;
            }

            if (extDict == null)
            {
                return true;
            }

            extDict.Remove(SyncSettings.Name);

            if (documentSettings == null)
            {
                return true;
            }

            string documentJson = JsonConvert.SerializeObject(documentSettings, Formatting.Indented);
            Dictionary<string, JToken>? docDict = JsonConvert.DeserializeObject<Dictionary<string, JToken>>(documentJson);
            if (docDict == null)
            {
                return true;
            }

            docDict.Remove(SyncSettings.Name);

            foreach (KeyValuePair<string, JToken> kvp in extDict)
            {
                if (docDict.TryGetValue(kvp.Key, out JToken? docToken) && !JToken.DeepEquals(kvp.Value, docToken))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
