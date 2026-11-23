using System;
using System.Windows;
using Synthetic.Features.SharedCoordinates.ViewModels;
using Synthetic.Platform.UI;

namespace Synthetic.Features.SharedCoordinates.Views
{
    /// <summary>
    /// Interaction logic for SyncSharedSitesWindow.xaml.
    /// </summary>
    public partial class SyncSharedSitesWindow : Window
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SyncSharedSitesWindow"/> class.
        /// </summary>
        /// <param name="mainWindowHandle">The native window handle of the Revit application.</param>
        public SyncSharedSitesWindow(IntPtr mainWindowHandle)
        {
            InitializeComponent();
            RevitWindowHelper.SetOwner(this, mainWindowHandle);
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (DataContext is SyncSharedSitesViewModel vm)
            {
                vm.RequestClose += OnRequestClose;
            }
        }

        private void OnRequestClose(bool dialogResult)
        {
            DialogResult = dialogResult;
            Close();
        }
    }
}
