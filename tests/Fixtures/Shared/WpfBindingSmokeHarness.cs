using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using NUnit.Framework;

namespace SyntheticTests.Fixtures
{
    /// <summary>
    /// Captures WPF <see cref="PresentationTraceSources.DataBindingSource"/> errors while exercising
    /// BAML-compiled <see cref="Window"/> instances under an STA thread.
    /// </summary>
    public sealed class WpfBindingSmokeHarness : IDisposable
    {
        private sealed class CapturingTraceListener : TraceListener
        {
            public List<string> Messages { get; } = new List<string>();

            public override void Write(string? message)
            {
                if (!string.IsNullOrEmpty(message))
                {
                    Messages.Add(message!);
                }
            }

            public override void WriteLine(string? message)
            {
                if (!string.IsNullOrEmpty(message))
                {
                    Messages.Add(message!);
                }
            }
        }

        private readonly CapturingTraceListener _listener = new CapturingTraceListener();
        private readonly TraceSource _dataBindingSource;
        private readonly SourceLevels _previousLevel;
        private bool _disposed;

        /// <summary>
        /// Initializes the trace listener and refreshes WPF presentation trace sources.
        /// </summary>
        public WpfBindingSmokeHarness()
        {
            _dataBindingSource = PresentationTraceSources.DataBindingSource;
            _previousLevel = _dataBindingSource.Switch.Level;
            _dataBindingSource.Switch.Level = SourceLevels.Error;
            _dataBindingSource.Listeners.Add(_listener);
            PresentationTraceSources.Refresh();
        }

        /// <summary>
        /// Gets all captured WPF data-binding error messages.
        /// </summary>
        public IReadOnlyList<string> Errors => _listener.Messages;

        /// <summary>
        /// Shows <paramref name="window"/>, updates its layout, pumps <see cref="DispatcherPriority.Loaded"/>,
        /// asserts <see cref="Window.IsVisible"/>, and closes the window cleanly in <c>finally</c>.
        /// </summary>
        public static void ExerciseWindow(Window window, bool clearDataContextOnClose = false)
        {
            if (window == null) throw new ArgumentNullException(nameof(window));

            try
            {
                window.Show();
                window.UpdateLayout();
                Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.Loaded, new Action(() => { }));
                Assert.IsTrue(window.IsVisible, $"{window.GetType().Name} should show cleanly without resource exceptions.");
            }
            finally
            {
                if (clearDataContextOnClose)
                {
                    window.DataContext = null;
                }
                window.Close();
            }
        }

        /// <summary>
        /// Asserts that zero WPF <c>BindingExpression</c> errors were captured.
        /// </summary>
        public void AssertNoBindingErrors(string scopeDescription)
        {
            Assert.IsEmpty(
                _listener.Messages,
                $"Expected zero WPF DataBinding errors across {scopeDescription}:" + Environment.NewLine
                + string.Join(Environment.NewLine, _listener.Messages));
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _dataBindingSource.Listeners.Remove(_listener);
            _dataBindingSource.Switch.Level = _previousLevel;
            _disposed = true;
        }
    }
}
