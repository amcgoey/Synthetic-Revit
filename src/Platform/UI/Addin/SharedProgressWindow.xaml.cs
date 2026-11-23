using System;
using System.ComponentModel;
using System.Windows;

namespace Synthetic.Platform.UI
{
    /// <summary>
    /// Interaction logic for SharedProgressWindow.xaml.
    /// Parented to Revit's main window handle to prevent it from dropping behind the Revit main frame.
    /// </summary>
    public partial class SharedProgressWindow : Window
    {
        private readonly SharedProgressViewModel? _viewModel;

        /// <summary>
        /// Gets or sets a value indicating whether the window is being closed programmatically
        /// by <see cref="ProgressCoordinator"/> rather than by the user clicking the close button.
        /// </summary>
        internal bool AllowProgrammaticClose { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="SharedProgressWindow"/> class.
        /// </summary>
        /// <param name="ownerHandle">Revit main window owner handle.</param>
        /// <param name="viewModel">ViewModel containing data bindings.</param>
        public SharedProgressWindow(IntPtr ownerHandle, SharedProgressViewModel viewModel)
        {
            InitializeComponent();
            _viewModel = viewModel;
            DataContext = viewModel;
            RevitWindowHelper.SetOwner(this, ownerHandle);
        }

        /// <summary>
        /// Triggers cancellation when the user closes the window directly via the chrome close button.
        /// </summary>
        protected override void OnClosing(CancelEventArgs e)
        {
            if (!AllowProgrammaticClose && _viewModel != null)
            {
                _viewModel.CancelCommand.Execute(null);
            }

            base.OnClosing(e);
        }
    }
}
