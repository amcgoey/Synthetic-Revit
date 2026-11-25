using System;

namespace Synthetic.Features.MultiModelPdfExport.Models
{
    /// <summary>
    /// Immutable output filename token resolution result for a single project document.
    /// </summary>
    public sealed class MultiModelPdfResolvedToken
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="MultiModelPdfResolvedToken"/> class.
        /// </summary>
        public MultiModelPdfResolvedToken(
            string documentKey,
            string baseToken,
            string finalToken,
            bool isCollisionDisambiguated)
        {
            DocumentKey = documentKey ?? string.Empty;
            BaseToken = baseToken ?? string.Empty;
            FinalToken = finalToken ?? string.Empty;
            IsCollisionDisambiguated = isCollisionDisambiguated;
        }

        /// <summary>Gets the deterministic document identity key.</summary>
        public string DocumentKey { get; }

        /// <summary>Gets the sanitized base filename token before collision disambiguation.</summary>
        public string BaseToken { get; }

        /// <summary>Gets the collision-free final filename token (without <c>.pdf</c> extension).</summary>
        public string FinalToken { get; }

        /// <summary>Gets a value indicating whether a disambiguation suffix was applied.</summary>
        public bool IsCollisionDisambiguated { get; }
    }
}
