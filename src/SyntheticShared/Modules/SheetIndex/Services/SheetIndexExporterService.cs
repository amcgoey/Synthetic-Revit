using System;
using System.IO;
using System.IO.Compression;
using System.Text;

using Synthetic.Modules.SheetIndex.Models;

namespace Synthetic.Modules.SheetIndex.Services
{
    /// <summary>
    /// Headless Excel exporter service generating formatted .xlsx spreadsheets from a SheetIndexMatrix.
    /// Matches standard drawing index layout styling (Arial font, 90-degree rotated revision headers, exact column dimensions, and centered dot indicators).
    /// </summary>
    public class SheetIndexExporterService
    {
        /// <summary>
        /// Exports a SheetIndexMatrix to an Excel .xlsx workbook at the specified file path.
        /// </summary>
        /// <param name="matrix">The populated sheet index 2D matrix model.</param>
        /// <param name="filePath">Target destination file path for the .xlsx file.</param>
        public void ExportToExcel(SheetIndexMatrix matrix, string filePath)
        {
            if (matrix == null) throw new ArgumentNullException(nameof(matrix));
            if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentException("File path cannot be empty.", nameof(filePath));

            // Ensure destination directory exists
            string? dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }

            using (var zip = ZipFile.Open(filePath, ZipArchiveMode.Create))
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
            var entry = zip.CreateEntry("[Content_Types].xml");
            using (var writer = new StreamWriter(entry.Open(), Encoding.UTF8))
            {
                writer.Write(@"<?xml opacity=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
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
            var entry = zip.CreateEntry("_rels/.rels");
            using (var writer = new StreamWriter(entry.Open(), Encoding.UTF8))
            {
                writer.Write(@"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<Relationships xmlns=""http://schemas.openxmlformats.org/package/2006/relationships"">
  <Relationship Id=""rId1"" Type=""http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument"" Target=""xl/workbook.xml""/>
</Relationships>");
            }
        }

        private static void WriteWorkbook(ZipArchive zip)
        {
            var entry = zip.CreateEntry("xl/workbook.xml");
            using (var writer = new StreamWriter(entry.Open(), Encoding.UTF8))
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
            var entry = zip.CreateEntry("xl/_rels/workbook.xml.rels");
            using (var writer = new StreamWriter(entry.Open(), Encoding.UTF8))
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
            var entry = zip.CreateEntry("xl/styles.xml");
            using (var writer = new StreamWriter(entry.Open(), Encoding.UTF8))
            {
                // Style IDs:
                // 0 = Normal left (Arial 10pt)
                // 1 = Header left bold thin border (SHEET NUMBER / SHEET NAME)
                // 2 = Revision header centered bold thin border (90 degree rotated text)
                // 3 = Left data cell thin border
                // 4 = Centered data cell thin border (Dot indicator ●)
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

        private const int StyleNormalLeft = 0;
        private const int StyleHeaderLeft = 1;
        private const int StyleHeaderCenter = 2;
        private const int StyleDataLeft = 3;
        private const int StyleDataCenter = 4;
        private const int StyleSectionHeader = 5;
        private const int StyleDataLeftZebra = 6;
        private const int StyleDataCenterZebra = 7;

        private static void WriteWorksheet(ZipArchive zip, SheetIndexMatrix matrix)
        {
            var entry = zip.CreateEntry("xl/worksheets/sheet1.xml");
            using (var writer = new StreamWriter(entry.Open(), Encoding.UTF8))
            {
                writer.Write(@"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<worksheet xmlns=""http://schemas.openxmlformats.org/spreadsheetml/2006/main"">
  <sheetViews>
    <sheetView tabSelected=""1"" workbookViewId=""0"">
      <showGridLines val=""1""/>
    </sheetView>
  </sheetViews>
  <cols>
    <col min=""1"" max=""1"" width=""16"" customWidth=""1""/>
    <col min=""2"" max=""2"" width=""45"" customWidth=""1""/>
    <col min=""3"" max=""200"" width=""6"" customWidth=""1""/>
  </cols>
  <sheetData>");

                int rowIndex = 1;

                // Row 1: Left column names & Combined Revision Headers ("Revision Name - Revision Date") with 90-degree text rotation
                writer.Write($"<row r=\"{rowIndex}\" ht=\"140\" customHeight=\"1\">");
                int colIndex = 1;
                foreach (var leftCol in matrix.LeftColumns)
                {
                    string headerName = leftCol.ToUpperInvariant();
                    writer.Write($"<c r=\"{GetColumnAddress(colIndex++)}{rowIndex}\" t=\"inlineStr\" s=\"{StyleHeaderLeft}\"><is><t>{EscapeXml(headerName)}</t></is></c>");
                }
                foreach (var revHeader in matrix.RevisionHeaders)
                {
                    writer.Write($"<c r=\"{GetColumnAddress(colIndex++)}{rowIndex}\" t=\"inlineStr\" s=\"{StyleHeaderCenter}\"><is><t>{EscapeXml(revHeader.DisplayName)}</t></is></c>");
                }
                writer.Write("</row>");
                rowIndex++;

                int dataRowCounter = 0;

                // Data Rows
                foreach (var row in matrix.Rows)
                {
                    writer.Write($"<row r=\"{rowIndex}\" ht=\"20\" customHeight=\"1\">");
                    colIndex = 1;

                    if (row.IsSectionHeader)
                    {
                        writer.Write($"<c r=\"{GetColumnAddress(colIndex++)}{rowIndex}\" t=\"inlineStr\" s=\"{StyleSectionHeader}\"><is><t>{EscapeXml(row.SheetNumber)}</t></is></c>");
                        writer.Write($"<c r=\"{GetColumnAddress(colIndex++)}{rowIndex}\" t=\"inlineStr\" s=\"{StyleSectionHeader}\"><is><t></t></is></c>");

                        foreach (var _ in matrix.RevisionHeaders)
                        {
                            writer.Write($"<c r=\"{GetColumnAddress(colIndex++)}{rowIndex}\" t=\"inlineStr\" s=\"{StyleSectionHeader}\"><is><t></t></is></c>");
                        }
                    }
                    else
                    {
                        bool isZebra = (dataRowCounter % 2 == 1);
                        int styleLeft = isZebra ? StyleDataLeftZebra : StyleDataLeft;
                        int styleCenter = isZebra ? StyleDataCenterZebra : StyleDataCenter;

                        writer.Write($"<c r=\"{GetColumnAddress(colIndex++)}{rowIndex}\" t=\"inlineStr\" s=\"{styleLeft}\"><is><t>{EscapeXml(row.SheetNumber)}</t></is></c>");
                        writer.Write($"<c r=\"{GetColumnAddress(colIndex++)}{rowIndex}\" t=\"inlineStr\" s=\"{styleLeft}\"><is><t>{EscapeXml(row.SheetName)}</t></is></c>");

                        foreach (var cellVal in row.Cells)
                        {
                            writer.Write($"<c r=\"{GetColumnAddress(colIndex++)}{rowIndex}\" t=\"inlineStr\" s=\"{styleCenter}\"><is><t>{EscapeXml(cellVal)}</t></is></c>");
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
            if (string.IsNullOrEmpty(input)) return string.Empty;
            return input.Replace("&", "&amp;")
                        .Replace("<", "&lt;")
                        .Replace(">", "&gt;")
                        .Replace("\"", "&quot;")
                        .Replace("'", "&apos;");
        }
    }
}
