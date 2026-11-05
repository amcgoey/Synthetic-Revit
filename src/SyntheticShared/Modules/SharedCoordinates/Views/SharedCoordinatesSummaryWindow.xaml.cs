using System;
using System.Text;
using System.Windows;
using Synthetic.Modules.SharedCoordinates.Models;
using Synthetic.Shared.UI;

namespace Synthetic.Modules.SharedCoordinates.Views
{
    /// <summary>
    /// Modal summary dialog presenting execution results for Shared Site Sync and Shared Site Link Repositioning.
    /// </summary>
    public partial class SharedCoordinatesSummaryWindow : Window
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SharedCoordinatesSummaryWindow"/> class for Shared Site Sync results.
        /// </summary>
        /// <param name="mainWindowHandle">The native window handle of the Revit application.</param>
        /// <param name="summary">The summary data of the completed Shared Site Sync execution.</param>
        public SharedCoordinatesSummaryWindow(IntPtr mainWindowHandle, SyncSharedSitesSummary summary)
        {
            InitializeComponent();
            RevitWindowHelper.SetOwner(this, mainWindowHandle);

            Title = "Shared Site Sync Summary";
            TxtSummaryTitle.Text = "Shared Site Sync Complete";

            TxtStat1Count.Text = summary?.CreatedCount.ToString() ?? "0";
            TxtStat1Label.Text = "Sites Created";

            TxtStat2Count.Text = summary?.UpdatedCount.ToString() ?? "0";
            TxtStat2Label.Text = "Sites Updated";

            BorderStat3.Visibility = Visibility.Collapsed;
            ColStat3.Width = new GridLength(0);

            if (summary != null)
            {
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

        /// <summary>
        /// Initializes a new instance of the <see cref="SharedCoordinatesSummaryWindow"/> class for Link Repositioning results.
        /// </summary>
        /// <param name="mainWindowHandle">The native window handle of the Revit application.</param>
        /// <param name="summary">The summary data of the completed Move Links to Shared Sites execution.</param>
        public SharedCoordinatesSummaryWindow(IntPtr mainWindowHandle, RepositionLinksSummary summary)
        {
            InitializeComponent();
            RevitWindowHelper.SetOwner(this, mainWindowHandle);

            Title = "Move Links to Shared Sites Summary";
            TxtSummaryTitle.Text = "Link Repositioning Complete";

            TxtStat1Count.Text = summary?.RepositionedCount.ToString() ?? "0";
            TxtStat1Label.Text = "Repositioned";

            TxtStat2Count.Text = summary?.AlreadyAlignedCount.ToString() ?? "0";
            TxtStat2Label.Text = "Already Aligned";

            TxtStat3Count.Text = summary?.SkippedCount.ToString() ?? "0";
            TxtStat3Label.Text = "Not Shared (Skipped)";

            BorderStat3.Visibility = Visibility.Visible;
            ColStat3.Width = new GridLength(1, GridUnitType.Star);

            if (summary != null)
            {
                var sb = new StringBuilder();
                if (summary.RepositionedInstanceNames.Count > 0)
                {
                    sb.AppendLine("Repositioned Link Instances:");
                    foreach (var name in summary.RepositionedInstanceNames)
                    {
                        sb.AppendLine($"  + {name}");
                    }
                    sb.AppendLine();
                }

                if (summary.SkippedInstanceNames.Count > 0)
                {
                    sb.AppendLine("Skipped Instances (Not Shared or Unresolved):");
                    foreach (var name in summary.SkippedInstanceNames)
                    {
                        sb.AppendLine($"  - {name}");
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

                if (summary.RepositionedCount == 0 && summary.AlreadyAlignedCount == 0 && summary.SkippedCount == 0 && summary.ErrorMessages.Count == 0)
                {
                    sb.AppendLine("No link instances were evaluated or repositioned.");
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
