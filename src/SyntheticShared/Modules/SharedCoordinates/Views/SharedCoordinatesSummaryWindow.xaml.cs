using System;
using System.Text;
using System.Windows;
using Synthetic.Modules.SharedCoordinates.Models;
using Synthetic.Shared.UI;

namespace Synthetic.Modules.SharedCoordinates.Views
{
    /// <summary>
    /// Modal summary dialog presenting the counts and names of created and updated Level-Based Shared Sites.
    /// </summary>
    public partial class SharedCoordinatesSummaryWindow : Window
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SharedCoordinatesSummaryWindow"/> class.
        /// </summary>
        /// <param name="mainWindowHandle">The native window handle of the Revit application.</param>
        /// <param name="summary">The summary data of the completed Shared Site Sync execution.</param>
        public SharedCoordinatesSummaryWindow(IntPtr mainWindowHandle, SyncSharedSitesSummary summary)
        {
            InitializeComponent();
            RevitWindowHelper.SetOwner(this, mainWindowHandle);

            if (summary != null)
            {
                TxtCreatedCount.Text = summary.CreatedCount.ToString();
                TxtUpdatedCount.Text = summary.UpdatedCount.ToString();

                var sb = new StringBuilder();
                if (summary.CreatedSiteNames.Count > 0)
                {
                    sb.AppendLine("Created Shared Sites:");
                    foreach (var name in summary.CreatedSiteNames)
                    {
                        sb.AppendLine($"  + {name}");
                    }
                    sb.AppendLine();
                }

                if (summary.UpdatedSiteNames.Count > 0)
                {
                    sb.AppendLine("Updated Shared Sites:");
                    foreach (var name in summary.UpdatedSiteNames)
                    {
                        sb.AppendLine($"  * {name}");
                    }
                    sb.AppendLine();
                }

                if (summary.ErrorMessages.Count > 0)
                {
                    sb.AppendLine("Warnings / Errors:");
                    foreach (var err in summary.ErrorMessages)
                    {
                        sb.AppendLine($"  ! {err}");
                    }
                }

                if (summary.CreatedCount == 0 && summary.UpdatedCount == 0 && summary.ErrorMessages.Count == 0)
                {
                    sb.AppendLine("No Shared Sites were created or updated.");
                }

                TxtSummaryDetails.Text = sb.ToString().TrimEnd();
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
