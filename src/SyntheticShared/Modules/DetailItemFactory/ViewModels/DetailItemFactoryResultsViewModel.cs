using Synthetic.Modules.DetailItemFactory.Commands;
using Synthetic.Modules.DetailItemFactory.Handlers;
using Synthetic.Modules.DetailItemFactory.ViewModels;
using Synthetic.Modules.DetailItemFactory.Views;
using Synthetic.Modules.DetailItemFactory.Settings;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using Autodesk.Revit.UI;
using TaskDialog = Autodesk.Revit.UI.TaskDialog;

using Synthetic.Modules.RevitDOM;
using Synthetic.Infrastructure.Serialization;

using Synthetic.Shared.UI;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.Modules.DetailItemFactory.Models;
namespace Synthetic.Modules.DetailItemFactory.ViewModels
{
    /// <summary>
    /// ViewModel for the Detail Item Factory batch results dashboard.
    /// Provides collections for list views and garbage collection commands for temporary DWG files.
    /// </summary>
    public class DetailItemFactoryResultsViewModel : ViewModelBase
    {
        private readonly string _outputFolder;

        /// <summary>
        /// Gets the collection of elements conversion results.
        /// </summary>
        public ObservableCollection<DetailItemResultItem> Results { get; }

        /// <summary>
        /// Gets the command to delete temporary DWG files from the output folder.
        /// </summary>
        public ICommand DeleteTempDwgsCommand { get; }

        /// <summary>
        /// Gets the command to close the window.
        /// </summary>
        public ICommand CloseCommand { get; }

        /// <summary>
        /// Delegate assigned by the View to handle window closure.
        /// </summary>
        public Action? CloseAction { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="DetailItemFactoryResultsViewModel"/> class.
        /// </summary>
        /// <param name="outputFolder">The output folder path where files were saved.</param>
        /// <param name="results">The list of execution result items.</param>
        public DetailItemFactoryResultsViewModel(string outputFolder, IEnumerable<DetailItemResultItem> results)
        {
            _outputFolder = outputFolder ?? throw new ArgumentNullException(nameof(outputFolder));
            Results = new ObservableCollection<DetailItemResultItem>(results ?? new List<DetailItemResultItem>());

            DeleteTempDwgsCommand = new RelayCommand(ExecuteDeleteTempDwgs);
            CloseCommand = new RelayCommand(ExecuteClose);
        }

        private void ExecuteDeleteTempDwgs(object obj)
        {
            if (!Directory.Exists(_outputFolder))
            {
                TaskDialog.Show("Detail Item Factory", "The output folder does not exist.");
                return;
            }

            int deletedCount = 0;
            int failedCount = 0;

             try
             {
                 string[] dwgFiles = Directory.GetFiles(_outputFolder, "*.dwg");
                 string[] pcpFiles = Directory.GetFiles(_outputFolder, "*.pcp");
                 List<string> files = new List<string>();
                 files.AddRange(dwgFiles);
                 files.AddRange(pcpFiles);

                 foreach (string file in files)
                 {
                     string filename = Path.GetFileName(file);
                     // Strictly match the temporary export naming pattern (temp_export_*)
                     if (filename.StartsWith("temp_export_", StringComparison.OrdinalIgnoreCase))
                     {
                         try
                         {
                             File.Delete(file);
                             deletedCount++;
                         }
                         catch
                         {
                             failedCount++;
                         }
                     }
                 }

                 string msg = $"Garbage Collection Complete.\n\nSuccessfully deleted {deletedCount} temporary CAD file{(deletedCount == 1 ? "" : "s")} (.dwg/.pcp).";
                if (failedCount > 0)
                {
                    msg += $"\nFailed to delete {failedCount} file{(failedCount == 1 ? "" : "s")} (possibly locked or in use).";
                }
                TaskDialog.Show("Detail Item Factory - Cleanup", msg);
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Detail Item Factory - Error", $"An error occurred during DWG cleanup:\n{ex.Message}");
            }
        }

        private void ExecuteClose(object obj)
        {
            CloseAction?.Invoke();
        }
    }
}
