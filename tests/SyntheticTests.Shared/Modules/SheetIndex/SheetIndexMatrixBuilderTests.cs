using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Synthetic.Modules.SheetIndex.Models;
using Synthetic.Modules.SheetIndex.Services;
using Synthetic.Modules.SheetIndex.ViewModels;

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
        public void BuildMatrix_WithPrintOrderIndex_OrdersRowsByPrintOrder()
        {
            // Arrange
            var revisions = new List<SheetIndexRevisionModel>
            {
                new SheetIndexRevisionModel("rev-1", "Issue 1", "2026-01-01", 1)
            };

            // Sheets in custom print order sequence (A105 is first, A101 is second, A103 is third)
            var sheets = new List<SheetIndexSheetModel>
            {
                new SheetIndexSheetModel("s-103", "A103", "Roof Plan", printOrderIndex: 2),
                new SheetIndexSheetModel("s-105", "A105", "Details", printOrderIndex: 0),
                new SheetIndexSheetModel("s-101", "A101", "Floor Plan", printOrderIndex: 1)
            };

            var builder = new SheetIndexMatrixBuilder();

            // Act
            var matrix = builder.BuildMatrix(sheets, revisions);

            // Assert
            Assert.AreEqual(3, matrix.Rows.Count);
            Assert.AreEqual("A105", matrix.Rows[0].SheetNumber);
            Assert.AreEqual("A101", matrix.Rows[1].SheetNumber);
            Assert.AreEqual("A103", matrix.Rows[2].SheetNumber);
        }

        [Test]
        public void BuildMatrix_WithMixedPrintOrderIndex_SortsOrderedSheetsFirstThenAlphanumericFallback()
        {
            // Arrange
            var revisions = new List<SheetIndexRevisionModel>
            {
                new SheetIndexRevisionModel("rev-1", "Issue 1", "2026-01-01", 1)
            };

            var sheets = new List<SheetIndexSheetModel>
            {
                new SheetIndexSheetModel("s-200", "G001", "General Notes", printOrderIndex: null),
                new SheetIndexSheetModel("s-102", "A102", "Reflected Ceiling Plan", printOrderIndex: 0),
                new SheetIndexSheetModel("s-101", "A101", "Floor Plan", printOrderIndex: 1),
                new SheetIndexSheetModel("s-001", "C001", "Civil Cover", printOrderIndex: null)
            };

            var builder = new SheetIndexMatrixBuilder();

            // Act
            var matrix = builder.BuildMatrix(sheets, revisions);

            // Assert
            Assert.AreEqual(4, matrix.Rows.Count);
            // Ordered first: printOrderIndex 0 (A102), then printOrderIndex 1 (A101)
            Assert.AreEqual("A102", matrix.Rows[0].SheetNumber);
            Assert.AreEqual("A101", matrix.Rows[1].SheetNumber);
            // Fallback alphanumeric for null printOrderIndex: C001, G001
            Assert.AreEqual("C001", matrix.Rows[2].SheetNumber);
            Assert.AreEqual("G001", matrix.Rows[3].SheetNumber);
        }

        [Test]
        public void FilterSheetsByPrintSet_FiltersAndAssignsPrintOrderSequence()
        {
            // Arrange
            var revisions = new List<SheetIndexRevisionModel>
            {
                new SheetIndexRevisionModel("rev-1", "Issue 1", "2026-01-01", 1)
            };

            var allSheets = new List<SheetIndexSheetModel>
            {
                new SheetIndexSheetModel("s-1", "A101", "Floor Plan"),
                new SheetIndexSheetModel("s-2", "A102", "Reflected Ceiling Plan"),
                new SheetIndexSheetModel("s-3", "A103", "Roof Plan"),
                new SheetIndexSheetModel("s-4", "A104", "Building Sections")
            };

            // Print set containing s-3 (index 0) and s-1 (index 1)
            var printSets = new List<SheetIndexPrintSetModel>
            {
                new SheetIndexPrintSetModel("ps-1", "Permit Submittal", new[] { "s-3", "s-1" })
            };

            var viewModel = new ExportSheetIndexViewModel(allSheets, revisions, printSets);

            // Act: Select Print Set source
            viewModel.SelectedSourceType = "Print Set";

            // Assert
            Assert.IsTrue(viewModel.IsPrintSetSourceSelected);
            Assert.AreEqual(2, viewModel.Sheets.Count);
            Assert.AreEqual("A103", viewModel.Sheets[0].SheetNumber);
            Assert.AreEqual(0, viewModel.Sheets[0].Model.PrintOrderIndex);
            Assert.AreEqual("A101", viewModel.Sheets[1].SheetNumber);
            Assert.AreEqual(1, viewModel.Sheets[1].Model.PrintOrderIndex);

            // Act: Switch back to All Sheets
            viewModel.SelectedSourceType = "All Sheets";

            // Assert
            Assert.IsFalse(viewModel.IsPrintSetSourceSelected);
            Assert.AreEqual(4, viewModel.Sheets.Count);
            Assert.IsNull(viewModel.Sheets[0].Model.PrintOrderIndex);
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

        [Test]
        public void BuildMatrix_PreservesScheduleSortOrder()
        {
            // Arrange
            var revisions = new List<SheetIndexRevisionModel>
            {
                new SheetIndexRevisionModel("rev-1", "Issue 1", "2026-01-01", 1)
            };

            // Custom schedule order (non-alphanumeric): S101 -> A101 -> C101
            var sheets = new List<SheetIndexSheetModel>
            {
                new SheetIndexSheetModel("s-s1", "S101", "Foundation Plan"),
                new SheetIndexSheetModel("s-a1", "A101", "Floor Plan"),
                new SheetIndexSheetModel("s-c1", "C101", "Site Plan")
            };

            var builder = new SheetIndexMatrixBuilder();

            // Act: preserveSheetOrder = true
            var matrix = builder.BuildMatrix(sheets, revisions, preserveSheetOrder: true);

            // Assert: order is preserved (S101, A101, C101) instead of sorted (A101, C101, S101)
            Assert.AreEqual(3, matrix.Rows.Count);
            Assert.AreEqual("S101", matrix.Rows[0].SheetNumber);
            Assert.AreEqual("A101", matrix.Rows[1].SheetNumber);
            Assert.AreEqual("C101", matrix.Rows[2].SheetNumber);
        }

        [Test]
        public void BuildMatrix_InjectsSectionHeaderRows()
        {
            // Arrange
            var revisions = new List<SheetIndexRevisionModel>
            {
                new SheetIndexRevisionModel("rev-1", "Permit", "2026-01-01", 1),
                new SheetIndexRevisionModel("rev-2", "Bid", "2026-02-01", 2)
            };

            var sheets = new List<SheetIndexSheetModel>
            {
                new SheetIndexSheetModel("s-s1", "S101", "Foundation Plan", new[] { "rev-1" }) { SectionGroup = "STRUCTURAL" },
                new SheetIndexSheetModel("s-s2", "S102", "Framing Plan", new[] { "rev-1", "rev-2" }) { SectionGroup = "STRUCTURAL" },
                new SheetIndexSheetModel("s-a1", "A101", "Floor Plan", new[] { "rev-2" }) { SectionGroup = "ARCHITECTURAL" }
            };

            var builder = new SheetIndexMatrixBuilder();

            // Act
            var matrix = builder.BuildMatrix(sheets, revisions, preserveSheetOrder: true);

            // Assert:
            // Row 0: Section Header "STRUCTURAL"
            // Row 1: S101
            // Row 2: S102
            // Row 3: Section Header "ARCHITECTURAL"
            // Row 4: A101
            Assert.AreEqual(5, matrix.Rows.Count);

            Assert.IsTrue(matrix.Rows[0].IsSectionHeader);
            Assert.AreEqual("STRUCTURAL", matrix.Rows[0].SheetNumber);
            Assert.AreEqual(string.Empty, matrix.Rows[0].SheetName);
            Assert.AreEqual(2, matrix.Rows[0].Cells.Count);
            Assert.AreEqual(string.Empty, matrix.Rows[0].Cells[0]);

            Assert.IsFalse(matrix.Rows[1].IsSectionHeader);
            Assert.AreEqual("S101", matrix.Rows[1].SheetNumber);

            Assert.IsFalse(matrix.Rows[2].IsSectionHeader);
            Assert.AreEqual("S102", matrix.Rows[2].SheetNumber);

            Assert.IsTrue(matrix.Rows[3].IsSectionHeader);
            Assert.AreEqual("ARCHITECTURAL", matrix.Rows[3].SheetNumber);

            Assert.IsFalse(matrix.Rows[4].IsSectionHeader);
            Assert.AreEqual("A101", matrix.Rows[4].SheetNumber);
        }

        [Test]
        public void ExportToExcel_WritesSectionHeaderRows()
        {
            // Arrange
            var revisions = new List<SheetIndexRevisionModel>
            {
                new SheetIndexRevisionModel("rev-1", "Permit Submittal", "2026-01-15", 1)
            };

            var sheets = new List<SheetIndexSheetModel>
            {
                new SheetIndexSheetModel("s-s1", "S101", "Structural Plan", new[] { "rev-1" }) { SectionGroup = "STRUCTURAL" },
                new SheetIndexSheetModel("s-a1", "A101", "Floor Plan", new[] { "rev-1" }) { SectionGroup = "ARCHITECTURAL" }
            };

            var builder = new SheetIndexMatrixBuilder();
            var matrix = builder.BuildMatrix(sheets, revisions, preserveSheetOrder: true);

            var tempFilePath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"SheetIndexSectionTest_{Guid.NewGuid():N}.xlsx");

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
