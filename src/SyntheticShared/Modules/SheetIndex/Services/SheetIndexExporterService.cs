using System;
using System.IO;
using System.IO.Compression;
using System.Text;

using Synthetic.Modules.SheetIndex.Models;

namespace Synthetic.Modules.SheetIndex.Services
{
    /// <summary>
    /// Headless Excel exporter service generating formatted .xlsx spreadsheets from a SheetIndexMatrix.
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

            using (var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var zip = new ZipArchive(fileStream, ZipArchiveMode.Create))
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
                // 0 = Normal left
                // 1 = Header left bold thin border
                // 2 = Revision header centered bold thin border
                // 3 = Left data cell thin border
                // 4 = Centered data cell thin border
                writer.Write(@"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<styleSheet xmlns=""http://schemas.openxmlformats.org/spreadsheetml/2006/main"">
  <fonts count=""2"">
    <font><sz val=""11""/><name val=""Calibri""/></font>
    <font><b/><sz val=""11""/><name val=""Calibri""/></font>
  </fonts>
  <fills count=""2"">
    <fill><patternFill patternType=""none""/></fill>
    <fill><patternFill patternType=""gray125""/></fill>
  </fills>
  <borders count=""2"">
    <border><left/><right/><top/><bottom/></border>
    <border>
      <left style=""thin""><color auto=""1""/></left>
      <right style=""thin""><color auto=""1""/></right>
      <top style=""thin""><color auto=""1""/></top>
      <bottom style=""thin""><color auto=""1""/></bottom>
    </border>
  </borders>
  <cellStyleXfs count=""1"">
    <xf numFmtId=""0"" fontId=""0"" fillId=""0"" borderId=""0""/>
  </cellStyleXfs>
  <cellXfs count=""5"">
    <xf numFmtId=""0"" fontId=""0"" fillId=""0"" borderId=""0"" xfId=""0""/>
    <xf numFmtId=""0"" fontId=""1"" fillId=""0"" borderId=""1"" xfId=""0""><alignment horizontal=""left"" vertical=""center""/></xf>
    <xf numFmtId=""0"" fontId=""1"" fillId=""0"" borderId=""1"" xfId=""0""><alignment horizontal=""center"" vertical=""center""/></xf>
    <xf numFmtId=""0"" fontId=""0"" fillId=""0"" borderId=""1"" xfId=""0""><alignment horizontal=""left"" vertical=""center""/></xf>
    <xf numFmtId=""0"" fontId=""0"" fillId=""0"" borderId=""1"" xfId=""0""><alignment horizontal=""center"" vertical=""center""/></xf>
  </cellXfs>
</styleSheet>");
            }
        }

        private const int StyleNormalLeft = 0;
        private const int StyleHeaderLeft = 1;
        private const int StyleHeaderCenter = 2;
        private const int StyleDataLeft = 3;
        private const int StyleDataCenter = 4;

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
    <col min=""2"" max=""2"" width=""36"" customWidth=""1""/>
    <col min=""3"" max=""100"" width=""18"" customWidth=""1""/>
  </cols>
  <sheetData>");

                int rowIndex = 1;

                // Row 1: Left column names & Revision Names
                writer.Write($"<row r=\"{rowIndex}\">");
                int colIndex = 1;
                foreach (var leftCol in matrix.LeftColumns)
                {
                    writer.Write($"<c r=\"{GetColumnAddress(colIndex++)}{rowIndex}\" t=\"inlineStr\" s=\"{StyleHeaderLeft}\"><is><t>{EscapeXml(leftCol)}</t></is></c>");
                }
                foreach (var revHeader in matrix.RevisionHeaders)
                {
                    writer.Write($"<c r=\"{GetColumnAddress(colIndex++)}{rowIndex}\" t=\"inlineStr\" s=\"{StyleHeaderCenter}\"><is><t>{EscapeXml(revHeader.Name)}</t></is></c>");
                }
                writer.Write("</row>");
                rowIndex++;

                // Row 2: Revision Dates under revision headers (left cells blank)
                writer.Write($"<row r=\"{rowIndex}\">");
                colIndex = 1;
                foreach (var leftCol in matrix.LeftColumns)
                {
                    writer.Write($"<c r=\"{GetColumnAddress(colIndex++)}{rowIndex}\" t=\"inlineStr\" s=\"{StyleHeaderLeft}\"><is><t></t></is></c>");
                }
                foreach (var revHeader in matrix.RevisionHeaders)
                {
                    writer.Write($"<c r=\"{GetColumnAddress(colIndex++)}{rowIndex}\" t=\"inlineStr\" s=\"{StyleHeaderCenter}\"><is><t>{EscapeXml(revHeader.Date)}</t></is></c>");
                }
                writer.Write("</row>");
                rowIndex++;

                // Data Rows
                foreach (var row in matrix.Rows)
                {
                    writer.Write($"<row r=\"{rowIndex}\">");
                    colIndex = 1;
                    writer.Write($"<c r=\"{GetColumnAddress(colIndex++)}{rowIndex}\" t=\"inlineStr\" s=\"{StyleDataLeft}\"><is><t>{EscapeXml(row.SheetNumber)}</t></is></c>");
                    writer.Write($"<c r=\"{GetColumnAddress(colIndex++)}{rowIndex}\" t=\"inlineStr\" s=\"{StyleDataLeft}\"><is><t>{EscapeXml(row.SheetName)}</t></is></c>");

                    foreach (var cellVal in row.Cells)
                    {
                        writer.Write($"<c r=\"{GetColumnAddress(colIndex++)}{rowIndex}\" t=\"inlineStr\" s=\"{StyleDataCenter}\"><is><t>{EscapeXml(cellVal)}</t></is></c>");
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
