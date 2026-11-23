using System;
using System.Windows;
using Synthetic.Platform.UI;

namespace Synthetic.Features.ViewManagement.Settings
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
