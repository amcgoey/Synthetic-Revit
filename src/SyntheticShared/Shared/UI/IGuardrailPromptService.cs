namespace Synthetic.Shared.UI
{
    /// <summary>
    /// Represents the action choices when a master standards file overwrite is intercepted.
    /// </summary>
    public enum GuardrailResult
    {
        OverwriteAll,
        MergeOverwrite,
        MergePreserve,
        SaveAs,
        Cancel,
        Overwrite = OverwriteAll,
        Skip = Cancel
    }

    /// <summary>
    /// Service interface to prompt the user when attempting to write to a protected master standard file.
    /// </summary>
    public interface IGuardrailPromptService
    {
        /// <summary>
        /// Prompts the user with Overwrite, Save As, and Skip options for a protected file path.
        /// </summary>
        /// <param name="filePath">The protected file path being written to.</param>
        /// <returns>The user's choice.</returns>
        GuardrailResult PromptProtectedFileOverwrite(string filePath);
    }
}
