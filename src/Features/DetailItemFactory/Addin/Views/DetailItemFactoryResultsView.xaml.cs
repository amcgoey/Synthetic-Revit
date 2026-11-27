using System;
using System.Windows;
using Synthetic.Features.DetailItemFactory.ViewModels;
using Synthetic.Platform.UI;

namespace Synthetic.Features.DetailItemFactory.Views
{
    /// <summary>
    /// Interaction logic for DetailItemFactoryResultsView.xaml
    /// </summary>
    public partial class DetailItemFactoryResultsView : Window
    {
        private DetailItemFactoryResultsViewModel? _boundViewModel;

        /// <summary>
        /// Initializes a new instance of the <see cref="DetailItemFactoryResultsView"/> class.
        /// </summary>
        /// <param name="mainWindowHandle">The parent Revit main window handle.</param>
        public DetailItemFactoryResultsView(IntPtr mainWindowHandle)
        {
            InitializeComponent();
            RevitWindowHelper.SetOwner(this, mainWindowHandle);

            DataContextChanged += OnDataContextChanged;
            Closed += OnWindowClosed;
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            DetachViewModel(_boundViewModel);
            _boundViewModel = e.NewValue as DetailItemFactoryResultsViewModel;
            if (_boundViewModel != null)
            {
                _boundViewModel.RequestClose += OnRequestClose;
            }
        }

        private void OnRequestClose()
        {
            Close();
        }

        private void OnWindowClosed(object? sender, EventArgs e)
        {
            DetachViewModel(_boundViewModel);
            _boundViewModel = null;
            DataContextChanged -= OnDataContextChanged;
            Closed -= OnWindowClosed;
        }

        private void DetachViewModel(DetailItemFactoryResultsViewModel? vm)
        {
            if (vm != null)
            {
                vm.RequestClose -= OnRequestClose;
            }
        }
    }
}
