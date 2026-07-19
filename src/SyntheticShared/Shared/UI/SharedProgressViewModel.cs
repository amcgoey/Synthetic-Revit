using System;
using System.Threading;
using System.Windows.Input;

using Synthetic.Shared.UI;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;
using Synthetic.Modules.StandardsManagement.ViewModels;
namespace Synthetic.Shared.UI
{
    /// <summary>
    /// ViewModel for universal progress tracking.
    /// Implements properties for task description, progress values, and cancel command.
    /// </summary>
    public class SharedProgressViewModel : ViewModelBase
    {
        private readonly CancellationTokenSource _cts;

        private string _windowTitle = "Processing...";
        /// <summary>
        /// Gets or sets the title of the progress window.
        /// </summary>
        public string WindowTitle
        {
            get => _windowTitle;
            set => SetProperty(ref _windowTitle, value);
        }

        private string _mainTaskDescription = "Starting...";
        /// <summary>
        /// Gets or sets the main task description message.
        /// </summary>
        public string MainTaskDescription
        {
            get => _mainTaskDescription;
            set => SetProperty(ref _mainTaskDescription, value);
        }

        private string _currentItemName = "";
        /// <summary>
        /// Gets or sets the name of the current item being processed.
        /// </summary>
        public string CurrentItemName
        {
            get => _currentItemName;
            set => SetProperty(ref _currentItemName, value);
        }

        private double _maximumValue = 100.0;
        /// <summary>
        /// Gets or sets the total number of items to process.
        /// </summary>
        public double MaximumValue
        {
            get => _maximumValue;
            set
            {
                if (SetProperty(ref _maximumValue, value))
                {
                    OnPropertyChanged(nameof(ProgressPercentage));
                }
            }
        }

        private double _currentValue = 0.0;
        /// <summary>
        /// Gets or sets the number of items processed so far.
        /// </summary>
        public double CurrentValue
        {
            get => _currentValue;
            set
            {
                if (SetProperty(ref _currentValue, value))
                {
                    OnPropertyChanged(nameof(ProgressPercentage));
                }
            }
        }

        /// <summary>
        /// Gets the calculated progress percentage (0 to 100).
        /// </summary>
        public double ProgressPercentage => MaximumValue > 0.0 ? (CurrentValue / MaximumValue) * 100.0 : 0.0;

        private bool _isCancellationRequested;
        /// <summary>
        /// Gets or sets a value indicating whether cancellation has been requested.
        /// </summary>
        public bool IsCancellationRequested
        {
            get => _isCancellationRequested;
            set => SetProperty(ref _isCancellationRequested, value);
        }

        private ICommand? _cancelCommand;
        /// <summary>
        /// Gets the command executed to cancel the operation.
        /// </summary>
        public ICommand CancelCommand => _cancelCommand ??= new RelayCommand(_ => Cancel());

        /// <summary>
        /// Initializes a new instance of the <see cref="SharedProgressViewModel"/> class.
        /// </summary>
        /// <param name="cts">The cancellation token source to trigger when cancelling.</param>
        public SharedProgressViewModel(CancellationTokenSource cts)
        {
            _cts = cts ?? throw new ArgumentNullException(nameof(cts));
        }

        private void Cancel()
        {
            if (!IsCancellationRequested)
            {
                IsCancellationRequested = true;
                MainTaskDescription = "Cancelling...";
                try
                {
                    _cts.Cancel();
                }
                catch (ObjectDisposedException) { }
            }
        }
    }
}
