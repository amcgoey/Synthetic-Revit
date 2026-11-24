using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security;
using System.Text;
using Synthetic.Features.SheetIndex.Models;

namespace Synthetic.Features.SheetIndex.Services
{
    /// <summary>
    /// Deep Core Dot Sheet Index exporter that builds the chronological issuance matrix from sheet
    /// and revision models and writes a formatted OpenXML .xlsx package without external Excel dependencies.
    /// </summary>
    public class SheetIndexExporter
    {
        private static readonly AlphanumericComparer SheetComparer = new AlphanumericComparer();
        private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        private const int StyleNormalLeft = 0;
        private const int StyleHeaderLeft = 1;
        private const int StyleHeaderCenter = 2;
        private const int StyleDataLeft = 3;
        private const int StyleDataCenter = 4;
        private const int StyleSectionHeader = 5;
        private const int StyleDataLeftZebra = 6;
        private const int StyleDataCenterZebra = 7;

        /// <summary>
        /// Builds the sheet index matrix and exports it as a formatted OpenXML .xlsx workbook at <paramref name="filePath"/>.
        /// </summary>
        /// <param name="sheets">Selected sheet models to export.</param>
        /// <param name="revisions">Selected revision models forming chronological Issuance Columns.</param>
        /// <param name="filePath">Target destination .xlsx file path.</param>
        /// <param name="preserveSheetOrder">True to preserve caller-supplied sheet order (e.g. from a schedule or Print Set); false to sort by print order then alphanumeric Sheet Number.</param>
        /// <returns>A typed <see cref="SheetIndexExportOutcome"/> summarizing counts, destination, warnings, or save failure.</returns>
        public SheetIndexExportOutcome Export(
            IEnumerable<SheetIndexSheetModel>? sheets,
            IEnumerable<SheetIndexRevisionModel>? revisions,
            string? filePath,
            bool preserveSheetOrder = false)
        {
            var warnings = new List<string>();
            SheetIndexMatrix matrix = BuildMatrixCore(sheets, revisions, preserveSheetOrder, warnings);

            if (string.IsNullOrWhiteSpace(filePath))
            {
                return SheetIndexExportOutcome.Failure(
                    filePath,
                    "Output file path cannot be empty.",
                    warnings);
            }

            try
            {
                string targetPath = filePath!;
                string? dir = Path.GetDirectoryName(targetPath);

                using (var buffer = new MemoryStream())
                {
                    WritePackageToStream(buffer, matrix);

                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }

                    using (var fileStream = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        buffer.WriteTo(fileStream);
                    }
                }

                return CreateSuccessOutcome(targetPath, matrix, warnings);
            }
            catch (Exception ex) when (ex is IOException
                                    || ex is UnauthorizedAccessException
                                    || ex is ArgumentException
                                    || ex is NotSupportedException
                                    || ex is SecurityException)
            {
                return SheetIndexExportOutcome.Failure(
                    filePath,
                    $"Could not save Sheet Index workbook to '{filePath}': {ex.Message}",
                    warnings);
            }
        }

        /// <summary>
        /// Builds the sheet index matrix and writes the formatted OpenXML .xlsx package into <paramref name="destinationStream"/>.
        /// The stream is left open after writing.
        /// </summary>
        /// <param name="sheets">Selected sheet models to export.</param>
        /// <param name="revisions">Selected revision models forming chronological Issuance Columns.</param>
        /// <param name="destinationStream">Writable target stream receiving the .xlsx ZIP package.</param>
        /// <param name="preserveSheetOrder">True to preserve caller-supplied sheet order; false to sort by print order then alphanumeric Sheet Number.</param>
        /// <returns>A typed <see cref="SheetIndexExportOutcome"/> summarizing counts, warnings, or stream write failure.</returns>
        public SheetIndexExportOutcome Export(
            IEnumerable<SheetIndexSheetModel>? sheets,
            IEnumerable<SheetIndexRevisionModel>? revisions,
            Stream? destinationStream,
            bool preserveSheetOrder = false)
        {
            var warnings = new List<string>();
            SheetIndexMatrix matrix = BuildMatrixCore(sheets, revisions, preserveSheetOrder, warnings);

            if (destinationStream == null)
            {
                return SheetIndexExportOutcome.Failure(
                    string.Empty,
                    "Destination stream cannot be null.",
                    warnings);
            }

            if (!destinationStream.CanWrite)
            {
                return SheetIndexExportOutcome.Failure(
                    string.Empty,
                    "Destination stream must be writable.",
                    warnings);
            }

            try
            {
                WritePackageToStream(destinationStream, matrix);
                return CreateSuccessOutcome(string.Empty, matrix, warnings);
            }
            catch (Exception ex) when (ex is IOException
                                    || ex is UnauthorizedAccessException
                                    || ex is ArgumentException
                                    || ex is NotSupportedException
                                    || ex is SecurityException
                                    || ex is ObjectDisposedException)
            {
                return SheetIndexExportOutcome.Failure(
                    string.Empty,
                    $"Could not write Sheet Index workbook to stream: {ex.Message}",
                    warnings);
            }
        }

        /// <summary>
        /// Builds the 2D <see cref="SheetIndexMatrix"/> from raw sheet and revision models.
        /// </summary>
        internal SheetIndexMatrix BuildMatrix(
            IEnumerable<SheetIndexSheetModel>? sheets,
            IEnumerable<SheetIndexRevisionModel>? revisions,
            bool preserveSheetOrder = false)
        {
            return BuildMatrixCore(sheets, revisions, preserveSheetOrder, warnings: null);
        }

        private static SheetIndexExportOutcome CreateSuccessOutcome(
            string outputFilePath,
            SheetIndexMatrix matrix,
            IReadOnlyList<string> warnings)
        {
            int exportedSheetCount = 0;
            int sectionHeaderCount = 0;
            foreach (SheetIndexRow row in matrix.Rows)
            {
                if (row.IsSectionHeader)
                {
                    sectionHeaderCount++;
                }
                else
                {
                    exportedSheetCount++;
                }
            }

            return SheetIndexExportOutcome.Success(
                outputFilePath,
                exportedSheetCount,
                matrix.RevisionHeaders.Count,
                sectionHeaderCount,
                warnings);
        }

        private static SheetIndexMatrix BuildMatrixCore(
            IEnumerable<SheetIndexSheetModel>? sheets,
            IEnumerable<SheetIndexRevisionModel>? revisions,
            bool preserveSheetOrder,
            List<string>? warnings)
        {
            var matrix = new SheetIndexMatrix();

            List<SheetIndexSheetModel> sheetList = sheets != null
                ? sheets.Where(s => s != null).ToList()
                : new List<SheetIndexSheetModel>();
            List<SheetIndexRevisionModel> revisionList = revisions != null
                ? revisions.Where(r => r != null).ToList()
                : new List<SheetIndexRevisionModel>();

            if (sheetList.Count == 0)
            {
                warnings?.Add("No sheets were provided for export.");
            }

            if (revisionList.Count == 0)
            {
                warnings?.Add("No revisions were selected for export.");
            }

            // 1. Order selected revisions chronologically by Sequence, then Date
            List<SheetIndexRevisionModel> sortedRevisions = revisionList
                .OrderBy(r => r.Sequence)
                .ThenBy(r => r.Date ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (SheetIndexRevisionModel rev in sortedRevisions)
            {
                matrix.RevisionHeaders.Add(new SheetIndexRevisionHeader(
                    rev.UniqueId ?? string.Empty,
                    rev.Name ?? string.Empty,
                    rev.Date ?? string.Empty,
                    rev.Sequence));
            }

            // 2. Order sheets by strategy (preserve sequence vs PrintOrderIndex / fallback alphanumeric sorting)
            List<SheetIndexSheetModel> sortedSheets = preserveSheetOrder
                ? sheetList
                : sheetList
                    .OrderBy(s => s.PrintOrderIndex.HasValue ? s.PrintOrderIndex.Value : int.MaxValue)
                    .ThenBy(s => s.SheetNumber ?? string.Empty, SheetComparer)
                    .ToList();

            // 3. Build data rows and inject section headers when SectionGroup changes,
            // resetting currentSectionGroup when an ungrouped sheet follows grouped sheets.
            string? currentSectionGroup = null;

            foreach (SheetIndexSheetModel sheet in sortedSheets)
            {
                if (string.IsNullOrWhiteSpace(sheet.SectionGroup))
                {
                    currentSectionGroup = null;
                }
                else if (!string.Equals(sheet.SectionGroup, currentSectionGroup, StringComparison.OrdinalIgnoreCase))
                {
                    var headerCells = matrix.RevisionHeaders.Select(_ => string.Empty).ToList();
                    matrix.Rows.Add(new SheetIndexRow(sheet.SectionGroup!, string.Empty, headerCells, isSectionHeader: true));
                    currentSectionGroup = sheet.SectionGroup;
                }

                var rowCells = new List<string>(matrix.RevisionHeaders.Count);
                foreach (SheetIndexRevisionHeader revHeader in matrix.RevisionHeaders)
                {
                    bool isIncluded = sheet.RevisionIds != null &&
                                      !string.IsNullOrEmpty(revHeader.UniqueId) &&
                                      sheet.RevisionIds.Contains(revHeader.UniqueId);
                    rowCells.Add(isIncluded ? SheetIndexMatrix.IssuanceIndicatorSymbol : string.Empty);
                }

                matrix.Rows.Add(new SheetIndexRow(
                    sheet.SheetNumber ?? string.Empty,
                    sheet.SheetName ?? string.Empty,
                    rowCells));
            }

            return matrix;
        }

        private static void WritePackageToStream(Stream destinationStream, SheetIndexMatrix matrix)
        {
            using (var zip = new ZipArchive(destinationStream, ZipArchiveMode.Create, leaveOpen: true))
            {
                WriteContentTypes(zip);
                WriteRootRels(zip);
                WriteWorkbook(zip);
                WriteWorkbookRels(zip);
                WriteStyles(zip);
                WriteWorksheet(zip, matrix);
            }
        }

        private static void WriteContentTypes(ZipArchive zip)
        {
            ZipArchiveEntry entry = zip.CreateEntry("[Content_Types].xml");
            using (var writer = new StreamWriter(entry.Open(), Utf8NoBom))
            {
                writer.Write(@"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<Types xmlns=""http://schemas.openxmlformats.org/package/2006/content-types"">
  <Default Extension=""rels"" ContentType=""application/vnd.openxmlformats-package.relationships+xml""/>
  <Default Extension=""xml"" ContentType=""application/xml""/>
  <Override PartName=""/xl/workbook.xml"" ContentType=""application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml""/>
  <Override PartName=""/xl/worksheets/sheet1.xml"" ContentType=""application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml""/>
  <Override PartName=""/xl/styles.xml"" ContentType=""application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml""/>
</Types>");
            }
        }

        private static void WriteRootRels(ZipArchive zip)
        {
            ZipArchiveEntry entry = zip.CreateEntry("_rels/.rels");
            using (var writer = new StreamWriter(entry.Open(), Utf8NoBom))
            {
                writer.Write(@"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<Relationships xmlns=""http://schemas.openxmlformats.org/package/2006/relationships"">
  <Relationship Id=""rId1"" Type=""http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument"" Target=""xl/workbook.xml""/>
</Relationships>");
            }
        }

        private static void WriteWorkbook(ZipArchive zip)
        {
            ZipArchiveEntry entry = zip.CreateEntry("xl/workbook.xml");
            using (var writer = new StreamWriter(entry.Open(), Utf8NoBom))
            {
                writer.Write(@"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<workbook xmlns=""http://schemas.openxmlformats.org/spreadsheetml/2006/main"" xmlns:r=""http://schemas.openxmlformats.org/officeDocument/2006/relationships"">
  <sheets>
    <sheet name=""Sheet Index"" sheetId=""1"" r:id=""rId1""/>
  </sheets>
</workbook>");
            }
        }

        private static void WriteWorkbookRels(ZipArchive zip)
        {
            ZipArchiveEntry entry = zip.CreateEntry("xl/_rels/workbook.xml.rels");
            using (var writer = new StreamWriter(entry.Open(), Utf8NoBom))
            {
                writer.Write(@"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<Relationships xmlns=""http://schemas.openxmlformats.org/package/2006/relationships"">
  <Relationship Id=""rId1"" Type=""http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet"" Target=""worksheets/sheet1.xml""/>
  <Relationship Id=""rId2"" Type=""http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles"" Target=""styles.xml""/>
</Relationships>");
            }
        }

        private static void WriteStyles(ZipArchive zip)
        {
            ZipArchiveEntry entry = zip.CreateEntry("xl/styles.xml");
            using (var writer = new StreamWriter(entry.Open(), Utf8NoBom))
            {
                // Style IDs:
                // 0 = Normal left (Arial 10pt)
                // 1 = Header left bold thin border (SHEET NUMBER / SHEET NAME)
                // 2 = Revision header centered bold thin border (90 degree rotated text)
                // 3 = Left data cell thin border
                // 4 = Centered data cell thin border (Issuance Indicator ●)
                // 5 = Section header bold thin border (Light Gray Fill #E0E0E0)
                // 6 = Left data cell zebra (Subtle Off-White #F8F9FA)
                // 7 = Centered data cell zebra (Subtle Off-White #F8F9FA)
                writer.Write(@"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<styleSheet xmlns=""http://schemas.openxmlformats.org/spreadsheetml/2006/main"">
  <fonts count=""2"">
    <font><sz val=""10""/><name val=""Arial""/></font>
    <font><b/><sz val=""10""/><name val=""Arial""/></font>
  </fonts>
  <fills count=""4"">
    <fill><patternFill patternType=""none""/></fill>
    <fill><patternFill patternType=""gray125""/></fill>
    <fill><patternFill patternType=""solid""><fgColor rgb=""FFE0E0E0""/></patternFill></fill>
    <fill><patternFill patternType=""solid""><fgColor rgb=""FFF8F9FA""/></patternFill></fill>
  </fills>
  <borders count=""2"">
    <border><left/><right/><top/><bottom/></border>
    <border>
      <left style=""thin""><color rgb=""FFD3D3D3""/></left>
      <right style=""thin""><color rgb=""FFD3D3D3""/></right>
      <top style=""thin""><color rgb=""FFD3D3D3""/></top>
      <bottom style=""thin""><color rgb=""FFD3D3D3""/></bottom>
    </border>
  </borders>
  <cellStyleXfs count=""1"">
    <xf numFmtId=""0"" fontId=""0"" fillId=""0"" borderId=""0""/>
  </cellStyleXfs>
  <cellXfs count=""8"">
    <xf numFmtId=""0"" fontId=""0"" fillId=""0"" borderId=""0"" xfId=""0""/>
    <xf numFmtId=""0"" fontId=""1"" fillId=""0"" borderId=""1"" xfId=""0""><alignment horizontal=""left"" vertical=""bottom""/></xf>
    <xf numFmtId=""0"" fontId=""1"" fillId=""0"" borderId=""1"" xfId=""0""><alignment horizontal=""center"" vertical=""bottom"" textRotation=""90""/></xf>
    <xf numFmtId=""0"" fontId=""0"" fillId=""0"" borderId=""1"" xfId=""0""><alignment horizontal=""left"" vertical=""center""/></xf>
    <xf numFmtId=""0"" fontId=""0"" fillId=""0"" borderId=""1"" xfId=""0""><alignment horizontal=""center"" vertical=""center""/></xf>
    <xf numFmtId=""0"" fontId=""1"" fillId=""2"" borderId=""1"" xfId=""0""><alignment horizontal=""left"" vertical=""center""/></xf>
    <xf numFmtId=""0"" fontId=""0"" fillId=""3"" borderId=""1"" xfId=""0""><alignment horizontal=""left"" vertical=""center""/></xf>
    <xf numFmtId=""0"" fontId=""0"" fillId=""3"" borderId=""1"" xfId=""0""><alignment horizontal=""center"" vertical=""center""/></xf>
  </cellXfs>
</styleSheet>");
            }
        }

        private static void WriteWorksheet(ZipArchive zip, SheetIndexMatrix matrix)
        {
            ZipArchiveEntry entry = zip.CreateEntry("xl/worksheets/sheet1.xml");
            using (var writer = new StreamWriter(entry.Open(), Utf8NoBom))
            {
                int leftColumnCount = matrix.LeftColumns.Count;
                int revisionColumnCount = matrix.RevisionHeaders.Count;
                int totalColumnCount = leftColumnCount + revisionColumnCount;

                writer.Write(@"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<worksheet xmlns=""http://schemas.openxmlformats.org/spreadsheetml/2006/main"">
  <sheetViews>
    <sheetView tabSelected=""1"" workbookViewId=""0"">
      <showGridLines val=""1""/>
    </sheetView>
  </sheetViews>
  <cols>
    <col min=""1"" max=""1"" width=""16"" customWidth=""1""/>
    <col min=""2"" max=""2"" width=""45"" customWidth=""1""/>");

                if (revisionColumnCount > 0)
                {
                    int firstRevCol = leftColumnCount + 1;
                    writer.Write(string.Format(
                        CultureInfo.InvariantCulture,
                        "\n    <col min=\"{0}\" max=\"{1}\" width=\"6\" customWidth=\"1\"/>",
                        firstRevCol,
                        totalColumnCount));
                }

                writer.Write(@"
  </cols>
  <sheetData>");

                int rowIndex = 1;

                // Row 1: Left column headers & rotated Revision Headers ("Revision Name - Revision Date")
                writer.Write(string.Format(CultureInfo.InvariantCulture, "<row r=\"{0}\" ht=\"140\" customHeight=\"1\">", rowIndex));
                int colIndex = 1;
                foreach (string leftCol in matrix.LeftColumns)
                {
                    string headerName = (leftCol ?? string.Empty).ToUpperInvariant();
                    WriteInlineStringCell(writer, colIndex++, rowIndex, StyleHeaderLeft, headerName);
                }

                foreach (SheetIndexRevisionHeader revHeader in matrix.RevisionHeaders)
                {
                    WriteInlineStringCell(writer, colIndex++, rowIndex, StyleHeaderCenter, revHeader.DisplayName);
                }

                writer.Write("</row>");
                rowIndex++;

                int dataRowCounter = 0;

                foreach (SheetIndexRow row in matrix.Rows)
                {
                    writer.Write(string.Format(CultureInfo.InvariantCulture, "<row r=\"{0}\" ht=\"20\" customHeight=\"1\">", rowIndex));
                    colIndex = 1;

                    if (row.IsSectionHeader)
                    {
                        WriteInlineStringCell(writer, colIndex++, rowIndex, StyleSectionHeader, row.SheetNumber);
                        WriteInlineStringCell(writer, colIndex++, rowIndex, StyleSectionHeader, string.Empty);

                        for (int i = 0; i < revisionColumnCount; i++)
                        {
                            WriteInlineStringCell(writer, colIndex++, rowIndex, StyleSectionHeader, string.Empty);
                        }
                    }
                    else
                    {
                        bool isZebra = (dataRowCounter % 2 == 1);
                        int styleLeft = isZebra ? StyleDataLeftZebra : StyleDataLeft;
                        int styleCenter = isZebra ? StyleDataCenterZebra : StyleDataCenter;

                        WriteInlineStringCell(writer, colIndex++, rowIndex, styleLeft, row.SheetNumber);
                        WriteInlineStringCell(writer, colIndex++, rowIndex, styleLeft, row.SheetName);

                        foreach (string cellVal in row.Cells)
                        {
                            WriteInlineStringCell(writer, colIndex++, rowIndex, styleCenter, cellVal);
                        }

                        dataRowCounter++;
                    }

                    writer.Write("</row>");
                    rowIndex++;
                }

                writer.Write(@"</sheetData>
</worksheet>");
            }
        }

        private static void WriteInlineStringCell(
            StreamWriter writer,
            int colIndex,
            int rowIndex,
            int styleIndex,
            string? text)
        {
            string cellRef = GetColumnAddress(colIndex) + rowIndex.ToString(CultureInfo.InvariantCulture);
            writer.Write(string.Format(
                CultureInfo.InvariantCulture,
                "<c r=\"{0}\" t=\"inlineStr\" s=\"{1}\"><is><t xml:space=\"preserve\">{2}</t></is></c>",
                cellRef,
                styleIndex,
                EscapeXml(text)));
        }

        private static string GetColumnAddress(int colIndex)
        {
            string colAddr = string.Empty;
            while (colIndex > 0)
            {
                int rem = (colIndex - 1) % 26;
                colAddr = (char)('A' + rem) + colAddr;
                colIndex = (colIndex - 1) / 26;
            }

            return colAddr;
        }

        private static string EscapeXml(string? input)
        {
            if (string.IsNullOrEmpty(input))
            {
                return string.Empty;
            }

            var sb = new StringBuilder(input!.Length);
            for (int i = 0; i < input.Length; i++)
            {
                char ch = input[i];

                if (char.IsHighSurrogate(ch))
                {
                    if (i + 1 < input.Length && char.IsLowSurrogate(input[i + 1]))
                    {
                        sb.Append(ch);
                        sb.Append(input[i + 1]);
                        i++;
                    }

                    continue;
                }

                if (char.IsLowSurrogate(ch) || !IsValidXml10Char(ch))
                {
                    continue;
                }

                switch (ch)
                {
                    case '&':
                        sb.Append("&amp;");
                        break;
                    case '<':
                        sb.Append("&lt;");
                        break;
                    case '>':
                        sb.Append("&gt;");
                        break;
                    case '"':
                        sb.Append("&quot;");
                        break;
                    case '\'':
                        sb.Append("&apos;");
                        break;
                    default:
                        sb.Append(ch);
                        break;
                }
            }

            return sb.ToString();
        }

        private static bool IsValidXml10Char(char ch)
        {
            return ch == '\t'
                || ch == '\n'
                || ch == '\r'
                || (ch >= '\u0020' && ch <= '\uD7FF')
                || (ch >= '\uE000' && ch <= '\uFFFD');
        }
    }
}
