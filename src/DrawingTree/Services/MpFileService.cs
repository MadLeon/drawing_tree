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
    /// The PO revision suffix is stripped ("RT98-88040-PN-R004-R. 1" -> "RT98-88040-PN-R004"),
    /// since the folder on disk is named after the base order only. An existing folder whose name
    /// contains the PO number as a token is preferred over the bare PO path — see FindPoFolder.
    /// </summary>
    /// <param name="folderName">Already-resolved customer folder name from MpConfig.</param>
    private static string BuildTargetFolder(string folderName, string? poNumber)
    {
        var customerRoot = Path.Combine(NetworkBase, folderName);

        var po = SanitizeFolderName(OeNormalization.GetPoBase(poNumber));
        if (string.IsNullOrEmpty(po)) return customerRoot;

        return FindPoFolder(customerRoot, po) ?? Path.Combine(customerRoot, po);
    }

    /// <summary>
    /// Looks for an existing sub-folder that belongs to the given PO. Folder names often carry a
    /// trailing description, e.g. "RT98-88040-PN-R004  FC Axial Adjustment Tool J-72775" — so the
    /// name is split on whitespace and matched against the PO number token by token.
    /// </summary>
    /// <param name="customerRoot">Customer folder to search in</param>
    /// <param name="po">Base PO number to match (revision suffix already stripped)</param>
    /// <returns>Full path of the matching folder, or null when none matches or the root is unreachable</returns>
    private static string? FindPoFolder(string customerRoot, string po)
    {
        try
        {
            if (!Directory.Exists(customerRoot)) return null;

            return Directory.EnumerateDirectories(customerRoot)
                .OrderBy(d => d, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(d => Path.GetFileName(d)
                    .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                    .Any(token => token.Equals(po, StringComparison.OrdinalIgnoreCase)));
        }
        catch (Exception ex)
        {
            Logger.Instance.Warning($"MpFileService.FindPoFolder: could not scan '{customerRoot}': {ex.Message}");
            return null;
        }
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

    /// <summary>Workbook extensions accepted as MP files. Most files on the share are .xlsx.</summary>
    private static readonly string[] MpExtensions = { ".xlsm", ".xlsx" };

    /// <summary>
    /// Returns MP workbooks in the folder whose names start with the given drawing number,
    /// newest first. Returns an empty list if the folder does not exist.
    /// </summary>
    public static List<string> GetExistingFiles(string folder, string? drawingNumber)
    {
        if (!Directory.Exists(folder)) return new List<string>();

        return Directory.GetFiles(folder)
            .Where(f => MpExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
            .Where(f => string.IsNullOrWhiteSpace(drawingNumber) ||
                        Path.GetFileName(f).StartsWith(drawingNumber.Trim(), StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(f => File.GetLastWriteTime(f))
            .ToList();
    }

    /// <summary>
    /// Resolves the MP target folder for the given context without creating any files.
    /// Throws <see cref="MpFolderNotConfiguredException"/> when the customer has no configured folder.
    /// </summary>
    public static string ResolveFolder(MpContext ctx)
        => BuildTargetFolder(ResolveFolderName(ctx), ctx.PoNumber);

    /// <summary>
    /// Resolves the customer's own MP folder (one level above the PO folder), used as the starting
    /// point when the user picks an MP file manually.
    /// Throws <see cref="MpFolderNotConfiguredException"/> when the customer has no configured folder.
    /// </summary>
    /// <param name="ctx">Order item context carrying the customer name</param>
    /// <returns>Full path of the customer folder under the MP network base</returns>
    public static string ResolveCustomerFolder(MpContext ctx)
        => Path.Combine(NetworkBase, ResolveFolderName(ctx));

    /// <summary>
    /// Looks up the configured folder name for the context's customer.
    /// </summary>
    /// <param name="ctx">Order item context carrying the customer name</param>
    /// <returns>Folder name from config.txt</returns>
    /// <exception cref="MpFolderNotConfiguredException">The customer has no folder configured.</exception>
    private static string ResolveFolderName(MpContext ctx)
    {
        var customerName = ctx.CustomerName ?? "Unknown";
        var folderName   = MpConfig.GetFolderName(customerName);
        if (folderName == null)
        {
            var allCustomers = new PoRepository().GetAllCustomers().Select(c => c.Name);
            var unconfigured = MpConfig.SyncCustomerKeys(allCustomers);
            throw new MpFolderNotConfiguredException(customerName, MpConfig.ConfigFilePath, unconfigured);
        }

        return folderName;
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

        var folder   = ResolveFolder(ctx);
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
