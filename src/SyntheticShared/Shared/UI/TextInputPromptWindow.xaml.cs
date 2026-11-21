using System;
using System.Windows;
using System.Windows.Controls;

namespace Synthetic.Shared.UI
{
    /// <summary>
    /// Dark-themed modal text input dialog supporting optional inline validation.
    /// </summary>
    public partial class TextInputPromptWindow : Window
    {
        private readonly Func<string, string?>? _validator;

        /// <summary>
        /// Initializes a new instance of the <see cref="TextInputPromptWindow"/> class.
        /// </summary>
        public TextInputPromptWindow(
            string title,
            string promptMessage,
            string? initialValue = null,
            Func<string, string?>? validator = null,
            IntPtr parentHandle = default)
        {
            _validator = validator;
            InitializeComponent();
            RevitWindowHelper.SetOwner(this, parentHandle);

            SetCurrentValue(TitleProperty, string.IsNullOrWhiteSpace(title) ? "Enter Name" : title);
            PromptLabel.Text = promptMessage ?? string.Empty;
            InputTextBox.Text = initialValue ?? string.Empty;

            Loaded += OnWindowLoaded;
        }

        /// <summary>
        /// Gets the confirmed trimmed text entered by the user.
        /// </summary>
        public string ConfirmedText { get; private set; } = string.Empty;

        private void OnWindowLoaded(object sender, RoutedEventArgs e)
        {
            InputTextBox.Focus();
            InputTextBox.SelectAll();
        }

        private void InputTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (ErrorTextBlock.Visibility == Visibility.Visible)
            {
                ErrorTextBlock.Text = string.Empty;
                ErrorTextBlock.Visibility = Visibility.Collapsed;
            }
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            string trimmed = (InputTextBox.Text ?? string.Empty).Trim();
            string? error = _validator != null ? _validator(trimmed) : null;
            if (error == null && string.IsNullOrWhiteSpace(trimmed))
            {
                error = "Name cannot be blank.";
            }

            if (!string.IsNullOrEmpty(error))
            {
                ErrorTextBlock.Text = error!;
                ErrorTextBlock.Visibility = Visibility.Visible;
                InputTextBox.Focus();
                InputTextBox.SelectAll();
                return;
            }

            ConfirmedText = trimmed;
            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
