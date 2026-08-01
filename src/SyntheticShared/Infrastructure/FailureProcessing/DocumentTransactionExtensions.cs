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

        /// <summary>
        /// Executes an action within a managed transaction context, automatically committing on success or rolling back on exception.
        /// </summary>
        /// <param name="doc">The Revit Document context.</param>
        /// <param name="name">The name of the transaction.</param>
        /// <param name="action">The action to execute within the transaction.</param>
        /// <returns>A FailureProcessingReport containing failure execution diagnostics.</returns>
        public static FailureProcessingReport ExecuteTransaction(
            this Document doc,
            string name,
            Action<Transaction> action)
        {
            return ExecuteTransaction(doc, name, (IFailuresPreprocessor?)null, action);
        }

        /// <summary>
        /// Executes an action within a managed transaction context with a failure preprocessor, automatically committing on success or rolling back on exception.
        /// </summary>
        /// <param name="doc">The Revit Document context.</param>
        /// <param name="name">The name of the transaction.</param>
        /// <param name="preprocessor">Optional IFailuresPreprocessor to assign to transaction options.</param>
        /// <param name="action">The action to execute within the transaction.</param>
        /// <returns>A FailureProcessingReport containing failure execution diagnostics.</returns>
        public static FailureProcessingReport ExecuteTransaction(
            this Document doc,
            string name,
            IFailuresPreprocessor? preprocessor,
            Action<Transaction> action)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (action == null) throw new ArgumentNullException(nameof(action));

            using (Transaction tx = doc.CreateTransaction(name, preprocessor))
            {
                tx.Start();
                try
                {
                    action(tx);
                    tx.Commit();
                }
                catch (Exception)
                {
                    if (tx.GetStatus() == TransactionStatus.Started)
                    {
                        tx.RollBack();
                    }
                    throw;
                }

                if (preprocessor is CompositeFailuresPreprocessor composite)
                {
                    return composite.Report;
                }

                return new FailureProcessingReport();
            }
        }

        /// <summary>
        /// Executes an action accepting the Document within a managed transaction context.
        /// </summary>
        /// <param name="doc">The Revit Document context.</param>
        /// <param name="name">The name of the transaction.</param>
        /// <param name="action">The action accepting Document to execute.</param>
        /// <returns>A FailureProcessingReport containing failure execution diagnostics.</returns>
        public static FailureProcessingReport ExecuteTransaction(
            this Document doc,
            string name,
            Action<Document> action)
        {
            return ExecuteTransaction(doc, name, (IFailuresPreprocessor?)null, action);
        }

        /// <summary>
        /// Executes an action accepting the Document within a managed transaction context with a failure preprocessor.
        /// </summary>
        /// <param name="doc">The Revit Document context.</param>
        /// <param name="name">The name of the transaction.</param>
        /// <param name="preprocessor">Optional IFailuresPreprocessor to assign to transaction options.</param>
        /// <param name="action">The action accepting Document to execute.</param>
        /// <returns>A FailureProcessingReport containing failure execution diagnostics.</returns>
        public static FailureProcessingReport ExecuteTransaction(
            this Document doc,
            string name,
            IFailuresPreprocessor? preprocessor,
            Action<Document> action)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (action == null) throw new ArgumentNullException(nameof(action));
            return ExecuteTransaction(doc, name, preprocessor, (Transaction _) => action(doc));
        }

        /// <summary>
        /// Executes a parameterless action within a managed transaction context.
        /// </summary>
        /// <param name="doc">The Revit Document context.</param>
        /// <param name="name">The name of the transaction.</param>
        /// <param name="action">The parameterless action to execute.</param>
        /// <returns>A FailureProcessingReport containing failure execution diagnostics.</returns>
        public static FailureProcessingReport ExecuteTransaction(
            this Document doc,
            string name,
            Action action)
        {
            return ExecuteTransaction(doc, name, (IFailuresPreprocessor?)null, action);
        }

        /// <summary>
        /// Executes a parameterless action within a managed transaction context with a failure preprocessor.
        /// </summary>
        /// <param name="doc">The Revit Document context.</param>
        /// <param name="name">The name of the transaction.</param>
        /// <param name="preprocessor">Optional IFailuresPreprocessor to assign to transaction options.</param>
        /// <param name="action">The parameterless action to execute.</param>
        /// <returns>A FailureProcessingReport containing failure execution diagnostics.</returns>
        public static FailureProcessingReport ExecuteTransaction(
            this Document doc,
            string name,
            IFailuresPreprocessor? preprocessor,
            Action action)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (action == null) throw new ArgumentNullException(nameof(action));
            return ExecuteTransaction(doc, name, preprocessor, (Transaction _) => action());
        }
    }
}
