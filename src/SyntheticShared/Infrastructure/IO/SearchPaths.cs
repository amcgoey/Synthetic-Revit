using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Synthetic.Modules.MergeDuplicates.Handlers;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;
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
