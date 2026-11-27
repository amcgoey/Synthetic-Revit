using System;
using System.Windows;
using Synthetic.Features.DetailItemFactory.ViewModels;
using Synthetic.Platform.UI;

namespace Synthetic.Features.DetailItemFactory.Views
{
    /// <summary>
    /// Interaction logic for DetailItemFactoryView.xaml
    /// </summary>
    public partial class DetailItemFactoryView : Window
    {
        private DetailItemFactoryViewModel? _boundViewModel;

        /// <summary>
        /// Initializes a new instance of the <see cref="DetailItemFactoryView"/> class.
        /// </summary>
        /// <param name="mainWindowHandle">The parent Revit main window handle.</param>
        public DetailItemFactoryView(IntPtr mainWindowHandle)
        {
            InitializeComponent();
            RevitWindowHelper.SetOwner(this, mainWindowHandle);

            DataContextChanged += OnDataContextChanged;
            Closed += OnWindowClosed;
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            DetachViewModel(_boundViewModel);
            _boundViewModel = e.NewValue as DetailItemFactoryViewModel;
            if (_boundViewModel != null)
            {
                _boundViewModel.RequestClose += OnRequestClose;
            }
        }

        private void OnRequestClose(bool dialogResult)
        {
            try
            {
                DialogResult = dialogResult;
            }
            catch (InvalidOperationException)
            {
                // Window is closing or was shown modelessly.
            }

            Close();
        }

        private void OnWindowClosed(object? sender, EventArgs e)
        {
            DetachViewModel(_boundViewModel);
            _boundViewModel = null;
            DataContextChanged -= OnDataContextChanged;
            Closed -= OnWindowClosed;
        }

        private void DetachViewModel(DetailItemFactoryViewModel? vm)
        {
            if (vm != null)
            {
                vm.RequestClose -= OnRequestClose;
            }
        }
    }
}
