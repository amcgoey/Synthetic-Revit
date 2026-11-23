namespace Synthetic.Platform.UI
{
    /// <summary>
    /// Service interface for displaying a folder browser dialog.
    /// </summary>
    public interface IFolderDialogService
    {
        /// <summary>
        /// Prompts the user to select a folder.
        /// </summary>
        /// <param name="title">The dialog title or description.</param>
        /// <param name="initialPath">Optional initial folder path.</param>
        /// <returns>The selected folder path, or <c>null</c> if cancelled.</returns>
        string? SelectFolder(string title, string? initialPath = null);
    }
}
