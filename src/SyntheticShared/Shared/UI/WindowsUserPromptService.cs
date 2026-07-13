using System;
using System.Windows;

namespace Synthetic.Shared.UI
{
    /// <summary>
    /// Production implementation of IUserPromptService using WPF's MessageBox.
    /// </summary>
    public class WindowsUserPromptService : IUserPromptService
    {
        /// <summary>
        /// Displays an informational message box.
        /// </summary>
        public void ShowMessage(string message, string title)
        {
            System.Windows.MessageBox.Show(message, title, System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
        }

        /// <summary>
        /// Displays a confirmation message box with Yes/No options.
        /// </summary>
        public bool ConfirmAction(string message, string title)
        {
            var result = System.Windows.MessageBox.Show(message, title, System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question);
            return result == System.Windows.MessageBoxResult.Yes;
        }
    }
}
