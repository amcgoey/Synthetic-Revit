using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Interop;

namespace Synthetic.Platform.UI
{
    /// <summary>
    /// Helper methods for managing WPF window parenting/owners within Revit.
    /// </summary>
    public static class RevitWindowHelper
    {
        private static IntPtr _mainWindowHandle;

        /// <summary>
        /// Gets or sets the main Revit application window handle, falling back to
        /// the current process main window handle when no host handle was explicitly seeded.
        /// </summary>
        public static IntPtr MainWindowHandle
        {
            get
            {
                if (_mainWindowHandle != IntPtr.Zero)
                {
                    return _mainWindowHandle;
                }

                try
                {
                    return Process.GetCurrentProcess().MainWindowHandle;
                }
                catch
                {
                    return IntPtr.Zero;
                }
            }
            set => _mainWindowHandle = value;
        }
        /// <summary>
        /// Sets the parent owner window of a WPF window to the main Revit application window.
        /// </summary>
        /// <param name="wpfWindow">The WPF window to parent.</param>
        /// <param name="revitMainWindowHandle">The window handle of the main Revit window.</param>
        public static void SetOwner(Window wpfWindow, IntPtr revitMainWindowHandle)
        {
            if (wpfWindow == null)
                throw new ArgumentNullException(nameof(wpfWindow));
            if (revitMainWindowHandle == IntPtr.Zero)
                return;
            WindowInteropHelper helper = new WindowInteropHelper(wpfWindow)
            {
                Owner = revitMainWindowHandle
            };
        }
    }
}
