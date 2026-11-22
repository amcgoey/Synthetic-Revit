using System;
using System.IO;

using Synthetic.Shared.UI;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;
using Synthetic.Modules.StandardsManagement.ViewModels;
namespace Synthetic.Shared.UI{
    /// <summary>
    /// Helper methods for showing Win32 file and folder dialogs with proper window parenting.
    /// </summary>
    public static class FileDialogHelper
    {
        /// <summary>
        /// Displays a folder browser dialog parented to the specified window handle.
        /// </summary>
        /// <param name="ownerHandle">The parent window handle (typically Revit's MainWindowHandle).</param>
        /// <param name="title">The description/title to show in the folder browser dialog.</param>
        /// <param name="initialPath">The initial folder path to display.</param>
        /// <returns>The selected folder path, or null if the selection was cancelled.</returns>
        public static string SelectFolder(IntPtr ownerHandle, string title, string? initialPath = null)
        {
            using (var dialog = new System.Windows.Forms.FolderBrowserDialog())
            {
                dialog.Description = title;
                
                if (!string.IsNullOrEmpty(initialPath) && Directory.Exists(initialPath))
                {
                    dialog.SelectedPath = initialPath;
                }

                System.Windows.Forms.DialogResult result;
                if (ownerHandle != IntPtr.Zero)
                {
                    result = dialog.ShowDialog(new Win32WindowWrapper(ownerHandle));
                }
                else
                {
                    result = dialog.ShowDialog();
                }

                if (result == System.Windows.Forms.DialogResult.OK)
                {
                    return dialog.SelectedPath ?? string.Empty;
                }
            }

            return string.Empty;
        }

        private class Win32WindowWrapper : System.Windows.Forms.IWin32Window
        {
            public IntPtr Handle { get; }
            public Win32WindowWrapper(IntPtr handle) => Handle = handle;
        }
    }
}
