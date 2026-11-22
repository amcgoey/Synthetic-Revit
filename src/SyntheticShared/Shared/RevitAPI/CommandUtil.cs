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
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;
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
