using System;

namespace Synthetic.Platform.UI
{
    /// <summary>
    /// Production <see cref="ITextInputPromptService"/> presenting <see cref="TextInputPromptWindow"/>.
    /// </summary>
    public class WindowsTextInputPromptService : ITextInputPromptService
    {
        private readonly IntPtr _parentHandle;

        /// <summary>
        /// Initializes a new instance of the <see cref="WindowsTextInputPromptService"/> class.
        /// </summary>
        public WindowsTextInputPromptService(IntPtr parentHandle = default)
        {
            _parentHandle = parentHandle;
        }

        /// <inheritdoc />
        public string? PromptText(
            string title,
            string promptMessage,
            string? initialValue = null,
            Func<string, string?>? validator = null)
        {
            var window = new TextInputPromptWindow(title, promptMessage, initialValue, validator, _parentHandle);
            bool? result = window.ShowDialog();
            return result == true ? window.ConfirmedText : null;
        }
    }
}
