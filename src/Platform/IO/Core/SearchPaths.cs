using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Synthetic.Platform.IO
{
    /// <summary>
    /// SearchPaths provides utility functions that search recursively through a list of paths for files,
    /// exposing the resolved file library and file-copy helpers.
    /// </summary>
    public class SearchPaths
    {
        private List<string> paths;
        private Dictionary<string, string> fileLibrary;

        /// <summary>
        /// List of paths to be searched.
        /// </summary>
        public List<string> Paths
        {
            get { return this.paths; }
            set
            {
                this.paths = value ?? new List<string>();
                this.fileLibrary = this._GetFileLibrary(this.paths);
            }
        }

        /// <summary>
        /// Dictionary of files in the search paths (filename to full path). Only the first occurrence of a filename is included.
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
        /// Creates a new search path object with a library of all the unique files and their paths.
        /// Only the first instance of a filename is included so the order of the Paths gives priority.
        /// </summary>
        /// <param name="Paths">List of paths to be searched.</param>
        public SearchPaths(List<string> Paths)
        {
            this.paths = Paths ?? new List<string>();
            this.fileLibrary = this._GetFileLibrary(this.paths);
        }

        /// <summary>
        /// Adds additional paths and files to an existing SearchPaths object.
        /// </summary>
        /// <param name="paths">A list of paths.</param>
        /// <returns>This SearchPaths object.</returns>
        public SearchPaths AddPaths(List<string> paths)
        {
            if (paths == null)
            {
                return this;
            }

            List<string> newPaths = new List<string>();
            foreach (string path in paths)
            {
                if (!this.Paths.Contains(path))
                {
                    this.Paths.Add(path);
                    newPaths.Add(path);
                }
            }
            this.fileLibrary = _AddFileLibrary(newPaths, this.fileLibrary);
            return this;
        }

        /// <summary>
        /// Searches the paths for a file and returns its path if found, otherwise returns null.
        /// </summary>
        /// <param name="File">Name of the file to search for.</param>
        /// <returns>The file path if found; otherwise null.</returns>
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
        /// <param name="File">Name of the file to search for.</param>
        /// <returns>The relative file path if found; otherwise null.</returns>
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
        /// <returns>True if the file is in the FileLibrary; otherwise false.</returns>
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
        /// <param name="Path">A string of the path.</param>
        /// <returns>True if the path is a search path; otherwise false.</returns>
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
        /// <param name="Files">A list of file names to copy.</param>
        /// <param name="Path">The root path to copy files into.</param>
        /// <param name="Overwrite">If true, overwrites existing files; if false, only new files are copied.</param>
        /// <returns>A dictionary containing copy result flags, copied filenames, and uncopied filenames.</returns>
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
        /// Gets all the files in the search paths. If more than one file has the same name,
        /// only the first file found is included, giving priority to paths listed first.
        /// </summary>
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
                        string fileName = Path.GetFileName(filepath);
                        if (!files.ContainsKey(fileName))
                        {
                            files.Add(fileName, filepath);
                        }
                    }
                }
            }
            return files;
        }

        /// <summary>
        /// Adds files from additional search paths into an existing fileLibrary dictionary when filenames are unique.
        /// </summary>
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
        /// Returns the qualified type name of this SearchPaths instance.
        /// </summary>
        public override string ToString()
        {
            Type t = typeof(SearchPaths);
            return t.Namespace + "." + GetType().Name;
        }
    }
}
