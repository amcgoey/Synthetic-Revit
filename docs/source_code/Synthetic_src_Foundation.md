### File: Core/App.cs
```csharp
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
using Synthetic.Modules.RevitDOM;
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
            SyntheticRibbon.Create(appControlled, _path);

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
```

### File: Core/SyntheticRibbon.cs
```csharp
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows.Media.Imaging;
using Synthetic.Modules.AutoTagger.Commands;
using Synthetic.Modules.BatchPrint.Commands;
using Synthetic.Modules.DetailItemFactory.Commands;
using Synthetic.Modules.FamilyManagement.Commands;
using Synthetic.Modules.MaterialManagement.Commands;
using Synthetic.Modules.ViewManagement.Commands;
using Synthetic.Modules.Worksets.Commands;
using Synthetic.Modules.MergeDuplicates.Commands;
using Synthetic.Modules.StandardsManagement.Commands;
using Synthetic.Modules.SettingsDashboard.Commands;
using Synthetic.Infrastructure.Diagnostics;

using Synthetic.Core;
using Synthetic.Infrastructure.IO;
using Synthetic.Settings;
using Synthetic.Modules.MergeDuplicates.Commands;
using Synthetic.Modules.StandardsManagement.Commands;
using Synthetic.Modules.RevitDOM;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.Modules.SettingsDashboard.Commands;
namespace Synthetic.Core{
    /// <summary>
    /// Creation and management class for the Synthetic Revit ribbon interface.
    /// </summary>
    public class SyntheticRibbon
    {
        /// <summary>
        /// The Ribbon Tab Name.
        /// </summary>
        public const string TabName = "Synthetic";

        /// <summary>
        /// The Ribbon Panel Name.
        /// </summary>
        public const string PanelName = "Commands";

        /// <summary>
        /// Creates a new Ribbon Tab for the App along with panels and buttons.
        /// </summary>
        /// <param name="appControlled">The Revit UIControlledApplication object</param>
        /// <param name="path">Path to the DLL assembly</param>
        public static void Create (UIControlledApplication appControlled, string path) {
            string assetsDir = Path.Combine(Path.GetDirectoryName(path) ?? string.Empty, "Assets");

            appControlled.CreateRibbonTab(TabName);

            // --- Panel 1: Views & Tags ---
            RibbonPanel panelViewsTags = appControlled.CreateRibbonPanel(TabName, "Views & Tags");

            PushButtonData btAutoNumber = new PushButtonData(
                "Synthetic.Modules.ViewManagement.Commands.ViewsAutoNumber",
                " Autonumber\nViews ",
                path,
                "Synthetic.Modules.ViewManagement.Commands.ViewsAutoNumber"
                );
            btAutoNumber.ToolTip = "Autonumber Views on the Active Sheet";
            btAutoNumber.LargeImage = new BitmapImage(new Uri(Path.Combine(assetsDir, "autonumber_32.png")));
            btAutoNumber.Image = new BitmapImage(new Uri(Path.Combine(assetsDir, "autonumber_16.png")));
            panelViewsTags.AddItem(btAutoNumber);

            PushButtonData btBatchTag = new PushButtonData(
                "Synthetic.Modules.AutoTagger.Commands.CmdBatchTag",
                " AutoTag \nFamilies",
                path,
                "Synthetic.Modules.AutoTagger.Commands.CmdBatchTag"
                );
            btBatchTag.ToolTip = "Automatically tag all valid elements in the active view (or current selection) using your saved Universal Auto-Tagger templates.";
            btBatchTag.LargeImage = new BitmapImage(new Uri(Path.Combine(assetsDir, "autotag_32.png")));
            btBatchTag.Image = new BitmapImage(new Uri(Path.Combine(assetsDir, "autotag_16.png")));
            panelViewsTags.AddItem(btBatchTag);

            PushButtonData btManageTemplates = new PushButtonData(
                "Synthetic.Modules.AutoTagger.Commands.CmdManageTemplates",
                "Manage Templates",
                path,
                "Synthetic.Modules.AutoTagger.Commands.CmdManageTemplates"
                );
            btManageTemplates.ToolTip = "Open the Universal Auto-Tagger control panel to view, delete, import, or export tag templates.";
            btManageTemplates.LargeImage = new BitmapImage(new Uri(Path.Combine(assetsDir, "autotag_32.png")));
            btManageTemplates.Image = new BitmapImage(new Uri(Path.Combine(assetsDir, "autotag_16.png")));

            PushButtonData btAutoNumberConfig = new PushButtonData(
                "Synthetic.Modules.ViewManagement.Commands.ViewAutoNumberConfig",
                "Autonumber Config",
                path,
                "Synthetic.Modules.ViewManagement.Commands.ViewAutoNumberConfig"
                );
            btAutoNumberConfig.ToolTip = "Configure Autonumber View";
            btAutoNumberConfig.LargeImage = new BitmapImage(new Uri(Path.Combine(assetsDir, "autonumber_32.png")));
            btAutoNumberConfig.Image = new BitmapImage(new Uri(Path.Combine(assetsDir, "autonumber_16.png")));

            panelViewsTags.AddStackedItems(btManageTemplates, btAutoNumberConfig);

            PushButtonData btDetailItemFactory = new PushButtonData(
                "Synthetic.Modules.DetailItemFactory.Commands.CmdDetailItemFactory",
                "Detail Item\nFactory",
                path,
                "Synthetic.Modules.DetailItemFactory.Commands.CmdDetailItemFactory"
                );
            btDetailItemFactory.ToolTip = "Batch process selected elements into 2D Detail Item families via DWG tracing.";
            btDetailItemFactory.LargeImage = new BitmapImage(new Uri(Path.Combine(assetsDir, "convert_32.png")));
            btDetailItemFactory.Image = new BitmapImage(new Uri(Path.Combine(assetsDir, "convert_16.png")));
            panelViewsTags.AddItem(btDetailItemFactory);

            // --- Panel 2: Legends ---
            RibbonPanel panelLegends = appControlled.CreateRibbonPanel(TabName, "Legends");

            PushButtonData btDraftingToLegend = new PushButtonData(
                "Synthetic.Modules.ViewManagement.Commands.ConvertDraftingToLegend",
                " Drafting to\nLegend ",
                path,
                "Synthetic.Modules.ViewManagement.Commands.ConvertDraftingToLegend"
                );
            btDraftingToLegend.ToolTip = "Converts Drafting Views to Legends";
            btDraftingToLegend.LargeImage = new BitmapImage(new Uri(Path.Combine(assetsDir, "convert_32.png")));
            btDraftingToLegend.Image = new BitmapImage(new Uri(Path.Combine(assetsDir, "convert_16.png")));
            panelLegends.AddItem(btDraftingToLegend);

            PushButtonData btLegendToDrafting = new PushButtonData(
                "Synthetic.Modules.ViewManagement.Commands.ConvertLegendToDrafting",
                " Legend to\nDrafting ",
                path,
                "Synthetic.Modules.ViewManagement.Commands.ConvertLegendToDrafting"
                );
            btLegendToDrafting.ToolTip = "Converts Legends to Drafting Views";
            btLegendToDrafting.LargeImage = new BitmapImage(new Uri(Path.Combine(assetsDir, "convert_32.png")));
            btLegendToDrafting.Image = new BitmapImage(new Uri(Path.Combine(assetsDir, "convert_16.png")));
            panelLegends.AddItem(btLegendToDrafting);

            // --- Panel 3: Model Management ---
            RibbonPanel panelModel = appControlled.CreateRibbonPanel(TabName, "Model Management");

            PushButtonData btnMergeDuplicates = new PushButtonData(
                "Synthetic.Modules.MergeDuplicates.Commands.CmdMergeDuplicates",
                "Merge\nDuplicates",
                path,
                "Synthetic.Modules.MergeDuplicates.Commands.CmdMergeDuplicates"
                );
            btnMergeDuplicates.ToolTip = "Scans the model or active selection for duplicate families, groups, or assemblies, matching parameters and swapping instances cleanly.";
            btnMergeDuplicates.LargeImage = new BitmapImage(new Uri(Path.Combine(assetsDir, "family_32.png")));
            btnMergeDuplicates.Image = new BitmapImage(new Uri(Path.Combine(assetsDir, "family_16.png")));
            panelModel.AddItem(btnMergeDuplicates);

            PushButtonData btnProjectStandards = new PushButtonData(
                "Synthetic.Modules.StandardsManagement.Commands.CmdProjectStandards",
                "Project Standards",
                path,
                "Synthetic.Modules.StandardsManagement.Commands.CmdProjectStandards"
                );
            btnProjectStandards.ToolTip = "Launch the Project Standards Workspace to load, stage, batch edit, and enforce standard configurations.";
            btnProjectStandards.LargeImage = new BitmapImage(new Uri(Path.Combine(assetsDir, "family_32.png")));
            btnProjectStandards.Image = new BitmapImage(new Uri(Path.Combine(assetsDir, "family_16.png")));
            panelModel.AddItem(btnProjectStandards);

            PushButtonData btMaterialsRepathAll = new PushButtonData(
                "Synthetic.Modules.MaterialManagement.Commands.MaterialsRepathAll",
                "Material Repath",
                path,
                "Synthetic.Modules.MaterialManagement.Commands.MaterialsRepathAll"
                );
            btMaterialsRepathAll.ToolTip = "Command will replace the file paths for the selected materials based on a series of selected file paths to search.  The first instance of the file will be chosen based on the order of the paths.";
            btMaterialsRepathAll.LargeImage = new BitmapImage(new Uri(Path.Combine(assetsDir, "material_32.png")));
            btMaterialsRepathAll.Image = new BitmapImage(new Uri(Path.Combine(assetsDir, "material_16.png")));

            PushButtonData btMaterialImagePackage = new PushButtonData(
                MaterialImagesPackage.CommandPath,
                "Material Image Pkg",
                path,
                MaterialImagesPackage.CommandPath
                );
            btMaterialImagePackage.ToolTip = MaterialImagesPackage.CommandTooltip;
            btMaterialImagePackage.LargeImage = new BitmapImage(new Uri(Path.Combine(assetsDir, "material_32.png")));
            btMaterialImagePackage.Image = new BitmapImage(new Uri(Path.Combine(assetsDir, "material_16.png")));

            PushButtonData btPaintElements = new PushButtonData(
                "Synthetic.Modules.MaterialManagement.Commands.PaintElements",
                " Paint\nElements ",
                path,
                "Synthetic.Modules.MaterialManagement.Commands.PaintElements"
                );
            btPaintElements.ToolTip = "Paints all faces of the selected elements with the chosen material.";
            btPaintElements.LargeImage = new BitmapImage(new Uri(Path.Combine(assetsDir, "paint_32.png")));
            btPaintElements.Image = new BitmapImage(new Uri(Path.Combine(assetsDir, "paint_16.png")));

            panelModel.AddStackedItems(btMaterialsRepathAll, btMaterialImagePackage, btPaintElements);

            panelModel.AddSlideOut();

            PushButtonData btAuditPurgeAllFamilies = new PushButtonData(
                "Synthetic.Modules.FamilyManagement.Commands.AuditPurgeAllFamilies",
                "Audit & Purge Families",
                path,
                "Synthetic.Modules.FamilyManagement.Commands.AuditPurgeAllFamilies"
                );
            btAuditPurgeAllFamilies.ToolTip = "Command open each family in the project, checking it for warnings and errors as well as purging it and deleting all schema in memory.";
            btAuditPurgeAllFamilies.LargeImage = new BitmapImage(new Uri(Path.Combine(assetsDir, "family_32.png")));
            btAuditPurgeAllFamilies.Image = new BitmapImage(new Uri(Path.Combine(assetsDir, "family_16.png")));
            panelModel.AddItem(btAuditPurgeAllFamilies);

#if DEBUG
            PushButtonData btTestAuditPurgeJournal = new PushButtonData(
                "Synthetic.Modules.FamilyManagement.Commands.CmdTestAuditPurgeJournal",
                "Test Audit Purge",
                path,
                "Synthetic.Modules.FamilyManagement.Commands.CmdTestAuditPurgeJournal"
                );
            btTestAuditPurgeJournal.ToolTip = "Headless integration test command for Audit & Purge";
            btTestAuditPurgeJournal.AvailabilityClassName = "Synthetic.Modules.FamilyManagement.Commands.CmdTestAuditPurgeJournalAvailability";
            panelModel.AddItem(btTestAuditPurgeJournal);
#endif

            PushButtonData btFamilyForceReinsert = new PushButtonData(
                "Synthetic.Modules.FamilyManagement.Commands.FamiliesForceReinsert",
                "Force Reinsert Families",
                path,
                "Synthetic.Modules.FamilyManagement.Commands.FamiliesForceReinsert"
                );
            btFamilyForceReinsert.ToolTip = "Forces a reinsert and overwrite of all families.  This can fix some post upgrade issues such as when text in families revert back to default font style.";
            btFamilyForceReinsert.LargeImage = new BitmapImage(new Uri(Path.Combine(assetsDir, "family_32.png")));
            btFamilyForceReinsert.Image = new BitmapImage(new Uri(Path.Combine(assetsDir, "family_16.png")));
            panelModel.AddItem(btFamilyForceReinsert);



            // --- Panel 4: Worksets & Setup ---
            RibbonPanel panelWorkset = appControlled.CreateRibbonPanel(TabName, "Worksets & Setup");

            PushButtonData btWorksetImport = new PushButtonData(
                "Synthetic.Modules.Worksets.Commands.WorksetsImport",
                " Import\nWorksets ",
                path,
                "Synthetic.Modules.Worksets.Commands.WorksetsImport"
                );
            btWorksetImport.ToolTip = "Creates worksets from an Excel file";
            btWorksetImport.LargeImage = new BitmapImage(new Uri(Path.Combine(assetsDir, "workset_32.png")));
            btWorksetImport.Image = new BitmapImage(new Uri(Path.Combine(assetsDir, "workset_16.png")));
            panelWorkset.AddItem(btWorksetImport);

            PushButtonData btMoveScopeBoxes = new PushButtonData(
                "Synthetic.Modules.Worksets.Commands.ScopeBoxesMoveToWorkset",
                " Move\nScope Boxes ",
                path,
                "Synthetic.Modules.Worksets.Commands.ScopeBoxesMoveToWorkset"
                );
            btMoveScopeBoxes.ToolTip = "Moves all scope boxes in the project to a selected workset.";
            btMoveScopeBoxes.LargeImage = new BitmapImage(new Uri(Path.Combine(assetsDir, "scopebox_32.png")));
            btMoveScopeBoxes.Image = new BitmapImage(new Uri(Path.Combine(assetsDir, "scopebox_16.png")));
            panelWorkset.AddItem(btMoveScopeBoxes);

            PushButtonData btWorksetFile = new PushButtonData(
                "Synthetic.Modules.Worksets.Commands.WorksetSetFile",
                "Set Workset File",
                path,
                "Synthetic.Modules.Worksets.Commands.WorksetSetFile"
                );
            btWorksetFile.ToolTip = "Set the path and worksheet for importing Worksets";
            btWorksetFile.LargeImage = new BitmapImage(new Uri(Path.Combine(assetsDir, "workset_32.png")));
            btWorksetFile.Image = new BitmapImage(new Uri(Path.Combine(assetsDir, "workset_16.png")));

            PushButtonData btWorksetShow = new PushButtonData(
                "Synthetic.Modules.Worksets.Commands.WorksetSettingsShow",
                "Display Settings",
                path,
                "Synthetic.Modules.Worksets.Commands.WorksetSettingsShow"
                );
            btWorksetShow.ToolTip = "Display the project's Workset Settings";
            btWorksetShow.LargeImage = new BitmapImage(new Uri(Path.Combine(assetsDir, "workset_32.png")));
            btWorksetShow.Image = new BitmapImage(new Uri(Path.Combine(assetsDir, "workset_16.png")));

            PushButtonData btWorksetStartView = new PushButtonData(
                "Synthetic.Modules.Worksets.Commands.WorksetStartView",
                "Workset Startview",
                path,
                "Synthetic.Modules.Worksets.Commands.WorksetStartView"
                );
            btWorksetStartView.ToolTip = "Sets the project information (like start view) from the Excel file";
            btWorksetStartView.LargeImage = new BitmapImage(new Uri(Path.Combine(assetsDir, "workset_32.png")));
            btWorksetStartView.Image = new BitmapImage(new Uri(Path.Combine(assetsDir, "workset_16.png")));

            panelWorkset.AddStackedItems(btWorksetFile, btWorksetShow, btWorksetStartView);

            panelWorkset.AddSlideOut();

            PushButtonData btRecordWorkset = new PushButtonData(
                "Synthetic.Modules.Worksets.Commands.ElementsOnWorksetRecord",
                "Record Elements",
                path,
                "Synthetic.Modules.Worksets.Commands.ElementsOnWorksetRecord"
                );
            btRecordWorkset.ToolTip = "Records the elements on a workset in a text file to be recalled later.";
            btRecordWorkset.LargeImage = new BitmapImage(new Uri(Path.Combine(assetsDir, "workset_32.png")));
            btRecordWorkset.Image = new BitmapImage(new Uri(Path.Combine(assetsDir, "workset_16.png")));
            panelWorkset.AddItem(btRecordWorkset);

            PushButtonData btReloadWorkset = new PushButtonData(
                "Synthetic.Modules.Worksets.Commands.ElementsOnWorksetReload",
                "Reload Elements",
                path,
                "Synthetic.Modules.Worksets.Commands.ElementsOnWorksetReload"
                );
            btReloadWorkset.ToolTip = "Reloads a list of elements from a file and moves them to the workset listed.  Command will create the workset if it doesn't already exist.";
            btReloadWorkset.LargeImage = new BitmapImage(new Uri(Path.Combine(assetsDir, "workset_32.png")));
            btReloadWorkset.Image = new BitmapImage(new Uri(Path.Combine(assetsDir, "workset_16.png")));
            panelWorkset.AddItem(btReloadWorkset);

            // --- Panel 5: Publish ---
            RibbonPanel panelPublish = appControlled.CreateRibbonPanel(TabName, "Publish");

            PushButtonData btMultiPrint = new PushButtonData(
                "Synthetic.Modules.BatchPrint.Commands.PrintBatchMultiDoc",
                " Batch Print\nMulti Doc ",
                path,
                "Synthetic.Modules.BatchPrint.Commands.PrintBatchMultiDoc"
                );
            btMultiPrint.ToolTip = "Prints a view set in multiple documents";
            btMultiPrint.LargeImage = new BitmapImage(new Uri(Path.Combine(assetsDir, "print_32.png")));
            btMultiPrint.Image = new BitmapImage(new Uri(Path.Combine(assetsDir, "print_16.png")));
            panelPublish.AddItem(btMultiPrint);

            // --- Panel 6: Admin & Settings ---
            RibbonPanel panelSettings = appControlled.CreateRibbonPanel(TabName, "Admin & Settings");

            PushButtonData btSettingsDashboard = new PushButtonData(
                "Synthetic.Modules.SettingsDashboard.Commands.SettingsDashboardCommand",
                "Settings Dashboard",
                path,
                "Synthetic.Modules.SettingsDashboard.Commands.SettingsDashboardCommand"
                );
            btSettingsDashboard.ToolTip = "Opens the Settings Dashboard to view and edit project configuration overrides.";
            btSettingsDashboard.LargeImage = new BitmapImage(new Uri(Path.Combine(assetsDir, "settings_32.png")));
            btSettingsDashboard.Image = new BitmapImage(new Uri(Path.Combine(assetsDir, "settings_16.png")));
            panelSettings.AddItem(btSettingsDashboard);

            // Extensible Storage
            SplitButtonData sbd1 = new SplitButtonData("Synthetic.Split.Schema", "Ext. Storage");
            SplitButton? sb1 = panelSettings.AddItem(sbd1) as SplitButton;

            if (sb1 != null)
            {
                PushButtonData btStorageQuery = new PushButtonData(
                    "Synthetic.Infrastructure.Diagnostics.StorageQuery",
                    "Display Ext Storage",
                    path,
                    "Synthetic.Infrastructure.Diagnostics.StorageQuery"
                    );
                btStorageQuery.ToolTip = "List all Schemas and entities";
                btStorageQuery.LargeImage = new BitmapImage(new Uri(Path.Combine(assetsDir, "schema_32.png")));
                btStorageQuery.Image = new BitmapImage(new Uri(Path.Combine(assetsDir, "schema_16.png")));
                sb1.AddPushButton(btStorageQuery);

                PushButtonData btStorageDelete = new PushButtonData(
                    "Synthetic.Infrastructure.Diagnostics.StorageDelete",
                    "Delete Ext Storage",
                    path,
                    "Synthetic.Infrastructure.Diagnostics.StorageDelete"
                    );
                btStorageDelete.ToolTip = "Delete All Schema and entities from current model.  Will throw errors if links are opened.";
                btStorageDelete.LargeImage = new BitmapImage(new Uri(Path.Combine(assetsDir, "schema_32.png")));
                btStorageDelete.Image = new BitmapImage(new Uri(Path.Combine(assetsDir, "schema_16.png")));
                sb1.AddPushButton(btStorageDelete);
            }
        }

    }
}
```

### File: Infrastructure/Diagnostics/StorageDelete.cs
```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using TaskDialog = Autodesk.Revit.UI.TaskDialog;
using Autodesk.Revit.DB.ExtensibleStorage;

using Synthetic.Shared.UI;

using Synthetic.Shared.RevitAPI;
using Synthetic.Infrastructure.IO;

using Synthetic.Infrastructure.Diagnostics;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.Modules.RevitDOM;
namespace Synthetic.Infrastructure.Diagnostics{
    /// <summary>
    /// Deletes all extensible storage created by any application all active documents.
    /// This command will also report if there is no storage in the active document to delete.
    /// The document must be saved after the storage is deleted to commit the deletion.
    /// </summary>
    [Autodesk.Revit.Attributes.Transaction(Autodesk.Revit.Attributes.TransactionMode.Manual)]
    [Autodesk.Revit.Attributes.Regeneration(Autodesk.Revit.Attributes.RegenerationOption.Manual)]
    public class StorageDelete : IExternalCommand
    {
        /// <summary>
        /// Executes the extensible storage deletion command.
        /// </summary>
        /// <param name="commandData">Revit external command data.</param>
        /// <param name="message">A message returning errors if any.</param>
        /// <param name="elements">Revit elements set.</param>
        /// <returns>Result code of the execution.</returns>
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiapp = commandData.Application;
            Document document = uiapp.ActiveUIDocument.Document;

            if (!(StorageUtil.DoesAnyStorageExist(document)))
                message = "No storage in this document to delete.";
            else
            {
                IList<Schema> schemas = Schema.ListSchemas();
                List<string> itemList = new List<string>();

                foreach (Schema schema in schemas)
                {
                    itemList.Add(schema.SchemaName + " | " + schema.GUID);
                }

                ListByCheckboxViewModel viewModel = new ListByCheckboxViewModel();
                viewModel.Title = "Select Extensible Storage Schemas to Delete";
                viewModel.Instruction = "Choose which Extensible Storage Schemas to delete from the active document.";
                viewModel.IsSingleSelection = false;
                viewModel.SetItems(itemList, false);

                ListByCheckboxView dialog = new ListByCheckboxView(uiapp.MainWindowHandle) { DataContext = viewModel };

                bool? dResult = dialog.ShowDialog();

                if (dResult == true)
                {
                    List<Schema> filteredSchemas = new List<Schema>();
                    foreach (Schema schema in schemas)
                    {
                        if (viewModel.CheckedItems.Contains(schema.SchemaName + " | " + schema.GUID))
                        {
                            filteredSchemas.Add(schema);
                        }
                    }
                    if (filteredSchemas.Count > 0)
                    {
                        List<string>? purgedSchema = StorageUtil.PurgeSchema(filteredSchemas, document);
                        if (purgedSchema != null && purgedSchema.Count > 0)
                        {
                            message = "Extensible storage was deleted.\n";
                            message = message + String.Join("\n", purgedSchema.ToArray());
                        }
                        else { message = "Error: There was a problem deleting the extensible storage."; }
                    }
                    else
                    {
                        message = "No Schema selected.  No action taken.";
                    }
                }
            }

            TaskDialog.Show("ExtensibleStorageUtility", message);
            return Result.Succeeded;
        }
    }
}
```

### File: Infrastructure/Diagnostics/StorageQuery.cs
```csharp
﻿//
// (C) Copyright 2003-2019 by Autodesk, Inc.
//
// Permission to use, copy, modify, and distribute this software in
// object code form for any purpose and without fee is hereby granted,
// provided that the above copyright notify appears in all copies and
// that both that copyright notify and the limited warranty and
// restricted rights notify below appear in all supporting
// documentation.
//
// AUTODESK PROVIDES THIS PROGRAM "AS IS" AND WITH ALL FAULTS.
// AUTODESK SPECIFICALLY DISCLAIMS ANY IMPLIED WARRANTY OF
// MERCHANTABILITY OR FITNESS FOR A PARTICULAR USE. AUTODESK, INC.
// DOES NOT WARRANT THAT THE OPERATION OF THE PROGRAM WILL BE
// UNINTERRUPTED OR ERROR FREE.
//
// Use, duplication, or disclosure by the U.S. Government is subject to
// restrictions set forth in FAR 52.227-19 (Commercial Computer
// Software - Restricted Rights) and DFAR 252.227-7013(c)(1)(ii)
// (Rights in Technical Data and Computer Software), as applicable.
//

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Diagnostics;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.DB.ExtensibleStorage;



using Synthetic.Infrastructure.Diagnostics;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.Modules.RevitDOM;
using Synthetic.Shared.RevitAPI;
namespace Synthetic.Infrastructure.Diagnostics{

    /// <summary>
    /// Checks to see if any extensible storage in the document exists and displays elements
    /// containing storage to the user.
    /// </summary>
    [Autodesk.Revit.Attributes.Transaction(Autodesk.Revit.Attributes.TransactionMode.Manual)]
    [Autodesk.Revit.Attributes.Regeneration(Autodesk.Revit.Attributes.RegenerationOption.Manual)]
    public class StorageQuery : IExternalCommand
    {
        /// <summary>
        /// Execute a Revit Command
        /// </summary>
        /// <param name="commandData">commandData</param>
        /// <param name="message">message</param>
        /// <param name="elements">Currently selected elements</param>
        /// <returns>A Autodesk.Revit.UI.Result</returns>
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            Document document = commandData.Application.ActiveUIDocument.Document;
            string storageElements = StorageUtil.GetElementStringWithAllSchemas(document);
            Autodesk.Revit.UI.TaskDialog.Show("ExtensibleStorageUtility", storageElements);


            return Result.Succeeded;
        }
    }

}
```

### File: Infrastructure/IO/Excel.cs
```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using ExcelMS = Microsoft.Office.Interop.Excel;

using Synthetic.Infrastructure.IO;
using Synthetic.Modules.RevitDOM;
using Synthetic.Modules.StandardsManagement.ViewModels;
namespace Synthetic.Infrastructure.IO{
    /// <summary>
    /// Helper class to read data from Microsoft Excel spreadsheets using Interop.
    /// </summary>
    public class Excel
    {
        /// <summary>
        /// Gets or sets the file path to the Excel workbook.
        /// </summary>
        public string path { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the name of the worksheet to read.
        /// </summary>
        public string? worksheetName { get; set; }

        /// <summary>
        /// Gets or sets the cell values read from the spreadsheet, stored as a 2D list of objects.
        /// </summary>
        public List<List<object>>? cells { get; set; }

        /// <summary>
        /// Initializes a new instance of the Excel class with a file path.
        /// </summary>
        /// <param name="path">The file path to the Excel workbook.</param>
        public Excel(string path)
        {
            this.path = path;
            this.worksheetName = null;
            this.cells = null;
        }

        /// <summary>
        /// Initializes a new instance of the Excel class with a file path and worksheet name.
        /// </summary>
        /// <param name="path">The file path to the Excel workbook.</param>
        /// <param name="worksheetName">The name of the worksheet to load.</param>
        public Excel(string path, string? worksheetName)
        {
            this.path = path;
            this.worksheetName = worksheetName;
            this.cells = null;
        }

        /// <summary>
        /// Reads the spreadsheet and returns cell contents as a 2D list.
        /// </summary>
        /// <returns>A list of rows, where each row is a list of cell values.</returns>
        public List<List<object>>? ReadExcel()
        {
            if (path == null || !File.Exists(path))
            {
                Console.WriteLine("File not found.");
                return null;
            }

            Microsoft.Office.Interop.Excel.Application excelApp = new Microsoft.Office.Interop.Excel.Application();
            Microsoft.Office.Interop.Excel.Workbook excelWorkbook = excelApp.Workbooks.Open(path);

            Microsoft.Office.Interop.Excel.Worksheet? excelWorksheet = null;

            if (this.worksheetName != null)
            {
                excelWorksheet = excelWorkbook.Worksheets[worksheetName] as Microsoft.Office.Interop.Excel.Worksheet;
            }
            else
            {
                excelWorksheet = excelWorkbook.Worksheets[1] as Microsoft.Office.Interop.Excel.Worksheet;
            }

            if (excelWorksheet != null)
            {
                excelWorksheet.AutoFilterMode = false;

                Microsoft.Office.Interop.Excel.Range range = excelWorksheet.UsedRange;

                int rowCount = range.Rows.Count;
                int columnCount = range.Columns.Count;
                this.cells = new List<List<object>>();

                for (int row = 1; row <= rowCount; row++)
                {
                    List<object> rowData = new List<object>();
                    for (int col = 1; col <= columnCount; col++)
                    {
                        Microsoft.Office.Interop.Excel.Range? cellRange = range.Cells[row, col] as Microsoft.Office.Interop.Excel.Range;
                        object? cellVal = cellRange?.Value;
                        if (cellVal != null)
                        {
                            rowData.Add(cellVal.ToString() ?? "");
                        }
                        else
                        {
                            rowData.Add("");
                        }
                    }
                    this.cells.Add(rowData);
                }
            }

            //excelWorkbook.Save();
            excelWorkbook.Close();
            excelApp.Quit();

            return this.cells;
        }

        /// <summary>
        /// Retrieves the list of worksheet names from the Excel workbook.
        /// </summary>
        /// <returns>A list of worksheet name strings.</returns>
        public List<string> WorkSheetNames ()
        {
            if (path == null || !File.Exists(path))
            {
                Console.WriteLine("File not found.");
                return new List<string>();
            }

            Microsoft.Office.Interop.Excel.Application excelApp = new Microsoft.Office.Interop.Excel.Application();
            Microsoft.Office.Interop.Excel.Workbook excelWorkbook = excelApp.Workbooks.Open(path);

            List<string> names = new List<string>();
            foreach (Microsoft.Office.Interop.Excel.Worksheet ws in excelWorkbook.Worksheets)
            {
                names.Add(ws.Name);
            }

            excelWorkbook.Close();
            excelApp.Quit();

            return names;
        }
    }
}
```

### File: Infrastructure/IO/FileUtil.cs
```csharp
using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using System.Linq;
using System.Windows.Forms.VisualStyles;
using Autodesk.Revit.DB;
using System.Text.RegularExpressions;
using Synthetic.Settings;
using Synthetic.Infrastructure.Persistence;

using Synthetic.Infrastructure.IO;
using Synthetic.Modules.RevitDOM;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.Shared.RevitAPI;
namespace Synthetic.Infrastructure.IO{
    /// <summary>
    /// Utility methods for file operations, alternate path searches, and file organization.
    /// </summary>
    public class FileUtil
    {
        /// <summary>
        /// Copies a list of files to a new root directory, optionally preserving directory sub-structures and searching alternate paths.
        /// </summary>
        /// <param name="filePaths">The list of file paths to copy.</param>
        /// <param name="newPathRoot">The new destination root path.</param>
        /// <param name="rootNames">Optional list of root directory names to separate and preserve sub-structures.</param>
        /// <param name="overwrite">If true, existing files will be overwritten; otherwise, they will be skipped.</param>
        /// <returns>A dictionary containing details of files successfully copied and files not copied.</returns>
        public static Dictionary<string, Dictionary<string,object>>
            CopyFiles (List<string> filePaths, string newPathRoot, List<string>? rootNames = null, bool overwrite = true)
        {
            Dictionary<string, object> filesCopied = new Dictionary<string, object>();
            Dictionary<string, object> filesNotCopied = new Dictionary<string, object>();

            List<Dictionary<string, string>> FilesToCopy = new List<Dictionary<string, string>>();

            List<string> uniqueFilePaths = filePaths.Distinct().ToList();
            // Dynamically load archive directories and alternate paths from settings with fallbacks
            List<string>? archiveRoots = null;
            Dictionary<string, List<string>>? configAlternatePaths = null;

            try
            {
                Config? appConfig = Config.ReadAppConfig();
                if (appConfig != null)
                {
                    FileUtilitySettings? fileUtilSettings = appConfig.GetSettings<FileUtilitySettings>(FileUtilitySettings.Name);
                    if (fileUtilSettings != null)
                    {
                        archiveRoots = fileUtilSettings.ArchiveDirectories;
                        configAlternatePaths = fileUtilSettings.AlternatePaths;
                    }
                }
            }
            catch (Exception)
            {
                // Fallback gracefully to hardcoded defaults
            }

            // Fallback for archive roots
            if (archiveRoots == null || archiveRoots.Count == 0)
            {
                archiveRoots = new List<string> { @"\\NAS04\Archive" };
            }

            // Fallback for alternate paths
            if (configAlternatePaths == null || configAlternatePaths.Count == 0)
            {
                configAlternatePaths = new Dictionary<string, List<string>>
                {
                    { @"\\server05\library\inc materials", new List<string> { @"G:\Shared drives\INC Library\INC Viz\INC Material Maps" } },
                    { @"\\server05", new List<string> { @"\\incserver03vm" } }
                };
            }

            // Resolve actual subdirectories from archive roots with null checking and Directory.Exists checks
            List<string> archiveDirectories = new List<string>();
            foreach (string root in archiveRoots)
            {
                if (!string.IsNullOrEmpty(root))
                {
                    try
                    {
                        if (Directory.Exists(root))
                        {
                            archiveDirectories.AddRange(Directory.GetDirectories(root));
                        }
                    }
                    catch (Exception)
                    {
                        // Safely ignore individual access errors
                    }
                }
            }

            foreach (string possiblePath in uniqueFilePaths)
            {
                string fullPath = possiblePath.ToLower();
                if (fullPath != null && fullPath != String.Empty)
                {
                    // Check alternate paths
                    if(!File.Exists(fullPath) && !fullPath.Contains("|"))
                    {
                        Dictionary<string, List<string>> alternatePaths = new Dictionary<string, List<string>>();
                        if (configAlternatePaths != null)
                        {
                            foreach (KeyValuePair<string, List<string>> kvp in configAlternatePaths)
                            {
                                if (kvp.Key != null && kvp.Value != null)
                                {
                                    alternatePaths[kvp.Key] = kvp.Value.Select(v => v.ToLower()).ToList();
                                }
                            }
                        }

                        string separator = "bim";
                        int index = fullPath.IndexOf(separator, StringComparison.OrdinalIgnoreCase);
                        if (index >= 0)
                        {
                            index = index + separator.Length;
                            string projectRoot = fullPath.Substring(0, index);
                            string projectName = fullPath.Substring(index + 1);

                            index = projectName.IndexOf("\\", StringComparison.OrdinalIgnoreCase);
                            if (index >= 0)
                            {
                                projectName = projectName.Substring(0, index);
                                //projectName = projectName.Replace(" ", "");

                                if (projectName != null && projectName != String.Empty && !projectName.Contains("\\"))
                                {
                                    if (archiveDirectories != null && archiveDirectories.Count > 0)
                                    {
                                        List<string> testDirectories = new List<string>();
                                        foreach (string dir in archiveDirectories)
                                        {
                                            if (dir.ToLower().Contains(projectName.ToLower().Replace(" ", "")))
                                            {
                                                testDirectories.Add(Path.Combine(dir, "2 Design").ToLower());
                                                testDirectories.Add(Path.Combine(dir, "B_Design").ToLower());
                                                testDirectories.Add(Path.Combine(dir, "B Design").ToLower());
                                                testDirectories.Add(Path.Combine(dir, "2_Design").ToLower());

                                            }
                                        }
                                        if (testDirectories.Count > 0)
                                        {
                                            string replaceKey = Path.Combine(projectRoot, projectName);
                                            alternatePaths.Add(replaceKey.ToLower(), testDirectories);
                                        }
                                    }
                                }
                            }
                        }

                        foreach (KeyValuePair<string, List<string>> alternate in alternatePaths)
                        {
                            string pattern = alternate.Key;
                            foreach (string replacement in alternate.Value)
                            {
                                string testPath = fullPath.Replace(pattern.ToLower(), replacement.ToLower());
                                if (File.Exists(testPath))
                                {
                                    fullPath = testPath;
                                    break;
                                }
                                string ext = Path.GetExtension(testPath);
                                testPath = testPath.Replace(ext, "webp");
                                if (File.Exists(testPath))
                                {
                                    fullPath = testPath;
                                    break;
                                }
                            }
                        }
                           
                    }
                    if (//Uri.IsWellFormedUriString(fullPath, UriKind.RelativeOrAbsolute) && 
                        File.Exists(fullPath))

                    {
                        Dictionary<string, string> file = new Dictionary<string, string>();

                        string fileName = Path.GetFileName(fullPath);
                        string? directory = Path.GetDirectoryName(fullPath);
                        string partialPath = String.Empty;

                        if (rootNames != null && rootNames.Count > 0)
                        {
                            partialPath = SeparatePath(directory, rootNames);
                        }

                        string existPath = fullPath;
                        string newPath = Path.Combine(newPathRoot + partialPath, fileName);

                        file.Add("Existing Path", existPath);
                        file.Add("New Path", newPath);

                        try
                        {
                            string? dirName = Path.GetDirectoryName(newPath);
                            if(!string.IsNullOrEmpty(dirName) && !Directory.Exists(dirName))
                            {
                                Directory.CreateDirectory(dirName);
                            }

                            File.Copy(existPath, newPath, overwrite);
                            if (!filesCopied.ContainsKey(fileName))
                            {
                                filesCopied.Add(fileName, file);
                            }
                            else
                            {
                                Dictionary<string, string> tempFile = (Dictionary<string, string>) filesCopied[fileName];
                                tempFile["Existing Path"] = tempFile["Existing Path"] + " | " + existPath;
                                tempFile["New Path"] = tempFile["New Path"] + " | " + newPath;
                                filesCopied[fileName] = tempFile;
                            }
                        }
                        catch (Exception e)
                        {
                            if (File.Exists(newPath) && !overwrite)
                            {
                                file.Add("Error", "File already exists and overwriting is set to false." + e.Message);
                            }
                            else
                            {
                                file.Add("Error", "There was a problem copying the file. " + e.Message);
                            }

                            if (!filesNotCopied.ContainsKey(fileName))
                            {
                                filesNotCopied.Add(fileName, file);
                            }
                            else
                            {
                                Dictionary<string, string> tempFile = (Dictionary<string, string>)filesNotCopied[fileName];
                                tempFile["Existing Path"] = tempFile["Existing Path"] + " | " + existPath;
                                tempFile["New Path"] = tempFile["New Path"] + " | " + newPath;
                                filesNotCopied[fileName] = tempFile;
                            }
                        }
                    }
                    else
                    {
                        if (!File.Exists(fullPath))
                        {
                            Dictionary<string, string> file = new Dictionary<string, string>();
                            if(fullPath.Contains("|"))
                            {
                                fullPath = fullPath.Split('|').FirstOrDefault() ?? string.Empty;
                            }
                            string fileName = Path.GetFileName(fullPath);
                            file.Add("Existing Path", fullPath);
                            file.Add("Error", "File does not exist at this location.");

                            if (!filesNotCopied.ContainsKey(fileName))
                            {
                                filesNotCopied.Add(fileName, file);
                            }
                            else
                            {
                                Dictionary<string, string> tempFile = (Dictionary<string, string>)filesNotCopied[fileName];
                                tempFile["Existing Path"] = tempFile["Existing Path"] + " | " + fullPath;
                                tempFile["Error"] = tempFile["Error"] + " | " + "File does not exist at this location.";
                                filesNotCopied[fileName] = tempFile;
                            }
                        }
                        else 
                        {
                            if (!filesNotCopied.ContainsKey(fullPath))
                            {
                                Dictionary<string, string> file = new Dictionary<string, string>();
                                file.Add("Existing Path", fullPath);
                                file.Add("Error", "Unkown problem.");

                                filesNotCopied.Add(fullPath, file);
                            }
                        }
                    }
                }
            }

            return new Dictionary<string, Dictionary<string, object>>
            {
                {"Files Copied", filesCopied },
                {"Files Not Copied", filesNotCopied }
            };
        }

        /// <summary>
        /// Given a file path, returns the partial path after one of the root names.  If a root isn't found, it returns null.
        /// </summary>
        /// <param name="filePath">A string of the filepath</param>
        /// <param name="rootNames">Names of directories to separate </param>
        /// <returns></returns>
        public static string SeparatePath (string? filePath, List<string> rootNames)
        {
            if (filePath == null) return String.Empty;
            int index = -1;
            string partialPath = String.Empty;
            foreach (string root in rootNames)
            {
                int tempIndex = filePath.LastIndexOf(root, StringComparison.OrdinalIgnoreCase);
                if (tempIndex != -1)
                {
                    index = tempIndex + root.Length;
                    break;
                }
            }
            if (index != -1)
            {
                partialPath = filePath.Substring(index);
            }
            return partialPath;
        }
    }
}
```

### File: Infrastructure/IO/SearchPaths.cs
```csharp
using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Synthetic.Modules.MergeDuplicates.Handlers;
using Synthetic.Modules.RevitDOM;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.Infrastructure.IO;

namespace Synthetic.Infrastructure.IO
{
    /// <summary>
    /// SearchPaths are utility functions that search recursively through a list of paths for a list of files and provides the paths of the found files and a list of files not found.
    /// </summary>
    public class SearchPaths
    {
        private List<string> paths;
        private Dictionary<string, string> fileLibrary;

        /// <summary>
        /// List of Paths to be searched.
        /// </summary> 
        public List<string> Paths
        {
            get { return this.paths; }
            set
            {
                this.paths = value;
                this.fileLibrary = this._GetFileLibrary(value);
            }
        }

        /// <summary>
        /// List of files in the search path.  Only one unique file name in all the paths is included.
        /// </summary>
        public Dictionary<string, string> FileLibrary { get { return fileLibrary; } }

        /// <summary>
        /// Creates a new SearchPaths object without any paths or a file library.
        /// </summary>
        public SearchPaths() 
        {
            this.paths = new List<string>();
            this.fileLibrary = new Dictionary<string, string>();
        }

        /// <summary>
        /// Creates a new search path object with a library of all the unique files and their paths.  Only the first instance of a filename is included so the order of the Paths give priority.
        /// </summary>
        /// <param name="Paths">List of Paths to be searched.</param>
        public SearchPaths(List<string> Paths)
        {
            this.paths = Paths;
            this.fileLibrary = this._GetFileLibrary(this.paths);
        }

        /// <summary>
        /// Adds additional paths and files to an existing SearchPaths object.
        /// </summary>
        /// <param name="paths">A List of paths</param>
        /// <returns>This SearchPath object</returns>
        public SearchPaths AddPaths(List<string> paths)
        {
            List<string> newPaths = new List<string>();
            foreach(string path in paths)
            {
                if(!this.Paths.Contains(path))
                {
                    this.Paths.Add(path);
                }
            }
            this.fileLibrary = _AddFileLibrary(newPaths, this.fileLibrary);
            return this;
        }

        /// <summary>
        /// Searches the paths for a file and returns if path if found, otherwise returns null.
        /// </summary>
        /// <param name="File">Name of the file to search for.</param>
        /// <returns name="FilePath">A string of the file path if found.  If the file is not found, it returns null.</returns>
        public string? GetFilePath(string File)
        {
            if (File != null && this.fileLibrary.ContainsKey(File))
                return this.fileLibrary[File];
            else
                return null;
        }

        /// <summary>
        /// Searches the paths for a file and returns a relative path if found, otherwise returns null.
        /// </summary>
        /// <param name="File">A string that is the path to the a file in the search paths</param>
        /// <returns name="RelativePath">A string of the relative file path if found.  If the file is not found, it returns null.  The relative path removes the search path from the FilePath</returns>
        public string? GetRelativeFilePath(string File)
        {
            string? relativePath = null;

            if (File != null && this.fileLibrary.ContainsKey(File))
            {
                string FilePath = this.fileLibrary[File];

                foreach (string path in this.Paths)
                {
                    if (FilePath.Contains(path))
                    {
                        relativePath = FilePath.Replace(path, "");
                    }
                }
            }
            return relativePath;
        }

        /// <summary>
        /// Checks if the FileLibrary contains the file.
        /// </summary>
        /// <param name="File">A string of the file name.</param>
        /// <returns name="bool">Returns true if the file is in the File Library, false if it does not.</returns>
        public bool ContainsFile(string File)
        {
            if (File != null && this.fileLibrary.ContainsKey(File))
                return true;
            else
                return false;
        }

        /// <summary>
        /// Checks if the given path is a search path.
        /// </summary>
        /// <param name="Path">A string of the path</param>
        /// <returns name="bool">Returns True if the path is a search path and false if it is not.</returns>
        public bool ContainsPath(string Path)
        {
            if (Path != null && this.Paths.Contains(Path))
                return true;
            else
                return false;
        }

        /// <summary>
        /// Copies the files given that are found in the Search Path File Library to a new location.
        /// </summary>
        /// <param name="Files">A list of file names to copy</param>
        /// <param name="Path">The root path to copy files into</param>
        /// <param name="Overwrite">If True, the method will overwrite any files if they already exist. If false, only new files will be copied.</param>
        /// <returns name="Copied?">If true, the file was copied, otherwise it was not because it didn't exist or there was an trouble copying the file.</returns>
        /// <returns name="Files Copied">A list of the file names copied</returns>
        /// <returns name="Files Not Copied">A list of the file names that were not able to be copied</returns>
        public IDictionary CopyFiles(List<string> Files, string Path, bool Overwrite = false)
        {
            bool result;
            List<bool> resultsBool = new List<bool>();
            List<string> filesCopied = new List<string>();
            List<string> filesNotCopied = new List<string>();


            foreach (string file in Files)
            {
                result = false;
                if (this.fileLibrary.ContainsKey(file))
                {
                    string? relativeFilePath = GetRelativeFilePath(file);
                    string? relativePath = relativeFilePath != null ? System.IO.Path.GetDirectoryName(relativeFilePath) : null;
                    string newPath = Path + (relativePath ?? string.Empty);
                    string newFilePath = newPath + "\\" + file;

                    bool newPathExists = Directory.Exists(newPath);

                    if (!newPathExists)
                    {
                        Directory.CreateDirectory(newPath);
                        newPathExists = Directory.Exists(newPath);
                    }
                    if (newPathExists)
                    {
                        bool newFileExists = File.Exists(newFilePath);
                        if (!newFileExists || (newFileExists && Overwrite))
                        {

                            File.Copy(this.FileLibrary[file], newFilePath, Overwrite);
                            newFileExists = File.Exists(newFilePath);

                            if (newFileExists)
                            {
                                result = true;
                                resultsBool.Add(result);
                                filesCopied.Add(file);
                            }
                        }
                    }
                }
                if (result == false)
                {
                    resultsBool.Add(result);
                    filesNotCopied.Add(file);
                }
            }
            return new Dictionary<string, object>
            {
                {"Copied?", resultsBool},
                {"Files Copied", filesCopied },
                {"Files Not Copied", filesNotCopied }
            };
        }

        /// <summary>
        /// Gets all the files in the search paths.  If more than one file has the same name, then only the first file found will be included.  This gives priority to paths listed first in the SearchPaths.
        /// </summary>
        /// <returns>A Dictionary with the file name as the key and the path as the value.</returns>
        private Dictionary<string, string> _GetFileLibrary(List<string> Paths)
        {
            Dictionary<string, string> files = new Dictionary<string, string>();

            foreach (string path in Paths)
            {

                if (Directory.Exists(path))
                {
                    List<string> filesAll = Directory.GetFiles(path, "*.*", SearchOption.AllDirectories).ToList();

                    foreach (string filepath in filesAll)
                    {
                        if (!files.ContainsKey(Path.GetFileName(filepath)))
                        {
                            files.Add(Path.GetFileName(filepath), filepath);
                        }
                    }
                }
            }
            return files;
        }

        /// <summary>
        /// Gets all the files in the search path and if they have a unique name, adds them to the fileLibrary Dictionary
        /// </summary>
        /// <param name="Paths">List of paths</param>
        /// <param name="fileLibrary">A Dictionary object with the filename as the key and the full path as the value.</param>
        /// <returns>An updated Dictionary object with the filename as the key and the full path as the value.</returns>
        private Dictionary<string, string> _AddFileLibrary(List<string> Paths, Dictionary<string, string> fileLibrary)
        {
            foreach (string path in Paths)
            {

                if (Directory.Exists(path))
                {
                    List<string> filesAll = Directory.GetFiles(path, "*.*", SearchOption.AllDirectories).ToList();

                    foreach (string filepath in filesAll)
                    {
                        string fileName = Path.GetFileName(filepath);
                        if (!fileLibrary.ContainsKey(fileName))
                        {
                            fileLibrary.Add(fileName, filepath);
                        }
                    }
                }
            }
            return fileLibrary;
        }

        /// <summary>
        /// Prints the Searchpaths as a string including all the files in the library.
        /// </summary>
        /// <returns name="string">Converts to a string.</returns>
        public override string ToString()
        {
            Type t = typeof(SearchPaths);

            string s = t.Namespace + "." + GetType().Name;

            // Disabled printing the contents of the File Library becuase it was taking a very long time given the number of files.
            //int i = 0;
            //foreach (KeyValuePair<string, string> file in this.fileLibrary)
            //{
            //    s = s + string.Format("\n  {0} file-> \"{1}\" path-> \"{2}\"", i, file.Key, file.Value);
            //    i++;
            //}
            return s;
        }
    }
}
```

### File: Infrastructure/Persistence/LegacySettingsMigration.cs
```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;

using Synthetic.Infrastructure.Persistence;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.Modules.RevitDOM;
namespace Synthetic.Infrastructure.Persistence{
    /// <summary>
    /// Utility class to find and purge legacy settings storage schemas from the Revit document database.
    /// </summary>
    public static class LegacySettingsMigration
    {
        private static readonly Guid LegacyStorageIdGuid = new Guid("0b5fd0ef-3558-47d2-81b5-d1918952d2b1");
        private static readonly Guid LegacyConfigSettingsGuid = new Guid("5855a6d8-e694-46e5-bd71-c227a305143b");

        private static Schema GetLegacyStorageIdSchema()
        {
            Schema schema = Schema.Lookup(LegacyStorageIdGuid);
            if (schema != null) return schema;

            SchemaBuilder builder = new SchemaBuilder(LegacyStorageIdGuid);
            builder.SetReadAccessLevel(AccessLevel.Public);
            builder.SetWriteAccessLevel(AccessLevel.Public);
            builder.SetSchemaName("SyntheticStorageId");
            builder.AddSimpleField("Id", typeof(Guid));
            return builder.Finish();
        }

        private static Schema GetLegacyConfigSettingsSchema()
        {
            Schema schema = Schema.Lookup(LegacyConfigSettingsGuid);
            if (schema != null) return schema;

            SchemaBuilder builder = new SchemaBuilder(LegacyConfigSettingsGuid);
            builder.SetReadAccessLevel(AccessLevel.Public);
            builder.SetWriteAccessLevel(AccessLevel.Public);
            builder.SetSchemaName("SyntheticConfig");
            builder.AddSimpleField("Settings", typeof(string));
            return builder.Finish();
        }

        /// <summary>
        /// Scans the document for DataStorage elements matching the legacy GUIDs and purges them.
        /// </summary>
        /// <param name="doc">The active Revit document.</param>
        public static void PurgeLegacySchemas(Document doc)
        {
            if (doc == null) return;

            try
            {
                Schema legacyStorageIdSchema = GetLegacyStorageIdSchema();
                Schema legacyConfigSchema = GetLegacyConfigSettingsSchema();

                var dataStorages = new FilteredElementCollector(doc)
                    .OfClass(typeof(DataStorage))
                    .Cast<DataStorage>()
                    .ToList();

                var elementsToDelete = new List<ElementId>();

                foreach (var dataStorage in dataStorages)
                {
                    try
                    {
                        if (dataStorage.Name.Equals("Synthetic_Materials", StringComparison.OrdinalIgnoreCase))
                        {
                            elementsToDelete.Add(dataStorage.Id);
                            continue;
                        }

                        Entity idEntity = dataStorage.GetEntity(legacyStorageIdSchema);
                        if (idEntity != null && idEntity.IsValid())
                        {
                            elementsToDelete.Add(dataStorage.Id);
                            continue;
                        }

                        Entity configEntity = dataStorage.GetEntity(legacyConfigSchema);
                        if (configEntity != null && configEntity.IsValid())
                        {
                            elementsToDelete.Add(dataStorage.Id);
                        }
                    }
                    catch (Exception)
                    {
                        // Safely catch extensible storage exceptions
                    }
                }

                if (elementsToDelete.Count > 0)
                {
                    using (Transaction trans = new Transaction(doc, "Purge Legacy Synthetic Settings"))
                    {
                        trans.Start();
                        foreach (var id in elementsToDelete)
                        {
                            try
                            {
                                doc.Delete(id);
                            }
                            catch (Exception)
                            {
                                // Safely handle element deletion errors
                            }
                        }
                        trans.Commit();
                    }
                }
            }
            catch (Exception)
            {
                // General safety fallback
            }
        }
    }
}
```

### File: Infrastructure/Persistence/SettingsManager.cs
```csharp
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
using Synthetic.Modules.RevitDOM;
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
```

### File: Infrastructure/Persistence/SyntheticSettingsJsonSchema.cs
```csharp
using System;
using Autodesk.Revit.DB.ExtensibleStorage;

using Synthetic.Infrastructure.Persistence;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.Modules.RevitDOM;
namespace Synthetic.Infrastructure.Persistence{
    /// <summary>
    /// Schema definition for storing settings modules as serialized JSON string payloads inside Revit extensible storage.
    /// </summary>
    public static class SyntheticSettingsJsonSchema
    {
        private static readonly Guid SchemaGuid = new Guid("8a07153a-c85c-444f-9e79-50c117d91e60");
        /// <summary>
        /// The name of the settings schema.
        /// </summary>
        public const string SchemaName = "SyntheticSettingsJson";

        /// <summary>
        /// Description of the settings schema.
        /// </summary>
        public const string SchemaDocumentation = "Universal JSON payload storage for Synthetic settings.";

        /// <summary>
        /// The name of the ModuleKey field.
        /// </summary>
        public const string ModuleKeyFieldName = "ModuleKey";

        /// <summary>
        /// The name of the JsonData field.
        /// </summary>
        public const string JsonDataFieldName = "JsonData";

        /// <summary>
        /// Retrieves the existing settings schema or constructs and registers it if missing.
        /// </summary>
        /// <returns>The registered Autodesk.Revit.DB.ExtensibleStorage.Schema.</returns>
        public static Schema GetSchema()
        {
            Schema schema = Schema.Lookup(SchemaGuid);
            if (schema != null)
            {
                return schema;
            }

            SchemaBuilder schemaBuilder = new SchemaBuilder(SchemaGuid);
            schemaBuilder.SetReadAccessLevel(AccessLevel.Public);
            schemaBuilder.SetWriteAccessLevel(AccessLevel.Public);
            schemaBuilder.SetSchemaName(SchemaName);
            schemaBuilder.SetDocumentation(SchemaDocumentation);

            schemaBuilder.AddSimpleField(ModuleKeyFieldName, typeof(string));
            schemaBuilder.AddSimpleField(JsonDataFieldName, typeof(string));

            return schemaBuilder.Finish();
        }
    }
}
```

### File: Infrastructure/Serialization/Json.cs
```csharp
using System;
using System.Collections.Generic;
using j = Newtonsoft.Json;

using Synthetic.Infrastructure.Serialization;
using Synthetic.Modules.RevitDOM;
using Synthetic.Modules.StandardsManagement.ViewModels;
namespace Synthetic.Infrastructure.Serialization{
    /// <summary>
    /// Convert Revit Elements to and from JSON.
    /// </summary>
    public class Json
    {
        internal Json() { }

        /// <summary>
        /// Serializes an element into JSON.
        /// </summary>
        /// <param name="object">An object to serialize.</param>
        /// <returns name="JSON">A string of JSON.</returns>
        public static string Encode(System.Object @object)
        {
            return j.JsonConvert.SerializeObject(@object, j.Formatting.Indented);
        }

        /// <summary>
        /// Serializes an object to JSON string with no indentation (minimal formatting).
        /// </summary>
        /// <param name="object">The object to serialize.</param>
        /// <returns>A JSON string representation of the object.</returns>
        public static string EncodeMinimal(System.Object @object)
        {
            return j.JsonConvert.SerializeObject(@object, j.Formatting.None);
        }

        /// <summary>
        /// Deserializes an element from JSON.
        /// </summary>
        /// <param name="json">A string of JSON</param>
        /// <returns>An object to deserialize.</returns>
        public static System.Object? Decode(string json)
        {
            return j.JsonConvert.DeserializeObject(json);
        }

        /// <summary>
        /// Converts a list of objects to an indented JSON string.
        /// </summary>
        /// <param name="ListJSON">The list of objects to serialize.</param>
        /// <returns>An indented JSON string.</returns>
        public static string ListToJSON(List<System.Object> ListJSON)
        {
            return j.JsonConvert.SerializeObject(ListJSON, j.Formatting.Indented);
        }

        /// <summary>
        /// Deserializes a JSON string into a general object list structure.
        /// </summary>
        /// <param name="JSON">The JSON string representing list data.</param>
        /// <returns>A deserialized object structure.</returns>
        public static object? JsonToList(string JSON)
        {
            return j.JsonConvert.DeserializeObject(JSON);
        }
    }
}
```

### File: Settings/Config.cs
```csharp
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
```

### File: Settings/ConfigCollection.cs
```csharp
using Autodesk.Revit.DB.Events;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Text;
using RevitDoc = Autodesk.Revit.DB.Document;

using Synthetic.Shared.RevitAPI;
using Synthetic.Settings;
using Synthetic.Modules.RevitDOM;
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
```

### File: Settings/FileUtilitySettings.cs
```csharp
using Newtonsoft.Json;
using System.IO;
using System.Collections.Generic;
using Autodesk.Revit.DB;

using Synthetic.Settings;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.Modules.RevitDOM;
namespace Synthetic.Settings
{
    /// <summary>
    /// Configuration settings for file utility operations, including archive and alternate paths.
    /// </summary>
    public class FileUtilitySettings : ISettingModule
    {
        /// <summary>
        /// Gets the settings module key.
        /// </summary>
        [JsonIgnore]
        public string ModuleKey => Name;

        /// <summary>
        /// The name of this settings module key.
        /// </summary>
        [JsonIgnore]
        public const string Name = "FileUtility";

        /// <summary>
        /// Gets or sets the list of archive directories.
        /// </summary>
        public List<string> ArchiveDirectories { get; set; }

        /// <summary>
        /// Gets or sets the dictionary of alternate paths and their replacements.
        /// </summary>
        public Dictionary<string, List<string>> AlternatePaths { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="FileUtilitySettings"/> class.
        /// </summary>
        public FileUtilitySettings()
        {
            ArchiveDirectories = new List<string>();
            AlternatePaths = new Dictionary<string, List<string>>();
        }

        /// <summary>
        /// Populates default values.
        /// </summary>
        /// <returns>This settings instance.</returns>
        public FileUtilitySettings Defaults()
        {
            ArchiveDirectories = new List<string> { @"\\NAS04\Archive" };
            AlternatePaths = new Dictionary<string, List<string>>
            {
                { @"\\server05\library\inc materials", new List<string> { @"G:\Shared drives\INC Library\INC Viz\INC Material Maps" } },
                { @"\\server05", new List<string> { @"\\incserver03vm" } }
            };
            return this;
        }

        /// <summary>
        /// Validates settings module.
        /// </summary>
        public bool IsValid(Document doc)
        {
            return true;
        }
    }
}
```

### File: Settings/ISettingModule.cs
```csharp
using Autodesk.Revit.DB;

using Synthetic.Settings;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.Modules.RevitDOM;
namespace Synthetic.Settings
{
    /// <summary>
    /// Represents a settings module that can be serialized/deserialized and validated.
    /// </summary>
    public interface ISettingModule
    {
        /// <summary>
        /// The unique key identifying the settings module.
        /// </summary>
        string ModuleKey { get; }

        /// <summary>
        /// Validates the settings module settings within the context of the active document.
        /// </summary>
        /// <param name="doc">The Revit Document context.</param>
        /// <returns>True if settings are valid; otherwise, false.</returns>
        bool IsValid(Document doc);
    }
}
```

### File: Settings/MaterialLibrarySettings.cs
```csharp
using Newtonsoft.Json;
using System.IO;
using Autodesk.Revit.DB;

using Synthetic.Settings;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.Modules.RevitDOM;
namespace Synthetic.Settings
{
    /// <summary>
    /// Configuration settings for the material library path.
    /// </summary>
    public class MaterialLibrarySettings : ISettingModule
    {
        /// <summary>
        /// Gets the settings module key.
        /// </summary>
        [JsonIgnore]
        public string ModuleKey => Name;

        /// <summary>
        /// The name of this settings module key.
        /// </summary>
        [JsonIgnore]
        public const string Name = "MaterialLibrary";

        /// <summary>
        /// Gets or sets the folder path to the material library.
        /// </summary>
        public string LibraryFolderPath { get; set; } = string.Empty;

        /// <summary>
        /// Initializes a new instance of the <see cref="MaterialLibrarySettings"/> class.
        /// </summary>
        public MaterialLibrarySettings() { }

        /// <summary>
        /// Populates default values.
        /// </summary>
        /// <returns>This settings instance.</returns>
        public MaterialLibrarySettings Defaults()
        {
            LibraryFolderPath = "C:\\Materials\\";
            return this;
        }

        /// <summary>
        /// Validates that the folder path exists.
        /// </summary>
        public bool IsValid(Document doc)
        {
            return !string.IsNullOrEmpty(LibraryFolderPath) && Directory.Exists(LibraryFolderPath);
        }
    }
}
```

### File: Settings/ProjectMaterialSettings.cs
```csharp
using Newtonsoft.Json;
using System;
using System.IO;
using Autodesk.Revit.DB;

using Synthetic.Settings;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.Modules.RevitDOM;
namespace Synthetic.Settings
{
    /// <summary>
    /// Configuration settings for project-specific material paths.
    /// </summary>
    public class ProjectMaterialSettings : ISettingModule
    {
        /// <summary>
        /// Gets the settings module key.
        /// </summary>
        [JsonIgnore]
        public string ModuleKey => Name;

        /// <summary>
        /// The name of this settings module key.
        /// </summary>
        [JsonIgnore]
        public const string Name = "ProjectMaterials";

        /// <summary>
        /// Gets or sets the default relative path.
        /// </summary>
        public string DefaultRelativePath { get; set; } = ".\\Materials\\";

        /// <summary>
        /// Gets or sets the override folder path.
        /// </summary>
        public string OverrideFolderPath { get; set; } = "";

        /// <summary>
        /// Initializes a new instance of the <see cref="ProjectMaterialSettings"/> class.
        /// </summary>
        public ProjectMaterialSettings() { }

        /// <summary>
        /// Populates default values.
        /// </summary>
        /// <returns>This settings instance.</returns>
        public ProjectMaterialSettings Defaults()
        {
            DefaultRelativePath = ".\\Materials\\";
            OverrideFolderPath = "";
            return this;
        }

        /// <summary>
        /// Resolves the absolute directory path of the materials.
        /// </summary>
        /// <param name="doc">The active Revit document.</param>
        /// <returns>The resolved absolute folder path, or null.</returns>
        public string? GetResolvedPath(Document doc)
        {
            // The Override Check (Highest Priority)
            if (!string.IsNullOrEmpty(OverrideFolderPath))
            {
                return OverrideFolderPath;
            }

            // The Null Check
            if (doc == null || !doc.IsValidObject)
            {
                return null;
            }

            // The Cloud Intercept (Crucial)
            if (doc.IsModelInCloud)
            {
                return null;
            }

            // The Unsaved File Check
            if (string.IsNullOrEmpty(doc.PathName))
            {
                return null;
            }

            string? basePath = null;

            // The Workshared Check
            if (doc.IsWorkshared)
            {
                try
                {
                    ModelPath centralModelPath = doc.GetWorksharingCentralModelPath();
                    if (centralModelPath != null)
                    {
                        basePath = ModelPathUtils.ConvertModelPathToUserVisiblePath(centralModelPath);
                    }
                }
                catch
                {
                    // Fallback
                }
            }

            // The Local Fallback
            if (string.IsNullOrEmpty(basePath))
            {
                basePath = doc.PathName;
            }

            if (string.IsNullOrEmpty(basePath))
            {
                return null;
            }

            // The Path Construction
            try
            {
                string? dir = Path.GetDirectoryName(basePath);
                if (string.IsNullOrEmpty(dir)) return null;

                string relative = DefaultRelativePath ?? ".\\Materials\\";
                return Path.GetFullPath(Path.Combine(dir, relative));
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Validates that the resolved folder path exists.
        /// </summary>
        public bool IsValid(Document doc)
        {
            string? path = GetResolvedPath(doc);
            return !string.IsNullOrEmpty(path) && Directory.Exists(path);
        }
    }
}
```

### File: Settings/StandardsSettings.cs
```csharp
using Newtonsoft.Json;
using System;
using System.IO;
using Autodesk.Revit.DB;

using Synthetic.Settings;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.Modules.RevitDOM;
namespace Synthetic.Settings
{
    /// <summary>
    /// Configuration settings for the standards workflow, storing the path to the standards JSON file.
    /// This module is serialized and saved inside the Revit Document's Extensible Storage schema
    /// to persist the firmwide or project-specific standards path across user sessions.
    /// </summary>
    public class StandardsSettings : ISettingModule
    {
        /// <summary>
        /// Gets the settings module key.
        /// </summary>
        [JsonIgnore]
        public string ModuleKey => Name;

        /// <summary>
        /// The name of this settings module key.
        /// </summary>
        [JsonIgnore]
        public const string Name = "Standards";

        /// <summary>
        /// Gets or sets the path to the standards JSON file.
        /// </summary>
        public string StandardsFilePath { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="StandardsSettings"/> class.
        /// </summary>
        public StandardsSettings()
        {
            StandardsFilePath = string.Empty;
        }

        /// <summary>
        /// Validates settings module.
        /// </summary>
        public bool IsValid(Document doc)
        {
            if (string.IsNullOrEmpty(StandardsFilePath)) return false;
            try
            {
                return Path.GetExtension(StandardsFilePath).Equals(".json", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }
    }
}
```

### File: Settings/SyncSettings.cs
```csharp
using System;
using System.IO;
using Autodesk.Revit.DB;
using Newtonsoft.Json;

using Synthetic.Settings;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.Modules.RevitDOM;
namespace Synthetic.Settings
{
    /// <summary>
    /// Configuration settings for project configuration synchronization.
    /// </summary>
    public class SyncSettings : ISettingModule
    {
        /// <summary>
        /// The setting configuration name.
        /// </summary>
        [JsonIgnore]
        public const string Name = "SyncSettings";

        /// <summary>
        /// Gets the settings module key.
        /// </summary>
        [JsonIgnore]
        public string ModuleKey => Name;

        /// <summary>
        /// Gets or sets the path to the linked external settings JSON file.
        /// </summary>
        public string LinkedFilePath { get; set; } = string.Empty;

        /// <summary>
        /// Initializes a new instance of the <see cref="SyncSettings"/> class.
        /// </summary>
        public SyncSettings() { }

        /// <summary>
        /// Validates the settings module within the document context.
        /// </summary>
        /// <param name="doc">The Revit Document.</param>
        /// <returns>True if settings are valid; otherwise, false.</returns>
        public bool IsValid(Document doc)
        {
            return string.IsNullOrEmpty(LinkedFilePath) || File.Exists(LinkedFilePath);
        }
    }
}
```

### File: Settings/ViewAutoNumSettings.cs
```csharp
using Autodesk.Revit.DB;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

//Aliases for Revit Classes
using RevitDoc = Autodesk.Revit.DB.Document;

using Synthetic.Settings;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.Modules.RevitDOM;
using Synthetic.Shared.RevitAPI;
namespace Synthetic.Settings
{
    /// <summary>
    /// Settings for Autonumbering views.
    /// </summary>
    public class ViewAutoNumSettings : ISettingModule
    {
        /// <summary>
        /// Gets the settings module key.
        /// </summary>
        [JsonIgnore]
        public string ModuleKey { get { return Name; } }

        internal string defaultViewAutoNumFamily { get { return "Titleblock Grid Location Marker"; } }
        internal string defaultViewAutoNumFamilyType { get { return "Location Marker INC Titleblock - CD"; } }
        internal string defaultViewAutoNumXGridName { get { return "Grid Size X Direction"; } }
        internal string defaultViewAutoNumYGridName { get { return "Grid Size Y Direction"; } }

        /// <summary>
        /// The setting configuration name.
        /// </summary>
        [JsonIgnore]
        public const string Name = "ViewAutoNumber";

        /// <summary>
        /// Gets or sets the family name used for view autonumbering.
        /// </summary>
        [JsonProperty("viewAutoNumFamily")]
        public string ViewAutoNumFamily { get; set; } = "Titleblock Grid Location Marker";

        /// <summary>
        /// Gets or sets the family type name used for view autonumbering.
        /// </summary>
        [JsonProperty("viewAutoNumFamilyType")]
        public string ViewAutoNumFamilyType { get; set; } = "Location Marker INC Titleblock - CD";

        /// <summary>
        /// Gets or sets the X grid spacing parameter name.
        /// </summary>
        [JsonProperty("viewAutoNumXGridName")]
        public string ViewAutoNumXGridName { get; set; } = "Grid Size X Direction";

        /// <summary>
        /// Gets or sets the Y grid spacing parameter name.
        /// </summary>
        [JsonProperty("viewAutoNumYGridName")]
        public string ViewAutoNumYGridName { get; set; } = "Grid Size Y Direction";

        /// <summary>
        /// Constructor
        /// </summary>
        public ViewAutoNumSettings() { }

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="family">Name of family to serve as the origin</param>
        /// <param name="familytype">Name of the family type</param>
        /// <param name="xGridName">Name of the parameter to determine the X grid spacing</param>
        /// <param name="yGridName">Name of the parameter to determine the Y grid spacing</param>
        public ViewAutoNumSettings(string family, string familytype, string xGridName, string yGridName)
        {
            this.ViewAutoNumFamily = family;
            this.ViewAutoNumFamilyType = familytype;
            this.ViewAutoNumXGridName = xGridName;
            this.ViewAutoNumYGridName = yGridName;
        }

        /// <summary>
        /// Reset the ViewAutoNumSettings properties to the Defaults
        /// </summary>
        /// <returns>The ViewRenumberSettings to allow for chaining</returns>
        public ViewAutoNumSettings Defaults()
        {
            this.ViewAutoNumFamily = defaultViewAutoNumFamily;
            this.ViewAutoNumFamilyType = defaultViewAutoNumFamilyType;
            this.ViewAutoNumXGridName = defaultViewAutoNumXGridName;
            this.ViewAutoNumYGridName = defaultViewAutoNumYGridName;

            return this;
        }

        /// <summary>
        /// Checks if all the setting values exist and the family and type are loaded in the project.
        /// </summary>
        /// <param name="doc">The Revit Document context.</param>
        /// <returns>True if settings are valid; otherwise, false.</returns>
        public bool IsValid (Document doc)
        {
            if (string.IsNullOrEmpty(this.ViewAutoNumFamily) ||
                string.IsNullOrEmpty(this.ViewAutoNumFamilyType) ||
                string.IsNullOrEmpty(this.ViewAutoNumXGridName) ||
                string.IsNullOrEmpty(this.ViewAutoNumYGridName))
            {
                return false;
            }

            try
            {
                var symbol = new FilteredElementCollector(doc)
                    .OfClass(typeof(FamilySymbol))
                    .Cast<FamilySymbol>()
                    .FirstOrDefault(fs => fs.Name.Equals(this.ViewAutoNumFamilyType, StringComparison.OrdinalIgnoreCase) &&
                                          fs.Family.Name.Equals(this.ViewAutoNumFamily, StringComparison.OrdinalIgnoreCase));
                return symbol != null;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
```

### File: Settings/WorksetSettings.cs
```csharp
using System;
using System.IO;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json;
using Synthetic.Infrastructure.Serialization;
using Synthetic.Infrastructure.IO;

//Aliases for Revit Classes
using RevitDoc = Autodesk.Revit.DB.Document;
using Autodesk.Revit.DB;

using Synthetic.Core;
using Synthetic.Settings;
using Synthetic.Modules.Worksets.Utilities;
using Synthetic.Modules.RevitDOM;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.Shared.RevitAPI;
namespace Synthetic.Settings
{
    /// <summary>
    /// Configuration settings for worksets, including excel mapping files and group configurations.
    /// </summary>
    public class WorksetSettings : ISettingModule
    {
        /// <summary>
        /// Gets the settings module key.
        /// </summary>
        [JsonIgnore]
        public string ModuleKey { get { return Name; } }

        internal string defaultWorksetFile { get { return "SyntheticWorksets.xlsx"; } }
        internal string defaultWorksetGroup { get { return "Worksets"; } }

        /// <summary>
        /// Name of the WorksetSettings type
        /// </summary>
        [JsonIgnore]
        public const string Name = "Worksets";

        /// <summary>
        /// Gets or sets the workset configuration Excel filename.
        /// </summary>
        public string? WorksetFile { get; set; }

        /// <summary>
        /// Gets or sets the path to the workset Excel file.
        /// </summary>
        public string? WorksetPath { get; set; }

        /// <summary>
        /// Gets or sets the workset group name.
        /// </summary>
        public string? WorksetGroup { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="WorksetSettings"/> class.
        /// </summary>
        public WorksetSettings () { }

        /// <summary>
        /// Constructor using individual parameters to create
        /// </summary>
        /// <param name="file">File name as a string.</param>
        /// <param name="path">Path to file as a string.  If null, app will use the assembly path instead.</param>
        /// <param name="group">Name of workset group as a string.</param>
        public WorksetSettings (string? file, string? path = null, string? group = null)
        {
            this.WorksetFile = file;
            this.WorksetPath = path;
            this.WorksetGroup = group;
        }

        /// <summary>
        /// App default WorksetSettings object
        /// </summary>
        /// <returns>WorksetSettings object with app default settings.</returns>
        public WorksetSettings Defaults ()
        {
            this.WorksetFile = defaultWorksetFile;
            this.WorksetPath = null;
            this.WorksetGroup = defaultWorksetGroup;

            return this;
        }

        /// <summary>
        /// If the WorksetPath setting is a path, return the path, otherwise return the addin's path.
        /// </summary>
        /// <returns>Path as a string</returns>
        public string PathOrDefault()
        {
            string? path = this.WorksetPath;
            return !string.IsNullOrEmpty(path) ? path! : Config.addinPath;
        }

        /// <summary>
        /// Takes the WorksetPath and WorksetFile to create a path to the file.  If the WorksetPath is empty, uses the addin's path.
        /// </summary>
        /// <returns>Full path to the file as a string</returns>
        public string? FullPath()
        {
            return !string.IsNullOrEmpty(this.WorksetFile) ? Path.Combine(this.PathOrDefault(), this.WorksetFile) : null;
        }

        /// <summary>
        /// Validates the settings module within the document context.
        /// </summary>
        /// <param name="doc">The Revit Document.</param>
        /// <returns>True if settings are valid; otherwise, false.</returns>
        public bool IsValid (Document doc)
        {
            return this.FileExists() && !string.IsNullOrEmpty(this.WorksetGroup);
        }

        /// <summary>
        /// Checks if the settings are empty (i.e. no WorksetFile is defined).
        /// </summary>
        /// <returns>True if WorksetFile is null or empty.</returns>
        public bool IsSettingsEmpty()
        {
            return string.IsNullOrEmpty(this.WorksetFile);
        }

        /// <summary>
        /// Checks if the Workset File exists at the path
        /// </summary>
        /// <returns>True if the Workset File exists at the path, false if the Settings aren't valid or the file doesn't exist at the path.</returns>
        public bool FileExists ()
        {
            string? path = this.FullPath();
            return path != null && File.Exists(path);
        }

        /// <summary>
        /// Loads workset configurations from the Excel file specified in the settings.
        /// </summary>
        /// <returns>A WorksetUtil utility class containing loaded workset configurations.</returns>
        public WorksetUtil? WorksetsByExcel ()
        {
            WorksetUtil? worksetUtil = null;
            if (this.FileExists())
            {
                string? path = this.FullPath();
                string? worksetGroup = this.WorksetGroup;

                if (path != null) 
                {
                    Excel excel = new Excel(path, worksetGroup);
                    excel.ReadExcel();

                    if (excel.cells != null)
                    {
                        worksetUtil = new WorksetUtil(excel.cells);
                    }
                }
            }
            return worksetUtil;
        }
    }
}
```

### File: Shared/EnumUtil.cs
```csharp
using System;
using System.Collections.Generic;
using Synthetic.Modules.MergeDuplicates.Handlers;
using Synthetic.Modules.RevitDOM;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.Shared.RevitAPI;

namespace Synthetic.Shared
{
    /// <summary>
    /// Wrapper for using enumerations
    /// </summary>
    public class EnumUtil
    {
        internal EnumUtil() { }

        /// <summary>
        /// Retrieves a enum of the given type and name.
        /// </summary>
        /// <param name="enumTypeName">The enum type as a string. Requires the full assembly path in front of the type.</param>
        /// <param name="name">Name of the enum.</param>
        /// <returns name="enum">Returns a enum.</returns>
        public static System.Object? Parse(string enumTypeName, string name)
        {
            System.Object? e = null;
            try
            {
                Type? et = GetEnumType(enumTypeName);
                if (et != null)
                {
                    e = Enum.Parse(et, name);
                }
            }
            catch (ArgumentException)
            {
                e = null;
            }
            return e;
        }

        /// <summary>
        /// Retrieves a list of the names of the constants in a specified enumeration.
        /// </summary>
        /// <param name="enumTypeName">The enum type as a string. Requires the full assembly path in front of the type.</param>
        /// <returns name="names">A string list of the names of the constants in enumType.</returns>
        public static List<string> GetNames(string enumTypeName)
        {
            List<string> e = new List<string>();
            Type? eType = GetEnumType(enumTypeName);
            if (eType != null)
            {
                foreach (string name in Enum.GetNames(eType))
                {
                    e.Add(name);
                }
            }
            return e;
        }

        /// <summary> 
        /// Retrieves the name as a string of the enumeration. 
        /// </summary> 
        /// <param name="enumeration">A enum</param> 
        /// <returns name="name">Returns the name of the enum as a string</returns> 
        public static string GetName(Enum enumeration)
        {
            var enumType = enumeration.GetType();
            return Enum.GetName(enumType, enumeration) ?? enumeration.ToString();
        }

        /// <summary>
        /// Retrieves all enums of a given type.
        /// </summary>
        /// <param name="enumTypeName">The enum type as a string. Requires the full assembly path in front of the type.</param>
        /// <returns name="enums">A list of enums.</returns>
        public static List<System.Object> GetEnums(string enumTypeName)
        {
            List<System.Object> eList = new List<System.Object>();
            Type? eType = GetEnumType(enumTypeName);
            if (eType != null)
            {
                foreach (string name in Enum.GetNames(eType))
                {
                    System.Object? e = Parse(enumTypeName, name);
                    if (e != null)
                    {
                        eList.Add(e);
                    }
                }
            }
            return eList;
        }

        /// <summary>
        /// Evaluates a enum and returns its value.
        /// </summary>
        /// <param name="enumeration">A enum</param>
        /// <returns name="value">Returns the value of the enum</returns>
        public static object GetValue(object enumeration)
        {
            //int i = Convert.ToInt32(enumeration);
            //return i;
            var enumType = enumeration.GetType();
            var underlyingType = Enum.GetUnderlyingType(enumType);
            var numericValue = System.Convert.ChangeType(enumeration, underlyingType);
            return numericValue;
        }

        /// <summary>
        /// Retrieves the Enum Type from a string name.
        /// </summary>
        /// <param name="enumTypeName">The enum type as a string. Requires the full assembly path in front of the type.</param>
        /// <returns name="enumType">Returns the enum type if it exists in the current domain.</returns>
        public static Type? GetEnumType(string enumTypeName)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var type = assembly.GetType(enumTypeName);
                if (type == null)
                    continue;
                if (type.IsEnum)
                    return type;
            }
            return null;
        }

        /// <summary>
        /// Returns an indication whether a constant with a specified value exists in a specified enumeration.
        /// </summary>
        /// <param name="enumTypeName">The enum type as a string. Requires the full assembly path in front of the type.</param>
        /// <param name="name">Name of the enum.</param>
        /// <returns name="bool">True if a constant in enumType has a value equal to value; otherwise, false.</returns>
        public static bool IsDefined(string enumTypeName, string name)
        {
            Type? eType = GetEnumType(enumTypeName);
            System.Object? e = Parse(enumTypeName, name);

            if (eType != null && e != null)
            {
                return Enum.IsDefined(eType, e);
            }
            return false;
        }
    }

    public static class EnumExtensions
    {
        public static Enum ToEnum(this EnumModel model)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));
            var parsed = EnumUtil.Parse(model.Type, model.Value);
            if (parsed is Enum e) return e;
            throw new InvalidOperationException($"Could not parse enum of type {model.Type} with value {model.Value}");
        }

        public static EnumModel ToModel(this Enum value)
        {
            if (value == null) return null;
            return new EnumModel(value.GetType(), value);
        }
    }
}
```

### File: Shared/RevitAPI/CommandUtil.cs
```csharp
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using System;
using System.IO;
using System.Collections.Generic;
using System.Text;
using System.Windows.Forms;

using Synthetic.Shared.RevitAPI;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.Modules.RevitDOM;
namespace Synthetic.Shared.RevitAPI
{
    /// <summary>
    /// Utility methods for executing Revit commands and saving/loading results.
    /// </summary>
    public class CommandUtil
    {
        /// <summary>
        /// Saves a command results to a json file in the same location as the document.
        /// </summary>
        /// <param name="results">The object/results to serialize.</param>
        /// <param name="document">The Revit document.</param>
        /// <param name="fileName">The base file name for the saved JSON file.</param>
        /// <param name="path">The folder path to save the file. If null, the document folder is used.</param>
        /// <returns>The full path of the saved file.</returns>
        public static string? SaveResults(object results, Document document, string fileName = "Results", string? path = null)
        {
            if (path == null)
            {
                path = DocumentUtil.GetFolderPath(document);
            }
            if (path == null)
            {
                return null;
            }
            string newFileName = DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss") + " - " + fileName;
            string ext = ".json";

            string fullPath = Path.Combine(path, newFileName + ext);

            if (fullPath != null /*&& Uri.IsWellFormedUriString(fullPath, UriKind.Absolute)*/)
            {
                string json = Newtonsoft.Json.JsonConvert.SerializeObject(results, Formatting.Indented);
                File.WriteAllText(fullPath, json);
            }
            return fullPath;
        }

        /// <summary>
        /// Displays a save file dialog and saves the command results to the chosen JSON file path.
        /// </summary>
        /// <param name="results">The object/results to serialize.</param>
        /// <returns>The full path of the saved file, or null if cancelled.</returns>
        public static string? SaveAsResults(object results)
        {
            string? fullPath = null;

            // Create an instance of the open file dialog box.
            FileSaveDialog saveFileDialog = new FileSaveDialog("JSON Files (*.json)|*.json");
            saveFileDialog.Title = "Select JSON File to save results into";

            // Call the ShowDialog method to show the dialog box.
            ItemSelectionDialogResult resultSave = saveFileDialog.Show();
            // Process input if the user clicked OK.
            if (resultSave == ItemSelectionDialogResult.Confirmed)
            {
                ModelPath modelPath = saveFileDialog.GetSelectedModelPath();
                fullPath = ModelPathUtils.ConvertModelPathToUserVisiblePath(modelPath);

                if (fullPath != null /*&& Uri.IsWellFormedUriString(fullPath, UriKind.Absolute)*/)
                {
                    string json = Newtonsoft.Json.JsonConvert.SerializeObject(results, Formatting.Indented);
                    File.WriteAllText(fullPath, json);
                }
            }
            saveFileDialog.Dispose();
            return fullPath;
        }

        /// <summary>
        /// Opens a FileSaveDialog to select a path and file name for a JSON file.
        /// </summary>
        /// <param name="initialFileName">Optional initial file name to suggest in the dialog.</param>
        /// <returns>Full path of the file to save.</returns>
        public static string? SaveAsJSON(string? initialFileName = null)
        {
            string? fullPath = null;

            // Create an instance of the open file dialog box.
            FileSaveDialog saveFileDialog = new FileSaveDialog("JSON Files (*.json)|*.json");
            saveFileDialog.Title = "Select a File to save";

            if(initialFileName != null)
            {
                saveFileDialog.InitialFileName = initialFileName;
            }

            // Call the ShowDialog method to show the dialog box.
            ItemSelectionDialogResult resultSave = saveFileDialog.Show();
            // Process input if the user clicked OK.
            if (resultSave == ItemSelectionDialogResult.Confirmed)
            {
                ModelPath modelPath = saveFileDialog.GetSelectedModelPath();
                fullPath = ModelPathUtils.ConvertModelPathToUserVisiblePath(modelPath);
            }
            saveFileDialog.Dispose();
            return fullPath;
        }

        /// <summary>
        /// Selects a JSON file to open
        /// </summary>
        /// <returns>the full path of the JSON file.</returns>
        public static string? OpenJSON()
        {
            string? fullPath = null;

            // Create an instance of the open file dialog box.
            FileOpenDialog openFileDialog = new FileOpenDialog("JSON Files (*.json)|*.json");
            openFileDialog.Title = "Select a File to save";

            // Call the ShowDialog method to show the dialog box.
            ItemSelectionDialogResult resultSave = openFileDialog.Show();
            // Process input if the user clicked OK.
            if (resultSave == ItemSelectionDialogResult.Confirmed)
            {
                ModelPath modelPath = openFileDialog.GetSelectedModelPath();
                fullPath = ModelPathUtils.ConvertModelPathToUserVisiblePath(modelPath);
            }
            openFileDialog.Dispose();
            return fullPath;
        }
    }
}
```

### File: Shared/RevitAPI/CoordinateUtility.cs
```csharp
using Autodesk.Revit.DB;

using Synthetic.Modules.AutoTagger.Models;
using Synthetic.Modules.RevitDOM;
using Synthetic.Infrastructure.Serialization;

using Synthetic.Shared.RevitAPI;
using Synthetic.Modules.StandardsManagement.ViewModels;
namespace Synthetic.Shared.RevitAPI{
    /// <summary>
    /// Provides linear algebra transformation mapping for tag placement relative to host elements.
    /// </summary>
    public static class CoordinateUtility
    {
        /// <summary>
        /// Converts a World Coordinate point into the Local Coordinate system of the host element.
        /// </summary>
        public static XYZ? GetLocalOffset(FamilyInstance? host, XYZ? worldPoint)
        {
            if (host == null || worldPoint == null) return null;
            
            Transform transform = host.GetTransform();
            if (!transform.IsConformal) return null; // Safety check for malformed scaling

            XYZ origin = transform.Origin;
            XYZ handDir = host.HandOrientation;
            XYZ faceDir = host.FacingOrientation;
            XYZ upDir = transform.BasisZ;

            XYZ translation = worldPoint - origin;
            
            double x = translation.DotProduct(handDir);
            double y = translation.DotProduct(faceDir);
            double z = translation.DotProduct(upDir);

            return new XYZ(x, y, z);
        }

        /// <summary>
        /// Converts a Local Coordinate offset back into the global World Coordinate space of the model.
        /// </summary>
        public static XYZ? GetWorldPoint(FamilyInstance? host, XYZ? localOffset)
        {
            if (host == null || localOffset == null) return null;

            Transform transform = host.GetTransform();
            XYZ origin = transform.Origin;
            XYZ handDir = host.HandOrientation;
            XYZ faceDir = host.FacingOrientation;
            XYZ upDir = transform.BasisZ;

            return origin + 
                   localOffset.X * handDir + 
                   localOffset.Y * faceDir + 
                   localOffset.Z * upDir;
        }

        /// <summary>
        /// Calculates the tag's target orientation based on the family instance's current rotation relative to the template's baseline.
        /// </summary>
        public static TagOrientation CalculateTagOrientation(FamilyInstance host, TagTemplate template)
        {
            if (host == null || template == null) return TagOrientation.Horizontal;
            if (!template.AllowOrientationChange) return template.Orientation;

            // Baseline hand orientation of the template
            XYZ vTmpl = new XYZ(template.HostHandX, template.HostHandY, template.HostHandZ);
            if (vTmpl.GetLength() < 1e-5)
            {
                // Default fallback if not previously saved or zero vector
                vTmpl = new XYZ(1, 0, 0);
            }

            XYZ vTgt = host.HandOrientation;

            // Project vectors onto the XY plane for 2D orientation calculation in plan views
            XYZ vTmpl2d = new XYZ(vTmpl.X, vTmpl.Y, 0);
            XYZ vTgt2d = new XYZ(vTgt.X, vTgt.Y, 0);

            if (vTmpl2d.GetLength() < 1e-5 || vTgt2d.GetLength() < 1e-5)
            {
                // If either is degenerate, default to template's baseline orientation
                return template.Orientation;
            }

            vTmpl2d = vTmpl2d.Normalize();
            vTgt2d = vTgt2d.Normalize();

            double cosTheta = vTmpl2d.DotProduct(vTgt2d);
            double absCos = System.Math.Abs(cosTheta);

            // Using cos(45 degrees) approx 0.70711 as the boundary.
            // If absCos >= 0.70711, angle is within [-45, 45] or [135, 225] -> Keep original orientation.
            // If absCos < 0.70711, angle is within (45, 135) or (225, 315) -> Toggle orientation.
            if (absCos >= 0.70711)
            {
                return template.Orientation;
            }
            else
            {
                return template.Orientation == TagOrientation.Horizontal 
                    ? TagOrientation.Vertical 
                    : TagOrientation.Horizontal;
            }
        }
    }
}
```

### File: Shared/RevitAPI/DocumentUtil.cs
```csharp
using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using Autodesk.Revit.DB;
using Application = Autodesk.Revit.ApplicationServices.Application;
using RevitDoc = Autodesk.Revit.DB.Document;
using Autodesk.Revit.UI;

using Synthetic.Infrastructure.Diagnostics;
using Autodesk.Revit.DB.ExtensibleStorage;

#if !REVIT2022
using eTransmitForRevitDB;
#endif

using Synthetic.Shared.RevitAPI;
using Synthetic.Modules.RevitDOM;
using Synthetic.Modules.StandardsManagement.ViewModels;
namespace Synthetic.Shared.RevitAPI{
    /// <summary>
    /// Utility methods for interacting with Revit Document objects.
    /// </summary>
    public class DocumentUtil
    {
        /// <summary>
        /// Opens an RVT Revit file as detached.
        /// </summary>
        /// <param name="path">Autodesk.Revit.DB.ModelPath object pointing to the Revit file to open.</param>
        /// <param name="uiApp">The Autodesk.Revit.UI.UIApplication object to openthe file in.</param>
        /// <returns>The Autodeks.Revit.DB.Document object of the opened document.</returns>
        public static Document? OpenRvtDetached(ModelPath path, UIApplication uiApp)
        {
            Document? doc = null;

            try
            {
                string pathString = ModelPathUtils.ConvertModelPathToUserVisiblePath(path);
                BasicFileInfo fileInfo = BasicFileInfo.Extract(pathString);
            }
            catch { }

            return doc;
        }

        /// <summary>
        /// Retrieves the file path of the document depending on if the file is a cloud model, workshared or just a regular project.
        /// </summary>
        /// <param name="document">A Autodesk.Revit.DB.Document obejct.</param>
        /// <returns>A string of the path to the document.</returns>
        public static string? GetFilePath(Document document)
        {
            string? fullPath = null;
            if (document.IsModelInCloud)
            {
                fullPath = ModelPathUtils.ConvertModelPathToUserVisiblePath(
                    document.GetCloudModelPath());
            }
            else if (document.IsWorkshared)
            {
                fullPath = ModelPathUtils.ConvertModelPathToUserVisiblePath(
                    document.GetWorksharingCentralModelPath());
            }
            else
            {
                fullPath = document.PathName;
            }
            return fullPath;
        }

        /// <summary>
        /// Retrieves the folder path of the document depending on if the file is a cloud model, workshared or just a regular project.
        /// </summary>
        /// <param name="document">A Autodesk.Revit.DB.Document obejct.</param>
        /// <returns>A string of the path to the document.</returns>
        public static string? GetFolderPath(Document document)
        {
            string? fullPath = null;
            string? folderPath = null;
            if (document.IsModelInCloud)
            {
                fullPath = ModelPathUtils.ConvertModelPathToUserVisiblePath(
                    document.GetCloudModelPath());
            }
            else if (document.IsWorkshared)
            {
                fullPath = ModelPathUtils.ConvertModelPathToUserVisiblePath(
                    document.GetWorksharingCentralModelPath());
            }
            else
            {
                fullPath = document.PathName;
            }
            if (fullPath != null && fullPath != String.Empty)
            {
                folderPath = Path.GetDirectoryName(fullPath);
            }
                return folderPath;
        }

#if !REVIT2022
        /// <summary>
        /// Using etransmit, purges the model.
        /// </summary>
        /// <param name="app">The Revit Application</param>
        /// <param name="doc">Revit Document object</param>
        /// <returns>True if purge was successful.</returns>
        public static bool Purge(Application app, Document doc)
        {
            eTransmitUpgradeOMatic eTransmitUpgradeOMatic
              = new eTransmitUpgradeOMatic(app);

            UpgradeFailureType result
              = eTransmitUpgradeOMatic.purgeUnused(doc);

            return (result == UpgradeFailureType.UpgradeSucceeded);
        }
#endif
        
    }
}
```

### File: Shared/RevitAPI/ElementUtil.cs
```csharp
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

using RevitDB = Autodesk.Revit.DB;
using RevitElem = Autodesk.Revit.DB.Element;
using RevitElemId = Autodesk.Revit.DB.ElementId;
using RevitDoc = Autodesk.Revit.DB.Document;
using RevitFaceArray = Autodesk.Revit.DB.FaceArray;
using RevitFace = Autodesk.Revit.DB.Face;
using RevitGeo = Autodesk.Revit.DB.GeometryObject;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;


using Synthetic.Shared.RevitAPI;
using Synthetic.Modules.MergeDuplicates.Handlers;
using Synthetic.Modules.RevitDOM;
using Synthetic.Modules.StandardsManagement.ViewModels;
namespace Synthetic.Shared.RevitAPI{
    /// <summary>
    /// Manipulation and modification of Dynamo wrapped Revit elements.
    /// </summary>
    public class ElementUtil
    {
        /// <summary>
        /// Dummy constructor for the class.  Not used.
        /// </summary>
        internal ElementUtil() { }

        /// <summary>
        /// Gets an elements document
        /// </summary>
        /// <param name="Element">A dynamo wrapped element</param>
        /// <returns name="Document">A Autodesk.Revit.DB.Document</returns>
        public static RevitDoc Document(RevitElem Element)
        {
            return Element.Document;
        }

        /// <summary>
        /// Sets an element's parameters based on a Dictionary object with the Key being the parameter name and the Value being the parameter value.
        /// </summary>
        /// <param name="element">A Dynamo wrapped element.</param>
        /// <param name="dictionary">A Synthetic Dictionary</param>
        /// <returns></returns>
        public static RevitElem SetParamterByDictionary(RevitElem element, Dictionary<string, RevitDB.Parameter> dictionary)
        {
            RevitDoc doc = element.Document;

            foreach (KeyValuePair<string, RevitDB.Parameter> keyValue in dictionary)
            {
                if (element.GetParameters(keyValue.Key)[0].IsReadOnly == false)
                {
                    RevitDB.Parameter param = element.LookupParameter(keyValue.Key);

                    switch (param.StorageType)
                    {
                        case RevitDB.StorageType.ElementId:
                            param.Set(keyValue.Value.AsElementId());
                            break;
                        case RevitDB.StorageType.String:
                            param.Set(keyValue.Value.AsString());
                            break;
                        case RevitDB.StorageType.Integer:
                            param.Set(keyValue.Value.AsInteger());
                            break;
                        case RevitDB.StorageType.Double:
                            param.Set(keyValue.Value.AsDouble());
                            break;
                        default:
                            throw new Exception("Parameter doesn't have a storage type");
                    }
                }
            }
            return element;
        }

        /// <summary>
        /// Gets the listed parameters of an element and returns a Dictionary with the Key being the parameter name and the Value being the parameter value.
        /// </summary>
        /// <param name="element">A Dynamo wrapped element.</param>
        /// <param name="parameterNames">A list of parameter names.</param>
        /// <returns></returns>
        public static Dictionary<string, RevitDB.Parameter> GetParamterToDictionary(RevitElem element, List<string> parameterNames)
        {
            Dictionary<string, RevitDB.Parameter> dict = new Dictionary<string, RevitDB.Parameter>();
            RevitDoc doc = element.Document;

            foreach (string name in parameterNames)
            {
                RevitDB.Parameter value = element.LookupParameter(name);
                if (value != null)
                {
                    dict.Add(name, value);
                }
            }

            return dict;
        }

        /// <summary>
        /// Overwrite an elements parameters with the parameter values from a source element.
        /// </summary>
        /// <param name="Element">Destination element</param>
        /// <param name="SourceElement">Source element for the parameter values</param>
        /// <returns name="Element">The destination element</returns>
        public static RevitElem TransferParameters(RevitElem Element, RevitElem SourceElement)
        {
            Action<RevitElem, RevitElem> transfer = (sElem, dElem) =>
            {
                _transferParameters(sElem, dElem);
            };

            RevitDoc document = Element.Document;

            transfer(SourceElement, Element);

            return Element;
        }

        /// <summary>
        /// Copy elements to the same location between documents.  Can be used to copy system types or view templates between documents.  Model elements are copied in the same location.  If the elements already exist, Revit will give you an option to either duplicate the types or cancel the operation.  Please note that documents are to be a Autodesk.Revit.DB.Document objects, not a Dynamo wrapped Revit Document.
        /// </summary>
        /// <param name="sourceDoc">The source document to copy items from.</param>
        /// <param name="elementIds">List of Element Ids of elements to be copied.</param>
        /// <param name="destinationDoc">The destination document.</param>
        /// <returns></returns>
        public static List<RevitElemId> CopyElements(RevitDoc sourceDoc, List<int> elementIds, RevitDoc destinationDoc)
        {
            string transactionName = "Copy Elements from document " + sourceDoc.Title;
            List<RevitElemId> copiedElemsIds;
            List<RevitElemId> revitElemIds = new List<RevitElemId>();

            Func<RevitDoc, List<RevitElemId>, RevitDoc, List<RevitElemId>> copy = (sDoc, elemIds, dDoc) =>
            {
                Autodesk.Revit.DB.CopyPasteOptions cpo = new Autodesk.Revit.DB.CopyPasteOptions();
                return (List<RevitElemId>)Autodesk.Revit.DB.ElementTransformUtils.CopyElements(sDoc, elemIds, dDoc, null, cpo);
            };

            foreach (int id in elementIds)
            {
#if REVIT2022 || REVIT2023
                revitElemIds.Add(new RevitElemId(id));
#else
                revitElemIds.Add(new RevitElemId((long)id));
#endif
            }
            copiedElemsIds = copy(sourceDoc, revitElemIds, destinationDoc);

            return copiedElemsIds;
        }

        /// <summary>
        /// Overwrites the parameters of an element with the parameters of an element from a different document.  Associated elements such as materials may be duplicated in the document.
        /// </summary>
        /// <param name="Element"></param>
        /// <param name="SourceElement"></param>
        /// <returns></returns>
        public static RevitElem TransferElements(
            RevitElem Element,
            RevitElem SourceElement)
        {
            RevitDoc destinationDoc = Element.Document;
            RevitDoc sourceDoc = SourceElement.Document;

            string transactionName = "Element overwritten from " + sourceDoc.Title;

            RevitElem returnElem;

            Func<RevitElem, RevitDoc, RevitElem, RevitDoc, RevitElem> transfer = (dElem, dDoc, sElem, sDoc) =>
            {
                List<RevitElemId> revitElemIds = new List<RevitElemId>();
                revitElemIds.Add(sElem.Id);

                Autodesk.Revit.DB.CopyPasteOptions cpo = new Autodesk.Revit.DB.CopyPasteOptions();
                List<RevitElemId> ids = (List<RevitElemId>)Autodesk.Revit.DB.ElementTransformUtils.CopyElements(sDoc, revitElemIds, dDoc, null, cpo);

                RevitElem tempElem = dDoc.GetElement(ids[0]);
                _transferParameters(tempElem, dElem);
                destinationDoc.Delete(tempElem.Id);

                return dElem;
            };

            returnElem = transfer(Element, destinationDoc, SourceElement, sourceDoc);

            return returnElem;
        }

        /// <summary>
        /// Changes an element's subcategory.  Only works inside of Family documents
        /// </summary>
        /// <param name="element">Element to change</param>
        /// <param name="category">Subcategory to set the element too.</param>
        /// <returns name="Suceeded">If the element's category was changed.</returns>
        /// <returns name="Failed">If the change in category failed.</returns>
        public static IDictionary SetCategory(RevitElem element, RevitDB.Category category)
        {
            //  Name of Transaction
            string transactionName = "Set Element Category to" + category.Name;

            RevitDoc document = element.Document;

            // Intialize list for elements that are successfully merged and failed to merge.
            List<RevitElem> elementsMerged = new List<RevitElem>();
            List<RevitElem> elementsFailed = new List<RevitElem>();

            // Define Function to change element's category.
            Action<RevitElem, RevitDB.Category> _SetCategory = (elem, cat) =>
            {
                // If Element is in a group, put the element in the failed list
#if REVIT2022 || REVIT2023
                long groupId = elem.GroupId.IntegerValue;
#else
                long groupId = elem.GroupId.Value;
#endif
                if (groupId == -1)
                {
                    //elem.TextNoteType = rToType;
                    RevitDB.Parameter parameter = elem.get_Parameter(RevitDB.BuiltInParameter.FAMILY_ELEM_SUBCATEGORY);
                    parameter.Set(cat.Id);
                    elementsMerged.Add(elem);
                }
                else
                {
                    elementsFailed.Add(elem);
                }
            };
            _SetCategory(element, category);

            return new Dictionary<string, object>
            {
                {"Succeeded", elementsMerged},
                {"Failed", elementsFailed}
            };
        }

        /// <summary>
        /// Given a list of elements, sets their worksets to the given workset.
        /// </summary>
        /// <param name="elements">A list of elements to change</param>
        /// <param name="workset">A Revit Workset object</param>
        /// <param name="document">A Revit Document</param>
        /// <returns>The list of elements for chaining.</returns>
        public static List<RevitElem> SetWorkset(List<RevitElem> elements, Workset workset, RevitDoc document)
        {
            //  Name of Transaction
            string transactionName = "Move " + elements.Count + " elements to workset " + workset.Name;

            using (Autodesk.Revit.DB.Transaction trans = new Autodesk.Revit.DB.Transaction(document))
            {
                trans.Start(transactionName);
                foreach (RevitElem element in elements)
                {
#if !REVIT2022
                    if (element.IsModifiable)
                    {
#endif
                    Parameter worksetParam = element.get_Parameter(BuiltInParameter.ELEM_PARTITION_PARAM);
                        if (worksetParam != null 
                        && worksetParam.AsInteger() != workset.Id.IntegerValue 
                        && !worksetParam.IsReadOnly 
                        /*&& worksetParam.UserModifiable*/)
                        {
                            worksetParam.Set(workset.Id.IntegerValue);
                        }
#if !REVIT2022
                    }
#endif
                }
                trans.Commit();
            }
            return elements;
        }

        /// <summary>
        /// Merges ElementType FromType into ToType.  FromType will be deleted if all instances of the Type are successfully changed.  Elements in groups will not be changed.
        /// </summary>
        /// <param name="FromType">All instances of this ElementType will be merged into the ToType and the Type will be deleted.</param>
        /// <param name="ToType">ElementType to merge into.</param>
        /// <returns name="Merged">A list of instances that were successfully changed to ToType</returns>
        /// <returns name="Failed">A list of instances that failed to changed to ToType</returns>
        public static IDictionary MergeElementTypes(RevitElem FromType, RevitElem ToType)
        {
            // Get the Revit elements from the Dynamo Elements
            RevitDB.ElementType rFromType = (RevitDB.ElementType)FromType;
            RevitDB.ElementType rToType = (RevitDB.ElementType)ToType;

            RevitDoc document = rToType.Document;

            // Collect all instances of FromType
            IEnumerable<RevitDB.Element> instances = Select.GetInstancesFromElemType(rFromType, document);

            // Intialize list for elements that are successfully merged and failed to merge.
            List<RevitElem> elementsMerged = new List<RevitElem>();
            List<RevitElem> elementsFailed = new List<RevitElem>();

            // Define Function to change instances types.
            Action<IEnumerable<RevitDB.Element>> _SetType = (elements) =>
            {
                foreach (RevitDB.Element elem in elements)
                {
                    // If Element is in a group, put the element in the failed list
#if REVIT2022 || REVIT2023
                    long groupId = elem.GroupId.IntegerValue;
#else
                    long groupId = elem.GroupId.Value;
#endif
                    if (groupId == -1)
                    {
                        //elem.TextNoteType = rToType;
                        RevitDB.Parameter param = elem.get_Parameter(RevitDB.BuiltInParameter.ELEM_TYPE_PARAM);
                        param.Set(rToType.Id);
                        RevitElem dElem = elem;
                        elementsMerged.Add(dElem);
                    }
                    else
                    {
                        RevitElem dElem = elem;
                        elementsFailed.Add(dElem);
                    }
                }

                // Check if there are any instances of FromType left (either failed to merge, or none existed)
                if (elementsFailed.Count == 0)
                {
                    document.Delete(rFromType.Id);
                }
            };

            _SetType(instances);

            return new Dictionary<string, object>
            {
                {"Merged", elementsMerged},
                {"Failed", elementsFailed}
            };
        }

        /// <summary>
        /// Merges ElementType FromType into ToType.  FromType will be deleted if all instances of the Type are successfully changed.  Elements in groups will not be changed.
        /// </summary>
        /// <param name="FromType">All instances of this ElementType will be merged into the ToType and the Type will be deleted.</param>
        /// <param name="ToType">ElementType to merge into.</param>
        /// <returns name="Merged">A list of instances that were successfully changed to ToType</returns>
        /// <returns name="Failed">A list of instances that failed to changed to ToType</returns>
        public static IDictionary MergeElementTypesRevit(RevitElem FromType, RevitElem ToType)
        {
            //  Name of Transaction
            string transactionName = "Merge Element Type";

            RevitDoc document = ToType.Document;

            // Collect all instances of FromType
            IEnumerable<RevitDB.Element> instances = Select.GetInstancesFromElemType(FromType, document);

            // Intialize list for elements that are successfully merged and failed to merge.
            List<RevitElem> elementsMerged = new List<RevitElem>();
            List<RevitElem> elementsFailed = new List<RevitElem>();

            // Define Function to change instances types.
            Action<IEnumerable<RevitDB.Element>> _SetType = (elements) =>
            {
                foreach (RevitDB.Element elem in elements)
                {
                    // If Element is in a group, put the element in the failed list
#if REVIT2022 || REVIT2023
                    long groupId = elem.GroupId.IntegerValue;
#else
                    long groupId = elem.GroupId.Value;
#endif
                    if (groupId == -1)
                    {
                        //elem.TextNoteType = rToType;
                        RevitDB.Parameter param = elem.get_Parameter(RevitDB.BuiltInParameter.ELEM_TYPE_PARAM);
                        param.Set(ToType.Id);
                        elementsMerged.Add(elem);
                    }
                    else
                    {
                        elementsFailed.Add(elem);
                    }
                }

                // Check if there are any instances of FromType left (either failed to merge, or none existed)
                if (elementsFailed.Count == 0)
                {
                            document.Delete(FromType.Id);           
                }
            };

            try
            {
                using (Autodesk.Revit.DB.Transaction trans = new Autodesk.Revit.DB.Transaction(document))
                {
                    trans.Start(transactionName);
                    _SetType(instances);
                    trans.Commit();
                }
            }
            catch (Exception) { }

            return new Dictionary<string, object>
            {
                {"Merged", elementsMerged},
                {"Failed", elementsFailed}
            };
        }

        /// <summary>
        /// Tests whether the element has a parameter of a given value.  Returns true if the parameter has an equal value and false otherwise.  A list of parameters names can be given to test against values in elements within parameters.  For example, one can test for a type description from a instance by creating a list of each parameter.  Please note that the comparision is done using the string representation of each parameter.
        /// </summary>
        /// <param name="element">A dynamo wrapped Revit element.</param>
        /// <param name="parameterNames">A list of parameter names.  The parameters in the list will each be retrieved iteratively.  So the first name is on the input element, the next name on the element returned from the first parameter and so on.</param>
        /// <param name="value">A parameter value as a string.</param>
        /// <returns name="Filter">True if the element's parameter equals the input value, otherwise false.</returns>
        //[MultiReturn(new[] { "Filter", "Debug" })]
        //public static Dictionary<string,object> FilterByParameterValue (dynamoElem element, List<string> parameterNames, string value )
        public static bool FilterByParameterValue(RevitElem element, List<string> parameterNames, string value)
        {
            bool filter;
            object valueParam = element;

            //List<string> debug = new List<string>();

            foreach (string param in parameterNames)
            {
                Type vType = valueParam.GetType();

                if (vType != typeof(string) && vType != typeof(int) && vType != typeof(double))
                {
                    RevitElem e = (RevitElem)valueParam;
                    valueParam = e.LookupParameter(param);
                }
            }

            if (value.ToString() == valueParam.ToString())
            {
                filter = true;
            }
            else
            {
                filter = false;
            }

            return filter;
        }

        /// <summary>
        /// Gets an element given the ElementId
        /// </summary>
        /// <param name="elementId">A Autodesk.Revit.DB.ElementId</param>
        /// <param name="document">Document that the element is in.</param>
        /// <returns name="Element">Returns a unwrapped Autodesk.Revit.DB.Element</returns>
        public static RevitElem GetByElementId(RevitElemId elementId, RevitDoc document)
        {
            return document.GetElement(elementId);
        }

        /// <summary>
        /// Gets an element given its UniqueId
        /// </summary>
        /// <param name="UniqueId">A UniqueId as a string</param>
        /// <param name="document">Document that the element is in.</param>
        /// <returns name="Element">Returns a unwrapped Autodesk.Revit.DB.Element</returns>
        public static RevitElem GetByUniqueId(string UniqueId, RevitDoc document)
        {
            return document.GetElement(UniqueId);
        }

        /// <summary>
        /// Gets a Element's ElementId
        /// </summary>
        /// <param name="Element">A Autodesk.Revit.DB.Element, NOT a Dynamo wrapped element</param>
        /// <returns name="ElementId">The Autodesk.Revit.DB.ElementId</returns>
        public static RevitElemId Id(System.Object Element)
        {
            RevitElem elem = (RevitElem)Element;
            return elem.Id;
        }

        /// <summary>
        /// Gets a Element's name
        /// </summary>
        /// <param name="Element">A Autodesk.Revit.DB.Element, NOT a Dynamo wrapped element</param>
        /// <returns name="Name">The name of the element</returns>
        public static string Name(System.Object Element)
        {
            RevitElem elem = (RevitElem)Element;
            return elem.Name;
        }

        /// <summary>
        /// Gets a Element's UniqueId
        /// </summary>
        /// <param name="Element">A Autodesk.Revit.DB.Element, NOT a Dynamo wrapped element</param>
        /// <returns name="UniqueId">The UniqueId of the element</returns>
        public static string UniqueId(System.Object Element)
        {
            RevitElem elem = (RevitElem)Element;
            return elem.UniqueId;
        }

        /// <summary>
        /// If the object 
        /// </summary>
        /// <param name="Object"></param>
        /// <returns></returns>
        public static RevitElem CastRevitElement(System.Object Object)
        {
            return (RevitElem)Object;
        }

        /// <summary>
        /// Paints every face in an element with a material
        /// </summary>
        /// <param name="Element">The element to paint</param>
        /// <param name="MaterialId">The material to paint</param>
        /// <returns name="Element">The modified element</returns>
        public static RevitElem PaintElement(RevitElem Element, RevitElemId MaterialId)
        {
            RevitDoc document = Element.Document;
            RevitElemId elementId = Element.Id;

            RevitDB.Options op = new RevitDB.Options();
            RevitDB.GeometryElement geoElem = Element.get_Geometry(op);

            IList<RevitDB.Face> faces = new List<RevitDB.Face>();

            foreach (RevitDB.GeometryObject geo in geoElem)
            {
                if (geo is RevitDB.Solid solid)
                {
                    foreach (RevitDB.Face face in solid.Faces)
                    {
                        faces.Add(face);
                    }

                }
                else if (geo is RevitDB.Face face)
                {
                    faces.Add(face);
                }

                Action<RevitElemId, IList<RevitDB.Face>, RevitElemId> paintFaces = (RevitElemId elemId, IList<RevitDB.Face> faceList, RevitElemId matId) =>
                {
                    foreach (RevitDB.Face face in faceList)
                    {
                        document.Paint(elemId, face, matId);
                    }
                };

                paintFaces(elementId, faces, MaterialId);
            }

            return Element;
        }

        /// <summary>
        /// Removes all painted faces on an elementl
        /// </summary>
        /// <param name="Element">The element to removve painted faces</param>
        /// <returns name="Element">The modified element</returns>
        public static RevitElem RemovePaintElement(RevitElem Element)
        {
            RevitDoc document = Element.Document;
            RevitElemId elementId = Element.Id;

            RevitDB.Options op = new RevitDB.Options();
            RevitDB.GeometryElement geoElem = Element.get_Geometry(op);

            IList<RevitDB.Face> faces = new List<RevitDB.Face>();

            foreach (RevitDB.GeometryObject geo in geoElem)
            {
                if (geo is RevitDB.Solid solid)
                {
                    foreach (RevitDB.Face face in solid.Faces)
                    {
                        faces.Add(face);
                    }

                }
                else if (geo is RevitDB.Face face)
                {
                    faces.Add(face);
                }

                Action<RevitElemId, IList<RevitDB.Face>> RemovePaintedFaces = (RevitElemId elemId, IList<RevitDB.Face> faceList) =>
                {
                    foreach (RevitDB.Face face in faceList)
                    {
                        document.RemovePaint(elemId, face);
                    }
                };

                RemovePaintedFaces(elementId, faces);
            }

            return Element;
        }

        #region Helper Functions

        private static void _transferParameters(RevitElem SourceElement, RevitElem DestinationElement)
        {
            RevitDB.ParameterSet sourceParameters = SourceElement.Parameters;

            foreach (RevitDB.Parameter sourceParam in sourceParameters)
            {
                if (sourceParam.IsReadOnly == false)
                {
                    RevitDB.Definition def = sourceParam.Definition;
                    RevitDB.Parameter destinationParam = DestinationElement.get_Parameter(def);

                    RevitDB.StorageType st = sourceParam.StorageType;
                    switch (st)
                    {
                        case RevitDB.StorageType.Double:
                            destinationParam.Set(sourceParam.AsDouble());
                            break;
                        case RevitDB.StorageType.ElementId:
                            destinationParam.Set(sourceParam.AsElementId());
                            break;
                        case RevitDB.StorageType.Integer:
                            destinationParam.Set(sourceParam.AsInteger());
                            break;
                        case RevitDB.StorageType.String:
                            destinationParam.Set(sourceParam.AsString());
                            break;
                    }
                }
            }
        }
        #endregion

    }
}
```

### File: Shared/RevitAPI/FamilySymbolUtil.cs
```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using RevitDoc = Autodesk.Revit.DB.Document;
using View = Autodesk.Revit.DB.View;
using Synthetic.Modules.MergeDuplicates.Handlers;
using Synthetic.Modules.RevitDOM;
using Synthetic.Modules.StandardsManagement.ViewModels;

namespace Synthetic.Shared.RevitAPI
{
    /// <summary>
    /// Utility methods for managing and query Revit FamilySymbol elements.
    /// </summary>
    public class FamilySymbolUtil
    {
        /// <summary>
        /// Retrieves a FamilySymbol by its family name and symbol/type name.
        /// </summary>
        /// <param name="doc">The active Revit document.</param>
        /// <param name="familyName">The name of the Family.</param>
        /// <param name="symbolName">The name of the FamilySymbol/Type.</param>
        /// <returns>The matching FamilySymbol, or null if not found.</returns>
        public static FamilySymbol? GetByName (RevitDoc doc, string familyName, string symbolName)
        {
            FilteredElementCollector collector = new FilteredElementCollector(doc);

            return collector
                .OfClass(typeof(Family))
                .OfType<Family>()
                .FirstOrDefault(f => f.Name.Equals(familyName))?
                .GetFamilySymbolIds()
                .Select(id => doc.GetElement(id))
                .OfType<FamilySymbol>()
                .FirstOrDefault(symbol => symbol.Name.Equals(symbolName));
        }

        /// <summary>
        /// Gets all family symbols/types in the document belonging to the specified BuiltInCategory.
        /// </summary>
        /// <param name="doc">The active Revit document.</param>
        /// <param name="category">The BuiltInCategory to query.</param>
        /// <returns>A list of family symbol elements matching the category.</returns>
        public static IList<Element> GetFamiliesOfCategory (RevitDoc doc, BuiltInCategory category)
        {
            FilteredElementCollector collector = new FilteredElementCollector (doc);
            collector.OfClass(typeof(FamilySymbol)).OfCategory(category).WhereElementIsElementType();

            return collector.ToElements();
        }

        /// <summary>
        /// Checks if a FamilySymbol with the specified family name and symbol name is loaded in the document.
        /// </summary>
        /// <param name="doc">The active Revit document.</param>
        /// <param name="familyName">The name of the Family.</param>
        /// <param name="symbolName">The name of the FamilySymbol/Type.</param>
        /// <returns>True if loaded; otherwise, false.</returns>
        public static bool IsLoaded (RevitDoc doc, string familyName, string symbolName)
        {
            FilteredElementCollector collector = new FilteredElementCollector(doc);

            FamilySymbol? familySymbol = collector
                .OfClass(typeof(Family))
                .OfType<Family>()
                .FirstOrDefault(f => f.Name.Equals(familyName))?
                .GetFamilySymbolIds()
                .Select(id => doc.GetElement(id))
                .OfType<FamilySymbol>()
                .FirstOrDefault(symbol => symbol.Name.Equals(symbolName));

            return familySymbol != null;
        }

        /// <summary>
        /// Gets the location point origin of a FamilySymbol.
        /// </summary>
        /// <param name="family">The FamilySymbol to query.</param>
        /// <returns>The XYZ origin point, or null if not found.</returns>
        public static XYZ? GetOrigin(FamilySymbol? family)
        {
            if (family != null)
            {
                LocationPoint location = (LocationPoint)family.Location;
                return location.Point;
            }
            return null;
        }

        /// <summary>
        /// Gets all instances of a FamilySymbol that exist within a specific View.
        /// </summary>
        /// <param name="doc">The active Revit document.</param>
        /// <param name="family">The FamilySymbol whose instances to query.</param>
        /// <param name="view">The Revit View to search in.</param>
        /// <returns>A FilteredElementCollector containing the family instances.</returns>
        public static FilteredElementCollector GetInstancesInView (Document doc, FamilySymbol family, View view)
        {
            FilteredElementCollector collector = new FilteredElementCollector(doc, view.Id);
            ElementFilter filterInstance = new FamilyInstanceFilter(doc, family.Id);

            collector.OfClass(typeof(FamilyInstance)).WherePasses(filterInstance);

            return collector;
        }
    }
}
```

### File: Shared/RevitAPI/FamilyUtil.cs
```csharp
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Events;
using Application = Autodesk.Revit.ApplicationServices.Application;
using Document = Autodesk.Revit.DB.Document;
using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using Microsoft.Office.Interop.Excel;
using static System.Net.Mime.MediaTypeNames;
using System.Windows.Forms;
using Autodesk.Revit.DB.ExtensibleStorage;

using Synthetic.Modules.RevitDOM;
using Synthetic.Infrastructure.Serialization;

using Synthetic.Shared.UI;
using Synthetic.Core;

using Synthetic.Shared.RevitAPI;
using Synthetic.Modules.StandardsManagement.ViewModels;
namespace Synthetic.Shared.RevitAPI{
    /// <summary>
    /// Utility methods for auditing, loading, and modifying Revit Families.
    /// </summary>
    public class FamilyUtil
    {
        /// <summary>
        /// Opens all the Families in a project to test if any have been corrupted.
        /// </summary>
        /// <param name="document">A Revit Document</param>
        /// <param name="purge">If true, unused elements in the family are purged.</param>
        /// <param name="purgeSchema">If true, extensible storage schemas are purged from the family.</param>
        /// <param name="schemaExceptions">A list of schema names that should not be purged.</param>
        /// <returns name="results">Returns a text string of the results of opening each family.</returns>
        public static Dictionary<string, object> AuditProjectFamilies(Document document, bool purge = false, bool purgeSchema = false, List<string>? schemaExceptions = null)
        {
            List<List<string>> results = new List<List<string>>();
            List<List<string>> errors = new List<List<string>>();

            FilteredElementCollector families = new FilteredElementCollector(document);
            families.OfClass(typeof(Family)).ToElements();

            IFamilyLoadOptions opt = new ffrFamilyLoadOptions();
            Application app = document.Application;

            List<Tuple<Family, ElementId>> familiesToReinsert = new List<Tuple<Family, ElementId>>();

            foreach (Family family in families)
            {
                string name = family.Name;
                if (document.IsWorkshared == true)
                {
                    IList<WorksetId> worksetIds = new List<WorksetId>();
                    worksetIds.Add(family.WorksetId);
                    WorksharingUtils.CheckoutWorksets(document, worksetIds);
                }
                familiesToReinsert.Add(new Tuple<Family, ElementId>(family, family.Id));
            }

            ProgressCoordinator.Initialize("Audit & Purge Families", "Auditing and purging project families...", familiesToReinsert.Count);

            try
            {
                foreach (Tuple<Family, ElementId> familyTuple in familiesToReinsert)
                {
                    if (ProgressCoordinator.IsCancelled())
                    {
                        break;
                    }

                    Family family = familyTuple.Item1;
                    ElementId familyId = familyTuple.Item2;
                    Element searchElem = document.GetElement(familyId);

                    List<string> familyResult = new List<string>();
                    familyResult.Add(family.Name);
                    familyResult.Add(family.Id.ToString());
                    familyResult.Add(family.UniqueId);

                    if (searchElem != null && family.IsEditable)
                    {
                        {
                            try
                            {
                                Document familyDoc = document.EditFamily(family);
                                familyResult.Add("Opened successfully");

                                IList<FailureMessage> warnings = familyDoc.GetWarnings();

                                if (warnings.Count > 0)
                                {
                                    StringBuilder warningString = new StringBuilder();

                                    foreach (FailureMessage warning in warnings)
                                    {
                                        warningString.AppendLine(warning.GetDescriptionText());
                                    }
                                    familyResult.Add(warningString.ToString());
                                }
                                if (purge)
                                {
#if !REVIT2022
                                    bool purgeResult = DocumentUtil.Purge(app, familyDoc);
                                    familyResult.Add("Unused Purged");
#endif
                                }
                                if (purgeSchema && StorageUtil.DoesAnyStorageExist(familyDoc))
                                {
                                    List<Schema>? schemas = StorageUtil.GetDocumentSchemas(document);
                                    List<Schema> filteredSchemas = new List<Schema>();
                                    if (schemas != null)
                                    {
                                        if (schemaExceptions != null)
                                        {
                                            foreach (Schema schema in schemas)
                                            {
                                                if (schema != null && schema.SchemaName != null && !schemaExceptions.Any(schema.SchemaName.Contains))
                                                {
                                                    filteredSchemas.Add(schema);
                                                }
                                            }
                                        }
                                        else
                                        {
                                            filteredSchemas.AddRange(schemas);
                                        }
                                    }
                                    
                                    List<string>? schemaResults = StorageUtil.PurgeSchema(filteredSchemas, document);
                                    if(schemaResults != null)
                                    {
                                        if (schemaResults.Count > 0) { familyResult.Add(String.Join(", ", schemaResults.ToArray())); }
                                        else { familyResult.Add("No schemas were purged"); }
                                    }
                                    else { familyResult.Add("Error: Could not purge schemas"); }
                                }

                                if (App.AppControlled != null)
                                {
                                    App.AppControlled.ControlledApplication.FailuresProcessing +=
                                            new EventHandler<Autodesk.Revit.DB.Events.FailuresProcessingEventArgs>
                                            (ResolveWarnings);
                                }

                                familyDoc.LoadFamily(document, opt);

                                if (App.AppControlled != null)
                                {
                                    App.AppControlled.ControlledApplication.FailuresProcessing -=
                                        new EventHandler<Autodesk.Revit.DB.Events.FailuresProcessingEventArgs>
                                        (ResolveWarnings);
                                }

                                familyDoc.Close(false);
                                results.Add(familyResult);
                            }
                            catch (Exception ex)
                            {
                                familyResult.Add("Error: " + ex.Message);
                                errors.Add(familyResult);
                            }
                        }
                    }
                    else
                    {
                        familyResult.Add("Skipped: Family not found or not editable");
                        errors.Add(familyResult);
                    }

                    ProgressCoordinator.UpdateProgress(family.Name);
                }
            }
            finally
            {
                ProgressCoordinator.Close();
            }
            return new Dictionary<string, object>
            {
                {"Results", results},
                {"Errors", errors}
            };
        }

        /// <summary>
        /// Opens every Annotation family in the project and creates the standard element types.
        /// </summary>
        /// <param name="doc">The Revit Document</param>
        /// <param name="standards">A ModelsToSerialize object that contains the standard element types.</param>
        public static void LoadStandards(Document doc, IEnumerable<ElementModel> standards)
        {
            Categories categories = doc.Settings.Categories;
            List<ElementId> annotationCategories = new List<ElementId>();
            foreach (Category category in categories)
            {
                if (category.CategoryType == CategoryType.Annotation)
                {
                    annotationCategories.Add(category.Id);
                }
            }

            IList<Element> families = new FilteredElementCollector(doc)
                .OfClass(typeof(Family))
                .ToElements();

            IFamilyLoadOptions opt = new ffrFamilyLoadOptions();

            List<Tuple<Family, ElementId>> familiesToReinsert = new List<Tuple<Family, ElementId>>();

            foreach (Family family in families)
            {
                string name = family.Name;
                if (family.FamilyCategory.CategoryType == CategoryType.Annotation)
                {
                    if (doc.IsWorkshared == true)
                    {
                        IList<WorksetId> worksetIds = new List<WorksetId>();
                        worksetIds.Add(family.WorksetId);
                        WorksharingUtils.CheckoutWorksets(doc, worksetIds);
                    }
                    familiesToReinsert.Add(new Tuple<Family, ElementId>(family, family.Id));
                }
            }

            foreach (Tuple<Family, ElementId> familyTuple in familiesToReinsert)
            {
                Family family = familyTuple.Item1;
                ElementId familyId = familyTuple.Item2;
                Element searchElem = doc.GetElement(familyId);
                if (searchElem != null && family.IsEditable)
                {

                    Document familyDoc = doc.EditFamily(family);

                    if (App.AppControlled != null)
                    {
                        App.AppControlled.ControlledApplication.FailuresProcessing +=
                                new EventHandler<Autodesk.Revit.DB.Events.FailuresProcessingEventArgs>
                                (ResolveWarnings);
                    }

                    if (standards != null)
                    {
                        var engine = new StandardSerializationEngine();
                        engine.ToRevit(standards.OfType<ElementTypeModel>().Cast<ObjectModel>(), familyDoc);
                    }
                    familyDoc.LoadFamily(doc, opt);

                    if (App.AppControlled != null)
                    {
                        App.AppControlled.ControlledApplication.FailuresProcessing -=
                            new EventHandler<Autodesk.Revit.DB.Events.FailuresProcessingEventArgs>
                            (ResolveWarnings);
                    }
                    familyDoc.Close(false);
                }
                else
                {
                    //family.Name;
                }
            }
        }
        /// <summary>
        /// Forces all annotation families in the project to be reinserted/reloaded to ensure they are up to date.
        /// </summary>
        /// <param name="doc">The Revit Document.</param>
        public static void ForceReinsertAnnotation(Document doc)
        {
            Categories categories = doc.Settings.Categories;
            List<ElementId> annotationCategories = new List<ElementId>();
                foreach (Category category in categories)
                {
                    if (category.CategoryType == CategoryType.Annotation)
                    {
                        annotationCategories.Add(category.Id);
                    }
                }

            //ElementMulticategoryFilter catFilter = new ElementMulticategoryFilter(annotationCategories);

            IList<Element> families = new FilteredElementCollector(doc)
                .OfClass(typeof(Family))
                //.WherePasses(catFilter)
                .ToElements();

            IFamilyLoadOptions opt = new ffrFamilyLoadOptions();

            //IList<WorksetId> worksetIds = (IList < WorksetId > ) new FilteredWorksetCollector(doc)
            //    .OfKind(WorksetKind.FamilyWorkset)
            //    .ToWorksetIds();

            //WorksharingUtils.CheckoutWorksets(doc, worksetIds);

            List<Tuple<Family, ElementId>> familiesToReinsert = new List<Tuple<Family, ElementId>>();

            foreach (Family family in families)
            {
                string name = family.Name;
                if (family.FamilyCategory.CategoryType == CategoryType.Annotation)
                {
                    if (doc.IsWorkshared == true)
                    {
                        IList<WorksetId> worksetIds = new List<WorksetId>();
                        worksetIds.Add(family.WorksetId);
                        WorksharingUtils.CheckoutWorksets(doc, worksetIds);
                    }
                    familiesToReinsert.Add(new Tuple<Family, ElementId>(family, family.Id));
                }
            }

            foreach (Tuple<Family, ElementId> familyTuple in familiesToReinsert)
            {
                Family family = familyTuple.Item1;
                ElementId familyId = familyTuple.Item2;
                Element searchElem = doc.GetElement(familyId);
                if (searchElem != null && family.IsEditable)
                {

                    Document familyDoc = doc.EditFamily(family);
                    //FamilyUtil.ForceReinsert(familyDoc);

                    if (App.AppControlled != null)
                    {
                        App.AppControlled.ControlledApplication.FailuresProcessing +=
                                new EventHandler<Autodesk.Revit.DB.Events.FailuresProcessingEventArgs>
                                (ResolveWarnings);
                    }

                    SetIsChanged(doc, familyDoc);
                    familyDoc.LoadFamily(doc, opt);

                    if (App.AppControlled != null)
                    {
                        App.AppControlled.ControlledApplication.FailuresProcessing -=
                            new EventHandler<Autodesk.Revit.DB.Events.FailuresProcessingEventArgs>
                            (ResolveWarnings);
                    }
                    familyDoc.Close(false);
                }
                else
                {
                    //family.Name;
                }
            }
        }

        /// <summary>
        /// Marks a family document as changed/dirty by creating and deleting a temporary text note.
        /// </summary>
        /// <param name="doc">The active Revit document.</param>
        /// <param name="familyDoc">The family document to mark as changed.</param>
        public static void SetIsChanged (Document doc, Document familyDoc)
        {
            ElementId? viewId = null;

            viewId = new FilteredElementCollector(familyDoc)
            .OfClass(typeof(ViewPlan))
            .ToElementIds()
            .FirstOrDefault();

            if (viewId == null)
            {
                viewId = new FilteredElementCollector(familyDoc)
                    .OfClass(typeof(ViewSheet))
                    .ToElementIds()
                    .FirstOrDefault();
            }

            if (viewId == null)
            {
                viewId = new FilteredElementCollector(familyDoc)
                    .OfClass(typeof(Autodesk.Revit.DB.View))
                    .Cast<Autodesk.Revit.DB.View>()
                    .Where(v => !v.IsTemplate)
                    .Select(v => v.Id)
                    .FirstOrDefault();
            }

            if (viewId != null)
            {
                ElementId textTypeId = new FilteredElementCollector(familyDoc)
                .OfClass(typeof(TextNoteType))
                .ToElementIds()
                .FirstOrDefault() ?? ElementId.InvalidElementId;

                TextNote? note = null;
                ElementId? noteId = null;

                XYZ origin = XYZ.Zero;
                string text = "test";

                using (Autodesk.Revit.DB.Transaction trans = new Autodesk.Revit.DB.Transaction(familyDoc))
                {
                    trans.Start("Temp text note");
                    if (viewId != null)
                    {
                        try
                        {
                            note = TextNote.Create(familyDoc, viewId, origin, text, textTypeId);
                            noteId = note.Id;
                        }
                        catch { }
                    }
                    trans.Commit();
                }
                using (Autodesk.Revit.DB.Transaction trans = new Autodesk.Revit.DB.Transaction(familyDoc))
                {
                    trans.Start("Delete temp text note");
                    if (noteId != null)
                    {
                        //Element Searchelem = doc.GetElement(noteId);
                        //if (Searchelem != null)
                        //{
                            familyDoc.Delete(noteId);
                        //}
                    }
                    trans.Commit();
                }
            }
        }


        /// <summary>
        /// Catches warners and resolves them so they are not displayed to the user.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private static void ResolveWarnings(object? sender, FailuresProcessingEventArgs e)
        {
            FailuresAccessor fa = e.GetFailuresAccessor();
            IList<FailureMessageAccessor> failList = new List<FailureMessageAccessor>();
            failList = fa.GetFailureMessages(); // Inside event handler, get all warnings

            if (failList.Count == 0)
            {
                // FailureProcessingResult.Continue is to let 
                // the failure cycle continue next step.

                e.SetProcessingResult(FailureProcessingResult.Continue);

                return;
            }

            foreach (FailureMessageAccessor failure in failList)
            {
                // check FailureDefinitionIds against ones that you want to dismiss, 
                FailureDefinitionId failID = failure.GetFailureDefinitionId();
                // prevent Revit from showing Unenclosed room warnings
                if (failID == BuiltInFailures.RoomFailures.RoomNotEnclosed)
                {
                    fa.DeleteWarning(failure);
                }
            }
            e.SetProcessingResult(FailureProcessingResult.ProceedWithCommit);

            return;
        }
    }
    class ffrFamilyLoadOptions : IFamilyLoadOptions
    {
        public bool OnFamilyFound(
          bool familyInUse,
          out bool overwriteParameterValues)
        {
            overwriteParameterValues = true;
            return true;
        }

        public bool OnSharedFamilyFound(
          Family sharedFamily,
          bool familyInUse,
          out FamilySource source,
          out bool overwriteParameterValues)
        {
            source = FamilySource.Family;
            overwriteParameterValues = true;
            return true;
        }
    }
}
```

### File: Shared/RevitAPI/Select.cs
```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using RevitDB = Autodesk.Revit.DB;
using RevitArch = Autodesk.Revit.DB.Architecture;
using RevitDoc = Autodesk.Revit.DB.Document;
using RevitFECollector = Autodesk.Revit.DB.FilteredElementCollector;
using RevitElem = Autodesk.Revit.DB.Element;
using Synthetic.Modules.MergeDuplicates.Handlers;
using Synthetic.Modules.RevitDOM;
using Synthetic.Modules.StandardsManagement.ViewModels;

//using SynthCollect = Synthetic.Collector;
//using SynthCollectFilter = Synthetic.CollectorElementFilter;

namespace Synthetic.Shared.RevitAPI
{
    /// <summary>
    /// Nodes that certain sets of elements using pre-configured Collectors and filters.
    /// </summary>
    public class Select
    {
        internal Select() { }

        ///// <summary>
        ///// Selects all elements of a type.  Works with documents other than the active document.
        ///// </summary>
        ///// <param name="type">The element type of the object, such as WallTypes or Walls.</param>
        ///// <param name="inverted">If false, elements in the chosen category will be selected.  If true, elements NOT in the chosen category will be selected.</param>
        ///// <param name="document">A Autodesk.Revit.DB.Document object.  This does not work with Dynamo document objects.</param>
        ///// <returns name="Elements">A list of Dynamo elements that pass the filer.</returns>
        //public static IList<RevitElem> AllElementsOfType(Type type,
        //    bool inverted,
        //    RevitDoc document)
        //{
        //    SynthCollect collector = new SynthCollect(document);
        //    List<RevitDB.ElementFilter> filters = new List<Autodesk.Revit.DB.ElementFilter>();
        //    filters.Add(SynthCollectFilter.FilterElementClass(type, inverted));

        //    SynthCollect.SetFilters(collector, filters);

        //    return SynthCollect.ToElements(collector);
        //}

        ///// <summary>
        ///// Selects all instance elements in a category, excludes element types.  Use a Dynamo Category wrapper
        ///// </summary>
        ///// <param name="category">The Dynamo Category wrapper of the elements you wish to select.</param>
        ///// <param name="inverted">If false, elements in the chosen category will be selected.  If true, elements NOT in the chosen category will be selected.</param>
        ///// <param name="document">A Autodesk.Revit.DB.Document object.  This does not work with Dynamo document objects.</param>
        ///// <returns name="Elements">A list of Dynamo elements that pass the filer.</returns>
        //public static IList<RevitElem> AllElementsOfCategoryDynamo(RevitDB.Category categoryDynamo,
        //    bool inverted,
        //    RevitDoc document)
        //{
        //    SynthCollect collector = new SynthCollect(document);

        //    // Select only elements that are NOT Types (the filter is inverted)
        //    List<RevitDB.ElementFilter> filters = new List<Autodesk.Revit.DB.ElementFilter>();
        //    filters.Add(SynthCollectFilter.FilterElementIsElementType(true));
        //    filters.Add(SynthCollectFilter.FilterElementCategory(categoryDynamo, inverted));

        //    SynthCollect.SetFilters(collector, filters);

        //    return SynthCollect.ToElements(collector);
        //}

        ///// <summary>
        ///// Selects all Family Symbol types in a category, but excludes instances of those elements.  The node does not work with System familes because System Families do not have a Family Sybmol.
        ///// </summary>
        ///// <param name="category">The categoryId of the elements you wish to select.</param>
        ///// <param name="inverted">If false, elements in the chosen category will be selected.  If true, elements NOT in the chosen category will be selected.</param>
        ///// <param name="document">A Autodesk.Revit.DB.Document object.  This does not work with Dynamo document objects.</param>
        ///// <returns name="Elements">A list of Dynamo elements that pass the filer.</returns>
        //public static IList<RevitElem> AllFamilyTypesOfCategory(RevitDB.Category category,
        //    bool inverted,
        //    RevitDoc document)
        //{
        //    SynthCollect collector = new SynthCollect(document);

        //    // Select only elements that are Family Symbols
        //    List<RevitDB.ElementFilter> filters = new List<Autodesk.Revit.DB.ElementFilter>();
        //    filters.Add(SynthCollectFilter.FilterElementClass(typeof(RevitDB.FamilySymbol), false));
        //    filters.Add(SynthCollectFilter.FilterElementCategory(category, inverted));

        //    SynthCollect.SetFilters(collector, filters);

        //    return SynthCollect.ToElements(collector);
        //}

        /// <summary>
        /// Retrieve all materials in the document.
        /// </summary>
        /// <param name="document">>A Autodesk.Revit.DB.Document object.  This does not work with Dynamo document objects.</param>
        /// <returns name="Materials">A list of Auotdesk.Revit.DB.Materials</returns>
        public static IEnumerable<RevitDB.Material> AllMaterials(RevitDoc document)
        {
            RevitFECollector collector
                = new RevitFECollector(document);

            return collector
                .OfClass(typeof(RevitDB.Material))
                .OfType<RevitDB.Material>();
        }

        /// <summary>
        /// Given a list of materials, returns the material that matches the given name.
        /// </summary>
        /// <param name="materials">A list of Autodesk.Revit.DB.Materials</param>
        /// <param name="materialName">The name of the material</param>
        /// <returns name="Material">A Autodesk.Revit.DB.Material that matches the given name.</returns>
        public static RevitDB.Material? GetMaterialByName(IEnumerable<RevitDB.Material> materials, string materialName)
        {
            return materials
                .OfType<RevitDB.Material>()
                .FirstOrDefault(
                m => m.Name.Equals(materialName));
        }

        /// <summary>
        /// Retrieves a Revit element of the specified class/type that matches the given name.
        /// </summary>
        /// <param name="Name">The name of the element to retrieve.</param>
        /// <param name="Class">The class type of the element (e.g. typeof(Material)).</param>
        /// <param name="document">The Revit document to search.</param>
        /// <returns>The matching element, or null if not found.</returns>
        public static RevitElem? ElementByNameClass(string Name, Type Class, RevitDoc document)
        {
            RevitFECollector collector
                = new RevitFECollector(document);

            RevitElem? elem = collector
                .OfClass(Class)
                .FirstOrDefault(e => e.Name.Equals(Name));

            return elem;
        }

        /// <summary>
        /// Get the Type of a Revit Class from RevitAPI.dll given its name.
        /// </summary>
        /// <param name="typeName">Name of the Autodesk.Revit.DB Class</param>
        /// <returns name="Type">The Type of a Revit Class</returns>
        public static Type? RevitClassByString(string typeName)
        {
            // Assembly and Class that the Element should be
            Assembly assembly = typeof(RevitElem).Assembly;
            Type? elemClass = assembly.GetType(typeName);

            return elemClass;
        }

        /// <summary>
        /// Given the an ElementType Type retrieves the corresponding Instance Type.  For example Type WallType returns Type Wall or Type TextNoteType returns Type TextNote.
        /// </summary>
        /// <param name="elementType">A Type of ElementType</param>
        /// <returns name="instanceType">The Type of Instance</returns>
        public static Type? InstanceClassFromTypeClass(Type elementType)
        {
            Type? instanceType = null;

            // Create a dictionary of ElementTypes and InstanceTypes.
            Dictionary<Type, Type> types = new Dictionary<Type, Type>();
            types.Add(typeof(RevitDB.FamilySymbol), typeof(RevitDB.FamilyInstance));
            types.Add(typeof(RevitDB.TextNoteType), typeof(RevitDB.TextElement));
            types.Add(typeof(RevitDB.DimensionType), typeof(RevitDB.Dimension));
            types.Add(typeof(RevitDB.WallType), typeof(RevitDB.Wall));
            types.Add(typeof(RevitDB.FloorType), typeof(RevitDB.Floor));
            types.Add(typeof(RevitDB.CeilingType), typeof(RevitDB.Ceiling));
            types.Add(typeof(RevitDB.RoofType), typeof(RevitDB.RoofBase));
            types.Add(typeof(RevitDB.BuildingPadType), typeof(RevitArch.BuildingPad));
            types.Add(typeof(RevitDB.WallFoundationType), typeof(RevitDB.WallFoundation));
            types.Add(typeof(RevitDB.BeamSystemType), typeof(RevitDB.BeamSystem));
            types.Add(typeof(RevitDB.GroupType), typeof(RevitDB.Group));
            types.Add(typeof(RevitDB.ViewFamilyType), typeof(RevitDB.View));
            types.Add(typeof(RevitDB.FilledRegionType), typeof(RevitDB.FilledRegion));
            types.Add(typeof(RevitDB.GridType), typeof(RevitDB.Grid));
            types.Add(typeof(RevitDB.LevelType), typeof(RevitDB.Level));
            types.Add(typeof(RevitDB.TextElementType), typeof(RevitDB.TextElement));
            types.Add(typeof(RevitDB.RevitLinkType), typeof(RevitDB.RevitLinkInstance));
            types.Add(typeof(RevitDB.AssemblyType), typeof(RevitDB.AssemblyInstance));

            if (types.ContainsKey(elementType))
            {
                instanceType = types[elementType];
            }

            return instanceType;
        }
        /// <summary>
        /// Retrieves all instances in the document that use the specified element type.
        /// </summary>
        /// <param name="ElemType">The element type whose instances are to be retrieved.</param>
        /// <param name="document">The Revit document.</param>
        /// <returns>A collection of matching instance elements.</returns>
        public static IEnumerable<RevitElem> GetInstancesFromElemType(RevitElem ElemType, RevitDoc document)
        {
            RevitFECollector collector = new RevitFECollector(document);
            RevitDB.BuiltInParameter parameterId = RevitDB.BuiltInParameter.ELEM_TYPE_PARAM;
            RevitDB.FilterNumericEquals filterNumberRule = new RevitDB.FilterNumericEquals();
            RevitDB.ParameterValueProvider provider = new RevitDB.ParameterValueProvider(new RevitDB.ElementId(parameterId));
            RevitDB.FilterRule filterRule = new RevitDB.FilterElementIdRule(provider, filterNumberRule, ElemType.Id);
            RevitDB.ElementFilter filterParameter = new RevitDB.ElementParameterFilter(filterRule, false);

            Type? instanceType = Select.InstanceClassFromTypeClass(ElemType.GetType());
            if (instanceType != null)
            {
                collector.OfClass(instanceType);
            }

            IEnumerable<RevitElem> instances = collector
                .WhereElementIsNotElementType()
                .WherePasses(filterParameter)
                .ToElements();

            return instances;
        }

        /// <summary>
        /// Retrieves an ElementType by its name.
        /// </summary>
        /// <param name="Name">The name of the ElementType.</param>
        /// <param name="document">The Revit document.</param>
        /// <returns>The matching ElementType, or null if not found.</returns>
        public static RevitElem? GetElementTypeByName(string Name, RevitDoc document)
        {
            RevitFECollector collector = new RevitFECollector(document);

            return collector
                .WhereElementIsElementType()
                .ToElements()
                .FirstOrDefault(e => e.Name.Equals(Name));
        }
    }
}
```

### File: Shared/RevitAPI/StorageUtil.cs
```csharp
//
// (C) Copyright 2003-2019 by Autodesk, Inc.
//
// Permission to use, copy, modify, and distribute this software in
// object code form for any purpose and without fee is hereby granted,
// provided that the above copyright notify appears in all copies and
// that both that copyright notify and the limited warranty and
// restricted rights notify below appear in all supporting
// documentation.
//
// AUTODESK PROVIDES THIS PROGRAM "AS IS" AND WITH ALL FAULTS.
// AUTODESK SPECIFICALLY DISCLAIMS ANY IMPLIED WARRANTY OF
// MERCHANTABILITY OR FITNESS FOR A PARTICULAR USE. AUTODESK, INC.
// DOES NOT WARRANT THAT THE OPERATION OF THE PROGRAM WILL BE
// UNINTERRUPTED OR ERROR FREE.
//
// Use, duplication, or disclosure by the U.S. Government is subject to
// restrictions set forth in FAR 52.227-19 (Commercial Computer
// Software - Restricted Rights) and DFAR 252.227-7013(c)(1)(ii)
// (Rights in Technical Data and Computer Software), as applicable.
//

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Diagnostics;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.DB.ExtensibleStorage;
using Document = Autodesk.Revit.DB.Document;
using System.Windows.Forms;
using Synthetic.Modules.RevitDOM;
using Synthetic.Modules.StandardsManagement.ViewModels;



namespace Synthetic.Shared.RevitAPI
{
    /// <summary>
    /// Utility methods for interacting with Revit Extensible Storage.
    /// </summary>
    public class StorageUtil
    {
        /// <summary>
        /// Returns true if any extensible storage exists in the document, false otherwise.
        /// </summary>
        public static bool DoesAnyStorageExist(Document doc)
        {
            IList<Schema> schemas = Schema.ListSchemas();
            if (schemas.Count == 0)
                return false;
            else
            {
                foreach (Schema schema in schemas)
                {
                    List<ElementId> ids = ElementsWithStorage(doc, schema);
                    if (ids.Count > 0)
                        return true;
                }
                return false;
            }
        }

        /// <summary>
        /// Purges the given list of Schema from all open documents.
        /// </summary>
        /// <param name="schemas">List of schemas</param>
        /// <param name="document">Document to perform the transaction in.</param>
        public static List<string>? PurgeSchema(List<Schema> schemas, Document document)
        {
            List<string> results = new List<string>();
            int appVersion = int.Parse(document.Application.VersionNumber);
            string transactionName = "Erase Extensible Storage";
            using (Autodesk.Revit.DB.Transaction trans = new Autodesk.Revit.DB.Transaction(document))
            {
                trans.Start(transactionName);
                try
                {
                    foreach (Schema schema in schemas)
                    {
#if REVIT2022
                        // Schema.ErasSchemaAndAllEntities deprecated from Revit API in Revit 2023 and later
                        if (appVersion < 2023)
                        {
                            Schema.EraseSchemaAndAllEntities(schema, true);
                            results.Add(schema.SchemaName);
                        }
#endif
                        //Note-this will delete storage of this schema in *all* open documents.
                        if (schema.WriteAccessGranted())
                        {
                            document.EraseSchemaAndAllEntities(schema);
                            results.Add(schema.SchemaName);
                        }
                    }
                    trans.Commit();
                }
                catch { trans.RollBack(); }
            }
            if (results.Count > 0) { return results; }
            else { return null; }
        }

        /// <summary>
        /// Gets the schemas that have elements in the given document.
        /// </summary>
        /// <param name="doc">An Autodesk Revit Document obejct</param>
        /// <returns>List of schemas in the document.</returns>
        public static List<Schema>? GetDocumentSchemas(Document doc)
        {
            IList<Schema> schemas = Schema.ListSchemas();
            List<Schema> docSchemas = new List<Schema>();
            if (schemas.Count == 0)
                return null;
            else
            {
                foreach (Schema schema in schemas)
                {
                    List<ElementId> ids = ElementsWithStorage(doc, schema);
                    if(ids.Count > 0)
                    {
                        docSchemas.Add(schema);
                    }
                }
                if (schemas.Count > 0)
                {
                    return docSchemas;
                }
                else return null;
            }
        }

        /// <summary>
        /// Returns a formatted string containing schema guids and element info for all elements
        /// containing extensible storage.
        /// </summary>
        public static string GetElementStringWithAllSchemas(Document doc)
        {
            StringBuilder sBuilder = new StringBuilder();
            IList<Schema> schemas = Schema.ListSchemas();
            if (schemas.Count == 0)
                return "No schemas or storage.";
            else
            {
                foreach (Schema schema in schemas)
                {
                    sBuilder.Append(StorageUtil.GetElementStringWithSchema(doc, schema));
                }
                return sBuilder.ToString();
            }
        }

        /// <summary>
        /// Returns a formatted string containing a schema guid and element info for all elements
        /// containing extensible storage of a given schema.
        /// </summary>
        private static string GetElementStringWithSchema(Document doc, Schema schema)
        {
            StringBuilder sBuilder = new StringBuilder();
            sBuilder.AppendLine("Schema: " + schema.GUID.ToString() + ", " + schema.SchemaName);
            List<ElementId> elementsofSchema = ElementsWithStorage(doc, schema);
            if (elementsofSchema.Count == 0)
                sBuilder.AppendLine("No elements.");
            else
            {
                foreach (ElementId id in elementsofSchema)
                {
                    sBuilder.AppendLine(PrintElementInfo(id, doc));
                }
            }
            return sBuilder.ToString();
        }

        /// <summary>
        /// Returns a list of ElementIds that contain extensible storage of a given schema using
        /// the ExtensibleStorageFilter ElementQuickFilter.
        /// </summary>
        private static List<ElementId> ElementsWithStorage(Document doc, Schema schema)
        {
            List<ElementId> ids = new List<ElementId>();
            FilteredElementCollector collector = new FilteredElementCollector(doc);
            collector.WherePasses(new ExtensibleStorageFilter(schema.GUID));
            ids.AddRange(collector.ToElementIds());
            return ids;
        }



        /// <summary>
        /// Writes basic element info to a string.
        /// </summary>
        private static string PrintElementInfo(ElementId id, Document document)
        {
            Element element = document.GetElement(id);
            string retval = (element.Id.ToString() + ", " + element.Name + ", " + element.GetType().FullName);
            Debug.WriteLine(retval);
            return retval;
        }

    }
}
```

### File: Shared/UI/DropdownSelectionView.xaml
```xml
<Window x:Class="Synthetic.Shared.UI.DropdownSelectionView"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:local="clr-namespace:Synthetic.Shared.UI"
        Title="{Binding Title}" SizeToContent="WidthAndHeight" MinHeight="220" MinWidth="400"
        WindowStartupLocation="CenterOwner" ResizeMode="CanResizeWithGrip" ShowInTaskbar="False"
        Style="{DynamicResource SyntheticWindowStyle}"
        local:WindowChromeBehavior.EnableWindowCommands="True">
    <Window.Resources>
        <ResourceDictionary>
            <ResourceDictionary.MergedDictionaries>
                <ResourceDictionary Source="SyntheticTheme.xaml"/>
            </ResourceDictionary.MergedDictionaries>
        </ResourceDictionary>
    </Window.Resources>

    <Grid Margin="15">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/> <!-- Title / Instruction -->
            <RowDefinition Height="*"/>    <!-- ComboBox Selection -->
            <RowDefinition Height="Auto"/> <!-- Buttons -->
        </Grid.RowDefinitions>

        <!-- Header Title and Description -->
        <StackPanel Grid.Row="0" Margin="0,0,0,15">
            <TextBlock Text="{Binding Title}"
                       FontSize="16" FontWeight="SemiBold"
                       Foreground="{DynamicResource Synthetic.Brushes.TextPrimary}"
                       Margin="0,0,0,5" TextWrapping="Wrap"/>
            <TextBlock Text="{Binding Instruction}"
                       FontSize="12"
                       Foreground="{DynamicResource Synthetic.Brushes.TextSecondary}"
                       TextWrapping="Wrap"/>
        </StackPanel>

        <!-- Selection Area -->
        <Grid Grid.Row="1" VerticalAlignment="Center" Margin="0,0,0,15">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="Auto"/>
                <ColumnDefinition Width="*"/>
            </Grid.ColumnDefinitions>
            <TextBlock Grid.Column="0"
                       Text="{Binding ItemLabel}"
                       FontWeight="SemiBold"
                       Foreground="{DynamicResource Synthetic.Brushes.TextPrimary}"
                       VerticalAlignment="Center" Margin="0,0,12,0"/>
            <ComboBox Grid.Column="1"
                      ItemsSource="{Binding Items}"
                      SelectedItem="{Binding SelectedItem}"
                      VerticalContentAlignment="Center"/>
        </Grid>

        <!-- Actions -->
        <StackPanel Grid.Row="2" Orientation="Horizontal" HorizontalAlignment="Right">
            <Button Content="OK" Command="{Binding OkCommand}"
                    IsDefault="True" Margin="0,0,10,0"/>
            <Button Content="Cancel" Command="{Binding CancelCommand}"
                    IsCancel="True"/>
        </StackPanel>
    </Grid>
</Window>
```

### File: Shared/UI/DropdownSelectionView.xaml.cs
```csharp
using System;
using System.Windows;

using Synthetic.Shared.UI;
using Synthetic.Core;

using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.Modules.RevitDOM;

namespace Synthetic.Shared.UI
{
    /// <summary>
    /// Interaction logic for DropdownSelectionView.xaml.
    /// </summary>
    public partial class DropdownSelectionView : Window
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="DropdownSelectionView"/> class.
        /// </summary>
        /// <param name="mainWindowHandle">The handle to the main Revit application window, used to set the owner of this window.</param>
        public DropdownSelectionView(IntPtr mainWindowHandle)
        {
            InitializeComponent();
            RevitWindowHelper.SetOwner(this, mainWindowHandle);

            DataContextChanged += (sender, args) =>
            {
                if (DataContext is DropdownSelectionViewModel vm)
                {
                    vm.CloseAction = (dialogResult) =>
                    {
                        try
                        {
                            this.DialogResult = dialogResult;
                        }
                        catch (InvalidOperationException)
                        {
                            // Window is closing/closed
                        }
                        this.Close();
                    };
                }
            };
        }
    }
}
```

### File: Shared/UI/DropdownSelectionViewModel.cs
```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;

using Synthetic.Shared.UI;
using Synthetic.Modules.RevitDOM;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.Shared.RevitAPI;
namespace Synthetic.Shared.UI
{
    /// <summary>
    /// ViewModel for dropdown selection views.
    /// </summary>
    public class DropdownSelectionViewModel : ViewModelBase
    {
        private string _title = "Title";
        private string _instruction = "Instructions";
        private string _itemLabel = "Label";
        private IEnumerable<string> _items = new List<string>();
        private string? _selectedItem;
        private bool _isSorted;

        /// <summary>
        /// Initializes a new instance of the <see cref="DropdownSelectionViewModel"/> class.
        /// </summary>
        public DropdownSelectionViewModel()
        {
            OkCommand = new RelayCommand(OnOk, CanOk);
            CancelCommand = new RelayCommand(OnCancel);
        }

        /// <summary>
        /// Gets or sets the title of the view.
        /// </summary>
        public string Title
        {
            get => _title;
            set => SetProperty(ref _title, value);
        }

        /// <summary>
        /// Gets or sets the instructions shown to the user.
        /// </summary>
        public string Instruction
        {
            get => _instruction;
            set => SetProperty(ref _instruction, value);
        }

        /// <summary>
        /// Gets or sets the label for the dropdown.
        /// </summary>
        public string ItemLabel
        {
            get => _itemLabel;
            set => SetProperty(ref _itemLabel, value);
        }

        /// <summary>
        /// Gets or sets the items in the dropdown list.
        /// </summary>
        public IEnumerable<string> Items
        {
            get => _items;
            set
            {
                var list = value ?? new List<string>();
                if (_isSorted)
                {
                    list = list.OrderBy(x => x).ToList();
                }
                SetProperty(ref _items, list);
                
                // Select first item by default if there are items
                if (list.Any())
                {
                    SelectedItem = list.First();
                }
            }
        }

        /// <summary>
        /// Gets or sets the selected item from the dropdown list.
        /// </summary>
        public string? SelectedItem
        {
            get => _selectedItem;
            set => SetProperty(ref _selectedItem, value);
        }

        /// <summary>
        /// Gets or sets a value indicating whether items are sorted alphabetically.
        /// </summary>
        public bool IsSorted
        {
            get => _isSorted;
            set
            {
                if (SetProperty(ref _isSorted, value) && _items != null)
                {
                    Items = _items; // Trigger re-sorting
                }
            }
        }

        /// <summary>
        /// Gets or sets the action to close the window, passing a boolean result.
        /// </summary>
        public Action<bool>? CloseAction { get; set; }

        /// <summary>
        /// Gets the OK command.
        /// </summary>
        public ICommand OkCommand { get; }

        /// <summary>
        /// Gets the Cancel command.
        /// </summary>
        public ICommand CancelCommand { get; }

        private bool CanOk(object parameter)
        {
            return !string.IsNullOrEmpty(SelectedItem);
        }

        private void OnOk(object parameter)
        {
            CloseAction?.Invoke(true);
        }

        private void OnCancel(object parameter)
        {
            CloseAction?.Invoke(false);
        }
    }
}
```

### File: Shared/UI/EnumToBooleanConverter.cs
```csharp
using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

using Synthetic.Shared.UI;
using Synthetic.Modules.MergeDuplicates.Handlers;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.Modules.RevitDOM;
using Synthetic.Shared.RevitAPI;
namespace Synthetic.Shared.UI{
    /// <summary>
    /// Converts an enum value to a boolean for binding RadioButtons to Enum properties.
    /// </summary>
    public class EnumToBooleanConverter : IValueConverter
    {
        /// <summary>
        /// Converts an enum value to a boolean. Returns true if the value equals the parameter.
        /// </summary>
        /// <param name="value">The enum value produced by the binding source.</param>
        /// <param name="targetType">The type of the binding target property.</param>
        /// <param name="parameter">The converter parameter to compare against.</param>
        /// <param name="culture">The culture to use in the converter.</param>
        /// <returns>True if the value equals the parameter; otherwise, false.</returns>
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null || parameter == null)
                return DependencyProperty.UnsetValue;

            return value.Equals(parameter);
        }

        /// <summary>
        /// Converts a boolean back to the enum value. Returns the parameter if value is true.
        /// </summary>
        /// <param name="value">The boolean value produced by the binding target.</param>
        /// <param name="targetType">The type to convert to.</param>
        /// <param name="parameter">The converter parameter specifying the target enum value.</param>
        /// <param name="culture">The culture to use in the converter.</param>
        /// <returns>The parameter value if true; otherwise DependencyProperty.UnsetValue.</returns>
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null || parameter == null)
                return DependencyProperty.UnsetValue;

            if (value is bool isChecked && isChecked)
            {
                return parameter;
            }

            return DependencyProperty.UnsetValue;
        }
    }
}
```

### File: Shared/UI/FileDialogHelper.cs
```csharp
using System;
using System.IO;

using Synthetic.Shared.UI;
using Synthetic.Modules.RevitDOM;
using Synthetic.Modules.StandardsManagement.ViewModels;
namespace Synthetic.Shared.UI{
    /// <summary>
    /// Helper methods for showing Win32 file and folder dialogs with proper window parenting.
    /// </summary>
    public static class FileDialogHelper
    {
        /// <summary>
        /// Displays a folder browser dialog parented to the specified window handle.
        /// </summary>
        /// <param name="ownerHandle">The parent window handle (typically Revit's MainWindowHandle).</param>
        /// <param name="title">The description/title to show in the folder browser dialog.</param>
        /// <param name="initialPath">The initial folder path to display.</param>
        /// <returns>The selected folder path, or null if the selection was cancelled.</returns>
        public static string SelectFolder(IntPtr ownerHandle, string title, string? initialPath = null)
        {
            using (var dialog = new System.Windows.Forms.FolderBrowserDialog())
            {
                dialog.Description = title;
                
                if (!string.IsNullOrEmpty(initialPath) && Directory.Exists(initialPath))
                {
                    dialog.SelectedPath = initialPath;
                }

                System.Windows.Forms.DialogResult result;
                if (ownerHandle != IntPtr.Zero)
                {
                    result = dialog.ShowDialog(new Win32WindowWrapper(ownerHandle));
                }
                else
                {
                    result = dialog.ShowDialog();
                }

                if (result == System.Windows.Forms.DialogResult.OK)
                {
                    return dialog.SelectedPath ?? string.Empty;
                }
            }

            return string.Empty;
        }

        private class Win32WindowWrapper : System.Windows.Forms.IWin32Window
        {
            public IntPtr Handle { get; }
            public Win32WindowWrapper(IntPtr handle) => Handle = handle;
        }
    }
}
```

### File: Shared/UI/GuardrailPromptWindow.xaml
```xml
<Window x:Class="Synthetic.Shared.UI.GuardrailPromptWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="Master File Overwrite Guardrail"
        SizeToContent="WidthAndHeight" Width="480" MinHeight="250"
        WindowStartupLocation="CenterOwner" ResizeMode="NoResize" ShowInTaskbar="False"
        Style="{DynamicResource SyntheticWindowStyle}">
    
    <Window.Resources>
        <ResourceDictionary>
            <ResourceDictionary.MergedDictionaries>
                <ResourceDictionary Source="SyntheticTheme.xaml"/>
            </ResourceDictionary.MergedDictionaries>
        </ResourceDictionary>
    </Window.Resources>

    <Grid Margin="20">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="*"/>
            <RowDefinition Height="Auto"/>
        </Grid.RowDefinitions>

        <StackPanel Grid.Row="0" Orientation="Horizontal" Margin="0,0,0,15">
            <Path Data="M12,2L2,22H22L12,2M12,17A1,1 0 1,1 11,18A1,1 0 0,1 12,17M11,10H13V15H11V10Z"
                  Width="24" Height="24" Fill="{DynamicResource Synthetic.Brushes.Warning}" Margin="0,0,10,0" VerticalAlignment="Center"/>
            <TextBlock Text="Protected File Overwrite Intercepted" FontSize="16" FontWeight="Bold" Foreground="{DynamicResource Synthetic.Brushes.TextPrimary}" VerticalAlignment="Center"/>
        </StackPanel>

        <TextBlock Grid.Row="1" Text="You are attempting to write to a protected standards master file:" FontSize="11" Foreground="{DynamicResource Synthetic.Brushes.TextSecondary}" Margin="0,0,0,5"/>
        
        <Border Grid.Row="2" Background="{DynamicResource Synthetic.Brushes.ControlSurfaceLighter}" BorderBrush="{DynamicResource Synthetic.Brushes.BorderNormal}" BorderThickness="1" CornerRadius="4" Padding="10" Margin="0,0,0,20">
            <TextBlock Text="{Binding FilePath}" FontWeight="SemiBold" TextWrapping="Wrap" FontSize="11" Foreground="{DynamicResource Synthetic.Brushes.TextPrimary}"/>
        </Border>

        <StackPanel Grid.Row="3" Orientation="Vertical">
            <Button Content="Overwrite All (Replace Entire File)" Click="OverwriteAll_Click" Margin="0,0,0,8" Style="{DynamicResource Synthetic.Styles.PrimaryButton}"/>
            <Button Content="Merge and Overwrite Duplicates" Click="MergeOverwrite_Click" Margin="0,0,0,8" Style="{DynamicResource Synthetic.Styles.NeutralButton}"/>
            <Button Content="Merge and Preserve Duplicates" Click="MergePreserve_Click" Margin="0,0,0,8" Style="{DynamicResource Synthetic.Styles.NeutralButton}"/>
            <Grid>
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="*"/>
                    <ColumnDefinition Width="*"/>
                </Grid.ColumnDefinitions>
                <Button Grid.Column="0" Content="Save As..." Click="SaveAs_Click" Margin="0,0,4,0" Style="{DynamicResource Synthetic.Styles.NeutralButton}"/>
                <Button Grid.Column="1" Content="Cancel" Click="Cancel_Click" Margin="4,0,0,0"/>
            </Grid>
        </StackPanel>
    </Grid>
</Window>
```

### File: Shared/UI/GuardrailPromptWindow.xaml.cs
```csharp
using System;
using System.Windows;
using Synthetic.Shared.RevitAPI;

namespace Synthetic.Shared.UI
{
    /// <summary>
    /// Interaction logic for GuardrailPromptWindow.xaml
    /// </summary>
    public partial class GuardrailPromptWindow : Window
    {
        /// <summary>
        /// Gets the chosen guardrail action.
        /// </summary>
        public GuardrailResult Result { get; private set; } = GuardrailResult.Cancel;

        /// <summary>
        /// Gets the file path targeted for saving.
        /// </summary>
        public string FilePath { get; }

        /// <summary>
        /// Initializes a new instance of the GuardrailPromptWindow.
        /// </summary>
        /// <param name="filePath">The protected file path.</param>
        /// <param name="mainWindowHandle">The parent Revit window handle.</param>
        public GuardrailPromptWindow(string filePath, IntPtr mainWindowHandle)
        {
            FilePath = filePath;
            InitializeComponent();
            DataContext = this;

            if (mainWindowHandle != IntPtr.Zero)
            {
                RevitWindowHelper.SetOwner(this, mainWindowHandle);
            }
        }

        private void OverwriteAll_Click(object sender, RoutedEventArgs e)
        {
            Result = GuardrailResult.OverwriteAll;
            DialogResult = true;
            Close();
        }

        private void MergeOverwrite_Click(object sender, RoutedEventArgs e)
        {
            Result = GuardrailResult.MergeOverwrite;
            DialogResult = true;
            Close();
        }

        private void MergePreserve_Click(object sender, RoutedEventArgs e)
        {
            Result = GuardrailResult.MergePreserve;
            DialogResult = true;
            Close();
        }

        private void SaveAs_Click(object sender, RoutedEventArgs e)
        {
            Result = GuardrailResult.SaveAs;
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            Result = GuardrailResult.Cancel;
            DialogResult = false;
            Close();
        }
    }
}
```

### File: Shared/UI/IFileDialogService.cs
```csharp
namespace Synthetic.Shared.UI
{
    /// <summary>
    /// Service interface for extracting and showing file dialogs.
    /// </summary>
    public interface IFileDialogService
    {
        /// <summary>
        /// Displays a save file dialog to select a path.
        /// </summary>
        /// <param name="filter">The file extension filter string.</param>
        /// <param name="title">The dialog window title.</param>
        /// <param name="defaultFileName">The default pre-populated file name.</param>
        /// <returns>The chosen file path, or null if cancelled.</returns>
        string? SaveFileDialog(string filter, string title, string defaultFileName);

        /// <summary>
        /// Displays an open file dialog to select a path.
        /// </summary>
        /// <param name="filter">The file extension filter string.</param>
        /// <param name="title">The dialog window title.</param>
        /// <param name="defaultFileName">The default pre-populated file name.</param>
        /// <returns>The chosen file path, or null if cancelled.</returns>
        string? OpenFileDialog(string filter, string title, string defaultFileName);
    }
}
```

### File: Shared/UI/IGuardrailPromptService.cs
```csharp
namespace Synthetic.Shared.UI
{
    /// <summary>
    /// Represents the action choices when a master standards file overwrite is intercepted.
    /// </summary>
    public enum GuardrailResult
    {
        OverwriteAll,
        MergeOverwrite,
        MergePreserve,
        SaveAs,
        Cancel,
        Overwrite = OverwriteAll,
        Skip = Cancel
    }

    /// <summary>
    /// Service interface to prompt the user when attempting to write to a protected master standard file.
    /// </summary>
    public interface IGuardrailPromptService
    {
        /// <summary>
        /// Prompts the user with Overwrite, Save As, and Skip options for a protected file path.
        /// </summary>
        /// <param name="filePath">The protected file path being written to.</param>
        /// <returns>The user's choice.</returns>
        GuardrailResult PromptProtectedFileOverwrite(string filePath);
    }
}
```

### File: Shared/UI/ISingleItemSelectionViewModel.cs
```csharp
using System;

namespace Synthetic.Shared.UI
{
    /// <summary>
    /// Non-generic interface to facilitate interaction between non-generic WPF views/converters
    /// and the generic <see cref="SingleItemSelectionViewModel{T}"/>.
    /// </summary>
    public interface ISingleItemSelectionViewModel
    {
        /// <summary>
        /// Gets or sets the action to close the window, passing a boolean dialog result.
        /// </summary>
        Action<bool>? CloseAction { get; set; }

        /// <summary>
        /// Resolves the display name for a given item using the configured delegate.
        /// </summary>
        /// <param name="item">The item to resolve the display name for.</param>
        /// <returns>A string representation of the item.</returns>
        string GetItemDisplayName(object item);
    }
}
```

### File: Shared/UI/ISummaryDisplayService.cs
```csharp
using System;

namespace Synthetic.Shared.UI
{
    /// <summary>
    /// Service interface for displaying process/execution summaries.
    /// </summary>
    public interface ISummaryDisplayService
    {
        /// <summary>
        /// Displays the summary for a given view model.
        /// </summary>
        /// <param name="viewModel">The view model containing the summary data.</param>
        /// <param name="parentWindowHandle">The parent window handle.</param>
        void ShowSummary(object viewModel, IntPtr parentWindowHandle);
    }
}
```

### File: Shared/UI/IUserPromptService.cs
```csharp
using System;

namespace Synthetic.Shared.UI
{
    /// <summary>
    /// Defines a service for showing message prompts and asking for confirmation,
    /// decoupling ViewModels from native UI dialog dependencies (like MessageBox.Show).
    /// </summary>
    public interface IUserPromptService
    {
        /// <summary>
        /// Displays an informational message to the user.
        /// </summary>
        /// <param name="message">The message text.</param>
        /// <param name="title">The window title.</param>
        void ShowMessage(string message, string title);

        /// <summary>
        /// Prompts the user with a Yes/No question and returns true if they choose Yes.
        /// </summary>
        /// <param name="message">The question text.</param>
        /// <param name="title">The window title.</param>
        /// <returns>True if the user confirmed/clicked Yes, otherwise false.</returns>
        bool ConfirmAction(string message, string title);
    }
}
```

### File: Shared/UI/ListByCheckboxView.xaml
```xml
<Window x:Class="Synthetic.Shared.UI.ListByCheckboxView"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:local="clr-namespace:Synthetic.Shared.UI"
        Title="{Binding Title}" Height="400" Width="500" MinHeight="300" MinWidth="400" 
        WindowStartupLocation="CenterOwner" ResizeMode="CanResizeWithGrip" ShowInTaskbar="False"
        Style="{DynamicResource SyntheticWindowStyle}"
        local:WindowChromeBehavior.EnableWindowCommands="True">

    <Window.Resources>
        <ResourceDictionary>
            <ResourceDictionary.MergedDictionaries>
                <ResourceDictionary Source="SyntheticTheme.xaml"/>
            </ResourceDictionary.MergedDictionaries>
        </ResourceDictionary>
    </Window.Resources>

    <Grid Margin="15">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/> <!-- Title / Instruction -->
            <RowDefinition Height="*"/>    <!-- Items ListBox -->
            <RowDefinition Height="Auto"/> <!-- Bottom Buttons -->
        </Grid.RowDefinitions>

        <!-- Header Title and Description -->
        <StackPanel Grid.Row="0" Margin="0,0,0,15">
            <TextBlock Text="{Binding Title}" FontSize="16" FontWeight="SemiBold" Foreground="{DynamicResource Synthetic.Brushes.TextPrimary}" Margin="0,0,0,5" TextWrapping="Wrap"/>
            <TextBlock Text="{Binding Instruction}" FontSize="12" Foreground="{DynamicResource Synthetic.Brushes.TextSecondary}" TextWrapping="Wrap" Visibility="{Binding InstructionVisibility}"/>
        </StackPanel>

        <!-- List Area -->
        <Border Grid.Row="1" BorderBrush="{DynamicResource Synthetic.Brushes.BorderNormal}" BorderThickness="1" Margin="0,0,0,15">
            <ListBox ItemsSource="{Binding Items}" HorizontalContentAlignment="Stretch" BorderThickness="0" ScrollViewer.HorizontalScrollBarVisibility="Disabled">
                <ListBox.ItemTemplate>
                    <DataTemplate>
                        <CheckBox IsChecked="{Binding IsChecked}" Content="{Binding Name}" Margin="5,4" VerticalContentAlignment="Center"/>
                    </DataTemplate>
                </ListBox.ItemTemplate>
            </ListBox>
        </Border>

        <!-- Bottom Controls -->
        <Grid Grid.Row="2">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="*"/>
                <ColumnDefinition Width="Auto"/>
            </Grid.ColumnDefinitions>
            
            <!-- Selection Controls (Left) -->
            <StackPanel Grid.Column="0" Orientation="Horizontal">
                <Button Content="Select All" Command="{Binding SelectAllCommand}" Margin="0,0,8,0"
                        Visibility="{Binding SelectAllVisibility}"/>
                <Button Content="Select None" Command="{Binding SelectNoneCommand}"/>
            </StackPanel>

            <!-- Actions (Right) -->
            <StackPanel Grid.Column="1" Orientation="Horizontal">
                <Button Content="OK" Command="{Binding OkCommand}" IsDefault="True" Style="{DynamicResource Synthetic.Styles.PrimaryButton}" Margin="0,0,10,0"/>
                <Button Content="Cancel" Command="{Binding CancelCommand}" IsCancel="True"/>
            </StackPanel>
        </Grid>
    </Grid>
</Window>
```

### File: Shared/UI/ListByCheckboxView.xaml.cs
```csharp
using System;
using System.Windows;

using Synthetic.Shared.UI;
using Synthetic.Core;

using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.Modules.RevitDOM;

namespace Synthetic.Shared.UI
{
    /// <summary>
    /// Interaction logic for ListByCheckboxView.xaml.
    /// </summary>
    public partial class ListByCheckboxView : Window
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ListByCheckboxView"/> class.
        /// </summary>
        /// <param name="mainWindowHandle">The handle to the main Revit application window, used to set the owner of this window.</param>
        public ListByCheckboxView(IntPtr mainWindowHandle)
        {
            InitializeComponent();
            RevitWindowHelper.SetOwner(this, mainWindowHandle);

            DataContextChanged += (sender, args) =>
            {
                if (DataContext is ListByCheckboxViewModel vm)
                {
                    vm.CloseAction = (dialogResult) =>
                    {
                        try
                        {
                            this.DialogResult = dialogResult;
                        }
                        catch (InvalidOperationException)
                        {
                            // Window is closing/closed
                        }
                        this.Close();
                    };
                }
            };
        }
    }
}
```

### File: Shared/UI/ListByCheckboxViewModel.cs
```csharp
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using Autodesk.Revit.DB;

using Synthetic.Shared.UI;

using Synthetic.Modules.RevitDOM;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.Shared.RevitAPI;
namespace Synthetic.Shared.UI
{
    /// <summary>
    /// ViewModel for lists where items can be selected using checkboxes.
    /// </summary>
    public class ListByCheckboxViewModel : ViewModelBase
    {
        private string _title = "Title";
        private string _instruction = "Instructions";
        private ObservableCollection<CheckableItem> _items = new ObservableCollection<CheckableItem>();
        private bool _isSorted = true;
        private bool _isSingleSelection;
        private bool _isUpdating;

        /// <summary>
        /// Initializes a new instance of the <see cref="ListByCheckboxViewModel"/> class.
        /// </summary>
        public ListByCheckboxViewModel()
        {
            OkCommand = new RelayCommand(OnOk, CanOk);
            CancelCommand = new RelayCommand(OnCancel);
            SelectAllCommand = new RelayCommand(OnSelectAll, CanSelectAll);
            SelectNoneCommand = new RelayCommand(OnSelectNone, CanSelectNone);
        }

        /// <summary>
        /// Gets or sets the title of the view.
        /// </summary>
        public string Title
        {
            get => _title;
            set => SetProperty(ref _title, value);
        }

        /// <summary>
        /// Gets or sets the instructions shown to the user.
        /// </summary>
        public string Instruction
        {
            get => _instruction;
            set
            {
                if (SetProperty(ref _instruction, value))
                {
                    OnPropertyChanged(nameof(InstructionVisibility));
                }
            }
        }

        /// <summary>
        /// Gets the visibility status of the instructions.
        /// </summary>
        public System.Windows.Visibility InstructionVisibility => 
            string.IsNullOrEmpty(Instruction) ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;

        /// <summary>
        /// Gets or sets a value indicating whether items are sorted alphabetically.
        /// </summary>
        public bool IsSorted
        {
            get => _isSorted;
            set => SetProperty(ref _isSorted, value);
        }

        /// <summary>
        /// Gets or sets a value indicating whether only one item can be selected at a time.
        /// </summary>
        public bool IsSingleSelection
        {
            get => _isSingleSelection;
            set
            {
                if (SetProperty(ref _isSingleSelection, value))
                {
                    OnPropertyChanged(nameof(SelectAllVisibility));
                }
            }
        }

        /// <summary>
        /// Gets the visibility status of the select all command.
        /// </summary>
        public System.Windows.Visibility SelectAllVisibility =>
            IsSingleSelection ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;

        /// <summary>
        /// Gets or sets the observable collection of checkable items.
        /// </summary>
        public ObservableCollection<CheckableItem> Items
        {
            get => _items;
            set => SetProperty(ref _items, value);
        }

        /// <summary>
        /// Gets the list of names of items that are checked.
        /// </summary>
        public List<string> CheckedItems
        {
            get
            {
                return Items.Where(i => i.IsChecked).Select(i => i.Name).ToList();
            }
        }

        /// <summary>
        /// Gets the list of ElementIds of items that are checked.
        /// </summary>
        public List<ElementId> CheckedElementIds
        {
            get
            {
                return Items.Where(i => i.IsChecked && i.Value != null).Select(i => i.Value!).ToList();
            }
        }

        /// <summary>
        /// Sets the checkable items from a list of item names.
        /// </summary>
        /// <param name="itemNames">The collection of item names.</param>
        /// <param name="checkAll">Whether to check all items by default.</param>
        public void SetItems(IEnumerable<string> itemNames, bool checkAll = false)
        {
            _isUpdating = true;
            Items.Clear();
            
            var list = itemNames ?? new List<string>();
            if (IsSorted)
            {
                list = list.OrderBy(x => x).ToList();
            }

            foreach (var name in list)
            {
                Items.Add(new CheckableItem(name, checkAll, OnItemCheckChanged));
            }
            _isUpdating = false;
        }

        /// <summary>
        /// Sets the checkable items from a list of tuples containing item names and ElementIds.
        /// </summary>
        /// <param name="items">The collection of tuples containing name and ElementId.</param>
        /// <param name="checkAll">Whether to check all items by default.</param>
        public void SetItems(IEnumerable<Tuple<string, ElementId>> items, bool checkAll = false)
        {
            _isUpdating = true;
            Items.Clear();

            var list = items ?? new List<Tuple<string, ElementId>>();
            if (IsSorted)
            {
                list = list.OrderBy(x => x.Item1).ToList();
            }

            foreach (var item in list)
            {
                Items.Add(new CheckableItem(item.Item1, item.Item2, checkAll, OnItemCheckChanged));
            }
            _isUpdating = false;
        }

        private void OnItemCheckChanged(CheckableItem changedItem)
        {
            if (_isUpdating) return;

            if (IsSingleSelection && changedItem.IsChecked)
            {
                _isUpdating = true;
                foreach (var item in Items)
                {
                    if (item != changedItem)
                    {
                        item.IsChecked = false;
                    }
                }
                _isUpdating = false;
            }
        }

        /// <summary>
        /// Gets or sets the action to close the window, passing a boolean result.
        /// </summary>
        public Action<bool>? CloseAction { get; set; }

        /// <summary>
        /// Gets the OK command.
        /// </summary>
        public ICommand OkCommand { get; }

        /// <summary>
        /// Gets the Cancel command.
        /// </summary>
        public ICommand CancelCommand { get; }

        /// <summary>
        /// Gets the Select All command.
        /// </summary>
        public ICommand SelectAllCommand { get; }

        /// <summary>
        /// Gets the Select None command.
        /// </summary>
        public ICommand SelectNoneCommand { get; }

        private bool CanOk(object parameter)
        {
            return Items.Any(i => i.IsChecked);
        }

        private void OnOk(object parameter)
        {
            CloseAction?.Invoke(true);
        }

        private void OnCancel(object parameter)
        {
            CloseAction?.Invoke(false);
        }

        private bool CanSelectAll(object parameter) => !IsSingleSelection;
        private void OnSelectAll(object parameter)
        {
            _isUpdating = true;
            foreach (var item in Items)
            {
                item.IsChecked = true;
            }
            _isUpdating = false;
        }

        private bool CanSelectNone(object parameter) => true;
        private void OnSelectNone(object parameter)
        {
            _isUpdating = true;
            foreach (var item in Items)
            {
                item.IsChecked = false;
            }
            _isUpdating = false;
        }
    }

    /// <summary>
    /// Represents an item in a checklist that can be checked or unchecked.
    /// </summary>
    public class CheckableItem : ViewModelBase
    {
        private string _name = string.Empty;
        private ElementId? _value;
        private bool _isChecked;
        private readonly Action<CheckableItem>? _onCheckChanged;

        /// <summary>
        /// Initializes a new instance of the <see cref="CheckableItem"/> class.
        /// </summary>
        /// <param name="name">The display name of the item.</param>
        /// <param name="isChecked">The initial check state of the item.</param>
        /// <param name="onCheckChanged">Action to invoke when checked status changes.</param>
        public CheckableItem(string name, bool isChecked = false, Action<CheckableItem>? onCheckChanged = null)
        {
            _name = name;
            _isChecked = isChecked;
            _onCheckChanged = onCheckChanged;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="CheckableItem"/> class.
        /// </summary>
        /// <param name="name">The display name of the item.</param>
        /// <param name="value">The ElementId associated with the item.</param>
        /// <param name="isChecked">The initial check state of the item.</param>
        /// <param name="onCheckChanged">Action to invoke when checked status changes.</param>
        public CheckableItem(string name, ElementId? value, bool isChecked = false, Action<CheckableItem>? onCheckChanged = null)
        {
            _name = name;
            _value = value;
            _isChecked = isChecked;
            _onCheckChanged = onCheckChanged;
        }

        /// <summary>
        /// Gets or sets the display name of the item.
        /// </summary>
        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }

        /// <summary>
        /// Gets or sets the ElementId value associated with the item.
        /// </summary>
        public ElementId? Value
        {
            get => _value;
            set => SetProperty(ref _value, value);
        }

        /// <summary>
        /// Gets or sets a value indicating whether the item is checked.
        /// </summary>
        public bool IsChecked
        {
            get => _isChecked;
            set
            {
                if (SetProperty(ref _isChecked, value))
                {
                    _onCheckChanged?.Invoke(this);
                }
            }
        }
    }
}
```

### File: Shared/UI/ProgressCoordinator.cs
```csharp
using System;
using System.Threading;
using System.Windows.Threading;

using Synthetic.Shared.UI;

using Synthetic.Core;
using Synthetic.Modules.RevitDOM;
using Synthetic.Modules.StandardsManagement.ViewModels;
namespace Synthetic.Shared.UI
{
    /// <summary>
    /// Service coordinator to manage progress dialog lifecycle and state.
    /// Exposes static methods for initializing, updating, checking cancellation, and closing.
    /// </summary>
    public static class ProgressCoordinator
    {
        public static bool SuppressUI { get; set; } = false;
        public static bool ForceCancel { get; set; } = false;

        private static SharedProgressViewModel? _viewModel;
        private static SharedProgressWindow? _window;
        private static CancellationTokenSource? _cts;

        /// <summary>
        /// Gets the cancellation token.
        /// </summary>
        public static CancellationToken Token
        {
            get
            {
                if (ForceCancel)
                {
                    var cts = new CancellationTokenSource();
                    cts.Cancel();
                    return cts.Token;
                }
                return _cts?.Token ?? CancellationToken.None;
            }
        }

        /// <summary>
        /// Initializes the progress indicator: instantiates the ViewModel, opens the Window modelessly,
        /// and resets the cancellation token source.
        /// </summary>
        /// <param name="title">Title of the progress dialog.</param>
        /// <param name="taskDescription">Main task description message.</param>
        /// <param name="totalItems">Total count of elements to process.</param>
        public static void Initialize(string title, string taskDescription, int totalItems)
        {
            // Safeguard: close any existing tracking window/resources first.
            Close();

            _cts = new CancellationTokenSource();
            if (SuppressUI) return;

            _viewModel = new SharedProgressViewModel(_cts)
            {
                WindowTitle = title,
                MainTaskDescription = taskDescription,
                MaximumValue = totalItems,
                CurrentValue = 0,
                CurrentItemName = "Starting..."
            };

            // Retrieve active Revit window handle using AppControlled if available; otherwise use active Process main window.
            IntPtr ownerHandle = IntPtr.Zero;
            if (App.AppControlled != null)
            {
                ownerHandle = App.AppControlled.MainWindowHandle;
            }
            else
            {
                ownerHandle = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
            }

            _window = new SharedProgressWindow(ownerHandle, _viewModel);
            _window.Show();

            AllowUIToUpdate();
        }

        /// <summary>
        /// Increments the current progress value by 1 and updates the current item status text.
        /// </summary>
        /// <param name="currentItemName">The name of the item currently being processed.</param>
        public static void UpdateProgress(string currentItemName)
        {
            if (_viewModel != null)
            {
                _viewModel.CurrentValue += 1;
                _viewModel.CurrentItemName = currentItemName;
                AllowUIToUpdate();
            }
        }

        /// <summary>
        /// Updates the status text without incrementing the progress value.
        /// Useful for intermediate step status updates within a single loop iteration.
        /// </summary>
        /// <param name="status">The status text to show.</param>
        public static void UpdateStatus(string status)
        {
            if (_viewModel != null)
            {
                _viewModel.CurrentItemName = status;
                AllowUIToUpdate();
            }
        }

        /// <summary>
        /// Checks if cancellation has been requested by the user.
        /// </summary>
        /// <returns>True if cancellation was requested, false otherwise.</returns>
        public static bool IsCancelled()
        {
            if (ForceCancel) return true;
            return _viewModel?.IsCancellationRequested ?? false;
        }

        /// <summary>
        /// Safely disposes token sources and closes the progress dialog.
        /// </summary>
        public static void Close()
        {
            if (_window != null)
            {
                try
                {
                    _window.Dispatcher.Invoke(() =>
                    {
                        try
                        {
                            _window.Close();
                        }
                        catch { }
                    });
                }
                catch { }
                _window = null;
            }

            if (_cts != null)
            {
                try
                {
                    _cts.Cancel();
                }
                catch { }
                try
                {
                    _cts.Dispose();
                }
                catch { }
                _cts = null;
            }

            _viewModel = null;
        }

        /// <summary>
        /// Force pumps the WPF Dispatcher queue to allow UI redraw events to process immediately,
        /// preventing the progress window from freezing on Revit's main thread.
        /// </summary>
        private static void AllowUIToUpdate()
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
            try
            {
                dispatcher.Invoke(DispatcherPriority.Background, new Action(delegate { }));
            }
            catch { }
        }
    }
}
```

### File: Shared/UI/RelayCommand.cs
```csharp
using System;
using System.Windows.Input;
using Synthetic.Shared.UI;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.Modules.RevitDOM;
namespace Synthetic.Shared.UI{
    /// <summary>
    /// A command implementation that relays its functionality by invoking delegates.
    /// </summary>
    public class RelayCommand : ICommand
    {
        private readonly Action<object> _execute;
        private readonly Predicate<object>? _canExecute;

        /// <summary>
        /// Initializes a new instance of the <see cref="RelayCommand"/> class.
        /// </summary>
        /// <param name="execute">The execution logic delegate.</param>
        /// <param name="canExecute">The execution status logic delegate.</param>
        public RelayCommand(Action<object> execute, Predicate<object>? canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        /// <summary>
        /// Event raised when the command execution status changes.
        /// </summary>
        public event EventHandler? CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }

        /// <summary>
        /// Determines whether the command can execute in its current state.
        /// </summary>
        /// <param name="parameter">Data used by the command.</param>
        /// <returns>True if the command can execute, false otherwise.</returns>
        public bool CanExecute(object? parameter)
        {
            return _canExecute == null || _canExecute(parameter!);
        }

        /// <summary>
        /// Executes the command.
        /// </summary>
        /// <param name="parameter">Data used by the command.</param>
        public void Execute(object? parameter)
        {
            _execute(parameter!);
        }
    }
}
```

### File: Shared/UI/RevitWindowHelper.cs
```csharp
using System;
using System.Windows;
using System.Windows.Interop;
using Synthetic.Shared.UI;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.Modules.RevitDOM;
namespace Synthetic.Shared.UI{
    /// <summary>
    /// Helper methods for managing WPF window parenting/owners within Revit.
    /// </summary>
    public static class RevitWindowHelper
    {
        /// <summary>
        /// Sets the parent owner window of a WPF window to the main Revit application window.
        /// </summary>
        /// <param name="wpfWindow">The WPF window to parent.</param>
        /// <param name="revitMainWindowHandle">The window handle of the main Revit window.</param>
        public static void SetOwner(Window wpfWindow, IntPtr revitMainWindowHandle)
        {
            if (wpfWindow == null)
                throw new ArgumentNullException(nameof(wpfWindow));
            if (revitMainWindowHandle == IntPtr.Zero)
                return;
            WindowInteropHelper helper = new WindowInteropHelper(wpfWindow)
            {
                Owner = revitMainWindowHandle
            };
        }
    }
}
```

### File: Shared/UI/SelectSearchPathsView.xaml
```xml
<Window x:Class="Synthetic.Shared.UI.SelectSearchPathsView"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:local="clr-namespace:Synthetic.Shared.UI"
        Title="{Binding Title}" Height="500" Width="600" MinHeight="400" MinWidth="500" 
        WindowStartupLocation="CenterOwner" ResizeMode="CanResizeWithGrip" ShowInTaskbar="False"
        Style="{DynamicResource SyntheticWindowStyle}"
        local:WindowChromeBehavior.EnableWindowCommands="True">

    <Window.Resources>
        <ResourceDictionary>
            <ResourceDictionary.MergedDictionaries>
                <ResourceDictionary Source="SyntheticTheme.xaml"/>
            </ResourceDictionary.MergedDictionaries>
        </ResourceDictionary>
    </Window.Resources>

    <Grid Margin="15">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/> <!-- Title / Instruction -->
            <RowDefinition Height="*"/>    <!-- User Paths Section -->
            <RowDefinition Height="*"/>    <!-- Default Paths Section -->
            <RowDefinition Height="Auto"/> <!-- Actions -->
        </Grid.RowDefinitions>

        <!-- Header Title and Description -->
        <StackPanel Grid.Row="0" Margin="0,0,0,15">
            <TextBlock Text="{Binding Title}" FontSize="16" FontWeight="SemiBold" Foreground="{DynamicResource Synthetic.Brushes.TextPrimary}" Margin="0,0,0,5" TextWrapping="Wrap"/>
            <TextBlock Text="{Binding Instruction}" FontSize="12" Foreground="{DynamicResource Synthetic.Brushes.TextSecondary}" TextWrapping="Wrap" Visibility="{Binding InstructionVisibility}"/>
        </StackPanel>

        <!-- User Selected Paths Section -->
        <GroupBox Grid.Row="1" Header="User Selected Paths" Margin="0,0,0,10" Padding="8"
                  Foreground="{DynamicResource Synthetic.Brushes.TextPrimary}"
                  BorderBrush="{DynamicResource Synthetic.Brushes.BorderNormal}"
                  Background="{DynamicResource Synthetic.Brushes.ControlSurface}">
            <Grid>
                <Grid.RowDefinitions>
                    <RowDefinition Height="*"/>
                    <RowDefinition Height="Auto"/>
                </Grid.RowDefinitions>
                
                <Border Grid.Row="0" BorderBrush="{DynamicResource Synthetic.Brushes.BorderNormal}" BorderThickness="1" Margin="0,0,0,8">
                    <ListBox ItemsSource="{Binding UserPaths}" HorizontalContentAlignment="Stretch" BorderThickness="0" ScrollViewer.HorizontalScrollBarVisibility="Disabled">
                        <ListBox.ItemTemplate>
                            <DataTemplate>
                                <CheckBox IsChecked="{Binding IsChecked}" Content="{Binding Name}" Margin="5,3" VerticalContentAlignment="Center"/>
                            </DataTemplate>
                        </ListBox.ItemTemplate>
                    </ListBox>
                </Border>
                
                <Grid Grid.Row="1">
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="*"/>
                        <ColumnDefinition Width="Auto"/>
                    </Grid.ColumnDefinitions>
                    <StackPanel Grid.Column="0" Orientation="Horizontal">
                        <Button Content="Select All" Command="{Binding SelectAllUserPathsCommand}" Margin="0,0,8,0"/>
                        <Button Content="Select None" Command="{Binding SelectNoneUserPathsCommand}"/>
                    </StackPanel>
                    <Button Grid.Column="1" Content="Add Path..." Command="{Binding AddPathCommand}" 
                            CommandParameter="{Binding RelativeSource={RelativeSource AncestorType=Window}}"/>
                </Grid>
            </Grid>
        </GroupBox>

        <!-- Default Paths Section -->
        <GroupBox Grid.Row="2" Header="Default Paths" Margin="0,0,0,15" Padding="8"
                  Foreground="{DynamicResource Synthetic.Brushes.TextPrimary}"
                  BorderBrush="{DynamicResource Synthetic.Brushes.BorderNormal}"
                  Background="{DynamicResource Synthetic.Brushes.ControlSurface}">
            <Grid>
                <Grid.RowDefinitions>
                    <RowDefinition Height="*"/>
                    <RowDefinition Height="Auto"/>
                </Grid.RowDefinitions>

                <Border Grid.Row="0" BorderBrush="{DynamicResource Synthetic.Brushes.BorderNormal}" BorderThickness="1" Margin="0,0,0,8">
                    <ListBox ItemsSource="{Binding DefaultPaths}" HorizontalContentAlignment="Stretch" BorderThickness="0" ScrollViewer.HorizontalScrollBarVisibility="Disabled">
                        <ListBox.ItemTemplate>
                            <DataTemplate>
                                <CheckBox IsChecked="{Binding IsChecked}" Content="{Binding Name}" Margin="5,3" VerticalContentAlignment="Center"/>
                            </DataTemplate>
                        </ListBox.ItemTemplate>
                    </ListBox>
                </Border>

                <StackPanel Grid.Row="1" Orientation="Horizontal">
                    <Button Content="Select All" Command="{Binding SelectAllDefaultPathsCommand}" Margin="0,0,8,0"/>
                    <Button Content="Select None" Command="{Binding SelectNoneDefaultPathsCommand}"/>
                </StackPanel>
            </Grid>
        </GroupBox>

        <!-- Actions -->
        <StackPanel Grid.Row="3" Orientation="Horizontal" HorizontalAlignment="Right">
            <Button Content="OK" Command="{Binding OkCommand}" IsDefault="True" Style="{DynamicResource Synthetic.Styles.PrimaryButton}" Margin="0,0,10,0"/>
            <Button Content="Cancel" Command="{Binding CancelCommand}" IsCancel="True"/>
        </StackPanel>
    </Grid>
</Window>
```

### File: Shared/UI/SelectSearchPathsView.xaml.cs
```csharp
using System;
using System.Windows;

using Synthetic.Shared.UI;
using Synthetic.Core;

using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.Modules.RevitDOM;

namespace Synthetic.Shared.UI
{
    /// <summary>
    /// Interaction logic for SelectSearchPathsView.xaml.
    /// </summary>
    public partial class SelectSearchPathsView : Window
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SelectSearchPathsView"/> class.
        /// </summary>
        /// <param name="mainWindowHandle">The handle to the main Revit application window, used to set the owner of this window.</param>
        public SelectSearchPathsView(IntPtr mainWindowHandle)
        {
            InitializeComponent();
            RevitWindowHelper.SetOwner(this, mainWindowHandle);

            DataContextChanged += (sender, args) =>
            {
                if (DataContext is SelectSearchPathsViewModel vm)
                {
                    vm.CloseAction = (dialogResult) =>
                    {
                        try
                        {
                            this.DialogResult = dialogResult;
                        }
                        catch (InvalidOperationException)
                        {
                            // Window is closing/closed
                        }
                        this.Close();
                    };
                }
            };
        }
    }
}
```

### File: Shared/UI/SelectSearchPathsViewModel.cs
```csharp
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

using Synthetic.Shared.UI;
using Synthetic.Core;

using Synthetic.Modules.RevitDOM;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.Shared.RevitAPI;
namespace Synthetic.Shared.UI
{
    /// <summary>
    /// ViewModel for selecting search paths.
    /// </summary>
    public class SelectSearchPathsViewModel : ViewModelBase
    {
        private string _title = "Select Search Paths";
        private string _instruction = "Add and select the folder paths that you want to search for files.";
        private ObservableCollection<CheckableItem> _userPaths = new ObservableCollection<CheckableItem>();
        private ObservableCollection<CheckableItem> _defaultPaths = new ObservableCollection<CheckableItem>();

        /// <summary>
        /// Initializes a new instance of the <see cref="SelectSearchPathsViewModel"/> class.
        /// </summary>
        public SelectSearchPathsViewModel()
        {
            OkCommand = new RelayCommand(OnOk);
            CancelCommand = new RelayCommand(OnCancel);
            AddPathCommand = new RelayCommand(OnAddPath);
            SelectAllUserPathsCommand = new RelayCommand(OnSelectAllUserPaths);
            SelectNoneUserPathsCommand = new RelayCommand(OnSelectNoneUserPaths);
            SelectAllDefaultPathsCommand = new RelayCommand(OnSelectAllDefaultPaths);
            SelectNoneDefaultPathsCommand = new RelayCommand(OnSelectNoneDefaultPaths);
        }

        /// <summary>
        /// Gets or sets the title of the window.
        /// </summary>
        public string Title
        {
            get => _title;
            set => SetProperty(ref _title, value);
        }

        /// <summary>
        /// Gets or sets the instruction text displayed to the user.
        /// </summary>
        public string Instruction
        {
            get => _instruction;
            set
            {
                if (SetProperty(ref _instruction, value))
                {
                    OnPropertyChanged(nameof(InstructionVisibility));
                }
            }
        }

        /// <summary>
        /// Gets the visibility status of the instruction text.
        /// </summary>
        public Visibility InstructionVisibility =>
            string.IsNullOrEmpty(Instruction) ? Visibility.Collapsed : Visibility.Visible;

        /// <summary>
        /// Gets or sets the list of search paths added by the user.
        /// </summary>
        public ObservableCollection<CheckableItem> UserPaths
        {
            get => _userPaths;
            set => SetProperty(ref _userPaths, value);
        }

        /// <summary>
        /// Gets or sets the list of default search paths.
        /// </summary>
        public ObservableCollection<CheckableItem> DefaultPaths
        {
            get => _defaultPaths;
            set => SetProperty(ref _defaultPaths, value);
        }

        /// <summary>
        /// Gets the list of checked search path strings.
        /// </summary>
        public List<string> CheckedItems
        {
            get
            {
                var result = new List<string>();
                result.AddRange(UserPaths.Where(x => x.IsChecked).Select(x => x.Name));
                result.AddRange(DefaultPaths.Where(x => x.IsChecked).Select(x => x.Name));
                return result;
            }
        }

        /// <summary>
        /// Checks all user search paths.
        /// </summary>
        public void CheckAllItems()
        {
            foreach (var item in UserPaths)
            {
                item.IsChecked = true;
            }
        }

        /// <summary>
        /// Checks all default search paths.
        /// </summary>
        public void CheckAllDefaults()
        {
            foreach (var item in DefaultPaths)
            {
                item.IsChecked = true;
            }
        }

        /// <summary>
        /// Gets or sets the action to close the window, passing a boolean result.
        /// </summary>
        public Action<bool>? CloseAction { get; set; }

        /// <summary>
        /// Gets the OK command.
        /// </summary>
        public ICommand OkCommand { get; }

        /// <summary>
        /// Gets the Cancel command.
        /// </summary>
        public ICommand CancelCommand { get; }

        /// <summary>
        /// Gets the Add Path command.
        /// </summary>
        public ICommand AddPathCommand { get; }

        /// <summary>
        /// Gets the command to select all user paths.
        /// </summary>
        public ICommand SelectAllUserPathsCommand { get; }

        /// <summary>
        /// Gets the command to deselect all user paths.
        /// </summary>
        public ICommand SelectNoneUserPathsCommand { get; }

        /// <summary>
        /// Gets the command to select all default paths.
        /// </summary>
        public ICommand SelectAllDefaultPathsCommand { get; }

        /// <summary>
        /// Gets the command to deselect all default paths.
        /// </summary>
        public ICommand SelectNoneDefaultPathsCommand { get; }

        private void OnOk(object parameter)
        {
            CloseAction?.Invoke(true);
        }

        private void OnCancel(object parameter)
        {
            CloseAction?.Invoke(false);
        }

        private void OnAddPath(object parameter)
        {
            IntPtr ownerHandle = IntPtr.Zero;
            if (parameter is Window window)
            {
                ownerHandle = new WindowInteropHelper(window).Handle;
            }

            string selected = FileDialogHelper.SelectFolder(ownerHandle, "Select Search Path to Add");
            if (!string.IsNullOrEmpty(selected))
            {
                // Prevent adding duplicate paths to the user list
                if (!UserPaths.Any(x => string.Equals(x.Name, selected, StringComparison.OrdinalIgnoreCase)))
                {
                    UserPaths.Add(new CheckableItem(selected, true));
                }
            }
        }

        private void OnSelectAllUserPaths(object parameter)
        {
            foreach (var item in UserPaths)
            {
                item.IsChecked = true;
            }
        }

        private void OnSelectNoneUserPaths(object parameter)
        {
            foreach (var item in UserPaths)
            {
                item.IsChecked = false;
            }
        }

        private void OnSelectAllDefaultPaths(object parameter)
        {
            foreach (var item in DefaultPaths)
            {
                item.IsChecked = true;
            }
        }

        private void OnSelectNoneDefaultPaths(object parameter)
        {
            foreach (var item in DefaultPaths)
            {
                item.IsChecked = false;
            }
        }
    }
}
```

### File: Shared/UI/SharedProgressViewModel.cs
```csharp
using System;
using System.Threading;
using System.Windows.Input;

using Synthetic.Shared.UI;
using Synthetic.Modules.RevitDOM;
using Synthetic.Modules.StandardsManagement.ViewModels;
namespace Synthetic.Shared.UI
{
    /// <summary>
    /// ViewModel for universal progress tracking.
    /// Implements properties for task description, progress values, and cancel command.
    /// </summary>
    public class SharedProgressViewModel : ViewModelBase
    {
        private readonly CancellationTokenSource _cts;

        private string _windowTitle = "Processing...";
        /// <summary>
        /// Gets or sets the title of the progress window.
        /// </summary>
        public string WindowTitle
        {
            get => _windowTitle;
            set => SetProperty(ref _windowTitle, value);
        }

        private string _mainTaskDescription = "Starting...";
        /// <summary>
        /// Gets or sets the main task description message.
        /// </summary>
        public string MainTaskDescription
        {
            get => _mainTaskDescription;
            set => SetProperty(ref _mainTaskDescription, value);
        }

        private string _currentItemName = "";
        /// <summary>
        /// Gets or sets the name of the current item being processed.
        /// </summary>
        public string CurrentItemName
        {
            get => _currentItemName;
            set => SetProperty(ref _currentItemName, value);
        }

        private double _maximumValue = 100.0;
        /// <summary>
        /// Gets or sets the total number of items to process.
        /// </summary>
        public double MaximumValue
        {
            get => _maximumValue;
            set
            {
                if (SetProperty(ref _maximumValue, value))
                {
                    OnPropertyChanged(nameof(ProgressPercentage));
                }
            }
        }

        private double _currentValue = 0.0;
        /// <summary>
        /// Gets or sets the number of items processed so far.
        /// </summary>
        public double CurrentValue
        {
            get => _currentValue;
            set
            {
                if (SetProperty(ref _currentValue, value))
                {
                    OnPropertyChanged(nameof(ProgressPercentage));
                }
            }
        }

        /// <summary>
        /// Gets the calculated progress percentage (0 to 100).
        /// </summary>
        public double ProgressPercentage => MaximumValue > 0.0 ? (CurrentValue / MaximumValue) * 100.0 : 0.0;

        private bool _isCancellationRequested;
        /// <summary>
        /// Gets or sets a value indicating whether cancellation has been requested.
        /// </summary>
        public bool IsCancellationRequested
        {
            get => _isCancellationRequested;
            set => SetProperty(ref _isCancellationRequested, value);
        }

        private ICommand? _cancelCommand;
        /// <summary>
        /// Gets the command executed to cancel the operation.
        /// </summary>
        public ICommand CancelCommand => _cancelCommand ??= new RelayCommand(_ => Cancel());

        /// <summary>
        /// Initializes a new instance of the <see cref="SharedProgressViewModel"/> class.
        /// </summary>
        /// <param name="cts">The cancellation token source to trigger when cancelling.</param>
        public SharedProgressViewModel(CancellationTokenSource cts)
        {
            _cts = cts ?? throw new ArgumentNullException(nameof(cts));
        }

        private void Cancel()
        {
            if (!IsCancellationRequested)
            {
                IsCancellationRequested = true;
                MainTaskDescription = "Cancelling...";
                try
                {
                    _cts.Cancel();
                }
                catch (ObjectDisposedException) { }
            }
        }
    }
}
```

### File: Shared/UI/SharedProgressWindow.xaml
```xml
<Window x:Class="Synthetic.Shared.UI.SharedProgressWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:local="clr-namespace:Synthetic.Shared.UI"
        Title="{Binding WindowTitle}" SizeToContent="WidthAndHeight" MinHeight="180" MinWidth="400" 
        WindowStartupLocation="CenterOwner" ResizeMode="NoResize"
        Style="{DynamicResource SyntheticWindowStyle}"
        local:WindowChromeBehavior.EnableWindowCommands="True">

    <Window.Resources>
        <ResourceDictionary>
            <ResourceDictionary.MergedDictionaries>
                <ResourceDictionary Source="SyntheticTheme.xaml"/>
            </ResourceDictionary.MergedDictionaries>
            
            <Style TargetType="ProgressBar">
                <Setter Property="Background" Value="{DynamicResource Synthetic.Brushes.ControlSurface}"/>
                <Setter Property="Foreground" Value="{DynamicResource Synthetic.Brushes.AccentActive}"/>
                <Setter Property="BorderBrush" Value="{DynamicResource Synthetic.Brushes.BorderNormal}"/>
                <Setter Property="BorderThickness" Value="1"/>
                <Setter Property="Template">
                    <Setter.Value>
                        <ControlTemplate TargetType="ProgressBar">
                            <Grid x:Name="TemplateRoot">
                                <Border x:Name="ProgressBarTrack" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="{TemplateBinding BorderThickness}" CornerRadius="2"/>
                                <Border x:Name="PART_Indicator" Background="{TemplateBinding Foreground}" HorizontalAlignment="Left" CornerRadius="1"/>
                            </Grid>
                        </ControlTemplate>
                    </Setter.Value>
                </Setter>
            </Style>
        </ResourceDictionary>
    </Window.Resources>
    
    <Grid Margin="20">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/> <!-- Main Task Description -->
            <RowDefinition Height="Auto"/> <!-- Current Item Name -->
            <RowDefinition Height="Auto"/> <!-- ProgressBar -->
            <RowDefinition Height="*"/>    <!-- Cancel Button -->
        </Grid.RowDefinitions>
        
        <TextBlock Text="{Binding MainTaskDescription}" Grid.Row="0" 
                   TextWrapping="Wrap" Margin="0,0,0,5" Foreground="{DynamicResource Synthetic.Brushes.TextPrimary}" FontSize="12" FontWeight="Bold"/>

        <TextBlock Text="{Binding CurrentItemName}" Grid.Row="1" 
                   TextWrapping="Wrap" Margin="0,0,0,15" Foreground="{DynamicResource Synthetic.Brushes.TextSecondary}" FontSize="11"/>
                   
        <ProgressBar Grid.Row="2" Height="16" Minimum="0" Maximum="{Binding MaximumValue}" Value="{Binding CurrentValue}" 
                      IsIndeterminate="False" Margin="0,0,0,20"/>
                      
        <Button Content="Cancel" Command="{Binding CancelCommand}" Grid.Row="3" 
                HorizontalAlignment="Right" VerticalAlignment="Bottom"/>
    </Grid>
</Window>
```

### File: Shared/UI/SharedProgressWindow.xaml.cs
```csharp
using System;
using System.Windows;

using Synthetic.Shared.UI;
using Synthetic.Core;

using Synthetic.Modules.RevitDOM;
using Synthetic.Modules.StandardsManagement.ViewModels;

namespace Synthetic.Shared.UI
{
    /// <summary>
    /// Interaction logic for SharedProgressWindow.xaml.
    /// Parented to Revit's main window handle to prevent it from dropping behind the Revit main frame.
    /// </summary>
    public partial class SharedProgressWindow : Window
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SharedProgressWindow"/> class.
        /// </summary>
        /// <param name="ownerHandle">Revit main window owner handle.</param>
        /// <param name="viewModel">ViewModel containing data bindings.</param>
        public SharedProgressWindow(IntPtr ownerHandle, SharedProgressViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
            RevitWindowHelper.SetOwner(this, ownerHandle);
        }
    }
}
```

### File: Shared/UI/SingleItemSelectionDisplayConverter.cs
```csharp
using System;
using System.Globalization;
using System.Windows.Data;

namespace Synthetic.Shared.UI
{
    /// <summary>
    /// MultiValueConverter to dynamically resolve names of items for single-item selection.
    /// Expects two values: the item itself, and the parent DataContext implementing <see cref="ISingleItemSelectionViewModel"/>.
    /// </summary>
    public class SingleItemSelectionDisplayConverter : IMultiValueConverter
    {
        /// <inheritdoc/>
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values != null && values.Length >= 2 && values[1] is ISingleItemSelectionViewModel vm)
            {
                var item = values[0];
                return vm.GetItemDisplayName(item);
            }

            return values?[0]?.ToString() ?? string.Empty;
        }

        /// <inheritdoc/>
        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
```

### File: Shared/UI/SingleItemSelectionViewModel.cs
```csharp
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows.Input;

namespace Synthetic.Shared.UI
{
    /// <summary>
    /// Generic ViewModel for selecting a single item from a list.
    /// </summary>
    /// <typeparam name="T">The type of item to select.</typeparam>
    public class SingleItemSelectionViewModel<T> : ViewModelBase, ISingleItemSelectionViewModel
    {
        private string _title = "Select Item";
        private string _prompt = "Select an item from the list below:";
        private T? _selectedItem;
        private readonly Func<T, string> _displayMemberPath;

        /// <summary>
        /// Initializes a new instance of the <see cref="SingleItemSelectionViewModel{T}"/> class.
        /// </summary>
        /// <param name="items">The items available to select from.</param>
        /// <param name="prompt">The prompt message to display to the user.</param>
        /// <param name="displayMemberPath">A delegate to resolve the display name for each item.</param>
        public SingleItemSelectionViewModel(IEnumerable<T> items, string prompt, Func<T, string> displayMemberPath)
        {
            if (items == null) throw new ArgumentNullException(nameof(items));
            _displayMemberPath = displayMemberPath ?? throw new ArgumentNullException(nameof(displayMemberPath));
            _prompt = prompt;
            Items = new ObservableCollection<T>(items);
            OkCommand = new RelayCommand(OnOk, CanOk);
            CancelCommand = new RelayCommand(OnCancel);
        }

        /// <summary>
        /// Gets or sets the title of the selection window.
        /// </summary>
        public string Title
        {
            get => _title;
            set => SetProperty(ref _title, value);
        }

        /// <summary>
        /// Gets or sets the instructions or prompt shown to the user.
        /// </summary>
        public string Prompt
        {
            get => _prompt;
            set => SetProperty(ref _prompt, value);
        }

        /// <summary>
        /// Gets the collection of items to select from.
        /// </summary>
        public ObservableCollection<T> Items { get; }

        /// <summary>
        /// Gets or sets the selected item.
        /// </summary>
        public T? SelectedItem
        {
            get => _selectedItem;
            set => SetProperty(ref _selectedItem, value);
        }

        /// <summary>
        /// Gets or sets the action to close the window, passing a boolean dialog result.
        /// </summary>
        public Action<bool>? CloseAction { get; set; }

        /// <summary>
        /// Gets the OK command.
        /// </summary>
        public ICommand OkCommand { get; }

        /// <summary>
        /// Gets the Cancel command.
        /// </summary>
        public ICommand CancelCommand { get; }

        private bool CanOk(object parameter)
        {
            return SelectedItem != null;
        }

        private void OnOk(object parameter)
        {
            CloseAction?.Invoke(true);
        }

        private void OnCancel(object parameter)
        {
            CloseAction?.Invoke(false);
        }

        /// <inheritdoc/>
        public string GetItemDisplayName(object item)
        {
            if (item is T typedItem)
            {
                return _displayMemberPath(typedItem);
            }
            return item?.ToString() ?? string.Empty;
        }
    }
}
```

### File: Shared/UI/SingleItemSelectionWindow.xaml
```xml
<Window x:Class="Synthetic.Shared.UI.SingleItemSelectionWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:local="clr-namespace:Synthetic.Shared.UI"
        Title="{Binding Title}"
        SizeToContent="WidthAndHeight" MinHeight="280" MinWidth="400"
        WindowStartupLocation="CenterOwner" ResizeMode="CanResizeWithGrip" ShowInTaskbar="False"
        Style="{DynamicResource SyntheticWindowStyle}"
        local:WindowChromeBehavior.EnableWindowCommands="True">

    <Window.Resources>
        <ResourceDictionary>
            <ResourceDictionary.MergedDictionaries>
                <ResourceDictionary Source="SyntheticTheme.xaml"/>
            </ResourceDictionary.MergedDictionaries>
            <local:SingleItemSelectionDisplayConverter x:Key="SingleItemSelectionDisplayConverter"/>
        </ResourceDictionary>
    </Window.Resources>

    <Grid Margin="15">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/> <!-- Header Prompt -->
            <RowDefinition Height="*"/>    <!-- Selection Area -->
            <RowDefinition Height="Auto"/> <!-- Actions -->
        </Grid.RowDefinitions>

        <!-- Prompt Message -->
        <StackPanel Grid.Row="0" Margin="0,0,0,12">
            <TextBlock Text="{Binding Title}" FontSize="15" FontWeight="Bold"
                       Foreground="{DynamicResource Synthetic.Brushes.TextPrimary}" Margin="0,0,0,4"/>
            <TextBlock Text="{Binding Prompt}"
                       FontSize="11" Foreground="{DynamicResource Synthetic.Brushes.TextSecondary}" TextWrapping="Wrap"/>
        </StackPanel>

        <!-- Selection Container -->
        <Border Grid.Row="1" Background="{DynamicResource Synthetic.Brushes.ControlSurface}"
                BorderBrush="{DynamicResource Synthetic.Brushes.BorderNormal}"
                BorderThickness="1" CornerRadius="4" Padding="5" Margin="0,0,0,15">
            <ListBox ItemsSource="{Binding Items}" SelectedItem="{Binding SelectedItem}"
                     Background="Transparent" BorderThickness="0" ScrollViewer.HorizontalScrollBarVisibility="Disabled">
                <ListBox.ItemTemplate>
                    <DataTemplate>
                        <RadioButton IsChecked="{Binding IsSelected, RelativeSource={RelativeSource AncestorType=ListBoxItem}}"
                                     GroupName="ItemSelectionGroup"
                                     Margin="2,1"
                                     VerticalAlignment="Center">
                            <RadioButton.Content>
                                <MultiBinding Converter="{StaticResource SingleItemSelectionDisplayConverter}">
                                    <Binding />
                                    <Binding Path="DataContext" RelativeSource="{RelativeSource AncestorType=Window}"/>
                                </MultiBinding>
                            </RadioButton.Content>
                        </RadioButton>
                    </DataTemplate>
                </ListBox.ItemTemplate>
                <ListBox.ItemContainerStyle>
                    <Style TargetType="ListBoxItem">
                        <Setter Property="Background" Value="Transparent"/>
                        <Setter Property="BorderThickness" Value="0"/>
                        <Setter Property="Padding" Value="6,4"/>
                        <Setter Property="Margin" Value="0,1"/>
                        <Style.Resources>
                            <!-- Kills OS blue selection highlight -->
                            <SolidColorBrush x:Key="{x:Static SystemColors.HighlightBrushKey}"
                                             Color="Transparent"/>
                            <SolidColorBrush x:Key="{x:Static SystemColors.InactiveSelectionHighlightBrushKey}"
                                             Color="Transparent"/>
                            <SolidColorBrush x:Key="{x:Static SystemColors.HighlightTextBrushKey}"
                                             Color="{StaticResource Synthetic.Colors.TextPrimary}"/>
                        </Style.Resources>
                        <Style.Triggers>
                            <Trigger Property="IsSelected" Value="True">
                                <Setter Property="Background" Value="{DynamicResource Synthetic.Brushes.ControlSurfaceLighter}"/>
                            </Trigger>
                            <Trigger Property="IsMouseOver" Value="True">
                                <Setter Property="Background" Value="#1A3A3938"/>
                            </Trigger>
                        </Style.Triggers>
                    </Style>
                </ListBox.ItemContainerStyle>
            </ListBox>
        </Border>

        <!-- Actions -->
        <StackPanel Grid.Row="2" Orientation="Horizontal" HorizontalAlignment="Right">
            <Button Content="OK" Command="{Binding OkCommand}"
                    Style="{DynamicResource Synthetic.Styles.PrimaryButton}"
                    IsDefault="True" Margin="0,0,10,0"/>
            <Button Content="Cancel" Command="{Binding CancelCommand}"
                    IsCancel="True" Style="{DynamicResource Synthetic.Styles.SecondaryButton.Right}"/>
        </StackPanel>
    </Grid>
</Window>
```

### File: Shared/UI/SingleItemSelectionWindow.xaml.cs
```csharp
using System;
using System.Windows;

namespace Synthetic.Shared.UI
{
    /// <summary>
    /// Interaction logic for SingleItemSelectionWindow.xaml.
    /// </summary>
    public partial class SingleItemSelectionWindow : Window
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SingleItemSelectionWindow"/> class.
        /// </summary>
        /// <param name="mainWindowHandle">The handle to the main Revit application window, used to set the owner of this window.</param>
        public SingleItemSelectionWindow(IntPtr mainWindowHandle)
        {
            InitializeComponent();
            RevitWindowHelper.SetOwner(this, mainWindowHandle);

            DataContextChanged += (sender, args) =>
            {
                if (DataContext is ISingleItemSelectionViewModel vm)
                {
                    vm.CloseAction = (dialogResult) =>
                    {
                        try
                        {
                            this.DialogResult = dialogResult;
                        }
                        catch (InvalidOperationException)
                        {
                            // Window is closing/closed
                        }
                        this.Close();
                    };
                }
            };
        }
    }
}
```

### File: Shared/UI/SyntheticTheme.xaml
```xml
<ResourceDictionary
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:shell="clr-namespace:System.Windows.Shell;assembly=PresentationFramework">

    <!-- ================================================================ -->
    <!-- SYNTHETIC UI THEME - Semantic Color Palette                       -->
    <!-- Source of truth: docs/Synthetic_UI_Theme_Specification.md         -->
    <!-- WCAG 2.1 AA Compliant                                            -->
    <!-- ================================================================ -->

    <!-- Core Colors -->
    <Color x:Key="Synthetic.Colors.BackgroundBase">#1C1B1A</Color>
    <Color x:Key="Synthetic.Colors.ControlSurface">#252423</Color>
    <Color x:Key="Synthetic.Colors.ControlSurfaceLighter">#2E2D2C</Color>
    <Color x:Key="Synthetic.Colors.BorderNormal">#3A3938</Color>
    <Color x:Key="Synthetic.Colors.AccentActive">#E65100</Color>
    <Color x:Key="Synthetic.Colors.TextPrimary">#F1F1F1</Color>
    <Color x:Key="Synthetic.Colors.TextSecondary">#A1A1AA</Color>
    <Color x:Key="Synthetic.Colors.TextDark">#121111</Color>
    <Color x:Key="Synthetic.Colors.Success">#7A8F75</Color>
    <Color x:Key="Synthetic.Colors.Warning">#C2A26A</Color>
    <Color x:Key="Synthetic.Colors.Error">#D37B75</Color>

    <!-- Core Brushes -->
    <SolidColorBrush x:Key="Synthetic.Brushes.BackgroundBase"    Color="{StaticResource Synthetic.Colors.BackgroundBase}"/>
    <SolidColorBrush x:Key="Synthetic.Brushes.ControlSurface"    Color="{StaticResource Synthetic.Colors.ControlSurface}"/>
    <SolidColorBrush x:Key="Synthetic.Brushes.ControlSurfaceLighter" Color="{StaticResource Synthetic.Colors.ControlSurfaceLighter}"/>
    <SolidColorBrush x:Key="Synthetic.Brushes.BorderNormal"      Color="{StaticResource Synthetic.Colors.BorderNormal}"/>
    <SolidColorBrush x:Key="Synthetic.Brushes.AccentActive"      Color="{StaticResource Synthetic.Colors.AccentActive}"/>
    <SolidColorBrush x:Key="Synthetic.Brushes.TextPrimary"       Color="{StaticResource Synthetic.Colors.TextPrimary}"/>
    <SolidColorBrush x:Key="Synthetic.Brushes.TextSecondary"     Color="{StaticResource Synthetic.Colors.TextSecondary}"/>
    <SolidColorBrush x:Key="Synthetic.Brushes.TextDark"          Color="{StaticResource Synthetic.Colors.TextDark}"/>
    <SolidColorBrush x:Key="Synthetic.Brushes.Success"           Color="{StaticResource Synthetic.Colors.Success}"/>
    <SolidColorBrush x:Key="Synthetic.Brushes.Warning"           Color="{StaticResource Synthetic.Colors.Warning}"/>
    <SolidColorBrush x:Key="Synthetic.Brushes.Error"             Color="{StaticResource Synthetic.Colors.Error}"/>
    <SolidColorBrush x:Key="Synthetic.Brushes.Transparent"       Color="Transparent"/>

    <!-- Hover brushes derived from accent -->
    <SolidColorBrush x:Key="Synthetic.Brushes.ChromeButtonHoverMinMax" Color="{StaticResource Synthetic.Colors.AccentActive}"/>
    <SolidColorBrush x:Key="Synthetic.Brushes.ChromeButtonHoverClose"  Color="{StaticResource Synthetic.Colors.Error}"/>

    <!-- ================================================================ -->
    <!-- SEMANTIC VECTOR GEOMETRIES (SCALABLE ICONOGRAPHY)                 -->
    <!-- ================================================================ -->
    <StreamGeometry x:Key="Synthetic.Geometries.Folder">M2,4 C2,2.9 2.9,2 4,2 H9 L11,4 H20 C21.1,4 22,4.9 22,6 V18 C22,19.1 21.1,20 20,20 H4 C2.9,20 2,19.1 2,18 Z</StreamGeometry>
    <StreamGeometry x:Key="Synthetic.Geometries.File">M14,2 H6 C4.9,2 4,2.9 4,4 V20 C4,21.1 4.9,22 6,22 H18 C19.1,22 20,21.1 20,20 V8 L14,2 Z M13,9 V3.5 L18.5,9 H13 Z</StreamGeometry>
    <StreamGeometry x:Key="Synthetic.Geometries.Warning">M12,2 L1,21 H23 L12,2 M12,6 L19.8,19 H4.2 L12,6 M11,10 V14 H13 V10 H11 M11,16 V18 H13 V16 H11 Z</StreamGeometry>
    <StreamGeometry x:Key="Synthetic.Geometries.Blocked">M12,2 C6.48,2 2,6.48 2,12 C2,17.52 6.48,22 12,22 C17.52,22 22,17.52 22,12 C22,6.48 17.52,2 12,2 Z M12,20 C7.59,20 4,16.41 4,12 C4,10.13 4.64,8.42 5.72,7.05 L16.95,18.28 C15.58,19.36 13.87,20 12,20 Z M18.28,16.95 L7.05,5.72 C8.42,4.64 10.13,4 12,4 C16.41,4 20,7.59 20,12 C20,13.87 19.36,15.58 18.28,16.95 Z</StreamGeometry>
    <StreamGeometry x:Key="Synthetic.Geometries.Success">M9,16.17 L4.83,12 L3.41,13.41 L9,19 L21,7 L19.59,5.59 Z</StreamGeometry>
    <StreamGeometry x:Key="Synthetic.Geometries.Info">M12,2 C6.48,2 2,6.48 2,12 C2,17.52 6.48,22 12,22 C17.52,22 22,17.52 22,12 C22,6.48 17.52,2 12,2 Z M13,17 H11 V11 H13 V17 Z M13,9 H11 V7 H13 V9 Z</StreamGeometry>
    <StreamGeometry x:Key="Synthetic.Geometries.Gear">M19.14,12.94 C19.18,12.64 19.2,12.33 19.2,12 C19.2,11.67 19.18,11.36 19.14,11.06 L21.17,9.48 C21.35,9.34 21.4,9.07 21.29,8.87 L19.37,5.55 C19.25,5.33 19,5.26 18.78,5.33 L16.39,6.29 C15.89,5.91 15.36,5.59 14.77,5.35 L14.41,2.81 C14.37,2.57 14.17,2.4 13.93,2.4 H10.07 C9.83,2.4 9.63,2.57 9.59,2.81 L9.23,5.35 C8.64,5.59 8.11,5.91 7.61,6.29 L5.22,5.33 C5,5.26 4.75,5.33 4.63,5.55 L2.71,8.87 C2.6,9.07 2.65,9.34 2.83,9.48 L4.86,11.06 C4.82,11.36 4.8,11.67 4.8,12 C4.8,12.33 4.82,12.64 4.86,12.94 L2.83,14.52 C2.65,14.66 2.6,14.93 2.71,15.13 L4.63,18.45 C4.75,18.67 5,18.74 5.22,18.67 L7.61,17.71 C8.11,18.09 8.64,18.41 9.23,18.65 L9.59,21.19 C9.63,21.43 9.83,21.6 10.07,21.6 H13.93 C14.17,21.6 14.37,21.43 14.41,21.19 L14.77,18.65 C15.36,18.41 15.89,18.09 16.39,17.71 L18.78,18.67 C19,18.74 19.25,18.67 19.37,18.45 L21.29,15.13 C21.4,14.93 21.35,14.66 21.17,14.52 L19.14,12.94 Z M12,15.6 C10.02,15.6 8.4,13.98 8.4,12 C8.4,10.02 10.02,8.4 12,8.4 C13.98,8.4 15.6,10.02 15.6,12 C15.6,13.98 13.98,15.6 12,15.6 Z</StreamGeometry>

    <!-- ================================================================ -->
    <!-- CUSTOM WINDOW CHROME CONTROL BUTTON STYLE                        -->
    <!-- ================================================================ -->

    <!-- Shared base style for Min/Max/Close chrome buttons -->
    <Style x:Key="Synthetic.Styles.ChromeButton" TargetType="Button">
        <Setter Property="Width"               Value="46"/>
        <Setter Property="Height"              Value="45"/>
        <Setter Property="Background"          Value="Transparent"/>
        <Setter Property="BorderThickness"     Value="0"/>
        <Setter Property="Focusable"           Value="False"/>
        <Setter Property="WindowChrome.IsHitTestVisibleInChrome" Value="True"/>
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="Button">
                    <Border x:Name="ButtonBorder"
                            Background="{TemplateBinding Background}"
                            SnapsToDevicePixels="True">
                        <ContentPresenter HorizontalAlignment="Center"
                                          VerticalAlignment="Center"/>
                    </Border>
                    <ControlTemplate.Triggers>
                        <Trigger Property="IsMouseOver" Value="True">
                            <Setter TargetName="ButtonBorder" Property="Background"
                                    Value="{StaticResource Synthetic.Brushes.ChromeButtonHoverMinMax}"/>
                        </Trigger>
                        <Trigger Property="IsPressed" Value="True">
                            <Setter TargetName="ButtonBorder" Property="Background"
                                    Value="{StaticResource Synthetic.Brushes.ControlSurfaceLighter}"/>
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <!-- Close button overrides the hover color to error red -->
    <Style x:Key="Synthetic.Styles.ChromeCloseButton"
           BasedOn="{StaticResource Synthetic.Styles.ChromeButton}"
           TargetType="Button">
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="Button">
                    <Border x:Name="CloseBorder"
                            Background="{TemplateBinding Background}"
                            SnapsToDevicePixels="True">
                        <ContentPresenter HorizontalAlignment="Center"
                                          VerticalAlignment="Center"/>
                    </Border>
                    <ControlTemplate.Triggers>
                        <Trigger Property="IsMouseOver" Value="True">
                            <Setter TargetName="CloseBorder" Property="Background"
                                    Value="{StaticResource Synthetic.Brushes.ChromeButtonHoverClose}"/>
                        </Trigger>
                        <Trigger Property="IsPressed" Value="True">
                            <Setter TargetName="CloseBorder" Property="Background"
                                    Value="{StaticResource Synthetic.Brushes.ControlSurfaceLighter}"/>
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <!-- ================================================================ -->
    <!-- BUTTON STYLES                                                     -->
    <!-- Primary  = Rust-Orange full-fill, dark text (#E65100 / #121111)  -->
    <!-- Secondary = Flat dark surface, light text (implicit default)     -->
    <!-- Neutral  = Transparent, secondary text — for low-priority actions-->
    <!-- ================================================================ -->

    <!-- ── Secondary Button (implicit — all unstyled Buttons inherit this) ── -->
    <Style TargetType="Button">
        <Setter Property="Background"      Value="{StaticResource Synthetic.Brushes.ControlSurfaceLighter}"/>
        <Setter Property="Foreground"      Value="{StaticResource Synthetic.Brushes.TextPrimary}"/>
        <Setter Property="BorderBrush"     Value="{StaticResource Synthetic.Brushes.BorderNormal}"/>
        <Setter Property="BorderThickness" Value="1"/>
        <Setter Property="MinHeight"       Value="28"/>
        <Setter Property="MinWidth"        Value="80"/>
        <Setter Property="Padding"         Value="12,6"/>
        <Setter Property="FontSize"        Value="12"/>
        <Setter Property="Cursor"          Value="Hand"/>
        <Setter Property="SnapsToDevicePixels" Value="True"/>
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="Button">
                    <Border x:Name="Bd"
                            Background="{TemplateBinding Background}"
                            BorderBrush="{TemplateBinding BorderBrush}"
                            BorderThickness="{TemplateBinding BorderThickness}"
                            CornerRadius="4"
                            Padding="{TemplateBinding Padding}"
                            SnapsToDevicePixels="True">
                        <ContentPresenter HorizontalAlignment="Center"
                                          VerticalAlignment="Center"/>
                    </Border>
                    <ControlTemplate.Triggers>
                        <Trigger Property="IsMouseOver" Value="True">
                            <Setter TargetName="Bd" Property="Background"
                                    Value="{StaticResource Synthetic.Brushes.BorderNormal}"/>
                        </Trigger>
                        <Trigger Property="IsPressed" Value="True">
                            <Setter TargetName="Bd" Property="Background"
                                    Value="{StaticResource Synthetic.Brushes.ControlSurface}"/>
                        </Trigger>
                        <Trigger Property="IsEnabled" Value="False">
                            <Setter TargetName="Bd" Property="Background"
                                    Value="{StaticResource Synthetic.Brushes.BackgroundBase}"/>
                            <Setter Property="Foreground"
                                    Value="{StaticResource Synthetic.Brushes.TextSecondary}"/>
                            <Setter Property="Opacity" Value="0.5"/>
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <!-- ── Primary Action Button: Rust-Orange fill, dark text ── -->
    <Style x:Key="Synthetic.Styles.PrimaryButton" TargetType="Button">
        <Setter Property="Background"      Value="{StaticResource Synthetic.Brushes.AccentActive}"/>
        <Setter Property="Foreground"      Value="{StaticResource Synthetic.Brushes.TextDark}"/>
        <Setter Property="BorderThickness" Value="0"/>
        <Setter Property="MinHeight"       Value="28"/>
        <Setter Property="MinWidth"        Value="80"/>
        <Setter Property="Padding"         Value="12,6"/>
        <Setter Property="FontSize"        Value="12"/>
        <Setter Property="FontWeight"      Value="SemiBold"/>
        <Setter Property="Cursor"          Value="Hand"/>
        <Setter Property="SnapsToDevicePixels" Value="True"/>
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="Button">
                    <Border x:Name="Bd"
                            Background="{TemplateBinding Background}"
                            BorderThickness="0"
                            CornerRadius="4"
                            Padding="{TemplateBinding Padding}"
                            SnapsToDevicePixels="True">
                        <ContentPresenter HorizontalAlignment="Center"
                                          VerticalAlignment="Center"/>
                    </Border>
                    <ControlTemplate.Triggers>
                        <Trigger Property="IsMouseOver" Value="True">
                            <Setter TargetName="Bd" Property="Background" Value="#FF6F20"/>
                        </Trigger>
                        <Trigger Property="IsPressed" Value="True">
                            <Setter TargetName="Bd" Property="Background" Value="#B33F00"/>
                        </Trigger>
                        <Trigger Property="IsEnabled" Value="False">
                            <Setter TargetName="Bd" Property="Background"
                                    Value="{StaticResource Synthetic.Brushes.ControlSurface}"/>
                            <Setter Property="Foreground"
                                    Value="{StaticResource Synthetic.Brushes.TextSecondary}"/>
                            <Setter Property="Opacity" Value="0.5"/>
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <!-- ── Neutral / Text Button: no background, secondary text ── -->
    <Style x:Key="Synthetic.Styles.NeutralButton" TargetType="Button">
        <Setter Property="Background"      Value="Transparent"/>
        <Setter Property="Foreground"      Value="{StaticResource Synthetic.Brushes.TextSecondary}"/>
        <Setter Property="BorderThickness" Value="0"/>
        <Setter Property="MinHeight"       Value="28"/>
        <Setter Property="MinWidth"        Value="80"/>
        <Setter Property="Padding"         Value="12,6"/>
        <Setter Property="FontSize"        Value="12"/>
        <Setter Property="Cursor"          Value="Hand"/>
        <Setter Property="SnapsToDevicePixels" Value="True"/>
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="Button">
                    <Border Background="Transparent"
                            Padding="{TemplateBinding Padding}"
                            SnapsToDevicePixels="True">
                        <ContentPresenter HorizontalAlignment="Center"
                                          VerticalAlignment="Center"/>
                    </Border>
                    <ControlTemplate.Triggers>
                        <Trigger Property="IsMouseOver" Value="True">
                            <Setter Property="Foreground"
                                    Value="{StaticResource Synthetic.Brushes.TextPrimary}"/>
                        </Trigger>
                        <Trigger Property="IsEnabled" Value="False">
                            <Setter Property="Opacity" Value="0.4"/>
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <!-- ── Right-aligned Action Button margin ── -->
    <Thickness x:Key="Synthetic.Margins.RightAction">0,0,10,0</Thickness>

    <!-- ── Right-justified Action Buttons sub-styles ── -->
    <Style x:Key="Synthetic.Styles.PrimaryButton.Right" TargetType="Button" BasedOn="{StaticResource Synthetic.Styles.PrimaryButton}">
        <Setter Property="Margin" Value="{StaticResource Synthetic.Margins.RightAction}"/>
    </Style>

    <Style x:Key="Synthetic.Styles.NeutralButton.Right" TargetType="Button" BasedOn="{StaticResource Synthetic.Styles.NeutralButton}">
        <Setter Property="Margin" Value="{StaticResource Synthetic.Margins.RightAction}"/>
    </Style>

    <Style x:Key="Synthetic.Styles.SecondaryButton.Right" TargetType="Button" BasedOn="{StaticResource {x:Type Button}}">
        <Setter Property="Margin" Value="{StaticResource Synthetic.Margins.RightAction}"/>
    </Style>

    <!-- ================================================================ -->
    <!-- TEXTBOX STYLE                                                     -->
    <!-- Resting: #2E2D2C bg, 1px #3A3938 border                         -->
    <!-- Focus (IsKeyboardFocusWithin): 1px #E65100 border — no shadows  -->
    <!-- ================================================================ -->
    <Style TargetType="TextBox">
        <Setter Property="Background"              Value="{StaticResource Synthetic.Brushes.ControlSurfaceLighter}"/>
        <Setter Property="Foreground"              Value="{StaticResource Synthetic.Brushes.TextPrimary}"/>
        <Setter Property="CaretBrush"              Value="{StaticResource Synthetic.Brushes.TextPrimary}"/>
        <Setter Property="SelectionBrush"          Value="{StaticResource Synthetic.Brushes.AccentActive}"/>
        <Setter Property="SelectionTextBrush"      Value="{StaticResource Synthetic.Brushes.TextDark}"/>
        <Setter Property="BorderBrush"             Value="{StaticResource Synthetic.Brushes.BorderNormal}"/>
        <Setter Property="BorderThickness"         Value="1"/>
        <Setter Property="MinHeight"               Value="28"/>
        <Setter Property="Padding"                 Value="4,2"/>
        <Setter Property="FontSize"                Value="12"/>
        <Setter Property="FontFamily"              Value="PT Sans Narrow, Segoe UI"/>
        <Setter Property="SnapsToDevicePixels"     Value="True"/>
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="TextBox">
                    <Border x:Name="Bd"
                            Background="{TemplateBinding Background}"
                            BorderBrush="{TemplateBinding BorderBrush}"
                            BorderThickness="{TemplateBinding BorderThickness}"
                            CornerRadius="4"
                            SnapsToDevicePixels="True">
                        <ScrollViewer x:Name="PART_ContentHost"
                                      Margin="{TemplateBinding Padding}"
                                      VerticalAlignment="Center"
                                      Focusable="False"
                                      HorizontalScrollBarVisibility="Hidden"
                                      VerticalScrollBarVisibility="Hidden"/>
                    </Border>
                    <ControlTemplate.Triggers>
                        <Trigger Property="IsKeyboardFocusWithin" Value="True">
                            <Setter TargetName="Bd" Property="BorderBrush"
                                    Value="{StaticResource Synthetic.Brushes.AccentActive}"/>
                        </Trigger>
                        <Trigger Property="IsEnabled" Value="False">
                            <Setter TargetName="Bd" Property="Background"
                                    Value="{StaticResource Synthetic.Brushes.BackgroundBase}"/>
                            <Setter Property="Foreground"
                                    Value="{StaticResource Synthetic.Brushes.TextSecondary}"/>
                            <Setter Property="Opacity" Value="0.5"/>
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <!-- ================================================================ -->
    <!-- CHECKBOX STYLE                                                    -->
    <!-- Resting:  #2E2D2C bg, #3A3938 border, 3px corner                -->
    <!-- Checked:  #E65100 fill, #E65100 border, #121111 vector checkmark-->
    <!-- Disabled: 25% opacity                                            -->
    <!-- ================================================================ -->
    <Style TargetType="CheckBox">
        <Setter Property="Foreground"          Value="{StaticResource Synthetic.Brushes.TextPrimary}"/>
        <Setter Property="VerticalContentAlignment" Value="Center"/>
        <Setter Property="Padding"             Value="6,0,0,0"/>
        <Setter Property="FontSize"            Value="12"/>
        <Setter Property="Cursor"              Value="Hand"/>
        <Setter Property="SnapsToDevicePixels" Value="True"/>
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="CheckBox">
                    <Grid>
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="16"/>
                            <ColumnDefinition Width="*"/>
                        </Grid.ColumnDefinitions>

                        <!-- ── Checkbox Box ── -->
                        <Border x:Name="CheckBorder"
                                Grid.Column="0"
                                Width="16" Height="16"
                                Background="{StaticResource Synthetic.Brushes.ControlSurfaceLighter}"
                                BorderBrush="{StaticResource Synthetic.Brushes.BorderNormal}"
                                BorderThickness="1"
                                CornerRadius="3"
                                VerticalAlignment="Center"
                                SnapsToDevicePixels="True">

                            <!-- ── Checkmark vector path (hidden until Checked) ── -->
                            <Path x:Name="CheckMark"
                                  Data="M2,7 L6,11 L13,3"
                                  Stroke="{StaticResource Synthetic.Brushes.TextDark}"
                                  StrokeThickness="2"
                                  StrokeLineJoin="Round"
                                  StrokeStartLineCap="Round"
                                  StrokeEndLineCap="Round"
                                  Visibility="Collapsed"
                                  HorizontalAlignment="Center"
                                  VerticalAlignment="Center"
                                  SnapsToDevicePixels="True"/>
                        </Border>

                        <!-- ── Label ── -->
                        <ContentPresenter Grid.Column="1"
                                          Margin="{TemplateBinding Padding}"
                                          VerticalAlignment="{TemplateBinding VerticalContentAlignment}"
                                          HorizontalAlignment="Left"
                                          RecognizesAccessKey="True"/>
                    </Grid>
                    <ControlTemplate.Triggers>
                        <!-- Checked state -->
                        <Trigger Property="IsChecked" Value="True">
                            <Setter TargetName="CheckBorder" Property="Background"
                                    Value="{StaticResource Synthetic.Brushes.AccentActive}"/>
                            <Setter TargetName="CheckBorder" Property="BorderBrush"
                                    Value="{StaticResource Synthetic.Brushes.AccentActive}"/>
                            <Setter TargetName="CheckMark"   Property="Visibility" Value="Visible"/>
                        </Trigger>
                        <!-- Indeterminate state -->
                        <Trigger Property="IsChecked" Value="{x:Null}">
                            <Setter TargetName="CheckBorder" Property="Background"
                                    Value="{StaticResource Synthetic.Brushes.ControlSurfaceLighter}"/>
                            <Setter TargetName="CheckBorder" Property="BorderBrush"
                                    Value="{StaticResource Synthetic.Brushes.AccentActive}"/>
                        </Trigger>
                        <!-- Hover -->
                        <Trigger Property="IsMouseOver" Value="True">
                            <Setter TargetName="CheckBorder" Property="BorderBrush"
                                    Value="{StaticResource Synthetic.Brushes.AccentActive}"/>
                        </Trigger>
                        <!-- Disabled -->
                        <Trigger Property="IsEnabled" Value="False">
                            <Setter TargetName="CheckBorder" Property="Background" Value="#121111"/>
                            <Setter TargetName="CheckBorder" Property="Opacity"    Value="0.25"/>
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <!-- ================================================================ -->
    <!-- COMBOBOX STYLE                                                    -->
    <!-- Flat #2E2D2C input, custom toggle button arrow, popup #252423   -->
    <!-- MouseOver / Focus border: 1px #E65100                           -->
    <!-- ================================================================ -->

    <!-- Internal ToggleButton for the ComboBox arrow -->
    <Style x:Key="Synthetic.Internal.ComboBoxToggleButton" TargetType="ToggleButton">
        <Setter Property="Focusable"           Value="False"/>
        <Setter Property="Background"          Value="Transparent"/>
        <Setter Property="BorderThickness"     Value="0"/>
        <Setter Property="ClickMode"           Value="Press"/>
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="ToggleButton">
                    <Grid Background="Transparent">
                        <!-- Chevron-down path arrow -->
                        <Path x:Name="Arrow"
                              Data="M0,0 L5,5 L10,0"
                              Stroke="{StaticResource Synthetic.Brushes.TextSecondary}"
                              StrokeThickness="1.5"
                              StrokeLineJoin="Round"
                              HorizontalAlignment="Center"
                              VerticalAlignment="Center"
                              SnapsToDevicePixels="True"/>
                    </Grid>
                    <ControlTemplate.Triggers>
                        <Trigger Property="IsMouseOver" Value="True">
                            <Setter TargetName="Arrow" Property="Stroke"
                                    Value="{StaticResource Synthetic.Brushes.TextPrimary}"/>
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <Style TargetType="ComboBox">
        <Setter Property="Background"          Value="{StaticResource Synthetic.Brushes.ControlSurfaceLighter}"/>
        <Setter Property="Foreground"          Value="{StaticResource Synthetic.Brushes.TextPrimary}"/>
        <Setter Property="BorderBrush"         Value="{StaticResource Synthetic.Brushes.BorderNormal}"/>
        <Setter Property="BorderThickness"     Value="1"/>
        <Setter Property="MinHeight"           Value="28"/>
        <Setter Property="Padding"             Value="4,2"/>
        <Setter Property="FontSize"            Value="12"/>
        <Setter Property="VerticalContentAlignment" Value="Center"/>
        <Setter Property="SnapsToDevicePixels" Value="True"/>
        <Setter Property="ScrollViewer.HorizontalScrollBarVisibility" Value="Disabled"/>
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="ComboBox">
                    <Grid>
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="*"/>
                            <ColumnDefinition Width="28"/>
                        </Grid.ColumnDefinitions>

                        <!-- Outer border wraps the whole control -->
                        <Border x:Name="Bd"
                                Grid.ColumnSpan="2"
                                Background="{TemplateBinding Background}"
                                BorderBrush="{TemplateBinding BorderBrush}"
                                BorderThickness="{TemplateBinding BorderThickness}"
                                CornerRadius="4"
                                SnapsToDevicePixels="True"/>

                        <!-- Selected item presenter -->
                        <ContentPresenter Grid.Column="0"
                                          x:Name="ContentSite"
                                          Content="{TemplateBinding SelectionBoxItem}"
                                          ContentTemplate="{TemplateBinding SelectionBoxItemTemplate}"
                                          Margin="{TemplateBinding Padding}"
                                          VerticalAlignment="{TemplateBinding VerticalContentAlignment}"
                                          IsHitTestVisible="False"/>

                        <!-- Custom arrow ToggleButton -->
                        <ToggleButton Grid.Column="1"
                                      Style="{StaticResource Synthetic.Internal.ComboBoxToggleButton}"
                                      IsChecked="{Binding IsDropDownOpen, RelativeSource={RelativeSource TemplatedParent}, Mode=TwoWay}"/>

                        <!-- Dropdown Popup -->
                        <Popup x:Name="PART_Popup"
                               Grid.ColumnSpan="2"
                               IsOpen="{TemplateBinding IsDropDownOpen}"
                               AllowsTransparency="True"
                               Focusable="False"
                               PopupAnimation="Fade"
                               Placement="Bottom">
                            <Border x:Name="DropDownBorder"
                                    Background="{StaticResource Synthetic.Brushes.ControlSurface}"
                                    BorderBrush="{StaticResource Synthetic.Brushes.BorderNormal}"
                                    BorderThickness="1"
                                    CornerRadius="4"
                                    MinWidth="{TemplateBinding ActualWidth}"
                                    MaxHeight="{TemplateBinding MaxDropDownHeight}"
                                    SnapsToDevicePixels="True">
                                <ScrollViewer SnapsToDevicePixels="True">
                                    <ItemsPresenter KeyboardNavigation.DirectionalNavigation="Contained"/>
                                </ScrollViewer>
                            </Border>
                        </Popup>
                    </Grid>
                    <ControlTemplate.Triggers>
                        <Trigger Property="IsMouseOver" Value="True">
                            <Setter TargetName="Bd" Property="BorderBrush"
                                    Value="{StaticResource Synthetic.Brushes.AccentActive}"/>
                        </Trigger>
                        <Trigger Property="IsKeyboardFocusWithin" Value="True">
                            <Setter TargetName="Bd" Property="BorderBrush"
                                    Value="{StaticResource Synthetic.Brushes.AccentActive}"/>
                        </Trigger>
                        <Trigger Property="IsEnabled" Value="False">
                            <Setter TargetName="Bd" Property="Background"
                                    Value="{StaticResource Synthetic.Brushes.BackgroundBase}"/>
                            <Setter Property="Foreground"
                                    Value="{StaticResource Synthetic.Brushes.TextSecondary}"/>
                            <Setter Property="Opacity" Value="0.5"/>
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <!-- ComboBoxItem -->
    <Style TargetType="ComboBoxItem">
        <Setter Property="Foreground"              Value="{StaticResource Synthetic.Brushes.TextPrimary}"/>
        <Setter Property="Background"              Value="Transparent"/>
        <Setter Property="Padding"                 Value="8,5"/>
        <Setter Property="SnapsToDevicePixels"     Value="True"/>
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="ComboBoxItem">
                    <Border x:Name="ItemBd"
                            Background="{TemplateBinding Background}"
                            Padding="{TemplateBinding Padding}"
                            SnapsToDevicePixels="True">
                        <ContentPresenter VerticalAlignment="Center"/>
                    </Border>
                    <ControlTemplate.Triggers>
                        <Trigger Property="IsMouseOver" Value="True">
                            <Setter TargetName="ItemBd" Property="Background" Value="#332E2D2C"/>
                        </Trigger>
                        <Trigger Property="IsSelected" Value="True">
                            <Setter TargetName="ItemBd" Property="Background"
                                    Value="{StaticResource Synthetic.Brushes.BorderNormal}"/>
                        </Trigger>
                        <MultiTrigger>
                            <MultiTrigger.Conditions>
                                <Condition Property="IsSelected"   Value="True"/>
                                <Condition Property="IsMouseOver"  Value="True"/>
                            </MultiTrigger.Conditions>
                            <Setter TargetName="ItemBd" Property="Background" Value="#553A3938"/>
                        </MultiTrigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <!-- ================================================================ -->
    <!-- SCROLLBAR & THUMB STYLES                                          -->
    <!-- Modern minimalist dark themed scrollbars                          -->
    <!-- ================================================================ -->

    <!-- ScrollBar Thumb Style -->
    <Style x:Key="Synthetic.Styles.ScrollBarThumb" TargetType="{x:Type Thumb}">
        <Setter Property="OverridesDefaultStyle" Value="True"/>
        <Setter Property="IsTabStop" Value="False"/>
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="{x:Type Thumb}">
                    <Border x:Name="ThumbBorder"
                            Background="{DynamicResource Synthetic.Brushes.ControlSurfaceLighter}"
                            BorderBrush="{DynamicResource Synthetic.Brushes.BorderNormal}"
                            BorderThickness="1"
                            CornerRadius="4"
                            SnapsToDevicePixels="True"/>
                    <ControlTemplate.Triggers>
                        <Trigger Property="IsMouseOver" Value="True">
                            <Setter TargetName="ThumbBorder" Property="Background" Value="#3A3938"/>
                            <Setter TargetName="ThumbBorder" Property="BorderBrush" Value="{DynamicResource Synthetic.Brushes.TextSecondary}"/>
                        </Trigger>
                        <Trigger Property="IsDragging" Value="True">
                            <Setter TargetName="ThumbBorder" Property="Background" Value="{DynamicResource Synthetic.Brushes.AccentActive}"/>
                            <Setter TargetName="ThumbBorder" Property="BorderBrush" Value="{DynamicResource Synthetic.Brushes.AccentActive}"/>
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <!-- Implicit ScrollBar Style -->
    <Style TargetType="{x:Type ScrollBar}">
        <Setter Property="OverridesDefaultStyle" Value="True"/>
        <Setter Property="Background" Value="{DynamicResource Synthetic.Brushes.BackgroundBase}"/>
        <Setter Property="BorderThickness" Value="0"/>
        <!-- Default Template (Vertical) -->
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="{x:Type ScrollBar}">
                    <Grid x:Name="Bg" SnapsToDevicePixels="True" Background="{TemplateBinding Background}">
                        <Track x:Name="PART_Track" IsEnabled="{TemplateBinding IsEnabled}" IsDirectionReversed="True">
                            <Track.DecreaseRepeatButton>
                                <RepeatButton Command="{x:Static ScrollBar.PageUpCommand}" Focusable="False" Opacity="0">
                                    <RepeatButton.Template>
                                        <ControlTemplate TargetType="RepeatButton">
                                            <Border Background="Transparent"/>
                                        </ControlTemplate>
                                    </RepeatButton.Template>
                                </RepeatButton>
                            </Track.DecreaseRepeatButton>
                            <Track.IncreaseRepeatButton>
                                <RepeatButton Command="{x:Static ScrollBar.PageDownCommand}" Focusable="False" Opacity="0">
                                    <RepeatButton.Template>
                                        <ControlTemplate TargetType="RepeatButton">
                                            <Border Background="Transparent"/>
                                        </ControlTemplate>
                                    </RepeatButton.Template>
                                </RepeatButton>
                            </Track.IncreaseRepeatButton>
                            <Track.Thumb>
                                <Thumb Style="{StaticResource Synthetic.Styles.ScrollBarThumb}" Margin="2"/>
                            </Track.Thumb>
                        </Track>
                    </Grid>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
        <Style.Triggers>
            <Trigger Property="Orientation" Value="Vertical">
                <Setter Property="Width" Value="10"/>
                <Setter Property="MinWidth" Value="10"/>
            </Trigger>
            <Trigger Property="Orientation" Value="Horizontal">
                <Setter Property="Height" Value="10"/>
                <Setter Property="MinHeight" Value="10"/>
                <Setter Property="Template">
                    <Setter.Value>
                        <ControlTemplate TargetType="{x:Type ScrollBar}">
                            <Grid x:Name="Bg" SnapsToDevicePixels="True" Background="{TemplateBinding Background}">
                                <Track x:Name="PART_Track" IsEnabled="{TemplateBinding IsEnabled}" IsDirectionReversed="False">
                                    <Track.DecreaseRepeatButton>
                                        <RepeatButton Command="{x:Static ScrollBar.PageLeftCommand}" Focusable="False" Opacity="0">
                                            <RepeatButton.Template>
                                                <ControlTemplate TargetType="RepeatButton">
                                                    <Border Background="Transparent"/>
                                                </ControlTemplate>
                                            </RepeatButton.Template>
                                        </RepeatButton>
                                    </Track.DecreaseRepeatButton>
                                    <Track.IncreaseRepeatButton>
                                        <RepeatButton Command="{x:Static ScrollBar.PageRightCommand}" Focusable="False" Opacity="0">
                                            <RepeatButton.Template>
                                                <ControlTemplate TargetType="RepeatButton">
                                                    <Border Background="Transparent"/>
                                                </ControlTemplate>
                                            </RepeatButton.Template>
                                        </RepeatButton>
                                    </Track.IncreaseRepeatButton>
                                    <Track.Thumb>
                                        <Thumb Style="{StaticResource Synthetic.Styles.ScrollBarThumb}" Margin="2"/>
                                    </Track.Thumb>
                                </Track>
                            </Grid>
                        </ControlTemplate>
                    </Setter.Value>
                </Setter>
            </Trigger>
        </Style.Triggers>
    </Style>

    <!-- ================================================================ -->
    <!-- DATAGRID STYLES                                                   -->
    <!-- Flat #252423 rows, thin #3A3938 horizontal lines, no zebra       -->
    <!-- Selection: desaturated #3A3938 fill, TextPrimary foreground      -->
    <!-- No OS-blue highlight bleed — overridden via Style.Resources      -->
    <!-- ================================================================ -->

    <!-- DataGridColumnHeader: dark base, bottom separator only -->
    <Style TargetType="DataGridColumnHeader">
        <Setter Property="Background"                Value="{StaticResource Synthetic.Brushes.BackgroundBase}"/>
        <Setter Property="Foreground"                Value="{StaticResource Synthetic.Brushes.TextPrimary}"/>
        <Setter Property="BorderBrush"               Value="{StaticResource Synthetic.Brushes.BorderNormal}"/>
        <Setter Property="BorderThickness"           Value="0,0,0,1"/>
        <Setter Property="Padding"                   Value="10,6"/>
        <Setter Property="FontSize"                  Value="11"/>
        <Setter Property="FontWeight"                Value="SemiBold"/>
        <Setter Property="HorizontalContentAlignment" Value="Left"/>
        <Setter Property="SnapsToDevicePixels"       Value="True"/>
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="DataGridColumnHeader">
                    <Border Background="{TemplateBinding Background}"
                            BorderBrush="{TemplateBinding BorderBrush}"
                            BorderThickness="{TemplateBinding BorderThickness}"
                            Padding="{TemplateBinding Padding}"
                            SnapsToDevicePixels="True">
                        <ContentPresenter HorizontalAlignment="{TemplateBinding HorizontalContentAlignment}"
                                          VerticalAlignment="Center"/>
                    </Border>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <!-- DataGridRow: flat surface, custom hover, no OS highlight -->
    <Style TargetType="DataGridRow">
        <Setter Property="Background"       Value="{StaticResource Synthetic.Brushes.ControlSurface}"/>
        <Setter Property="Foreground"       Value="{StaticResource Synthetic.Brushes.TextPrimary}"/>
        <Setter Property="BorderThickness"  Value="0"/>
        <Setter Property="SnapsToDevicePixels" Value="True"/>
        <!-- Kill OS selection highlight bleed at row level -->
        <Style.Resources>
            <SolidColorBrush x:Key="{x:Static SystemColors.HighlightBrushKey}"
                             Color="{StaticResource Synthetic.Colors.BorderNormal}"/>
            <SolidColorBrush x:Key="{x:Static SystemColors.InactiveSelectionHighlightBrushKey}"
                             Color="{StaticResource Synthetic.Colors.ControlSurfaceLighter}"/>
            <SolidColorBrush x:Key="{x:Static SystemColors.HighlightTextBrushKey}"
                             Color="{StaticResource Synthetic.Colors.TextPrimary}"/>
            <SolidColorBrush x:Key="{x:Static SystemColors.InactiveSelectionHighlightTextBrushKey}"
                             Color="{StaticResource Synthetic.Colors.TextSecondary}"/>
        </Style.Resources>
        <Style.Triggers>
            <Trigger Property="IsMouseOver" Value="True">
                <Setter Property="Background" Value="#1A3A3938"/>
            </Trigger>
            <Trigger Property="IsSelected" Value="True">
                <Setter Property="Background" Value="{StaticResource Synthetic.Brushes.BorderNormal}"/>
                <Setter Property="Foreground" Value="{StaticResource Synthetic.Brushes.TextPrimary}"/>
            </Trigger>
        </Style.Triggers>
    </Style>

    <!-- DataGridCell: no border, suppress focus rectangle -->
    <Style TargetType="DataGridCell">
        <Setter Property="BorderThickness"     Value="0"/>
        <Setter Property="Padding"             Value="0"/>
        <Setter Property="FocusVisualStyle"    Value="{x:Null}"/>
        <Setter Property="SnapsToDevicePixels" Value="True"/>
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="DataGridCell">
                    <Border Background="{TemplateBinding Background}"
                            BorderThickness="0"
                            SnapsToDevicePixels="True">
                        <ContentPresenter VerticalAlignment="Center"/>
                    </Border>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <!-- DataGrid (implicit) -->
    <Style TargetType="DataGrid">
        <Setter Property="Background"                Value="{StaticResource Synthetic.Brushes.ControlSurface}"/>
        <Setter Property="Foreground"                Value="{StaticResource Synthetic.Brushes.TextPrimary}"/>
        <Setter Property="BorderBrush"               Value="{StaticResource Synthetic.Brushes.BorderNormal}"/>
        <Setter Property="BorderThickness"           Value="1"/>
        <!-- Suppress zebra striping — both row backgrounds are the same surface -->
        <Setter Property="RowBackground"             Value="{StaticResource Synthetic.Brushes.ControlSurface}"/>
        <Setter Property="AlternatingRowBackground"  Value="{StaticResource Synthetic.Brushes.ControlSurface}"/>
        <Setter Property="GridLinesVisibility"       Value="Horizontal"/>
        <Setter Property="HorizontalGridLinesBrush"  Value="{StaticResource Synthetic.Brushes.BorderNormal}"/>
        <Setter Property="VerticalGridLinesBrush"    Value="Transparent"/>
        <Setter Property="HeadersVisibility"         Value="Column"/>
        <Setter Property="RowHeaderWidth"            Value="0"/>
        <Setter Property="AutoGenerateColumns"       Value="False"/>
        <Setter Property="CanUserAddRows"            Value="False"/>
        <Setter Property="CanUserDeleteRows"         Value="False"/>
        <Setter Property="RowHeight"                 Value="32"/>
        <Setter Property="SelectionMode"             Value="Single"/>
        <Setter Property="SnapsToDevicePixels"       Value="True"/>
    </Style>

    <!-- ================================================================ -->
    <!-- TREEVIEW STYLES                                                   -->
    <!-- TreeView: flat #252423 container, thin border                    -->
    <!-- TreeViewItem: path-chevron expander, desaturated hover/selection -->
    <!-- ================================================================ -->

    <!-- Internal expander ToggleButton for TreeViewItem -->
    <Style x:Key="Synthetic.Internal.TreeViewItemToggle" TargetType="ToggleButton">
        <Setter Property="Focusable"       Value="False"/>
        <Setter Property="Width"           Value="16"/>
        <Setter Property="Height"          Value="16"/>
        <Setter Property="Background"      Value="Transparent"/>
        <Setter Property="BorderThickness" Value="0"/>
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="ToggleButton">
                    <Grid Background="Transparent">
                        <!-- Collapsed chevron: pointing right → -->
                        <Path x:Name="ExpandPath"
                              Data="M5,3 L11,8 L5,13"
                              Stroke="{StaticResource Synthetic.Brushes.TextSecondary}"
                              StrokeThickness="1.5"
                              StrokeLineJoin="Round"
                              HorizontalAlignment="Center"
                              VerticalAlignment="Center"
                              SnapsToDevicePixels="True"/>
                    </Grid>
                    <ControlTemplate.Triggers>
                        <!-- Checked (expanded): chevron rotates to point down ↓ -->
                        <Trigger Property="IsChecked" Value="True">
                            <Setter TargetName="ExpandPath" Property="Data"
                                    Value="M3,5 L8,11 L13,5"/>
                            <Setter TargetName="ExpandPath" Property="Stroke"
                                    Value="{StaticResource Synthetic.Brushes.AccentActive}"/>
                        </Trigger>
                        <Trigger Property="IsMouseOver" Value="True">
                            <Setter TargetName="ExpandPath" Property="Stroke"
                                    Value="{StaticResource Synthetic.Brushes.TextPrimary}"/>
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <Style TargetType="TreeViewItem">
        <Setter Property="Background"          Value="Transparent"/>
        <Setter Property="Foreground"          Value="{StaticResource Synthetic.Brushes.TextPrimary}"/>
        <Setter Property="Padding"             Value="4,2"/>
        <Setter Property="Margin"              Value="0,1"/>
        <Setter Property="IsTabStop"           Value="False"/>
        <Setter Property="FocusVisualStyle"    Value="{x:Null}"/>
        <Setter Property="SnapsToDevicePixels" Value="True"/>
        <Setter Property="HorizontalContentAlignment" Value="Left"/>
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="TreeViewItem">
                    <Grid>
                        <Grid.RowDefinitions>
                            <RowDefinition Height="Auto"/>
                            <RowDefinition/>
                        </Grid.RowDefinitions>

                        <!-- Item Header row -->
                        <Border x:Name="ItemBd"
                                Grid.Row="0"
                                Background="{TemplateBinding Background}"
                                SnapsToDevicePixels="True">
                            <Grid>
                                <Grid.ColumnDefinitions>
                                    <ColumnDefinition Width="Auto"/>
                                    <ColumnDefinition Width="Auto"/>
                                    <ColumnDefinition Width="*"/>
                                </Grid.ColumnDefinitions>

                                <!-- Indent spacer — TreeView handles nesting via ItemsPanel indent -->
                                <Rectangle Grid.Column="0" Width="{Binding ActualWidth,
                                           RelativeSource={RelativeSource Self}}"/>

                                <!-- Expander Toggle -->
                                <ToggleButton Grid.Column="1"
                                              x:Name="Expander"
                                              Style="{StaticResource Synthetic.Internal.TreeViewItemToggle}"
                                              IsChecked="{Binding IsExpanded,
                                                          RelativeSource={RelativeSource TemplatedParent},
                                                          Mode=TwoWay}"
                                              Visibility="Hidden"/>

                                <!-- Content -->
                                <ContentPresenter Grid.Column="2"
                                                  x:Name="PART_Header"
                                                  ContentSource="Header"
                                                  HorizontalAlignment="Left"
                                                  Margin="{TemplateBinding Padding}"
                                                  VerticalAlignment="Center"/>
                            </Grid>
                        </Border>

                        <!-- Children -->
                        <ItemsPresenter Grid.Row="1" x:Name="ItemsHost"
                                        Margin="16,0,0,0"
                                        Visibility="Collapsed"/>
                    </Grid>
                    <ControlTemplate.Triggers>
                        <!-- Show expander only when there are child items -->
                        <Trigger Property="HasItems" Value="True">
                            <Setter TargetName="Expander" Property="Visibility" Value="Visible"/>
                        </Trigger>
                        <!-- Show children when expanded -->
                        <Trigger Property="IsExpanded" Value="True">
                            <Setter TargetName="ItemsHost" Property="Visibility" Value="Visible"/>
                        </Trigger>
                        <!-- Hover -->
                        <Trigger Property="IsMouseOver" Value="True">
                            <Setter TargetName="ItemBd" Property="Background" Value="#1A3A3938"/>
                        </Trigger>
                        <!-- Selected + focused -->
                        <Trigger Property="IsSelected" Value="True">
                            <Setter TargetName="ItemBd" Property="Background"
                                    Value="{StaticResource Synthetic.Brushes.BorderNormal}"/>
                            <Setter Property="Foreground"
                                    Value="{StaticResource Synthetic.Brushes.TextPrimary}"/>
                        </Trigger>
                        <!-- Selected + unfocused -->
                        <MultiTrigger>
                            <MultiTrigger.Conditions>
                                <Condition Property="IsSelected" Value="True"/>
                                <Condition Property="IsSelectionActive" Value="False"/>
                            </MultiTrigger.Conditions>
                            <Setter TargetName="ItemBd" Property="Background"
                                    Value="{StaticResource Synthetic.Brushes.ControlSurfaceLighter}"/>
                            <Setter Property="Foreground"
                                    Value="{StaticResource Synthetic.Brushes.TextSecondary}"/>
                        </MultiTrigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <Style TargetType="TreeView">
        <Setter Property="Background"          Value="{StaticResource Synthetic.Brushes.ControlSurface}"/>
        <Setter Property="BorderBrush"         Value="{StaticResource Synthetic.Brushes.BorderNormal}"/>
        <Setter Property="BorderThickness"     Value="1"/>
        <Setter Property="Foreground"          Value="{StaticResource Synthetic.Brushes.TextPrimary}"/>
        <Setter Property="Padding"             Value="4"/>
        <Setter Property="SnapsToDevicePixels" Value="True"/>
    </Style>

    <!-- Implicit TabItem Style -->
    <Style TargetType="{x:Type TabItem}">
        <Setter Property="OverridesDefaultStyle" Value="True"/>
        <Setter Property="Background" Value="Transparent"/>
        <Setter Property="Foreground" Value="{DynamicResource Synthetic.Brushes.TextSecondary}"/>
        <Setter Property="Padding" Value="12,8"/>
        <Setter Property="Margin" Value="0,0,2,0"/>
        <Setter Property="FocusVisualStyle" Value="{x:Null}"/>
        <Setter Property="SnapsToDevicePixels" Value="True"/>
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="{x:Type TabItem}">
                    <Border x:Name="TabBorder"
                            Background="{TemplateBinding Background}"
                            BorderBrush="Transparent"
                            BorderThickness="0,0,0,2"
                            Padding="{TemplateBinding Padding}"
                            SnapsToDevicePixels="True">
                        <ContentPresenter x:Name="ContentSite"
                                          ContentSource="Header"
                                          HorizontalAlignment="Center"
                                          VerticalAlignment="Center"
                                          RecognizesAccessKey="True"/>
                    </Border>
                    <ControlTemplate.Triggers>
                        <!-- Hover State -->
                        <Trigger Property="IsMouseOver" Value="True">
                            <Setter Property="Background" Value="{DynamicResource Synthetic.Brushes.ControlSurfaceLighter}"/>
                            <Setter Property="Foreground" Value="{DynamicResource Synthetic.Brushes.TextPrimary}"/>
                        </Trigger>
                        <!-- Selected State -->
                        <Trigger Property="IsSelected" Value="True">
                            <Setter Property="Background" Value="{DynamicResource Synthetic.Brushes.ControlSurfaceLighter}"/>
                            <Setter Property="Foreground" Value="{DynamicResource Synthetic.Brushes.TextPrimary}"/>
                            <Setter TargetName="TabBorder" Property="BorderBrush" Value="{DynamicResource Synthetic.Brushes.AccentActive}"/>
                        </Trigger>
                        <!-- Disabled State -->
                        <Trigger Property="IsEnabled" Value="False">
                            <Setter Property="Foreground" Value="#55A1A1AA"/>
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <!-- Implicit TabControl Style -->
    <Style TargetType="{x:Type TabControl}">
        <Setter Property="OverridesDefaultStyle" Value="True"/>
        <Setter Property="Background" Value="{DynamicResource Synthetic.Brushes.ControlSurface}"/>
        <Setter Property="BorderBrush" Value="{DynamicResource Synthetic.Brushes.BorderNormal}"/>
        <Setter Property="BorderThickness" Value="1"/>
        <Setter Property="Padding" Value="0"/>
        <Setter Property="SnapsToDevicePixels" Value="True"/>
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="{x:Type TabControl}">
                    <Grid KeyboardNavigation.TabNavigation="Local">
                        <Grid.RowDefinitions>
                            <RowDefinition Height="Auto"/>
                            <RowDefinition Height="*"/>
                        </Grid.RowDefinitions>
                        <!-- TabPanel for Headers -->
                        <TabPanel Grid.Row="0"
                                  IsItemsHost="True"
                                  Panel.ZIndex="1"
                                  Background="Transparent"
                                  KeyboardNavigation.TabNavigation="Once"
                                  Margin="0,0,0,-1"/>
                        <!-- Main Content Area Border -->
                        <Border Grid.Row="1"
                                Background="{TemplateBinding Background}"
                                BorderBrush="{TemplateBinding BorderBrush}"
                                BorderThickness="{TemplateBinding BorderThickness}"
                                Padding="{TemplateBinding Padding}">
                            <ContentPresenter ContentSource="SelectedContent"/>
                        </Border>
                    </Grid>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <!-- Implicit ToggleButton Style for Expander Header -->
    <Style x:Key="Synthetic.Styles.ExpanderHeaderToggle" TargetType="{x:Type ToggleButton}">
        <Setter Property="OverridesDefaultStyle" Value="True"/>
        <Setter Property="Background" Value="Transparent"/>
        <Setter Property="Foreground" Value="{DynamicResource Synthetic.Brushes.TextPrimary}"/>
        <Setter Property="Padding" Value="4"/>
        <Setter Property="HorizontalContentAlignment" Value="Left"/>
        <Setter Property="VerticalContentAlignment" Value="Center"/>
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="{x:Type ToggleButton}">
                    <Border Background="{TemplateBinding Background}"
                            Padding="{TemplateBinding Padding}"
                            SnapsToDevicePixels="True">
                        <Grid>
                            <Grid.ColumnDefinitions>
                                <ColumnDefinition Width="Auto"/>
                                <ColumnDefinition Width="*"/>
                            </Grid.ColumnDefinitions>
                            
                            <!-- Chevron Arrow Path -->
                            <Path x:Name="Arrow"
                                  Grid.Column="0"
                                  Data="M 1,1.5 L 4.5,5 L 8,1.5"
                                  Stroke="{DynamicResource Synthetic.Brushes.TextSecondary}"
                                  StrokeThickness="2"
                                  StrokeLineJoin="Round"
                                  StrokeEndLineCap="Round"
                                  StrokeStartLineCap="Round"
                                  Width="10"
                                  Height="8"
                                  Margin="2,0,8,0"
                                  HorizontalAlignment="Center"
                                  VerticalAlignment="Center"
                                  RenderTransformOrigin="0.5,0.5">
                                <Path.RenderTransform>
                                    <RotateTransform Angle="-90"/>
                                </Path.RenderTransform>
                            </Path>
                            
                            <ContentPresenter Grid.Column="1"
                                              Content="{TemplateBinding Content}"
                                              ContentTemplate="{TemplateBinding ContentTemplate}"
                                              HorizontalAlignment="{TemplateBinding HorizontalContentAlignment}"
                                              VerticalAlignment="{TemplateBinding VerticalContentAlignment}"/>
                        </Grid>
                    </Border>
                    <ControlTemplate.Triggers>
                        <!-- Chevron Rotation Trigger -->
                        <Trigger Property="IsChecked" Value="True">
                            <Setter TargetName="Arrow" Property="RenderTransform">
                                <Setter.Value>
                                    <RotateTransform Angle="0"/>
                                </Setter.Value>
                            </Setter>
                            <Setter TargetName="Arrow" Property="Stroke" Value="{DynamicResource Synthetic.Brushes.TextPrimary}"/>
                        </Trigger>
                        <!-- Hover states -->
                        <Trigger Property="IsMouseOver" Value="True">
                            <Setter Property="Background" Value="{DynamicResource Synthetic.Brushes.ControlSurfaceLighter}"/>
                            <Setter TargetName="Arrow" Property="Stroke" Value="{DynamicResource Synthetic.Brushes.TextPrimary}"/>
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <!-- Implicit Expander Style -->
    <Style TargetType="{x:Type Expander}">
        <Setter Property="OverridesDefaultStyle" Value="True"/>
        <Setter Property="Background" Value="Transparent"/>
        <Setter Property="Foreground" Value="{DynamicResource Synthetic.Brushes.TextPrimary}"/>
        <Setter Property="BorderBrush" Value="{DynamicResource Synthetic.Brushes.BorderNormal}"/>
        <Setter Property="BorderThickness" Value="1"/>
        <Setter Property="HorizontalContentAlignment" Value="Stretch"/>
        <Setter Property="VerticalContentAlignment" Value="Stretch"/>
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="{x:Type Expander}">
                    <Border BorderBrush="{TemplateBinding BorderBrush}"
                            BorderThickness="{TemplateBinding BorderThickness}"
                            Background="{TemplateBinding Background}"
                            CornerRadius="3"
                            SnapsToDevicePixels="True">
                        <DockPanel>
                            <ToggleButton DockPanel.Dock="Top"
                                          Style="{StaticResource Synthetic.Styles.ExpanderHeaderToggle}"
                                          Content="{TemplateBinding Header}"
                                          ContentTemplate="{TemplateBinding HeaderTemplate}"
                                          IsChecked="{Binding IsExpanded, RelativeSource={RelativeSource TemplatedParent}, Mode=TwoWay}"
                                          Padding="8,6"
                                          Background="{DynamicResource Synthetic.Brushes.ControlSurface}"/>
                            
                            <!-- Content Area -->
                            <ContentPresenter x:Name="ExpandSite"
                                              DockPanel.Dock="Bottom"
                                              Visibility="Collapsed"
                                              Content="{TemplateBinding Content}"
                                              ContentTemplate="{TemplateBinding ContentTemplate}"
                                              Margin="{TemplateBinding Padding}"
                                              HorizontalAlignment="{TemplateBinding HorizontalContentAlignment}"
                                              VerticalAlignment="{TemplateBinding VerticalContentAlignment}"/>
                        </DockPanel>
                    </Border>
                    <ControlTemplate.Triggers>
                        <!-- Trigger to show content when expanded -->
                        <Trigger Property="IsExpanded" Value="True">
                            <Setter TargetName="ExpandSite" Property="Visibility" Value="Visible"/>
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <!-- Implicit ToolTip Style -->
    <Style TargetType="{x:Type ToolTip}">
        <Setter Property="OverridesDefaultStyle" Value="True"/>
        <Setter Property="Background" Value="{DynamicResource Synthetic.Brushes.ControlSurface}"/>
        <Setter Property="Foreground" Value="{DynamicResource Synthetic.Brushes.TextPrimary}"/>
        <Setter Property="BorderBrush" Value="{DynamicResource Synthetic.Brushes.BorderNormal}"/>
        <Setter Property="BorderThickness" Value="1"/>
        <Setter Property="Padding" Value="8,4"/>
        <Setter Property="HasDropShadow" Value="False"/>
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="{x:Type ToolTip}">
                    <Border Background="{TemplateBinding Background}"
                            BorderBrush="{TemplateBinding BorderBrush}"
                            BorderThickness="{TemplateBinding BorderThickness}"
                            Padding="{TemplateBinding Padding}"
                            SnapsToDevicePixels="True">
                        <ContentPresenter Content="{TemplateBinding Content}"
                                          ContentTemplate="{TemplateBinding ContentTemplate}"
                                          ContentTemplateSelector="{TemplateBinding ContentTemplateSelector}"
                                          ContentStringFormat="{TemplateBinding ContentStringFormat}"/>
                    </Border>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <!-- ================================================================ -->
    <!-- SYNTHETIC WINDOW STYLE WITH CUSTOM WINDOW CHROME                 -->
    <!-- Strategy: WindowChrome preserves native Aero snap, max/min,      -->
    <!-- hardware drop shadows, and taskbar behaviour while allowing a     -->
    <!-- fully custom title-bar drawn in the non-client area.             -->
    <!-- ================================================================ -->

    <Style x:Key="SyntheticWindowStyle" TargetType="Window">
        <Setter Property="WindowStyle"               Value="None"/>
        <Setter Property="AllowsTransparency"        Value="False"/>
        <Setter Property="Background"                Value="{StaticResource Synthetic.Brushes.BackgroundBase}"/>
        <Setter Property="Foreground"                Value="{StaticResource Synthetic.Brushes.TextPrimary}"/>
        <Setter Property="BorderBrush"               Value="{StaticResource Synthetic.Brushes.BorderNormal}"/>
        <Setter Property="BorderThickness"           Value="1"/>

        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="Window">
                    <!-- Outer border provides the 1px window edge -->
                    <Border BorderBrush="{StaticResource Synthetic.Brushes.BorderNormal}"
                            BorderThickness="1"
                            Background="{StaticResource Synthetic.Brushes.BackgroundBase}"
                            SnapsToDevicePixels="True">
                        <DockPanel LastChildFill="True">

                            <!-- ── Custom Title Bar ── -->
                            <Grid DockPanel.Dock="Top"
                                  Height="45"
                                  Background="{StaticResource Synthetic.Brushes.BackgroundBase}">
                                <Grid.ColumnDefinitions>
                                    <!-- Draggable title region -->
                                    <ColumnDefinition Width="*"/>
                                    <!-- Chrome buttons -->
                                    <ColumnDefinition Width="Auto"/>
                                </Grid.ColumnDefinitions>

                                <!-- Title text - full drag region -->
                                <TextBlock Grid.Column="0"
                                           Text="{TemplateBinding Title}"
                                           Foreground="{StaticResource Synthetic.Brushes.TextPrimary}"
                                           FontFamily="Gotham Condensed, Barlow Condensed, Segoe UI"
                                           FontSize="19.5"
                                           FontWeight="Bold"
                                           TextOptions.TextFormattingMode="Display"
                                           VerticalAlignment="Center"
                                           Margin="14,0,0,0"
                                           shell:WindowChrome.IsHitTestVisibleInChrome="False"/>

                                <!-- Chrome buttons panel -->
                                <StackPanel Grid.Column="1"
                                            Orientation="Horizontal"
                                            VerticalAlignment="Top">

                                    <!-- Minimize -->
                                    <Button x:Name="MinimizeButton"
                                            Style="{StaticResource Synthetic.Styles.ChromeButton}"
                                            ToolTip="Minimize"
                                            Command="{x:Static SystemCommands.MinimizeWindowCommand}">
                                        <Path Data="M0,0 H10"
                                              Stroke="{StaticResource Synthetic.Brushes.TextSecondary}"
                                              StrokeThickness="1.5"
                                              HorizontalAlignment="Center"
                                              VerticalAlignment="Center"
                                              SnapsToDevicePixels="True"/>
                                    </Button>

                                    <!-- Maximize / Restore -->
                                    <Button x:Name="MaxRestoreButton"
                                            Style="{StaticResource Synthetic.Styles.ChromeButton}"
                                            ToolTip="Maximize"
                                            Command="{x:Static SystemCommands.MaximizeWindowCommand}">
                                        <!-- Maximize icon (rectangle outline) -->
                                        <Path x:Name="MaximizeIcon"
                                              Data="M0,0 H10 V10 H0 Z"
                                              Stroke="{StaticResource Synthetic.Brushes.TextSecondary}"
                                              StrokeThickness="1.5"
                                              Fill="Transparent"
                                              HorizontalAlignment="Center"
                                              VerticalAlignment="Center"
                                              SnapsToDevicePixels="True"/>
                                    </Button>

                                    <!-- Close -->
                                    <Button x:Name="CloseButton"
                                            Style="{StaticResource Synthetic.Styles.ChromeCloseButton}"
                                            ToolTip="Close"
                                            Command="{x:Static SystemCommands.CloseWindowCommand}">
                                        <Path Data="M0,0 L10,10 M10,0 L0,10"
                                              Stroke="{StaticResource Synthetic.Brushes.TextSecondary}"
                                              StrokeThickness="1.5"
                                              HorizontalAlignment="Center"
                                              VerticalAlignment="Center"
                                              SnapsToDevicePixels="True"/>
                                    </Button>
                                </StackPanel>
                            </Grid>

                            <!-- ── 1px Separator ── -->
                            <Border DockPanel.Dock="Top"
                                    Height="1"
                                    Background="{StaticResource Synthetic.Brushes.BorderNormal}"/>

                            <!-- ── Main Content Area ── -->
                            <AdornerDecorator>
                                <ContentPresenter/>
                            </AdornerDecorator>
                        </DockPanel>
                    </Border>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

</ResourceDictionary>
```

### File: Shared/UI/ViewModelBase.cs
```csharp
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Synthetic.Shared.UI;
using Synthetic.Modules.RevitDOM;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.Shared.RevitAPI;
namespace Synthetic.Shared.UI{
    /// <summary>
    /// Abstract base class implementing <see cref="INotifyPropertyChanged"/> for ViewModel data binding.
    /// </summary>
    public abstract class ViewModelBase : INotifyPropertyChanged
    {
        /// <summary>
        /// Event raised when a property value changes.
        /// </summary>
        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>
        /// Raises the <see cref="PropertyChanged"/> event.
        /// </summary>
        /// <param name="propertyName">The name of the property that changed.</param>
        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        /// <summary>
        /// Sets a property value and raises the <see cref="PropertyChanged"/> event if the value changed.
        /// </summary>
        /// <typeparam name="T">The type of the property.</typeparam>
        /// <param name="storage">Reference to the backing field of the property.</param>
        /// <param name="value">The new value to set.</param>
        /// <param name="propertyName">The name of the property.</param>
        /// <returns>True if the value changed, false otherwise.</returns>
        protected virtual bool SetProperty<T>(ref T storage, T value, [CallerMemberName] string? propertyName = null)
        {
            if (Equals(storage, value)) return false;
            storage = value;
            OnPropertyChanged(propertyName);
            return true;
        }
    }
}
```

### File: Shared/UI/WindowChromeBehavior.cs
```csharp
using System.Windows;
using System.Windows.Input;
using System.Windows.Shell;

namespace Synthetic.Shared.UI
{
    /// <summary>
    /// Provides attached properties that hook up standard WPF
    /// <see cref="SystemCommands"/> (Close, Minimize, Maximize, Restore) and a
    /// native <see cref="WindowChrome"/> to a <see cref="Window"/> whose title bar
    /// is rendered with the custom <c>SyntheticWindowStyle</c> ControlTemplate.
    ///
    /// Usage — add this to the Window:
    ///   <code>
    ///   local:WindowChromeBehavior.EnableWindowCommands="True"
    ///   </code>
    /// </summary>
    public static class WindowChromeBehavior
    {
        /// <summary>
        /// Attached property that, when set to <c>True</c> on a <see cref="Window"/>,
        /// applies a <see cref="WindowChrome"/> and registers the four
        /// <see cref="SystemCommands"/> bindings required by the custom chrome buttons.
        /// </summary>
        public static readonly DependencyProperty EnableWindowCommandsProperty =
            DependencyProperty.RegisterAttached(
                "EnableWindowCommands",
                typeof(bool),
                typeof(WindowChromeBehavior),
                new PropertyMetadata(false, OnEnableWindowCommandsChanged));

        /// <summary>Gets the <see cref="EnableWindowCommandsProperty"/> value.</summary>
        public static bool GetEnableWindowCommands(DependencyObject obj)
            => (bool)obj.GetValue(EnableWindowCommandsProperty);

        /// <summary>Sets the <see cref="EnableWindowCommandsProperty"/> value.</summary>
        public static void SetEnableWindowCommands(DependencyObject obj, bool value)
            => obj.SetValue(EnableWindowCommandsProperty, value);

        // ────────────────────────────────────────────────────────────────

        private static void OnEnableWindowCommandsChanged(
            DependencyObject d,
            DependencyPropertyChangedEventArgs e)
        {
            if (d is not Window window)
                return;

            if ((bool)e.NewValue)
                AttachCommandBindings(window);
            else
                DetachCommandBindings(window);
        }

        private static void AttachCommandBindings(Window window)
        {
            // Apply the WindowChrome so native Aero snapping, resize, and
            // taskbar integration are preserved while WindowStyle="None" is set.
            WindowChrome.SetWindowChrome(window, new WindowChrome
            {
                CaptionHeight           = 45,
                ResizeBorderThickness   = new Thickness(5),
                GlassFrameThickness     = new Thickness(0),
                CornerRadius            = new CornerRadius(0),
                UseAeroCaptionButtons   = false,
            });

            // Close
            window.CommandBindings.Add(new CommandBinding(
                SystemCommands.CloseWindowCommand,
                (_, __) => SystemCommands.CloseWindow(window)));

            // Minimize
            window.CommandBindings.Add(new CommandBinding(
                SystemCommands.MinimizeWindowCommand,
                (_, __) => SystemCommands.MinimizeWindow(window)));

            // Maximize
            window.CommandBindings.Add(new CommandBinding(
                SystemCommands.MaximizeWindowCommand,
                (_, __) => SystemCommands.MaximizeWindow(window)));

            // Restore
            window.CommandBindings.Add(new CommandBinding(
                SystemCommands.RestoreWindowCommand,
                (_, __) => SystemCommands.RestoreWindow(window)));
        }

        private static void DetachCommandBindings(Window window)
        {
            // Remove the WindowChrome
            WindowChrome.SetWindowChrome(window, null);

            // Remove only the SystemCommands bindings added by this behavior.
            for (int i = window.CommandBindings.Count - 1; i >= 0; i--)
            {
                var cb = window.CommandBindings[i];
                if (cb.Command == SystemCommands.CloseWindowCommand    ||
                    cb.Command == SystemCommands.MinimizeWindowCommand  ||
                    cb.Command == SystemCommands.MaximizeWindowCommand  ||
                    cb.Command == SystemCommands.RestoreWindowCommand)
                {
                    window.CommandBindings.RemoveAt(i);
                }
            }
        }
    }
}
```

### File: Shared/UI/WindowsFileDialogService.cs
```csharp
using System;
using Microsoft.Win32;

namespace Synthetic.Shared.UI
{
    /// <summary>
    /// Production implementation of IFileDialogService using Microsoft.Win32 file dialogs.
    /// </summary>
    public class WindowsFileDialogService : IFileDialogService
    {
        /// <summary>
        /// Displays the native Microsoft.Win32.SaveFileDialog.
        /// </summary>
        public string? SaveFileDialog(string filter, string title, string defaultFileName)
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = filter,
                Title = title,
                FileName = defaultFileName
            };

            if (dialog.ShowDialog() == true)
            {
                return dialog.FileName;
            }

            return null;
        }

        /// <summary>
        /// Displays the native Microsoft.Win32.OpenFileDialog.
        /// </summary>
        public string? OpenFileDialog(string filter, string title, string defaultFileName)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = filter,
                Title = title,
                FileName = defaultFileName
            };

            if (dialog.ShowDialog() == true)
            {
                return dialog.FileName;
            }

            return null;
        }
    }
}
```

### File: Shared/UI/WindowsGuardrailPromptService.cs
```csharp
using System;
using System.Windows;
using System.Diagnostics;

namespace Synthetic.Shared.UI
{
    /// <summary>
    /// Production implementation of IGuardrailPromptService using custom GuardrailPromptWindow.
    /// </summary>
    public class WindowsGuardrailPromptService : IGuardrailPromptService
    {
        /// <inheritdoc/>
        public GuardrailResult PromptProtectedFileOverwrite(string filePath)
        {
            IntPtr ownerHandle = Process.GetCurrentProcess().MainWindowHandle;
            var dialog = new GuardrailPromptWindow(filePath, ownerHandle);

            try
            {
                if (System.Windows.Application.Current?.MainWindow != null)
                {
                    dialog.Owner = System.Windows.Application.Current.MainWindow;
                }
            }
            catch {}

            bool? dialogResult = dialog.ShowDialog();
            if (dialogResult == true)
            {
                return dialog.Result;
            }

            return GuardrailResult.Cancel;
        }
    }
}
```

### File: Shared/UI/WindowsSummaryDisplayService.cs
```csharp
using System;
using Synthetic.Modules.StandardsManagement.Views;

namespace Synthetic.Shared.UI
{
    /// <summary>
    /// Production implementation of ISummaryDisplayService displaying a WPF summary dialog.
    /// </summary>
    public class WindowsSummaryDisplayService : ISummaryDisplayService
    {
        /// <summary>
        /// Displays the WPF ImportSummaryWindow dialog.
        /// </summary>
        public void ShowSummary(object viewModel, IntPtr parentWindowHandle)
        {
            var window = new ImportSummaryWindow(parentWindowHandle, viewModel);
            window.ShowDialog();
        }
    }

    /// <summary>
    /// No-op implementation of ISummaryDisplayService for headless testing contexts.
    /// </summary>
    public class NoOpSummaryDisplayService : ISummaryDisplayService
    {
        /// <summary>
        /// Does nothing.
        /// </summary>
        public void ShowSummary(object viewModel, IntPtr parentWindowHandle)
        {
        }
    }
}
```

### File: Shared/UI/WindowsUserPromptService.cs
```csharp
using System;
using System.Windows;

namespace Synthetic.Shared.UI
{
    /// <summary>
    /// Production implementation of IUserPromptService using WPF's MessageBox.
    /// </summary>
    public class WindowsUserPromptService : IUserPromptService
    {
        /// <summary>
        /// Displays an informational message box.
        /// </summary>
        public void ShowMessage(string message, string title)
        {
            System.Windows.MessageBox.Show(message, title, System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
        }

        /// <summary>
        /// Displays a confirmation message box with Yes/No options.
        /// </summary>
        public bool ConfirmAction(string message, string title)
        {
            var result = System.Windows.MessageBox.Show(message, title, System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question);
            return result == System.Windows.MessageBoxResult.Yes;
        }
    }
}
```

### File: src/fix_qualified_references.py
```python
import os
import re

workspace_dir = r"c:\Users\amcgoey\Dropbox\Projects\Revit API Synthetic v2"
src_dir = os.path.join(workspace_dir, "src")

# 1. Map of relocated files to their new namespaces
relocated_mappings = {
    # MergeDuplicates
    r"SyntheticShared\Modules\MergeDuplicates\Commands\CmdMergeDuplicates.cs": ("Synthetic.Commands", "Synthetic.Modules.MergeDuplicates.Commands"),
    r"SyntheticShared\Modules\MergeDuplicates\Handlers\ProcessMergeEventHandler.cs": ("Synthetic", "Synthetic.Modules.MergeDuplicates.Handlers"),
    r"SyntheticShared\Modules\MergeDuplicates\Engine\MergeAnalysisEngine.cs": ("Synthetic", "Synthetic.Modules.MergeDuplicates.Engine"),
    r"SyntheticShared\Modules\MergeDuplicates\ViewModels\MergeDuplicatesViewModel.cs": ("Synthetic.ViewModels", "Synthetic.Modules.MergeDuplicates.ViewModels"),
    r"SyntheticShared\Modules\MergeDuplicates\ViewModels\MergeDetailedReviewViewModel.cs": ("Synthetic.ViewModels", "Synthetic.Modules.MergeDuplicates.ViewModels"),
    r"SyntheticShared\Modules\MergeDuplicates\ViewModels\MergeQueueViewModel.cs": ("Synthetic.ViewModels", "Synthetic.Modules.MergeDuplicates.ViewModels"),
    r"SyntheticShared\Modules\MergeDuplicates\Views\MergeDuplicatesWindow.xaml.cs": ("Synthetic.Views", "Synthetic.Modules.MergeDuplicates.Views"),
    r"SyntheticShared\Modules\MergeDuplicates\Views\MergeDuplicatesWindow.xaml": ("Synthetic.Views", "Synthetic.Modules.MergeDuplicates.Views"),
    r"SyntheticShared\Modules\MergeDuplicates\Views\MergeDetailedReviewWindow.xaml.cs": ("Synthetic.Views", "Synthetic.Modules.MergeDuplicates.Views"),
    r"SyntheticShared\Modules\MergeDuplicates\Views\MergeDetailedReviewWindow.xaml": ("Synthetic.Views", "Synthetic.Modules.MergeDuplicates.Views"),
    r"SyntheticShared\Modules\MergeDuplicates\Models\ParameterDiffRowModel.cs": ("Synthetic.Models", "Synthetic.Modules.MergeDuplicates.Models"),
    r"SyntheticShared\Modules\MergeDuplicates\Models\DuplicateTypeModel.cs": ("Synthetic.Models", "Synthetic.Modules.MergeDuplicates.Models"),
    r"SyntheticShared\Modules\MergeDuplicates\Models\DuplicateItemModel.cs": ("Synthetic.Models", "Synthetic.Modules.MergeDuplicates.Models"),
    r"SyntheticShared\Modules\MergeDuplicates\Models\DuplicateClusterModel.cs": ("Synthetic.Models", "Synthetic.Modules.MergeDuplicates.Models"),
    r"SyntheticShared\Modules\MergeDuplicates\Models\TypeMappingModel.cs": ("Synthetic.Models", "Synthetic.Modules.MergeDuplicates.Models"),
    r"SyntheticShared\Modules\MergeDuplicates\Models\RecommendedAction.cs": ("Synthetic.Models", "Synthetic.Modules.MergeDuplicates.Models"),

    # StandardsManagement
    r"SyntheticShared\Modules\StandardsManagement\Commands\StandardsEditorShow.cs": ("Synthetic", "Synthetic.Modules.StandardsManagement.Commands"),
    r"SyntheticShared\Modules\StandardsManagement\Commands\CmdEnforceStandards.cs": ("Synthetic", "Synthetic.Modules.StandardsManagement.Commands"),
    r"SyntheticShared\Modules\StandardsManagement\Commands\CmdExportStandards.cs": ("Synthetic", "Synthetic.Modules.StandardsManagement.Commands"),
    r"SyntheticShared\Modules\StandardsManagement\Handlers\JsonEditorExternalEventHandler.cs": ("Synthetic", "Synthetic.Modules.StandardsManagement.Handlers"),
    r"SyntheticShared\Modules\StandardsManagement\Engine\StandardsDiffEngine.cs": ("Synthetic", "Synthetic.Modules.StandardsManagement.Engine"),
    r"SyntheticShared\Modules\StandardsManagement\ViewModels\JsonEditorMainViewModel.cs": ("Synthetic.ViewModels", "Synthetic.Modules.StandardsManagement.ViewModels"),
    r"SyntheticShared\Modules\StandardsManagement\ViewModels\NestedDataEditorViewModel.cs": ("Synthetic.ViewModels", "Synthetic.Modules.StandardsManagement.ViewModels"),
    r"SyntheticShared\Modules\StandardsManagement\ViewModels\ExportStylesViewModel.cs": ("Synthetic.ViewModels", "Synthetic.Modules.StandardsManagement.ViewModels"),
    r"SyntheticShared\Modules\StandardsManagement\ViewModels\EnforceStandardsViewModel.cs": ("Synthetic.ViewModels", "Synthetic.Modules.StandardsManagement.ViewModels"),
    r"SyntheticShared\Modules\StandardsManagement\ViewModels\StandardsReviewViewModel.cs": ("Synthetic.ViewModels", "Synthetic.Modules.StandardsManagement.ViewModels"),
    r"SyntheticShared\Modules\StandardsManagement\ViewModels\StandardsClassSelectionViewModel.cs": ("Synthetic.ViewModels", "Synthetic.Modules.StandardsManagement.ViewModels"),
    r"SyntheticShared\Modules\StandardsManagement\ViewModels\ImportSummaryViewModel.cs": ("Synthetic.ViewModels", "Synthetic.Modules.StandardsManagement.ViewModels"),
    r"SyntheticShared\Modules\StandardsManagement\ViewModels\ElementTypeWrapperVM.cs": ("Synthetic.ViewModels", "Synthetic.Modules.StandardsManagement.ViewModels"),
    r"SyntheticShared\Modules\StandardsManagement\ViewModels\ParameterWrapperVM.cs": ("Synthetic.ViewModels", "Synthetic.Modules.StandardsManagement.ViewModels"),
    r"SyntheticShared\Modules\StandardsManagement\ViewModels\FindReplaceViewModel.cs": ("Synthetic.ViewModels", "Synthetic.Modules.StandardsManagement.ViewModels"),
    r"SyntheticShared\Modules\StandardsManagement\ViewModels\CategorySelectionViewModel.cs": ("Synthetic.ViewModels", "Synthetic.Modules.StandardsManagement.ViewModels"),
    r"SyntheticShared\Modules\StandardsManagement\Views\JsonEditorWindow.xaml.cs": ("Synthetic.Views", "Synthetic.Modules.StandardsManagement.Views"),
    r"SyntheticShared\Modules\StandardsManagement\Views\JsonEditorWindow.xaml": ("Synthetic.Views", "Synthetic.Modules.StandardsManagement.Views"),
    r"SyntheticShared\Modules\StandardsManagement\Views\NestedDataEditorWindow.xaml.cs": ("Synthetic.Views", "Synthetic.Modules.StandardsManagement.Views"),
    r"SyntheticShared\Modules\StandardsManagement\Views\NestedDataEditorWindow.xaml": ("Synthetic.Views", "Synthetic.Modules.StandardsManagement.Views"),
    r"SyntheticShared\Modules\StandardsManagement\Views\ExportStylesView.xaml.cs": ("Synthetic.Views", "Synthetic.Modules.StandardsManagement.Views"),
    r"SyntheticShared\Modules\StandardsManagement\Views\ExportStylesView.xaml": ("Synthetic.Views", "Synthetic.Modules.StandardsManagement.Views"),
    r"SyntheticShared\Modules\StandardsManagement\Views\EnforceStandardsView.xaml.cs": ("Synthetic.Views", "Synthetic.Modules.StandardsManagement.Views"),
    r"SyntheticShared\Modules\StandardsManagement\Views\EnforceStandardsView.xaml": ("Synthetic.Views", "Synthetic.Modules.StandardsManagement.Views"),
    r"SyntheticShared\Modules\StandardsManagement\Views\StandardsReviewWindow.xaml.cs": ("Synthetic.Views", "Synthetic.Modules.StandardsManagement.Views"),
    r"SyntheticShared\Modules\StandardsManagement\Views\StandardsReviewWindow.xaml": ("Synthetic.Views", "Synthetic.Modules.StandardsManagement.Views"),
    r"SyntheticShared\Modules\StandardsManagement\Views\StandardsClassSelectionControl.xaml.cs": ("Synthetic.Views", "Synthetic.Modules.StandardsManagement.Views"),
    r"SyntheticShared\Modules\StandardsManagement\Views\StandardsClassSelectionControl.xaml": ("Synthetic.Views", "Synthetic.Modules.StandardsManagement.Views"),
    r"SyntheticShared\Modules\StandardsManagement\Views\FindReplaceWindow.xaml.cs": ("Synthetic.Views", "Synthetic.Modules.StandardsManagement.Views"),
    r"SyntheticShared\Modules\StandardsManagement\Views\FindReplaceWindow.xaml": ("Synthetic.Views", "Synthetic.Modules.StandardsManagement.Views"),
    r"SyntheticShared\Modules\StandardsManagement\Views\ImportSummaryWindow.xaml.cs": ("Synthetic.Views", "Synthetic.Modules.StandardsManagement.Views"),
    r"SyntheticShared\Modules\StandardsManagement\Views\ImportSummaryWindow.xaml": ("Synthetic.Views", "Synthetic.Modules.StandardsManagement.Views"),
    r"SyntheticShared\Modules\StandardsManagement\Views\CategorySelectionControl.xaml.cs": ("Synthetic.Views", "Synthetic.Modules.StandardsManagement.Views"),
    r"SyntheticShared\Modules\StandardsManagement\Views\CategorySelectionControl.xaml": ("Synthetic.Views", "Synthetic.Modules.StandardsManagement.Views"),
    r"SyntheticShared\Modules\StandardsManagement\Models\ImportLogItem.cs": ("Synthetic.Models", "Synthetic.Modules.StandardsManagement.Models"),

    # SettingsDashboard
    r"SyntheticShared\Modules\SettingsDashboard\Commands\SettingsDashboardCommand.cs": ("Synthetic.Commands", "Synthetic.Modules.SettingsDashboard.Commands"),
    r"SyntheticShared\Modules\SettingsDashboard\Handlers\SyncExternalEventHandler.cs": ("Synthetic", "Synthetic.Modules.SettingsDashboard.Handlers"),
    r"SyntheticShared\Modules\SettingsDashboard\ViewModels\SettingsDashboardViewModel.cs": ("Synthetic.ViewModels", "Synthetic.Modules.SettingsDashboard.ViewModels"),
    r"SyntheticShared\Modules\SettingsDashboard\ViewModels\SyncSettingsViewModel.cs": ("Synthetic.ViewModels", "Synthetic.Modules.SettingsDashboard.ViewModels"),
    r"SyntheticShared\Modules\SettingsDashboard\ViewModels\SyncWizardViewModel.cs": ("Synthetic.ViewModels", "Synthetic.Modules.SettingsDashboard.ViewModels"),
    r"SyntheticShared\Modules\SettingsDashboard\ViewModels\FileUtilitySettingsViewModel.cs": ("Synthetic.ViewModels", "Synthetic.Modules.SettingsDashboard.ViewModels"),
    r"SyntheticShared\Modules\SettingsDashboard\ViewModels\MaterialLibraryViewModel.cs": ("Synthetic.ViewModels", "Synthetic.Modules.SettingsDashboard.ViewModels"),
    r"SyntheticShared\Modules\SettingsDashboard\ViewModels\ProjectMaterialsViewModel.cs": ("Synthetic.ViewModels", "Synthetic.Modules.SettingsDashboard.ViewModels"),
    r"SyntheticShared\Modules\SettingsDashboard\ViewModels\WorksetSettingsViewModel.cs": ("Synthetic.ViewModels", "Synthetic.Modules.SettingsDashboard.ViewModels"),
    r"SyntheticShared\Modules\SettingsDashboard\ViewModels\WorksetWizardViewModel.cs": ("Synthetic.ViewModels", "Synthetic.Modules.SettingsDashboard.ViewModels"),
    r"SyntheticShared\Modules\SettingsDashboard\ViewModels\ViewAutoNumSettingsViewModel.cs": ("Synthetic.ViewModels", "Synthetic.Modules.SettingsDashboard.ViewModels"),
    r"SyntheticShared\Modules\SettingsDashboard\ViewModels\ViewAutoNumWizardViewModel.cs": ("Synthetic.ViewModels", "Synthetic.Modules.SettingsDashboard.ViewModels"),
    r"SyntheticShared\Modules\SettingsDashboard\ViewModels\NetworkPathsWizardViewModel.cs": ("Synthetic.ViewModels", "Synthetic.Modules.SettingsDashboard.ViewModels"),
    r"SyntheticShared\Modules\SettingsDashboard\ViewModels\PathMappingViewModel.cs": ("Synthetic.ViewModels", "Synthetic.Modules.SettingsDashboard.ViewModels"),
    r"SyntheticShared\Modules\SettingsDashboard\ViewModels\ISettingModuleViewModel.cs": ("Synthetic.ViewModels", "Synthetic.Modules.SettingsDashboard.ViewModels"),
    r"SyntheticShared\Modules\SettingsDashboard\ViewModels\StandardsSettingsViewModel.cs": ("Synthetic.ViewModels", "Synthetic.Modules.SettingsDashboard.ViewModels"),
    r"SyntheticShared\Modules\SettingsDashboard\Views\SettingsDashboardWindow.xaml.cs": ("Synthetic.Views", "Synthetic.Modules.SettingsDashboard.Views"),
    r"SyntheticShared\Modules\SettingsDashboard\Views\SettingsDashboardWindow.xaml": ("Synthetic.Views", "Synthetic.Modules.SettingsDashboard.Views"),
    r"SyntheticShared\Modules\SettingsDashboard\Views\SyncWizardWindow.xaml.cs": ("Synthetic.Views", "Synthetic.Modules.SettingsDashboard.Views"),
    r"SyntheticShared\Modules\SettingsDashboard\Views\SyncWizardWindow.xaml": ("Synthetic.Views", "Synthetic.Modules.SettingsDashboard.Views"),
    r"SyntheticShared\Modules\SettingsDashboard\Views\SyncResolutionWindow.xaml.cs": ("Synthetic.Views", "Synthetic.Modules.SettingsDashboard.Views"),
    r"SyntheticShared\Modules\SettingsDashboard\Views\SyncResolutionWindow.xaml": ("Synthetic.Views", "Synthetic.Modules.SettingsDashboard.Views"),
    r"SyntheticShared\Modules\SettingsDashboard\Views\SyncSettingsView.xaml.cs": ("Synthetic.Views", "Synthetic.Modules.SettingsDashboard.Views"),
    r"SyntheticShared\Modules\SettingsDashboard\Views\SyncSettingsView.xaml": ("Synthetic.Views", "Synthetic.Modules.SettingsDashboard.Views"),
    r"SyntheticShared\Modules\SettingsDashboard\Views\SyncToastNotification.xaml.cs": ("Synthetic.Views", "Synthetic.Modules.SettingsDashboard.Views"),
    r"SyntheticShared\Modules\SettingsDashboard\Views\SyncToastNotification.xaml": ("Synthetic.Views", "Synthetic.Modules.SettingsDashboard.Views"),
    r"SyntheticShared\Modules\SettingsDashboard\Views\NetworkPathsWizardWindow.xaml.cs": ("Synthetic.Views", "Synthetic.Modules.SettingsDashboard.Views"),
    r"SyntheticShared\Modules\SettingsDashboard\Views\NetworkPathsWizardWindow.xaml": ("Synthetic.Views", "Synthetic.Modules.SettingsDashboard.Views"),
    r"SyntheticShared\Modules\SettingsDashboard\Views\ViewAutoNumWizardWindow.xaml.cs": ("Synthetic.Views", "Synthetic.Modules.SettingsDashboard.Views"),
    r"SyntheticShared\Modules\SettingsDashboard\Views\ViewAutoNumWizardWindow.xaml": ("Synthetic.Views", "Synthetic.Modules.SettingsDashboard.Views"),
    r"SyntheticShared\Modules\SettingsDashboard\Views\WorksetWizardWindow.xaml.cs": ("Synthetic.Views", "Synthetic.Modules.SettingsDashboard.Views"),
    r"SyntheticShared\Modules\SettingsDashboard\Views\WorksetWizardWindow.xaml": ("Synthetic.Views", "Synthetic.Modules.SettingsDashboard.Views"),
    r"SyntheticShared\Modules\SettingsDashboard\Views\StandardsSettingsView.xaml.cs": ("Synthetic.Views", "Synthetic.Modules.SettingsDashboard.Views"),
    r"SyntheticShared\Modules\SettingsDashboard\Views\StandardsSettingsView.xaml": ("Synthetic.Views", "Synthetic.Modules.SettingsDashboard.Views"),

    # Shared UI
    r"SyntheticShared\Shared\UI\DropdownSelectionViewModel.cs": ("Synthetic.ViewModels", "Synthetic.Shared.UI"),
    r"SyntheticShared\Shared\UI\DropdownSelectionView.xaml.cs": ("Synthetic.Views", "Synthetic.Shared.UI"),
    r"SyntheticShared\Shared\UI\DropdownSelectionView.xaml": ("Synthetic.Views", "Synthetic.Shared.UI"),
    r"SyntheticShared\Shared\UI\ListByCheckboxViewModel.cs": ("Synthetic.ViewModels", "Synthetic.Shared.UI"),
    r"SyntheticShared\Shared\UI\ListByCheckboxView.xaml.cs": ("Synthetic.Views", "Synthetic.Shared.UI"),
    r"SyntheticShared\Shared\UI\ListByCheckboxView.xaml": ("Synthetic.Views", "Synthetic.Shared.UI"),
    r"SyntheticShared\Shared\UI\SelectSearchPathsViewModel.cs": ("Synthetic.ViewModels", "Synthetic.Shared.UI"),
    r"SyntheticShared\Shared\UI\SelectSearchPathsView.xaml.cs": ("Synthetic.Views", "Synthetic.Shared.UI"),
    r"SyntheticShared\Shared\UI\SelectSearchPathsView.xaml": ("Synthetic.Views", "Synthetic.Shared.UI"),
    r"SyntheticShared\Shared\UI\SharedProgressViewModel.cs": ("Synthetic.ViewModels", "Synthetic.Shared.UI"),
    r"SyntheticShared\Shared\UI\SharedProgressWindow.xaml.cs": ("Synthetic.Views", "Synthetic.Shared.UI"),
    r"SyntheticShared\Shared\UI\SharedProgressWindow.xaml": ("Synthetic.Views", "Synthetic.Shared.UI"),
    r"SyntheticShared\Shared\UI\ProgressCoordinator.cs": ("Synthetic", "Synthetic.Shared.UI"),
    r"SyntheticShared\Shared\UI\ViewModelBase.cs": ("Synthetic.ViewModels", "Synthetic.Shared.UI"),

    # Shared Revit API
    r"SyntheticShared\Shared\RevitAPI\CommandUtil.cs": ("Synthetic", "Synthetic.Shared.RevitAPI"),
    r"SyntheticShared\Shared\RevitAPI\FamilySymbolUtil.cs": ("Synthetic", "Synthetic.Shared.RevitAPI"),
    r"SyntheticShared\Shared\RevitAPI\Select.cs": ("Synthetic", "Synthetic.Shared.RevitAPI"),
    r"SyntheticShared\Shared\RevitAPI\StorageUtil.cs": ("Synthetic", "Synthetic.Shared.RevitAPI"),
    r"SyntheticShared\Shared\EnumUtil.cs": ("Synthetic", "Synthetic.Shared"),

    # Revit DOM Models
    r"SyntheticShared\Modules\RevitDOM\BooleanModel.cs": ("Synthetic", "Synthetic.Modules.RevitDOM"),
    r"SyntheticShared\Modules\RevitDOM\ListModel.cs": ("Synthetic", "Synthetic.Modules.RevitDOM"),
    r"SyntheticShared\Modules\RevitDOM\DimensionTypeModel.cs": ("Synthetic.Models", "Synthetic.Modules.RevitDOM"),

    # DetailItemFactory Model
    r"SyntheticShared\Modules\DetailItemFactory\Models\DetailItemResultItem.cs": ("Synthetic.Models", "Synthetic.Modules.DetailItemFactory.Models"),

    # FamilyManagement Handlers/Utilities
    r"SyntheticShared\Modules\FamilyManagement\Handlers\AuditPurgeEventHandler.cs": ("Synthetic", "Synthetic.Modules.FamilyManagement.Handlers"),
    r"SyntheticShared\Modules\FamilyManagement\Utilities\SafeFamilyLoadOptions.cs": ("Synthetic", "Synthetic.Modules.FamilyManagement.Utilities"),
    r"SyntheticShared\Modules\FamilyManagement\Utilities\PurgeFailuresPreprocessor.cs": ("Synthetic", "Synthetic.Modules.FamilyManagement.Utilities"),

    # Infrastructure IO
    r"SyntheticShared\Infrastructure\IO\SearchPaths.cs": ("Synthetic", "Synthetic.Infrastructure.IO")
}

# Normalize paths to OS specific backslashes
relocated_mappings = {os.path.join(src_dir, os.path.normpath(k)): v for k, v in relocated_mappings.items()}

# 2. Discover all type names and their old/new namespaces
type_to_new_ns = {}
type_to_old_ns = {}

type_decl_re = re.compile(r'\b(class|struct|enum|interface)\s+([a-zA-Z0-9_]+)\b')

for filepath, (old_ns, new_ns) in relocated_mappings.items():
    if not filepath.endswith(".cs"):
        continue
    if not os.path.exists(filepath):
        continue
    with open(filepath, 'r', encoding='utf-8') as f:
        content = f.read()
    
    matches = type_decl_re.findall(content)
    for t_type, t_name in matches:
        type_to_new_ns[t_name] = new_ns
        type_to_old_ns[t_name] = old_ns

# Explicit nested/inner types
nested_types = {
    "SearchScope": ("Synthetic.ViewModels", "Synthetic.Modules.StandardsManagement.ViewModels"),
    "SearchableField": ("Synthetic.ViewModels", "Synthetic.Modules.StandardsManagement.ViewModels"),
    "CompoundLayerRowVM": ("Synthetic.ViewModels", "Synthetic.Modules.StandardsManagement.ViewModels"),
    "VisibilityOverrideRowVM": ("Synthetic.ViewModels", "Synthetic.Modules.StandardsManagement.ViewModels"),
    "CategorySelectionItem": ("Synthetic.ViewModels", "Synthetic.Modules.StandardsManagement.ViewModels"),
    "ProcessMergeFamilyLoadOptions": ("Synthetic", "Synthetic.Modules.MergeDuplicates.Handlers"),
    "MergeExecutionReport": ("Synthetic", "Synthetic.Modules.MergeDuplicates.Handlers"),
    "ParameterValueOption": ("Synthetic.Models", "Synthetic.Modules.MergeDuplicates.Models")
}
for t_name, (old_ns, new_ns) in nested_types.items():
    type_to_new_ns[t_name] = new_ns
    type_to_old_ns[t_name] = old_ns

print(f"Total resolved type names: {len(type_to_new_ns)}")

# 3. Replace all qualified usages solution-wide
# E.g. Synthetic.Models.ImportLogItem -> Synthetic.Modules.StandardsManagement.Models.ImportLogItem
# or Synthetic.ViewModels.ViewModelBase -> Synthetic.Shared.UI.ViewModelBase
count = 0
for root, dirs, files in os.walk(src_dir):
    if any(p in root for p in ["obj", "bin", ".git", ".vs"]):
        continue
    for file in files:
        if not file.endswith((".cs", ".xaml")):
            continue
        filepath = os.path.join(root, file)
        
        with open(filepath, 'r', encoding='utf-8') as f:
            content = f.read()
            
        modified = False
        new_content = content
        
        for type_name, new_ns in type_to_new_ns.items():
            old_ns = type_to_old_ns[type_name]
            
            # Match old qualified reference like "Synthetic.Models.ImportLogItem"
            # whole words check
            pattern_str = r'\b' + re.escape(old_ns) + r'\.' + re.escape(type_name) + r'\b'
            replacement_str = f"{new_ns}.{type_name}"
            
            new_content_2, num_subs = re.subn(pattern_str, replacement_str, new_content)
            if num_subs > 0:
                new_content = new_content_2
                modified = True
                print(f"  Replaced qualified {old_ns}.{type_name} -> {new_ns}.{type_name} in {file}")
                
        if modified:
            with open(filepath, 'w', encoding='utf-8') as f:
                f.write(new_content)
            count += 1

print(f"Completed! Modified {count} files.")
```

