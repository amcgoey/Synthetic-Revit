using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Synthetic.Platform.Settings
{
    /// <summary>
    /// Represents configuration settings for the add-in.
    /// </summary>
    public class Config
    {
        /// <summary>
        /// Gets the resolved add-in root folder path (mutable settings/templates root). Ignored in JSON.
        /// Resolution order: SYNTHETIC_ADDIN_ROOT env var -> AppDomain/AppContext data -> executing assembly directory.
        /// </summary>
        [JsonIgnore]
        public static string addinPath => ResolveAddinRoot();

        internal static string ResolveAddinRoot()
        {
            string? envRoot = Environment.GetEnvironmentVariable("SYNTHETIC_ADDIN_ROOT");
            if (!string.IsNullOrWhiteSpace(envRoot))
            {
                return envRoot.Trim();
            }

#if NETFRAMEWORK
            if (AppDomain.CurrentDomain.GetData("SYNTHETIC_ADDIN_ROOT") is string appContextRoot &&
                !string.IsNullOrWhiteSpace(appContextRoot))
            {
                return appContextRoot.Trim();
            }
#else
            if (AppContext.GetData("SYNTHETIC_ADDIN_ROOT") is string appContextRoot &&
                !string.IsNullOrWhiteSpace(appContextRoot))
            {
                return appContextRoot.Trim();
            }
#endif

            return Path.GetDirectoryName(typeof(Config).Assembly.Location) ?? string.Empty;
        }

        /// <summary>
        /// Gets the default filename for settings. Ignored in JSON.
        /// </summary>
        [JsonIgnore]
        public static string defaultSettingsFile => "SyntheticSettings.json";

        /// <summary>
        /// Gets the default full file path to the settings file. Ignored in JSON.
        /// </summary>
        [JsonIgnore]
        public static string defaultSettingsPath => Path.Combine(addinPath, defaultSettingsFile);

        /// <summary>
        /// Gets or sets the collection of configuration setting key-value pairs.
        /// </summary>
        [JsonProperty("Settings")]
        public Dictionary<string, object?> settings { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="Config"/> class.
        /// </summary>
        public Config()
        {
            this.settings = new Dictionary<string, object?>();
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Config"/> class with a single setting option.
        /// </summary>
        /// <param name="name">The name of the setting.</param>
        /// <param name="settingsValue">The value of the setting.</param>
        public Config(string name, object? settingsValue)
            : this()
        {
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
            if (this.settings != null && this.settings.TryGetValue(name, out object? val))
            {
                if (val is T typedVal)
                {
                    return typedVal;
                }

                if (val is JObject jObject)
                {
                    // Backward compatibility check:
                    // If the old format nested the keys inside a "Data" sub-object, extract it.
                    if (jObject.TryGetValue("Data", out JToken? dataToken) && dataToken is JObject dataObj)
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
                catch
                {
                }
            }

            return new T();
        }

        /// <summary>
        /// Populates the configuration with default settings produced by the supplied factories.
        /// </summary>
        /// <param name="defaultFactories">Ordered pairs of module key to default-instance factory.</param>
        /// <returns>This <see cref="Config"/> instance.</returns>
        public Config Defaults(IEnumerable<KeyValuePair<string, Func<ISettingModule>>> defaultFactories)
        {
            if (defaultFactories == null)
            {
                throw new ArgumentNullException(nameof(defaultFactories));
            }

            foreach (KeyValuePair<string, Func<ISettingModule>> entry in defaultFactories)
            {
                if (string.IsNullOrEmpty(entry.Key) || entry.Value == null)
                {
                    continue;
                }

                this.SetSettings(entry.Key, entry.Value());
            }

            return this;
        }

        /// <summary>
        /// Gets the full path for a file relative to the add-in directory.
        /// </summary>
        /// <param name="filename">The filename.</param>
        /// <returns>The combined full path.</returns>
        public string FilePath(string filename)
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
        public override string ToString()
        {
            string text = string.Empty;
            if (this.settings != null)
            {
                foreach (KeyValuePair<string, object?> kvp in this.settings)
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
        public static string FilePathDefaults()
        {
            return Path.Combine(addinPath, defaultSettingsFile);
        }

        /// <summary>
        /// Deserializes a <see cref="Config"/> instance from a JSON string.
        /// </summary>
        /// <param name="json">The JSON string.</param>
        /// <returns>A <see cref="Config"/> instance.</returns>
        public static Config ByJson(string json)
        {
            return JsonConvert.DeserializeObject<Config>(json) ?? new Config();
        }

        /// <summary>
        /// Reads a <see cref="Config"/> instance from a JSON file.
        /// </summary>
        /// <param name="path">The file path.</param>
        /// <returns>A <see cref="Config"/> instance, or null if file not found.</returns>
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
        /// Writes a <see cref="Config"/> instance to a JSON file.
        /// </summary>
        /// <param name="path">The file path to write to.</param>
        /// <param name="settings">The <see cref="Config"/> instance to serialize.</param>
        public static void WriteToFile(string path, Config settings)
        {
            string json = JsonConvert.SerializeObject(settings, Formatting.Indented);
            File.WriteAllText(path, json);
        }

        /// <summary>
        /// Reads the application level configuration.
        /// </summary>
        /// <returns>A <see cref="Config"/> instance, or null if the file does not exist.</returns>
        public static Config? ReadAppConfig()
        {
            return ReadFromFile(FilePathDefaults());
        }

        /// <summary>
        /// Writes the application level configuration.
        /// </summary>
        /// <param name="settings">The <see cref="Config"/> settings to save.</param>
        public static void WriteAppConfig(Config settings)
        {
            WriteToFile(FilePathDefaults(), settings);
        }
    }
}
