/// <summary>
/// DirFileService.cs
/// Handles DIR file operations: path generation, template copy,
/// Excel cell population via COM Interop (requires Excel installed).
/// </summary>

using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using DrawingTree.Data;
using DrawingTree.Logging;

namespace DrawingTree.Services;

public static class DirFileService
{
    public const string TemplatePath = @"\\rtdnas2\QCReports\FINAL REPORTS\DIR Template.xlsm";

    /// <summary>
    /// Returns whether the DIR template file is reachable.
    /// </summary>
    public static bool TemplateExists() => File.Exists(TemplatePath);

    /// <summary>
    /// Resolves the DIR target folder for the given context without creating any files.
    /// Throws <see cref="DirFolderNotConfiguredException"/> when the customer has no configured folder.
    /// </summary>
    public static string ResolveFolder(MpContext ctx)
    {
        var customerName = ctx.CustomerName ?? "Unknown";
        var baseFolder = DirConfig.GetDirFolder(customerName)
            ?? throw new DirFolderNotConfiguredException(customerName, DirConfig.ConfigFilePath);

        var po = SanitizeFolderName(ctx.PoNumber);
        return string.IsNullOrEmpty(po) ? baseFolder : Path.Combine(baseFolder, po);
    }

    /// <summary>
    /// Generates the DIR file name: {DrawingNumber} REV. {Revision} @{JobNumber}.xlsm (uppercase).
    /// </summary>
    public static string BuildFileName(string? drawingNumber, string? revision, string jobNumber)
        => $"{(drawingNumber ?? string.Empty).Trim()} REV. {(revision ?? string.Empty).Trim()} @{jobNumber.Trim()}.xlsm"
            .ToUpperInvariant();

    /// <summary>
    /// Creates a new DIR file from the template into <paramref name="confirmedFolder"/>, pre-fills cells,
    /// records it in the database, and opens it in Excel. The caller must have already verified/created
    /// the target folder — this method never creates directories.
    /// Throws <see cref="InvalidOperationException"/> when a DIR file with the same name already exists.
    /// Throws <see cref="FileNotFoundException"/> when the template is missing.
    /// </summary>
    /// <param name="ctx">Order item context used to populate the cells.</param>
    /// <param name="confirmedFolder">Target folder, already confirmed to exist.</param>
    /// <returns>Full path of the created file.</returns>
    public static string CreateAndOpen(MpContext ctx, string confirmedFolder)
    {
        if (!TemplateExists())
            throw new FileNotFoundException($"DIR template not found: {TemplatePath}");

        var fileName = BuildFileName(ctx.DrawingNumber, ctx.Revision, ctx.JobNumber);
        var target   = Path.Combine(confirmedFolder, fileName);

        // Never overwrite an existing DIR file
        if (File.Exists(target))
            throw new InvalidOperationException($"DIR file already exists:\n{target}");

        File.Copy(TemplatePath, target);

        FillCells(target, ctx);

        // Record in database
        new PartRepository().AddDirAttachment(ctx.PartId, ctx.OrderItemId, fileName, target);

        Process.Start(new ProcessStartInfo { FileName = target, UseShellExecute = true });
        Logger.Instance.Info($"DirFileService: created and opened '{target}'");
        return target;
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    /// <summary>
    /// Opens the file with invisible Excel COM, sets the required cells (uppercased), then saves and releases.
    /// </summary>
    private static void FillCells(string filePath, MpContext ctx)
    {
        var excelType = Type.GetTypeFromProgID("Excel.Application")
            ?? throw new InvalidOperationException("Excel is not installed or not registered.");

        dynamic? app = null;
        dynamic? wb  = null;
        dynamic? ws  = null;

        try
        {
            app = Activator.CreateInstance(excelType)!;
            app.Visible              = false;
            app.DisplayAlerts        = false;
            app.ScreenUpdating       = false;
            app.AskToUpdateLinks     = false;

            wb = app.Workbooks.Open(
                filePath,
                UpdateLinks: false,
                ReadOnly: false,
                IgnoreReadOnlyRecommended: true,
                Notify: false);

            ws = wb.Sheets[1];

            SetCell(ws, "C6",  ctx.CustomerName);
            SetCell(ws, "C7",  $"{ctx.DrawingNumber} REV. {ctx.Revision}");
            SetCell(ws, "C8",  ctx.Description);
            SetCell(ws, "C11", "INCH");
            SetCell(ws, "H6",  ctx.JobNumber);
            SetCell(ws, "H7",  ctx.OeNumber);
            SetCell(ws, "H8",  ctx.PoNumber);

            wb.Save();
        }
        finally
        {
            if (ws  != null) { Marshal.ReleaseComObject(ws);  ws  = null; }
            if (wb  != null) { wb.Close(false); Marshal.ReleaseComObject(wb);  wb  = null; }
            if (app != null) { app.Quit();      Marshal.ReleaseComObject(app); app = null; }
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
    }

    private static void SetCell(dynamic ws, string address, string? value)
        => ws.Range[address].Value2 = (value ?? string.Empty).ToUpperInvariant();

    private static string? SanitizeFolderName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var invalid = Path.GetInvalidFileNameChars();
        return new string(name.Trim().Select(c => invalid.Contains(c) ? '_' : c).ToArray());
    }
}
