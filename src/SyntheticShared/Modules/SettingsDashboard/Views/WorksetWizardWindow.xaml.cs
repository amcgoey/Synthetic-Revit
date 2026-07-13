using System;
using System.Windows;

using Synthetic.Shared.UI;
using Synthetic.Core;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.Modules.RevitDOM;

namespace Synthetic.Modules.SettingsDashboard.Views
{
    /// <summary>
    /// Interaction logic for WorksetWizardWindow.xaml.
    /// </summary>
    public partial class WorksetWizardWindow : Window
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="WorksetWizardWindow"/> class.
        /// </summary>
        /// <param name="mainWindowHandle">The parent Revit main window handle.</param>
        public WorksetWizardWindow(IntPtr mainWindowHandle)
        {
            InitializeComponent();
            RevitWindowHelper.SetOwner(this, mainWindowHandle);
        }
    }
}
