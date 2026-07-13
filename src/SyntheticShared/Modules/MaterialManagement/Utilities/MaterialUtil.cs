using Synthetic.Modules.MaterialManagement.Commands;
using Synthetic.Modules.MaterialManagement.Utilities;

using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text;
using System.Threading.Tasks;

using revitDB = Autodesk.Revit.DB;
using revitDoc = Autodesk.Revit.DB.Document;
using revitMaterial = Autodesk.Revit.DB.Material;
using Autodesk.Revit.DB.Visual;
using Autodesk.Revit.DB;
using System.Globalization;
using Synthetic.Modules.RevitDOM;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.Infrastructure.IO;

namespace Synthetic.Modules.MaterialManagement.Utilities
{
    /// <summary>
    /// Extensions of Dynamo Revit
    /// </summary>
    public class MaterialUtil
    {
        /// <summary>
        /// The default path to the Autodesk Shared Materials Textures directory.
        /// </summary>
        public const string AutodeskMaterialLibrary = "C:\\Program Files (x86)\\Common Files\\Autodesk Shared\\Materials\\Textures";

        internal MaterialUtil() { }

        /// <summary>
        /// Gets a material given its name and document
        /// </summary>
        /// <param name="Name">Name of a material</param>
        /// <param name="Document">Document to get the material from</param>
        /// <returns name="Material">A Autodeks.Revit.DB.Material</returns>
        public static revitMaterial? GetByNameDocument(string Name, revitDoc Document)
        {
            revitDB.FilteredElementCollector collector
                = new revitDB.FilteredElementCollector(Document);

            collector
                .OfClass(typeof(revitDB.Material))
                .OfType<revitDB.Material>();

            return collector
                .OfType<revitDB.Material>()
                .FirstOrDefault(
                m => m.Name.Equals(Name));
        }

        /// <summary>
        /// Retrieves all material elements in the specified document.
        /// </summary>
        /// <param name="Document">The Revit document.</param>
        /// <returns>A FilteredElementCollector containing the materials.</returns>
        public static FilteredElementCollector GetAllMaterials(revitDoc Document)
        {
            revitDB.FilteredElementCollector collector
                = new revitDB.FilteredElementCollector(Document);

            collector
                .OfClass(typeof(revitDB.Material))
                .OfType<revitDB.Material>();

            return collector;
        }

        /// <summary>
        /// Gets all the connected files with paths associated with a material.
        /// </summary>
        /// <param name="Material">A revit material</param>
        /// <returns name="Paths">Full file names with paths</returns>
        public static List<string>? GetMaterialBitmapPaths(revitMaterial Material)
        {
            List<string>? paths = null;

            revitDB.ElementId appearanceAssetID = Material.AppearanceAssetId;

#if REVIT2022 || REVIT2023
            if (appearanceAssetID.IntegerValue != -1)
#else
            if (appearanceAssetID.Value != -1)
#endif
                {
                    paths = new List<string>();
                revitDB.AppearanceAssetElement? assetElem = Material.Document.GetElement(appearanceAssetID) as revitDB.AppearanceAssetElement;

                if (assetElem != null)
                {
                    Asset renderingAsset = assetElem.GetRenderingAsset();

                    for (int idx = 0; idx < renderingAsset.Size; idx++)
                    {
                        AssetProperty property = renderingAsset.Get(idx);
                        List<string> tempPath = _ReadAssetPropertyPaths(property);
                        if (tempPath != null && tempPath.Count > 0)
                        {
                            paths.AddRange(tempPath);
                        }
                    }
                }
            }
            return paths;
        }

        /// <summary>
        /// Given a material list and a SearchPaths object, the method will replace the path of any bitmaps in the material based on the SearchPaths.
        /// </summary>
        /// <param name="Materials">A list of Autodesk.Revit.DB.Material elements</param>
        /// <param name="searchPaths">A SearchPaths object that includes a prioritzed list of search paths</param>
        /// <param name="ReplaceRelativePaths">If True, replace files that are Relative Paths, otherwise don't replace.</param>
        /// <param name="RunTest">If False, replace the paths. If True, the method runs a test replacement instead but doesn't actually edit the material.</param>
        /// <returns name="Paths Replaced">A list of the paths replaced</returns>
        /// <returns name="Paths NOT Replaced">A list of the paths that were left unchanged.</returns>
        public static Dictionary<string, object> ReplaceBitmapPaths(List<revitMaterial> Materials,
            SearchPaths searchPaths,
            bool ReplaceRelativePaths = false,
            bool RunTest = false
            )
        {
            List<List<string>>? results = null;

            if (searchPaths != null && Materials != null && Materials.Count > 0)
            {
                foreach (revitMaterial Material in Materials)
                {
                    revitDB.ElementId appearanceAssetID = Material.AppearanceAssetId;


#if REVIT2022 || REVIT2023
                    if (appearanceAssetID.IntegerValue != -1)
#else
                    if (appearanceAssetID.Value != -1)
#endif

                    {
                        revitDB.AppearanceAssetElement? assetElem 
                            = Material.Document.GetElement(appearanceAssetID) as revitDB.AppearanceAssetElement;

                        if (assetElem != null)
                        {
                            revitDoc document = Material.Document;

                            if (!RunTest)
                            {
                                results = _ReplaceBitmapPaths(assetElem, searchPaths, ReplaceRelativePaths, RunTest);
                            }
                        }
                    }
                }
            }
            if (results != null)
            {
                return new Dictionary<string, object>
            {
                {"Paths Replaced", results[0]},
                {"Paths NOT Replaced", results[1] }
            };
            }
            else
            {
                return new Dictionary<string, object>
            {
                {"Paths Replaced", new List<string>()},
                {"Paths NOT Replaced", new List<string>()}
            };
            }
        }

        private static List<List<string>> _ReplaceBitmapPaths(
            revitDB.AppearanceAssetElement assetElem,
            SearchPaths searchPaths,
            bool ReplaceRelativePaths,
            bool RunTest
            )
        {
            List<string> pathsReplaced = new List<string>();
            List<string> pathsNotReplaced = new List<string>();
            string? file = null;
            string? filePath = null;
            string? newFilePath = null;

            using (AppearanceAssetEditScope editScope = new AppearanceAssetEditScope(assetElem.Document))
            {
                Asset? renderAsset = null;

                if (!RunTest)
                {
                    // returns an editable copy of the appearance asset
                    renderAsset = editScope.Start(assetElem.Id);
                }
                else
                {
                    renderAsset = assetElem.GetRenderingAsset();
                }

                if (renderAsset != null)
                {
                    for (int idx = 0; idx < renderAsset.Size; idx++)
                    {
                        AssetProperty? property = renderAsset.Get(idx);
                        Asset? connectedAsset = property?.GetSingleConnectedAsset();

                        if (connectedAsset != null)
                        {
                            AssetPropertyString? bitmapProperty = connectedAsset.FindByName(UnifiedBitmap.UnifiedbitmapBitmap) as AssetPropertyString;

                            if (bitmapProperty == null)
                            {
                                bitmapProperty = connectedAsset.FindByName(BumpMap.BumpmapBitmap) as AssetPropertyString;
                            }
                            if (bitmapProperty != null)
                            {
                                char[] separator = { '|' };

                                filePath = bitmapProperty.Value;

                                if (filePath != null && filePath != String.Empty)
                                {
                                    // If the Path is from the Revit Library it will contain a '|' character.
                                    // If ReplaceRelativePaths is True, then generate a new path
                                    // Otherwise, if the path is absolute, then generate a new path
                                    if (filePath.Contains(separator[0]) && ReplaceRelativePaths)
                                    {
                                        filePath = filePath.Split(separator).Last();
                                        file = Path.GetFileName(filePath);
                                        newFilePath = searchPaths.GetFilePath(file);
                                    }
                                    else if ((MaterialUtil.IsPathFullyQualified(filePath)
                                        && Uri.IsWellFormedUriString(filePath, UriKind.RelativeOrAbsolute))
                                        || ReplaceRelativePaths)
                                    {
                                        file = Path.GetFileName(filePath);
                                        newFilePath = searchPaths.GetFilePath(file);
                                    }

                                    // If a new path is found and it is a valid property value, then edit the path.
                                    // Else, record that the path was not changed.
                                    if (newFilePath != null && bitmapProperty.Value != newFilePath)
                                    {
                                        // Only make the change is Execute is True
                                        if (!RunTest && bitmapProperty.IsValidValue(newFilePath))
                                        { bitmapProperty.Value = newFilePath; }

                                        pathsReplaced.Add(newFilePath);
                                    }
                                    else
                                    {
                                        pathsNotReplaced.Add(filePath);
                                    }
                                }
                            }
                        }
                    }
                }
                if (!RunTest)
                {
                    editScope.Commit(true);
                }
            }
            return new List<List<string>> { { pathsReplaced }, { pathsNotReplaced } };
        }

        /// <summary>
        /// Checks if a AssetProperty has connected properties and if those connected properties have a bitmap path, it returns the path.
        /// </summary>
        /// <param name="assetProperty">A Revit AssetProperty element</param>
        /// <returns name="paths">Bitmap paths</returns>
        private static List<string> _ReadAssetPropertyPaths(AssetProperty assetProperty)
        {
            List<string> paths = new List<string>();

            if (assetProperty.NumberOfConnectedProperties == 1)
            {
                Asset connectedAsset = assetProperty.GetSingleConnectedAsset();
                if (connectedAsset != null)
                {
                    AssetPropertyString? bitmapProperty = connectedAsset.FindByName(UnifiedBitmap.UnifiedbitmapBitmap) as AssetPropertyString;

                    if (bitmapProperty == null)
                    {
                        bitmapProperty = connectedAsset.FindByName(BumpMap.BumpmapBitmap) as AssetPropertyString;
                    }
                    if (bitmapProperty != null && bitmapProperty.Value != "")
                    {
                        string? path = bitmapProperty.Value;
                        if (path != null && (path.StartsWith("1\\", true, CultureInfo.CurrentCulture) ||
                            path.StartsWith("2\\", true, CultureInfo.CurrentCulture) ||
                            path.StartsWith("3\\", true, CultureInfo.CurrentCulture)))
                        {
                            path = AutodeskMaterialLibrary + "\\" + path;
                        }
                        if (path != null)
                        {
                            paths.Add(path);
                        }
                        path = null;
                    }
                }
            }
            else
            {
                int num = assetProperty.NumberOfConnectedProperties;
            }

            return paths;
        }

        private static bool IsPathFullyQualified(string path)
        {
            if (path == null) throw new ArgumentNullException(nameof(path));
            if (path.Length < 2) return false; //There is no way to specify a fixed path with one character (or less).
            if (path.Length == 2 && IsValidDriveChar(path[0]) && path[1] == System.IO.Path.VolumeSeparatorChar) return true; //Drive Root C:
            if (path.Length >= 3 && IsValidDriveChar(path[0]) && path[1] == System.IO.Path.VolumeSeparatorChar && IsDirectorySeperator(path[2])) return true; //Check for standard paths. C:\
            if (path.Length >= 3 && IsDirectorySeperator(path[0]) && IsDirectorySeperator(path[1])) return true; //This is start of a UNC path
            return false; //Default
        }

        private static bool IsDirectorySeperator(char c) => c == System.IO.Path.DirectorySeparatorChar | c == System.IO.Path.AltDirectorySeparatorChar;
        private static bool IsValidDriveChar(char c) => c >= 'A' && c <= 'Z' || c >= 'a' && c <= 'z';
    }
}
