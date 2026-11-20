using System;
using System.Windows;

namespace Synthetic.Shared.UI
{
    /// <summary>
    /// Shared dark-themed completion dialog for single-file and multi-output batch exports.
    /// </summary>
    public partial class ExportCompletionWindow : Window
    {
        /// <summary>
        /// Gets the ViewModel driving this completion dialog.
        /// </summary>
        public ExportCompletionViewModel ViewModel { get; }

        /// <summary>
        /// Initializes a new instance of <see cref="ExportCompletionWindow"/> using a configured ViewModel.
        /// </summary>
        /// <param name="viewModel">The export completion presentation ViewModel.</param>
        /// <param name="parentHandle">Optional parent Revit window handle.</param>
        public ExportCompletionWindow(ExportCompletionViewModel viewModel, IntPtr parentHandle = default)
        {
            ViewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
            InitializeComponent();
            DataContext = ViewModel;
            SetCurrentValue(TitleProperty, ViewModel.WindowTitle);
            ViewModel.RequestClose += OnRequestClose;
            Closed += OnWindowClosed;
            RevitWindowHelper.SetOwner(this, parentHandle);
        }

        /// <summary>
        /// Initializes a new instance of <see cref="ExportCompletionWindow"/> for a single exported file.
        /// </summary>
        /// <param name="filePath">The exported file path.</param>
        /// <param name="parentHandle">Optional parent Revit window handle.</param>
        public ExportCompletionWindow(string filePath, IntPtr parentHandle = default)
            : this(ExportCompletionViewModel.ForSingleFile(filePath), parentHandle)
        {
        }

        private void OnRequestClose()
        {
            Close();
        }

        private void OnWindowClosed(object? sender, EventArgs e)
        {
            ViewModel.RequestClose -= OnRequestClose;
            Closed -= OnWindowClosed;
        }
    }
}
