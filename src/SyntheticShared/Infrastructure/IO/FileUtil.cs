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
            if (archiveRoots == null)
            {
                archiveRoots = new List<string>();
            }

            // Fallback for alternate paths
            if (configAlternatePaths == null)
            {
                configAlternatePaths = new Dictionary<string, List<string>>();
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
