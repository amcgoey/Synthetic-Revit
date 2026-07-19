using System;
using System.Windows;

using Synthetic.Shared.UI;
using Synthetic.Core;

using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;

namespace Synthetic.Modules.StandardsManagement.Views
{
    /// <summary>
    /// Interaction logic for StandardsReviewWindow.xaml.
    /// Displays comparison analysis between standard definitions and live document elements.
    /// </summary>
    public partial class StandardsReviewWindow : Window
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="StandardsReviewWindow"/> class.
        /// </summary>
        /// <param name="mainWindowHandle">The parent Revit main window handle.</param>
        public StandardsReviewWindow(IntPtr mainWindowHandle)
        {
            InitializeComponent();
            RevitWindowHelper.SetOwner(this, mainWindowHandle);

            DataContextChanged += (sender, args) =>
            {
                if (DataContext is StandardsReviewViewModel vm)
                {
                    vm.CloseAction = () =>
                    {
                        try
                        {
                            this.DialogResult = true;
                        }
                        catch (InvalidOperationException)
                        {
                            // Modeless close fallback
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
                // Modeless close fallback
            }
            this.Close();
        }
    }
}
