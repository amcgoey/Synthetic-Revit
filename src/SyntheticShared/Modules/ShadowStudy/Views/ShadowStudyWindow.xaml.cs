using System;
using System.Windows;
using Synthetic.Modules.ShadowStudy.ViewModels;
using Synthetic.Shared.UI;

namespace Synthetic.Modules.ShadowStudy.Views
{
    /// <summary>
    /// Interaction logic for ShadowStudyWindow.xaml.
    /// </summary>
    public partial class ShadowStudyWindow : Window
    {
        private ShadowStudyViewModel? _boundViewModel;

        /// <summary>
        /// Initializes a new instance of the <see cref="ShadowStudyWindow"/> class.
        /// </summary>
        public ShadowStudyWindow(IntPtr mainWindowHandle)
        {
            InitializeComponent();
            RevitWindowHelper.SetOwner(this, mainWindowHandle);
            Loaded += OnLoaded;
            Closed += OnWindowClosed;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (DataContext is ShadowStudyViewModel vm && !ReferenceEquals(_boundViewModel, vm))
            {
                _boundViewModel = vm;
                _boundViewModel.RequestClose += OnRequestClose;
            }
        }

        private void OnRequestClose(bool dialogResult)
        {
            DialogResult = dialogResult;
            Close();
        }

        private void OnWindowClosed(object? sender, EventArgs e)
        {
            if (_boundViewModel != null)
            {
                _boundViewModel.RequestClose -= OnRequestClose;
                _boundViewModel = null;
            }

            Loaded -= OnLoaded;
            Closed -= OnWindowClosed;
        }
    }
}
