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

using Synthetic.Platform.UI;
using Synthetic.Platform.Revit;
using Synthetic.Platform.Settings;
using Synthetic.Features.SettingsDashboard.Handlers;
using Synthetic.Features.SettingsDashboard.Views;

#endregion

namespace Synthetic.Platform.Host
{
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
        /// Called when Revit starts up. Initializes the ribbon UI and registers document event handlers.
        /// </summary>
        /// <param name="appControlled">The UIControlledApplication object representing the Revit application.</param>
        /// <returns>A Result indicating success or failure of the startup process.</returns>
        public Result OnStartup(UIControlledApplication appControlled)
        {
            AppControlled = appControlled;
            RevitWindowHelper.MainWindowHandle = appControlled.MainWindowHandle;
            HostSettingsRegistrar.EnsureRegistered();
            RibbonManager.Create(appControlled, _path);

            appControlled.ControlledApplication.DocumentOpened +=
                new EventHandler<DocumentOpenedEventArgs>(OnDocumentOpen);
            appControlled.ControlledApplication.DocumentClosing +=
                new EventHandler<DocumentClosingEventArgs>(OnDocumentClose);

            ETransmitAssemblyResolver.EnsureRegistered();

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
            return Result.Succeeded;
        }

        /// <summary>
        /// Event handler triggered when a Revit document is opened.
        /// Checks for linked settings sync drift and displays a non-blocking toast notification if needed.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="args">The DocumentOpenedEventArgs containing event data.</param>
        public void OnDocumentOpen(object? sender, Autodesk.Revit.DB.Events.DocumentOpenedEventArgs args)
        {
            if (SettingsRuntimeOptions.SuppressSyncDriftNotifications)
            {
                return;
            }

            Document doc = args.Document;

            if (doc != null && !doc.IsFamilyDocument)
            {
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

                        var handler = new SyncExternalEventHandler();
                        var externalEvent = ExternalEvent.Create(handler);
                        var toast = new SyncToastNotification(mainWindowHandle, doc, linkedFilePath, handler, externalEvent);
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
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="args">The DocumentClosingEventArgs containing event data.</param>
        public void OnDocumentClose(object? sender, Autodesk.Revit.DB.Events.DocumentClosingEventArgs args)
        {
            Document doc = args.Document;
            if (doc != null)
            {
                SettingsManager.ClearCache(doc);
            }
        }
    }
}
