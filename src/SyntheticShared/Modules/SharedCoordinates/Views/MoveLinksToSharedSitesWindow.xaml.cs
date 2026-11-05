using System;
using System.Windows;
using Synthetic.Modules.SharedCoordinates.ViewModels;
using Synthetic.Shared.UI;

namespace Synthetic.Modules.SharedCoordinates.Views
{
    /// <summary>
    /// Interaction logic for MoveLinksToSharedSitesWindow.xaml.
    /// Checklist dialog for selecting linked model types to reposition instances in full 3D.
    /// </summary>
    public partial class MoveLinksToSharedSitesWindow : Window
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="MoveLinksToSharedSitesWindow"/> class.
        /// </summary>
        /// <param name="mainWindowHandle">The native window handle of the Revit application.</param>
        public MoveLinksToSharedSitesWindow(IntPtr mainWindowHandle)
        {
            InitializeComponent();
            RevitWindowHelper.SetOwner(this, mainWindowHandle);

            DataContextChanged += OnDataContextChanged;
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.OldValue is MoveLinksToSharedSitesViewModel oldVm)
            {
                oldVm.RequestClose -= OnRequestClose;
            }

            if (e.NewValue is MoveLinksToSharedSitesViewModel newVm)
            {
                newVm.RequestClose += OnRequestClose;
            }
        }

        private void OnRequestClose(bool dialogResult)
        {
            DialogResult = dialogResult;
            Close();
        }
    }
}
