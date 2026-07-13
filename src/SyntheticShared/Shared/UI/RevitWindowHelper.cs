using System;
using System.Windows;
using System.Windows.Interop;
using Synthetic.Shared.UI;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.Modules.RevitDOM;
namespace Synthetic.Shared.UI{
    /// <summary>
    /// Helper methods for managing WPF window parenting/owners within Revit.
    /// </summary>
    public static class RevitWindowHelper
    {
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
