using System;
using System.Windows;

using Synthetic.Shared.UI;
using Synthetic.Core;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;

namespace Synthetic.Modules.SettingsDashboard.Views
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
