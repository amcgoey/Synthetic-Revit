using System;
using System.Windows;

using Synthetic.Platform.UI;
using Synthetic.Features.StandardsManagement.ViewModels;

namespace Synthetic.Features.StandardsManagement.Views
{
    /// <summary>
    /// Interaction logic for ImportSummaryWindow.xaml.
    /// </summary>
    public partial class ImportSummaryWindow : Window
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ImportSummaryWindow"/> class.
        /// </summary>
        /// <param name="parentMainWindowHandle">The handle to the main Revit application window, used to set the owner of this window.</param>
        /// <param name="viewModel">The view model associated with this summary window.</param>
        public ImportSummaryWindow(IntPtr parentMainWindowHandle, object viewModel)
        {
            InitializeComponent();
            RevitWindowHelper.SetOwner(this, parentMainWindowHandle);
            DataContext = viewModel;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}
