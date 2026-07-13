using System;

namespace Synthetic.Shared.UI
{
    /// <summary>
    /// Defines a service for showing message prompts and asking for confirmation,
    /// decoupling ViewModels from native UI dialog dependencies (like MessageBox.Show).
    /// </summary>
    public interface IUserPromptService
    {
        /// <summary>
        /// Displays an informational message to the user.
        /// </summary>
        /// <param name="message">The message text.</param>
        /// <param name="title">The window title.</param>
        void ShowMessage(string message, string title);

        /// <summary>
        /// Prompts the user with a Yes/No question and returns true if they choose Yes.
        /// </summary>
        /// <param name="message">The question text.</param>
        /// <param name="title">The window title.</param>
        /// <returns>True if the user confirmed/clicked Yes, otherwise false.</returns>
        bool ConfirmAction(string message, string title);
    }
}
