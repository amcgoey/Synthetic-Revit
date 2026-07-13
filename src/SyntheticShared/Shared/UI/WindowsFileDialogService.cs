using System;
using Microsoft.Win32;

namespace Synthetic.Shared.UI
{
    /// <summary>
    /// Production implementation of IFileDialogService using Microsoft.Win32 file dialogs.
    /// </summary>
    public class WindowsFileDialogService : IFileDialogService
    {
        /// <summary>
        /// Displays the native Microsoft.Win32.SaveFileDialog.
        /// </summary>
        public string? SaveFileDialog(string filter, string title, string defaultFileName)
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = filter,
                Title = title,
                FileName = defaultFileName
            };

            if (dialog.ShowDialog() == true)
            {
                return dialog.FileName;
            }

            return null;
        }

        /// <summary>
        /// Displays the native Microsoft.Win32.OpenFileDialog.
        /// </summary>
        public string? OpenFileDialog(string filter, string title, string defaultFileName)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = filter,
                Title = title,
                FileName = defaultFileName
            };

            if (dialog.ShowDialog() == true)
            {
                return dialog.FileName;
            }

            return null;
        }
    }
}
