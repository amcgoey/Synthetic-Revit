using System;

namespace Synthetic.Shared.UI
{
    /// <summary>
    /// Non-generic interface to facilitate interaction between non-generic WPF views/converters
    /// and the generic <see cref="SingleItemSelectionViewModel{T}"/>.
    /// </summary>
    public interface ISingleItemSelectionViewModel
    {
        /// <summary>
        /// Gets or sets the action to close the window, passing a boolean dialog result.
        /// </summary>
        Action<bool>? CloseAction { get; set; }

        /// <summary>
        /// Resolves the display name for a given item using the configured delegate.
        /// </summary>
        /// <param name="item">The item to resolve the display name for.</param>
        /// <returns>A string representation of the item.</returns>
        string GetItemDisplayName(object item);
    }
}
