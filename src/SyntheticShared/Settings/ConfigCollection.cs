using Autodesk.Revit.DB.Events;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Text;
using RevitDoc = Autodesk.Revit.DB.Document;

using Synthetic.Shared.RevitAPI;
using Synthetic.Settings;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;
using Synthetic.Modules.StandardsManagement.ViewModels;
namespace Synthetic.Settings
{
    /// <summary>
    /// Manages application-level and project-level configurations.
    /// </summary>
    public class ConfigCollection
    {
        internal Dictionary<string, Config> ConfigProjects;
        internal Config? ConfigApp;

        /// <summary>
        /// Initializes a new instance of the ConfigCollection class.
        /// </summary>
        public ConfigCollection()
        {
            ConfigProjects = new Dictionary<string, Config>();
        }

        /// <summary>
        /// Gets the application configuration.
        /// </summary>
        /// <returns>A Config instance representing the app configuration.</returns>
        public Config GetAppConfig()
        {
            return ConfigApp ?? new Config();
        }

        /// <summary>
        /// Gets the project configuration associated with the specified file path.
        /// </summary>
        /// <param name="path">The document file path.</param>
        /// <returns>The Config instance.</returns>
        public Config GetProjectConfig(string path)
        {
            return ConfigProjects[path];
        }

        /// <summary>
        /// Gets the project configuration associated with the specified Revit document.
        /// </summary>
        /// <param name="doc">The Revit Document.</param>
        /// <returns>The Config instance.</returns>
        public Config GetProjectConfig(RevitDoc doc)
        {
            string path = DocumentUtil.GetFilePath(doc) ?? string.Empty;
            return ConfigProjects[path];
        }

        /// <summary>
        /// Retrieves the requested settings from project configuration first, falling back to app configuration.
        /// </summary>
        /// <typeparam name="T">The type of the settings object.</typeparam>
        /// <param name="path">The project file path.</param>
        /// <param name="settingsName">The name of the settings category/key.</param>
        /// <returns>The settings instance.</returns>
        public T GetSettings<T>(string path, string settingsName) where T : class, new()
        {
            Config? configProject = null;

            if (ConfigProjects.ContainsKey(path))
            {
                configProject = ConfigProjects[path];
            }
            if (configProject != null &&
                configProject.Contains(settingsName))
            {
                return configProject.GetSettings<T>(settingsName);
            }
            else if (ConfigApp != null &&
                ConfigApp.Contains(settingsName))
            {
                return ConfigApp.GetSettings<T>(settingsName);
            }
            return new T();
        }

        /// <summary>
        /// Retrieves the requested settings for the specified Revit document, falling back to app configuration.
        /// </summary>
        /// <typeparam name="T">The type of the settings object.</typeparam>
        /// <param name="doc">The Revit Document.</param>
        /// <param name="settingsName">The name of the settings category/key.</param>
        /// <returns>The settings instance.</returns>
        public T GetSettings<T>(RevitDoc doc, string settingsName) where T : class, new()
        {
            string path = DocumentUtil.GetFilePath(doc) ?? string.Empty;
            return this.GetSettings<T>(path, settingsName);
        }

        /// <summary>
        /// Sets/adds the application configuration.
        /// </summary>
        /// <param name="configApp">The Config instance for the app.</param>
        /// <returns>This ConfigCollection instance.</returns>
        public ConfigCollection AddAppConfig (Config? configApp)
        {
            ConfigApp = configApp;
            return this;
        }

        /// <summary>
        /// Sets/adds a project configuration associated with a file path.
        /// </summary>
        /// <param name="path">The file path.</param>
        /// <param name="value">The Config instance to associate.</param>
        /// <returns>This ConfigCollection instance.</returns>
        public ConfigCollection AddProjectConfig(string path, Config value)
        {
            ConfigProjects[path] = value;
            return this;
        }

        /// <summary>
        /// Sets/adds a project configuration associated with a Revit document.
        /// </summary>
        /// <param name="doc">The Revit Document.</param>
        /// <param name="value">The Config instance to associate.</param>
        /// <returns>This ConfigCollection instance.</returns>
        public ConfigCollection AddProjectConfig(RevitDoc doc, Config value)
        {
            string path = DocumentUtil.GetFilePath(doc) ?? string.Empty;
            ConfigProjects[path] = value;
            return this;
        }

        /// <summary>
        /// Removes the project configuration associated with the specified file path.
        /// </summary>
        /// <param name="path">The file path.</param>
        /// <returns>This ConfigCollection instance.</returns>
        public ConfigCollection RemoveProjectConfig(string path)
        {
            ConfigProjects.Remove(path);
            return this;
        }

        /// <summary>
        /// Removes the project configuration associated with the specified Revit document.
        /// </summary>
        /// <param name="doc">The Revit Document.</param>
        /// <returns>This ConfigCollection instance.</returns>
        public ConfigCollection RemoveProjectConfig(RevitDoc doc)
        {
            string path = DocumentUtil.GetFilePath(doc) ?? string.Empty;
            ConfigProjects.Remove(path);
            return this;
        }

        /// <summary>
        /// Determines whether a project configuration exists for the specified file path.
        /// </summary>
        /// <param name="path">The file path.</param>
        /// <returns>True if configuration exists; otherwise, false.</returns>
        public bool ContainsProjectConfig (string path)
        {
            return ConfigProjects.ContainsKey(path);
        }

        /// <summary>
        /// Determines whether a project configuration exists for the specified Revit document.
        /// </summary>
        /// <param name="doc">The Revit Document.</param>
        /// <returns>True if configuration exists; otherwise, false.</returns>
        public bool ContainsProjectConfig(RevitDoc doc)
        {
            string path = DocumentUtil.GetFilePath(doc) ?? string.Empty;
            return ConfigProjects.ContainsKey(path);
        }
    }
}
