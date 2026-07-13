/// <summary>
/// OeExcelParser.cs
/// Reads the "DELIVERY SCHEDULE" sheet of the OE Excel log file into a list of OeExcelRow.
/// Read-only: this file is never written back to (the old AA-column write-back is retired).
/// </summary>

using System.Data;
using System.Globalization;
using System.IO;
using System.Text;
using DrawingTree.Models;
using ExcelDataReader;

namespace DrawingTree.Services;

public static class OeExcelParser
{
    private const string SheetName = "DELIVERY SCHEDULE";
    private const int HeaderRowIndex = 2; // 0-based; sheet row 3
    private const int DataStartRowIndex = 3; // 0-based; sheet row 4

    // 0-based column indices, matching the DELIVERY SCHEDULE header row.
    private const int ColOeNumber = 0;
    private const int ColJobNumber = 1;
    private const int ColCustomer = 2;
    private const int ColQuantity = 3;
    private const int ColPartNumber = 4;
    private const int ColRevision = 5;
    private const int ColContact = 6;
    private const int ColDrawingReleaseDate = 7;
    private const int ColLineNumber = 8;
    private const int ColDescription = 9;
    private const int ColPrice = 10;
    private const int ColPoNumber = 11;
    private const int ColDeliveryRequiredDate = 15;

    private static readonly string[] DateInputFormats =
    {
        "M/d/yyyy", "M-d-yyyy", "M.d.yyyy",
        "d-MMM-yy", "d-MMM-yyyy",
        "yyyy-M-d", "yyyy/M/d",
    };

    private static bool _encodingRegistered;

    /// <summary>Parses the DELIVERY SCHEDULE sheet at the given path into OeExcelRow records.</summary>
    /// <param name="filePath">Full path to the OE Excel workbook (.xlsm/.xlsx).</param>
    /// <returns>All data rows found, including rows carrying ParseWarnings (never silently dropped).</returns>
    /// <exception cref="InvalidOperationException">Thrown when the sheet or its header layout is missing/unexpected.</exception>
    public static List<OeExcelRow> Parse(string filePath)
    {
        if (!_encodingRegistered)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            _encodingRegistered = true;
        }

        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = ExcelReaderFactory.CreateReader(stream);
        var dataSet = reader.AsDataSet(new ExcelDataSetConfiguration
        {
            ConfigureDataTable = _ => new ExcelDataTableConfiguration { UseHeaderRow = false }
        });

        DataTable? sheet = null;
        foreach (DataTable table in dataSet.Tables)
        {
            if (table.TableName.Trim().Equals(SheetName, StringComparison.OrdinalIgnoreCase))
            {
                sheet = table;
                break;
            }
        }

        if (sheet == null)
            throw new InvalidOperationException(
                $"OE Excel file does not contain a sheet named \"{SheetName}\". Sheets found: " +
                string.Join(", ", dataSet.Tables.Cast<DataTable>().Select(t => t.TableName)));

        ValidateHeaderRow(sheet);

        var rows = new List<OeExcelRow>();
        for (int r = DataStartRowIndex; r < sheet.Rows.Count; r++)
        {
            var dataRow = sheet.Rows[r];
            var jobNumber = CellToString(dataRow, ColJobNumber);
            var partNumber = CellToString(dataRow, ColPartNumber);
            var poNumber = CellToString(dataRow, ColPoNumber);

            // Block-separator row between job groups: skip entirely.
            if (jobNumber.Length == 0 && partNumber.Length == 0 && poNumber.Length == 0)
                continue;

            var row = new OeExcelRow
            {
                SourceRowIndex = r + 1, // 1-based, matches the row number a user sees in Excel
                OeNumber = CellToString(dataRow, ColOeNumber),
                JobNumber = jobNumber,
                Customer = CellToString(dataRow, ColCustomer),
                PartNumber = partNumber,
                Revision = CellToString(dataRow, ColRevision),
                Contact = CellToString(dataRow, ColContact),
                Description = CellToString(dataRow, ColDescription),
                PoNumber = poNumber,
            };

            row.Quantity = CellToString(dataRow, ColQuantity);

            row.LineNumber = ParseLineNumber(dataRow, ColLineNumber, row);
            row.DrawingReleaseDate = ParseDate(dataRow, ColDrawingReleaseDate, row, "DWG Rel.");
            row.DeliveryRequiredDate = ParseDate(dataRow, ColDeliveryRequiredDate, row, "Del. Req'd:");
            row.Price = ParsePrice(dataRow, ColPrice, row);

            rows.Add(row);
        }

        return rows;
    }

    private static void ValidateHeaderRow(DataTable sheet)
    {
        if (sheet.Rows.Count <= HeaderRowIndex)
            throw new InvalidOperationException($"OE Excel sheet \"{SheetName}\" has no header row at row {HeaderRowIndex + 1}.");

        var header = sheet.Rows[HeaderRowIndex];
        CheckHeaderCell(header, ColOeNumber, "O.E.");
        CheckHeaderCell(header, ColJobNumber, "Job #");
        CheckHeaderCell(header, ColLineNumber, "M");
        CheckHeaderCell(header, ColPoNumber, "P.O.");
    }

    private static void CheckHeaderCell(DataRow header, int col, string expectedPrefix)
    {
        var actual = CellToString(header, col);
        if (!actual.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"OE Excel sheet \"{SheetName}\" header column {col + 1} was expected to start with " +
                $"\"{expectedPrefix}\" but found \"{actual}\". The sheet layout may have changed.");
    }

    private static string CellToString(DataRow row, int col)
    {
        if (col >= row.ItemArray.Length) return "";
        var value = row[col];
        if (value == null || value == DBNull.Value) return "";

        if (value is double d)
        {
            // Integer-valued doubles must render without a trailing ".0" (e.g. the "M" / line_number column).
            return d == Math.Floor(d) && !double.IsInfinity(d)
                ? ((long)d).ToString(CultureInfo.InvariantCulture)
                : d.ToString(CultureInfo.InvariantCulture);
        }

        if (value is DateTime dt)
            return dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        return value.ToString()?.Trim() ?? "";
    }

    private static string ParseLineNumber(DataRow row, int col, OeExcelRow target)
    {
        var raw = CellToString(row, col);
        if (raw.Length == 0)
        {
            target.ParseWarnings.Add("\"M\" (line number) column is blank");
        }
        return raw;
    }

    private static string ParseDate(DataRow row, int col, OeExcelRow target, string columnLabel)
    {
        var raw = CellToString(row, col);
        if (raw.Length == 0) return "";

        // Already normalized by CellToString when the source cell was a native Excel date.
        if (DateTime.TryParseExact(raw, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            return raw;

        if (DateTime.TryParseExact(raw, DateInputFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            return parsed.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        target.ParseWarnings.Add($"\"{columnLabel}\" value \"{raw}\" could not be parsed as a date");
        return raw;
    }

    private static decimal? ParsePrice(DataRow row, int col, OeExcelRow target)
    {
        var raw = CellToString(row, col);
        if (raw.Length == 0) return null;

        var cleaned = raw.Replace("$", "").Replace(",", "").Trim();
        if (cleaned.Length == 0) return null;

        if (decimal.TryParse(cleaned, NumberStyles.Number, CultureInfo.InvariantCulture, out var price))
            return price;

        target.ParseWarnings.Add($"\"Price:\" value \"{raw}\" could not be parsed as a number");
        return null;
    }
}
