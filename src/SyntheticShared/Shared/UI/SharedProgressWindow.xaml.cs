using System;
using System.Windows;

using Synthetic.Shared.UI;
using Synthetic.Core;

using Synthetic.Modules.RevitDOM;
using Synthetic.Modules.StandardsManagement.ViewModels;

namespace Synthetic.Shared.UI
{
    /// <summary>
    /// Interaction logic for SharedProgressWindow.xaml.
    /// Parented to Revit's main window handle to prevent it from dropping behind the Revit main frame.
    /// </summary>
    public partial class SharedProgressWindow : Window
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SharedProgressWindow"/> class.
        /// </summary>
        /// <param name="ownerHandle">Revit main window owner handle.</param>
        /// <param name="viewModel">ViewModel containing data bindings.</param>
        public SharedProgressWindow(IntPtr ownerHandle, SharedProgressViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
            RevitWindowHelper.SetOwner(this, ownerHandle);
        }
    }
}
