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
    }
}
