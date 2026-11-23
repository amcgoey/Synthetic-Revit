using System;

namespace Synthetic.Platform.UI
{
    /// <summary>
    /// Production implementation of <see cref="IFolderDialogService"/> delegating to <see cref="FileDialogHelper"/>.
    /// </summary>
    public class WindowsFolderDialogService : IFolderDialogService
    {
        private readonly IntPtr _ownerHandle;

        /// <summary>
        /// Initializes a new instance of the <see cref="WindowsFolderDialogService"/> class.
        /// </summary>
        /// <param name="ownerHandle">Optional native parent window handle.</param>
        public WindowsFolderDialogService(IntPtr ownerHandle = default)
        {
            _ownerHandle = ownerHandle;
        }

        /// <inheritdoc />
        public string? SelectFolder(string title, string? initialPath = null)
        {
            string selected = FileDialogHelper.SelectFolder(_ownerHandle, title, initialPath);
            return string.IsNullOrWhiteSpace(selected) ? null : selected;
        }
    }
}
