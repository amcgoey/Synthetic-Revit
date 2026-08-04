using System;
using System.Diagnostics;
using System.IO;
using System.Windows;

namespace Synthetic.Modules.SheetIndex.Views
{
    public partial class ExportCompletionWindow : Window
    {
        private readonly string _filePath;

        public ExportCompletionWindow(string filePath, IntPtr parentHandle = default)
        {
            InitializeComponent();
            _filePath = filePath ?? string.Empty;
            TxtFilePath.Text = $"File saved successfully to:\n{_filePath}";

            if (parentHandle != IntPtr.Zero)
            {
                var helper = new System.Windows.Interop.WindowInteropHelper(this);
                helper.Owner = parentHandle;
            }
        }

        private void BtnOpenFile_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (File.Exists(_filePath))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = _filePath,
                        UseShellExecute = true
                    });
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Could not open file: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnOpenFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (File.Exists(_filePath))
                {
                    string argument = $"/select,\"{_filePath}\"";
                    Process.Start("explorer.exe", argument);
                }
                else
                {
                    string? dir = Path.GetDirectoryName(_filePath);
                    if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                    {
                        Process.Start("explorer.exe", dir);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Could not open folder: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
