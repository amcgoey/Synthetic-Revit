using System;
using System.Windows;
using Synthetic.Platform.UI;

namespace Synthetic.Features.Worksets.Settings
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
