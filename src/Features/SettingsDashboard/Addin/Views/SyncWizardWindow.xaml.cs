using System;
using System.Windows;
using Synthetic.Platform.UI;

namespace Synthetic.Features.SettingsDashboard.Views
{
    /// <summary>
    /// Interaction logic for SyncWizardWindow.xaml.
    /// </summary>
    public partial class SyncWizardWindow : Window
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SyncWizardWindow"/> class.
        /// </summary>
        /// <param name="mainWindowHandle">The parent Revit main window handle.</param>
        public SyncWizardWindow(IntPtr mainWindowHandle)
        {
            InitializeComponent();
            RevitWindowHelper.SetOwner(this, mainWindowHandle);
        }
    }
}
