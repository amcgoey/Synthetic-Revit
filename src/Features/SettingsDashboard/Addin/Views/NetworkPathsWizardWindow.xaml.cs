using System;
using System.Windows;
using Synthetic.Platform.UI;

namespace Synthetic.Features.SettingsDashboard.Views
{
    /// <summary>
    /// Interaction logic for NetworkPathsWizardWindow.xaml.
    /// </summary>
    public partial class NetworkPathsWizardWindow : Window
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="NetworkPathsWizardWindow"/> class.
        /// </summary>
        /// <param name="mainWindowHandle">The parent Revit main window handle.</param>
        public NetworkPathsWizardWindow(IntPtr mainWindowHandle)
        {
            InitializeComponent();
            RevitWindowHelper.SetOwner(this, mainWindowHandle);
        }
    }
}
