using System;
using System.Windows;
using Synthetic.Shared.UI;

namespace Synthetic.Modules.StandardsManagement.Views
{
    /// <summary>
    /// Interaction logic for ProjectStandardsDashboardWindow.xaml
    /// </summary>
    public partial class ProjectStandardsDashboardWindow : Window
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ProjectStandardsDashboardWindow"/> class.
        /// </summary>
        /// <param name="mainWindowHandle">The handle to the main Revit application window, used to set the owner of this window.</param>
        public ProjectStandardsDashboardWindow(IntPtr mainWindowHandle)
        {
            InitializeComponent();
            RevitWindowHelper.SetOwner(this, mainWindowHandle);
        }
    }
}
