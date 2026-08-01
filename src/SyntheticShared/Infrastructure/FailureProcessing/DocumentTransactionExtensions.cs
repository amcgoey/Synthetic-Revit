using System;
using Autodesk.Revit.DB;

namespace Synthetic.Infrastructure.FailureProcessing
{
    /// <summary>
    /// Extension methods for Revit Document transaction creation and failure handling setup.
    /// </summary>
    public static class DocumentTransactionExtensions
    {
        /// <summary>
        /// Creates a new Transaction for the document, optionally configuring failure handling options with a preprocessor.
        /// </summary>
        /// <param name="doc">The Revit Document context.</param>
        /// <param name="name">The name of the transaction.</param>
        /// <param name="preprocessor">Optional IFailuresPreprocessor to assign to failure handling options.</param>
        /// <returns>A newly instantiated Transaction.</returns>
        public static Transaction CreateTransaction(this Document doc, string name, IFailuresPreprocessor? preprocessor = null)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));

            Transaction trans = new Transaction(doc, name);
            if (preprocessor != null)
            {
                FailureHandlingOptions options = trans.GetFailureHandlingOptions();
                options.SetFailuresPreprocessor(preprocessor);
                trans.SetFailureHandlingOptions(options);
            }
            return trans;
        }
    }
}
