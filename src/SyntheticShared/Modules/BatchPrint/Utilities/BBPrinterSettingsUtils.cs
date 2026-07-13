using Synthetic.Modules.BatchPrint.Commands;
using Synthetic.Modules.BatchPrint.Utilities;

using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Win32;
using Synthetic.Modules.RevitDOM;
using Synthetic.Modules.StandardsManagement.ViewModels;

namespace Synthetic.Modules.BatchPrint.Utilities
{
    /// <summary>
    /// Utilities for setting BlueBeam Printer settings
    /// </summary>
    public class BBPrinterSettingsUtils
    {
        /// <summary>
        /// The BlueBeam Printer registry path.
        /// </summary>
        public string BBPrinterRegistryPath =
            "HKEY_CURRENT_USER\\SOFTWARE\\Bluebeam Software\\21\\Brewery\\V45\\Printer Driver";

        /// <summary>
        /// Gets or sets the value specifying whether to open in viewer.
        /// </summary>
        public string OpenInViewer;

        /// <summary>
        /// Gets or sets the value specifying whether to prompt for file name.
        /// </summary>
        public string PromptForFileName;

        /// <summary>
        /// Gets or sets the projects folder path.
        /// </summary>
        public string ProjectsFolder;

        /// <summary>
        /// Gets or sets the save as folder path.
        /// </summary>
        public string SaveAsFolder;

        /// <summary>
        /// Gets or sets the value specifying whether to use the last folder.
        /// </summary>
        public string UseLastFolder;

        /// <summary>
        /// Initializes a new instance of the <see cref="BBPrinterSettingsUtils"/> class by reading values from the registry.
        /// </summary>
        public BBPrinterSettingsUtils ()
        {
            this.OpenInViewer = Registry.GetValue(BBPrinterRegistryPath, "OpenInViewer", "1") as string ?? "1";
            this.PromptForFileName = Registry.GetValue(BBPrinterRegistryPath, "PromptForFileName", "1") as string ?? "1";
            this.ProjectsFolder = Registry.GetValue(BBPrinterRegistryPath, "ProjectsFolder", 
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)) as string ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            this.SaveAsFolder = Registry.GetValue(BBPrinterRegistryPath, "SaveAsFolder", 
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)) as string ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            this.UseLastFolder = Registry.GetValue(BBPrinterRegistryPath, "UseLastFolder", "0") as string ?? "0";
        }
        
        /// <summary>
        /// Initializes a new instance of the <see cref="BBPrinterSettingsUtils"/> class with custom settings.
        /// </summary>
        /// <param name="openInViewer">Specifies whether to open in viewer.</param>
        /// <param name="promptForFileName">Specifies whether to prompt for file name.</param>
        /// <param name="path">The folder path for projects and save locations.</param>
        public BBPrinterSettingsUtils (string openInViewer, string promptForFileName, string path)
        {
            this.OpenInViewer = openInViewer;
            this.PromptForFileName = promptForFileName;
            this.ProjectsFolder = path;
            this.SaveAsFolder = path;
            this.UseLastFolder = "2";
        }

        /// <summary>
        /// Sets registry keys according to the current property values.
        /// </summary>
        public void SetRegistryKeys ()
        {
            Registry.SetValue(BBPrinterRegistryPath, "OpenInViewer", this.OpenInViewer);
            Registry.SetValue(BBPrinterRegistryPath, "PromptForFileName", this.PromptForFileName);
            Registry.SetValue(BBPrinterRegistryPath, "ProjectsFolder", this.ProjectsFolder);
            Registry.SetValue(BBPrinterRegistryPath, "SaveAsFolder", this.SaveAsFolder);
            Registry.SetValue(BBPrinterRegistryPath, "UseLastFolder", this.UseLastFolder);
        }

        /// <summary>
        /// Sets registry keys to their default values.
        /// </summary>
        public void SetDefaultRegistryKeys()
        {
            Registry.SetValue(BBPrinterRegistryPath, "OpenInViewer", "1");
            Registry.SetValue(BBPrinterRegistryPath, "PromptForFileName", "1");
            Registry.SetValue(BBPrinterRegistryPath, "ProjectsFolder", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
            Registry.SetValue(BBPrinterRegistryPath, "SaveAsFolder", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
            Registry.SetValue(BBPrinterRegistryPath, "UseLastFolder", "0");
        }
    }
}
