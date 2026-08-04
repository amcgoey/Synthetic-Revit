using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Synthetic.Modules.SheetIndex.Models;
using Synthetic.Modules.SheetIndex.Services;

namespace SyntheticTests.Shared.Modules.SheetIndex
{
    [TestFixture]
    public class SheetIndexMatrixBuilderTests
    {
        [Test]
        public void BuildMatrix_OrdersRevisionsChronologically()
        {
            // Arrange
            var revisions = new List<SheetIndexRevisionModel>
            {
                new SheetIndexRevisionModel("rev-3", "Issue 3", "2026-03-01", 3),
                new SheetIndexRevisionModel("rev-1", "Issue 1", "2026-01-01", 1),
                new SheetIndexRevisionModel("rev-2", "Issue 2", "2026-02-01", 2)
            };

            var sheets = new List<SheetIndexSheetModel>
            {
                new SheetIndexSheetModel("s-1", "A101", "Floor Plan", new[] { "rev-1" })
            };

            var builder = new SheetIndexMatrixBuilder();

            // Act
            var matrix = builder.BuildMatrix(sheets, revisions);

            // Assert
            Assert.AreEqual(3, matrix.RevisionHeaders.Count);
            Assert.AreEqual("rev-1", matrix.RevisionHeaders[0].UniqueId);
            Assert.AreEqual("rev-2", matrix.RevisionHeaders[1].UniqueId);
            Assert.AreEqual("rev-3", matrix.RevisionHeaders[2].UniqueId);
        }

        [Test]
        public void BuildMatrix_SortsSheetsAlphanumerically()
        {
            // Arrange
            var revisions = new List<SheetIndexRevisionModel>
            {
                new SheetIndexRevisionModel("rev-1", "Issue 1", "2026-01-01", 1)
            };

            var sheets = new List<SheetIndexSheetModel>
            {
                new SheetIndexSheetModel("s-100", "A100", "Overall Plan"),
                new SheetIndexSheetModel("s-2", "A2", "Site Plan"),
                new SheetIndexSheetModel("s-10", "A10", "Level 1 Plan"),
                new SheetIndexSheetModel("s-1", "A1", "Cover"),
                new SheetIndexSheetModel("s-b1", "B101", "Structural Plan")
            };

            var builder = new SheetIndexMatrixBuilder();

            // Act
            var matrix = builder.BuildMatrix(sheets, revisions);

            // Assert
            Assert.AreEqual(5, matrix.Rows.Count);
            Assert.AreEqual("A1", matrix.Rows[0].SheetNumber);
            Assert.AreEqual("A2", matrix.Rows[1].SheetNumber);
            Assert.AreEqual("A10", matrix.Rows[2].SheetNumber);
            Assert.AreEqual("A100", matrix.Rows[3].SheetNumber);
            Assert.AreEqual("B101", matrix.Rows[4].SheetNumber);
        }

        [Test]
        public void BuildMatrix_PlacesBulletAndBlankCellsCorrectly()
        {
            // Arrange
            var revisions = new List<SheetIndexRevisionModel>
            {
                new SheetIndexRevisionModel("rev-1", "Permit", "2026-01-01", 1),
                new SheetIndexRevisionModel("rev-2", "Bid", "2026-02-01", 2)
            };

            var sheets = new List<SheetIndexSheetModel>
            {
                new SheetIndexSheetModel("s-101", "A101", "Floor Plan", new[] { "rev-1", "rev-2" }),
                new SheetIndexSheetModel("s-102", "A102", "Reflected Ceiling Plan", new[] { "rev-2" }),
                new SheetIndexSheetModel("s-103", "A103", "Roof Plan", new string[0])
            };

            var builder = new SheetIndexMatrixBuilder();

            // Act
            var matrix = builder.BuildMatrix(sheets, revisions);

            // Assert
            Assert.AreEqual(3, matrix.Rows.Count);

            // Row 0: A101 (rev-1, rev-2) -> ["●", "●"]
            Assert.AreEqual("A101", matrix.Rows[0].SheetNumber);
            Assert.AreEqual(SheetIndexMatrix.IssuanceIndicatorSymbol, matrix.Rows[0].Cells[0]);
            Assert.AreEqual(SheetIndexMatrix.IssuanceIndicatorSymbol, matrix.Rows[0].Cells[1]);

            // Row 1: A102 (rev-2 only) -> ["", "●"]
            Assert.AreEqual("A102", matrix.Rows[1].SheetNumber);
            Assert.AreEqual(string.Empty, matrix.Rows[1].Cells[0]);
            Assert.AreEqual(SheetIndexMatrix.IssuanceIndicatorSymbol, matrix.Rows[1].Cells[1]);

            // Row 2: A103 (none) -> ["", ""]
            Assert.AreEqual("A103", matrix.Rows[2].SheetNumber);
            Assert.AreEqual(string.Empty, matrix.Rows[2].Cells[0]);
            Assert.AreEqual(string.Empty, matrix.Rows[2].Cells[1]);
        }

        [Test]
        public void BuildMatrix_HandlesEmptyOrNullInputs()
        {
            var builder = new SheetIndexMatrixBuilder();

            var emptyMatrix = builder.BuildMatrix(null!, null!);
            Assert.IsNotNull(emptyMatrix);
            Assert.AreEqual(0, emptyMatrix.Rows.Count);
            Assert.AreEqual(0, emptyMatrix.RevisionHeaders.Count);
        }

        [Test]
        public void ExportToExcel_CreatesValidFileOnDisk()
        {
            // Arrange
            var revisions = new List<SheetIndexRevisionModel>
            {
                new SheetIndexRevisionModel("rev-1", "Permit Submittal", "2026-01-15", 1)
            };

            var sheets = new List<SheetIndexSheetModel>
            {
                new SheetIndexSheetModel("s-1", "A101", "Floor Plan", new[] { "rev-1" })
            };

            var builder = new SheetIndexMatrixBuilder();
            var matrix = builder.BuildMatrix(sheets, revisions);

            var tempFilePath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"SheetIndexTest_{Guid.NewGuid():N}.xlsx");

            try
            {
                var exporter = new SheetIndexExporterService();

                // Act
                exporter.ExportToExcel(matrix, tempFilePath);

                // Assert
                Assert.IsTrue(System.IO.File.Exists(tempFilePath));
                var fileInfo = new System.IO.FileInfo(tempFilePath);
                Assert.IsTrue(fileInfo.Length > 0);
            }
            finally
            {
                if (System.IO.File.Exists(tempFilePath))
                {
                    System.IO.File.Delete(tempFilePath);
                }
            }
        }
    }
}
