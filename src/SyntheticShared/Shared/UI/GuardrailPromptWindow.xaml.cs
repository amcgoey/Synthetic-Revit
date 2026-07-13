using System;
using System.Windows;
using Synthetic.Shared.RevitAPI;

namespace Synthetic.Shared.UI
{
    /// <summary>
    /// Interaction logic for GuardrailPromptWindow.xaml
    /// </summary>
    public partial class GuardrailPromptWindow : Window
    {
        /// <summary>
        /// Gets the chosen guardrail action.
        /// </summary>
        public GuardrailResult Result { get; private set; } = GuardrailResult.Cancel;

        /// <summary>
        /// Gets the file path targeted for saving.
        /// </summary>
        public string FilePath { get; }

        /// <summary>
        /// Initializes a new instance of the GuardrailPromptWindow.
        /// </summary>
        /// <param name="filePath">The protected file path.</param>
        /// <param name="mainWindowHandle">The parent Revit window handle.</param>
        public GuardrailPromptWindow(string filePath, IntPtr mainWindowHandle)
        {
            FilePath = filePath;
            InitializeComponent();
            DataContext = this;

            if (mainWindowHandle != IntPtr.Zero)
            {
                RevitWindowHelper.SetOwner(this, mainWindowHandle);
            }
        }

        private void OverwriteAll_Click(object sender, RoutedEventArgs e)
        {
            Result = GuardrailResult.OverwriteAll;
            DialogResult = true;
            Close();
        }

        private void MergeOverwrite_Click(object sender, RoutedEventArgs e)
        {
            Result = GuardrailResult.MergeOverwrite;
            DialogResult = true;
            Close();
        }

        private void MergePreserve_Click(object sender, RoutedEventArgs e)
        {
            Result = GuardrailResult.MergePreserve;
            DialogResult = true;
            Close();
        }

        private void SaveAs_Click(object sender, RoutedEventArgs e)
        {
            Result = GuardrailResult.SaveAs;
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            Result = GuardrailResult.Cancel;
            DialogResult = false;
            Close();
        }
    }
}
