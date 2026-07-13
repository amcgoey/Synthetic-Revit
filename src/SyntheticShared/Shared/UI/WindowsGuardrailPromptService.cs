using System;
using System.Windows;
using System.Diagnostics;

namespace Synthetic.Shared.UI
{
    /// <summary>
    /// Production implementation of IGuardrailPromptService using custom GuardrailPromptWindow.
    /// </summary>
    public class WindowsGuardrailPromptService : IGuardrailPromptService
    {
        /// <inheritdoc/>
        public GuardrailResult PromptProtectedFileOverwrite(string filePath)
        {
            IntPtr ownerHandle = Process.GetCurrentProcess().MainWindowHandle;
            var dialog = new GuardrailPromptWindow(filePath, ownerHandle);

            try
            {
                if (System.Windows.Application.Current?.MainWindow != null)
                {
                    dialog.Owner = System.Windows.Application.Current.MainWindow;
                }
            }
            catch {}

            bool? dialogResult = dialog.ShowDialog();
            if (dialogResult == true)
            {
                return dialog.Result;
            }

            return GuardrailResult.Cancel;
        }
    }
}
