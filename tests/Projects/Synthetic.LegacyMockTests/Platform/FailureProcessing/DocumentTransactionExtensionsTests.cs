using System;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Serialization;
using NUnit.Framework;
using Autodesk.Revit.DB;
using Synthetic.Platform.Revit;
using Synthetic.Features.FamilyManagement.Handlers;

namespace SyntheticTests.Modules.FailureProcessing
{
    [TestFixture]
    [QuarantineTest]
    public class DocumentTransactionExtensionsTests
    {
        private Document _doc = null!;

        [SetUp]
        public void SetUp()
        {
#pragma warning disable CS0618 // Type or member is obsolete
            _doc = (Document)FormatterServices.GetUninitializedObject(typeof(Document));
#pragma warning restore CS0618
        }

        [Test]
        public void CreateTransaction_WithValidDocNameAndPreprocessor_ShouldReturnTransactionWithName()
        {
            IFailuresPreprocessor preprocessor = FailurePipelines.ObserveOnly();

            using (Transaction trans = _doc.CreateTransaction("Test Transaction", preprocessor))
            {
                Assert.IsNotNull(trans);
                Assert.AreEqual("Test Transaction", trans.GetName());
            }
        }

        [Test]
        public void CreateTransaction_WithPreprocessor_ShouldSetFailureHandlingOptions()
        {
            IFailuresPreprocessor preprocessor = FailurePipelines.Purge();

            using (Transaction trans = _doc.CreateTransaction("Purge Transaction", preprocessor))
            {
                Assert.IsNotNull(trans);
                FailureHandlingOptions options = trans.GetFailureHandlingOptions();
                Assert.IsNotNull(options);
                Assert.AreSame(preprocessor, options.GetFailuresPreprocessor());
            }
        }

        [Test]
        public void CreateTransaction_WithObserveOnlyPreprocessor_ShouldSetPreprocessorAndDefaultClearAfterRollbackFalse()
        {
            IFailuresPreprocessor preprocessor = FailurePipelines.ObserveOnly();

            using (Transaction trans = _doc.CreateTransaction("Default Transaction", preprocessor))
            {
                Assert.IsNotNull(trans);
                FailureHandlingOptions options = trans.GetFailureHandlingOptions();
                Assert.IsNotNull(options);
                Assert.AreSame(preprocessor, options.GetFailuresPreprocessor());
                Assert.IsFalse(options.GetClearAfterRollback());
            }
        }

        [Test]
        public void CreateTransaction_WithClearAfterRollbackTrue_ShouldSetClearAfterRollbackOnOptions()
        {
            IFailuresPreprocessor preprocessor = FailurePipelines.ObserveOnly();

            using (Transaction trans = _doc.CreateTransaction("ClearRollback Transaction", preprocessor, clearAfterRollback: true))
            {
                Assert.IsNotNull(trans);
                FailureHandlingOptions options = trans.GetFailureHandlingOptions();
                Assert.IsNotNull(options);
                Assert.IsTrue(options.GetClearAfterRollback());
            }
        }

        [Test]
        public void CreateTransaction_WithDeleteAllWarningsRecordedAndClearAfterRollback_ShouldConfigureBoth()
        {
            IFailuresPreprocessor preprocessor = FailurePipelines.DeleteAllWarningsRecorded();

            using (Transaction trans = _doc.CreateTransaction("Composite ClearRollback Transaction", preprocessor, clearAfterRollback: true))
            {
                Assert.IsNotNull(trans);
                FailureHandlingOptions options = trans.GetFailureHandlingOptions();
                Assert.IsNotNull(options);
                Assert.AreSame(preprocessor, options.GetFailuresPreprocessor());
                Assert.IsTrue(options.GetClearAfterRollback());
            }
        }

        [Test]
        public void CreateTransaction_NullDocOrNullPreprocessor_ShouldThrowArgumentNullException()
        {
            Document nullDoc = null!;
            IFailuresPreprocessor preprocessor = FailurePipelines.ObserveOnly();

#pragma warning disable CS8625
            Assert.Throws<ArgumentNullException>(() => nullDoc.CreateTransaction("Null Doc Tx", preprocessor));
            Assert.Throws<ArgumentNullException>(() => _doc.CreateTransaction("Null Preprocessor Tx", null));
#pragma warning restore CS8625
        }

        [Test]
        public void AuditPurgeEventHandler_CanInstantiateAndHasDefaultProperties()
        {
            AuditPurgeEventHandler handler = new AuditPurgeEventHandler();

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

            FailureProcessingReport report = _doc.ExecuteTransaction("Tx Action Test", FailurePipelines.ObserveOnly(), (Transaction tx) =>
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

            FailureProcessingReport report = _doc.ExecuteTransaction("Doc Action Test", FailurePipelines.ObserveOnly(), (Document doc) =>
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

            FailureProcessingReport report = _doc.ExecuteTransaction("Parameterless Action Test", FailurePipelines.ObserveOnly(), () =>
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
                _doc.ExecuteTransaction("Failing Transaction", FailurePipelines.ObserveOnly(), (Transaction tx) =>
                {
                    capturedTx = tx;
                    statusDuringAction = tx.GetStatus();
                    throw new InvalidOperationException("Simulated transaction failure");
                });
            });

            Assert.AreEqual(TransactionStatus.Started, statusDuringAction);
            Assert.IsNotNull(capturedTx);
            Assert.AreEqual(TransactionStatus.RolledBack, capturedTx!.GetStatus());
        }

        [Test]
        public void ExecuteTransaction_NullDocPreprocessorOrAction_ThrowsArgumentNullException()
        {
            Document nullDoc = null!;
            IFailuresPreprocessor preprocessor = FailurePipelines.ObserveOnly();

#pragma warning disable CS8625
            Assert.Throws<ArgumentNullException>(() => nullDoc.ExecuteTransaction("Null Doc Tx", preprocessor, (Transaction tx) => { }));
            Assert.Throws<ArgumentNullException>(() => nullDoc.ExecuteTransaction("Null Doc Doc", preprocessor, (Document d) => { }));
            Assert.Throws<ArgumentNullException>(() => nullDoc.ExecuteTransaction("Null Doc Void", preprocessor, () => { }));

            Assert.Throws<ArgumentNullException>(() => _doc.ExecuteTransaction("Null Preprocessor Tx", null, (Transaction tx) => { }));
            Assert.Throws<ArgumentNullException>(() => _doc.ExecuteTransaction("Null Preprocessor Doc", null, (Document d) => { }));
            Assert.Throws<ArgumentNullException>(() => _doc.ExecuteTransaction("Null Preprocessor Void", null, () => { }));

            Assert.Throws<ArgumentNullException>(() => _doc.ExecuteTransaction("Null Action Tx", preprocessor, (Action<Transaction>)null));
            Assert.Throws<ArgumentNullException>(() => _doc.ExecuteTransaction("Null Action Doc", preprocessor, (Action<Document>)null));
            Assert.Throws<ArgumentNullException>(() => _doc.ExecuteTransaction("Null Action Void", preprocessor, (Action)null));
#pragma warning restore CS8625
        }

#if SYNTHETIC_LOGIC_TESTS
        [Test]
        public void ExecuteCommittedBatchGroup_AssimilatesOnSuccess_AndRejectsReadOnlyOrModifiableEntry()
        {
            var doc = new Document();
            doc.ExecuteCommittedBatchGroup("Mock Batch", () =>
            {
                doc.ExecuteTransaction("Inner Tx", FailurePipelines.ObserveOnly(), () => { });
            });

            Assert.IsNotNull(TransactionGroup.LastCreatedGroup);
            Assert.IsTrue(TransactionGroup.LastCreatedGroup!.WasAssimilated);

            var readOnlyDoc = new Document { IsReadOnly = true };
            Assert.Throws<InvalidOperationException>(() =>
                readOnlyDoc.ExecuteCommittedBatchGroup("ReadOnly Batch", () => { }));

            using (var activeTx = new Transaction(doc, "Open Tx"))
            {
                activeTx.Start();
                try
                {
                    Assert.Throws<InvalidOperationException>(() =>
                        doc.ExecuteCommittedBatchGroup("Nested Batch", () => { }));
                }
                finally
                {
                    activeTx.RollBack();
                }
            }
        }

        [Test]
        public void ExecuteCommittedBatchGroup_DoesNotMaskInnerExceptionWhenInnerTransactionRemainedOpen()
        {
            var doc = new Document();
            Transaction? leakedTx = null;

            var thrown = Assert.Throws<ApplicationException>(() =>
            {
                doc.ExecuteCommittedBatchGroup("Failing Batch", () =>
                {
                    leakedTx = new Transaction(doc, "Unclosed Inner Tx");
                    leakedTx.Start();
                    throw new ApplicationException("Inner batch failure");
                });
            });

            Assert.AreEqual("Inner batch failure", thrown!.Message);
            leakedTx?.RollBack();
            leakedTx?.Dispose();
        }

        [Test]
        public void ExecuteRolledBackGroup_RollsBackUnconditionally_AndDoesNotMaskInnerException()
        {
            var doc = new Document();
            doc.ExecuteRolledBackGroup("Ephemeral Group", () =>
            {
                doc.ExecuteTransaction("Inner Tx", FailurePipelines.ObserveOnly(), () => { });
            });

            Assert.IsNotNull(TransactionGroup.LastCreatedGroup);
            Assert.AreEqual(TransactionStatus.RolledBack, TransactionGroup.LastCreatedGroup!.GetStatus());

            Transaction? leakedTx = null;
            var thrown = Assert.Throws<ApplicationException>(() =>
            {
                doc.ExecuteRolledBackGroup("Failing Ephemeral Group", () =>
                {
                    leakedTx = new Transaction(doc, "Unclosed Inner Tx");
                    leakedTx.Start();
                    throw new ApplicationException("Inner ephemeral failure");
                });
            });

            Assert.AreEqual("Inner ephemeral failure", thrown!.Message);
            leakedTx?.RollBack();
            leakedTx?.Dispose();
        }
#endif

        [Test]
        public void PayloadAssembly_EmitsRevitVersionMetadata_AndETransmitResolverFallsBackUnderMockApi()
        {
            Assembly payloadAssembly = typeof(ETransmitAssemblyResolver).Assembly;
            AssemblyMetadataAttribute? metadata = payloadAssembly
                .GetCustomAttributes<AssemblyMetadataAttribute>()
                .FirstOrDefault(attr => string.Equals(attr.Key, "RevitVersion", StringComparison.Ordinal));

            Assert.IsNotNull(metadata, "Payload assembly must emit [AssemblyMetadata(\"RevitVersion\", ...)].");
            Assert.AreEqual("2027", metadata!.Value);

            Version? mockApiVersion = typeof(Document).Assembly.GetName().Version;
            Assert.IsNotNull(mockApiVersion);
            Assert.AreEqual(1, mockApiVersion!.Major, "RevitAPIMock should report major version 1.0.0.0.");

            Assert.AreEqual("2027", ETransmitAssemblyResolver.ResolveRevitYear());
        }

        [Test]
        public void ETransmitAssemblyResolver_PrioritizesLiveRevitApiVersion_AndFailsFastOnMissingOrUnparseableMetadata()
        {
            Assembly liveRevitApi27 = CreateDynamicAssembly(new Version(27, 3, 0, 0));
            Assembly liveRevitApi2027 = CreateDynamicAssembly(new Version(2027, 0, 0, 0));
            Assembly mockRevitApi = CreateDynamicAssembly(new Version(1, 0, 0, 0));
            Assembly metadata2026 = CreateDynamicAssembly(null, ("RevitVersion", "2026"));
            Assembly metadataWhitespace = CreateDynamicAssembly(null, ("RevitVersion", " 2027 "));
            Assembly missingMetadata = CreateDynamicAssembly(null);
            Assembly invalidAlphaMetadata = CreateDynamicAssembly(null, ("RevitVersion", "not-a-year"));
            Assembly twoDigitMetadata = CreateDynamicAssembly(null, ("RevitVersion", "26"));
            Assembly emptyMetadata = CreateDynamicAssembly(null, ("RevitVersion", "   "));

            Assert.AreEqual("2027", ETransmitAssemblyResolver.ResolveRevitYear(liveRevitApi27, metadata2026));
            Assert.AreEqual("2027", ETransmitAssemblyResolver.ResolveRevitYear(liveRevitApi2027, metadata2026));
            Assert.AreEqual("2027", ETransmitAssemblyResolver.ResolveRevitYear(mockRevitApi, metadataWhitespace));

            InvalidOperationException missingEx = Assert.Throws<InvalidOperationException>(
                () => ETransmitAssemblyResolver.ResolveRevitYear(mockRevitApi, missingMetadata))!;
            StringAssert.Contains("RevitVersion", missingEx.Message);

            foreach (Assembly badMetadata in new[] { invalidAlphaMetadata, twoDigitMetadata, emptyMetadata })
            {
                InvalidOperationException parseEx = Assert.Throws<InvalidOperationException>(
                    () => ETransmitAssemblyResolver.ResolveRevitYear(mockRevitApi, badMetadata))!;
                StringAssert.Contains("RevitVersion", parseEx.Message);
            }
        }

        private static Assembly CreateDynamicAssembly(Version? version, params (string Key, string Value)[] metadataEntries)
        {
            AssemblyName name = new AssemblyName("DynamicRevitVersionTest_" + Guid.NewGuid().ToString("N"));
            if (version != null)
            {
                name.Version = version;
            }

            AssemblyBuilder builder = AssemblyBuilder.DefineDynamicAssembly(name, AssemblyBuilderAccess.Run);
            if (metadataEntries != null && metadataEntries.Length > 0)
            {
                ConstructorInfo ctor = typeof(AssemblyMetadataAttribute).GetConstructor(new[] { typeof(string), typeof(string) })!;
                foreach ((string key, string value) in metadataEntries)
                {
                    builder.SetCustomAttribute(new CustomAttributeBuilder(ctor, new object[] { key, value }));
                }
            }

            return builder;
        }
    }
}
