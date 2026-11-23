using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using NUnit.Framework;
using Synthetic.Platform.Revit;
using SyntheticTests.Helpers;

namespace SyntheticTests.Platform.Revit
{
    [TestFixture]
    [RevitAdapterTest]
    public class DocumentTransactionExtensionsRevitTests
    {
        private UIApplication? _uiapp;

        [OneTimeSetUp]
        public void OneTimeSetup(UIApplication uiapp)
        {
            _uiapp = uiapp;
            TestWorkerJournalMarker.EnsureWritten(uiapp);
        }

        [Test]
        public void ExecuteCommittedBatchGroup_AssimilatesTransactionsAndPersistsChanges()
        {
            Assert.IsNotNull(_uiapp);
            Document doc = _uiapp!.Application.NewProjectDocument(UnitSystem.Metric);
            try
            {
                Level level = FindFirstLevel(doc);

                doc.ExecuteCommittedBatchGroup("Create Wall Batch", () =>
                {
                    doc.ExecuteTransaction("Wall 1", FailurePipelines.ObserveOnly(), () =>
                    {
                        Wall.Create(doc, Line.CreateBound(XYZ.Zero, new XYZ(10, 0, 0)), level.Id, false);
                    });

                    doc.ExecuteTransaction("Wall 2", FailurePipelines.ObserveOnly(), () =>
                    {
                        Wall.Create(doc, Line.CreateBound(new XYZ(0, 10, 0), new XYZ(10, 10, 0)), level.Id, false);
                    });
                });

                int wallCount = new FilteredElementCollector(doc).OfClass(typeof(Wall)).GetElementCount();
                Assert.AreEqual(2, wallCount, "Assimilated batch group must persist both walls.");
                Assert.IsFalse(doc.IsModifiable, "Document must not remain modifiable after group assimilation.");
            }
            finally
            {
                doc.Close(false);
            }
        }

        [Test]
        public void ExecuteCommittedBatchGroup_RollsBackGroupOnException_AndDoesNotMaskInnerExceptionWhenInnerTransactionOpen()
        {
            Assert.IsNotNull(_uiapp);
            Document doc = _uiapp!.Application.NewProjectDocument(UnitSystem.Metric);
            try
            {
                Level level = FindFirstLevel(doc);

                // Part 1: Clean inner transaction followed by exception rolls back earlier committed work
                Assert.Throws<InvalidOperationException>(() =>
                {
                    doc.ExecuteCommittedBatchGroup("Failing Wall Batch", () =>
                    {
                        doc.ExecuteTransaction("Wall 1", FailurePipelines.ObserveOnly(), () =>
                        {
                            Wall.Create(doc, Line.CreateBound(XYZ.Zero, new XYZ(10, 0, 0)), level.Id, false);
                        });

                        throw new InvalidOperationException("Simulated batch failure");
                    });
                });

                int wallCount = new FilteredElementCollector(doc).OfClass(typeof(Wall)).GetElementCount();
                Assert.AreEqual(0, wallCount, "Rolled-back batch group must undo earlier inner transactions.");

                // Part 2: Inner Transaction inside using block throws ApplicationException;
                // inner tx rolls back cleanly on unwind and TransactionGroup rolls back without masking ApplicationException.
                var thrown = Assert.Throws<ApplicationException>(() =>
                {
                    doc.ExecuteCommittedBatchGroup("Inner Tx Exception Batch", () =>
                    {
                        using (var innerTx = doc.CreateTransaction("Inner Failing Tx", FailurePipelines.ObserveOnly()))
                        {
                            innerTx.Start();
                            Wall.Create(doc, Line.CreateBound(XYZ.Zero, new XYZ(10, 0, 0)), level.Id, false);
                            throw new ApplicationException("Original inner exception");
                        }
                    });
                });

                Assert.AreEqual("Original inner exception", thrown!.Message);
                Assert.AreEqual(0, new FilteredElementCollector(doc).OfClass(typeof(Wall)).GetElementCount());
                Assert.IsFalse(doc.IsModifiable);
            }
            finally
            {
                doc.Close(false);
            }
        }

        [Test]
        public void ExecuteRolledBackGroup_RollsBackCommittedInnerTransactionsUnconditionally()
        {
            Assert.IsNotNull(_uiapp);
            Document doc = _uiapp!.Application.NewProjectDocument(UnitSystem.Metric);
            try
            {
                Level level = FindFirstLevel(doc);
                int countInsideGroup = 0;

                doc.ExecuteRolledBackGroup("Ephemeral Wall Batch", () =>
                {
                    doc.ExecuteTransaction("Create Ephemeral Wall", FailurePipelines.ObserveOnly(), () =>
                    {
                        Wall.Create(doc, Line.CreateBound(XYZ.Zero, new XYZ(10, 0, 0)), level.Id, false);
                    });

                    countInsideGroup = new FilteredElementCollector(doc).OfClass(typeof(Wall)).GetElementCount();
                });

                int countAfterGroup = new FilteredElementCollector(doc).OfClass(typeof(Wall)).GetElementCount();
                Assert.AreEqual(1, countInsideGroup, "Wall should exist while inside the TransactionGroup.");
                Assert.AreEqual(0, countAfterGroup, "ExecuteRolledBackGroup must roll back all changes unconditionally.");
                Assert.IsFalse(doc.IsModifiable, "Document must not remain modifiable after group rollback.");
            }
            finally
            {
                doc.Close(false);
            }
        }

        [Test]
        public void TransactionGroups_RejectModifiableDocumentOnEntry()
        {
            Assert.IsNotNull(_uiapp);
            Document doc = _uiapp!.Application.NewProjectDocument(UnitSystem.Metric);
            try
            {
                using (var activeTx = new Transaction(doc, "Outer Open Transaction"))
                {
                    activeTx.Start();
                    try
                    {
                        Assert.IsTrue(doc.IsModifiable);

                        Assert.Throws<InvalidOperationException>(() =>
                            doc.ExecuteCommittedBatchGroup("Nested Committed Group", () => { }));

                        Assert.Throws<InvalidOperationException>(() =>
                            doc.ExecuteRolledBackGroup("Nested RolledBack Group", () => { }));
                    }
                    finally
                    {
                        if (activeTx.GetStatus() == TransactionStatus.Started)
                        {
                            activeTx.RollBack();
                        }
                    }
                }
            }
            finally
            {
                doc.Close(false);
            }
        }

        private static Level FindFirstLevel(Document doc)
        {
            Level? level = new FilteredElementCollector(doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .FirstOrDefault();
            Assert.IsNotNull(level, "Metric project template must contain at least one Level.");
            return level!;
        }
    }
}
