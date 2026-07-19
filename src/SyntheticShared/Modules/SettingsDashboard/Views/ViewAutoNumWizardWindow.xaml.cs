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
    /// Interaction logic for ViewAutoNumWizardWindow.xaml.
    /// </summary>
    public partial class ViewAutoNumWizardWindow : Window
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ViewAutoNumWizardWindow"/> class.
        /// </summary>
        /// <param name="mainWindowHandle">The parent Revit main window handle.</param>
        public ViewAutoNumWizardWindow(IntPtr mainWindowHandle)
        {
            InitializeComponent();
            RevitWindowHelper.SetOwner(this, mainWindowHandle);
        }
    }
}
