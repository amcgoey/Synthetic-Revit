using System;
using System.IO;
using System.Runtime.CompilerServices;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using eTransmitForRevitDB;
using Application = Autodesk.Revit.ApplicationServices.Application;

namespace Synthetic.Platform.Revit
{
    /// <summary>
    /// Utility methods for interacting with Revit <see cref="Document"/> objects.
    /// </summary>
    public class DocumentUtil
    {
        /// <summary>
        /// Opens an RVT Revit file as detached.
        /// </summary>
        /// <param name="path">Autodesk.Revit.DB.ModelPath object pointing to the Revit file to open.</param>
        /// <param name="uiApp">The Autodesk.Revit.UI.UIApplication object to open the file in.</param>
        /// <returns>The Autodesk.Revit.DB.Document object of the opened document.</returns>
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
        /// <param name="document">An Autodesk.Revit.DB.Document object.</param>
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
        /// <param name="document">An Autodesk.Revit.DB.Document object.</param>
        /// <returns>A string of the folder path to the document.</returns>
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

        /// <summary>
        /// Using eTransmit, purges the model.
        /// </summary>
        /// <param name="app">The Revit Application.</param>
        /// <param name="doc">Revit Document object.</param>
        /// <returns>True if purge was successful.</returns>
        public static bool Purge(Application app, Document doc)
        {
            ETransmitAssemblyResolver.EnsureRegistered();
            return PurgeCore(app, doc);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool PurgeCore(Application app, Document doc)
        {
            eTransmitUpgradeOMatic eTransmitUpgradeOMatic = new eTransmitUpgradeOMatic(app);
            UpgradeFailureType result = eTransmitUpgradeOMatic.purgeUnused(doc);
            return result == UpgradeFailureType.UpgradeSucceeded;
        }
    }
}
