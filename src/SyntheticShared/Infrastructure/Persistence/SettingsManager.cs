using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using Newtonsoft.Json;

using Synthetic.Infrastructure.Persistence;
using Synthetic.Settings;
using Synthetic.Modules.DetailItemFactory.Settings;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;
using Synthetic.Shared.RevitAPI;
namespace Synthetic.Infrastructure.Persistence{
    /// <summary>
    /// Manager class responsible for lazy-loading, caching, retrieving, and saving in-document settings modules.
    /// </summary>
    public static class SettingsManager
    {
        private static readonly Dictionary<string, object> _cache = new Dictionary<string, object>();
        private static readonly object _lock = new object();

        private static string GetCacheKey(Document doc, string moduleKey)
        {
            return doc.GetHashCode() + "_" + moduleKey;
        }

        /// <summary>
        /// Retrieves the settings module of type T, checking the cache, document Extensible Storage, and firmwide defaults.
        /// </summary>
        /// <typeparam name="T">The type implementing ISettingModule.</typeparam>
        /// <param name="doc">The Revit Document context.</param>
        /// <returns>The populated settings module.</returns>
        public static T Get<T>(Document doc) where T : ISettingModule, new()
        {
            T instance = new T();
            string moduleKey = instance.ModuleKey;
            string cacheKey = GetCacheKey(doc, moduleKey);

            lock (_lock)
            {
                if (_cache.TryGetValue(cacheKey, out object? cached))
                {
                    return (T)cached;
                }
            }

            T? settings = default;

            // 1. Query DataStorage elements in doc
            try
            {
                var dataStorages = new FilteredElementCollector(doc)
                    .OfClass(typeof(DataStorage))
                    .Cast<DataStorage>()
                    .ToList();

                string storageName = "Synthetic_" + moduleKey;
                DataStorage? targetStorage = dataStorages.FirstOrDefault(ds => ds.Name.Equals(storageName, StringComparison.OrdinalIgnoreCase));

                if (targetStorage != null)
                {
                    Schema schema = SyntheticSettingsJsonSchema.GetSchema();
                    Entity entity = targetStorage.GetEntity(schema);
                    if (entity != null && entity.IsValid())
                    {
                        string jsonData = entity.Get<string>(SyntheticSettingsJsonSchema.JsonDataFieldName);
                        if (!string.IsNullOrEmpty(jsonData))
                        {
                            settings = JsonConvert.DeserializeObject<T>(jsonData);
                        }
                    }
                }
            }
            catch (Exception)
            {
                // Safely handle extensible storage exceptions
            }

            // 2. Deserialize the firmwide default from SyntheticSettings.json on local disk
            if (settings == null)
            {
                if (Synthetic.Shared.UI.ProgressCoordinator.SuppressUI)
                {
                    settings = new T();
                }
                else
                {
                    try
                    {
                        string defaultPath = Path.Combine(Config.addinPath, "SyntheticSettings.json");
                        if (File.Exists(defaultPath))
                        {
                            Config? defaultConfig = Config.ReadFromFile(defaultPath);
                            if (defaultConfig != null && defaultConfig.Contains(moduleKey))
                            {
                                settings = defaultConfig.GetSettings<T>(moduleKey);
                            }
                        }
                    }
                    catch (Exception)
                    {
                        // Catch fallback errors gracefully
                    }
                }
            }

            // 3. Fallback to fresh instantiation if not loaded yet
            if (settings == null)
            {
                settings = new T();
                // Check if Defaults() method exists to initialize baseline configurations
                var defaultsMethod = typeof(T).GetMethod("Defaults");
                if (defaultsMethod != null)
                {
                    defaultsMethod.Invoke(settings, null);
                }
            }

            lock (_lock)
            {
                _cache[cacheKey] = settings;
            }

            return settings!;
        }

        /// <summary>
        /// Serializes and writes settings module of type T to the Revit document's extensible storage.
        /// </summary>
        /// <typeparam name="T">The type implementing ISettingModule.</typeparam>
        /// <param name="doc">The Revit Document context.</param>
        /// <param name="setting">The settings module instance.</param>
        public static void Save<T>(Document doc, T setting) where T : ISettingModule
        {
            if (doc == null || setting == null) return;

            string moduleKey = setting.ModuleKey;
            string cacheKey = GetCacheKey(doc, moduleKey);
            string jsonData = JsonConvert.SerializeObject(setting, Formatting.Indented);

            void WriteEntity(Document d, DataStorage targetStorage, string key, string json)
            {
                Schema schema = SyntheticSettingsJsonSchema.GetSchema();
                Entity entity = new Entity(schema);
                entity.Set(SyntheticSettingsJsonSchema.ModuleKeyFieldName, key);
                entity.Set(SyntheticSettingsJsonSchema.JsonDataFieldName, json);
                targetStorage.SetEntity(entity);
            }

            try
            {
                var dataStorages = new FilteredElementCollector(doc)
                    .OfClass(typeof(DataStorage))
                    .Cast<DataStorage>()
                    .ToList();

                string storageName = "Synthetic_" + moduleKey;
                DataStorage? targetStorage = dataStorages.FirstOrDefault(ds => ds.Name.Equals(storageName, StringComparison.OrdinalIgnoreCase));

                if (doc.IsModifiable)
                {
                    if (targetStorage == null)
                    {
                        targetStorage = DataStorage.Create(doc);
                        targetStorage.Name = storageName;
                    }
                    WriteEntity(doc, targetStorage, moduleKey, jsonData);
                }
                else
                {
                    using (Transaction trans = new Transaction(doc, $"Save Synthetic {moduleKey} Settings"))
                    {
                        trans.Start();
                        if (targetStorage == null)
                        {
                            targetStorage = DataStorage.Create(doc);
                            targetStorage.Name = storageName;
                        }
                        WriteEntity(doc, targetStorage, moduleKey, jsonData);
                        trans.Commit();
                    }
                }
            }
            catch (Exception)
            {
                throw;
            }

            lock (_lock)
            {
                _cache[cacheKey] = setting;
            }
        }

        /// <summary>
        /// Deletes the extensible storage element for the given settings module in the Revit document.
        /// </summary>
        /// <param name="doc">The Revit Document context.</param>
        /// <param name="moduleKey">The key of the settings module.</param>
        public static void Delete(Document doc, string moduleKey)
        {
            if (doc == null || string.IsNullOrEmpty(moduleKey)) return;

            string cacheKey = GetCacheKey(doc, moduleKey);

            try
            {
                var dataStorages = new FilteredElementCollector(doc)
                    .OfClass(typeof(DataStorage))
                    .Cast<DataStorage>()
                    .ToList();

                string storageName = "Synthetic_" + moduleKey;
                DataStorage? targetStorage = dataStorages.FirstOrDefault(ds => ds.Name.Equals(storageName, StringComparison.OrdinalIgnoreCase));

                if (targetStorage != null)
                {
                    if (doc.IsModifiable)
                    {
                        doc.Delete(targetStorage.Id);
                    }
                    else
                    {
                        using (Transaction trans = new Transaction(doc, $"Delete Synthetic {moduleKey} Settings"))
                        {
                            trans.Start();
                            doc.Delete(targetStorage.Id);
                            trans.Commit();
                        }
                    }
                }
            }
            catch (Exception)
            {
                // Safe catch
            }

            lock (_lock)
            {
                if (_cache.ContainsKey(cacheKey))
                {
                    _cache.Remove(cacheKey);
                }
            }
        }

        /// <summary>
        /// Checks if there is a sync drift between the document settings and the linked external JSON file.
        /// </summary>
        /// <param name="doc">The Revit Document context.</param>
        /// <param name="linkedFilePath">Output containing the linked file path if drift exists.</param>
        /// <returns>True if drift is detected; otherwise, false.</returns>
        public static bool CheckSyncDrift(Document doc, out string linkedFilePath)
        {
            linkedFilePath = string.Empty;

            try
            {
                var syncSettings = Get<SyncSettings>(doc);
                if (syncSettings == null || string.IsNullOrEmpty(syncSettings.LinkedFilePath) || !File.Exists(syncSettings.LinkedFilePath))
                {
                    return false;
                }

                linkedFilePath = syncSettings.LinkedFilePath;

                // 1. Read JSON text from LinkedFilePath
                string externalJson = File.ReadAllText(linkedFilePath);

                // 2. Serialize current document settings (excluding SyncSettings)
                var docSettings = new System.Collections.Generic.Dictionary<string, object>
                {
                    { WorksetSettings.Name, Get<WorksetSettings>(doc) },
                    { ViewAutoNumSettings.Name, Get<ViewAutoNumSettings>(doc) },
                    { MaterialLibrarySettings.Name, Get<MaterialLibrarySettings>(doc) },
                    { ProjectMaterialSettings.Name, Get<ProjectMaterialSettings>(doc) },
                    { FileUtilitySettings.Name, Get<FileUtilitySettings>(doc) },
                    { StandardsSettings.Name, Get<StandardsSettings>(doc) },
                    { DetailItemFactorySettings.Name, Get<DetailItemFactorySettings>(doc) }
                };

                string documentJson = JsonConvert.SerializeObject(docSettings, Formatting.Indented);

                // Compare JSONs structurally
                var extDict = JsonConvert.DeserializeObject<System.Collections.Generic.Dictionary<string, Newtonsoft.Json.Linq.JToken>>(externalJson);
                if (extDict == null) return true; // Drift/corrupted file

                if (extDict.ContainsKey(SyncSettings.Name))
                {
                    extDict.Remove(SyncSettings.Name);
                }

                var docDict = JsonConvert.DeserializeObject<System.Collections.Generic.Dictionary<string, Newtonsoft.Json.Linq.JToken>>(documentJson);
                if (docDict == null) return true;

                // Structurally compare the dictionaries
                return !Newtonsoft.Json.Linq.JToken.DeepEquals(
                    Newtonsoft.Json.Linq.JToken.FromObject(extDict),
                    Newtonsoft.Json.Linq.JToken.FromObject(docDict)
                );
            }
            catch (Exception)
            {
                // Gracefully handle file locked, IO exceptions, network timeout
                return false;
            }
        }

        /// <summary>
        /// Exports all active settings from the Document Extensible Storage (excluding SyncSettings) to a JSON file.
        /// </summary>
        /// <param name="doc">The Revit Document context.</param>
        /// <param name="filePath">The path of the destination JSON file.</param>
        public static void ExportAllToFile(Document doc, string filePath)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (string.IsNullOrEmpty(filePath)) throw new ArgumentException("File path cannot be null or empty.", nameof(filePath));

            try
            {
                var docSettings = new System.Collections.Generic.Dictionary<string, object>
                {
                    { WorksetSettings.Name, Get<WorksetSettings>(doc) },
                    { ViewAutoNumSettings.Name, Get<ViewAutoNumSettings>(doc) },
                    { MaterialLibrarySettings.Name, Get<MaterialLibrarySettings>(doc) },
                    { ProjectMaterialSettings.Name, Get<ProjectMaterialSettings>(doc) },
                    { FileUtilitySettings.Name, Get<FileUtilitySettings>(doc) },
                    { StandardsSettings.Name, Get<StandardsSettings>(doc) },
                    { DetailItemFactorySettings.Name, Get<DetailItemFactorySettings>(doc) }
                };

                string jsonText = JsonConvert.SerializeObject(docSettings, Formatting.Indented);
                File.WriteAllText(filePath, jsonText);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to export settings: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Imports settings from a JSON file and overwrites the Revit document's Extensible Storage.
        /// </summary>
        /// <param name="doc">The Revit Document context.</param>
        /// <param name="filePath">The path of the source JSON file.</param>
        public static void ImportAllFromFile(Document doc, string filePath)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (string.IsNullOrEmpty(filePath)) throw new ArgumentException("File path cannot be null or empty.", nameof(filePath));
            if (!File.Exists(filePath)) throw new FileNotFoundException("Settings file not found.", filePath);

            try
            {
                string jsonText = File.ReadAllText(filePath);
                var dict = JsonConvert.DeserializeObject<System.Collections.Generic.Dictionary<string, Newtonsoft.Json.Linq.JToken>>(jsonText);

                if (dict == null)
                {
                    throw new InvalidOperationException("Failed to deserialize settings from the file.");
                }

                using (Transaction trans = new Transaction(doc, "Import Synthetic Settings"))
                {
                    trans.Start();

                    if (dict.TryGetValue(WorksetSettings.Name, out var worksetToken))
                    {
                        var settings = worksetToken.ToObject<WorksetSettings>();
                        if (settings != null) Save(doc, settings);
                    }
                    if (dict.TryGetValue(ViewAutoNumSettings.Name, out var viewAutoNumToken))
                    {
                        var settings = viewAutoNumToken.ToObject<ViewAutoNumSettings>();
                        if (settings != null) Save(doc, settings);
                    }
                    if (dict.TryGetValue(MaterialLibrarySettings.Name, out var materialLibToken))
                    {
                        var settings = materialLibToken.ToObject<MaterialLibrarySettings>();
                        if (settings != null) Save(doc, settings);
                    }
                    if (dict.TryGetValue(ProjectMaterialSettings.Name, out var projectMatToken))
                    {
                        var settings = projectMatToken.ToObject<ProjectMaterialSettings>();
                        if (settings != null) Save(doc, settings);
                    }
                    if (dict.TryGetValue(FileUtilitySettings.Name, out var fileUtilToken))
                    {
                        var settings = fileUtilToken.ToObject<FileUtilitySettings>();
                        if (settings != null) Save(doc, settings);
                    }
                    if (dict.TryGetValue(StandardsSettings.Name, out var standardsToken))
                    {
                        var settings = standardsToken.ToObject<StandardsSettings>();
                        if (settings != null) Save(doc, settings);
                    }
                    if (dict.TryGetValue(DetailItemFactorySettings.Name, out var detailItemFactoryToken))
                    {
                        var settings = detailItemFactoryToken.ToObject<DetailItemFactorySettings>();
                        if (settings != null) Save(doc, settings);
                    }

                    trans.Commit();
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to import settings: {ex.Message}", ex);
            }
        }
    }
}
