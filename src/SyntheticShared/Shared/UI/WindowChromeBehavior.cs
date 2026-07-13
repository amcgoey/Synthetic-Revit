using System.Windows;
using System.Windows.Input;
using System.Windows.Shell;

namespace Synthetic.Shared.UI
{
    /// <summary>
    /// Provides attached properties that hook up standard WPF
    /// <see cref="SystemCommands"/> (Close, Minimize, Maximize, Restore) and a
    /// native <see cref="WindowChrome"/> to a <see cref="Window"/> whose title bar
    /// is rendered with the custom <c>SyntheticWindowStyle</c> ControlTemplate.
    ///
    /// Usage — add this to the Window:
    ///   <code>
    ///   local:WindowChromeBehavior.EnableWindowCommands="True"
    ///   </code>
    /// </summary>
    public static class WindowChromeBehavior
    {
        /// <summary>
        /// Attached property that, when set to <c>True</c> on a <see cref="Window"/>,
        /// applies a <see cref="WindowChrome"/> and registers the four
        /// <see cref="SystemCommands"/> bindings required by the custom chrome buttons.
        /// </summary>
        public static readonly DependencyProperty EnableWindowCommandsProperty =
            DependencyProperty.RegisterAttached(
                "EnableWindowCommands",
                typeof(bool),
                typeof(WindowChromeBehavior),
                new PropertyMetadata(false, OnEnableWindowCommandsChanged));

        /// <summary>Gets the <see cref="EnableWindowCommandsProperty"/> value.</summary>
        public static bool GetEnableWindowCommands(DependencyObject obj)
            => (bool)obj.GetValue(EnableWindowCommandsProperty);

        /// <summary>Sets the <see cref="EnableWindowCommandsProperty"/> value.</summary>
        public static void SetEnableWindowCommands(DependencyObject obj, bool value)
            => obj.SetValue(EnableWindowCommandsProperty, value);

        // ────────────────────────────────────────────────────────────────

        private static void OnEnableWindowCommandsChanged(
            DependencyObject d,
            DependencyPropertyChangedEventArgs e)
        {
            if (d is not Window window)
                return;

            if ((bool)e.NewValue)
                AttachCommandBindings(window);
            else
                DetachCommandBindings(window);
        }

        private static void AttachCommandBindings(Window window)
        {
            // Apply the WindowChrome so native Aero snapping, resize, and
            // taskbar integration are preserved while WindowStyle="None" is set.
            WindowChrome.SetWindowChrome(window, new WindowChrome
            {
                CaptionHeight           = 45,
                ResizeBorderThickness   = new Thickness(5),
                GlassFrameThickness     = new Thickness(0),
                CornerRadius            = new CornerRadius(0),
                UseAeroCaptionButtons   = false,
            });

            // Close
            window.CommandBindings.Add(new CommandBinding(
                SystemCommands.CloseWindowCommand,
                (_, __) => SystemCommands.CloseWindow(window)));

            // Minimize
            window.CommandBindings.Add(new CommandBinding(
                SystemCommands.MinimizeWindowCommand,
                (_, __) => SystemCommands.MinimizeWindow(window)));

            // Maximize
            window.CommandBindings.Add(new CommandBinding(
                SystemCommands.MaximizeWindowCommand,
                (_, __) => SystemCommands.MaximizeWindow(window)));

            // Restore
            window.CommandBindings.Add(new CommandBinding(
                SystemCommands.RestoreWindowCommand,
                (_, __) => SystemCommands.RestoreWindow(window)));
        }

        private static void DetachCommandBindings(Window window)
        {
            // Remove the WindowChrome
            WindowChrome.SetWindowChrome(window, null);

            // Remove only the SystemCommands bindings added by this behavior.
            for (int i = window.CommandBindings.Count - 1; i >= 0; i--)
            {
                var cb = window.CommandBindings[i];
                if (cb.Command == SystemCommands.CloseWindowCommand    ||
                    cb.Command == SystemCommands.MinimizeWindowCommand  ||
                    cb.Command == SystemCommands.MaximizeWindowCommand  ||
                    cb.Command == SystemCommands.RestoreWindowCommand)
                {
                    window.CommandBindings.RemoveAt(i);
                }
            }
        }
    }
}
