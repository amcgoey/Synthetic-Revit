using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

using Synthetic.Shared.UI;
using Synthetic.Core;

using Synthetic.Modules.MergeDuplicates.ViewModels;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;

namespace Synthetic.Modules.MergeDuplicates.Views
{
    /// <summary>
    /// Modeless window that displays side-by-side parameter differences for a selected cluster.
    /// </summary>
    public partial class MergeDetailedReviewWindow : Window
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="MergeDetailedReviewWindow"/> class.
        /// </summary>
        /// <param name="mainWindowHandle">The parent window handle.</param>
        public MergeDetailedReviewWindow(IntPtr mainWindowHandle)
        {
            InitializeComponent();
            RevitWindowHelper.SetOwner(this, mainWindowHandle);

            DataContextChanged += (sender, args) =>
            {
                if (DataContext is MergeDetailedReviewViewModel vm)
                {
                    vm.CloseAction = () =>
                    {
                        try
                        {
                            this.DialogResult = true;
                        }
                        catch (InvalidOperationException)
                        {
                            // Window was not shown as a dialog (modeless)
                        }
                        this.Close();
                    };
                }
            };
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                this.DialogResult = false;
            }
            catch (InvalidOperationException)
            {
                // Window was not shown as a dialog (modeless)
            }
            this.Close();
        }

        private void RenameTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            if (string.IsNullOrEmpty(e.Text)) return;

            char[] illegalChars = new char[] { '\\', ':', '{', '}', '[', ']', '|', ';', '<', '>', '?', '\'', '~' };
            if (e.Text.IndexOfAny(illegalChars) >= 0)
            {
                e.Handled = true;

                if (sender is System.Windows.Controls.TextBox textBox)
                {
                    var originalToolTip = textBox.ToolTip;
                    var toolTip = new System.Windows.Controls.ToolTip
                    {
                        Content = "Family names cannot contain any of the following characters:\n\\ : { } [ ] | ; < > ? ' ~",
                        IsOpen = true,
                        PlacementTarget = textBox,
                        Placement = System.Windows.Controls.Primitives.PlacementMode.Top,
                        StaysOpen = false
                    };

                    textBox.ToolTip = toolTip;

                    var timer = new DispatcherTimer
                    {
                        Interval = TimeSpan.FromSeconds(3)
                    };
                    timer.Tick += (s, args) =>
                    {
                        toolTip.IsOpen = false;
                        textBox.ToolTip = originalToolTip;
                        timer.Stop();
                    };
                    timer.Start();
                }
            }
        }
    }
}
