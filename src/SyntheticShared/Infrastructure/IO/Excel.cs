using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using ExcelMS = Microsoft.Office.Interop.Excel;

using Synthetic.Infrastructure.IO;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;
using Synthetic.Modules.StandardsManagement.ViewModels;
namespace Synthetic.Infrastructure.IO{
    /// <summary>
    /// Helper class to read data from Microsoft Excel spreadsheets using Interop.
    /// </summary>
    public class Excel
    {
        /// <summary>
        /// Gets or sets the file path to the Excel workbook.
        /// </summary>
        public string path { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the name of the worksheet to read.
        /// </summary>
        public string? worksheetName { get; set; }

        /// <summary>
        /// Gets or sets the cell values read from the spreadsheet, stored as a 2D list of objects.
        /// </summary>
        public List<List<object>>? cells { get; set; }

        /// <summary>
        /// Initializes a new instance of the Excel class with a file path.
        /// </summary>
        /// <param name="path">The file path to the Excel workbook.</param>
        public Excel(string path)
        {
            this.path = path;
            this.worksheetName = null;
            this.cells = null;
        }

        /// <summary>
        /// Initializes a new instance of the Excel class with a file path and worksheet name.
        /// </summary>
        /// <param name="path">The file path to the Excel workbook.</param>
        /// <param name="worksheetName">The name of the worksheet to load.</param>
        public Excel(string path, string? worksheetName)
        {
            this.path = path;
            this.worksheetName = worksheetName;
            this.cells = null;
        }

        /// <summary>
        /// Reads the spreadsheet and returns cell contents as a 2D list.
        /// </summary>
        /// <returns>A list of rows, where each row is a list of cell values.</returns>
        public List<List<object>>? ReadExcel()
        {
            if (path == null || !File.Exists(path))
            {
                Console.WriteLine("File not found.");
                return null;
            }

            Microsoft.Office.Interop.Excel.Application excelApp = new Microsoft.Office.Interop.Excel.Application();
            Microsoft.Office.Interop.Excel.Workbook excelWorkbook = excelApp.Workbooks.Open(path);

            Microsoft.Office.Interop.Excel.Worksheet? excelWorksheet = null;

            if (this.worksheetName != null)
            {
                excelWorksheet = excelWorkbook.Worksheets[worksheetName] as Microsoft.Office.Interop.Excel.Worksheet;
            }
            else
            {
                excelWorksheet = excelWorkbook.Worksheets[1] as Microsoft.Office.Interop.Excel.Worksheet;
            }

            if (excelWorksheet != null)
            {
                excelWorksheet.AutoFilterMode = false;

                Microsoft.Office.Interop.Excel.Range range = excelWorksheet.UsedRange;

                int rowCount = range.Rows.Count;
                int columnCount = range.Columns.Count;
                this.cells = new List<List<object>>();

                for (int row = 1; row <= rowCount; row++)
                {
                    List<object> rowData = new List<object>();
                    for (int col = 1; col <= columnCount; col++)
                    {
                        Microsoft.Office.Interop.Excel.Range? cellRange = range.Cells[row, col] as Microsoft.Office.Interop.Excel.Range;
                        object? cellVal = cellRange?.Value;
                        if (cellVal != null)
                        {
                            rowData.Add(cellVal.ToString() ?? "");
                        }
                        else
                        {
                            rowData.Add("");
                        }
                    }
                    this.cells.Add(rowData);
                }
            }

            //excelWorkbook.Save();
            excelWorkbook.Close();
            excelApp.Quit();

            return this.cells;
        }

        /// <summary>
        /// Retrieves the list of worksheet names from the Excel workbook.
        /// </summary>
        /// <returns>A list of worksheet name strings.</returns>
        public List<string> WorkSheetNames ()
        {
            if (path == null || !File.Exists(path))
            {
                Console.WriteLine("File not found.");
                return new List<string>();
            }

            Microsoft.Office.Interop.Excel.Application excelApp = new Microsoft.Office.Interop.Excel.Application();
            Microsoft.Office.Interop.Excel.Workbook excelWorkbook = excelApp.Workbooks.Open(path);

            List<string> names = new List<string>();
            foreach (Microsoft.Office.Interop.Excel.Worksheet ws in excelWorkbook.Worksheets)
            {
                names.Add(ws.Name);
            }

            excelWorkbook.Close();
            excelApp.Quit();

            return names;
        }
    }
}
