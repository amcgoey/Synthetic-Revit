using System;
using System.Windows;

using Synthetic.Shared.UI;
using Synthetic.Core;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;
using Synthetic.Modules.StandardsManagement.ViewModels;

namespace Synthetic.Modules.StandardsManagement.Views
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
