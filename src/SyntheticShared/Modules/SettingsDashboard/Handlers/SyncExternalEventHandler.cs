using System;
using System.IO;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using Synthetic.Settings;
using Synthetic.Infrastructure.Persistence;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.Modules.RevitDOM;
using Synthetic.Modules.SettingsDashboard.Handlers;

namespace Synthetic.Modules.SettingsDashboard.Handlers
{
    /// <summary>
    /// Request types supported by the SyncExternalEventHandler.
    /// </summary>
    public enum SyncRequestType
    {
        /// <summary>
        /// No request queued.
        /// </summary>
        None,
        
        /// <summary>
        /// Pull settings from file into document.
        /// </summary>
        Pull,
        
        /// <summary>
        /// Push settings from document into file.
        /// </summary>
        Push
    }

    /// <summary>
    /// External event handler to marshal settings pull/push operations from modeless UI to the Revit API thread.
    /// </summary>
    public class SyncExternalEventHandler : IExternalEventHandler
    {
        private readonly object _lock = new object();
        private SyncRequestType _requestType = SyncRequestType.None;
        private Document? _doc;
        private string? _filePath;

        /// <summary>
        /// Queues a sync request to be executed on the Revit API thread.
        /// </summary>
        public void QueueRequest(SyncRequestType requestType, Document doc, string filePath)
        {
            lock (_lock)
            {
                _requestType = requestType;
                _doc = doc;
                _filePath = filePath;
            }

        }

        /// <summary>
        /// Executes the queued sync request on the Revit API thread.
        /// </summary>
        public void Execute(UIApplication app)
        {
            SyncRequestType currentRequest;
            Document? currentDoc;
            string? currentFilePath;

            lock (_lock)
            {
                currentRequest = _requestType;
                currentDoc = _doc;
                currentFilePath = _filePath;

                // Reset state
                _requestType = SyncRequestType.None;
                _doc = null;
                _filePath = null;
            }

            if (currentDoc == null || string.IsNullOrEmpty(currentFilePath) || currentRequest == SyncRequestType.None)
            {
                return;
            }


            if (currentRequest == SyncRequestType.Pull)
            {
                try
                {
                    if (!File.Exists(currentFilePath))
                    {
                        Autodesk.Revit.UI.TaskDialog.Show("Pull Error", "The linked settings file no longer exists.");
                        return;
                    }

                    string jsonText = File.ReadAllText(currentFilePath);
                    var dict = JsonConvert.DeserializeObject<System.Collections.Generic.Dictionary<string, Newtonsoft.Json.Linq.JToken>>(jsonText);

                    if (dict == null)
                    {
                        Autodesk.Revit.UI.TaskDialog.Show("Pull Error", "Failed to deserialize settings from the linked file.");
                        return;
                    }

                    using (var trans = new Transaction(currentDoc, "Pull Settings from File"))
                    {
                        trans.Start();

                        if (dict.TryGetValue(WorksetSettings.Name, out var worksetToken))
                        {
                            var settings = worksetToken.ToObject<WorksetSettings>();
                            if (settings != null) SettingsManager.Save(currentDoc, settings);
                        }
                        if (dict.TryGetValue(ViewAutoNumSettings.Name, out var viewAutoNumToken))
                        {
                            var settings = viewAutoNumToken.ToObject<ViewAutoNumSettings>();
                            if (settings != null) SettingsManager.Save(currentDoc, settings);
                        }
                        if (dict.TryGetValue(MaterialLibrarySettings.Name, out var materialLibToken))
                        {
                            var settings = materialLibToken.ToObject<MaterialLibrarySettings>();
                            if (settings != null) SettingsManager.Save(currentDoc, settings);
                        }
                        if (dict.TryGetValue(ProjectMaterialSettings.Name, out var projectMatToken))
                        {
                            var settings = projectMatToken.ToObject<ProjectMaterialSettings>();
                            if (settings != null) SettingsManager.Save(currentDoc, settings);
                        }

                        trans.Commit();
                    }


                    Autodesk.Revit.UI.TaskDialog.Show("Settings Synced", "Successfully pulled configuration settings into the project.");
                }
                catch (Exception ex)
                {

                    Autodesk.Revit.UI.TaskDialog.Show("Pull Error", $"Failed to pull settings: {ex.Message}");
                }
            }
            else if (currentRequest == SyncRequestType.Push)
            {
                try
                {
                    var docSettings = new System.Collections.Generic.Dictionary<string, object>
                    {
                        { WorksetSettings.Name, SettingsManager.Get<WorksetSettings>(currentDoc) },
                        { ViewAutoNumSettings.Name, SettingsManager.Get<ViewAutoNumSettings>(currentDoc) },
                        { MaterialLibrarySettings.Name, SettingsManager.Get<MaterialLibrarySettings>(currentDoc) },
                        { ProjectMaterialSettings.Name, SettingsManager.Get<ProjectMaterialSettings>(currentDoc) }
                    };

                    // Maintain the linked file path in the file as well
                    var syncSettings = SettingsManager.Get<SyncSettings>(currentDoc);
                    docSettings.Add(SyncSettings.Name, syncSettings);

                    string jsonText = JsonConvert.SerializeObject(docSettings, Formatting.Indented);
                    File.WriteAllText(currentFilePath, jsonText);


                    Autodesk.Revit.UI.TaskDialog.Show("Settings Synced", "Successfully pushed project settings to the linked configuration file.");
                }
                catch (Exception ex)
                {

                    Autodesk.Revit.UI.TaskDialog.Show("Push Error", $"Failed to push settings: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Gets the name of this external event handler.
        /// </summary>
        public string GetName()
        {
            return "Synthetic Settings Synchronization External Event Handler";
        }
    }
}
