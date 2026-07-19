#region Namespaces
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
//using win = Autodesk.Windows;
using System;
using System.IO;
using System.Collections.Generic;
using System.Windows.Media.Imaging;
using Autodesk.Revit.DB.Events;

using Synthetic.Shared.UI;
using Synthetic.Core;
using Synthetic.Settings;
using Synthetic.Infrastructure.Persistence;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;
using Synthetic.Modules.SettingsDashboard.Handlers;
using Synthetic.Modules.SettingsDashboard.Views;

#endregion

namespace Synthetic.Core{
    /// <summary>
    /// The main external application class for the Revit addon.
    /// Manages the application lifecycle, ribbon UI creation, and document events.
    /// </summary>
    public class App : IExternalApplication
    {
        //Path to Assembly
        static string _path = System.Reflection.Assembly.GetExecutingAssembly().Location;

        /// <summary>
        /// Gets or sets the UIControlledApplication instance for the application session.
        /// </summary>
        public static UIControlledApplication? AppControlled;

        /// <summary>
        /// Gets the configuration collection managing app and project settings.
        /// </summary>
        public static ConfigCollection Configurations = new ConfigCollection();

        /// <summary>
        /// Called when Revit starts up. Initializes the ribbon UI and registers document event handlers.
        /// </summary>
        /// <param name="appControlled">The UIControlledApplication object representing the Revit application.</param>
        /// <returns>A Result indicating success or failure of the startup process.</returns>
        public Result OnStartup(UIControlledApplication appControlled)
        {
            AppControlled = appControlled;
            RibbonManager.Create(appControlled, _path);

            Configurations.AddAppConfig(Config.ReadAppConfig());

            appControlled.ControlledApplication.DocumentOpened +=
                new EventHandler<DocumentOpenedEventArgs>(OnDocumentOpen);
            appControlled.ControlledApplication.DocumentClosing +=
                new EventHandler<DocumentClosingEventArgs>(OnDocumentClose);

            AppDomain.CurrentDomain.AssemblyResolve += OnAssemblyResolve;

            return Result.Succeeded;
        }

        /// <summary>
        /// Called when Revit shuts down. Performs any necessary cleanup.
        /// </summary>
        /// <param name="appControlled">The UIControlledApplication object representing the Revit application.</param>
        /// <returns>A Result indicating success or failure of the shutdown process.</returns>
        public Result OnShutdown(UIControlledApplication appControlled)
        {
            appControlled.ControlledApplication.DocumentOpened -= OnDocumentOpen;
            appControlled.ControlledApplication.DocumentClosing -= OnDocumentClose;
            AppDomain.CurrentDomain.AssemblyResolve -= OnAssemblyResolve;
            return Result.Succeeded;
        }

        [ThreadStatic]
        private static bool _resolvingAssembly;

        private static System.Reflection.Assembly? OnAssemblyResolve(object? sender, ResolveEventArgs args)
        {
            if (_resolvingAssembly) return null;

            try
            {
                _resolvingAssembly = true;
                if (args.Name.Contains("eTransmitForRevitDB"))
                {
                    string version = AppControlled?.ControlledApplication.VersionNumber ?? "2026";
                    string eTransmitPath = $@"C:\Program Files\Autodesk\eTransmit for Revit {version}\eTransmitForRevitDB.dll";
                    if (File.Exists(eTransmitPath))
                    {
                        return System.Reflection.Assembly.LoadFrom(eTransmitPath);
                    }
                }
                return null;
            }
            finally
            {
                _resolvingAssembly = false;
            }
        }

        /// <summary>
        /// Event handler triggered when a Revit document is opened.
        /// Reads and registers the project configuration for the opened document.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="args">The DocumentOpenedEventArgs containing event data.</param>
        public void OnDocumentOpen(object? sender, Autodesk.Revit.DB.Events.DocumentOpenedEventArgs args)
        {
            Document doc = args.Document;

            if (doc != null && !doc.IsFamilyDocument)
            {
                var configProject = Config.ReadProjectConfig(doc);
                if (configProject != null)
                {
                    App.Configurations.AddProjectConfig(doc, configProject);
                }
                else
                {
                    App.Configurations.AddProjectConfig(doc, new Config());
                }

                try
                {
                    if (SettingsManager.CheckSyncDrift(doc, out string linkedFilePath))
                    {
                        IntPtr mainWindowHandle = IntPtr.Zero;
                        if (AppControlled != null)
                        {
                            mainWindowHandle = AppControlled.MainWindowHandle;
                        }
                        else
                        {
                            var uiApp = new Autodesk.Revit.UI.UIApplication(doc.Application);
                            mainWindowHandle = uiApp.MainWindowHandle;
                        }

                        var handler = new Synthetic.Modules.SettingsDashboard.Handlers.SyncExternalEventHandler();
                        var externalEvent = ExternalEvent.Create(handler);
                        var toast = new Synthetic.Modules.SettingsDashboard.Views.SyncToastNotification(mainWindowHandle, doc, linkedFilePath, handler, externalEvent);
                        toast.Show();
                    }
                }
                catch (Exception)
                {
                    // Fail silently to avoid interrupting document open process in case of issues
                }
            }
        }

        /// <summary>
        /// Event handler triggered when a Revit document is closing.
        /// Removes the project configuration for the closing document.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="args">The DocumentClosingEventArgs containing event data.</param>
        public void OnDocumentClose(object? sender, Autodesk.Revit.DB.Events.DocumentClosingEventArgs args)
        {
            Document doc = args.Document;

            if (doc != null && !doc.IsFamilyDocument && App.Configurations.ContainsProjectConfig(doc))
            {
                App.Configurations.RemoveProjectConfig(doc);
            }
        }
    }
}
