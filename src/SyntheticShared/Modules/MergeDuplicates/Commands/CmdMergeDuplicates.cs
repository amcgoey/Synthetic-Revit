using System;
using System.Threading;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;
using Synthetic.Infrastructure.Serialization;

using Synthetic.Shared.UI;

using Synthetic.Shared.RevitAPI;
using Synthetic.Infrastructure.IO;
using Synthetic.Modules.MergeDuplicates.Engine;
using Synthetic.Modules.MergeDuplicates.ViewModels;
using Synthetic.Modules.MergeDuplicates.Views;
using Synthetic.Modules.StandardsManagement.ViewModels;

namespace Synthetic.Modules.MergeDuplicates.Commands
{
    /// <summary>
    /// Revit command to scan model for duplicate families and display the Merge Duplicates Modeless UI.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class CmdMergeDuplicates : IExternalCommand
    {
        /// <summary>
        /// Executes the merge duplicates command.
        /// </summary>
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                UIApplication uiapp = commandData.Application;
                Document? doc = uiapp.ActiveUIDocument?.Document;
                if (doc == null)
                {
                    message = "No active document found.";
                    return Result.Failed;
                }

                // 1. Run Fast Scan
                var clusters = MergeAnalysisEngine.RunFastScan(doc, CancellationToken.None);

                if (clusters.Count == 0)
                {
                    Autodesk.Revit.UI.TaskDialog.Show("Merge Duplicates", "No duplicate clusters found in the project.");
                    return Result.Succeeded;
                }

                // Run deep scan and generate recommendations for all discovered clusters
                foreach (var cluster in clusters)
                {
                    MergeAnalysisEngine.RunDeepScan(doc, cluster, CancellationToken.None);
                    MergeAnalysisEngine.GenerateRecommendations(cluster);
                }

                // 2. Initialize ViewModel
                var vm = new MergeDuplicatesViewModel
                {
                    MainWindowHandle = uiapp.MainWindowHandle,
                    Document = doc,
                    ScannedClusters = clusters
                };

                // 3. Open main window modelessly
                var window = new MergeDuplicatesWindow(uiapp.MainWindowHandle)
                {
                    DataContext = vm
                };
                window.Show();

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message + "\n" + ex.StackTrace;
                return Result.Failed;
            }
        }
    }
}
