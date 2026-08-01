using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using NUnit.Framework;
using Autodesk.Revit.DB;
using Synthetic.Infrastructure.FailureProcessing;
using Synthetic.Modules.FamilyManagement.Handlers;

namespace SyntheticTests.Modules.FailureProcessing
{
    [TestFixture]
    public class DocumentTransactionExtensionsTests
    {
        private Document _doc;

        [SetUp]
        public void SetUp()
        {
#pragma warning disable CS0618 // Type or member is obsolete
            _doc = (Document)FormatterServices.GetUninitializedObject(typeof(Document));
#pragma warning restore CS0618
        }

        [Test]
        public void CreateTransaction_WithValidDocAndName_ShouldReturnTransactionWithName()
        {
            // Act
            using (Transaction trans = _doc.CreateTransaction("Test Transaction"))
            {
                // Assert
                Assert.IsNotNull(trans);
                Assert.AreEqual("Test Transaction", trans.GetName());
            }
        }

        [Test]
        public void CreateTransaction_WithPreprocessor_ShouldSetFailureHandlingOptions()
        {
            IFailuresPreprocessor preprocessor = FailurePipelines.Purge();

            // Act
            using (Transaction trans = _doc.CreateTransaction("Purge Transaction", preprocessor))
            {
                // Assert
                Assert.IsNotNull(trans);
                FailureHandlingOptions options = trans.GetFailureHandlingOptions();
                Assert.IsNotNull(options);
                Assert.AreSame(preprocessor, options.GetFailuresPreprocessor());
            }
        }

        [Test]
        public void CreateTransaction_WithoutPreprocessor_ShouldHaveNullPreprocessor()
        {
            // Act
            using (Transaction trans = _doc.CreateTransaction("Default Transaction"))
            {
                // Assert
                Assert.IsNotNull(trans);
                FailureHandlingOptions options = trans.GetFailureHandlingOptions();
                Assert.IsNotNull(options);
                Assert.IsNull(options.GetFailuresPreprocessor());
            }
        }

        [Test]
        public void CreateTransaction_NullDoc_ShouldThrowArgumentNullException()
        {
            // Arrange
            Document nullDoc = null!;

#pragma warning disable CS8625
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => nullDoc.CreateTransaction("Null Doc Tx"));
#pragma warning restore CS8625
        }

        [Test]
        public void AuditPurgeEventHandler_CanInstantiateAndHasDefaultProperties()
        {
            // Act
            AuditPurgeEventHandler handler = new AuditPurgeEventHandler();

            // Assert
            Assert.IsNotNull(handler);
            Assert.IsFalse(handler.Purge);
            Assert.IsFalse(handler.PurgeSchema);
            Assert.AreEqual("Audit & Purge Families Async Handler", handler.GetName());
        }

        [Test]
        public void ExecuteTransaction_ActionTransaction_CommitsAndReturnsReport()
        {
            bool executed = false;
            TransactionStatus statusDuringAction = (TransactionStatus)(-1);

            FailureProcessingReport report = _doc.ExecuteTransaction("Tx Action Test", (Transaction tx) =>
            {
                executed = true;
                statusDuringAction = tx.GetStatus();
            });

            Assert.IsTrue(executed);
            Assert.AreEqual(TransactionStatus.Started, statusDuringAction);
            Assert.IsNotNull(report);
        }

        [Test]
        public void ExecuteTransaction_ActionDocument_CommitsAndReturnsReport()
        {
            Document? passedDoc = null;

            FailureProcessingReport report = _doc.ExecuteTransaction("Doc Action Test", (Document doc) =>
            {
                passedDoc = doc;
            });

            Assert.AreSame(_doc, passedDoc);
            Assert.IsNotNull(report);
        }

        [Test]
        public void ExecuteTransaction_ActionNoArgs_CommitsAndReturnsReport()
        {
            bool executed = false;

            FailureProcessingReport report = _doc.ExecuteTransaction("Parameterless Action Test", () =>
            {
                executed = true;
            });

            Assert.IsTrue(executed);
            Assert.IsNotNull(report);
        }

        [Test]
        public void ExecuteTransaction_WithCompositePreprocessor_ReturnsCompositeReport()
        {
            CompositeFailuresPreprocessor preprocessor = FailurePipelines.Purge();

            FailureProcessingReport report = _doc.ExecuteTransaction("Composite Preprocessor Test", preprocessor, (Transaction tx) =>
            {
                // Action logic
            });

            Assert.IsNotNull(report);
            Assert.AreSame(preprocessor.Report, report);
        }

        [Test]
        public void ExecuteTransaction_OnDelegateFailure_RollsBackTransactionAndRethrows()
        {
            TransactionStatus statusDuringAction = (TransactionStatus)(-1);
            Transaction? capturedTx = null;

            Assert.Throws<InvalidOperationException>(() =>
            {
                _doc.ExecuteTransaction("Failing Transaction", (Transaction tx) =>
                {
                    capturedTx = tx;
                    statusDuringAction = tx.GetStatus();
                    throw new InvalidOperationException("Simulated transaction failure");
                });
            });

            TestContext.WriteLine($"statusDuringAction={statusDuringAction}, capturedTxStatus={capturedTx?.GetStatus()}");

            Assert.AreEqual(TransactionStatus.Started, statusDuringAction);
            Assert.IsNotNull(capturedTx);
            Assert.AreEqual(TransactionStatus.RolledBack, capturedTx.GetStatus());
        }

        [Test]
        public void ExecuteTransaction_NullDocOrAction_ThrowsArgumentNullException()
        {
            Document nullDoc = null!;

#pragma warning disable CS8625
            Assert.Throws<ArgumentNullException>(() => nullDoc.ExecuteTransaction("Null Doc Tx", (Transaction tx) => { }));
            Assert.Throws<ArgumentNullException>(() => nullDoc.ExecuteTransaction("Null Doc Doc", (Document d) => { }));
            Assert.Throws<ArgumentNullException>(() => nullDoc.ExecuteTransaction("Null Doc Void", () => { }));

            Assert.Throws<ArgumentNullException>(() => _doc.ExecuteTransaction("Null Action Tx", (Action<Transaction>)null));
            Assert.Throws<ArgumentNullException>(() => _doc.ExecuteTransaction("Null Action Doc", (Action<Document>)null));
            Assert.Throws<ArgumentNullException>(() => _doc.ExecuteTransaction("Null Action Void", (Action)null));
#pragma warning restore CS8625
        }
    }
}
