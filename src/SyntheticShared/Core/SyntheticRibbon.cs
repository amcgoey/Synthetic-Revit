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
