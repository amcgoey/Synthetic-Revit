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
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;
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
