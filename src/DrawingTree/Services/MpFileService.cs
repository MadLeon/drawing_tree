/// <summary>
/// MpFileService.cs
/// Handles Manufacturing Process file operations: path generation, template copy,
/// Excel cell population via COM Interop (requires Excel installed), and file listing.
/// </summary>

using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using DrawingTree.Data;
using DrawingTree.Logging;

namespace DrawingTree.Services;

public static class MpFileService
{
    private const string NetworkBase = @"\\rtdnas2\Manufacturing Process";

    private static readonly string TemplatePath =
        Path.Combine(AppContext.BaseDirectory, "Resources", "mp_template.xlsm");

    /// <summary>
    /// Builds the target folder path: \\rtdnas2\Manufacturing Process\{folderName}\{PO}
    /// </summary>
    /// <param name="folderName">Already-resolved customer folder name from MpConfig.</param>
    private static string BuildTargetFolder(string folderName, string? poNumber)
    {
        var po = SanitizeFolderName(poNumber);
        return string.IsNullOrEmpty(po)
            ? Path.Combine(NetworkBase, folderName)
            : Path.Combine(NetworkBase, folderName, po);
    }

    /// <summary>
    /// Generates the MP file name: {DrawingNumber} {Description} (J#{JobNumber}).xlsm
    /// </summary>
    public static string BuildFileName(string? drawingNumber, string? description, string jobNumber)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(drawingNumber)) parts.Add(drawingNumber.Trim());
        if (!string.IsNullOrWhiteSpace(description))   parts.Add(description.Trim());
        var baseName = parts.Count > 0 ? string.Join(" ", parts) : "Document";
        return $"{baseName} (J#{jobNumber.Trim()}).xlsm";
    }

    /// <summary>
    /// Returns .xlsm files in the folder whose names start with the given drawing number.
    /// Returns an empty list if the folder does not exist.
    /// </summary>
    public static List<string> GetExistingFiles(string folder, string? drawingNumber)
    {
        if (!Directory.Exists(folder)) return new List<string>();

        return Directory.GetFiles(folder, "*.xlsm")
            .Where(f => string.IsNullOrWhiteSpace(drawingNumber) ||
                        Path.GetFileName(f).StartsWith(drawingNumber.Trim(), StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(f => File.GetLastWriteTime(f))
            .ToList();
    }

    /// <summary>
    /// Creates a new MP file from the template, pre-fills cells, records it in the database,
    /// and opens it in Excel.
    /// Throws <see cref="MpFolderNotConfiguredException"/> when the customer has no configured folder.
    /// Throws <see cref="InvalidOperationException"/> when an MP file with the same name already exists.
    /// Throws <see cref="FileNotFoundException"/> when the template is missing.
    /// </summary>
    /// <param name="ctx">Order item context used to populate the cells.</param>
    /// <returns>Full path of the created file.</returns>
    public static string CreateAndOpen(MpContext ctx)
    {
        if (!File.Exists(TemplatePath))
            throw new FileNotFoundException($"MP template not found: {TemplatePath}");

        // Resolve customer folder from config
        var customerName = ctx.CustomerName ?? "Unknown";
        var folderName   = MpConfig.GetFolderName(customerName);
        if (folderName == null)
        {
            var allCustomers = new PoRepository().GetAllCustomers().Select(c => c.Name);
            var unconfigured = MpConfig.SyncCustomerKeys(allCustomers);
            throw new MpFolderNotConfiguredException(customerName, MpConfig.ConfigFilePath, unconfigured);
        }

        var folder   = BuildTargetFolder(folderName, ctx.PoNumber);
        var fileName = BuildFileName(ctx.DrawingNumber, ctx.Description, ctx.JobNumber);
        var target   = Path.Combine(folder, fileName);

        // Never overwrite an existing MP file
        if (File.Exists(target))
            throw new InvalidOperationException($"MP file already exists:\n{target}");

        Directory.CreateDirectory(folder);
        File.Copy(TemplatePath, target);

        FillCells(target, ctx);

        // Record in database
        new PartRepository().AddMpAttachment(ctx.PartId, ctx.OrderItemId, fileName, target);

        Process.Start(new ProcessStartInfo { FileName = target, UseShellExecute = true });
        Logger.Instance.Info($"MpFileService: created and opened '{target}'");
        return target;
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    /// <summary>
    /// Opens the file with invisible Excel COM, sets the required cells, then saves and releases.
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

            SetCell(ws, "B7", ctx.PoNumber);
            SetCell(ws, "N6", ctx.OeNumber);
            SetCell(ws, "Q6", ctx.JobNumber);
            SetCell(ws, "F7", ctx.LineNumber.ToString());
            SetCell(ws, "J7", ctx.DrawingNumber);
            SetCell(ws, "R7", ctx.Revision);
            SetCell(ws, "B8", ctx.ReleaseDate);
            SetCell(ws, "H8", ctx.Description);
            SetCell(ws, "D9", ctx.DueDate);
            SetCell(ws, "Q9", ctx.Quantity.ToString());

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
        => ws.Range[address].Value2 = value ?? string.Empty;

    private static string? SanitizeFolderName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var invalid = Path.GetInvalidFileNameChars();
        return new string(name.Trim().Select(c => invalid.Contains(c) ? '_' : c).ToArray());
    }
}
