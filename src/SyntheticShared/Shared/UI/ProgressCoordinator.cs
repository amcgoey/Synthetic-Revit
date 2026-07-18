using System;
using System.Threading;
using System.Windows.Threading;

using Synthetic.Shared.UI;

using Synthetic.Core;
using Synthetic.Modules.RevitDOM;
using Synthetic.Modules.StandardsManagement.ViewModels;
namespace Synthetic.Shared.UI
{
    /// <summary>
    /// Service coordinator to manage progress dialog lifecycle and state.
    /// Exposes static methods for initializing, updating, checking cancellation, and closing.
    /// </summary>
    public static class ProgressCoordinator
    {
        public static bool SuppressUI { get; set; } = false;
        public static bool ForceCancel { get; set; } = false;

        private static SharedProgressViewModel? _viewModel;
        private static SharedProgressWindow? _window;
        private static CancellationTokenSource? _cts;

        /// <summary>
        /// Gets the cancellation token.
        /// </summary>
        public static CancellationToken Token
        {
            get
            {
                if (ForceCancel)
                {
                    var cts = new CancellationTokenSource();
                    cts.Cancel();
                    return cts.Token;
                }
                return _cts?.Token ?? CancellationToken.None;
            }
        }

        /// <summary>
        /// Initializes the progress indicator: instantiates the ViewModel, opens the Window modelessly,
        /// and resets the cancellation token source.
        /// </summary>
        /// <param name="title">Title of the progress dialog.</param>
        /// <param name="taskDescription">Main task description message.</param>
        /// <param name="totalItems">Total count of elements to process.</param>
        public static void Initialize(string title, string taskDescription, int totalItems)
        {
            // Safeguard: close any existing tracking window/resources first.
            Close();

            _cts = new CancellationTokenSource();
            if (SuppressUI) return;

            _viewModel = new SharedProgressViewModel(_cts)
            {
                WindowTitle = title,
                MainTaskDescription = taskDescription,
                MaximumValue = totalItems,
                CurrentValue = 0,
                CurrentItemName = "Starting..."
            };

            // Retrieve active Revit window handle using AppControlled if available; otherwise use active Process main window.
            IntPtr ownerHandle = IntPtr.Zero;
            if (App.AppControlled != null)
            {
                ownerHandle = App.AppControlled.MainWindowHandle;
            }
            else
            {
                ownerHandle = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
            }

            _window = new SharedProgressWindow(ownerHandle, _viewModel);
            _window.Show();

            AllowUIToUpdate();
        }

        /// <summary>
        /// Creates an IProgress reporter targeting this coordinator.
        /// </summary>
        /// <returns>An progress reporter instance.</returns>
        public static IProgress<ProgressState> AsProgressReporter()
        {
            return new Progress<ProgressState>(state =>
            {
                if (state.IsCompleted)
                {
                    Close();
                }
                else if (_viewModel != null)
                {
                    _viewModel.MainTaskDescription = state.TaskDescription;
                    _viewModel.MaximumValue = state.MaximumBounds;
                    _viewModel.CurrentValue = state.ProgressIndex;
                    _viewModel.CurrentItemName = state.CurrentItemName;
                    AllowUIToUpdate();
                }
            });
        }

        /// <summary>
        /// Increments the current progress value by 1 and updates the current item status text.
        /// </summary>
        /// <param name="currentItemName">The name of the item currently being processed.</param>
        public static void UpdateProgress(string currentItemName)
        {
            if (_viewModel != null)
            {
                _viewModel.CurrentValue += 1;
                _viewModel.CurrentItemName = currentItemName;
                AllowUIToUpdate();
            }
        }

        /// <summary>
        /// Updates the status text without incrementing the progress value.
        /// Useful for intermediate step status updates within a single loop iteration.
        /// </summary>
        /// <param name="status">The status text to show.</param>
        public static void UpdateStatus(string status)
        {
            if (_viewModel != null)
            {
                _viewModel.CurrentItemName = status;
                AllowUIToUpdate();
            }
        }

        /// <summary>
        /// Checks if cancellation has been requested by the user.
        /// </summary>
        /// <returns>True if cancellation was requested, false otherwise.</returns>
        public static bool IsCancelled()
        {
            if (ForceCancel) return true;
            return _viewModel?.IsCancellationRequested ?? false;
        }

        /// <summary>
        /// Safely disposes token sources and closes the progress dialog.
        /// </summary>
        public static void Close()
        {
            if (_window != null)
            {
                try
                {
                    _window.Dispatcher.Invoke(() =>
                    {
                        try
                        {
                            _window.Close();
                        }
                        catch { }
                    });
                }
                catch { }
                _window = null;
            }

            if (_cts != null)
            {
                try
                {
                    _cts.Cancel();
                }
                catch { }
                try
                {
                    _cts.Dispose();
                }
                catch { }
                _cts = null;
            }

            _viewModel = null;
        }

        /// <summary>
        /// Force pumps the WPF Dispatcher queue to allow UI redraw events to process immediately,
        /// preventing the progress window from freezing on Revit's main thread.
        /// </summary>
        private static void AllowUIToUpdate()
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
            try
            {
                dispatcher.Invoke(DispatcherPriority.Background, new Action(delegate { }));
            }
            catch { }
        }
    }
}
