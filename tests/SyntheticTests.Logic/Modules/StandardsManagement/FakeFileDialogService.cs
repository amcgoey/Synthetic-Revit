using Synthetic.Shared.UI;

namespace SyntheticTests.Modules.StandardsManagement
{
    /// <summary>
    /// Fake implementation of IFileDialogService for headless testing.
    /// </summary>
    public class FakeFileDialogService : IFileDialogService
    {
        /// <summary>
        /// Gets or sets the preset path to return.
        /// </summary>
        public string? PresetPath { get; set; } = @"C:\Temp\ExportedStandards.json";

        /// <summary>
        /// Immediately returns the preset path without opening a UI.
        /// </summary>
        public string? SaveFileDialog(string filter, string title, string defaultFileName)
        {
            return PresetPath;
        }

        /// <summary>
        /// Immediately returns the preset path without opening a UI.
        /// </summary>
        public string? OpenFileDialog(string filter, string title, string defaultFileName)
        {
            return PresetPath;
        }
    }
}
