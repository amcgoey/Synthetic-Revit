using System;
using System.Windows;
using Synthetic.Features.MultiModelPdfExport.ViewModels;
using Synthetic.Platform.UI;

namespace Synthetic.Features.MultiModelPdfExport.Views
{
    /// <summary>
    /// Interaction logic for MultiModelPdfExportWindow.xaml.
    /// </summary>
    public partial class MultiModelPdfExportWindow : Window
    {
        private MultiModelPdfExportViewModel? _boundViewModel;

        /// <summary>
        /// Initializes a new instance of the <see cref="MultiModelPdfExportWindow"/> class.
        /// </summary>
        public MultiModelPdfExportWindow(IntPtr mainWindowHandle)
        {
            InitializeComponent();
            RevitWindowHelper.SetOwner(this, mainWindowHandle);
            Loaded += OnLoaded;
            Closed += OnWindowClosed;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            double maxWorkAreaWidth = Math.Max(MinWidth, SystemParameters.WorkArea.Width * 0.90);
            if (Width > maxWorkAreaWidth)
            {
                Width = maxWorkAreaWidth;
            }

            double maxWorkAreaHeight = Math.Max(MinHeight, SystemParameters.WorkArea.Height * 0.90);
            if (Height > maxWorkAreaHeight)
            {
                Height = maxWorkAreaHeight;
            }

            if (DataContext is MultiModelPdfExportViewModel vm && !ReferenceEquals(_boundViewModel, vm))
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
