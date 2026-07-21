/// <summary>
/// MpExtractorService.cs
/// Reads a Manufacturing Process (.xlsm) workbook and extracts its header info fields
/// and process steps. Read-only: the workbook is never modified.
/// </summary>
/// <remarks>
/// Cell layout matches the MP template used by MpFileService.FillCells:
/// - Info fields: B7 PO, N6 OE, Q6 Job, F7 Line, J7 Drawing, R7 Rev, B8 Release date,
///   H8 Description, D9 Delivery date, Q9 Quantity
/// - Process steps: rows 11-26, column D shop code, column E row number,
///   columns F:N (merged) process description
/// </remarks>

using System.Data;
using System.Globalization;
using System.IO;
using System.Text;
using DrawingTree.Logging;
using DrawingTree.Models;
using ExcelDataReader;

namespace DrawingTree.Services;

public static class MpExtractorService
{
    // 0-based row/column indices of the info fields (Excel address in the comment).
    private const int RowInfoTop = 5;    // sheet row 6
    private const int RowInfoMid = 6;    // sheet row 7
    private const int RowInfoLow = 7;    // sheet row 8
    private const int RowInfoBottom = 8; // sheet row 9

    private const int ColB = 1;
    private const int ColD = 3;
    private const int ColE = 4;
    private const int ColF = 5;
    private const int ColH = 7;
    private const int ColJ = 9;
    private const int ColN = 13;
    private const int ColQ = 16;
    private const int ColR = 17;

    // 0-based process step rows (sheet rows 11-26).
    private const int ProcessRowStart = 10;
    private const int ProcessRowEnd = 25;

    private static bool _encodingRegistered;

    /// <summary>
    /// Extracts the info fields and process steps from an MP workbook.
    /// </summary>
    /// <param name="filePath">Full path to the MP .xlsm file</param>
    /// <returns>The extracted result; process steps may be empty if the sheet has none</returns>
    /// <exception cref="InvalidOperationException">Thrown when the workbook contains no worksheet</exception>
    public static MpExtractionResult Extract(string filePath)
    {
        if (!_encodingRegistered)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            _encodingRegistered = true;
        }

        // Share read/write so the file can be parsed while it is open in Excel.
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = ExcelReaderFactory.CreateReader(stream);
        var dataSet = reader.AsDataSet(new ExcelDataSetConfiguration
        {
            ConfigureDataTable = _ => new ExcelDataTableConfiguration { UseHeaderRow = false }
        });

        if (dataSet.Tables.Count == 0)
            throw new InvalidOperationException($"MP file contains no worksheet: {filePath}");

        var sheet = dataSet.Tables[0];

        var result = new MpExtractionResult
        {
            FileName             = Path.GetFileName(filePath),
            FilePath             = filePath,
            PoNumber             = Cell(sheet, RowInfoMid,    ColB), // B7
            OeNumber             = Cell(sheet, RowInfoTop,    ColN), // N6
            JobNumber            = Cell(sheet, RowInfoTop,    ColQ), // Q6
            LineNumber           = Cell(sheet, RowInfoMid,    ColF), // F7
            DrawingNumber        = Cell(sheet, RowInfoMid,    ColJ), // J7
            Revision             = Cell(sheet, RowInfoMid,    ColR), // R7
            DrawingReleaseDate   = Cell(sheet, RowInfoLow,    ColB), // B8
            Description          = Cell(sheet, RowInfoLow,    ColH), // H8
            DeliveryRequiredDate = Cell(sheet, RowInfoBottom, ColD), // D9
            Quantity             = Cell(sheet, RowInfoBottom, ColQ), // Q9
        };

        for (int row = ProcessRowStart; row <= ProcessRowEnd; row++)
        {
            var shopCode    = Cell(sheet, row, ColD);
            var description = Cell(sheet, row, ColF);

            if (shopCode.Length == 0 && description.Length == 0)
                continue;

            // Fall back to a 10/20/30... sequence when the row number cell is blank or non-numeric.
            if (!int.TryParse(Cell(sheet, row, ColE), out var rowNumber))
                rowNumber = (row - ProcessRowStart + 1) * 10;

            result.ProcessSteps.Add(new MpProcessStep(rowNumber, shopCode, description));
        }

        Logger.Instance.Info(
            $"MpExtractorService: extracted '{result.FileName}' - drawing '{result.DrawingNumber}' " +
            $"rev '{result.Revision}', {result.ProcessSteps.Count} process step(s)");

        return result;
    }

    /// <summary>
    /// Reads a cell as trimmed text, returning an empty string when out of range or blank.
    /// </summary>
    /// <param name="sheet">Worksheet table</param>
    /// <param name="row">0-based row index</param>
    /// <param name="col">0-based column index</param>
    /// <returns>Cell text, never null</returns>
    private static string Cell(DataTable sheet, int row, int col)
    {
        if (row >= sheet.Rows.Count || col >= sheet.Columns.Count) return string.Empty;

        var value = sheet.Rows[row][col];
        if (value == null || value == DBNull.Value) return string.Empty;

        if (value is double d)
        {
            // Integer-valued doubles must render without a trailing ".0" (e.g. quantity, line number).
            return d == Math.Floor(d) && !double.IsInfinity(d)
                ? ((long)d).ToString(CultureInfo.InvariantCulture)
                : d.ToString(CultureInfo.InvariantCulture);
        }

        if (value is DateTime dt)
            return dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        return value.ToString()?.Trim() ?? string.Empty;
    }
}
