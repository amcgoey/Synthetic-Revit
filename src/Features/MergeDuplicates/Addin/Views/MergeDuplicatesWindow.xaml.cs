using System;
using System.Windows;

using Synthetic.Platform.UI;

namespace Synthetic.Features.MergeDuplicates.Views
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
