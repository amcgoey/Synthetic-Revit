using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using NUnit.Framework;
using Synthetic.Features.SheetIndex.Models;
using Synthetic.Features.SheetIndex.Services;
using SyntheticTests.Fixtures;

namespace SyntheticTests.Features.SheetIndex
{
    [TestFixture]
    [UnitTest]
    public class SheetIndexExporterTests
    {
        private static readonly XNamespace SpreadsheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        private static readonly XNamespace RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        private static readonly XNamespace PackageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
        private static readonly XNamespace ContentTypesNs = "http://schemas.openxmlformats.org/package/2006/content-types";
        private static readonly XName XmlSpaceAttr = XNamespace.Xml + "space";

        [Test]
        public void Export_ToDisk_ProducesValidOpenXmlPackageStructureStylesAndOutcomeCounts()
        {
            var revisions = new List<SheetIndexRevisionModel>
            {
                new SheetIndexRevisionModel("rev-2", "Construction Set", "2026-02-01", 2),
                new SheetIndexRevisionModel("rev-1", "Permit Set", "2026-01-15", 1)
            };

            var sheets = new List<SheetIndexSheetModel>
            {
                new SheetIndexSheetModel("s-10", "A10", "Level 1 Plan", new[] { "rev-1", "rev-2" }),
                new SheetIndexSheetModel("s-2", "A2", "Site Plan", new[] { "rev-1" }),
                new SheetIndexSheetModel("s-100", "A100", "Overall Plan", Array.Empty<string>())
            };

            string tempDir = Path.Combine(Path.GetTempPath(), "SheetIndexExporterTests_" + Guid.NewGuid().ToString("N"));
            string outputPath = Path.Combine(tempDir, "nested", "Dot Sheet Index.xlsx");

            try
            {
                var exporter = new SheetIndexExporter();
                SheetIndexExportOutcome outcome = exporter.Export(sheets, revisions, outputPath);

                Assert.IsTrue(outcome.Succeeded, outcome.ErrorMessage);
                Assert.AreEqual(outputPath, outcome.OutputFilePath);
                Assert.AreEqual(3, outcome.ExportedSheetCount);
                Assert.AreEqual(2, outcome.ExportedRevisionColumnCount);
                Assert.AreEqual(0, outcome.SectionHeaderCount);
                Assert.IsEmpty(outcome.Warnings);
                Assert.IsNull(outcome.ErrorMessage);
                Assert.IsTrue(File.Exists(outputPath));

                using (var zip = ZipFile.OpenRead(outputPath))
                {
                    // 1. Verify expected package entries & UTF-8 without BOM across every entry
                    string[] expectedEntries =
                    {
                        "[Content_Types].xml",
                        "_rels/.rels",
                        "xl/workbook.xml",
                        "xl/_rels/workbook.xml.rels",
                        "xl/styles.xml",
                        "xl/worksheets/sheet1.xml"
                    };

                    CollectionAssert.AreEquivalent(
                        expectedEntries,
                        zip.Entries.Select(e => e.FullName).ToArray());

                    foreach (ZipArchiveEntry entry in zip.Entries)
                    {
                        byte[] rawBytes = ReadEntryBytes(entry);
                        Assert.Greater(rawBytes.Length, 5, $"Entry '{entry.FullName}' should not be empty.");
                        bool hasBom = rawBytes.Length >= 3 &&
                                      rawBytes[0] == 0xEF &&
                                      rawBytes[1] == 0xBB &&
                                      rawBytes[2] == 0xBF;
                        Assert.IsFalse(hasBom, $"Entry '{entry.FullName}' must be UTF-8 without BOM.");
                        Assert.AreEqual("<?xml", Encoding.UTF8.GetString(rawBytes, 0, 5), $"Entry '{entry.FullName}' must start with XML declaration.");
                    }

                    // 2. Verify [Content_Types].xml
                    XDocument contentTypes = LoadXmlEntry(zip, "[Content_Types].xml");
                    var defaults = contentTypes.Root!.Elements(ContentTypesNs + "Default")
                        .ToDictionary(e => (string)e.Attribute("Extension")!, e => (string)e.Attribute("ContentType")!);
                    Assert.AreEqual("application/vnd.openxmlformats-package.relationships+xml", defaults["rels"]);
                    Assert.AreEqual("application/xml", defaults["xml"]);

                    var overrides = contentTypes.Root.Elements(ContentTypesNs + "Override")
                        .ToDictionary(e => (string)e.Attribute("PartName")!, e => (string)e.Attribute("ContentType")!);
                    Assert.AreEqual("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml", overrides["/xl/workbook.xml"]);
                    Assert.AreEqual("application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml", overrides["/xl/worksheets/sheet1.xml"]);
                    Assert.AreEqual("application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml", overrides["/xl/styles.xml"]);

                    // 3. Verify root relationships & workbook relationships
                    XDocument rootRels = LoadXmlEntry(zip, "_rels/.rels");
                    XElement? officeDocRel = rootRels.Root!.Elements(PackageRelNs + "Relationship")
                        .FirstOrDefault(r => (string?)r.Attribute("Id") == "rId1");
                    Assert.IsNotNull(officeDocRel);
                    Assert.AreEqual("http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument", (string?)officeDocRel!.Attribute("Type"));
                    Assert.AreEqual("xl/workbook.xml", (string?)officeDocRel.Attribute("Target"));

                    XDocument workbook = LoadXmlEntry(zip, "xl/workbook.xml");
                    XElement? sheetElem = workbook.Root!.Element(SpreadsheetNs + "sheets")?.Element(SpreadsheetNs + "sheet");
                    Assert.IsNotNull(sheetElem);
                    Assert.AreEqual("Sheet Index", (string?)sheetElem!.Attribute("name"));
                    Assert.AreEqual("1", (string?)sheetElem.Attribute("sheetId"));
                    Assert.AreEqual("rId1", (string?)sheetElem.Attribute(RelNs + "id"));

                    XDocument workbookRels = LoadXmlEntry(zip, "xl/_rels/workbook.xml.rels");
                    var wbRels = workbookRels.Root!.Elements(PackageRelNs + "Relationship")
                        .ToDictionary(r => (string)r.Attribute("Id")!, r => (Target: (string)r.Attribute("Target")!, Type: (string)r.Attribute("Type")!));
                    Assert.AreEqual("worksheets/sheet1.xml", wbRels["rId1"].Target);
                    Assert.AreEqual("http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet", wbRels["rId1"].Type);
                    Assert.AreEqual("styles.xml", wbRels["rId2"].Target);
                    Assert.AreEqual("http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles", wbRels["rId2"].Type);

                    // 4. Verify xl/styles.xml (fonts, fills, borders, textRotation="90")
                    XDocument styles = LoadXmlEntry(zip, "xl/styles.xml");
                    List<XElement> fonts = styles.Root!.Element(SpreadsheetNs + "fonts")!.Elements(SpreadsheetNs + "font").ToList();
                    Assert.AreEqual(2, fonts.Count);
                    Assert.IsNull(fonts[0].Element(SpreadsheetNs + "b"));
                    Assert.IsNotNull(fonts[1].Element(SpreadsheetNs + "b"));
                    Assert.AreEqual("Arial", (string?)fonts[0].Element(SpreadsheetNs + "name")!.Attribute("val"));

                    List<XElement> fills = styles.Root.Element(SpreadsheetNs + "fills")!.Elements(SpreadsheetNs + "fill").ToList();
                    Assert.AreEqual(4, fills.Count);
                    Assert.AreEqual("none", (string?)fills[0].Element(SpreadsheetNs + "patternFill")!.Attribute("patternType"));
                    Assert.AreEqual("gray125", (string?)fills[1].Element(SpreadsheetNs + "patternFill")!.Attribute("patternType"));
                    Assert.AreEqual("FFE0E0E0", (string?)fills[2].Element(SpreadsheetNs + "patternFill")!.Element(SpreadsheetNs + "fgColor")!.Attribute("rgb"));
                    Assert.AreEqual("FFF8F9FA", (string?)fills[3].Element(SpreadsheetNs + "patternFill")!.Element(SpreadsheetNs + "fgColor")!.Attribute("rgb"));

                    List<XElement> borders = styles.Root.Element(SpreadsheetNs + "borders")!.Elements(SpreadsheetNs + "border").ToList();
                    Assert.AreEqual(2, borders.Count);
                    XElement thinBorder = borders[1];
                    foreach (string side in new[] { "left", "right", "top", "bottom" })
                    {
                        XElement? sideElem = thinBorder.Element(SpreadsheetNs + side);
                        Assert.IsNotNull(sideElem);
                        Assert.AreEqual("thin", (string?)sideElem!.Attribute("style"));
                        Assert.AreEqual("FFD3D3D3", (string?)sideElem.Element(SpreadsheetNs + "color")!.Attribute("rgb"));
                    }

                    List<XElement> cellXfs = styles.Root.Element(SpreadsheetNs + "cellXfs")!.Elements(SpreadsheetNs + "xf").ToList();
                    Assert.AreEqual(8, cellXfs.Count);
                    XElement revHeaderAlignment = cellXfs[2].Element(SpreadsheetNs + "alignment")!;
                    Assert.AreEqual("90", (string?)revHeaderAlignment.Attribute("textRotation"));
                    Assert.AreEqual("center", (string?)revHeaderAlignment.Attribute("horizontal"));
                    Assert.AreEqual("1", (string?)cellXfs[2].Attribute("fontId"));
                    Assert.AreEqual("1", (string?)cellXfs[2].Attribute("borderId"));

                    Assert.AreEqual("2", (string?)cellXfs[5].Attribute("fillId")); // Section Header fill
                    Assert.AreEqual("3", (string?)cellXfs[6].Attribute("fillId")); // Zebra left fill
                    Assert.AreEqual("3", (string?)cellXfs[7].Attribute("fillId")); // Zebra center fill

                    // 5. Verify sheet1.xml dynamic cols, cell coordinates, chronological revision order, alphanumeric sort, zebra striping, and "●"
                    XDocument sheet1 = LoadXmlEntry(zip, "xl/worksheets/sheet1.xml");
                    List<XElement> cols = sheet1.Root!.Element(SpreadsheetNs + "cols")!.Elements(SpreadsheetNs + "col").ToList();
                    Assert.AreEqual(3, cols.Count);
                    Assert.AreEqual("1", (string?)cols[0].Attribute("min"));
                    Assert.AreEqual("1", (string?)cols[0].Attribute("max"));
                    Assert.AreEqual("2", (string?)cols[1].Attribute("min"));
                    Assert.AreEqual("2", (string?)cols[1].Attribute("max"));
                    Assert.AreEqual("3", (string?)cols[2].Attribute("min"));
                    Assert.AreEqual("4", (string?)cols[2].Attribute("max"));

                    var cells = ReadCellMap(sheet1);
                    Assert.AreEqual(("SHEET NUMBER", "1"), cells["A1"]);
                    Assert.AreEqual(("SHEET NAME", "1"), cells["B1"]);
                    Assert.AreEqual(("Permit Set - 2026-01-15", "2"), cells["C1"]);
                    Assert.AreEqual(("Construction Set - 2026-02-01", "2"), cells["D1"]);

                    // Row 2: A2 (data row 0 -> non-zebra styles 3/4), rev-1 only
                    Assert.AreEqual(("A2", "3"), cells["A2"]);
                    Assert.AreEqual(("Site Plan", "3"), cells["B2"]);
                    Assert.AreEqual((SheetIndexMatrix.IssuanceIndicatorSymbol, "4"), cells["C2"]);
                    Assert.AreEqual((string.Empty, "4"), cells["D2"]);

                    // Row 3: A10 (data row 1 -> zebra styles 6/7), rev-1 & rev-2
                    Assert.AreEqual(("A10", "6"), cells["A3"]);
                    Assert.AreEqual(("Level 1 Plan", "6"), cells["B3"]);
                    Assert.AreEqual((SheetIndexMatrix.IssuanceIndicatorSymbol, "7"), cells["C3"]);
                    Assert.AreEqual((SheetIndexMatrix.IssuanceIndicatorSymbol, "7"), cells["D3"]);

                    // Row 4: A100 (data row 2 -> non-zebra styles 3/4), no revisions
                    Assert.AreEqual(("A100", "3"), cells["A4"]);
                    Assert.AreEqual(("Overall Plan", "3"), cells["B4"]);
                    Assert.AreEqual((string.Empty, "4"), cells["C4"]);
                    Assert.AreEqual((string.Empty, "4"), cells["D4"]);
                }
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, recursive: true);
                }
            }
        }

        [Test]
        public void Export_ResetsCurrentSectionGroup_WhenUngroupedSheetsFollowGroupedSheets_AndPreservesZebraParity()
        {
            var revisions = new List<SheetIndexRevisionModel>
            {
                new SheetIndexRevisionModel("rev-1", "Permit", "2026-01-01", 1),
                new SheetIndexRevisionModel("rev-2", "Bid", "2026-02-01", 2)
            };

            var sheets = new List<SheetIndexSheetModel>
            {
                new SheetIndexSheetModel("s-1", "S101", "Foundation Plan", new[] { "rev-1" }) { SectionGroup = "STRUCTURAL" },
                new SheetIndexSheetModel("s-2", "G001", "Cover Sheet", new[] { "rev-1", "rev-2" }) { SectionGroup = null },
                new SheetIndexSheetModel("s-3", "S102", "Framing Plan", new[] { "rev-2" }) { SectionGroup = "STRUCTURAL" },
                new SheetIndexSheetModel("s-4", "A101", "Floor Plan", new[] { "rev-1" }) { SectionGroup = "ARCHITECTURAL" }
            };

            using var ms = new MemoryStream();
            var exporter = new SheetIndexExporter();
            SheetIndexExportOutcome outcome = exporter.Export(sheets, revisions, ms, preserveSheetOrder: true);

            Assert.IsTrue(outcome.Succeeded, outcome.ErrorMessage);
            Assert.AreEqual(string.Empty, outcome.OutputFilePath);
            Assert.AreEqual(4, outcome.ExportedSheetCount);
            Assert.AreEqual(2, outcome.ExportedRevisionColumnCount);
            Assert.AreEqual(3, outcome.SectionHeaderCount);
            Assert.IsTrue(ms.CanRead, "Export to stream must leave stream open for caller inspection.");

            ms.Position = 0;
            using var zip = new ZipArchive(ms, ZipArchiveMode.Read, leaveOpen: true);
            XDocument sheet1 = LoadXmlEntry(zip, "xl/worksheets/sheet1.xml");
            var cells = ReadCellMap(sheet1);

            // Row 2: Section Header "STRUCTURAL" (all columns styled "5")
            Assert.AreEqual(("STRUCTURAL", "5"), cells["A2"]);
            Assert.AreEqual((string.Empty, "5"), cells["B2"]);
            Assert.AreEqual((string.Empty, "5"), cells["C2"]);
            Assert.AreEqual((string.Empty, "5"), cells["D2"]);

            // Row 3: S101 (data row 0 -> styles 3/4)
            Assert.AreEqual(("S101", "3"), cells["A3"]);
            Assert.AreEqual((SheetIndexMatrix.IssuanceIndicatorSymbol, "4"), cells["C3"]);
            Assert.AreEqual((string.Empty, "4"), cells["D3"]);

            // Row 4: G001 (ungrouped, data row 1 -> zebra styles 6/7)
            Assert.AreEqual(("G001", "6"), cells["A4"]);
            Assert.AreEqual((SheetIndexMatrix.IssuanceIndicatorSymbol, "7"), cells["C4"]);
            Assert.AreEqual((SheetIndexMatrix.IssuanceIndicatorSymbol, "7"), cells["D4"]);

            // Row 5: Section Header "STRUCTURAL" MUST be re-emitted after ungrouped G001 reset!
            Assert.AreEqual(("STRUCTURAL", "5"), cells["A5"]);
            Assert.AreEqual((string.Empty, "5"), cells["B5"]);
            Assert.AreEqual((string.Empty, "5"), cells["C5"]);
            Assert.AreEqual((string.Empty, "5"), cells["D5"]);

            // Row 6: S102 (data row 2 -> non-zebra styles 3/4; section header does not flip zebra parity)
            Assert.AreEqual(("S102", "3"), cells["A6"]);
            Assert.AreEqual((string.Empty, "4"), cells["C6"]);
            Assert.AreEqual((SheetIndexMatrix.IssuanceIndicatorSymbol, "4"), cells["D6"]);

            // Row 7: Section Header "ARCHITECTURAL"
            Assert.AreEqual(("ARCHITECTURAL", "5"), cells["A7"]);
            Assert.AreEqual((string.Empty, "5"), cells["C7"]);

            // Row 8: A101 (data row 3 -> zebra styles 6/7)
            Assert.AreEqual(("A101", "6"), cells["A8"]);
            Assert.AreEqual((SheetIndexMatrix.IssuanceIndicatorSymbol, "7"), cells["C8"]);
            Assert.AreEqual((string.Empty, "7"), cells["D8"]);
        }

        [Test]
        public void Export_StripsIllegalXml10ControlCharacters_EscapesXml_AndEmitsXmlSpacePreserve()
        {
            var illegalCharsBuilder = new StringBuilder();
            for (int code = 0x00; code <= 0x1F; code++)
            {
                if (code == 0x09 || code == 0x0A || code == 0x0D)
                {
                    continue;
                }
                illegalCharsBuilder.Append((char)code);
            }

            string illegalChars = illegalCharsBuilder.ToString();
            var revisions = new List<SheetIndexRevisionModel>
            {
                new SheetIndexRevisionModel("rev-1", $"Bid & Award{illegalChars}", "2026-03-01", 1)
            };

            var sheets = new List<SheetIndexSheetModel>
            {
                new SheetIndexSheetModel(
                    "s-1",
                    $"  A101{illegalChars} ",
                    $"  Floor <Plan> & \"Notes\" '1'\tLine1\nLine2{illegalChars}  ",
                    new[] { "rev-1" })
                {
                    SectionGroup = $"  ARCH{illegalChars}  "
                }
            };

            using var ms = new MemoryStream();
            var exporter = new SheetIndexExporter();
            SheetIndexExportOutcome outcome = exporter.Export(sheets, revisions, ms, preserveSheetOrder: true);

            Assert.IsTrue(outcome.Succeeded, outcome.ErrorMessage);
            ms.Position = 0;

            using var zip = new ZipArchive(ms, ZipArchiveMode.Read, leaveOpen: true);
            XDocument sheet1 = LoadXmlEntry(zip, "xl/worksheets/sheet1.xml");

            // Every <t> element must carry xml:space="preserve"
            List<XElement> textElements = sheet1.Descendants(SpreadsheetNs + "t").ToList();
            Assert.IsNotEmpty(textElements);
            foreach (XElement textElem in textElements)
            {
                Assert.AreEqual("preserve", (string?)textElem.Attribute(XmlSpaceAttr), $"Missing xml:space=\"preserve\" on <t>: {textElem}");
            }

            var cells = ReadCellMap(sheet1);
            Assert.AreEqual("Bid & Award - 2026-03-01", cells["C1"].Value);
            Assert.AreEqual("  ARCH  ", cells["A2"].Value);
            Assert.AreEqual("  A101 ", cells["A3"].Value);
            Assert.AreEqual("  Floor <Plan> & \"Notes\" '1'\tLine1\nLine2  ", cells["B3"].Value);
            Assert.AreEqual(SheetIndexMatrix.IssuanceIndicatorSymbol, cells["C3"].Value);
        }

        [Test]
        public void Export_ComputesDynamicColumnMax_ForZeroAndOverTwoHundredRevisions()
        {
            var sheets = new List<SheetIndexSheetModel>
            {
                new SheetIndexSheetModel("s-1", "A101", "Floor Plan")
            };

            var exporter = new SheetIndexExporter();

            // Case 1: 0 revisions -> omit column 3 range completely (never <col min="3" max="200"/> or invalid max < min)
            using (var zeroRevStream = new MemoryStream())
            {
                SheetIndexExportOutcome zeroOutcome = exporter.Export(sheets, Array.Empty<SheetIndexRevisionModel>(), zeroRevStream);
                Assert.IsTrue(zeroOutcome.Succeeded);
                Assert.AreEqual(0, zeroOutcome.ExportedRevisionColumnCount);
                Assert.IsNotEmpty(zeroOutcome.Warnings);

                zeroRevStream.Position = 0;
                using var zip = new ZipArchive(zeroRevStream, ZipArchiveMode.Read);
                XDocument sheet1 = LoadXmlEntry(zip, "xl/worksheets/sheet1.xml");
                List<XElement> cols = sheet1.Root!.Element(SpreadsheetNs + "cols")!.Elements(SpreadsheetNs + "col").ToList();
                Assert.AreEqual(2, cols.Count);
                Assert.AreEqual("1", (string?)cols[0].Attribute("max"));
                Assert.AreEqual("2", (string?)cols[1].Attribute("max"));
            }

            // Case 2: 205 revisions -> <col min="3" max="207"/> (exceeding legacy hardcoded 200) and multi-letter column GY1
            var manyRevisions = Enumerable.Range(1, 205)
                .Select(i => new SheetIndexRevisionModel($"rev-{i}", $"Rev {i}", "2026-01-01", i))
                .ToList();
            sheets[0].RevisionIds.Add("rev-205");

            using (var manyRevStream = new MemoryStream())
            {
                SheetIndexExportOutcome manyOutcome = exporter.Export(sheets, manyRevisions, manyRevStream);
                Assert.IsTrue(manyOutcome.Succeeded);
                Assert.AreEqual(205, manyOutcome.ExportedRevisionColumnCount);
                Assert.IsEmpty(manyOutcome.Warnings);

                manyRevStream.Position = 0;
                using var zip = new ZipArchive(manyRevStream, ZipArchiveMode.Read);
                XDocument sheet1 = LoadXmlEntry(zip, "xl/worksheets/sheet1.xml");
                List<XElement> cols = sheet1.Root!.Element(SpreadsheetNs + "cols")!.Elements(SpreadsheetNs + "col").ToList();
                Assert.AreEqual(3, cols.Count);
                Assert.AreEqual("3", (string?)cols[2].Attribute("min"));
                Assert.AreEqual("207", (string?)cols[2].Attribute("max"));

                var cells = ReadCellMap(sheet1);
                // Column 207 = 7*26 + 25 -> GY
                Assert.IsTrue(cells.ContainsKey("GY1"));
                Assert.AreEqual("Rev 205 - 2026-01-01", cells["GY1"].Value);
                Assert.AreEqual(SheetIndexMatrix.IssuanceIndicatorSymbol, cells["GY2"].Value);
            }
        }

        [Test]
        public void Export_OrdersSheetsByPrintOrderIndexThenAlphanumericFallback_UnlessPreserveOrderIsTrue()
        {
            var revisions = new List<SheetIndexRevisionModel>
            {
                new SheetIndexRevisionModel("rev-1", "Issue 1", "2026-01-01", 1)
            };

            var mixedSheets = new List<SheetIndexSheetModel>
            {
                new SheetIndexSheetModel("s-200", "G001", "General Notes", printOrderIndex: null),
                new SheetIndexSheetModel("s-102", "A102", "Reflected Ceiling Plan", printOrderIndex: 0),
                new SheetIndexSheetModel("s-10", "A10", "Level 10", printOrderIndex: null),
                new SheetIndexSheetModel("s-2", "A2", "Level 2", printOrderIndex: null),
                new SheetIndexSheetModel("s-101", "A101", "Floor Plan", printOrderIndex: 1)
            };

            var exporter = new SheetIndexExporter();

            // Default: printOrderIndex ascending, then alphanumeric SheetNumber
            using (var sortedStream = new MemoryStream())
            {
                SheetIndexExportOutcome sortedOutcome = exporter.Export(mixedSheets, revisions, sortedStream, preserveSheetOrder: false);
                Assert.IsTrue(sortedOutcome.Succeeded);
                sortedStream.Position = 0;

                using var zip = new ZipArchive(sortedStream, ZipArchiveMode.Read);
                XDocument sheet1 = LoadXmlEntry(zip, "xl/worksheets/sheet1.xml");
                var cells = ReadCellMap(sheet1);
                CollectionAssert.AreEqual(
                    new[] { "A102", "A101", "A2", "A10", "G001" },
                    new[] { cells["A2"].Value, cells["A3"].Value, cells["A4"].Value, cells["A5"].Value, cells["A6"].Value });
            }

            // Preserve caller sequence (e.g. ViewSchedule order)
            using (var preservedStream = new MemoryStream())
            {
                SheetIndexExportOutcome preservedOutcome = exporter.Export(mixedSheets, revisions, preservedStream, preserveSheetOrder: true);
                Assert.IsTrue(preservedOutcome.Succeeded);
                preservedStream.Position = 0;

                using var zip = new ZipArchive(preservedStream, ZipArchiveMode.Read);
                XDocument sheet1 = LoadXmlEntry(zip, "xl/worksheets/sheet1.xml");
                var cells = ReadCellMap(sheet1);
                CollectionAssert.AreEqual(
                    new[] { "G001", "A102", "A10", "A2", "A101" },
                    new[] { cells["A2"].Value, cells["A3"].Value, cells["A4"].Value, cells["A5"].Value, cells["A6"].Value });
            }
        }

        [Test]
        public void Export_HandlesEmptyAndNullInputs_WithWarningsAndValidEmptyWorkbook()
        {
            var exporter = new SheetIndexExporter();
            using var ms = new MemoryStream();

            SheetIndexExportOutcome outcome = exporter.Export(null, null, ms);

            Assert.IsTrue(outcome.Succeeded, outcome.ErrorMessage);
            Assert.AreEqual(0, outcome.ExportedSheetCount);
            Assert.AreEqual(0, outcome.ExportedRevisionColumnCount);
            Assert.AreEqual(0, outcome.SectionHeaderCount);
            Assert.AreEqual(2, outcome.Warnings.Count);

            ms.Position = 0;
            using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
            XDocument sheet1 = LoadXmlEntry(zip, "xl/worksheets/sheet1.xml");
            List<XElement> rows = sheet1.Root!.Element(SpreadsheetNs + "sheetData")!.Elements(SpreadsheetNs + "row").ToList();
            Assert.AreEqual(1, rows.Count);
        }

        [Test]
        public void Export_ReturnsFailureOutcomeWithErrorMessage_OnBlankPathLockedFileDirectoryOrNonWritableStream()
        {
            var revisions = new List<SheetIndexRevisionModel>
            {
                new SheetIndexRevisionModel("rev-1", "Issue 1", "2026-01-01", 1)
            };
            var sheets = new List<SheetIndexSheetModel>
            {
                new SheetIndexSheetModel("s-1", "A101", "Floor Plan", new[] { "rev-1" })
            };

            var exporter = new SheetIndexExporter();

            // 1. Blank / null filePath
            SheetIndexExportOutcome blankPathOutcome = exporter.Export(sheets, revisions, "   ");
            Assert.IsFalse(blankPathOutcome.Succeeded);
            Assert.IsFalse(string.IsNullOrWhiteSpace(blankPathOutcome.ErrorMessage));
            Assert.AreEqual(0, blankPathOutcome.ExportedSheetCount);
            Assert.AreEqual(0, blankPathOutcome.ExportedRevisionColumnCount);

            // 2. Null stream & non-writable stream
            SheetIndexExportOutcome nullStreamOutcome = exporter.Export(sheets, revisions, (Stream?)null);
            Assert.IsFalse(nullStreamOutcome.Succeeded);
            Assert.IsFalse(string.IsNullOrWhiteSpace(nullStreamOutcome.ErrorMessage));

            using (var readOnlyStream = new MemoryStream(Array.Empty<byte>(), writable: false))
            {
                SheetIndexExportOutcome readOnlyOutcome = exporter.Export(sheets, revisions, readOnlyStream);
                Assert.IsFalse(readOnlyOutcome.Succeeded);
                Assert.IsFalse(string.IsNullOrWhiteSpace(readOnlyOutcome.ErrorMessage));
            }

            // 3. Locked destination file (must fail cleanly WITHOUT deleting or overwriting existing bytes)
            string tempDir = Path.Combine(Path.GetTempPath(), "SheetIndexLockTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            string lockedPath = Path.Combine(tempDir, "LockedSheetIndex.xlsx");

            try
            {
                byte[] sentinelBytes = Encoding.UTF8.GetBytes("EXISTING_WORKBOOK");
                File.WriteAllBytes(lockedPath, sentinelBytes);

                using (var lockHandle = new FileStream(lockedPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    SheetIndexExportOutcome lockedOutcome = exporter.Export(sheets, revisions, lockedPath);
                    Assert.IsFalse(lockedOutcome.Succeeded);
                    Assert.AreEqual(lockedPath, lockedOutcome.OutputFilePath);
                    Assert.IsFalse(string.IsNullOrWhiteSpace(lockedOutcome.ErrorMessage));
                    Assert.AreEqual(0, lockedOutcome.ExportedSheetCount);
                }

                CollectionAssert.AreEqual(sentinelBytes, File.ReadAllBytes(lockedPath));

                // 4. Directory path passed as filePath
                SheetIndexExportOutcome dirOutcome = exporter.Export(sheets, revisions, tempDir);
                Assert.IsFalse(dirOutcome.Succeeded);
                Assert.IsFalse(string.IsNullOrWhiteSpace(dirOutcome.ErrorMessage));
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, recursive: true);
                }
            }
        }

        [Test]
        public void BuildMatrix_InternalSeam_ConstructsExpectedMatrixRowsAndResetBehavior()
        {
            var revisions = new List<SheetIndexRevisionModel>
            {
                new SheetIndexRevisionModel("rev-2", "Bid", "2026-02-01", 2),
                new SheetIndexRevisionModel("rev-1", "Permit", "2026-01-01", 1)
            };
            var sheets = new List<SheetIndexSheetModel>
            {
                new SheetIndexSheetModel("s-1", "S101", "Foundation", new[] { "rev-1" }) { SectionGroup = "STRUCTURAL" },
                new SheetIndexSheetModel("s-2", "G001", "Notes", Array.Empty<string>()) { SectionGroup = string.Empty },
                new SheetIndexSheetModel("s-3", "S102", "Framing", new[] { "rev-2" }) { SectionGroup = "structural" }
            };

            var exporter = new SheetIndexExporter();
            SheetIndexMatrix matrix = exporter.BuildMatrix(sheets, revisions, preserveSheetOrder: true);

            Assert.AreEqual(2, matrix.RevisionHeaders.Count);
            Assert.AreEqual("rev-1", matrix.RevisionHeaders[0].UniqueId);
            Assert.AreEqual("rev-2", matrix.RevisionHeaders[1].UniqueId);

            Assert.AreEqual(5, matrix.Rows.Count);
            Assert.IsTrue(matrix.Rows[0].IsSectionHeader);
            Assert.AreEqual("STRUCTURAL", matrix.Rows[0].SheetNumber);
            Assert.IsFalse(matrix.Rows[1].IsSectionHeader);
            Assert.AreEqual("S101", matrix.Rows[1].SheetNumber);
            Assert.IsFalse(matrix.Rows[2].IsSectionHeader);
            Assert.AreEqual("G001", matrix.Rows[2].SheetNumber);
            Assert.IsTrue(matrix.Rows[3].IsSectionHeader);
            Assert.AreEqual("structural", matrix.Rows[3].SheetNumber);
            Assert.IsFalse(matrix.Rows[4].IsSectionHeader);
            Assert.AreEqual("S102", matrix.Rows[4].SheetNumber);
        }

        private static byte[] ReadEntryBytes(ZipArchiveEntry entry)
        {
            using Stream stream = entry.Open();
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            return ms.ToArray();
        }

        private static XDocument LoadXmlEntry(ZipArchive zip, string entryPath)
        {
            ZipArchiveEntry? entry = zip.GetEntry(entryPath);
            Assert.IsNotNull(entry, $"Expected ZIP entry '{entryPath}'.");
            using Stream stream = entry!.Open();
            return XDocument.Load(stream, LoadOptions.PreserveWhitespace);
        }

        private static Dictionary<string, (string Value, string Style)> ReadCellMap(XDocument sheet1)
        {
            var map = new Dictionary<string, (string Value, string Style)>(StringComparer.Ordinal);
            foreach (XElement cell in sheet1.Descendants(SpreadsheetNs + "c"))
            {
                string coord = (string)cell.Attribute("r")!;
                string style = (string?)cell.Attribute("s") ?? string.Empty;
                string value = cell.Element(SpreadsheetNs + "is")?.Element(SpreadsheetNs + "t")?.Value ?? string.Empty;
                map[coord] = (value, style);
            }

            return map;
        }
    }
}
