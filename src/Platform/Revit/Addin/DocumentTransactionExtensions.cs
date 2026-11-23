using System;
using System.Diagnostics;
using Autodesk.Revit.DB;

namespace Synthetic.Platform.Revit
{
    /// <summary>
    /// Extension methods for Revit <see cref="Document"/> transactions and <see cref="TransactionGroup"/> lifecycles
    /// with entry guards and explicit <see cref="IFailuresPreprocessor"/> policies.
    /// </summary>
    public static class DocumentTransactionExtensions
    {
        /// <summary>
        /// Creates a new <see cref="Transaction"/> for the document with an explicit <see cref="IFailuresPreprocessor"/> policy.
        /// </summary>
        /// <param name="doc">The Revit Document context.</param>
        /// <param name="name">The name of the transaction.</param>
        /// <param name="preprocessor">The required <see cref="IFailuresPreprocessor"/> to assign to failure handling options.</param>
        /// <param name="clearAfterRollback">Whether to clear unhandled failures after rollback.</param>
        /// <returns>A newly instantiated <see cref="Transaction"/>.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="doc"/> or <paramref name="preprocessor"/> is null.</exception>
        /// <exception cref="ArgumentException">Thrown when <paramref name="name"/> is null or whitespace.</exception>
        public static Transaction CreateTransaction(
            this Document doc,
            string name,
            IFailuresPreprocessor preprocessor,
            bool clearAfterRollback = false)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Transaction name cannot be null or whitespace.", nameof(name));
            if (preprocessor == null) throw new ArgumentNullException(nameof(preprocessor));

            var trans = new Transaction(doc, name);
            FailureHandlingOptions options = trans.GetFailureHandlingOptions();
            options.SetFailuresPreprocessor(preprocessor);
            if (clearAfterRollback)
            {
                options.SetClearAfterRollback(true);
            }
            trans.SetFailureHandlingOptions(options);
            return trans;
        }

        /// <summary>
        /// Executes an action within a managed transaction context with an explicit <see cref="IFailuresPreprocessor"/>,
        /// automatically committing on success or rolling back on exception.
        /// </summary>
        /// <param name="doc">The Revit Document context.</param>
        /// <param name="name">The name of the transaction.</param>
        /// <param name="preprocessor">The required <see cref="IFailuresPreprocessor"/> to assign to transaction options.</param>
        /// <param name="action">The action to execute within the transaction.</param>
        /// <param name="clearAfterRollback">Whether to clear unhandled failures after rollback.</param>
        /// <returns>A <see cref="FailureProcessingReport"/> containing failure execution diagnostics.</returns>
        public static FailureProcessingReport ExecuteTransaction(
            this Document doc,
            string name,
            IFailuresPreprocessor preprocessor,
            Action<Transaction> action,
            bool clearAfterRollback = false)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Transaction name cannot be null or whitespace.", nameof(name));
            if (preprocessor == null) throw new ArgumentNullException(nameof(preprocessor));
            if (action == null) throw new ArgumentNullException(nameof(action));

            EnsureDocumentWritableForEntry(doc, name);

            using (Transaction tx = doc.CreateTransaction(name, preprocessor, clearAfterRollback))
            {
                tx.Start();
                try
                {
                    action(tx);
                    tx.Commit();
                }
                catch
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
        /// Executes an action accepting the <see cref="Document"/> within a managed transaction context
        /// with an explicit <see cref="IFailuresPreprocessor"/>.
        /// </summary>
        /// <param name="doc">The Revit Document context.</param>
        /// <param name="name">The name of the transaction.</param>
        /// <param name="preprocessor">The required <see cref="IFailuresPreprocessor"/> to assign to transaction options.</param>
        /// <param name="action">The action accepting <see cref="Document"/> to execute.</param>
        /// <param name="clearAfterRollback">Whether to clear unhandled failures after rollback.</param>
        /// <returns>A <see cref="FailureProcessingReport"/> containing failure execution diagnostics.</returns>
        public static FailureProcessingReport ExecuteTransaction(
            this Document doc,
            string name,
            IFailuresPreprocessor preprocessor,
            Action<Document> action,
            bool clearAfterRollback = false)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (action == null) throw new ArgumentNullException(nameof(action));
            return ExecuteTransaction(doc, name, preprocessor, (Transaction _) => action(doc), clearAfterRollback);
        }

        /// <summary>
        /// Executes a parameterless action within a managed transaction context
        /// with an explicit <see cref="IFailuresPreprocessor"/>.
        /// </summary>
        /// <param name="doc">The Revit Document context.</param>
        /// <param name="name">The name of the transaction.</param>
        /// <param name="preprocessor">The required <see cref="IFailuresPreprocessor"/> to assign to transaction options.</param>
        /// <param name="action">The parameterless action to execute.</param>
        /// <param name="clearAfterRollback">Whether to clear unhandled failures after rollback.</param>
        /// <returns>A <see cref="FailureProcessingReport"/> containing failure execution diagnostics.</returns>
        public static FailureProcessingReport ExecuteTransaction(
            this Document doc,
            string name,
            IFailuresPreprocessor preprocessor,
            Action action,
            bool clearAfterRollback = false)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (action == null) throw new ArgumentNullException(nameof(action));
            return ExecuteTransaction(doc, name, preprocessor, (Transaction _) => action(), clearAfterRollback);
        }

        /// <summary>
        /// Executes a multi-transaction batch inside a <see cref="TransactionGroup"/>, calling <see cref="TransactionGroup.Assimilate"/>
        /// on clean completion and rolling back on error without masking any inner exception if an inner <see cref="Transaction"/> remained open.
        /// </summary>
        /// <param name="doc">The Revit Document context.</param>
        /// <param name="name">The name of the transaction group.</param>
        /// <param name="action">The batch action to execute.</param>
        public static void ExecuteCommittedBatchGroup(
            this Document doc,
            string name,
            Action action)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Transaction group name cannot be null or whitespace.", nameof(name));
            if (action == null) throw new ArgumentNullException(nameof(action));

            EnsureDocumentWritableForEntry(doc, name);

            using (var group = new TransactionGroup(doc, name))
            {
                group.Start();
                try
                {
                    action();
                    group.Assimilate();
                }
                catch
                {
                    TryRollBackGroupWithoutMasking(group, name);
                    throw;
                }
            }
        }

        /// <summary>
        /// Executes a multi-transaction batch accepting the <see cref="Document"/> inside an assimilated <see cref="TransactionGroup"/>.
        /// </summary>
        /// <param name="doc">The Revit Document context.</param>
        /// <param name="name">The name of the transaction group.</param>
        /// <param name="action">The batch action accepting <see cref="Document"/> to execute.</param>
        public static void ExecuteCommittedBatchGroup(
            this Document doc,
            string name,
            Action<Document> action)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (action == null) throw new ArgumentNullException(nameof(action));
            ExecuteCommittedBatchGroup(doc, name, () => action(doc));
        }

        /// <summary>
        /// Executes an ephemeral or test batch inside a <see cref="TransactionGroup"/> that unconditionally
        /// rolls back in <c>finally</c>, protecting any inner exception from being masked during rollback.
        /// </summary>
        /// <param name="doc">The Revit Document context.</param>
        /// <param name="name">The name of the transaction group.</param>
        /// <param name="action">The action to execute inside the rolled-back group.</param>
        public static void ExecuteRolledBackGroup(
            this Document doc,
            string name,
            Action action)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Transaction group name cannot be null or whitespace.", nameof(name));
            if (action == null) throw new ArgumentNullException(nameof(action));

            EnsureDocumentWritableForEntry(doc, name);

            using (var group = new TransactionGroup(doc, name))
            {
                group.Start();
                bool actionSucceeded = false;
                try
                {
                    action();
                    actionSucceeded = true;
                }
                finally
                {
                    if (group.GetStatus() == TransactionStatus.Started)
                    {
                        if (actionSucceeded)
                        {
                            group.RollBack();
                        }
                        else
                        {
                            TryRollBackGroupWithoutMasking(group, name);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Executes an ephemeral or test batch accepting the <see cref="Document"/> inside an unconditionally rolled-back <see cref="TransactionGroup"/>.
        /// </summary>
        /// <param name="doc">The Revit Document context.</param>
        /// <param name="name">The name of the transaction group.</param>
        /// <param name="action">The action accepting <see cref="Document"/> to execute.</param>
        public static void ExecuteRolledBackGroup(
            this Document doc,
            string name,
            Action<Document> action)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (action == null) throw new ArgumentNullException(nameof(action));
            ExecuteRolledBackGroup(doc, name, () => action(doc));
        }

        private static void EnsureDocumentWritableForEntry(Document doc, string operationName)
        {
            if (doc.IsReadOnly)
            {
                throw new InvalidOperationException($"Cannot start '{operationName}' because the document is read-only.");
            }

            if (doc.IsModifiable)
            {
                throw new InvalidOperationException($"Cannot start '{operationName}' while a transaction is already open on the document.");
            }
        }

        private static void TryRollBackGroupWithoutMasking(TransactionGroup group, string groupName)
        {
            if (group.GetStatus() != TransactionStatus.Started)
            {
                return;
            }

            try
            {
                group.RollBack();
            }
            catch (Exception rollbackEx)
            {
                Debug.WriteLine($"[TransactionGroup] RollBack failed during exception unwinding for '{groupName}': {rollbackEx.Message}");
            }
        }
    }
}
