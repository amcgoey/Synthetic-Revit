using System;

namespace Synthetic.Shared.UI
{
    /// <summary>
    /// Defines a service for prompting the user to enter a text value (for example, a Print Set or Preset name),
    /// decoupling ViewModels from modal WPF input dialogs.
    /// </summary>
    public interface ITextInputPromptService
    {
        /// <summary>
        /// Prompts the user for text input.
        /// </summary>
        /// <param name="title">The dialog window title.</param>
        /// <param name="promptMessage">The descriptive label or prompt message shown above the text box.</param>
        /// <param name="initialValue">Optional initial text pre-filled in the input box.</param>
        /// <param name="validator">
        /// Optional validation callback receiving the trimmed input text and returning an error message if invalid,
        /// or <c>null</c>/<c>""</c> when valid.
        /// </param>
        /// <returns>The confirmed trimmed text value, or <c>null</c> if cancelled.</returns>
        string? PromptText(
            string title,
            string promptMessage,
            string? initialValue = null,
            Func<string, string?>? validator = null);
    }
}
