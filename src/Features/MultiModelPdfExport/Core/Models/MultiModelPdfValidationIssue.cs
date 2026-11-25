using System;

namespace Synthetic.Features.MultiModelPdfExport.Models
{
    /// <summary>
    /// Immutable blocking validation issue raised prior to executing an export batch.
    /// </summary>
    public sealed class MultiModelPdfValidationIssue
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="MultiModelPdfValidationIssue"/> class.
        /// </summary>
        public MultiModelPdfValidationIssue(
            MultiModelPdfValidationCode code,
            string message,
            string? targetKey = null)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                throw new ArgumentException("Validation message cannot be blank.", nameof(message));
            }

            Code = code;
            Message = message;
            TargetKey = targetKey;
        }

        /// <summary>Gets the blocking validation code.</summary>
        public MultiModelPdfValidationCode Code { get; }

        /// <summary>Gets the user-facing error message.</summary>
        public string Message { get; }

        /// <summary>Gets the optional target document key or control identifier.</summary>
        public string? TargetKey { get; }
    }
}
