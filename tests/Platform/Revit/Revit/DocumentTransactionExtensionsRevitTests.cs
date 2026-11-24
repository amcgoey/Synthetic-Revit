using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using NUnit.Framework;
using Synthetic.Platform.Revit;
using SyntheticTests.Fixtures;

namespace SyntheticTests.Platform.Revit
{
    [TestFixture]
    [RevitAdapterTest]
    public class DocumentTransactionExtensionsRevitTests : RevitTemplateRollbackFixture
    {
        [Test]
        public void ExecuteCommittedBatchGroup_AssimilatesTransactionsAndPersistsChanges()
        {
            WithRolledBackDocument(doc =>
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
            });
        }

        [Test]
        public void ExecuteCommittedBatchGroup_RollsBackGroupOnException_AndDoesNotMaskInnerExceptionWhenInnerTransactionOpen()
        {
            WithRolledBackDocument(doc =>
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
            });
        }

        [Test]
        public void ExecuteRolledBackGroup_RollsBackCommittedInnerTransactionsUnconditionally()
        {
            WithRolledBackDocument(doc =>
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
            });
        }

        [Test]
        public void ExecuteCommittedBatchGroup_FuncBoolTrue_AssimilatesAndReturnsTrue()
        {
            WithRolledBackDocument(doc =>
            {
                Level level = FindFirstLevel(doc);

                bool assimilated = doc.ExecuteCommittedBatchGroup("Predicate True Batch", () =>
                {
                    doc.ExecuteTransaction("Wall 1", FailurePipelines.ObserveOnly(), () =>
                    {
                        Wall.Create(doc, Line.CreateBound(XYZ.Zero, new XYZ(10, 0, 0)), level.Id, false);
                    });

                    return true;
                });

                Assert.IsTrue(assimilated, "Returning true must assimilate the TransactionGroup and return true.");
                Assert.AreEqual(1, new FilteredElementCollector(doc).OfClass(typeof(Wall)).GetElementCount());
                Assert.IsFalse(doc.IsModifiable);
            });
        }

        [Test]
        public void ExecuteCommittedBatchGroup_FuncDocumentBoolTrue_AssimilatesAndReturnsTrue()
        {
            WithRolledBackDocument(doc =>
            {
                Level level = FindFirstLevel(doc);

                bool assimilated = doc.ExecuteCommittedBatchGroup("Predicate Document True Batch", d =>
                {
                    Assert.AreSame(doc, d);
                    d.ExecuteTransaction("Wall 1", FailurePipelines.ObserveOnly(), () =>
                    {
                        Wall.Create(d, Line.CreateBound(XYZ.Zero, new XYZ(10, 0, 0)), level.Id, false);
                    });

                    return true;
                });

                Assert.IsTrue(assimilated, "Func<Document, bool> returning true must assimilate and return true.");
                Assert.AreEqual(1, new FilteredElementCollector(doc).OfClass(typeof(Wall)).GetElementCount());
                Assert.IsFalse(doc.IsModifiable);
            });
        }

        [Test]
        public void ExecuteCommittedBatchGroup_FuncBoolFalse_RollsBackChangesDirectlyAndReturnsFalse()
        {
            WithRolledBackDocument(doc =>
            {
                Level level = FindFirstLevel(doc);

                bool assimilated = doc.ExecuteCommittedBatchGroup("Predicate False Batch", () =>
                {
                    doc.ExecuteTransaction("Wall 1", FailurePipelines.ObserveOnly(), () =>
                    {
                        Wall.Create(doc, Line.CreateBound(XYZ.Zero, new XYZ(10, 0, 0)), level.Id, false);
                    });

                    return false;
                });

                Assert.IsFalse(assimilated, "Returning false must roll back the TransactionGroup and return false.");
                Assert.AreEqual(0, new FilteredElementCollector(doc).OfClass(typeof(Wall)).GetElementCount(), "Rolled-back predicate group must undo inner committed transactions.");
                Assert.IsFalse(doc.IsModifiable);
            });
        }

        [Test]
        public void ExecuteCommittedBatchGroup_FuncDocumentBoolFalse_RollsBackChangesDirectlyAndReturnsFalse()
        {
            WithRolledBackDocument(doc =>
            {
                Level level = FindFirstLevel(doc);

                bool assimilated = doc.ExecuteCommittedBatchGroup("Predicate Document False Batch", d =>
                {
                    Assert.AreSame(doc, d);
                    d.ExecuteTransaction("Wall 1", FailurePipelines.ObserveOnly(), () =>
                    {
                        Wall.Create(d, Line.CreateBound(XYZ.Zero, new XYZ(10, 0, 0)), level.Id, false);
                    });

                    return false;
                });

                Assert.IsFalse(assimilated, "Func<Document, bool> returning false must roll back and return false.");
                Assert.AreEqual(0, new FilteredElementCollector(doc).OfClass(typeof(Wall)).GetElementCount());
                Assert.IsFalse(doc.IsModifiable);
            });
        }

        [Test]
        public void ExecuteCommittedBatchGroup_FuncBoolRollsBackOnException_WithoutMaskingInnerTransactionUnwind()
        {
            WithRolledBackDocument(doc =>
            {
                Level level = FindFirstLevel(doc);

                var thrown = Assert.Throws<ApplicationException>(() =>
                {
                    doc.ExecuteCommittedBatchGroup("Predicate Inner Tx Exception Batch", () =>
                    {
                        doc.ExecuteTransaction("Committed Before Failure", FailurePipelines.ObserveOnly(), () =>
                        {
                            Wall.Create(doc, Line.CreateBound(XYZ.Zero, new XYZ(10, 0, 0)), level.Id, false);
                        });

                        using (var innerTx = doc.CreateTransaction("Inner Failing Tx", FailurePipelines.ObserveOnly()))
                        {
                            innerTx.Start();
                            Wall.Create(doc, Line.CreateBound(new XYZ(0, 10, 0), new XYZ(10, 10, 0)), level.Id, false);
                            throw new ApplicationException("Original predicate inner exception");
                        }
                    });
                });

                Assert.AreEqual("Original predicate inner exception", thrown!.Message);
                Assert.AreEqual(0, new FilteredElementCollector(doc).OfClass(typeof(Wall)).GetElementCount());
                Assert.IsFalse(doc.IsModifiable);
            });
        }

        [Test]
        public void TransactionGroups_RejectModifiableDocumentOnEntry()
        {
            WithRolledBackDocument(doc =>
            {
                Assert.Throws<ArgumentNullException>(() => doc.ExecuteCommittedBatchGroup("Null Func", (Func<bool>)null!));
                Assert.Throws<ArgumentNullException>(() => doc.ExecuteCommittedBatchGroup("Null Doc Func", (Func<Document, bool>)null!));
                Assert.Throws<ArgumentException>(() => doc.ExecuteCommittedBatchGroup("   ", () => false));

                using (var activeTx = new Transaction(doc, "Outer Open Transaction"))
                {
                    activeTx.Start();
                    try
                    {
                        Assert.IsTrue(doc.IsModifiable);

                        Assert.Throws<InvalidOperationException>(() =>
                            doc.ExecuteCommittedBatchGroup("Nested Committed Group", () => { }));

                        Assert.Throws<InvalidOperationException>(() =>
                            doc.ExecuteCommittedBatchGroup("Nested Predicate Group", () => true));

                        Assert.Throws<InvalidOperationException>(() =>
                            doc.ExecuteCommittedBatchGroup("Nested Doc Predicate Group", _ => false));

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
            });
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
