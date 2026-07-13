namespace Synthetic.Shared.UI
{
    /// <summary>
    /// Service interface for extracting and showing file dialogs.
    /// </summary>
    public interface IFileDialogService
    {
        /// <summary>
        /// Displays a save file dialog to select a path.
        /// </summary>
        /// <param name="filter">The file extension filter string.</param>
        /// <param name="title">The dialog window title.</param>
        /// <param name="defaultFileName">The default pre-populated file name.</param>
        /// <returns>The chosen file path, or null if cancelled.</returns>
        string? SaveFileDialog(string filter, string title, string defaultFileName);

        /// <summary>
        /// Displays an open file dialog to select a path.
        /// </summary>
        /// <param name="filter">The file extension filter string.</param>
        /// <param name="title">The dialog window title.</param>
        /// <param name="defaultFileName">The default pre-populated file name.</param>
        /// <returns>The chosen file path, or null if cancelled.</returns>
        string? OpenFileDialog(string filter, string title, string defaultFileName);
    }
}
