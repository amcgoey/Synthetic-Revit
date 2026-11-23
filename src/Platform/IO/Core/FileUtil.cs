using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Synthetic.Platform.IO
{
    /// <summary>
    /// Utility methods for file operations, alternate path searches, and file organization.
    /// </summary>
    public class FileUtil
    {
        /// <summary>
        /// Copies a list of files to a new root directory, optionally preserving directory sub-structures
        /// and resolving missing files from explicit archive directories and alternate path mappings.
        /// </summary>
        /// <param name="filePaths">The list of file paths to copy.</param>
        /// <param name="newPathRoot">The new destination root path.</param>
        /// <param name="rootNames">Optional list of root directory names to separate and preserve sub-structures.</param>
        /// <param name="overwrite">If true, existing files will be overwritten; otherwise, they will be skipped.</param>
        /// <param name="archiveDirectories">Optional archive root directories whose immediate subdirectories are searched when resolving missing <c>bim\&lt;project&gt;</c> paths.</param>
        /// <param name="alternatePaths">Optional mapping from source path prefixes to candidate replacement prefixes.</param>
        /// <returns>A dictionary containing details of files successfully copied and files not copied.</returns>
        public static Dictionary<string, Dictionary<string, object>> CopyFiles(
            List<string> filePaths,
            string newPathRoot,
            List<string>? rootNames = null,
            bool overwrite = true,
            IEnumerable<string>? archiveDirectories = null,
            IReadOnlyDictionary<string, List<string>>? alternatePaths = null)
        {
            if (filePaths == null)
            {
                throw new ArgumentNullException(nameof(filePaths));
            }

            Dictionary<string, object> filesCopied = new Dictionary<string, object>();
            Dictionary<string, object> filesNotCopied = new Dictionary<string, object>();

            List<string> uniqueFilePaths = filePaths.Distinct().ToList();

            // Resolve actual subdirectories from explicit archive roots with null checking and Directory.Exists checks
            List<string> resolvedArchiveDirs = new List<string>();
            if (archiveDirectories != null)
            {
                foreach (string root in archiveDirectories)
                {
                    if (!string.IsNullOrEmpty(root))
                    {
                        try
                        {
                            if (Directory.Exists(root))
                            {
                                resolvedArchiveDirs.AddRange(Directory.GetDirectories(root));
                            }
                        }
                        catch (Exception)
                        {
                            // Safely ignore individual access errors
                        }
                    }
                }
            }

            foreach (string possiblePath in uniqueFilePaths)
            {
                if (string.IsNullOrEmpty(possiblePath))
                {
                    continue;
                }

                string fullPath = possiblePath.ToLower();
                if (!string.IsNullOrEmpty(fullPath))
                {
                    // Check alternate paths
                    if (!File.Exists(fullPath) && !fullPath.Contains("|"))
                    {
                        Dictionary<string, List<string>> resolvedAlternatePaths = new Dictionary<string, List<string>>();
                        if (alternatePaths != null)
                        {
                            foreach (KeyValuePair<string, List<string>> kvp in alternatePaths)
                            {
                                if (kvp.Key != null && kvp.Value != null)
                                {
                                    resolvedAlternatePaths[kvp.Key] = kvp.Value.Select(v => v.ToLower()).ToList();
                                }
                            }
                        }

                        string separator = "bim";
                        int index = fullPath.IndexOf(separator, StringComparison.OrdinalIgnoreCase);
                        if (index >= 0)
                        {
                            index = index + separator.Length;
                            if (index + 1 <= fullPath.Length)
                            {
                                string projectRoot = fullPath.Substring(0, index);
                                string projectName = fullPath.Substring(index + 1);

                                index = projectName.IndexOf("\\", StringComparison.OrdinalIgnoreCase);
                                if (index >= 0)
                                {
                                    projectName = projectName.Substring(0, index);

                                    if (!string.IsNullOrEmpty(projectName) && !projectName.Contains("\\"))
                                    {
                                        if (resolvedArchiveDirs.Count > 0)
                                        {
                                            List<string> testDirectories = new List<string>();
                                            foreach (string dir in resolvedArchiveDirs)
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
                                                resolvedAlternatePaths[replaceKey.ToLower()] = testDirectories;
                                            }
                                        }
                                    }
                                }
                            }
                        }

                        foreach (KeyValuePair<string, List<string>> alternate in resolvedAlternatePaths)
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
                                if (!string.IsNullOrEmpty(ext))
                                {
                                    testPath = testPath.Replace(ext, "webp");
                                    if (File.Exists(testPath))
                                    {
                                        fullPath = testPath;
                                        break;
                                    }
                                }
                            }
                        }
                    }

                    if (File.Exists(fullPath))
                    {
                        Dictionary<string, string> file = new Dictionary<string, string>();

                        string fileName = Path.GetFileName(fullPath);
                        string? directory = Path.GetDirectoryName(fullPath);
                        string partialPath = string.Empty;

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
                            if (!string.IsNullOrEmpty(dirName) && !Directory.Exists(dirName))
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
                                Dictionary<string, string> tempFile = (Dictionary<string, string>)filesCopied[fileName];
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
                            if (fullPath.Contains("|"))
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
        /// Given a file path, returns the partial path after one of the root names. If a root isn't found, returns empty string.
        /// </summary>
        /// <param name="filePath">A string of the filepath.</param>
        /// <param name="rootNames">Names of directories to separate.</param>
        /// <returns>The partial subpath after the matched root name, or empty string.</returns>
        public static string SeparatePath(string? filePath, List<string> rootNames)
        {
            if (filePath == null) return string.Empty;
            int index = -1;
            string partialPath = string.Empty;
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
