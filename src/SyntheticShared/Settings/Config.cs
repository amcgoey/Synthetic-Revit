using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using Autodesk.Revit.DB;

using Synthetic.Settings;
using Synthetic.Modules.DetailItemFactory.Settings;
using Synthetic.Modules.RevitDOM;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.Shared.RevitAPI;
namespace Synthetic.Settings
{
    /// <summary>
    /// Represents configuration settings for the add-in.
    /// </summary>
    public class Config
    {
        /// <summary>
        /// Gets the folder path where the executing add-in assembly is located. Ignored in JSON.
        /// </summary>
        [JsonIgnore]
        public static string addinPath { get { return Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? string.Empty; } }

        /// <summary>
        /// Gets the default filename for settings. Ignored in JSON.
        /// </summary>
        [JsonIgnore]
        public static string defaultSettingsFile { get { return "SyntheticSettings.json"; } }

        /// <summary>
        /// Gets the default full file path to the settings file. Ignored in JSON.
        /// </summary>
        [JsonIgnore]
        public static string defaultSettingsPath { get { return Path.Combine(addinPath, defaultSettingsFile); } }

        /// <summary>
        /// Gets or sets the collection of configuration setting key-value pairs.
        /// </summary>
        [JsonProperty("Settings")]
        public Dictionary<string, object?> settings { get; set; }

        /// <summary>
        /// Initializes a new instance of the Config class.
        /// </summary>
        public Config ()
        {
            this.settings = new Dictionary<string, object?> ();
        }

        /// <summary>
        /// Initializes a new instance of the Config class with a single setting option.
        /// </summary>
        /// <param name="name">The name of the setting.</param>
        /// <param name="settingsValue">The value of the setting.</param>
        public Config (string name, object? settingsValue) : base()
        {
            this.settings = new Dictionary<string, object?>();
            this.settings.Add(name, settingsValue);
        }

        /// <summary>
        /// Sets a specific setting value by name.
        /// </summary>
        /// <typeparam name="T">The type of the setting value.</typeparam>
        /// <param name="name">The name of the setting.</param>
        /// <param name="value">The value of the setting.</param>
        public void SetSettings<T>(string name, T value)
        {
            if (this.settings == null)
            {
                this.settings = new Dictionary<string, object?>();
            }
            this.settings[name] = value;
        }

        /// <summary>
        /// Gets a specific setting value by name, deserializing it if necessary.
        /// </summary>
        /// <typeparam name="T">The expected type of the setting.</typeparam>
        /// <param name="name">The name of the setting.</param>
        /// <returns>The setting value, or a new instance of T if not found.</returns>
        public T GetSettings<T>(string name) where T : new()
        {
            if (this.settings != null && this.settings.TryGetValue(name, out var val))
            {
                if (val is T typedVal) return typedVal;

                if (val is JObject jObject)
                {
                    // Backward compatibility check:
                    // If the old format nested the keys inside a "Data" sub-object, extract it.
                    if (jObject.TryGetValue("Data", out var dataToken) && dataToken is JObject dataObj)
                    {
                        return dataObj.ToObject<T>() ?? new T();
                    }
                    return jObject.ToObject<T>() ?? new T();
                }

                // If it is another type (e.g. dictionary or serialized string), serialize & deserialize as fallback
                try
                {
                    string json = JsonConvert.SerializeObject(val);
                    return JsonConvert.DeserializeObject<T>(json) ?? new T();
                }
                catch { }
            }
            return new T();
        }

        /// <summary>
        /// Populates the configuration with default settings.
        /// </summary>
        /// <returns>This Config instance.</returns>
        public Config Defaults ()
        {
            this.SetSettings(WorksetSettings.Name, new WorksetSettings().Defaults());
            this.SetSettings(ViewAutoNumSettings.Name, new ViewAutoNumSettings().Defaults());
            this.SetSettings(MaterialLibrarySettings.Name, new MaterialLibrarySettings().Defaults());
            this.SetSettings(ProjectMaterialSettings.Name, new ProjectMaterialSettings().Defaults());
            this.SetSettings(FileUtilitySettings.Name, new FileUtilitySettings().Defaults());
            this.SetSettings(DetailItemFactorySettings.Name, new DetailItemFactorySettings().Defaults());

            return this;
        }

        /// <summary>
        /// Gets the full path for a file relative to the add-in directory.
        /// </summary>
        /// <param name="filename">The filename.</param>
        /// <returns>The combined full path.</returns>
        public string FilePath (string filename)
        {
            return Path.Combine(addinPath, filename);
        }

        /// <summary>
        /// Determines whether the configuration contains a setting with the specified key.
        /// </summary>
        /// <param name="key">The setting key.</param>
        /// <returns>True if the key exists; otherwise, false.</returns>
        public bool Contains(string key)
        {
            return this.settings != null && this.settings.ContainsKey(key);
        }

        /// <summary>
        /// Returns a string representation of all settings.
        /// </summary>
        /// <returns>A formatted string displaying all key-value settings.</returns>
        public override string ToString ()
        {
            string text = string.Empty;
            if (this.settings != null)
            {
                foreach(KeyValuePair<string, object?> kvp in this.settings)
                {
                    text += kvp.Key + ": " + JsonConvert.SerializeObject(kvp.Value, Formatting.Indented) + " \n\n";
                }
            }
            return text;
        }

        /// <summary>
        /// Gets the default settings file path.
        /// </summary>
        /// <returns>The default settings file path.</returns>
        public static string FilePathDefaults ()
        {
            return Path.Combine(addinPath, defaultSettingsFile);
        }

        /// <summary>
        /// Deserializes a Config instance from a JSON string.
        /// </summary>
        /// <param name="json">The JSON string.</param>
        /// <returns>A Config instance.</returns>
        public static Config ByJson(string json)
        {
            return JsonConvert.DeserializeObject<Config>(json) ?? new Config();
        }

        /// <summary>
        /// Reads a Config instance from a JSON file.
        /// </summary>
        /// <param name="path">The file path.</param>
        /// <returns>A Config instance, or null if file not found.</returns>
        public static Config? ReadFromFile(string path)
        {
            Config? config = null;

            if (File.Exists(path))
            {
                string json = File.ReadAllText(path);
                config = JsonConvert.DeserializeObject<Config>(json);
            }
            return config;
        }

        /// <summary>
        /// Writes a Config instance to a JSON file.
        /// </summary>
        /// <param name="path">The file path to write to.</param>
        /// <param name="settings">The Config instance to serialize.</param>
        public static void WriteToFile(string path, Config settings)
        {
            string json = JsonConvert.SerializeObject(settings, Formatting.Indented);
            File.WriteAllText(path, json);
        }

        /// <summary>
        /// Reads the application level configuration.
        /// </summary>
        /// <returns>A Config instance.</returns>
        public static Config? ReadAppConfig()
        {
            return ReadFromFile(FilePathDefaults());
        }

        /// <summary>
        /// Writes the application level configuration.
        /// </summary>
        /// <param name="settings">The Config settings to save.</param>
        public static void WriteAppConfig(Config settings)
        {
            WriteToFile(FilePathDefaults(), settings);
        }

        /// <summary>
        /// Reads the project specific configuration.
        /// </summary>
        /// <param name="doc">The Revit Document.</param>
        /// <returns>A Config instance, or null.</returns>
        public static Config? ReadProjectConfig(Document doc)
        {
            return null;
        }

        /// <summary>
        /// Writes the current project configuration to the project specific file.  This will overwrite the existing file.
        /// </summary>
        /// <param name="doc">The document the file belongs to</param>
        public static void WriteProjectConfig(Document doc)
        {
        }
    }
}
