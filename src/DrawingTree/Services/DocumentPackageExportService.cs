/// <summary>
/// DocumentPackageExportService.cs
/// Injects a PO's job/drawing/DIR-completion rows into the shared "Document
/// Package Template.xlsm" network file via Excel COM Interop.
/// </summary>

using System.Runtime.InteropServices;

namespace DrawingTree.Services;

public static class DocumentPackageExportService
{
    public const string TargetPath = @"\\rtdnas2\QCReports\FINAL REPORTS\Document Package Template.xlsm";

    private const int DataStartRow = 4;
    private const int RowsPerPage = 44;
    private const int RowsPerPageTotal = RowsPerPage * 2; // left + right column

    /// <param name="JobNumber">job.job_number; blank when the same as the previous row's job</param>
    /// <param name="DrawingNumber">part.drawing_number</param>
    /// <param name="Completed">true if the row's DIR attachment status is "completed"</param>
    /// <param name="Reviewed">true if the row's DIR attachment status is "reviewed"</param>
    public record ExportRow(string JobNumber, string DrawingNumber, bool Completed, bool Reviewed);

    /// <summary>
    /// Opens the shared Document Package Template.xlsm, clears its "Template" sheet, then writes
    /// the given PO's rows (44 rows per column, two columns per page; once a page's 88 rows are
    /// full, the next 88 rows continue directly below with no repeated header), suppressing
    /// repeated Job No values, and resizes the print area to match the number of pages actually
    /// used. Leaves the workbook open and visible, unsaved, for the user to review and save
    /// manually.
    /// </summary>
    public static void Export(string poNumber, List<ExportRow> rows)
    {
        var excelType = Type.GetTypeFromProgID("Excel.Application")
            ?? throw new InvalidOperationException("Excel is not installed or not registered.");

        dynamic? app = null;
        dynamic? wb = null;
        dynamic? ws = null;
        bool handedOff = false;

        try
        {
            app = Activator.CreateInstance(excelType)!;
            app.Visible          = false;
            app.DisplayAlerts    = false;
            app.ScreenUpdating   = false;
            app.AskToUpdateLinks = false;

            wb = app.Workbooks.Open(
                TargetPath,
                UpdateLinks: false,
                ReadOnly: false,
                IgnoreReadOnlyRecommended: true,
                Notify: false);

            ws = wb.Sheets["Template"];

            ResetRowExtent(ws, rows.Count);
            ClearPageContent(ws, rows.Count);

            ws.Range["A1"].Value2 = poNumber;
            WriteRows(ws, rows);
            ws.PageSetup.PrintArea = "A1:I" + LastNeededRow(rows.Count);

            app.ScreenUpdating = true;
            app.Visible = true;
            wb.Activate();
            handedOff = true;
        }
        finally
        {
            if (!handedOff)
            {
                try { if (wb != null) wb.Close(false); } catch { /* best-effort cleanup */ }
                try { if (app != null) app.Quit(); } catch { /* best-effort cleanup */ }
            }

            if (ws  != null) { Marshal.ReleaseComObject(ws);  ws  = null; }
            if (wb  != null) { Marshal.ReleaseComObject(wb);  wb  = null; }
            if (app != null) { Marshal.ReleaseComObject(app); app = null; }

            if (!handedOff)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
        }
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private static int LastNeededRow(int rowCount)
    {
        int blocksNeeded = Math.Max(1, (int)Math.Ceiling(rowCount / (double)RowsPerPageTotal));
        return DataStartRow + blocksNeeded * RowsPerPage - 1;
    }

    /// <summary>Deletes any leftover data rows from a previous, larger export.</summary>
    private static void ResetRowExtent(dynamic ws, int rowCount)
    {
        int lastNeeded = LastNeededRow(rowCount);
        int usedLastRow = (int)ws.UsedRange.Row + (int)ws.UsedRange.Rows.Count - 1;
        if (usedLastRow > lastNeeded)
            ws.Rows[(lastNeeded + 1) + ":" + usedLastRow].Delete();
    }

    /// <summary>
    /// Clears the PO number cell and all data rows this export will touch, and un-merges any
    /// stray merged cells in the data range (e.g. left over from a previous export) — a merged
    /// non-anchor cell silently rejects Value2 writes, which would otherwise drop data.
    /// </summary>
    private static void ClearPageContent(dynamic ws, int rowCount)
    {
        ws.Range["A1:B2"].ClearContents();
        ws.Range["F1"].ClearContents();

        dynamic dataRange = ws.Range["A" + DataStartRow + ":I" + LastNeededRow(rowCount)];
        dataRange.UnMerge();
        dataRange.ClearContents();
    }

    private static void WriteRows(dynamic ws, List<ExportRow> rows)
    {
        string? lastJobNumber = null;

        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i];

            int withinBlock = i % RowsPerPageTotal;
            int block       = i / RowsPerPageTotal;
            bool leftColumn = withinBlock < RowsPerPage;
            int rowInBlock  = leftColumn ? withinBlock : withinBlock - RowsPerPage;
            int actualRow   = DataStartRow + block * RowsPerPage + rowInBlock;

            string jobCol       = leftColumn ? "A" : "F";
            string drawingCol   = leftColumn ? "B" : "G";
            string completedCol = leftColumn ? "C" : "H";
            string reviewedCol  = leftColumn ? "D" : "I";

            string jobText = row.JobNumber == lastJobNumber ? string.Empty : row.JobNumber;
            lastJobNumber = row.JobNumber;

            ws.Range[jobCol + actualRow].Value2       = jobText;
            ws.Range[drawingCol + actualRow].Value2   = row.DrawingNumber;
            ws.Range[completedCol + actualRow].Value2 = row.Completed ? "✔" : string.Empty;
            ws.Range[reviewedCol + actualRow].Value2  = row.Reviewed ? "✔" : string.Empty;
        }
    }
}
