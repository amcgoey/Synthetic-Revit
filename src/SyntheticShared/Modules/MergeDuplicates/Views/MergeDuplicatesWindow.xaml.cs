using System;
using System.Windows;

using Synthetic.Shared.UI;
using Synthetic.Core;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;

namespace Synthetic.Modules.MergeDuplicates.Views
{
    /// <summary>
    /// Interaction logic for MergeDuplicatesWindow.xaml.
    /// </summary>
    public partial class MergeDuplicatesWindow : Window
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="MergeDuplicatesWindow"/> class.
        /// </summary>
        /// <param name="mainWindowHandle">The handle to the main Revit application window, used to set the owner of this window.</param>
        public MergeDuplicatesWindow(IntPtr mainWindowHandle)
        {
            InitializeComponent();
            RevitWindowHelper.SetOwner(this, mainWindowHandle);
        }
    }
}
