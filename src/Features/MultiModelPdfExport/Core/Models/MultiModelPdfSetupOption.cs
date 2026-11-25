using System;

namespace Synthetic.Features.MultiModelPdfExport.Models
{
    /// <summary>
    /// Immutable snapshot of a PDF Export Setup (<c>ExportPDFSettings</c> or built-in default fallback).
    /// </summary>
    public sealed class MultiModelPdfSetupOption
    {
        /// <summary>Sentinel identifier for the built-in default sheet-size fallback setup.</summary>
        public const string DefaultSetupId = "__DEFAULT__";

        /// <summary>Display label for the built-in default sheet-size fallback setup.</summary>
        public const string DefaultSetupDisplayName = "<Default — Use Sheet Size>";

        /// <summary>
        /// Initializes a new instance of the <see cref="MultiModelPdfSetupOption"/> class.
        /// </summary>
        public MultiModelPdfSetupOption(string id, string displayName, bool isDefault = false)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException("Setup identifier cannot be blank.", nameof(id));
            }

            if (string.IsNullOrWhiteSpace(displayName))
            {
                throw new ArgumentException("Setup display name cannot be blank.", nameof(displayName));
            }

            Id = id;
            DisplayName = displayName;
            IsDefault = isDefault;
        }

        /// <summary>Gets the setup identifier (or <see cref="DefaultSetupId"/>).</summary>
        public string Id { get; }

        /// <summary>Gets the user-facing setup display name.</summary>
        public string DisplayName { get; }

        /// <summary>Gets a value indicating whether this option is the built-in default sheet-size fallback.</summary>
        public bool IsDefault { get; }

        /// <summary>
        /// Creates the built-in default sheet-size fallback setup option.
        /// </summary>
        public static MultiModelPdfSetupOption CreateDefault() =>
            new MultiModelPdfSetupOption(DefaultSetupId, DefaultSetupDisplayName, isDefault: true);
    }
}
